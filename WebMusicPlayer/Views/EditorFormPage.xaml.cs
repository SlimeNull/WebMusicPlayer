using WebMusicPlayer.Models;

namespace WebMusicPlayer.Views;

public partial class EditorFormPage : ContentPage
{
    private readonly TaskCompletionSource<EditorFormResult?> _resultSource = new();
    private bool _isClosing;

    public EditorFormPage(EditorFormOptions options)
    {
        InitializeComponent();

        TitleLabel.Text = options.Title;
        SubtitleLabel.Text = options.Subtitle;
        PrimaryLabel.Text = options.PrimaryLabel;
        PrimaryEntry.Placeholder = options.PrimaryPlaceholder;
        PrimaryEntry.Text = options.PrimaryValue;
        SecondaryLabel.Text = options.SecondaryLabel;
        SecondaryEntry.Placeholder = options.SecondaryPlaceholder;
        SecondaryEntry.Text = options.SecondaryValue;
        SaveButton.Text = options.SaveButtonText;
    }

    public static async Task<EditorFormResult?> ShowAsync(Page page, EditorFormOptions options)
    {
        var editorPage = new EditorFormPage(options);
        await page.Navigation.PushModalAsync(editorPage, false);
        return await editorPage._resultSource.Task;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(60);
            PrimaryEntry.Focus();
        });
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(null);
        return true;
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        await CloseAsync(null);
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;

        var primary = PrimaryEntry.Text?.Trim() ?? string.Empty;
        var secondary = SecondaryEntry.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(primary))
        {
            ShowError($"请输入{PrimaryLabel.Text}。");
            return;
        }

        if (string.IsNullOrWhiteSpace(secondary))
        {
            ShowError($"请输入{SecondaryLabel.Text}。");
            return;
        }

        await CloseAsync(new EditorFormResult(primary, secondary));
    }

    private void OnPrimaryCompleted(object? sender, EventArgs e)
    {
        SecondaryEntry.Focus();
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }

    private async Task CloseAsync(EditorFormResult? result)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        if (!_resultSource.Task.IsCompleted)
        {
            _resultSource.TrySetResult(result);
        }

        if (Navigation.ModalStack.LastOrDefault() == this)
        {
            await Navigation.PopModalAsync(false);
        }
    }
}
