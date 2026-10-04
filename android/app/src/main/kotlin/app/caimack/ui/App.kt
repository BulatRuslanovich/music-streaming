// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.WindowInsetsSides
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.only
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.em
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.navigation.NavController
import androidx.navigation.NavDestination.Companion.hasRoute
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import androidx.navigation.toRoute
import app.caimack.R
import app.caimack.api.Track
import app.caimack.api.User
import kotlin.reflect.KClass
import kotlinx.coroutines.launch
import kotlinx.serialization.Serializable

@Serializable data object HomeRoute
@Serializable data object SearchRoute
@Serializable data object TracksRoute
@Serializable data object PlaylistsRoute
@Serializable data object FavoritesRoute
@Serializable data object AlbumsRoute
@Serializable data object ArtistsRoute
@Serializable data object GenresRoute
@Serializable data object RecentRoute
@Serializable data object SettingsRoute
@Serializable data object DownloadsRoute
@Serializable data class AlbumRoute(val id: String)
@Serializable data class ArtistRoute(val id: String)
@Serializable data class PlaylistRoute(val id: String)
@Serializable data class GenreRoute(val id: String, val name: String)
@Serializable data class MixRoute(val kind: String)

private data class Tab(val route: Any, val type: KClass<*>, val label: Int, val icon: ImageVector)

private val tabs = listOf(
    Tab(HomeRoute, HomeRoute::class, R.string.nav_home, Lucide.House),
    Tab(SearchRoute, SearchRoute::class, R.string.nav_search, Lucide.Search),
    Tab(TracksRoute, TracksRoute::class, R.string.nav_tracks, Lucide.AudioLines),
    Tab(PlaylistsRoute, PlaylistsRoute::class, R.string.nav_playlists, Lucide.ListMusic),
)

private val library = listOf(
    Tab(FavoritesRoute, FavoritesRoute::class, R.string.nav_favorites, Lucide.Heart),
    Tab(AlbumsRoute, AlbumsRoute::class, R.string.nav_albums, Lucide.Disc3),
    Tab(ArtistsRoute, ArtistsRoute::class, R.string.nav_artists, Lucide.UsersRound),
    Tab(GenresRoute, GenresRoute::class, R.string.nav_genres, Lucide.Tags),
    Tab(RecentRoute, RecentRoute::class, R.string.nav_recently_played, Lucide.History),
    Tab(DownloadsRoute, DownloadsRoute::class, R.string.downloads_title, Lucide.Download),
    Tab(SettingsRoute, SettingsRoute::class, R.string.settings_title, Lucide.Settings),
)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun App(user: User) {
    val palette = LocalPalette.current
    val container = LocalContainer.current
    val nav = rememberNavController()
    val scope = rememberCoroutineScope()
    var more by remember { mutableStateOf(false) }
    var expanded by rememberSaveable { mutableStateOf(false) }
    val playback by container.player.state.collectAsStateWithLifecycle()
    val play: (List<Track>, Int) -> Unit = { tracks, index -> container.player.play(tracks, index) }

    val full = expanded && playback.current != null

    LaunchedEffect(Unit) { container.player.connect() }

    LaunchedEffect(playback.current == null) {
        if (playback.current == null) expanded = false
    }

    LaunchedEffect(user.id) {
        runCatching { withNetworkRetries { container.api.settings() } }.onSuccess { container.settings.value = it }
    }

    val navigate: (Any) -> Unit = { route ->
        expanded = false
        nav.navigate(route) { launchSingleTop = true }
    }

    CompositionLocalProvider(LocalNavigate provides navigate) {
    Scaffold(
        modifier = Modifier
            .windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Horizontal))
            .then(if (full) Modifier.clearAndSetSemantics {} else Modifier),
        containerColor = palette.background,
        contentWindowInsets = WindowInsets(0),
        topBar = { Header() },
        bottomBar = {
            Column {
                MiniPlayer(playback) { expanded = true }
                BottomBar(nav) { more = true }
            }
        },
    ) { padding ->
        NavHost(nav, startDestination = HomeRoute, modifier = Modifier.padding(padding)) {
            composable<HomeRoute> { HomeScreen(nav, play) }
            composable<SearchRoute> { SearchScreen(nav, play) }
            composable<TracksRoute> { TracksScreen(play) }
            composable<PlaylistsRoute> { PlaylistsScreen(nav) }
            composable<FavoritesRoute> { FavoritesScreen(play) }
            composable<AlbumsRoute> { AlbumsScreen(nav) }
            composable<ArtistsRoute> { ArtistsScreen(nav) }
            composable<GenresRoute> { GenresScreen(nav) }
            composable<RecentRoute> { RecentScreen(play) }
            composable<SettingsRoute> { SettingsScreen() }
            composable<DownloadsRoute> { DownloadsScreen(play) }
            composable<AlbumRoute> { AlbumScreen(it.toRoute<AlbumRoute>().id, nav, play) }
            composable<ArtistRoute> { ArtistScreen(it.toRoute<ArtistRoute>().id, nav, play) }
            composable<PlaylistRoute> { PlaylistScreen(it.toRoute<PlaylistRoute>().id, play) }
            composable<GenreRoute> { it.toRoute<GenreRoute>().let { route -> GenreScreen(route.id, route.name, play) } }
            composable<MixRoute> { MixScreen(it.toRoute<MixRoute>().kind, play) }
        }
    }

    if (full) {
        FullPlayer(playback) { expanded = false }
    }
    }

    if (more) {
        ModalBottomSheet(onDismissRequest = { more = false }, containerColor = palette.card) {
            Column(Modifier.navigationBarsPadding().padding(horizontal = 12.dp, vertical = 8.dp)) {
                library.forEach { entry ->
                    SheetRow(stringResource(entry.label), entry.icon) {
                        more = false
                        nav.navigate(entry.route) { launchSingleTop = true }
                    }
                }
                HorizontalDivider(Modifier.padding(vertical = 8.dp), color = palette.border)
                Row(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                    Box(Modifier.size(32.dp).clip(androidx.compose.foundation.shape.CircleShape).background(palette.raised), contentAlignment = Alignment.Center) {
                        Text(user.username.take(1).uppercase(), style = Type.tiny.copy(fontWeight = FontWeight.SemiBold))
                    }
                    Text(user.username, style = Type.small.copy(fontWeight = FontWeight.Medium), modifier = Modifier.weight(1f).padding(horizontal = 10.dp))
                    Icon(
                        Lucide.LogOut,
                        stringResource(R.string.sign_out),
                        tint = palette.muted,
                        modifier = Modifier.clip(Radius.row).clickable {
                            more = false
                            Remote.cache.clear()
                            container.player.stop()
                            scope.launch { container.session.logout() }
                        }.padding(8.dp),
                    )
                }
            }
        }
    }
}

