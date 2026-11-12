using Architectus.Pages;
using Architectus.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Architectus;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        try
        {
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Architectus.ico"));
            AppWindow.Resize(new SizeInt32(1240, 820));
            if (AppWindow.Presenter is OverlappedPresenter p) { p.IsMinimizable = true; p.IsMaximizable = true; }
            CenterOnScreen();
        }
        catch { /* cosmetic only */ }

        ApplyTheme(SettingsStore.Current.Theme);
        Nav.SelectedItem = GenerateItem;
    }

    private void CenterOnScreen()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var size = AppWindow.Size;
        AppWindow.Move(new PointInt32(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2));
    }

    public void ApplyTheme(string theme)
    {
        RootGrid.RequestedTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        // Keep caption buttons readable on the custom title bar.
        try
        {
            var tb = AppWindow.TitleBar;
            bool dark = RootGrid.ActualTheme == ElementTheme.Dark;
            tb.ButtonForegroundColor = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        }
        catch { }
    }

    public void NavigateTo(string tag)
    {
        if (tag == "settings") { Nav.SelectedItem = Nav.SettingsItem; return; }
        foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
            if ((string)item.Tag == tag) { Nav.SelectedItem = item; return; }
    }

    public void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        ErrorBar.IsOpen = false;
        Type page = args.IsSettingsSelected
            ? typeof(SettingsPage)
            : ((args.SelectedItem as NavigationViewItem)?.Tag as string) switch
            {
                "library" => typeof(LibraryPage),
                _ => typeof(GeneratePage),
            };
        if (ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page);
    }
}
