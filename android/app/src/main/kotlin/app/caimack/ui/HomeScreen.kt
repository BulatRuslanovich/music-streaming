// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.navigation.NavController
import app.caimack.R
import app.caimack.api.HomeBlock
import app.caimack.api.RecommendationReason
import app.caimack.api.Track

private const val HERO_PREVIEW = 5
private const val MOBILE_UP_NEXT = 4
private const val MOBILE_QUICK = 4
private const val MOBILE_CHART = 5
private const val MIN_DISTINCT = 3

@Composable
fun HomeScreen(nav: NavController, play: (List<Track>, Int) -> Unit) {
    val container = LocalContainer.current

    Load(
        key = "home",
        load = { container.api.homeFeed().blocks },
        isEmpty = { it.isEmpty() },
        empty = { Empty(stringResource(R.string.home_empty_title), stringResource(R.string.home_empty_description)) },
    ) { blocks ->
        val shown = blocks
            .filter { it.zone != "Browse" && it.layout != "Tile" }
            .flatMap { if (it.layout == "Hero") it.tracks.orEmpty().take(HERO_PREVIEW) else it.tracks.orEmpty() }
            .map { it.id }
            .toSet()

        LazyColumn(
            Modifier.fillMaxSize(),
            contentPadding = PaddingValues(vertical = 16.dp),
            verticalArrangement = Arrangement.spacedBy(32.dp),
        ) {
            blocks.filter { it.zone == "Lead" }.forEach { block -> item(block.key) { Block(block, nav, play, shown) } }

            val quick = blocks.filter { it.zone == "Quick" }
            if (quick.isNotEmpty()) item("quick") { QuickTiles(quick, nav, play) }

            blocks.filter { it.zone == "Browse" }.forEach { block -> item(block.key) { Block(block, nav, play, shown) } }
        }
    }
}

@Composable
private fun Block(block: HomeBlock, nav: NavController, play: (List<Track>, Int) -> Unit, shown: Set<String>) {
    val title = blockTitle(block)
    val note = when {
        block.baseKey == "topTracks" -> stringResource(R.string.home_top_period)
        block.baseKey in setOf("forYou", "artistsForYou") -> block.reason?.let { reasonLabel(it) }
        else -> null
    }
    val seeAll = blockLink(block)?.let { route -> { nav.navigate(route) } }
    val tracks = block.tracks.orEmpty()

    when (block.layout) {
        "Hero" -> DailyMix(tracks, block.totalCount ?: tracks.size, title, play) { nav.navigate(MixRoute("daily")) }
        "Grid" -> Column(verticalArrangement = Arrangement.spacedBy(16.dp)) {
            SectionHeader(title, note, seeAll)
            val fresh = onePerAlbum(tracks.filter { it.id !in shown })
            val cards = (if (fresh.size >= MIN_DISTINCT) fresh else onePerAlbum(tracks)).take(4)
            Column(Modifier.padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                cards.chunked(2).forEach { pair ->
                    Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        pair.forEach { track ->
                            Card(track.title, artistsOf(track), { play(tracks, tracks.indexOf(track)) }, Modifier.weight(1f)) {
                                Cover(trackCover(track, small = false), track.albumTitle ?: track.title, it)
                            }
                        }
                        if (pair.size == 1) Box(Modifier.weight(1f))
                    }
                }
            }
        }
        "Chart" -> Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
            SectionHeader(title, note, seeAll)
            tracks.take(MOBILE_CHART).forEachIndexed { index, track ->
                TrackRow(track, { play(tracks, index) }, index = index + 1)
            }
        }
        else -> Column(verticalArrangement = Arrangement.spacedBy(16.dp)) {
            SectionHeader(title, note, seeAll)
            Shelf(block, nav, play)
        }
    }
}

@Composable
private fun Shelf(block: HomeBlock, nav: NavController, play: (List<Track>, Int) -> Unit) {
    val media = LocalContainer.current.media
    val width = Modifier.width(116.dp)

    LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        when {
            !block.artists.isNullOrEmpty() -> items(block.artists, key = { it.id }) { artist ->
                Card(artist.name, pluralStringResource(R.plurals.count_tracks, artist.trackCount, artist.trackCount), { nav.navigate(ArtistRoute(artist.id)) }, width, round = true) {
                    Cover(media.artistImage(artist.id, artist.hasImage, small = true), artist.name, it, round = true)
                }
            }
            !block.albums.isNullOrEmpty() -> items(block.albums, key = { it.id }) { album ->
                Card(album.title, listOfNotNull(album.artistName, album.year?.toString()).joinToString(", "), { nav.navigate(AlbumRoute(album.id)) }, width) {
                    Cover(media.albumCover(album, small = true), album.title, it)
                }
            }
            !block.playlists.isNullOrEmpty() -> items(block.playlists, key = { it.id }) { playlist ->
                Card(playlist.name, pluralStringResource(R.plurals.count_tracks, playlist.trackCount, playlist.trackCount), { nav.navigate(PlaylistRoute(playlist.id)) }, width) {
                    Cover(media.playlistCover(playlist.id, playlist.hasCover, playlist.coverTrackId, small = true), playlist.name, it)
                }
            }
            else -> {
                val tracks = block.tracks.orEmpty()
                items(tracks, key = { it.id }) { track ->
                    Card(track.title, artistsOf(track), { play(tracks, tracks.indexOf(track)) }, width) {
                        Cover(trackCover(track), track.albumTitle ?: track.title, it)
                    }
                }
            }
        }
    }
}

