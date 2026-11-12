using Architectus.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

namespace Architectus.Pages;

public sealed partial class LibraryPage : Page
{
    private LibraryItem? _selected;
    private bool _loading;

    public LibraryPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Reload();
    }

    private void Reload(string? selectId = null)
    {
        string q = SearchBox.Text?.Trim() ?? "";
        var items = LibraryStore.Items
            .Where(i => q.Length == 0
                || i.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || i.Prompt.Contains(q, StringComparison.OrdinalIgnoreCase)
                || i.RoughInput.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();

        ItemsList.ItemsSource = items;
        bool empty = items.Count == 0;
        EmptyList.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        EmptyListText.Text = LibraryStore.Items.Count == 0
            ? "Nothing saved yet.\nGenerate a prompt and hit Save to library."
            : "No prompts match your search.";

        var toSelect = items.FirstOrDefault(i => i.Id == selectId);
        ItemsList.SelectedItem = toSelect;
        if (toSelect is null) ShowItem(null);
    }

    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) =>
        Reload(_selected?.Id);

    private void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ShowItem(ItemsList.SelectedItem as LibraryItem);

    private void ShowItem(LibraryItem? item)
    {
        _selected = item;
        _loading = true;
        Detail.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        NoSelection.Visibility = item is null ? Visibility.Visible : Visibility.Collapsed;
        if (item is not null)
        {
            TitleBox.Text = item.Title;
            PromptBox.Text = item.Prompt;
            RoughText.Text = string.IsNullOrWhiteSpace(item.RoughInput) ? "(none)" : item.RoughInput;
            MetaText.Text = item.Subtitle;
        }
        SaveChangesButton.IsEnabled = false;
        _loading = false;
    }

    private void Detail_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || _selected is null) return;
        SaveChangesButton.IsEnabled = TitleBox.Text != _selected.Title || PromptBox.Text != _selected.Prompt;
    }

    private void SaveChanges_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        _selected.Title = string.IsNullOrWhiteSpace(TitleBox.Text) ? "Untitled prompt" : TitleBox.Text.Trim();
        _selected.Prompt = PromptBox.Text;
        _selected.UpdatedUtc = DateTime.UtcNow;
        LibraryStore.Save();
        Reload(_selected.Id);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete this prompt?",
            Content = $"“{_selected.Title}” will be removed from your library. This can't be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        LibraryStore.Remove(_selected.Id);
        Reload();
    }

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        var data = new DataPackage();
        data.SetText(PromptBox.Text);
        Clipboard.SetContent(data);
        Clipboard.Flush();
        CopyIcon.Glyph = "";
        CopyText.Text = "Copied";
        await Task.Delay(1500);
        CopyIcon.Glyph = "";
        CopyText.Text = "Copy";
    }
}
