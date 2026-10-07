// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.ui

import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
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
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import app.caimack.R
import app.caimack.api.Mood
import app.caimack.api.RadioRequest
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

// Список настроений приходит с сервера: убранное из moods.json пропадает и здесь без обновления
// приложения, а незнакомое показывается по ключу.
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
    val palette = LocalPalette.current
    val radio by container.radio.state.collectAsState()
    val moods by produceState(emptyList<Mood>()) { value = runCatching { container.api.moods() }.getOrDefault(emptyList()) }
    var starting by remember { mutableStateOf<String?>(null) }

    if (moods.isEmpty()) return

    Column(verticalArrangement = Arrangement.spacedBy(16.dp)) {
        SectionHeader(stringResource(R.string.mood_title), stringResource(R.string.mood_note))
        LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            items(moods, key = { it.key }) { mood ->
                val active = radio.mood == mood.key
                val tint = if (active) palette.primary else palette.muted

                Row(
                    Modifier
                        .height(40.dp)
                        .clip(CircleShape)
                        .background(if (active) palette.primarySoft else palette.card)
                        .clickable(enabled = starting == null) {
                            starting = mood.key
                            container.scope.launch {
                                val batch = runCatching { container.api.radio(RadioRequest(null, emptyList(), mood.key)) }.getOrNull()
                                withContext(Dispatchers.Main) {
                                    starting = null
                                    if (batch == null || batch.tracks.isEmpty()) {
                                        Toast.makeText(context, R.string.radio_failed, Toast.LENGTH_SHORT).show()
                                    } else {
                                        container.player.playMyRadio(batch, mood.key)
                                    }
                                }
                            }
                        }
                        .alpha(if (starting != null && starting != mood.key) 0.5f else 1f)
                        .padding(horizontal = 16.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    Icon(moodIcon(mood.key), null, tint = tint, modifier = Modifier.size(16.dp))
                    Text(
                        moodLabel(mood.key),
                        style = Type.small.copy(fontWeight = FontWeight.Medium),
                        color = if (active) palette.primary else palette.foreground,
                    )
                }
            }
        }
    }
}
