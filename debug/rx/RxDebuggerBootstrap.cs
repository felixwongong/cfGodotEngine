using Godot;


namespace cfGodotEngine.Debug
{
    /// <summary>
    /// Autoload bootstrap that attaches the RxRecorder hook and instantiates the
    /// in-game overlay. Always compiled as a Node so the `RxDebugger` autoload
    /// resolves in every configuration (Debug, ExportDebug, Release) and never
    /// reports "does not inherit from 'Node'". Behaviour is gated at runtime: in a
    /// release export the bootstrap is a no-op, so the Rx debugger stays unavailable
    /// in shipped release builds.
    /// </summary>
    public partial class RxDebuggerBootstrap : Node
    {
        private bool _recorderAttached;

        public override void _Ready()
        {
            if (!OS.IsDebugBuild() || !RxRecorder.InstrumentationCompiled)
                return;

            RxRecorder.Instance.Attach();
            _recorderAttached = true;
            GD.Print("[RxDebugger] Recorder attached; press F8 for overlay.");
            AddChild(new RxDebuggerOverlay());
        }

        public override void _ExitTree()
        {
            if (!_recorderAttached)
                return;

            RxRecorder.Instance.Detach();
            _recorderAttached = false;
        }
    }
}
