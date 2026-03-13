package com.silmenull.webmusicplayer.viewmodel

import android.app.Application
import android.database.Cursor
import android.net.Uri
import android.provider.OpenableColumns
import androidx.annotation.StringRes
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import androidx.media3.common.MediaItem
import androidx.media3.common.MediaMetadata
import androidx.media3.common.PlaybackException
import androidx.media3.common.Player
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.session.MediaSession
import com.silmenull.webmusicplayer.R
import com.silmenull.webmusicplayer.data.AppStateStore
import com.silmenull.webmusicplayer.data.StreamImportService
import com.silmenull.webmusicplayer.models.AppState
import com.silmenull.webmusicplayer.models.AppTab
import com.silmenull.webmusicplayer.models.FilterOption
import com.silmenull.webmusicplayer.models.ImportStreamCandidate
import com.silmenull.webmusicplayer.models.StreamItem
import com.silmenull.webmusicplayer.models.StreamOriginKind
import com.silmenull.webmusicplayer.models.SubscriptionImportOptions
import com.silmenull.webmusicplayer.models.SubscriptionItem
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.isActive
import kotlinx.coroutines.withContext
import java.net.URI
import java.nio.file.Path
import java.time.Instant
import java.util.Locale

class MainViewModel(application: Application) : AndroidViewModel(application) {
    private val appStateStore = AppStateStore(application)
    private val streamImportService = StreamImportService()
    private val player = ExoPlayer.Builder(application).build()
    private val session = MediaSession.Builder(application, player).build()
    private val eventMessages = MutableSharedFlow<String>()
    private val uiStateFlow = MutableStateFlow(
        MainUiState(
            busyText = text(R.string.busy_please_wait),
        )
    )

    private var appState = AppState()
    private var isInitialized = false
    private var busyBaseText = text(R.string.busy_please_wait)
    private var busyJob: Job? = null
    private val subscriptionProgressById = linkedMapOf<String, Int>()
    @Volatile
    private var busyOperationAbortRequested = false

    val uiState: StateFlow<MainUiState> = uiStateFlow.asStateFlow()
    val events: SharedFlow<String> = eventMessages.asSharedFlow()

    init {
        player.addListener(
            object : Player.Listener {
                override fun onIsPlayingChanged(isPlaying: Boolean) {
                    uiStateFlow.update { it.copy(isPlaying = isPlaying) }
                }

                override fun onPlaybackStateChanged(playbackState: Int) {
                    val isLoading = playbackState == Player.STATE_BUFFERING
                    val shouldStop = playbackState == Player.STATE_ENDED
                    uiStateFlow.update {
                        it.copy(
                            isLoading = isLoading,
                            isPlaying = when (playbackState) {
                                Player.STATE_READY -> player.isPlaying
                                Player.STATE_ENDED,
                                Player.STATE_IDLE -> false
                                else -> it.isPlaying
                            },
                        )
                    }
                    if (shouldStop) {
                        uiStateFlow.update { it.copy(isPlaying = false, isLoading = false) }
                    }
                }

                override fun onPlayerError(error: PlaybackException) {
                    uiStateFlow.update { it.copy(isPlaying = false, isLoading = false) }
                    viewModelScope.launch {
                        eventMessages.emit(error.localizedMessage ?: text(R.string.playback_failed_message))
                    }
                }
            }
        )
    }

    suspend fun initialize() {
        if (isInitialized) {
            return
        }

        appState = appStateStore.load().normalize()
        publishState()
        isInitialized = true
    }

    fun selectTab(tab: AppTab) {
        uiStateFlow.update { it.copy(selectedTab = tab) }
    }

    fun playStream(streamId: String) {
        val stream = appState.streams.firstOrNull { it.id == streamId } ?: return
        uiStateFlow.update { it.copy(currentStreamId = streamId, isLoading = true) }
        val metadataBuilder = MediaMetadata.Builder().setTitle(stream.name)
        stream.artworkUrl?.takeIf { it.isNotBlank() }?.let { artwork ->
            runCatching { metadataBuilder.setArtworkUri(Uri.parse(artwork)) }
        }
        player.setMediaItem(
            MediaItem.Builder()
                .setUri(stream.url)
                .setMediaMetadata(metadataBuilder.build())
                .build()
        )
        player.prepare()
        player.playWhenReady = true
    }

    fun togglePlayback() {
        val currentStream = currentStream() ?: return
        if (uiStateFlow.value.isPlaying) {
            player.stop()
            uiStateFlow.update { it.copy(isPlaying = false, isLoading = false) }
            return
        }

        playStream(currentStream.id)
    }

