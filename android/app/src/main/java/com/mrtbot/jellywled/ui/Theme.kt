package com.mrtbot.jellywled.ui

import android.view.inputmethod.InputMethodManager
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.text.input.InputTransformation
import androidx.compose.foundation.text.input.OutputTransformation
import androidx.compose.foundation.text.input.TextFieldLineLimits
import androidx.compose.foundation.text.input.rememberTextFieldState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.snapshotFlow
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.composed
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.key
import androidx.compose.ui.input.key.onPreviewKeyEvent
import androidx.compose.ui.input.key.type
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontVariation
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.mrtbot.jellywled.R
import kotlinx.coroutines.delay
import org.json.JSONException
import java.io.IOException
import java.net.ConnectException
import java.net.SocketTimeoutException
import java.net.UnknownHostException

val Ink = Color(0xFF0B0B0F)
val Panel = Color(0xFF15151C)
val Panel2 = Color(0xFF20202A)
val Line = Color(0xFF34343F)
val TextHi = Color(0xFFF4F4F8)
val TextLo = Color(0xFFA9A9B8)
val Orange = Color(0xFFFF7A18)
val Crimson = Color(0xFFAF002D)
val Teal = Color(0xFF319197)
val Good = Color(0xFF5FD3A6)
val Bad = Color(0xFFFF6B6B)

/** The warm-to-cool accent, used sparingly: focus rings, the selected tab, the active glow state. */
val GlowBrush = Brush.linearGradient(listOf(Orange, Crimson, Teal))

private fun outfit(weight: Int) =
    Font(R.font.outfit, FontWeight(weight), variationSettings = FontVariation.Settings(FontVariation.weight(weight)))

/** Outfit (OFL, bundled) for headings; the system font for body text. */
val Outfit = FontFamily(outfit(500), outfit(600), outfit(700))

private val Type = Typography().let { t ->
    t.copy(
        headlineMedium = t.headlineMedium.copy(fontFamily = Outfit, fontWeight = FontWeight.SemiBold),
        headlineSmall = t.headlineSmall.copy(fontFamily = Outfit, fontWeight = FontWeight.SemiBold),
        titleLarge = t.titleLarge.copy(fontFamily = Outfit, fontWeight = FontWeight.SemiBold),
        titleMedium = t.titleMedium.copy(fontFamily = Outfit, fontWeight = FontWeight.Medium),
        labelLarge = t.labelLarge.copy(fontFamily = Outfit, fontWeight = FontWeight.SemiBold),
    )
}

@Composable
fun GlowTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = darkColorScheme(
            primary = Orange, onPrimary = Ink, secondary = Teal, onSecondary = Ink, tertiary = Crimson,
            background = Ink, onBackground = TextHi, surface = Panel, onSurface = TextHi,
            surfaceVariant = Panel2, onSurfaceVariant = TextLo, outline = Line, error = Bad,
        ),
        typography = Type,
        content = content,
    )
}

/**
 * The D-pad focus treatment from AGENTS.md: a 2 dp accent ring and a 1.04 scale while focused, animated in 160 ms.
 * Apply it before the element's own clickable or focusable so it observes that element's focus.
 */
fun Modifier.glowFocus(shape: Shape = RoundedCornerShape(14.dp), ring: Boolean = true): Modifier = composed {
    var focused by remember { mutableStateOf(false) }
    val scale by animateFloatAsState(if (focused) 1.04f else 1f, tween(160), label = "focus-scale")
    this
        .onFocusChanged { focused = it.hasFocus }
        .graphicsLayer {
            scaleX = scale
            scaleY = scale
        }
        .then(if (ring) Modifier.border(2.dp, if (focused) GlowBrush else SolidColor(Color.Transparent), shape) else Modifier)
}

/** A scrolling, centered column for one screen; content is capped at a readable width on a TV. */
@Composable
fun ScreenColumn(content: @Composable ColumnScope.() -> Unit) {
    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.TopCenter) {
        Column(
            Modifier
                .widthIn(max = 760.dp)
                .fillMaxWidth()
                .verticalScroll(rememberScrollState())
                .padding(start = 20.dp, end = 20.dp, top = 12.dp, bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
            content = content,
        )
    }
}

@Composable
fun SectionTitle(text: String) {
    Text(text, style = MaterialTheme.typography.headlineSmall, color = TextHi)
}

