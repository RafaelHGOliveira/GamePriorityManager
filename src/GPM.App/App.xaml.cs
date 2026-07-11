using System.Windows;
using GPM.Core.Worker;

namespace GPM.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Elevated worker mode: no window, apply and exit (spec "Elevated worker protocol").
        if (e.Args is ["--apply", var opsPath])
        {
            var exitCode = ApplyWorker.Run(opsPath, CompositionRoot.CreatePriorityService());
            Shutdown(exitCode);
            return;
        }

        new MainWindow(CompositionRoot.CreateMainViewModel()).Show();
    }
}
