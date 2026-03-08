using WebMusicPlayer.Localization;
using WebMusicPlayer.Models;

namespace WebMusicPlayer.Views;

public partial class SubscriptionEditorPage : ContentPage
{
    private readonly TaskCompletionSource<SubscriptionEditorResult?> _resultSource = new();
    private bool _isClosing;

    public SubscriptionEditorPage(string title, string subtitle, string saveButtonText, SubscriptionEditorResult? initialValue)
    {
        InitializeComponent();
        TitleLabel.Text = title;
        SubtitleLabel.Text = subtitle;
        SaveButton.Text = saveButtonText;

        NameEntry.Text = initialValue?.Name ?? string.Empty;
        UrlEntry.Text = initialValue?.Url ?? string.Empty;
        CountEntry.Text = (initialValue?.MaxStreamCount ?? SubscriptionImportOptions.Default.MaxStreamCount).ToString();
    }

    public static async Task<SubscriptionEditorResult?> ShowAsync(Page page, string title, string subtitle, string saveButtonText, SubscriptionEditorResult? initialValue = null)
    {
        var modal = new SubscriptionEditorPage(title, subtitle, saveButtonText, initialValue);
        await page.Navigation.PushModalAsync(modal, false);
        return await modal._resultSource.Task;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(60);
            NameEntry.Focus();
        });
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(null);
        return true;
    }

    private void OnNameCompleted(object? sender, EventArgs e) => UrlEntry.Focus();

    private void OnUrlCompleted(object? sender, EventArgs e) => CountEntry.Focus();

    private async void OnCancelClicked(object? sender, EventArgs e) => await CloseAsync(null);

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;

        var name = NameEntry.Text?.Trim() ?? string.Empty;
        var url = UrlEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError(TranslateExtension.Get("SubscriptionNameRequired"));
            return;
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            ShowError(TranslateExtension.Get("SubscriptionUrlRequired"));
            return;
        }

        if (!int.TryParse(CountEntry.Text?.Trim(), out var maxCount) || maxCount <= 0 || maxCount > 200000)
        {
            ShowError(TranslateExtension.Get("ValidationMaxCountRange"));
            return;
        }

        await CloseAsync(new SubscriptionEditorResult(name, url, maxCount));
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }

    private async Task CloseAsync(SubscriptionEditorResult? result)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _resultSource.TrySetResult(result);

        if (Navigation.ModalStack.LastOrDefault() == this)
        {
            await Navigation.PopModalAsync(false);
        }
    }
}
