// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.playback

import android.app.PendingIntent
import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.widget.Toast
import androidx.annotation.OptIn
import androidx.core.net.toUri
import androidx.core.util.writeText
import androidx.media3.common.AudioAttributes
import androidx.media3.common.C
import androidx.media3.common.MediaItem
import androidx.media3.common.MimeTypes
import androidx.media3.common.PlaybackException
import androidx.media3.common.Player
import androidx.media3.common.util.UnstableApi
import androidx.media3.datasource.DataSourceBitmapLoader
import androidx.media3.datasource.HttpDataSource
import androidx.media3.datasource.okhttp.OkHttpDataSource
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory
import androidx.media3.exoplayer.upstream.DefaultLoadErrorHandlingPolicy
import androidx.media3.exoplayer.upstream.LoadErrorHandlingPolicy
import androidx.media3.session.CacheBitmapLoader
import androidx.media3.session.CommandButton
import androidx.media3.session.DefaultMediaNotificationProvider
import androidx.media3.session.MediaLibraryService
import androidx.media3.session.MediaSession
import androidx.media3.session.SessionCommand
import app.caimack.AppContainer
import app.caimack.CaimackApp
import app.caimack.MainActivity
import app.caimack.R
import app.caimack.api.RadioRequest
import app.caimack.api.Track
import app.caimack.ui.Appearance
import app.caimack.ui.withNetworkRetries
import kotlinx.coroutines.Job
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.serialization.Serializable

@Serializable
data class SavedQueue(val tracks: List<Track>, val index: Int, val positionMs: Long)

@OptIn(UnstableApi::class)
class PlaybackService : MediaLibraryService() {
    private var session: MediaLibrarySession? = null
    private val fellBack = mutableSetOf<String>()
    private val ticks = Handler(Looper.getMainLooper())
    private var radioSeed: String? = null
    private var listening: String? = null
    private var failures = 0
    private val saving = Dispatchers.IO.limitedParallelism(1)
    private lateinit var signals: Signals
    private lateinit var exclusive: ExclusiveSession
    private var heart: Job? = null

    override fun onCreate() {
        super.onCreate()
        val container = (application as CaimackApp).container
        val data = OkHttpDataSource.Factory(container.http)

        val player = ExoPlayer.Builder(this)
            .setMediaSourceFactory(
                DefaultMediaSourceFactory(container.downloads.playback).setLoadErrorHandlingPolicy(
                    object : DefaultLoadErrorHandlingPolicy(NETWORK_RETRIES) {
                        override fun getRetryDelayMsFor(info: LoadErrorHandlingPolicy.LoadErrorInfo): Long =
                            if (statusOf(info.exception) in 400..499) C.TIME_UNSET else super.getRetryDelayMsFor(info)
                    },
                ),
            )
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
                if (player.playbackState == Player.STATE_IDLE && player.playerError != null) player.prepare()
                save(container, player)

                val fallback = reason == Player.MEDIA_ITEM_TRANSITION_REASON_PLAYLIST_CHANGED &&
                    mediaItem?.mediaId == listening && mediaItem?.mediaId in fellBack
                if (fallback) return

                val natural = reason == Player.MEDIA_ITEM_TRANSITION_REASON_AUTO || reason == Player.MEDIA_ITEM_TRANSITION_REASON_REPEAT
                signals.tracker.finish(if (natural) ListeningTracker.COMPLETED else ListeningTracker.SKIPPED)
                listening = null
                if (player.playWhenReady) listen(player)
                continueWithRadio(container, player)
                showHeart(container)
            }

            override fun onPlaybackStateChanged(playbackState: Int) {
                if (playbackState == Player.STATE_ENDED) signals.tracker.finish(ListeningTracker.COMPLETED)
            }

            override fun onIsPlayingChanged(isPlaying: Boolean) {
                ticks.removeCallbacks(tick)
                if (isPlaying) {
                    failures = 0
                    listen(player)
                    ticks.post(tick)
                    exclusive.hold()
                } else {
                    exclusive.release()
                    signals.flush()
                    save(container, player)
                }
            }

