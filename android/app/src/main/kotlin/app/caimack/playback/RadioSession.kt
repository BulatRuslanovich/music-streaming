// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.playback

import android.content.Context
import androidx.core.content.edit
import app.caimack.api.RadioBatch
import app.caimack.api.RecommendationReason
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update

data class RadioNote(val reason: RecommendationReason, val explore: Boolean)

// Почему трек радио попал в очередь; у треков, поставленных самим слушателем, заметки нет.
data class RadioState(val seedTrackId: String? = null, val notes: Map<String, RadioNote> = emptyMap())

class RadioSession(context: Context) {
    private val preferences = context.getSharedPreferences(FILE, Context.MODE_PRIVATE)
    private val current = MutableStateFlow(RadioState())
    private val continuing = MutableStateFlow(preferences.getBoolean(AUTOPLAY, true))

    val state: StateFlow<RadioState> = current

    val autoplay: StateFlow<Boolean> = continuing

    fun start(seedTrackId: String, batch: RadioBatch) {
        current.value = RadioState(seedTrackId, notesOf(batch))
    }

    fun extend(batch: RadioBatch) = current.update { it.copy(notes = it.notes + notesOf(batch)) }

    fun reset() {
        current.value = RadioState()
    }

    fun restore(state: RadioState) {
        current.value = state
    }

    fun setAutoplay(enabled: Boolean) {
        preferences.edit { putBoolean(AUTOPLAY, enabled) }
        continuing.value = enabled
    }

    private fun notesOf(batch: RadioBatch) = batch.tracks.mapNotNull { recommended ->
        recommended.reason?.let { recommended.track.id to RadioNote(it, recommended.signals?.explore == true) }
    }.toMap()

    private companion object {
        const val FILE = "radio"
        const val AUTOPLAY = "autoplay"
    }
}
