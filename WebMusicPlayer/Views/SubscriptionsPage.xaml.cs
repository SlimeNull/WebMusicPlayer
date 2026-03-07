using WebMusicPlayer.Localization;
using WebMusicPlayer.Models;
using WebMusicPlayer.ViewModels;

namespace WebMusicPlayer.Views;

public partial class SubscriptionsPage : ContentView
{
    public SubscriptionsPage()
    {
        InitializeComponent();
    }

    private async void OnEditSubscriptionClicked(object? sender, EventArgs e)
    {
        if (BindingContext is not MainViewModel viewModel || sender is not Button { BindingContext: SubscriptionItem subscription })
        {
            return;
        }

        var page = this.GetParentPage();
        if (page is null)
        {
            return;
        }

        var result = await SubscriptionEditorPage.ShowAsync(
            page,
            TranslateExtension.Get("EditorEditSubscriptionTitle"),
            TranslateExtension.Get("EditorEditSubscriptionSubtitle"),
            TranslateExtension.Get("EditorEditSubscriptionSave"),
            new SubscriptionEditorResult(subscription.Name, subscription.Url, subscription.MaxPlaylistDepth, subscription.MaxStreamCount));
        if (result is null)
        {
            return;
        }

        try
        {
            await viewModel.EditSubscriptionAsync(subscription, result.Name, result.Url, result.MaxPlaylistDepth, result.MaxStreamCount);
        }
        catch (Exception ex)
        {
            await page.DisplayAlertAsync(TranslateExtension.Get("EditFailedTitle"), ex.Message, TranslateExtension.Get("GenericGotIt"));
        }
    }

    private async void OnDeleteSubscriptionInvoked(object? sender, EventArgs e)
    {
        if (BindingContext is not MainViewModel viewModel || sender is not SwipeItem { BindingContext: SubscriptionItem subscription })
        {
            return;
        }

        var page = this.GetParentPage();
        if (page is null)
        {
            return;
        }

        var confirmed = await page.DisplayAlertAsync(
            TranslateExtension.Get("DeleteSubscriptionConfirmTitle"),
            TranslateExtension.Format("DeleteSubscriptionConfirmMessageFormat", subscription.Name),
            TranslateExtension.Get("GenericDelete"),
            TranslateExtension.Get("GenericCancel"));
        if (!confirmed)
        {
            return;
        }

        await viewModel.DeleteSubscriptionAsync(subscription);
    }
}
