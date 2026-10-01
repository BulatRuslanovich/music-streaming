// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.ui

import android.content.Context
import android.content.res.Configuration
import androidx.core.content.edit
import java.util.Locale
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow

class Appearance(context: Context) {
    private val preferences = context.getSharedPreferences(FILE, Context.MODE_PRIVATE)
    private val chosenTheme = MutableStateFlow(preferences.getString(THEME, SYSTEM) ?: SYSTEM)

    val theme: StateFlow<String> = chosenTheme

    val language: String get() = preferences.getString(LANGUAGE, "").orEmpty()

    fun setTheme(value: String) {
        preferences.edit { putString(THEME, value) }
        chosenTheme.value = value
    }

    fun setLanguage(value: String) = preferences.edit(commit = true) { putString(LANGUAGE, value) }

    companion object {
        const val SYSTEM = "system"
        const val DARK = "dark"
        const val LIGHT = "light"

        private const val FILE = "appearance"
        private const val THEME = "theme"
        private const val LANGUAGE = "language"

        fun localized(base: Context): Context {
            val language = base.getSharedPreferences(FILE, Context.MODE_PRIVATE).getString(LANGUAGE, "").orEmpty()
            if (language.isEmpty()) return base
            val configuration = Configuration(base.resources.configuration).apply { setLocale(Locale.forLanguageTag(language)) }
            return base.createConfigurationContext(configuration)
        }
    }
}
