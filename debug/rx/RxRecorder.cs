using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using cfEngine.Rx;

namespace cfGodotEngine.Debug
{
    /// <summary>
    /// Subscribes to RxInstrumentation hooks, buffers dispatch events in a ring buffer,
    /// supports capture toggling and filtering, and fans events out to IRxRecorderSinks.
    /// Pure C# (no Godot) — sinks may be Godot-dependent.
    /// </summary>
    public class RxRecorder
    {
        public static readonly RxRecorder Instance = new RxRecorder();

        /// <summary>
        /// True only when the cfEngine <c>RxInstrumentation</c> hooks are compiled into this
        /// build (i.e. <c>CF_RX_INSTRUMENTED</c>, Debug configuration). Exposed as a runtime
        /// property so Godot-facing bootstraps can decide at runtime whether attaching a
        /// recorder/overlay is meaningful, instead of compile-gating the class itself.
        /// </summary>
        public static bool InstrumentationCompiled
        {
            get
            {
#if CF_RX_INSTRUMENTED
                return true;
#else
                return false;
#endif
            }
        }

        private readonly RxSubscriptionRegistry _registry = new();
        private readonly List<IRxRecorderSink> _sinks = new();
        private readonly RxDispatchEvent[] _ringBuffer;
        private readonly int _ringCapacity;
        private int _ringHead;
        private int _ringCount;
        private int _dispatchOrder;
        private long _bookmarkedIndex = -1;
        private string _bookmarkLabel;

        private readonly Dictionary<string, RxDispatchProfile> _byOwnerTag = new();
        private readonly Dictionary<string, RxDispatchProfile> _byListenerMethod = new();
        private readonly Dictionary<int, long> _fanOutHistogram = new();
        private long _dispatchBatches;
        private long _dispatchInvocations;
        private long _exceptionCount;
        private readonly List<RxBookmark> _bookmarks = new();

        [ThreadStatic]
        private static int _batchInvocations;

        [ThreadStatic]
        private static object[] _currentArgs;

        [ThreadStatic]
        private static string _currentListenerMethod;

        [ThreadStatic]
        private static List<DispatchFrame> _cycleStack;

        private static List<DispatchFrame> CycleStack => _cycleStack ??= new List<DispatchFrame>();

        public bool Capturing = true;
        public bool CaptureStackTraces = false;
        public string OwnerTagFilter;
        public string ListenerMethodFilter;
        public bool ExceptionsOnly;
        public long MinElapsedMicros;

        public int Capacity => _ringCapacity;
        public int RingCount => _ringCount;
        public RxSubscriptionRegistry Registry => _registry;
        public IReadOnlyDictionary<string, RxDispatchProfile> ByOwnerTag => _byOwnerTag;
        public IReadOnlyDictionary<string, RxDispatchProfile> ByListenerMethod => _byListenerMethod;
        public IReadOnlyDictionary<int, long> FanOutHistogram => _fanOutHistogram;
        public long DispatchBatches => _dispatchBatches;
        public long DispatchInvocations => _dispatchInvocations;
        public long ExceptionCount => _exceptionCount;
        public IReadOnlyList<RxBookmark> Bookmarks => _bookmarks;

        public bool HasBookmark => _bookmarkedIndex >= 0;
        public string BookmarkLabel => _bookmarkLabel;
        public long BookmarkIndex => _bookmarkedIndex;

        private readonly struct DispatchFrame
        {
            public readonly object Owner;
            public readonly string OwnerTag;
            public readonly string ListenerMethod;

            public DispatchFrame(object owner, string ownerTag, string listenerMethod)
            {
                Owner = owner;
                OwnerTag = ownerTag;
                ListenerMethod = listenerMethod;
            }
        }

        private RxRecorder(int ringCapacity = 5000)
        {
            _ringCapacity = ringCapacity;
            _ringBuffer = new RxDispatchEvent[_ringCapacity];
        }

        public void AddSink(IRxRecorderSink sink)
        {
            if (sink != null && !_sinks.Contains(sink)) _sinks.Add(sink);
        }

        public void RemoveSink(IRxRecorderSink sink)
        {
            _sinks.Remove(sink);
        }

        public void Attach()
        {
#if CF_RX_INSTRUMENTED
            RxInstrumentation.OnListenerSubscribed = OnListenerSubscribed;
            RxInstrumentation.OnListenerRemoved = OnListenerRemoved;
            RxInstrumentation.OnDispatching = OnDispatching;
            RxInstrumentation.OnDispatched = OnDispatched;
            RxInstrumentation.OnDispatchBatchBegin = OnBatchBegin;
            RxInstrumentation.OnDispatchBatchEnd = OnBatchEnd;
#endif
        }

