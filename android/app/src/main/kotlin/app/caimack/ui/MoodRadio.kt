// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.ui

import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import app.caimack.R
import app.caimack.api.RadioRequest
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

private const val MOODS = "moods"

// Список настроений приходит с сервера, так что незнакомое клиенту показывается по ключу.
@Composable
fun moodLabel(key: String): String = when (key) {
    "workout" -> stringResource(R.string.mood_workout)
    "drive" -> stringResource(R.string.mood_drive)
    "party" -> stringResource(R.string.mood_party)
    "focus" -> stringResource(R.string.mood_focus)
    "chill" -> stringResource(R.string.mood_chill)
    "sleep" -> stringResource(R.string.mood_sleep)
    "happy" -> stringResource(R.string.mood_happy)
    "sad" -> stringResource(R.string.mood_sad)
    else -> key.replaceFirstChar { it.uppercase() }
}

fun moodIcon(key: String): ImageVector = when (key) {
    "workout" -> Lucide.Dumbbell
    "drive" -> Lucide.Car
    "party" -> Lucide.PartyPopper
    "focus" -> Lucide.Target
    "chill" -> Lucide.Coffee
    "sleep" -> Lucide.Moon
    "happy" -> Lucide.Sun
    "sad" -> Lucide.CloudRain
    else -> Lucide.Radio
}

private class MoodColors(val top: Color, val bottom: Color, val ink: Color)

// Те же цвета, что у плиток на вебе (frontend/src/app/styles/mood.css): общий OKLCH, свой оттенок у
// каждого настроения, заранее переведённые в sRGB — Compose OKLCH не умеет.
private val DARK = mapOf(
    "workout" to MoodColors(Color(0xFF923B30), Color(0xFF5E1B13), Color(0xFFFFF1ED)),
    "happy" to MoodColors(Color(0xFF755600), Color(0xFF493100), Color(0xFFFAF5E6)),
    "chill" to MoodColors(Color(0xFF146D34), Color(0xFF004316), Color(0xFFECF9EE)),
    "focus" to MoodColors(Color(0xFF006F6A), Color(0xFF004441), Color(0xFFE7FAF8)),
    "sad" to MoodColors(Color(0xFF006590), Color(0xFF003C5E), Color(0xFFE8F8FF)),
    "drive" to MoodColors(Color(0xFF34589D), Color(0xFF173268), Color(0xFFEEF6FF)),
    "sleep" to MoodColors(Color(0xFF644994), Color(0xFF3D2761), Color(0xFFF6F3FF)),
    "party" to MoodColors(Color(0xFF8A3A64), Color(0xFF591A3C), Color(0xFFFFF0F7)),
)

private val LIGHT = mapOf(
    "workout" to MoodColors(Color(0xFFFFC2B5), Color(0xFFF99F90), Color(0xFF521710)),
    "happy" to MoodColors(Color(0xFFEED592), Color(0xFFD6B763), Color(0xFF3F2A00)),
    "chill" to MoodColors(Color(0xFFADE9B8), Color(0xFF85CF95), Color(0xFF003912)),
    "focus" to MoodColors(Color(0xFF8DEBE5), Color(0xFF55D1CA), Color(0xFF003B38)),
    "sad" to MoodColors(Color(0xFF98E3FF), Color(0xFF68C8F4), Color(0xFF003452)),
    "drive" to MoodColors(Color(0xFFB7D8FF), Color(0xFF94BBFF), Color(0xFF132B5A)),
    "sleep" to MoodColors(Color(0xFFDECBFF), Color(0xFFC3ABF8), Color(0xFF342154)),
    "party" to MoodColors(Color(0xFFFFC0E0), Color(0xFFF09DC4), Color(0xFF4D1634)),
)

