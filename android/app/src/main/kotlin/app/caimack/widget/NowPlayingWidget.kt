// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.widget

import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.BroadcastReceiver
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.res.ColorStateList
import android.content.res.Configuration
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.widget.RemoteViews
import androidx.compose.ui.graphics.toArgb
import androidx.core.content.edit
import app.caimack.CaimackApp
import app.caimack.MainActivity
import app.caimack.R
import app.caimack.api.RadioRequest
import app.caimack.api.Track
import app.caimack.session.SessionState
import app.caimack.ui.Appearance
import app.caimack.ui.Palette
import app.caimack.ui.artistsOf
import app.caimack.ui.coverCrop
import app.caimack.ui.drawVinyl
import app.caimack.ui.paletteOf
import coil3.SingletonImageLoader
import coil3.request.ImageRequest
import coil3.request.SuccessResult
import coil3.request.allowHardware
import coil3.toBitmap
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch

// Виджет «Сейчас играет»: конверт с выглядывающей пластинкой, название и две кнопки. Пока трек есть —
// «играть/пауза» и «дальше»; когда играть нечего — «Моё радио» и «Плейлист дня». Обновляет его сервис
// воспроизведения при смене трека и паузе.
class NowPlayingWidget : AppWidgetProvider() {
    override fun onUpdate(context: Context, manager: AppWidgetManager, ids: IntArray) = refresh(context)

    companion object {
        private const val PREFERENCES = "widget"
        private const val TITLE = "title"
        private const val ARTIST = "artist"
        private const val COVER = "cover"
        private const val ART_WIDTH = 312
        private const val ART_HEIGHT = 240

        @Volatile private var playing = false

        // Последний трек хранится, чтобы виджет помнил его и после того, как сервис уснул.
        fun show(context: Context, track: Track?, isPlaying: Boolean) {
            playing = isPlaying && track != null
            val container = (context.applicationContext as CaimackApp).container
            context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE).edit {
                if (track != null) {
                    putString(TITLE, track.title)
                    putString(ARTIST, artistsOf(track))
                    putString(COVER, container.media.cover(track.albumId, track.id, track.hasCover, small = true))
                }
            }
            refresh(context)
        }

