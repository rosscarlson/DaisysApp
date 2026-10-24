// DaisysSim: the sketch for Daisy's App's SimEffects → Arduino. One sketch for every board: the app says what's on
// it when it connects (each feature's type, pin and settings), so nothing here needs changing for your wiring.
// Needs the FastLED library (Sketch → Include Library → Manage Libraries… → FastLED). See README.md beside it.
//
// Protocol (115200 baud): frames of 0xA5, length, body (command, slot, data…), XOR of the body. A slot is one
// feature (0-3). LEDs change only on SHOW, answered with READY once written (writing WS2812s stops interrupts, so the
// app sends nothing meanwhile). With nothing from the app for 3 seconds, everything goes off.

#include <FastLED.h>
#ifdef __AVR__
#include <avr/wdt.h>
#endif

#define VERSION 1
#define MAX_SLOTS 4
#define MAX_LEDS 128   // every slot's LEDs together (3 bytes of RAM each)
#define MAX_BODY 200
#define START 0xA5

// commands from the app
#define CMD_HELLO 0x01
#define CMD_CONFIG 0x02
#define CMD_RESET 0x03
#define CMD_PIXELS 0x10
#define CMD_BITMAP 0x11
#define CMD_CLEAR 0x12
#define CMD_SHOW 0x20
#define CMD_PING 0x7F
// answers
#define REPLY_HELLO 0x81
#define REPLY_CONFIG 0x82
#define REPLY_READY 0x8A
#define REPLY_PONG 0x8F
// config problems
#define BAD_TYPE 1
#define BAD_PIN 2
#define TOO_MANY 3
#define SLOT_TAKEN 4

// feature types (the app's IArduinoFeature.Type); add yours after these (README: "Adding a feature")
#define TYPE_STRIP 1
#define TYPE_MATRIX 2

// matrix flags
#define FLAG_ZIGZAG 0x01   // every other row runs backwards
#define FLAG_ROTATE 0x06   // two bits: 0, 90, 180, 270 degrees
#define FLAG_MIRROR 0x08
#define FLAG_RGB 0x10      // the LEDs take red first (most WS2812Bs take green first)

struct Slot {
  uint8_t type, pin, count, brightness, flags;
  uint16_t first;  // its first LED in leds[]
};

CRGB leds[MAX_LEDS];
Slot slots[MAX_SLOTS];
uint16_t usedLeds = 0;
bool lit = false;
unsigned long lastHeard = 0;

uint8_t body[MAX_BODY];
uint8_t state = 0, len = 0, at = 0;

// FastLED wants the pin when it's compiled: one output for each pin it could be
bool addLeds(uint8_t pin, CRGB *data, int count) {
  switch (pin) {
#define PIN_CASE(n) case n: FastLED.addLeds<WS2812B, n, GRB>(data, count); return true;
    PIN_CASE(2) PIN_CASE(3) PIN_CASE(4) PIN_CASE(5) PIN_CASE(6) PIN_CASE(7)
    PIN_CASE(8) PIN_CASE(9) PIN_CASE(10) PIN_CASE(11) PIN_CASE(12) PIN_CASE(13)
  }
  return false;
}

void send(uint8_t command, uint8_t slot, const uint8_t *data, uint8_t n) {
  uint8_t x = command ^ slot;
  Serial.write(START);
  Serial.write((uint8_t)(n + 2));
  Serial.write(command);
  Serial.write(slot);
  for (uint8_t i = 0; i < n; i++) { Serial.write(data[i]); x ^= data[i]; }
  Serial.write(x);
}

void restart() {
#ifdef __AVR__
  wdt_enable(WDTO_15MS);
  for (;;) {}
#else
  // (other boards: unplug it to start again)
#endif
}

// a colour as the slot's LEDs want it: its brightness, and its colour order
CRGB colour(const Slot &s, uint8_t r, uint8_t g, uint8_t b) {
  r = scale8_video(r, s.brightness);
  g = scale8_video(g, s.brightness);
  b = scale8_video(b, s.brightness);
  return (s.flags & FLAG_RGB) ? CRGB(g, r, b) : CRGB(r, g, b);
}

// where (x, y) of an 8x8 picture is on the matrix's wire
uint8_t matrixIndex(const Slot &s, uint8_t x, uint8_t y) {
  if (s.flags & FLAG_MIRROR) x = 7 - x;
  for (uint8_t r = (s.flags & FLAG_ROTATE) >> 1; r > 0; r--) {
    uint8_t t = x;
    x = 7 - y;
    y = t;
  }
  if ((s.flags & FLAG_ZIGZAG) && (y & 1)) x = 7 - x;
  return y * 8 + x;
}

