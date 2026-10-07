// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.ui

import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxHeight
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
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalResources
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.navigation.NavController
import app.caimack.R
import app.caimack.api.Recap
import app.caimack.api.RecapMonth
import app.caimack.api.RadioRequest
import app.caimack.api.Track
import java.time.DayOfWeek
import java.time.LocalDate
import java.time.YearMonth
import java.time.format.TextStyle
import java.util.Locale
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.sqrt
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

@Composable
fun RecapScreen(year: Int?, month: Int?, nav: NavController, play: (List<Track>, Int) -> Unit) {
    val api = LocalContainer.current.api

    Load(
        key = "recap",
        load = { api.recapMonths() },
        isEmpty = { it.isEmpty() },
        empty = { Empty(stringResource(R.string.recap_empty_title), stringResource(R.string.recap_empty_description)) },
    ) { months ->
        var chosen by rememberSaveable { mutableStateOf(months.indexOfFirst { it.year == year && it.month == month }.coerceAtLeast(0)) }
        val selected = months[chosen.coerceIn(months.indices)]

        Load(key = "recap-${selected.year}-${selected.month}", load = { api.recap(selected.year, selected.month) }) { recap ->
            LazyColumn(
                Modifier.fillMaxSize(),
                contentPadding = PaddingValues(bottom = 24.dp),
                verticalArrangement = Arrangement.spacedBy(32.dp),
            ) {
                item("header") {
                    Column {
                        Headline(recap)
                        if (months.size > 1) MonthChips(months, chosen) { chosen = it }
                    }
                }
                if (recap.topArtists.isNotEmpty()) item("artists") { TopArtists(recap, nav) }
                if (recap.topTracks.isNotEmpty()) item("tracks") { TopTracks(recap, play) }
                item("calendar") { Calendar(recap) }
                item("hours") { Hours(recap) }
                recap.moods.firstOrNull()?.let { lead -> item("mood") { Mood(recap, lead.key) } }
                recap.soundOfMonth?.let { track ->
                    item("sound") {
                        Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                            SectionHeader(stringResource(R.string.recap_sound), stringResource(R.string.recap_sound_note))
                            TrackRow(track, { play(listOf(track), 0) })
                        }
                    }
                }
                recap.newArtists?.let { item("discoveries") { Discoveries(recap, it, nav) } }
                if (recap.topGenres.isNotEmpty()) item("genres") { Genres(recap) }
            }
        }
    }
}

@Composable
private fun MonthChips(months: List<RecapMonth>, chosen: Int, choose: (Int) -> Unit) {
    LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        items(months.size) { index ->
            Chip(monthLabel(months[index].year, months[index].month), active = index == chosen) { choose(index) }
        }
    }
}

@Composable
private fun Headline(recap: Recap) {
    val palette = LocalPalette.current
    val media = LocalContainer.current.media
    val lead = recap.topArtists.firstOrNull()?.artist
    val backdrop = lead?.let { media.artistImage(it.id, it.hasImage, small = false) }
        ?: recap.topTracks.firstOrNull()?.track?.let { trackCover(it, small = false) }
    val delta = recap.previousListenedSeconds?.let { recap.listenedSeconds - it }

    Box(Modifier.fillMaxWidth()) {
        ArtBackdrop(backdrop, BackdropMode.Header, Modifier.matchParentSize())
        Column(Modifier.fillMaxWidth().padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Text(monthLabel(recap.year, recap.month), style = Type.display)
            Row(verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Text(listeningTime(recap.listenedSeconds), style = Type.display.copy(fontSize = 44.sp, lineHeight = 48.sp))
                Text(stringResource(R.string.recap_of_music), style = Type.body, color = palette.muted, modifier = Modifier.padding(bottom = 6.dp))
            }
            FlowRow(horizontalArrangement = Arrangement.spacedBy(20.dp)) {
                Text(pluralStringResource(R.plurals.recap_plays, recap.plays, recap.plays), style = Type.small, color = palette.muted)
                Text(pluralStringResource(R.plurals.count_tracks, recap.distinctTracks, recap.distinctTracks), style = Type.small, color = palette.muted)
                Text(pluralStringResource(R.plurals.count_artists, recap.distinctArtists, recap.distinctArtists), style = Type.small, color = palette.muted)
            }
            if (delta != null && abs(delta) >= 60) {
                Text(
                    stringResource(if (delta > 0) R.string.recap_more else R.string.recap_less, listeningTime(abs(delta))),
                    style = Type.small,
                )
            }
            if (!recap.complete) Text(stringResource(R.string.recap_in_progress), style = Type.small, color = palette.faint)
        }
    }
}