@Composable
fun Hint(text: String) {
    Text(text, style = MaterialTheme.typography.bodyMedium, color = TextLo)
}

/** A one-line result: green when [ok], red when not, neutral when null. */
@Composable
fun Outcome(text: String, ok: Boolean?) {
    Text(text, style = MaterialTheme.typography.bodyMedium, color = when (ok) { true -> Good; false -> Bad; null -> TextLo })
}

@Composable
fun Card(modifier: Modifier = Modifier, content: @Composable ColumnScope.() -> Unit) {
    Column(
        modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(Panel).padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
        content = content,
    )
}

@Composable
fun GlowButton(text: String, modifier: Modifier = Modifier, primary: Boolean = false, enabled: Boolean = true, onClick: () -> Unit) {
    val shape = RoundedCornerShape(14.dp)
    Button(
        onClick = onClick,
        modifier = modifier.glowFocus(shape).heightIn(min = 48.dp),
        enabled = enabled,
        shape = shape,
        colors = ButtonDefaults.buttonColors(
            containerColor = if (primary) Orange else Panel2,
            contentColor = if (primary) Ink else TextHi,
            disabledContainerColor = Panel2.copy(alpha = 0.5f),
            disabledContentColor = TextLo.copy(alpha = 0.6f),
        ),
        contentPadding = PaddingValues(horizontal = 20.dp, vertical = 12.dp),
    ) {
        Text(text, style = MaterialTheme.typography.labelLarge)
    }
}

/** Buttons that wrap onto more lines on a narrow phone. */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun ButtonRow(content: @Composable () -> Unit) {
    FlowRow(horizontalArrangement = Arrangement.spacedBy(10.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) { content() }
}

/** A whole-row switch: the row takes focus and toggles, the switch only shows the state. */
@Composable
fun ToggleRow(title: String, checked: Boolean, subtitle: String? = null, enabled: Boolean = true, onChange: (Boolean) -> Unit) {
    val shape = RoundedCornerShape(14.dp)
    Row(
        Modifier
            .fillMaxWidth()
            .glowFocus(shape)
            .clip(shape)
            .background(Panel)
            .toggleable(checked, enabled = enabled, role = Role.Switch, onValueChange = onChange)
            .heightIn(min = 56.dp)
            .padding(horizontal = 16.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(Modifier.weight(1f).padding(end = 12.dp)) {
            Text(title, style = MaterialTheme.typography.titleMedium, color = if (enabled) TextHi else TextLo)
            if (subtitle != null) Text(subtitle, style = MaterialTheme.typography.bodySmall, color = TextLo)
        }
        Switch(
            checked = checked,
            onCheckedChange = null,
            enabled = enabled,
            colors = SwitchDefaults.colors(
                checkedThumbColor = Ink, checkedTrackColor = Orange, checkedBorderColor = Orange,
                uncheckedThumbColor = TextLo, uncheckedTrackColor = Panel2, uncheckedBorderColor = Line,
            ),
        )
    }
}

/** A labeled set of mutually exclusive options. */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun Choice(label: String, options: List<Pair<String, String>>, selected: String, onSelect: (String) -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(label, style = MaterialTheme.typography.labelLarge, color = TextLo)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            options.forEach { (value, text) ->
                val on = value.equals(selected, ignoreCase = true)
                val shape = RoundedCornerShape(12.dp)
                Box(
                    Modifier
                        .glowFocus(shape)
                        .clip(shape)
                        .background(if (on) Orange.copy(alpha = 0.16f) else Panel2)
                        .border(1.dp, if (on) Orange else Line, shape)
                        .selectable(on, role = Role.RadioButton) { onSelect(value) }
                        .heightIn(min = 48.dp)
                        .padding(horizontal = 16.dp, vertical = 12.dp),
                    contentAlignment = Alignment.Center,
                ) {
                    Text(text, color = if (on) Orange else TextHi, style = MaterialTheme.typography.labelLarge)
                }
            }
        }
    }
}

/**
 * A single-line text field holding its own text, seeded with [value]; [onValue] gets every change and [accept]
 * rejects edits outright. Built on TextFieldState because only that text field honors showKeyboardOnFocus: with a
 * remote, moving focus onto a field must not open the keyboard (it would take over the D-pad); the center key or a tap
 * opens it.
 */
