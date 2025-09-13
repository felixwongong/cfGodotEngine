using System;
using cfEngine.Core;
using cfGodotEngine.Util;

namespace cfGodotEngine.Core;

public class GameDomain: Domain
{
    public override void HandleException(Exception ex)
    {
        base.HandleException(ex);
        Application.Quit();
    }
}