@Composable
private fun TopArtists(recap: Recap, nav: NavController) {
    val palette = LocalPalette.current
    val media = LocalContainer.current.media

    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        SectionHeader(stringResource(R.string.recap_top_artists))
        recap.topArtists.forEachIndexed { index, item ->
            Row(
                Modifier.fillMaxWidth().clickable { nav.navigate(ArtistRoute(item.artist.id)) }.padding(horizontal = 16.dp, vertical = 6.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Text("${index + 1}", style = Type.small, color = palette.faint, textAlign = TextAlign.End, modifier = Modifier.width(22.dp))
                Cover(media.artistImage(item.artist.id, item.artist.hasImage, small = true), item.artist.name, Modifier.size(44.dp), round = true)
                Text(
                    item.artist.name,
                    style = Type.small.copy(fontWeight = FontWeight.Medium),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.weight(1f),
                )
                Text(listeningTime(item.listenedSeconds), style = Type.small, color = palette.muted)
            }
        }
    }
}

@Composable
private fun TopTracks(recap: Recap, play: (List<Track>, Int) -> Unit) {
    val palette = LocalPalette.current
    val tracks = recap.topTracks.map { it.track }

    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        SectionHeader(stringResource(R.string.recap_top_tracks))
        recap.topTracks.forEachIndexed { index, item ->
            Column {
                TrackRow(item.track, { play(tracks, index) }, index = index + 1)
                Text(
                    pluralStringResource(R.plurals.recap_times, item.plays, item.plays),
                    style = Type.tiny,
                    color = palette.faint,
                    modifier = Modifier.padding(start = 106.dp),
                )
            }
        }
    }
}

// Календарь месяца: клетка дня тем ярче, чем дольше в этот день звучала музыка.
@Composable
private fun Calendar(recap: Recap) {
    val palette = LocalPalette.current
    val locale = currentLocale()
    val first = LocalDate.of(recap.year, recap.month, 1)
    val blanks = first.dayOfWeek.value - 1
    val max = recap.daySeconds.maxOrNull() ?: 0
    val peak = recap.daySeconds.indices.filter { recap.daySeconds[it] > 0 }.maxByOrNull { recap.daySeconds[it] }
    val cells = List(blanks) { null } + recap.daySeconds.indices.map { it }

    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        SectionHeader(stringResource(R.string.recap_by_day))
        Column(Modifier.padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                DayOfWeek.entries.forEach {
                    Text(
                        it.getDisplayName(TextStyle.SHORT, locale),
                        style = Type.tiny,
                        color = palette.faint,
                        textAlign = TextAlign.Center,
                        modifier = Modifier.weight(1f),
                    )
                }
            }
            cells.chunked(7).forEach { week ->
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    week.forEach { day ->
                        val level = if (day == null) 0f else intensity(recap.daySeconds[day], max)
                        Box(
                            Modifier
                                .weight(1f)
                                .aspectRatio(1f)
                                .clip(Radius.tile)
                                .then(if (day == null) Modifier else Modifier.background(lerp(palette.card, palette.primary, if (level > 0) 0.15f + level * 0.85f else 0f)))
                                .then(if (day != null && day == peak) Modifier.border(2.dp, palette.foreground, Radius.tile) else Modifier),
                            contentAlignment = Alignment.Center,
                        ) {
                            if (day != null) {
                                Text("${day + 1}", style = Type.tiny, color = if (level > 0.6f) palette.onPrimary else palette.muted)
                            }
                        }
                    }
                    repeat(7 - week.size) { Box(Modifier.weight(1f)) }
                }
            }
            peak?.let {
                val date = LocalDate.of(recap.year, recap.month, it + 1)
                Text(
                    stringResource(R.string.recap_busiest_day, "${date.dayOfMonth} ${date.month.getDisplayName(TextStyle.FULL, locale)}", listeningTime(recap.daySeconds[it])),
                    style = Type.small,
                    color = palette.muted,
                )
            }
        }
    }
}

