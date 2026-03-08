using System.Collections.Concurrent;
using System.IO.Compression;
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
    private static readonly SubscriptionImportOptions ManualImportOptions = new(32, 200000, 4);
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
            return await ParseM3uPlaylistAsync(text, sourceUri, playlistName, options, progress, shouldAbort, cancellationToken);
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

    private async Task<IReadOnlyList<ImportStreamCandidate>> ParseM3uPlaylistAsync(
        string playlistText,
        Uri? playlistUri,
        string? defaultName,
        SubscriptionImportOptions options,
        IProgress<int>? progress,
        Func<bool>? shouldAbort,
        CancellationToken cancellationToken)
    {
        var workerCount = Math.Clamp(options.MaxConcurrentRequests, 1, 8);
        var queue = new ConcurrentQueue<PlaylistWorkItem>();
        var signal = new SemaphoreSlim(0);
        var visitedPlaylists = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var candidates = new ConcurrentDictionary<string, ImportStreamCandidate>(StringComparer.OrdinalIgnoreCase);
        var sawNestedPlaylist = 0;
        var pendingCount = 0;
        var stopRequested = 0;

        EnqueueWork(new PlaylistWorkItem(playlistUri, defaultName, 0, playlistText));

        var workers = Enumerable.Range(0, workerCount).Select(_ => ProcessQueueAsync()).ToArray();
        await Task.WhenAll(workers);

        if (candidates.IsEmpty && Volatile.Read(ref sawNestedPlaylist) == 0 && playlistUri is not null)
        {
            return [CreateCandidate(defaultName, playlistUri.AbsoluteUri, defaultName)];
        }

        return candidates.Values.ToList();

        void EnqueueWork(PlaylistWorkItem workItem)
        {
            if (ShouldStop())
            {
                return;
            }

            var key = workItem.PlaylistUri?.AbsoluteUri ?? $"inline::{workItem.DefaultName}::{workItem.Depth}::{workItem.InlineText?.GetHashCode()}";
            if (!visitedPlaylists.TryAdd(key, 0))
            {
                return;
            }

            queue.Enqueue(workItem);
            Interlocked.Increment(ref pendingCount);
            signal.Release();
        }

        async Task ProcessQueueAsync()
        {
            while (true)
            {
                await signal.WaitAsync(cancellationToken);

                if (!queue.TryDequeue(out var workItem))
                {
                    if (Volatile.Read(ref pendingCount) == 0)
                    {
                        break;
                    }

                    continue;
                }

                try
                {
                    if (!ShouldStop())
                    {
                        await ProcessWorkItemAsync(workItem);
                    }
                }
                finally
                {
                    if (Interlocked.Decrement(ref pendingCount) == 0)
                    {
                        signal.Release(workerCount);
                    }
                }
            }
        }

        async Task ProcessWorkItemAsync(PlaylistWorkItem workItem)
        {
            var text = workItem.InlineText;
            var playlistUriForText = workItem.PlaylistUri;

            if (text is null)
            {
                if (workItem.PlaylistUri is null || ShouldStop())
                {
                    return;
                }

                using var response = await _httpClient.GetAsync(workItem.PlaylistUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                var contentType = response.Content.Headers.ContentType?.MediaType;

                if (ShouldTreatAsDirectMedia(workItem.PlaylistUri.AbsoluteUri, contentType))
                {
                    AddCandidate(CreateCandidate(null, workItem.PlaylistUri.AbsoluteUri, workItem.DefaultName));
                    return;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                text = DecodeText(bytes);
                playlistUriForText = workItem.PlaylistUri;

                if (!LooksLikeM3u(text, workItem.PlaylistUri.AbsolutePath, contentType))
                {
                    foreach (var candidate in ParsePlainText(text, workItem.DefaultName))
                    {
                        AddCandidate(candidate);
                    }

                    return;
                }
            }

            foreach (var entry in EnumeratePlaylistEntries(text!, playlistUriForText))
            {
                if (ShouldStop())
                {
                    return;
                }

                if (entry.Kind == PlaylistEntryKind.Media)
                {
                    AddCandidate(CreateCandidate(null, entry.Url, workItem.DefaultName));
                    continue;
                }

                Interlocked.Exchange(ref sawNestedPlaylist, 1);
                if (workItem.Depth >= options.MaxPlaylistDepth)
                {
                    continue;
                }

                EnqueueWork(new PlaylistWorkItem(new Uri(entry.Url), workItem.DefaultName, workItem.Depth + 1, null));
            }
        }

        void AddCandidate(ImportStreamCandidate candidate)
        {
            if (ShouldStop())
            {
                return;
            }

            if (candidates.TryAdd(candidate.Url, candidate) && candidates.Count >= options.MaxStreamCount)
            {
                Interlocked.Exchange(ref stopRequested, 1);
            }

            progress?.Report(candidates.Count);
        }

        bool ShouldStop()
        {
            if (Volatile.Read(ref stopRequested) == 1)
            {
                return true;
            }

            if (shouldAbort?.Invoke() == true)
            {
                Interlocked.Exchange(ref stopRequested, 1);
                return true;
            }

            return false;
        }
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

    private static IEnumerable<PlaylistEntry> EnumeratePlaylistEntries(string playlistText, Uri? playlistUri)
    {
        var lines = playlistText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string? previousDirective = null;
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
                var resolvedEmbeddedUri = ResolveUri(playlistUri, embeddedUri ?? string.Empty);
                if (IsHttpAddress(resolvedEmbeddedUri))
                {
                    yield return new PlaylistEntry(PlaylistEntryKind.Playlist, resolvedEmbeddedUri!);
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
            yield return new PlaylistEntry(isPlaylistReference ? PlaylistEntryKind.Playlist : PlaylistEntryKind.Media, resolvedUri);

            previousDirective = null;
        }
    }

    private sealed record PlaylistWorkItem(Uri? PlaylistUri, string? DefaultName, int Depth, string? InlineText);

    private sealed record PlaylistEntry(PlaylistEntryKind Kind, string Url);

    private enum PlaylistEntryKind
    {
        Playlist,
        Media
    }
}
