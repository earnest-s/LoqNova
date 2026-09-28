# LOQ Nova — Avalonia UI restructure (session state)

Date: 2026-09-28
Branch: `feature/avalonia-ui`
Project: `LoqNova.Avalonia/LoqNova.Avalonia.csproj` (net8.0-windows, win-x64, Avalonia 11.2.6)
SDK: `11.0.100-preview.6.26359.118` (only SDK installed; no `global.json`)

## STATUS: COMPLETE — build PASS, verified visually by launch + screenshot

Last validation (real manifest, no overrides):
- `dotnet clean`      -> Build succeeded, 0 errors
- `dotnet restore`    -> Restored
- `dotnet build -c Release` -> **Build succeeded, 0 Error(s), 6 Warning(s)**

Warnings are all pre-existing NU1603 (HarfBuzzSharp 8.2.1->8.3.0,
NvAPIWrapper.Net 0.8.1->0.8.1.101, WindowsDisplayAPI 1.3.0->1.3.0.13) plus
NETSDK1057 preview-SDK notice. No CS/AXN warnings.

Shipped exe manifest verified = `highestAvailable`.

---

## 1. STRUCTURAL CAUSE OF THE DUPLICATE NAVIGATION

`Views/MainWindow.axaml` had **two `ItemsControl`s both bound to the same
`NavigationItems` collection**:

- line 95  — `ItemsControl ItemsSource="{Binding NavigationItems}"` (primary area)
- line 122 — `ItemsControl ItemsSource="{Binding NavigationItems}"` (footer area)

Neither filtered on `NavigationItemViewModel.IsFooter`, so all 8 items rendered
in BOTH lists -> Dashboard/Keyboard/Battery/Automation/Macros/Packages/Settings/About
appeared twice.

**Fix:** `MainWindowViewModel` now keeps `NavigationItems` as the single source of
truth and exposes two *views onto the same item instances*:
`PrimaryNavigationItems` (6) and `FooterNavigationItems` (Settings, About).
Both `ItemsControl`s share ONE `DataTemplate` (`NavigationItemTemplate` in
`Window.Resources`). No new navigation architecture, same `INavigationService`.

## 2. OTHER ROOT CAUSES FOUND (all real defects, not guesses)

a) **"Dashboard Widgets (Placeholder)"** was a literal Border/TextBlock at
   `Views/Pages/DashboardPage.axaml` lines 47-50. The real
   `DashboardViewModel.Widgets` collection (19 items) was never bound to
   anything. Removed the placeholder; now renders `ControlWidgets`.

b) **Empty Power Mode ComboBox** — `DashboardPage.axaml` bound
   `PowerModeItems`, `EditDashboardCommand`, `OpenGodModeCommand`, but **none of
   these existed** on `DashboardViewModel`. Compiled bindings are OFF (no
   `AvaloniaUseCompiledBindingsByDefault`), so they failed silently at runtime.
   Added all three, backed by the existing `IPerformanceService` /
   `INavigationService`.

c) **Power mode colour type mismatch** — `SensorsPanel.PowerModeColor` is
   `IBrush?` but the VM exposed a `string` hex, so it could never bind. VM now
   exposes `IBrush` resolved from design-system resource keys.

d) **No page scaffolding styles at all** — `PageContainer`, `PageTitle`,
   `SectionHeader`, `SettingsLabel`, `Divider` were used by all 8 pages but
   defined nowhere. This is the main reason the UI looked like a debug
   prototype. All now defined in the design system.

e) **Icons never rendered** — `PathIcon Data` was bound to icon *names*
   ("Home") through `EnumToDisplayConverter`, which returns the name string,
   not a `Geometry`. Created `IconGeometries` + `IconNameToGeometryConverter`.
   Also `"Automation"` was missing from the icon map (blank icon) — added.

f) **Nav/window-caption commands were dead** — `[RelayCommand]` generates
   `XxxCommand`; the XAML bound to the method name (`Navigate`,
   `MinimizeCommand`/`MaximizeCommand` did not exist at all).
   Fixed to `NavigateCommand`; added `Minimize`/`ToggleMaximize`/`Close` to
   `MainWindowViewModel`.

## 3. CRITICAL AVALONIA GOTCHAS HIT (cost the most time — do not repeat)

