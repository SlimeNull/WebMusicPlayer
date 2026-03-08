using CommunityToolkit.Mvvm.ComponentModel;
using WebMusicPlayer.Localization;

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
    private int maxStreamCount = SubscriptionImportOptions.Default.MaxStreamCount;

    [ObservableProperty]
    private DateTimeOffset? lastUpdatedUtc;

    public string LimitsLabel => TranslateExtension.Format("SubscriptionLimitsFormat", MaxStreamCount);

    public string LastUpdatedLabel => LastUpdatedUtc.HasValue
        ? TranslateExtension.Format("SubscriptionLastUpdatedFormat", LastUpdatedUtc.Value.LocalDateTime)
        : TranslateExtension.Get("SubscriptionLastUpdatedNever");

    public SubscriptionImportOptions GetImportOptions() => new(MaxStreamCount);

    partial void OnMaxStreamCountChanged(int value)
    {
        OnPropertyChanged(nameof(LimitsLabel));
    }

    partial void OnLastUpdatedUtcChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(LastUpdatedLabel));
    }
}
