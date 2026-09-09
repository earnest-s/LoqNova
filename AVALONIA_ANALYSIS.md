# LOQ Nova — Avalonia UI Migration Analysis

> **Generated:** 2026-08-26 | **Branch:** `feature/avalonia-ui`
> **Status:** Analysis only. No files were modified.

---

## 1. Existing Solution Architecture

### Projects (7 total)

| Project | Type | Framework | Role |
|---|---|---|---|
| **LoqNova.Lib** | Class Library | net8.0-windows | Core backend: hardware access, features, controllers, listeners, RGB, settings |
| **LoqNova.Lib.Automation** | Class Library | net8.0-windows | Automation pipeline system (triggers + steps) |
| **LoqNova.Lib.Macro** | Class Library | net8.0-windows | Macro recording/playback (keyboard/mouse hooks) |
| **LoqNova.CLI** | Console Exe | net8.0-windows | CLI tool (`ln.exe`) — named-pipe IPC client |
| **LoqNova.CLI.Lib** | Class Library | net8.0-windows | Shared IPC protocol types |
| **LoqNova.WPF** | WinExe | net8.0-windows | WPF frontend (the UI we are replacing) |
| **LoqNova.SpectrumTester** | Console Exe | net8.0-windows | Dev/testing tool (under Tools folder) |

### Dependency Graph

```
LoqNova.WPF
  ├── LoqNova.Lib
  ├── LoqNova.Lib.Automation → LoqNova.Lib.Macro → LoqNova.Lib
  ├── LoqNova.Lib.Macro
  └── LoqNova.CLI.Lib

LoqNova.CLI
  └── LoqNova.CLI.Lib

LoqNova.SpectrumTester
  └── LoqNova.Lib
```

### Key NuGet Dependencies

| Package | Used By | Purpose |
|---|---|---|
| Autofac 8.2.0 | Lib, WPF, Automation, Macro | IoC container |
| PubSub 4.0.2 | Lib | `MessagingCenter` pub/sub |
| NeoSmart.AsyncLock 3.2.1 | Lib, Automation, Macro | Async mutex |
| NAudio.Wasapi 2.2.1 | Lib | Microphone/speaker control |
| NvAPIWrapper.Net 0.8.1 | Lib | GPU monitoring/overclock |
| System.Management 9.0.2 | Lib | WMI access |
| Microsoft.Windows.CsWin32 0.3.183 | Lib | P/Invoke (HID, Win32) |
| Newtonsoft.Json 13.0.3 | Lib, CLI.Lib, Automation, Macro | Settings serialization |
| Octokit 14.0.0 | Lib | GitHub update checker |
| TaskScheduler 2.12.1 | Lib | Windows Task Scheduler |
| WindowsDisplayAPI 1.3.0 | Lib | Display mode control |
| CoordinateSharp 3.1.1.1 | Lib | Sunrise/sunset calculation |
| ManagedNativeWifi 2.7.0 | Lib | WiFi auto-listener |
| Ben.Demystifier 0.4.1 | Lib | Stack trace demystification |
| Markdig 0.40.0 | WPF | Markdown rendering (About page) |
| Markdig.Wpf 0.5.0.1 | WPF | WPF Markdown rendering |
| PixiEditor.ColorPicker 3.4.2 | WPF | RGB zone color pickers |
| WPF-UI 2.1.0 | WPF | Fluent-style WPF controls (NavigationStore, CardControl, Snackbar, TitleBar) |
| System.CommandLine 2.0.0-beta4 | CLI | CLI argument parsing |

### Platform Constraints

- **All projects target `net8.0-windows`** — platform-specific Windows APIs throughout
- `LoqNova.Lib` uses `AllowUnsafeBlocks`, `UseWindowsForms` (for screen capture, display APIs)
- `LoqNova.Lib.Macro` uses `UseWindowsForms` (for `SendInput`/low-level hooks)
- P/Invoke is pervasive: HID, WMI, Win32 messages, registry, UEFI variables, NVAPI

---

## 2. Backend Architecture Map

### Feature System (`IFeature<T>`)

The core abstraction for all hardware-controllable settings:

```
IFeature<T>
  ├── IsSupportedAsync() → bool
  ├── GetAllStatesAsync() → T[]
  ├── GetStateAsync() → T
  └── SetStateAsync(T) → void
```

**Implementation layers:**

| Base Class | Mechanism | Examples |
|---|---|---|
| `AbstractWmiFeature<T>` | WMI `GetSmartFanMode`/`SetSmartFanMode` style | PowerModeFeature, TouchpadLock, WinKey |
| `AbstractDriverFeature<T>` | Kernel driver IOCTL via `DeviceIoControl` | BatteryFeature, FnLock, AlwaysOnUSB, WhiteKB, OneLevelWhiteKB, PanelLogo |
| `AbstractCapabilityFeature<T>` | WMI `LenovoOtherMethod.GetFeatureValue` | FlipToStart, OverDrive, IGPUMode, InstantBoot |
| `AbstractUEFIFeature<T>` | UEFI firmware variable R/W | FlipToStart (UEFI path) |
| `AbstractLenovoLightingFeature<T>` | WMI `LenovoLightingMethod` | PanelLogo, PortsBacklight, WhiteKB (Lenovo Lighting path) |
| `AbstractCompositeFeature<T>` | Resolves between multiple `IFeature<T>` implementations | HybridModeFeature (GSync + iGPU), IGPUModeFeature (3 backends), PanelLogoBacklight, FlipToStart, OverDrive, InstantBoot, WhiteKeyboardBacklight |

**All 35+ features registered in `IoCModule`:**

| Feature | State Enum | Base | Notes |
|---|---|---|---|
| AlwaysOnUSB | `AlwaysOnUSBState` (Off/OnWhenSleeping/OnAlways) | Driver | |
| Battery | `BatteryState` (Normal/RapidCharge/Conservation) | Driver | + registry sync |
| BatteryNightCharge | `BatteryNightChargeState` | Driver | |
| DpiScale | `DpiScale` | DirectDisplay | |
| FnLock | `FnLockState` | Driver | |
| GSync | `GSyncState` | WMI | Part of HybridMode |
| HDR | `HDRState` | DirectDisplay | |
| HybridMode | `HybridModeState` | Composite | GSync + iGPU |
| iGPUMode | `IGPUModeState` | Composite | 3 backends |
| InstantBoot | `InstantBootState` | Composite | Capability + FeatureFlags |
| Microphone | `MicrophoneState` | NAudio | Mute/unmute |
| OneLevelWhiteKB | `OneLevelWhiteKBState` | Driver | |
| OverDrive | `OverDriveState` | Composite | Capability + GameZone |
| PanelLogoBacklight | `PanelLogoBacklightState` | Composite | LenovoLighting + Spectrum |
| PortsBacklight | `PortsBacklightState` | LenovoLighting | |
| PowerMode | `PowerModeState` | WMI | Central orchestrator |
| RefreshRate | `RefreshRate` | DirectDisplay | |
| Resolution | `Resolution` | DirectDisplay | |
| Speaker | `SpeakerState` | NAudio | Mute/unmute |
| TouchpadLock | `TouchpadLockState` | WMI GameZone | |
| WinKey | `WinKeyState` | WMI GameZone | |
| WhiteKBBacklight | `WhiteKeyboardBacklightState` | Composite | LenovoLighting + Driver |
| FlipToStart | `FlipToStartState` | Composite | Capability + UEFI |

### Controller System

Controllers manage complex hardware subsystems that go beyond simple get/set:

| Controller | Responsibility |
|---|---|
| `RGBKeyboardBacklightController` | RGB keyboard orchestration (presets, effects, transitions, ownership) |
| `RgbFrameDispatcher` | Central HID writer, frame rendering, override gating |
| `CustomRGBEffectController` | Custom effect lifecycle (start/stop/resume) |
| `SpectrumKeyboardBacklightController` | Spectrum-brand keyboard (alternative HID protocol) |
| `GPUController` | NVAPI dGPU monitoring, process management |
| `GPUOverclockController` | NVAPI PState20 overclock |
| `GodModeController` | Composite (V1/V2) GPU power limit control |
| `SensorsController` | Composite (V1/V2/V3) CPU/GPU/fan sensor readings |
| `DisplayBrightnessController` | Display brightness control |
| `WindowsPowerModeController` | Windows power overlay schemes |
| `WindowsPowerPlanController` | Windows power plan activation |
| `AIController` | Lenovo AI engine (AI Sense) |
| `SmartFnLockController` | Smart FnLock modifier key logic |

### Listener System (`IListener<T>`)

Listeners subscribe to hardware/system events and fire `Changed` events:

| Listener | Source | Fires On |
|---|---|---|
| `PowerModeListener` | Win32 `PowerSettingRegisterNotification` | Windows power mode changes |
| `PowerStateListener` | Win32 `PowerSettingRegisterNotification` | AC connect/disconnect |
| `ThermalModeListener` | WMI `LENOVO_GAMEZONE_THERMAL_MODE_EVENT` | Thermal mode changes (AC-driven) |
| `WinKeyListener` | WMI `LenovoGameZoneData` | Win key lock status |
| `DisplayBrightnessListener` | WMI | Display brightness changes |
| `DisplayConfigurationListener` | WMI | Display config changes (resolution, refresh, DPI) |
| `LightingChangeListener` | WMI `LenovoLightingMethod` | Panel logo / ports backlight changes |
| `RGBKeyboardBacklightListener` | WMI `LenovoGameZoneLightProfileChangeEvent` | RGB preset changes (Fn+Q cycle) |
| `SpecialKeyListener` | WMI `LenovoGameZoneData` | Fn key combos (Fn+N, Fn+F9, FnLock, etc.) |
| `DriverKeyListener` | Event Log | Driver-level key events (CapsLock, NumLock, Touchpad, Mic, Speaker, FnSpace) |
| `SystemThemeListener` | Registry change notification | Windows light/dark mode |
| `SessionLockUnlockListener` | Win32 `SessionSwitch` | Session lock/unlock |
| `NativeWindowsMessageListener` | Win32 WndProc (abstract) | Device arrival, power events, display device changes, monitor power-off |

**Auto-activated listeners** (registered with `AutoActivateListener()`): All 13 listeners above are auto-activated by the DI container — they start listening immediately on app launch.

### Service System

| Service | Responsibility |
|---|---|
| `VolumeBrightnessReactiveRgbService` | Volume/brightness → temporary RGB visualization |
| `BatteryDischargeRateMonitorService` | Polls battery discharge rate every 3s |
| `AutomationProcessor` | Pipeline engine: evaluates triggers, runs steps |
| `MacroController` | Low-level keyboard/mouse hook, record/playback |
| `IpcServer` | Named-pipe IPC for CLI communication |

### Settings System

All settings extend `AbstractSettings<T>` — generic JSON serialization to `%LOCALAPPDATA%\LOQNova\`:

| Settings | File | Contents |
|---|---|---|
| `ApplicationSettings` | `settings.json` | Theme, accent, language, window size, notifications, autorun, tray behavior, smart key actions |
| `BalanceModeSettings` | `balancemode.json` | Balance mode presets (CPU/GPU/Thermal targets) |
| `GodModeSettings` | `godmode.json` | GodMode power limits (PL1, PL2, GPU power, thermal) |
| `GPUOverclockSettings` | `gpu_oc.json` | GPU core/memory overclock deltas |
| `IntegrationsSettings` | `integrations.json` | HWiNFO, CLI enabled flags |
| `PackageDownloaderSettings` | `package_downloader.json` | Download path, hidden packages, only-show-updates |
| `RGBKeyboardSettings` | `rgb_keyboard.json` | Selected preset + 4 preset descriptions |
| `SpectrumKeyboardSettings` | `spectrum_keyboard.json` | Spectrum keyboard profiles |
| `SunriseSunsetSettings` | `sunrise_sunset.json` | Lat/lon for sunrise/sunset calculation |
| `UpdateCheckSettings` | `update_check.json` | Last check datetime, frequency |
| `DashboardSettings` | `dashboard.json` | Dashboard groups, show/hide sensors (WPF-only) |
| `AutomationSettings` | `automation.json` | Pipelines, enabled state |
| `MacroSettings` | `macro.json` | Macro sequences, enabled state |

### Messaging System

`MessagingCenter` (PubSub library) — topic-based pub/sub with weak references:

- `NotificationMessage(NotificationType, args)` → triggers OSD notifications
- `SpectrumBacklightChangedMessage` → spectrum state changed
- `RGBKeyboardBacklightChangedMessage` → RGB state changed
- `BrightnessChangedMessage` → display brightness changed

### Device Access Layer

| Access | Mechanism | Files |
|---|---|---|
| HID keyboard | `HidD_SetFeature` via CsWin32 | `RgbFrameDispatcher`, `Devices.GetRGBKeyboard()` |
| WMI GameZone | `ManagementObjectSearcher` | `WMI.LenovoGameZoneData`, `WMI.LenovoGameZoneThermalModeEvent` |
| WMI OtherMethod | `ManagementObjectSearcher` | `WMI.LenovoOtherMethod` (capabilities, feature flags) |
| WMI Lighting | `ManagementObjectSearcher` | `WMI.LenovoLightingMethod` |
| Driver IOCTL | `DeviceIoControl` via CsWin32 | `Drivers.GetEnergy`, `Drivers.GetEnlightenment` |
| NVAPI | NvAPIWrapper.Net | `GPUController`, `GPUOverclockController` |
| UEFI | `GetFirmwareEnvironmentVariableEx` | `FlipToStartUEFIFeature` |
| DirectDisplay | WindowsDisplayAPI | `HDRFeature`, `RefreshRateFeature`, `ResolutionFeature`, `DpiScaleFeature` |
| NAudio | Wasapi MMDeviceEnumerator | `MicrophoneFeature`, `SpeakerFeature` |
| Registry | `Microsoft.Win32.Registry` | `BatteryFeature` (Vantage sync), `SystemThemeListener` |

---

## 3. WPF UI Architecture Map

### Application Lifecycle

```
App.xaml.cs Application_Startup
  ├── Parse CLI flags (Flags)
  ├── Single-instance mutex + EventWaitHandle
  ├── PowerOptimization.Apply() (EcoQoS bypass, timer resolution)
  ├── LocalizationHelper.SetLanguageAsync
  ├── Compatibility check (basic + extended)
  ├── IoCContainer.Initialize (4 modules: Lib, Automation, Macro, WPF)
  ├── Feature init sequence:
  │   ├── PowerModeFeature (ensure GodMode state, correct power plan)
  │   ├── BatteryFeature (ensure correct charge mode)
  │   ├── RGBKeyboardBacklightController (set light control owner, restore preset)
  │   ├── SpectrumKeyboardBacklightController (start Aurora if needed)
  │   ├── GPUOverclockController (ensure overclock applied)
  │   ├── HybridModeFeature (ensure dGPU ejected if needed)
  │   ├── AutomationProcessor (initialize + run-on-startup pipelines)
  │   └── MacroController (start hooks)
  ├── Service startup:
  │   ├── AIController
  │   ├── HWiNFOIntegration
  │   ├── IpcServer
  │   ├── BatteryDischargeRateMonitorService
  │   └── VolumeBrightnessReactiveRgbService
  ├── Autorun.Validate
  ├── MainWindow creation + show (or minimize to tray)
  └── ThemeManager.Apply

App.ShutdownAsync
  ├── AIController.StopAsync
  ├── RGBKeyboardBacklightController.SetLightControlOwner(false)
  ├── SpectrumKeyboardBacklightController.StopAurora
  ├── NativeWindowsMessageListener.StopAsync
  ├── SessionLockUnlockListener.StopAsync
  ├── HWiNFOIntegration.StopAsync
  ├── IpcServer.StopAsync
  ├── BatteryDischargeRateMonitorService.StopAsync
  ├── VolumeBrightnessReactiveRgbService.StopAsync
  └── Application.Shutdown
```

### Navigation Architecture

**WPF-UI `NavigationStore`** — left sidebar with icon + label, `Frame`-based page switching:

```
NavigationStore
  ├── DashboardPage        (Home24 icon)
  ├── KeyboardBacklightPage (Keyboard24 icon) [hidden if unsupported]
  ├── BatteryPage          (BatteryCheckmark24 icon)
  ├── AutomationPage       (Rocket24 icon)
  ├── MacroPage            (ReceiptPlay24 icon)
  ├── PackagesPage         (Box24 icon)
  ├── ── separator ──
  ├── SettingsPage         (Settings24 icon) [Footer]
  └── AboutPage            (Info24 icon) [Footer]
```

Navigation supports:
- Ctrl+Tab / Ctrl+Shift+Tab (next/previous)
- Ctrl+1 through Ctrl+8 (direct page)
- Tray context menu (page items)
- `SmartKeyHelper` (Fn+N → bring to foreground, Fn+F9 → configurable action)

### Window Architecture

```
MainWindow (BaseWindow)
  ├── TitleBar (WPF-UI)
  ├── Device info badge (model name)
  ├── Log badge (if tracing)
  ├── NavigationStore + Frame
  ├── Snackbar (toast notifications)
  ├── Vantage indicator (orange bar)
  ├── LegionZone indicator (orange bar)
  └── FnKeys indicator (orange bar)
