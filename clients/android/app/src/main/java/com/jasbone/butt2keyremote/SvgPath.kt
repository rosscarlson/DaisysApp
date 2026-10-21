package com.jasbone.butt2keyremote

import android.graphics.Path
import kotlin.math.PI
import kotlin.math.abs
import kotlin.math.atan2
import kotlin.math.ceil
import kotlin.math.cos
import kotlin.math.sin
import kotlin.math.sqrt
import kotlin.math.tan

/**
 * SVG path data (the icon library's: Material Design Icons, on a 24 × 24 grid) made into an android.graphics.Path:
 * M L H V C S Q T A Z, absolute and relative. (Android's own parser is hidden, and AndroidX's would be a library to
 * bring for one function.)
 */
object SvgPath {
    fun parse(d: String): Path? = try { Parser(d).run() } catch (_: Exception) { null }

    private class Parser(private val d: String) {
        private var i = 0
        private val path = Path()
        private var x = 0f; private var y = 0f
        private var startX = 0f; private var startY = 0f
        private var ctrlX = 0f; private var ctrlY = 0f // the last curve's second control point, for S and T
        private var last = ' '

        fun run(): Path {
            var cmd = ' '
            while (true) {
                skip()
                if (i >= d.length) break
                val c = d[i]
                if (c.isLetter()) { cmd = c; i++ } else if (cmd == ' ') throw IllegalArgumentException("no command")
                command(cmd)
                // a moveto followed by more numbers means linetos
                if (cmd == 'M') cmd = 'L' else if (cmd == 'm') cmd = 'l'
                // (and Z takes no numbers: one after it is an error, not a loop)
                else if (cmd == 'Z' || cmd == 'z') cmd = ' '
            }
            return path
        }

        private fun command(c: Char) {
            val rel = c.isLowerCase()
            val ox = if (rel) x else 0f
            val oy = if (rel) y else 0f
            when (c.uppercaseChar()) {
                'M' -> { x = ox + num(); y = oy + num(); path.moveTo(x, y); startX = x; startY = y }
                'L' -> { x = ox + num(); y = oy + num(); path.lineTo(x, y) }
                'H' -> { x = ox + num(); path.lineTo(x, y) }
                'V' -> { y = oy + num(); path.lineTo(x, y) }
                'C' -> {
                    val x1 = ox + num(); val y1 = oy + num(); val x2 = ox + num(); val y2 = oy + num()
                    x = ox + num(); y = oy + num()
                    path.cubicTo(x1, y1, x2, y2, x, y); ctrlX = x2; ctrlY = y2
                }
                'S' -> {
                    val (x1, y1) = if (last.uppercaseChar() in "CS") Pair(2 * x - ctrlX, 2 * y - ctrlY) else Pair(x, y)
                    val x2 = ox + num(); val y2 = oy + num()
                    x = ox + num(); y = oy + num()
                    path.cubicTo(x1, y1, x2, y2, x, y); ctrlX = x2; ctrlY = y2
                }
                'Q' -> {
                    val x1 = ox + num(); val y1 = oy + num()
                    x = ox + num(); y = oy + num()
                    path.quadTo(x1, y1, x, y); ctrlX = x1; ctrlY = y1
                }
                'T' -> {
                    val (x1, y1) = if (last.uppercaseChar() in "QT") Pair(2 * x - ctrlX, 2 * y - ctrlY) else Pair(x, y)
                    x = ox + num(); y = oy + num()
                    path.quadTo(x1, y1, x, y); ctrlX = x1; ctrlY = y1
                }
                'A' -> {
                    val rx = num(); val ry = num(); val rot = num()
                    val large = flag(); val sweep = flag()
                    val x1 = ox + num(); val y1 = oy + num()
                    arc(x, y, x1, y1, rx, ry, rot, large, sweep)
                    x = x1; y = y1
                }
                'Z' -> { path.close(); x = startX; y = startY }
                else -> throw IllegalArgumentException("command $c")
            }
            last = c
        }

