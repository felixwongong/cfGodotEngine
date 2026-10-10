using System.Collections.Generic;

namespace cfGodotEngine.Debug
{
    public class RxDispatchProfile
    {
        public long DispatchCount;
        public long ExceptionCount;
        public long TotalElapsedMicros;
        public long MaxElapsedMicros;
        public string SlowestListener;
        public readonly HashSet<string> ListenerMethods = new();
    }
}
