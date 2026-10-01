// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.playback

import android.app.PendingIntent
import android.content.Intent
import android.os.Handler
import android.os.Looper
import android.widget.Toast
import androidx.annotation.OptIn
import androidx.core.net.toUri
import androidx.media3.common.AudioAttributes
import androidx.media3.common.C
import androidx.media3.common.MediaItem
import androidx.media3.common.MimeTypes
import androidx.media3.common.PlaybackException
import androidx.media3.common.Player
import androidx.media3.common.util.UnstableApi
import androidx.media3.datasource.DataSourceBitmapLoader
import androidx.media3.datasource.okhttp.OkHttpDataSource
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory
import androidx.media3.session.CacheBitmapLoader
import androidx.media3.session.DefaultMediaNotificationProvider
import androidx.media3.session.MediaLibraryService
import androidx.media3.session.MediaSession
import app.caimack.AppContainer
import app.caimack.CaimackApp
import app.caimack.MainActivity
import app.caimack.R
import app.caimack.api.RadioRequest
import app.caimack.ui.withNetworkRetries
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

@OptIn(UnstableApi::class)
class PlaybackService : MediaLibraryService() {
    private var session: MediaLibrarySession? = null
    private val fellBack = mutableSetOf<String>()
    private val ticks = Handler(Looper.getMainLooper())
    private var radioSeed: String? = null
    private lateinit var signals: Signals
    private lateinit var exclusive: ExclusiveSession

    override fun onCreate() {
        super.onCreate()
        val container = (application as CaimackApp).container
        val data = OkHttpDataSource.Factory(container.http)

        val player = ExoPlayer.Builder(this)
            .setMediaSourceFactory(DefaultMediaSourceFactory(container.downloads.playback))
            .setAudioAttributes(
                AudioAttributes.Builder().setUsage(C.USAGE_MEDIA).setContentType(C.AUDIO_CONTENT_TYPE_MUSIC).build(),
                true,
            )
            .setHandleAudioBecomingNoisy(true)
            .setWakeMode(C.WAKE_MODE_NETWORK)
            .build()

        signals = Signals(container.api, container.scope, container.deviceId)
        exclusive = ExclusiveSession(container.http, container.server, container.deviceId, container.scope) {
            player.pause()
            Toast.makeText(this, R.string.player_playing_elsewhere, Toast.LENGTH_LONG).show()
        }

        val tick = object : Runnable {
            override fun run() {
                player.currentMediaItem?.let { signals.progress(it.mediaId, secondsOf(it), player.currentPosition / 1000.0) }
                ticks.postDelayed(this, TICK_MS)
            }
        }

        player.addListener(object : Player.Listener {
            override fun onMediaItemTransition(mediaItem: MediaItem?, reason: Int) {
                val natural = reason == Player.MEDIA_ITEM_TRANSITION_REASON_AUTO || reason == Player.MEDIA_ITEM_TRANSITION_REASON_REPEAT
                signals.tracker.finish(if (natural) ListeningTracker.COMPLETED else ListeningTracker.SKIPPED)
                mediaItem?.let { signals.begin(it.mediaId, secondsOf(it)) }
                continueWithRadio(container, player)
            }

            override fun onPlaybackStateChanged(playbackState: Int) {
                if (playbackState == Player.STATE_ENDED) signals.tracker.finish(ListeningTracker.COMPLETED)
            }

            override fun onIsPlayingChanged(isPlaying: Boolean) {
                ticks.removeCallbacks(tick)
                if (isPlaying) {
                    ticks.post(tick)
                    exclusive.hold()
                } else {
                    exclusive.release()
                    signals.flush()
                }
            }

            override fun onPlayerError(error: PlaybackException) {
                val index = player.currentMediaItemIndex
                val item = player.currentMediaItem ?: return
                if (item.localConfiguration?.mimeType != MimeTypes.APPLICATION_M3U8 || !fellBack.add(item.mediaId)) return

                val position = player.currentPosition
                player.replaceMediaItem(index, item.buildUpon().setUri(original(container, item.mediaId)).setMimeType(null).build())
                player.seekTo(index, position)
                player.prepare()
                player.play()
            }
        })

        session = MediaLibrarySession.Builder(this, player, LibraryTree(this, container) { resolve(container, it) })
            .setBitmapLoader(CacheBitmapLoader(DataSourceBitmapLoader.Builder(this).setDataSourceFactory(data).build()))
            .setSessionActivity(
                PendingIntent.getActivity(
                    this,
                    0,
                    Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),
                    PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
                ),
            )
            .build()

