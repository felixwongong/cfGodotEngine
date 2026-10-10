using System;
using System.Collections.Generic;

namespace cfGodotEngine.Debug
{
    public class RxSubscriptionRegistry
    {
        private readonly List<RxSubscriptionRecord> _liveRecords = new();
        private readonly Dictionary<string, int> _liveCountByOwner = new();
        private long _totalSubscribed;
        private long _totalRemoved;

        public long TotalSubscribed => _totalSubscribed;
        public long TotalRemoved => _totalRemoved;
        public IReadOnlyDictionary<string, int> LiveCountByOwner => _liveCountByOwner;
        public IReadOnlyList<RxSubscriptionRecord> LiveRecords => _liveRecords;

        public void RecordSubscribe(RxSubscriptionRecord record)
        {
            _totalSubscribed++;
            _liveRecords.Add(record);
            _liveCountByOwner.TryGetValue(record.OwnerType, out var c);
            _liveCountByOwner[record.OwnerType] = c + 1;
        }

        public void RecordRemove(object owner, Delegate listener)
        {
            _totalRemoved++;

            for (int i = 0; i < _liveRecords.Count; i++)
            {
                if (_liveRecords[i].IsSameOwnerAndListener(owner, listener))
                {
                    var removed = _liveRecords[i];
                    _liveRecords.RemoveAt(i);
                    if (_liveCountByOwner.TryGetValue(removed.OwnerType, out var c))
                    {
                        var newCount = Math.Max(0, c - 1);
                        if (newCount == 0)
                            _liveCountByOwner.Remove(removed.OwnerType);
                        else
                            _liveCountByOwner[removed.OwnerType] = newCount;
                    }
                    return;
                }
            }
        }

        public void PruneDeadRecords()
        {
            for (int i = _liveRecords.Count - 1; i >= 0; i--)
            {
                if (!_liveRecords[i].IsAlive)
                {
                    var removed = _liveRecords[i];
                    _liveRecords.RemoveAt(i);
                    if (_liveCountByOwner.TryGetValue(removed.OwnerType, out var c))
                    {
                        var newCount = Math.Max(0, c - 1);
                        if (newCount == 0)
                            _liveCountByOwner.Remove(removed.OwnerType);
                        else
                            _liveCountByOwner[removed.OwnerType] = newCount;
                    }
                }
            }
        }

        public void Reset()
        {
            _liveRecords.Clear();
            _liveCountByOwner.Clear();
            _totalSubscribed = 0;
            _totalRemoved = 0;
        }
    }
}

