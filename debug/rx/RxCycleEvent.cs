using System;
using System.Collections.Generic;

namespace cfGodotEngine.Debug
{
    /// <summary>
    /// Recorded when a synchronous Relay dispatch cycle is detected under
    /// CF_RX_INSTRUMENTED. Carries the full owner→listener→owner path so the
    /// overlay can show the developer exactly where the cycle is.
    /// </summary>
    public readonly struct RxCycleEvent
    {
        public readonly long TimestampTicks;
        public readonly IReadOnlyList<string> OwnerChain;
        public readonly IReadOnlyList<string> ListenerChain;
        public readonly int CycleStartIndex;

        public RxCycleEvent(
            long timestampTicks,
            IReadOnlyList<string> ownerChain,
            IReadOnlyList<string> listenerChain,
            int cycleStartIndex)
        {
            TimestampTicks = timestampTicks;
            OwnerChain = ownerChain;
            ListenerChain = listenerChain;
            CycleStartIndex = cycleStartIndex;
        }
    }
}