```

**Dialog windows (21 total):**

| Window | Purpose |
|---|---|
| `AddDashboardItemWindow` | Add widget to dashboard |
| `BalanceModeSettingsWindow` | Balance mode AI toggle |
| `EditDashboardWindow` | Reorder/add/remove dashboard groups |
| `ExtendedHybridModeInfoWindow` | Hybrid mode explanation |
| `GodModeSettingsWindow` | GodMode power limit sliders |
| `OverclockDiscreteGPUSettingsWindow` | GPU overclock core/memory sliders |
| `AddAutomationStepWindow` | Add step to automation pipeline |
| `AutomationPipelineTriggerConfigurationWindow` | Configure trigger parameters |
| `CreateAutomationPipelineWindow` | Select trigger type for new pipeline |
| `BootLogoWindow` | Custom boot logo |
| `ExcludeRefreshRatesWindow` | Exclude specific refresh rates |
| `NotificationsSettingsWindow` | Per-notification-type toggles |
| `SelectSmartKeyPipelinesWindow` | Select smart key action pipeline |
| `WindowsPowerModesWindow` | Windows power mode mapping |
| `WindowsPowerPlansWindow` | Windows power plan mapping |
| `DeviceInformationWindow` | Full system info display |
| `LanguageSelectorWindow` | Language selection |
| `StatusWindow` | Tray tooltip status display |
| `SymbolRegularPicker` | Icon picker for automation |
| `UnsupportedWindow` | Compatibility override dialog |
| `UpdateWindow` | Update notification |
| `NotificationWindow` / `NotificationAoTWindow` | OSD popups |
| `MacroRecordingWindow` | Macro recording UI |
| `SpectrumKeyboardBacklightEditEffectWindow` | Spectrum effect editor |

---

## 4. Complete WPF Feature Inventory

### Page: Dashboard

**Purpose:** Configurable grid of hardware control widgets + real-time sensor telemetry

**Controls:**
- `SensorsControl` — CPU/GPU utilization, temperature, fan speed bars with animated transitions
- `DashboardGroupControl` — dynamically created from `DashboardSettings.Store.Groups`
- 28+ dashboard widget types (each is a feature card):

| Widget | Feature | Control Type |
|---|---|---|
| AlwaysOnUSB | `AlwaysOnUSBState` | ComboBox card |
| BatteryMode | `BatteryState` | ComboBox card |
| BatteryNightCharge | `BatteryNightChargeState` | Toggle card |
| DpiScale | `DpiScale` | ComboBox card (hidden if <2 options) |
| FlipToStart | `FlipToStartState` | Toggle card |
| FnLock | `FnLockState` | Toggle card |
| HDR | `HDRState` | Toggle card (with "blocked" warning) |
| HybridMode | `HybridModeState` | ComboBox or Toggle (depends on IGPU support) |
| InstantBoot | `InstantBootState` | ComboBox card |
| Microphone | `MicrophoneState` | Toggle card |
| OneLevelWhiteKB | `OneLevelWhiteKBState` | Toggle card |
| OverDrive | `OverDriveState` | Toggle card |
| PanelLogoBacklight | `PanelLogoBacklightState` | Toggle card |
| PortsBacklight | `PortsBacklightState` | Toggle card |
| PowerMode | `PowerModeState` | ComboBox card + config button (GodMode settings) |
| RefreshRate | `RefreshRate` | ComboBox card (hidden if <2 options) |
| Resolution | `Resolution` | ComboBox card (hidden if <2 options) |
| TouchpadLock | `TouchpadLockState` | Toggle card |
| WhiteKBBacklight | `WhiteKeyboardBacklightState` | ComboBox card |
| WinKey | `WinKeyState` | Toggle card |
| DiscreteGPU | — | Custom card (GPU status, processes, restart/kill) |
| OverclockDiscreteGPU | — | Toggle + config button (GPU overclock) |
| TurnOffMonitors | — | Button card |
| GodModeValue | — | Slider + ComboBox card (per-power-limit) |

**Backend dependencies:** ISensorsController, GPUController, GPUOverclockController, PowerModeFeature, BatteryFeature, all IFeature<T> implementations, NativeWindowsMessageListener, DisplayConfigurationListener, SpecialKeyListener, DriverKeyListener, LightingChangeListener, WinKeyListener, PowerModeListener, ThermalModeListener

**Dashboard editing:** EditDashboardWindow with drag-and-reorder, add/remove groups, per-group item management

### Page: Keyboard Backlight

**Purpose:** Full RGB keyboard customization (or Spectrum keyboard control)

**Controls (RGB path):**
- 5 preset buttons (Off, 1, 2, 3, 4)
- Brightness ComboBox (Low/High)
- Effect ComboBox (Static, Breath, Wave R→L, Wave L→R, Smooth, + 12 custom effects)
- Speed ComboBox (Slowest, Slow, Fast, Fastest)
- 4x ColorPickerControl (Zone 1–4) with "Synchronise zones" context menu
- LoqKeyboardPreview (live keyboard visualization)
- Vantage warning InfoBar

**Controls (Spectrum path):**
- SpectrumKeyboardBacklightControl (profile management)
- SpectrumKeyboardEffectControl (effect selection)
- Per-device zone controls (keyboard, front, full, ANSI/ISO/JIS layouts)

**Custom RGB Effects available:**
Ambient, AudioVisualizer, BreathingColorCycle, Christmas, Disco, Fade, Lightning, RainbowWave, Ripple, Strobe, Swipe, Temperature

**Backend dependencies:** RGBKeyboardBacklightController, SpectrumKeyboardBacklightController, CustomRGBEffectController, RgbFrameDispatcher, VantageDisabler, ISensorsController

### Page: Battery

**Purpose:** Battery telemetry display (read-only)

**Controls:**
- Battery icon (10-level dynamic symbol)
- Percentage, status text, low battery warning, low wattage charger warning
- Battery temperature card (C/F toggle)
- Discharge rate / min discharge / max discharge cards
- Current capacity / full charge capacity / design capacity cards
- Battery health percentage
- On-battery-since timestamp + duration
- Cycle count
- Manufacture date / first use date (conditionally shown)

**Backend dependencies:** `Battery.GetBatteryInformation()`, `Power.IsPowerAdapterConnectedAsync()`, `Battery.GetOnBatterySince()`, ApplicationSettings (temperature unit)

**Polling:** 2-second interval while visible, cancelled when hidden

### Page: Actions (Automation)

**Purpose:** Pipeline builder — automatic triggers + manual quick actions

**Controls:**
- Enable/disable automatic pipelines toggle
- Automatic pipelines list (with trigger configuration)
- Manual pipelines list (Quick Actions)
- Save/Revert buttons
- Per-pipeline: name, icon, step list, add/delete steps, reorder

**37 automation steps:** AlwaysOnUSB, Battery, BatteryNightCharge, DeactivateGPU, Delay, DisplayBrightness, DpiScale, FlipToStart, FnLock, GodModePreset, HDR, HybridMode, InstantBoot, Macro, Microphone, Notification, OneLevelWhiteKB, OverclockDiscreteGPU, OverDrive, PanelLogoBacklight, PlaySound, PortsBacklight, PowerMode, QuickAction, RefreshRate, Resolution, RGBKeyboardBacklight, Run (external process), SpectrumBrightness, SpectrumProfile, SpectrumImportProfile, TouchpadLock, TurnOffMonitors, TurnOffWiFi, TurnOnWiFi, WhiteKBBacklight, WinKey

**Trigger types:** GameAutoListener, InstanceStarted/Stopped, ProcessAutoListener, TimeAutoListener, UserInactivityAutoListener, WiFiAutoListener

**Backend dependencies:** AutomationProcessor, all IAutomationStep implementations, all IAutomationPipelineTrigger implementations

### Page: Macro

**Purpose:** Keyboard/macro recording and playback

**Controls:**
- Enable/disable macro toggle
- Number pad buttons (0–9, select which key to configure)
- MacroSequenceControl (per-key macro editor)
- Record button, playback

**Backend dependencies:** MacroController

### Page: Packages

**Purpose:** Lenovo driver/firmware package downloader

**Controls:**
- Machine type input
- OS selector ComboBox
- Source radio (Vantage primary / PCSupport secondary)
- Only-show-updates checkbox
- Filter text box
- Sorting ComboBox (name, category, date)
- Download location path + browse
- Download / Cancel buttons
- Package list (PackageControl per package)
- Hide / HideAll context menu
- Progress bar

**Backend dependencies:** PackageDownloaderFactory, PackageDownloaderSettings

### Page: Settings

**Purpose:** Application configuration

**Controls (grouped):**

*General:*
- Language selector
- Temperature unit (C/F)
- Theme (System/Dark/Light)
- Accent color source (System/Custom) + color picker
- Autorun (Disabled/Enabled)
- Minimize to tray toggle
- Minimize on close toggle

*Device Integration:*
- Vantage disable toggle
- LegionZone disable toggle
- FnKeys disable toggle
- Smart FnLock modifier key ComboBox
- Smart key single/double press action selectors
- GodMode Fn+Q switchable toggle
- Power mode mapping mode

*Notifications:*
- Enable/disable all notifications
- Per-type toggles (AC, FnLock, CapsLock, Microphone, Keyboard, RefreshRate, Touchpad, Update, SmartKey, AlwaysOnTop, AllScreens, Duration, Position)
- NotificationSettingsWindow (advanced)

*Integrations:*
- CLI (named-pipe IPC) enable/disable
- HWiNFO integration enable/disable
- HWiNFO shared memory path

*System:*
- Synchronize brightness to all power plans toggle
- Reset battery-on-since on reboot toggle
- Boot logo card (if supported)
- Windows power modes mapping
- Windows power plans mapping
- Exclude refresh rates
- Check for updates button
- Open log folder button

*About:*
- Launches AboutPage

### Page: About

**Purpose:** App info, credits, licenses, donation

**Controls:**
- App name, version, build date
- GitHub link, update check
- Localized license
- Open-source licenses (Markdown rendered via Markdig)
- Donation section (PayPal)

### Page: Donate

**Purpose:** Donation links (PayPal)

### System Tray

**Controls:**
- NotifyIcon with tooltip (StatusWindow — shows battery, power mode, fan speeds)
- Context menu: page navigation, Open, Close
- Dynamic automation quick-action items (no-trigger pipelines)

### OSD Notifications

**Architecture:** `MessagingCenter` → `NotificationsManager` → `NotificationWindow` / `NotificationAoTWindow`

- Per-type enable/disable via settings
- 250ms debounce per type
- PowerMode mode-identity gate (deduplicates)
- Configurable: position (top/bottom left/right/center), duration (short/normal/long), always-on-top, all-screens
- Fullscreen detection (suppress when app fullscreen)

---

## 5. WPF UI → Backend Dependency Map

### Dashboard → Backend

| Dashboard Widget | UI Code-Behind | Backend Call | Event Listener |
|---|---|---|---|
| PowerMode | `PowerModeControl.cs` | `PowerModeFeature.SetStateAsync()` | `ThermalModeListener`, `PowerModeListener` |
| BatteryMode | `BatteryModeControl.cs` | `BatteryFeature.SetStateAsync()` | — |
| FnLock | `FnLockControl.cs` | `FnLockFeature.SetStateAsync()` | `SpecialKeyListener` |
| WinKey | `WinKeyControl.cs` | `WinKeyFeature.SetStateAsync()` | `WinKeyListener` |
| Microphone | `MicrophoneControl.cs` | `MicrophoneFeature.SetStateAsync()` | `DriverKeyListener` (FnF4) |
| TouchpadLock | `TouchpadLockControl.cs` | `TouchpadLockFeature.SetStateAsync()` | `DriverKeyListener` (FnF10) |
| DiscreteGPU | `DiscreteGPUControl.xaml.cs` | `GPUController.StartAsync()` | `NativeWindowsMessageListener` |
| OverclockGPU | `OverclockDiscreteGPUControl.cs` | `GPUOverclockController` | `NativeWindowsMessageListener` |
| Sensors | `SensorsControl.xaml.cs` | `ISensorsController` polling | `PowerModeListener` (for accent color) |
| All toggle/combo features | `AbstractToggleFeatureCardControl` / `AbstractComboBoxFeatureCardControl` | `IFeature<T>.SetStateAsync()` | Per-feature listener (if any) |

### Keyboard → Backend

| UI Action | Backend Call |
|---|---|
| Preset button click | `RGBKeyboardBacklightController.SetPresetAsync()` |
| Effect/Speed/Brightness change | `RGBKeyboardBacklightController.SetStateAsync()` |
| Zone color change | `RGBKeyboardBacklightController.SetStateAsync()` |
| Frame preview update | `RgbFrameDispatcher.FrameRendered` event → `LoqKeyboardPreview.UpdateZones()` |

### Settings → Backend

| UI Control | Backend Call |
|---|---|
| Theme ComboBox | `ThemeManager.Apply()` (WPF-UI theme API) |
| Accent color picker | `ThemeManager.Apply()` → `Wpf.Ui.Appearance.Accent.Apply()` |
| Autorun ComboBox | `Autorun.SetState()` |
| Vantage disable toggle | `VantageDisabler.SetStatusAsync()` |
| Check for updates | `UpdateChecker.CheckForUpdateAsync()` |
| CLI toggle | `IpcServer.StartStopIfNeededAsync()` |
| HWiNFO toggle | `HWiNFOIntegration.StartStopIfNeededAsync()` |

---

## 6. RGB Architecture Map

### Pipeline Overview

```
User/Firmware Event
  ↓
