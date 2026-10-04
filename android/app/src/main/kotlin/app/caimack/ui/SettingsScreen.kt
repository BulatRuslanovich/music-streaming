// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.ui

import androidx.compose.foundation.shape.CircleShape
import kotlin.math.abs
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.semantics.setProgress
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.progressBarRangeInfo
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.ProgressBarRangeInfo
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.geometry.Offset
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.gestures.drag
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.Canvas
import kotlin.math.roundToInt
import app.caimack.playback.EqualizerState
import androidx.compose.ui.text.style.TextAlign
import androidx.activity.compose.LocalActivity
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import app.caimack.R
import app.caimack.api.SettingsChanges
import kotlinx.coroutines.launch

private data class Option(val value: String, val label: String, val hint: String? = null)

@Composable
fun SettingsScreen() {
    val palette = LocalPalette.current
    val container = LocalContainer.current
    val settings by container.settings.collectAsStateWithLifecycle()
    val theme by container.appearance.theme.collectAsStateWithLifecycle()
    val equalizer by container.equalizer.state.collectAsStateWithLifecycle()
    val activity = LocalActivity.current
    val scope = rememberCoroutineScope()

    val update = { changes: SettingsChanges ->
        container.settings.value = settings.copy(
            quality = changes.quality ?: settings.quality,
            dataSaver = changes.dataSaver ?: settings.dataSaver,
        )
        scope.launch { runCatching { container.api.updateSettings(changes) }.onSuccess { container.settings.value = it } }
        Unit
    }

    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(bottom = 24.dp)) {
        PageTitle(stringResource(R.string.settings_title))

        Panel(stringResource(R.string.settings_playback)) {
            Choice(
                stringResource(R.string.settings_quality),
                stringResource(R.string.settings_quality_hint),
                settings.quality,
                listOf(
                    Option("Low", stringResource(R.string.settings_quality_low), stringResource(R.string.settings_quality_bitrate, 64)),
                    Option("Normal", stringResource(R.string.settings_quality_normal), stringResource(R.string.settings_quality_bitrate, 128)),
                    Option("Original", stringResource(R.string.settings_quality_original), stringResource(R.string.settings_quality_as_uploaded)),
                ),
            ) { update(SettingsChanges(quality = it)) }

            Row(Modifier.fillMaxWidth().clickable { update(SettingsChanges(dataSaver = !settings.dataSaver)) }, verticalAlignment = Alignment.Top) {
                Column(Modifier.weight(1f).padding(end = 12.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    Text(stringResource(R.string.settings_data_saver), style = Type.body.copy(fontWeight = FontWeight.Medium))
                    Text(stringResource(R.string.settings_data_saver_hint), style = Type.small, color = palette.muted)
                }
                Switch(
                    checked = settings.dataSaver,
                    onCheckedChange = { update(SettingsChanges(dataSaver = it)) },
                    colors = SwitchDefaults.colors(
                        checkedTrackColor = palette.primary,
                        checkedThumbColor = palette.onPrimary,
                        uncheckedTrackColor = palette.raised,
                        uncheckedBorderColor = palette.controlBorder,
                        uncheckedThumbColor = palette.muted,
                    ),
                )
            }
        }

        Panel(stringResource(R.string.settings_equalizer)) {
            Row(Modifier.fillMaxWidth().clickable { container.equalizer.setEnabled(!equalizer.enabled) }, verticalAlignment = Alignment.Top) {
                Text(
                    stringResource(R.string.settings_equalizer_hint),
                    style = Type.small,
                    color = palette.muted,
                    modifier = Modifier.weight(1f).padding(end = 12.dp),
                )
                Switch(
                    checked = equalizer.enabled,
                    onCheckedChange = { container.equalizer.setEnabled(it) },
                    colors = SwitchDefaults.colors(
                        checkedTrackColor = palette.primary,
                        checkedThumbColor = palette.onPrimary,
                        uncheckedTrackColor = palette.raised,
                        uncheckedBorderColor = palette.controlBorder,
                        uncheckedThumbColor = palette.muted,
                    ),
                )
            }

            if (equalizer.enabled) {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    listOf(
                        "flat" to R.string.settings_equalizer_flat,
                        "bass" to R.string.settings_equalizer_bass,
                        "vocal" to R.string.settings_equalizer_vocal,
                        "treble" to R.string.settings_equalizer_treble,
                    ).forEach { (preset, label) ->
                        val active = equalizer.preset == preset
                        Text(
                            stringResource(label),
                            style = Type.small.copy(fontWeight = FontWeight.Medium),
                            color = if (active) palette.primary else palette.muted,
                            textAlign = TextAlign.Center,
                            maxLines = 1,
                            modifier = Modifier
                                .weight(1f)
                                .clip(CircleShape)
                                .background(if (active) palette.primarySoft else palette.raised)
                                .then(if (active) Modifier.border(1.dp, palette.primary, CircleShape) else Modifier)
                                .clickable { container.equalizer.applyPreset(preset) }
                                .padding(vertical = 8.dp),
                        )
                    }
                }

                val locale = LocalConfiguration.current.locales[0]
                val frequencies = EqualizerState.BANDS_HZ.map { hz ->
                    if (hz < 1000) stringResource(R.string.settings_equalizer_hz, hz)
                    else stringResource(R.string.settings_equalizer_khz, String.format(locale, "%.1f", hz / 1000.0).trimEnd('0').trimEnd('.', ','))
                }
                val decibels = equalizer.gains.map { stringResource(R.string.settings_equalizer_db, if (it > 0) "+$it" else "$it") }
                EqualizerCurve(equalizer.gains, frequencies, decibels) { band, gain -> container.equalizer.setGain(band, gain) }
            }
        }

        Panel(stringResource(R.string.settings_appearance)) {
            Choice(
                stringResource(R.string.settings_theme),
                stringResource(R.string.settings_theme_hint),
                theme,
                listOf(
                    Option(Appearance.SYSTEM, stringResource(R.string.settings_theme_system)),
                    Option(Appearance.DARK, stringResource(R.string.settings_theme_dark)),
                    Option(Appearance.LIGHT, stringResource(R.string.settings_theme_light)),
                ),
            ) { container.appearance.setTheme(it) }

            val current = container.appearance.language.ifEmpty { LocalConfiguration.current.locales[0].language }
            Choice(
                stringResource(R.string.settings_language),
                stringResource(R.string.settings_language_hint),
                if (current == "ru") "ru" else "en",
                listOf(Option("en", stringResource(R.string.language_english)), Option("ru", stringResource(R.string.language_russian))),
            ) {
                container.appearance.setLanguage(it)
                activity?.recreate()
            }
        }

        TextButton(onClick = { scope.launch { Remote.cache.clear(); container.player.stop(); container.session.logout() } }, modifier = Modifier.padding(horizontal = 8.dp)) {
            Text(stringResource(R.string.sign_out), color = palette.muted)
        }
    }
}

