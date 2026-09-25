/*
 * OmniDeck - Arduino Firmware
 * NOTE: Only compatible with native USB / HID-capable Arduinos
 * (e.g. Arduino Micro, Leonardo, Pro Micro - ATmega32U4 / SAMD boards).
 * 
 * Maps analog potentiometer inputs to pipe-delimited serial output:
 * Format: val0|val1|val2|...|valN\n (0 - 1023 per channel)
 */

// =================== CONFIGURATION ===================
const int NUM_SLIDERS = 4;
// Pins for each slider channel (0 -> A0, 1 -> A1, 2 -> A2, 3 -> A3)
const int SLIDER_PINS[NUM_SLIDERS] = { A0, A1, A2, A3 };

// Set to true if moving the slider UP physically decreases volume
// (Keep false if you have Invert enabled inside OmniDeck settings)
const bool INVERT_SLIDERS = false; 

// Baud rate (must match OmniDeck Settings, default is 9600)
const long BAUD_RATE = 9600;

// Sampling rate: check slider positions every 15ms (~66Hz)
const unsigned long UPDATE_INTERVAL_MS = 15;

// Heartbeat: force-send current values at least every 200ms
// (Ensures OmniDeck receives initial positions immediately upon connecting)
const unsigned long HEARTBEAT_INTERVAL_MS = 200;

// Minimum ADC change required to trigger an immediate frame (deadband against electrical noise)
const int JITTER_THRESHOLD = 3;
// =====================================================

int lastValues[NUM_SLIDERS] = { -1, -1, -1, -1 };
unsigned long lastSampleTime = 0;
unsigned long lastSentTime = 0;

void setup() {
  Serial.begin(BAUD_RATE);
  
  for (int i = 0; i < NUM_SLIDERS; i++) {
    pinMode(SLIDER_PINS[i], INPUT);
  }
}

void loop() {
  unsigned long now = millis();
  if (now - lastSampleTime < UPDATE_INTERVAL_MS) {
    return;
  }
  lastSampleTime = now;

  int currentValues[NUM_SLIDERS];
  bool hasMoved = false;

  for (int i = 0; i < NUM_SLIDERS; i++) {
    // Read raw 10-bit analog value (0 - 1023)
    int raw = analogRead(SLIDER_PINS[i]);

    if (INVERT_SLIDERS) {
      raw = 1023 - raw;
    }

    raw = constrain(raw, 0, 1023);
    currentValues[i] = raw;

    // Check if slider moved beyond jitter noise threshold
    if (abs(currentValues[i] - lastValues[i]) >= JITTER_THRESHOLD) {
      hasMoved = true;
    }
  }

  // Send packet if:
  // 1. Any slider physically moved, OR
  // 2. Heartbeat timer elapsed (keeps app synced even when idle), OR
  // 3. First run after boot
  if (hasMoved || (now - lastSentTime >= HEARTBEAT_INTERVAL_MS) || lastValues[0] == -1) {
    lastSentTime = now;

    for (int i = 0; i < NUM_SLIDERS; i++) {
      lastValues[i] = currentValues[i];
      Serial.print(currentValues[i]);
      if (i < NUM_SLIDERS - 1) {
        Serial.print('|');
      }
    }
    Serial.println(); // Sends '\r\n'
  }
}


