namespace WebMusicPlayer.Models;

public sealed class AppState
{
    public List<StreamItem> Streams { get; set; } = [];

    public List<SubscriptionItem> Subscriptions { get; set; } = [];

    public DateTimeOffset? DeletePromptSuppressedUntilUtc { get; set; }

    public string SelectedFilterKey { get; set; } = FilterOption.AllKey;
}