        public void Detach()
        {
#if CF_RX_INSTRUMENTED
            RxInstrumentation.OnListenerSubscribed = null;
            RxInstrumentation.OnListenerRemoved = null;
            RxInstrumentation.OnDispatching = null;
            RxInstrumentation.OnDispatched = null;
            RxInstrumentation.OnDispatchBatchBegin = null;
            RxInstrumentation.OnDispatchBatchEnd = null;
#endif
        }

        public void Mark(string label)
        {
            _bookmarkLabel = label;
            _bookmarkedIndex = _dispatchOrder;
            _bookmarks.Add(new RxBookmark(_dispatchOrder, label, Stopwatch.GetTimestamp()));
        }

        public void ClearBookmark()
        {
            _bookmarkLabel = null;
            _bookmarkedIndex = -1;
        }

        public void Reset()
        {
            _ringHead = 0;
            _ringCount = 0;
            _dispatchOrder = 0;
            _bookmarkedIndex = -1;
            _bookmarkLabel = null;
            _registry.Reset();
            _byOwnerTag.Clear();
            _byListenerMethod.Clear();
            _fanOutHistogram.Clear();
            _dispatchBatches = 0;
            _dispatchInvocations = 0;
            _exceptionCount = 0;
            _bookmarks.Clear();
            CycleStack.Clear();
        }

        public List<RxDispatchEvent> Snapshot()
        {
            var result = new List<RxDispatchEvent>(_ringCount);
            for (int i = 0; i < _ringCount; i++)
            {
                var idx = (_ringHead - _ringCount + i + _ringCapacity) % _ringCapacity;
                result.Add(_ringBuffer[idx]);
            }
            return result;
        }

        private void OnListenerSubscribed(object owner, Delegate listener, WeakReference<Subscription> subscriptionWeakRef)
        {
            var ownerType = owner?.GetType().Name ?? "null";
            var ownerTag = RxTagResolver.ResolveTag(owner);
            var method = listener?.Method?.Name ?? "null";
            var targetType = listener?.Target?.GetType().Name ?? "static";
            var ticks = Stopwatch.GetTimestamp();
            var stack = CaptureStackTrace();
            var record = new RxSubscriptionRecord(ownerTag, ownerType, owner, method, targetType, subscriptionWeakRef, listener?.Target, ticks, stack);
            _registry.RecordSubscribe(record);
            foreach (var sink in _sinks)
                sink.OnListenerSubscribed(ownerType, ownerTag, method, targetType, ticks);
        }

        private void OnListenerRemoved(object owner, Delegate listener)
        {
            var ownerType = owner?.GetType().Name ?? "null";
            var ownerTag = RxTagResolver.ResolveTag(owner);
            var method = listener?.Method?.Name ?? "null";
            var targetType = listener?.Target?.GetType().Name ?? "static";
            _registry.RecordRemove(owner, listener);
            var ticks = Stopwatch.GetTimestamp();
            foreach (var sink in _sinks)
                sink.OnListenerRemoved(ownerType, ownerTag, method, targetType, ticks);
        }

        private static string CaptureStackTrace()
        {
            if (!Instance.CaptureStackTraces) return "";
            try
            {
                var stack = Environment.StackTrace;
                const int maxFrames = 30;
                var lines = stack.Split(new[] { '\n' }, StringSplitOptions.None);
                if (lines.Length <= maxFrames) return stack;
                return string.Join("\n", lines, 0, maxFrames) + "\n...";
            }
            catch
            {
                return "";
            }
        }

        private void OnDispatching(object owner, Delegate listener, object[] args)
        {
            _currentArgs = args;
            _currentListenerMethod = listener?.Method?.Name ?? "null";
        }

