using System;
using System.Collections.Generic;
using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Debug
{
    public class RxLeakAuditor
    {
        private readonly RxSubscriptionRegistry _registry;
        private readonly List<SubSnapshot> _sceneSnapshots = new();
        private SubSnapshot _currentSceneSnapshot;

        public RxLeakAuditor(RxSubscriptionRegistry registry = null)
        {
            _registry = registry ?? RxRecorder.Instance.Registry;
        }

        public void OnSceneLoaded(string sceneName)
        {
            _currentSceneSnapshot = new SubSnapshot(sceneName, DateTime.UtcNow, SnapshotLiveSubscriptions());
            _sceneSnapshots.Add(_currentSceneSnapshot);
        }

        public List<LeakReport> OnSceneUnloaded()
        {
            var leaks = new List<LeakReport>();
            if (_currentSceneSnapshot == null) return leaks;

            var current = SnapshotLiveSubscriptions();
            foreach (var entry in current)
            {
                if (_currentSceneSnapshot.Contains(entry.SubscriptionRef))
                {
                    leaks.Add(CreateReport(entry, "SceneSurvivor", $"Survived scene exit ({_currentSceneSnapshot.SceneName})", _currentSceneSnapshot.SceneName));
                }
            }
            _currentSceneSnapshot = null;
            return leaks;
        }

        public List<LeakReport> PollDeadOwnerLeaks()
        {
            var leaks = new List<LeakReport>();
            foreach (var record in _registry.LiveRecords)
            {
                if (!record.IsAlive) continue;
                if (record.OwnerRef == null) continue;
                if (!record.OwnerRef.TryGetTarget(out _))
                {
                    leaks.Add(CreateReport(record, "DeadOwner", "Owner GC'd but Subscription handle still alive"));
                }
            }
            return leaks;
        }

        public List<LeakReport> PollGodotLeaks()
        {
            var leaks = new List<LeakReport>();
            foreach (var record in _registry.LiveRecords)
            {
                if (!record.IsAlive) continue;
                if (!record.ListenerTargetRef.TryGetTarget(out var target)) continue;

                if (target is GodotObject go && !GodotObject.IsInstanceValid(go))
                {
                    leaks.Add(CreateReport(record, "DeadGodotObject", "Listener target is invalid Godot object"));
                    continue;
                }

                if (target is Node node && !IsInsideTree(node))
                {
                    leaks.Add(CreateReport(record, "NotInTree", "Listener target Node is not inside tree"));
                }
            }
            return leaks;
        }

        private List<RxSubscriptionRecord> SnapshotLiveSubscriptions()
        {
            _registry.PruneDeadRecords();
            var snapshot = new List<RxSubscriptionRecord>(_registry.LiveRecords.Count);
            foreach (var record in _registry.LiveRecords)
            {
                if (record.IsAlive) snapshot.Add(record);
            }
            return snapshot;
        }

        private LeakReport CreateReport(RxSubscriptionRecord record, string category, string reason, string sceneName = null)
        {
            return new LeakReport
            {
                Category = category,
                SubscribeId = record.SubscribeId,
                OwnerTag = record.OwnerTag,
                OwnerType = record.OwnerType,
                ListenerMethod = record.ListenerMethod,
                ListenerTargetType = record.ListenerTargetType,
                SubscribeTicks = record.SubscribeTicks,
                Reason = reason,
                StackTrace = record.SubscribeStackTrace,
                SceneName = sceneName
            };
        }

        private static bool IsInsideTree(Node node)
        {
            try { return node.IsInsideTree(); } catch { return false; }
        }
    }

    public class SubSnapshot
    {
        public readonly string SceneName;
        public readonly DateTime Time;
        public readonly List<RxSubscriptionRecord> Entries;

        public SubSnapshot(string sceneName, DateTime time, List<RxSubscriptionRecord> entries)
        {
            SceneName = sceneName;
            Time = time;
            Entries = entries;
        }

        public bool Contains(WeakReference<Subscription> subscriptionRef)
        {
            if (subscriptionRef == null) return false;
            if (!subscriptionRef.TryGetTarget(out var subscription)) return false;
            foreach (var e in Entries)
            {
                if (e.SubscriptionRef.TryGetTarget(out var s) && ReferenceEquals(s, subscription))
                    return true;
            }
            return false;
        }
    }

    public class LeakReport
    {
        public string Category;
        public long SubscribeId;
        public string OwnerTag;
        public string OwnerType;
        public string ListenerMethod;
        public string ListenerTargetType;
        public long SubscribeTicks;
        public string Reason;
        public string StackTrace;
        public string SceneName;
    }
}
