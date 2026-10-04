// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontVariation
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.em
import androidx.compose.ui.unit.sp
import app.caimack.R

@Immutable
data class Palette(
    val background: Color,
    val card: Color,
    val popover: Color,
    val raised: Color,
    val accent: Color,
    val foreground: Color,
    val muted: Color,
    val faint: Color,
    val primary: Color,
    val onPrimary: Color,
    val primarySoft: Color,
    val action: Color,
    val onAction: Color,
    val destructive: Color,
    val warning: Color,
    val success: Color,
    val sleeves: List<Color>,
    val dark: Boolean,
) {
    val border = foreground.copy(alpha = 0.12f)
    val borderStrong = foreground.copy(alpha = 0.24f)
    val controlBorder = foreground.copy(alpha = if (dark) 0.40f else 0.55f)
}

private val Brown = Palette(
    background = Color(0xFF1C1714),
    card = Color(0xFF2A221D),
    popover = Color(0xFF342A24),
    raised = Color(0xFF3A2F28),
    accent = Color(0xFF463A31),
    foreground = Color(0xFFEFE4D2),
    muted = Color(0xFFB3A595),
    faint = Color(0xFF9D9182),
    primary = Color(0xFFD9A441),
    onPrimary = Color(0xFF1C1714),
    primarySoft = Color(0x24D9A441),
    action = Color(0xFFEFE4D2),
    onAction = Color(0xFF1C1714),
    destructive = Color(0xFFF07A6A),
    warning = Color(0xFFE0A93A),
    success = Color(0xFF8FC27A),
    sleeves = listOf(
        Color(0xFF4C2A26), Color(0xFF403E28), Color(0xFF2E3B38),
        Color(0xFF574422), Color(0xFF583320), Color(0xFF402C36),
    ),
    dark = true,
)

private val Kraft = Palette(
    background = Color(0xFFE6DCCB),
    card = Color(0xFFEFE7DA),
    popover = Color(0xFFF7F1E7),
    raised = Color(0xFFF7F1E7),
    accent = Color(0xFFD9CEBA),
    foreground = Color(0xFF2B221C),
    muted = Color(0xFF54493D),
    faint = Color(0xFF6A5C4E),
    primary = Color(0xFF79510C),
    onPrimary = Color(0xFFF7F1E7),
    primarySoft = Color(0x1F79510C),
    action = Color(0xFF2B221C),
    onAction = Color(0xFFF7F1E7),
    destructive = Color(0xFFAE362A),
    warning = Color(0xFF8A5D00),
    success = Color(0xFF3F7A2C),
    sleeves = listOf(
        Color(0xFFD4B7AD), Color(0xFFC9C4A6), Color(0xFFB7C4BB),
        Color(0xFFDCC592), Color(0xFFD9B194), Color(0xFFC8B1B9),
    ),
    dark = false,
)

object Radius {
    val cover = RoundedCornerShape(2.dp)
    val tile = RoundedCornerShape(4.dp)
    val row = RoundedCornerShape(6.dp)
    val panel = RoundedCornerShape(8.dp)
}

private fun onest(weight: Int) = Font(
    R.font.onest_variable,
    FontWeight(weight),
    variationSettings = FontVariation.Settings(FontVariation.weight(weight)),
)

val Onest = FontFamily(onest(400), onest(500), onest(600), onest(700))

val Unbounded = FontFamily(Font(R.font.unbounded_medium, FontWeight.Medium))

object Type {
    val display = TextStyle(
        fontFamily = Unbounded, fontWeight = FontWeight.Medium,
        fontSize = 30.sp, lineHeight = 32.sp, letterSpacing = (-0.02).em,
    )
    val title = TextStyle(
        fontFamily = Unbounded, fontWeight = FontWeight.Medium,
        fontSize = 22.sp, lineHeight = 25.sp, letterSpacing = (-0.015).em,
    )
    val section = TextStyle(
        fontFamily = Onest, fontWeight = FontWeight.SemiBold,
        fontSize = 18.sp, lineHeight = 23.sp, letterSpacing = (-0.01).em,
    )
    val body = TextStyle(fontFamily = Onest, fontSize = 15.sp, lineHeight = 22.sp)
    val small = TextStyle(fontFamily = Onest, fontSize = 14.sp, lineHeight = 20.sp)
    val tiny = TextStyle(fontFamily = Onest, fontSize = 12.sp, lineHeight = 16.sp)
    val micro = TextStyle(fontFamily = Onest, fontSize = 11.sp, lineHeight = 16.sp)
}

val LocalPalette = staticCompositionLocalOf { Brown }

@Composable
fun CaimackTheme(dark: Boolean = isSystemInDarkTheme(), content: @Composable () -> Unit) {
    val palette = if (dark) Brown else Kraft

    val base = if (dark) darkColorScheme() else lightColorScheme()
    val colors = base.copy(
        primary = palette.action,
        onPrimary = palette.onAction,
        secondary = palette.primary,
        onSecondary = palette.onPrimary,
        background = palette.background,
        onBackground = palette.foreground,
        surface = palette.background,
        onSurface = palette.foreground,
        surfaceVariant = palette.raised,
        onSurfaceVariant = palette.muted,
        surfaceContainer = palette.card,
        surfaceContainerHigh = palette.popover,
        surfaceContainerHighest = palette.raised,
        outline = palette.controlBorder,
        outlineVariant = palette.border,
        error = palette.destructive,
    )

    val typography = Typography(
        displaySmall = Type.display,
        headlineSmall = Type.title,
        titleMedium = Type.section,
        bodyLarge = Type.body,
        bodyMedium = Type.small,
        bodySmall = Type.tiny,
        labelLarge = Type.small.copy(fontWeight = FontWeight.Medium),
        labelMedium = Type.tiny.copy(fontWeight = FontWeight.Medium),
        labelSmall = Type.micro.copy(fontWeight = FontWeight.Medium),
    )

    CompositionLocalProvider(LocalPalette provides palette) {
        MaterialTheme(
            colorScheme = colors,
            typography = typography,
            shapes = Shapes(
                extraSmall = Radius.cover,
                small = Radius.row,
                medium = Radius.row,
                large = Radius.panel,
                extraLarge = Radius.panel,
            ),
            content = content,
        )
    }
}
