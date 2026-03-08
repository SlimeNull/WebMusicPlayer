using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using System.Text;
using System.Xml.Linq;
using WebMusicPlayer.Localization;
using WebMusicPlayer.Models;

namespace WebMusicPlayer.Services;

public sealed class StreamImportService(HttpClient httpClient)
{
    private static readonly string[] PlaylistExtensions = [".m3u8", ".m3u"];
    private static readonly string[] DirectMediaContentTypePrefixes = ["audio/", "video/"];
    private static readonly string[] InvalidStreamNames = ["playlist", "stream", "listen", "live", "audio", "default", "mount", "radio", "hls", "aac", "mp3", "ogg", "opus"];
    private static readonly Regex BitrateOnlyNameRegex = new(@"^\d+\s*(kbps|k|mbps)(\s+[a-z0-9]+)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly SubscriptionImportOptions ManualImportOptions = new(200000);
    private readonly HttpClient _httpClient = httpClient;

    public async Task<IReadOnlyList<ImportStreamCandidate>> ResolveInvalidStreamNamesAsync(
        IReadOnlyList<ImportStreamCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        if (candidates.Count == 0)
        {
            return candidates;
        }

        var resolved = new ImportStreamCandidate[candidates.Count];
        var throttler = new SemaphoreSlim(4);
        var tasks = candidates.Select(async (candidate, index) =>
        {
            if (!IsInvalidStreamName(candidate.Name))
            {
                resolved[index] = candidate;
                return;
            }

            await throttler.WaitAsync(cancellationToken);
            try
            {
                var icyName = await TryGetIcyNameAsync(candidate.Url, cancellationToken);
                resolved[index] = !string.IsNullOrWhiteSpace(icyName) && !IsInvalidStreamName(icyName)
                    ? candidate with { Name = icyName.Trim() }
                    : candidate;
            }
            catch
            {
                resolved[index] = candidate;
            }
            finally
            {
                throttler.Release();
            }
        });

        await Task.WhenAll(tasks);
        return resolved;
    }

    public async Task<string?> TryGetIcyNameAsync(string address, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Icy-MetaData", "1");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (TryReadHeaderValue(response.Headers, "icy-name", out var icyName))
        {
            return icyName;
        }

        if (TryReadHeaderValue(response.Content.Headers, "icy-name", out icyName))
        {
            return icyName;
        }

        return null;
    }

    public async Task<IReadOnlyList<ImportStreamCandidate>> ParseFromFileAsync(string fileName, Stream stream, CancellationToken cancellationToken = default)
    {
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);
        return await ParsePayloadAsync(fileName, memoryStream.ToArray(), null, null, ManualImportOptions, progress: null, shouldAbort: null, cancellationToken);
    }

    public Task<IReadOnlyList<ImportStreamCandidate>> ParseFromAddressAsync(string address, CancellationToken cancellationToken = default)
        => ParseFromAddressAsync(address, SubscriptionImportOptions.Default, cancellationToken);

    public Task<IReadOnlyList<ImportStreamCandidate>> ParseFromAddressAsync(
        string address,
        SubscriptionImportOptions options,
        CancellationToken cancellationToken = default)
        => ParseFromAddressAsync(address, options, progress: null, shouldAbort: null, cancellationToken);

    public async Task<IReadOnlyList<ImportStreamCandidate>> ParseFromAddressAsync(
        string address,
        SubscriptionImportOptions options,
        IProgress<int>? progress,
        Func<bool>? shouldAbort,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(TranslateExtension.Get("ValidationHttpOrHttpsOnly"));
        }

        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (ShouldTreatAsDirectMedia(uri.AbsoluteUri, contentType))
        {
            progress?.Report(1);
            return [CreateCandidate(null, uri.AbsoluteUri, GetNameFromUrl(uri.AbsoluteUri))];
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return await ParsePayloadAsync(uri.AbsolutePath, bytes, uri, contentType, options, progress, shouldAbort, cancellationToken);
    }

    private async Task<IReadOnlyList<ImportStreamCandidate>> ParsePayloadAsync(
        string fileNameOrHint,
        byte[] data,
        Uri? sourceUri,
        string? contentType,
        SubscriptionImportOptions options,
        IProgress<int>? progress,
        Func<bool>? shouldAbort,
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
            return ParseM3uPlaylist(text, sourceUri, playlistName, options, progress, shouldAbort);
        }

        var directCandidates = ParsePlainText(text, Path.GetFileNameWithoutExtension(fileNameOrHint));
        progress?.Report(directCandidates.Count);
        return directCandidates;
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

    private static IReadOnlyList<ImportStreamCandidate> ParseM3uPlaylist(
        string playlistText,
        Uri? playlistUri,
        string? defaultName,
        SubscriptionImportOptions options,
        IProgress<int>? progress,
        Func<bool>? shouldAbort)
    {
        var candidates = new Dictionary<string, ImportStreamCandidate>(StringComparer.OrdinalIgnoreCase);
        var playlistName = TryExtractPlaylistName(playlistText) ?? defaultName;
        M3uEntryMetadata? pendingEntry = null;

        if (LooksLikeHlsMasterPlaylist(playlistText) && playlistUri is not null)
        {
            progress?.Report(1);
            return [CreateCandidate(defaultName, playlistUri.AbsoluteUri, playlistName)];
        }

        foreach (var rawLine in playlistText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (shouldAbort?.Invoke() == true || candidates.Count >= options.MaxStreamCount)
            {
                break;
            }

            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                pendingEntry = ParseM3uEntryMetadata(line, playlistUri);
                continue;
            }

            if (line.StartsWith('#'))
            {
                continue;
            }

            var resolvedUrl = ResolveUri(playlistUri, line);
            if (!IsHttpAddress(resolvedUrl))
            {
                pendingEntry = null;
                continue;
            }

            var candidate = CreateCandidate(
                pendingEntry?.Title,
                resolvedUrl!,
                playlistName,
                pendingEntry?.ArtworkUrl);

            if (!candidates.ContainsKey(candidate.Url))
            {
                candidates[candidate.Url] = candidate;
                progress?.Report(candidates.Count);
            }

            pendingEntry = null;
        }

