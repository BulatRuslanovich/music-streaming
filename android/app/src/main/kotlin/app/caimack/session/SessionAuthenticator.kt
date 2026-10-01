// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.session

import java.io.IOException
import okhttp3.Authenticator
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.Response
import okhttp3.Route

class SessionAuthenticator(
    private val server: Server,
    private val client: () -> OkHttpClient,
    private val onExpired: () -> Unit,
) : Authenticator {
    private var refreshedAt = 0L

    override fun authenticate(route: Route?, response: Response): Request? {
        if (response.request.url.encodedPath.endsWith(LOGIN) || response.request.url.encodedPath.endsWith(REFRESH)) {
            return null
        }
        if (response.priorResponse?.code == UNAUTHORIZED) return null

        synchronized(this) {
            if (refreshedAt >= response.sentRequestAtMillis) return response.request

            val url = server.resolve(REFRESH.trimStart('/')) ?: return null
            val refresh = Request.Builder().url(url).post(ByteArray(0).toRequestBody()).build()

            val status = try {
                client().newBuilder().authenticator(Authenticator.NONE).build().newCall(refresh).execute().use { it.code }
            } catch (_: IOException) {
                return null
            }

            if (status == UNAUTHORIZED) {
                onExpired()
                return null
            }
            if (status !in 200..299) return null

            refreshedAt = System.currentTimeMillis()
            return response.request
        }
    }

    private companion object {
        const val UNAUTHORIZED = 401
        const val LOGIN = "/api/auth/login"
        const val REFRESH = "/api/auth/refresh"
    }
}
