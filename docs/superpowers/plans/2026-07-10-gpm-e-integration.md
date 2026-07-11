# GPM Plan E — Integration, UI Completion, Probe & Release

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the real services into the UI (replacing plan D's mocks), implement the `--apply` elevated entry point, complete the remaining spec UI (manual add, exe verification, settings, I/O column, rescan/cache, icons), ship `GPM.Probe`, run the effectiveness smoke test (including the `IoPriority = 3` checkpoint), and produce the first portable release build.

**Architecture:** `App.OnStartup` routes: `--apply <path>` → run `ApplyWorker` against the real HKLM IFEO root and exit (no window); otherwise compose the real object graph and show `MainWindow`.

**Tech Stack:** Everything from plans 0/A/B/C/D, `Process`/`runas` for elevation, `System.Drawing.Common` (icon extraction), P/Invoke (`NtQueryInformationProcess`) in the probe.

**Prerequisites:** Plans 0, A, B, C, D ALL merged to `main`. Work on branch/worktree `ws-e-integration`. This plan is serial — do not start until the others are merged.

**Spec:** `docs/superpowers/specs/2026-07-10-gpm-mvp-design.md` — sections "Architecture" (apply flow), "Elevated worker protocol", "UI", "Testing", "Decisions" (distribution).

## Global Constraints

- Main app never runs elevated (`app.manifest` stays `asInvoker`); only the `--apply` relaunch elevates via `runas`.
- UAC declined (`Win32Exception`, native error 1223 `ERROR_CANCELLED`) must map to `null` from `IElevationLauncher` — changes stay pending.
- Production IFEO root: `RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)` + `PriorityValues.IfeoPath`.
- All new user-visible strings via `Strings.resx` (entries already exist from plan D1).
- Release build: self-contained single-file win-x64 (~70–150 MB expected; WPF supports neither trimming nor AOT).
- **Accepted simplification (call out in the PR):** the spec's "per-store progress" during first scan is implemented as a single "Scanning stores..." status line for the MVP.
- Commits: conventional prefixes, **never add a `Co-Authored-By` trailer**.

---

### Task E1: ElevationLauncher + `--apply` entry point + real composition root

**Files:**
- Create: `src/GPM.App/Services/ElevationLauncher.cs`, `src/GPM.App/CompositionRoot.cs`
- Modify: `src/GPM.App/App.xaml.cs`
- Test: manual (elevation and HKLM cannot run in CI); logic already covered by plan A/D tests.

**Interfaces:**
- Consumes: `ApplyWorker`, `PriorityService`, `RealRegistryRoot`, `PriorityValues` (plans A/0); `GameScanner`, all sources, `WindowsStoreRegistry`, `JsonConfigStore` (plan B); `ExeResolver`, `KnownGames` (plan C); `MainViewModel`, `MainWindow`, `IElevationLauncher` (plan D).
- Produces: `static IPriorityService CompositionRoot.CreatePriorityService()`, `static MainViewModel CompositionRoot.CreateMainViewModel()`; `ElevationLauncher : IElevationLauncher`.

- [ ] **Step 1: Implement `src/GPM.App/Services/ElevationLauncher.cs`**

```csharp
using System.ComponentModel;
using System.Diagnostics;

namespace GPM.App.Services;

public sealed class ElevationLauncher : IElevationLauncher
{
    private const int ErrorCancelled = 1223; // user declined the UAC prompt

    public async Task<int?> RunApplyAsync(string opsPath)
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!, $"--apply \"{opsPath}\"")
        {
            UseShellExecute = true,
            Verb = "runas",
        };
        try
        {
            using var process = Process.Start(psi)!;
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return null;
        }
    }
}
```

- [ ] **Step 2: Implement `src/GPM.App/CompositionRoot.cs`**

```csharp
using GPM.App.Services;
using GPM.App.ViewModels;
using GPM.Core.Detection;
using GPM.Core.Detection.Steam;
using GPM.Core.Persistence;
using GPM.Core.Priority;
using GPM.Core.Registry;
using GPM.Core.Resolve;
using Microsoft.Win32;

namespace GPM.App;

public static class CompositionRoot
{
    public static IPriorityService CreatePriorityService() =>
        new PriorityService(new RealRegistryRoot(
            RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
            PriorityValues.IfeoPath));

    public static MainViewModel CreateMainViewModel()
    {
        var storeRegistry = new WindowsStoreRegistry();
        var scanner = new GameScanner(
        [
            new SteamSource(storeRegistry),
            new EpicSource(),
            new GogSource(storeRegistry),
            new EaSource(storeRegistry),
            new UbisoftSource(storeRegistry),
        ]);
        var config = new JsonConfigStore(
            preferredDir: AppContext.BaseDirectory,
            fallbackDir: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GPM"));

        return new MainViewModel(
            scanGames: () => scanner.ScanAsync(config.Load().BetaSourcesEnabled),
            resolver: new ExeResolver(KnownGames.LoadEmbedded()),
            priorities: CreatePriorityService(),
            elevation: new ElevationLauncher(),
            config: config);
    }
}
```

- [ ] **Step 3: Rewrite `src/GPM.App/App.xaml.cs`** (worker route + real wiring; `MockFactory` remains only for design use)

```csharp
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
```

- [ ] **Step 4: Verify**

Run: `dotnet test tests/GPM.Core.Tests` — all green (nothing regressed).
Run: `dotnet run --project src/GPM.App` — the window opens and lists YOUR real installed games (first scan may take seconds). Change one CPU value → Apply → UAC prompt → accept → row goes clean. Confirm in `regedit` under `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\<exe>\PerfOptions`. Set back to Default → Apply → key disappears. Then: change → Apply → decline UAC → status shows "Apply cancelled" and the row stays dirty.

- [ ] **Step 5: Commit**

```powershell
git add src/GPM.App
git commit -m "feat: real composition root, elevation launcher and --apply worker entry"
```

---

### Task E2: Manual games + exe verification

**Files:**
- Modify: `src/GPM.App/ViewModels/MainViewModel.cs`, `src/GPM.App/ViewModels/GameRowViewModel.cs`, `src/GPM.App/MainWindow.xaml`, `src/GPM.App/MainWindow.xaml.cs`
- Test: `tests/GPM.Core.Tests/ManualGamesTests.cs`

**Interfaces:**
- Consumes: everything from plan D; `GpmConfig.ManualGames`/`ExeChoices` (plan 0).
- Produces: `Task MainViewModel.AddManualGameAsync(string exePath)`, `Task RemoveManualGameAsync(GameRowViewModel row)`, `Task ConfirmExeAsync(GameRowViewModel row, string exePath)`; `bool GameRowViewModel.IsManual`.

- [ ] **Step 1: Write the failing tests**

`tests/GPM.Core.Tests/ManualGamesTests.cs`:
```csharp
using GPM.App.Services;
using GPM.App.ViewModels;
using GPM.Core.Models;
using GPM.Core.Persistence;
using GPM.Core.Priority;
using GPM.Core.Resolve;

namespace GPM.Core.Tests;

public class ManualGamesTests
{
    private sealed class PassthroughResolver : IExeResolver
    {
        public ExeResolution Resolve(Game game) => new(game.ExePath, game.ExePath is null, []);
    }

    private sealed class NoopLauncher : IElevationLauncher
    {
        public Task<int?> RunApplyAsync(string opsPath) => Task.FromResult<int?>(0);
    }

    private sealed class MemoryConfigStore : IConfigStore
    {
        public GpmConfig Cfg { get; private set; } = new();
        public string FilePath => "test://games.json";
        public GpmConfig Load() => Cfg;
        public void Save(GpmConfig config) => Cfg = config;
    }

    private static MainViewModel Make(MemoryConfigStore cfg, params Game[] detected) =>
        new(() => Task.FromResult<IReadOnlyList<Game>>(detected),
            new PassthroughResolver(), new InMemoryPriorityService(), new NoopLauncher(), cfg);

    [Fact]
    public async Task add_manual_game_persists_and_shows_as_removable_row()
    {
        var cfg = new MemoryConfigStore();
        var vm = Make(cfg);
        await vm.LoadAsync();

        await vm.AddManualGameAsync(@"C:\tools\voicemeeterpro.exe");

        var row = Assert.Single(vm.Rows);
        Assert.Equal("voicemeeterpro", row.Game.Name);
        Assert.Equal(GameStore.Manual, row.Game.Store);
        Assert.True(row.IsManual);
        Assert.Single(cfg.Cfg.ManualGames);
    }

    [Fact]
    public async Task remove_manual_game_deletes_it_from_config_and_rows()
    {
        var cfg = new MemoryConfigStore();
        var vm = Make(cfg);
        await vm.LoadAsync();
        await vm.AddManualGameAsync(@"C:\tools\a.exe");

        await vm.RemoveManualGameAsync(vm.Rows.Single());

        Assert.Empty(vm.Rows);
        Assert.Empty(cfg.Cfg.ManualGames);
    }

    [Fact]
    public async Task remove_is_a_noop_for_store_games()
    {
        var cfg = new MemoryConfigStore();
        var vm = Make(cfg, new Game { Name = "CS2", Store = GameStore.Steam, ExePath = @"C:\s\cs2.exe" });
        await vm.LoadAsync();

        await vm.RemoveManualGameAsync(vm.Rows.Single());
        Assert.Single(vm.Rows);
    }

    [Fact]
    public async Task confirmed_exe_choice_persists_and_wins_over_resolver()
    {
        // ExeChoices only applies when the chosen file exists -> use a real temp file
        var dir = Directory.CreateTempSubdirectory("gpm-choice-").FullName;
        var chosen = Path.Combine(dir, "real_game.exe");
        File.WriteAllBytes(chosen, [0]);

        var cfg = new MemoryConfigStore();
        var game = new Game { Name = "Ambiguous", Store = GameStore.Steam, StoreId = "42", InstallDir = dir };
        var vm = Make(cfg, game);
        await vm.LoadAsync();

        await vm.ConfirmExeAsync(vm.Rows.Single(), chosen);

        Assert.Equal(chosen, cfg.Cfg.ExeChoices["Steam:42"]);
        var row = vm.Rows.Single();
        Assert.Equal("real_game.exe", row.State!.ExeName);
        Assert.False(row.NeedsExeConfirmation);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/GPM.Core.Tests --filter ManualGamesTests`
Expected: FAIL — the new MainViewModel methods don't exist.

- [ ] **Step 3: Implement the ViewModel changes**

Add to `src/GPM.App/ViewModels/GameRowViewModel.cs` (inside the class):
```csharp
    public bool IsManual => Game.Store == GameStore.Manual;
```

Add to `src/GPM.App/ViewModels/MainViewModel.cs` (inside the class):
```csharp
    private static string ExeChoiceKey(Game g) => $"{g.Store}:{g.StoreId ?? g.InstallDir ?? g.Name}";

    public async Task AddManualGameAsync(string exePath)
    {
        var cfg = config.Load();
        cfg.ManualGames.Add(new Game
        {
            Name = Path.GetFileNameWithoutExtension(exePath),
            Store = GameStore.Manual,
            InstallDir = Path.GetDirectoryName(exePath),
            ExePath = exePath,
        });
        config.Save(cfg);
        await LoadAsync();
    }

    public async Task RemoveManualGameAsync(GameRowViewModel row)
    {
        if (row.Game.Store != GameStore.Manual) return;
        var cfg = config.Load();
        cfg.ManualGames.RemoveAll(g => g.ExePath == row.Game.ExePath);
        config.Save(cfg);
        await LoadAsync();
    }

    public async Task ConfirmExeAsync(GameRowViewModel row, string exePath)
    {
        var cfg = config.Load();
        cfg.ExeChoices[ExeChoiceKey(row.Game)] = exePath;
        config.Save(cfg);
        await LoadAsync();
    }
```

In `LoadAsync`, replace the `resolved` block with (user choice wins over the resolver):
```csharp
        var resolved = games.Select(g =>
        {
            if (cfg.ExeChoices.TryGetValue(ExeChoiceKey(g), out var chosen) && File.Exists(chosen))
                return g with { ExePath = chosen, NeedsExeConfirmation = false, ExeCandidates = [] };
            var r = resolver.Resolve(g);
            return g with
            {
                ExePath = r.ExePath ?? g.ExePath,
                NeedsExeConfirmation = r.NeedsConfirmation,
                ExeCandidates = r.Candidates,
            };
        }).ToList();
```

- [ ] **Step 4: Wire the dialogs**

In `src/GPM.App/MainWindow.xaml`, give the Add button a handler:
```xml
            <ui:Button DockPanel.Dock="Right" Margin="8,0,0,0"
                       Content="{x:Static app:Strings.AddGame}" Click="OnAddGameClick" />
```

In the game-row `StackPanel` (column 2), after the Bug icon, add:
```xml
                            <ui:Button Margin="6,0,0,0" Padding="6,0" FontSize="11"
                                       Content="{x:Static app:Strings.VerifyExe}" Click="OnVerifyExeClick"
                                       Visibility="{Binding NeedsExeConfirmation, Converter={StaticResource BoolToVis}}" />
                            <ui:Button Margin="6,0,0,0" Padding="6,0"
                                       Icon="{ui:SymbolIcon Dismiss24}" Click="OnRemoveGameClick"
                                       Visibility="{Binding IsManual, Converter={StaticResource BoolToVis}}" />
```

Add to `src/GPM.App/MainWindow.xaml.cs`:
```csharp
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
```

- [ ] **Step 5: Run tests, verify manually, commit**

Run: `dotnet test tests/GPM.Core.Tests --filter ManualGamesTests` — PASS (4 tests).
Run: `dotnet run --project src/GPM.App` — "+ Add game" picks any exe and it appears with a ✕; ✕ removes it; a game flagged "verify exe" opens the picker seeded at its install dir.

```powershell
git add src/GPM.App tests/GPM.Core.Tests/ManualGamesTests.cs
git commit -m "feat: manual games and exe verification flow"
```

---

### Task E3: Settings, I/O column, rescan + cache, icons, error display

**Files:**
- Modify: `src/GPM.Core/Persistence/GpmConfig.cs`, `src/GPM.App/ViewModels/MainViewModel.cs`, `src/GPM.App/MainWindow.xaml`, `src/GPM.App/MainWindow.xaml.cs`, `src/GPM.App/GPM.App.csproj`, `src/GPM.App/App.xaml`
- Create: `src/GPM.App/ExeIconConverter.cs`
- Test: `tests/GPM.Core.Tests/SettingsCacheTests.cs`

**Interfaces:**
- Consumes: plan D ViewModels, `GpmConfig` (plan 0), `Strings` entries `Settings_*`, `ScanProgress` (plan D1).
- Produces: `GpmConfig.DetectedGames` (new `List<Game>`); observable `MainViewModel.ShowIoColumn`, `BetaSourcesEnabled`, `Theme`, `string ConfigPath`, `IAsyncRelayCommand RescanCommand`; `LoadAsync(bool forceScan = false)` uses the cache.

- [ ] **Step 1: Write the failing tests**

`tests/GPM.Core.Tests/SettingsCacheTests.cs`:
```csharp
using GPM.App.Services;
using GPM.App.ViewModels;
using GPM.Core.Models;
using GPM.Core.Persistence;
using GPM.Core.Priority;
using GPM.Core.Resolve;

namespace GPM.Core.Tests;

public class SettingsCacheTests
{
    private sealed class PassthroughResolver : IExeResolver
    {
        public ExeResolution Resolve(Game game) => new(game.ExePath, false, []);
    }

    private sealed class NoopLauncher : IElevationLauncher
    {
        public Task<int?> RunApplyAsync(string opsPath) => Task.FromResult<int?>(0);
    }

    private sealed class MemoryConfigStore : IConfigStore
    {
        public GpmConfig Cfg { get; private set; } = new();
        public string FilePath => "test://games.json";
        public GpmConfig Load() => Cfg;
        public void Save(GpmConfig config) => Cfg = config;
    }

    private static Game G(string name) =>
        new() { Name = name, Store = GameStore.Steam, ExePath = $@"C:\g\{name}.exe" };

    [Fact]
    public async Task first_load_scans_and_caches_second_load_uses_cache()
    {
        var cfg = new MemoryConfigStore();
        var scans = 0;
        var vm = new MainViewModel(
            () => { scans++; return Task.FromResult<IReadOnlyList<Game>>([G("cs2")]); },
            new PassthroughResolver(), new InMemoryPriorityService(), new NoopLauncher(), cfg);

        await vm.LoadAsync();
        Assert.Equal(1, scans);
        Assert.Single(cfg.Cfg.DetectedGames);

        await vm.LoadAsync();          // cache hit
        Assert.Equal(1, scans);
        Assert.Single(vm.Rows);

        await vm.LoadAsync(forceScan: true); // rescan button
        Assert.Equal(2, scans);
    }

    [Fact]
    public async Task settings_load_from_config_and_persist_on_change()
    {
        var cfg = new MemoryConfigStore();
        cfg.Cfg.ShowIoColumn = true;
        cfg.Cfg.Theme = "dark";
        var vm = new MainViewModel(
            () => Task.FromResult<IReadOnlyList<Game>>([]),
            new PassthroughResolver(), new InMemoryPriorityService(), new NoopLauncher(), cfg);

        await vm.LoadAsync();
        Assert.True(vm.ShowIoColumn);
        Assert.Equal("dark", vm.Theme);

        vm.BetaSourcesEnabled = true;
        vm.Theme = "light";
        Assert.True(cfg.Cfg.BetaSourcesEnabled);
        Assert.Equal("light", cfg.Cfg.Theme);
        Assert.True(cfg.Cfg.ShowIoColumn); // untouched settings survive
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/GPM.Core.Tests --filter SettingsCacheTests`
Expected: FAIL — `DetectedGames`, `forceScan`, settings properties don't exist.

- [ ] **Step 3: Implement Core + ViewModel changes**

Add to `GpmConfig` (`src/GPM.Core/Persistence/GpmConfig.cs`), after `ManualGames`:
```csharp
    /// Detected-game cache (spec "UI": first-run scan cached; rescan via the toolbar button).
    public List<Game> DetectedGames { get; set; } = [];
```

In `src/GPM.App/ViewModels/MainViewModel.cs`:

Add the observable settings + rescan members:
```csharp
    [ObservableProperty] private bool showIoColumn;
    [ObservableProperty] private bool betaSourcesEnabled;
    [ObservableProperty] private string theme = "auto";

    public string ConfigPath => config.FilePath;

    private bool _loadingSettings;

    partial void OnShowIoColumnChanged(bool value) => SaveSettings();
    partial void OnBetaSourcesEnabledChanged(bool value) => SaveSettings();
    partial void OnThemeChanged(string value) => SaveSettings();

    private void SaveSettings()
    {
        if (_loadingSettings) return;
        var cfg = config.Load();
        cfg.ShowIoColumn = ShowIoColumn;
        cfg.BetaSourcesEnabled = BetaSourcesEnabled;
        cfg.Theme = Theme;
        config.Save(cfg);
    }

    [RelayCommand]
    private Task RescanAsync() => LoadAsync(forceScan: true);
```

Change `LoadAsync`'s signature and its opening so the cache is used (full updated opening — everything from the resolver `resolved = ...` line onward is unchanged from E2):
```csharp
    public async Task LoadAsync(bool forceScan = false)
    {
        var cfg = config.Load();
        _loadingSettings = true;
        ShowIoColumn = cfg.ShowIoColumn;
        BetaSourcesEnabled = cfg.BetaSourcesEnabled;
        Theme = cfg.Theme;
        _loadingSettings = false;

        List<Game> detected;
        if (!forceScan && cfg.DetectedGames.Count > 0)
        {
            detected = cfg.DetectedGames; // cache (spec: cached in games.json)
        }
        else
        {
            StatusMessage = string.Format(Strings.ScanProgress, "stores"); // simplified per-store progress
            detected = (await scanGames()).ToList();
            cfg.DetectedGames = detected;
            config.Save(cfg);
            StatusMessage = null;
        }
        var games = detected.Concat(cfg.ManualGames).ToList();
        // ... resolver block and the rest exactly as before ...
```

- [ ] **Step 4: Icons, I/O column, settings popup, error tooltip (XAML + code-behind)**

Add to `src/GPM.App/GPM.App.csproj` package group:
```xml
    <PackageReference Include="System.Drawing.Common" Version="10.*" />
```

Create `src/GPM.App/ExeIconConverter.cs`:
```csharp
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace GPM.App;

/// exe path -> its embedded icon as an ImageSource (spec "UI": icons from the exe).
public sealed class ExeIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path)) return null;
        using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
        if (icon is null) return null;
        var source = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

Register it in `App.xaml` resources next to `BoolToVis`:
```xml
            <app:ExeIconConverter x:Key="ExeIcon" />
```
(add `xmlns:app="clr-namespace:GPM.App"` to the `Application` element.)

In `MainWindow.xaml`:

1. Toolbar — add rescan + settings on the right of the Add button:
```xml
            <ui:Button DockPanel.Dock="Right" Margin="8,0,0,0" Icon="{ui:SymbolIcon ArrowClockwise24}"
                       Command="{Binding RescanCommand}" />
            <ToggleButton DockPanel.Dock="Right" Margin="8,0,0,0" x:Name="SettingsToggle" Content="⚙" />
            <Popup PlacementTarget="{Binding ElementName=SettingsToggle}" Placement="Bottom" StaysOpen="False"
                   IsOpen="{Binding IsChecked, ElementName=SettingsToggle, Mode=TwoWay}">
                <Border Background="{DynamicResource ApplicationBackgroundBrush}" Padding="12" CornerRadius="8">
                    <StackPanel>
                        <CheckBox Content="{x:Static app:Strings.Settings_ShowIo}"
                                  IsChecked="{Binding ShowIoColumn}" />
                        <CheckBox Content="{x:Static app:Strings.Settings_BetaSources}"
                                  IsChecked="{Binding BetaSourcesEnabled}" />
                        <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
                            <TextBlock Text="{x:Static app:Strings.Settings_Theme}" VerticalAlignment="Center" Margin="0,0,8,0" />
                            <ComboBox SelectedValue="{Binding Theme}" SelectedValuePath="Tag">
                                <ComboBoxItem Tag="auto" Content="auto" />
                                <ComboBoxItem Tag="dark" Content="dark" />
                                <ComboBoxItem Tag="light" Content="light" />
                            </ComboBox>
                        </StackPanel>
                        <TextBlock Text="{Binding ConfigPath}" Opacity="0.5" FontSize="11" Margin="0,8,0,0" />
                    </StackPanel>
                </Border>
            </Popup>
```

2. Game-row template — icon at the start, I/O column before the dirty dot (row grid becomes 7 columns; full updated `ColumnDefinitions` + the two new cells):
```xml
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="28" />   <!-- icon -->
                            <ColumnDefinition Width="*" />    <!-- name -->
                            <ColumnDefinition Width="90" />   <!-- store -->
                            <ColumnDefinition Width="170" />  <!-- exe -->
                            <ColumnDefinition Width="130" />  <!-- cpu -->
                            <ColumnDefinition Width="Auto" /> <!-- io (toggle) -->
                            <ColumnDefinition Width="24" />   <!-- dirty -->
                        </Grid.ColumnDefinitions>
                        <Image Grid.Column="0" Width="20" Height="20"
                               Source="{Binding Game.ExePath, Converter={StaticResource ExeIcon}}" />
```
Shift the existing cells one column right (name=1, store=2, exe panel=3, cpu combo=4) and add:
```xml
                        <ComboBox Grid.Column="5" Width="120" Margin="4,0,0,0"
                                  ItemsSource="{x:Static vm:PriorityOptions.Io}"
                                  DisplayMemberPath="Label" SelectedValuePath="Value"
                                  SelectedValue="{Binding State.PendingIo}"
                                  Visibility="{Binding DataContext.ShowIoColumn,
                                      RelativeSource={RelativeSource AncestorType=Window},
                                      Converter={StaticResource BoolToVis}}" />
