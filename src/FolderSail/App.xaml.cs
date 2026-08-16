using FolderSail.Core.Services;
using FolderSail.Helpers;

namespace FolderSail;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        var settings = new SettingsStore().Load();
        ThemeManager.Apply(settings.IsDarkTheme);
        LanguageManager.Apply(settings.Language);
        base.OnStartup(e);
    }
}
