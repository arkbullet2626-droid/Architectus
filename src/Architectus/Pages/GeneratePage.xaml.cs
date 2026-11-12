using System.Diagnostics;
using System.Text;
using Architectus.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

namespace Architectus.Pages;

public sealed partial class GeneratePage : Page
{
    private CancellationTokenSource? _cts;
    private bool _streaming;
    private string _lastRough = "";
    private string _lastProvider = "";
    private string _lastModel = "";

    public GeneratePage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        RefreshProviderState();
    }

    private void RefreshProviderState()
    {
        var s = SettingsStore.Current;
        var info = ProviderCatalog.Get(s.Provider);
        bool hasKey = CredentialStore.Exists(info.Id);
        NoKeyBar.IsOpen = !hasKey;
        ModelLabel.Text = $"{info.DisplayName} · {s.ModelFor(info.Id)}";
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool hasInput = !string.IsNullOrWhiteSpace(InputBox.Text);
        bool hasOutput = !string.IsNullOrWhiteSpace(OutputBox.Text);
        GenerateButton.IsEnabled = hasInput && !_streaming && !NoKeyBar.IsOpen;
        StopButton.Visibility = _streaming ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.IsEnabled = hasOutput && !_streaming;
        SaveButton.IsEnabled = hasOutput && !_streaming;
        ClearButton.IsEnabled = (hasOutput || hasInput) && !_streaming;
    }

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateButtons();
    private void OutputBox_TextChanged(object sender, TextChangedEventArgs e) { if (!_streaming) UpdateButtons(); }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => App.Window?.NavigateTo("settings");

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (_streaming || string.IsNullOrWhiteSpace(InputBox.Text)) return;

        var s = SettingsStore.Current;
        var info = ProviderCatalog.Get(s.Provider);
        string? key = CredentialStore.Load(info.Id);
        if (key is null) { RefreshProviderState(); return; }

        string model = s.ModelFor(info.Id);
        string rough = InputBox.Text;
        var client = ProviderCatalog.CreateClient(info.Id);

        _cts = new CancellationTokenSource();
        _streaming = true;
        OutputBox.Text = "";
        OutputBox.IsReadOnly = true;
        Busy.IsActive = true;
        StatusText.Text = $"Connecting to {info.DisplayName}…";
        UpdateButtons();

        var sw = Stopwatch.StartNew();
        var buffer = new StringBuilder();
        bool gotAny = false;
        try
        {
            await foreach (var chunk in client.StreamAsync(key, model, MetaPrompt.System, MetaPrompt.WrapUserInput(rough), _cts.Token))
            {
                if (!gotAny) { gotAny = true; StatusText.Text = "Writing…"; }
                buffer.Append(chunk);
                // Only auto-scroll if the user hasn't scrolled up to read.
                bool atBottom = OutputScroll.ScrollableHeight - OutputScroll.VerticalOffset < 80;
                OutputBox.Text = buffer.ToString();
                if (atBottom) OutputScroll.ChangeView(null, double.MaxValue, null, true);
            }

            sw.Stop();
            if (!gotAny)
                StatusText.Text = "The model returned an empty response. Try again or pick another model.";
            else
                StatusText.Text = $"Done in {sw.Elapsed.TotalSeconds:0.0}s · {CountWords(buffer)} words";

            _lastRough = rough;
            _lastProvider = info.Id;
            _lastModel = model;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Stopped.";
            _lastRough = rough; _lastProvider = info.Id; _lastModel = model;
        }
        catch (ProviderException ex)
        {
            StatusText.Text = "";
            App.Window?.ShowError(ex.Message);
        }
        catch (HttpRequestException)
        {
            StatusText.Text = "";
            App.Window?.ShowError($"Couldn't reach {info.DisplayName}. Check your internet connection.");
        }
        catch (Exception ex)
        {
            StatusText.Text = "";
            App.Window?.ShowError(ex.Message);
        }
        finally
        {
            key = null;
            _streaming = false;
            OutputBox.IsReadOnly = false;
            Busy.IsActive = false;
            _cts?.Dispose();
            _cts = null;
            UpdateButtons();
        }
    }

    private static int CountWords(StringBuilder sb) =>
        sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private void Stop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        var data = new DataPackage();
        data.SetText(TextUtil.Newlines(OutputBox.Text));
        Clipboard.SetContent(data);
        Clipboard.Flush();

        CopyIcon.Glyph = "";
        CopyText.Text = "Copied";
        await Task.Delay(1500);
        CopyIcon.Glyph = "";
        CopyText.Text = "Copy";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        string rough = string.IsNullOrWhiteSpace(_lastRough) ? InputBox.Text : _lastRough;
        var titleBox = new TextBox
        {
            Header = "Title",
            Text = SuggestTitle(rough),
            SelectionStart = 0,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Save to library",
            Content = titleBox,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        titleBox.SelectAll();
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var s = SettingsStore.Current;
        LibraryStore.Add(new LibraryItem
        {
            Title = string.IsNullOrWhiteSpace(titleBox.Text) ? SuggestTitle(rough) : titleBox.Text.Trim(),
            RoughInput = TextUtil.Newlines(rough),
            Prompt = TextUtil.Newlines(OutputBox.Text),
            Provider = string.IsNullOrEmpty(_lastProvider) ? s.Provider : _lastProvider,
            Model = string.IsNullOrEmpty(_lastModel) ? s.ModelFor(s.Provider) : _lastModel,
        });
        StatusText.Text = "Saved to library.";
    }

    private static string SuggestTitle(string rough)
    {
        string t = rough.Trim().Replace('\n', ' ').Replace('\r', ' ');
        if (t.Length == 0) return "Untitled prompt";
        t = char.ToUpper(t[0]) + t[1..];
        return t.Length > 60 ? t[..60].TrimEnd() + "…" : t;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        InputBox.Text = "";
        OutputBox.Text = "";
        StatusText.Text = "";
        _lastRough = "";
        UpdateButtons();
        InputBox.Focus(FocusState.Programmatic);
    }
}
