namespace WebMusicPlayer.Models;

public sealed record SubscriptionEditorResult(
    string Name,
    string Url,
    int MaxPlaylistDepth,
    int MaxStreamCount);