```
Dirty dot moves to `Grid.Column="6"` and gains the error tooltip (spec: failed rows return to dirty with the specific error):
```xml
                        <TextBlock Grid.Column="6" Text="●" Foreground="#4cc2ff" VerticalAlignment="Center"
                                   HorizontalAlignment="Center" ToolTip="{Binding State.LastError}"
                                   Visibility="{Binding State.IsDirty, Converter={StaticResource BoolToVis}}" />
```
(Also add `xmlns:app="clr-namespace:GPM.App"` usage is already present in MainWindow.)

3. Theme application in `MainWindow.xaml.cs` — replace the constructor and add the helper:
```csharp
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
```

- [ ] **Step 5: Run tests, verify manually, commit**

Run: `dotnet test tests/GPM.Core.Tests` — all green (including the 2 new SettingsCacheTests).
Run: `dotnet run --project src/GPM.App` — second launch lists games instantly (cache); ⟳ rescans; ⚙ popup toggles the I/O column live, beta sources and theme persist across restarts; game icons render; `games.json` shows `detectedGames`.

```powershell
git add src/GPM.Core/Persistence/GpmConfig.cs src/GPM.App tests/GPM.Core.Tests/SettingsCacheTests.cs
git commit -m "feat: settings, io column, scan cache with rescan, exe icons and error tooltips"
```

---

### Task E4: GPM.Probe (effectiveness probe)

**Files:**
- Create: `src/GPM.Probe/Program.cs` (replace template)

**Interfaces:**
- Consumes: nothing from GPM (standalone by design — it must behave like any external game exe).
- Produces: a console exe that prints its own effective CPU and IO priority.

- [ ] **Step 1: Implement `src/GPM.Probe/Program.cs`**

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;

// Prints this process's EFFECTIVE priorities. Used to verify that IFEO
// PerfOptions overrides are actually honored by Windows (the product's core
// promise) — including the IoPriority=3 implementation checkpoint from the spec.

var process = Process.GetCurrentProcess();
Console.WriteLine($"Process:            {Path.GetFileName(Environment.ProcessPath)}");
Console.WriteLine($"CPU priority class: {process.PriorityClass}");

int ioPriority = -1;
int status = NtQueryInformationProcess(process.Handle, ProcessIoPriority, ref ioPriority, sizeof(int), out _);
Console.WriteLine(status == 0
    ? $"IO priority:        {ioPriority} (0=VeryLow 1=Low 2=Normal 3=High)"
    : $"IO priority:        query failed (NTSTATUS 0x{status:X8})");

Console.WriteLine();
Console.WriteLine("Press Enter to exit.");
Console.ReadLine();

partial class Program
{
    private const int ProcessIoPriority = 33;

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        nint processHandle, int processInformationClass,
        ref int processInformation, int processInformationLength, out int returnLength);
}
```

