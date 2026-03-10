package com.silmenull.webmusicplayer.ui

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.Crossfade
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.animateContentSize
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.wrapContentSize
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.Favorite
import androidx.compose.material.icons.filled.FavoriteBorder
import androidx.compose.material.icons.filled.FilterAlt
import androidx.compose.material.icons.filled.LibraryMusic
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Stop
import androidx.compose.material.icons.filled.Subscriptions
import androidx.compose.material.icons.filled.Tune
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Divider
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
import androidx.compose.material3.SwipeToDismissBox
import androidx.compose.material3.SwipeToDismissBoxState
import androidx.compose.material3.SwipeToDismissBoxValue
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberSwipeToDismissBoxState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.silmenull.webmusicplayer.R
import com.silmenull.webmusicplayer.models.AppTab
import com.silmenull.webmusicplayer.models.FilterOption
import com.silmenull.webmusicplayer.models.StreamItem
import com.silmenull.webmusicplayer.models.StreamOriginKind
import com.silmenull.webmusicplayer.models.SubscriptionItem
import com.silmenull.webmusicplayer.viewmodel.MainUiState
import com.silmenull.webmusicplayer.viewmodel.MainViewModel
import kotlinx.coroutines.launch
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

private enum class ImportMenuAction {
    XSPF,
    ZIP,
}

private data class StreamFormState(
    val title: String,
    val subtitle: String,
    val saveLabel: String,
    val name: String = "",
    val url: String = "",
    val artworkUrl: String = "",
)

private data class SubscriptionFormState(
    val title: String,
    val subtitle: String,
    val saveLabel: String,
    val subscriptionId: String? = null,
    val name: String = "",
    val url: String = "",
    val maxStreamCount: String = "1000",
)

