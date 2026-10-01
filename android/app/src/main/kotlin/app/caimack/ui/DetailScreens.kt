// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.ui

import androidx.annotation.OptIn
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.draw.clip
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.media3.common.util.UnstableApi
import androidx.media3.exoplayer.offline.Download
import androidx.navigation.NavController
import app.caimack.R
import app.caimack.api.Album
import app.caimack.api.Track
import kotlinx.coroutines.launch

@Composable
fun AlbumScreen(id: String, nav: NavController, play: (List<Track>, Int) -> Unit) {
    val container = LocalContainer.current
    Load("album:$id", { container.api.album(id) }) { album ->
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
            item {
                DetailHeader(
                    kind = stringResource(R.string.albums_kind),
                    title = album.title,
                    facts = listOfNotNull(album.artistName, album.year?.toString(), pluralStringResource(R.plurals.count_tracks, album.tracks.size, album.tracks.size)),
                ) { Cover(container.media.cover(album.id, null, album.hasCover, small = false), album.title, it) }
            }
            item { Actions(album.tracks, play) }
            itemsIndexed(album.tracks) { index, track -> TrackRow(track, { play(album.tracks, index) }, index = index + 1) }
            item {
                Load("artist:${album.artistId}", { container.api.artist(album.artistId) }) { artist ->
                    val others = artist.albums.filter { it.id != album.id }
                    if (others.isNotEmpty()) {
                        AlbumShelf(stringResource(R.string.albums_more_by_artist, artist.name), others, nav)
                    }
                }
            }
        }
    }
}

@Composable
fun ArtistScreen(id: String, nav: NavController, play: (List<Track>, Int) -> Unit) {
    val container = LocalContainer.current
    Load("artist:$id", { container.api.artist(id) }) { artist ->
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
            item {
                DetailHeader(
                    kind = stringResource(R.string.artists_kind),
                    title = artist.name,
                    facts = listOfNotNull(
                        artist.albums.size.takeIf { it > 0 }?.let { pluralStringResource(R.plurals.count_albums, it, it) },
                        pluralStringResource(R.plurals.count_tracks, artist.tracks.total, artist.tracks.total),
                    ),
                    round = true,
                ) { Cover(container.media.artistImage(artist.id, artist.hasImage, small = false), artist.name, it, round = true) }
            }
            item {
                Load("artist-top:$id", { container.api.artistTopTracks(id) }, isEmpty = { it.isEmpty() }) { top ->
                    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Actions(top, play)
                        SectionHeader(stringResource(R.string.artists_top_tracks))
                        top.take(5).forEachIndexed { index, track -> TrackRow(track, { play(top, index) }, index = index + 1) }
                    }
                }
            }
            if (artist.albums.isNotEmpty()) {
                item { AlbumShelf(stringResource(R.string.artists_discography), artist.albums, nav) }
            }
        }
    }
}

@Composable
fun PlaylistScreen(id: String, play: (List<Track>, Int) -> Unit) {
    val container = LocalContainer.current
    Load("playlist:$id", { container.api.playlist(id) }) { playlist ->
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
            item {
                DetailHeader(
                    kind = stringResource(R.string.playlists_kind),
                    title = playlist.name,
                    description = playlist.description,
                    facts = listOf(
                        stringResource(R.string.playlists_by, playlist.ownerName),
                        pluralStringResource(R.plurals.count_tracks, playlist.tracks.size, playlist.tracks.size),
                    ),
                ) { Cover(container.media.playlistCover(playlist.id, playlist.hasCover, playlist.coverTrackId, small = false), playlist.name, it) }
            }
            if (playlist.tracks.isEmpty()) {
                item { Empty(stringResource(R.string.playlists_empty_playlist_title)) }
            } else {
                item { Actions(playlist.tracks, play) }
                itemsIndexed(playlist.tracks) { index, track -> TrackRow(track, { play(playlist.tracks, index) }) }
            }
        }
    }
}

