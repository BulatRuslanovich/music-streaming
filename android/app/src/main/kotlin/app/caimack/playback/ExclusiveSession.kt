// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.playback

import app.caimack.session.Server
import java.io.IOException
import java.util.concurrent.TimeUnit
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okhttp3.Call
import okhttp3.OkHttpClient
import okhttp3.Request

class ExclusiveSession(
    http: OkHttpClient,
    private val server: Server,
    private val deviceId: String,
    private val scope: CoroutineScope,
    private val onDisplaced: () -> Unit,
) {
    private val client = http.newBuilder().readTimeout(0, TimeUnit.MILLISECONDS).build()
    private var job: Job? = null
    private var call: Call? = null

    fun hold() {
        if (job?.isActive == true) return
        job = scope.launch(Dispatchers.IO) {
            var attempt = 0
            while (isActive) {
                val url = server.resolve("api/playback/session")?.newBuilder()?.addQueryParameter("deviceId", deviceId)?.build() ?: return@launch
                val displaced = try {
                    val current = client.newCall(Request.Builder().url(url).header("Accept", "text/event-stream").build())
                    call = current
                    current.execute().use { response ->
                        if (!response.isSuccessful) return@use false
                        attempt = 0
                        val source = response.body.source()
                        generateSequence { source.readUtf8Line() }.any { it.trim() == "event: displaced" }
                    }
                } catch (_: IOException) {
                    false
                }

                if (displaced) {
                    withContext(Dispatchers.Main) { onDisplaced() }
                    return@launch
                }
                val pause = RECONNECT_DELAYS_MS.getOrNull(attempt++) ?: return@launch
                delay(pause)
            }
        }
    }

    fun release() {
        call?.cancel()
        call = null
        job?.cancel()
        job = null
    }

    private companion object {
        val RECONNECT_DELAYS_MS = listOf(1_000L, 3_000L, 8_000L)
    }
}
