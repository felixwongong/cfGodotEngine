# Settings

Declare a setting by subclassing `Setting<T>` and applying `[SettingPath("res://path/to/setting.tres")]`.

Use `Setting<T>.GetSetting()` to load the resource at runtime:

```csharp
[SettingPath("res://Settings/MyGame/MySetting.tres")]
public partial class MySetting : Setting<MySetting> { }

var setting = MySetting.GetSetting();
```
