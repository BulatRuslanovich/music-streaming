// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.ui

import android.widget.Toast
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.detectVerticalDragGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.TransformOrigin
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.zIndex
import androidx.navigation.NavController
import app.caimack.R
import app.caimack.api.Album
import app.caimack.api.Track
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlin.math.abs
import kotlin.math.min

// Сколько конвертов видно за передним и сколько уже перелистанных ещё падают вперёд.
private const val BEHIND = 12
private const val FLIPPED = 2
// Подгрузка следующей страницы, когда до конца ящика осталось столько конвертов.
private const val PREFETCH_AHEAD = 10

fun letterOf(title: String): String {
    val first = title.trim().firstOrNull()?.uppercaseChar() ?: return "#"
    return if (first.isLetter()) first.toString() else "#"
}

// Альбомы как пластинки в ящике — как на вебе (frontend/src/components/crate/RecordCrate.tsx): стопка
// конвертов, наклонённых назад; свайп вверх листает вперёд, перелистнутые падают к зрителю, передний
// можно вытащить — из него выезжает пластинка.
@Composable
fun RecordCrate(albums: List<Album>, total: Int, hasMore: Boolean, onNeedMore: () -> Unit, nav: NavController) {
    val palette = LocalPalette.current
    val container = LocalContainer.current
    val context = LocalContext.current
    var focus by rememberSaveable { mutableIntStateOf(0) }
    var pulled by remember { mutableStateOf(false) }
    val last = albums.lastIndex
    val current = albums.getOrNull(min(focus, last)) ?: return
    val swipeStep = with(LocalDensity.current) { 42.dp.toPx() }

    fun go(delta: Int) {
        pulled = false
        focus = (focus + delta).coerceIn(0, last)
    }

    LaunchedEffect(focus, albums.size, hasMore) {
        if (hasMore && focus >= albums.size - PREFETCH_AHEAD) onNeedMore()
    }

    fun play(album: Album) {
        container.scope.launch {
            val tracks: List<Track> = runCatching { container.api.album(album.id).tracks }.getOrDefault(emptyList())
            withContext(Dispatchers.Main) {
                if (tracks.isEmpty()) Toast.makeText(context, R.string.error_load, Toast.LENGTH_SHORT).show()
                else container.player.play(tracks, 0, "album")
            }
        }
    }

    Column(Modifier.fillMaxWidth().padding(top = 8.dp), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(18.dp)) {
        BoxWithConstraints(Modifier.fillMaxWidth().padding(horizontal = 24.dp)) {
            val sleeve = min(maxWidth.value * 0.72f, 300f).dp
            val crateLabel = stringResource(R.string.crate_label)
            var travel by remember { mutableFloatStateOf(0f) }

            Box(
                Modifier
                    .align(Alignment.Center)
                    .width(sleeve * 1.3f)
                    .height(sleeve * 1.62f)
                    .semantics { contentDescription = crateLabel }
                    .pointerInput(last) {
                        detectVerticalDragGestures(
                            onDragStart = { travel = 0f },
                        ) { change, amount ->
                            change.consume()
                            travel -= amount
                            val steps = (travel / swipeStep).toInt()
                            if (steps != 0) {
                                travel -= steps * swipeStep
                                go(steps)
                            }
                        }
                    }
                    .pointerInput(Unit) { detectTapGestures { pulled = !pulled } },
            ) {
                albums.forEachIndexed { index, album ->
                    val depth = index - focus
                    if (depth < -FLIPPED || depth > BEHIND) return@forEachIndexed
                    key(album.id) {
                        Sleeve(album, depth, pulled && depth == 0, sleeve, Modifier.align(Alignment.BottomCenter).offset(y = -sleeve * 0.1f))
                    }
                }

                // Передняя стенка ящика с разделителем-буквой, как в магазине пластинок.
                Box(
                    Modifier
                        .align(Alignment.BottomCenter)
                        .zIndex(50f)
                        .fillMaxWidth()
                        .height(sleeve * 0.26f)
                        .shadow(18.dp, RoundedCornerShape(12.dp, 12.dp, 8.dp, 8.dp))
                        .clip(RoundedCornerShape(12.dp, 12.dp, 8.dp, 8.dp))
                        .background(Brush.verticalGradient(listOf(androidx.compose.ui.graphics.lerp(palette.card, palette.foreground, 0.1f), palette.card))),
                )
                Box(
                    Modifier
                        .align(Alignment.BottomStart)
                        .zIndex(51f)
                        .offset(x = 20.dp, y = -(sleeve * 0.26f) + 20.dp)
                        .size(44.dp)
                        .clip(RoundedCornerShape(4.dp, 4.dp, 0.dp, 0.dp))
                        .background(palette.primary),
                    contentAlignment = Alignment.Center,
                ) {
                    Text(letterOf(current.title), style = Type.title.copy(fontSize = 22.sp), color = palette.onPrimary)
                }
            }
        }

        Row(Modifier.widthIn(max = 420.dp).fillMaxWidth().padding(horizontal = 12.dp), verticalAlignment = Alignment.CenterVertically) {
            CrateButton(Lucide.ChevronUp, stringResource(R.string.crate_previous), enabled = focus > 0) { go(-1) }
            Column(Modifier.weight(1f), horizontalAlignment = Alignment.CenterHorizontally) {
                Text(
                    current.title,
                    style = Type.body.copy(fontWeight = FontWeight.SemiBold),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    textAlign = TextAlign.Center,
                    modifier = Modifier.clickable { nav.navigate(AlbumRoute(current.id)) },
                )
                Text(
                    listOfNotNull(current.artistName, current.year?.toString()).joinToString(" · "),
                    style = Type.small,
                    color = palette.muted,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(stringResource(R.string.crate_position, focus + 1, maxOf(total, albums.size)), style = Type.tiny, color = palette.faint)
            }
            CrateButton(Lucide.ChevronDown, stringResource(R.string.crate_next), enabled = focus < last) { go(1) }
        }

        Row(
            Modifier
                .height(48.dp)
                .clip(CircleShape)
                .background(palette.action)
                .clickable { play(current) }
                .padding(horizontal = 22.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Icon(Lucide.Play, null, tint = palette.onAction, modifier = Modifier.size(16.dp))
            Text(
                stringResource(R.string.crate_play, current.title),
                style = Type.small.copy(fontWeight = FontWeight.SemiBold),
                color = palette.onAction,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.widthIn(max = 260.dp),
            )
        }
    }
}

// Конверт в ящике. Глубина плавно анимируется, поэтому перелистывание — движение, а не скачок.
@Composable
private fun Sleeve(album: Album, depth: Int, pulled: Boolean, size: androidx.compose.ui.unit.Dp, modifier: Modifier) {
    val container = LocalContainer.current
    val d by animateFloatAsState(depth.toFloat(), tween(520), label = "crate-depth")
    val lift by animateFloatAsState(if (pulled) 1f else 0f, tween(520), label = "crate-pull")
    val density = LocalDensity.current
    val edge = with(density) { size.toPx() }

    val behind = d.coerceAtLeast(0f)
    val flipped = (-d).coerceIn(0f, 1f)
    val dim = (behind * 0.05f).coerceAtMost(0.6f)

    Box(
        modifier
            .zIndex((BEHIND + 1 - abs(depth)).toFloat())
            .size(size)
            .graphicsLayer {
                transformOrigin = TransformOrigin(0.5f, 1f)
                cameraDistance = 14f * density.density
                // За передним — наклон назад, сдвиг вверх и вглубь; перелистнутые падают вперёд и гаснут.
                rotationX = when {
                    d > 0f -> -14f
                    d < 0f -> 72f * flipped
                    else -> -4f
                } + (if (d in 0f..1f) -10f * d else 0f) + lift * 4f
                // Подъём на шаг больше, чем съедает уменьшение, — верхушки задних конвертов торчат над передним.
                translationY = -behind * edge * 0.075f - lift * edge * 0.08f
                val depthScale = 1f - behind * 0.03f
                scaleX = depthScale
                scaleY = depthScale
                alpha = 1f - flipped
            }
            .drawWithContent {
                drawContent()
                if (dim > 0f) drawRect(Color.Black.copy(alpha = dim))
            },
    ) {
        if (depth == 0) {
            Record(
                Track(id = album.id, title = album.title, artistId = album.artistId, artistName = album.artistName, albumId = album.id, albumTitle = album.title, durationSeconds = 0, hasCover = album.hasCover),
                spinning = false,
                out = pulled,
                vertical = true,
                modifier = Modifier.size(size),
            )
        } else {
            Cover(container.media.albumCover(album, small = abs(depth) >= 3), album.title, Modifier.size(size).shadow(12.dp, Radius.cover))
        }
    }
}

@Composable
private fun CrateButton(icon: androidx.compose.ui.graphics.vector.ImageVector, label: String, enabled: Boolean, onClick: () -> Unit) {
    val palette = LocalPalette.current
    Box(
        Modifier.size(44.dp).clip(CircleShape).clickable(enabled = enabled, onClickLabel = label, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) {
        Icon(icon, label, tint = if (enabled) palette.foreground else palette.faint.copy(alpha = 0.5f), modifier = Modifier.size(20.dp))
    }
}
