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
    private DateTimeOffset? lastUpdatedUtc;

    public string LastUpdatedLabel => LastUpdatedUtc.HasValue
        ? $"上次更新: {LastUpdatedUtc.Value.LocalDateTime:yyyy-MM-dd HH:mm}"
        : "尚未更新";

    partial void OnLastUpdatedUtcChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(LastUpdatedLabel));
    }
}
