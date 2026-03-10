package com.silmenull.webmusicplayer.data

import androidx.core.text.HtmlCompat
import com.silmenull.webmusicplayer.models.ImportStreamCandidate
import com.silmenull.webmusicplayer.models.SubscriptionImportOptions
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit
import kotlinx.coroutines.withContext
import org.w3c.dom.Element
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.InputStream
import java.net.HttpURLConnection
import java.net.URI
import java.net.URL
import java.nio.charset.StandardCharsets
import java.util.Locale
import java.util.zip.ZipInputStream
import javax.xml.parsers.DocumentBuilderFactory

class StreamImportService {
    private val playlistExtensions = setOf(".m3u8", ".m3u")
    private val directMediaPrefixes = listOf("audio/", "video/")
    private val invalidStreamNames = setOf(
        "playlist", "stream", "listen", "live", "audio", "default", "mount", "radio", "hls", "aac", "mp3", "ogg", "opus"
    )
    private val bitrateOnlyNameRegex = Regex("^\\d+\\s*(kbps|k|mbps)(\\s+[a-z0-9]+)?$", RegexOption.IGNORE_CASE)
    private val manualImportOptions = SubscriptionImportOptions(maxStreamCount = 200000)

    suspend fun resolveInvalidStreamNames(
        candidates: List<ImportStreamCandidate>,
    ): List<ImportStreamCandidate> = coroutineScope {
        if (candidates.isEmpty()) {
            return@coroutineScope candidates
        }

        val semaphore = Semaphore(4)
        candidates.map { candidate ->
            async {
                if (!isInvalidStreamName(candidate.name)) {
                    return@async candidate
                }

                semaphore.withPermit {
                    val icyName = runCatching { tryGetIcyName(candidate.url) }.getOrNull()
                    if (!icyName.isNullOrBlank() && !isInvalidStreamName(icyName)) {
                        candidate.copy(name = icyName.trim())
                    } else {
                        candidate
                    }
                }
            }
        }.awaitAll()
    }

    suspend fun tryGetIcyName(address: String): String? = withContext(Dispatchers.IO) {
        val url = validateHttpUrl(address)
        val connection = (URL(url).openConnection() as HttpURLConnection).apply {
            requestMethod = "GET"
            connectTimeout = 15_000
            readTimeout = 15_000
            setRequestProperty("Icy-MetaData", "1")
            instanceFollowRedirects = true
        }

        try {
            connection.connect()
            val headerValue = connection.headerFields.entries.firstOrNull { entry ->
                entry.key?.equals("icy-name", ignoreCase = true) == true
            }?.value?.firstOrNull()?.trim()
            headerValue?.takeIf { it.isNotBlank() }
        } finally {
            connection.inputStream?.closeQuietly()
            connection.errorStream?.closeQuietly()
            connection.disconnect()
        }
    }

    suspend fun parseFromFile(
        fileName: String,
        inputStream: InputStream,
    ): List<ImportStreamCandidate> = withContext(Dispatchers.IO) {
        val bytes = inputStream.readBytes()
        parsePayload(
            fileNameOrHint = fileName,
            data = bytes,
            sourceUrl = null,
            contentType = null,
            options = manualImportOptions,
            progress = null,
            shouldAbort = null,
        )
    }

    suspend fun parseFromAddress(
        address: String,
        options: SubscriptionImportOptions = SubscriptionImportOptions(),
        progress: ((Int) -> Unit)? = null,
        shouldAbort: (() -> Boolean)? = null,
    ): List<ImportStreamCandidate> = withContext(Dispatchers.IO) {
        currentCoroutineContext().ensureActive()
        val url = validateHttpUrl(address)
        val connection = (URL(url).openConnection() as HttpURLConnection).apply {
            requestMethod = "GET"
            connectTimeout = 20_000
            readTimeout = 20_000
            instanceFollowRedirects = true
        }

        try {
            connection.connect()
            val contentType = connection.contentType?.substringBefore(';')?.trim()
            val resolvedUrl = connection.url?.toString() ?: url
            if (shouldTreatAsDirectMedia(resolvedUrl, contentType)) {
                progress?.invoke(1)
                return@withContext listOf(createCandidate(null, resolvedUrl, getNameFromUrl(resolvedUrl)))
            }

            val bytes = connection.inputStream.use { it.readBytes() }
            parsePayload(
                fileNameOrHint = URL(resolvedUrl).path.ifBlank { resolvedUrl },
                data = bytes,
                sourceUrl = resolvedUrl,
                contentType = contentType,
                options = options,
                progress = progress,
                shouldAbort = shouldAbort,
            )
        } finally {
            connection.errorStream?.closeQuietly()
            connection.disconnect()
        }
    }

