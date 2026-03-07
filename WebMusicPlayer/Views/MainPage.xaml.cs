using CommunityToolkit.Maui.Core;
using WebMusicPlayer.Models;
using WebMusicPlayer.ViewModels;

namespace WebMusicPlayer.Views;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;
    private bool _isInitialized;

    public MainPage(MainViewModel viewModel)
    {
        BindingContext = _viewModel = viewModel;
        InitializeComponent();

        _viewModel.PlayRequested += OnPlayRequested;
        _viewModel.StopRequested += OnStopRequested;
    }

    protected override async void OnAppearing()
    {
        _viewModel.SelectedTab = AppTab.Streams;
        base.OnAppearing();
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("加载失败", ex.Message, "知道了");
        }
    }

    private async void OnStreamsMenuClicked(object? sender, EventArgs e)
    {
        var selection = await DisplayActionSheetAsync("添加媒体流", "取消", null, "手动添加", "导入 XSPF", "导入 ZIP");
        switch (selection)
        {
            case "手动添加":
                await AddManualStreamAsync();
                break;
            case "导入 XSPF":
                await ImportFileAsync("请选择由 VLC 导出的 XSPF 文件");
                break;
            case "导入 ZIP":
                await ImportFileAsync("请选择包含 m3u8 或 txt 的 ZIP 压缩包");
                break;
        }
    }

    private async void OnFilterClicked(object? sender, EventArgs e)
    {
        var result = await StreamFilterPage.ShowAsync(
            this,
            _viewModel.GetAvailableFilters(),
            _viewModel.SelectedFilterKey,
            _viewModel.SelectedFilterKeyword);

        if (result is not null)
        {
            await _viewModel.ApplyFilterAsync(result.SourceKey, result.Keyword);
        }
    }

    private async void OnSubscriptionsMenuClicked(object? sender, EventArgs e)
    {
        var selection = await DisplayActionSheetAsync("订阅操作", "取消", null, "添加订阅");
        if (selection == "添加订阅")
        {
            await AddSubscriptionAsync();
        }
    }

    private async void OnUpdateSubscriptionsClicked(object? sender, EventArgs e)
    {
        try
        {
            await _viewModel.UpdateAllSubscriptionsAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("更新失败", ex.Message, "知道了");
        }
    }

    private async Task AddManualStreamAsync()
    {
        var result = await EditorFormPage.ShowAsync(this, new EditorFormOptions(
            Title: "添加媒体流",
            Subtitle: "输入名称与网络地址，即可将新的 Stream 加入列表。",
            PrimaryLabel: "媒体流名称",
            PrimaryPlaceholder: "例如：Jazz Radio",
            SecondaryLabel: "媒体流地址",
            SecondaryPlaceholder: "https://example.com/live.m3u8",
            SaveButtonText: "添加媒体流"));
        if (result is null)
        {
            return;
        }

        try
        {
            await _viewModel.AddManualStreamAsync(result.PrimaryValue, result.SecondaryValue);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("添加失败", ex.Message, "知道了");
        }
    }

    private async Task ImportFileAsync(string title)
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = title
            });

            if (file is null)
            {
                return;
            }

            await using var stream = await file.OpenReadAsync();
            await _viewModel.ImportManualFileAsync(file.FileName, stream);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("导入失败", ex.Message, "知道了");
        }
    }

    private async Task AddSubscriptionAsync()
    {
        var result = await EditorFormPage.ShowAsync(this, new EditorFormOptions(
            Title: "添加订阅",
            Subtitle: "订阅地址可以返回 xspf、m3u8 或 zip，应用会自动解析。",
            PrimaryLabel: "订阅名称",
            PrimaryPlaceholder: "例如：我的电台合集",
            SecondaryLabel: "订阅地址",
            SecondaryPlaceholder: "https://example.com/subscription",
            SaveButtonText: "添加订阅"));
        if (result is null)
        {
            return;
        }

        try
        {
            await _viewModel.AddSubscriptionAsync(result.PrimaryValue, result.SecondaryValue);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("添加失败", ex.Message, "知道了");
        }
    }

    private void OnPlayRequested(object? sender, StreamItem stream)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Player.Source = stream.Url;
            Player.Play();
            _viewModel.SetPlaybackState(true);
        });
    }

    private void OnStopRequested(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Player.Stop();
            _viewModel.SetPlaybackState(false);
        });
    }

    private void OnPlayerMediaOpened(object? sender, EventArgs e)
    {
        _viewModel.SetPlaybackState(true);
    }

    private void OnPlayerMediaEnded(object? sender, EventArgs e)
    {
        _viewModel.SetPlaybackState(false);
    }

    private async void OnPlayerMediaFailed(object? sender, MediaFailedEventArgs e)
    {
        _viewModel.SetPlaybackState(false);
        await DisplayAlertAsync("播放失败", e.ErrorMessage ?? "媒体流无法播放。", "知道了");
    }
}
