using System.Windows;

namespace GPM.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow(DesignData.MockFactory.CreateMainViewModel());
        window.Show();
    }
}
