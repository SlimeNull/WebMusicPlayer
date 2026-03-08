using System.ComponentModel;
using CommunityToolkit.Maui.Core;
using WebMusicPlayer.Localization;
using WebMusicPlayer.Models;
using WebMusicPlayer.Services;
using WebMusicPlayer.ViewModels;

namespace WebMusicPlayer.Views;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;
    private readonly MediaArtworkService _mediaArtworkService;
    private bool _isInitialized;
    private StreamItem? _metadataStream;
    private string _metadataArtworkUrl = string.Empty;

    public MainPage(MainViewModel viewModel, MediaArtworkService mediaArtworkService)
    {
        BindingContext = _viewModel = viewModel;
        _mediaArtworkService = mediaArtworkService;
        InitializeComponent();

        _viewModel.PlayRequested += OnPlayRequested;
        _viewModel.StopRequested += OnStopRequested;
    }

    protected override async void OnAppearing()
    {
        if (_viewModel.SelectedTab is AppTab.None)
        {
            _viewModel.SelectedTab = AppTab.Streams;
        }

        base.OnAppearing();
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        try
        {
            await _viewModel.InitializeAsync();
            _metadataArtworkUrl = await _mediaArtworkService.GetArtworkDataUrlAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(TranslateExtension.Get("LoadFailedTitle"), ex.Message, TranslateExtension.Get("GenericGotIt"));
        }
    }

    private async void OnStreamsMenuClicked(object? sender, EventArgs e)
    {
        var manualAdd = TranslateExtension.Get("ActionManualAdd");
        var importXspf = TranslateExtension.Get("ActionImportXspf");
        var importZip = TranslateExtension.Get("ActionImportZip");
        var selection = await DisplayActionSheetAsync(TranslateExtension.Get("ActionAddStreamTitle"), TranslateExtension.Get("GenericCancel"), null, manualAdd, importXspf, importZip);
        switch (selection)
        {
            case var _ when selection == manualAdd:
                await AddManualStreamAsync();
                break;
            case var _ when selection == importXspf:
                await ImportFileAsync(TranslateExtension.Get("PickXspfTitle"));
                break;
            case var _ when selection == importZip:
                await ImportFileAsync(TranslateExtension.Get("PickZipTitle"));
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
        var addSubscription = TranslateExtension.Get("ActionAddSubscription");
        var selection = await DisplayActionSheetAsync(TranslateExtension.Get("ActionSubscriptionTitle"), TranslateExtension.Get("GenericCancel"), null, addSubscription);
        if (selection == addSubscription)
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
            await DisplayAlertAsync(TranslateExtension.Get("UpdateFailedTitle"), ex.Message, TranslateExtension.Get("GenericGotIt"));
        }
    }

    private async Task AddManualStreamAsync()
    {
        var result = await EditorFormPage.ShowAsync(this, new EditorFormOptions(
            Title: TranslateExtension.Get("EditorAddStreamTitle"),
            Subtitle: TranslateExtension.Get("EditorAddStreamSubtitle"),
            PrimaryLabel: TranslateExtension.Get("StreamNameLabel"),
            PrimaryPlaceholder: TranslateExtension.Get("StreamNamePlaceholder"),
            SecondaryLabel: TranslateExtension.Get("StreamUrlLabel"),
            SecondaryPlaceholder: TranslateExtension.Get("StreamUrlPlaceholder"),
            SaveButtonText: TranslateExtension.Get("EditorAddStreamSave")));
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
            await DisplayAlertAsync(TranslateExtension.Get("AddFailedTitle"), ex.Message, TranslateExtension.Get("GenericGotIt"));
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
            await DisplayAlertAsync(TranslateExtension.Get("ImportFailedTitle"), ex.Message, TranslateExtension.Get("GenericGotIt"));
        }
    }

    private async Task AddSubscriptionAsync()
    {
        var result = await SubscriptionEditorPage.ShowAsync(
            this,
            TranslateExtension.Get("EditorAddSubscriptionTitle"),
            TranslateExtension.Get("EditorAddSubscriptionSubtitle"),
            TranslateExtension.Get("EditorAddSubscriptionSave"));
        if (result is null)
        {
            return;
        }

        try
        {
            await _viewModel.AddSubscriptionAsync(result.Name, result.Url, result.MaxPlaylistDepth, result.MaxStreamCount);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(TranslateExtension.Get("AddFailedTitle"), ex.Message, TranslateExtension.Get("GenericGotIt"));
        }
    }

    private void OnPlayRequested(object? sender, StreamItem stream)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            SetMetadataStream(stream);
            ApplyPlayerMetadata(stream);
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
            ClearPlayerMetadata();
            _viewModel.SetPlaybackState(false);
        });
    }

    private void OnPlayerMediaOpened(object? sender, EventArgs e)
    {
        _viewModel.SetPlaybackState(true);
    }

    private void OnPlayerMediaEnded(object? sender, EventArgs e)
    {
        ClearPlayerMetadata();
        _viewModel.SetPlaybackState(false);
    }

    private async void OnPlayerMediaFailed(object? sender, MediaFailedEventArgs e)
    {
        ClearPlayerMetadata();
        _viewModel.SetPlaybackState(false);
        await DisplayAlertAsync(TranslateExtension.Get("PlaybackFailedTitle"), e.ErrorMessage ?? TranslateExtension.Get("PlaybackFailedMessage"), TranslateExtension.Get("GenericGotIt"));
    }

    private void SetMetadataStream(StreamItem stream)
    {
        if (ReferenceEquals(_metadataStream, stream))
        {
            return;
        }

        if (_metadataStream is not null)
        {
            _metadataStream.PropertyChanged -= OnMetadataStreamPropertyChanged;
        }

        _metadataStream = stream;
        _metadataStream.PropertyChanged += OnMetadataStreamPropertyChanged;
    }

    private void OnMetadataStreamPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not StreamItem stream)
        {
            return;
        }

        if (e.PropertyName is nameof(StreamItem.Name) or nameof(StreamItem.OriginLabel))
        {
            MainThread.BeginInvokeOnMainThread(() => ApplyPlayerMetadata(stream));
        }
    }

    private void ApplyPlayerMetadata(StreamItem stream)
    {
        Player.MetadataTitle = stream.Name;
        Player.MetadataArtist = stream.OriginLabel;
        Player.MetadataArtworkUrl = _metadataArtworkUrl;
    }

    private void ClearPlayerMetadata()
    {
        if (_metadataStream is not null)
        {
            _metadataStream.PropertyChanged -= OnMetadataStreamPropertyChanged;
            _metadataStream = null;
        }

        Player.MetadataTitle = string.Empty;
        Player.MetadataArtist = string.Empty;
        Player.MetadataArtworkUrl = string.Empty;
    }
}
