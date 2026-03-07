using WebMusicPlayer.Models;
using WebMusicPlayer.ViewModels;

namespace WebMusicPlayer.Views;

public partial class StreamsPage : ContentView
{
    public StreamsPage()
    {
        InitializeComponent();
    }

    private async void OnDeleteStreamInvoked(object? sender, EventArgs e)
    {
        if (BindingContext is not MainViewModel viewModel || sender is not SwipeItem { BindingContext: StreamItem stream })
        {
            return;
        }

        var shouldDelete = true;
        var suppressReminder = false;
        if (viewModel.ShouldConfirmDelete)
        {
            var page = this.GetParentPage();
            if (page is null)
            {
                return;
            }

            var choice = await page.DisplayActionSheetAsync("是否删除此媒体流", "否", null, "是", "是, 并且在接下来的五分钟内不要提醒我");
            shouldDelete = choice is "是" or "是, 并且在接下来的五分钟内不要提醒我";
            suppressReminder = choice == "是, 并且在接下来的五分钟内不要提醒我";
        }

        if (!shouldDelete)
        {
            return;
        }

        await viewModel.DeleteStreamAsync(stream, suppressReminder);
    }
}
