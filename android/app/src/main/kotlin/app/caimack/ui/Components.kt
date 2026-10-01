// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.ui

import android.util.Log
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import app.caimack.AppContainer
import app.caimack.R
import app.caimack.api.Track
import coil3.compose.SubcomposeAsyncImage
import java.util.concurrent.ConcurrentHashMap
import java.io.IOException
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.delay

val LocalContainer = staticCompositionLocalOf<AppContainer> { error("No container") }

object Remote {
    val cache = ConcurrentHashMap<String, Any>()
}

@Composable
fun <T : Any> Load(
    key: String,
    load: suspend () -> T,
    isEmpty: (T) -> Boolean = { false },
    empty: @Composable () -> Unit = {},
    content: @Composable (T) -> Unit,
) {
    @Suppress("UNCHECKED_CAST")
    var value by remember(key) { mutableStateOf(Remote.cache[key] as T?) }
    var failed by remember(key) { mutableStateOf(false) }
    var attempt by remember(key) { mutableIntStateOf(0) }

    LaunchedEffect(key, attempt) {
        failed = false
        runCatching { withNetworkRetries(load) }
            .onSuccess {
                Remote.cache[key] = it
                value = it
            }
            .onFailure {
                if (it is CancellationException) throw it
                Log.w("Caimack", "Loading $key failed", it)
                failed = value == null
            }
    }

    val shown = value
    when {
        shown != null -> if (isEmpty(shown)) empty() else content(shown)
        failed -> Failure { attempt++ }
        else -> Box(Modifier.fillMaxWidth().padding(vertical = 64.dp), contentAlignment = Alignment.Center) {
            RecordLoading(44.dp)
        }
    }
}

suspend fun <T> withNetworkRetries(load: suspend () -> T): T {
    for (pause in NETWORK_RETRY_PAUSES) {
        try {
            return load()
        } catch (_: IOException) {
            delay(pause)
        }
    }
    return load()
}

private val NETWORK_RETRY_PAUSES = listOf(1_000L, 3_000L)

@Composable
fun Failure(retry: () -> Unit) {
    val palette = LocalPalette.current
    Column(
        Modifier.fillMaxWidth().padding(vertical = 48.dp, horizontal = 16.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Icon(Lucide.TriangleAlert, null, tint = palette.destructive)
        Text(stringResource(R.string.error_load), style = Type.small, color = palette.muted)
        TextButton(onClick = retry) { Text(stringResource(R.string.action_try_again), color = palette.foreground) }
    }
}

@Composable
fun Empty(title: String, description: String? = null) {
    val palette = LocalPalette.current
    Column(
        Modifier.fillMaxWidth().padding(vertical = 48.dp, horizontal = 24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Text(title, style = Type.section, textAlign = TextAlign.Center)
        description?.let { Text(it, style = Type.small, color = palette.muted, textAlign = TextAlign.Center) }
    }
}

@Composable
fun RecordLoading(size: Dp) {
    val palette = LocalPalette.current
    val armed = size > 24.dp
    val spin = rememberInfiniteTransition(label = "record")
    val angle by spin.animateFloat(0f, 360f, infiniteRepeatable(tween(1800, easing = LinearEasing)), label = "spin")
    val track by spin.animateFloat(35f, 41f, infiniteRepeatable(tween(3000), RepeatMode.Reverse), label = "track")
    val drop = remember { Animatable(6f) }
    LaunchedEffect(Unit) { drop.animateTo(37f, tween(700)) }

    Canvas(Modifier.width(if (armed) size * 1.3f else size).height(size)) {
        val disc = this.size.height
        val radius = disc / 2
        val center = Offset(radius, radius)

        drawCircle(Color(0xFF121010), radius, center)
        var groove = radius * 0.42f
        while (groove < radius * 0.96f) {
            drawCircle(Color(0xFF221E1C), groove, center, style = Stroke(width = 1f))
            groove += 2.4f
        }
        drawCircle(palette.borderStrong, radius, center, style = Stroke(width = 1.dp.toPx()))
        drawCircle(palette.primary, radius * 0.36f, center)
        drawCircle(palette.background, radius * 0.08f, center)

        rotate(angle, center) {
            if (armed) {
                drawRect(palette.onPrimary, Offset(radius - disc * 0.02f, disc * 0.34f), Size(disc * 0.04f, disc * 0.09f))
            } else {
                drawRoundRect(
                    palette.primary,
                    Offset(radius - disc * 0.07f, disc * 0.06f),
                    Size(disc * 0.14f, disc * 0.18f),
                    CornerRadius(disc * 0.07f),
                )
            }
        }

        if (armed) {
            val pivot = Offset(disc * 1.18f + disc * 0.0225f, disc * 0.06f)
            val swing = if (drop.isRunning) drop.value else track
            rotate(swing, pivot) {
                drawLine(palette.faint, pivot, pivot + Offset(0f, disc * 0.83f), disc * 0.045f, StrokeCap.Round)
                drawCircle(palette.faint, disc * 0.08f, pivot)
                drawRect(palette.primary, pivot + Offset(-disc * 0.045f, disc * 0.83f - disc * 0.105f), Size(disc * 0.09f, disc * 0.15f))
            }
        }
    }
}

@Composable
fun Cover(url: String?, name: String, modifier: Modifier = Modifier, round: Boolean = false) {
    val palette = LocalPalette.current
    val shape: Shape = if (round) CircleShape else Radius.cover

    Box(modifier.clip(shape).background(palette.accent)) {
        val fallback = @Composable {
            if (round) {
                Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Text(initialsOf(name), style = Type.section, color = palette.faint)
                }
            } else {
                Sleeve(name, Modifier.fillMaxSize())
            }
        }

        if (url == null) {
            fallback()
        } else {
            SubcomposeAsyncImage(
                model = url,
                contentDescription = null,
                contentScale = ContentScale.Crop,
                modifier = Modifier.fillMaxSize(),
                error = { fallback() },
            )
        }
    }
}

private fun initialsOf(name: String): String {
    val words = name.trim().split(Regex("\\s+")).filter { it.isNotEmpty() }
    return when (words.size) {
        0 -> "?"
        1 -> words[0].take(2).uppercase()
        else -> "${words[0][0]}${words[1][0]}".uppercase()
    }
}

@Composable
fun trackCover(track: Track, small: Boolean = true): String? = LocalContainer.current.media.cover(track.albumId, track.id, track.hasCover, small)

fun artistsOf(track: Track): String = track.artists?.takeIf { it.isNotEmpty() }?.joinToString(", ") { it.name } ?: track.artistName

fun durationOf(seconds: Int): String = "%d:%02d".format(seconds / 60, seconds % 60)

@Composable
fun SectionHeader(title: String, note: String? = null, onSeeAll: (() -> Unit)? = null) {
    val palette = LocalPalette.current
    Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp), verticalAlignment = Alignment.Bottom) {
        Column(Modifier.weight(1f)) {
            Text(title, style = Type.section, maxLines = 1, overflow = TextOverflow.Ellipsis)
            note?.let { Text(it, style = Type.small, color = palette.muted, maxLines = 1, overflow = TextOverflow.Ellipsis) }
        }
        onSeeAll?.let {
            Row(Modifier.clickable(onClick = it).padding(start = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                Text(stringResource(R.string.action_see_all), style = Type.small, color = palette.muted)
                Icon(Lucide.ChevronRight, null, tint = palette.muted, modifier = Modifier.size(16.dp))
            }
        }
    }
}