        private fun skip() {
            while (i < d.length && (d[i] == ' ' || d[i] == ',' || d[i] == '\n' || d[i] == '\t' || d[i] == '\r')) i++
        }

        private fun num(): Float {
            skip()
            val s = i
            if (i < d.length && (d[i] == '-' || d[i] == '+')) i++
            var dot = false
            while (i < d.length) {
                val c = d[i]
                if (c.isDigit()) i++
                else if (c == '.' && !dot) { dot = true; i++ }
                else if ((c == 'e' || c == 'E') && i + 1 < d.length) {
                    i++
                    if (d[i] == '-' || d[i] == '+') i++
                } else break
            }
            if (s == i) throw IllegalArgumentException("number expected at $i")
            return d.substring(s, i).toFloat()
        }

        /** An arc's flags are single digits, and may run into the next number ("a1 1 0 011 1"). */
        private fun flag(): Boolean {
            skip()
            val c = d[i++]
            return c == '1'
        }

        /** An SVG arc as cubic Béziers (the SVG spec's endpoint-to-centre conversion, then quarter turns at most). */
        private fun arc(x0: Float, y0: Float, x1: Float, y1: Float, rxIn: Float, ryIn: Float, angle: Float, large: Boolean, sweep: Boolean) {
            if (x0 == x1 && y0 == y1) return
            var rx = abs(rxIn).toDouble(); var ry = abs(ryIn).toDouble()
            if (rx == 0.0 || ry == 0.0) { path.lineTo(x1, y1); return }
            val phi = angle * PI / 180
            val cp = cos(phi); val sp = sin(phi)
            val dx = (x0 - x1) / 2.0; val dy = (y0 - y1) / 2.0
            val x1p = cp * dx + sp * dy
            val y1p = -sp * dx + cp * dy
            val lambda = (x1p * x1p) / (rx * rx) + (y1p * y1p) / (ry * ry)
            if (lambda > 1) { val s = sqrt(lambda); rx *= s; ry *= s }
            val num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p
            val den = rx * rx * y1p * y1p + ry * ry * x1p * x1p
            var co = sqrt(maxOf(0.0, num / den))
            if (large == sweep) co = -co
            val cxp = co * rx * y1p / ry
            val cyp = -co * ry * x1p / rx
            val cx = cp * cxp - sp * cyp + (x0 + x1) / 2.0
            val cy = sp * cxp + cp * cyp + (y0 + y1) / 2.0
            val t1 = atan2((y1p - cyp) / ry, (x1p - cxp) / rx)
            var dt = atan2((-y1p - cyp) / ry, (-x1p - cxp) / rx) - t1
            if (sweep && dt < 0) dt += 2 * PI else if (!sweep && dt > 0) dt -= 2 * PI
            val segments = ceil(abs(dt) / (PI / 2)).toInt().coerceAtLeast(1)
            val step = dt / segments
            val k = 4.0 / 3.0 * tan(step / 4)
            var t = t1
            for (n in 0 until segments) {
                val c1 = cos(t); val s1 = sin(t)
                val t2 = t + step
                val c2 = cos(t2); val s2 = sin(t2)
                // on the unit circle, then scaled, rotated and moved
                val p1x = c1 - k * s1; val p1y = s1 + k * c1
                val p2x = c2 + k * s2; val p2y = s2 - k * c2
                fun mx(ux: Double, uy: Double) = (cx + cp * rx * ux - sp * ry * uy).toFloat()
                fun my(ux: Double, uy: Double) = (cy + sp * rx * ux + cp * ry * uy).toFloat()
                path.cubicTo(mx(p1x, p1y), my(p1x, p1y), mx(p2x, p2y), my(p2x, p2y), mx(c2, s2), my(c2, s2))
                t = t2
            }
        }
    }
}
