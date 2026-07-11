using GPM.App.ViewModels;

namespace GPM.App;

public partial class MainWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this); // follow OS dark/light
    }
}
