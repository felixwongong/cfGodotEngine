#if CF_GOOGLE_DRIVE

using Godot;

namespace cfGodotEngine.GoogleDrive;

[Tool]
[GlobalClass]
public partial class SettingItem: Resource {
    [Export]
    public string fileName
    {
        get => _fileName;
        set
        {
            _fileName = value;
            SetName(value);
        }
    }

    private string _fileName;

    [Export] public string assetPath;
    [Export] public string driveLink;
}

#endif