@Composable
fun Field(
    label: String,
    value: String,
    modifier: Modifier = Modifier,
    keyboard: KeyboardType = KeyboardType.Text,
    password: Boolean = false,
    supporting: String? = null,
    isError: Boolean = false,
    accept: (String) -> Boolean = { true },
    onValue: (String) -> Unit,
) {
    val state = rememberTextFieldState(value)
    val report by rememberUpdatedState(onValue)
    val allow by rememberUpdatedState(accept)
    LaunchedEffect(state) { snapshotFlow { state.text.toString() }.collect { report(it) } }
    // Remote: focus alone starts no input session. The first center press flips showKeyboardOnFocus, which makes the
    // focused field start its session with the keyboard; later presses reach the field, which reopens the keyboard.
    var typing by remember { mutableStateOf(false) }
    val view = LocalView.current
    LaunchedEffect(typing) {
        if (!typing) return@LaunchedEffect
        delay(150) // let the field start its input session first
        view.context.getSystemService(InputMethodManager::class.java)?.showSoftInput(view, 0)
    }
    OutlinedTextField(
        state = state,
        // The text field's own focused border is the 2 dp accent ring; add only the scale.
        modifier = modifier
            .fillMaxWidth()
            .onFocusChanged { if (!it.hasFocus) typing = false }
            .onPreviewKeyEvent { e ->
                if (typing || (e.key != Key.DirectionCenter && e.key != Key.Enter && e.key != Key.NumPadEnter)) return@onPreviewKeyEvent false
                if (e.type == KeyEventType.KeyUp) typing = true
                true
            }
            .glowFocus(ring = false),
        label = { Text(label) },
        lineLimits = TextFieldLineLimits.SingleLine,
        isError = isError,
        supportingText = supporting?.let { { Text(it) } },
        keyboardOptions = KeyboardOptions(keyboardType = keyboard, showKeyboardOnFocus = typing),
        inputTransformation = InputTransformation { if (!allow(toString())) revertAllChanges() },
        outputTransformation = if (password) OutputTransformation { replace(0, length, "\u2022".repeat(length)) } else null,
        shape = RoundedCornerShape(12.dp),
        colors = OutlinedTextFieldDefaults.colors(
            focusedBorderColor = Orange, unfocusedBorderColor = Line, focusedLabelColor = Orange, cursorColor = Orange,
            focusedContainerColor = Panel, unfocusedContainerColor = Panel, focusedTextColor = TextHi, unfocusedTextColor = TextHi,
        ),
    )
}

/** An integer field that reports only values inside [range] and flags anything else. */
@Composable
fun IntField(label: String, value: Int, range: IntRange, modifier: Modifier = Modifier, onValue: (Int) -> Unit) {
    var text by remember { mutableStateOf(value.toString()) }
    val valid = text.toIntOrNull()?.let { it in range } == true
    Field(
        label, value.toString(), modifier, KeyboardType.Number,
        supporting = if (valid) null else "${range.first} to ${range.last}", isError = !valid,
        accept = { t -> t.length <= 6 && t.withIndex().all { (i, c) -> c.isDigit() || (c == '-' && i == 0) } },
    ) { t ->
        text = t
        t.toIntOrNull()?.takeIf { it in range }?.let(onValue)
    }
}

/** A decimal field that reports only values inside [range] and flags anything else. */
@Composable
fun DecimalField(label: String, value: Double, range: ClosedFloatingPointRange<Double>, modifier: Modifier = Modifier, onValue: (Double) -> Unit) {
    var text by remember { mutableStateOf(value.toString()) }
    val valid = text.toDoubleOrNull()?.let { it in range } == true
    Field(
        label, value.toString(), modifier, KeyboardType.Decimal,
        supporting = if (valid) null else "${range.start} to ${range.endInclusive}", isError = !valid,
        accept = { t -> t.length <= 6 && t.all { it.isDigit() || it == '.' } },
    ) { t ->
        text = t
        t.toDoubleOrNull()?.takeIf { it in range }?.let(onValue)
    }
}

/** Turns network and parsing failures into a sentence for the screen. */
fun friendly(e: Throwable): String = when (e) {
    is UnknownHostException -> "Cannot find that host. Check the address."
    is ConnectException -> "Nothing answered at that address. Check the host and port."
    is SocketTimeoutException -> "The server took too long to answer."
    is JSONException -> "That address answered, but not like a Jellyfin server."
    is IOException -> e.message ?: "Network error."
    else -> e.message ?: e.javaClass.simpleName
}
