// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.ui

import android.widget.Toast
import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalResources
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import app.caimack.R
import app.caimack.api.RadioRequest
import app.caimack.api.Recap
import app.caimack.api.Track
import java.time.LocalDate
import java.time.format.TextStyle
import java.util.Locale
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.roundToInt
import kotlin.math.sqrt
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

private const val SLIDE_MS = 7000

// Короткое касание листает, удержание дольше этого — пауза, как в сторис.
private const val HOLD_MS = 250L

private class RecapSlide(val key: String, val art: String?, val content: @Composable () -> Unit)

// Единственный вход в итоги. Сервер отдаёт их только в первую неделю месяца (иначе 204), так что
// плашка появляется 1-го числа и исчезает 8-го сама.
@Composable
fun RecapBanner(play: (List<Track>, Int) -> Unit) {
    val palette = LocalPalette.current
    val container = LocalContainer.current
    var recap by remember { mutableStateOf(Remote.cache[RECAP] as Recap?) }
    var open by remember { mutableStateOf(false) }

    LaunchedEffect(Unit) {
        runCatching { withNetworkRetries { container.api.recap() } }.getOrNull()?.let { response ->
            recap = response.body()?.takeIf { response.code() == 200 }
            if (recap != null) Remote.cache[RECAP] = recap!! else Remote.cache.remove(RECAP)
        }
    }

    val shown = recap ?: return
    val covers = shown.topTracks.map { it.track }.filter { it.hasCover }.take(3)

    if (open) {
        Dialog(
            onDismissRequest = { open = false },
            properties = DialogProperties(usePlatformDefaultWidth = false, decorFitsSystemWindows = false),
        ) {
            RecapStory(shown, play) { open = false }
        }
    }

    Box(
        Modifier
            .padding(horizontal = 16.dp)
            .fillMaxWidth()
            .clip(Radius.panel)
            .background(palette.card)
            .clickable { open = true },
    ) {
        covers.firstOrNull()?.let { ArtBackdrop(trackCover(it, small = false), BackdropMode.Header, Modifier.matchParentSize()) }
        Row(
            Modifier.padding(16.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            if (covers.isNotEmpty()) {
                Box(Modifier.width((48 + 18 * (covers.size - 1)).dp).height(56.dp)) {
                    covers.forEachIndexed { index, track ->
                        Cover(
                            trackCover(track),
                            track.albumTitle ?: track.title,
                            Modifier
                                .padding(start = (18 * index).dp, top = 4.dp)
                                .size(48.dp)
                                .rotate((index - (covers.size - 1) / 2f) * 7f),
                        )
                    }
                }
            }
            Column(Modifier.weight(1f)) {
                Text(
                    stringResource(R.string.recap_banner, monthName(shown.year, shown.month)),
                    style = Type.section,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    stringResource(R.string.recap_banner_note, listeningTime(shown.listenedSeconds)),
                    style = Type.small,
                    color = palette.muted,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            Icon(Lucide.ChevronRight, null, tint = palette.foreground, modifier = Modifier.size(18.dp))
        }
    }
}

private const val RECAP = "recap"

@Composable
private fun RecapStory(recap: Recap, play: (List<Track>, Int) -> Unit, onClose: () -> Unit) {
    val palette = LocalPalette.current
    var index by remember { mutableIntStateOf(0) }
    val slides = recapSlides(recap, play, onClose) { index = 0 }
    val last = slides.lastIndex
    val slide = slides[index.coerceAtMost(last)]

    var paused by remember { mutableStateOf(false) }
    var held by remember { mutableStateOf(false) }
    val progress = remember { Animatable(0f) }

    val go: (Int) -> Unit = { step -> index = (index + step).coerceIn(0, last) }

    LaunchedEffect(index) { progress.snapTo(0f) }
    LaunchedEffect(index, paused, held) {
        if (paused || held) return@LaunchedEffect
        val left = ((1f - progress.value) * SLIDE_MS).roundToInt()
        progress.animateTo(1f, tween(left, easing = LinearEasing))
        if (index < last) index++
    }

    BackHandler(onBack = onClose)

    Surface(Modifier.fillMaxSize(), color = palette.background) {
        Box(Modifier.fillMaxSize()) {
            ArtBackdrop(slide.art, BackdropMode.Stage, Modifier.matchParentSize())
            Column(Modifier.safeDrawingPadding().padding(horizontal = 20.dp, vertical = 12.dp)) {
                Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                    slides.indices.forEach { position ->
                        Box(Modifier.weight(1f).height(3.dp).clip(CircleShape).background(palette.foreground.copy(alpha = 0.2f))) {
                            val fill = when {
                                position < index -> 1f
                                position == index -> progress.value
                                else -> 0f
                            }
                            Box(Modifier.fillMaxWidth(fill).fillMaxHeight().background(palette.foreground))
                        }
                    }
                }
                Row(Modifier.fillMaxWidth().padding(top = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text(stringResource(R.string.recap_title), style = Type.small, color = palette.muted, modifier = Modifier.weight(1f))
                    Icon(
                        if (paused) Lucide.Play else Lucide.Pause,
                        stringResource(if (paused) R.string.recap_resume else R.string.recap_pause),
                        tint = palette.foreground,
                        modifier = Modifier.clip(CircleShape).clickable { paused = !paused }.padding(10.dp).size(20.dp),
                    )
                    Icon(
                        Lucide.X,
                        stringResource(R.string.recap_close),
                        tint = palette.foreground,
                        modifier = Modifier.clip(CircleShape).clickable(onClick = onClose).padding(10.dp).size(22.dp),
                    )
                }

                BoxWithConstraints(
                    Modifier
                        .weight(1f)
                        .fillMaxWidth()
                        .pointerInput(last) {
                            var downAt = 0L
                            detectTapGestures(
                                onPress = {
                                    downAt = System.currentTimeMillis()
                                    held = true
                                    tryAwaitRelease()
                                    held = false
                                },
                                onTap = { offset ->
                                    if (System.currentTimeMillis() - downAt < HOLD_MS) go(if (offset.x < size.width / 3f) -1 else 1)
                                },
                            )
                        },
                ) {
                    AnimatedContent(
                        targetState = index.coerceAtMost(last),
                        transitionSpec = { (fadeIn(tween(400)) + slideInVertically(tween(400)) { it / 12 }) togetherWith fadeOut(tween(150)) },
                        label = "recap slide",
                    ) { shown ->
                        Column(
                            Modifier.fillMaxSize().verticalScroll(rememberScrollState()),
                            verticalArrangement = Arrangement.spacedBy(18.dp, Alignment.CenterVertically),
                        ) {
                            slides[shown].content()
                        }
                    }
                }
            }
        }
    }
}

// Слайды собираются из того, что есть: пустой раздел просто выпадает из истории.
@Composable
private fun recapSlides(recap: Recap, play: (List<Track>, Int) -> Unit, onClose: () -> Unit, onRestart: () -> Unit): List<RecapSlide> {
    val media = LocalContainer.current.media
    val artist = recap.topArtists.firstOrNull()?.artist
    val track = recap.topTracks.firstOrNull()?.track
    val artistArt = artist?.let { media.artistImage(it.id, it.hasImage, small = false) }
    val coverOf = @Composable { item: Track? -> item?.takeIf { it.hasCover }?.let { trackCover(it, small = false) } }
    val fallback = artistArt ?: coverOf(track)
    val trackArt = coverOf(track) ?: fallback
    val soundArt = coverOf(recap.soundOfMonth) ?: fallback

    return buildList {
        add(RecapSlide("intro", trackArt) { Intro(recap) })
        add(RecapSlide("time", fallback) { TimeSlide(recap) })
        if (artist != null) add(RecapSlide("artist", artistArt ?: fallback) { ArtistSlide(recap) })
        if (track != null) add(RecapSlide("track", trackArt) { TrackSlide(recap, play) })
        if (recap.daySeconds.any { it > 0 }) add(RecapSlide("day", fallback) { DaySlide(recap) })
        if (dominantDaypart(recap.hourSeconds) != null) add(RecapSlide("hours", fallback) { HoursSlide(recap) })
        recap.moods.firstOrNull()?.let { lead -> add(RecapSlide("mood", fallback) { MoodSlide(recap, lead.key) }) }
        recap.soundOfMonth?.let { sound -> add(RecapSlide("sound", soundArt) { SoundSlide(sound, play) }) }
        recap.newArtists?.let { count -> add(RecapSlide("discoveries", fallback) { DiscoveriesSlide(recap, count) }) }
        add(RecapSlide("summary", fallback) { SummarySlide(recap, onClose, onRestart) })
    }
}

private val Huge = Type.display.copy(fontSize = 52.sp, lineHeight = 54.sp)
private val Big = Type.display.copy(fontSize = 36.sp, lineHeight = 40.sp)

@Composable
private fun Eyebrow(text: String) {
    Text(text.uppercase(), style = Type.small.copy(fontWeight = FontWeight.SemiBold, letterSpacing = 1.sp), color = LocalPalette.current.primary)
}

@Composable
private fun Muted(text: String, big: Boolean = false) {
    Text(text, style = if (big) Type.section.copy(fontWeight = FontWeight.Normal) else Type.body, color = LocalPalette.current.muted)
}

@Composable
private fun Intro(recap: Recap) {
    val covers = recap.topTracks.map { it.track }.filter { it.hasCover }.take(4)
    if (covers.isNotEmpty()) {
        Box(Modifier.height(112.dp).width((96 + 56 * (covers.size - 1)).dp)) {
            covers.forEachIndexed { index, track ->
                Cover(
                    trackCover(track, small = false),
                    track.albumTitle ?: track.title,
                    Modifier.padding(start = (56 * index).dp, top = 8.dp).size(96.dp).rotate((index - (covers.size - 1) / 2f) * 6f),
                )
            }
        }
    }
    Text(stringResource(R.string.recap_intro_title, monthName(recap.year, recap.month)), style = Huge)
    Muted(stringResource(R.string.recap_intro_note), big = true)
}

@Composable
private fun TimeSlide(recap: Recap) {
    val delta = recap.previousListenedSeconds?.let { recap.listenedSeconds - it }
    Eyebrow(stringResource(R.string.recap_time_eyebrow))
    Text(listeningTime(recap.listenedSeconds), style = Huge)
    Muted(stringResource(R.string.recap_time_of_music), big = true)
    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Text(pluralStringResource(R.plurals.recap_plays, recap.plays, recap.plays), style = Type.section.copy(fontWeight = FontWeight.Normal))
        Text(pluralStringResource(R.plurals.count_tracks, recap.distinctTracks, recap.distinctTracks), style = Type.section.copy(fontWeight = FontWeight.Normal))
        Text(pluralStringResource(R.plurals.count_artists, recap.distinctArtists, recap.distinctArtists), style = Type.section.copy(fontWeight = FontWeight.Normal))
    }
    if (delta != null && abs(delta) >= 60) {
        Muted(stringResource(if (delta > 0) R.string.recap_time_more else R.string.recap_time_less, listeningTime(abs(delta))))
    }
}

@Composable
private fun ArtistSlide(recap: Recap) {
    val media = LocalContainer.current.media
    val lead = recap.topArtists.first()
    Eyebrow(stringResource(R.string.recap_artist_eyebrow))
    Cover(media.artistImage(lead.artist.id, lead.artist.hasImage, small = false), lead.artist.name, Modifier.size(220.dp), round = true)
    Text(lead.artist.name, style = Big)
    Muted(
        stringResource(R.string.recap_artist_together, listeningTime(lead.listenedSeconds)) + ", " +
            pluralStringResource(R.plurals.recap_plays, lead.plays, lead.plays),
    )
    Runners(recap.topArtists.drop(1).map { it.artist.name })
}

@Composable
private fun TrackSlide(recap: Recap, play: (List<Track>, Int) -> Unit) {
    val lead = recap.topTracks.first()
    val tracks = recap.topTracks.map { it.track }
    Eyebrow(stringResource(R.string.recap_track_eyebrow))
    PlayableCover(lead.track) { play(tracks, 0) }
    Column {
        Text(lead.track.title, style = Big)
        Muted("${artistsOf(lead.track)} · ${pluralStringResource(R.plurals.recap_times, lead.plays, lead.plays)}")
    }
    Runners(recap.topTracks.drop(1).map { it.track.title })
}

@Composable
private fun Runners(names: List<String>) {
    if (names.isEmpty()) return
    val palette = LocalPalette.current
    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Text(stringResource(R.string.recap_also_top), style = Type.small, color = palette.muted)
        names.forEachIndexed { index, name ->
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Text("${index + 2}", style = Type.body, color = palette.faint)
                Text(name, style = Type.body, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
        }
    }
}

@Composable
private fun PlayableCover(track: Track, onPlay: () -> Unit) {
    val palette = LocalPalette.current
    Box(Modifier.size(220.dp).clip(Radius.cover).clickable(onClick = onPlay)) {
        Cover(trackCover(track, small = false), track.albumTitle ?: track.title, Modifier.fillMaxSize())
        Box(
            Modifier.align(Alignment.BottomEnd).padding(12.dp).size(52.dp).clip(CircleShape).background(palette.action),
            contentAlignment = Alignment.Center,
        ) {
            Icon(Lucide.Play, null, tint = palette.onAction, modifier = Modifier.size(22.dp))
        }
    }
}

@Composable
private fun DaySlide(recap: Recap) {
    val palette = LocalPalette.current
    val locale = currentLocale()
    val peak = recap.daySeconds.indices.maxBy { recap.daySeconds[it] }
    val max = recap.daySeconds[peak]
    val date = LocalDate.of(recap.year, recap.month, peak + 1)
    val blanks = LocalDate.of(recap.year, recap.month, 1).dayOfWeek.value - 1
    val cells = List(blanks) { null } + recap.daySeconds.indices.toList()

    Eyebrow(stringResource(R.string.recap_day_eyebrow))
    Text("${date.dayOfMonth} ${date.month.getDisplayName(TextStyle.FULL, locale)}", style = Big)
    Muted(
        date.dayOfWeek.getDisplayName(TextStyle.FULL_STANDALONE, locale).replaceFirstChar { it.titlecase(locale) } + " · " +
            stringResource(R.string.recap_day_note, listeningTime(max)),
    )
    Column(Modifier.width(300.dp), verticalArrangement = Arrangement.spacedBy(5.dp)) {
        cells.chunked(7).forEach { week ->
            Row(horizontalArrangement = Arrangement.spacedBy(5.dp)) {
                week.forEach { day ->
                    val level = if (day == null) 0f else intensity(recap.daySeconds[day], max)
                    Box(
                        Modifier
                            .weight(1f)
                            .aspectRatio(1f)
                            .clip(Radius.tile)
                            .then(
                                if (day == null) Modifier
                                else Modifier.background(if (level > 0) lerp(palette.foreground.copy(alpha = 0.08f), palette.primary, 0.2f + level * 0.8f) else palette.foreground.copy(alpha = 0.08f)),
                            )
                            .then(if (day == peak) Modifier.border(2.dp, palette.foreground, Radius.tile) else Modifier),
                        contentAlignment = Alignment.Center,
                    ) {
                        if (day != null) Text("${day + 1}", style = Type.tiny.copy(fontSize = 10.sp), color = if (level > 0.6f) palette.onPrimary else palette.muted)
                    }
                }
                repeat(7 - week.size) { Box(Modifier.weight(1f)) }
            }
        }
    }
}

private val DAYPARTS = listOf(
    Triple(R.string.recap_hours_morning, 5, 11),
    Triple(R.string.recap_hours_day, 11, 17),
    Triple(R.string.recap_hours_evening, 17, 23),
    Triple(R.string.recap_hours_night, 23, 5),
)

private fun inside(hour: Int, from: Int, to: Int) = if (from < to) hour in from until to else hour >= from || hour < to

// Те же границы, что на сервере и в вебе: утро 5–11, день 11–17, вечер 17–23, ночь 23–5.
private fun dominantDaypart(hours: List<Long>): Triple<Int, Int, Int>? =
    DAYPARTS.maxByOrNull { (_, from, to) -> hours.indices.filter { inside(it, from, to) }.sumOf { hours[it] } }
        ?.takeIf { (_, from, to) -> hours.indices.filter { inside(it, from, to) }.sumOf { hours[it] } > 0 }

@Composable
private fun HoursSlide(recap: Recap) {
    val palette = LocalPalette.current
    val (title, from, to) = dominantDaypart(recap.hourSeconds) ?: return
    val total = recap.hourSeconds.sum()
    val share = recap.hourSeconds.indices.filter { inside(it, from, to) }.sumOf { recap.hourSeconds[it] } * 100 / max(total, 1)
    val max = recap.hourSeconds.max()

    Eyebrow(stringResource(R.string.recap_hours_eyebrow))
    Text(stringResource(title), style = Big)
    Muted(stringResource(R.string.recap_hours_note, share.toInt(), from, to))
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Row(Modifier.fillMaxWidth().height(140.dp), verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(3.dp)) {
            recap.hourSeconds.forEachIndexed { hour, seconds ->
                Box(
                    Modifier
                        .weight(1f)
                        .fillMaxHeight(max(0.03f, if (max > 0) seconds.toFloat() / max else 0f))
                        .clip(Radius.cover)
                        .background(if (inside(hour, from, to)) palette.primary else palette.foreground.copy(alpha = 0.25f)),
                )
            }
        }
        Row(Modifier.fillMaxWidth()) {
            listOf("00", "06", "12", "18").forEach { Text(it, style = Type.tiny, color = palette.faint, modifier = Modifier.weight(1f)) }
        }
    }
}

@Composable
private fun MoodSlide(recap: Recap, lead: String) {
    val palette = LocalPalette.current
    val container = LocalContainer.current
    val context = LocalContext.current
    var starting by remember { mutableStateOf(false) }

    Eyebrow(stringResource(R.string.recap_mood_eyebrow))
    Icon(moodIcon(lead), null, tint = palette.primary, modifier = Modifier.size(56.dp))
    Text(moodLabel(lead), style = Huge)
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        recap.moods.take(4).forEach { mood ->
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Text(moodLabel(mood.key), style = Type.small, modifier = Modifier.width(96.dp), maxLines = 1, overflow = TextOverflow.Ellipsis)
                Box(Modifier.weight(1f).height(8.dp).clip(CircleShape).background(palette.foreground.copy(alpha = 0.15f))) {
                    Box(Modifier.fillMaxWidth(mood.share.toFloat()).fillMaxHeight().clip(CircleShape).background(palette.primary))
                }
                Text("${(mood.share * 100).roundToInt()}%", style = Type.small, color = palette.muted, textAlign = TextAlign.End, modifier = Modifier.width(40.dp))
            }
        }
    }
    Chip(stringResource(R.string.recap_mood_radio, moodLabel(lead)), active = true, icon = moodIcon(lead), enabled = !starting) {
        starting = true
        container.scope.launch {
            val batch = runCatching { container.api.radio(RadioRequest(null, emptyList(), lead)) }.getOrNull()
            withContext(Dispatchers.Main) {
                starting = false
                if (batch == null || batch.tracks.isEmpty()) Toast.makeText(context, R.string.radio_failed, Toast.LENGTH_SHORT).show()
                else container.player.playMyRadio(batch, lead)
            }
        }
    }
}

