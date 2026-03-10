package com.silmenull.webmusicplayer

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.tooling.preview.Preview
import androidx.lifecycle.viewmodel.compose.viewModel
import com.silmenull.webmusicplayer.ui.WebMusicPlayerRoute
import com.silmenull.webmusicplayer.ui.theme.WebMusicPlayerTheme
import com.silmenull.webmusicplayer.viewmodel.MainViewModel

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            WebMusicPlayerApp()
        }
    }
}

@Preview
@Composable
private fun WebMusicPlayerApp() {
    WebMusicPlayerTheme {
        val viewModel: MainViewModel = viewModel()
        WebMusicPlayerRoute(
            viewModel = viewModel,
            modifier = Modifier,
        )
    }
}