    private suspend fun parsePayload(
        fileNameOrHint: String,
        data: ByteArray,
        sourceUrl: String?,
        contentType: String?,
        options: SubscriptionImportOptions,
        progress: ((Int) -> Unit)?,
        shouldAbort: (() -> Boolean)?,
    ): List<ImportStreamCandidate> {
        currentCoroutineContext().ensureActive()

        if (looksLikeZip(data, fileNameOrHint, contentType)) {
            return parseZip(data)
        }

        val text = decodeText(data)
        if (looksLikeXspf(text, fileNameOrHint, contentType)) {
            return parseXspf(text, sourceUrl, fileNameOrHint)
        }

        if (looksLikeM3u(text, fileNameOrHint, contentType)) {
            val playlistName = fileNameOrHint.substringAfterLast('/').substringAfterLast('\\').substringBeforeLast('.')
            return parseM3uPlaylist(text, sourceUrl, playlistName, options, progress, shouldAbort)
        }

        val directCandidates = parsePlainText(text, fileNameOrHint.substringAfterLast('/').substringAfterLast('\\').substringBeforeLast('.'))
        progress?.invoke(directCandidates.size)
        return directCandidates
    }

    private suspend fun parseZip(data: ByteArray): List<ImportStreamCandidate> = withContext(Dispatchers.IO) {
        val results = mutableListOf<ImportStreamCandidate>()
        ZipInputStream(ByteArrayInputStream(data)).use { zipStream ->
            while (true) {
                currentCoroutineContext().ensureActive()
                val entry = zipStream.nextEntry ?: break
                if (entry.isDirectory || entry.name.isBlank()) {
                    zipStream.closeEntry()
                    continue
                }

                val bytes = zipStream.readCurrentEntryBytes()
                results += parsePayload(
                    fileNameOrHint = entry.name,
                    data = bytes,
                    sourceUrl = null,
                    contentType = null,
                    options = manualImportOptions,
                    progress = null,
                    shouldAbort = null,
                )
                zipStream.closeEntry()
            }
        }
        distinctCandidates(results)
    }

    private fun parseXspf(
        xmlText: String,
        sourceUrl: String?,
        fileNameOrHint: String,
    ): List<ImportStreamCandidate> {
        val builder = DocumentBuilderFactory.newInstance().apply {
            isNamespaceAware = true
        }.newDocumentBuilder()
        val document = builder.parse(ByteArrayInputStream(xmlText.toByteArray(StandardCharsets.UTF_8)))
        val tracks = document.getElementsByTagNameNS("*", "track")
        val items = mutableListOf<ImportStreamCandidate>()

        for (index in 0 until tracks.length) {
            val track = tracks.item(index) as? Element ?: continue
            val location = track.getElementsByTagNameNS("*", "location").item(0)?.textContent?.trim().orEmpty()
            if (location.isBlank()) {
                continue
            }

            val title = track.getElementsByTagNameNS("*", "title").item(0)?.textContent?.trim()
            val resolved = resolveUri(sourceUrl, location) ?: continue
            items += createCandidate(title, resolved, fileNameOrHint.substringAfterLast('/').substringAfterLast('\\').substringBeforeLast('.'))
        }

        return distinctCandidates(items)
    }

    private suspend fun parseM3uPlaylist(
        playlistText: String,
        playlistUrl: String?,
        defaultName: String?,
        options: SubscriptionImportOptions,
        progress: ((Int) -> Unit)?,
        shouldAbort: (() -> Boolean)?,
    ): List<ImportStreamCandidate> {
        currentCoroutineContext().ensureActive()
        val candidates = linkedMapOf<String, ImportStreamCandidate>()
        val playlistName = tryExtractPlaylistName(playlistText) ?: defaultName
        var pendingEntry: M3uEntryMetadata? = null

        if (looksLikeHlsMasterPlaylist(playlistText) && playlistUrl != null) {
            progress?.invoke(1)
            return listOf(createCandidate(defaultName, playlistUrl, playlistName))
        }

        playlistText.lineSequence()
            .map { it.trim() }
            .filter { it.isNotBlank() }
            .forEach { line ->
                if (shouldAbort?.invoke() == true || candidates.size >= options.maxStreamCount) {
                    return@forEach
                }

                if (line.startsWith("#EXTINF", ignoreCase = true)) {
                    pendingEntry = parseM3uEntryMetadata(line, playlistUrl)
                    return@forEach
                }

                if (line.startsWith('#')) {
                    return@forEach
                }

                val resolvedUrl = resolveUri(playlistUrl, line)
                if (!isHttpAddress(resolvedUrl)) {
                    pendingEntry = null
                    return@forEach
                }

                val candidate = createCandidate(
                    preferredName = pendingEntry?.title,
                    url = resolvedUrl!!,
                    fallbackName = playlistName,
                    artworkUrl = pendingEntry?.artworkUrl,
                )
                if (!candidates.containsKey(candidate.url)) {
                    candidates[candidate.url] = candidate
                    progress?.invoke(candidates.size)
                }
                pendingEntry = null
            }

        if (candidates.isEmpty() && playlistUrl != null) {
            progress?.invoke(1)
            return listOf(createCandidate(defaultName, playlistUrl, playlistName))
        }

        return candidates.values.toList()
    }

