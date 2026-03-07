using CommunityToolkit.Mvvm.ComponentModel;

namespace WebMusicPlayer.Models;

public partial class SubscriptionItem : ObservableObject
{
    [ObservableProperty]
    private Guid id = Guid.NewGuid();

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string url = string.Empty;

    [ObservableProperty]
    private int maxPlaylistDepth = SubscriptionImportOptions.Default.MaxPlaylistDepth;

    [ObservableProperty]
    private int maxStreamCount = SubscriptionImportOptions.Default.MaxStreamCount;

    [ObservableProperty]
    private DateTimeOffset? lastUpdatedUtc;

    public string LimitsLabel => $"递归 {MaxPlaylistDepth} 层 · 最多 {MaxStreamCount} 个媒体流";

    public string LastUpdatedLabel => LastUpdatedUtc.HasValue
        ? $"上次更新: {LastUpdatedUtc.Value.LocalDateTime:yyyy-MM-dd HH:mm}"
        : "尚未更新";

    public SubscriptionImportOptions GetImportOptions() => new(MaxPlaylistDepth, MaxStreamCount);

    partial void OnMaxPlaylistDepthChanged(int value)
    {
        OnPropertyChanged(nameof(LimitsLabel));
    }

    partial void OnMaxStreamCountChanged(int value)
    {
        OnPropertyChanged(nameof(LimitsLabel));
    }

    partial void OnLastUpdatedUtcChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(LastUpdatedLabel));
    }
}