- [ ] **Step 2: Verify baseline**

Run: `dotnet run --project src/GPM.Probe`
Expected output (no override yet):
```
Process:            GPM.Probe.exe
CPU priority class: Normal
IO priority:        2 (0=VeryLow 1=Low 2=Normal 3=High)
```

- [ ] **Step 3: Commit**

```powershell
git add src/GPM.Probe
git commit -m "feat: GPM.Probe effective-priority probe"
```

---

### Task E5: Effectiveness smoke test (manual, admin) — the IoPriority=3 checkpoint

**Files:**
- Create: `docs/manual-smoke-test.md` (record the results)

This validates the product's core promise. Registry writes alone prove nothing.

- [ ] **Step 1: Build the probe and add it as a manual game**

```powershell
dotnet publish src/GPM.Probe -c Release -o out/probe
dotnet run --project src/GPM.App
```
In GPM: "+ Add game" → pick `out/probe/GPM.Probe.exe`. Enable "Show I/O column" in ⚙. Set CPU = High, IO = High. Apply, accept UAC.

- [ ] **Step 2: Launch the probe from a NEW terminal and read its output**

Run: `out/probe/GPM.Probe.exe`
Expected: `CPU priority class: High`. Record what IO priority prints:
- If `IO priority: 3` → Windows honors High via IFEO. Keep the UI as is.
- If `IO priority: 2` (write ignored) → **spec checkpoint triggered**: remove the `High` entry from `PriorityOptions.Io` in `src/GPM.App/ViewModels/PriorityOptions.cs`, update `PriorityOptionsTests.io_options_match_spec` (expected values become `[null, 0, 1, 2]`), and note the finding in `docs/manual-smoke-test.md`.