        private void OnBatchBegin(object owner)
        {
            _batchInvocations = 0;
            var ownerTag = RxTagResolver.ResolveTag(owner);

            // Push this frame first, then scan the existing stack (excluding the frame we just pushed)
            // for a duplicate owner. That means a cycle has closed: this dispatch re-entered an owner
            // that is already dispatching on the current thread.
            CycleStack.Add(new DispatchFrame(owner, ownerTag, _currentListenerMethod));

            for (int i = 0; i < CycleStack.Count - 1; i++)
            {
                if (ReferenceEquals(CycleStack[i].Owner, owner))
                {
                    var ownerChain = new List<string>();
                    var listenerChain = new List<string>();
                    for (int j = i; j < CycleStack.Count; j++)
                    {
                        ownerChain.Add(CycleStack[j].OwnerTag);
                        listenerChain.Add(CycleStack[j].ListenerMethod);
                    }

                    var cycleEvent = new RxCycleEvent(
                        Stopwatch.GetTimestamp(),
                        ownerChain,
                        listenerChain,
                        i);

                    foreach (var sink in _sinks)
                        sink.OnCycleDetected(cycleEvent);

                    throw new RelayCycleException(ownerChain, BuildCycleMessage(ownerChain, listenerChain));
                }
            }
        }

        private void OnBatchEnd(object owner, int listenerCount)
        {
            if (CycleStack.Count > 0 && ReferenceEquals(CycleStack[CycleStack.Count - 1].Owner, owner))
            {
                CycleStack.RemoveAt(CycleStack.Count - 1);
            }

            if (listenerCount <= 0) return;

            _dispatchBatches++;
            _fanOutHistogram.TryGetValue(listenerCount, out var c);
            _fanOutHistogram[listenerCount] = c + 1;
        }

        private static string BuildCycleMessage(IReadOnlyList<string> ownerChain, IReadOnlyList<string> listenerChain)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Relay dispatch cycle detected:");
            for (int i = 0; i < ownerChain.Count; i++)
            {
                var listener = i < listenerChain.Count ? listenerChain[i] : "(re-entry)";
                sb.AppendLine($"  {ownerChain[i]} (listener: {listener})");
            }
            sb.AppendLine($"  → {ownerChain[ownerChain.Count - 1]}  ← CYCLE (re-entered)");
            return sb.ToString();
        }

        private void OnDispatched(object owner, Delegate listener, long elapsedMicros, Exception ex)
        {
            var ownerType = owner?.GetType().Name ?? "null";
            var ownerTag = RxTagResolver.ResolveTag(owner);
            var method = listener?.Method?.Name ?? "null";
            var targetType = listener?.Target?.GetType().Name ?? "static";

            var args = _currentArgs;
            _currentArgs = null;
            var argsString = FormatArgs(args);

            _dispatchInvocations++;
            _batchInvocations++;
            if (ex != null) _exceptionCount++;
            AccumulateProfile(_byOwnerTag, ownerTag, method, elapsedMicros, ex);
            AccumulateProfile(_byListenerMethod, method, method, elapsedMicros, ex);

            if (!Capturing) return;

            var evt = new RxDispatchEvent(
                Stopwatch.GetTimestamp(),
                ++_dispatchOrder,
                ownerType,
                ownerTag,
                method,
                targetType,
                argsString,
                elapsedMicros,
                ex);

            if (ExceptionsOnly && ex == null) return;
            if (!string.IsNullOrEmpty(OwnerTagFilter) && !ownerTag.Contains(OwnerTagFilter, StringComparison.OrdinalIgnoreCase)) return;
            if (!string.IsNullOrEmpty(ListenerMethodFilter) && !method.Contains(ListenerMethodFilter, StringComparison.OrdinalIgnoreCase)) return;
            if (elapsedMicros < MinElapsedMicros) return;

            PushRing(evt);

            foreach (var sink in _sinks)
                sink.OnDispatchEvent(evt);
        }

        private void PushRing(RxDispatchEvent evt)
        {
            _ringBuffer[_ringHead] = evt;
            _ringHead = (_ringHead + 1) % _ringCapacity;
            if (_ringCount < _ringCapacity) _ringCount++;
        }

        private static string FormatArgs(object[] args)
        {
            if (args == null || args.Length == 0) return "";
            var s = string.Join(", ", args.Select(a => a?.ToString() ?? "null"));
            return s.Length > 200 ? s.Substring(0, 200) + "…" : s;
        }

        private static void AccumulateProfile(Dictionary<string, RxDispatchProfile> table, string key, string method, long elapsedMicros, Exception ex)
        {
            if (!table.TryGetValue(key, out var profile))
            {
                profile = new RxDispatchProfile();
                table[key] = profile;
            }

            profile.DispatchCount++;
            profile.TotalElapsedMicros += elapsedMicros;
            profile.ListenerMethods.Add(method);
            if (ex != null)
            {
                profile.ExceptionCount++;
            }

            if (elapsedMicros > profile.MaxElapsedMicros)
            {
                profile.MaxElapsedMicros = elapsedMicros;
                profile.SlowestListener = method;
            }
        }
    }
}
