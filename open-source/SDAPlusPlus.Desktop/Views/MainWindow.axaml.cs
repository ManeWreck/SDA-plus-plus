using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using System.ComponentModel;
using SDAPlusPlus.Desktop.ViewModels;

namespace SDAPlusPlus.Desktop.Views;

public partial class MainWindow : Window
{
    private async void ConfirmCredentialsUpgrade(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel model) return;
        bool ru = model.IsRussian;
        var confirm = new Button { Content = ru ? "Создать копию и перейти на v3" : "Back up and upgrade to v3", IsEnabled = false };
        var acknowledge = new CheckBox { Content = ru ? "На всех моих устройствах есть поддержка v3" : "All my devices support v3" };
        acknowledge.IsCheckedChanged += (_, _) => confirm.IsEnabled = acknowledge.IsChecked == true;
        var cancel = new Button { Content = ru ? "Отмена" : "Cancel" };
        var dialog = new Window
        {
            Title = model.UpgradeCredentialsLabel, Width = 560, Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Margin = new Thickness(24), Spacing = 16,
                    Children =
                    {
                        new TextBlock
                        {
                            TextWrapping = TextWrapping.Wrap,
                            Text = ru
                                ? "Старые выпущенные версии Desktop и Mobile не открывают v3. Сначала установите версии с поддержкой AES-GCM на всех устройствах. Будет сохранена зашифрованная резервная копия рядом с исходным файлом; исходный ключ остаётся прежним. Отправка в облако выполняется отдельно. Для отката потребуется эта копия и исходный ключ."
                                : "Older released Desktop and Mobile versions cannot open v3. Install AES-GCM-capable builds on every device first. An encrypted backup will be kept beside the original; the key stays unchanged. Cloud upload is separate. Rollback requires the backup and original key."
                        }, acknowledge, confirm, cancel
                    }
                }
            }
        };
        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);
        if (await dialog.ShowDialog<bool>(this)) model.UpgradeCredentialsEncryption();
    }

    private async void OpenEnrollment(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel model) return;
        await new EnrollmentWindow(model.EnrollmentDirectory, model.IsRussian).ShowDialog(this);
        model.ReloadAccountsCommand.Execute(null);
    }

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (DataContext is not MainViewModel model || OwnedWindows.Count > 0) return;
            var kind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
            if (kind == PointerUpdateKind.XButton1Pressed) { model.NavigateBackCommand.Execute(null); e.Handled = true; }
            if (kind == PointerUpdateKind.XButton2Pressed) { model.NavigateForwardCommand.Execute(null); e.Handled = true; }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        KeyDown += (_, e) =>
        {
            if (DataContext is not MainViewModel model || OwnedWindows.Count > 0 || e.KeyModifiers != KeyModifiers.Alt) return;
            if (e.Key == Key.Left) { model.NavigateBackCommand.Execute(null); e.Handled = true; }
            if (e.Key == Key.Right) { model.NavigateForwardCommand.Execute(null); e.Handled = true; }
        };
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, args) => UpdateResponsiveLayout(args.NewSize.Width);
        Opened += (_, _) => UpdateResponsiveLayout(ClientSize.Width);
        Opened += (_, _) => (DataContext as MainViewModel)?.ShowInitialSetupIfNeeded();
        Opened += async (_, _) =>
        {
            if (DataContext is MainViewModel model && model.CheckUpdatesOnStartup)
                await model.CheckForUpdatesCommand.ExecuteAsync(null);
        };
    }

    private void UpdateResponsiveLayout(double width)
    {
        bool compact = width < 1080;
        Classes.Set("compact", compact);

        AppShell.ColumnDefinitions = new ColumnDefinitions(compact ? "230,*" : "260,*");
        Sidebar.Margin = compact ? new Thickness(10) : new Thickness(14);
        Sidebar.Padding = compact ? new Thickness(8) : new Thickness(12);
        PageHost.Margin = compact ? new Thickness(4, 10, 10, 10) : new Thickness(8, 14, 18, 14);

        DashboardHeader.ColumnDefinitions = new ColumnDefinitions("*");
        DashboardHeader.RowDefinitions = new RowDefinitions("Auto,Auto");
        Grid.SetRow(DashboardActions, 1);
        Grid.SetColumn(DashboardActions, 0);
        DashboardActions.Margin = default;
        DashboardBody.ColumnDefinitions = new ColumnDefinitions(compact ? "250,*" : "290,*");

        foreach (var grid in PageHost.GetLogicalDescendants().OfType<Grid>().Where(g => g.Classes.Contains("responsive-columns")))
        {
            bool stacked = width < 1280;
            grid.ColumnDefinitions = new ColumnDefinitions(stacked ? "*" : "1.2*,0.8*");
            grid.RowDefinitions = new RowDefinitions(stacked ? "Auto,Auto" : "Auto");
            for (int i = 0; i < grid.Children.Count; i++)
            {
                Grid.SetRow(grid.Children[i], stacked ? i : 0);
                Grid.SetColumn(grid.Children[i], stacked ? 0 : i);
            }
        }

        SelectedAccountLayout.ColumnDefinitions = new ColumnDefinitions("*");
        SelectedAccountLayout.RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto");
        Grid.SetRow(SelectedAccountActions, 1);
        Grid.SetColumn(SelectedAccountActions, 0);
        SelectedAccountActions.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetRow(SelectedAccountBadges, 2);
        Grid.SetRow(SelectedAccountCommands, 3);

        SelectedAccountCommands.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "*,*,*");
        SelectedAccountCommands.RowDefinitions = new RowDefinitions(compact ? "Auto,Auto,Auto" : "Auto");
        Grid.SetRow(ConfirmationsButton, 0);
        Grid.SetColumn(ConfirmationsButton, 0);
        Grid.SetRow(LoginAgainButton, compact ? 1 : 0);
        Grid.SetColumn(LoginAgainButton, compact ? 0 : 1);
        Grid.SetRow(EndSessionsButton, compact ? 2 : 0);
        Grid.SetColumn(EndSessionsButton, compact ? 0 : 2);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplyTheme(viewModel.SelectedThemeIndex);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedThemeIndex) && sender is MainViewModel viewModel)
            ApplyTheme(viewModel.SelectedThemeIndex);
    }

    private void ApplyTheme(int index)
    {
        ThemePalette palette = index switch
        {
            1 => new("#07090E", "#0D1119", "#131925", "#1B2433", "#222D3E", "#F5F7FB", "#7E899A", "#9B6DFF", "#172034", "#111722", "#0A0E15", "#EE0A0E15", "#111827", "#263C61"),
            2 => new("#0D141A", "#142029", "#1A2934", "#223542", "#304857", "#EDF6F8", "#91A8AF", "#39B9C7", "#1C303A", "#213C47", "#101B22", "#ED101B22", "#172832", "#244C57"),
            _ => new("#090D15", "#111722", "#171E2B", "#1E2838", "#2A3548", "#F2F6FF", "#8996AA", "#5F87FF", "#172031", "#23304A", "#0E141F", "#E60E141F", "#111B2A", "#24395F")
        };

        SetBrush("AppBackgroundBrush", palette.Background);
        SetBrush("SurfaceBrush", palette.Surface);
        SetBrush("SurfaceRaisedBrush", palette.Raised);
        SetBrush("SurfaceHoverBrush", palette.Hover);
        SetBrush("StrokeBrush", palette.Stroke);
        SetBrush("TextPrimaryBrush", palette.Text);
        SetBrush("TextMutedBrush", palette.Muted);
        SetBrush("AccentBrush", palette.Accent);
        SetBrush("ButtonBrush", palette.Button);
        SetBrush("ButtonHoverBrush", palette.ButtonHover);
        SetBrush("InputBrush", palette.Input);
        SetBrush("SidebarBrush", palette.Sidebar);
        SetBrush("SidebarFooterBrush", palette.SidebarFooter);
        SetBrush("SelectionBrush", palette.Selection);
    }

    private void SetBrush(string key, string color)
    {
        if (Resources.TryGetResource(key, ActualThemeVariant, out object? value) && value is SolidColorBrush brush)
            brush.Color = Color.Parse(color);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        (DataContext as IDisposable)?.Dispose();
        base.OnClosed(e);
    }

    private sealed record ThemePalette(
        string Background, string Surface, string Raised, string Hover, string Stroke, string Text, string Muted,
        string Accent, string Button, string ButtonHover, string Input, string Sidebar, string SidebarFooter, string Selection);
}
