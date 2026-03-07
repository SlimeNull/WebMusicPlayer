namespace WebMusicPlayer.Models;

public sealed record FilterOption(string Key, string Label)
{
    public const string AllKey = "all";
    public const string ManualKey = "manual";
}
