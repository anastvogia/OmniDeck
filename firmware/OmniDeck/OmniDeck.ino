/*
 * OmniDeck - Arduino Firmware
 * NOTE: Compatible with native USB / HID-capable Arduinos
 * (e.g. Arduino Micro, Leonardo, Pro Micro - ATmega32U4 / SAMD boards).
 * 
 * 1. Maps analog potentiometer inputs to pipe-delimited serial output:
 *    Format: val0|val1|val2|...|valN\n (0 - 1023 per channel)
 * 
 * 2. Maps digital macro switch inputs to discrete serial events:
 *    Format: BTN:<pinName>:DOWN\n (when pressed, e.g. BTN:D2:DOWN)
 *            BTN:<pinName>:UP\n   (when released, e.g. BTN:D2:UP)
 */

// =================== CONFIGURATION ===================
// ── Sliders Configuration ────────────────────────────
const int NUM_SLIDERS = 4;
// Pins for each slider channel (0 -> A0, 1 -> A1, 2 -> A2, 3 -> A3)
const int SLIDER_PINS[NUM_SLIDERS] = { A0, A1, A2, A3 };

// Set to true if moving the slider UP physically decreases volume
// (Keep false if you have Invert enabled inside OmniDeck desktop settings)
const bool INVERT_SLIDERS = false; 

// ── Macro Switches Configuration ─────────────────────
const int NUM_MACROS = 4;
// Digital pins for each macro switch (0 -> D2, 1 -> D3, 2 -> D4, 3 -> D5)
const int MACRO_PINS[NUM_MACROS] = { 2, 3, 4, 5 };
// Hardware pin labels/identifiers sent to PC (e.g. "D2", "D3", "D4", "D5")
const char* const MACRO_PIN_NAMES[NUM_MACROS] = { "D2", "D3", "D4", "D5" };

// Set to true if switch connects pin to GND when pressed (standard INPUT_PULLUP)
// Set to false if your hardware connects pin to VCC when pressed (active HIGH)
const bool MACRO_ACTIVE_LOW = true;

// Debounce delay in milliseconds for mechanical key bounce
const unsigned long MACRO_DEBOUNCE_MS = 30;

// ── Communication & Sampling ─────────────────────────
// Baud rate (must match OmniDeck Settings, default is 9600)
const long BAUD_RATE = 9600;

// Sampling rate: check slider positions every 4ms (~250Hz for instantaneous response)
const unsigned long UPDATE_INTERVAL_MS = 4;

// Heartbeat: force-send slider values at least every 200ms
// (Ensures OmniDeck receives initial positions immediately upon connecting)
const unsigned long HEARTBEAT_INTERVAL_MS = 200;

// Minimum ADC change required to trigger an immediate frame (deadband against electrical noise)
const int JITTER_THRESHOLD = 2;
// =====================================================

// Slider state tracking
int lastSliderValues[NUM_SLIDERS];
unsigned long lastSampleTime = 0;
unsigned long lastSentTime = 0;

// Macro switch state tracking
int lastRawRead[NUM_MACROS];
int stableMacroState[NUM_MACROS];
unsigned long lastDebounceTime[NUM_MACROS];

void setup() {
  Serial.begin(BAUD_RATE);

  // Initialize Sliders
  for (int i = 0; i < NUM_SLIDERS; i++) {
    pinMode(SLIDER_PINS[i], INPUT);
    lastSliderValues[i] = -1;
  }

  // Initialize Macro Switches
  for (int i = 0; i < NUM_MACROS; i++) {
    if (MACRO_ACTIVE_LOW) {
      pinMode(MACRO_PINS[i], INPUT_PULLUP);
    } else {
      pinMode(MACRO_PINS[i], INPUT);
    }
    
    int initialRead = digitalRead(MACRO_PINS[i]);
    lastRawRead[i] = initialRead;
    stableMacroState[i] = initialRead;
    lastDebounceTime[i] = 0;
  }
}

void loop() {
  unsigned long now = millis();

  // 1. Process Macro Switches (Fast, event-driven, with debouncing)
  handleMacroSwitches(now);

  // 2. Process Sliders (Sampled on regular interval)
  handleSliders(now);
}

// ── Check and send Macro Switch events ────────────────
void handleMacroSwitches(unsigned long now) {
  for (int i = 0; i < NUM_MACROS; i++) {
    int currentRead = digitalRead(MACRO_PINS[i]);

    // Check if the pin reading changed (noise or actual press)
    if (currentRead != lastRawRead[i]) {
      lastDebounceTime[i] = now;
      lastRawRead[i] = currentRead;
    }

    // If reading has remained stable longer than the debounce delay
    if ((now - lastDebounceTime[i]) >= MACRO_DEBOUNCE_MS) {
      if (currentRead != stableMacroState[i]) {
        stableMacroState[i] = currentRead;

        bool isPressed = MACRO_ACTIVE_LOW ? (currentRead == LOW) : (currentRead == HIGH);

        // Send discrete event immediately over Serial (e.g. BTN:D2:DOWN)
        Serial.print(F("BTN:"));
        Serial.print(MACRO_PIN_NAMES[i]);
        if (isPressed) {
          Serial.println(F(":DOWN"));
        } else {
          Serial.println(F(":UP"));
        }
      }
    }
  }
}

// ── Check and stream Slider analog values ─────────────
void handleSliders(unsigned long now) {
  if (now - lastSampleTime < UPDATE_INTERVAL_MS) {
    return;
  }
  lastSampleTime = now;

  int currentValues[NUM_SLIDERS];
  bool hasMoved = false;

  for (int i = 0; i < NUM_SLIDERS; i++) {
    int raw = analogRead(SLIDER_PINS[i]);

    if (INVERT_SLIDERS) {
      raw = 1023 - raw;
    }

    raw = constrain(raw, 0, 1023);
    currentValues[i] = raw;

    // Check if slider moved beyond jitter threshold
    if (abs(currentValues[i] - lastSliderValues[i]) >= JITTER_THRESHOLD) {
      hasMoved = true;
    }
  }

  // Send slider packet if:
  // 1. Any slider physically moved, OR
  // 2. Heartbeat timer elapsed, OR
  // 3. First run after boot
  if (hasMoved || (now - lastSentTime >= HEARTBEAT_INTERVAL_MS) || lastSliderValues[0] == -1) {
    lastSentTime = now;

    for (int i = 0; i < NUM_SLIDERS; i++) {
      lastSliderValues[i] = currentValues[i];
      Serial.print(currentValues[i]);
      if (i < NUM_SLIDERS - 1) {
        Serial.print('|');
      }
    }
    Serial.println(); // Sends '\r\n'
  }
}
