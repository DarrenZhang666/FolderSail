using FolderSail.Core.Services;
using FolderSail.Helpers;

namespace FolderSail;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        var dark = new SettingsStore().Load().IsDarkTheme;
        ThemeManager.Apply(dark);
        base.OnStartup(e);
    }
}
