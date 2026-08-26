# LOQ Nova

[![Build](https://github.com/earnest-s/LoqNova/actions/workflows/build.yml/badge.svg?branch=master)](https://github.com/earnest-s/LoqNova/actions/workflows/build.yml)

---

**LOQ Nova** (v3.1.0) is a free, open-source Windows utility for **Lenovo LOQ** and compatible Lenovo gaming laptops (including Legion and IdeaPad Gaming series). It exposes hardware features that are otherwise available only through Lenovo Vantage or Legion Zone — power modes, fan behavior, battery charging controls, GPU options, keyboard lighting and more — in a single lightweight application.

- Windows-only
- Runs no separate background services
- Virtually no CPU usage at idle
- No telemetry

Join the Legion Series Discord: https://discord.com/invite/legionseries

<img src="assets/screenshot_main.png" width="700" />

&nbsp;

# Table of Contents
  - [Disclaimer](#disclaimer)
  - [Download](#download)
  - [Compatibility](#compatibility)
  - [Features](#features)
    - [Keyboard lighting (RGB)](#keyboard-lighting-rgb)
    - [Volume / Brightness reactive visualization](#volume--brightness-reactive-visualization)
  - [Custom Mode](#custom-mode)
  - [Hybrid Mode and GPU Working Modes](#hybrid-mode-and-gpu-working-modes)
  - [Deactivate discrete NVIDIA GPU](#deactivate-discrete-nvidia-gpu)
  - [Overclock discrete NVIDIA GPUs](#overclock-discrete-nvidia-gpus)
  - [Windows Power Plans & Windows Power Mode](#windows-power-plans--windows-power-mode)
  - [Boot Logo](#boot-logo)
  - [Running programs or scripts from actions](#running-programs-or-scripts-from-actions)
  - [CLI](#cli)
  - [Arguments](#arguments)
  - [How to collect logs?](#how-to-collect-logs)
  - [FAQ](#faq)
  - [Credits](#credits)
  - [Contribution](#contribution)

## Disclaimer

**The tool comes with no warranty. Use at your own risk.**

Please be patient and read through this readme carefully - it contains important information.

## Download

Download the latest installer or portable build from the
[Releases page](https://github.com/earnest-s/LoqNova/releases).

> [!NOTE]
> Release binaries are currently not code-signed, so Windows SmartScreen may show a warning on first run. All releases are built directly on GitHub Actions from this repository.

### Required drivers

If you installed LOQ Nova on a clean Windows install, make sure to have necessary drivers installed. If drivers are missing, some options might not be available. Especially make sure that these two are installed on your system:

1. Lenovo Energy Management
2. Lenovo Vantage Gaming Feature Driver

### Problems with .NET?

LOQ Nova requires the **.NET 8 Desktop Runtime (x64)**. The installer detects and offers to download it automatically. To install it manually:

1. Go to https://dotnet.microsoft.com/en-us/download/dotnet/8.0
2. Find section ".NET Desktop Runtime"
3. Download x64 Windows installer
4. Run the installer

After following these steps, you can open Terminal and type: `dotnet --info`. In the output look for section `.NET runtimes installed` — you should see `Microsoft.WindowsDesktop.App 8.x.x` listed there.

## Compatibility

LOQ Nova is made for Lenovo LOQ laptops and other similar Lenovo devices, such as Legion and IdeaPad Gaming series, including their Chinese variants.

Generations 6 (MY2021), 7 (MY2022), 8 (MY2023) and 9 (MY2024) are supported, although some features also work on the 5th generation (MY2020). Issues related to devices older than Gen 6 or that are not LOQ/Legion-class Lenovo laptops are out of scope of this project.

If you are getting an incompatible message on startup, you can check the *Contribution* section down at the bottom to see how you can help. Keep in mind that not all options can be made compatible with all hardware.

**Support for other laptop brands is not planned.**

### Lenovo's software

Overall the recommendation is to disable or uninstall Vantage, Hotkeys and Legion Zone while using LOQ Nova. Some functions cause conflicts or may not work properly when running alongside other Lenovo apps. Built-in disable toggles for these components are available in Settings.

> [!TIP]
> Using the disable option in LOQ Nova is often the easiest option.

## Features

- Change settings like power mode, battery charging mode, etc. that are available only through Vantage
- 4-zone RGB keyboard control with presets and dynamic RGB effects (see below)
- Audio-reactive 4-zone **Audio Visualizer**
- **Performance-mode transition strobe** on AC connect/disconnect
- **Volume / display-brightness reactive** 4-zone visualization (background feature)
- Monitor dGPU activity (NVIDIA only), deactivate/restart dGPU
- Simple NVIDIA GPU overclocking
- Actions/automation: run steps automatically on triggers (AC adapter, power mode, games, processes, time, Wi-Fi, lid, HDR, ...)
- View battery statistics and health information
- Control features from the command line (`ln.exe`)
- Check for driver and software updates
- Check warranty status
- Disable/enable Lenovo Vantage, Legion Zone and Lenovo Hotkeys without uninstalling them

### Keyboard lighting (RGB)

#### 4-Zone RGB

LOQ Nova supports 4-zone RGB keyboards with:

- Multiple presets (independent configurations you can switch between)
- Per-zone color configuration where supported
- Brightness and speed controls where supported
- Firmware-driven effects: Static, Breath, Wave (left/right), Smooth
- Software-driven dynamic effects rendered by the app:
  Disco, Swipe (with fill/clean variants), Lightning, Christmas,
  Rainbow Wave, Breathing Color Cycle, Temperature (CPU/GPU temperature reactive),
  Fade, Ripple (input reactive), Ambient (screen reactive), Strobe,
  and the **Audio Visualizer**

#### Audio Visualizer

The Audio Visualizer is a real 4-zone audio-reactive effect: it captures the current system audio output in real time and maps loudness across frequency bands to the four keyboard zones. Select it like any other effect from your preset's effect list.

#### Performance Mode Transition Strobe

When connecting or disconnecting the AC adapter causes the firmware/performance mode to change (for example Performance dropping to Balance on battery, or Balance returning to Performance/Custom on AC), LOQ Nova can play a short keyboard strobe using the color associated with the resulting performance mode. The same strobe plays when you change performance modes with Fn+Q or from the dashboard. This is instant visual feedback only — LOQ Nova never forces a performance mode; the firmware remains in control.

#### Volume / Brightness Reactive Visualization

This is a **background reactive feature**, *not* a selectable RGB effect and *not* listed in the Effects dropdown.

While LOQ Nova is running:

- Changing the **Windows master volume** triggers a temporary 4-zone level-meter visualization.
- Changing the **Windows display brightness** triggers the same visualization.
- The visualization temporarily takes control of the keyboard, then the previously selected preset/effect resumes exactly as it was.
- It uses a dedicated fixed color progression — it does not modify your configured zone colors.

Mapping (identical for volume and brightness):

| Level | Zone 1 | Zone 2 | Zone 3 | Zone 4 |
|---|---|---|---|---|
| 0–25 % | fills GREEN progressively | – | – | – |
| 25–50 % | GREEN | fills YELLOW progressively | – | – |
| 50–75 % | GREEN | YELLOW | fills ORANGE progressively | – |
| 75–100 % | GREEN | YELLOW | ORANGE | fills RED progressively |

At 100 %: Zone 1 = GREEN, Zone 2 = YELLOW, Zone 3 = ORANGE, Zone 4 = RED — all fully lit.

Each zone ramps smoothly within its own 25 % stage (for example, volume 37.5 % = green full + yellow at half). If another change arrives while the visualization is showing, it updates in place — the most recent value always wins. Rapid changes never queue stale animations, and the performance-mode strobe always takes priority over this visualization.

Other lighting features like white keyboard backlight (where supported), panel logo backlight, rear ports lighting and Spectrum per-key RGB keyboards are also supported on capable hardware, subject to model/BIOS limitations:

* Some (mostly Gen 6) laptop models might not show all options or show options that aren't there - this is due to misconfigured BIOS that doesn't report availability of these features.

Lighting that required Corsair iCue is not supported by LOQ Nova.

> [!IMPORTANT]
> Riot Vanguard DRM (used in Valorant for example) is known to cause issues with RGB controls. If you don't see RGB settings and have it installed, make sure it doesn't run on startup or uninstall it._

## Custom Mode

Custom Mode is available on all devices that support it. You can find it in the Power Mode dropdown as it basically is 4th power mode and it allows for adjusting power limits and fans. Custom Mode can't be accessed with Fn+Q shortcut unless enabled in Settings. Not all features of Custom Mode are supported by all devices.

If you have one of the following BIOSes:
* G9CN (24 or higher)
* GKCN (46 or higher)
* H1CN (39 or higher)
* HACN (31 or higher)
* HHCN (20 or higher)

Make sure to update it to at least minimum version mentioned above for Custom Mode to function properly.

## Hybrid Mode and GPU Working Modes

> [!NOTE]
> Hybrid Mode/GPU Working Mode options _are not_ Advanced Optimus and work separately from it.

There are two main ways you can use your dGPU:

1. Hybrid mode on - internal laptop display is connected to integrated GPU, discrete GPU will work when needed and power off when not in use, giving better battery life
2. Hybrid mode off (aka dGPU) - internal laptop display is connected directly to discrete GPU, giving best performance but also worst battery life

Switching between two modes requires restart.

On Gen 7 and 8 laptops, there are additional 2 settings for Hybrid mode:

1. Hybrid iGPU-only - in this mode dGPU will be disconnected (think of it like ejecting USB drive), so there is no risk of it using power when you want to achieve best battery life
2. Hybrid Auto - similar to the above, but tries to automate the process by automatically disconnecting dGPU on battery power and reconnecting it when you plug in AC adapter

Discrete GPU may not disconnect, and in most cases will not disconnect, when it is used. That includes apps using dGPU, external monitor connected and probably some other cases that aren't specified by Lenovo. If you use the "Deactivate GPU" option, make sure that it reports dGPU Powered Off and no external screens are connected, before switching between Hybrid Modes in case you encounter problems.

All above settings are using built in functions of the EC and how well they work relies on Lenovo's firmware implementation. From our observations, they are reliable, unless you start switching them frequently. Be patient, because changes to these methods are not instantaneous. LOQ Nova also attempts to mitigate these issues by disallowing frequent Hybrid Mode switching and additional attempts to wake dGPU if EC failed to do so. It may take up to 10 seconds for dGPU to reappear when switching to Hybrid Mode, in case EC failed to wake it.

> [!WARNING]
> Disabling dGPU via Device Manager DOES NOT disconnect the device and will cause high power consumption!

## Deactivate discrete NVIDIA GPU

Sometimes discrete GPU stays active even when it should not. This can happen for example if you work with an external screen and you disconnect it - some processes will keep running on discrete GPU keeping it alive and shortening battery life.

There are two ways to help the GPU deactivate:

1. killing all processes running on dGPU (this one seems to work better),
2. disabling dGPU for a short amount of time, which will force all processes to move to the integrated GPU.

Deactivate button will be enabled when dGPU is active, you have Hybrid mode enabled and there are no screens connected to dGPU. If you hover over the button, you will see the current P state of dGPU and the list of processes running on it.

> [!NOTE]
> Some apps may not like this feature and crash when you use deactivate dGPU option.

## Overclock discrete NVIDIA GPUs

The overclock option is intended for simple overclocking, similar to the one available in Vantage. It is not intended to replace tools like Afterburner. Here are some points to keep in mind:
* Make sure GPU overclocking is enabled in BIOS, if your laptop has such option.
* Overclocking does not work with Vantage or LegionZone running in the background.
* It is not recommended to use the option while using other tools like Afterburner.
* If you edited your Dashboard, you might need to add the control manually.

## Windows Power Plans & Windows Power Mode

First of all, the Power Mode you see in LOQ Nova (or toggle with Fn+Q) **is not** the same as Power Plans (that you access from Control Panel) or Power Mode (that you can change from Settings app).

The modern (and recommended) approach is to use Windows Power Modes and only one, default, "Balanced (recommended)" power plan. You should have 3 Power Modes to choose from in Windows Settings app:

* Best power efficiency
* Balanced
* Best performance

You can assign these in LOQ Nova settings to each of the laptop's Power Modes: Quiet, Balance, Performance and Custom. If you choose to do so, respective Windows Power Mode will be automatically set when you change Power Modes.

The legacy approach is to use multiple Power Plans, that some devices had installed from factory. If you decide to use them, or configure your own plans, leave the settings in Windows Settings app on the default "Balanced" setting. You can configure LOQ Nova to switch Power Plans automatically whenever you change the Power Mode in LOQ Nova settings.

If you encounter issues with power mode or plan synchronization, especially when switching between the two approaches, you can reset Windows power settings to default using `powercfg -restoredefaultschemes; shutdown /r /t 0` command. This command will reset all power plans to default and reboot your device. All plans except for the default "Balanced (recommended)" will be deleted, so make sure to make a copy if you plan on using them again.

## Boot Logo

On some laptops, it is possible to change the boot logo (the default "Legion" image you see at boot). Boot logo is *not* stored in UEFI - it is stored on the UEFI partition on boot drive. When setting custom boot logo, LOQ Nova conducts basic checks, like resolution, image format and calculates a checksum to ensure compatibility. However, the real verification happens on the next boot. UEFI will attempt to load the image from UEFI partition and show it. If that fails for whatever reason, default image will be used. Exact criteria, except for resolution and image format, are not known and some images might not be shown. In this case, try another image, edited with different image editor.

## Running programs or scripts from actions

You can use "Run" step in Actions to start any program or script. To configure it, you need to provide path to the executable (`.exe`) or a script (`.bat`). Optionally, you can also provide arguments that the script or program supports - just like running anything from command line.

<details>
<summary>Examples</summary>

_Shutdown laptop_
 - Executable path: `shutdown`
 - Arguments: `/s /t 0`

_Restart laptop_
 - Executable path: `shutdown`
 - Arguments: `/r`

_Running a program_
 - Executable path: `C:\path\to\the\program.exe` (if the program is on your PATH variable, you can use the name only)
 - Arguments: `` (optional, for list of supported argument check the program's readme, website etc.)

_Running a script_
 - Executable path: `C:\path\to\the\script.bat` (if the program is on your PATH variable, you can use the name only)
 - Arguments: `` (optional)

_Python script_
 - Executable path: `C:\path\to\python.exe` (or just `python`, if it is on your PATH variable)
 - Arguments: `C:\path\to\script.py`

  </details>

#### Environment

LOQ Nova automatically adds some variables to the process environment that can be accessed from within the script. Depending on what was the trigger, different variables are added. All variables use the `LN_` prefix:

<details>
<summary>Environment variables</summary>

- When AC power adapter is connected
	- `LN_IS_AC_ADAPTER_CONNECTED=TRUE`
- When low wattage AC power adapter is connected
	- `LN_IS_AC_ADAPTER_CONNECTED=TRUE`
	- `LN_IS_AC_ADAPTER_LOW_POWER=TRUE`
- When AC power adapter is disconnected
	- `LN_IS_AC_ADAPTER_CONNECTED=FALSE`
- When Power Mode is changed:
	- `LN_POWER_MODE=<value>`, where `value` is one of: `1` - Quiet, `2` - Balance, `3` - Performance, `255` - Custom
	- `LN_POWER_MODE_NAME=<value>`, where `value` is one of: `QUIET`, `BALANCE`, `PERFORMANCE`, `CUSTOM`
- When game is running
	- `LN_IS_GAME_RUNNING=TRUE`
- When game closes
	- `LN_IS_GAME_RUNNING=FALSE`
- When app starts
	- `LN_PROCESSES_STARTED=TRUE`
	- `LN_PROCESSES=<value>`, where `value` is comma separated list of process names
- When app closes
	- `LN_PROCESSES_STARTED=FALSE`
	- `LN_PROCESSES=<value>`, where `value` is comma separated list of process names
- Lid opened
	- `LN_IS_LID_OPEN=TRUE`
- Lid closed
	- `LN_IS_LID_OPEN=FALSE`
- When displays turn on
	- `LN_IS_DISPLAY_ON=TRUE`
- When displays turn off
	- `LN_IS_DISPLAY_ON=FALSE`
- When external display is connected
	- `LN_IS_EXTERNAL_DISPLAY_CONNECTED=TRUE`
- When external display is disconnected
	- `LN_IS_EXTERNAL_DISPLAY_CONNECTED=FALSE`
- When HDR is on
	- `LN_IS_HDR_ON=TRUE`
- When HDR is off
	- `LN_IS_HDR_ON=FALSE`
- When WiFi is connected
	- `LN_WIFI_CONNECTED=TRUE`
	- `LN_WIFI_SSID=<value>`, where `value` is the SSID of the network
- When WiFi is disconnected
	- `LN_WIFI_CONNECTED=FALSE`
- At specified time
	- `LN_IS_SUNSET=<value>` (`TRUE`/`FALSE`, depending on trigger configuration)
	- `LN_IS_SUNRISE=<value>` (`TRUE`/`FALSE`, depending on trigger configuration)
	- `LN_TIME=<value>` (`HH:mm`)
	- `LN_DAYS=<value>` (comma separated: `MONDAY`, `TUESDAY`, ..., `SUNDAY`)
- Periodic action
	- `LN_PERIOD=<value>`, interval in seconds
- On startup
	- `LN_STARTUP=TRUE`
- On resume
	- `LN_RESUME=TRUE`

</details>

#### Output

If "Wait for exit" is checked, LOQ Nova will capture the standard output of the launched process. This output is stored in `$RUN_OUTPUT$` variable and can be displayed in a Show notification step.

## CLI

It is possible to control some features of LOQ Nova directly from the command line. The CLI executable is called `ln.exe` and can be found in the install directory.

For the CLI to work properly, LOQ Nova needs to run in the background and the CLI option needs to be enabled in LOQ Nova settings. You can also choose to add `ln.exe` to your PATH variable for easier access.

The CLI does not need to be ran as Administrator.

<details>
<summary>Commands</summary>

```
ln quickAction --list              list all Quick Actions
ln quickAction <name>              run Quick Action with given <name>
ln feature --list                  list all supported features
ln feature get <name>              get value of a feature
ln feature set <name> --list       list all values for a feature
ln feature set <name> <value>      set feature to a specified value
ln spectrum profile get            get current Spectrum RGB profile
ln spectrum profile set <profile>  set Spectrum RGB profile
ln spectrum brightness get         get current Spectrum RGB brightness
ln spectrum brightness set <v>     set Spectrum RGB brightness
ln rgb get                         get current 4-zone RGB preset
ln rgb set <preset>                set 4-zone RGB preset
```

</details>

## Arguments

Some, less frequently needed, features or options can be enabled by using additional arguments. These arguments can either be passed as parameters or added to an `args.txt` file located in the settings folder (`%LOCALAPPDATA%\LOQNova`) — one argument per line.

* `--trace` - enables logging to `%LOCALAPPDATA%\LOQNova\log`
* `--minimized` - starts minimized to tray
* `--skip-compat-check` - disables compatibility check on startup _(No support is provided when this argument is used)_
* `--disable-tray-tooltip` - disables tray tooltip shown when hovering over the tray icon
* `--allow-all-power-modes-on-battery` - allows using all Power Modes without AC adapter _(No support is provided when this argument is used)_
* `--enable-hybrid-mode-automation` - allows changing Hybrid Mode/GPU Working Mode with actions _(No support is provided when this argument is used)_
* `--force-disable-rgbkb` - disables all lighting features for 4-zone RGB keyboards
* `--force-disable-spectrumkb` - disables all lighting features for Spectrum per-key RGB keyboards
* `--force-disable-lenovolighting` - disables lighting features related to panel logo, ports backlight and some white backlit keyboards
* `--experimental-gpu-working-mode` - changes GPU Working Mode switch to use experimental method used by LegionZone _(No support is provided when this argument is used)_
* `--proxy-url=example.com` - specifies proxy server URL to use
* `--proxy-username=some_username` - proxy server username, if applicable
* `--proxy-password=some_password` - proxy server password, if applicable
* `--proxy-allow-all-certs` - relaxes criteria needed to establish HTTPS/SSL connections via proxy server
* `--disable-update-checker` - disables update checks, in case you want to rely on manual downloads
* `--disable-conflicting-software-warning` - disables warning banners when conflicting software is running

## How to collect logs?

In all troubleshooting situations, logs provide important information. **Always** attach logs to your issues. Critical error logs are saved automatically under `%LOCALAPPDATA%\LOQNova\log`.

To collect logs:

1. Make sure LOQ Nova is not running (also gone from tray area).
2. Open `Run` (Win+R) and type: `"%LOCALAPPDATA%\Programs\LOQNova\LOQ Nova.exe" --trace` (adjust path to your install location) and hit OK
3. LOQ Nova will start and in the title bar you should see: `[LOGGING ENABLED]`
4. Reproduce the issue you have (i.e. try to use the option that causes issues)
5. Close LOQ Nova (also make sure it's gone from tray area)
6. Again, in `Run` (Win+R) type: `%LOCALAPPDATA%\LOQNova\log`
7. You should see at least one file. These are the logs you should attach to the issue.

## FAQ

#### Why do I get a message that Vantage is still running, even though I uninstalled it?

Vantage installs 3 components:

1. Lenovo Vantage app
2. Lenovo Vantage Service
3. System Interface Foundation V2 Device

The easiest solution is to go into LOQ Nova settings and select options to disable Lenovo Vantage, Legion Zone and Hotkeys (only still installed ones are shown).

If you want to remove them instead, make sure that you uninstall all 3, otherwise some options in LOQ Nova will not be available. You can check Task Manager for any processes containing `Vantage` or `ImController`. You can also check this guide for more info: [Uninstalling System Interface Foundation V2 Device](https://support.lenovo.com/us/en/solutions/HT506070), if you have troubles getting rid of `ImController` processes.

#### Why is my antivirus reporting that the installer contains a virus/trojan/malware?

LOQ Nova makes use of many low-level Windows APIs that can be falsely flagged by antiviruses as suspicious, resulting in a false-positive. LOQ Nova is open source and can easily be audited by anyone who has doubts as to what this software does. All installers are built directly on GitHub with GitHub Actions, so there is no doubt what they contain. This problem could be solved by signing all code, but an Extended Validation certificate costs hundreds of dollars per year.

If you downloaded the installer from this project's releases page, you shouldn't worry - the warning is a false-positive.

#### Can I customize hotkeys?

You can customize Fn+F9 hotkey in LOQ Nova settings. Other hotkeys can't be customized.

#### Can I customize Conservation mode threshold?

No. Conservation mode threshold is set in firmware to 60% (2021 and earlier) or 80% (2022 and later) and it can't be changed.

#### Can I customize fans in Quiet, Balance or Performance modes?

No, it isn't possible to customize how the fans work in power modes other than Custom.

#### Why can't I switch to Performance or Custom Power Mode on battery?

By default, switching to Performance or Custom modes is blocked when the laptop runs on battery, matching Vantage/Legion Zone behavior.

If for whatever reason you want to use these modes on battery anyway, you can use `--allow-all-power-modes-on-battery` argument. Check [Arguments](#arguments) section for more details.

> [!WARNING]
> Power limits and other settings are not applied correctly on most devices when laptop is not connected to a full power AC adapter and unpredictable behavior is expected. Therefore, no support is provided for issues related to using this argument.*

#### Why does switching to Performance mode seem buggy, when AI Engine is enabled?

It seems that some BIOS versions indeed have weird issues when using Fn+Q. Only hope is to wait for Lenovo to fix it.

#### Why am I getting incompatible message after motherboard replacement?

Sometimes new motherboard does not contain correct model numbers and serial numbers. You should try [this tutorial](https://laptopwiki.eu/laptopwiki/guides/lenovo/legion_bios_lvarrecovery) to try and recover them. If that method does not succeed, you can workaround it with `--skip-compat-check` argument. Check [Arguments](#arguments) section for more details.

#### Why isn't a game detected, even though Actions are configured properly?

Game detection feature is built on top of Windows' game detection, meaning LOQ Nova will react to EXE files that Windows considers "a game". That also means that if you nuked Xbox Game Bar from your installation, there is 99.9% chance this feature will not work.

Windows probably doesn't recognize all games properly, but you can mark any program as a game in Xbox Game Bar settings (Win+G). You can find list of recognized games in registry: `HKEY_CURRENT_USER\System\GameConfigStore\Children`.

#### Can I use other RGB software while using LOQ Nova?

In general, yes. LOQ Nova will disable RGB controls when Vantage is running to avoid conflicts. If you use other RGB software like L5P-Keyboard-RGB or [OpenRGB](https://openrgb.org/), you can disable RGB in LOQ Nova with `--force-disable-rgbkb` or `--force-disable-spectrumkb` arguments. Check [Arguments](#arguments) section for more details.

#### Will iCue RGB keyboards be supported?

No. Check out [OpenRGB](https://openrgb.org/) project.

#### What dynamic RGB effects are available for 4-zone keyboards?

LOQ Nova includes a set of software-rendered dynamic effects for 4-zone RGB keyboards: Disco, Swipe (with fill/clean variants), Lightning, Christmas, Rainbow Wave, Breathing Color Cycle, Temperature (reacts to CPU/GPU temperatures), Fade, Ripple (input-reactive), Ambient (screen-reactive), Strobe and the Audio Visualizer (system-audio reactive). Firmware-driven effects (Static, Breath, Wave left/right, Smooth) remain available as well. User-created/custom effect plugins are not supported.

#### Can I add my own custom RGB effects?

Not at this time. Effects are compiled into the application. The rendering architecture lives behind a single RGB frame pipeline, but there is currently no plugin interface for third-party or user-authored effects.

#### Can you add fan control to other models?

Fan control is available on Gen 7 and later models. Older models will not be supported due to technical limitations.

#### Why don't I see the custom tooltip when I hover the tray icon?

In Windows 10 and 11, Microsoft did plenty of changes to the tray, breaking a lot of things on the way. As a result custom tooltips do not always work properly. Solution? Update your Windows and keep fingers crossed.

#### How can I OC/UV my CPU?

There are very good tools like [Intel XTU](https://www.intel.com/content/www/us/en/download/17881/intel-extreme-tuning-utility-intel-xtu.html) (which is used by Vantage) or [ThrottleStop](https://www.techpowerup.com/download/techpowerup-throttlestop/) made just for that.

#### What if I overclocked my GPU too much?

If you end up in a situation where your GPU is not stable and you can't boot into Windows, there are two things you can do:

1. Go into BIOS and try to find and option similar to "Enable GPU Overclocking" and disable it, start Windows, and toggle the BIOS option again to Enabled.
2. Start Windows in Safe Mode and delete `gpu_oc.json` file under LOQ Nova settings, which are located in `%LOCALAPPDATA%\LOQNova`.

#### Why is my Boot Logo not applied?

When you change the Boot Logo, LOQ Nova verifies that it is in the correct format and resolution. If LOQ Nova shows that boot logo is applied, it means that the setting was correctly saved to UEFI. If you don't see the custom boot logo, it means that even though UEFI is configured and custom image is saved to UEFI partition, your UEFI for some reason does not render it. In this case the best idea is to try a different image, maybe in different format, edited with different image editor etc. If the boot logo is not shown after all these steps, it's probably a problem with your BIOS version.

#### Why do I see stuttering when using Smart Fn Lock?

On some BIOS versions, toggling Fn Lock causes a brief stutter and since Smart Fn Lock is basically an automatic toggle for Fn Lock, it is also affected by this issue. Try disabling "Fool proof Fn Lock" (or similar) option in BIOS - it was reported that it fixes stutter when toggling Fn Lock.

#### Which generation is my laptop?

Check the model number. Example model numbers are `16ACH6H` or `15IRX9`. The last number of the model number indicates generation.

## Credits

LOQ Nova is an independent continuation of **Lenovo Legion Toolkit**, originally created by **Bartosz Cichecki** and archived in July 2025. This project would not exist without his work and the work of all original contributors.

| Contributor | Commits | Lines Changed |
|-------------|---------|---------------|
| [Bartosz Cichecki](https://github.com/BartoszCichecki) | 2,384 | +222,357 / -90,337 |
| [Mario Bălănică](https://github.com/mariobalanica) | 26 | +3,879 / -432 |
| [Sichen Lyu](https://github.com/Ace-Radom) | 24 | +2,734 / -447 |
| [凌卡Karl](https://github.com/KarlLee830) | 18 | +220 / -137 |
| [Karl Lee](https://github.com/KarlLee830) | 16 | +2,114 / -368 |
| [Earnest S](https://github.com/earnest-s) | 8+ | ongoing |
| And many more amazing contributors! | | |

Special thanks to:

* [ViRb3](https://github.com/ViRb3), for creating [Lenovo Controller](https://github.com/ViRb3/LenovoController), which was used as a base for the original tool
* [falahati](https://github.com/falahati), for creating [NvAPIWrapper](https://github.com/falahati/NvAPIWrapper) and [WindowsDisplayAPI](https://github.com/falahati/WindowsDisplayAPI)
* [SmokelessCPU](https://github.com/SmokelessCPU), for help with 4-zone RGB and Spectrum keyboard support
* [Mario Bălănică](https://github.com/mariobalanica), for all contributions
* [Ace-Radom](https://github.com/Ace-Radom), for all contributions

Translations were contributed by many people across the original project and continue to ship with LOQ Nova.

## Contribution

We appreciate any feedback that you have, so please do not hesitate to report issues. Pull Requests are also welcome, but make sure to follow the existing rules first!

House rules:

1. Read this README first — many questions are answered here.
2. Search existing issues/discussions before opening a new one.
3. English only.
4. Stay in scope: LOQ Nova targets Lenovo LOQ and compatible Lenovo gaming laptops. Do not open compatibility requests for other devices.
5. Verify the bug is really LOQ Nova's (no free general troubleshooting).
6. Fill issue forms completely and attach logs (`%LOCALAPPDATA%\LOQNova\log`).
7. Use descriptive titles, stay on topic, one problem per issue.
8. Discuss non-trivial ideas in an issue before sending large PRs.
9. Follow the existing code style and architecture.

Thanks in advance!
