# OmniDeck

**OmniDeck** is a Windows utility (.NET 8 WPF) that maps physical sliders (from an Arduino/USB serial device) to Windows audio volume controls. It supports master volume, active-window volume, and per-process audio mixing.

---

## 🏗️ Building and Compiling

The project includes a unified build pipeline to compile and pack the application in two target formats:
1. **Minimal Build**: A lightweight, framework-dependent single executable. It requires the [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) to be installed on the target machine.
2. **Bundled Build**: A fully self-contained single executable. It includes the entire .NET 8.0 runtime and native WPF libraries, so it runs on any 64-bit Windows PC without requiring any pre-installed framework.

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) installed on your system.

### Option A: Local Build Script (Recommended)
We have provided an automated PowerShell script `build.ps1` in the root directory. It stops any running instances of OmniDeck (to prevent file lock errors), cleans previous outputs, and publishes both versions.

To run it:
1. Open PowerShell in the root directory.
2. Execute the script:
   ```powershell
   ./build.ps1
   ```
3. Once completed, the outputs will be located in the `publish/` directory:
   - **Minimal**: `publish/minimal/OmniDeck.exe` (~1.0 MB)
   - **Bundled**: `publish/bundled/OmniDeck.exe` (~155 MB)

### Option B: Manual CLI Build
If you prefer running the commands manually:

* **For Minimal Build:**
  ```bash
  dotnet publish OmniDeck/OmniDeck.csproj -c Release -r win-x64 --self-contained false -o publish/minimal -p:PublishSingleFile=true -p:PublishReadyToRun=false -p:IncludeNativeLibrariesForSelfExtract=true
  ```

* **For Bundled Build:**
  ```bash
  dotnet publish OmniDeck/OmniDeck.csproj -c Release -r win-x64 --self-contained true -o publish/bundled -p:PublishSingleFile=true -p:PublishReadyToRun=false -p:IncludeNativeLibrariesForSelfExtract=true
  ```

---

## 🚀 How to Run the App

1. Navigate to the published directory of your choice (`publish/minimal` or `publish/bundled`).
2. Run the `OmniDeck.exe` executable.
3. **Application Setup**:
   - In the left sidebar under **Connection**, select the appropriate **Serial Port** (e.g. `COM3` or `COM4` corresponding to your Arduino).
   - Choose the matching **Baud Rate** (typically `9600`).
   - Click **Connect**.
   - Under **Volume Mixing Deck**, map your physical slider channels (e.g., `CH 0`, `CH 1`) to targets:
     - `master`: Global system volume.
     - `active_window`: Current active foreground application.
     - `active_not_mapped`: Foreground application, unless it is already explicitly assigned to another slider.
     - Any running application (e.g. `spotify.exe`, `discord.exe`, `chrome.exe`).
   - Click **Save Settings** to persist the configuration.

> [!NOTE]
> **Single Instance Policy**: OmniDeck enforces a single running instance. If you try to run another instance of `OmniDeck.exe` while one is already running, the new instance will broadcast a message to bring the existing window to the foreground and then exit immediately.

---

## ⚙️ CI/CD Pipeline

A standard GitHub Actions CI/CD configuration is available at `.github/workflows/build.yml`. On every push or pull request to `main`/`master`, it restores dependencies, compiles both versions, and packages them as downloadable build artifacts (`OmniDeck-Minimal-win-x64` and `OmniDeck-Bundled-win-x64`).
