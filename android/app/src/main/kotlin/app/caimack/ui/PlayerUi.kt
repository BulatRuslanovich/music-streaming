// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.ui

import androidx.activity.compose.BackHandler
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.systemGestureExclusion
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.Snackbar
import androidx.compose.material3.SnackbarDuration
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.SnackbarResult
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.media3.common.Player
import app.caimack.R
import app.caimack.api.Lyrics
import app.caimack.api.Track
import app.caimack.playback.PlayerState
import app.caimack.playback.QueueSnapshot
import kotlin.math.abs
import kotlin.math.roundToInt
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

@Composable
private fun rememberPosition(state: PlayerState): Long {
    val player = LocalContainer.current.player
    val position by produceState(player.position, state.index, state.playing) {
        while (true) {
            value = player.position
            delay(if (state.playing) 250 else 1000)
        }
    }
    return position
}

private fun clock(ms: Long) = durationOf((ms / 1000).toInt())

@Composable
fun MiniPlayer(state: PlayerState, onOpen: () -> Unit) {
    val palette = LocalPalette.current
    val player = LocalContainer.current.player
    val track = state.current ?: return
    val position = rememberPosition(state)
    val duration = state.durationMs.takeIf { it > 0 } ?: (track.durationSeconds * 1000L)
    val swipe = remember { Animatable(0f) }
    val scope = rememberCoroutineScope()
    val skipPx = with(LocalDensity.current) { 80.dp.toPx() }
    val openPx = with(LocalDensity.current) { 40.dp.toPx() }

    Column(
        Modifier
            .background(palette.card)
            .clickable(onClick = onOpen)
            .pointerInput(Unit) {
                var total = Offset.Zero
                val settle = { scope.launch { swipe.animateTo(0f, tween(200)) } }
                detectDragGestures(
                    onDragStart = { total = Offset.Zero },
                    onDragEnd = {
                        if (abs(total.x) > abs(total.y)) {
                            if (total.x <= -skipPx) player.next() else if (total.x >= skipPx) player.previous()
                        } else if (-total.y > openPx) {
                            onOpen()
                        }
                        settle()
                    },
                    onDragCancel = { settle() },
                ) { change, amount ->
                    change.consume()
                    total += amount
                    if (abs(total.x) > abs(total.y)) scope.launch { swipe.snapTo(total.x) }
                }
            },
    ) {
        HorizontalDivider(color = palette.border)
        Row(
            Modifier
                .fillMaxWidth()
                .graphicsLayer {
                    translationX = swipe.value
                    alpha = 1f - (abs(swipe.value) / (skipPx * 3)).coerceAtMost(0.5f)
                }
                .padding(start = 12.dp, end = 8.dp, top = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Cover(trackCover(track), track.albumTitle ?: track.title, Modifier.size(46.dp))
            Column(Modifier.weight(1f)) {
                Text(track.title, style = Type.body.copy(fontWeight = FontWeight.Medium), color = palette.foreground, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(artistsOf(track), style = Type.small, color = palette.muted, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            Box(contentAlignment = Alignment.Center) {
                IconAction(if (state.playing) Lucide.Pause else Lucide.Play, stringResource(if (state.playing) R.string.action_pause else R.string.action_play)) { player.toggle() }
                BufferingRing(state.buffering, Modifier.size(40.dp))
            }
            IconAction(Lucide.SkipForward, stringResource(R.string.player_next)) { player.next() }
            IconAction(Lucide.ChevronUp, stringResource(R.string.player_open_full), onClick = onOpen)
        }
        Row(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 4.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(clock(position), style = Type.micro, color = palette.muted)
            Box(Modifier.weight(1f).height(3.dp).clip(CircleShape).background(palette.borderStrong)) {
                Box(Modifier.fillMaxWidth((position.toFloat() / duration).coerceIn(0f, 1f)).height(3.dp).background(palette.foreground))
            }
            Text(clock(duration), style = Type.micro, color = palette.muted)
        }
    }
}

@Composable
private fun IconAction(icon: ImageVector, label: String, tint: Color = LocalPalette.current.foreground, size: Int = 22, onClick: () -> Unit) {
    Box(Modifier.size(44.dp).clip(CircleShape).clickable(onClick = onClick), contentAlignment = Alignment.Center) {
        Icon(icon, label, tint = tint, modifier = Modifier.size(size.dp))
    }
}

private enum class Panel { None, Lyrics, Queue }

@Composable
fun FullPlayer(state: PlayerState, onClose: () -> Unit) {
    val palette = LocalPalette.current
    val container = LocalContainer.current
    val player = container.player
    val track = state.current ?: return
    var panel by rememberSaveable { mutableStateOf(Panel.None) }
    var actions by remember { mutableStateOf(false) }
    container.favorites.state.collectAsState().value
    val liked = container.favorites.isFavorite(track)
    val note = container.radio.state.collectAsState().value.notes[track.id]
    val snackbar = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()
    val undo = stringResource(R.string.action_undo)
    val undoable: (String, QueueSnapshot) -> Unit = { message, snapshot ->
        scope.launch {
            snackbar.currentSnackbarData?.dismiss()
            if (snackbar.showSnackbar(message, undo, duration = SnackbarDuration.Short) == SnackbarResult.ActionPerformed) player.restore(snapshot)
        }
    }

    if (actions) TrackSheet(track) { actions = false }
    val position = rememberPosition(state)
    val duration = state.durationMs.takeIf { it > 0 } ?: (track.durationSeconds * 1000L)

    BackHandler(onBack = onClose)

    val view = LocalView.current
    DisposableEffect(view) {
        view.keepScreenOn = true
        onDispose { view.keepScreenOn = false }
    }

    Surface(Modifier.fillMaxSize(), color = palette.background) {
        Box(Modifier.fillMaxSize()) {
        BoxWithConstraints(Modifier.safeDrawingPadding().padding(horizontal = 20.dp, vertical = 12.dp)) {
            val wide = maxWidth > maxHeight

            val header = @Composable {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    IconAction(Lucide.ChevronDown, stringResource(R.string.player_close_full), onClick = onClose)
                    Text(stringResource(R.string.player_now_playing), style = Type.small, color = palette.muted, textAlign = TextAlign.Center, modifier = Modifier.weight(1f))
                    IconAction(Lucide.MicVocal, stringResource(R.string.lyrics_title), tint = if (panel == Panel.Lyrics) palette.primary else palette.foreground) {
                        panel = if (panel == Panel.Lyrics) Panel.None else Panel.Lyrics
                    }
                    IconAction(Lucide.ListVideo, stringResource(R.string.queue_title), tint = if (panel == Panel.Queue) palette.primary else palette.foreground) {
                        panel = if (panel == Panel.Queue) Panel.None else Panel.Queue
                    }
                }
            }

            val stage: @Composable (Modifier) -> Unit = { modifier ->
                Box(modifier) {
                    when (panel) {
                        Panel.None -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                            Record(track, state.playing && !state.buffering, Modifier.fillMaxWidth(0.72f).widthIn(max = 320.dp).offset(x = (-40).dp))
                        }
                        Panel.Lyrics -> LyricsPanel(track, position)
                        Panel.Queue -> QueuePanel(state, undoable)
                    }
                }
            }

            val controls = @Composable {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            Text(track.title, style = Type.display, maxLines = if (wide) 1 else 3, overflow = TextOverflow.Ellipsis)
                            Text(artistsOf(track), style = Type.body, color = palette.muted, maxLines = 1, overflow = TextOverflow.Ellipsis)
                            note?.let { RadioNoteLine(it) }
                        }
                        IconAction(
                            if (liked) Lucide.HeartFilled else Lucide.Heart,
                            stringResource(if (liked) R.string.menu_unlike else R.string.menu_like),
                            tint = if (liked) palette.primary else palette.muted,
                        ) { container.favorites.toggle(track) }
                        IconAction(Lucide.EllipsisVertical, stringResource(R.string.menu_more), tint = palette.muted) { actions = true }
                    }
                    Spacer(Modifier.height(8.dp))
                    Seekbar(position, duration) { player.seekTo(it) }
                    Row {
                        Text(clock(position), style = Type.tiny, color = palette.muted)
                        Spacer(Modifier.weight(1f))
                        Text(clock(duration), style = Type.tiny, color = palette.muted)
                    }
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                        IconAction(Lucide.Shuffle, stringResource(R.string.player_shuffle), tint = if (state.shuffle) palette.primary else palette.muted) { player.toggleShuffle() }
                        IconAction(Lucide.SkipBack, stringResource(R.string.player_previous), size = 26) { player.previous() }
                        Box(contentAlignment = Alignment.Center) {
                            Box(
                                Modifier.size(68.dp).clip(CircleShape).background(palette.action).clickable { player.toggle() },
                                contentAlignment = Alignment.Center,
                            ) {
                                Icon(
                                    if (state.playing) Lucide.Pause else Lucide.Play,
                                    stringResource(if (state.playing) R.string.action_pause else R.string.action_play),
                                    tint = palette.onAction,
                                    modifier = Modifier.size(28.dp),
                                )
                            }
                            BufferingRing(state.buffering, Modifier.size(78.dp))
                        }
                        IconAction(Lucide.SkipForward, stringResource(R.string.player_next), size = 26) { player.next() }
                        IconAction(
                            if (state.repeat == Player.REPEAT_MODE_ONE) Lucide.Repeat1 else Lucide.Repeat,
                            stringResource(R.string.player_repeat),
                            tint = if (state.repeat == Player.REPEAT_MODE_OFF) palette.muted else palette.primary,
                        ) { player.cycleRepeat() }
                    }
                }
            }

            if (wide) {
                Row(horizontalArrangement = Arrangement.spacedBy(24.dp)) {
                    stage(Modifier.weight(1f).fillMaxHeight())
                    Column(Modifier.weight(1f).fillMaxHeight()) {
                        header()
                        Spacer(Modifier.weight(1f))
                        controls()
                    }
                }
            } else {
                Column {
                    header()
                    stage(Modifier.weight(1f).fillMaxWidth())
                    controls()
                }
            }
        }
        SnackbarHost(snackbar, Modifier.align(Alignment.BottomCenter).safeDrawingPadding().padding(16.dp)) { data ->
            Snackbar(data, containerColor = palette.popover, contentColor = palette.foreground, actionColor = palette.primary)
        }
        }
    }
}