Cross-check both values in Process Explorer (process properties) or Task Manager (Details → Base priority column).

- [ ] **Step 3: Full manual smoke checklist** — record each item in `docs/manual-smoke-test.md`:

```markdown
# GPM manual smoke test — YYYY-MM-DD

- [ ] First run: auto-scan lists installed games with store badges and icons
- [ ] Second run: instant list from cache; ⟳ rescans
- [ ] Search filters the list
- [ ] CPU dropdown shows: Default, Idle, Below Normal, Normal, Above Normal, High (NO Realtime)
- [ ] I/O column hidden by default; ⚙ toggle shows it
- [ ] Dirty row shows ●; bottom bar counts pending
- [ ] Discard reverts all pending changes
- [ ] Apply: single UAC prompt for a multi-row batch
- [ ] UAC declined: "Apply cancelled", changes stay pending
- [ ] Applied override visible in regedit under IFEO\<exe>\PerfOptions
- [ ] Default+Default then Apply: exe key removed from IFEO (regedit)
- [ ] GPM.Probe launched with override prints CPU = High
- [ ] IoPriority=3 checkpoint result: honored / not honored (value printed: __)
- [ ] Manual game: add via picker, remove via ✕
- [ ] "Verify exe" opens picker at the game's install dir; choice survives rescan
- [ ] Other overrides section lists non-game overrides; orphan flagged (with cleanup) after removing a manual game that had an override
- [ ] Rows sharing an exe filename show 🔗 and edit together
- [ ] Theme auto/dark/light works and persists
- [ ] games.json created next to the exe; when exe dir is read-only, falls back to %APPDATA%\GPM
```

