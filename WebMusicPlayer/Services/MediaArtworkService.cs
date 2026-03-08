using System.Text;

namespace WebMusicPlayer.Services;

public sealed class MediaArtworkService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cachedDataUrl;

    public async Task<string> GetArtworkDataUrlAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_cachedDataUrl))
        {
            return _cachedDataUrl;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_cachedDataUrl))
            {
                return _cachedDataUrl;
            }

            await using var stream = await FileSystem.OpenAppPackageFileAsync("media-artwork.svg");
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: false);
            var svg = await reader.ReadToEndAsync(cancellationToken);
            _cachedDataUrl = $"data:image/svg+xml;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))}";
            return _cachedDataUrl;
        }
        finally
        {
            _gate.Release();
        }
    }
}
