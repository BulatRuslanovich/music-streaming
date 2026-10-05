// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.ui

import android.widget.Toast
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalResources
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import app.caimack.R
import app.caimack.api.AddTracksRequest
import app.caimack.api.Playlist
import app.caimack.api.RadioRequest
import app.caimack.api.Track
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

val LocalNavigate = staticCompositionLocalOf<(Any) -> Unit> { {} }

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun TrackSheet(track: Track, onDismiss: () -> Unit) {
    val palette = LocalPalette.current
    val container = LocalContainer.current
    val navigate = LocalNavigate.current
    val context = LocalContext.current
    val resources = LocalResources.current
    var choosingPlaylist by remember { mutableStateOf(false) }
    container.favorites.state.collectAsState().value
    val liked = container.favorites.isFavorite(track)

    val say = { message: String -> Toast.makeText(context, message, Toast.LENGTH_SHORT).show() }

    ModalBottomSheet(onDismissRequest = onDismiss, containerColor = palette.card) {
        Column(Modifier.navigationBarsPadding().padding(horizontal = 12.dp, vertical = 8.dp)) {
            Row(
                Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 8.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Cover(trackCover(track), track.albumTitle ?: track.title, Modifier.size(44.dp))
                Column(Modifier.weight(1f)) {
                    Text(track.title, style = Type.small.copy(fontWeight = FontWeight.Medium), maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Text(artistsOf(track), style = Type.small, color = palette.muted, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
            }
            HorizontalDivider(Modifier.padding(vertical = 8.dp), color = palette.border)

            if (choosingPlaylist) {
                val playlists by produceState<List<Playlist>?>(null) {
                    value = runCatching { container.api.playlists() }.getOrDefault(emptyList())
                }
                when {
                    playlists == null -> RecordLoading(28.dp)
                    playlists!!.isEmpty() -> Text(
                        stringResource(R.string.menu_no_playlists),
                        style = Type.small,
                        color = palette.faint,
                        modifier = Modifier.padding(horizontal = 12.dp, vertical = 10.dp),
                    )
                    else -> playlists!!.forEach { playlist ->
                        SheetRow(playlist.name, Lucide.ListMusic) {
                            onDismiss()
                            container.scope.launch {
                                val added = runCatching { container.api.addToPlaylist(playlist.id, AddTracksRequest(listOf(track.id))) }.isSuccess
                                withContext(Dispatchers.Main) {
                                    say(
                                        if (added) resources.getString(R.string.menu_added_to_playlist, playlist.name)
                                        else resources.getString(R.string.error_load),
                                    )
                                }
                            }
                        }
                    }
                }
                return@Column
            }

            SheetRow(stringResource(R.string.menu_play_next), Lucide.CornerDownRight) {
                container.player.enqueue(track, next = true)
                say(resources.getString(R.string.menu_playing_next, track.title))
                onDismiss()
            }
            SheetRow(stringResource(R.string.menu_add_to_queue), Lucide.ListVideo) {
                container.player.enqueue(track, next = false)
                say(resources.getString(R.string.menu_added_to_queue, track.title))
                onDismiss()
            }
            SheetRow(stringResource(R.string.menu_radio), Lucide.Radio) {
                onDismiss()
                container.scope.launch {
                    val radio = runCatching { container.api.radio(RadioRequest(track.id, emptyList())) }.getOrNull()
                    withContext(Dispatchers.Main) {
                        if (radio == null) say(resources.getString(R.string.radio_failed))
                        else container.player.startRadio(track, radio.tracks.map { it.track }.filter { it.id != track.id })
                    }
                }
            }

            HorizontalDivider(Modifier.padding(vertical = 8.dp), color = palette.border)

            SheetRow(
                stringResource(if (liked) R.string.menu_unlike else R.string.menu_like),
                if (liked) Lucide.HeartFilled else Lucide.Heart,
            ) {
                container.favorites.toggle(track)
                onDismiss()
            }
            SheetRow(stringResource(R.string.menu_add_to_playlist), Lucide.Plus) { choosingPlaylist = true }

            HorizontalDivider(Modifier.padding(vertical = 8.dp), color = palette.border)

            track.albumId?.let { albumId ->
                SheetRow(stringResource(R.string.menu_go_to_album), Lucide.Disc3) {
                    onDismiss()
                    navigate(AlbumRoute(albumId))
                }
            }
            SheetRow(stringResource(R.string.menu_go_to_artist), Lucide.UsersRound) {
                onDismiss()
                navigate(ArtistRoute(track.artistId))
            }
        }
    }
}
