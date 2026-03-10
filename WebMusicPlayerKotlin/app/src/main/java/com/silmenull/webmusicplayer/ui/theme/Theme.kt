package com.silmenull.webmusicplayer.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

private val LightColorScheme = lightColorScheme(
    primary = Iris,
    onPrimary = Color.White,
    primaryContainer = LilacContainer,
    onPrimaryContainer = Color(0xFF22135C),
    secondary = Violet,
    onSecondary = Color.White,
    secondaryContainer = Color(0xFFF1EEFF),
    onSecondaryContainer = Color(0xFF2D2445),
    tertiary = Fuchsia,
    onTertiary = Color.White,
    tertiaryContainer = Color(0xFFF6E6FF),
    onTertiaryContainer = Color(0xFF4B155D),
    error = Color(0xFFBA1A1A),
    errorContainer = ErrorSoft,
    onErrorContainer = Color(0xFF410002),
    background = Mist,
    onBackground = Color(0xFF16131F),
    surface = Color.White,
    onSurface = Color(0xFF1A1625),
    surfaceVariant = Color(0xFFE9E2F4),
    onSurfaceVariant = Color(0xFF4B445B),
    outline = Color(0xFF7D758F),
)

private val DarkColorScheme = darkColorScheme(
    primary = IrisDark,
    onPrimary = Color(0xFF24145F),
    primaryContainer = LilacContainerDark,
    onPrimaryContainer = Color(0xFFE7E0FF),
    secondary = Color(0xFFD1C2FF),
    onSecondary = Color(0xFF362A5B),
    secondaryContainer = Color(0xFF2A2341),
    onSecondaryContainer = Color(0xFFE8E0FF),
    tertiary = Color(0xFFF0B3FF),
    onTertiary = Color(0xFF501A63),
    tertiaryContainer = Color(0xFF6C267F),
    onTertiaryContainer = Color(0xFFFFD7FF),
    error = Color(0xFFFFB4AB),
    errorContainer = ErrorSoftDark,
    onErrorContainer = Color(0xFFFFDAD6),
    background = Night,
    onBackground = Color(0xFFF1EEFF),
    surface = NightSurface,
    onSurface = Color(0xFFF1EEFF),
    surfaceVariant = Color(0xFF302B3B),
    onSurfaceVariant = Color(0xFFCDC4D8),
    outline = Color(0xFF978DAA),
)

@Composable
fun WebMusicPlayerTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit,
) {
    MaterialTheme(
        colorScheme = if (darkTheme) DarkColorScheme else LightColorScheme,
        typography = Typography,
        content = content,
    )
}