@Composable
fun GenreScreen(id: String, name: String, play: (List<Track>, Int) -> Unit) {
    val api = LocalContainer.current.api
    val pager = rememberPager("genre:$id") { api.genreTracks(id, it, TRACK_PAGE_SIZE) }
    val total = pager.total
    PagedColumn(
        pager,
        stringResource(R.string.tracks_empty),
        header = {
            item { PageTitle(name, total?.let { pluralStringResource(R.plurals.count_tracks, it, it) }) }
            if (total != null && total > 0) item { Actions(pager.items, play) }
        },
    ) { index, track -> TrackRow(track, { play(pager.items, index) }) }
}

@Composable
fun MixScreen(kind: String, play: (List<Track>, Int) -> Unit) {
    val container = LocalContainer.current
    val (title, description) = when (kind) {
        "new" -> R.string.home_new_arrivals to R.string.mixes_new_description
        "top" -> R.string.home_top_this_week to R.string.mixes_top_description
        else -> R.string.home_daily_mix to R.string.mixes_daily_description
    }

    Load("mix:$kind", { container.api.homeMix(kind).tracks }) { tracks ->
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp)) {
            item {
                DetailHeader(
                    kind = stringResource(R.string.mixes_kind),
                    title = stringResource(title),
                    description = stringResource(description),
                    facts = listOf(pluralStringResource(R.plurals.count_tracks, tracks.size, tracks.size)),
                ) { modifier ->
                    val lead = tracks.firstOrNull()
                    Cover(lead?.let { trackCover(it, small = false) }, lead?.albumTitle ?: stringResource(title), modifier)
                }
            }
            if (tracks.isEmpty()) {
                item { Empty(stringResource(R.string.mixes_empty)) }
            } else {
                item { Actions(tracks, play) }
                itemsIndexed(tracks) { index, track -> TrackRow(track, { play(tracks, index) }, index = if (kind == "top") index + 1 else null) }
            }
        }
    }
}

@OptIn(UnstableApi::class)
@Composable
private fun Actions(tracks: List<Track>, play: (List<Track>, Int) -> Unit) {
    if (tracks.isEmpty()) return
    val palette = LocalPalette.current
    val container = LocalContainer.current
    val scope = rememberCoroutineScope()
    val offline by container.downloads.state.collectAsStateWithLifecycle()
    val mine = tracks.mapNotNull { offline[it.id] }.filter { it.download.state != Download.STATE_FAILED }
    val pending = mine.any { !it.done }
    val complete = mine.count { it.done } == tracks.size

    Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
        PlayActions(Modifier.weight(1f), onPlay = { play(tracks, 0) }, onShuffle = { play(tracks.shuffled(), 0) })
        Box(
            Modifier.size(44.dp).clip(CircleShape).clickable {
                if (pending || complete) container.downloads.remove(mine.map { it.download.request.id })
                else scope.launch { container.downloads.add(tracks, container.settings.value) }
            },
            contentAlignment = Alignment.Center,
        ) {
            when {
                pending -> CircularProgressIndicator(
                    progress = { mine.sumOf { if (it.done) 1.0 else it.percent / 100.0 }.toFloat() / tracks.size },
                    modifier = Modifier.size(22.dp),
                    color = palette.primary,
                    trackColor = palette.raised,
                    strokeWidth = 2.5.dp,
                )
                complete -> Icon(Lucide.CircleCheck, stringResource(R.string.downloads_remove), tint = palette.primary)
                else -> Icon(Lucide.Download, stringResource(R.string.downloads_add), tint = palette.muted)
            }
        }
    }
}

@Composable
private fun AlbumShelf(title: String, albums: List<Album>, nav: NavController) {
    val media = LocalContainer.current.media
    Column(Modifier.padding(top = 24.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        SectionHeader(title)
        LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            items(albums, key = { it.id }) { album ->
                Card(album.title, album.year?.toString() ?: album.artistName, { nav.navigate(AlbumRoute(album.id)) }, Modifier.width(116.dp)) {
                    Cover(media.albumCover(album, small = true), album.title, it)
                }
            }
        }
    }
}
