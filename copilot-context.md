# LoqNova System Context

## Project Type
C# WPF desktop application controlling Lenovo LOQ RGB + performance system

## Core Systems
- RGB Engine (4-zone keyboard lighting)
- Performance Mode Controller (Quiet / Balance / Performance / Custom)
- OSD Notification System
- Hardware Listeners (WMI, keyboard hooks)
- Animation Engine (strobe, breathing, audio visualizer)

## Critical Behavior Rules
- NEVER break real-time responsiveness
- RGB updates must be non-blocking
- No UI thread blocking
- Hardware events must be instant (Fn+Q level latency)

## Known Issues
- Strobe effect timing inconsistencies
- Keyboard glitch after performance animation
- OSD delay mismatch vs hardware trigger
- Battery mode lag due to efficiency mode

## Architecture Style
- Event-driven
- Async + background processing
- Separation of UI and hardware logic

## Important Notes
- Keyboard preview must reflect REAL hardware output (not simulation)
- Performance mode changes must sync:
  - OSD
  - RGB animation
  - UI updates

## Instruction to Copilot
Treat this file as SYSTEM ARCHITECTURE.
Do NOT analyze files in isolation.
Always maintain continuity across modules.

## Runtime Truth: Fn+Q Sequence (Verified)
1. Listener auto-start
  - PowerModeListener is auto-activated in IoC and StartAsync is called on activation.
2. Hardware trigger
  - LenovoGameZoneSmartFanModeEvent emits value.
3. WMI callback
  - AbstractWMIListener.Handler receives event, converts raw value, awaits OnChangedAsync.
4. Power mode feature entry
  - PowerModeListener.OnChangedAsync calls PowerModeFeature.ApplyPerformanceModeAsync(value).
5. OSD publish (first)
  - PowerModeFeature.PublishNotification publishes NotificationMessage through MessagingCenter.
6. RGB transition trigger (concurrent)
  - PowerModeFeature.FireStrobeAsync triggers RGBKeyboardBacklightController.PlayTransitionAsync.
7. Dependency chain (awaited)
  - ApplyDependenciesAsync runs GodMode apply (if needed), WindowsPowerModeController, WindowsPowerPlanController.
8. UI refresh event (later)
  - After ApplyPerformanceModeAsync returns, AbstractWMIListener raises Changed.
  - PowerModeControl receives listener event and refreshes through ThrottleLastDispatcher.

## Drift Points (Verified)
- WMI callback scheduling and async-void listener boundary.
- Fire-and-forget strobe running in parallel with dependency await chain.
- WindowsPowerModeController has 2-second throttle dispatch.
- PowerModeControl refresh path uses 500 ms throttle.
- Notification manager has 250 ms debounce and duplicate suppression for power-mode OSD.
- RGB writes are serialized by single HID lock and raw writes run through Task.Run.

## Confirmed Race at Transition End
### Components involved
- RGBKeyboardBacklightController.RunTransitionAsync and ResumeAfterTransitionAsync.
- CustomRGBEffectController render loop (SetColorsAsync -> dispatcher.RenderAsync).
- RgbFrameDispatcher override gate (IsOverrideActive) and force path.

### Current race window
- ResumeFromOverrideAsync currently does:
  1. IsOverrideActive = false
  2. ForceRenderAsync(_currentColors)
- While this happens, custom effect loop can also push RenderAsync as soon as override is lifted.
- Transition code also pushes a final black frame before resume.

### Result
- Multiple near-simultaneous writers at handoff can reorder frames.
- Brief static frame can appear before normal effect cadence resumes.

## Frame Writers at Transition End
- Writer A: Transition task final black ForceRenderAsync.
- Writer B: Resume path ForceRenderAsync(_currentColors).
- Writer C: Live custom effect RenderAsync next frame.
- Optional Writer D: New transition if Fn+Q retriggers during teardown.

## Ownership-Transfer Fix Contract (Must Implement)
Goal: zero static frame, no flicker, deterministic handoff.

1. Make dispatcher the only ownership authority.
  - Replace bare IsOverrideActive handoff with override session token model.
  - Add BeginOverride() -> sessionId.
  - Add ForceRenderOverrideAsync(sessionId, frame).
  - Add EndOverrideAndFlushPendingAsync(sessionId).

2. Buffer latest normal frame during override.
  - In RenderAsync, if override is active, store latest pending normal frame instead of dropping forever.
  - Do not emit FrameRendered for suppressed normal frames.

3. End override atomically under HID lock.
  - EndOverrideAndFlushPendingAsync must:
    - validate session id,
    - clear override ownership,
    - immediately flush last pending normal frame as first post-transition visible frame.

4. Remove synthetic resume writer from CustomRGBEffectController.
  - Do not perform "override false then ForceRenderAsync(_currentColors)" in resume path.
  - Resume ownership only through dispatcher end-override method.

5. Update transition flow in RGBKeyboardBacklightController.
  - PlayTransitionAsync acquires session id from dispatcher.
  - RunTransitionAsync renders only through session-aware force method.
  - ResumeAfterTransitionAsync ends override via dispatcher and does not inject extra static frame.

## Non-Negotiable Guardrails
- Never block UI thread for Fn+Q path.
- Never let multiple owners write frame output during transfer.
- Preview must mirror real hardware output from same dispatcher path.
- Keep OSD, RGB transition, and UI state changes consistent even when throttled/debounced.

## Frame Ownership Rule (STRICT)
At any time, exactly ONE component owns frame output to hardware.

Valid owners:
- Transition controller (during override session)
- CustomRGBEffectController (normal operation)

Dispatcher enforces ownership.
No other component may write frames directly.

Violation of this rule causes flicker or undefined output.

## Timing Model
- Frame rate target: ~60 FPS (16 ms)
- Transition timing must not depend on UI thread
- All RGB writes must be serialized under single HID lock
- Task.Delay is non-deterministic; logic must tolerate jitter

## Forbidden Patterns
- Do NOT use fire-and-forget for critical RGB transitions without ownership control
- Do NOT write directly to hardware outside dispatcher
- Do NOT toggle override flags outside dispatcher session model
- Do NOT inject synthetic frames during ownership transfer

## State Consistency Rule
On performance mode change, the following must represent the SAME state:
- RGB keyboard state
- OSD display
- UI selected mode

Temporary desync is allowed only during transition, but must converge deterministically.