package com.jasbone.butt2keyremote

import android.app.Activity
import android.os.Bundle
import android.text.Editable
import android.text.TextWatcher
import android.view.Gravity
import android.view.ViewGroup
import android.widget.LinearLayout
import android.widget.RadioButton
import android.widget.RadioGroup
import android.widget.ScrollView
import android.widget.Switch
import com.jasbone.butt2keyremote.Ui.card
import com.jasbone.butt2keyremote.Ui.dp
import com.jasbone.butt2keyremote.Ui.field
import com.jasbone.butt2keyremote.Ui.heading
import com.jasbone.butt2keyremote.Ui.matchWrap
import com.jasbone.butt2keyremote.Ui.text

/** This device's settings: its name, full screen and its Exit button's corner, and how pressing feels. Saved as they change. */
class SettingsActivity : Activity() {
    private lateinit var prefs: Prefs

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        prefs = Prefs(this)
        val scroll = ScrollView(this)
        val col = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(16), dp(12), dp(16), dp(24))
        }
        scroll.addView(col)
        MainActivity.fitSystemBars(scroll)
        col.addView(text("Settings", 26f, bold = true).apply { setPadding(dp(4), dp(8), 0, 0) })

        col.addView(heading("This device"))
        col.addView(card().apply {
            addView(text("Name", 15f, bold = true))
            val name = field(prefs.defaultDeviceName(), prefs.deviceName.takeIf { it != prefs.defaultDeviceName() } ?: "")
            name.addTextChangedListener(object : TextWatcher {
                override fun beforeTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) {}
                override fun onTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) {}
                override fun afterTextChanged(s: Editable?) { prefs.deviceName = s.toString() }
            })
            addView(name, matchWrap(dp(8)))
            addView(secondary("What the PC calls this device (its Devices list, and the editor's sizes). Empty: the device's own name."))
        }, matchWrap())

        col.addView(heading("Full screen"))
        col.addView(card().apply {
            addView(switch("Start in full screen", prefs.startFullScreen) { prefs.startFullScreen = it })
            addView(secondary("Full screen shows only the ButtScreen, with a small Exit button in a corner. (⛶ at the top goes to full screen any time.)"))
            addView(text("The Exit button's corner", 15f, bold = true).apply { setPadding(0, dp(14), 0, dp(4)) })
            val corners = RadioGroup(this@SettingsActivity).apply { orientation = RadioGroup.VERTICAL }
            for (c in Corner.entries) corners.addView(RadioButton(this@SettingsActivity).apply {
                id = 100 + c.ordinal
                text = c.label
                setTextColor(Ui.TEXT)
                isChecked = prefs.exitCorner == c
            })
            corners.setOnCheckedChangeListener { _, id -> prefs.exitCorner = Corner.entries[id - 100] }
            addView(corners)
            addView(switch("Exit needs a long press", prefs.exitLongPress) { prefs.exitLongPress = it })
            addView(secondary("So a tap in the corner during a game doesn't leave full screen."))
            addView(text("Border round the buttons", 15f, bold = true).apply { setPadding(0, dp(14), 0, dp(4)) })
            val sizes = listOf(0 to "None", 8 to "Small", 16 to "Medium", 32 to "Large")
            val border = RadioGroup(this@SettingsActivity).apply { orientation = RadioGroup.HORIZONTAL }
            for ((dpSize, label) in sizes) border.addView(RadioButton(this@SettingsActivity).apply {
                id = 200 + dpSize
                text = label
                setTextColor(Ui.TEXT)
                isChecked = prefs.fullScreenBorder == dpSize
                setPadding(0, 0, dp(16), 0)
            })
            border.setOnCheckedChangeListener { _, id -> prefs.fullScreenBorder = id - 200 }
            addView(border)
            addView(secondary("Full screen keeps the buttons clear of the screen's rounded corners and camera by itself; this adds a margin all round."))
        }, matchWrap())

        col.addView(heading("ButtScreens"))
        col.addView(card().apply {
            addView(switch("Turn the screen to fit each ButtScreen", prefs.turnToFit) { prefs.turnToFit = it })
            addView(secondary("A ButtScreen made wide shows in landscape, a tall one in portrait. Off: the screen turns as the device is held."))
            addView(switch("Show a ButtScreen's name when it changes", prefs.showPageName) { prefs.showPageName = it })
            addView(switch("Vibrate when a button is pressed", prefs.vibrate) { prefs.vibrate = it })
            addView(switch("Keep the screen on while connected", prefs.keepScreenOn) { prefs.keepScreenOn = it })
            addView(switch("Connect to the PC used last when the app starts", prefs.autoConnect) { prefs.autoConnect = it })
            addView(switch("Show the BigButts to pick from after connecting", prefs.chooseBigButt) { prefs.chooseBigButt = it })
            if (!Updater.embedded(this@SettingsActivity)) addView(switch("Check for updates when the app starts", prefs.checkUpdates) { prefs.checkUpdates = it })
        }, matchWrap())

        col.addView(heading("About"))
        col.addView(card().apply {
            val v = try { packageManager.getPackageInfo(packageName, 0).versionName } catch (_: Exception) { "" }
            addView(text("${getString(R.string.app_name)} $v", 15f, bold = true))
            addView(secondary("For Daisy's App's Button2Key Remote (Gaming tab), protocol version ${Wire.VERSION}. " +
                "Swipe between buttons to go through the BigButt's ButtScreens; tap the BigButt's name at the top to pick another."))
        }, matchWrap())
        setContentView(scroll)
    }

    private fun secondary(s: String) = text(s, 13f, Ui.SECONDARY).apply { setPadding(0, dp(4), 0, dp(4)) }

    @Suppress("DEPRECATION")
    private fun switch(label: String, on: Boolean, changed: (Boolean) -> Unit) = Switch(this).apply {
        text = label
        textSize = 15f
        setTextColor(Ui.TEXT)
        isChecked = on
        gravity = Gravity.CENTER_VERTICAL
        minHeight = dp(44)
        layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
        setOnCheckedChangeListener { _, v -> changed(v) }
    }
}
