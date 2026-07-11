using GPM.App.ViewModels;

namespace GPM.App;

public partial class MainWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Theme)) ApplyTheme(viewModel.Theme);
        };
        ApplyTheme(viewModel.Theme);
    }

    private void ApplyTheme(string theme)
    {
        switch (theme)
        {
            case "dark":
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
                break;
            case "light":
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
                break;
            default:
                Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this); // follow OS
                break;
        }
    }

    private async void OnAddGameClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Programs (*.exe)|*.exe" };
        if (dialog.ShowDialog(this) == true && DataContext is MainViewModel vm)
            await vm.AddManualGameAsync(dialog.FileName);
    }

    private async void OnVerifyExeClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm ||
            (sender as System.Windows.FrameworkElement)?.DataContext is not GameRowViewModel row) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Programs (*.exe)|*.exe",
            InitialDirectory = row.Game.InstallDir ?? "",
        };
        if (dialog.ShowDialog(this) == true)
            await vm.ConfirmExeAsync(row, dialog.FileName);
    }

    private async void OnRemoveGameClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm &&
            (sender as System.Windows.FrameworkElement)?.DataContext is GameRowViewModel row)
            await vm.RemoveManualGameAsync(row);
    }
}
