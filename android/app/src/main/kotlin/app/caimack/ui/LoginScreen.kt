// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

package app.caimack.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.scale
import androidx.compose.ui.graphics.vector.PathParser
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.em
import androidx.compose.ui.unit.sp
import app.caimack.R
import app.caimack.session.LoginFailure
import app.caimack.session.Session
import kotlinx.coroutines.launch

@Composable
fun LoginScreen(session: Session, lastAddress: String, expired: Boolean) {
    val palette = LocalPalette.current
    var address by rememberSaveable { mutableStateOf(lastAddress) }
    var username by rememberSaveable { mutableStateOf("") }
    var password by rememberSaveable { mutableStateOf("") }
    var busy by remember { mutableStateOf(false) }
    var failure by remember { mutableStateOf<LoginFailure?>(null) }
    val scope = rememberCoroutineScope()

    val ready = !busy && address.isNotBlank() && username.isNotBlank() && password.isNotEmpty()
    val submit = {
        if (ready) {
            busy = true
            scope.launch {
                failure = session.login(address, username, password)
                busy = false
            }
        }
    }

    Box(
        modifier = Modifier.fillMaxSize().systemBarsPadding().imePadding().verticalScroll(rememberScrollState()).padding(24.dp),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            modifier = Modifier.widthIn(max = 384.dp).fillMaxWidth().clip(Radius.panel).background(palette.card).padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Column(
                modifier = Modifier.fillMaxWidth().padding(bottom = 8.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                BrandMark(palette.foreground, palette.primary, Modifier.size(72.dp))
                Text(
                    stringResource(R.string.app_name),
                    style = Type.title.copy(fontSize = 20.sp, letterSpacing = (-0.025).em),
                )
                Text(
                    stringResource(R.string.auth_tagline),
                    style = Type.small,
                    color = palette.muted,
                    textAlign = TextAlign.Center,
                )
            }

            Field(
                label = stringResource(R.string.auth_server),
                value = address,
                onValueChange = { address = it },
                keyboard = KeyboardOptions(keyboardType = KeyboardType.Uri, imeAction = ImeAction.Next),
            )
            Field(
                label = stringResource(R.string.auth_username),
                value = username,
                onValueChange = { username = it },
                keyboard = KeyboardOptions(imeAction = ImeAction.Next),
            )
            Field(
                label = stringResource(R.string.auth_password),
                value = password,
                onValueChange = { password = it },
                keyboard = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = ImeAction.Done),
                actions = KeyboardActions(onDone = { submit() }),
                hidden = true,
            )

            val message = when (val shown = failure) {
                LoginFailure.BadAddress -> stringResource(R.string.auth_bad_address)
                LoginFailure.Unreachable -> stringResource(R.string.auth_unreachable)
                is LoginFailure.Refused -> shown.reason ?: stringResource(R.string.auth_failed)
                null -> if (expired) stringResource(R.string.session_expired) else null
            }
            message?.let { Text(it, style = Type.small, color = palette.destructive) }

            Button(
                onClick = { submit() },
                enabled = ready,
                modifier = Modifier.fillMaxWidth().padding(top = 8.dp).height(40.dp),
                colors = ButtonDefaults.buttonColors(
                    disabledContainerColor = palette.action.copy(alpha = 0.5f),
                    disabledContentColor = palette.onAction,
                ),
            ) {
                Text(
                    stringResource(if (busy) R.string.auth_signing_in else R.string.auth_sign_in),
                    style = Type.small.copy(fontWeight = FontWeight.SemiBold),
                )
            }
        }
    }
}

@Composable
private fun Field(
    label: String,
    value: String,
    onValueChange: (String) -> Unit,
    keyboard: KeyboardOptions,
    actions: KeyboardActions = KeyboardActions.Default,
    hidden: Boolean = false,
) {
    val palette = LocalPalette.current
    var revealed by rememberSaveable { mutableStateOf(false) }

    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(label, style = Type.small.copy(fontWeight = FontWeight.Medium), color = palette.muted)
        BasicTextField(
            value = value,
            onValueChange = onValueChange,
            singleLine = true,
            textStyle = Type.body.copy(color = palette.foreground),
            cursorBrush = SolidColor(palette.foreground),
            keyboardOptions = keyboard,
            keyboardActions = actions,
            visualTransformation = if (hidden && !revealed) PasswordVisualTransformation() else VisualTransformation.None,
            modifier = Modifier
                .fillMaxWidth()
                .height(40.dp)
                .clip(Radius.row)
                .background(palette.raised)
                .border(1.dp, palette.controlBorder, Radius.row)
                .padding(start = 12.dp, end = if (hidden) 0.dp else 12.dp),
            decorationBox = { field ->
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Box(Modifier.weight(1f), contentAlignment = Alignment.CenterStart) { field() }
                    if (hidden) {
                        val description = stringResource(if (revealed) R.string.auth_hide_password else R.string.auth_show_password)
                        Box(
                            contentAlignment = Alignment.Center,
                            modifier = Modifier
                                .size(40.dp)
                                .clickable(role = Role.Button, onClickLabel = description) { revealed = !revealed }
                                .semantics { contentDescription = description },
                        ) {
                            Icon(if (revealed) Lucide.EyeOff else Lucide.Eye, null, tint = palette.muted, modifier = Modifier.size(18.dp))
                        }
                    }
                }
            },
        )
    }
}

@Composable
fun BrandMark(ink: Color, brass: Color, modifier: Modifier = Modifier) {
    val ring = remember { PathParser().parsePathString("M23.955 23.955A11.25 11.25 0 1 1 23.955 8.045").toPath() }
    val outer = remember { PathParser().parsePathString("M8.8 14V18M12 12V20M24.8 12.5V19.5M28 14V18").toPath() }
    val inner = remember { PathParser().parsePathString("M15.2 10V22M18.4 8.5V23.5M21.6 11.5V20.5").toPath() }

    Canvas(modifier) {
        scale(size.minDimension / 32f, pivot = Offset.Zero) {
            drawPath(ring, ink, style = Stroke(width = 4.25f, cap = StrokeCap.Round))
            drawPath(outer, ink, style = Stroke(width = 2.1f, cap = StrokeCap.Round))
            drawPath(inner, brass, style = Stroke(width = 2.1f, cap = StrokeCap.Round))
        }
    }
}
