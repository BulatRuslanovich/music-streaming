// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
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
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import androidx.navigation.NavController
import app.caimack.R
import app.caimack.api.SearchResults
import app.caimack.api.Track
import kotlinx.coroutines.delay

private const val MIN_LENGTH = 3
private const val LIMIT = 25
private const val PREVIEW = 5

@Composable
fun SearchScreen(nav: NavController, play: (List<Track>, Int) -> Unit) {
    val palette = LocalPalette.current
    val container = LocalContainer.current
    var input by rememberSaveable { mutableStateOf("") }
    var query by rememberSaveable { mutableStateOf("") }
    var tab by rememberSaveable { mutableStateOf("all") }

    LaunchedEffect(input) {
        delay(300)
        query = input.trim()
    }

    Column(Modifier.fillMaxSize()) {
        PageTitle(stringResource(R.string.nav_search))
        Row(
            Modifier
                .padding(horizontal = 16.dp)
                .fillMaxWidth()
                .height(44.dp)
                .clip(Radius.row)
                .background(palette.raised)
                .border(1.dp, palette.controlBorder, Radius.row)
                .padding(horizontal = 14.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Icon(Lucide.Search, null, tint = palette.muted, modifier = Modifier.size(16.dp))
            Box(Modifier.weight(1f)) {
                if (input.isEmpty()) Text(stringResource(R.string.search_placeholder), style = Type.body, color = palette.faint)
                BasicTextField(
                    value = input,
                    onValueChange = { input = it },
                    singleLine = true,
                    textStyle = Type.body.copy(color = palette.foreground),
                    cursorBrush = SolidColor(palette.foreground),
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        }

        if (query.length < MIN_LENGTH) {
            Text(
                stringResource(R.string.search_hint),
                style = Type.small,
                color = palette.muted,
                modifier = Modifier.padding(16.dp),
            )
            BrowseGenres(nav)
            return@Column
        }

        LazyRow(contentPadding = PaddingValues(16.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            items(listOf("all" to R.string.search_tab_all, "tracks" to R.string.nav_tracks, "albums" to R.string.nav_albums, "artists" to R.string.nav_artists)) { (value, label) ->
                Chip(stringResource(label), tab == value) { tab = value }
            }
        }

        when (tab) {
            "tracks" -> {
                val pager = rememberPager("search-tracks:$query") { container.api.searchTracks(query, it, TRACK_PAGE_SIZE) }
                PagedColumn(pager, stringResource(R.string.search_nothing_found), header = {}) { index, track -> TrackRow(track, { play(pager.items, index) }) }
            }
            "albums" -> {
                val pager = rememberPager("search-albums:$query") { container.api.searchAlbums(query, it, CARD_PAGE_SIZE) }
                PagedGrid(pager, stringResource(R.string.search_nothing_found), header = {}) { album ->
                    Card(album.title, album.artistName, { nav.navigate(AlbumRoute(album.id)) }) { Cover(container.media.albumCover(album, small = true), album.title, it) }
                }
            }
            "artists" -> {
                val pager = rememberPager("search-artists:$query") { container.api.searchArtists(query, it, CARD_PAGE_SIZE) }
                PagedGrid(pager, stringResource(R.string.search_nothing_found), header = {}) { artist ->
                    Card(artist.name, pluralStringResource(R.plurals.count_tracks, artist.trackCount, artist.trackCount), { nav.navigate(ArtistRoute(artist.id)) }, round = true) {
                        Cover(container.media.artistImage(artist.id, artist.hasImage, small = true), artist.name, it, round = true)
                    }
                }
            }
            else -> Load("search:$query", { container.api.search(query, LIMIT) }, isEmpty = { it.isEmpty() }, empty = { Empty(stringResource(R.string.search_nothing_found)) }) { results ->
                AllResults(results, nav, play)
            }
        }
    }
}

private fun SearchResults.isEmpty() = tracks.isEmpty() && albums.isEmpty() && artists.isEmpty() && genres.isEmpty()

@Composable
private fun AllResults(results: SearchResults, nav: NavController, play: (List<Track>, Int) -> Unit) {
    val palette = LocalPalette.current
    val media = LocalContainer.current.media

    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(bottom = 24.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        results.top?.let { top ->
            item {
                SectionHeader(stringResource(R.string.search_top_result))
                val (title, subtitle, url, round, open) = when {
                    top.artist != null -> TopRow(top.artist.name, stringResource(R.string.artists_kind), media.artistImage(top.artist.id, top.artist.hasImage, true), true) { nav.navigate(ArtistRoute(top.artist.id)) }
                    top.album != null -> TopRow(top.album.title, top.album.artistName, media.albumCover(top.album, true), false) { nav.navigate(AlbumRoute(top.album.id)) }
                    top.genre != null -> TopRow(top.genre.name, pluralStringResource(R.plurals.count_tracks, top.genre.trackCount, top.genre.trackCount), null, false) { nav.navigate(GenreRoute(top.genre.id, top.genre.name)) }
                    top.track != null -> TopRow(top.track.title, artistsOf(top.track), trackCover(top.track), false) { play(listOf(top.track), 0) }
                    else -> return@item
                }
                Row(
                    Modifier.padding(16.dp).fillMaxWidth().clip(Radius.panel).background(palette.card).clickable(onClick = open).padding(16.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(16.dp),
                ) {
                    Cover(url, title, Modifier.size(72.dp), round = round)
                    Column {
                        Text(title, style = Type.title)
                        Text(subtitle, style = Type.small, color = palette.muted)
                    }
                }
            }
        }
        if (results.tracks.isNotEmpty()) {
            item { SectionHeader(stringResource(R.string.nav_tracks)) }
            itemsIndexed(results.tracks.take(PREVIEW)) { index, track -> TrackRow(track, { play(results.tracks, index) }) }
        }
        if (results.albums.isNotEmpty()) {
            item {
                Column(Modifier.padding(top = 16.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                    SectionHeader(stringResource(R.string.nav_albums))
                    LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        items(results.albums, key = { it.id }) { album ->
                            Card(album.title, album.artistName, { nav.navigate(AlbumRoute(album.id)) }, Modifier.width(116.dp)) { Cover(media.albumCover(album, true), album.title, it) }
                        }
                    }
                }
            }
        }
        if (results.artists.isNotEmpty()) {
            item {
                Column(Modifier.padding(top = 16.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                    SectionHeader(stringResource(R.string.nav_artists))
                    LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        items(results.artists, key = { it.id }) { artist ->
                            Card(artist.name, pluralStringResource(R.plurals.count_tracks, artist.trackCount, artist.trackCount), { nav.navigate(ArtistRoute(artist.id)) }, Modifier.width(116.dp), round = true) {
                                Cover(media.artistImage(artist.id, artist.hasImage, true), artist.name, it, round = true)
                            }
                        }
                    }
                }
            }
        }
        if (results.genres.isNotEmpty()) {
            item { SectionHeader(stringResource(R.string.nav_genres)) }
            items(results.genres, key = { it.id }) { genre -> GenreRow(genre) { nav.navigate(GenreRoute(genre.id, genre.name)) } }
        }
    }
}

private data class TopRow(val title: String, val subtitle: String, val url: String?, val round: Boolean, val open: () -> Unit)

@Composable
private fun BrowseGenres(nav: NavController) {
    val container = LocalContainer.current
    Load("genres", { container.api.genres() }, isEmpty = { it.isEmpty() }) { genres ->
        Column(Modifier.padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text(stringResource(R.string.search_browse_genres), style = Type.section)
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                genres.forEach { genre -> Chip(genre.name, false) { nav.navigate(GenreRoute(genre.id, genre.name)) } }
            }
        }
    }
}

@Composable
private fun Chip(label: String, active: Boolean, onClick: () -> Unit) {
    val palette = LocalPalette.current
    Text(
        label,
        style = Type.small.copy(fontWeight = FontWeight.Medium),
        color = if (active) palette.primary else palette.muted,
        modifier = Modifier
            .clip(CircleShape)
            .background(if (active) palette.primarySoft else palette.raised)
            .then(if (active) Modifier.border(1.dp, palette.primary, CircleShape) else Modifier)
            .clickable(onClick = onClick)
            .padding(horizontal = 16.dp, vertical = 8.dp),
    )
}