@Composable
private fun DailyMix(tracks: List<Track>, total: Int, title: String, play: (List<Track>, Int) -> Unit, seeAll: () -> Unit) {
    val palette = LocalPalette.current
    val lead = tracks.firstOrNull() ?: return
    val collage = tracks
        .filter { it.hasCover }
        .distinctBy { it.albumId ?: it.id }
        .take(4)

    Column(verticalArrangement = Arrangement.spacedBy(20.dp)) {
        Row(Modifier.padding(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(20.dp), verticalAlignment = Alignment.Bottom) {
            Box(Modifier.size(112.dp).clip(Radius.cover)) {
                if (collage.size == 4) {
                    Column {
                        collage.chunked(2).forEach { pair ->
                            Row(Modifier.weight(1f)) {
                                pair.forEach { Cover(trackCover(it), it.albumTitle ?: it.title, Modifier.weight(1f).fillMaxSize()) }
                            }
                        }
                    }
                } else {
                    Cover(trackCover(lead, small = false), lead.albumTitle ?: lead.title, Modifier.fillMaxSize())
                }
            }
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(stringResource(R.string.home_daily_mix_subtitle), style = Type.small, color = palette.muted)
                Text(title, style = Type.display, maxLines = 2, overflow = TextOverflow.Ellipsis)
            }
        }
        Text(
            "${pluralStringResource(R.plurals.count_tracks, total, total)}, ${artistsOf(lead)}",
            style = Type.body,
            color = palette.muted,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.padding(horizontal = 16.dp),
        )
        PlayActions(Modifier.padding(horizontal = 16.dp), onPlay = { play(tracks, 0) }, onShuffle = { play(tracks.shuffled(), 0) })

        Column {
            Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 6.dp)) {
                Text(stringResource(R.string.home_up_next), style = Type.small, color = palette.muted, modifier = Modifier.weight(1f))
                Text(stringResource(R.string.action_see_all), style = Type.small, color = palette.muted, modifier = Modifier.clickable(onClick = seeAll))
            }
            tracks.take(MOBILE_UP_NEXT).forEachIndexed { index, track -> TrackRow(track, { play(tracks, index) }) }
        }
    }
}

