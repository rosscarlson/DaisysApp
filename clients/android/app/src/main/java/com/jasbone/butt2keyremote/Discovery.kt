package com.jasbone.butt2keyremote

import android.content.Context
import android.net.wifi.WifiManager
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.NetworkInterface
import java.net.SocketTimeoutException

/** A PC that answered: its name, address and port. */
data class FoundPc(val name: String, val host: String, val port: Int)

/**
 * Finding PCs: {"op":"discover"} as a UDP broadcast to the port, and each PC with Button2Key Remote on answers
 * {"op":"here","pc","port"} to where it came from. Blocks for [waitMs]: call it off the main thread.
 */
object Discovery {
    fun find(context: Context, port: Int = Pc.DEFAULT_PORT, waitMs: Int = 2000): List<FoundPc> {
        val wifi = context.applicationContext.getSystemService(Context.WIFI_SERVICE) as? WifiManager
        // some phones drop broadcasts unless something holds this
        val lock = try { wifi?.createMulticastLock("butt-discover")?.apply { setReferenceCounted(false); acquire() } } catch (_: Exception) { null }
        val found = LinkedHashMap<String, FoundPc>()
        try {
            DatagramSocket().use { s ->
                s.broadcast = true
                s.soTimeout = 300
                val msg = JSONObject().put("op", "discover").toString().toByteArray(Charsets.UTF_8)
                val targets = (listOf(InetAddress.getByName("255.255.255.255")) + broadcasts()).distinct()
                val until = System.currentTimeMillis() + waitMs
                var nextSend = 0L
                val buf = ByteArray(2048)
                while (System.currentTimeMillis() < until) {
                    // sent a few times: UDP may lose one
                    if (System.currentTimeMillis() >= nextSend) {
                        for (t in targets) try { s.send(DatagramPacket(msg, msg.size, t, port)) } catch (_: Exception) { }
                        nextSend = System.currentTimeMillis() + 600
                    }
                    val p = DatagramPacket(buf, buf.size)
                    try { s.receive(p) } catch (_: SocketTimeoutException) { continue }
                    try {
                        val o = JSONObject(String(p.data, 0, p.length, Charsets.UTF_8))
                        if (o.optString("op") != "here" || o.optString("service") != Wire.SERVICE) continue
                        val host = p.address.hostAddress ?: continue
                        found[host] = FoundPc(o.optString("pc", host), host, o.optInt("port", port))
                    } catch (_: Exception) { }
                }
            }
        } catch (_: Exception) {
        } finally {
            try { lock?.release() } catch (_: Exception) { }
        }
        return found.values.toList()
    }

    /** Each network's own broadcast address (192.168.1.255…), for routers that don't pass 255.255.255.255 on. */
    private fun broadcasts(): List<InetAddress> = try {
        NetworkInterface.getNetworkInterfaces().toList()
            .filter { it.isUp && !it.isLoopback }
            .flatMap { it.interfaceAddresses }
            .mapNotNull { it.broadcast }
    } catch (_: Exception) { emptyList() }
}
