package com.silmenull.webmusicplayer

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Menu
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.Button
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import com.silmenull.webmusicplayer.models.AppPage
import com.silmenull.webmusicplayer.ui.theme.WebMusicPlayerTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            WebMusicPlayerTheme {
                Scaffold(modifier = Modifier.fillMaxSize()) { innerPadding ->
                    MainPage(
                        modifier = Modifier.padding(innerPadding)
                    )
                }
            }
        }
    }
}

@Composable
fun MainPage(modifier: Modifier) {

    var activePage by remember { mutableStateOf(AppPage.Streams) }

    Box(modifier = modifier.padding(16.dp)) {
        Column() {
            Row() {
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text =
                            if (activePage == AppPage.Favourites) {
                                "Favorites"
                            } else if (activePage == AppPage.Streams) {
                                "Streams"
                            } else if (activePage == AppPage.Subscriptions) {
                                "Subscriptions"
                            } else {
                                "Invalid"
                            },
                        style = MaterialTheme.typography.headlineLarge
                    )
                    Text("WebMusicPlayer")
                }

                Spacer(modifier = Modifier.width(8.dp))
                Row(modifier = Modifier.align(Alignment.Top)) {
                    if (activePage == AppPage.Streams) {
                        IconButton({

                        }) {
                            Icon(
                                Icons.Default.Add,
                                contentDescription = "QWQ"
                            )
                        }
                        Spacer(modifier = Modifier.width(8.dp))
                        Box() {
                            var isAddMediaStreamExpanded by remember { mutableStateOf(false) }

                            IconButton({
                                isAddMediaStreamExpanded = true
                            }) {
                                Icon(
                                    Icons.Default.Menu,
                                    "Add Media Stream"
                                )
                            }

                            DropdownMenu(
                                isAddMediaStreamExpanded,
                                onDismissRequest = { isAddMediaStreamExpanded = false }
                            ) {
                                DropdownMenuItem({
                                    Text("QWQ")
                                }, {

                                })
                                DropdownMenuItem({
                                    Text("QWQ")
                                }, {

                                })
                                DropdownMenuItem({
                                    Text("QWQ")
                                }, {

                                })
                            }
                        }
                    } else if (activePage == AppPage.Subscriptions) {
                        IconButton({

                        }) {
                            Icon(
                                Icons.Default.Refresh,
                                "Refresh subscription"
                            )
                        }
                        Spacer(modifier = Modifier.width(8.dp))
                        IconButton({ }) {
                            Icon(
                                Icons.Default.Add,
                                "Add subscription"
                            )
                        }
                    }
                }
            }
            Spacer(modifier = Modifier.height(4.dp))
            Row() {
                Button(
                    { activePage = AppPage.Favourites },
                    modifier = Modifier.weight(1f)
                ) {
                    Text("Fav")
                }
                Spacer(modifier = Modifier.width(8.dp))
                Button(
                    { activePage = AppPage.Streams },
                    modifier = Modifier.weight(1f)
                ) { }
                Spacer(modifier = Modifier.width(8.dp))
                Button(
                    { activePage = AppPage.Subscriptions },
                    modifier = Modifier.weight(1f)
                ) { }
            }
        }
    }
}

@Preview
@Composable
fun MainPagePreview() {
    MainPage(Modifier.padding(0.dp))
}