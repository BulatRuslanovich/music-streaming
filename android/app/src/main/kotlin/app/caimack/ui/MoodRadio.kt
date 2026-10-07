// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.ui

import android.widget.Toast
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
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

private fun moodIcon(key: String): ImageVector = when (key) {
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

    if (moods.isEmpty()) return

    Column(verticalArrangement = Arrangement.spacedBy(16.dp)) {
        SectionHeader(stringResource(R.string.mood_title), stringResource(R.string.mood_note))
        LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            items(moods, key = { it }) { mood ->
                Chip(
                    moodLabel(mood),
                    active = activeMood == mood,
                    modifier = Modifier.alpha(if (starting != null && starting != mood) 0.5f else 1f),
                    icon = moodIcon(mood),
                    enabled = starting == null,
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
