package com.jasbone.butt2keyremote

import android.content.Context
import android.content.res.ColorStateList
import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.RippleDrawable
import android.os.Handler
import android.os.Looper
import android.text.InputType
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.TextView

/** Daisy's App's dark colours (src/DaisysApp/Themes/Dark.xaml), and small builders for views made in code. */
object Ui {
    val WINDOW = Color.parseColor("#202020")
    val CARD = Color.parseColor("#2B2B2B")
    val CARD_BORDER = Color.parseColor("#383838")
    val CONTROL = Color.parseColor("#373737")
    val CONTROL_BORDER = Color.parseColor("#474747")
    val TEXT = Color.WHITE
    val SECONDARY = Color.parseColor("#A8A8A8")
    val ACCENT = Color.parseColor("#4CC2FF")
    val DANGER = Color.parseColor("#E5484D")
    val SUCCESS = Color.parseColor("#3DDC84")
    val ERROR_TEXT = Color.parseColor("#FF8A80")

    fun Context.dp(v: Float): Int = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v, resources.displayMetrics).toInt()
    fun Context.dp(v: Int): Int = dp(v.toFloat())

    fun rounded(color: Int, radius: Float, border: Int = Color.TRANSPARENT, borderWidth: Int = 0) = GradientDrawable().apply {
        setColor(color)
        cornerRadius = radius
        if (borderWidth > 0) setStroke(borderWidth, border)
    }

    fun Context.card(): LinearLayout = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL
        background = rounded(CARD, dp(8).toFloat(), CARD_BORDER, dp(1))
        setPadding(dp(16), dp(14), dp(16), dp(14))
    }

    fun Context.text(s: CharSequence, size: Float = 15f, color: Int = TEXT, bold: Boolean = false) = TextView(this).apply {
        text = s
        textSize = size
        setTextColor(color)
        if (bold) setTypeface(typeface, android.graphics.Typeface.BOLD)
    }

    fun Context.heading(s: String) = text(s, 13f, SECONDARY, bold = true).apply {
        isAllCaps = true
        letterSpacing = 0.06f
        setPadding(dp(4), dp(18), 0, dp(8))
    }

    /** A button: [accent] for the main one, otherwise plain. */
    fun Context.button(s: String, accent: Boolean = false, onClick: (View) -> Unit) = Button(this).apply {
        text = s
        isAllCaps = false
        textSize = 15f
        minHeight = dp(44)
        minimumHeight = dp(44)
        stateListAnimator = null
        setPadding(dp(16), 0, dp(16), 0)
        paint(this, if (accent) ACCENT else CONTROL, if (accent) Color.BLACK else TEXT, if (accent) ACCENT else CONTROL_BORDER)
        setOnClickListener { onClick(it) }
    }

    fun paint(b: Button, back: Int, fore: Int, border: Int) {
        val ctx = b.context
        b.background = RippleDrawable(ColorStateList.valueOf(Color.argb(60, 255, 255, 255)), rounded(back, ctx.dp(6).toFloat(), border, ctx.dp(1)), null)
        b.setTextColor(fore)
    }

    /**
     * A button for something that can't be undone: the first press turns it into a red "Confirm?", a second does it;
     * left alone it goes back after 4 seconds (as every confirming button in Daisy's App works).
     */
    fun Context.confirmButton(s: String, onConfirmed: () -> Unit): Button {
        val b = button(s) { }
        val main = Handler(Looper.getMainLooper())
        var armed = false
        val revert = Runnable { armed = false; b.text = s; paint(b, CONTROL, TEXT, CONTROL_BORDER) }
        b.setOnClickListener {
            if (armed) { main.removeCallbacks(revert); revert.run(); onConfirmed(); return@setOnClickListener }
            armed = true
            b.text = "Confirm?"
            paint(b, DANGER, Color.WHITE, DANGER)
            main.postDelayed(revert, 4000)
        }
        return b
    }

    fun Context.field(hint: String, value: String = "", type: Int = InputType.TYPE_CLASS_TEXT) = EditText(this).apply {
        setText(value)
        this.hint = hint
        setSingleLine() // (before inputType: it would replace a password's masking with its own)
        inputType = type
        setTextColor(TEXT)
        setHintTextColor(Color.parseColor("#707070"))
        textSize = 16f
        background = rounded(CONTROL, dp(6).toFloat(), CONTROL_BORDER, dp(1))
        setPadding(dp(12), dp(10), dp(12), dp(10))
    }

    /** A label and its box on one row, the label a fixed width and right-aligned (as Daisy's App's Add a PC). */
    fun Context.row(label: String, view: View, labelWidth: Int = 110): LinearLayout = LinearLayout(this).apply {
        orientation = LinearLayout.HORIZONTAL
        gravity = Gravity.CENTER_VERTICAL
        addView(text(label, 15f, SECONDARY).apply { gravity = Gravity.END }, LinearLayout.LayoutParams(dp(labelWidth), ViewGroup.LayoutParams.WRAP_CONTENT).apply { marginEnd = dp(12) })
        addView(view, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        setPadding(0, dp(5), 0, dp(5))
    }

    fun matchWrap(top: Int = 0) = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = top }
}
