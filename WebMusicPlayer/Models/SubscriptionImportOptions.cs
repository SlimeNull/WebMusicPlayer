namespace WebMusicPlayer.Models;

public sealed record SubscriptionImportOptions(int MaxPlaylistDepth, int MaxStreamCount, int MaxConcurrentRequests = 4)
{
    public static SubscriptionImportOptions Default { get; } = new(6, 1000, 4);
}
