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
An automated PowerShell script `build.ps1` is provided in the repository root. It stops running instances of OmniDeck, cleans previous outputs, and publishes both versions.

```powershell
./build.ps1
```

Once completed, the outputs will be located in the publish/ directory:
- **Minimal**: publish/minimal/OmniDeck.exe
- **Bundled**: publish/bundled/OmniDeck.exe

### Option B: Manual CLI Build
If you prefer running standard dotnet CLI commands:

* **Minimal Build:**
  ```bash
  dotnet publish OmniDeck/OmniDeck.csproj -c Release -r win-x64 --self-contained false -o publish/minimal -p:PublishSingleFile=true -p:PublishReadyToRun=false -p:IncludeNativeLibrariesForSelfExtract=true
  ```

* **Bundled Build:**
  ```bash
  dotnet publish OmniDeck/OmniDeck.csproj -c Release -r win-x64 --self-contained true -o publish/bundled -p:PublishSingleFile=true -p:PublishReadyToRun=false -p:IncludeNativeLibrariesForSelfExtract=true
  ```

---

## 🚀 How to Run the App

1. Navigate to the published directory of your choice (publish/minimal or publish/bundled).
2. Run `OmniDeck.exe`.
3. **Application Setup**:
   - In the left sidebar under **Connection**, select your **Serial Port** (e.g. COM6).
   - Select the matching **Baud Rate** (typically 9600).
   - Click **Connect**.
   - **Sliders Tab**:
     - Map your physical fader channels (e.g., CH 0, CH 1) to targets:
       - `master`: Global system volume.
       - `active_window`: Current active foreground application.
       - `active_not_mapped`: Foreground application, unless it is already explicitly assigned to another slider.
       - Any running application (e.g. spotify.exe, discord.exe, chrome.exe).
     - To register a new slider, click **Add Channel** and move the physical slider (requires $\ge 5$ ADC delta movement).
   - **Macros Tab**:
     - View and configure mechanical buttons and macro switches in a clean keycap grid.
     - Click **+ Add Macro** and press an unmapped physical button to pair it.
     - Select any keycap to configure its action:
       - **Media Control**: Play/Pause, Next Track, Previous Track, Volume Up/Down, Stop.
       - **System Utility**: Screenshot (PrintScreen), Lock Workstation.
       - **Audio Mute**: Instant mute/unmute toggle for master or any active application.
       - **Keystroke**: Custom key combinations (e.g. `Ctrl+Shift+M`).
       - **Run Program**: Launch executables, scripts, or URLs.
     - *Note*: Macro execution is automatically disabled while on the Macros tab (**Test Mode**) so you can press hardware buttons to select and inspect them without accidentally triggering shortcuts.
   - Click **Save Settings** to persist your configuration.

> [!NOTE]
> **Single Instance Policy**: OmniDeck enforces a single running instance. If you run another instance of `OmniDeck.exe`, the new instance will signal the existing window to restore to the foreground and then exit immediately.

> [!TIP]
> **Windows SmartScreen Notice**: Because OmniDeck is an open-source tool without a paid commercial certificate, Windows Defender SmartScreen may display an *Unrecognized app* notice on first launch. Click **More info** → **Run anyway**. All source code and build pipelines are 100% open and inspectable right here on GitHub.

---

## 🔄 CI/CD & Automated Releases

A complete GitHub Actions CI/CD pipeline is configured in [.github/workflows/build.yml](.github/workflows/build.yml):
- **On every push or pull request**: Restores dependencies, verifies compilation, and packages test builds in the **Actions** tab.
- **Automated Releases**: Pushing a version tag (e.g. git tag v1.0.0; git push origin v1.0.0) automatically compiles both builds and publishes a new entry on the [GitHub Releases](../../releases) page with OmniDeck-Minimal.exe and OmniDeck-Bundled.exe attached.

---

## 🎛️ Physical Deck & Hardware Options

OmniDeck is completely agnostic regarding the physical enclosure and build — as long as your sliders output a standard 0–5V analog voltage and your switches connect to digital pins, the design is entirely up to your imagination:

- **3D Printed Enclosures**: The most popular and ergonomic approach. You can 3D print an angled desktop console or wedge with cutouts matching your exact fader travel length (e.g., 45mm, 60mm, or 100mm faders) and mechanical keyboard switch cutouts.
- **DIY Enclosures**: Off-the-shelf plastic project boxes, laser-cut acrylic plates, machined aluminum, woodcraft, or simple desktop enclosures.
- **Hardware Components Needed**:
  - **Slide Potentiometers**: Standard 10kΩ linear faders (single gang, linear taper B10K).
  - **Mechanical Switches / Buttons**: Standard tactile pushbuttons or mechanical keyboard switches (Cherry MX, Gateron, etc.).
  - **Microcontroller**: Any native USB / HID-capable Arduino (e.g. **Arduino Micro**, **Pro Micro** with ATmega32U4).
  - **Wiring**:
    - Sliders: 3-pin hookup per slider (VCC, GND, and Wiper pin to analog inputs A0–A3).
    - Switches: 2-pin hookup per switch (One pin to Digital pin D2–D5, other pin to GND using internal `INPUT_PULLUP`).

<p align=center>
  <img src=assets/deck_photo.jpg alt=3D Printed OmniDeck width=600 />
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
   - **Wiper** (Middle pin) → **A0, A1, A2, A3** (or customize `SLIDER_PINS` in sketch)
3. Connect your macro switches (pushbuttons or mechanical switches):
   - **Pin 1** → **D2, D3, D4, D5** (or customize `MACRO_PINS` in sketch)
   - **Pin 2** → **GND** (internal pullups are active)
4. Under **Tools**, select your board (e.g. **Arduino Micro**) and port, then click **Upload**.
5. Open **OmniDeck**, select the COM port at 9600 baud, and click **Connect**.

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