    suspend fun applyFilter(key: String, keyword: String?) {
        appState = appState.copy(
            selectedFilterKey = key.ifBlank { FilterOption.ALL_KEY },
            selectedFilterKeyword = keyword.orEmpty().trim(),
        )
        publishState()
        saveState()
    }

    suspend fun addManualStream(name: String, address: String, artworkUrl: String?) {
        val normalizedUrl = validateHttpAddress(address)
        val normalizedName = normalizeName(name, normalizedUrl)
        val normalizedArtworkUrl = validateOptionalHttpAddress(artworkUrl)
        if (containsStream(normalizedUrl)) {
            return
        }

        appState = appState.copy(
            streams = appState.streams + StreamItem(
                name = normalizedName,
                url = normalizedUrl,
                artworkUrl = normalizedArtworkUrl,
                originKind = StreamOriginKind.MANUAL,
                isFavourite = false,
            )
        )
        publishState()
        saveState()
    }

    suspend fun importManualFile(uri: Uri) {
        runBusy(text(R.string.busy_importing_streams)) {
            val resolver = getApplication<Application>().contentResolver
            val displayName = resolver.queryDisplayName(uri) ?: "imported-file"
            val candidates = withContext(Dispatchers.IO) {
                resolver.openInputStream(uri)?.use { input ->
                    streamImportService.parseFromFile(displayName, input)
                }
            } ?: emptyList()
            mergeImportedStreams(candidates, StreamOriginKind.MANUAL, null, null)
            publishState()
            saveState()
        }
    }

    suspend fun addSubscription(name: String, address: String, maxStreamCount: Int) {
        val normalizedUrl = validateHttpAddress(address)
        val normalizedName = normalizeName(name, normalizedUrl)
        val normalizedCount = validateMaxStreamCount(maxStreamCount)
        val existing = appState.subscriptions.firstOrNull { it.url.equals(normalizedUrl, ignoreCase = true) }
        if (existing != null) {
            editSubscription(existing.id, normalizedName, normalizedUrl, normalizedCount)
            return
        }

        val item = SubscriptionItem(
            name = normalizedName,
            url = normalizedUrl,
            maxStreamCount = normalizedCount,
        )
        appState = appState.copy(subscriptions = appState.subscriptions + item)
        publishState()
        saveState()
        refreshSubscription(item.id)
    }

    suspend fun editSubscription(subscriptionId: String, name: String, address: String, maxStreamCount: Int) {
        val normalizedUrl = validateHttpAddress(address)
        val normalizedName = normalizeName(name, normalizedUrl)
        val normalizedCount = validateMaxStreamCount(maxStreamCount)
        appState = appState.copy(
            subscriptions = appState.subscriptions.map { subscription ->
                if (subscription.id == subscriptionId) {
                    subscription.copy(
                        name = normalizedName,
                        url = normalizedUrl,
                        maxStreamCount = normalizedCount,
                    )
                } else {
                    subscription
                }
            }
        )
        publishState()
        saveState()
        refreshSubscription(subscriptionId)
    }

    suspend fun updateAllSubscriptions() {
        val subscriptions = appState.subscriptions
        if (subscriptions.isEmpty()) {
            return
        }

        val tokenJob = beginSubscriptionBusyOperation(text(R.string.busy_updating_subscriptions))
        try {
            val failures = mutableListOf<Throwable>()
            val snapshots = mutableListOf<Pair<String, List<ImportStreamCandidate>>>()
            subscriptions.forEach { subscription ->
                if (!tokenJob.isActive) {
                    return@forEach
                }
                if (busyOperationAbortRequested) {
                    return@forEach
                }

                runCatching {
                    fetchSubscriptionSnapshot(subscription)
                }.onSuccess { candidates ->
                    snapshots += subscription.id to candidates
                }.onFailure { error ->
                    if (error !is CancellationException) {
                        failures += error
                    } else {
                        throw error
                    }
                }
            }

            snapshots.forEach { (subscriptionId, candidates) ->
                applySubscriptionSnapshot(subscriptionId, candidates)
            }
            publishState()
            saveState()

            if (failures.isNotEmpty()) {
                throw IllegalStateException(
                    text(
                        R.string.busy_update_failures_format,
                        failures.size,
                        failures.first().message ?: text(R.string.update_failed_title),
                    )
                )
            }
        } finally {
            endBusyOperation()
        }
    }

