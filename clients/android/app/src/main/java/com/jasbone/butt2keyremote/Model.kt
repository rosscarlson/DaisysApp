package com.jasbone.butt2keyremote

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Color
import android.graphics.Path
import android.util.Base64
import org.json.JSONArray
import org.json.JSONObject

/** A ButtScreen as the state names it: enough to know its shape and whether the layout we have is the latest. */
data class ScreenInfo(val id: String, val name: String, val width: Int, val height: Int, val version: Int)

data class BigButt(val name: String, val screens: List<String>)

/** The PC's state: the BigButt in use (its ButtScreens, in order), every BigButt, every ButtScreen. */
data class State(
    val pc: String,
    val bigButt: String,
    val currentName: String,
    val current: List<String>,
    val bigButts: List<BigButt>,
    val screens: Map<String, ScreenInfo>,
) {
    companion object {
        fun parse(o: JSONObject): State {
            val cur = o.optJSONObject("current")
            val current = cur?.optJSONArray("screens").objects().map { it.optString("id") }
            val screens = o.optJSONArray("screens").objects().map {
                ScreenInfo(it.optString("id"), it.optString("name"), it.optInt("width", 1080), it.optInt("height", 2400), it.optInt("version"))
            }.associateBy { it.id }
            val bigButts = o.optJSONArray("bigButts").objects().map { b ->
                BigButt(b.optString("name"), b.optJSONArray("screens").strings())
            }
            val active = o.optString("bigButt")
            return State(o.optString("pc"), active, cur?.optString("name")?.ifEmpty { null } ?: active, current, bigButts, screens)
        }
    }
}

data class Style(
    val rounded: Boolean,
    val background: Int,
    val outline: Int,
    val outlineWidth: Float,
    val font: String,
    val fontFamily: String,
    val fontSize: Float,
    val fontColor: Int,
    val bold: Boolean,
)

data class Butt(
    val id: String,
    val column: Int, val row: Int, val columnSpan: Int, val rowSpan: Int,
    val label: String,
    val icon: Path?,
    val image: Bitmap?,
    val shows: String?,
    val style: Style,
)

/** A ButtScreen's layout, ready to draw: its grid, colours and buttons (icons made into paths, pictures decoded). */
class Layout(
    val id: String,
    val name: String,
    val version: Int,
    val width: Float,
    val height: Float,
    val columns: Int,
    val rows: Int,
    val gap: Float,
    val cornerRadius: Float,
    val background: Int,
    val iconColor: Int,
    val buttons: List<Butt>,
) {
    val cellWidth get() = maxOf(1f, (width - gap * (columns + 1)) / columns)
    val cellHeight get() = maxOf(1f, (height - gap * (rows + 1)) / rows)

    fun left(b: Butt) = gap + b.column * (cellWidth + gap)
    fun top(b: Butt) = gap + b.row * (cellHeight + gap)
    fun widthOf(b: Butt) = b.columnSpan * cellWidth + (b.columnSpan - 1) * gap
    fun heightOf(b: Butt) = b.rowSpan * cellHeight + (b.rowSpan - 1) * gap

    /** The button under a point in the ButtScreen's pixels, or null. */
    fun buttonAt(x: Float, y: Float): Butt? = buttons.lastOrNull {
        val l = left(it); val t = top(it)
        x >= l && y >= t && x < l + widthOf(it) && y < t + heightOf(it)
    }

    companion object {
        /** The answer to "get": {"op":"screen","screen":{…},"icons":{name: path},"images":{file: base64}}. */
        fun parse(o: JSONObject): Layout {
            val s = o.getJSONObject("screen")
            val iconPaths = HashMap<String, Path>()
            o.optJSONObject("icons")?.let { icons ->
                for (k in icons.keys()) SvgPath.parse(icons.optString(k))?.let { iconPaths[k] = it }
            }
            val images = HashMap<String, Bitmap>()
            o.optJSONObject("images")?.let { im ->
                for (k in im.keys()) try {
                    val bytes = Base64.decode(im.optString(k), Base64.DEFAULT)
                    BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.let { images[k] = it }
                } catch (_: Exception) { }
            }
            val buttons = s.optJSONArray("buttons").objects().map { b ->
                val st = b.optJSONObject("style") ?: JSONObject()
                Butt(
                    id = b.optString("id"),
                    column = b.optInt("column"), row = b.optInt("row"),
                    columnSpan = maxOf(1, b.optInt("columnSpan", 1)), rowSpan = maxOf(1, b.optInt("rowSpan", 1)),
                    label = b.optString("label"),
                    icon = b.optStringOrNull("icon")?.let { iconPaths[it] },
                    image = b.optStringOrNull("image")?.let { images[it] },
                    shows = b.optStringOrNull("shows"),
                    style = Style(
                        rounded = st.optBoolean("rounded", true),
                        background = colour(st.optString("background"), Color.rgb(0x2D, 0x35, 0x40)),
                        outline = colour(st.optString("outline"), Color.TRANSPARENT),
                        outlineWidth = st.optDouble("outlineWidth", 0.0).toFloat().coerceAtLeast(0f),
                        font = st.optString("font"),
                        fontFamily = st.optString("fontFamily", "sans-serif"),
                        fontSize = st.optDouble("fontSize", 40.0).toFloat().coerceAtLeast(4f),
                        fontColor = colour(st.optString("fontColor"), Color.WHITE),
                        bold = st.optBoolean("bold"),
                    ),
                )
            }
            return Layout(
                id = s.optString("id"), name = s.optString("name"), version = s.optInt("version"),
                width = s.optDouble("width", 1080.0).toFloat().coerceAtLeast(1f),
                height = s.optDouble("height", 2400.0).toFloat().coerceAtLeast(1f),
                columns = maxOf(1, s.optInt("columns", 1)), rows = maxOf(1, s.optInt("rows", 1)),
                gap = s.optDouble("gap", 0.0).toFloat().coerceAtLeast(0f),
                cornerRadius = s.optDouble("cornerRadius", 0.0).toFloat(),
                background = colour(s.optString("background"), Color.rgb(0x15, 0x18, 0x1C)),
                iconColor = colour(s.optString("iconColor"), Color.WHITE),
                buttons = buttons,
            )
        }

        /** #AARRGGBB (what the PC sends), #RRGGBB, #ARGB or #RGB. */
        fun colour(text: String?, fallback: Int): Int {
            val t = text?.trim()?.removePrefix("#") ?: return fallback
            return try {
                when (t.length) {
                    8 -> t.toLong(16).toInt()
                    6 -> (0xFF000000 or t.toLong(16)).toInt()
                    4, 3 -> {
                        val full = (if (t.length == 3) "F$t" else t).map { "$it$it" }.joinToString("")
                        full.toLong(16).toInt()
                    }
                    else -> Color.parseColor(text)
                }
            } catch (_: Exception) { fallback }
        }
    }
}

fun JSONArray?.objects(): List<JSONObject> =
    if (this == null) emptyList() else (0 until length()).mapNotNull { optJSONObject(it) }

fun JSONArray?.strings(): List<String> =
    if (this == null) emptyList() else (0 until length()).map { optString(it) }

fun JSONObject.optStringOrNull(name: String): String? =
    if (isNull(name)) null else optString(name).ifEmpty { null }
