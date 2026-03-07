using AndroidX.Lifecycle;
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
        var filters = _viewModel.GetAvailableFilters().ToList();
        var selection = await DisplayActionSheetAsync("筛选媒体流", "取消", null, filters.Select(filter => filter.Label).ToArray());
        var selectedFilter = filters.FirstOrDefault(filter => filter.Label == selection);
        if (selectedFilter is not null)
        {
            _viewModel.ApplyFilter(selectedFilter.Key);
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
        var name = await DisplayPromptAsync("手动添加", "请输入媒体流名称", initialValue: string.Empty);
        if (name is null)
        {
            return;
        }

        var url = await DisplayPromptAsync("手动添加", "请输入 http/https 音频流或 m3u8 地址", keyboard: Keyboard.Url);
        if (url is null)
        {
            return;
        }

        try
        {
            await _viewModel.AddManualStreamAsync(name, url);
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
        var name = await DisplayPromptAsync("添加订阅", "请输入订阅名称", initialValue: string.Empty);
        if (name is null)
        {
            return;
        }

        var url = await DisplayPromptAsync("添加订阅", "请输入订阅地址", keyboard: Keyboard.Url);
        if (url is null)
        {
            return;
        }

        try
        {
            await _viewModel.AddSubscriptionAsync(name, url);
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
