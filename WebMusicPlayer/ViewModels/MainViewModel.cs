using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WebMusicPlayer.Localization;
using WebMusicPlayer.Models;
using WebMusicPlayer.Services;

namespace WebMusicPlayer.ViewModels;

public sealed partial class MainViewModel(AppStateStore appStateStore, StreamImportService streamImportService) : ObservableObject
{
    private readonly AppStateStore _appStateStore = appStateStore;
    private readonly StreamImportService _streamImportService = streamImportService;
    private readonly Dictionary<Guid, int> _subscriptionProgressById = [];
    private AppState _state = new();
    private bool _isInitialized;
    private CancellationTokenSource? _busyOperationCancellationTokenSource;
    private string _busyBaseText = TranslateExtension.Get("BusyPleaseWait");
    private volatile bool _busyOperationAbortRequested;

    public ObservableCollection<StreamItem> VisibleStreams { get; } = [];

    public ObservableCollection<StreamItem> FavouriteStreams { get; } = [];

    public ObservableCollection<SubscriptionItem> Subscriptions { get; } = [];

    public event EventHandler<StreamItem>? PlayRequested;

    public event EventHandler? StopRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPageTitle))]
    [NotifyPropertyChangedFor(nameof(CurrentPageIconGlyph))]
    [NotifyPropertyChangedFor(nameof(IsFavouritesVisible))]
    [NotifyPropertyChangedFor(nameof(IsStreamsVisible))]
    [NotifyPropertyChangedFor(nameof(IsSubscriptionsVisible))]
    [NotifyPropertyChangedFor(nameof(ShowStreamsToolbar))]
    [NotifyPropertyChangedFor(nameof(ShowSubscriptionsToolbar))]
    private AppTab selectedTab = AppTab.None;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStreamName))]
    [NotifyPropertyChangedFor(nameof(CurrentStreamSubtitle))]
    [NotifyPropertyChangedFor(nameof(HasCurrentStream))]
    private StreamItem? currentStream;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaybackButtonText))]
    [NotifyPropertyChangedFor(nameof(PlaybackIconGlyph))]
    private bool isPlaying;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string busyText = TranslateExtension.Get("BusyPleaseWait");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBusyActions))]
    private bool canAbortBusyOperation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBusyActions))]
    private bool canCancelBusyOperation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedFilterLabel))]
    [NotifyPropertyChangedFor(nameof(StreamsSummaryText))]
    private string selectedFilterKey = FilterOption.AllKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StreamsSummaryText))]
    private string selectedFilterKeyword = string.Empty;

    public string CurrentPageTitle => SelectedTab switch
    {
        AppTab.Favourites => TranslateExtension.Get("PageTitleFavourites"),
        AppTab.Streams => TranslateExtension.Get("PageTitleStreams"),
        AppTab.Subscriptions => TranslateExtension.Get("PageTitleSubscriptions"),
        _ => TranslateExtension.Get("AppName")
    };

    public string CurrentPageIconGlyph => SelectedTab switch
    {
        AppTab.Favourites => AppIcons.FavouriteFilled,
        AppTab.Streams => AppIcons.Stream,
        AppTab.Subscriptions => AppIcons.Subscription,
        _ => AppIcons.Stream
    };

    public bool IsFavouritesVisible => SelectedTab == AppTab.Favourites;

    public bool IsStreamsVisible => SelectedTab == AppTab.Streams;

    public bool IsSubscriptionsVisible => SelectedTab == AppTab.Subscriptions;

    public bool ShowStreamsToolbar => SelectedTab == AppTab.Streams;

    public bool ShowSubscriptionsToolbar => SelectedTab == AppTab.Subscriptions;

    public bool HasCurrentStream => CurrentStream is not null;

    public bool ShowBusyActions => CanAbortBusyOperation || CanCancelBusyOperation;

    public string CurrentStreamName => CurrentStream?.Name ?? TranslateExtension.Get("CurrentStreamNoneTitle");

    public string CurrentStreamSubtitle => CurrentStream?.OriginLabel ?? TranslateExtension.Get("CurrentStreamNoneSubtitle");

    public string PlaybackButtonText => IsPlaying ? TranslateExtension.Get("PlaybackStop") : TranslateExtension.Get("PlaybackPlay");

    public string PlaybackIconGlyph => IsPlaying ? AppIcons.Stop : AppIcons.Play;

    public string SelectedFilterLabel => GetAvailableFilters().FirstOrDefault(option => option.Key == SelectedFilterKey)?.Label ?? TranslateExtension.Get("FilterAllSources");

    public string StreamsSummaryText => string.IsNullOrWhiteSpace(SelectedFilterKeyword)
        ? TranslateExtension.Format("StreamsSummaryFormat", SelectedFilterLabel, VisibleStreams.Count, _state.Streams.Count)
        : TranslateExtension.Format("StreamsSummaryWithKeywordFormat", SelectedFilterLabel, SelectedFilterKeyword, VisibleStreams.Count, _state.Streams.Count);

    public string FavouritesSummaryText => TranslateExtension.Format("FavouritesSummaryFormat", FavouriteStreams.Count);

    public string SubscriptionsSummaryText => TranslateExtension.Format("SubscriptionsSummaryFormat", Subscriptions.Count);

    public bool ShouldConfirmDelete => !_state.DeletePromptSuppressedUntilUtc.HasValue || _state.DeletePromptSuppressedUntilUtc <= DateTimeOffset.UtcNow;

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _state = await _appStateStore.LoadAsync();
        _state.Streams ??= [];
        _state.Subscriptions ??= [];
        _state.SelectedFilterKey = string.IsNullOrWhiteSpace(_state.SelectedFilterKey) ? FilterOption.AllKey : _state.SelectedFilterKey;
        _state.SelectedFilterKeyword = _state.SelectedFilterKeyword?.Trim() ?? string.Empty;

        foreach (var stream in _state.Streams)
        {
            AttachStream(stream);
        }

        foreach (var subscription in _state.Subscriptions)
        {
            Subscriptions.Add(subscription);
        }

        SelectedFilterKey = _state.SelectedFilterKey;
        SelectedFilterKeyword = _state.SelectedFilterKeyword;
        RefreshAllViews();
        _isInitialized = true;
    }

    public IReadOnlyList<FilterOption> GetAvailableFilters()
    {
        var filters = new List<FilterOption>
        {
            new(FilterOption.AllKey, TranslateExtension.Get("FilterAllSources")),
            new(FilterOption.ManualKey, TranslateExtension.Get("FilterManualAdded"))
        };

        filters.AddRange(Subscriptions.Select(subscription => new FilterOption(subscription.Id.ToString(), subscription.Name)));
        return filters;
    }

    public async Task ApplyFilterAsync(string key, string? keyword)
    {
        SelectedFilterKey = string.IsNullOrWhiteSpace(key) ? FilterOption.AllKey : key;
        SelectedFilterKeyword = keyword?.Trim() ?? string.Empty;
        _state.SelectedFilterKey = SelectedFilterKey;
        _state.SelectedFilterKeyword = SelectedFilterKeyword;
        RefreshAllViews();
        await SaveStateAsync();
    }

    public async Task AddManualStreamAsync(string name, string address)
    {
        var normalizedUrl = ValidateHttpAddress(address);
        var normalizedName = NormalizeName(name, normalizedUrl);

        if (ContainsStream(normalizedUrl))
        {
            return;
        }

        var stream = new StreamItem
        {
            Name = normalizedName,
            Url = normalizedUrl,
            OriginKind = StreamOriginKind.Manual,
            IsFavourite = false
        };

        _state.Streams.Add(stream);
        AttachStream(stream);
        RefreshAllViews();
        await SaveStateAsync();
    }

    public async Task ImportManualFileAsync(string fileName, Stream stream, CancellationToken cancellationToken = default)
    {
        await RunBusyAsync(TranslateExtension.Get("BusyImportingStreams"), async () =>
        {
            var candidates = await _streamImportService.ParseFromFileAsync(fileName, stream, cancellationToken);
            MergeImportedStreams(candidates, StreamOriginKind.Manual, null, null);
            RefreshAllViews();
            await SaveStateAsync();
        });
    }

    public async Task AddSubscriptionAsync(string name, string address, int maxPlaylistDepth, int maxStreamCount, CancellationToken cancellationToken = default)
    {
        var normalizedUrl = ValidateHttpAddress(address);
        var normalizedName = NormalizeName(name, normalizedUrl);
        var normalizedDepth = ValidateMaxPlaylistDepth(maxPlaylistDepth);
        var normalizedStreamCount = ValidateMaxStreamCount(maxStreamCount);
        var existing = Subscriptions.FirstOrDefault(subscription => string.Equals(subscription.Url, normalizedUrl, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Name = normalizedName;
            existing.MaxPlaylistDepth = normalizedDepth;
            existing.MaxStreamCount = normalizedStreamCount;
            await RefreshSubscriptionAsync(existing, cancellationToken);
            return;
        }

        var subscriptionItem = new SubscriptionItem
        {
            Name = normalizedName,
            Url = normalizedUrl,
            MaxPlaylistDepth = normalizedDepth,
            MaxStreamCount = normalizedStreamCount
        };

        _state.Subscriptions.Add(subscriptionItem);
        Subscriptions.Add(subscriptionItem);
        await SaveStateAsync();
        await RefreshSubscriptionAsync(subscriptionItem, cancellationToken);
    }

    public async Task EditSubscriptionAsync(SubscriptionItem subscription, string name, string address, int maxPlaylistDepth, int maxStreamCount, CancellationToken cancellationToken = default)
    {
        subscription.Name = NormalizeName(name, address);
        subscription.Url = ValidateHttpAddress(address);
        subscription.MaxPlaylistDepth = ValidateMaxPlaylistDepth(maxPlaylistDepth);
        subscription.MaxStreamCount = ValidateMaxStreamCount(maxStreamCount);
        await SaveStateAsync();
        await RefreshSubscriptionAsync(subscription, cancellationToken);
    }

    public async Task UpdateAllSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        var operationToken = BeginSubscriptionBusyOperation(TranslateExtension.Get("BusyUpdatingSubscriptions"), cancellationToken);
        try
        {
            var subscriptions = Subscriptions.ToList();
            var throttler = new SemaphoreSlim(Math.Clamp(subscriptions.Count, 1, 4));
            var tasks = subscriptions.Select(async subscription =>
            {
                await throttler.WaitAsync(operationToken);
                try
                {
                    operationToken.ThrowIfCancellationRequested();
                    if (IsBusyOperationAbortRequested())
                    {
                        return new SubscriptionRefreshSnapshot(subscription, [], null, false);
                    }

                    return await FetchSubscriptionSnapshotAsync(subscription, operationToken);
                }
                catch (OperationCanceledException ex) when (operationToken.IsCancellationRequested)
                {
                    return new SubscriptionRefreshSnapshot(subscription, [], ex, false);
                }
                catch (Exception ex)
                {
                    return new SubscriptionRefreshSnapshot(subscription, [], ex, false);
                }
                finally
                {
                    throttler.Release();
                }
            });

            var snapshots = await Task.WhenAll(tasks);
            if (operationToken.IsCancellationRequested)
            {
                return;
            }

            foreach (var snapshot in snapshots.Where(static x => x.Error is null && x.ShouldApply))
            {
                ApplySubscriptionSnapshot(snapshot);
            }

            RefreshAllViews();
            await SaveStateAsync();

            var failures = snapshots.Where(static x => x.Error is not null).ToList();
            if (failures.Count > 0)
            {
                throw new InvalidOperationException(TranslateExtension.Format("BusyUpdateFailuresFormat", failures.Count, failures[0].Error!.Message));
            }
        }
        finally
        {
            EndBusyOperation();
        }
    }

    public async Task DeleteSubscriptionAsync(SubscriptionItem subscription)
    {
        _state.Subscriptions.Remove(subscription);
        Subscriptions.Remove(subscription);
        RemoveStreamsFromSubscription(subscription.Id);
        RefreshAllViews();
        await SaveStateAsync();
    }

    public async Task DeleteStreamAsync(StreamItem stream, bool suppressReminder)
    {
        if (suppressReminder)
        {
            _state.DeletePromptSuppressedUntilUtc = DateTimeOffset.UtcNow.AddMinutes(5);
        }

        RemoveStream(stream);
        RefreshAllViews();
        await SaveStateAsync();
    }

    public async Task SetFavouriteAsync(StreamItem stream, bool isFavourite)
    {
        stream.IsFavourite = isFavourite;
        RefreshAllViews();
        await SaveStateAsync();
    }

    [RelayCommand]
    private async Task ToggleFavouriteAsync(StreamItem? stream)
    {
        if (stream is null)
        {
            return;
        }

        await SetFavouriteAsync(stream, !stream.IsFavourite);
    }

    [RelayCommand]
    private async Task ArchiveFavouriteAsync(StreamItem? stream)
    {
        if (stream is null)
        {
            return;
        }

        await SetFavouriteAsync(stream, false);
    }

    [RelayCommand]
    private void SelectTab(string? tabName)
    {
        if (Enum.TryParse<AppTab>(tabName, ignoreCase: true, out var parsed))
        {
            SelectedTab = parsed;
        }
    }

    [RelayCommand]
    private Task PlayStreamAsync(StreamItem? stream)
    {
        if (stream is null)
        {
            return Task.CompletedTask;
        }

        CurrentStream = stream;
        IsPlaying = true;
        PlayRequested?.Invoke(this, stream);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task TogglePlaybackAsync()
    {
        if (CurrentStream is null)
        {
            return Task.CompletedTask;
        }

        if (IsPlaying)
        {
            IsPlaying = false;
            StopRequested?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        IsPlaying = true;
        PlayRequested?.Invoke(this, CurrentStream);
        return Task.CompletedTask;
    }

    public void SetPlaybackState(bool playing)
    {
        IsPlaying = playing;
    }

    public void ClearCurrentStream()
    {
        CurrentStream = null;
        IsPlaying = false;
    }

    public async Task SuppressDeletePromptAsync()
    {
        _state.DeletePromptSuppressedUntilUtc = DateTimeOffset.UtcNow.AddMinutes(5);
        await SaveStateAsync();
    }

    [RelayCommand]
    private void AbortBusyOperation()
    {
        if (!CanAbortBusyOperation)
        {
            return;
        }

        _busyOperationAbortRequested = true;
        CanAbortBusyOperation = false;
        SetBusyTextThreadSafe(TranslateExtension.Format("BusyStoppingParsedFormat", _busyBaseText, GetResolvedMediaCount()));
    }

    [RelayCommand]
    private void CancelBusyOperation()
    {
        if (!CanCancelBusyOperation)
        {
            return;
        }

        CanAbortBusyOperation = false;
        CanCancelBusyOperation = false;
        SetBusyTextThreadSafe(TranslateExtension.Get("BusyCancelling"));
        _busyOperationCancellationTokenSource?.Cancel();
    }

    private async Task RefreshSubscriptionAsync(SubscriptionItem subscription, CancellationToken cancellationToken)
    {
        var operationToken = BeginSubscriptionBusyOperation(TranslateExtension.Format("BusyUpdatingSubscriptionFormat", subscription.Name), cancellationToken);
        try
        {
            var snapshot = await FetchSubscriptionSnapshotAsync(subscription, operationToken);
            if (operationToken.IsCancellationRequested)
            {
                return;
            }

            if (snapshot.ShouldApply)
            {
                ApplySubscriptionSnapshot(snapshot);
                RefreshAllViews();
                await SaveStateAsync();
            }
        }
        finally
        {
            EndBusyOperation();
        }
    }

    private async Task<SubscriptionRefreshSnapshot> FetchSubscriptionSnapshotAsync(SubscriptionItem subscription, CancellationToken cancellationToken)
    {
        var progress = new Progress<int>(count => ReportSubscriptionProgress(subscription, count));
        var candidates = await Task.Run(
            () => _streamImportService.ParseFromAddressAsync(subscription.Url, subscription.GetImportOptions(), progress, IsBusyOperationAbortRequested, cancellationToken),
            cancellationToken);

        return new SubscriptionRefreshSnapshot(subscription, candidates, null, true);
    }

    private void ApplySubscriptionSnapshot(SubscriptionRefreshSnapshot snapshot)
    {
        RemoveStreamsFromSubscription(snapshot.Subscription.Id);
        MergeImportedStreams(snapshot.Candidates, StreamOriginKind.Subscription, snapshot.Subscription.Id, snapshot.Subscription.Name);
        snapshot.Subscription.LastUpdatedUtc = DateTimeOffset.UtcNow;
    }

    private void MergeImportedStreams(
        IEnumerable<ImportStreamCandidate> candidates,
        StreamOriginKind originKind,
        Guid? subscriptionId,
        string? subscriptionName)
    {
        foreach (var candidate in candidates)
        {
            if (ContainsStream(candidate.Url))
            {
                continue;
            }

            var stream = new StreamItem
            {
                Name = NormalizeName(candidate.Name, candidate.Url),
                Url = candidate.Url,
                OriginKind = originKind,
                SubscriptionId = subscriptionId,
                SubscriptionName = subscriptionName,
                IsFavourite = false
            };

            _state.Streams.Add(stream);
            AttachStream(stream);
        }
    }

    private void AttachStream(StreamItem stream)
    {
        stream.PropertyChanged += (_, __) =>
        {
            OnPropertyChanged(nameof(FavouritesSummaryText));
            OnPropertyChanged(nameof(StreamsSummaryText));
        };
    }

    private void RefreshAllViews()
    {
        RefreshVisibleStreams();
        RefreshFavouriteStreams();
        OnPropertyChanged(nameof(FavouritesSummaryText));
        OnPropertyChanged(nameof(StreamsSummaryText));
        OnPropertyChanged(nameof(SubscriptionsSummaryText));
        OnPropertyChanged(nameof(SelectedFilterLabel));
    }

    private void RefreshVisibleStreams()
    {
        var filtered = _state.Streams
            .Where(MatchesSelectedFilter)
            .OrderBy(stream => stream.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        ReplaceCollection(VisibleStreams, filtered);
        _state.SelectedFilterKey = SelectedFilterKey;
        _state.SelectedFilterKeyword = SelectedFilterKeyword;
    }

    private void RefreshFavouriteStreams()
    {
        var favourites = _state.Streams
            .Where(static stream => stream.IsFavourite)
            .OrderBy(stream => stream.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        ReplaceCollection(FavouriteStreams, favourites);
    }

    private bool MatchesSelectedFilter(StreamItem stream)
    {
        var matchesSource = SelectedFilterKey switch
        {
            FilterOption.AllKey => true,
            FilterOption.ManualKey => stream.OriginKind == StreamOriginKind.Manual,
            _ => string.Equals(stream.SubscriptionId?.ToString(), SelectedFilterKey, StringComparison.OrdinalIgnoreCase)
        };

        if (!matchesSource)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SelectedFilterKeyword))
        {
            return true;
        }

        return ContainsKeyword(stream.Name, SelectedFilterKeyword)
            || ContainsKeyword(stream.Url, SelectedFilterKeyword);
    }

    private static bool ContainsKeyword(string? source, string keyword)
    {
        return !string.IsNullOrWhiteSpace(source)
            && source.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);
    }

    private void RemoveStreamsFromSubscription(Guid subscriptionId)
    {
        var streams = _state.Streams
            .Where(stream => stream.SubscriptionId == subscriptionId)
            .ToList();

        foreach (var stream in streams)
        {
            RemoveStream(stream);
        }
    }

    private void RemoveStream(StreamItem stream)
    {
        _state.Streams.Remove(stream);
        if (CurrentStream == stream)
        {
            IsPlaying = false;
            StopRequested?.Invoke(this, EventArgs.Empty);
            CurrentStream = null;
        }
    }

    private bool ContainsStream(string url)
    {
        return _state.Streams.Any(stream => string.Equals(stream.Url, url, StringComparison.OrdinalIgnoreCase));
    }

    private async Task SaveStateAsync()
    {
        await _appStateStore.SaveAsync(_state);
    }

    private async Task RunBusyAsync(string message, Func<Task> action)
    {
        _busyBaseText = message;
        _busyOperationAbortRequested = false;
        CanAbortBusyOperation = false;
        CanCancelBusyOperation = false;
        BusyText = message;
        IsBusy = true;
        try
        {
            await action();
        }
        finally
        {
            CanAbortBusyOperation = false;
            CanCancelBusyOperation = false;
            IsBusy = false;
        }
    }

    private CancellationToken BeginSubscriptionBusyOperation(string message, CancellationToken externalCancellationToken)
    {
        _busyOperationCancellationTokenSource?.Dispose();
        _busyOperationCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken);
        _subscriptionProgressById.Clear();
        _busyOperationAbortRequested = false;
        _busyBaseText = message;
        BusyText = message;
        CanAbortBusyOperation = true;
        CanCancelBusyOperation = true;
        IsBusy = true;
        return _busyOperationCancellationTokenSource.Token;
    }

    private void EndBusyOperation()
    {
        _busyOperationCancellationTokenSource?.Dispose();
        _busyOperationCancellationTokenSource = null;
        _subscriptionProgressById.Clear();
        _busyOperationAbortRequested = false;
        CanAbortBusyOperation = false;
        CanCancelBusyOperation = false;
        IsBusy = false;
    }

    private void ReportSubscriptionProgress(SubscriptionItem subscription, int count)
    {
        _subscriptionProgressById[subscription.Id] = count;
        SetBusyTextThreadSafe(TranslateExtension.Format("BusyParsedCountFormat", _busyBaseText, GetResolvedMediaCount()));
    }

    private int GetResolvedMediaCount()
    {
        return _subscriptionProgressById.Values.Sum();
    }

    private bool IsBusyOperationAbortRequested()
    {
        return _busyOperationAbortRequested;
    }

    private void SetBusyTextThreadSafe(string text)
    {
        if (MainThread.IsMainThread)
        {
            BusyText = text;
            return;
        }

        MainThread.BeginInvokeOnMainThread(() => BusyText = text);
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private static string ValidateHttpAddress(string address)
    {
        if (!Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(TranslateExtension.Get("ValidationHttpUrl"));
        }

        return uri.AbsoluteUri;
    }

    private static int ValidateMaxPlaylistDepth(int value)
    {
        if (value < 0 || value > 32)
        {
            throw new InvalidOperationException(TranslateExtension.Get("ValidationMaxDepthRange"));
        }

        return value;
    }

    private static int ValidateMaxStreamCount(int value)
    {
        if (value <= 0 || value > 200000)
        {
            throw new InvalidOperationException(TranslateExtension.Get("ValidationMaxCountRange"));
        }

        return value;
    }

    private static string NormalizeName(string? name, string address)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            var tail = Path.GetFileName(uri.AbsolutePath.TrimEnd('/'));
            if (!string.IsNullOrWhiteSpace(tail))
            {
                return tail;
            }

            return uri.Host;
        }

        return TranslateExtension.Get("UnnamedStream");
    }

    private sealed record SubscriptionRefreshSnapshot(
        SubscriptionItem Subscription,
        IReadOnlyList<ImportStreamCandidate> Candidates,
        Exception? Error,
        bool ShouldApply);
}