@Composable
fun WebMusicPlayerRoute(
    viewModel: MainViewModel,
    modifier: Modifier = Modifier,
) {
    val uiState by viewModel.uiState.collectAsState()
    val scope = rememberCoroutineScope()
    val snackbarHostState = remember { SnackbarHostState() }
    val addStreamTitle = stringResource(R.string.editor_add_stream_title)
    val addStreamSubtitle = stringResource(R.string.editor_add_stream_subtitle)
    val addStreamSave = stringResource(R.string.editor_add_stream_save)
    val addSubscriptionTitle = stringResource(R.string.editor_add_subscription_title)
    val addSubscriptionSubtitle = stringResource(R.string.editor_add_subscription_subtitle)
    val addSubscriptionSave = stringResource(R.string.editor_add_subscription_save)
    val editSubscriptionTitle = stringResource(R.string.editor_edit_subscription_title)
    val editSubscriptionSubtitle = stringResource(R.string.editor_edit_subscription_subtitle)
    val editSubscriptionSave = stringResource(R.string.editor_edit_subscription_save)
    val availableFilters = remember(uiState.subscriptions) {
        listOf(
            FilterOption(FilterOption.ALL_KEY, "all"),
            FilterOption(FilterOption.MANUAL_KEY, "manual"),
        ) + uiState.subscriptions.map { FilterOption(it.id, it.name) }
    }

    var streamsMenuExpanded by remember { mutableStateOf(false) }
    var subscriptionsMenuExpanded by remember { mutableStateOf(false) }
    var showFilterDialog by remember { mutableStateOf(false) }
    var streamFormState by remember { mutableStateOf<StreamFormState?>(null) }
    var subscriptionFormState by remember { mutableStateOf<SubscriptionFormState?>(null) }
    var deleteStreamTarget by remember { mutableStateOf<StreamItem?>(null) }
    var deleteSubscriptionTarget by remember { mutableStateOf<SubscriptionItem?>(null) }
    var pendingImportAction by remember { mutableStateOf<ImportMenuAction?>(null) }

    val filePickerLauncher =
        rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
            pendingImportAction = null
            if (uri == null) {
                return@rememberLauncherForActivityResult
            }
            scope.launch {
                runCatching { viewModel.importManualFile(uri) }
                    .onFailure { snackbarHostState.showSnackbar(it.message ?: "") }
            }
        }

    LaunchedEffect(Unit) {
        runCatching { viewModel.initialize() }
            .onFailure { snackbarHostState.showSnackbar(it.message ?: "") }
    }

    LaunchedEffect(viewModel) {
        viewModel.events.collect { message ->
            snackbarHostState.showSnackbar(message)
        }
    }

    Scaffold(
        modifier = modifier.fillMaxSize(),
        contentWindowInsets = WindowInsets.safeDrawing,
        snackbarHost = { SnackbarHost(hostState = snackbarHostState) },
        containerColor = MaterialTheme.colorScheme.background,
        bottomBar = {
            TabSection(
                selectedTab = uiState.selectedTab,
                onSelectTab = viewModel::selectTab,
            )
        }
    ) { innerPadding ->
        Column(
            modifier = Modifier
                .padding(innerPadding)
                .padding(16.dp, 8.dp)
        ) {
            HeaderSection(
                uiState = uiState,
                onOpenFilter = { showFilterDialog = true },
                streamsMenuExpanded = streamsMenuExpanded,
                onStreamsMenuExpandedChange = { streamsMenuExpanded = it },
                subscriptionsMenuExpanded = subscriptionsMenuExpanded,
                onSubscriptionsMenuExpandedChange = { subscriptionsMenuExpanded = it },
                onAddStream = {
                    streamsMenuExpanded = false
                    streamFormState = StreamFormState(
                        title = addStreamTitle,
                        subtitle = addStreamSubtitle,
                        saveLabel = addStreamSave,
                    )
                },
                onImportXspf = {
                    streamsMenuExpanded = false
                    pendingImportAction = ImportMenuAction.XSPF
                    filePickerLauncher.launch(arrayOf("*/*"))
                },
                onImportZip = {
                    streamsMenuExpanded = false
                    pendingImportAction = ImportMenuAction.ZIP
                    filePickerLauncher.launch(arrayOf("*/*"))
                },
                onRefreshSubscriptions = {
                    scope.launch {
                        runCatching { viewModel.updateAllSubscriptions() }
                            .onFailure { snackbarHostState.showSnackbar(it.message ?: "") }
                    }
                },
                onAddSubscription = {
                    subscriptionsMenuExpanded = false
                    subscriptionFormState = SubscriptionFormState(
                        title = addSubscriptionTitle,
                        subtitle = addSubscriptionSubtitle,
                        saveLabel = addSubscriptionSave,
                    )
                },
            )

            Spacer(modifier = Modifier.height(12.dp))

            Box(modifier = Modifier.weight(1f)) {
                AnimatedContent(
                    targetState = uiState.selectedTab,
                    label = "page_transition",
                ) { tab ->
                    when (tab) {
                        AppTab.FAVOURITES -> StreamsLikePage(
                            summary = stringResource(
                                R.string.favourites_summary_format,
                                uiState.favouriteStreams.size
                            ),
                            emptyTitle = stringResource(R.string.favourites_empty_title),
                            emptySubtitle = stringResource(R.string.favourites_empty_subtitle),
                            streams = uiState.favouriteStreams,
                            currentStreamId = uiState.currentStreamId,
                            showOrigin = false,
                            onPlay = { viewModel.playStream(it.id) },
                            onStartAction = { stream ->
                                scope.launch { viewModel.archiveFavourite(stream.id) }
                            },
                            onEndAction = { stream ->
                                scope.launch { viewModel.archiveFavourite(stream.id) }
                            },
                            startActionLabel = stringResource(R.string.swipe_unfavourite),
                            endActionLabel = "",
                        )

                        AppTab.STREAMS -> StreamsLikePage(
                            summary = streamSummary(uiState, availableFilters),
                            emptyTitle = stringResource(R.string.streams_empty_title),
                            emptySubtitle = stringResource(R.string.streams_empty_subtitle),
                            streams = uiState.visibleStreams,
                            currentStreamId = uiState.currentStreamId,
                            showOrigin = true,
                            onPlay = { viewModel.playStream(it.id) },
                            onStartAction = { stream ->
                                scope.launch { viewModel.toggleFavourite(stream.id) }
                            },
                            onEndAction = { stream ->
                                if (viewModel.shouldConfirmDelete()) {
                                    deleteStreamTarget = stream
                                } else {
                                    scope.launch {
                                        viewModel.deleteStream(
                                            stream.id,
                                            suppressReminder = false
                                        )
                                    }
                                }
                            },
                            startActionLabel = stringResource(R.string.favourite_action_add),
                            endActionLabel = stringResource(R.string.swipe_delete),
                        )

                        AppTab.SUBSCRIPTIONS -> SubscriptionsPage(
                            summary = stringResource(
                                R.string.subscriptions_summary_format,
                                uiState.subscriptions.size
                            ),
                            emptyTitle = stringResource(R.string.subscriptions_empty_title),
                            emptySubtitle = stringResource(R.string.subscriptions_empty_subtitle),
                            subscriptions = uiState.subscriptions,
                            onEdit = { subscription ->
                                subscriptionFormState = SubscriptionFormState(
                                    title = editSubscriptionTitle,
                                    subtitle = editSubscriptionSubtitle,
                                    saveLabel = editSubscriptionSave,
                                    subscriptionId = subscription.id,
                                    name = subscription.name,
                                    url = subscription.url,
                                    maxStreamCount = subscription.maxStreamCount.toString(),
                                )
                            },
                            onDelete = { deleteSubscriptionTarget = it },
                        )
                    }
                }
            }

            Spacer(modifier = Modifier.height(12.dp))

            NowPlayingCard(
                uiState = uiState,
                currentStream = uiState.allStreams.firstOrNull { it.id == uiState.currentStreamId },
                onTogglePlayback = viewModel::togglePlayback,
            )
        }
    }

    if (showFilterDialog) {
        FilterDialog(
            availableFilters = availableFilters,
            selectedFilterKey = uiState.selectedFilterKey,
            keyword = uiState.selectedFilterKeyword,
            onDismiss = { showFilterDialog = false },
            onApply = { key, keyword ->
                showFilterDialog = false
                scope.launch {
                    runCatching { viewModel.applyFilter(key, keyword) }
                        .onFailure { snackbarHostState.showSnackbar(it.message ?: "") }
                }
            },
        )
    }

    streamFormState?.let { formState ->
        StreamEditorDialog(
            initial = formState,
            onDismiss = { streamFormState = null },
            onSubmit = { name, url, artworkUrl ->
                scope.launch {
                    runCatching { viewModel.addManualStream(name, url, artworkUrl) }
                        .onSuccess { streamFormState = null }
                        .onFailure { snackbarHostState.showSnackbar(it.message ?: "") }
                }
            },
        )
    }

    subscriptionFormState?.let { formState ->
        SubscriptionEditorDialog(
            initial = formState,
            onDismiss = { subscriptionFormState = null },
            onSubmit = { name, url, maxStreamCount ->
                scope.launch {
                    runCatching {
                        if (formState.subscriptionId == null) {
                            viewModel.addSubscription(name, url, maxStreamCount)
                        } else {
                            viewModel.editSubscription(
                                formState.subscriptionId,
                                name,
                                url,
                                maxStreamCount
                            )
                        }
                    }.onSuccess {
                        subscriptionFormState = null
                    }.onFailure {
                        snackbarHostState.showSnackbar(it.message ?: "")
                    }
                }
            },
        )
    }

    deleteStreamTarget?.let { stream ->
        DeleteStreamDialog(
            stream = stream,
            onDismiss = { deleteStreamTarget = null },
            onDelete = { suppressReminder ->
                scope.launch {
                    viewModel.deleteStream(stream.id, suppressReminder)
                    deleteStreamTarget = null
                }
            },
        )
    }

    deleteSubscriptionTarget?.let { subscription ->
        ConfirmDialog(
            title = stringResource(R.string.delete_subscription_confirm_title),
            message = stringResource(
                R.string.delete_subscription_confirm_message_format,
                subscription.name
            ),
            confirmLabel = stringResource(R.string.generic_delete),
            dismissLabel = stringResource(R.string.generic_cancel),
            onDismiss = { deleteSubscriptionTarget = null },
            onConfirm = {
                scope.launch {
                    viewModel.deleteSubscription(subscription.id)
                    deleteSubscriptionTarget = null
                }
            },
        )
    }

    BusyOverlay(
        isVisible = uiState.isBusy,
        message = uiState.busyText,
        canAbort = uiState.canAbortBusyOperation,
        canCancel = uiState.canCancelBusyOperation,
        onAbort = viewModel::abortBusyOperation,
        onCancel = viewModel::cancelBusyOperation,
    )
}

