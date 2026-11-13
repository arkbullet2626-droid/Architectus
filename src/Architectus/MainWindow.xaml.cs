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

        StyleTitleBar();
        Nav.SelectedItem = GenerateItem;
    }

    private void CenterOnScreen()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var size = AppWindow.Size;
        AppWindow.Move(new PointInt32(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2));
    }

    /// <summary>Caption buttons in the Opifex palette (the app is always dark).</summary>
    private void StyleTitleBar()
    {
        try
        {
            var tb = AppWindow.TitleBar;
            tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 0xF2, 0xEB, 0xDD);
            tb.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 0xA3, 0x96, 0x7E);
            tb.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 0x1C, 0x17, 0x0F);
            tb.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 0xF2, 0xB5, 0x44);
            tb.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 0x2E, 0x26, 0x17);
            tb.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(255, 0xF2, 0xEB, 0xDD);
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
