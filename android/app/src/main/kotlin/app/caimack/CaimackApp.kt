// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack

import android.app.Application
import coil3.ImageLoader
import coil3.PlatformContext
import coil3.SingletonImageLoader
import coil3.network.okhttp.OkHttpNetworkFetcherFactory
import coil3.request.crossfade
import kotlinx.coroutines.launch

class CaimackApp : Application(), SingletonImageLoader.Factory {
    val container by lazy { AppContainer(this) }

    override fun onCreate() {
        super.onCreate()
        container.scope.launch { container.session.restore() }
    }

    override fun newImageLoader(context: PlatformContext): ImageLoader = ImageLoader.Builder(context)
        .components { add(OkHttpNetworkFetcherFactory(callFactory = { container.http })) }
        .crossfade(true)
        .build()
}
