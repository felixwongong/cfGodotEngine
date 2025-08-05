#if CF_GOOGLE_DRIVE

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using cfEngine.Logging;
using cfGodotEngine.Core;
using cfGodotEngine.Util;
using cfGodotEngine.GoogleDrive;

namespace cfGodotEngine.GoogleDrive;

public partial class DriveMirror {
    public static DriveMirror instance { get; }

    static DriveMirror() {
        ILogger logger = new GodotLogger();
        instance = new DriveMirror(new AssetDirectFileMirror(logger, Application.assetDataPath), logger);
    }

    public async Task RefreshWithProgressBar() {
        try
        {
            await RefreshAsync();
        }
        catch (Exception e) {
            DriveUtil.godotLogger.LogException(e);
        }
    }
    
    public async Task ClearAllAndRefreshWithProgressBar() {
        try
        {
            await ClearAllAndRefreshAsync();
        }
        catch (Exception e) {
            DriveUtil.godotLogger.LogException(e);
        }
    }
}

#endif