    suspend fun deleteSubscription(subscriptionId: String) {
        appState = appState.copy(
            subscriptions = appState.subscriptions.filterNot { it.id == subscriptionId },
            streams = appState.streams.filterNot { it.subscriptionId == subscriptionId },
        )
        if (uiStateFlow.value.currentStreamId != null && appState.streams.none { it.id == uiStateFlow.value.currentStreamId }) {
            stopAndClearCurrentStream()
        }
        normalizeSelectedFilterIfNeeded()
        publishState()
        saveState()
    }

    suspend fun deleteStream(streamId: String, suppressReminder: Boolean) {
        if (suppressReminder) {
            appState = appState.copy(deletePromptSuppressedUntilUtcMillis = Instant.now().plusSeconds(5 * 60L).toEpochMilli())
        }
        val stream = appState.streams.firstOrNull { it.id == streamId }
        appState = appState.copy(streams = appState.streams.filterNot { it.id == streamId })
        if (stream != null && uiStateFlow.value.currentStreamId == stream.id) {
            stopAndClearCurrentStream()
        }
        publishState()
        saveState()
    }

    suspend fun setFavourite(streamId: String, isFavourite: Boolean) {
        appState = appState.copy(
            streams = appState.streams.map { stream ->
                if (stream.id == streamId) stream.copy(isFavourite = isFavourite) else stream
            }
        )
        publishState()
        saveState()
    }

    suspend fun toggleFavourite(streamId: String) {
        val stream = appState.streams.firstOrNull { it.id == streamId } ?: return
        setFavourite(streamId, !stream.isFavourite)
    }

    suspend fun archiveFavourite(streamId: String) {
        setFavourite(streamId, false)
    }

    fun abortBusyOperation() {
        if (!uiStateFlow.value.canAbortBusyOperation) {
            return
        }
        busyOperationAbortRequested = true
        uiStateFlow.update {
            it.copy(
                canAbortBusyOperation = false,
                busyText = text(R.string.busy_stopping_parsed_format, busyBaseText, resolvedMediaCount()),
            )
        }
    }

    fun cancelBusyOperation() {
        if (!uiStateFlow.value.canCancelBusyOperation) {
            return
        }
        uiStateFlow.update {
            it.copy(
                canAbortBusyOperation = false,
                canCancelBusyOperation = false,
                busyText = text(R.string.busy_cancelling),
            )
        }
        busyJob?.cancel()
    }

    fun shouldConfirmDelete(): Boolean {
        val suppressedUntil = appState.deletePromptSuppressedUntilUtcMillis ?: return true
        return suppressedUntil <= System.currentTimeMillis()
    }

    fun getStream(streamId: String?): StreamItem? = appState.streams.firstOrNull { it.id == streamId }

    fun getSubscription(subscriptionId: String?): SubscriptionItem? = appState.subscriptions.firstOrNull { it.id == subscriptionId }

    private suspend fun refreshSubscription(subscriptionId: String) {
        val subscription = appState.subscriptions.firstOrNull { it.id == subscriptionId } ?: return
        beginSubscriptionBusyOperation(text(R.string.busy_updating_subscription_format, subscription.name))
        try {
            val candidates = fetchSubscriptionSnapshot(subscription)
            if (busyJob?.isCancelled == true) {
                return
            }
            if (!busyOperationAbortRequested) {
                applySubscriptionSnapshot(subscriptionId, candidates)
                publishState()
                saveState()
            }
        } finally {
            endBusyOperation()
        }
    }

    private suspend fun fetchSubscriptionSnapshot(subscription: SubscriptionItem): List<ImportStreamCandidate> {
        val candidates = streamImportService.parseFromAddress(
            address = subscription.url,
            options = SubscriptionImportOptions(subscription.maxStreamCount),
            progress = { count -> reportSubscriptionProgress(subscription.id, count) },
            shouldAbort = { busyOperationAbortRequested },
        )
        return streamImportService.resolveInvalidStreamNames(candidates)
    }

