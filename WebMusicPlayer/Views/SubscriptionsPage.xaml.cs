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

        var name = await page.DisplayPromptAsync("修改订阅", "请输入订阅名称", initialValue: subscription.Name);
        if (name is null)
        {
            return;
        }

        var url = await page.DisplayPromptAsync("修改订阅", "请输入订阅地址", initialValue: subscription.Url, keyboard: Keyboard.Url);
        if (url is null)
        {
            return;
        }

        try
        {
            await viewModel.EditSubscriptionAsync(subscription, name, url);
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