@Composable
private fun Panel(title: String, content: @Composable () -> Unit) {
    val palette = LocalPalette.current
    Column(
        Modifier.padding(horizontal = 16.dp, vertical = 8.dp).fillMaxWidth().clip(Radius.panel).background(palette.card).padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        Text(title, style = Type.section)
        content()
    }
}

@Composable
private fun Choice(legend: String, hint: String, value: String, options: List<Option>, onChange: (String) -> Unit) {
    val palette = LocalPalette.current
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(legend, style = Type.body.copy(fontWeight = FontWeight.SemiBold))
        Text(hint, style = Type.small, color = palette.muted)
        options.forEach { option ->
            val checked = option.value == value
            Column(
                Modifier
                    .fillMaxWidth()
                    .clip(Radius.row)
                    .background(if (checked) palette.primarySoft else palette.card)
                    .border(1.dp, if (checked) palette.primary else palette.border, Radius.row)
                    .clickable { onChange(option.value) }
                    .padding(12.dp),
                verticalArrangement = Arrangement.spacedBy(2.dp),
            ) {
                Text(option.label, style = Type.body.copy(fontWeight = FontWeight.Medium))
                option.hint?.let { Text(it, style = Type.tiny, color = palette.muted) }
            }
        }
    }
}

@Composable
private fun EqualizerCurve(gains: List<Int>, frequencies: List<String>, decibels: List<String>, onChange: (Int, Int) -> Unit) {
    val palette = LocalPalette.current
    val limit = EqualizerState.LIMIT_DB
    val update by rememberUpdatedState(onChange)
    val current by rememberUpdatedState(gains)

    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Row(Modifier.fillMaxWidth()) {
            decibels.forEach { Text(it, style = Type.micro, color = palette.muted, textAlign = TextAlign.Center, modifier = Modifier.weight(1f)) }
        }

        Box(Modifier.fillMaxWidth().height(200.dp)) {
            Canvas(Modifier.matchParentSize()) {
                val n = gains.size
                fun x(band: Int) = (band + 0.5f) / n * size.width
                fun y(gain: Int) = (limit - gain) / (2f * limit) * size.height

                listOf(limit, limit / 2, 0, -limit / 2, -limit).forEach { tick ->
                    drawLine(
                        palette.border,
                        Offset(0f, y(tick)),
                        Offset(size.width, y(tick)),
                        strokeWidth = 1.dp.toPx(),
                        pathEffect = if (tick == 0) null else PathEffect.dashPathEffect(floatArrayOf(4.dp.toPx(), 4.dp.toPx())),
                    )
                }
                for (band in 0 until n) drawLine(palette.border, Offset(x(band), 0f), Offset(x(band), size.height), strokeWidth = 1.dp.toPx())

                val points = listOf(Offset(0f, y(gains.first()))) + gains.mapIndexed { band, gain -> Offset(x(band), y(gain)) } +
                    Offset(size.width, y(gains.last()))
                val line = Path().apply {
                    moveTo(points[0].x, points[0].y)
                    for (i in 1 until points.size) {
                        val before = points[(i - 2).coerceAtLeast(0)]
                        val from = points[i - 1]
                        val to = points[i]
                        val after = points[(i + 1).coerceAtMost(points.size - 1)]
                        cubicTo(
                            from.x + (to.x - before.x) / 6, from.y + (to.y - before.y) / 6,
                            to.x - (after.x - from.x) / 6, to.y - (after.y - from.y) / 6,
                            to.x, to.y,
                        )
                    }
                }
                val area = Path().apply {
                    addPath(line)
                    lineTo(size.width, size.height)
                    lineTo(0f, size.height)
                    close()
                }
                drawPath(area, Brush.verticalGradient(listOf(palette.primary.copy(alpha = 0.35f), palette.primary.copy(alpha = 0.02f))))
                drawPath(line, palette.primary, style = Stroke(width = 2.dp.toPx(), cap = StrokeCap.Round))

                gains.forEachIndexed { band, gain ->
                    drawCircle(palette.card, radius = 9.dp.toPx(), center = Offset(x(band), y(gain)))
                    drawCircle(palette.primary, radius = 7.dp.toPx(), center = Offset(x(band), y(gain)))
                }
            }

            Row(Modifier.matchParentSize()) {
                gains.forEachIndexed { band, gain ->
                    Box(
                        Modifier
                            .weight(1f)
                            .fillMaxHeight()
                            .semantics {
                                contentDescription = frequencies[band]
                                stateDescription = decibels[band]
                                progressBarRangeInfo = ProgressBarRangeInfo(gain.toFloat(), -limit.toFloat()..limit.toFloat(), limit * 2 - 1)
                                setProgress { value ->
                                    update(band, value.roundToInt().coerceIn(-limit, limit))
                                    true
                                }
                            }
                            .pointerInput(band) {
                                fun gainAt(y: Float) = (limit - y / size.height * 2 * limit).roundToInt().coerceIn(-limit, limit)
                                awaitEachGesture {
                                    val down = awaitFirstDown()
                                    val knob = (limit - current[band]) / (2f * limit) * size.height
                                    if (abs(down.position.y - knob) > 28.dp.toPx()) return@awaitEachGesture
                                    down.consume()
                                    update(band, gainAt(down.position.y))
                                    drag(down.id) { change ->
                                        change.consume()
                                        update(band, gainAt(change.position.y))
                                    }
                                }
                            },
                    )
                }
            }
        }

        Row(Modifier.fillMaxWidth()) {
            frequencies.forEach { Text(it, style = Type.micro, color = palette.muted, textAlign = TextAlign.Center, modifier = Modifier.weight(1f)) }
        }
    }
}
