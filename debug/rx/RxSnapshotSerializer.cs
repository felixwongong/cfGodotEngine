using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace cfGodotEngine.Debug
{
    public static class RxSnapshotSerializer
    {
        private static readonly JsonSerializerOptions _options = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static string ToJson(RxRecorder recorder, int maxEvents = 5000)
        {
            var snapshot = recorder.Snapshot();
            if (maxEvents > 0 && snapshot.Count > maxEvents)
                snapshot = snapshot.GetRange(snapshot.Count - maxEvents, maxEvents);

            var doc = new
            {
                totals = new
                {
                    batches = recorder.DispatchBatches,
                    invocations = recorder.DispatchInvocations,
                    exceptions = recorder.ExceptionCount
                },
                byOwnerTag = BuildProfileList(recorder.ByOwnerTag),
                byListenerMethod = BuildProfileList(recorder.ByListenerMethod),
                fanOut = BuildFanOutList(recorder.FanOutHistogram),
                bookmarks = BuildBookmarkList(recorder.Bookmarks),
                events = BuildEventList(snapshot)
            };

            return JsonSerializer.Serialize(doc, _options);
        }

        public static void WriteToFile(RxRecorder recorder, string path, int maxEvents = 5000)
        {
            var json = ToJson(recorder, maxEvents);
            File.WriteAllText(path, json);
        }

        private static List<object> BuildProfileList(IReadOnlyDictionary<string, RxDispatchProfile> table)
        {
            var list = new List<object>(table.Count);
            foreach (var kvp in table)
            {
                list.Add(new
                {
                    key = kvp.Key,
                    count = kvp.Value.DispatchCount,
                    totalMicros = kvp.Value.TotalElapsedMicros,
                    maxMicros = kvp.Value.MaxElapsedMicros,
                    exceptions = kvp.Value.ExceptionCount,
                    slowestListener = kvp.Value.SlowestListener
                });
            }
            return list;
        }

        private static List<object> BuildFanOutList(IReadOnlyDictionary<int, long> histogram)
        {
            var list = new List<object>(histogram.Count);
            foreach (var kvp in histogram)
            {
                list.Add(new { listeners = kvp.Key, batches = kvp.Value });
            }
            return list;
        }

        private static List<object> BuildBookmarkList(IReadOnlyList<RxBookmark> bookmarks)
        {
            var list = new List<object>(bookmarks.Count);
            foreach (var b in bookmarks)
            {
                list.Add(new
                {
                    order = b.DispatchOrder,
                    label = b.Label,
                    ticks = b.TimestampTicks
                });
            }
            return list;
        }

        private static List<object> BuildEventList(List<RxDispatchEvent> events)
        {
            var list = new List<object>(events.Count);
            foreach (var e in events)
            {
                list.Add(new
                {
                    order = e.DispatchOrder,
                    timeTicks = e.TimestampTicks,
                    ownerType = e.OwnerType,
                    ownerTag = e.OwnerTag,
                    listenerMethod = e.ListenerMethod,
                    listenerTargetType = e.ListenerTargetType,
                    args = e.ArgsString,
                    elapsedMicros = e.ElapsedMicros,
                    exception = e.Exception?.Message
                });
            }
            return list;
        }
    }
}
