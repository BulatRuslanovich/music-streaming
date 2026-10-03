// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack

import android.app.SearchManager
import android.content.Context
import android.content.Intent
import android.graphics.Color
import android.os.Bundle
import android.provider.MediaStore
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Surface
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import app.caimack.session.SessionState
import app.caimack.ui.App
import app.caimack.ui.Appearance
import app.caimack.ui.CaimackTheme
import app.caimack.ui.LocalContainer
import app.caimack.ui.LoginScreen

class MainActivity : ComponentActivity() {
    override fun attachBaseContext(newBase: Context) = super.attachBaseContext(Appearance.localized(newBase))

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()

        val container = (application as CaimackApp).container
        if (savedInstanceState == null) playFromSearch(intent)

        setContent {
            val state by container.session.state.collectAsStateWithLifecycle()
            val address by container.server.url.collectAsStateWithLifecycle()
            val theme by container.appearance.theme.collectAsStateWithLifecycle()
            val dark = theme == Appearance.DARK || (theme == Appearance.SYSTEM && isSystemInDarkTheme())

            DisposableEffect(dark) {
                val bars = if (dark) SystemBarStyle.dark(Color.TRANSPARENT) else SystemBarStyle.light(Color.TRANSPARENT, Color.TRANSPARENT)
                enableEdgeToEdge(bars, bars)
                onDispose {}
            }

            CaimackTheme(dark = dark) {
                CompositionLocalProvider(LocalContainer provides container) {
                    Surface(Modifier.fillMaxSize()) {
                        when (val current = state) {
                            SessionState.Restoring -> Unit
                            is SessionState.SignedOut -> LoginScreen(
                                session = container.session,
                                lastAddress = address?.toString()?.trimEnd('/').orEmpty(),
                                expired = current.expired,
                            )
                            is SessionState.SignedIn -> App(current.user)
                        }
                    }
                }
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        playFromSearch(intent)
    }

    private fun playFromSearch(intent: Intent) {
        if (intent.action != MediaStore.INTENT_ACTION_MEDIA_PLAY_FROM_SEARCH) return
        (application as CaimackApp).container.player.playFromSearch(intent.getStringExtra(SearchManager.QUERY).orEmpty())
    }
}
