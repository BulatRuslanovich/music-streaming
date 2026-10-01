// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.playback

import android.content.ContentProvider
import android.content.ContentValues
import android.database.Cursor
import android.net.Uri
import android.os.ParcelFileDescriptor
import app.caimack.CaimackApp
import java.io.File
import java.io.FileNotFoundException
import okhttp3.Request

class ArtworkProvider : ContentProvider() {
    override fun onCreate() = true

    override fun openFile(uri: Uri, mode: String): ParcelFileDescriptor {
        val context = context ?: throw FileNotFoundException(uri.toString())
        val path = uri.path?.let { ARTWORK.find(it)?.value } ?: throw FileNotFoundException(uri.toString())
        val file = File(context.cacheDir, "artwork/${path.replace('/', '-')}")

        if (!file.exists()) {
            val container = (context.applicationContext as CaimackApp).container
            val url = container.server.resolve(path)?.newBuilder()?.addQueryParameter("size", "thumb")?.build()
                ?: throw FileNotFoundException(path)
            container.http.newCall(Request.Builder().url(url).build()).execute().use { response ->
                if (!response.isSuccessful) throw FileNotFoundException(path)
                file.parentFile?.mkdirs()
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

        private val ARTWORK = Regex("api/(albums|tracks|playlists|artists)/[0-9a-fA-F-]+/(cover|image)$")
    }
}