@Composable
fun PlayActions(modifier: Modifier = Modifier, onPlay: () -> Unit, onShuffle: () -> Unit) {
    val palette = LocalPalette.current
    Row(modifier, horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(
            Modifier.size(52.dp).clip(CircleShape).background(palette.action).clickable(onClick = onPlay),
            contentAlignment = Alignment.Center,
        ) {
            Icon(Lucide.Play, stringResource(R.string.action_play), tint = palette.onAction, modifier = Modifier.size(22.dp))
        }
        Row(
            Modifier
                .height(40.dp)
                .clip(CircleShape)
                .background(palette.background)
                .border(1.dp, palette.controlBorder, CircleShape)
                .clickable(onClick = onShuffle)
                .padding(horizontal = 20.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Icon(Lucide.Shuffle, null, tint = palette.foreground, modifier = Modifier.size(16.dp))
            Text(stringResource(R.string.action_shuffle), style = Type.small.copy(fontWeight = FontWeight.Medium))
        }
    }
}

@Composable
private fun QuickTiles(blocks: List<HomeBlock>, nav: NavController, play: (List<Track>, Int) -> Unit) {
    val media = LocalContainer.current.media
    val tiles = mutableListOf<@Composable () -> Unit>()

    for (block in blocks) {
        val tracks = block.tracks.orEmpty()
        if (block.layout == "Tile") {
            tiles += {
                Tile(stringResource(R.string.home_liked_songs), pluralStringResource(R.plurals.count_tracks, block.totalCount ?: tracks.size, block.totalCount ?: tracks.size), { nav.navigate(FavoritesRoute) }) {
                    Mosaic(tracks)
                }
            }
            continue
        }
        block.albums.orEmpty().forEach { album ->
            tiles += { Tile(album.title, album.artistName, { nav.navigate(AlbumRoute(album.id)) }) { Cover(media.albumCover(album, small = true), album.title, Modifier.fillMaxSize()) } }
        }
        tracks.forEachIndexed { index, track ->
            tiles += { Tile(track.title, artistsOf(track), { play(tracks, index) }) { Cover(trackCover(track), track.albumTitle ?: track.title, Modifier.fillMaxSize()) } }
        }
        block.playlists.orEmpty().forEach { playlist ->
            tiles += {
                Tile(playlist.name, pluralStringResource(R.plurals.count_tracks, playlist.trackCount, playlist.trackCount), { nav.navigate(PlaylistRoute(playlist.id)) }) {
                    Cover(media.playlistCover(playlist.id, playlist.hasCover, playlist.coverTrackId, small = true), playlist.name, Modifier.fillMaxSize())
                }
            }
        }
    }

    Column(Modifier.padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        tiles.take(MOBILE_QUICK).forEach { it() }
    }
}

@Composable
private fun Tile(label: String, sublabel: String, onClick: () -> Unit, art: @Composable () -> Unit) {
    val palette = LocalPalette.current
    Row(
        Modifier.fillMaxWidth().height(56.dp).clip(Radius.tile).background(palette.card).clickable(onClick = onClick),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(Modifier.size(56.dp)) { art() }
        Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
            Text(label, style = Type.small.copy(fontWeight = FontWeight.Medium), maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(sublabel, style = Type.tiny, color = palette.muted, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
    }
}

@Composable
private fun Mosaic(tracks: List<Track>) {
    val covers = tracks.take(4)
    if (covers.size < 4) {
        covers.firstOrNull()?.let { Cover(trackCover(it), it.albumTitle ?: it.title, Modifier.fillMaxSize()) }
        return
    }
    Column(Modifier.fillMaxSize()) {
        covers.chunked(2).forEach { pair ->
            Row(Modifier.weight(1f)) { pair.forEach { Cover(trackCover(it), it.albumTitle ?: it.title, Modifier.weight(1f).fillMaxSize()) } }
        }
    }
}

private fun onePerAlbum(tracks: List<Track>): List<Track> {
    val seen = mutableSetOf<String>()
    val distinct = tracks.filter { track -> track.albumId?.let { seen.add(it) } ?: true }
    return if (distinct.size >= MIN_DISTINCT) distinct else tracks
}

@Composable
private fun blockTitle(block: HomeBlock): String {
    val subject = block.reason?.subject
    return when (block.baseKey) {
        "dailyMix" -> stringResource(R.string.home_daily_mix)
        "favorites" -> stringResource(R.string.home_liked_songs)
        "quickTiles" -> stringResource(R.string.home_quick_picks)
        "newArrivals" -> stringResource(R.string.home_new_arrivals)
        "topTracks" -> stringResource(R.string.home_top_this_week)
        "newAlbums" -> stringResource(R.string.home_new_albums)
        "yourPlaylists" -> stringResource(R.string.home_your_playlists)
        "becauseYouListened" -> if (subject != null) stringResource(R.string.rec_because_you_listened, subject) else stringResource(R.string.rec_for_you)
        "discover" -> stringResource(R.string.rec_discover)
        "artistsForYou" -> stringResource(R.string.rec_artists_for_you)
        else -> stringResource(R.string.rec_for_you)
    }
}

@Composable
private fun reasonLabel(reason: RecommendationReason): String? {
    val subject = reason.subject.orEmpty()
    return when (reason.kind) {
        "becauseYouListened" -> stringResource(R.string.reason_because_you_listened, subject)
        "similarTaste" -> stringResource(R.string.reason_similar_taste)
        "newFromArtist" -> stringResource(R.string.reason_new_from_artist, subject)
        "genre" -> stringResource(R.string.reason_genre, subject)
        "trending" -> stringResource(R.string.reason_trending)
        "fresh" -> stringResource(R.string.reason_fresh)
        "soundsLike" -> stringResource(R.string.reason_sounds_like, subject)
        "matchesYourTaste" -> stringResource(R.string.reason_matches_your_taste)
        "discovery" -> stringResource(R.string.reason_discovery)
        else -> null
    }
}

private fun blockLink(block: HomeBlock): Any? = when (block.baseKey) {
    "dailyMix" -> MixRoute("daily")
    "favorites" -> FavoritesRoute
    "quickTiles" -> RecentRoute
    "newArrivals" -> MixRoute("new")
    "topTracks" -> MixRoute("top")
    "newAlbums" -> AlbumsRoute
    "yourPlaylists" -> PlaylistsRoute
    "artistsForYou" -> ArtistsRoute
    else -> null
}