@Composable
private fun SoundSlide(track: Track, play: (List<Track>, Int) -> Unit) {
    Eyebrow(stringResource(R.string.recap_sound_eyebrow))
    PlayableCover(track) { play(listOf(track), 0) }
    Column {
        Text(track.title, style = Big)
        Muted(artistsOf(track))
    }
    Muted(stringResource(R.string.recap_sound_note))
}

@Composable
private fun DiscoveriesSlide(recap: Recap, count: Int) {
    val media = LocalContainer.current.media
    Eyebrow(stringResource(R.string.recap_discoveries_eyebrow))
    if (count == 0) {
        Text(stringResource(R.string.recap_discoveries_none), style = Big)
        Muted(stringResource(R.string.recap_discoveries_none_note))
        return
    }
    Text("$count", style = Huge)
    Muted(pluralStringResource(R.plurals.recap_new_artists, count), big = true)
    FlowRow(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
        recap.newArtistPicks.forEach { artist ->
            Column(Modifier.width(88.dp), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Cover(media.artistImage(artist.id, artist.hasImage, small = true), artist.name, Modifier.size(80.dp), round = true)
                Text(artist.name, style = Type.small, textAlign = TextAlign.Center, maxLines = 2, overflow = TextOverflow.Ellipsis)
            }
        }
    }
}