    private fun parsePlainText(text: String, defaultName: String?): List<ImportStreamCandidate> {
        val results = text.lineSequence()
            .map { it.trim() }
            .filter { isHttpAddress(it) }
            .map { line -> createCandidate(null, line, defaultName) }
            .toList()
        return distinctCandidates(results)
    }

    private fun looksLikeZip(data: ByteArray, fileNameOrHint: String, contentType: String?): Boolean {
        val hasZipSignature = data.size >= 4 && data[0] == 'P'.code.toByte() && data[1] == 'K'.code.toByte()
        return hasZipSignature ||
            fileNameOrHint.endsWith(".zip", ignoreCase = true) ||
            contentType.equals("application/zip", ignoreCase = true) ||
            contentType.equals("application/x-zip-compressed", ignoreCase = true)
    }

    private fun looksLikeXspf(text: String, fileNameOrHint: String, contentType: String?): Boolean {
        return fileNameOrHint.endsWith(".xspf", ignoreCase = true) ||
            contentType.equals("application/xspf+xml", ignoreCase = true) ||
            text.contains("<playlist", ignoreCase = true)
    }

    private fun looksLikeM3u(text: String, fileNameOrHint: String, contentType: String?): Boolean {
        return playlistExtensions.any { fileNameOrHint.endsWith(it, ignoreCase = true) } ||
            contentType.equals("application/vnd.apple.mpegurl", ignoreCase = true) ||
            contentType.equals("application/x-mpegURL", ignoreCase = true) ||
            contentType.equals("audio/mpegurl", ignoreCase = true) ||
            text.contains("#EXTM3U", ignoreCase = true) ||
            text.contains("#EXT-X-STREAM-INF", ignoreCase = true)
    }

    private fun shouldTreatAsDirectMedia(url: String, contentType: String?): Boolean {
        if (!contentType.isNullOrBlank()) {
            if (directMediaPrefixes.any { prefix -> contentType.startsWith(prefix, ignoreCase = true) }) {
                return true
            }
            if (isStructuredContentType(contentType)) {
                return false
            }
            if (contentType.equals("application/octet-stream", ignoreCase = true) ||
                contentType.equals("application/ogg", ignoreCase = true)
            ) {
                return true
            }
        }

        val extension = url.substringAfterLast('.', missingDelimiterValue = "").lowercase(Locale.ROOT)
        return extension in setOf("aac", "mp3", "ogg", "wav", "flac", "m4a")
    }

    private fun isStructuredContentType(contentType: String): Boolean {
        return contentType.equals("application/vnd.apple.mpegurl", ignoreCase = true) ||
            contentType.equals("application/x-mpegURL", ignoreCase = true) ||
            contentType.equals("audio/mpegurl", ignoreCase = true) ||
            contentType.equals("application/xspf+xml", ignoreCase = true) ||
            contentType.equals("application/zip", ignoreCase = true) ||
            contentType.equals("application/x-zip-compressed", ignoreCase = true) ||
            contentType.equals("text/plain", ignoreCase = true) ||
            contentType.equals("application/xml", ignoreCase = true) ||
            contentType.equals("text/xml", ignoreCase = true)
    }

    private fun decodeText(bytes: ByteArray): String {
        return if (bytes.size >= 3 && bytes[0] == 0xEF.toByte() && bytes[1] == 0xBB.toByte() && bytes[2] == 0xBF.toByte()) {
            bytes.copyOfRange(3, bytes.size).toString(StandardCharsets.UTF_8)
        } else {
            bytes.toString(StandardCharsets.UTF_8)
        }
    }

    private fun resolveUri(baseUri: String?, reference: String): String? {
        if (reference.isBlank()) {
            return null
        }

        return runCatching { URI(reference).toURL().toString() }.getOrNull()
            ?: runCatching {
                if (baseUri.isNullOrBlank()) {
                    null
                } else {
                    URI(baseUri).resolve(reference).toURL().toString()
                }
            }.getOrNull()
    }

    private fun createCandidate(
        preferredName: String?,
        url: String,
        fallbackName: String?,
        artworkUrl: String? = null,
    ): ImportStreamCandidate {
        val normalizedName = when {
            !preferredName.isNullOrBlank() -> decodeHtml(preferredName.trim())
            !fallbackName.isNullOrBlank() -> decodeHtml(fallbackName.trim())
            else -> getNameFromUrl(url)
        }
        return ImportStreamCandidate(
            name = normalizedName,
            url = url.trim(),
            artworkUrl = artworkUrl?.trim()?.takeIf { it.isNotBlank() },
        )
    }

