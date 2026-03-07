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
            "编辑订阅",
            "修改订阅名称、地址或抓取限制后，会重新拉取该订阅的媒体流。",
            "保存修改",
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
            await page.DisplayAlertAsync("修改失败", ex.Message, "知道了");
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

        var confirmed = await page.DisplayAlertAsync("删除订阅", $"要删除订阅 “{subscription.Name}” 以及它带来的媒体流吗？", "删除", "取消");
        if (!confirmed)
        {
            return;
        }

        await viewModel.DeleteSubscriptionAsync(subscription);
    }
}
