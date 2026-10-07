// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.ui

import android.view.HapticFeedbackConstants
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.awaitHorizontalTouchSlopOrCancellation
import androidx.compose.foundation.gestures.horizontalDrag
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.widthIn
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.input.pointer.positionChange
import androidx.compose.ui.input.pointer.util.VelocityTracker
import androidx.compose.ui.layout.LayoutCoordinates
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import app.caimack.api.Track
import app.caimack.playback.PlayerState
import kotlin.math.PI
import kotlin.math.abs
import kotlin.math.atan2
import kotlin.math.roundToInt
import kotlin.math.sign
import kotlinx.coroutines.launch

// How far the disc slides out of its sleeve while playing, as a share of the sleeve's width.
internal const val RECORD_OUT = 0.30f

// One full turn of the disc scrubs this much of the track, as on the web player.
private const val SCRATCH_MS_PER_TURN = 10_000f

// A haptic tick every this many degrees of scratching, so the grooves can be felt.
private const val GROOVE_DEGREES = 15f

// Share of the sleeve's width a swipe has to travel to change the track without a fling.
private const val SWIPE_COMMIT = 0.25f

// Swiping towards an end of the queue only gives this much, so it reads as "nothing there".
private const val SWIPE_RESISTANCE = 0.25f

private const val FLING_DP_PER_SECOND = 800

/**
 * The record player stage of the full-screen player. Two gestures share it, split the way a real
 * deck splits them: sliding the sleeve swaps the record (the next or previous track follows the
 * finger in), and holding the bare vinyl scratches through the current one.
 */
