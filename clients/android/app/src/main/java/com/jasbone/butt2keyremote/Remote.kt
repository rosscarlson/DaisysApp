package com.jasbone.butt2keyremote

import android.os.Handler
import android.os.Looper
import org.json.JSONObject
import java.io.IOException
import java.net.ConnectException
import java.net.NoRouteToHostException
import java.net.SocketTimeoutException
import java.net.UnknownHostException
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit

/**
 * Keeps a connection to one PC while [start]ed: connects (and again after it drops, a little later each time), pings
 * every 5 seconds (the PC drops a device silent for 30), sends what's [send]t in order, and hands what the PC says to
 * the [Listener] on the main thread. A wrong passphrase stops it: trying again wouldn't help.
 */
class Remote(private val pc: Pc, private val hello: () -> JSONObject, private val listener: Listener) {
    interface Listener {
        fun onConnecting(attempt: Int, lastError: String?)
        fun onConnected(pcName: String)
        fun onFailed(message: String)
        fun onMessage(m: JSONObject)
    }

    private val main = Handler(Looper.getMainLooper())
    private val outbox = LinkedBlockingQueue<JSONObject>()
    @Volatile private var running = false
    @Volatile private var stopped = false
    @Volatile private var wire: Wire? = null
    private var thread: Thread? = null

    val connected get() = wire != null

    fun start() {
        if (running) return
        running = true
        stopped = false
        outbox.clear()
        thread = Thread({ loop() }, "Butt2Key Remote").apply { isDaemon = true; start() }
    }

    fun stop() {
        running = false
        stopped = true
        wire?.close()
        thread?.interrupt()
        thread = null
    }

    /** Sends a message once connected (dropped while not: presses shouldn't arrive late). */
    fun send(m: JSONObject) {
        if (wire != null) outbox.offer(m)
    }

    private fun loop() {
        var attempt = 0
        var lastError: String? = null
        while (running) {
            attempt++
            post { listener.onConnecting(attempt, lastError) }
            val w = try {
                Wire.connect(pc.host.trim(), pc.port, pc.passphrase, hello())
            } catch (e: WrongPassphraseException) {
                running = false
                post { listener.onFailed("The passphrase isn't the one set on ${pc.name} (Gaming → Button2Key Remote → Devices), or it was changed there.") }
                return
            } catch (e: NotButtException) {
                lastError = e.message
                null
            } catch (e: Exception) {
                lastError = describe(e)
                null
            }
            if (w == null) {
                if (!pause(minOf(10000L, 1000L * attempt))) return
                continue
            }
            if (!running) { w.close(); return }
            attempt = 0
            lastError = null
            wire = w
            outbox.clear()
            post { listener.onConnected(w.pc) }
            val writer = Thread({ write(w) }, "Butt2Key Remote send").apply { isDaemon = true; start() }
            try {
                while (running) {
                    val m = w.read() ?: break
                    post { listener.onMessage(m) }
                }
                lastError = "${pc.name} closed the connection (Button2Key Remote switched off?)"
            } catch (e: Exception) {
                lastError = if (e is SocketTimeoutException) "Nothing heard from ${pc.name} for 20 seconds" else describe(e)
            } finally {
                wire = null
                w.close()
                writer.interrupt()
            }
            if (running && !pause(1000)) return
        }
    }

    private fun write(w: Wire) {
        var lastSent = System.currentTimeMillis()
        try {
            while (running && wire === w) {
                val m = outbox.poll(1, TimeUnit.SECONDS)
                if (m != null) { w.send(m); lastSent = System.currentTimeMillis() }
                if (System.currentTimeMillis() - lastSent >= 5000) { w.send(JSONObject().put("op", "ping")); lastSent = System.currentTimeMillis() }
            }
        } catch (_: InterruptedException) {
        } catch (_: Exception) {
            w.close() // the reader sees it and reconnects
        }
    }

    private fun pause(ms: Long): Boolean = try { Thread.sleep(ms); running } catch (_: InterruptedException) { false }

    private fun post(f: () -> Unit) { main.post { if (!stopped) f() } }

    private fun describe(e: Exception): String = when (e) {
        is UnknownHostException -> "Can't find ${pc.host}"
        is ConnectException -> "${pc.name} isn't answering on port ${pc.port}: is Daisy's App running, with Button2Key Remote on?"
        is NoRouteToHostException -> "Can't reach ${pc.host}: is this device on the same network?"
        is SocketTimeoutException -> "${pc.name} didn't answer: is this device on the same network, and the PC's firewall letting Daisy's App in?"
        is java.net.SocketException -> "The connection to ${pc.name} was lost (Daisy's App closed, or the network dropped)"
        is IOException -> e.message ?: "The connection failed"
        else -> e.message ?: e.javaClass.simpleName
    }
}
