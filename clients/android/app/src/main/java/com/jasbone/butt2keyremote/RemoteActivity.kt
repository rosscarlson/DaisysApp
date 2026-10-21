package com.jasbone.butt2keyremote

import android.app.Activity
import android.app.AlertDialog
import android.content.Intent
import android.content.pm.ActivityInfo
import android.graphics.Color
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.text.TextUtils
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.view.WindowManager
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.PopupMenu
import android.widget.TextView
import android.widget.Toast
import com.jasbone.butt2keyremote.Ui.button
import com.jasbone.butt2keyremote.Ui.card
import com.jasbone.butt2keyremote.Ui.dp
import com.jasbone.butt2keyremote.Ui.text
import org.json.JSONObject

/**
 * Connected to a PC: the BigButt in use, its ButtScreens one at a time (swipe between them, or the arrows), following
 * the PC as its BigButt changes (a game starting, or picked by hand there or here) and as a Show a ButtScreen button
 * says. Full screen hides everything but the ButtScreen and a small Exit button in the corner Settings picks.
 */
class RemoteActivity : Activity(), Remote.Listener, ScreenView.Listener {
    companion object { const val EXTRA_PC = "pc" }

    private lateinit var prefs: Prefs
    private lateinit var pc: Pc
    private var remote: Remote? = null
    private val main = Handler(Looper.getMainLooper())

    // what the PC told us
    private var state: State? = null
    private val layouts = HashMap<String, Layout>()
    private val asked = HashMap<String, Int>() // ButtScreen → the version asked for
    private var pages: List<String> = emptyList()
    private var index = 0
    private var shown: String? = null // a ButtScreen a Show a ButtScreen button asked for, not in the BigButt's
    private var connected = false
    private var fullScreen = false