@Composable
fun Deck(state: PlayerState, onScrub: (Long?) -> Unit, modifier: Modifier = Modifier) {
    val track = state.current ?: return
    val player = LocalContainer.current.player
    val view = LocalView.current
    val scope = rememberCoroutineScope()
    val latest by rememberUpdatedState(state)
    val swipe = remember { Animatable(0f) }
    var swiping by remember { mutableStateOf(false) }
    var scratching by remember { mutableStateOf(false) }
    var twist by remember(track.id) { mutableFloatStateOf(0f) }
    val layout = remember { DeckLayout() }

    // The neighbour that slid in is now the current track: put it back in the middle.
    LaunchedEffect(track.id) { swipe.snapTo(0f) }

    val tick = { view.performHapticFeedback(HapticFeedbackConstants.CLOCK_TICK) }

    Box(
        modifier
            .onGloballyPositioned { layout.deck = it }
            .pointerInput(Unit) {
                awaitEachGesture {
                    val down = awaitFirstDown()
                    val sleeve = layout.sleeve() ?: return@awaitEachGesture
                    val gap = 24.dp.toPx()
                    val step = sleeve.width * (1 + RECORD_OUT) + gap
                    val disc = Rect(sleeve.right, sleeve.top, sleeve.right + sleeve.width * RECORD_OUT, sleeve.bottom)

                    if (latest.playing && disc.contains(down.position)) {
                        // Scratch: the disc turns with the finger around its own centre.
                        val center = Offset(sleeve.left + sleeve.width * (RECORD_OUT + 0.5f), sleeve.center.y)
                        fun angleOf(point: Offset) = atan2(point.y - center.y, point.x - center.x)

                        down.consume()
                        scratching = true
                        player.pause()
                        val length = latest.durationMs.takeIf { it > 0 } ?: (track.durationSeconds * 1000L)
                        var target = player.position.toFloat()
                        var last = angleOf(down.position)
                        var groove = 0f
                        onScrub(target.toLong())

                        while (true) {
                            val change = awaitPointerEvent().changes.firstOrNull { it.id == down.id } ?: break
                            if (!change.pressed) break
                            change.consume()

                            val angle = angleOf(change.position)
                            var delta = angle - last
                            if (delta > PI) delta -= (2 * PI).toFloat()
                            if (delta < -PI) delta += (2 * PI).toFloat()
                            last = angle

                            val degrees = Math.toDegrees(delta.toDouble()).toFloat()
                            twist += degrees
                            groove += abs(degrees)
                            if (groove >= GROOVE_DEGREES) {
                                groove %= GROOVE_DEGREES
                                tick()
                            }
                            target = (target + degrees / 360f * SCRATCH_MS_PER_TURN).coerceIn(0f, length.toFloat())
                            onScrub(target.toLong())
                        }

                        // Scratching only starts on a spinning record, so it always resumes.
                        player.seekTo(target.toLong())
                        player.resume()
                        scratching = false
                        onScrub(null)
                        return@awaitEachGesture
                    }

                    // Swipe: the sleeve follows the finger and the neighbouring record slides in.
                    val velocity = VelocityTracker().apply { addPosition(down.uptimeMillis, down.position) }
                    var dragged = 0f
                    val start = awaitHorizontalTouchSlopOrCancellation(down.id) { change, over ->
                        change.consume()
                        dragged = over
                    } ?: return@awaitEachGesture

                    fun neighbour(towards: Float): Track? = if (towards < 0) latest.next else latest.previous
                    fun shown(distance: Float) = if (neighbour(distance) == null) distance * SWIPE_RESISTANCE else distance
                    fun committing(distance: Float) = neighbour(distance) != null && abs(distance) > sleeve.width * SWIPE_COMMIT

                    swiping = true
                    var armed = committing(dragged)
                    scope.launch { swipe.snapTo(shown(dragged)) }

                    horizontalDrag(start.id) { change ->
                        dragged += change.positionChange().x
                        change.consume()
                        velocity.addPosition(change.uptimeMillis, change.position)
                        // Crossing the point of no return clicks once, either way.
                        if (committing(dragged) != armed) {
                            armed = !armed
                            tick()
                        }
                        scope.launch { swipe.snapTo(shown(dragged)) }
                    }

                    val speed = velocity.calculateVelocity().x
                    val flung = abs(speed) > FLING_DP_PER_SECOND.dp.toPx() && sign(speed) == sign(dragged)
                    val toNext = dragged < 0
                    val index = if (toNext) latest.nextIndex else latest.previousIndex
                    val commit = neighbour(dragged) != null && (armed || flung)

                    scope.launch {
                        if (commit) {
                            swipe.animateTo(if (toNext) -step else step, tween(220))
                            player.skipTo(index)
                        } else {
                            swipe.animateTo(0f, spring())
                        }
                        swiping = false
                    }
                }
            },
        contentAlignment = Alignment.Center,
    ) {
        Box(
            Modifier
                .fillMaxWidth(0.72f)
                .widthIn(max = 320.dp)
                .offset(x = (-40).dp)
                .aspectRatio(1f)
                .onGloballyPositioned { layout.record = it },
        ) {
            val offset = swipe.value
            if (offset != 0f) {
                val gap = with(LocalDensity.current) { 24.dp.toPx() }
                val step = (layout.record?.size?.width ?: 0) * (1 + RECORD_OUT) + gap
                state.previous?.takeIf { offset > 0 }?.let { Neighbour(it, offset - step) }
                state.next?.takeIf { offset < 0 }?.let { Neighbour(it, offset + step) }
            }

            Record(
                track,
                spinning = state.playing && !state.buffering,
                out = (state.playing || scratching) && !swiping,
                twist = twist,
                modifier = Modifier.fillMaxSize().offset { IntOffset(swipe.value.roundToInt(), 0) },
            )
        }
    }
}

@Composable
private fun Neighbour(track: Track, x: Float) {
    Cover(
        trackCover(track, small = false),
        track.albumTitle ?: track.title,
        Modifier.fillMaxSize().offset { IntOffset(x.roundToInt(), 0) }.shadow(18.dp, Radius.cover),
    )
}

// Where the sleeve sits inside the deck, read at gesture time so rotation or resizing never
// leaves a stale hit area behind.
private class DeckLayout {
    var deck: LayoutCoordinates? = null
    var record: LayoutCoordinates? = null

    fun sleeve(): Rect? {
        val deck = deck?.takeIf { it.isAttached } ?: return null
        val record = record?.takeIf { it.isAttached } ?: return null
        return deck.localBoundingBoxOf(record, clipBounds = false)
    }
}
