// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.playback

import android.content.ContentProvider
import android.content.ContentValues
import android.database.Cursor
import android.net.Uri
import android.os.ParcelFileDescriptor
import app.caimack.CaimackApp
import app.caimack.session.SessionState
import java.io.File
import java.io.FileNotFoundException
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeoutOrNull
import okhttp3.HttpUrl.Companion.toHttpUrlOrNull
import okhttp3.Request

class ArtworkProvider : ContentProvider() {
    override fun onCreate() = true

    override fun openFile(uri: Uri, mode: String): ParcelFileDescriptor {
        val context = context ?: throw FileNotFoundException(uri.toString())
        val path = uri.path?.let { ARTWORK.find(it)?.value } ?: throw FileNotFoundException(uri.toString())
        // Провайдер экспортирован: size приходит от любого приложения и попадает в имя файла.
        val size = uri.getQueryParameter(SIZE)?.takeIf { it in SIZES }
        val folder = File(context.cacheDir, "artwork")
        val file = File(folder, "${path.replace('/', '-')}.${size ?: "full"}")
        if (file.canonicalFile.parentFile != folder.canonicalFile) throw FileNotFoundException(uri.toString())

        if (!file.exists()) {
            val container = (context.applicationContext as CaimackApp).container
            // Android Auto будит приложение с холодного старта и сразу просит обложки, а адрес сервера и cookie
            // восстанавливаются асинхронно. openFile вызывается на binder-потоке, поэтому подождать можно.
            runBlocking { withTimeoutOrNull(SESSION_WAIT_MS) { container.session.state.first { it !is SessionState.Restoring } } }
            val url = container.server.resolve(path)?.newBuilder()?.apply { size?.let { addQueryParameter(SIZE, it) } }?.build()
                ?: throw FileNotFoundException(path)
            container.http.newCall(Request.Builder().url(url).build()).execute().use { response ->
                if (!response.isSuccessful) throw FileNotFoundException(path)
                folder.mkdirs()
                val partial = File(file.path + ".part")
                partial.outputStream().use { response.body.byteStream().copyTo(it) }
                partial.renameTo(file)
            }
        }

        return ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY)
    }

    override fun getType(uri: Uri): String = "image/*"

    override fun query(uri: Uri, projection: Array<out String>?, selection: String?, selectionArgs: Array<out String>?, sortOrder: String?): Cursor? = null

    override fun insert(uri: Uri, values: ContentValues?): Uri? = null

    override fun update(uri: Uri, values: ContentValues?, selection: String?, selectionArgs: Array<out String>?): Int = 0

    override fun delete(uri: Uri, selection: String?, selectionArgs: Array<out String>?): Int = 0

    companion object {
        const val AUTHORITY = "app.caimack.artwork"

        private const val SIZE = "size"
        private val SIZES = setOf("thumb", "large")
        private const val SESSION_WAIT_MS = 10_000L

        // Android Auto и другие внешние контроллеры грузят обложку сами, без наших cookie, поэтому http-адрес
        // обложки им бесполезен: отдаём content://, который провайдер достаёт через сессионный OkHttp.
        fun uriFor(url: String?): Uri? = url?.toHttpUrlOrNull()?.let { http ->
            Uri.Builder()
                .scheme("content")
                .authority(AUTHORITY)
                .encodedPath(http.encodedPath)
                .apply { http.queryParameter(SIZE)?.let { appendQueryParameter(SIZE, it) } }
                .build()
        }

        private val ARTWORK = Regex("api/(albums|tracks|playlists|artists)/[0-9a-fA-F-]+/(cover|image)$")
    }
}