        fun refresh(context: Context) {
            val app = context.applicationContext
            val manager = AppWidgetManager.getInstance(app)
            val component = ComponentName(app, NowPlayingWidget::class.java)
            if (manager.getAppWidgetIds(component).isEmpty()) return

            val container = (app as CaimackApp).container
            container.scope.launch {
                val stored = app.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE)
                val title = stored.getString(TITLE, null)
                val cover = stored.getString(COVER, null)?.let { url ->
                    val request = ImageRequest.Builder(app).data(url).size(240).allowHardware(false).build()
                    (SingletonImageLoader.get(app).execute(request) as? SuccessResult)?.image?.toBitmap()
                }
                val palette = palette(app)
                manager.updateAppWidget(component, views(app, palette, title, stored.getString(ARTIST, null), cover))
            }
        }

        private fun palette(context: Context): Palette {
            val appearance = (context as CaimackApp).container.appearance
            val night = context.resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK == Configuration.UI_MODE_NIGHT_YES
            val dark = when (appearance.theme.value) {
                Appearance.DARK -> true
                Appearance.LIGHT -> false
                else -> night
            }
            return paletteOf(appearance.colorway.value, dark)
        }

        private fun views(context: Context, palette: Palette, title: String?, artist: String?, cover: Bitmap?): RemoteViews {
            val idle = title == null
            return RemoteViews(context.packageName, R.layout.widget_now_playing).apply {
                setColorStateList(R.id.widget_root, "setBackgroundTintList", ColorStateList.valueOf(palette.card.toArgb()))
                setImageViewBitmap(R.id.widget_art, art(cover, palette))
                setTextViewText(R.id.widget_title, title ?: "Caimack")
                setTextColor(R.id.widget_title, palette.foreground.toArgb())
                setTextViewText(R.id.widget_subtitle, artist ?: context.getString(R.string.widget_idle))
                setTextColor(R.id.widget_subtitle, palette.muted.toArgb())

                val open = PendingIntent.getActivity(
                    context, 0,
                    Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP),
                    PendingIntent.FLAG_IMMUTABLE,
                )
                setOnClickPendingIntent(R.id.widget_art, open)
                setOnClickPendingIntent(R.id.widget_text, open)

                // Главная кнопка — заливка акцентом, вторая — приподнятая поверхность.
                setColorStateList(R.id.widget_primary, "setBackgroundTintList", ColorStateList.valueOf(palette.action.toArgb()))
                setInt(R.id.widget_primary, "setColorFilter", palette.onAction.toArgb())
                setColorStateList(R.id.widget_secondary, "setBackgroundTintList", ColorStateList.valueOf(palette.raised.toArgb()))
                setInt(R.id.widget_secondary, "setColorFilter", palette.foreground.toArgb())

                if (idle) {
                    button(context, R.id.widget_primary, R.drawable.widget_radio, R.string.widget_my_radio, WidgetActions.MY_RADIO)
                    button(context, R.id.widget_secondary, R.drawable.widget_mix, R.string.widget_daily_mix, WidgetActions.DAILY_MIX)
                } else if (playing) {
                    button(context, R.id.widget_primary, R.drawable.widget_pause, R.string.widget_pause, WidgetActions.TOGGLE)
                    button(context, R.id.widget_secondary, R.drawable.widget_next, R.string.widget_next, WidgetActions.NEXT)
                } else {
                    button(context, R.id.widget_primary, R.drawable.widget_play, R.string.widget_play, WidgetActions.TOGGLE)
                    button(context, R.id.widget_secondary, R.drawable.widget_radio, R.string.widget_my_radio, WidgetActions.MY_RADIO)
                }
            }
        }

        private fun RemoteViews.button(context: Context, id: Int, icon: Int, label: Int, action: String) {
            setImageViewResource(id, icon)
            setContentDescription(id, context.getString(label))
            val intent = Intent(context, WidgetActions::class.java).setAction(action)
            setOnClickPendingIntent(id, PendingIntent.getBroadcast(context, action.hashCode(), intent, PendingIntent.FLAG_IMMUTABLE))
        }

        // Конверт слева, пластинка выглядывает справа — как в плеере и на главной.
        private fun art(cover: Bitmap?, palette: Palette): Bitmap {
            val bitmap = Bitmap.createBitmap(ART_WIDTH, ART_HEIGHT, Bitmap.Config.ARGB_8888)
            val canvas = Canvas(bitmap)
            val edge = ART_HEIGHT - 16f
            val top = 8f
            drawVinyl(canvas, edge * 0.86f, top + edge / 2, edge * 0.47f, cover, palette)

            val sleeve = RectF(4f, top, 4f + edge, top + edge)
            canvas.drawRoundRect(sleeve, 10f, 10f, Paint(Paint.ANTI_ALIAS_FLAG).apply {
                color = palette.raised.toArgb()
                setShadowLayer(14f, 0f, 6f, 0x66000000)
            })
            canvas.save()
            canvas.clipPath(Path().apply { addRoundRect(sleeve, 10f, 10f, Path.Direction.CW) })
            if (cover != null) canvas.drawBitmap(cover, coverCrop(cover), sleeve, Paint(Paint.FILTER_BITMAP_FLAG))
            canvas.restore()
            return bitmap
        }
    }
}

// Кнопки виджета. Работают и без открытого приложения — как плитка радио в шторке.
class WidgetActions : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val container = (context.applicationContext as CaimackApp).container
        val player = container.player

        when (intent.action) {
            TOGGLE -> player.toggle()
            NEXT -> player.next()
            MY_RADIO, DAILY_MIX -> {
                val done = goAsync()
                container.scope.launch {
                    try {
                        val session = container.session.state.first { it !is SessionState.Restoring }
                        if (session !is SessionState.SignedIn) {
                            context.startActivity(Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
                            return@launch
                        }
                        if (intent.action == MY_RADIO) {
                            runCatching { container.api.radio(RadioRequest(null, emptyList())) }.getOrNull()
                                ?.takeIf { it.tracks.isNotEmpty() }
                                ?.let { player.playMyRadio(it) }
                        } else {
                            runCatching { container.api.homeMix("daily").tracks }.getOrNull()
                                ?.takeIf { it.isNotEmpty() }
                                ?.let { player.play(it, 0, "widget") }
                        }
                    } finally {
                        done.finish()
                    }
                }
            }
        }
    }

    companion object {
        const val TOGGLE = "app.caimack.widget.TOGGLE"
        const val NEXT = "app.caimack.widget.NEXT"
        const val MY_RADIO = "app.caimack.widget.MY_RADIO"
        const val DAILY_MIX = "app.caimack.widget.DAILY_MIX"
    }
}
