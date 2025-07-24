#if CF_GOOGLE_DRIVE

using Godot;

namespace cfGodotEngine.GoogleDrive;

[Tool]
[GlobalClass]
public partial class SettingItem: Resource {
    [Export] public string fileName;
    [Export] public string assetPath;
    [Export] public string driveLink;
}

#endif