// Сутки полосой из 24 столбиков: видно, когда музыка звучит, а когда тишина.
@Composable
private fun Hours(recap: Recap) {
    val palette = LocalPalette.current
    val max = recap.hourSeconds.maxOrNull() ?: 0
    val note = dominantDaypart(recap.hourSeconds)?.let { stringResource(it) }

    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        SectionHeader(stringResource(R.string.recap_by_hour), note)
        Column(Modifier.padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Row(Modifier.fillMaxWidth().height(120.dp), verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(3.dp)) {
                recap.hourSeconds.forEach { seconds ->
                    val share = if (max > 0) seconds.toFloat() / max else 0f
                    Box(
                        Modifier
                            .weight(1f)
                            .fillMaxHeight(max(0.04f, share))
                            .clip(Radius.cover)
                            .background(if (seconds > 0) palette.primary.copy(alpha = if (seconds == max) 1f else 0.7f) else palette.card),
                    )
                }
            }
            Row(Modifier.fillMaxWidth()) {
                listOf("00", "06", "12", "18").forEach { Text(it, style = Type.tiny, color = palette.faint, modifier = Modifier.weight(1f)) }
            }
        }
    }
}

@Composable
private fun Mood(recap: Recap, lead: String) {
    val palette = LocalPalette.current
    val container = LocalContainer.current
    val context = LocalContext.current
    var starting by remember { mutableStateOf(false) }

    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        SectionHeader(stringResource(R.string.recap_mood))
        Column(Modifier.padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Icon(moodIcon(lead), null, tint = palette.primary, modifier = Modifier.size(24.dp))
                Text(stringResource(R.string.recap_mood_lead, moodLabel(lead)), style = Type.section)
            }
            Row(Modifier.fillMaxWidth().height(10.dp).clip(CircleShape).background(palette.card)) {
                recap.moods.forEachIndexed { index, mood ->
                    Box(
                        Modifier
                            .weight(mood.share.toFloat().coerceAtLeast(0.001f))
                            .fillMaxHeight()
                            .background(palette.primary.copy(alpha = max(0.2f, 1f - index * 0.18f))),
                    )
                }
            }
            FlowRow(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                recap.moods.forEach { Text("${moodLabel(it.key)} ${(it.share * 100).toInt()}%", style = Type.small, color = palette.muted) }
            }
            Chip(stringResource(R.string.recap_mood_radio, moodLabel(lead)), active = false, icon = moodIcon(lead), enabled = !starting) {
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
    }
}

