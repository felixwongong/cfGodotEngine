namespace cfGodotEngine.Debug
{
    public readonly struct RxBookmark
    {
        public readonly int DispatchOrder;
        public readonly string Label;
        public readonly long TimestampTicks;

        public RxBookmark(int dispatchOrder, string label, long timestampTicks)
        {
            DispatchOrder = dispatchOrder;
            Label = label;
            TimestampTicks = timestampTicks;
        }
    }
}