        if (candidates.Count == 0 && playlistUri is not null)
        {
            progress?.Report(1);
            return [CreateCandidate(defaultName, playlistUri.AbsoluteUri, playlistName)];
        }

        return candidates.Values.ToList();
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
            || string.Equals(contentType, "audio/mpegurl", StringComparison.OrdinalIgnoreCase)
            || text.Contains("#EXTM3U", StringComparison.OrdinalIgnoreCase)
            || text.Contains("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldTreatAsDirectMedia(string url, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            if (DirectMediaContentTypePrefixes.Any(prefix => contentType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (IsStructuredContentType(contentType))
            {
                return false;
            }

            if (string.Equals(contentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase)
                || string.Equals(contentType, "application/ogg", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var extension = Path.GetExtension(url);
        return string.Equals(extension, ".aac", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".mp3", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".ogg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".flac", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".m4a", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStructuredContentType(string? contentType)
    {
        return string.Equals(contentType, "application/vnd.apple.mpegurl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "application/x-mpegURL", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "audio/mpegurl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "application/xspf+xml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "application/zip", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "application/x-zip-compressed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "text/plain", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "application/xml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "text/xml", StringComparison.OrdinalIgnoreCase);
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

    private static ImportStreamCandidate CreateCandidate(string? preferredName, string url, string? fallbackName, string? artworkUrl = null)
    {
        var normalizedName = !string.IsNullOrWhiteSpace(preferredName)
            ? WebUtility.HtmlDecode(preferredName.Trim())
            : !string.IsNullOrWhiteSpace(fallbackName)
                ? WebUtility.HtmlDecode(fallbackName.Trim())
                : GetNameFromUrl(url);

        return new ImportStreamCandidate(normalizedName, url.Trim(), string.IsNullOrWhiteSpace(artworkUrl) ? null : artworkUrl.Trim());
    }

    private static string GetNameFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return TranslateExtension.Get("UnnamedStream");
        }

        var tail = Path.GetFileName(uri.AbsolutePath.TrimEnd('/'));
        if (!string.IsNullOrWhiteSpace(tail))
        {
            return tail;
        }

        return uri.Host;
    }

    private static bool IsInvalidStreamName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        var trimmed = name.Trim();
        var normalized = Path.GetFileNameWithoutExtension(trimmed)
            .Trim()
            .Replace('_', ' ')
            .Replace('-', ' ')
            .ToLowerInvariant();

        if (BitrateOnlyNameRegex.IsMatch(normalized))
        {
            return true;
        }

        if (InvalidStreamNames.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalized.StartsWith("playlist", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("stream", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("listen", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadHeaderValue(System.Net.Http.Headers.HttpHeaders headers, string name, out string value)
    {
        if (headers.TryGetValues(name, out var values))
        {
            value = values.FirstOrDefault(static x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        value = string.Empty;
        return false;
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

    private static string? TryExtractPlaylistName(string playlistText)
    {
        foreach (var rawLine in playlistText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!rawLine.StartsWith("#PLAYLIST:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var title = rawLine["#PLAYLIST:".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(title))
            {
                return WebUtility.HtmlDecode(title);
            }
        }

        return null;
    }

    private static bool LooksLikeHlsMasterPlaylist(string playlistText)
    {
        return playlistText.Contains("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase)
            && !playlistText.Contains("#EXTINF:", StringComparison.OrdinalIgnoreCase);
    }

    private static M3uEntryMetadata ParseM3uEntryMetadata(string directive, Uri? playlistUri)
    {
        var colonIndex = directive.IndexOf(':');
        var payload = colonIndex >= 0 ? directive[(colonIndex + 1)..] : directive;
        var commaIndex = FindUnquotedCommaIndex(payload);
        var title = commaIndex >= 0 ? payload[(commaIndex + 1)..].Trim() : string.Empty;
        var artworkReference = ExtractQuotedAttribute(directive, "tvg-logo");
        var artworkUrl = ResolveUri(playlistUri, artworkReference ?? string.Empty);

        return new M3uEntryMetadata(
            string.IsNullOrWhiteSpace(title) ? null : WebUtility.HtmlDecode(title),
            IsHttpAddress(artworkUrl) ? artworkUrl : null);
    }

    private static int FindUnquotedCommaIndex(string text)
    {
        var insideQuotes = false;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '"')
            {
                insideQuotes = !insideQuotes;
                continue;
            }

            if (text[index] == ',' && !insideQuotes)
            {
                return index;
            }
        }

        return -1;
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
            .Select(static group => group
                .OrderByDescending(static candidate => !string.IsNullOrWhiteSpace(candidate.ArtworkUrl))
                .ThenByDescending(static candidate => !string.IsNullOrWhiteSpace(candidate.Name))
                .First())
            .ToList();
    }

    private sealed record M3uEntryMetadata(string? Title, string? ArtworkUrl);
}
