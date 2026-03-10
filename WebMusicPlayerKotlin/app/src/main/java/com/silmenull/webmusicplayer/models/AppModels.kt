package com.silmenull.webmusicplayer.models

import java.util.UUID

enum class AppTab {
    FAVOURITES,
    STREAMS,
    SUBSCRIPTIONS
}

enum class StreamOriginKind {
    MANUAL,
    SUBSCRIPTION
}

data class FilterOption(
    val key: String,
    val label: String,
) {
    companion object {
        const val ALL_KEY = "all"
        const val MANUAL_KEY = "manual"
    }
}

data class SubscriptionImportOptions(
    val maxStreamCount: Int = 1000,
)

data class ImportStreamCandidate(
    val name: String,
    val url: String,
    val artworkUrl: String? = null,
)

data class StreamItem(
    val id: String = UUID.randomUUID().toString(),
    val name: String = "",
    val url: String = "",
    val artworkUrl: String? = null,
    val isFavourite: Boolean = false,
    val originKind: StreamOriginKind = StreamOriginKind.MANUAL,
    val subscriptionId: String? = null,
    val subscriptionName: String? = null,
)

data class SubscriptionItem(
    val id: String = UUID.randomUUID().toString(),
    val name: String = "",
    val url: String = "",
    val maxStreamCount: Int = SubscriptionImportOptions().maxStreamCount,
    val lastUpdatedUtcMillis: Long? = null,
)

data class AppState(
    val streams: List<StreamItem> = emptyList(),
    val subscriptions: List<SubscriptionItem> = emptyList(),
    val deletePromptSuppressedUntilUtcMillis: Long? = null,
    val selectedFilterKey: String = FilterOption.ALL_KEY,
    val selectedFilterKeyword: String = "",
)