@Composable
private fun HeaderSection(
    uiState: MainUiState,
    onOpenFilter: () -> Unit,
    streamsMenuExpanded: Boolean,
    onStreamsMenuExpandedChange: (Boolean) -> Unit,
    subscriptionsMenuExpanded: Boolean,
    onSubscriptionsMenuExpandedChange: (Boolean) -> Unit,
    onAddStream: () -> Unit,
    onImportXspf: () -> Unit,
    onImportZip: () -> Unit,
    onRefreshSubscriptions: () -> Unit,
    onAddSubscription: () -> Unit,
) {
    Surface(
        color = MaterialTheme.colorScheme.surface,
        shape = RoundedCornerShape(24.dp),
        tonalElevation = 2.dp,
        shadowElevation = 4.dp,
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 18.dp, vertical = 18.dp),
            verticalAlignment = Alignment.Top,
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(
                        imageVector = when (uiState.selectedTab) {
                            AppTab.FAVOURITES -> Icons.Filled.Favorite
                            AppTab.STREAMS -> Icons.Filled.LibraryMusic
                            AppTab.SUBSCRIPTIONS -> Icons.Filled.Subscriptions
                        },
                        contentDescription = null,
                        tint = MaterialTheme.colorScheme.primary,
                    )
                    Spacer(modifier = Modifier.width(10.dp))
                    Text(
                        text = when (uiState.selectedTab) {
                            AppTab.FAVOURITES -> stringResource(R.string.page_title_favourites)
                            AppTab.STREAMS -> stringResource(R.string.page_title_streams)
                            AppTab.SUBSCRIPTIONS -> stringResource(R.string.page_title_subscriptions)
                        },
                        style = MaterialTheme.typography.headlineMedium,
                        fontWeight = FontWeight.SemiBold,
                    )
                }
                Spacer(modifier = Modifier.height(4.dp))
                Text(
                    text = stringResource(R.string.app_name),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }

            if (uiState.selectedTab == AppTab.STREAMS) {
                HeaderIconButton(
                    icon = Icons.Filled.FilterAlt,
                    contentDescription = stringResource(R.string.filter_dialog_title),
                    onClick = onOpenFilter,
                )
                Box {
                    HeaderIconButton(
                        icon = Icons.Filled.MoreVert,
                        contentDescription = stringResource(R.string.action_add_stream_title),
                        onClick = { onStreamsMenuExpandedChange(true) },
                    )
                    DropdownMenu(
                        expanded = streamsMenuExpanded,
                        onDismissRequest = { onStreamsMenuExpandedChange(false) },
                    ) {
                        DropdownMenuItem(
                            text = { Text(stringResource(R.string.action_manual_add)) },
                            onClick = onAddStream,
                            leadingIcon = { Icon(Icons.Filled.Add, contentDescription = null) },
                        )
                        DropdownMenuItem(
                            text = { Text(stringResource(R.string.action_import_xspf)) },
                            onClick = onImportXspf,
                            leadingIcon = {
                                Icon(
                                    Icons.Filled.LibraryMusic,
                                    contentDescription = null
                                )
                            },
                        )
                        DropdownMenuItem(
                            text = { Text(stringResource(R.string.action_import_zip)) },
                            onClick = onImportZip,
                            leadingIcon = { Icon(Icons.Filled.Tune, contentDescription = null) },
                        )
                    }
                }
            }

            if (uiState.selectedTab == AppTab.SUBSCRIPTIONS) {
                HeaderIconButton(
                    icon = Icons.Filled.Refresh,
                    contentDescription = stringResource(R.string.busy_updating_subscriptions),
                    onClick = onRefreshSubscriptions,
                )
                Box {
                    HeaderIconButton(
                        icon = Icons.Filled.MoreVert,
                        contentDescription = stringResource(R.string.action_subscription_title),
                        onClick = { onSubscriptionsMenuExpandedChange(true) },
                    )
                    DropdownMenu(
                        expanded = subscriptionsMenuExpanded,
                        onDismissRequest = { onSubscriptionsMenuExpandedChange(false) },
                    ) {
                        DropdownMenuItem(
                            text = { Text(stringResource(R.string.action_add_subscription)) },
                            onClick = onAddSubscription,
                            leadingIcon = { Icon(Icons.Filled.Add, contentDescription = null) },
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun HeaderIconButton(
    icon: ImageVector,
    contentDescription: String,
    onClick: () -> Unit,
) {
    Surface(
        modifier = Modifier.padding(start = 8.dp),
        shape = CircleShape,
        color = MaterialTheme.colorScheme.secondaryContainer,
    ) {
        IconButton(onClick = onClick) {
            Icon(icon, contentDescription = contentDescription)
        }
    }
}

@Composable
private fun TabSection(
    selectedTab: AppTab,
    onSelectTab: (AppTab) -> Unit,
) {
    NavigationBar() {
        listOf(
            Triple(
                AppTab.FAVOURITES,
                stringResource(R.string.tab_favourites),
                Icons.Filled.Favorite
            ),
            Triple(
                AppTab.STREAMS,
                stringResource(R.string.tab_streams),
                Icons.Filled.LibraryMusic
            ),
            Triple(
                AppTab.SUBSCRIPTIONS,
                stringResource(R.string.tab_subscriptions),
                Icons.Filled.Subscriptions
            ),
        ).forEach { (tab, title, icon) ->
            NavigationBarItem(
                selected = selectedTab == tab,
                onClick = {
                    onSelectTab(tab)
                },
                icon = {
                    Icon(icon, contentDescription = title)
                },
                label = {
                    Text(title)
                }
            )
        }
    }
}

@Composable
private fun StreamsLikePage(
    summary: String,
    emptyTitle: String,
    emptySubtitle: String,
    streams: List<StreamItem>,
    currentStreamId: String?,
    showOrigin: Boolean,
    onPlay: (StreamItem) -> Unit,
    onStartAction: (StreamItem) -> Unit,
    onEndAction: (StreamItem) -> Unit,
    startActionLabel: String,
    endActionLabel: String,
) {
    Column(modifier = Modifier.fillMaxSize()) {
        SummaryCard(
            summary, modifier = Modifier
                .fillMaxWidth()
        )
        Spacer(modifier = Modifier.height(12.dp))
        if (streams.isEmpty()) {
            EmptyStateCard(emptyTitle, emptySubtitle)
        } else {
            LazyColumn(
                modifier = Modifier.fillMaxSize(),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                items(streams, key = { it.id }) { stream ->
                    StreamRow(
                        stream = stream,
                        currentStreamId = currentStreamId,
                        showOrigin = showOrigin,
                        onPlay = { onPlay(stream) },
                        onStartAction = { onStartAction(stream) },
                        onEndAction = { onEndAction(stream) },
                        startActionLabel = if (stream.isFavourite) stringResource(R.string.favourite_action_remove) else stringResource(
                            R.string.favourite_action_add
                        ),
                        endActionLabel = endActionLabel,
                    )
                }
            }
        }
    }
}

@Composable
private fun SubscriptionsPage(
    summary: String,
    emptyTitle: String,
    emptySubtitle: String,
    subscriptions: List<SubscriptionItem>,
    onEdit: (SubscriptionItem) -> Unit,
    onDelete: (SubscriptionItem) -> Unit,
) {
    Column(modifier = Modifier.fillMaxSize()) {
        SummaryCard(
            summary, modifier = Modifier
                .fillMaxWidth()
        )
        Spacer(modifier = Modifier.height(12.dp))
        if (subscriptions.isEmpty()) {
            EmptyStateCard(emptyTitle, emptySubtitle)
        } else {
            LazyColumn(
                modifier = Modifier.fillMaxSize(),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                items(subscriptions, key = { it.id }) { subscription ->
                    SubscriptionRow(
                        subscription = subscription,
                        onEdit = { onEdit(subscription) },
                        onDelete = { onDelete(subscription) },
                    )
                }
            }
        }
    }
}

@Composable
private fun SummaryCard(summary: String, modifier: Modifier) {
    Surface(
        shape = RoundedCornerShape(18.dp),
        color = MaterialTheme.colorScheme.primaryContainer,
        tonalElevation = 1.dp,
        modifier = Modifier
    ) {
        Text(
            text = summary,
            modifier = Modifier.padding(horizontal = 14.dp, vertical = 12.dp),
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onPrimaryContainer,
        )
    }
}

@Composable
private fun EmptyStateCard(
    title: String,
    subtitle: String,
) {
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(24.dp),
        color = MaterialTheme.colorScheme.surface,
        tonalElevation = 1.dp,
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 24.dp, vertical = 32.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Text(title, style = MaterialTheme.typography.titleMedium, textAlign = TextAlign.Center)
            Spacer(modifier = Modifier.height(8.dp))
            Text(
                text = subtitle,
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                textAlign = TextAlign.Center,
            )
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun StreamRow(
    stream: StreamItem,
    currentStreamId: String?,
    showOrigin: Boolean,
    onPlay: () -> Unit,
    onStartAction: () -> Unit,
    onEndAction: () -> Unit,
    startActionLabel: String,
    endActionLabel: String,
) {
    val dismissState = rememberSwipeToDismissBoxState(
        confirmValueChange = { value ->
            when (value) {
                SwipeToDismissBoxValue.StartToEnd -> {
                    if (!startActionLabel.isEmpty()) {
                        onStartAction()
                    }
                    false
                }

                SwipeToDismissBoxValue.EndToStart -> {
                    if (!endActionLabel.isEmpty()) {
                        onEndAction()
                    }
                    false
                }

                SwipeToDismissBoxValue.Settled -> false
            }
        }
    )

    SwipeToDismissBox(
        state = dismissState,
        backgroundContent = {
            Box(modifier = Modifier.fillMaxSize()) {
                when (dismissState.dismissDirection) {
                    SwipeToDismissBoxValue.StartToEnd -> {
                        if (!startActionLabel.isEmpty()) {
                            Icon(
                                if (stream.isFavourite)
                                    Icons.Default.Favorite
                                else
                                    Icons.Default.FavoriteBorder,
                                startActionLabel,
                                modifier = Modifier
                                    .align(Alignment.CenterStart)
                                    .clip(RoundedCornerShape(32.dp))
                                    .background(MaterialTheme.colorScheme.surfaceVariant)
                                    .padding(8.dp)
                            )
                        }
                    }

                    SwipeToDismissBoxValue.EndToStart -> {
                        if (!endActionLabel.isEmpty()) {
                            Icon(
                                Icons.Default.Delete,
                                endActionLabel,
                                modifier = Modifier
                                    .align(Alignment.CenterEnd)
                                    .clip(RoundedCornerShape(32.dp))
                                    .background(MaterialTheme.colorScheme.surfaceVariant)
                                    .padding(8.dp)
                            )
                        }
                    }

                    SwipeToDismissBoxValue.Settled -> {

                    }
                }
            }
        },
    ) {
        StreamCard(
            stream = stream,
            isCurrent = currentStreamId == stream.id,
            showOrigin = showOrigin,
            onClick = onPlay,
        )
    }
}

@Composable
private fun StreamCard(
    stream: StreamItem,
    isCurrent: Boolean,
    showOrigin: Boolean,
    onClick: () -> Unit,
) {
    Surface(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(20.dp))
            .clickable(onClick = onClick),
        shape = RoundedCornerShape(20.dp),
        color = MaterialTheme.colorScheme.surfaceContainer,
        tonalElevation = if (isCurrent) 3.dp else 1.dp,
        shadowElevation = if (isCurrent) 6.dp else 1.dp,
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 0.dp, vertical = 16.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Row(
                modifier = Modifier
                    .animateContentSize()
            ) {
                if (isCurrent) {
                    Box(
                        modifier = Modifier
                            .padding(start = 4.dp)
                            .width(6.dp)
                            .height(22.dp)
                            .clip(RoundedCornerShape(10.dp))
                            .background(MaterialTheme.colorScheme.primary),
                    )
                }
            }
            Column(
                modifier = Modifier
                    .weight(1f)
                    .padding(start = 16.dp, end = 12.dp),
            ) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        text = stream.name,
                        style = MaterialTheme.typography.titleMedium,
                        fontWeight = FontWeight.SemiBold,
                        modifier = Modifier.weight(1f, fill = false),
                    )
                    if (showOrigin) {
                        Spacer(modifier = Modifier.width(8.dp))
                        Text(
                            text = originLabel(stream),
                            style = MaterialTheme.typography.labelSmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.75f),
                        )
                    }
                }
                Spacer(modifier = Modifier.height(4.dp))
                Text(
                    text = stream.url,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            Icon(
                imageVector = if (stream.isFavourite) Icons.Filled.Favorite else Icons.Filled.FavoriteBorder,
                contentDescription = null,
                tint = if (stream.isFavourite) Color(0xFFF59E0B) else MaterialTheme.colorScheme.outline,
                modifier = Modifier
                    .padding(end = 16.dp)
                    .size(20.dp),
            )
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun SubscriptionRow(
    subscription: SubscriptionItem,
    onEdit: () -> Unit,
    onDelete: () -> Unit,
) {
    val dismissState = rememberSwipeToDismissBoxState(
        confirmValueChange = { value ->
            if (value == SwipeToDismissBoxValue.EndToStart) {
                onDelete()
            }
            false
        }
    )

    SwipeToDismissBox(
        state = dismissState,
        enableDismissFromStartToEnd = false,
        backgroundContent = {
            DismissBackground(
                state = dismissState,
                startLabel = "",
                endLabel = stringResource(R.string.swipe_delete),
                startColor = Color.Transparent,
                endColor = MaterialTheme.colorScheme.errorContainer,
                startIcon = Icons.Filled.Edit,
                endIcon = Icons.Filled.Delete,
            )
        },
    ) {
        Surface(
            modifier = Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(20.dp))
                .clickable(onClick = onEdit),
            shape = RoundedCornerShape(20.dp),
            color = MaterialTheme.colorScheme.surface,
            tonalElevation = 1.dp,
        ) {
            Column(modifier = Modifier.padding(16.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        text = subscription.name,
                        style = MaterialTheme.typography.titleMedium,
                        fontWeight = FontWeight.SemiBold,
                        modifier = Modifier.weight(1f),
                    )
                    Icon(
                        Icons.Filled.Edit,
                        contentDescription = null,
                        tint = MaterialTheme.colorScheme.primary
                    )
                }
                Spacer(modifier = Modifier.height(6.dp))
                Text(
                    text = subscription.url,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Spacer(modifier = Modifier.height(6.dp))
                Text(
                    text = stringResource(
                        R.string.subscription_limits_format,
                        subscription.maxStreamCount
                    ),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Spacer(modifier = Modifier.height(4.dp))
                Text(
                    text = subscriptionLastUpdatedLabel(subscription.lastUpdatedUtcMillis),
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun DismissBackground(
    state: SwipeToDismissBoxState,
    startLabel: String,
    endLabel: String,
    startColor: Color,
    endColor: Color,
    startIcon: ImageVector,
    endIcon: ImageVector,
) {
    val direction = state.dismissDirection
    val background = when (direction) {
        SwipeToDismissBoxValue.StartToEnd -> startColor
        SwipeToDismissBoxValue.EndToStart -> endColor
        SwipeToDismissBoxValue.Settled -> MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.35f)
    }
    val alignment = when (direction) {
        SwipeToDismissBoxValue.EndToStart -> Alignment.CenterEnd
        else -> Alignment.CenterStart
    }
    val label = when (direction) {
        SwipeToDismissBoxValue.EndToStart -> endLabel
        else -> startLabel
    }
    val icon = when (direction) {
        SwipeToDismissBoxValue.EndToStart -> endIcon
        else -> startIcon
    }

    Box(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(20.dp))
            .background(background)
            .padding(horizontal = 20.dp),
        contentAlignment = alignment,
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            if (direction == SwipeToDismissBoxValue.EndToStart) {
                Text(text = label, color = MaterialTheme.colorScheme.onErrorContainer)
                Spacer(modifier = Modifier.width(8.dp))
                Icon(
                    icon,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.onErrorContainer
                )
            } else {
                Icon(
                    icon,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.onTertiaryContainer
                )
                Spacer(modifier = Modifier.width(8.dp))
                Text(text = label, color = MaterialTheme.colorScheme.onTertiaryContainer)
            }
        }
    }
}

@Composable
private fun NowPlayingCard(
    uiState: MainUiState,
    currentStream: StreamItem?,
    onTogglePlayback: () -> Unit,
) {
    Surface(
        shape = RoundedCornerShape(24.dp),
        color = MaterialTheme.colorScheme.surface,
        tonalElevation = 3.dp,
        shadowElevation = 3.dp,
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(16.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Box(
                modifier = Modifier
                    .size(52.dp)
                    .clip(RoundedCornerShape(16.dp))
                    .background(
                        Brush.linearGradient(
                            listOf(
                                MaterialTheme.colorScheme.primary,
                                MaterialTheme.colorScheme.tertiary,
                            )
                        )
                    ),
                contentAlignment = Alignment.Center,
            ) {
                Icon(
                    imageVector = if (uiState.isPlaying) Icons.Filled.LibraryMusic else Icons.Filled.PlayArrow,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.onPrimary,
                )
            }
            Spacer(modifier = Modifier.width(14.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = currentStream?.name
                        ?: stringResource(R.string.current_stream_none_title),
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.SemiBold,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Spacer(modifier = Modifier.height(4.dp))
                Crossfade(
                    targetState = uiState.isLoading,
                    label = "loading_crossfade"
                ) { isLoading ->
                    if (isLoading) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            CircularProgressIndicator(
                                modifier = Modifier.size(14.dp),
                                strokeWidth = 2.dp
                            )
                            Spacer(modifier = Modifier.width(8.dp))
                            Text(
                                text = stringResource(R.string.loading),
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    } else {
                        Text(
                            text = currentStream?.let { originLabel(it) }
                                ?: stringResource(R.string.current_stream_none_subtitle),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                }
            }
            Surface(
                shape = RoundedCornerShape(16.dp),
                color = MaterialTheme.colorScheme.primary,
            ) {
                Row(
                    modifier = Modifier
                        .clickable(enabled = currentStream != null, onClick = onTogglePlayback)
                        .padding(horizontal = 14.dp, vertical = 12.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Icon(
                        imageVector = if (uiState.isPlaying) Icons.Filled.Stop else Icons.Filled.PlayArrow,
                        contentDescription = null,
                        tint = MaterialTheme.colorScheme.onPrimary,
                    )
                }
            }
        }
    }
}

@Composable
private fun FilterDialog(
    availableFilters: List<FilterOption>,
    selectedFilterKey: String,
    keyword: String,
    onDismiss: () -> Unit,
    onApply: (String, String) -> Unit,
) {
    var localKeyword by remember(selectedFilterKey, keyword) { mutableStateOf(keyword) }
    var localKey by remember(selectedFilterKey) { mutableStateOf(selectedFilterKey) }

    AlertDialog(
        onDismissRequest = onDismiss,
        confirmButton = {
            Row {
                TextButton(onClick = {
                    localKey = FilterOption.ALL_KEY
                    localKeyword = ""
                }) {
                    Text(stringResource(R.string.generic_reset))
                }
                TextButton(onClick = onDismiss) {
                    Text(stringResource(R.string.generic_cancel))
                }
                TextButton(onClick = { onApply(localKey, localKeyword) }) {
                    Text(stringResource(R.string.filter_apply))
                }
            }
        },
        title = {
            Column {
                Text(stringResource(R.string.filter_dialog_title))
                Spacer(modifier = Modifier.height(4.dp))
                Text(
                    text = stringResource(R.string.filter_dialog_subtitle),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        },
        text = {
            Column(
                modifier = Modifier.verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                OutlinedTextField(
                    value = localKeyword,
                    onValueChange = { localKeyword = it },
                    label = { Text(stringResource(R.string.filter_keyword_label)) },
                    placeholder = { Text(stringResource(R.string.filter_keyword_placeholder)) },
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true,
                )
                Text(
                    text = stringResource(R.string.filter_source_label),
                    style = MaterialTheme.typography.labelLarge,
                    fontWeight = FontWeight.SemiBold,
                )
                Text(
                    text = stringResource(R.string.filter_source_hint),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                availableFilters.forEach { filter ->
                    Surface(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clip(RoundedCornerShape(16.dp))
                            .clickable { localKey = filter.key },
                        shape = RoundedCornerShape(16.dp),
                        color = if (localKey == filter.key) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surfaceVariant.copy(
                            alpha = 0.45f
                        ),
                    ) {
                        Row(
                            modifier = Modifier.padding(horizontal = 10.dp, vertical = 8.dp),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            RadioButton(
                                selected = localKey == filter.key,
                                onClick = { localKey = filter.key })
                            Spacer(modifier = Modifier.width(6.dp))
                            Text(filterLabel(filter))
                        }
                    }
                }
            }
        },
        shape = RoundedCornerShape(24.dp),
    )
}

@Composable
private fun StreamEditorDialog(
    initial: StreamFormState,
    onDismiss: () -> Unit,
    onSubmit: (String, String, String?) -> Unit,
) {
    var name by remember(initial) { mutableStateOf(initial.name) }
    var url by remember(initial) { mutableStateOf(initial.url) }
    var artworkUrl by remember(initial) { mutableStateOf(initial.artworkUrl) }

    AlertDialog(
        onDismissRequest = onDismiss,
        confirmButton = {
            TextButton(onClick = {
                onSubmit(
                    name.trim(),
                    url.trim(),
                    artworkUrl.trim().takeIf { it.isNotEmpty() })
            }) {
                Text(initial.saveLabel)
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(R.string.generic_cancel))
            }
        },
        title = {
            Column {
                Text(initial.title)
                Spacer(modifier = Modifier.height(4.dp))
                Text(
                    text = initial.subtitle,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                OutlinedTextField(
                    value = name,
                    onValueChange = { name = it },
                    label = { Text(stringResource(R.string.stream_name_label)) },
                    placeholder = { Text(stringResource(R.string.stream_name_placeholder)) },
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true,
                )
                OutlinedTextField(
                    value = url,
                    onValueChange = { url = it },
                    label = { Text(stringResource(R.string.stream_url_label)) },
                    placeholder = { Text(stringResource(R.string.stream_url_placeholder)) },
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true,
                )
                OutlinedTextField(
                    value = artworkUrl,
                    onValueChange = { artworkUrl = it },
                    label = { Text(stringResource(R.string.stream_artwork_url_label)) },
                    placeholder = { Text(stringResource(R.string.stream_artwork_url_placeholder)) },
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true,
                )
                Text(
                    text = stringResource(R.string.editor_form_hint),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        },
        shape = RoundedCornerShape(24.dp),
    )
}

@Composable
private fun SubscriptionEditorDialog(
    initial: SubscriptionFormState,
    onDismiss: () -> Unit,
    onSubmit: (String, String, Int) -> Unit,
) {
    var name by remember(initial) { mutableStateOf(initial.name) }
    var url by remember(initial) { mutableStateOf(initial.url) }
    var maxStreamCount by remember(initial) { mutableStateOf(initial.maxStreamCount) }

    AlertDialog(
        onDismissRequest = onDismiss,
        confirmButton = {
            TextButton(onClick = {
                onSubmit(
                    name.trim(),
                    url.trim(),
                    maxStreamCount.trim().toIntOrNull() ?: -1
                )
            }) {
                Text(initial.saveLabel)
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(R.string.generic_cancel))
            }
        },
        title = {
            Column {
                Text(initial.title)
                Spacer(modifier = Modifier.height(4.dp))
                Text(
                    text = initial.subtitle,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                OutlinedTextField(
                    value = name,
                    onValueChange = { name = it },
                    label = { Text(stringResource(R.string.subscription_name_label)) },
                    placeholder = { Text(stringResource(R.string.subscription_name_placeholder)) },
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true,
                )
                OutlinedTextField(
                    value = url,
                    onValueChange = { url = it },
                    label = { Text(stringResource(R.string.subscription_url_label)) },
                    placeholder = { Text(stringResource(R.string.subscription_url_placeholder)) },
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true,
                )
                OutlinedTextField(
                    value = maxStreamCount,
                    onValueChange = { maxStreamCount = it.filter(Char::isDigit) },
                    label = { Text(stringResource(R.string.subscription_max_count_label)) },
                    placeholder = { Text(stringResource(R.string.subscription_max_count_placeholder)) },
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true,
                )
                Text(
                    text = stringResource(R.string.subscription_editor_hint),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        },
        shape = RoundedCornerShape(24.dp),
    )
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun DeleteStreamDialog(
    stream: StreamItem,
    onDismiss: () -> Unit,
    onDelete: (Boolean) -> Unit,
) {
    AlertDialog(
        onDismissRequest = onDismiss,
        confirmButton = {
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                TextButton(onClick = onDismiss) {
                    Text(stringResource(R.string.generic_no))
                }
                TextButton(onClick = { onDelete(false) }) {
                    Text(stringResource(R.string.generic_yes))
                }
                TextButton(onClick = { onDelete(true) }) {
                    Text(stringResource(R.string.delete_stream_confirm_suppress))
                }
            }
        },
        title = { Text(stringResource(R.string.delete_stream_confirm_title)) },
        text = {
            Text(
                text = stream.name,
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        },
        shape = RoundedCornerShape(24.dp),
    )
}

@Composable
private fun ConfirmDialog(
    title: String,
    message: String,
    confirmLabel: String,
    dismissLabel: String,
    onDismiss: () -> Unit,
    onConfirm: () -> Unit,
) {
    AlertDialog(
        onDismissRequest = onDismiss,
        confirmButton = {
            TextButton(onClick = onConfirm) {
                Text(confirmLabel)
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(dismissLabel)
            }
        },
        title = { Text(title) },
        text = { Text(message) },
        shape = RoundedCornerShape(24.dp),
    )
}

@Composable
private fun BusyOverlay(
    isVisible: Boolean,
    message: String,
    canAbort: Boolean,
    canCancel: Boolean,
    onAbort: () -> Unit,
    onCancel: () -> Unit,
) {
    if (!isVisible) {
        return
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(Color.Black.copy(alpha = 0.45f)),
        contentAlignment = Alignment.Center,
    ) {
        Surface(
            modifier = Modifier.padding(24.dp),
            shape = RoundedCornerShape(24.dp),
            color = MaterialTheme.colorScheme.surface,
            tonalElevation = 4.dp,
            shadowElevation = 10.dp,
        ) {
            Column(
                modifier = Modifier.padding(horizontal = 24.dp, vertical = 22.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
            ) {
                CircularProgressIndicator()
                Spacer(modifier = Modifier.height(14.dp))
                Text(message, textAlign = TextAlign.Center)
                if (canAbort || canCancel) {
                    Spacer(modifier = Modifier.height(16.dp))
                    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        if (canAbort) {
                            TextButton(onClick = onAbort) {
                                Text(stringResource(R.string.busy_abort_button))
                            }
                        }
                        if (canCancel) {
                            TextButton(onClick = onCancel) {
                                Text(stringResource(R.string.generic_cancel))
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun originLabel(stream: StreamItem): String {
    return when (stream.originKind) {
        StreamOriginKind.MANUAL -> stringResource(R.string.origin_manual_added)
        StreamOriginKind.SUBSCRIPTION -> stream.subscriptionName?.takeIf { it.isNotBlank() }
            ?: stringResource(R.string.origin_subscription)
    }
}

@Composable
private fun subscriptionLastUpdatedLabel(lastUpdatedUtcMillis: Long?): String {
    if (lastUpdatedUtcMillis == null) {
        return stringResource(R.string.subscription_last_updated_never)
    }
    val formatter = remember {
        DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm", Locale.getDefault())
    }
    val formatted = Instant.ofEpochMilli(lastUpdatedUtcMillis)
        .atZone(ZoneId.systemDefault())
        .format(formatter)
    return stringResource(R.string.subscription_last_updated_format, formatted)
}

@Composable
private fun streamSummary(uiState: MainUiState, availableFilters: List<FilterOption>): String {
    val selectedLabel = availableFilters.firstOrNull { it.key == uiState.selectedFilterKey }
        ?.let { filterLabel(it) }
        ?: stringResource(R.string.filter_all_sources)
    return if (uiState.selectedFilterKeyword.isBlank()) {
        stringResource(
            R.string.streams_summary_format,
            selectedLabel,
            uiState.visibleStreams.size,
            uiState.allStreams.size,
        )
    } else {
        stringResource(
            R.string.streams_summary_with_keyword_format,
            selectedLabel,
            uiState.selectedFilterKeyword,
            uiState.visibleStreams.size,
            uiState.allStreams.size,
        )
    }
}

@Composable
private fun filterLabel(filterOption: FilterOption): String {
    return when (filterOption.key) {
        FilterOption.ALL_KEY -> stringResource(R.string.filter_all_sources)
        FilterOption.MANUAL_KEY -> stringResource(R.string.filter_manual_added)
        else -> filterOption.label
    }
}