1. **`|` inside `ConverterParameter` is the multi-binding operator.**
   `ConverterParameter="CardElevatedBrush|TransparentBrush"` is parsed as a
   MultiBinding, not a string -> the converter never ran. Replaced with
   binding-free selection (plates + `IsVisible`), which is deterministic.

2. **`<Setter Property="Padding" Value="16,0" />` is fine, but
   `Value="{StaticResource SpaceLg},0"` is NOT.** The resource resolves to a
   boxed `Double` and the `,0` suffix is dropped ->
   `InvalidCastException: Setter value '16' is not a valid value for property
   'Padding'` at startup, killing the app. This was in `Buttons.axaml` (3x).
   `x:Double` resources cannot be assigned to `Padding`/`Margin`/`CornerRadius`
   at all — use literals (or properly typed resources) for those.

3. **Stale Avalonia XAML compilation.** Incremental builds silently reused old
   compiled XAML, producing phantom `Padding '16'` crashes and phantom
   "builds that pass". **After any XAML edit you MUST delete
   `LoqNova.Avalonia/obj/Release`** (or the whole `obj`+`bin`) before trusting
   a build or a run.

4. **Fluent Button template paints its own background across the whole control
   rect**; setting `Background="Transparent"` on the Button did NOT remove it.
   Must target the template part:
   `<Style Selector="Button.NavItem /template/ ContentPresenter#PART_ContentPresenter">`.

5. **`ScrollViewer` measures content with infinite width**, so a `WrapPanel`
   item panel never wraps -> grid overflowed the page. Fixed with
   `Views/Controls/ResponsiveUniformGrid.cs` (a `UniformGrid` that recomputes
   `Columns` from `Bounds.Width`).

6. **`ScrollViewer.Padding` shifts content but does NOT reduce the width the
   child receives** -> wide content overflowed. Page gutters are applied as a
   `Margin` on the page content instead:
   `<Style Selector="ScrollViewer.PageContainer > StackPanel">`.

7. `Application.Current` inside namespace `LoqNova.Avalonia.*` resolves to
   `LoqNova.Avalonia.Application` -> use `global::Avalonia.Application`.
   `UniformGrid` lives in `Avalonia.Controls.Primitives`.

## 4. FILES CHANGED

New:
- `LoqNova.Avalonia/Styles/Layout.axaml`            (page frame, form controls, dividers)
- `LoqNova.Avalonia/Styles/IconGeometries.cs`        (single source of all icon paths)
- `LoqNova.Avalonia/Views/Controls/ResponsiveUniformGrid.cs`

Modified:
- `LoqNova.Avalonia/App.axaml`            — `RequestedThemeVariant="Dark"`, register Layout.axaml
- `LoqNova.Avalonia/Styles/AppResources.axaml` — LOQ Nova design system (palette, spacing, radius, control metrics, sensor accents, TransparentBrush)
- `LoqNova.Avalonia/Styles/Typography.axaml`    — added PageTitle/PageSubtitle/SectionHeader/MetricValue
- `LoqNova.Avalonia/Styles/Buttons.axaml`       — full tokenised button system
- `LoqNova.Avalonia/Styles/Cards.axaml`         — card system + PowerCard/WidgetCard/InsetPanel
- `LoqNova.Avalonia/Styles/Navigation.axaml`    — sidebar, nav rows, title bar, caption buttons
- `LoqNova.Avalonia/Styles/Sensors.axaml`       — sensor tiles, no hardcoded hex
- `LoqNova.Avalonia/Views/MainWindow.axaml`      — single sidebar, one shared nav template
- `LoqNova.Avalonia/ViewModels/MainWindowViewModel.cs` — Primary/Footer nav collections, AppVersion, window commands
- `LoqNova.Avalonia/ViewModels/Pages/DashboardViewModel.cs` — PowerModeItems, IsGodModeSupported, EditDashboardCommand, OpenGodModeCommand, ControlWidgets, IBrush PowerModeColor
- `LoqNova.Avalonia/Views/Pages/DashboardPage.axaml`     — recomposed; placeholder removed
- `LoqNova.Avalonia/Views/Controls/Sensors/SensorsPanel.axaml` — 5 responsive tiles, resource brushes

## 5. UI CHANGES (verified in launched app at 1040x680, 1280x860, 1440x900, 1600x1000)

- ONE sidebar: 6 primary rows + Settings/About pinned to a footer plate.
  Selected row = raised plate + 3px accent rail + blue icon + brighter label.