- [ ] **Step 4: Commit the results**

```powershell
git add docs/manual-smoke-test.md src/GPM.App tests/GPM.Core.Tests
git commit -m "test: manual effectiveness smoke results (incl. IoPriority=3 checkpoint)"
```

---

### Task E6: Portable release build

**Files:**
- Create: `docs/release.md`

- [ ] **Step 1: Publish**

```powershell
dotnet publish src/GPM.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o out/release
```
Expected: `out/release/GPM.App.exe` exists; size in the 70–150 MB range.

- [ ] **Step 2: Verify the portable exe standalone**

Copy `out/release/GPM.App.exe` to an empty folder (e.g. `%TEMP%\gpm-portable-test`) and run it from there. Expected: window opens, scan works, `games.json` appears next to the exe.

- [ ] **Step 3: Document the release procedure in `docs/release.md`**

```markdown
# Release procedure

1. `dotnet test tests/GPM.Core.Tests` — must be green.
2. `dotnet publish src/GPM.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out/release`
3. Rename `out/release/GPM.App.exe` to `GPM-vX.Y.Z.exe`.
4. Run the manual smoke checklist (docs/manual-smoke-test.md) against the published exe.
5. Create a GitHub Release, attach the exe, note the size and SHA256
   (`Get-FileHash out/release/GPM-vX.Y.Z.exe`).

Known expectations: single exe ~70–150 MB (self-contained WPF; no trimming/AOT
support). AV vendors may flag unsigned new exes — document the HKLM write
rationale in the release notes (spec "Error handling").
```

- [ ] **Step 4: Commit**

```powershell
git add docs/release.md
git commit -m "chore: portable release build procedure"
```

Plan E complete — MVP done. Follow superpowers:finishing-a-development-branch to merge and tag `v0.1.0`.