        setMediaNotificationProvider(DefaultMediaNotificationProvider.Builder(this).build().apply { setSmallIcon(R.drawable.ic_notification) })
    }

    override fun onGetSession(controllerInfo: MediaSession.ControllerInfo): MediaLibrarySession? = session

    override fun onDestroy() {
        ticks.removeCallbacksAndMessages(null)
        exclusive.release()
        signals.tracker.finish(ListeningTracker.SKIPPED)
        signals.flush()
        session?.run {
            player.release()
            release()
        }
        session = null
        super.onDestroy()
    }

    private fun resolve(container: AppContainer, item: MediaItem): MediaItem {
        container.downloads.completed(item.mediaId)?.request?.let { request ->
            return item.buildUpon()
                .setUri(request.uri)
                .setMimeType(request.mimeType)
                .setCustomCacheKey(request.customCacheKey)
                .setStreamKeys(request.streamKeys)
                .build()
        }

        val settings = container.settings.value
        val quality = if (settings.dataSaver) LOW else settings.quality
        val codec = item.mediaMetadata.extras?.getString(CODEC)
        val adaptive = quality != ORIGINAL || codec == "alac"

        if (!adaptive || item.mediaId in fellBack) {
            return item.buildUpon().setUri(original(container, item.mediaId)).build()
        }

        val cap = if (quality == ORIGINAL) NORMAL else quality
        val master = container.server.resolve("api/tracks/${item.mediaId}/hls/master.m3u8")
            ?.newBuilder()?.addQueryParameter("maxQuality", cap)?.build().toString()
        return item.buildUpon().setUri(master.toUri()).setMimeType(MimeTypes.APPLICATION_M3U8).build()
    }

    private fun continueWithRadio(container: AppContainer, player: ExoPlayer) {
        if (player.repeatMode != Player.REPEAT_MODE_OFF || player.mediaItemCount == 0) return
        if (player.mediaItemCount - 1 - player.currentMediaItemIndex > RADIO_PREFETCH_AT) return

        val seed = player.currentMediaItem?.mediaId ?: return
        if (seed == radioSeed) return
        radioSeed = seed

        val queued = (0 until player.mediaItemCount).map { player.getMediaItemAt(it).mediaId }
        container.scope.launch {
            val batch = runCatching { withNetworkRetries { container.api.radio(RadioRequest(seed, queued)) } }.getOrNull() ?: return@launch
            val fresh = batch.tracks.map { it.track }.filter { it.id !in queued }
            if (fresh.isEmpty()) return@launch

            fresh.forEach { container.tracks[it.id] = it }
            withContext(Dispatchers.Main) {
                player.addMediaItems(fresh.map { resolve(container, it.toMediaItem(container.media)) })
            }
        }
    }

    private fun secondsOf(item: MediaItem) = ((item.mediaMetadata.durationMs ?: 0L) / 1000).toInt()

    private fun original(container: AppContainer, trackId: String) =
        container.server.resolve("api/tracks/$trackId/stream").toString().toUri()

    companion object {
        const val CODEC = "codec"

        private const val ORIGINAL = "Original"
        private const val NORMAL = "Normal"
        private const val LOW = "Low"
        private const val TICK_MS = 1_000L
        private const val RADIO_PREFETCH_AT = 1
    }
}