    private fun applySubscriptionSnapshot(subscriptionId: String, candidates: List<ImportStreamCandidate>) {
        val subscription = appState.subscriptions.firstOrNull { it.id == subscriptionId } ?: return
        val withoutSubscriptionStreams = appState.streams.filterNot { it.subscriptionId == subscriptionId }
        appState = appState.copy(
            streams = withoutSubscriptionStreams + candidates
                .filterNot { candidate -> withoutSubscriptionStreams.any { it.url.equals(candidate.url, ignoreCase = true) } }
                .map { candidate ->
                    StreamItem(
                        name = normalizeName(candidate.name, candidate.url),
                        url = candidate.url,
                        artworkUrl = candidate.artworkUrl,
                        originKind = StreamOriginKind.SUBSCRIPTION,
                        subscriptionId = subscription.id,
                        subscriptionName = subscription.name,
                        isFavourite = false,
                    )
                },
            subscriptions = appState.subscriptions.map {
                if (it.id == subscriptionId) it.copy(lastUpdatedUtcMillis = System.currentTimeMillis()) else it
            }
        )
        normalizeSelectedFilterIfNeeded()
    }

    private fun mergeImportedStreams(
        candidates: List<ImportStreamCandidate>,
        originKind: StreamOriginKind,
        subscriptionId: String?,
        subscriptionName: String?,
    ) {
        val merged = appState.streams.toMutableList()
        candidates.forEach { candidate ->
            val existingIndex = merged.indexOfFirst { it.url.equals(candidate.url, ignoreCase = true) }
            if (existingIndex >= 0) {
                val existing = merged[existingIndex]
                if (existing.artworkUrl.isNullOrBlank() && !candidate.artworkUrl.isNullOrBlank()) {
                    merged[existingIndex] = existing.copy(artworkUrl = candidate.artworkUrl)
                }
            } else {
                merged += StreamItem(
                    name = normalizeName(candidate.name, candidate.url),
                    url = candidate.url,
                    artworkUrl = candidate.artworkUrl,
                    originKind = originKind,
                    subscriptionId = subscriptionId,
                    subscriptionName = subscriptionName,
                    isFavourite = false,
                )
            }
        }
        appState = appState.copy(streams = merged)
    }

    private fun publishState() {
        val filterKey = appState.selectedFilterKey.ifBlank { FilterOption.ALL_KEY }
        val keyword = appState.selectedFilterKeyword.trim()
        val visible = appState.streams
            .filter { matchesSelectedFilter(it, filterKey, keyword) }
            .sortedByName()
        val favourites = appState.streams
            .filter { it.isFavourite }
            .sortedByName()

        uiStateFlow.update {
            it.copy(
                allStreams = appState.streams.sortedByName(),
                visibleStreams = visible,
                favouriteStreams = favourites,
                subscriptions = appState.subscriptions.sortedByName { subscription -> subscription.name },
                selectedFilterKey = filterKey,
                selectedFilterKeyword = keyword,
                deletePromptSuppressedUntilUtcMillis = appState.deletePromptSuppressedUntilUtcMillis,
                currentStreamId = it.currentStreamId?.takeIf { currentId -> appState.streams.any { stream -> stream.id == currentId } },
            )
        }
    }

    private fun matchesSelectedFilter(stream: StreamItem, key: String, keyword: String): Boolean {
        val matchesSource = when (key) {
            FilterOption.ALL_KEY -> true
            FilterOption.MANUAL_KEY -> stream.originKind == StreamOriginKind.MANUAL
            else -> stream.subscriptionId.equals(key, ignoreCase = true)
        }
        if (!matchesSource) {
            return false
        }
        if (keyword.isBlank()) {
            return true
        }
        return stream.name.contains(keyword, ignoreCase = true) || stream.url.contains(keyword, ignoreCase = true)
    }

    private suspend fun saveState() {
        appStateStore.save(appState)
    }

    private suspend fun runBusy(message: String, block: suspend () -> Unit) {
        busyBaseText = message
        busyOperationAbortRequested = false
        uiStateFlow.update {
            it.copy(
                isBusy = true,
                busyText = message,
                canAbortBusyOperation = false,
                canCancelBusyOperation = false,
            )
        }
        try {
            block()
        } finally {
            uiStateFlow.update {
                it.copy(
                    isBusy = false,
                    canAbortBusyOperation = false,
                    canCancelBusyOperation = false,
                )
            }
        }
    }

    private fun beginSubscriptionBusyOperation(message: String): Job {
        busyJob?.cancel()
        busyBaseText = message
        busyOperationAbortRequested = false
        subscriptionProgressById.clear()
        val job = viewModelScope.coroutineContext[Job]!!
        busyJob = job
        uiStateFlow.update {
            it.copy(
                isBusy = true,
                busyText = message,
                canAbortBusyOperation = true,
                canCancelBusyOperation = true,
            )
        }
        return job
    }

