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
import androidx.compose.ui.graphics.colorspace.ColorSpaces
import androidx.compose.ui.graphics.lerp
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
import kotlin.math.cos
import kotlin.math.sin

private const val MOODS = "moods"

// Список настроений приходит с сервера, так что незнакомое клиенту показывается по ключу.
@Composable
fun moodLabel(key: String): String = when (key) {
    "workout" -> stringResource(R.string.mood_workout)
    "party" -> stringResource(R.string.mood_party)
    "chill" -> stringResource(R.string.mood_chill)
    "sleep" -> stringResource(R.string.mood_sleep)
    "happy" -> stringResource(R.string.mood_happy)
    "sad" -> stringResource(R.string.mood_sad)
    else -> key.replaceFirstChar { it.uppercase() }
}

fun moodIcon(key: String): ImageVector = when (key) {
    "workout" -> Lucide.Dumbbell
    "party" -> Lucide.PartyPopper
    "chill" -> Lucide.Coffee
    "sleep" -> Lucide.Moon
    "happy" -> Lucide.Sun
    "sad" -> Lucide.CloudRain
    else -> Lucide.Radio
}

private class MoodColors(val top: Color, val bottom: Color, val icon: Color, val mark: Color, val edge: Color)

// Оттенок OKLCH для плитки — те же, что на вебе (frontend/src/lib/moods.ts). Незнакомое серверное
// настроение получает оттенок из своего ключа, чтобы цвет был стабильным между запусками.
private val HUES = mapOf(
    "workout" to 30f,
    "happy" to 90f,
    "chill" to 150f,
    "sad" to 230f,
    "sleep" to 298f,
    "party" to 350f,
)

internal fun moodHue(key: String): Float = HUES[key] ?: key.fold(7) { hash, char -> (hash * 31 + char.code) % 360 }.toFloat()

// OKLCH — тот же Oklab в полярных координатах, а Oklab Compose умеет.
internal fun oklch(lightness: Float, chroma: Float, hue: Float): Color {
    val radians = Math.toRadians(hue.toDouble())
    return Color(lightness, (chroma * cos(radians)).toFloat(), (chroma * sin(radians)).toFloat(), 1f, ColorSpaces.Oklab)
        .convert(ColorSpaces.Srgb)
}

// Как на вебе (frontend/src/app/styles/mood.css): оттенок настроения только подкрашивает карточку темы,
// поэтому плитки остаются в палитре и не спорят с её акцентом. lerp в Compose смешивает в Oklab.
private fun moodColors(key: String, palette: Palette): MoodColors {
    val hue = moodHue(key)
    val tint = oklch(0.62f, if (palette.dark) 0.11f else 0.10f, hue)
    val base = if (palette.dark) palette.card else palette.popover

    return MoodColors(
        top = lerp(base, tint, if (palette.dark) 0.34f else 0.30f),
        bottom = lerp(base, tint, if (palette.dark) 0.12f else 0.10f),
        icon = if (palette.dark) oklch(0.84f, 0.09f, hue) else oklch(0.42f, 0.10f, hue),
        mark = tint.copy(alpha = if (palette.dark) 0.32f else 0.26f),
        edge = tint.copy(alpha = 0.2f),
    )
}

@Composable
private fun MoodTile(mood: String, active: Boolean, playing: Boolean, enabled: Boolean, dimmed: Boolean, onClick: () -> Unit) {
    val palette = LocalPalette.current
    val colors = moodColors(mood, palette)

    Box(
        Modifier
            .size(width = 128.dp, height = 96.dp)
            .alpha(if (dimmed) 0.5f else 1f)
            .then(if (active) Modifier.border(2.dp, palette.foreground, Radius.panel) else Modifier)
            .padding(if (active) 4.dp else 0.dp)
            .clip(Radius.panel)
            .background(Brush.linearGradient(listOf(colors.top, colors.bottom)))
            .border(1.dp, colors.edge, Radius.panel)
            .clickable(enabled = enabled, onClick = onClick),
    ) {
        // Крупный знак настроения, срезанный углом плитки: он и различает плитки, когда цвета близки.
        Icon(
            moodIcon(mood),
            null,
            tint = colors.mark,
            modifier = Modifier.align(Alignment.BottomEnd).offset(x = 16.dp, y = 20.dp).size(96.dp).rotate(-12f),
        )
        Column(Modifier.fillMaxSize().padding(14.dp), verticalArrangement = Arrangement.SpaceBetween) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Icon(moodIcon(mood), null, tint = colors.icon, modifier = Modifier.size(22.dp))
                if (active && playing) {
                    Box(Modifier.weight(1f), contentAlignment = Alignment.CenterEnd) { NowPlayingBars(palette.foreground) }
                }
            }
            Text(
                moodLabel(mood),
                style = Type.small.copy(fontWeight = FontWeight.SemiBold),
                color = palette.foreground,
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
        SectionHeader(stringResource(R.string.mood_title))
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
