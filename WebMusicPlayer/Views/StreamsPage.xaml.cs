using WebMusicPlayer.Localization;
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

            var no = TranslateExtension.Get("GenericNo");
            var yes = TranslateExtension.Get("GenericYes");
            var suppress = TranslateExtension.Get("DeleteStreamConfirmSuppress");
            var choice = await page.DisplayActionSheetAsync(TranslateExtension.Get("DeleteStreamConfirmTitle"), no, null, yes, suppress);
            shouldDelete = choice is not null && (choice == yes || choice == suppress);
            suppressReminder = choice == suppress;
        }

        if (!shouldDelete)
        {
            return;
        }

        await viewModel.DeleteStreamAsync(stream, suppressReminder);
    }
}