            override fun onPlayerError(error: PlaybackException) {
                val item = player.currentMediaItem ?: return
                val offline = error.errorCode == PlaybackException.ERROR_CODE_IO_NETWORK_CONNECTION_FAILED ||
                    error.errorCode == PlaybackException.ERROR_CODE_IO_NETWORK_CONNECTION_TIMEOUT
                if (offline || statusOf(error.cause) == UNAUTHORIZED) return

                if (item.localConfiguration?.mimeType == MimeTypes.APPLICATION_M3U8 && fellBack.add(item.mediaId)) {
                    val index = player.currentMediaItemIndex
                    val position = player.currentPosition
                    player.replaceMediaItem(index, item.buildUpon().setUri(original(container, item.mediaId)).setMimeType(null).build())
                    player.seekTo(index, position)
                    player.prepare()
                    player.play()
                    return
                }

                if (player.hasNextMediaItem() && failures++ < MAX_SKIPS) player.seekToNextMediaItem()
            }
        })

        val tree = LibraryTree(this, container) { resolve(container, it) }
        container.scope.launch {
            val saved = runCatching { tree.resumption() }.getOrNull() ?: return@launch
            withContext(Dispatchers.Main) {
                if (session != null && player.mediaItemCount == 0) {
                    player.setMediaItems(saved.mediaItems, saved.startIndex, saved.startPositionMs)
                }
            }
        }

        session = MediaLibrarySession.Builder(this, player, tree)
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

        heart = container.scope.launch(Dispatchers.Main) { container.favorites.state.collect { showHeart(container) } }

        setMediaNotificationProvider(DefaultMediaNotificationProvider.Builder(this).build().apply { setSmallIcon(R.drawable.ic_notification) })
    }

    override fun onGetSession(controllerInfo: MediaSession.ControllerInfo): MediaLibrarySession? = session

    override fun onDestroy() {
        heart?.cancel()
        ticks.removeCallbacksAndMessages(null)
        exclusive.release()
        signals.tracker.finish(ListeningTracker.SKIPPED)
        signals.flush()
        session?.run {
            save((application as CaimackApp).container, player)
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

        val queued = idsOf(player)
        container.scope.launch {
            val batch = runCatching { withNetworkRetries { container.api.radio(RadioRequest(seed, queued)) } }.getOrNull() ?: return@launch
            val fresh = batch.tracks.map { it.track }.filter { it.id !in queued }
            if (fresh.isEmpty()) return@launch

            fresh.forEach { container.tracks[it.id] = it }
            withContext(Dispatchers.Main) {
                if (idsOf(player) == queued) player.addMediaItems(fresh.map { resolve(container, it.toMediaItem(container.media)) })
            }
        }
    }

    private fun showHeart(container: AppContainer) {
        val session = session ?: return
        val track = session.player.currentMediaItem?.mediaId?.let { container.tracks[it] }
        val liked = track != null && container.favorites.isFavorite(track)
        session.setMediaButtonPreferences(
            listOf(
                CommandButton.Builder(if (liked) CommandButton.ICON_HEART_FILLED else CommandButton.ICON_HEART_UNFILLED)
                    .setDisplayName(Appearance.localized(this).getString(if (liked) R.string.menu_unlike else R.string.menu_like))
                    .setSessionCommand(SessionCommand(LibraryTree.FAVORITE, Bundle.EMPTY))
                    .setSlots(CommandButton.SLOT_FORWARD_SECONDARY)
                    .build(),
            ),
        )
    }

    private fun listen(player: Player) {
        val item = player.currentMediaItem ?: return
        if (item.mediaId == listening) return
        listening = item.mediaId
        signals.begin(item.mediaId, secondsOf(item))
    }

    private fun save(container: AppContainer, player: Player) {
        val ids = idsOf(player)
        val tracks = ids.mapNotNull { container.tracks[it] }
        val saved = SavedQueue(tracks, player.currentMediaItemIndex, player.currentPosition).takeIf { ids.isNotEmpty() && tracks.size == ids.size }
        container.scope.launch(saving) {
            if (saved == null) container.queue.delete() else container.queue.writeText(container.json.encodeToString(SavedQueue.serializer(), saved))
        }
    }

    private fun idsOf(player: Player) = (0 until player.mediaItemCount).map { player.getMediaItemAt(it).mediaId }

    private fun statusOf(error: Throwable?) = (error as? HttpDataSource.InvalidResponseCodeException)?.responseCode

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
        private const val NETWORK_RETRIES = 10
        private const val MAX_SKIPS = 3
        private const val UNAUTHORIZED = 401
    }
}