// Незнакомое серверное настроение получает стабильный цвет из своего ключа.
private fun moodColors(key: String, dark: Boolean): MoodColors {
    (if (dark) DARK else LIGHT)[key]?.let { return it }
    val hue = key.fold(7) { hash, char -> (hash * 31 + char.code) % 360 }.toFloat()
    return if (dark) {
        MoodColors(Color.hsl(hue, 0.45f, 0.38f), Color.hsl(hue, 0.55f, 0.24f), Color.hsl(hue, 0.3f, 0.96f))
    } else {
        MoodColors(Color.hsl(hue, 0.8f, 0.85f), Color.hsl(hue, 0.7f, 0.76f), Color.hsl(hue, 0.6f, 0.2f))
    }
}

@Composable
private fun MoodTile(mood: String, active: Boolean, playing: Boolean, enabled: Boolean, dimmed: Boolean, onClick: () -> Unit) {
    val palette = LocalPalette.current
    val colors = moodColors(mood, palette.dark)

    Box(
        Modifier
            .size(width = 128.dp, height = 96.dp)
            .alpha(if (dimmed) 0.5f else 1f)
            .then(if (active) Modifier.border(2.dp, palette.foreground, Radius.panel) else Modifier)
            .padding(if (active) 4.dp else 0.dp)
            .clip(Radius.panel)
            .background(Brush.linearGradient(listOf(colors.top, colors.bottom)))
            .clickable(enabled = enabled, onClick = onClick),
    ) {
        // Крупный знак настроения, срезанный углом плитки: он и различает плитки, когда цвета близки.
        Icon(
            moodIcon(mood),
            null,
            tint = colors.ink.copy(alpha = 0.16f),
            modifier = Modifier.align(Alignment.BottomEnd).offset(x = 16.dp, y = 20.dp).size(96.dp).rotate(-12f),
        )
        Column(Modifier.fillMaxSize().padding(14.dp), verticalArrangement = Arrangement.SpaceBetween) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Icon(moodIcon(mood), null, tint = colors.ink, modifier = Modifier.size(22.dp))
                if (active && playing) {
                    Box(Modifier.weight(1f), contentAlignment = Alignment.CenterEnd) { NowPlayingBars(colors.ink) }
                }
            }
            Text(
                moodLabel(mood),
                style = Type.small.copy(fontWeight = FontWeight.SemiBold),
                color = colors.ink,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

@Composable
fun MoodRadio() {
    val container = LocalContainer.current
    val context = LocalContext.current
    val activeMood by remember { container.radio.state.map { it.mood }.distinctUntilChanged() }
        .collectAsState(container.radio.state.value.mood)
    @Suppress("UNCHECKED_CAST")
    val moods by produceState(Remote.cache[MOODS] as List<String>? ?: emptyList()) {
        if (value.isEmpty()) {
            runCatching { withNetworkRetries { container.api.moods() } }
                .onSuccess {
                    Remote.cache[MOODS] = it
                    value = it
                }
        }
    }
    var starting by remember { mutableStateOf<String?>(null) }
    val playing = container.player.state.collectAsState().value.playing

    if (moods.isEmpty()) return

    Column(verticalArrangement = Arrangement.spacedBy(16.dp)) {
        SectionHeader(stringResource(R.string.mood_title), stringResource(R.string.mood_note))
        LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            items(moods, key = { it }) { mood ->
                MoodTile(
                    mood,
                    active = activeMood == mood,
                    playing = playing,
                    enabled = starting == null,
                    dimmed = starting != null && starting != mood,
                ) {
                    starting = mood
                    container.scope.launch {
                        val batch = runCatching { container.api.radio(RadioRequest(null, emptyList(), mood)) }.getOrNull()
                        withContext(Dispatchers.Main) {
                            starting = null
                            if (batch == null || batch.tracks.isEmpty()) {
                                Toast.makeText(context, R.string.radio_failed, Toast.LENGTH_SHORT).show()
                            } else {
                                container.player.playMyRadio(batch, mood)
                            }
                        }
                    }
                }
            }
        }
    }
}