@Composable
private fun Seekbar(positionMs: Long, durationMs: Long, onSeek: (Long) -> Unit) {
    val palette = LocalPalette.current
    var dragging by remember { mutableStateOf<Float?>(null) }
    val progress = dragging ?: (positionMs.toFloat() / durationMs).coerceIn(0f, 1f)

    BoxWithConstraints(
        Modifier
            .fillMaxWidth()
            .height(24.dp)
            .systemGestureExclusion()
            .pointerInput(durationMs) {
                detectTapGestures { onSeek((it.x / size.width * durationMs).toLong()) }
            }
            .pointerInput(durationMs) {
                detectHorizontalDragGestures(
                    onDragStart = { dragging = (it.x / size.width).coerceIn(0f, 1f) },
                    onDragEnd = {
                        dragging?.let { onSeek((it * durationMs).toLong()) }
                        dragging = null
                    },
                    onDragCancel = { dragging = null },
                ) { change, _ -> dragging = (change.position.x / size.width).coerceIn(0f, 1f) }
            },
        contentAlignment = Alignment.CenterStart,
    ) {
        val fill = if (dragging != null) palette.primary else palette.foreground
        Box(Modifier.fillMaxWidth().height(5.dp).clip(CircleShape).background(palette.borderStrong)) {
            Box(Modifier.fillMaxWidth(progress).height(5.dp).background(fill))
        }
        if (dragging != null) {
            val width = with(LocalDensity.current) { maxWidth.toPx() }
            val thumb = with(LocalDensity.current) { 6.5.dp.toPx() }
            Box(Modifier.offset { IntOffset((width * progress - thumb).roundToInt(), 0) }.size(13.dp).clip(CircleShape).background(palette.foreground))
        }
    }
}