@Composable
internal fun SheetRow(label: String, icon: ImageVector, onClick: () -> Unit) {
    val palette = LocalPalette.current
    Row(
        Modifier.fillMaxWidth().clip(Radius.row).clickable(onClick = onClick).padding(horizontal = 12.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Icon(icon, null, tint = palette.muted)
        Text(label, style = Type.small.copy(fontWeight = FontWeight.Medium), color = palette.muted)
    }
}

@Composable
private fun Header() {
    val palette = LocalPalette.current
    Column(Modifier.background(palette.background).statusBarsPadding()) {
        Row(Modifier.fillMaxWidth().height(52.dp).padding(horizontal = 16.dp), verticalAlignment = Alignment.CenterVertically) {
            BrandMark(palette.foreground, palette.primary, Modifier.size(28.dp))
            Text(
                stringResource(R.string.app_name),
                style = Type.title.copy(fontSize = 18.sp, letterSpacing = (-0.025).em),
                modifier = Modifier.padding(start = 8.dp),
            )
        }
        HorizontalDivider(color = palette.border)
    }
}

@Composable
private fun BottomBar(nav: NavController, onMore: () -> Unit) {
    val palette = LocalPalette.current
    val destination = nav.currentBackStackEntryAsState().value?.destination
    val inLibrary = library.any { destination?.hasRoute(it.type) == true }

    Column(Modifier.background(palette.card).navigationBarsPadding()) {
        HorizontalDivider(color = palette.border)
        Row(Modifier.fillMaxWidth().height(62.dp)) {
            tabs.forEach { tab ->
                val active = destination?.hasRoute(tab.type) == true
                TabItem(stringResource(tab.label), tab.icon, active) {
                    nav.navigate(tab.route) {
                        popUpTo(HomeRoute) { saveState = true }
                        launchSingleTop = true
                        restoreState = true
                    }
                }
            }
            TabItem(stringResource(R.string.nav_more), Lucide.Ellipsis, inLibrary, onMore)
        }
    }
}

@Composable
private fun androidx.compose.foundation.layout.RowScope.TabItem(label: String, icon: ImageVector, active: Boolean, onClick: () -> Unit) {
    val palette = LocalPalette.current
    Column(
        Modifier.weight(1f).fillMaxSize().clickable(onClick = onClick),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(2.dp, Alignment.CenterVertically),
    ) {
        Icon(icon, null, tint = if (active) palette.primary else palette.faint)
        Text(
            label,
            style = Type.micro.copy(fontWeight = if (active) FontWeight.SemiBold else FontWeight.Normal),
            color = if (active) palette.foreground else palette.faint,
            maxLines = 1,
        )
    }
}
