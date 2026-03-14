package com.silmenull.webmusicplayer.playback

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Bitmap.CompressFormat
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.util.LruCache
import androidx.core.graphics.drawable.toBitmap
import coil.imageLoader
import coil.request.ImageRequest
import coil.request.SuccessResult
import java.io.ByteArrayOutputStream
import kotlin.math.max
import kotlin.math.min
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

object ArtworkCoverProcessor {
    private const val BLUR_RADIUS = 10

    private val bitmapCache = object : LruCache<String, Bitmap>(maxBitmapCacheBytes()) {
        override fun sizeOf(key: String, value: Bitmap): Int = value.allocationByteCount
    }

    private val bytesCache = object : LruCache<String, ByteArray>(8 * 1024 * 1024) {
        override fun sizeOf(key: String, value: ByteArray): Int = value.size
    }

    suspend fun loadProcessedBitmap(context: Context, artworkUrl: String): Bitmap? = withContext(Dispatchers.IO) {
        bitmapCache.get(artworkUrl)?.let { return@withContext it }

        val appContext = context.applicationContext
        val request = ImageRequest.Builder(appContext)
            .data(artworkUrl)
            .allowHardware(false)
            .build()

        val result = appContext.imageLoader.execute(request) as? SuccessResult ?: return@withContext null
        val drawable = result.drawable
        val origin = drawable.toBitmap(
            width = drawable.intrinsicWidth.takeIf { it > 0 } ?: 1,
            height = drawable.intrinsicHeight.takeIf { it > 0 } ?: 1,
            config = Bitmap.Config.ARGB_8888,
        )

        if (origin.width == origin.height) {
            bitmapCache.put(artworkUrl, origin)
            origin
        } else {
            val processed = makeMusicCover(origin)
            bitmapCache.put(artworkUrl, processed)
            processed
        }
    }

    suspend fun loadProcessedBytes(context: Context, artworkUrl: String): ByteArray? = withContext(Dispatchers.IO) {
        bytesCache.get(artworkUrl)?.let { return@withContext it }

        val bitmap = loadProcessedBitmap(context, artworkUrl) ?: return@withContext null
        val bytes = ByteArrayOutputStream().use { output ->
            bitmap.compress(CompressFormat.PNG, 100, output)
            output.toByteArray()
        }
        bytesCache.put(artworkUrl, bytes)
        bytes
    }

    private fun makeMusicCover(origin: Bitmap): Bitmap {
        val widthHeightMax = max(origin.width, origin.height).coerceAtLeast(1)
        val widthHeightMin = min(origin.width, origin.height).coerceAtLeast(1)
        val fillScale = widthHeightMax.toFloat() / widthHeightMin.toFloat()

        val cover = Bitmap.createBitmap(widthHeightMax, widthHeightMax, Bitmap.Config.ARGB_8888)
        val coverCanvas = Canvas(cover)

        val backgroundBitmap = Bitmap.createBitmap(widthHeightMax, widthHeightMax, Bitmap.Config.ARGB_8888)
        val backgroundCanvas = Canvas(backgroundBitmap)
        backgroundCanvas.drawBitmap(
            origin,
            null,
            RectF(
                widthHeightMax / 2f - origin.width * fillScale / 2f,
                widthHeightMax / 2f - origin.height * fillScale / 2f,
                widthHeightMax / 2f + origin.width * fillScale / 2f,
                widthHeightMax / 2f + origin.height * fillScale / 2f,
            ),
            Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG),
        )

        gaussianBlur(backgroundBitmap, BLUR_RADIUS)
        coverCanvas.drawBitmap(backgroundBitmap, 0f, 0f, Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG))

        coverCanvas.drawBitmap(
            origin,
            null,
            RectF(
                widthHeightMax / 2f - origin.width / 2f,
                widthHeightMax / 2f - origin.height / 2f,
                widthHeightMax / 2f + origin.width / 2f,
                widthHeightMax / 2f + origin.height / 2f,
            ),
            Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG),
        )

        backgroundBitmap.recycle()
        return cover
    }

    private fun gaussianBlur(bitmap: Bitmap, radius: Int) {
        if (radius <= 0) {
            return
        }

        repeat(3) {
            boxBlur(bitmap, radius)
        }
    }

    private fun boxBlur(bitmap: Bitmap, radius: Int) {
        val width = bitmap.width
        val height = bitmap.height
        if (width <= 1 || height <= 1) {
            return
        }

        val source = IntArray(width * height)
        val horizontal = IntArray(width * height)
        val output = IntArray(width * height)
        bitmap.getPixels(source, 0, width, 0, 0, width, height)

        val divisor = radius * 2 + 1

        for (y in 0 until height) {
            var alphaSum = 0
            var redSum = 0
            var greenSum = 0
            var blueSum = 0

            for (offset in -radius..radius) {
                val pixel = source[y * width + clamp(offset, 0, width - 1)]
                alphaSum += pixel ushr 24
                redSum += pixel shr 16 and 0xFF
                greenSum += pixel shr 8 and 0xFF
                blueSum += pixel and 0xFF
            }

            for (x in 0 until width) {
                horizontal[y * width + x] =
                    ((alphaSum / divisor) shl 24) or
                        ((redSum / divisor) shl 16) or
                        ((greenSum / divisor) shl 8) or
                        (blueSum / divisor)

                val removePixel = source[y * width + clamp(x - radius, 0, width - 1)]
                val addPixel = source[y * width + clamp(x + radius + 1, 0, width - 1)]

                alphaSum += (addPixel ushr 24) - (removePixel ushr 24)
                redSum += (addPixel shr 16 and 0xFF) - (removePixel shr 16 and 0xFF)
                greenSum += (addPixel shr 8 and 0xFF) - (removePixel shr 8 and 0xFF)
                blueSum += (addPixel and 0xFF) - (removePixel and 0xFF)
            }
        }

        for (x in 0 until width) {
            var alphaSum = 0
            var redSum = 0
            var greenSum = 0
            var blueSum = 0

            for (offset in -radius..radius) {
                val pixel = horizontal[clamp(offset, 0, height - 1) * width + x]
                alphaSum += pixel ushr 24
                redSum += pixel shr 16 and 0xFF
                greenSum += pixel shr 8 and 0xFF
                blueSum += pixel and 0xFF
            }

            for (y in 0 until height) {
                output[y * width + x] =
                    ((alphaSum / divisor) shl 24) or
                        ((redSum / divisor) shl 16) or
                        ((greenSum / divisor) shl 8) or
                        (blueSum / divisor)

                val removePixel = horizontal[clamp(y - radius, 0, height - 1) * width + x]
                val addPixel = horizontal[clamp(y + radius + 1, 0, height - 1) * width + x]

                alphaSum += (addPixel ushr 24) - (removePixel ushr 24)
                redSum += (addPixel shr 16 and 0xFF) - (removePixel shr 16 and 0xFF)
                greenSum += (addPixel shr 8 and 0xFF) - (removePixel shr 8 and 0xFF)
                blueSum += (addPixel and 0xFF) - (removePixel and 0xFF)
            }
        }

        bitmap.setPixels(output, 0, width, 0, 0, width, height)
    }

    private fun clamp(value: Int, min: Int, max: Int): Int {
        return when {
            value < min -> min
            value > max -> max
            else -> value
        }
    }

    private fun maxBitmapCacheBytes(): Int {
        val maxMemory = Runtime.getRuntime().maxMemory().coerceAtMost(Int.MAX_VALUE.toLong()).toInt()
        return (maxMemory / 8).coerceAtLeast(8 * 1024 * 1024)
    }
}