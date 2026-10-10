#nullable enable

using System;
using cfEngine.Rx;

namespace cfGodotEngine.Debug
{
    public class RxSubscriptionRecord
    {
        public readonly string OwnerTag;
        public readonly string OwnerType;
        public readonly WeakReference<object>? OwnerRef;
        public readonly string ListenerMethod;
        public readonly string ListenerTargetType;
        public readonly WeakReference<Subscription> SubscriptionRef;
        public readonly WeakReference<object>? ListenerTargetRef;
        public readonly long SubscribeTicks;
        public readonly string SubscribeStackTrace;
        public readonly long SubscribeId;

        private static long _nextId = 1;

        public RxSubscriptionRecord(string ownerTag, string ownerType, object owner, string listenerMethod, string listenerTargetType, WeakReference<Subscription> subscriptionRef, object listenerTarget, long ticks, string stackTrace)
        {
            OwnerTag = ownerTag;
            OwnerType = ownerType;
            OwnerRef = owner != null ? new WeakReference<object>(owner) : null;
            ListenerMethod = listenerMethod;
            ListenerTargetType = listenerTargetType;
            SubscriptionRef = subscriptionRef;
            ListenerTargetRef = listenerTarget != null ? new WeakReference<object>(listenerTarget) : null;
            SubscribeTicks = ticks;
            SubscribeStackTrace = stackTrace;
            SubscribeId = _nextId++;
        }

        public bool IsAlive => SubscriptionRef.TryGetTarget(out _);

        public bool IsSameOwnerAndListener(object owner, Delegate listener)
        {
            if (OwnerRef != null && OwnerRef.TryGetTarget(out var o) && !ReferenceEquals(o, owner))
                return false;
            if (OwnerRef == null && owner != null)
                return false;
            if (ListenerMethod != (listener?.Method?.Name ?? "null"))
                return false;

            var expectedTarget = listener?.Target;
            if (ListenerTargetRef != null && ListenerTargetRef.TryGetTarget(out var target))
            {
                if (!ReferenceEquals(target, expectedTarget))
                    return false;
            }
            else
            {
                if (expectedTarget != null)
                    return false;
            }

            return true;
        }
    }
}

