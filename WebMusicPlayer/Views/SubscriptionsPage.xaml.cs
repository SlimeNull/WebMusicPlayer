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

        var result = await EditorFormPage.ShowAsync(page, new EditorFormOptions(
            Title: "编辑订阅",
            Subtitle: "修改订阅名称或地址后，会重新拉取该订阅的媒体流。",
            PrimaryLabel: "订阅名称",
            PrimaryPlaceholder: "请输入订阅名称",
            SecondaryLabel: "订阅地址",
            SecondaryPlaceholder: "https://example.com/subscription",
            SaveButtonText: "保存修改",
            PrimaryValue: subscription.Name,
            SecondaryValue: subscription.Url));
        if (result is null)
        {
            return;
        }

        try
        {
            await viewModel.EditSubscriptionAsync(subscription, result.PrimaryValue, result.SecondaryValue);
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
