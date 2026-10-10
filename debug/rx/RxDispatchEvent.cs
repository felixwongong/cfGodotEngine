using System;

namespace cfGodotEngine.Debug
{
    public readonly struct RxDispatchEvent
    {
        public readonly long TimestampTicks;
        public readonly int DispatchOrder;
        public readonly string OwnerType;
        public readonly string OwnerTag;
        public readonly string ListenerMethod;
        public readonly string ListenerTargetType;
        public readonly string ArgsString;
        public readonly long ElapsedMicros;
        public readonly Exception Exception;

        public RxDispatchEvent(
            long timestampTicks,
            int dispatchOrder,
            string ownerType,
            string ownerTag,
            string listenerMethod,
            string listenerTargetType,
            string argsString,
            long elapsedMicros,
            Exception exception)
        {
            TimestampTicks = timestampTicks;
            DispatchOrder = dispatchOrder;
            OwnerType = ownerType;
            OwnerTag = ownerTag;
            ListenerMethod = listenerMethod;
            ListenerTargetType = listenerTargetType;
            ArgsString = argsString;
            ElapsedMicros = elapsedMicros;
            Exception = exception;
        }

        public bool IsException => Exception != null;
    }
}
