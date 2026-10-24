package com.jasbone.butt2keyremote

import android.app.Activity
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageInstaller
import android.net.Uri
import android.os.Build
import android.provider.Settings
import android.widget.Toast
import org.json.JSONArray
import java.io.File
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest

/**
 * Updates from inside the app: the APK rides on Daisy's App's GitHub releases (Button2Key-Remote-<version>.apk), so
 * [check] looks through the newest releases for the first with one; [download] fetches it (checked against the
 * SHA-256 GitHub gives, when it gives one) and [install] hands it to Android's PackageInstaller, which asks the user
 * to confirm. Android only installs it over this one if it's signed with the same key.
 */
object Updater {
    private const val RELEASES = "https://api.github.com/repos/rosscarlson/DaisysApp/releases?per_page=20"
    private val APK = Regex("""^Button2Key-Remote-(\d+(?:\.\d+)*)\.apk$""")

    data class Update(val version: String, val url: String, val size: Long, val sha256: String?, val page: String)

    /**
     * Built into another app (Daisy's App Mobile links this code in: see the README): that app updates itself, and
     * this one's updates are left out.
     */
    fun embedded(context: Context) = context.packageName != "com.jasbone.butt2keyremote"

    fun installedVersion(context: Context): String =
        try { context.packageManager.getPackageInfo(context.packageName, 0).versionName ?: "0" } catch (_: Exception) { "0" }

    /** The newest APK on the releases if it's newer than this one, else null. Blocks: not on the main thread. */
    fun check(context: Context): Update? {
        val c = open(RELEASES)
        c.setRequestProperty("Accept", "application/vnd.github+json")
        val releases = c.inputStream.use { JSONArray(it.readBytes().toString(Charsets.UTF_8)) }
        for (r in releases.objects()) {
            if (r.optBoolean("draft") || r.optBoolean("prerelease")) continue
            for (a in r.optJSONArray("assets").objects()) {
                val m = APK.matchEntire(a.optString("name")) ?: continue
                val version = m.groupValues[1]
                if (compare(version, installedVersion(context)) <= 0) return null
                val digest = a.optString("digest").takeIf { it.startsWith("sha256:") }?.removePrefix("sha256:")
                return Update(version, a.optString("browser_download_url"), a.optLong("size"), digest, r.optString("html_url"))
            }
        }
        return null
    }

    /** Fetches the APK into the app's cache, [progress] in percent. Blocks. */
    fun download(context: Context, u: Update, progress: (Int) -> Unit): File {
        val dir = File(context.cacheDir, "update").apply { mkdirs(); listFiles()?.forEach { it.delete() } }
        val file = File(dir, "Button2Key-Remote-${u.version}.apk")
        val sha = MessageDigest.getInstance("SHA-256")
        val c = open(u.url)
        val total = if (u.size > 0) u.size else c.contentLengthLong
        c.inputStream.use { input ->
            file.outputStream().use { out ->
                val buf = ByteArray(64 * 1024)
                var done = 0L
                var last = -1
                while (true) {
                    val n = input.read(buf)
                    if (n < 0) break
                    out.write(buf, 0, n)
                    sha.update(buf, 0, n)
                    done += n
                    val pct = if (total > 0) (done * 100 / total).toInt() else 0
                    if (pct != last) { last = pct; progress(pct) }
                }
            }
        }
        val got = sha.digest().joinToString("") { "%02x".format(it) }
        if (u.sha256 != null && !got.equals(u.sha256, ignoreCase = true)) {
            file.delete()
            throw IOException("The download doesn't match the release's checksum: try again")
        }
        return file
    }

    /**
     * Installs it: Android asks the user to confirm (and, the first time, to let this app install apps: then it opens
     * that setting, and Update is pressed again on coming back). Returns false when it went to that setting.
     */
    fun install(activity: Activity, apk: File): Boolean {
        if (Build.VERSION.SDK_INT >= 26 && !activity.packageManager.canRequestPackageInstalls()) {
            activity.startActivity(Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${activity.packageName}")))
            return false
        }
        val installer = activity.packageManager.packageInstaller
        val params = PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL)
        params.setAppPackageName(activity.packageName)
        if (Build.VERSION.SDK_INT >= 31) params.setRequireUserAction(PackageInstaller.SessionParams.USER_ACTION_NOT_REQUIRED)
        val id = installer.createSession(params)
        installer.openSession(id).use { session ->
            session.openWrite("base.apk", 0, apk.length()).use { out ->
                apk.inputStream().use { it.copyTo(out) }
                session.fsync(out)
            }
            val flags = PendingIntent.FLAG_UPDATE_CURRENT or if (Build.VERSION.SDK_INT >= 31) PendingIntent.FLAG_MUTABLE else 0
            val pending = PendingIntent.getBroadcast(activity, id, Intent(activity, InstallReceiver::class.java), flags)
            session.commit(pending.intentSender)
        }
        return true
    }

    /** -1, 0 or 1 as version a is older, the same or newer than b ("1.10.0" > "1.9.2"). */
    fun compare(a: String, b: String): Int {
        val x = a.split('.').map { it.toIntOrNull() ?: 0 }
        val y = b.split('.').map { it.toIntOrNull() ?: 0 }
        for (i in 0 until maxOf(x.size, y.size)) {
            val d = (x.getOrElse(i) { 0 }).compareTo(y.getOrElse(i) { 0 })
            if (d != 0) return d
        }
        return 0
    }

    private fun open(url: String): HttpURLConnection = (URL(url).openConnection() as HttpURLConnection).apply {
        connectTimeout = 10000
        readTimeout = 20000
        instanceFollowRedirects = true
        setRequestProperty("User-Agent", "Button2Key-Remote-Android")
    }
}

/** PackageInstaller's answers: the confirmation to show, or how it ended. */
class InstallReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        when (intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE)) {
            PackageInstaller.STATUS_PENDING_USER_ACTION -> {
                @Suppress("DEPRECATION")
                val confirm = (if (Build.VERSION.SDK_INT >= 33) intent.getParcelableExtra(Intent.EXTRA_INTENT, Intent::class.java)
                    else intent.getParcelableExtra(Intent.EXTRA_INTENT)) ?: return
                context.startActivity(confirm.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
            }
            PackageInstaller.STATUS_SUCCESS -> { } // (the app restarts on the new version)
            PackageInstaller.STATUS_FAILURE_ABORTED -> Toast.makeText(context, "Update cancelled", Toast.LENGTH_SHORT).show()
            else -> Toast.makeText(context, "The update didn't install: " + (intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE) ?: "unknown reason"), Toast.LENGTH_LONG).show()
        }
    }
}
