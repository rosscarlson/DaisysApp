package com.jasbone.butt2keyremote

import android.content.Context
import android.content.SharedPreferences
import android.os.Build
import android.provider.Settings
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import org.json.JSONArray
import org.json.JSONObject
import java.security.KeyStore
import java.util.UUID
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/** A PC to connect to: Daisy's App with Button2Key Remote on. */
data class Pc(
    val id: String = UUID.randomUUID().toString(),
    val name: String,
    val host: String,
    val port: Int = DEFAULT_PORT,
    val passphrase: String,
) {
    companion object { const val DEFAULT_PORT = 38870 }
}

enum class Corner(val label: String) {
    TOP_LEFT("Top left"), TOP_RIGHT("Top right"), BOTTOM_LEFT("Bottom left"), BOTTOM_RIGHT("Bottom right")
}

/** The PCs and the settings, in SharedPreferences; passphrases encrypted with a key kept in the Android Keystore. */
class Prefs(context: Context) {
    private val app = context.applicationContext
    private val p: SharedPreferences = app.getSharedPreferences("butt", Context.MODE_PRIVATE)

    var pcs: List<Pc>
        get() = try {
            JSONArray(p.getString("pcs", "[]")).objects().map {
                Pc(it.optString("id"), it.optString("name"), it.optString("host"), it.optInt("port", Pc.DEFAULT_PORT), Secret.open(it.optString("passphrase")))
            }
        } catch (_: Exception) { emptyList() }
        set(value) {
            val a = JSONArray()
            for (pc in value) a.put(JSONObject().put("id", pc.id).put("name", pc.name).put("host", pc.host).put("port", pc.port).put("passphrase", Secret.seal(pc.passphrase)))
            p.edit().putString("pcs", a.toString()).apply()
        }

    fun save(pc: Pc) {
        val list = pcs.toMutableList()
        val i = list.indexOfFirst { it.id == pc.id }
        if (i >= 0) list[i] = pc else list.add(pc)
        pcs = list
    }

    fun delete(id: String) { pcs = pcs.filter { it.id != id } }

    fun pc(id: String?): Pc? = pcs.firstOrNull { it.id == id }

    var lastPc: String?
        get() = p.getString("lastPc", null)
        set(v) = p.edit().putString("lastPc", v).apply()

    /** The name the PC knows this device by (in its Devices list and the editor's sizes). */
    var deviceName: String
        get() = p.getString("deviceName", null)?.ifBlank { null } ?: defaultDeviceName()
        set(v) = p.edit().putString("deviceName", v.trim()).apply()

    fun defaultDeviceName(): String {
        val own = if (Build.VERSION.SDK_INT >= 25) try { Settings.Global.getString(app.contentResolver, Settings.Global.DEVICE_NAME) } catch (_: Exception) { null } else null
        if (!own.isNullOrBlank()) return own
        val model = Build.MODEL ?: "Android"
        val maker = Build.MANUFACTURER ?: ""
        return if (maker.isNotEmpty() && !model.startsWith(maker, ignoreCase = true)) "${maker.replaceFirstChar { it.uppercase() }} $model" else model
    }

    var startFullScreen by flag("startFullScreen", false)
    var exitCorner: Corner
        get() = try { Corner.valueOf(p.getString("exitCorner", Corner.TOP_RIGHT.name)!!) } catch (_: Exception) { Corner.TOP_RIGHT }
        set(v) = p.edit().putString("exitCorner", v.name).apply()
    var exitLongPress by flag("exitLongPress", false)
    /** Full screen's border round the ButtScreen beyond what the screen's corners and camera need, in dp. */
    var fullScreenBorder: Int
        get() = p.getInt("fullScreenBorder", 8)
        set(v) = p.edit().putInt("fullScreenBorder", v).apply()
    var checkUpdates by flag("checkUpdates", true)
    var chooseBigButt by flag("chooseBigButt", true)
    var turnToFit by flag("turnToFit", true)
    var keepScreenOn by flag("keepScreenOn", true)
    var vibrate by flag("vibrate", true)
    var autoConnect by flag("autoConnect", true)
    var showPageName by flag("showPageName", true)

    private fun flag(key: String, default: Boolean) = object : kotlin.properties.ReadWriteProperty<Any?, Boolean> {
        override fun getValue(thisRef: Any?, property: kotlin.reflect.KProperty<*>) = p.getBoolean(key, default)
        override fun setValue(thisRef: Any?, property: kotlin.reflect.KProperty<*>, value: Boolean) = p.edit().putBoolean(key, value).apply()
    }
}

/** Passphrases at rest: AES-GCM under a key that never leaves the Keystore ("k:" + base64 of IV and ciphertext). */
private object Secret {
    private const val ALIAS = "butt-passphrases"

    private fun key(): SecretKey {
        val ks = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (ks.getKey(ALIAS, null) as? SecretKey)?.let { return it }
        val g = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        g.init(KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build())
        return g.generateKey()
    }

    fun seal(text: String): String = try {
        val c = Cipher.getInstance("AES/GCM/NoPadding")
        c.init(Cipher.ENCRYPT_MODE, key())
        val sealed = c.doFinal(text.toByteArray(Charsets.UTF_8))
        "k:" + Base64.encodeToString(c.iv + sealed, Base64.NO_WRAP)
    } catch (_: Exception) {
        "p:$text" // (a Keystore that doesn't work: kept as it is, private to the app)
    }

    fun open(stored: String): String = when {
        stored.startsWith("k:") -> try {
            val all = Base64.decode(stored.substring(2), Base64.NO_WRAP)
            val c = Cipher.getInstance("AES/GCM/NoPadding")
            c.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, all, 0, 12))
            String(c.doFinal(all, 12, all.size - 12), Charsets.UTF_8)
        } catch (_: Exception) { "" }
        stored.startsWith("p:") -> stored.substring(2)
        else -> stored
    }
}
