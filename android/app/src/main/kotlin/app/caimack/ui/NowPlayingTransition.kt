// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.ui

import androidx.compose.animation.AnimatedVisibilityScope
import androidx.compose.animation.ExperimentalSharedTransitionApi
import androidx.compose.animation.SharedTransitionScope
import androidx.compose.runtime.Composable
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Modifier

private const val NOW_PLAYING_COVER = "now-playing-cover"

// Обложка мини-плеера вырастает в конверт пластинки полноэкранного плеера и сворачивается
// обратно — как view transition now-playing на вебе. Без общей сцены (превью, тесты) — просто модификатор.
@OptIn(ExperimentalSharedTransitionApi::class)
@Immutable
class NowPlayingTransition(val scope: SharedTransitionScope, val full: Boolean)

val LocalNowPlayingTransition = staticCompositionLocalOf<NowPlayingTransition?> { null }

// Видимость полноэкранного плеера — вторая сторона перехода.
val LocalFullPlayerVisibility = staticCompositionLocalOf<AnimatedVisibilityScope?> { null }

@OptIn(ExperimentalSharedTransitionApi::class)
@Composable
fun Modifier.nowPlayingCover(visibility: AnimatedVisibilityScope?): Modifier {
    val transition = LocalNowPlayingTransition.current ?: return this
    if (visibility == null) return this
    return with(transition.scope) {
        this@nowPlayingCover.sharedElement(rememberSharedContentState(NOW_PLAYING_COVER), visibility)
    }
}
