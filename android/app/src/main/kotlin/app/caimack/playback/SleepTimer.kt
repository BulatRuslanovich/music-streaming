// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.playback

import android.os.SystemClock
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow

sealed interface SleepTimerState {
    // Время — по монотонным часам (elapsedRealtime): перевод часов телефона таймер не сбивает.
    data class Minutes(val minutes: Int, val endsAtMs: Long) : SleepTimerState {
        val remainingMs: Long get() = (endsAtMs - SystemClock.elapsedRealtime()).coerceAtLeast(0)
    }

    data object TrackEnd : SleepTimerState
}

// Таймер сна. Выбирают его в интерфейсе, а исполняет PlaybackService: затихание, пауза
// и «до конца трека» работают и с закрытым приложением.
class SleepTimer {
    private val current = MutableStateFlow<SleepTimerState?>(null)

    val state: StateFlow<SleepTimerState?> = current

    fun set(minutes: Int) {
        current.value = SleepTimerState.Minutes(minutes, SystemClock.elapsedRealtime() + minutes * 60_000L)
    }

    fun untilTrackEnd() {
        current.value = SleepTimerState.TrackEnd
    }

    fun cancel() {
        current.value = null
    }

    companion object {
        val MINUTES = listOf(15, 30, 45, 60, 90)

        // Последние секунды музыка затихает, а не обрывается.
        const val FADE_MS = 15_000L
    }
}
