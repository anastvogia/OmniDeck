# OmniDeck

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![.NET 8.0](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows&logoColor=white)

**OmniDeck** is a modern Windows utility (.NET 8 WPF) that maps physical sliders (from an Arduino/USB serial device) to Windows audio volume controls. It supports master volume, active-window volume, and per-process audio mixing with a real-time visual interface.

---

## 🛠️ Building and Compiling

The project includes a unified build pipeline to compile and pack the application in two target formats:
1. **Minimal Build**: A lightweight, framework-dependent single executable (~1 MB). Requires the [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) installed on the target machine.
2. **Bundled Build**: A fully self-contained single executable (~155 MB). Includes the entire .NET 8.0 runtime and native WPF libraries; runs out-of-the-box on any 64-bit Windows PC without requiring any prior installations.

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) installed on your system.

### Option A: Local Build Script (Recommended)
An automated PowerShell script uild.ps1 is provided in the repository root. It stops running instances of OmniDeck, cleans previous outputs, and publishes both versions.

`powershell
./build.ps1
`

Once completed, the outputs will be located in the publish/ directory:
- **Minimal**: publish/minimal/OmniDeck.exe
- **Bundled**: publish/bundled/OmniDeck.exe

### Option B: Manual CLI Build
If you prefer running standard dotnet CLI commands:

* **Minimal Build:**
  `ash
  dotnet publish OmniDeck/OmniDeck.csproj -c Release -r win-x64 --self-contained false -o publish/minimal -p:PublishSingleFile=true -p:PublishReadyToRun=false -p:IncludeNativeLibrariesForSelfExtract=true
  `

* **Bundled Build:**
  `ash
  dotnet publish OmniDeck/OmniDeck.csproj -c Release -r win-x64 --self-contained true -o publish/bundled -p:PublishSingleFile=true -p:PublishReadyToRun=false -p:IncludeNativeLibrariesForSelfExtract=true
  `

---

## 🚀 How to Run the App

1. Navigate to the published directory of your choice (publish/minimal or publish/bundled).
2. Run OmniDeck.exe.
3. **Application Setup**:
   - In the left sidebar under **Connection**, select your **Serial Port** (e.g. COM6).
   - Select the matching **Baud Rate** (typically 9600).
   - Click **Connect**.
   - Under **Volume Mixing Deck**, map your physical slider channels (e.g., CH 0, CH 1) to targets:
     - master: Global system volume.
     - ctive_window: Current active foreground application.
     - ctive_not_mapped: Foreground application, unless it is already explicitly assigned to another slider.
     - Any running application (e.g. spotify.exe, discord.exe, chrome.exe).
   - Click **Save Settings** to persist the configuration.

> [!NOTE]
> **Single Instance Policy**: OmniDeck enforces a single running instance. If you run another instance of OmniDeck.exe, the new instance will signal the existing window to restore to the foreground and then exit immediately.

---

## 🔄 CI/CD Pipeline

A GitHub Actions CI/CD configuration is available at .github/workflows/build.yml. On every push or pull request to main, it restores dependencies, compiles both versions, and packages them as downloadable build artifacts (OmniDeck-Minimal-win-x64 and OmniDeck-Bundled-win-x64).

---

## 🎛️ Physical Deck & Hardware Options

OmniDeck is completely agnostic regarding the physical enclosure and build—as long as your sliders output a standard 0–5V analog voltage to your Arduino, the construction and design are entirely up to your imagination:

- **3D Printed Enclosures**: The most popular and ergonomic approach. You can 3D print an angled desktop console or wedge with cutouts matching your exact fader travel length (e.g., 45mm, 60mm, or 100mm faders) and custom slide knobs.
- **DIY Enclosures**: Off-the-shelf plastic project boxes, laser-cut acrylic plates, machined aluminum, woodcraft, or even simple cardboard prototyping.
- **Hardware Components Needed**:
  - **Slide Potentiometers**: Standard 10kΩ linear faders (single gang, linear taper B10K).
  - **Microcontroller**: Any native USB / HID-capable Arduino (e.g. **Arduino Micro**, **Pro Micro** with ATmega32U4).
  - **Wiring**: Simple 3-pin hookup per slider (VCC, GND, and Wiper pin).

<p align="center">
  <img src="assets/deck_photo.jpg" alt="3D Printed OmniDeck" width="600" />
  <br />
  <em>My custom 3D-printed OmniDeck hardware enclosure</em>
</p>

---

## 🔌 Arduino Firmware

The firmware sketch is located at [firmware/OmniDeck/OmniDeck.ino](firmware/OmniDeck/OmniDeck.ino).

> [!IMPORTANT]
> **Hardware Compatibility**: The firmware is **only compatible with native USB / HID-capable Arduinos** (e.g. **Arduino Micro**, **Arduino Leonardo**, **SparkFun/Clone Pro Micro**, or other **ATmega32U4 / SAMD** microcontrollers featuring native USB). Standard boards with external USB-to-UART chips (such as Arduino Uno or Nano) are not supported.

### Setup & Flashing
1. Open [firmware/OmniDeck/OmniDeck.ino](firmware/OmniDeck/OmniDeck.ino) in the **Arduino IDE**.
2. Connect your slide potentiometers (e.g. 10kΩ linear):
   - **VCC** (Outer pin 1) → **5V**
   - **GND** (Outer pin 2) → **GND**
   - **Wiper** (Middle pin) → **A0, A1, A2, A3** (or customize SLIDER_PINS in the sketch)
3. Under **Tools**, select your board (e.g. **Arduino Micro**) and port, then click **Upload**.
4. Open **OmniDeck**, select the COM port at 9600 baud, and click **Connect**.

---

## 🙏 Inspiration & Acknowledgments

This project was inspired by the [deej](https://github.com/omriharel/deej) project by Omri Harel.

While **OmniDeck** shares the vision of physical hardware volume control, **all code in this repository is 100% original and written from scratch** (not copied or forked). OmniDeck was built to address specific limitations and personal workflow needs, offering:
- **Modern WPF GUI**: Real-time visual feedback, sleek dark theme, and interactive slider calibration instead of manual YAML configuration files.
- **Dynamic Channel Management**: Add, remove, invert, and reorder slider channels on the fly.
- **Process Auto-Discovery**: Automatic live detection of running audio sessions without needing to look up process executables.
- **Native Windows Integration**: System tray minimization, single-instance IPC, and seamless Windows startup registration.

---

## 📄 License

This project is open-source software licensed under the [MIT License](LICENSE).
Copyright (c) 2026 Anastasios Vogiantzis.