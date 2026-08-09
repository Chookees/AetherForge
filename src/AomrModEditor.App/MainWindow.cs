using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Themes.Fluent;
using AomrModEditor.Api.Client;
using AomrModEditor.Api.Contracts.V1;

namespace AomrModEditor.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

public sealed class App : Avalonia.Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            desktop.Exit += async (_, _) => await window.ShutdownEmbeddedAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

public sealed class MainWindow : Window
{
    private static readonly string[] AlwaysNav = ["Home", "Setup"];
    private static readonly string[] GatedNav =
        ["Entity Browser", "Unit Editor", "God Power Editor", "Diff Workspace", "Generation", "Migration"];

    private readonly EmbeddedApiHost _host = new();
    private readonly ContentControl _contentHost = new();
    private readonly TextBlock _status = new() { Foreground = Brush("#A8A59C"), FontSize = 12 };
    private readonly ListBox _nav = new();
    private readonly Border _blockingOverlay;
    private readonly TextBlock _blockingMessage;
    private AomrApiClient? _client;
    private string _importPath = "";
    private string _localModsPath = "";
    private string _iconSearchRoot = "";
    private string _selectedEntity = "";
    private string _selectedCategory = "";
    private bool _sourceLoaded;
    private bool _uiBlocked;
    private int _entityCount;
    private DiscoveryResultDto? _lastDiscovery;

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    public MainWindow()
    {
        Title = "Aether Forge";
        Width = 1280;
        Height = 800;
        Background = Brush("#131316");
        try
        {
            using var iconStream = AssetLoader.Open(new Uri("avares://AomrModEditor.App/Assets/aether-forge-icon.png"));
            Icon = new WindowIcon(iconStream);
        }
        catch
        {
            // Icon is optional at design-time; ignore missing asset.
        }

        _nav.Width = 200;
        _nav.Background = Brush("#1b1b1e");
        RebuildNav();
        _nav.SelectionChanged += (_, _) =>
        {
            if (_uiBlocked) return;
            if (_nav.SelectedItem is NavItem { Enabled: false })
            {
                SetStatus("Load a data source in Setup first.");
                _nav.SelectedItem = AlwaysNav.Contains((_nav.SelectedItem as NavItem)?.Title ?? "")
                    ? _nav.SelectedItem
                    : _nav.Items.OfType<NavItem>().First(i => i.Title == "Setup");
            }

            Navigate();
        };

        var brand = new TextBlock
        {
            Text = "Aether Forge",
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#ffb779"),
            Margin = new Thickness(16, 12, 16, 4)
        };
        var top = new DockPanel { Height = 48, Background = Brush("#1b1b1e") };
        DockPanel.SetDock(brand, Dock.Left);
        top.Children.Add(brand);
        top.Children.Add(new TextBlock
        {
            Text = "Age of Mythology: Retold Mod Editor",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("#e4e1e5"),
            Margin = new Thickness(8, 0, 0, 0)
        });

        var statusBar = new Border
        {
            Height = 32,
            Background = Brush("#1b1b1e"),
            Child = _status,
            Padding = new Thickness(12, 6)
        };

        var body = new DockPanel();
        DockPanel.SetDock(_nav, Dock.Left);
        body.Children.Add(_nav);
        _contentHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        _contentHost.VerticalAlignment = VerticalAlignment.Stretch;
        body.Children.Add(_contentHost);

        var shell = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(statusBar, Dock.Bottom);
        shell.Children.Add(top);
        shell.Children.Add(statusBar);
        shell.Children.Add(body);

        _blockingMessage = new TextBlock
        {
            Text = "Discovering Age of Mythology: Retold…",
            FontSize = 16,
            Foreground = Brush("#e4e1e5"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 480,
            TextAlignment = TextAlignment.Center
        };
        _blockingOverlay = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#CC0e0e11")),
            IsVisible = false,
            IsHitTestVisible = false,
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Please wait",
                        FontSize = 22,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brush("#ffb779"),
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    _blockingMessage
                }
            }
        };

        var root = new Grid();
        root.Children.Add(shell);
        root.Children.Add(_blockingOverlay);
        Content = root;

        // Show shell immediately; block + discover after first layout.
        Navigate();
        Opened += async (_, _) => await OnOpenedAsync();
    }

    private async Task OnOpenedAsync()
    {
        try
        {
            SetBlocking(true, "Starting local API…");
            await _host.StartAsync();
            _client = _host.Client;
            SetStatus($"API connected {_host.BaseUrl}");
            Navigate();

            SetBlocking(true, "Searching for Age of Mythology: Retold install and local mods folders…\n(Steam account folders under Games\\Age of Mythology Retold vary by SteamID64.)");
            await Task.Yield(); // let overlay paint before disk scan
            var discovery = await RequireClient().DiscoverInstallationsAsync();
            ApplyDiscovery(discovery);
        }
        catch (Exception ex)
        {
            SetStatus("Startup failed: " + ex.Message);
            _blockingMessage.Text = ex.Message;
        }
        finally
        {
            SetBlocking(false);
        }
    }

    private void SetBlocking(bool blocked, string? message = null)
    {
        _uiBlocked = blocked;
        _blockingOverlay.IsVisible = blocked;
        _blockingOverlay.IsHitTestVisible = blocked;
        _nav.IsEnabled = !blocked;
        if (!string.IsNullOrWhiteSpace(message))
            _blockingMessage.Text = message;
        if (blocked)
            SetStatus(message ?? "Busy…");
    }

    private void ApplyDiscovery(DiscoveryResultDto? discovery)
    {
        _lastDiscovery = discovery;
        if (discovery is null)
        {
            SetStatus("Discovery returned no result.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(discovery.PreferredInstallPath))
            _iconSearchRoot = discovery.PreferredInstallPath;
        if (!string.IsNullOrWhiteSpace(discovery.PreferredLocalModsPath))
            _localModsPath = discovery.PreferredLocalModsPath;

        SetStatus(discovery.Summary);
        // Refresh Setup if currently visible so paths appear.
        if ((_nav.SelectedItem as NavItem)?.Title == "Setup")
            Navigate();
    }

    public async Task ShutdownEmbeddedAsync() => await _host.DisposeAsync();

    private void SetStatus(string text) => _status.Text = text;

    private AomrApiClient RequireClient()
        => _client ?? throw new InvalidOperationException("API client not ready.");

    private void RebuildNav()
    {
        var items = new List<NavItem>();
        foreach (var t in AlwaysNav)
            items.Add(new NavItem(t, true));
        foreach (var t in GatedNav)
            items.Add(new NavItem(t, _sourceLoaded));
        var selected = (_nav.SelectedItem as NavItem)?.Title ?? "Home";
        _nav.ItemsSource = items;
        _nav.SelectedItem = items.FirstOrDefault(i => i.Title == selected) ?? items[0];
    }

    private void MarkSourceLoaded(int count)
    {
        _sourceLoaded = true;
        _entityCount = count;
        RebuildNav();
    }

    private void Navigate()
    {
        var screen = (_nav.SelectedItem as NavItem)?.Title ?? "Home";
        if (GatedNav.Contains(screen) && !_sourceLoaded)
            screen = "Setup";

        _contentHost.Content = screen switch
        {
            "Setup" => BuildSetup(),
            "Entity Browser" => BuildEntityBrowser(),
            "Unit Editor" => BuildAdaptiveEditor(),
            "God Power Editor" => BuildAdaptiveEditor(forceGodPower: true),
            "Diff Workspace" => BuildDiff(),
            "Generation" => BuildGenerate(),
            "Migration" => BuildMigration(),
            _ => BuildHome()
        };
    }

    private Control BuildHome()
    {
        var panel = Screen("Home", "Welcome to the Age of Mythology: Retold Mod Editor.");
        panel.Children.Add(Info(_sourceLoaded
            ? $"Source loaded · {_entityCount} entities"
            : "Source not loaded — open Setup to load default data or browse a folder."));
        if (!string.IsNullOrWhiteSpace(_selectedEntity))
            panel.Children.Add(Info($"Selected entity: {_selectedEntity}"));
        return panel;
    }

    private Control BuildSetup()
    {
        var panel = Screen("Setup", "Confirm local mods folder and load gameplay data.");
        var disclaimer = new TextBlock
        {
            Text = "Legal disclaimer: This is an unofficial community tool, unaffiliated with Microsoft, Xbox Game Studios, World's Edge, or the Age of Mythology developers. Game files are required; proprietary retail data is never committed or uploaded.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#e4e1e5"),
            Margin = new Thickness(0, 4, 0, 8)
        };
        var accept = new CheckBox
        {
            Content = "I accept the unofficial-tool disclaimer",
            Foreground = Brush("#e4e1e5")
        };

        var modsBox = PathRow("Local mods", _localModsPath, p => _localModsPath = p);
        var importBox = PathRow("Import folder", _importPath, p => _importPath = p);

        var rediscover = new Button { Content = "Re-run discovery", Margin = new Thickness(0, 8, 8, 0), IsEnabled = false };
        var loadEmbedded = new Button { Content = "Load default data", Margin = new Thickness(0, 8, 8, 0), IsEnabled = false };
        var import = new Button { Content = "Import selected folder", Margin = new Thickness(0, 8, 8, 0), IsEnabled = false };
        var decline = new Button { Content = "Decline / Exit", Margin = new Thickness(0, 8, 0, 0) };
        var log = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush("#e4e1e5") };
        if (_lastDiscovery is not null)
        {
            log.Text = _lastDiscovery.Summary;
            if (_lastDiscovery.LocalModsRoots.Count > 1)
            {
                log.Text += "\nAccounts:\n" + string.Join("\n",
                    _lastDiscovery.LocalModsRoots.Select(m =>
                        $"  • {m.AccountFolderName ?? "(root)"} → {m.Path} ({m.ExistingModCount} mods)"));
            }
        }

        void SyncEnabled()
        {
            var ok = accept.IsChecked == true;
            modsBox.IsEnabled = ok;
            importBox.IsEnabled = ok;
            loadEmbedded.IsEnabled = ok;
            import.IsEnabled = ok;
            rediscover.IsEnabled = ok;
        }

        accept.IsCheckedChanged += (_, _) => SyncEnabled();
        decline.Click += (_, _) =>
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        };
        rediscover.Click += async (_, _) =>
        {
            try
            {
                SetBlocking(true, "Re-running installation discovery…");
                await Task.Yield();
                var discovery = await RequireClient().DiscoverInstallationsAsync();
                ApplyDiscovery(discovery);
                log.Text = discovery?.Summary ?? "No result.";
            }
            catch (Exception ex) { log.Text = ex.Message; }
            finally { SetBlocking(false); }
        };
        loadEmbedded.Click += async (_, _) =>
        {
            try
            {
                var snap = await RequireClient().LoadEmbeddedSourceAsync();
                var project = Path.Combine(Path.GetTempPath(), "aomr-ui-project");
                await RequireClient().CreateProjectAsync("ui", project);
                MarkSourceLoaded(snap?.EntityCount ?? 0);
                log.Text = $"Loaded embedded default data · {snap?.EntityCount} entities.";
                SetStatus("Default data loaded");
            }
            catch (Exception ex) { log.Text = ex.Message; }
        };
        import.Click += async (_, _) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_importPath))
                {
                    log.Text = "Browse to an import folder first (optional — or use Load default data).";
                    return;
                }

                var snap = await RequireClient().ImportSourceAsync(_importPath);
                var project = Path.Combine(Path.GetTempPath(), "aomr-ui-project");
                await RequireClient().CreateProjectAsync("ui", project);
                MarkSourceLoaded(snap?.EntityCount ?? 0);
                log.Text = $"Imported {snap?.EntityCount} entities from {_importPath}.";
                SetStatus("Import complete");
            }
            catch (Exception ex) { log.Text = ex.Message; }
        };

        panel.Children.Add(disclaimer);
        panel.Children.Add(accept);
        panel.Children.Add(modsBox);
        panel.Children.Add(importBox);
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { rediscover, loadEmbedded, import, decline }
        });
        panel.Children.Add(log);
        return panel;
    }

    private Control PathRow(string label, string initial, Action<string> onChanged)
    {
        var box = new TextBox { Text = initial, Watermark = label, Width = 520 };
        var browse = new Button { Content = "Browse…", Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = $"Browse {label}",
                AllowMultiple = false
            });
            var path = folders.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                box.Text = path;
                onChanged(path);
            }
        };
        box.TextChanged += (_, _) => onChanged(box.Text ?? "");
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(0, 4, 0, 0),
            Children =
            {
                new TextBlock { Text = label, Width = 140, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#d8c2b2") },
                box,
                browse
            }
        };
    }

    private Control BuildEntityBrowser()
    {
        var query = new TextBox { Watermark = "Filter entities…", Text = "" };
        var unitType = new TextBox { Watermark = "Filter by unittype (optional)", Margin = new Thickness(0, 4, 0, 0) };
        var list = new ListBox
        {
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        list.DataTemplates.Add(new FuncDataTemplate<EntityGroupHeader>((header, _) =>
        {
            var tb = new TextBlock
            {
                Text = header.Title.ToUpperInvariant(),
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush("#A8A59C"),
                Margin = new Thickness(0, 8, 0, 2)
            };
            AutomationProperties.SetName(tb, header.Title);
            AutomationProperties.SetControlTypeOverride(tb, AutomationControlType.Header);
            return tb;
        }, true));
        list.DataTemplates.Add(new FuncDataTemplate<EntityRow>((row, _) =>
            new TextBlock { Text = row.ToString(), Foreground = Brush("#e4e1e5") }, true));
        list.ContainerPrepared += (_, args) =>
        {
            if (args.Container is ListBoxItem { DataContext: EntityGroupHeader header } item)
            {
                AutomationProperties.SetName(item, header.Title);
                AutomationProperties.SetControlTypeOverride(item, AutomationControlType.Header);
                item.Focusable = false;
            }
        };
        var empty = new TextBlock { Text = "", Foreground = Brush("#A8A59C") };

        async Task RefreshAsync()
        {
            try
            {
                var page = await RequireClient().GetEntitiesAsync(
                    string.IsNullOrWhiteSpace(query.Text) ? null : query.Text,
                    unitType: string.IsNullOrWhiteSpace(unitType.Text) ? null : unitType.Text,
                    take: 50_000);
                var items = page?.Items.ToList() ?? [];
                list.ItemsSource = BuildGroupedEntityRows(items);
                empty.Text = items.Count == 0 ? "No entities match" : $"{items.Count} entities";
            }
            catch (Exception ex)
            {
                empty.Text = ex.Message;
                SetStatus(ex.Message);
            }
        }

        query.TextChanged += async (_, _) => await RefreshAsync();
        unitType.TextChanged += async (_, _) => await RefreshAsync();

        void OpenSelected()
        {
            if (list.SelectedItem is not EntityRow row) return;
            _selectedEntity = row.Key;
            _selectedCategory = row.Category;
            _nav.SelectedItem = _nav.Items.OfType<NavItem>().First(i =>
                i.Title == (row.Category.Equals("GodPower", StringComparison.OrdinalIgnoreCase)
                    ? "God Power Editor"
                    : "Unit Editor"));
        }

        list.DoubleTapped += (_, _) => OpenSelected();
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                OpenSelected();
                e.Handled = true;
            }
        };

        var header = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = "Entity Browser", FontSize = 24, FontWeight = FontWeight.SemiBold, Foreground = Brush("#e4e1e5") },
                new TextBlock { Text = "Search and open any loaded entity.", Foreground = Brush("#A8A59C") },
                query,
                unitType,
                empty
            }
        };

        var root = new DockPanel
        {
            Margin = new Thickness(24),
            LastChildFill = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(list);

        _ = RefreshAsync();
        return root;
    }

    private static IEnumerable<object> BuildGroupedEntityRows(IReadOnlyList<EntitySummaryDto> items)
    {
        var order = new (string Category, string Header)[]
        {
            ("Unit", "Units"),
            ("MythUnit", "Myth units"),
            ("Hero", "Heroes"),
            ("Worker", "Workers"),
            ("Building", "Buildings"),
            ("ResourceNode", "Resource nodes"),
            ("GodPower", "God powers")
        };

        foreach (var (category, header) in order)
        {
            var group = items
                .Where(i => i.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i.InternalKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (group.Count == 0) continue;
            yield return new EntityGroupHeader(header);
            foreach (var item in group)
                yield return new EntityRow(item);
        }
    }

    private Control BuildAdaptiveEditor(bool forceGodPower = false)
    {
        var title = forceGodPower ? "God Power Editor" : "Unit Editor";
        if (string.IsNullOrWhiteSpace(_selectedEntity))
            return Screen(title, "Open an entity from the Entity Browser.");

        var thumb = EntityThumb(_selectedCategory, schemaIcon: null);
        var tabs = new TabControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        var log = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#e4e1e5"),
            Margin = new Thickness(0, 8, 0, 0)
        };
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                thumb,
                new TextBlock
                {
                    Text = _selectedEntity,
                    FontSize = 18,
                    Foreground = Brush("#e4e1e5"),
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 24,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("#e4e1e5")
        };
        var subtitle = new TextBlock
        {
            Text = _selectedEntity,
            Foreground = Brush("#A8A59C"),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var root = new DockPanel
        {
            Margin = new Thickness(24),
            LastChildFill = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        var top = new StackPanel { Spacing = 4, Children = { titleBlock, subtitle, header } };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(log, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(log);
        root.Children.Add(tabs);

        _ = LoadSchemaAsync();
        return root;

        async Task LoadSchemaAsync()
        {
            try
            {
                var selectedIndex = tabs.SelectedIndex;
                var schema = await RequireClient().GetEditorSchemaAsync(_selectedEntity);
                var entity = await RequireClient().GetEntityAsync(_selectedEntity);
                header.Children[0] = EntityThumb(_selectedCategory, schema?.IconPath ?? entity?.Summary.IconPath);
                tabs.Items.Clear();
                if (schema is null) return;

                foreach (var tab in schema.Tabs)
                {
                    Control content;
                    if (tab.Id == "rawXml")
                    {
                        content = new TextBox
                        {
                            Text = entity?.RawXml ?? "",
                            AcceptsReturn = true,
                            TextWrapping = TextWrapping.Wrap,
                            FontFamily = new FontFamily("Consolas"),
                            IsReadOnly = true,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Stretch
                        };
                    }
                    else if (string.Equals(tab.Id, "attack", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(tab.Id, "work", StringComparison.OrdinalIgnoreCase))
                    {
                        var stack = new StackPanel { Spacing = 14, Margin = new Thickness(4, 8, 12, 8) };
                        foreach (var card in BuildActionCards(tab.Fields, log, LoadSchemaAsync, tab.Id))
                            stack.Children.Add(card);
                        content = WrapEditorScroll(stack);
                    }
                    else
                    {
                        content = WrapEditorScroll(BuildTwoColumnFields(tab.Fields, log, LoadSchemaAsync));
                    }

                    tabs.Items.Add(new TabItem { Header = tab.Title, Content = content });
                }

                if (selectedIndex >= 0 && selectedIndex < tabs.Items.Count)
                    tabs.SelectedIndex = selectedIndex;
            }
            catch (Exception ex)
            {
                log.Text = ex.Message;
            }
        }
    }

    private static ScrollViewer WrapEditorScroll(Control content)
        => new()
        {
            Content = content,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

    private Control BuildTwoColumnFields(
        IReadOnlyList<EditorFieldDto> fields,
        TextBlock log,
        Func<Task>? reloadSchema = null,
        Thickness? margin = null)
    {
        var left = new StackPanel { Spacing = 16 };
        var right = new StackPanel { Spacing = 16 };
        for (var i = 0; i < fields.Count; i++)
        {
            var editor = BuildFieldEditor(fields[i], log, reloadSchema);
            if (i % 2 == 0)
                left.Children.Add(editor);
            else
                right.Children.Add(editor);
        }

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,16,*"),
            Margin = margin ?? new Thickness(4, 8, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    private IEnumerable<Control> BuildActionCards(
        IEnumerable<EditorFieldDto> fields,
        TextBlock log,
        Func<Task>? reloadSchema,
        string tabId)
    {
        var groups = fields.GroupBy(f => f.GroupKey ?? "action").ToList();
        if (string.Equals(tabId, "work", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var card in BuildKindCards(groups, log, reloadSchema, compact: false))
                yield return card;
            yield break;
        }

        var combat = groups.Where(g => IsKind(g, "combat")).ToList();
        var special = groups.Where(g => IsKind(g, "special") || (!IsKind(g, "combat") && !IsKind(g, "utility"))).ToList();

        if (combat.Count > 0)
        {
            yield return SectionLabel("Combat attacks");
            foreach (var card in BuildKindCards(combat, log, reloadSchema, compact: false))
                yield return card;
        }

        if (special.Count > 0)
        {
            yield return SectionLabel("Special abilities (read-only summary)");
            foreach (var card in BuildKindCards(special, log, reloadSchema, compact: true))
                yield return card;
        }
    }

    private static bool IsKind(IGrouping<string, EditorFieldDto> group, string kind)
        => group.Any(f => string.Equals(f.GroupKind, kind, StringComparison.OrdinalIgnoreCase));

    private static Control SectionLabel(string text)
        => new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("#A8A59C"),
            Margin = new Thickness(0, 8, 0, 6)
        };

    private IEnumerable<Control> BuildKindCards(
        IEnumerable<IGrouping<string, EditorFieldDto>> groups,
        TextBlock log,
        Func<Task>? reloadSchema,
        bool compact)
    {
        foreach (var group in groups)
        {
            if (compact)
            {
                var summary = group.FirstOrDefault(f =>
                    string.Equals(f.ValueKind, "summary", StringComparison.OrdinalIgnoreCase));
                var kind = group.Select(f => f.GroupKind).FirstOrDefault(k => !string.IsNullOrWhiteSpace(k)) ?? "utility";
                var badge = kind.Equals("special", StringComparison.OrdinalIgnoreCase) ? "Special" : "Work";
                yield return new Border
                {
                    Background = Brush("#1f1f22"),
                    BorderBrush = Brush("#3A3A3A"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 8),
                    Margin = new Thickness(0, 0, 0, 8),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Child = new StackPanel
                    {
                        Spacing = 2,
                        Children =
                        {
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 8,
                                Children =
                                {
                                    new TextBlock
                                    {
                                        Text = group.Key,
                                        FontFamily = new FontFamily("Consolas"),
                                        FontSize = 13,
                                        Foreground = Brush("#e4e1e5"),
                                        VerticalAlignment = VerticalAlignment.Center
                                    },
                                    new Border
                                    {
                                        Background = Brush("#2a2a2d"),
                                        CornerRadius = new CornerRadius(2),
                                        Padding = new Thickness(6, 2),
                                        Child = new TextBlock
                                        {
                                            Text = badge,
                                            FontSize = 11,
                                            Foreground = Brush("#ffb779")
                                        }
                                    }
                                }
                            },
                            new TextBlock
                            {
                                Text = summary?.CurrentValue ?? "Present — not a damage attack (editing blocked).",
                                Foreground = Brush("#A8A59C"),
                                FontSize = 12,
                                TextWrapping = TextWrapping.Wrap
                            }
                        }
                    }
                };
                continue;
            }

            var card = new Border
            {
                Background = Brush("#2a2a2d"),
                BorderBrush = Brush("#3A3A3A"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 14),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var stack = new StackPanel { Spacing = 12 };
            stack.Children.Add(new TextBlock
            {
                Text = group.Key,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                Foreground = Brush("#ffb779"),
                Margin = new Thickness(0, 0, 0, 4)
            });

            var editable = new List<EditorFieldDto>();
            EditorFieldDto? addBonus = null;
            foreach (var field in group)
            {
                if (field.Path.EndsWith("/name", StringComparison.OrdinalIgnoreCase)
                    && field.ElementName == "name")
                    continue;
                if (field.Path.EndsWith("/damagebonus[+]", StringComparison.OrdinalIgnoreCase))
                    addBonus = field;
                else
                    editable.Add(field);
            }

            stack.Children.Add(BuildTwoColumnFields(editable, log, reloadSchema, new Thickness(0)));
            if (addBonus is not null)
                stack.Children.Add(BuildAddBonusEditor(addBonus, log, reloadSchema));

            card.Child = stack;
            yield return card;
        }
    }

    private Control BuildAddBonusEditor(
        EditorFieldDto field,
        TextBlock log,
        Func<Task>? reloadSchema = null)
    {
        var label = new TextBlock { Text = "Add bonus…", Foreground = Brush("#d8c2b2") };
        var typeBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 180,
            ItemsSource = field.Options ?? Array.Empty<string>(),
            PlaceholderText = "Bonus type"
        };
        AutomationProperties.SetName(typeBox, "Add damage bonus type");
        AutomationProperties.SetHelpText(typeBox, "not set");
        var valueBox = new TextBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 100,
            Watermark = "Multiplier"
        };
        AutomationProperties.SetName(valueBox, "Add damage bonus multiplier");
        AutomationProperties.SetHelpText(valueBox, "not set");
        var apply = new Button
        {
            Content = "Apply",
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 72,
            VerticalAlignment = VerticalAlignment.Center
        };
        apply.Click += async (_, _) =>
        {
            try
            {
                var type = typeBox.SelectedItem as string;
                if (string.IsNullOrWhiteSpace(type))
                {
                    log.Text = "Select a damage bonus type.";
                    return;
                }

                var parts = field.Path.Split('/');
                var actionName = parts.Length > 1 ? parts[1] : "";
                await RequireClient().ApplyChangeAsync(new ApplyChangeRequest(
                    _selectedEntity,
                    "replaceactionvalue",
                    null,
                    actionName,
                    "damagebonus",
                    valueBox.Text ?? "1.000000",
                    null,
                    new Dictionary<string, string> { ["type"] = type }));
                log.Text = $"Added damage bonus · {type}.";
                if (reloadSchema is not null)
                    await reloadSchema();
            }
            catch (Exception ex) { log.Text = ex.Message; }
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*,Auto") };
        Grid.SetColumn(typeBox, 0);
        Grid.SetColumn(valueBox, 1);
        Grid.SetColumn(apply, 2);
        valueBox.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(typeBox);
        row.Children.Add(valueBox);
        row.Children.Add(apply);

        return new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children = { label, row }
        };
    }

    private Control BuildFieldEditor(
        EditorFieldDto field,
        TextBlock log,
        Func<Task>? reloadSchema = null)
    {
        var isPercent = string.Equals(field.DisplayFormat, "percent", StringComparison.OrdinalIgnoreCase);
        var isAddable = string.Equals(field.Presence, "addable", StringComparison.OrdinalIgnoreCase);
        var labelText = isPercent ? $"{field.Label} percent" : field.Label;
        var label = new TextBlock { Text = labelText, Foreground = Brush("#d8c2b2") };
        if (field.ValueKind == "chips" || field.ValueKind == "summary"
            || field.Path is "name" or "displaynameid" or "unittypes" or "cooldownTechKey"
            || (field.ElementName == "name" && field.GroupKey is not null))
        {
            return new StackPanel
            {
                Spacing = 6,
                Margin = new Thickness(0, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Children =
                {
                    label,
                    new TextBlock { Text = field.CurrentValue ?? "—", Foreground = Brush("#e4e1e5"), TextWrapping = TextWrapping.Wrap }
                }
            };
        }

        Control input;
        Control? namedInput;
        Func<string?> readValue;
        var warnHost = new ContentControl();
        if (field.ValueKind == "enum" && field.Options is { Count: > 0 })
        {
            var combo = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 240,
                ItemsSource = field.Options,
                SelectedItem = field.CurrentValue,
                PlaceholderText = isAddable ? "Not set — Apply to add" : null
            };
            namedInput = combo;
            input = combo;
            readValue = () => combo.SelectedItem as string;
        }
        else
        {
            var display = isPercent
                ? ArmorPercentDisplay(field.CurrentValue)
                : field.CurrentValue ?? "";
            var box = new TextBox
            {
                Text = display,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 240,
                Watermark = isAddable
                    ? (isPercent ? "Add %" : "Not set — Apply to add")
                    : null
            };
            if (isAddable)
                box.BorderBrush = Brush("#6a675f");
            namedInput = box;
            if (isPercent)
            {
                void RefreshWarn()
                {
                    if (decimal.TryParse(box.Text, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var pct) && pct > 100m)
                        warnHost.Content = WarningChip(">100% — atypical; Apply still allowed");
                    else
                        warnHost.Content = null;
                }

                box.TextChanged += (_, _) => RefreshWarn();
                RefreshWarn();
                var pctRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                var pctLabel = new TextBlock
                {
                    Text = "%",
                    Foreground = Brush("#A8A59C"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                Grid.SetColumn(box, 0);
                Grid.SetColumn(pctLabel, 1);
                pctRow.Children.Add(box);
                pctRow.Children.Add(pctLabel);
                input = pctRow;
            }
            else
            {
                input = box;
            }

            readValue = () => box.Text;
        }

        AutomationProperties.SetName(namedInput, field.Label);
        if (isAddable)
            AutomationProperties.SetHelpText(namedInput, "not set");

        var provenanceHost = new ContentControl
        {
            Content = ProvenanceStrip(null, isPercent),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var apply = new Button
        {
            Content = "Apply",
            Margin = new Thickness(10, 0, 0, 0),
            MinWidth = 72,
            VerticalAlignment = VerticalAlignment.Center
        };
        apply.Click += async (_, _) =>
        {
            try
            {
                var raw = readValue() ?? "";
                if (isPercent)
                {
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        log.Text = "Enter a percent value.";
                        return;
                    }

                    if (!decimal.TryParse(raw, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var pct))
                    {
                        log.Text = $"Invalid percent for {field.Label}.";
                        return;
                    }

                    if (pct > 100m)
                        warnHost.Content = WarningChip(">100% — atypical; Apply still allowed");

                    raw = ToArmorFraction(raw);
                }

                var hint = field.PersistHint?.ToLowerInvariant();
                if (hint == "setattribute"
                    || (!string.IsNullOrWhiteSpace(field.AttributeName)
                        && string.Equals(field.ElementName, "armor", StringComparison.OrdinalIgnoreCase)))
                {
                    await RequireClient().ApplyChangeAsync(new ApplyChangeRequest(
                        _selectedEntity,
                        "setattribute",
                        field.ElementName ?? field.Path,
                        null,
                        null,
                        raw,
                        field.AttributeName ?? "value",
                        field.MatchAttributes is null ? null : new Dictionary<string, string>(field.MatchAttributes)));
                }
                else if (hint == "replaceactionvalue"
                         || field.Path.StartsWith("protoaction/", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = field.Path.Split('/');
                    var actionName = parts.Length > 1 ? parts[1] : "";
                    await RequireClient().ApplyChangeAsync(new ApplyChangeRequest(
                        _selectedEntity,
                        "replaceactionvalue",
                        null,
                        actionName,
                        field.ElementName,
                        raw,
                        null,
                        field.MatchAttributes is null ? null : new Dictionary<string, string>(field.MatchAttributes)));
                }
                else
                {
                    await RequireClient().ApplyChangeAsync(new ApplyChangeRequest(
                        _selectedEntity,
                        "setscalar",
                        field.ElementName ?? field.Path,
                        null,
                        null,
                        raw,
                        null,
                        field.MatchAttributes is null ? null : new Dictionary<string, string>(field.MatchAttributes)));
                }

                var refreshed = await RequireClient().GetEntityAsync(_selectedEntity);
                if (refreshed?.Provenance.TryGetValue(field.ElementName ?? field.Path, out var p) == true)
                    provenanceHost.Content = ProvenanceStrip(p, isPercent);
                log.Text = isAddable ? $"Added {field.Label}." : $"Updated {field.Label}.";
                if (isAddable && reloadSchema is not null)
                    await reloadSchema();
            }
            catch (Exception ex) { log.Text = ex.Message; }
        };

        var inputRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(input, 0);
        Grid.SetColumn(apply, 1);
        inputRow.Children.Add(input);
        inputRow.Children.Add(apply);

        return new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                label,
                provenanceHost,
                warnHost,
                inputRow
            }
        };
    }

    private static string ArmorPercentDisplay(string? fraction)
    {
        if (string.IsNullOrWhiteSpace(fraction)) return "";
        if (!decimal.TryParse(fraction, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            return fraction;
        var percent = value * 100m;
        return percent.ToString("0.##############", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ToArmorFraction(string percent)
    {
        var value = decimal.Parse(percent, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture);
        return (value / 100m).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Designer ProvenanceStrip — Base / Override / Effective / Source always shown for editable fields (AC-09).</summary>
    private static Control ProvenanceStrip(FieldProvenanceDto? p, bool asPercent = false)
    {
        string Conv(string? v)
        {
            if (!asPercent || string.IsNullOrWhiteSpace(v) || v == "—") return v ?? "—";
            var d = ArmorPercentDisplay(v);
            return string.IsNullOrEmpty(d) ? "—" : $"{d}%";
        }

        var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
        row.Children.Add(Chip("Base", Conv(p?.BaseValue)));
        row.Children.Add(Chip("Override", Conv(p?.OverrideValue)));
        row.Children.Add(Chip("Effective", Conv(p?.EffectiveValue)));
        row.Children.Add(Chip("Source", string.IsNullOrWhiteSpace(p?.SourceFile) ? "—" : Path.GetFileName(p.SourceFile)));
        return row;
    }

    private static Border WarningChip(string text)
    {
        return new Border
        {
            Background = Brush("#3d2e1a"),
            BorderBrush = Brush("#c9a227"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(8, 4),
            Margin = new Thickness(0, 0, 0, 4),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
                Foreground = Brush("#ffb779")
            }
        };
    }

    private static Border Chip(string label, string value)
    {
        return new Border
        {
            Background = Brush("#2A2A2A"),
            BorderBrush = Brush("#3A3A3A"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(8, 4),
            Margin = new Thickness(0, 0, 6, 4),
            Child = new TextBlock
            {
                Text = $"{label}: {value}",
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
                Foreground = Brush("#C4A574")
            }
        };
    }

    private Control EntityThumb(string category, string? schemaIcon)
    {
        var resolved = TryResolveIconFile(schemaIcon);
        if (resolved is not null)
        {
            try
            {
                return new Border
                {
                    Width = 40,
                    Height = 40,
                    Background = Brush("#2a2a2d"),
                    CornerRadius = new CornerRadius(4),
                    ClipToBounds = true,
                    Child = new Image
                    {
                        Source = new Avalonia.Media.Imaging.Bitmap(resolved),
                        Stretch = Stretch.Uniform
                    }
                };
            }
            catch
            {
                // fall through to placeholder
            }
        }

        var glyph = category.ToLowerInvariant() switch
        {
            "godpower" => "⚡",
            "resourcenode" => "🌿",
            "technology" => "📜",
            _ => "⛨"
        };
        var placeholder = new TextBlock
        {
            Text = glyph,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 18
        };
        ToolTip.SetTip(placeholder, string.IsNullOrWhiteSpace(schemaIcon) ? "Placeholder icon" : $"Icon not found: {schemaIcon}");
        return new Border
        {
            Width = 40,
            Height = 40,
            Background = Brush("#2a2a2d"),
            CornerRadius = new CornerRadius(4),
            Child = placeholder
        };
    }

    private string? TryResolveIconFile(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath)) return null;
        if (Path.IsPathRooted(iconPath) && File.Exists(iconPath))
            return iconPath;

        var candidates = new List<string>();
        foreach (var root in new[] { _iconSearchRoot, _importPath })
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            candidates.Add(Path.Combine(root, iconPath));
            candidates.Add(Path.Combine(root, "textures", iconPath));
            candidates.Add(Path.Combine(root, "game", "textures", iconPath));
            candidates.Add(Path.Combine(root, "Art", iconPath));
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private Control BuildDiff()
    {
        var panel = Screen("Diff Workspace", "Compare semantic and raw changes for the selected entity.");
        var log = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush("#e4e1e5") };
        var btn = new Button { Content = "Diff selected entity", Margin = new Thickness(0, 8, 0, 0) };
        btn.Click += async (_, _) =>
        {
            try
            {
                var diff = await RequireClient().DiffAsync(_selectedEntity);
                log.Text = string.Join("\n", diff?.Semantic.Select(s => $"{s.Path}: {s.OldValue} → {s.NewValue}") ?? []);
            }
            catch (Exception ex) { log.Text = ex.Message; }
        };
        panel.Children.Add(btn);
        panel.Children.Add(log);
        return panel;
    }

    private Control BuildGenerate()
    {
        var panel = Screen("Generation", "Validate, generate additive mod files, and install into local mods.");
        var nameLabel = new TextBlock
        {
            Text = "Mod name",
            Foreground = Brush("#d8c2b2"),
            Margin = new Thickness(0, 12, 0, 4)
        };
        var nameHint = new TextBlock
        {
            Text = "This becomes the folder name under mods\\local (example: MoreHouses). Enter the name of your mod here.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#A8A59C"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        var nameBox = new TextBox
        {
            Watermark = "Name of your mod (folder under mods\\local)",
            Text = "",
            Margin = new Thickness(0, 0, 0, 0),
            MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(nameBox, "Mod name");
        AutomationProperties.SetHelpText(nameBox, "Folder name created under the local mods directory");
        var modsPath = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(_localModsPath)
                ? "Local mods path: (not discovered — set in Setup)"
                : $"Local mods path: {_localModsPath}",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#A8A59C"),
            Margin = new Thickness(0, 4, 0, 0)
        };
        var log = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush("#e4e1e5") };
        var generate = new Button { Content = "Generate", Margin = new Thickness(0, 8, 8, 0) };
        var install = new Button { Content = "Generate + Install to local mods", Margin = new Thickness(0, 8, 0, 0) };

        generate.Click += async (_, _) =>
        {
            try
            {
                var gen = await RequireClient().GenerateAsync();
                log.Text = $"Staging: {gen?.StagingDirectory}\n{gen?.ProtoModsXml}\nStrategies: "
                           + string.Join(", ", gen?.GodPowerStrategies.Select(g => $"{g.EntityKey}:{g.Strategy}") ?? []);
            }
            catch (Exception ex) { log.Text = ex.Message; }
        };
        install.Click += async (_, _) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_localModsPath))
                {
                    log.Text = "No local mods path. Run Setup discovery or browse to mods\\local first.";
                    return;
                }

                var folder = (nameBox.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(folder))
                {
                    log.Text = "Enter the name of your mod (this is the folder created under mods\\local).";
                    return;
                }

                SetBlocking(true, "Generating and installing mod…");
                await Task.Yield();
                var gen = await RequireClient().GenerateAsync();
                var result = await RequireClient().InstallAsync(_localModsPath, folder);
                log.Text =
                    $"Installed to:\n{result?.TargetDirectory}\n\nCopied:\n"
                    + string.Join("\n", result?.CopiedFiles ?? Array.Empty<string>())
                    + $"\n\nStaging was: {gen?.StagingDirectory}";
                SetStatus($"Installed mod '{folder}' → {result?.TargetDirectory}");
            }
            catch (Exception ex) { log.Text = ex.Message; }
            finally { SetBlocking(false); }
        };

        panel.Children.Add(modsPath);
        panel.Children.Add(nameLabel);
        panel.Children.Add(nameHint);
        panel.Children.Add(nameBox);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { generate, install } });
        panel.Children.Add(log);
        return panel;
    }

    private Control BuildMigration()
    {
        var panel = Screen("Migration", "Inspect fingerprint changes and rebase without silent fuzzy binds.");
        var log = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush("#e4e1e5") };
        var inspect = new Button { Content = "Inspect migration", Margin = new Thickness(0, 8, 8, 0) };
        var rebase = new Button { Content = "Rebase (ID match only)", Margin = new Thickness(0, 8, 0, 0) };
        inspect.Click += async (_, _) =>
        {
            try
            {
                var r = await RequireClient().InspectMigrationAsync();
                log.Text = $"RequiresMigration={r?.RequiresMigration} expected={r?.ExpectedFingerprint} actual={r?.ActualFingerprint}\n"
                           + $"Fuzzy suggestions (not auto-bound): {r?.FuzzySuggestions.Count}";
            }
            catch (Exception ex) { log.Text = ex.Message; }
        };
        rebase.Click += async (_, _) =>
        {
            try
            {
                var r = await RequireClient().RebaseMigrationAsync(false);
                log.Text = r?.ReportMarkdown ?? "rebase done";
            }
            catch (Exception ex) { log.Text = ex.Message; }
        };
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { inspect, rebase } });
        panel.Children.Add(log);
        return panel;
    }

    private static StackPanel Screen(string title, string subtitle)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeight.SemiBold, Foreground = Brush("#e4e1e5") });
        panel.Children.Add(new TextBlock { Text = subtitle, Foreground = Brush("#A8A59C") });
        return panel;
    }

    private static TextBlock Info(string text)
        => new() { Text = text, Foreground = Brush("#e4e1e5"), Margin = new Thickness(0, 4, 0, 0) };

    private sealed record NavItem(string Title, bool Enabled)
    {
        public override string ToString() => Enabled ? Title : $"{Title} (locked)";
    }

    private sealed record EntityGroupHeader(string Title)
    {
        public override string ToString() => Title.ToUpperInvariant();
    }

    private sealed record EntityRow(EntitySummaryDto Dto)
    {
        public string Key => Dto.InternalKey;
        public string Category => Dto.Category;
        public override string ToString()
        {
            var pantheon = Dto.ExclusivePantheon ?? Dto.Pantheon;
            var tag = string.IsNullOrWhiteSpace(pantheon) ? "" : $"  [{pantheon}]";
            var types = string.Join(", ", Dto.UnitTypes ?? Array.Empty<string>());
            return $"{Dto.InternalKey}{tag}  {types}";
        }
    }
}
