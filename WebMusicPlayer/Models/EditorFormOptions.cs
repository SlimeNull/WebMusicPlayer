namespace WebMusicPlayer.Models;

public sealed record EditorFormOptions(
    string Title,
    string Subtitle,
    string PrimaryLabel,
    string PrimaryPlaceholder,
    string SecondaryLabel,
    string SecondaryPlaceholder,
    string SaveButtonText,
    string PrimaryValue = "",
    string SecondaryValue = "");