@Composable
fun Record(track: Track, playing: Boolean, modifier: Modifier = Modifier) {
    val out by animateFloatAsState(if (playing) 0.30f else 0f, tween(650), label = "record-out")
    val angle = remember(track.id) { Animatable(0f) }

    LaunchedEffect(playing, track.id) {
        while (playing) {
            angle.animateTo(angle.value + 360f, tween(1800, easing = LinearEasing))
            angle.snapTo(angle.value % 360f)
        }
    }

    BoxWithConstraints(modifier.aspectRatio(1f)) {
        val edge = maxWidth
        val slide = with(LocalDensity.current) { edge.toPx() }
        Box(Modifier.fillMaxSize().padding(edge * 0.02f).offset { IntOffset((slide * out).roundToInt(), 0) }) {
            Box(Modifier.fillMaxSize().shadow(16.dp, CircleShape).graphicsLayer { rotationZ = angle.value }.clip(CircleShape)) {
                Canvas(Modifier.fillMaxSize()) {
                    val radius = size.minDimension / 2
                    drawCircle(Color(0xFF121010), radius)
                    var groove = radius * 0.34f
                    while (groove < radius) {
                        drawCircle(Color(0xFF221E1C), groove, style = Stroke(width = 1.3.dp.toPx()))
                        groove += 2.4.dp.toPx()
                    }
                    drawCircle(Color(0xFF121010), radius * 0.66f * 0.5f, style = Stroke(width = radius * 0.015f))
                }
                Cover(trackCover(track), track.albumTitle ?: track.title, Modifier.align(Alignment.Center).fillMaxSize(0.32f), round = true)
                Box(Modifier.align(Alignment.Center).fillMaxSize(0.032f).clip(CircleShape).background(LocalPalette.current.background))
            }
            Canvas(Modifier.fillMaxSize()) {
                val radius = size.minDimension / 2
                val sheen = Brush.sweepGradient(
                    0f to Color.Transparent,
                    0.08f to Color.Transparent,
                    0.12f to Color.White.copy(alpha = 0.09f),
                    0.17f to Color.Transparent,
                    0.50f to Color.Transparent,
                    0.62f to Color.White.copy(alpha = 0.07f),
                    0.67f to Color.Transparent,
                    1f to Color.Transparent,
                    center = center,
                )
                rotate(-70f) {
                    drawCircle(sheen, radius * 0.83f, style = Stroke(width = radius * 0.66f))
                }
            }
        }
        Cover(trackCover(track, small = false), track.albumTitle ?: track.title, Modifier.fillMaxSize().shadow(18.dp, Radius.cover))
    }
}