    private fun endBusyOperation() {
        subscriptionProgressById.clear()
        busyOperationAbortRequested = false
        uiStateFlow.update {
            it.copy(
                isBusy = false,
                canAbortBusyOperation = false,
                canCancelBusyOperation = false,
            )
        }
    }

    private fun reportSubscriptionProgress(subscriptionId: String, count: Int) {
        subscriptionProgressById[subscriptionId] = count
        uiStateFlow.update {
            it.copy(busyText = text(R.string.busy_parsed_count_format, busyBaseText, resolvedMediaCount()))
        }
    }

    private fun resolvedMediaCount(): Int = subscriptionProgressById.values.sum()

    private fun currentStream(): StreamItem? = getStream(uiStateFlow.value.currentStreamId)

    private fun containsStream(url: String): Boolean {
        return appState.streams.any { it.url.equals(url, ignoreCase = true) }
    }

    private fun stopAndClearCurrentStream() {
        player.stop()
        uiStateFlow.update { it.copy(currentStreamId = null, isPlaying = false, isLoading = false) }
    }

    private fun normalizeSelectedFilterIfNeeded() {
        val selectedKey = appState.selectedFilterKey
        if (selectedKey == FilterOption.ALL_KEY || selectedKey == FilterOption.MANUAL_KEY) {
            return
        }
        if (appState.subscriptions.none { it.id == selectedKey }) {
            appState = appState.copy(selectedFilterKey = FilterOption.ALL_KEY)
        }
    }

    private fun validateHttpAddress(address: String): String {
        val trimmed = address.trim()
        val uri = runCatching { URI(trimmed) }.getOrNull()
        if (uri == null || !(uri.scheme.equals("http", ignoreCase = true) || uri.scheme.equals("https", ignoreCase = true))) {
            throw IllegalArgumentException(text(R.string.validation_http_url))
        }
        return uri.toString()
    }

    private fun validateOptionalHttpAddress(address: String?): String? {
        if (address.isNullOrBlank()) {
            return null
        }
        return validateHttpAddress(address)
    }

    private fun validateMaxStreamCount(value: Int): Int {
        if (value !in 1..200000) {
            throw IllegalArgumentException(text(R.string.validation_max_count_range))
        }
        return value
    }

    private fun normalizeName(name: String?, address: String): String {
        if (!name.isNullOrBlank()) {
            return name.trim()
        }
        return runCatching {
            val uri = URI(address)
            val tail = Path.of(uri.path ?: "").fileName?.toString().orEmpty()
            tail.ifBlank { uri.host.orEmpty() }
        }.getOrDefault(text(R.string.unnamed_stream)).ifBlank { text(R.string.unnamed_stream) }
    }

    private fun AppState.normalize(): AppState {
        val normalizedFilterKey = selectedFilterKey.ifBlank { FilterOption.ALL_KEY }
        return copy(
            streams = streams,
            subscriptions = subscriptions,
            selectedFilterKey = normalizedFilterKey,
            selectedFilterKeyword = selectedFilterKeyword.trim(),
        )
    }

    private fun text(@StringRes resId: Int, vararg args: Any): String {
        return getApplication<Application>().getString(resId, *args)
    }

    override fun onCleared() {
        player.release()
        super.onCleared()
    }

    private fun <T> List<T>.sortedByName(selector: (T) -> String = { (it as StreamItem).name }): List<T> {
        return sortedBy { selector(it).lowercase(Locale.getDefault()) }
    }

    private fun android.content.ContentResolver.queryDisplayName(uri: Uri): String? {
        val cursor: Cursor? = query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)
        cursor.use {
            if (it != null && it.moveToFirst()) {
                val index = it.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                if (index >= 0) {
                    return it.getString(index)
                }
            }
        }
        return null
    }

}

data class MainUiState(
    val selectedTab: AppTab = AppTab.STREAMS,
    val allStreams: List<StreamItem> = emptyList(),
    val visibleStreams: List<StreamItem> = emptyList(),
    val favouriteStreams: List<StreamItem> = emptyList(),
    val subscriptions: List<SubscriptionItem> = emptyList(),
    val currentStreamId: String? = null,
    val isPlaying: Boolean = false,
    val isLoading: Boolean = false,
    val isBusy: Boolean = false,
    val busyText: String = "",
    val canAbortBusyOperation: Boolean = false,
    val canCancelBusyOperation: Boolean = false,
    val selectedFilterKey: String = FilterOption.ALL_KEY,
    val selectedFilterKeyword: String = "",
    val deletePromptSuppressedUntilUtcMillis: Long? = null,
)
