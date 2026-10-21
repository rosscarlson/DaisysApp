package com.jasbone.butt2keyremote

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Matrix
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.graphics.Typeface
import android.text.Layout as TextLayout
import android.text.StaticLayout
import android.text.TextPaint
import android.text.TextUtils
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.View
import kotlin.math.abs
import kotlin.math.min

/**
 * Draws a ButtScreen scaled to fit (keeping its shape, as the README's "Drawing a ButtScreen" says, and as the PC's
 * ScreenVisual does) and turns touches into presses: each finger on a button holds it until lifted (several at once),
 * and a swipe that starts between buttons moves to the next or previous ButtScreen.
 */
class ScreenView(context: Context) : View(context) {
    interface Listener {
        fun onDown(screen: String, button: String)
        fun onUp(screen: String, button: String)
        /** A swipe: +1 for the next ButtScreen, -1 for the one before. */
        fun onSwipe(direction: Int)
    }

    var listener: Listener? = null
    var vibrate = true

    var layout: Layout? = null
        set(value) {
            if (field === value) return
            letGoAll()
            field = value
            texts.clear()
            invalidate()
        }

    private val held = HashMap<Int, String>() // pointer id → button id
    private val swipes = HashMap<Int, FloatArray>() // pointer id → where it went down
    private val fill = Paint(Paint.ANTI_ALIAS_FLAG)
    private val stroke = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE }
    private val bitmapPaint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG)
    private val shade = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.argb(70, 255, 255, 255) }
    private val rect = RectF()
    private val iconPath = Path()
    private val matrix = Matrix()
    private val texts = HashMap<String, StaticLayout>()
    private var scale = 1f
    private var offsetX = 0f
    private var offsetY = 0f

    override fun onDraw(canvas: Canvas) {
        val l = layout ?: return
        // (the padding is a border in the ButtScreen's background: full screen keeps buttons clear of rounded corners)
        canvas.drawColor(l.background)
        val w = (width - paddingLeft - paddingRight).coerceAtLeast(1)
        val h = (height - paddingTop - paddingBottom).coerceAtLeast(1)
        scale = min(w / l.width, h / l.height)
        offsetX = paddingLeft + (w - l.width * scale) / 2
        offsetY = paddingTop + (h - l.height * scale) / 2
        canvas.save()
        canvas.translate(offsetX, offsetY)
        canvas.scale(scale, scale)
        val pressed = held.values.toSet()
        for (b in l.buttons) drawButton(canvas, l, b, b.id in pressed)
        canvas.restore()
    }

    private fun drawButton(canvas: Canvas, l: Layout, b: Butt, pressed: Boolean) {
        val st = b.style
        val x = l.left(b); val y = l.top(b); val w = l.widthOf(b); val h = l.heightOf(b)
        val radius = if (st.rounded) l.cornerRadius else 0f
        rect.set(x, y, x + w, y + h)
        fill.color = st.background
        canvas.drawRoundRect(rect, radius, radius, fill)
        if (st.outlineWidth > 0 && Color.alpha(st.outline) > 0) {
            // drawn inside the button, as the PC's border is
            val half = st.outlineWidth / 2
            stroke.color = st.outline
            stroke.strokeWidth = st.outlineWidth
            rect.set(x + half, y + half, x + w - half, y + h - half)
            val r = maxOf(0f, radius - half)
            canvas.drawRoundRect(rect, r, r, stroke)
        }

        canvas.save()
        canvas.clipRect(x, y, x + w, y + h)
        val side = min(w, h)
        val hasLabel = b.label.isNotBlank()
        val hasPicture = b.icon != null || b.image != null
        val pic = if (hasPicture) side * (if (hasLabel) 0.45f else 0.55f) else 0f
        val text = if (hasLabel) textOf(b, maxOf(1f, w - st.outlineWidth * 2 - side * 0.08f)) else null
        val space = if (hasPicture && text != null) side * 0.04f else 0f
        val total = pic + space + (text?.height ?: 0)
        var top = y + (h - total) / 2
        if (hasPicture) {
            val left = x + (w - pic) / 2
            if (b.image != null) {
                val bw = b.image.width.toFloat(); val bh = b.image.height.toFloat()
                val k = min(pic / bw, pic / bh)
                rect.set(left + (pic - bw * k) / 2, top + (pic - bh * k) / 2, left + (pic + bw * k) / 2, top + (pic + bh * k) / 2)
                canvas.drawBitmap(b.image, null, rect, bitmapPaint)
            } else if (b.icon != null) {
                matrix.setScale(pic / 24f, pic / 24f)
                matrix.postTranslate(left, top)
                b.icon.transform(matrix, iconPath)
                fill.color = l.iconColor
                canvas.drawPath(iconPath, fill)
            }
            top += pic + space
        }
        if (text != null) {
            canvas.save()
            canvas.translate(x + (w - text.width) / 2, top)
            text.draw(canvas)
            canvas.restore()
        }
        canvas.restore()

        if (pressed) {
            val r = maxOf(0f, radius - st.outlineWidth)
            rect.set(x + st.outlineWidth, y + st.outlineWidth, x + w - st.outlineWidth, y + h - st.outlineWidth)
            canvas.drawRoundRect(rect, r, r, shade)
        }
    }

    private fun textOf(b: Butt, maxWidth: Float): StaticLayout = texts.getOrPut(b.id) {
        val st = b.style
        val paint = TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            color = st.fontColor
            textSize = st.fontSize
            typeface = typefaceOf(st)
        }
        StaticLayout.Builder.obtain(b.label, 0, b.label.length, paint, maxWidth.toInt().coerceAtLeast(1))
            .setAlignment(TextLayout.Alignment.ALIGN_CENTER)
            .setEllipsize(TextUtils.TruncateAt.END)
            .setIncludePad(false)
            .build()
    }

    private fun typefaceOf(st: Style): Typeface {
        val bold = if (st.bold) Typeface.BOLD else Typeface.NORMAL
        // Android has families rather than the PC's fonts: the kind the PC says it is (sans-serif, serif or monospace)
        return Typeface.create(st.fontFamily.ifEmpty { "sans-serif" }, bold)
    }

    // ---- touches ----

    @SuppressLint("ClickableViewAccessibility")
    override fun onTouchEvent(e: MotionEvent): Boolean {
        val l = layout ?: return true
        when (e.actionMasked) {
            MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> {
                val i = e.actionIndex
                val id = e.getPointerId(i)
                val b = l.buttonAt((e.getX(i) - offsetX) / scale, (e.getY(i) - offsetY) / scale)
                if (b != null) {
                    val already = b.id in held.values
                    held[id] = b.id
                    if (!already) {
                        if (vibrate) performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY, HapticFeedbackConstants.FLAG_IGNORE_VIEW_SETTING)
                        listener?.onDown(l.id, b.id)
                    }
                    invalidate()
                } else swipes[id] = floatArrayOf(e.getX(i), e.getY(i))
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_POINTER_UP -> {
                val i = e.actionIndex
                val id = e.getPointerId(i)
                release(id)
                swipes.remove(id)?.let { start ->
                    val dx = e.getX(i) - start[0]
                    val dy = e.getY(i) - start[1]
                    if (abs(dx) > width * 0.12f && abs(dx) > abs(dy) * 1.5f) listener?.onSwipe(if (dx < 0) 1 else -1)
                }
            }
            MotionEvent.ACTION_CANCEL -> { letGoAll(); swipes.clear() }
        }
        return true
    }

    private fun release(pointer: Int) {
        val b = held.remove(pointer) ?: return
        // a button held by two fingers stays held until both let go
        if (b !in held.values) layout?.let { listener?.onUp(it.id, b) }
        invalidate()
    }

    /** Lets go of everything held (another ButtScreen shown, the connection gone, the app in the background). */
    fun letGoAll() {
        for (p in held.keys.toList()) release(p)
        swipes.clear()
    }

    override fun onDetachedFromWindow() {
        letGoAll()
        super.onDetachedFromWindow()
    }
}
