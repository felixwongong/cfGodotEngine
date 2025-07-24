# Google Drive Mirror

When compiled with the `CF_GOOGLE_DRIVE` symbol, `DriveMirror` can synchronize files from Google Drive.

Configuration lives in `DriveMirrorSetting` (`res://Settings/GoogleDrive/DriveMirrorSetting.tres`). Each `SettingItem` maps a Drive link to a local asset path.

Refresh the mirror with a progress bar:

```csharp
await DriveMirror.instance.RefreshWithProgressBar();
```