RGBKeyboardBacklightController (orchestrator)
  ├── SetPresetAsync / SetStateAsync / SetNextPresetAsync
  ├── PlayTransitionAsync (performance mode strobe)
  └── HandleCustomEffectAsync (start software effect)
  ↓
RgbFrameDispatcher (central writer)
  ├── SendFirmwareCommandAsync() — raw HID (preset change, off)
  ├── RenderAsync() — normal (custom effects), gated by IsOverrideActive
  ├── ForceRenderAsync() — override (strobe, volume/brightness), always writes
  └── RenderPreviewOnly() — UI preview only, no HID
  ↓
WriteRawState() → PInvoke.HidD_SetFeature(handle, ptr, 30 bytes)
  ↓
Lenovo RGB Keyboard HID Device (VID=0x048D, PID=0xC9xx)
```

### HID Protocol

30-byte `LENOVO_RGB_KEYBOARD_STATE` struct:
```
Header[2]    = [0xCC, 0x16]
Effect       = byte (0=off, 1=static, 3=breath, 4=wave, 6=smooth)
Speed        = byte (1-4)
Brightness   = byte (0=off, 1=low, 2=high)
Zone1Rgb[3]  = [R, G, B]
Zone2Rgb[3]  = [R, G, B]
Zone3Rgb[3]  = [R, G, B]
Zone4Rgb[3]  = [R, G, B]
Padding      = byte
WaveLTR      = byte
WaveRTL      = byte
Unused[13]
```

### RGB Categories

#### 1. Normal Persistent RGB Effects

These are the user-configured, saved-to-settings effects that persist across sessions:

- **Firmware effects:** Static, Breath, Wave (R→L, L→R), Smooth — handled by keyboard firmware, only initial command needed
- **Custom software effects:** Ambient, AudioVisualizer, BreathingColorCycle, Christmas, Disco, Fade, Lightning, RainbowWave, Ripple, Strobe, Swipe, Temperature — continuous software-rendered animation loop

State stored in: `RGBKeyboardSettings.Store.State` → `RGBKeyboardBacklightState` containing `SelectedPreset` + `Dictionary<RGBKeyboardBacklightPreset, RGBKeyboardBacklightBacklightPresetDescription>`

#### 2. Temporary RGB Events

Short-lived RGB takeovers that pause the current effect and restore it after:

| Event | Source | Duration | Frame Method | Resume Method |
|---|---|---|---|---|
| Performance Mode Strobe | `PowerModeFeature` → `RGBKeyboardBacklightController.PlayTransitionAsync` | ~2.5s (3 pulses + 0.5s black) | `ForceRenderAsync` | `ResumeAfterTransitionAsync` (custom: `ResumeFromOverrideAsync` or firmware: `SendFirmwareCommandAsync`) |
| Volume/Brightness Reactive | `VolumeBrightnessReactiveRgbService` | Event-driven, 900ms hold after last change | `ForceRenderAsync` | `ResumeFromOverrideAsync` (custom) or `RefreshCurrentPresetAsync` (firmware) |

**Ownership coordination:** `RgbFrameDispatcher.IsOverrideActive` flag
- When `true`, `RenderAsync()` silently drops frames (custom effects can't fight the override)
- `ForceRenderAsync()` ignores the flag (override always writes)
- Priority: Performance strobe > Volume/Brightness event (if strobe starts during volume event, the volume service yields immediately)

#### 3. Hardware/Firmware Effects

Effects handled entirely by the keyboard's internal controller after a single HID command:
- Static (solid zone colors)
- Breath (pulsing)
- Wave RTL / Wave LTR (directional wave)
- Smooth (color cycling)

The WPF UI "simulates" these in the keyboard preview using frame-by-frame rendering, but actual keyboard runs them in firmware.

#### 4. Software-Generated Effects

Continuous animation loop managed by `CustomRGBEffectController`:

```
CustomRGBEffectController.StartEffectAsync(effect)
  → Sets WMI light control ownership
  → Sends static mode command (firmware enters manual zone-color mode)
  → Spawns effect.RunAsync(cancellationToken)
    → effect calls SetColorsAsync(ZoneColors) per frame
      → RgbFrameDispatcher.RenderAsync(zones)
        → HID write + FrameRendered event
```

Effect interface (`ICustomRGBEffect`):
- `RunAsync(CancellationToken)` — main animation loop
- `SetColorsAsync(ZoneColors)` / `SetSolidColorAsync(RGBColor)` / `SetZoneAsync(int, RGBColor)` / `TransitionColorsAsync(RGBColor, int, int)` — frame output methods

Signal providers for effects that need input:
- `IInputSignalProvider` (keyboard input → Fade/Ripple effects)
- `IScreenColorProvider` (screen color → Ambient effect; GDI or DXGI capture)

### Keyboard Preview

`LoqKeyboardPreview` subscribes to `RgbFrameDispatcher.FrameRendered` event and updates a visual keyboard representation with per-zone colors. It receives frames from all three render paths.

---

## 7. Performance Mode Architecture Map

### Triggers

| Trigger | Entry Point | Code Path |
|---|---|---|
| UI dropdown | `PowerModeControl` → `IFeature.SetStateAsync()` | `PowerModeFeature.SetStateAsync()` |
| Fn+Q hardware key | `PowerModeListener.Changed` → `PowerModeFeature.ApplyPerformanceModeAsync()` | Direct |
| AC connect (thermal event) | `ThermalModeListener.Changed` → `PowerModeFeature.AnnounceModeChangeAsync()` | Strobe only (no write) |
| AC disconnect (thermal event) | `ThermalModeListener.Changed` → `PowerModeFeature.AnnounceModeChangeAsync()` | Strobe only (no write) |
| Automation step | `PowerModeAutomationStep` → `PowerModeFeature.SetStateAsync()` | Same as UI |

### SetStateAsync Flow (UI/Automation path)

```
PowerModeFeature.SetStateAsync(targetMode)
  ├── Validate mode is supported
  ├── Check AC requirement (Performance/GodMode need AC unless AllowAllPowerModesOnBattery)
  ├── PublishNotification(mode) → MessagingCenter → OSD
  ├── FireStrobeAsync(mode) → fire-and-forget
  │   ├── Acquire _strobeGuard (Interlocked)
  │   ├── Record _lastStrobeMode + _lastStrobeUtc (dedupe)
  │   └── RGBKeyboardBacklightController.PlayTransitionAsync(mode)
  ├── Handle switching bugs (Quiet→Performance, GodMode→Other)
  │   └── thermalModeListener.SuppressNext() + delay
  ├── thermalModeListener.SuppressNext()
  ├── base.SetStateAsync(targetMode) → WMI write
  ├── ApplyDependenciesAsync(mode)
  │   ├── GodMode: godModeController.ApplyStateAsync()
  │   ├── WindowsPowerModeController.SetPowerModeAsync(mode)
  │   └── WindowsPowerPlanController.SetPowerPlanAsync(mode)
  └── powerModeListener.NotifyAsync(mode)
