// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.playback

import android.content.Context
import androidx.core.content.edit
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow

// Настройки воспроизведения, которые живут только на этом телефоне.
class PlaybackPrefs(context: Context) {
    private val preferences = context.getSharedPreferences(FILE, Context.MODE_PRIVATE)
    private val smart = MutableStateFlow(preferences.getBoolean(SMART_TRANSITIONS, true))

    // Умные переходы: тишина в начале и в конце трека срезается, треки идут встык.
    val smartTransitions: StateFlow<Boolean> = smart

    fun setSmartTransitions(enabled: Boolean) {
        preferences.edit { putBoolean(SMART_TRANSITIONS, enabled) }
        smart.value = enabled
    }

    private companion object {
        const val FILE = "playback"
        const val SMART_TRANSITIONS = "smart_transitions"
    }
}