@Composable
private fun Discoveries(recap: Recap, count: Int, nav: NavController) {
    val media = LocalContainer.current.media
    val note = if (count > 0) pluralStringResource(R.plurals.recap_new_artists, count, count) else stringResource(R.string.recap_no_discoveries)

    Column(verticalArrangement = Arrangement.spacedBy(16.dp)) {
        SectionHeader(stringResource(R.string.recap_discoveries), note)
        if (recap.newArtistPicks.isNotEmpty()) {
            LazyRow(contentPadding = PaddingValues(horizontal = 16.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                items(recap.newArtistPicks, key = { it.id }) { artist ->
                    Card(artist.name, pluralStringResource(R.plurals.count_tracks, artist.trackCount, artist.trackCount), { nav.navigate(ArtistRoute(artist.id)) }, Modifier.width(116.dp), round = true) {
                        Cover(media.artistImage(artist.id, artist.hasImage, small = true), artist.name, it, round = true)
                    }
                }
            }
        }
    }
}

@Composable
private fun Genres(recap: Recap) {
    val palette = LocalPalette.current

    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        SectionHeader(stringResource(R.string.recap_genres))
        recap.topGenres.forEach { genre ->
            Row(
                Modifier.fillMaxWidth().padding(horizontal = 16.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Text(genre.name, style = Type.small.copy(fontWeight = FontWeight.Medium), maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.width(110.dp))
                Box(Modifier.weight(1f).height(8.dp).clip(CircleShape).background(palette.card)) {
                    Box(Modifier.fillMaxWidth(genre.share.toFloat()).fillMaxHeight().clip(CircleShape).background(palette.primary))
                }
                Text("${(genre.share * 100).toInt()}%", style = Type.small, color = palette.muted, textAlign = TextAlign.End, modifier = Modifier.width(40.dp))
            }
        }
    }
}

// Первую неделю месяца главная напоминает об итогах прошлого; дальше они живут в библиотеке.
@Composable
fun RecapBanner(nav: NavController) {
    val palette = LocalPalette.current
    val container = LocalContainer.current
    val today = LocalDate.now()
    if (today.dayOfMonth > 7) return

    val previous = YearMonth.from(today).minusMonths(1)
    var month by remember { mutableStateOf<RecapMonth?>(null) }
    LaunchedEffect(previous) {
        month = runCatching { container.api.recapMonths() }.getOrNull()
            ?.firstOrNull { it.year == previous.year && it.month == previous.monthValue }
    }
    val recap = month ?: return

    Row(
        Modifier
            .padding(horizontal = 16.dp)
            .fillMaxWidth()
            .clip(Radius.panel)
            .background(palette.card)
            .clickable { nav.navigate(RecapRoute(recap.year, recap.month)) }
            .padding(16.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        Box(Modifier.size(44.dp).clip(CircleShape).background(palette.primarySoft), contentAlignment = Alignment.Center) {
            Icon(Lucide.CalendarRange, null, tint = palette.primary, modifier = Modifier.size(20.dp))
        }
        Column(Modifier.weight(1f)) {
            Text(
                stringResource(R.string.recap_banner, monthLabel(recap.year, recap.month, capitalized = false)),
                style = Type.section,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(stringResource(R.string.recap_banner_note, listeningTime(recap.listenedSeconds)), style = Type.small, color = palette.muted, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        Icon(Lucide.ChevronRight, null, tint = palette.muted, modifier = Modifier.size(18.dp))
    }
}

@Composable
private fun currentLocale(): Locale = LocalResources.current.configuration.locales[0]

// «Сентябрь», а для прошлых лет — «Сентябрь 2025».
@Composable
private fun monthLabel(year: Int, month: Int, capitalized: Boolean = true): String {
    val locale = currentLocale()
    val name = java.time.Month.of(month).getDisplayName(TextStyle.FULL_STANDALONE, locale)
    val label = if (year == LocalDate.now().year) name else "$name $year"
    return if (capitalized) label.replaceFirstChar { it.titlecase(locale) } else label
}

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

// Те же границы, что на сервере и в вебе: утро 5–11, день 11–17, вечер 17–23, ночь 23–5.
private fun dominantDaypart(hours: List<Long>): Int? {
    val totals = mapOf(
        R.string.recap_daypart_morning to (5 until 11),
        R.string.recap_daypart_day to (11 until 17),
        R.string.recap_daypart_evening to (17 until 23),
        R.string.recap_daypart_night to (listOf(23) + (0 until 5)),
    ).mapValues { (_, range) -> range.sumOf { hours.getOrElse(it) { 0L } } }
    return totals.maxByOrNull { it.value }?.takeIf { it.value > 0 }?.key
}