```

### AnnounceModeChangeAsync Flow (AC-driven firmware changes)

```
ThermalModeListener.Changed (thermal event maps to PowerModeState)
  └── PowerModeFeature.AnnounceModeChangeAsync(mode)
      ├── Dedupe check: _lastStrobeMode == mode && within 2500ms → SKIP
      └── FireStrobeAsync(mode) (same as above, but NO WMI write, NO dependencies)
```

### PlayTransitionAsync Flow (RGB strobe)

```
RGBKeyboardBacklightController.PlayTransitionAsync(mode)
  ├── Cancel any in-flight transition
  ├── Pause running custom effect: dispatcher.IsOverrideActive = true
  ├── Spawn RunTransitionAsync on background task
  │   ├── Save brightness, set to max (2)
  │   ├── PerformanceModeTransitionEffect.PlayAsync(dispatcher, modeColor, token)
  │   │   └── 3 pulses: flash → sine breath → dark gap (total ~2.5s)
  │   │       via ForceRenderAsync per frame
  │   ├── Safety black frame
  │   └── ResumeAfterTransitionAsync()
  │       ├── If preset == Off → send off command
  │       ├── If custom effect running → customEffectController.ResumeFromOverrideAsync()
  │       ├── If custom effect not running → dispatcher.IsOverrideActive=false, restart effect
  │       └── If firmware preset → dispatcher.IsOverrideActive=false, re-send firmware command + preview
```

### Color Mapping

Centralized in `RgbFrameDispatcher.GetPerformanceModeColor()`:
- Quiet → Blue (0, 120, 255)
- Balance → White (255, 255, 255)
- Performance → Red (255, 0, 0)
- GodMode → Purple (180, 0, 255)

Used by: strobe animation, dashboard sensor bars (`PerformanceModeColors`), OSD notification icons.

---

## 8. Volume/Brightness Reactive Architecture Map

### Service: `VolumeBrightnessReactiveRgbService`

**Lifecycle:** Started via `StartStopIfNeededAsync()` at app startup, stopped at shutdown.

### Volume Event Flow

```
VolumeChangeEvent (NAudio MMNotificationClient)
  → VolumeBrightnessReactiveRgbService
  ├── Check: RGBKeyboardBacklightController.IsSupportedAsync()
  ├── Check: rgbKeyboardBacklightController.IsTransitionActive → yield if strobe running
  ├── RunTemporaryEventAsync(volumePercentage)
  │   ├── Acquire RgbFrameDispatcher.IsOverrideActive = true
  │   ├── Calculate 4-zone level meter:
  │   │   Zone1 = left-most (0–25%)
  │   │   Zone2 = left-center (25–50%)
  │   │   Zone3 = right-center (50–75%)
  │   │   Zone4 = right-most (75–100%)
  │   ├── Color gradient: Green → Yellow → Orange → Red (based on level)
  │   ├── Render via ForceRenderAsync at ~30 FPS
  │   ├── Hold 900ms after last change (debounce)
  │   └── Release IsOverrideActive
  └── Restore previous RGB state:
      ├── If custom effect was running → ResumeFromOverrideAsync()
      └── If firmware preset → RefreshCurrentPresetAsync()
