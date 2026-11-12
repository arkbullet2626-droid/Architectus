using System.Diagnostics;
using System.Reflection;
using Architectus.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Architectus.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _loading = true;
    private readonly Dictionary<string, ProviderCard> _cards = new();

    public SettingsPage()
    {
        InitializeComponent();

        var s = SettingsStore.Current;
        foreach (var p in ProviderCatalog.All)
        {
            ProviderChoice.Items.Add(new RadioButton { Content = p.DisplayName, Tag = p.Id });
            var card = new ProviderCard(p, this);
            _cards[p.Id] = card;
            ProviderCards.Children.Add(card.Root);
        }
        ProviderChoice.SelectedIndex = Math.Max(0, ProviderCatalog.All.ToList().FindIndex(p => p.Id == s.Provider));
        _cards[ProviderCatalog.Get(s.Provider).Id].Root.IsExpanded = true;

        ThemeBox.SelectedItem = s.Theme;

        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Architectus {v?.Major}.{v?.Minor}.{v?.Build}";
        EditionText.Text = MetaPrompt.IsOfficial
            ? "Official build · Architect meta-prompt"
            : "Community build · open baseline meta-prompt";

        _loading = false;
    }

    private void ProviderChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (ProviderChoice.SelectedItem is RadioButton rb && rb.Tag is string id)
        {
            SettingsStore.Current.Provider = id;
            SettingsStore.Save();
            foreach (var (pid, card) in _cards) card.Root.IsExpanded = pid == id;
        }
    }

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedItem is not string theme) return;
        SettingsStore.Current.Theme = theme;
        SettingsStore.Save();
        App.Window?.ApplyTheme(theme);
    }

    private void OpenCredMan_Click(object sender, RoutedEventArgs e) =>
        Start("control.exe", "/name Microsoft.CredentialManager");

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.Ensure();
        Start("explorer.exe", $"\"{AppPaths.DataDir}\"");
    }

    private static void Start(string file, string args)
    {
        try { Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true }); }
        catch (Exception ex) { App.Window?.ShowError(ex.Message); }
    }

    private async void RemoveAll_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Remove all API keys?",
            Content = "Every Architectus key will be deleted from Windows Credential Manager. Your library is not affected.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        CredentialStore.DeleteAll();
        foreach (var c in _cards.Values) c.RefreshKeyState("All keys removed.");
    }

    /// <summary>One expandable card per provider: key entry, test, and model picker.</summary>
    private sealed class ProviderCard
    {
        private readonly ProviderInfo _info;
        private readonly SettingsPage _page;
        public Expander Root { get; }
        private readonly TextBlock _badge = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly PasswordBox _keyBox = new() { Header = "API key", PasswordRevealMode = PasswordRevealMode.Peek, MinWidth = 360 };
        private readonly Button _saveKey = new() { Content = "Save key", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        private readonly Button _test = new() { Content = "Test key" };
        private readonly Button _remove = new() { Content = "Remove key" };
        private readonly ComboBox _model = new() { Header = "Model", IsEditable = true, MinWidth = 360 };
        private readonly Button _loadModels = new() { Content = "Load models from my account" };
        private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
        private readonly ProgressRing _ring = new() { Width = 16, Height = 16, IsActive = false };

        public ProviderCard(ProviderInfo info, SettingsPage page)
        {
            _info = info;
            _page = page;
            _status.Style = (Style)Application.Current.Resources["CaptionStyle"];

            var header = new Grid { Padding = new Thickness(0, 12, 0, 12) };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock { Text = info.DisplayName, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(_badge, 1);
            header.Children.Add(_badge);

            var link = new HyperlinkButton { Content = $"Get a {info.DisplayName} key", NavigateUri = new Uri(info.KeyUrl), Padding = new Thickness(0) };

            var keyButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            keyButtons.Children.Add(_saveKey);
            keyButtons.Children.Add(_test);
            keyButtons.Children.Add(_remove);

            foreach (var m in info.SuggestedModels) _model.Items.Add(m);
            _model.Text = SettingsStore.Current.ModelFor(info.Id);
            var modelHint = new TextBlock
            {
                Style = (Style)Application.Current.Resources["CaptionStyle"],
                Text = $"Default: {info.DefaultModel} (fast and cheap). You can type any model ID your account has access to.",
            };

            var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            statusRow.Children.Add(_ring);
            statusRow.Children.Add(_status);

            var body = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Left };
            body.Children.Add(_keyBox);
            body.Children.Add(keyButtons);
            body.Children.Add(link);
            body.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6), Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"] });
            body.Children.Add(_model);
            body.Children.Add(modelHint);
            body.Children.Add(_loadModels);
            body.Children.Add(statusRow);

            Root = new Expander
            {
                Header = header,
                Content = body,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };

            _keyBox.PasswordChanged += (_, _) => _saveKey.IsEnabled = !string.IsNullOrWhiteSpace(_keyBox.Password);
            _saveKey.Click += (_, _) => SaveKey();
            _remove.Click += (_, _) => RemoveKey();
            _test.Click += async (_, _) => await TestAsync(populate: false);
            _loadModels.Click += async (_, _) => await TestAsync(populate: true);
            _model.SelectionChanged += (_, _) => { if (_model.SelectedItem is string m) SaveModel(m); };
            _model.TextSubmitted += (_, a) => SaveModel(a.Text);
            _model.LostFocus += (_, _) => SaveModel(_model.Text);

            RefreshKeyState(null);
        }

        public void RefreshKeyState(string? message)
        {
            bool has = CredentialStore.Exists(_info.Id);
            _badge.Text = has ? "✓ Key saved" : "No key";
            _badge.Foreground = (Brush)Application.Current.Resources[has ? "SystemFillColorSuccessBrush" : "TextFillColorSecondaryBrush"];
            _keyBox.Password = "";
            _keyBox.PlaceholderText = has ? "Saved securely — paste a new key to replace it" : info_hint();
            _saveKey.IsEnabled = false;
            _test.IsEnabled = has;
            _remove.IsEnabled = has;
            _loadModels.IsEnabled = has;
            if (message is not null) _status.Text = message;

            string info_hint() => $"Paste your key ({_info.KeyHint})";
        }

        private void SaveKey()
        {
            try
            {
                CredentialStore.Save(_info.Id, _keyBox.Password);
                RefreshKeyState("Key saved to Windows Credential Manager.");
                _ = TestAsync(populate: false);
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }

        private void RemoveKey()
        {
            try { CredentialStore.Delete(_info.Id); RefreshKeyState("Key removed."); }
            catch (Exception ex) { _status.Text = ex.Message; }
        }

        private void SaveModel(string? model)
        {
            model = model?.Trim();
            if (string.IsNullOrEmpty(model)) return;
            if (SettingsStore.Current.ModelFor(_info.Id) == model) return;
            SettingsStore.Current.Models[_info.Id] = model;
            SettingsStore.Save();
            _status.Text = $"Model set to {model}.";
        }

        private async Task TestAsync(bool populate)
        {
            string? key = CredentialStore.Load(_info.Id);
            if (key is null) return;
            _ring.IsActive = true;
            _status.Text = "Checking with " + _info.DisplayName + "…";
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var models = await ProviderCatalog.CreateClient(_info.Id).ListModelsAsync(key, cts.Token);
                if (populate && models.Count > 0)
                {
                    string current = _model.Text;
                    _model.Items.Clear();
                    foreach (var m in models) _model.Items.Add(m);
                    _model.Text = current;
                    _status.Text = $"Loaded {models.Count} models from your account.";
                    _model.IsDropDownOpen = true;
                }
                else
                {
                    string current = SettingsStore.Current.ModelFor(_info.Id);
                    bool known = models.Count == 0 || models.Contains(current);
                    _status.Text = known
                        ? "✓ Key works."
                        : $"✓ Key works, but \"{current}\" isn't in your account's model list. Use Load models to pick one.";
                }
            }
            catch (OperationCanceledException) { _status.Text = "Timed out. Check your internet connection."; }
            catch (ProviderException ex) { _status.Text = ex.Message; }
            catch (HttpRequestException) { _status.Text = $"Couldn't reach {_info.DisplayName}. Check your internet connection."; }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { _ring.IsActive = false; }
        }
    }
}
