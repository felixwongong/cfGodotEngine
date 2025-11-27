using System;
using CatSweeper.Core;
using cfEngine;
using cfEngine.Core;
using cfEngine.Extension;
using cfEngine.Rx;
using cfGodotEngine.Util;

namespace cfGodotEngine.Core;

public class GameDomain: Domain
{
    public GameDomain()
    {
#if TOOLS
        GameDebugger.Instance.ConnectAsync().ContinueWithSynchronized(t =>
        {
            if (t.IsCompletedSuccessfully)
            {
                Log.LogInfo("GameDebugger connected successfully.");
            }
            else if (t.IsFaulted)
            {
                Log.LogException(t.Exception, "GameDebugger failed to connect.");
            }
        }); 
#endif
    }
    
    public override void HandleException(Exception ex)
    {
        base.HandleException(ex);
        Dispose();
        Application.Quit();
    }

    public override void Dispose()
    {
        base.Dispose();
        GameDebugger.Instance.Dispose();
    }
}