```

### Brightness Event Flow

Identical architecture to volume — same 4-zone level meter, same color gradient, same temporary ownership, same restore path. Triggered by `DisplayBrightnessListener.Changed`.

### Restoration Path (aligned with Performance Strobe)

The restore path was deliberately unified with the Performance Mode strobe's `ResumeAfterTransitionAsync()`:

1. Check if a custom effect is running:
   - Yes → `CustomRGBEffectController.ResumeFromOverrideAsync()` (resumes animation from last frame state)
   - No → `RgbFrameDispatcher.IsOverrideActive = false` + restart the preset's effect
2. If firmware preset (not custom): re-send firmware command + `RenderPreviewOnly()`

This ensures both the strobe and the reactive visualization use identical restoration logic.

---

## 9. Application Lifecycle Map

### Startup Sequence

1. **Parse flags** — CLI args (trace, proxy, minified, force-disable flags, etc.)
2. **Single instance** — Mutex + EventWaitHandle; second instance signals first and exits
3. **Power optimization** — EcoQoS bypass, timer resolution, AboveNormal priority
4. **Localization** — Set language from settings
5. **Compatibility** — Basic check (WMI presence) + extended check (vendor/model match)
6. **DI container** — 4 IoCModules loaded (Lib, Automation, Macro, WPF)
7. **Feature initialization** — Each hardware feature verified and synchronized
8. **Service startup** — AI, HWiNFO, IPC, battery monitor, reactive RGB
9. **Autorun validation** — Scheduled task check
10. **MainWindow creation** — Navigation, tray, theme, indicators

### Shutdown Sequence

1. Each service stopped individually (try/catch per service)
2. RGB keyboard ownership released (lights return to firmware control)
3. Named pipes closed
4. Listeners stopped
5. `Application.Shutdown()`

### Efficiency Mode

When minimized to tray:
- Process priority → IDLE_PRIORITY_CLASS
- Power throttling → EXECUTION_SPEED throttled
- Reversed on restore to Normal priority

---

## 10. WPF-Specific Dependencies

### WPF-UI Package (Lepo)

The entire UI is built on WPF-UI controls:
- `NavigationStore` — sidebar navigation
- `CardControl` / `CardExpander` — all feature cards
- `TitleBar` — custom window chrome
- `Snackbar` — toast notifications
- `Button`, `MenuItem`, `ComboBox`, `ToggleSwitch` — all standard controls
- `SymbolIcon` / `SymbolRegular` — icon system (200+ icons)
- `InfoBar` — warning/info banners
- `UiPage` — page base class
- `Theme` / `Accent` — dynamic theming (Mica background)

### WPF-Specific Patterns

1. **Code-behind heavy** — no MVVM; all logic in `.xaml.cs` files
2. **`Dispatcher.Invoke` everywhere** — every listener callback marshals to UI thread
3. **`Visibility` enum** — `Visible`/`Collapsed`/`Hidden` for show/hide
4. **`DependencyProperty`** — used in some controls but minimal binding
5. **`Frame` + `NavigationStore`** — WPF page navigation
6. **`ContextMenu`** — right-click menus on cards and pipelines
7. **`System.Windows.Forms`** — used for tray icon (`NotifyIcon`), `FolderBrowserDialog`, `Screen` info
8. **`Wpf.Ui.Appearance.Theme`** — Mica/Dark/Light theme application
9. **`Wpf.Ui.Appearance.Accent`** — System/Custom accent color
10. **`MarkupExtension`** — `x:Static` for resource localization throughout XAML
11. **`BitmapImage`/`DrawingImage`** — tray icon, asset resources
12. **WPF Data Templates** — not used (code-behind creates controls programmatically)

### WPF-Specific Services

| Service | WPF Dependency | Avalonia Equivalent Needed |
|---|---|---|
| `ThemeManager` | `Wpf.Ui.Appearance.Theme/Accent` | Avalonia theme + custom accent |
| `TrayHelper` | `System.Windows.Forms.NotifyIcon` | `Avalonia.Controls.Notifications` or Avalonia.Headless tray |
| `NotifyIcon` | Win32 `Shell_NotifyIcon` P/Invoke | Avalonia tray or custom Win32 |
| `NotificationsManager` | WPF `Dispatcher`, WPF windows | Avalonia notification system |
| `NotificationWindow` | WPF popup window, `Screen` | Avalonia overlay/popup |
| `SnackbarHelper` | WPF-UI `Snackbar` | Custom Avalonia snackbar |
| `MainThreadDispatcher` | `System.Windows.Application.Current.Dispatcher` | `Dispatcher.UIThread` |
| `FullscreenHelper` | Win32 `GetForegroundWindow` + `GetWindowRect` | Platform-specific (same Win32) |
| `ScreenHelper` | `System.Windows.Forms.Screen` | Avalonia `Screens` |
| `SmartKeyHelper` | WPF `Dispatcher` | Platform-agnostic (only needs `Dispatcher.UIThread`) |

---

## 11. Reusable Backend Components

These components can be referenced directly from an Avalonia project **without modification**:

### Fully Reusable (no WPF dependency)

| Component | Project | Reason |
|---|---|---|
| `LoqNova.Lib` (entire project) | Lib | Pure backend, no WPF references |
| `LoqNova.Lib.Automation` | Lib.Automation | Pure backend |
| `LoqNova.Lib.Macro` | Lib.Macro | Pure backend (uses WinForms for hooks, but not WPF) |
| `LoqNova.CLI.Lib` | CLI.Lib | Pure IPC protocol |
| All `IFeature<T>` implementations | Lib | Hardware abstraction |
| All `IListener` implementations | Lib | Event subscription |
| All `ISensorsController` implementations | Lib | Sensor data |
| `RGBKeyboardBacklightController` | Lib | RGB orchestration |
| `RgbFrameDispatcher` | Lib | HID rendering |
| `CustomRGBEffectController` | Lib | Effect lifecycle |
| `CustomRGBEffectFactory` + all effects | Lib | Effect creation |
| `VolumeBrightnessReactiveRgbService` | Lib | Reactive RGB |
| `PerformanceModeTransitionEffect` | Lib | Strobe animation |
| `PowerModeFeature` | Lib | Power mode orchestration |
| All `AbstractSettings<T>` | Lib | Settings persistence |
| `MessagingCenter` | Lib | Event messaging |
| `IoCContainer` / Autofac | Lib | DI |
| `Compatibility` / `MachineInformation` | Lib | Device detection |
| `Battery` static class | Lib | Battery info |
| `Devices` static class | Lib | HID device enumeration |
| `WMI.*` static classes | Lib | WMI access |
| `Drivers` static class | Lib | Driver IOCTL |
| `Autorun` | Lib | Scheduled task management |
| `UpdateChecker` | Lib | GitHub releases |
| `HttpClientFactory` | Lib | HTTP with proxy |
| `Log` | Lib | Logging |
| `IpcServer` | WPF | **Needs move to Lib or shared** |
| `FeatureRegistry` | WPF | **Needs move to Lib or shared** |

---

## 12. Components Requiring Avalonia Adapters

### Must Replace

| WPF Component | Reason | Avalonia Strategy |
|---|---|---|
| `MainWindow` | WPF Window + NavigationStore + Frame | Avalonia `Window` + custom navigation |
| All 9 Pages | WPF `UiPage` + XAML | Avalonia `UserControl` pages |
| All 21 Windows/Dialogs | WPF `Window` + `ShowDialog` | Avalonia `Window` or `ContentDialog` |
| `BaseWindow` | WPF-UI window chrome | Avalonia custom chrome |
| `NavigationStore` | WPF-UI sidebar nav | Custom `ListBox`-based nav |
| `CardControl` / `CardExpander` | WPF-UI styled controls | Custom Avalonia controls |
| `Badge` / `InfoBar` | WPF-UI controls | Custom Avalonia controls |
| `Snackbar` | WPF-UI snackbar | Custom Avalonia notification |
| `ColorPickerControl` | PixiEditor.ColorPicker (WPF) | Avalonia color picker |
| `LoqKeyboardPreview` | WPF Shape/Drawing | Avalonia Canvas/Drawing |
| `SensorsControl` | WPF ProgressBar + Storyboard | Avalonia ProgressBar + Animations |
| `FanCurveControl` | WPF Canvas + Point rendering | Avalonia Canvas |
| `ThemeManager` | WPF-UI Theme/Accent API | Avalonia theme system |
| `TrayHelper` / `NotifyIcon` | WPF + Win32 Shell_NotifyIcon | Avalonia.Headless tray or custom |
| `NotificationsManager` | WPF Dispatcher + Windows | Avalonia overlay |
| `SnackbarHelper` | WPF-UI Snackbar | Custom Avalonia |
| `FullscreenHelper` | Win32 (reusable) | Same Win32 code |
| `ScreenHelper` | System.Windows.Forms.Screen | Avalonia `Screens` |
| `LocalizationHelper` | WPF resources | Avalonia localization |
| `Resource.Designer.cs` / `.resx` | WPF resource system | Avalonia localization (resx can work) |
| `PowerOptimization` | Win32 (reusable) | Same Win32 code |
| All `.xaml` files | WPF XAML | Avalonia AXAML |
| `DispatcherExtensions` | WPF Dispatcher | `Dispatcher.UIThread` |
| `WindowExtensions` | WPF Window | Avalonia Window API |
| `UIElementExtensions` | WPF UIElement | Avalonia Visual |
| `ProgressBarAnimateBehavior` | WPF Behaviors | Avalonia animations |
| All 7 Style XAML files | WPF ResourceDictionary | Avalonia Styles |
| `DashboardSettings` | References WPF DashboardItem enum | Move to shared or keep in Avalonia |

### Can Likely Reuse with Minor Changes

| Component | Change Needed |
|---|---|
| `IpcServer` | Move from WPF to Lib or new shared project |
| `FeatureRegistry` | Move from WPF to Lib or new shared project |
| `SmartKeyHelper` | Replace `Action BringToForeground` with Avalonia equivalent |
| `PerformanceModeColors` | Change `System.Windows.Media.Color` to `Avalonia.Media.Color` |
| `ColorExtensions` | WPF `System.Windows.Media.Color` ↔ Avalonia `Avalonia.Media.Color` |
| `RGBColorExtensions` | WPF Brush creation → Avalonia Brush creation |

---

## 13. Complete Avalonia Project Architecture Proposal

### New Projects

```
LoqNova.Avalonia/           ← New Avalonia frontend (replaces LoqNova.WPF)
LoqNova.Lib/                ← UNCHANGED (backend reference)
LoqNova.Lib.Automation/     ← UNCHANGED
LoqNova.Lib.Macro/          ← UNCHANGED
LoqNova.CLI/                ← UNCHANGED
LoqNova.CLI.Lib/            ← UNCHANGED
LoqNova.SpectrumTester/     ← UNCHANGED (dev tool)
LoqNova.WPF/                ← RETAINED until parity reached (can coexist)
```

### LoqNova.Avalonia Project References

```xml
<ProjectReference Include="..\LoqNova.Lib\LoqNova.Lib.csproj" />
<ProjectReference Include="..\LoqNova.Lib.Automation\LoqNova.Lib.Automation.csproj" />
<ProjectReference Include="..\LoqNova.Lib.Macro\LoqNova.Lib.Macro.csproj" />
<ProjectReference Include="..\LoqNova.CLI.Lib\LoqNova.CLI.Lib.csproj" />
```

### Key NuGet Packages

| Package | Purpose |
|---|---|
| Avalonia 11.2.x | Core framework |
| Avalonia.Desktop | Desktop runtime |
| Avalonia.Themes.Fluent | Fluent theme base (not as primary look, as starting point) |
| Avalonia.Fonts.Inter | System font |
| Avalonia.Svg.Skia | SVG icon support |
| Avalonia.ReactiveUI | Optional MVVM support (ReactiveUI or CommunityToolkit) |
| Svg.Skia | SVG rendering |
| SkiaSharp | Custom rendering (keyboard preview, sensors) |
| HarfBuzzSharp | Text shaping (RTL support) |

---

## 14. Proposed Avalonia Folder Structure

```
LoqNova.Avalonia/
├── App.axaml
├── App.axaml.cs
├── Program.cs
├── LoqNova.Avalonia.csproj
├── app.manifest
│
├── Assets/
│   ├── Icons/
│   ├── Images/
│   └── Fonts/
│
├── Services/
│   ├── IThemeService.cs              ← replaces ThemeManager
│   ├── INavigationService.cs         ← page navigation
│   ├── INotificationService.cs       ← replaces NotificationsManager
│   ├── ITrayService.cs               ← replaces TrayHelper/NotifyIcon
│   ├── IFileDialogService.cs         ← replaces FolderBrowserDialog
│   ├── IMainThreadDispatcher.cs      ← wraps Dispatcher.UIThread
│   ├── ThemeService.cs
│   ├── NavigationService.cs
│   ├── NotificationService.cs
│   ├── TrayService.cs
│   ├── FileDialogService.cs
│   └── MainThreadDispatcher.cs
│
├── ViewModels/
│   ├── ViewModelBase.cs
│   ├── MainWindowViewModel.cs
│   ├── Pages/
│   │   ├── DashboardViewModel.cs
│   │   ├── KeyboardBacklightViewModel.cs
│   │   ├── BatteryViewModel.cs
│   │   ├── AutomationViewModel.cs
│   │   ├── MacroViewModel.cs
│   │   ├── PackagesViewModel.cs
│   │   ├── SettingsViewModel.cs
│   │   └── AboutViewModel.cs
│   └── Dialogs/
│       ├── GodModeSettingsViewModel.cs
│       ├── EditDashboardViewModel.cs
│       └── ... (per dialog)
│
├── Views/
│   ├── MainWindow.axaml
│   ├── MainWindow.axaml.cs
│   ├── Pages/
│   │   ├── DashboardPage.axaml
│   │   ├── KeyboardBacklightPage.axaml
│   │   ├── BatteryPage.axaml
│   │   ├── AutomationPage.axaml
│   │   ├── MacroPage.axaml
│   │   ├── PackagesPage.axaml
│   │   ├── SettingsPage.axaml
│   │   └── AboutPage.axaml
│   ├── Dialogs/
│   │   ├── GodModeSettingsDialog.axaml
│   │   ├── EditDashboardDialog.axaml
│   │   └── ... (per dialog)
│   └── Controls/
│       ├── Navigation/
│       │   ├── NavigationSidebar.axaml
│       │   └── NavigationItem.axaml
│       ├── Cards/
│       │   ├── FeatureCard.axaml          (replaces CardControl)
│       │   ├── FeatureCardExpander.axaml  (replaces CardExpander)
│       │   ├── CardHeader.axaml           (replaces CardHeaderControl)
│       │   └── ToggleFeatureCard.axaml    (replaces AbstractToggleFeatureCardControl)
│       ├── RGB/
│       │   ├── KeyboardPreviewControl.axaml (replaces LoqKeyboardPreview)
│       │   ├── ZoneColorPicker.axaml
│       │   └── RGBPresetSelector.axaml
│       ├── Sensors/
│       │   ├── SensorsPanel.axaml         (replaces SensorsControl)
│       │   └── SensorBar.axaml
│       ├── Dashboard/
│       │   ├── DashboardGroup.axaml
│       │   └── DashboardWidgetHost.axaml
│       ├── Automation/
│       │   ├── AutomationPipelineControl.axaml
│       │   └── AutomationStepControl.axaml
│       ├── Notifications/
│       │   ├── OsdNotification.axaml      (replaces NotificationWindow)
│       │   └── Snackbar.axaml
│       └── Common/
│           ├── Badge.axaml
│           ├── InfoBar.axaml
│           ├── LoadableControl.axaml
│           └── ColorPicker.axaml
│
├── Converters/
│   ├── BoolToVisibilityConverter.cs
│   ├── EnumToDisplayConverter.cs
│   ├── RGBColorToBrushConverter.cs
│   └── ... (per converter needed)
│
├── Styles/
│   ├── Colors.axaml
│   ├── Typography.axaml
│   ├── Cards.axaml
│   ├── Navigation.axaml
│   ├── Buttons.axaml
│   ├── Sensors.axaml
│   └── App.axaml (merged)
│
├── Localization/
│   ├── Resource.resx              ← reference existing Lib resources
│   └── Resource.Designer.cs
│
└── Platform/
    ├── Win32/
    │   ├── FullscreenHelper.cs    ← copied from WPF (Win32 only)
    │   ├── PowerOptimization.cs   ← copied from WPF (Win32 only)
    │   ├── ShellTrayIcon.cs       ← custom Win32 tray (if needed)
    │   └── NativeMethods.cs
    └── ScreenHelper.cs            ← Avalonia Screens adapter