    // views
    private lateinit var root: FrameLayout
    private lateinit var bar: LinearLayout
    private lateinit var chipsBox: android.widget.HorizontalScrollView
    private lateinit var chips: LinearLayout
    private lateinit var bigButtPanel: android.widget.ScrollView
    private lateinit var bigButtList: LinearLayout
    private var askedBigButt = false
    private lateinit var title: TextView
    private lateinit var subtitle: TextView
    private lateinit var prev: TextView
    private lateinit var next: TextView
    private lateinit var screen: ScreenView
    private lateinit var message: LinearLayout
    private lateinit var messageText: TextView
    private lateinit var messageButtons: LinearLayout
    private lateinit var exit: TextView
    private lateinit var pageName: TextView
    private val hidePageName = Runnable { pageName.animate().alpha(0f).setDuration(300).start() }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        prefs = Prefs(this)
        pc = prefs.pc(intent.getStringExtra(EXTRA_PC)) ?: run { finish(); return }
        build()
        setContentView(root)
        setFullScreen(savedInstanceState?.getBoolean("fullScreen") ?: prefs.startFullScreen)
    }

    override fun onSaveInstanceState(outState: Bundle) {
        super.onSaveInstanceState(outState)
        outState.putBoolean("fullScreen", fullScreen)
    }

    override fun onStart() {
        super.onStart()
        // settings may have changed while away
        screen.vibrate = prefs.vibrate
        if (prefs.keepScreenOn) window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON) else window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        placeExit()
        prefs.pc(pc.id)?.let { pc = it }
        connect()
    }

    override fun onStop() {
        screen.letGoAll()
        remote?.stop()
        remote = null
        connected = false
        super.onStop()
    }

    private fun connect() {
        remote?.stop()
        remote = Remote(pc, { MainActivity.hello(this) }, this).also { it.start() }
    }

    // ---- the views ----

    private fun build() {
        root = FrameLayout(this).apply { setBackgroundColor(Color.BLACK) }
        val col = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        root.addView(col, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))

        bar = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setBackgroundColor(Ui.WINDOW)
            setPadding(dp(4), dp(4), dp(4), dp(4))
        }
        bar.addView(barButton("‹", "Back to the PCs") { finish() })
        val titles = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(6), 0, dp(6), 0)
            isClickable = true
            foreground = getDrawable(android.R.drawable.list_selector_background)
            contentDescription = "BigButts"
            setOnClickListener { pickBigButt() }
        }
        title = text("", 17f, bold = true).apply { setSingleLine(); ellipsize = TextUtils.TruncateAt.END }
        subtitle = text("", 13f, Ui.SECONDARY).apply { setSingleLine(); ellipsize = TextUtils.TruncateAt.END }
        titles.addView(title)
        titles.addView(subtitle)
        bar.addView(titles, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        prev = barButton("◀", "The ButtScreen before") { onSwipe(-1) }
        next = barButton("▶", "The next ButtScreen") { onSwipe(1) }
        bar.addView(prev)
        bar.addView(next)
        bar.addView(TextView(this).apply {
            text = "BigButts"
            textSize = 15f
            gravity = Gravity.CENTER
            setTextColor(Ui.TEXT)
            background = Ui.rounded(Ui.CONTROL, dp(6).toFloat(), Ui.CONTROL_BORDER, dp(1))
            setPadding(dp(12), dp(8), dp(12), dp(8))
            setOnClickListener { pickBigButt() }
        }, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { marginStart = dp(4); marginEnd = dp(4) })
        bar.addView(barButton("⛶", "Full screen") { setFullScreen(true) })
        bar.addView(barButton("⋮", "More") { v -> menu(v) })
        col.addView(bar, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))

        // the PC's BigButts, a chip each: the one in use lit, a tap uses another
        chips = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; setPadding(dp(8), 0, dp(8), dp(8)) }
        chipsBox = android.widget.HorizontalScrollView(this).apply {
            isHorizontalScrollBarEnabled = false
            setBackgroundColor(Ui.WINDOW)
            visibility = View.GONE
            addView(chips)
        }
        col.addView(chipsBox, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))

        val content = FrameLayout(this)
        screen = ScreenView(this).apply { listener = this@RemoteActivity }
        content.addView(screen, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))

        message = card().apply { gravity = Gravity.CENTER_HORIZONTAL; visibility = View.GONE }
        messageText = text("", 16f).apply { gravity = Gravity.CENTER }
        messageButtons = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER }
        message.addView(messageText)
        message.addView(messageButtons, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(12) })
        content.addView(message, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.CENTER))
        // no wider than a phone's worth, centred, on a tablet
        content.addOnLayoutChangeListener { _, l, _, r, _, _, _, _, _ ->
            val w = minOf(r - l - dp(48), dp(560))
            val lp = message.layoutParams as FrameLayout.LayoutParams
            if (lp.width != w) { lp.width = w; message.post { message.layoutParams = lp } }
        }

        pageName = text("", 15f).apply {
            background = Ui.rounded(Color.argb(200, 0, 0, 0), dp(16).toFloat())
            setPadding(dp(14), dp(6), dp(14), dp(6))
            alpha = 0f
        }
        content.addView(pageName, FrameLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.BOTTOM or Gravity.CENTER_HORIZONTAL).apply { bottomMargin = dp(24) })

        // the BigButts screen, over the ButtScreen
        bigButtList = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(dp(20), dp(16), dp(20), dp(24)) }
        bigButtPanel = android.widget.ScrollView(this).apply {
            setBackgroundColor(Ui.WINDOW)
            visibility = View.GONE
            isClickable = true // (touches stay here, off the ButtScreen behind)
            addView(bigButtList)
        }
        content.addView(bigButtPanel, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
        col.addView(content, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f))

        exit = TextView(this).apply {
            text = "✕"
            textSize = 16f
            gravity = Gravity.CENTER
            setTextColor(Color.argb(220, 255, 255, 255))
            background = Ui.rounded(Color.argb(140, 0, 0, 0), dp(18).toFloat(), Color.argb(90, 255, 255, 255), dp(1))
            contentDescription = "Exit full screen"
            visibility = View.GONE
            setOnClickListener {
                if (prefs.exitLongPress) Toast.makeText(this@RemoteActivity, "Hold to leave full screen", Toast.LENGTH_SHORT).show()
                else setFullScreen(false)
            }
            setOnLongClickListener { setFullScreen(false); true }
        }
        root.addView(exit, FrameLayout.LayoutParams(dp(36), dp(36)))

        root.setOnApplyWindowInsetsListener { v, insets ->
            @Suppress("DEPRECATION")
            if (fullScreen) v.setPadding(0, 0, 0, 0)
            else if (Build.VERSION.SDK_INT >= 30) {
                val i = insets.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout())
                v.setPadding(i.left, i.top, i.right, i.bottom)
            } else v.setPadding(insets.systemWindowInsetLeft, insets.systemWindowInsetTop, insets.systemWindowInsetRight, insets.systemWindowInsetBottom)
            val b = if (fullScreen) border(insets) else IntArray(4)
            screen.setPadding(b[0], b[1], b[2], b[3])
            placeExit(insets)
            insets
        }
        render()
    }

    private fun barButton(s: String, description: String, onClick: (View) -> Unit) = TextView(this).apply {
        text = s
        textSize = 20f
        gravity = Gravity.CENTER
        setTextColor(Ui.TEXT)
        contentDescription = description
        minWidth = dp(44)
        minHeight = dp(44)
        isClickable = true
        background = getDrawable(android.R.drawable.list_selector_background)
        setOnClickListener { onClick(it) }
    }

    /**
     * Full screen's border round the ButtScreen (left, top, right, bottom): clear of the screen's rounded corners (a
     * corner of radius r cuts r × (1 - 1/√2) into a square's corner), of a camera cutout, plus Settings' own margin.
     */
    private fun border(insets: WindowInsets?): IntArray {
        val extra = dp(prefs.fullScreenBorder)
        val b = IntArray(4) { extra }
        if (insets == null) return b
        if (Build.VERSION.SDK_INT >= 31) {
            fun cut(position: Int) = ((insets.getRoundedCorner(position)?.radius ?: 0) * 0.30f).toInt()
            val tl = cut(android.view.RoundedCorner.POSITION_TOP_LEFT); val tr = cut(android.view.RoundedCorner.POSITION_TOP_RIGHT)
            val bl = cut(android.view.RoundedCorner.POSITION_BOTTOM_LEFT); val br = cut(android.view.RoundedCorner.POSITION_BOTTOM_RIGHT)
            val corner = maxOf(maxOf(tl, tr), maxOf(bl, br))
            for (i in 0..3) b[i] += corner
        }
        if (Build.VERSION.SDK_INT >= 28) insets.displayCutout?.let {
            b[0] = maxOf(b[0], it.safeInsetLeft + extra); b[1] = maxOf(b[1], it.safeInsetTop + extra)
            b[2] = maxOf(b[2], it.safeInsetRight + extra); b[3] = maxOf(b[3], it.safeInsetBottom + extra)
        }
        return b
    }

    /** The Exit button in its corner, clear of a camera cutout and the screen's rounded corner. */
    private fun placeExit(insets: WindowInsets? = root.rootWindowInsets) {
        var l = 0; var t = 0; var r = 0; var b = 0
        if (insets != null && Build.VERSION.SDK_INT >= 28) insets.displayCutout?.let { l = it.safeInsetLeft; t = it.safeInsetTop; r = it.safeInsetRight; b = it.safeInsetBottom }
        if (insets != null && Build.VERSION.SDK_INT >= 31) {
            val c = ((insets.getRoundedCorner(android.view.RoundedCorner.POSITION_TOP_LEFT)?.radius ?: 0) * 0.30f).toInt()
            l += c; t += c; r += c; b += c
        }
        val m = dp(10)
        val corner = prefs.exitCorner
        exit.layoutParams = (exit.layoutParams as FrameLayout.LayoutParams).apply {
            gravity = when (corner) {
                Corner.TOP_LEFT -> Gravity.TOP or Gravity.START
                Corner.TOP_RIGHT -> Gravity.TOP or Gravity.END
                Corner.BOTTOM_LEFT -> Gravity.BOTTOM or Gravity.START
                Corner.BOTTOM_RIGHT -> Gravity.BOTTOM or Gravity.END
            }
            setMargins(m + l, m + t, m + r, m + b)
        }
    }

    @Suppress("DEPRECATION")
    private fun setFullScreen(on: Boolean) {
        fullScreen = on
        bar.visibility = if (on) View.GONE else View.VISIBLE
        showChips()
        exit.visibility = if (on) View.VISIBLE else View.GONE
        if (Build.VERSION.SDK_INT >= 28) window.attributes = window.attributes.apply {
            layoutInDisplayCutoutMode = if (on) WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES else WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_DEFAULT
        }
        if (Build.VERSION.SDK_INT >= 30) {
            window.setDecorFitsSystemWindows(false)
            window.insetsController?.let {
                if (on) {
                    it.hide(WindowInsets.Type.systemBars())
                    it.systemBarsBehavior = WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
                } else it.show(WindowInsets.Type.systemBars())
            }
        } else {
            window.decorView.systemUiVisibility = View.SYSTEM_UI_FLAG_LAYOUT_STABLE or View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN or View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION or
                if (on) View.SYSTEM_UI_FLAG_FULLSCREEN or View.SYSTEM_UI_FLAG_HIDE_NAVIGATION or View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY else 0
        }
        root.requestApplyInsets()
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        // (older Androids show the bars again after a dialog)
        if (hasFocus && fullScreen) setFullScreen(true)
    }

    @Deprecated("Deprecated in Java")
    override fun onBackPressed() {
        if (bigButtPanel.visibility == View.VISIBLE) closeBigButts() else if (fullScreen) setFullScreen(false) else @Suppress("DEPRECATION") super.onBackPressed()
    }

    private fun menu(anchor: View) {
        val m = PopupMenu(this, anchor)
        m.menu.add(0, 1, 0, "BigButts…")
        m.menu.add(0, 2, 0, "Full screen")
        m.menu.add(0, 3, 0, "Settings")
        m.menu.add(0, 4, 0, "Disconnect")
        m.setOnMenuItemClickListener {
            when (it.itemId) {
                1 -> pickBigButt()
                2 -> setFullScreen(true)
                3 -> startActivity(Intent(this, SettingsActivity::class.java))
                4 -> finish()
            }
            true
        }
        m.show()
    }

    /** Every BigButt with its ButtScreens; picking one uses it on the PC (as picking it in the tab). */
    /**
     * The BigButts screen: every BigButt on the PC, a card each with its ButtScreens, the one in use marked; tapping one
     * uses it on the PC. Shown after connecting (Settings can leave that out) and from BigButts at the top.
     */
    private fun pickBigButt() {
        val s = state ?: return
        bigButtList.removeAllViews()
        bigButtList.addView(text("BigButts on ${s.pc}", 22f, bold = true))
        bigButtList.addView(text("Pick the one to use now. A BigButt's game starting on the PC switches to it by itself.", 14f, Ui.SECONDARY).apply { setPadding(0, dp(4), 0, dp(12)) })
        if (s.bigButts.isEmpty()) bigButtList.addView(text("${s.pc} has no BigButts yet.", 15f, Ui.SECONDARY))
        for (b in s.bigButts) {
            val on = b.name.equals(s.bigButt, ignoreCase = true)
            val names = b.screens.mapNotNull { s.screens[it]?.name }
            val row = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
                background = Ui.rounded(Ui.CARD, dp(8).toFloat(), if (on) Ui.ACCENT else Ui.CARD_BORDER, dp(if (on) 2 else 1))
                setPadding(dp(16), dp(14), dp(16), dp(14))
                isClickable = true
                foreground = getDrawable(android.R.drawable.list_selector_background)
                contentDescription = "Use BigButt ${b.name}"
                setOnClickListener {
                    if (!on) remote?.send(JSONObject().put("op", "use").put("bigButt", b.name))
                    closeBigButts()
                }
            }
            val info = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
            info.addView(text(b.name, 18f, bold = true))
            info.addView(text(if (names.isEmpty()) "No ButtScreens" else names.joinToString(" · "), 14f, Ui.SECONDARY).apply { setPadding(0, dp(2), 0, 0) })
            row.addView(info, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
            if (on) row.addView(text("In use", 13f, Color.BLACK, bold = true).apply {
                background = Ui.rounded(Ui.ACCENT, dp(12).toFloat())
                setPadding(dp(10), dp(4), dp(10), dp(4))
            })
            bigButtList.addView(row, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { bottomMargin = dp(8) })
        }
        bigButtList.addView(button("Close") { closeBigButts() }, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(8) })
        bigButtPanel.visibility = View.VISIBLE
        // (no ButtScreen is picked yet: the list follows how the device is held, rather than the ButtScreen behind it)
        requestedOrientation = ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED
        screen.letGoAll()
    }

    private fun closeBigButts() {
        bigButtPanel.visibility = View.GONE
        screen.layout?.let { turnToFit(it) }
    }

    // ---- what the PC says ----

    override fun onConnecting(attempt: Int, lastError: String?) {
        connected = false
        showChips()
        screen.letGoAll()
        if (attempt == 1 && lastError == null) showMessage("Connecting to ${pc.name}…")
        else showMessage("Connecting to ${pc.name} again…\n\n$lastError", "Edit this PC" to { editPc() }, "Back" to { finish() })
        render()
    }

    override fun onConnected(pcName: String) {
        connected = true
        showMessage("Connected to $pcName: waiting for its ButtScreens…")
        remote?.send(JSONObject().put("op", "state"))
    }

    override fun onFailed(message: String) {
        connected = false
        showChips()
        showMessage(message, "Edit this PC" to { editPc() }, "Try again" to { connect() })
        render()
    }

    override fun onMessage(m: JSONObject) {
        when (m.optString("op")) {
            "state" -> applyState(State.parse(m))
            "screen" -> {
                val l = try { Layout.parse(m) } catch (e: Exception) { return }
                layouts[l.id] = l
                if (l.id == showing()) show()
            }
            "show" -> {
                val id = m.optString("screen")
                val i = pages.indexOf(id)
                if (i >= 0) { index = i; shown = null } else shown = id
                fetch(id)
                show(announce = true)
            }
            "error" -> if (m.optString("message").isNotEmpty()) Toast.makeText(this, m.optString("message"), Toast.LENGTH_LONG).show()
        }
    }

    private fun applyState(s: State) {
        val old = state
        state = s
        if (old == null || old.current != s.current || !old.bigButt.equals(s.bigButt, ignoreCase = true)) {
            // another BigButt (or its ButtScreens changed): start again at its first
            val announce = old != null && !old.bigButt.equals(s.bigButt, ignoreCase = true)
            pages = s.current
            index = 0
            shown = null
            if (announce) flash(s.currentName)
        }
        if (shown != null && shown !in s.screens) shown = null
        for (id in pages) fetch(id)
        shown?.let { fetch(it) }
        if (!askedBigButt) {
            askedBigButt = true
            if (prefs.chooseBigButt && s.bigButts.size > 1) pickBigButt()
        }
        else if (bigButtPanel.visibility == View.VISIBLE) pickBigButt() // (kept up to date while it's open)
        show()
        showChips()
    }

    /** The BigButts row: shown while connected and not full screen. */
    private fun showChips() {
        val s = state
        chipsBox.visibility = if (s != null && s.bigButts.isNotEmpty() && connected && !fullScreen) View.VISIBLE else View.GONE
        if (s == null) return
        chips.removeAllViews()
        for (b in s.bigButts) {
            val on = b.name.equals(s.bigButt, ignoreCase = true)
            chips.addView(TextView(this).apply {
                text = b.name
                textSize = 14f
                setTextColor(if (on) Color.BLACK else Ui.TEXT)
                if (on) setTypeface(typeface, android.graphics.Typeface.BOLD)
                background = Ui.rounded(if (on) Ui.ACCENT else Ui.CONTROL, dp(16).toFloat(), if (on) Ui.ACCENT else Ui.CONTROL_BORDER, dp(1))
                setPadding(dp(14), dp(7), dp(14), dp(7))
                contentDescription = "BigButt ${b.name}" + if (on) ", in use" else ""
                setOnClickListener { if (!on) remote?.send(JSONObject().put("op", "use").put("bigButt", b.name)) }
            }, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { marginEnd = dp(6) })
        }
    }

    /** Asks for a ButtScreen's layout if ours isn't its latest version. */
    private fun fetch(id: String) {
        val v = state?.screens?.get(id)?.version ?: return
        if (layouts[id]?.version == v || asked[id] == v) return
        asked[id] = v
        remote?.send(JSONObject().put("op", "get").put("id", id))
    }

    private fun showing(): String? = shown ?: pages.getOrNull(index)

    private fun show(announce: Boolean = false) {
        val s = state ?: return
        val id = showing()
        if (id == null) {
            screen.layout = null
            showMessage(if (s.bigButts.isEmpty()) "${s.pc} has no BigButts yet."
                else "${s.currentName} has no ButtScreens yet. On ${s.pc}: Gaming → Button2Key Remote, tick ButtScreens for it under Its ButtScreens.",
                "BigButts…" to { pickBigButt() })
        } else {
            val l = layouts[id]
            if (l == null) {
                showMessage("Getting ${s.screens[id]?.name ?: "the ButtScreen"}…")
            } else {
                val changed = screen.layout?.id != l.id
                screen.layout = l
                hideMessage()
                turnToFit(l)
                if (changed && (announce || pages.size > 1)) flash(l.name)
            }
        }
        render()
    }

    private fun turnToFit(l: Layout) {
        requestedOrientation = when {
            !prefs.turnToFit || bigButtPanel.visibility == View.VISIBLE -> ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED
            l.width > l.height * 1.05f -> ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
            l.height > l.width * 1.05f -> ActivityInfo.SCREEN_ORIENTATION_SENSOR_PORTRAIT
            else -> ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED
        }
    }

    /** The bar's text: the BigButt, then the PC and which ButtScreen of how many. */
    private fun render() {
        val s = state
        title.text = s?.currentName?.ifEmpty { null } ?: pc.name
        val where = when {
            !connected -> "${pc.name} · not connected"
            s == null -> pc.name
            shown != null -> "${s.pc} · ${s.screens[shown!!]?.name ?: ""}"
            pages.isEmpty() -> "${s.pc} · no ButtScreens"
            else -> "${s.pc} · ${index + 1} of ${pages.size}: ${s.screens[pages[index]]?.name ?: ""}"
        }
        subtitle.text = where
        val many = pages.size > 1 || (shown != null && pages.isNotEmpty())
        prev.alpha = if (many) 1f else 0.3f
        next.alpha = if (many) 1f else 0.3f
    }

    private fun flash(text: String) {
        if (!prefs.showPageName || text.isEmpty()) return
        pageName.text = text
        pageName.animate().cancel()
        pageName.alpha = 1f
        main.removeCallbacks(hidePageName)
        main.postDelayed(hidePageName, 1200)
    }

    private fun showMessage(text: String, vararg buttons: Pair<String, () -> Unit>) {
        messageText.text = text
        messageButtons.removeAllViews()
        for ((label, action) in buttons) messageButtons.addView(button(label) { action() }, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { marginStart = dp(4); marginEnd = dp(4) })
        messageButtons.visibility = if (buttons.isEmpty()) View.GONE else View.VISIBLE
        message.visibility = View.VISIBLE
        // with a ButtScreen behind it, it's only drawn dimmed while not connected
        screen.alpha = if (connected) 1f else 0.35f
    }

    private fun hideMessage() {
        message.visibility = View.GONE
        screen.alpha = 1f
    }

    private fun editPc() {
        // the PCs' list edits it (opening its window); connecting again uses what's saved
        startActivity(Intent(this, MainActivity::class.java).putExtra(MainActivity.EXTRA_EDIT, pc.id)
            .addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP))
        finish()
    }

    // ---- the ButtScreen's presses and swipes ----

    override fun onDown(screen: String, button: String) {
        remote?.send(JSONObject().put("op", "down").put("screen", screen).put("button", button))
    }

    override fun onUp(screen: String, button: String) {
        remote?.send(JSONObject().put("op", "up").put("screen", screen).put("button", button))
    }

    override fun onSwipe(direction: Int) {
        if (pages.isEmpty()) return
        if (shown != null) shown = null
        else {
            if (pages.size < 2) return
            index = (index + direction + pages.size) % pages.size
        }
        fetch(pages[index])
        show(announce = true)
    }
}