@Composable
private fun SummarySlide(recap: Recap, onClose: () -> Unit, onRestart: () -> Unit) {
    val palette = LocalPalette.current
    val facts = listOfNotNull(
        recap.topArtists.firstOrNull()?.let { stringResource(R.string.recap_summary_artist) to it.artist.name },
        recap.topTracks.firstOrNull()?.let { stringResource(R.string.recap_summary_track) to it.track.title },
        recap.moods.firstOrNull()?.let { stringResource(R.string.recap_summary_mood) to moodLabel(it.key) },
        recap.topGenres.firstOrNull()?.let { stringResource(R.string.recap_summary_genre) to it.name },
    )

    Eyebrow(stringResource(R.string.recap_summary_eyebrow, monthName(recap.year, recap.month).replaceFirstChar { it.titlecase(currentLocale()) }))
    Column {
        Text(stringResource(R.string.recap_summary_time), style = Type.small, color = palette.muted)
        Text(listeningTime(recap.listenedSeconds), style = Big)
    }
    facts.chunked(2).forEach { pair ->
        Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
            pair.forEach { (label, value) ->
                Column(Modifier.weight(1f)) {
                    Text(label, style = Type.small, color = palette.muted)
                    Text(value, style = Type.title, maxLines = 2, overflow = TextOverflow.Ellipsis)
                }
            }
            if (pair.size == 1) Box(Modifier.weight(1f))
        }
    }
    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        Chip(stringResource(R.string.recap_close), active = true, onClick = onClose)
        Chip(stringResource(R.string.recap_again), active = false, onClick = onRestart)
    }
}

@Composable
private fun currentLocale(): Locale = LocalResources.current.configuration.locales[0]

// «сентябрь» — именительный падеж без числа, как в «Ваш сентябрь».
@Composable
private fun monthName(year: Int, month: Int): String =
    LocalDate.of(year, month, 1).month.getDisplayName(TextStyle.FULL_STANDALONE, currentLocale()).lowercase(currentLocale())

@Composable
private fun listeningTime(seconds: Long): String {
    val hours = (seconds / 3600).toInt()
    val minutes = ((seconds % 3600) / 60).toInt()
    return when {
        hours == 0 -> stringResource(R.string.unit_minutes, max(minutes, if (seconds > 0) 1 else 0))
        minutes == 0 -> stringResource(R.string.unit_hours, hours)
        else -> stringResource(R.string.unit_hours_minutes, hours, minutes)
    }
}

// Корень растягивает тихие дни: при линейной шкале один марафон гасит весь остальной месяц.
private fun intensity(seconds: Long, max: Long): Float = if (max > 0 && seconds > 0) sqrt(seconds.toFloat() / max) else 0f
