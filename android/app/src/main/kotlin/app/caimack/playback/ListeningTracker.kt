// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.playback

data class ListeningEvent(
    val type: String,
    val trackId: String,
    val durationSeconds: Int,
    val positionSeconds: Int? = null,
    val listenedSeconds: Int? = null,
)

class ListeningTracker(private val record: (ListeningEvent) -> Unit) {
    private var trackId = ""
    private var duration = 0
    private var listened = 0.0
    private var position = 0.0
    private var heartbeatAt = 0.0
    private val heard = mutableSetOf<String>()

    fun begin(id: String, durationSeconds: Int) {
        trackId = id
        duration = durationSeconds
        listened = 0.0
        position = 0.0
        heartbeatAt = 0.0

        record(ListeningEvent("trackStarted", id, durationSeconds))
        if (!heard.add(id)) record(ListeningEvent("trackReplayed", id, durationSeconds))
    }

    fun accumulate(seconds: Double) {
        if (trackId.isEmpty()) return

        val delta = seconds - position
        if (delta > 0 && delta < MAX_LISTENING_STEP_SECONDS) listened += delta
        position = seconds

        if (listened - heartbeatAt >= HEARTBEAT_INTERVAL_SECONDS) {
            heartbeatAt = listened
            progress("trackPlayed")
        }
    }

    fun finish(type: String) {
        if (trackId.isEmpty()) return
        progress(type)
        trackId = ""
    }

    private fun progress(type: String) = record(
        ListeningEvent(type, trackId, duration, positionSeconds = position.toInt(), listenedSeconds = listened.toInt()),
    )

    companion object {
        const val COMPLETED = "trackCompleted"
        const val SKIPPED = "trackSkipped"

        private const val MAX_LISTENING_STEP_SECONDS = 2.0
        private const val HEARTBEAT_INTERVAL_SECONDS = 30.0
        private const val HISTORY_THRESHOLD_SECONDS = 30

        fun historyThreshold(durationSeconds: Int): Int = minOf(HISTORY_THRESHOLD_SECONDS, maxOf(durationSeconds - 1, 1))
    }
}