```

---

## 15. Proposed Navigation Architecture

### Sidebar Navigation (left panel)

```
┌─────────────────────────────────────────┐
│  LOQ Nova                          [─][□][×]│
├──────────┬──────────────────────────────┤
│          │                              │
│  🏠 Dash │   [Current Page Content]     │
│  ⌨ KB   │                              │
│  🔋 Batt │                              │
│  🚀 Actn │                              │
│  🎹 Macr │                              │
│  📦 Pkgs │                              │
│          │                              │
│  ────── │                              │
│  ⚙ Sett │                              │
│  ℹ Abut │                              │
│          │                              │
└──────────┴──────────────────────────────┘
```

### Navigation State

```csharp
// NavigationService manages:
// - Current page (reactive property)
// - Page history (for back/forward)
// - Ctrl+Tab cycling
// - Ctrl+1-8 direct navigation
```

### Implementation

- Use `ListBox` with custom `DataTemplate` for nav items (icon + label)
- Content area uses `ContentControl` with `DataTemplate` per ViewModel
- No Frame-based navigation (Avalonia doesn't have WPF Frame)
- State held in `MainWindowViewModel.CurrentPage` (reactive property)

---

## 16. Proposed Design System

### Visual Direction

**NOT:** Windows Settings, default WPF, default Avalonia, Fluent, generic SaaS

**YES:** ASUS Armoury Crate / Alienware Command Center / Lenovo Legion Space style

### Characteristics

- **Dark-first:** Dark background as primary (not just "dark mode option")
- **Matte surfaces:** Subtle gradients, not flat colors; slight depth via layering
- **Accent system:** Single brand accent (red: `#FF2121`), with mode-specific accents for PowerMode (blue/white/red/purple)
- **Typography:** Inter (or similar geometric sans), strong hierarchy (large telemetry numbers, small labels)
- **Cards:** Dark glass-like surfaces with subtle borders, no heavy shadows
- **Spacing:** Generous padding, breathing room between sections
- **Motion:** Deliberate, hardware-feeling transitions (not bouncy/playful)

### Color Palette

```
Background:        #0D0D0D (near-black)
Surface Level 1:   #1A1A1A
Surface Level 2:   #242424
Surface Level 3:   #2E2E2E
Border:            #3A3A3A (subtle)
Text Primary:      #E8E8E8
Text Secondary:    #8A8A8A
Accent (Red):      #FF2121
Mode Quiet:        #0078FF (blue)
Mode Balance:      #FFFFFF (white)
Mode Performance:  #FF3C3C (red)
Mode GodMode:      #B400FF (purple)
Success:           #4CAF50
Warning:           #F2A541
Error:             #BF360C
```

### Typography Scale

```
Page Title:      28px / SemiBold
Section Title:   22px / Medium
Card Title:      16px / Medium
Card Subtitle:   13px / Regular / Secondary color
Telemetry Value: 36px / Bold (sensor readings)
Telemetry Label: 12px / Regular / Secondary color
Body:            14px / Regular
Small:           12px / Regular
```

### Custom Controls Needed

| Control | Purpose | Key Properties |
|---|---|---|
| `FeatureCard` | Base card for all features | Icon, Title, Subtitle, Content |
| `ToggleFeatureCard` | Card with toggle switch | State, OnChanged |
| `ComboBoxFeatureCard` | Card with dropdown | Items, SelectedItem, OnChanged |
| `NavigationSidebar` | Left nav panel | Items, SelectedItem |
| `KeyboardPreview` | RGB keyboard visualization | ZoneColors (4 zones) |
| `SensorBar` | Animated progress bar | Value, MaxValue, AccentColor |
| `OsdNotification` | Popup notification | Icon, Text, Duration, Position |
| `ColorPicker` | RGB color selection | Color, OnColorChanged |
| `LoadableControl` | Loading state wrapper | IsLoading |
| `Badge` | Small label badge | Content, Appearance |
| `InfoBar` | Warning/info banner | Severity, Title, Message |

---

## 17. Proposed Dashboard Information Architecture

### Layout

```
┌──────────────────────────────────────────────┐
│  SENSORS (telemetry strip)                    │
│  CPU 45°C ████░░░░ 67%  │  GPU 52°C ███░░ 45% │
│  Fan1 2400 RPM           │  Fan2 2100 RPM       │
├──────────────────────────────────────────────┤
│                                                │
│  PERFORMANCE          │  POWER                 │
│  ┌────────────────┐   │  ┌────────────────┐   │
│  │ Power Mode     │   │  │ Battery Mode   │   │
│  │ [Performance▾] │   │  │ [Normal▾]      │   │
│  │ [⚙]           │   │  │                │   │
│  └────────────────┘   │  └────────────────┘   │
│  ┌────────────────┐   │  ┌────────────────┐   │
│  │ OverDrive      │   │  │ Flip to Start  │   │
│  │ [ON ═══════]   │   │  │ [OFF ═══════]  │   │
│  └────────────────┘   │  └────────────────┘   │
│                                                │
│  DISPLAY            │  DEVICE                  │
│  ┌────────────────┐ │  ┌────────────────┐     │
│  │ Refresh Rate   │ │  │ Always On USB  │     │
│  │ [165Hz ▾]     │ │  │ [Sleep ▾]      │     │
│  └────────────────┘ │  └────────────────┘     │
│  ┌────────────────┐ │  ┌────────────────┐     │
│  │ Resolution     │ │  │ FnLock         │     │
│  │ [2560x1440 ▾] │ │  │ [ON ═══════]   │     │
│  └────────────────┘ │  └────────────────┘     │
│  ┌────────────────┐ │  ┌────────────────┐     │
│  │ HDR            │ │  │ dGPU Status    │     │
│  │ [OFF ═══════]  │ │  │ ● Active       │     │
│  └────────────────┘ │  └────────────────┘     │
│                                                │
│  [Customize Dashboard]                         │
└──────────────────────────────────────────────┘
```

### Key Differences from WPF

- **No code-behind widget creation** — data-driven via ViewModels
- **Reactive sensor updates** — ReactiveUI/CommunityToolkit observable properties
- **Animated sensor bars** — Avalonia animations instead of WPF Storyboard
- **Responsive layout** — Avalonia `UniformGrid` or custom panel with breakpoints
- **Dark theme by default** — no "System" theme option needed for gaming aesthetic

---

## 18. Proposed Keyboard/RGB Workspace

### Layout

```
┌──────────────────────────────────────────────┐
│  KEYBOARD BACKLIGHT                           │
│                                                │
│  [Off] [Preset 1] [Preset 2] [Preset 3] [Preset 4] │
│                                                │
│  EFFECT     [Static ▾]                        │
│  BRIGHTNESS [High ▾]                          │
│  SPEED      [Fast ▾]                          │
│                                                │
│  ZONE 1 🔴  ZONE 2 🟢  ZONE 3 🔵  ZONE 4 🟡  │
│  [ColorPicker] [ColorPicker] [ColorPicker] [ColorPicker] │
│                                                │
│  ┌──────────────────────────────────────────┐ │
│  │     [Live Keyboard Preview]              │ │
│  │     4-zone visualization                 │ │
│  │     Updates in real-time                 │ │
│  └──────────────────────────────────────────┘ │
└──────────────────────────────────────────────┘
```

### Custom Effect Details

When a custom effect is selected (Ambient, AudioVisualizer, etc.), the zone color pickers are hidden and replaced with effect-specific configuration (e.g., speed slider for Ripple, input source for Fade).

