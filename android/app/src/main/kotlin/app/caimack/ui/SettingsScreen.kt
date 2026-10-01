// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.ui

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
