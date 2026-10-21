package com.jasbone.butt2keyremote

import android.os.Build
import android.util.Base64
import org.json.JSONObject
import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.DataInputStream
import java.io.EOFException
import java.io.IOException
import java.net.InetSocketAddress
import java.net.Socket
import java.nio.ByteBuffer
import java.nio.ByteOrder
import javax.crypto.Cipher
import javax.crypto.Mac
import javax.crypto.SecretKeyFactory
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.PBEKeySpec
import javax.crypto.spec.SecretKeySpec

/** The passphrase isn't the PC's (it closed the connection after our hello). */
class WrongPassphraseException : IOException("Not the same passphrase as on the PC")

/** Something that isn't Button2Key Remote answered. */
class NotButtException(message: String) : IOException(message)

/**
 * One connection to Daisy's App's Button2Key Remote (protocol version 1, src/Modules/Butt2KeyRemote/README.md, "For
 * client makers"): JSON messages in frames (a 4-byte little-endian length, then the frame). The PC's first frame is
 * plain JSON (its name, a salt, the iteration count); every frame after that, both ways, is AES-256-GCM under
 * PBKDF2-SHA256(passphrase, salt): a 12-byte nonce (uint32 direction, 1 = from us, 2 = from the PC; uint64 count of
 * frames sent that way), the 16-byte tag, then the ciphertext.
 *
 * [connect] blocks (call it off the main thread). [send] may be called from any thread; [read] from one.
 */
class Wire private constructor(private val socket: Socket) {
    private val input = DataInputStream(BufferedInputStream(socket.getInputStream()))
    private val output = BufferedOutputStream(socket.getOutputStream())
    private var key: SecretKeySpec? = null
    private var sent = 0L
    private var received = 0L
    private val sendLock = Any()

    var pc: String = ""
        private set

    companion object {
        const val SERVICE = "Butt2KeyRemote"
        const val VERSION = 1
        private const val MAX_FRAME = 1 shl 20
        private const val FROM_DEVICE = 1
        private const val FROM_PC = 2

        /** Connects, signs in with the passphrase and says hello; returns once the PC has welcomed us. */
        fun connect(host: String, port: Int, passphrase: String, hello: JSONObject): Wire {
            val s = Socket()
            try {
                s.tcpNoDelay = true
                s.connect(InetSocketAddress(host, port), 6000)
                s.soTimeout = 20000
                val w = Wire(s)
                val hi = try {
                    JSONObject(String(w.readFrame() ?: throw EOFException("closed"), Charsets.UTF_8))
                } catch (e: org.json.JSONException) {
                    throw NotButtException("That isn't Button2Key Remote (check the port)")
                }
                if (hi.optString("service") != SERVICE) throw NotButtException("That isn't Button2Key Remote (check the port)")
                w.pc = hi.optString("pc")
                val salt = Base64.decode(hi.getString("salt"), Base64.DEFAULT)
                w.key = SecretKeySpec(pbkdf2(passphrase, salt, hi.optInt("iterations", 150000)), "AES")
                w.send(hello)
                // the wrong passphrase: the PC can't read the hello and closes the connection
                val welcome = try { w.read() } catch (e: IOException) { null } ?: throw WrongPassphraseException()
                if (welcome.optString("op") != "welcome") throw NotButtException("The PC answered something unexpected")
                if (welcome.optString("pc").isNotEmpty()) w.pc = welcome.optString("pc")
                return w
            } catch (e: Exception) {
                try { s.close() } catch (_: Exception) { }
                throw e
            }
        }

        /** PBKDF2-HMAC-SHA256 of the passphrase's UTF-8, 32 bytes. */
        fun pbkdf2(passphrase: String, salt: ByteArray, iterations: Int): ByteArray {
            // Android's own is much quicker; for anything but plain ASCII it's done here, so the bytes are surely UTF-8
            if (Build.VERSION.SDK_INT >= 26 && passphrase.all { it.code in 1..127 }) {
                try {
                    val f = SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256")
                    return f.generateSecret(PBEKeySpec(passphrase.toCharArray(), salt, iterations, 256)).encoded
                } catch (_: Exception) { }
            }
            val mac = Mac.getInstance("HmacSHA256")
            mac.init(SecretKeySpec(passphrase.toByteArray(Charsets.UTF_8), "HmacSHA256"))
            mac.update(salt)
            mac.update(byteArrayOf(0, 0, 0, 1))
            var u = mac.doFinal()
            val t = u.copyOf()
            for (i in 1 until iterations) {
                u = mac.doFinal(u)
                for (j in t.indices) t[j] = (t[j].toInt() xor u[j].toInt()).toByte()
            }
            return t
        }

        private fun nonce(direction: Int, count: Long): ByteArray =
            ByteBuffer.allocate(12).order(ByteOrder.LITTLE_ENDIAN).putInt(direction).putLong(count).array()
    }

    /** Sends a message (blocks: not on the main thread). */
    fun send(message: JSONObject) {
        val plain = message.toString().toByteArray(Charsets.UTF_8)
        synchronized(sendLock) {
            val n = nonce(FROM_DEVICE, sent)
            val c = Cipher.getInstance("AES/GCM/NoPadding")
            c.init(Cipher.ENCRYPT_MODE, key, GCMParameterSpec(128, n))
            val sealed = c.doFinal(plain) // ciphertext, then the tag: the frame wants the tag first
            val body = ByteArray(12 + sealed.size)
            System.arraycopy(n, 0, body, 0, 12)
            System.arraycopy(sealed, sealed.size - 16, body, 12, 16)
            System.arraycopy(sealed, 0, body, 28, sealed.size - 16)
            sent++
            val head = ByteBuffer.allocate(4).order(ByteOrder.LITTLE_ENDIAN).putInt(body.size).array()
            output.write(head)
            output.write(body)
            output.flush()
        }
    }

    /** The next message, or null when the PC has closed the connection. */
    fun read(): JSONObject? {
        val frame = readFrame() ?: return null
        if (frame.size < 28) throw IOException("A short frame")
        val b = ByteBuffer.wrap(frame, 0, 12).order(ByteOrder.LITTLE_ENDIAN)
        if (b.int != FROM_PC || b.long != received) throw IOException("A frame out of order")
        val sealed = ByteArray(frame.size - 12)
        System.arraycopy(frame, 28, sealed, 0, frame.size - 28)
        System.arraycopy(frame, 12, sealed, frame.size - 28, 16)
        val c = Cipher.getInstance("AES/GCM/NoPadding")
        c.init(Cipher.DECRYPT_MODE, key, GCMParameterSpec(128, frame, 0, 12))
        val plain = try { c.doFinal(sealed) } catch (e: Exception) { throw IOException("A frame that doesn't decrypt") }
        received++
        return JSONObject(String(plain, Charsets.UTF_8))
    }

    private fun readFrame(): ByteArray? {
        val head = ByteArray(4)
        try { input.readFully(head) } catch (e: EOFException) { return null }
        val n = ByteBuffer.wrap(head).order(ByteOrder.LITTLE_ENDIAN).int
        if (n < 0 || n > MAX_FRAME) throw IOException("A frame too big")
        val body = ByteArray(n)
        input.readFully(body)
        return body
    }

    fun close() {
        try { socket.close() } catch (_: Exception) { }
    }
}