@Composable
fun Card(title: String, subtitle: String, onClick: () -> Unit, modifier: Modifier = Modifier, round: Boolean = false, cover: @Composable (Modifier) -> Unit) {
    val palette = LocalPalette.current
    Column(modifier.clickable(onClick = onClick)) {
        cover(Modifier.fillMaxWidth().aspectRatio(1f))
        Text(
            title,
            style = Type.small.copy(fontWeight = FontWeight.Medium),
            maxLines = if (round) 1 else 2,
            overflow = TextOverflow.Ellipsis,
            textAlign = if (round) TextAlign.Center else TextAlign.Start,
            modifier = Modifier.fillMaxWidth().padding(top = 10.dp),
        )
        Text(
            subtitle,
            style = Type.small,
            color = palette.muted,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            textAlign = if (round) TextAlign.Center else TextAlign.Start,
            modifier = Modifier.fillMaxWidth(),
        )
    }
}

@Composable
fun TrackRow(track: Track, onClick: () -> Unit, index: Int? = null) {
    val palette = LocalPalette.current
    val current = LocalContainer.current.player.state.collectAsState().value.current?.id == track.id
    Row(
        Modifier.fillMaxWidth().clip(Radius.row).clickable(onClick = onClick).padding(horizontal = 16.dp, vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        index?.let {
            Text("$it", style = Type.small, color = palette.faint, textAlign = TextAlign.End, modifier = Modifier.width(22.dp))
        }
        Cover(trackCover(track), track.albumTitle ?: track.title, Modifier.size(40.dp))
        Column(Modifier.weight(1f)) {
            Text(
                track.title,
                style = Type.small.copy(fontWeight = FontWeight.Medium),
                color = if (current) palette.primary else palette.foreground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(artistsOf(track), style = Type.small, color = palette.muted, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        if (LocalContainer.current.downloads.state.collectAsState().value[track.id]?.done == true) {
            Icon(Lucide.CircleCheck, null, tint = palette.faint, modifier = Modifier.size(14.dp))
        }
        Text(durationOf(track.durationSeconds), style = Type.tiny, color = palette.faint)
    }
}

@Composable
fun DetailHeader(
    kind: String,
    title: String,
    facts: List<String>,
    description: String? = null,
    round: Boolean = false,
    cover: @Composable (Modifier) -> Unit,
) {
    val palette = LocalPalette.current
    Column(Modifier.fillMaxWidth().padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        cover(Modifier.size(176.dp).then(if (round) Modifier.clip(CircleShape) else Modifier))
        Text(kind, style = Type.small, color = palette.muted)
        Text(title, style = Type.display)
        description?.let { Text(it, style = Type.small, color = palette.muted) }
        if (facts.isNotEmpty()) {
            Row(horizontalArrangement = Arrangement.spacedBy(20.dp)) {
                facts.forEach { Text(it, style = Type.small, color = palette.muted) }
            }
        }
    }
}
