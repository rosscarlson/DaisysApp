"""A test client for Button2Key Remote's server, and a reference for writing real ones (see
src/Modules/Butt2KeyRemote/README.md, "For client makers"). Needs: pip install cryptography

    python tools/buttclient.py --find                         # who's on the network
    python tools/buttclient.py HOST PASSPHRASE [--port 38870]  # connect, list, fetch every ButtScreen
    python tools/buttclient.py HOST PASSPHRASE --press SCREEN BUTTON   # press and let go of a button
"""
import argparse, base64, json, socket, struct, sys, time
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from cryptography.hazmat.primitives.kdf.pbkdf2 import PBKDF2HMAC
from cryptography.hazmat.primitives import hashes

PORT = 38870


def find(port, wait=1.5):
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
    s.settimeout(wait)
    msg = json.dumps({"op": "discover"}).encode()
    s.sendto(msg, ("255.255.255.255", port))
    s.sendto(msg, ("127.0.0.1", port))
    seen = []
    try:
        while True:
            data, addr = s.recvfrom(2048)
            seen.append((addr[0], json.loads(data)))
    except socket.timeout:
        pass
    return seen


class Wire:
    def __init__(self, host, port, passphrase, device="Test client", width=1080, height=2400):
        self.sock = socket.create_connection((host, port), timeout=10)
        hi = json.loads(self._read_frame())
        assert hi.get("service") == "Butt2KeyRemote", hi
        key = PBKDF2HMAC(hashes.SHA256(), 32, base64.b64decode(hi["salt"]), hi["iterations"]).derive(passphrase.encode())
        self.aes = AESGCM(key)
        self.sent = self.received = 0
        self.pc = hi["pc"]
        self.send({"op": "hello", "device": device, "platform": "Python", "app": "buttclient", "width": width, "height": height})

    def _read_exact(self, n):
        b = b""
        while len(b) < n:
            c = self.sock.recv(n - len(b))
            if not c:
                raise EOFError("closed")
            b += c
        return b

    def _read_frame(self):
        n = struct.unpack("<i", self._read_exact(4))[0]
        return self._read_exact(n)

    def send(self, obj):
        nonce = struct.pack("<IQ", 1, self.sent)  # 1 = from the device
        self.sent += 1
        ct = self.aes.encrypt(nonce, json.dumps(obj).encode(), None)  # ciphertext + 16-byte tag
        frame = nonce + ct[-16:] + ct[:-16]                          # nonce, tag, ciphertext
        self.sock.sendall(struct.pack("<i", len(frame)) + frame)

    def read(self):
        f = self._read_frame()
        nonce, tag, ct = f[:12], f[12:28], f[28:]
        way, count = struct.unpack("<IQ", nonce)
        assert way == 2 and count == self.received, (way, count)
        self.received += 1
        return json.loads(self.aes.decrypt(nonce, ct + tag, None))


def main():
    a = argparse.ArgumentParser()
    a.add_argument("host", nargs="?")
    a.add_argument("passphrase", nargs="?")
    a.add_argument("--port", type=int, default=PORT)
    a.add_argument("--find", action="store_true")
    a.add_argument("--press", nargs=2, metavar=("SCREEN", "BUTTON"))
    o = a.parse_args()
    if o.find:
        for ip, here in find(o.port):
            print(ip, here)
        return
    w = Wire(o.host, o.port, o.passphrase)
    welcome = w.read()
    print("welcome:", welcome)
    state = w.read()
    print("state:", json.dumps(state)[:600])
    for s in state["screens"]:
        w.send({"op": "get", "id": s["id"]})
        m = w.read()
        sc = m["screen"]
        print(f"screen {sc['name']}: {sc['width']}x{sc['height']} grid {sc['columns']}x{sc['rows']}, "
              f"{len(sc['buttons'])} buttons, icons {list(m['icons'])}, images {list(m['images'])}")
        for b in sc["buttons"]:
            print("   ", json.dumps(b)[:200])
    w.send({"op": "ping"})
    print("ping:", w.read())
    if o.press:
        screen, button = o.press
        w.send({"op": "down", "screen": screen, "button": button})
        time.sleep(0.1)
        w.send({"op": "up", "screen": screen, "button": button})
        w.sock.settimeout(1.5)
        try:
            while True:
                print("after press:", w.read())
        except (socket.timeout, TimeoutError):
            pass
    w.send({"op": "bogus"})
    print("bogus:", w.read())


if __name__ == "__main__":
    main()