@Composable
private fun LyricsPanel(track: Track, positionMs: Long) {
    val palette = LocalPalette.current
    val container = LocalContainer.current

    Load(
        "lyrics:${track.id}",
        { container.api.lyrics(track.id).body() ?: Lyrics() },
        isEmpty = { it.plain.isBlank() && it.lines.isEmpty() },
        empty = { Empty(stringResource(R.string.lyrics_none)) },
    ) { lyrics ->
        if (lyrics.lines.isEmpty()) {
            LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(vertical = 16.dp)) {
                item { Text(lyrics.plain, style = Type.body.copy(lineHeight = Type.body.lineHeight * 1.2f), color = palette.foreground) }
            }
            return@Load
        }

        val active = lyrics.lines.indexOfLast { it.at <= positionMs }
        val list = rememberLazyListState()
        LaunchedEffect(active) { if (active >= 0) list.animateScrollToItem((active - 2).coerceAtLeast(0)) }

        LazyColumn(Modifier.fillMaxSize(), state = list, contentPadding = PaddingValues(vertical = 24.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            itemsIndexed(lyrics.lines) { index, line ->
                Text(
                    line.text.ifBlank { "♪" },
                    style = Type.section.copy(fontSize = Type.section.fontSize * 1.15f),
                    color = when {
                        index == active -> palette.foreground
                        index < active -> palette.faint
                        else -> palette.muted
                    },
                    modifier = Modifier.clickable { container.player.seekTo(line.at) },
                )
            }
        }
    }
}
