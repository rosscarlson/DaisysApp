package com.jasbone.butt2keyremote

import android.app.Activity
import android.app.AlertDialog
import android.content.Context
import android.content.Intent
import android.graphics.Point
import android.os.Build
import android.os.Bundle
import android.text.InputType
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.widget.CheckBox
import android.widget.LinearLayout
import android.widget.ScrollView
import com.jasbone.butt2keyremote.Ui.button
import com.jasbone.butt2keyremote.Ui.card
import com.jasbone.butt2keyremote.Ui.confirmButton
import com.jasbone.butt2keyremote.Ui.dp
import com.jasbone.butt2keyremote.Ui.field
import com.jasbone.butt2keyremote.Ui.heading
import com.jasbone.butt2keyremote.Ui.matchWrap
import com.jasbone.butt2keyremote.Ui.row
import com.jasbone.butt2keyremote.Ui.text
import org.json.JSONObject

/** The PCs: pick one to connect, add one (typed in, or found on the network), edit or delete one; and Settings. */
class MainActivity : Activity() {
    private lateinit var prefs: Prefs
    private lateinit var list: LinearLayout
    private lateinit var foundBox: LinearLayout
    private var finding = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        prefs = Prefs(this)
        val scroll = ScrollView(this).apply { isFillViewport = true }
        val col = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(16), dp(12), dp(16), dp(24))
        }
        scroll.addView(col)
        fitSystemBars(scroll)

        col.addView(text(getString(R.string.app_name), 26f, bold = true).apply { setPadding(dp(4), dp(8), 0, 0) })
        col.addView(text("Screens of buttons for Daisy's App on your PC", 14f, Ui.SECONDARY).apply { setPadding(dp(4), dp(2), 0, 0) })

        col.addView(heading("Your PCs"))
        list = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        col.addView(list, matchWrap())

        val actions = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        actions.addView(button("Find PCs on the network") { find() }, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply { marginEnd = dp(8) })
        actions.addView(button("Add a PC", accent = true) { edit(null) }, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        col.addView(actions, matchWrap(dp(12)))
        foundBox = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        col.addView(foundBox, matchWrap(dp(4)))

        col.addView(heading("This device"))
        col.addView(button("Settings") { startActivity(Intent(this, SettingsActivity::class.java)) }, matchWrap())
        setContentView(scroll)

        // straight back to the PC used last, when the app starts (not when coming back from it)
        if (savedInstanceState == null && prefs.autoConnect) prefs.pc(prefs.lastPc)?.let { connect(it) }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        // "Edit this PC" from a connection that failed
        intent.getStringExtra(EXTRA_EDIT)?.let { id -> prefs.pc(id)?.let { edit(it) } }
    }

    override fun onResume() {
        super.onResume()
        render()
    }

    private fun render() {
        list.removeAllViews()
        val pcs = prefs.pcs
        if (pcs.isEmpty()) {
            list.addView(card().apply {
                addView(text("No PCs yet.", bold = true))
                addView(text("On the PC: Daisy's App → Gaming → Button2Key Remote: switch it On and set a passphrase under Devices. " +
                    "Then Find PCs on the network here, or Add a PC with its address (shown there too).", 14f, Ui.SECONDARY).apply { setPadding(0, dp(6), 0, 0) })
            }, matchWrap())
            return
        }
        for (pc in pcs) {
            val c = card().apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
                isClickable = true
                isFocusable = true
                foreground = getDrawable(android.R.drawable.list_selector_background)
                setOnClickListener { connect(pc) }
            }
            val info = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
            info.addView(text(pc.name, 17f, bold = true))
            info.addView(text("${pc.host} · port ${pc.port}" + if (pc.id == prefs.lastPc) " · used last" else "", 13f, Ui.SECONDARY))
            c.addView(info, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
            c.addView(button("Edit") { edit(pc) })
            list.addView(c, matchWrap(if (list.childCount > 0) dp(8) else 0))
        }
    }

    private fun connect(pc: Pc) {
        prefs.lastPc = pc.id
        startActivity(Intent(this, RemoteActivity::class.java).putExtra(RemoteActivity.EXTRA_PC, pc.id))
    }

    private fun find() {
        if (finding) return
        finding = true
        foundBox.removeAllViews()
        foundBox.addView(text("Looking for PCs with Button2Key Remote on…", 14f, Ui.SECONDARY).apply { setPadding(dp(4), dp(8), 0, 0) })
        val ports = (prefs.pcs.map { it.port } + Pc.DEFAULT_PORT).distinct()
        Thread {
            val found = ports.flatMap { Discovery.find(this, it) }.distinctBy { it.host + ":" + it.port }
            runOnUiThread {
                finding = false
                foundBox.removeAllViews()
                if (found.isEmpty()) {
                    foundBox.addView(text("None found. Is Button2Key Remote On (on the PC: Gaming → Button2Key Remote), and this device on the same Wi-Fi? " +
                        "Windows' firewall may need to let Daisy's App in on private networks. You can also Add a PC by its address.", 14f, Ui.ERROR_TEXT).apply { setPadding(dp(4), dp(8), 0, 0) })
                    return@runOnUiThread
                }
                for (f in found) {
                    val known = prefs.pcs.firstOrNull { it.host == f.host && it.port == f.port }
                    val c = card().apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
                    val info = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
                    info.addView(text(f.name, 16f, bold = true))
                    info.addView(text("${f.host} · port ${f.port}" + if (known != null) " · saved as ${known.name}" else "", 13f, Ui.SECONDARY))
                    c.addView(info, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
                    if (known != null) c.addView(button("Connect", accent = true) { connect(known) })
                    else c.addView(button("Add", accent = true) { edit(Pc(name = f.name, host = f.host, port = f.port, passphrase = ""), isNew = true) })
                    foundBox.addView(c, matchWrap(dp(8)))
                }
            }
        }.start()
    }

    /** Add a PC (null, or one found: [isNew]) or edit one: Name, IP Address, Port, Passphrase; Test; Delete. */
    private fun edit(existing: Pc?, isNew: Boolean = existing == null) {
        val col = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(20), dp(8), dp(20), 0)
        }
        val name = field("PC's name", existing?.name ?: "")
        val host = field("192.168.1.20", existing?.host ?: "", InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_URI)
        val port = field("${Pc.DEFAULT_PORT}", (existing?.port ?: Pc.DEFAULT_PORT).toString(), InputType.TYPE_CLASS_NUMBER)
        val pass = field("8 characters or more", existing?.passphrase ?: "", InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD)
        col.addView(row("Name", name, 100))
        col.addView(row("IP Address", host, 100))
        col.addView(row("Port", port, 100))
        col.addView(row("Passphrase", pass, 100))
        val show = CheckBox(this).apply {
            text = "Show the passphrase"
            setTextColor(Ui.SECONDARY)
            setOnCheckedChangeListener { _, on ->
                pass.inputType = InputType.TYPE_CLASS_TEXT or if (on) InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD else InputType.TYPE_TEXT_VARIATION_PASSWORD
                pass.typeface = android.graphics.Typeface.DEFAULT
                pass.setSelection(pass.text.length)
            }
        }
        pass.typeface = android.graphics.Typeface.DEFAULT
        col.addView(show, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { marginStart = dp(112) })
        val note = text("The passphrase is the one set on the PC: Gaming → Button2Key Remote → Devices.", 13f, Ui.SECONDARY).apply { setPadding(0, dp(6), 0, dp(4)) }
        col.addView(note)

        fun read(): Pc? {
            val h = host.text.toString().trim()
            val p = port.text.toString().trim().toIntOrNull()
            val error = when {
                h.isEmpty() -> "Type the PC's IP address (or its name on the network)."
                p == null || p !in 1..65535 -> "The port is a number from 1 to 65535 (38870 unless it was changed on the PC)."
                pass.text.length < 8 -> "The passphrase is 8 characters or more, the same as on the PC."
                else -> null
            }
            if (error != null) { note.text = error; note.setTextColor(Ui.ERROR_TEXT); return null }
            return Pc(id = if (isNew) java.util.UUID.randomUUID().toString() else existing!!.id,
                name = name.text.toString().trim().ifEmpty { h }, host = h, port = p!!, passphrase = pass.text.toString())
        }

        val dialog = AlertDialog.Builder(this)
            .setTitle(if (isNew) "Add a PC" else "Edit ${existing!!.name}")
            .setView(ScrollView(this).apply { addView(col) })
            .setPositiveButton("Save", null)
            .setNegativeButton("Cancel", null)
            .setNeutralButton("Test", null)
            .create()
        if (!isNew) {
            col.addView(confirmButton("Delete this PC") {
                prefs.delete(existing!!.id)
                if (prefs.lastPc == existing.id) prefs.lastPc = null
                dialog.dismiss()
                render()
            }, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(8); bottomMargin = dp(8) })
        }
        dialog.setOnShowListener {
            dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener {
                val pc = read() ?: return@setOnClickListener
                prefs.save(pc)
                dialog.dismiss()
                render()
                if (isNew) foundBox.removeAllViews()
            }
            dialog.getButton(AlertDialog.BUTTON_NEUTRAL).setOnClickListener {
                val pc = read() ?: return@setOnClickListener
                note.text = "Connecting to ${pc.host}…"
                note.setTextColor(Ui.SECONDARY)
                Thread {
                    val result = try {
                        val w = Wire.connect(pc.host, pc.port, pc.passphrase, hello(this))
                        w.close()
                        Pair(true, "Connected to ${w.pc}: it works.")
                    } catch (e: WrongPassphraseException) {
                        Pair(false, "That PC answered, but the passphrase isn't the one set there.")
                    } catch (e: Exception) {
                        Pair(false, "Couldn't connect: " + (e.message ?: e.javaClass.simpleName) + ". Is Button2Key Remote On, and this device on the same network?")
                    }
                    runOnUiThread {
                        note.text = result.second
                        note.setTextColor(if (result.first) Ui.SUCCESS else Ui.ERROR_TEXT)
                        if (result.first && name.text.isBlank()) name.setText(result.second.removePrefix("Connected to ").substringBefore(':'))
                    }
                }.start()
            }
        }
        // (the window shrinks above the keyboard, so Test, Cancel and Save stay in reach)
        @Suppress("DEPRECATION")
        dialog.window?.setSoftInputMode(android.view.WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE)
        dialog.show()
    }

    companion object {
        const val EXTRA_EDIT = "edit"

        /** The hello: this device's name, Android version, the app, and its screen in pixels the way it's held. */
        fun hello(context: Context): JSONObject {
            val size = screenSize(context)
            val version = try { context.packageManager.getPackageInfo(context.packageName, 0).versionName } catch (_: Exception) { "1" }
            return JSONObject()
                .put("op", "hello")
                .put("device", Prefs(context).deviceName)
                .put("platform", "Android ${Build.VERSION.RELEASE}")
                .put("app", "Button2Key Remote $version")
                .put("width", size.x)
                .put("height", size.y)
        }

        /** The whole screen in pixels (as full screen uses it), the way it's held now. */
        @Suppress("DEPRECATION")
        fun screenSize(context: Context): Point {
            val wm = context.getSystemService(Context.WINDOW_SERVICE) as android.view.WindowManager
            return if (Build.VERSION.SDK_INT >= 30) {
                val b = wm.maximumWindowMetrics.bounds
                Point(b.width(), b.height())
            } else Point().also { wm.defaultDisplay.getRealSize(it) }
        }

        /** Pads a view clear of the status and navigation bars (Android draws apps edge to edge since 15). */
        @Suppress("DEPRECATION")
        fun fitSystemBars(v: View) {
            v.setOnApplyWindowInsetsListener { view, insets ->
                if (Build.VERSION.SDK_INT >= 30) {
                    val i = insets.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout() or WindowInsets.Type.ime())
                    view.setPadding(i.left, i.top, i.right, i.bottom)
                } else view.setPadding(insets.systemWindowInsetLeft, insets.systemWindowInsetTop, insets.systemWindowInsetRight, insets.systemWindowInsetBottom)
                insets
            }
        }
    }
}
