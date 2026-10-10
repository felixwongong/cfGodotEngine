using System;

namespace cfGodotEngine.Debug
{
    public interface IRxRecorderSink
    {
        void OnDispatchEvent(RxDispatchEvent evt);
        void OnListenerSubscribed(string ownerType, string ownerTag, string listenerMethod, string listenerTargetType, long timestampTicks);
        void OnListenerRemoved(string ownerType, string ownerTag, string listenerMethod, string listenerTargetType, long timestampTicks);
        void OnCycleDetected(RxCycleEvent evt);
    }
}
