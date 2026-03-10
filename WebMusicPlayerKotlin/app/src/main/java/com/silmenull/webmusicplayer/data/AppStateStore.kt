package com.silmenull.webmusicplayer.data

import android.content.Context
import com.silmenull.webmusicplayer.models.AppState
import com.silmenull.webmusicplayer.models.StreamItem
import com.silmenull.webmusicplayer.models.StreamOriginKind
import com.silmenull.webmusicplayer.models.SubscriptionItem
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.File

class AppStateStore(context: Context) {
    private val gate = Mutex()
    private val stateFile = File(context.filesDir, "app-state.json")

    suspend fun load(): AppState = gate.withLock {
        withContext(Dispatchers.IO) {
            if (!stateFile.exists()) {
                return@withContext AppState()
            }

            runCatching {
                val root = JSONObject(stateFile.readText(Charsets.UTF_8))
                AppState(
                    streams = root.optJSONArray("streams").toStreamItems(),
                    subscriptions = root.optJSONArray("subscriptions").toSubscriptionItems(),
                    deletePromptSuppressedUntilUtcMillis = root.optNullableLong("deletePromptSuppressedUntilUtcMillis"),
                    selectedFilterKey = root.optString("selectedFilterKey").ifBlank { "all" },
                    selectedFilterKeyword = root.optString("selectedFilterKeyword"),
                )
            }.getOrElse {
                AppState()
            }
        }
    }

    suspend fun save(state: AppState) = gate.withLock {
        withContext(Dispatchers.IO) {
            stateFile.parentFile?.mkdirs()
            stateFile.writeText(state.toJson().toString(2), Charsets.UTF_8)
        }
    }

    private fun AppState.toJson(): JSONObject = JSONObject().apply {
        put("streams", JSONArray().apply {
            streams.forEach { stream ->
                put(JSONObject().apply {
                    put("id", stream.id)
                    put("name", stream.name)
                    put("url", stream.url)
                    put("artworkUrl", stream.artworkUrl)
                    put("isFavourite", stream.isFavourite)
                    put("originKind", stream.originKind.name)
                    put("subscriptionId", stream.subscriptionId)
                    put("subscriptionName", stream.subscriptionName)
                })
            }
        })
        put("subscriptions", JSONArray().apply {
            subscriptions.forEach { subscription ->
                put(JSONObject().apply {
                    put("id", subscription.id)
                    put("name", subscription.name)
                    put("url", subscription.url)
                    put("maxStreamCount", subscription.maxStreamCount)
                    put("lastUpdatedUtcMillis", subscription.lastUpdatedUtcMillis)
                })
            }
        })
        put("deletePromptSuppressedUntilUtcMillis", deletePromptSuppressedUntilUtcMillis)
        put("selectedFilterKey", selectedFilterKey)
        put("selectedFilterKeyword", selectedFilterKeyword)
    }

    private fun JSONArray?.toStreamItems(): List<StreamItem> {
        if (this == null) {
            return emptyList()
        }

        return buildList(length()) {
            for (index in 0 until length()) {
                val item = optJSONObject(index) ?: continue
                add(
                    StreamItem(
                        id = item.optString("id").ifBlank { java.util.UUID.randomUUID().toString() },
                        name = item.optString("name"),
                        url = item.optString("url"),
                        artworkUrl = item.optString("artworkUrl").takeIf { it.isNotBlank() },
                        isFavourite = item.optBoolean("isFavourite"),
                        originKind = runCatching {
                            StreamOriginKind.valueOf(item.optString("originKind"))
                        }.getOrDefault(StreamOriginKind.MANUAL),
                        subscriptionId = item.optString("subscriptionId").takeIf { it.isNotBlank() },
                        subscriptionName = item.optString("subscriptionName").takeIf { it.isNotBlank() },
                    )
                )
            }
        }
    }

    private fun JSONArray?.toSubscriptionItems(): List<SubscriptionItem> {
        if (this == null) {
            return emptyList()
        }

        return buildList(length()) {
            for (index in 0 until length()) {
                val item = optJSONObject(index) ?: continue
                add(
                    SubscriptionItem(
                        id = item.optString("id").ifBlank { java.util.UUID.randomUUID().toString() },
                        name = item.optString("name"),
                        url = item.optString("url"),
                        maxStreamCount = item.optInt("maxStreamCount", 1000),
                        lastUpdatedUtcMillis = item.optNullableLong("lastUpdatedUtcMillis"),
                    )
                )
            }
        }
    }

    private fun JSONObject.optNullableLong(key: String): Long? {
        return if (has(key) && !isNull(key)) optLong(key) else null
    }
}