### Keyboard Preview

The `KeyboardPreviewControl` renders a top-down view of the keyboard with 4 colored zones. It subscribes to `RgbFrameDispatcher.FrameRendered` to stay in sync with hardware. Built with Avalonia `Canvas` + `SKCanvas` (SkiaSharp) for smooth rendering.

---

## 19. Proposed Performance Workspace

### Layout (within Dashboard)

Performance mode is a dashboard widget, not a separate page:

```
┌────────────────────────────────────────┐
│  ⚙ PERFORMANCE MODE                     │
│                                          │
│  [Quiet ▾]  Blue accent bar             │
│  [Balance ▾] White accent bar           │
│  [Performance ▾] Red accent bar         │
│  [GodMode ▾] Purple accent bar          │
│                                          │
│  [⚙ GodMode Settings]                   │
│  ┌──────────────────────────────┐       │
│  │ CPU PL1  [====65W====]       │       │
│  │ CPU PL2  [====110W====]      │       │
│  │ GPU Power [====100W====]     │       │
│  │ Thermal   [====95°C====]     │       │
│  └──────────────────────────────┘       │
└────────────────────────────────────────┘
```

### Sensor Telemetry Strip (persistent at top)

```
CPU 45°C  ████░░░░ 67%  │  GPU 52°C  ███░░░░░ 45%  │  Fan1 2400RPM  Fan2 2100RPM
```

Bars colored by current performance mode accent.

---

## 20. Migration Order by Dependency/Risk

### Phase 1: Foundation (Low Risk)

1. **Create `LoqNova.Avalonia` project** — reference all Lib projects
2. **DI container** — register same services as WPF's `IoCModule`
3. **App startup** — single instance, compatibility check, feature init (reuse `App` logic)
4. **MainWindow shell** — custom chrome, navigation sidebar, content area
5. **Navigation service** — page switching, Ctrl+Tab, keyboard shortcuts
6. **Theme system** — dark-first, accent colors, mode-specific colors
7. **Localization** — reference existing `.resx` files

### Phase 2: Dashboard (Medium Risk)

8. **Feature card base controls** — `FeatureCard`, `ToggleFeatureCard`, `ComboBoxFeatureCard`
9. **Dashboard data model** — groups, items, serialization (reuse `DashboardSettings`)
10. **Dashboard widget factory** — map `DashboardItem` enum → Avalonia controls
11. **Sensors panel** — CPU/GPU bars with animation
12. **All dashboard widgets** — one by one, test against hardware
13. **Dashboard editor** — add/remove/reorder groups

### Phase 3: Keyboard (High Risk — RGB)

14. **Keyboard preview control** — SkiaSharp rendering of 4-zone keyboard
15. **RGB preset selector** — Off + 4 presets
16. **Effect/brightness/speed ComboBoxes**
17. **Zone color pickers** — Avalonia color picker
18. **RGB state binding** — connect to `RGBKeyboardBacklightController`
19. **Custom effect support** — AudioVisualizer, Ambient, etc.
20. **Verify strobe works** — test `PlayTransitionAsync`
21. **Verify reactive RGB** — test `VolumeBrightnessReactiveRgbService`

### Phase 4: Remaining Pages (Medium Risk)

22. **Battery page** — telemetry display, polling
23. **Automation page** — pipeline builder (complex UI, but backend is solid)
24. **Macro page** — keyboard hook integration
25. **Packages page** — download management

### Phase 5: Settings & System (Medium Risk)

26. **Settings page** — all toggles/combos
27. **System tray** — Avalonia tray or Win32 Shell_NotifyIcon
28. **OSD notifications** — popup overlay
29. **Snackbar** — in-app toast

### Phase 6: Dialogs (Low Risk)

30. **All 21 dialog windows** — port one by one
31. **GodMode settings** — slider controls
32. **GPU overclock settings** — slider controls
33. **Automation step configuration** — trigger editors

### Phase 7: Polish & Parity (Low Risk)

34. **Efficiency mode** — minimize behavior
35. **Smart key support** — Fn+N, Fn+F9
36. **RTL support** — FlowDirection
37. **CLI IPC** — move `IpcServer` + `FeatureRegistry` to shared location
38. **Update checker** — integrate into Avalonia UI
39. **Build pipeline** — update `build.yml` for Avalonia

---

## 21. Potential Technical Risks

### High Risk

| Risk | Impact | Mitigation |
|---|---|---|
| **RGB keyboard preview rendering** | The live 4-zone keyboard visualization must work smoothly in Avalonia. WPF used `Shape` elements; Avalonia needs SkiaSharp Canvas. | Prototype early; SkiaSharp is well-supported in Avalonia |
| **HID write reliability** | All RGB and hardware control depends on `HidD_SetFeature`. Must verify it works identically from Avalonia process. | Same P/Invoke calls, same `net8.0-windows` target — should work |
| **Tray icon behavior** | WPF used custom Win32 `Shell_NotifyIcon`. Avalonia has no built-in tray on Windows. | Implement custom Win32 tray (copy `NotifyIcon.cs` from WPF, minimal adapter) |
| **Single-instance mutex** | Must replicate exact mutex/event pattern for CLI compatibility. | Direct copy of `EnsureSingleInstance` logic |

### Medium Risk

| Risk | Impact | Mitigation |
|---|---|---|
| **Dispatcher threading** | WPF used `Dispatcher.Invoke` extensively. Avalonia uses `Dispatcher.UIThread`. | Create `IMainThreadDispatcher` adapter (already an interface in Lib) |
| **Localization** | WPF `.resx` files work in Avalonia but `FlowDirection` handling differs. | Test RTL early; Avalonia supports `FlowDirection` on `Application` |
| **Dialog management** | WPF used `ShowDialog()` with `Owner`. Avalonia dialogs work differently. | Use Avalonia `Window.ShowDialog()` or `ContentDialog` |
| **Automation page complexity** | 37 step types with dynamic UI generation. WPF created controls programmatically. | Data-driven approach: each step type maps to a ViewModel + View |
| **Spectrum keyboard support** | Alternative keyboard protocol with different HID device and Aurora integration. | Can reference same Lib code; UI is simpler (profile/brightness) |

### Low Risk

| Risk | Impact | Mitigation |
|---|---|---|
| **Package downloader** | Network + progress reporting | Same Lib code; UI is list of controls |
| **Macro recording** | Low-level keyboard hooks | Same Lib code; UI is simple toggle + sequence editor |
| **Settings serialization** | JSON via Newtonsoft | Same Lib code |
| **Markdig rendering** | About page licenses | Avalonia has Markdown.Xaml alternatives or custom renderer |

---

## 22. Features That Must Reach 100% Parity Before WPF Can Be Retired

### Critical (must work identically)

| Feature | Verification |
|---|---|
| **Performance mode switching** (all 4 modes, Fn+Q, AC-driven) | Test: switch modes, verify OSD, verify strobe, verify no duplicates |
| **RGB keyboard control** (all presets, all effects, zone colors) | Test: set each preset, each effect, change zones, verify hardware |
| **Performance mode strobe** (3-pulse animation, resume after) | Test: switch mode, verify strobe plays, verify effect resumes |
| **Volume/brightness reactive RGB** (level meter, restore) | Test: change volume, verify 4-zone meter, verify restore |
| **Dashboard widgets** (all 28+ controls) | Test: each widget reads/writes correct feature |
| **Automation pipelines** (triggers + 37 step types) | Test: create pipeline, run manually, verify each step |
| **System tray** (minimize, restore, context menu, tooltip) | Test: minimize to tray, right-click, hover tooltip |
| **OSD notifications** (all types, per-type enable/disable) | Test: trigger each notification type, verify appearance |
| **Single-instance** (second instance brings first to front) | Test: launch twice, verify behavior |
| **CLI IPC** (named pipe, all commands) | Test: `ln feature list`, `ln feature set`, `ln qa list` |
| **Settings persistence** (all settings pages) | Test: change each setting, restart, verify persisted |
| **Autorun** (scheduled task creation/validation) | Test: enable/disable, verify task |

### Important (should work but minor differences acceptable)

| Feature | Notes |
|---|---|
| Battery page telemetry | Read-only display, timing may differ slightly |
| Packages page downloads | Network-dependent, UI layout can differ |
| Macro recording/playback | Keyboard hook must work identically |
| Theme/accent colors | Dark mode primary; exact colors can differ |
| Keyboard shortcuts | Ctrl+Tab, Ctrl+1-8 |
| Window size persistence | Save/restore on close |
| Smart key actions | Fn+N, Fn+F9 single/double press |

### Nice to Have (can differ significantly)

| Feature | Notes |
|---|---|
| Dashboard editor (reorder/add/remove) | Can be simplified |
| About page (licenses, credits) | Layout can differ |
| Donate page | Layout can differ |
| Device information window | Can be simplified |
| Boot logo | May be deferred |
| RTL layout | Important for i18n but can be deferred |

---

### DO NOT IMPLEMENT YET

**Confirmed: No source files were modified during this analysis.**

All findings are based on read-only inspection of the existing codebase on the `feature/avalonia-ui` branch. The `LoqNova.WPF` project, `LoqNova.Lib`, and all other projects remain untouched. This document serves as the architectural blueprint for the Avalonia migration — implementation will begin only after your review and approval.
