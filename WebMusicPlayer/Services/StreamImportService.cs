using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using WebMusicPlayer.Models;

namespace WebMusicPlayer.Services;

public sealed class StreamImportService(HttpClient httpClient)
{
    private static readonly string[] PlaylistExtensions = [".m3u8", ".m3u"];
    private readonly HttpClient _httpClient = httpClient;

    public async Task<IReadOnlyList<ImportStreamCandidate>> ParseFromFileAsync(string fileName, Stream stream, CancellationToken cancellationToken = default)
    {
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);
        return await ParsePayloadAsync(fileName, memoryStream.ToArray(), null, null, cancellationToken);
    }

    public async Task<IReadOnlyList<ImportStreamCandidate>> ParseFromAddressAsync(string address, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("地址必须是 http 或 https。");
        }

        using var response = await _httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        return await ParsePayloadAsync(uri.AbsolutePath, bytes, uri, contentType, cancellationToken);
    }

    private async Task<IReadOnlyList<ImportStreamCandidate>> ParsePayloadAsync(
        string fileNameOrHint,
        byte[] data,
        Uri? sourceUri,
        string? contentType,
        CancellationToken cancellationToken)
    {
        if (LooksLikeZip(data, fileNameOrHint, contentType))
        {
            await using var zipStream = new MemoryStream(data);
            return await ParseZipAsync(zipStream, cancellationToken);
        }

        var text = DecodeText(data);
        if (LooksLikeXspf(text, fileNameOrHint, contentType))
        {
            return ParseXspf(text, sourceUri, fileNameOrHint);
        }

        if (LooksLikeM3u(text, fileNameOrHint, contentType))
        {
            var playlistName = Path.GetFileNameWithoutExtension(fileNameOrHint);
            return await ParseM3uPlaylistAsync(text, sourceUri, playlistName, new HashSet<string>(StringComparer.OrdinalIgnoreCase), cancellationToken);
        }

        return ParsePlainText(text, Path.GetFileNameWithoutExtension(fileNameOrHint));
    }

    private async Task<IReadOnlyList<ImportStreamCandidate>> ParseZipAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var results = new List<ImportStreamCandidate>();

        foreach (var entry in archive.Entries.Where(static x => !string.IsNullOrWhiteSpace(x.Name)))
        {
            await using var entryStream = entry.Open();
            var parsed = await ParseFromFileAsync(entry.Name, entryStream, cancellationToken);
            results.AddRange(parsed);
        }

        return DistinctCandidates(results);
    }

    private IReadOnlyList<ImportStreamCandidate> ParseXspf(string xmlText, Uri? sourceUri, string fileNameOrHint)
    {
        var document = XDocument.Parse(xmlText);
        XNamespace ns = "http://xspf.org/ns/0/";

        var streams = document
            .Descendants(ns + "track")
            .Select(track =>
            {
                var location = track.Element(ns + "location")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(location))
                {
                    return null;
                }

                var title = track.Element(ns + "title")?.Value?.Trim();
                var resolvedLocation = ResolveUri(sourceUri, location);
                if (string.IsNullOrWhiteSpace(resolvedLocation))
                {
                    return null;
                }

                return CreateCandidate(title, resolvedLocation, Path.GetFileNameWithoutExtension(fileNameOrHint));
            })
            .OfType<ImportStreamCandidate>()
            .ToList();

        return DistinctCandidates(streams);
    }

    private async Task<IReadOnlyList<ImportStreamCandidate>> ParseM3uPlaylistAsync(
        string playlistText,
        Uri? playlistUri,
        string? defaultName,
        HashSet<string> visitedPlaylists,
        CancellationToken cancellationToken)
    {
        var key = playlistUri?.AbsoluteUri ?? $"inline::{defaultName}::{playlistText.GetHashCode()}";
        if (!visitedPlaylists.Add(key))
        {
            return [];
        }

        var results = new List<ImportStreamCandidate>();
        var lines = playlistText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string? previousDirective = null;
        var sawNestedPlaylist = false;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("#EXT-X-MEDIA", StringComparison.OrdinalIgnoreCase))
            {
                var embeddedUri = ExtractQuotedAttribute(line, "URI");
                if (!string.IsNullOrWhiteSpace(embeddedUri))
                {
                    var nestedResults = await ResolvePlaylistReferenceAsync(embeddedUri, playlistUri, defaultName, visitedPlaylists, cancellationToken);
                    sawNestedPlaylist |= nestedResults.Count > 0;
                    results.AddRange(nestedResults);
                }

                previousDirective = line;
                continue;
            }

            if (line.StartsWith('#'))
            {
                previousDirective = line;
                continue;
            }

            var resolvedUri = ResolveUri(playlistUri, line);
            if (string.IsNullOrWhiteSpace(resolvedUri))
            {
                previousDirective = null;
                continue;
            }

            var isVariantReference = previousDirective?.StartsWith("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase) == true;
            var isPlaylistReference = isVariantReference || PlaylistExtensions.Any(extension => resolvedUri.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
            if (isPlaylistReference)
            {
                var nestedResults = await ResolvePlaylistReferenceAsync(resolvedUri, playlistUri, defaultName, visitedPlaylists, cancellationToken);
                sawNestedPlaylist |= nestedResults.Count > 0;
                results.AddRange(nestedResults);
            }
            else if (IsHttpAddress(resolvedUri))
            {
                results.Add(CreateCandidate(null, resolvedUri, defaultName));
            }

            previousDirective = null;
        }

        if (!results.Any() && !sawNestedPlaylist && playlistUri is not null)
        {
            results.Add(CreateCandidate(defaultName, playlistUri.AbsoluteUri, defaultName));
        }

        return DistinctCandidates(results);
    }

    private async Task<IReadOnlyList<ImportStreamCandidate>> ResolvePlaylistReferenceAsync(
        string reference,
        Uri? baseUri,
        string? defaultName,
        HashSet<string> visitedPlaylists,
        CancellationToken cancellationToken)
    {
        var resolvedUri = ResolveUri(baseUri, reference);
        if (!IsHttpAddress(resolvedUri))
        {
            return [];
        }

        var resolvedPlaylistUri = resolvedUri!;

        using var response = await _httpClient.GetAsync(resolvedPlaylistUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        var contentText = DecodeText(contentBytes);
        if (!LooksLikeM3u(contentText, resolvedPlaylistUri, contentType))
        {
            return [CreateCandidate(defaultName, resolvedPlaylistUri, defaultName)];
        }

        return await ParseM3uPlaylistAsync(contentText, new Uri(resolvedPlaylistUri), defaultName, visitedPlaylists, cancellationToken);
    }

    private static IReadOnlyList<ImportStreamCandidate> ParsePlainText(string text, string? defaultName)
    {
        var results = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsHttpAddress)
            .Select(line => CreateCandidate(null, line, defaultName))
            .ToList();

        return DistinctCandidates(results);
    }

    private static bool LooksLikeZip(byte[] data, string fileNameOrHint, string? contentType)
    {
        var hasZipSignature = data.Length >= 4 && data[0] == 'P' && data[1] == 'K';
        return hasZipSignature
            || fileNameOrHint.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "application/zip", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "application/x-zip-compressed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeXspf(string text, string fileNameOrHint, string? contentType)
    {
        return fileNameOrHint.EndsWith(".xspf", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "application/xspf+xml", StringComparison.OrdinalIgnoreCase)
            || text.Contains("<playlist", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeM3u(string text, string fileNameOrHint, string? contentType)
    {
        return PlaylistExtensions.Any(extension => fileNameOrHint.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            || string.Equals(contentType, "application/vnd.apple.mpegurl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "application/x-mpegURL", StringComparison.OrdinalIgnoreCase)
            || text.Contains("#EXTM3U", StringComparison.OrdinalIgnoreCase)
            || text.Contains("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase);
    }

    private static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static string? ResolveUri(Uri? baseUri, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        if (Uri.TryCreate(reference, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.AbsoluteUri;
        }

        if (baseUri is not null && Uri.TryCreate(baseUri, reference, out var relativeUri))
        {
            return relativeUri.AbsoluteUri;
        }

        return null;
    }

    private static ImportStreamCandidate CreateCandidate(string? preferredName, string url, string? fallbackName)
    {
        var normalizedName = !string.IsNullOrWhiteSpace(preferredName)
            ? preferredName.Trim()
            : !string.IsNullOrWhiteSpace(fallbackName)
                ? fallbackName.Trim()
                : GetNameFromUrl(url);

        return new ImportStreamCandidate(normalizedName, url.Trim());
    }

    private static string GetNameFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "未命名媒体流";
        }

        var tail = Path.GetFileName(uri.AbsolutePath.TrimEnd('/'));
        if (!string.IsNullOrWhiteSpace(tail))
        {
            return tail;
        }

        return uri.Host;
    }

    private static string? ExtractQuotedAttribute(string directive, string attributeName)
    {
        var token = attributeName + "=\"";
        var start = directive.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        start += token.Length;
        var end = directive.IndexOf('"', start);
        if (end < 0)
        {
            return null;
        }

        return directive[start..end];
    }

    private static bool IsHttpAddress(string? address)
    {
        return Uri.TryCreate(address, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static IReadOnlyList<ImportStreamCandidate> DistinctCandidates(IEnumerable<ImportStreamCandidate> candidates)
    {
        return candidates
            .Where(static candidate => IsHttpAddress(candidate.Url))
            .GroupBy(static candidate => candidate.Url, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();
    }
}
