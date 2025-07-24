#if CF_GOOGLE_DRIVE

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using cfEngine.Logging;
using cfGodotEngine.Core;
using cfGodotEngine.Util;
using cfGodotTemplate.Util;
using cfGodotTemplate.GoogleDrive;

namespace cfGodotEngine.GoogleDrive;

public partial class DriveMirror {
    public static DriveMirror instance { get; }

    static DriveMirror() {
        ILogger logger = new GodotLogger();
        instance = new DriveMirror(new AssetDirectFileMirror(logger, Application.assetDataPath), logger);
    }

    public async Task RefreshWithProgressBar() {
        try {
            await foreach (var status in RefreshAsync())
            {
                EditorUtility.ShowProgress(status.progress, $"Refreshing Drive Mirror: {status.file.Name}");
            }
            EditorUtility.HideProgress();
        }
        catch (Exception e) {
            DriveUtil.godotLogger.LogException(e);
        }
    }
    
    public async Task ClearAllAndRefreshWithProgressBar() {
        try {
            await foreach (var status in ClearAllAndRefreshAsync()) {
            }
        }
        catch (Exception e) {
            DriveUtil.godotLogger.LogException(e);
        }
    }
}

#endif