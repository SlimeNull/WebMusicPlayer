namespace WebMusicPlayer.Models;

public sealed record SubscriptionImportOptions(int MaxStreamCount)
{
    public static SubscriptionImportOptions Default { get; } = new(1000);
}