    private fun getNameFromUrl(url: String): String {
        return runCatching {
            val uri = URI(url)
            uri.path.trimEnd('/').substringAfterLast('/').ifBlank { uri.host ?: "Unnamed stream" }
        }.getOrElse { "Unnamed stream" }
    }

    private fun isInvalidStreamName(name: String?): Boolean {
        if (name.isNullOrBlank()) {
            return true
        }

        val normalized = name.substringBeforeLast('.')
            .trim()
            .replace('_', ' ')
            .replace('-', ' ')
            .lowercase(Locale.ROOT)

        if (bitrateOnlyNameRegex.matches(normalized)) {
            return true
        }

        if (normalized in invalidStreamNames) {
            return true
        }

        return normalized.startsWith("playlist") || normalized.startsWith("stream") || normalized.startsWith("listen")
    }

    private fun tryExtractPlaylistName(playlistText: String): String? {
        return playlistText.lineSequence()
            .firstOrNull { it.startsWith("#PLAYLIST:", ignoreCase = true) }
            ?.substringAfter(':')
            ?.trim()
            ?.takeIf { it.isNotBlank() }
            ?.let(::decodeHtml)
    }

    private fun looksLikeHlsMasterPlaylist(playlistText: String): Boolean {
        return playlistText.contains("#EXT-X-STREAM-INF", ignoreCase = true) &&
            !playlistText.contains("#EXTINF:", ignoreCase = true)
    }

    private fun parseM3uEntryMetadata(directive: String, playlistUrl: String?): M3uEntryMetadata {
        val payload = directive.substringAfter(':', directive)
        val commaIndex = findUnquotedCommaIndex(payload)
        val title = if (commaIndex >= 0) payload.substring(commaIndex + 1).trim() else ""
        val artworkReference = extractQuotedAttribute(directive, "tvg-logo")
        val artworkUrl = resolveUri(playlistUrl, artworkReference.orEmpty())
        return M3uEntryMetadata(
            title = title.takeIf { it.isNotBlank() }?.let(::decodeHtml),
            artworkUrl = artworkUrl?.takeIf(::isHttpAddress),
        )
    }

    private fun extractQuotedAttribute(directive: String, attributeName: String): String? {
        val token = "$attributeName=\""
        val start = directive.indexOf(token, ignoreCase = true)
        if (start < 0) {
            return null
        }

        val valueStart = start + token.length
        val valueEnd = directive.indexOf('"', startIndex = valueStart)
        return if (valueEnd > valueStart) directive.substring(valueStart, valueEnd) else null
    }

    private fun findUnquotedCommaIndex(text: String): Int {
        var insideQuotes = false
        text.forEachIndexed { index, char ->
            when {
                char == '"' -> insideQuotes = !insideQuotes
                char == ',' && !insideQuotes -> return index
            }
        }
        return -1
    }

    private fun isHttpAddress(address: String?): Boolean {
        if (address.isNullOrBlank()) {
            return false
        }

        return runCatching {
            val uri = URI(address)
            uri.scheme.equals("http", ignoreCase = true) || uri.scheme.equals("https", ignoreCase = true)
        }.getOrDefault(false)
    }

    private fun distinctCandidates(candidates: List<ImportStreamCandidate>): List<ImportStreamCandidate> {
        return candidates
            .filter { isHttpAddress(it.url) }
            .groupBy { it.url.lowercase(Locale.ROOT) }
            .values
            .map { group ->
                group.sortedWith(
                    compareByDescending<ImportStreamCandidate> { !it.artworkUrl.isNullOrBlank() }
                        .thenByDescending { it.name.isNotBlank() }
                ).first()
            }
    }

    private fun decodeHtml(value: String): String {
        return HtmlCompat.fromHtml(value, HtmlCompat.FROM_HTML_MODE_LEGACY).toString()
    }

    private fun validateHttpUrl(address: String): String {
        val trimmed = address.trim()
        check(isHttpAddress(trimmed)) { "The address must use http or https." }
        return URI(trimmed).toString()
    }

    private fun InputStream.closeQuietly() {
        runCatching { close() }
    }

    private fun ZipInputStream.readCurrentEntryBytes(): ByteArray {
        val output = ByteArrayOutputStream()
        val buffer = ByteArray(DEFAULT_BUFFER_SIZE)
        while (true) {
            val read = read(buffer)
            if (read <= 0) {
                break
            }
            output.write(buffer, 0, read)
        }
        return output.toByteArray()
    }

    private data class M3uEntryMetadata(
        val title: String?,
        val artworkUrl: String?,
    )
}