void configure(uint8_t slot, const uint8_t *d, uint8_t n) {
  uint8_t problem = 0;
  if (slot >= MAX_SLOTS || n < 5) problem = BAD_TYPE;
  else {
    Slot &s = slots[slot];
    uint8_t type = d[0], pin = d[1], count = d[2];
    if (type != TYPE_STRIP && type != TYPE_MATRIX) problem = BAD_TYPE;
    else if (type == TYPE_MATRIX && count != 64) problem = BAD_TYPE;
    else if (s.type != 0) {
      // set up already (the board didn't restart when the port opened): fine if it's the same
      if (s.type != type || s.pin != pin || s.count != count) problem = SLOT_TAKEN;
      else { s.brightness = d[3]; s.flags = d[4]; }
    } else if (count == 0 || usedLeds + count > MAX_LEDS) problem = TOO_MANY;
    else if (!addLeds(pin, leds + usedLeds, count)) problem = BAD_PIN;
    else {
      s.type = type; s.pin = pin; s.count = count; s.brightness = d[3]; s.flags = d[4];
      s.first = usedLeds;
      usedLeds += count;
    }
  }
  uint8_t reply[1] = { problem };
  send(REPLY_CONFIG, slot, reply, 1);
}

void clearSlot(uint8_t slot) {
  for (uint8_t i = 0; i < MAX_SLOTS; i++)
    if ((slot == 0xFF || slot == i) && slots[i].type != 0)
      fill_solid(leds + slots[i].first, slots[i].count, CRGB::Black);
}

void handle() {
  uint8_t command = body[0], slot = body[1];
  const uint8_t *d = body + 2;
  uint8_t n = len - 2;
  lastHeard = millis();
  switch (command) {
    case CMD_HELLO: {
      uint8_t reply[] = { VERSION, MAX_SLOTS, 'D', 'a', 'i', 's', 'y', 'S', 'i', 'm' };
      send(REPLY_HELLO, 0xFF, reply, sizeof(reply));
      break;
    }
    case CMD_CONFIG: configure(slot, d, n); break;
    case CMD_RESET: restart(); break;
    case CMD_PIXELS: {
      if (slot >= MAX_SLOTS || slots[slot].type == 0 || n < 1) break;
      Slot &s = slots[slot];
      for (uint8_t i = 0, led = d[0]; i + 3 < n && led < s.count; i += 3, led++)
        leds[s.first + led] = colour(s, d[1 + i], d[2 + i], d[3 + i]);
      break;
    }
    case CMD_BITMAP: {
      if (slot >= MAX_SLOTS || slots[slot].type != TYPE_MATRIX || n < 14) break;
      Slot &s = slots[slot];
      CRGB on = colour(s, d[8], d[9], d[10]), off = colour(s, d[11], d[12], d[13]);
      for (uint8_t y = 0; y < 8; y++)
        for (uint8_t x = 0; x < 8; x++)
          leds[s.first + matrixIndex(s, x, y)] = (d[y] & (0x80 >> x)) ? on : off;
      break;
    }
    case CMD_CLEAR: clearSlot(slot); break;
    case CMD_SHOW:
      FastLED.show();
      lit = true;
      send(REPLY_READY, 0, 0, 0);
      break;
    case CMD_PING: send(REPLY_PONG, 0, 0, 0); break;
    // (your feature's own commands: 0x30-0x6F)
  }
}

void setup() {
#ifdef __AVR__
  // (after a RESET the watchdog is still on: off before it fires again)
  MCUSR = 0;
  wdt_disable();
#endif
  Serial.begin(115200);
  lastHeard = millis();
}

void loop() {
  while (Serial.available() > 0) {
    uint8_t b = Serial.read();
    switch (state) {
      case 0:
        if (b == START) state = 1;
        break;
      case 1:
        if (b < 2 || b > MAX_BODY) { state = b == START ? 1 : 0; break; }
        len = b; at = 0; state = 2;
        break;
      case 2:
        body[at++] = b;
        if (at == len) state = 3;
        break;
      default: {
        state = 0;
        uint8_t x = 0;
        for (uint8_t i = 0; i < len; i++) x ^= body[i];
        if (x == b) handle();
      }
    }
  }
  // the app's gone: lights off
  if (lit && millis() - lastHeard > 3000) {
    clearSlot(0xFF);
    FastLED.show();
    lit = false;
  }
}