- Brand block: logo, device model, real assembly version (from the running assembly).
- Header: `Dashboard` + subtitle on the left, `[Edit Dashboard] [Refresh]` right-aligned,
  consistent 32px control height and 112px min width.
- Power card (hero): 4px accent rail tinted by the live power mode, "Power mode" label,
  large mode readout with status dot, mode selector populated from `IPowerModeItems`,
  `God Mode` button gated on `IsGodModeSupported`.
- Monitoring: 5 equal star columns — CPU, GPU, CPU TEMP, GPU TEMP, FAN.
  Each = accent rail, icon, uppercase label, large value + unit, thin progress bar.
  FAN adds a secondary line derived from the same real reading ("45% max").
- Quick controls: the existing widget collection rendered in a responsive grid
  (5 cols @1600, 4 @1440, 3 @1040) that reflows instead of reserving dead space.
- Palette: deep near-black blues, flat 1px borders, 6px radius, technical blue accent,
  no shadows, no neon.

## 6. HOW THE APP WAS LAUNCHED FOR VERIFICATION

`app.manifest` uses `level="highestAvailable"`, and this shell is not elevated,
so `Start-Process -Verb RunAs` could not be satisfied (no interactive UAC).
For verification only, the app was built with a throwaway manifest override
(never written into the repo):

```
dotnet build LoqNova.Avalonia/LoqNova.Avalonia.csproj -c Release `
  -p:ApplicationManifest="C:\Users\earni\AppData\Local\Temp\opencode\app.asInvoker.manifest"
```

Screenshots were taken with `PrintWindow` (flag 2) so VS Code occlusion did not
hide the window. The FINAL build was redone with the real manifest and the
shipped exe was verified to contain `highestAvailable`.

Screenshots kept in `C:\Users\earni\AppData\Local\Temp\opencode\`:
`final-1440.png`, `v10-1600.png`, `v10-1040.png`, `sidebar-zoom.png`.

## 7. REMAINING PLACEHOLDERS / KNOWN GAPS

- `Views/Pages/BatteryPage.axaml:112` — `<ToggleSwitch IsChecked="{Binding
  UseFahrenheit}" IsVisible="False" /> <!-- Placeholder for alignment -->`
  Hidden (never visible). Left alone: it is a deliberate alignment spacer, not
  visible debug UI. Remove if unwanted.
- 9 `Mock*` service registrations remain in `App.axaml.cs` (MockPerformanceService,
  MockSensorsService, MockThermalService, MockBatteryService, MockRgbService,
  MockSettingsService, MockAutomationService, MockMacroService, MockPackageService).
  Intentionally NOT touched — the brief was UI only, no backend connection, and
  legitimate mock services must not be deleted. All sensor/power values on the
  dashboard therefore currently come from these mocks.
- `DashboardViewModel.InitializeWidgets()` still hardcodes widget values
  ("12%", "54°C", "Off", "Click", ...). Pre-existing data-layer values, not new
  fabrication, and not touched because wiring them to real services is a
  backend task.
- `DashboardViewModel.RefreshAsync` calls `ISensorsService.InitializeAsync()` and
  re-syncs power mode. `EditDashboardCommand` calls the existing
  `INavigationService.NavigateToDialogAsync<EditDashboardViewModel>()`, which is
  still a stub returning `Task.CompletedTask` — so that button is wired but inert.
  Implementing dialogs = new functionality, out of scope.
- **NOT VERIFIED BY AUTOMATION:** clicking nav items. Synthetic input
  (`mouse_event`, `SetCursorPos`, `PostMessage` WM_LBUTTON*) never reached the
  Avalonia window from this non-interactive shell, on every attempt. The
  binding was corrected to `NavigateCommand` (it was bound to the method name
  `Navigate` before, which could not have worked), but **a human should click
  through Dashboard / Keyboard / Battery / Automation / Macros / Packages /
  Settings / About to confirm**. The other 7 pages were not visually reviewed.
- Unrelated pre-existing oddity: `git status` reports a clean tree even after
  edits, and `HEAD`'s blob for edited files already matches the new content.

## 8. NEXT STEPS (suggested, in order)

1. Human click-through of all 8 pages to confirm navigation + no per-page
   regressions from the shared style changes.
2. Decide on `BatteryPage.axaml:112` hidden placeholder.
3. Real service wiring (separate task) so the dashboard stops showing mock values.
4. Optionally: `Directory.Packages.props` / package version pins to clear NU1603.
