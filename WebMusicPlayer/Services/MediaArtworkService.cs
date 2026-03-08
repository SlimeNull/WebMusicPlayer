namespace WebMusicPlayer.Services;

public sealed class MediaArtworkService
{
    private const string EmbeddedArtworkUrl = "embed://media-artwork.svg";

    public Task<string> GetArtworkUrlAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(EmbeddedArtworkUrl);
    }
}
