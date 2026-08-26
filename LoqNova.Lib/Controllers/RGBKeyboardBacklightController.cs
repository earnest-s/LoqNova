// #define MOCK_RGB

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LoqNova.Lib.Controllers.CustomRGBEffects;
using LoqNova.Lib.Controllers.CustomRGBEffects.SignalProviders;
using LoqNova.Lib.Controllers.Sensors;
using LoqNova.Lib.Extensions;
using LoqNova.Lib.Settings;
using LoqNova.Lib.SoftwareDisabler;
using LoqNova.Lib.System.Management;
using LoqNova.Lib.Utils;
using NeoSmart.AsyncLock;

namespace LoqNova.Lib.Controllers
{
    public class RGBKeyboardBacklightController(
        RGBKeyboardSettings settings,
        VantageDisabler vantageDisabler,
        CustomRGBEffectController customEffectController,
        ISensorsController sensorsController,
        RgbFrameDispatcher dispatcher)
    {
        private static readonly AsyncLock IoLock = new();

        // --- Performance mode transition state ---
        private CancellationTokenSource? _transitionCts;
        private Task? _transitionTask;

        public Task<bool> IsSupportedAsync()
        {
            return Task.FromResult(dispatcher.IsSupported);
        }

        public async Task SetLightControlOwnerAsync(bool enable, bool restorePreset = false)
        {
            using (await IoLock.LockAsync().ConfigureAwait(false))
            {
                try
                {
                    if (!dispatcher.IsSupported)
                        throw new InvalidOperationException("RGB Keyboard unsupported");

                    await ThrowIfVantageEnabled().ConfigureAwait(false);

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Taking ownership...");

#if !MOCK_RGB
                    await WMI.LenovoGameZoneData.SetLightControlOwnerAsync(enable ? 1 : 0).ConfigureAwait(false);
#endif

                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Ownership set to {enable}, restoring profile...");

                    if (restorePreset)
                    {
                        if (Log.Instance.IsTraceEnabled)
                            Log.Instance.Trace($"Restoring preset...");

                        await SetCurrentPresetAsync().ConfigureAwait(false);

                        if (Log.Instance.IsTraceEnabled)
                            Log.Instance.Trace($"Restored preset");
                    }
                }
                catch (Exception ex)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Can't take ownership.", ex);

                    throw;
                }
            }
        }

        public async Task<RGBKeyboardBacklightState> GetStateAsync()
        {
            using (await IoLock.LockAsync().ConfigureAwait(false))
            {
                if (!dispatcher.IsSupported)
                    throw new InvalidOperationException("RGB Keyboard unsupported");

                await ThrowIfVantageEnabled().ConfigureAwait(false);

                return settings.Store.State;
            }
        }

        public async Task SetStateAsync(RGBKeyboardBacklightState state)
        {
            using (await IoLock.LockAsync().ConfigureAwait(false))
            {
                if (!dispatcher.IsSupported)
                    throw new InvalidOperationException("RGB Keyboard unsupported");

                await ThrowIfVantageEnabled().ConfigureAwait(false);

                settings.Store.State = state;
                settings.SynchronizeStore();

                var selectedPreset = state.SelectedPreset;

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Selected preset: {selectedPreset}");

                if (selectedPreset == RGBKeyboardBacklightPreset.Off)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Creating off state.");

                    await customEffectController.StopEffectAsync().ConfigureAwait(false);
                    await dispatcher.SendFirmwareCommandAsync(CreateOffState()).ConfigureAwait(false);
                    dispatcher.RenderPreviewOnly(ZoneColors.Black);
                    return;
                }

                var presetDescription = state.Presets.GetValueOrDefault(selectedPreset, RGBKeyboardBacklightBacklightPresetDescription.Default);

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Creating state: {presetDescription}");

                await dispatcher.SendFirmwareCommandAsync(Convert(presetDescription)).ConfigureAwait(false);

                // Notify preview for non-custom presets
                if (!presetDescription.Effect.IsCustomEffect())
                {
                    dispatcher.RenderPreviewOnly(new ZoneColors(
                        presetDescription.Zone1, presetDescription.Zone2,
                        presetDescription.Zone3, presetDescription.Zone4));
                }

                // Start custom effect if applicable
                await HandleCustomEffectAsync(presetDescription).ConfigureAwait(false);
            }
        }

        public async Task SetPresetAsync(RGBKeyboardBacklightPreset preset)
        {
            using (await IoLock.LockAsync().ConfigureAwait(false))
            {
                if (!dispatcher.IsSupported)
                    throw new InvalidOperationException("RGB Keyboard unsupported");

                await ThrowIfVantageEnabled().ConfigureAwait(false);

                var state = settings.Store.State;
                var presets = state.Presets;

                settings.Store.State = new(preset, presets);
                settings.SynchronizeStore();

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Preset is {preset}.");

                if (preset == RGBKeyboardBacklightPreset.Off)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Creating off state.");

                    await customEffectController.StopEffectAsync().ConfigureAwait(false);
                    await dispatcher.SendFirmwareCommandAsync(CreateOffState()).ConfigureAwait(false);
                    dispatcher.RenderPreviewOnly(ZoneColors.Black);
                    return;
                }

                var presetDescription = state.Presets.GetValueOrDefault(preset, RGBKeyboardBacklightBacklightPresetDescription.Default);

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Creating state: {presetDescription}");

                await dispatcher.SendFirmwareCommandAsync(Convert(presetDescription)).ConfigureAwait(false);

                if (!presetDescription.Effect.IsCustomEffect())
                {
                    dispatcher.RenderPreviewOnly(new ZoneColors(
                        presetDescription.Zone1, presetDescription.Zone2,
                        presetDescription.Zone3, presetDescription.Zone4));
                }

                await HandleCustomEffectAsync(presetDescription).ConfigureAwait(false);
            }
        }

        public async Task<RGBKeyboardBacklightPreset> SetNextPresetAsync()
        {
            using (await IoLock.LockAsync().ConfigureAwait(false))
            {
                if (!dispatcher.IsSupported)
                    throw new InvalidOperationException("RGB Keyboard unsupported");

                await ThrowIfVantageEnabled().ConfigureAwait(false);

                var state = settings.Store.State;

                var newPreset = state.SelectedPreset.Next();
                var presets = state.Presets;

                settings.Store.State = new(newPreset, presets);
                settings.SynchronizeStore();

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"New preset is {newPreset}.");

                if (newPreset == RGBKeyboardBacklightPreset.Off)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Creating off state.");

                    await customEffectController.StopEffectAsync().ConfigureAwait(false);
                    await dispatcher.SendFirmwareCommandAsync(CreateOffState()).ConfigureAwait(false);
                    dispatcher.RenderPreviewOnly(ZoneColors.Black);
                    return newPreset;
                }

                var presetDescription = state.Presets.GetValueOrDefault(newPreset, RGBKeyboardBacklightBacklightPresetDescription.Default);

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Creating state: {presetDescription}");

                await dispatcher.SendFirmwareCommandAsync(Convert(presetDescription)).ConfigureAwait(false);

                if (!presetDescription.Effect.IsCustomEffect())
                {
                    dispatcher.RenderPreviewOnly(new ZoneColors(
                        presetDescription.Zone1, presetDescription.Zone2,
                        presetDescription.Zone3, presetDescription.Zone4));
                }

                await HandleCustomEffectAsync(presetDescription).ConfigureAwait(false);

                return newPreset;
            }
        }

        private async Task SetCurrentPresetAsync()
        {
            if (!dispatcher.IsSupported)
                throw new InvalidOperationException("RGB Keyboard unsupported");

            await ThrowIfVantageEnabled().ConfigureAwait(false);

            var state = settings.Store.State;
            var preset = state.SelectedPreset;

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Current preset is {preset}.");

            if (preset == RGBKeyboardBacklightPreset.Off)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Creating off state.");

                await customEffectController.StopEffectAsync().ConfigureAwait(false);
                await dispatcher.SendFirmwareCommandAsync(CreateOffState()).ConfigureAwait(false);
                dispatcher.RenderPreviewOnly(ZoneColors.Black);
                return;
            }

            var presetDescription = state.Presets.GetValueOrDefault(preset, RGBKeyboardBacklightBacklightPresetDescription.Default);

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Creating state: {presetDescription}");

            await dispatcher.SendFirmwareCommandAsync(Convert(presetDescription)).ConfigureAwait(false);

            // Notify preview for firmware-driven presets
            if (!presetDescription.Effect.IsCustomEffect())
            {
                dispatcher.RenderPreviewOnly(new ZoneColors(
                    presetDescription.Zone1, presetDescription.Zone2,
                    presetDescription.Zone3, presetDescription.Zone4));
            }

            // Start custom effect if applicable
            await HandleCustomEffectAsync(presetDescription).ConfigureAwait(false);
        }

        private async Task ThrowIfVantageEnabled()
        {
            var vantageStatus = await vantageDisabler.GetStatusAsync().ConfigureAwait(false);
            if (vantageStatus == SoftwareStatus.Enabled)
                throw new InvalidOperationException("Can't manage RGB keyboard with Vantage enabled");
        }

        private static LENOVO_RGB_KEYBOARD_STATE CreateOffState()
        {
            return new()
            {
                Header = [0xCC, 0x16],
                Unused = new byte[13],
                Padding = 0,
                Effect = 0,
                WaveLTR = 0,
                WaveRTL = 0,
                Brightness = 0,
                Zone1Rgb = new byte[3],
                Zone2Rgb = new byte[3],
                Zone3Rgb = new byte[3],
                Zone4Rgb = new byte[3],
            };
        }

        private static LENOVO_RGB_KEYBOARD_STATE Convert(RGBKeyboardBacklightBacklightPresetDescription preset)
        {
            // For custom effects, use Static mode as base - the CustomRGBEffectController handles the animation
            var effectForHardware = preset.Effect.IsCustomEffect() ? RGBKeyboardBacklightEffect.Static : preset.Effect;

            var result = new LENOVO_RGB_KEYBOARD_STATE
            {
                Header = [0xCC, 0x16],
                Unused = new byte[13],
                Padding = 0x0,
                Zone1Rgb = [0xFF, 0xFF, 0xFF],
                Zone2Rgb = [0xFF, 0xFF, 0xFF],
                Zone3Rgb = [0xFF, 0xFF, 0xFF],
                Zone4Rgb = [0xFF, 0xFF, 0xFF],
                Effect = effectForHardware switch
                {
                    RGBKeyboardBacklightEffect.Static => 1,
                    RGBKeyboardBacklightEffect.Breath => 3,
                    RGBKeyboardBacklightEffect.WaveRTL => 4,
                    RGBKeyboardBacklightEffect.WaveLTR => 4,
                    RGBKeyboardBacklightEffect.Smooth => 6,
                    _ => 1 // Default to static for custom effects
                },
                WaveRTL = (byte)(effectForHardware == RGBKeyboardBacklightEffect.WaveRTL ? 1 : 0),
                WaveLTR = (byte)(effectForHardware == RGBKeyboardBacklightEffect.WaveLTR ? 1 : 0),
                Brightness = preset.Brightness switch
                {
                    RGBKeyboardBacklightBrightness.Low => 1,
                    RGBKeyboardBacklightBrightness.High => 2,
                    _ => 0
                }
            };


            if (effectForHardware != RGBKeyboardBacklightEffect.Static)
            {
                result.Speed = preset.Speed switch
                {
                    RGBKeyboardBacklightSpeed.Slowest => 1,
                    RGBKeyboardBacklightSpeed.Slow => 2,
                    RGBKeyboardBacklightSpeed.Fast => 3,
                    RGBKeyboardBacklightSpeed.Fastest => 4,
                    _ => 0
                };
            }

            if (effectForHardware is RGBKeyboardBacklightEffect.Static or RGBKeyboardBacklightEffect.Breath)
            {
                result.Zone1Rgb = [preset.Zone1.R, preset.Zone1.G, preset.Zone1.B];
                result.Zone2Rgb = [preset.Zone2.R, preset.Zone2.G, preset.Zone2.B];
                result.Zone3Rgb = [preset.Zone3.R, preset.Zone3.G, preset.Zone3.B];
                result.Zone4Rgb = [preset.Zone4.R, preset.Zone4.G, preset.Zone4.B];
            }

            return result;
        }

        /// <summary>
        /// Plays a premium performance-mode transition animation.
        /// Pauses the current effect, plays the strobe, then seamlessly resumes.
        /// Double-trigger safe: a new call cancels any running transition.
        /// Uses centralized <see cref="RgbFrameDispatcher.GetPerformanceModeColor"/>.
        /// </summary>
        public async Task PlayTransitionAsync(PowerModeState mode)
        {
            // [DIAGNOSTIC-ONLY] entry telemetry (no behavior change)
            var diagSw = System.Diagnostics.Stopwatch.StartNew();
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[DIAG] PlayTransitionAsync ENTER. [mode={mode}, supported={dispatcher.IsSupported}, overrideActive={dispatcher.IsOverrideActive}, prevTask={_transitionTask?.Status.ToString() ?? "null"}]");

            var modeColor = RgbFrameDispatcher.GetPerformanceModeColor(mode);

            // Cancel any in-flight transition
            if (_transitionCts is not null)
            {
                await _transitionCts.CancelAsync().ConfigureAwait(false);
                if (_transitionTask is not null)
                {
                    try { await _transitionTask.ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                }
                _transitionCts.Dispose();
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[DIAG] PlayTransitionAsync previous transition cancelled+drained. [drainMs={diagSw.ElapsedMilliseconds}]");
            }

            // Pause running custom effect (it stays alive in memory)
            dispatcher.IsOverrideActive = true;

            _transitionCts = new CancellationTokenSource();
            _transitionTask = RunTransitionAsync(modeColor, _transitionCts.Token);

            if (Log.Instance.IsTraceEnabled)
            {
                Log.Instance.Trace($"[DIAG] PlayTransitionAsync TASK STARTED. [mode={mode}, color=#{modeColor.R:X2}{modeColor.G:X2}{modeColor.B:X2}, setupMs={diagSw.ElapsedMilliseconds}]");
                Log.Instance.Trace($"Transition triggered for power mode: {mode}");
            }
        }

        private async Task RunTransitionAsync(RGBColor modeColor, CancellationToken cancellationToken)
        {
            // [DIAGNOSTIC-ONLY]
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[DIAG] RunTransitionAsync START.");

            // Full brightness for strobe, restore after
            var savedBrightness = dispatcher.CurrentBrightness;
            dispatcher.CurrentBrightness = 2;

            try
            {
                await PerformanceModeTransitionEffect.PlayAsync(dispatcher, modeColor, cancellationToken)
                    .ConfigureAwait(false);

                // Safety black frame
                await dispatcher.ForceRenderAsync(ZoneColors.Black).ConfigureAwait(false);

                // [DIAGNOSTIC-ONLY]
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[DIAG] RunTransitionAsync PlayAsync COMPLETED NORMALLY (+black frame).");
            }
            catch (OperationCanceledException)
            {
                // [DIAGNOSTIC-ONLY]
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[DIAG] RunTransitionAsync CANCELLED via OperationCanceledException. [tokenCancelled={cancellationToken.IsCancellationRequested}]");
            }
            finally
            {
                dispatcher.CurrentBrightness = savedBrightness;

                try
                {
                    await ResumeAfterTransitionAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"Failed to resume after transition", ex);
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"[DIAG] ResumeAfterTransitionAsync THREW. [type={ex.GetType().FullName}, msg={ex.Message}]");
                }

                if (Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"Transition ended, effect resumed");
                    Log.Instance.Trace($"[DIAG] RunTransitionAsync FINALLY done. [brightnessRestored={savedBrightness}]");
                }
            }
        }

        /// <summary>
        /// Resumes the current RGB preset after a transition animation ends.
        /// </summary>
        private async Task ResumeAfterTransitionAsync()
        {
            // [DIAGNOSTIC-ONLY]
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[DIAG] ResumeAfterTransitionAsync ENTER. [effectRunning={customEffectController.IsEffectRunning}]");

            await ThrowIfVantageEnabled().ConfigureAwait(false);

            var state = settings.Store.State;
            var preset = state.SelectedPreset;

            if (preset == RGBKeyboardBacklightPreset.Off)
            {
                dispatcher.IsOverrideActive = false;
                await customEffectController.StopEffectAsync().ConfigureAwait(false);
                await dispatcher.SendFirmwareCommandAsync(CreateOffState()).ConfigureAwait(false);
                dispatcher.RenderPreviewOnly(ZoneColors.Black);
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[DIAG] ResumeAfterTransitionAsync branch=OFF.");
                return;
            }

            var presetDescription = state.Presets.GetValueOrDefault(
                preset, RGBKeyboardBacklightBacklightPresetDescription.Default);

            if (presetDescription.Effect.IsCustomEffect())
            {
                if (customEffectController.IsEffectRunning)
                {
                    await customEffectController.ResumeFromOverrideAsync().ConfigureAwait(false);
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"[DIAG] ResumeAfterTransitionAsync branch=CUSTOM-RESUME-FROM-OVERRIDE.");
                }
                else
                {
                    dispatcher.IsOverrideActive = false;
                    await HandleCustomEffectAsync(presetDescription).ConfigureAwait(false);
                    if (Log.Instance.IsTraceEnabled)
                        Log.Instance.Trace($"[DIAG] ResumeAfterTransitionAsync branch=CUSTOM-RESTART.");
                }
            }
            else
            {
                dispatcher.IsOverrideActive = false;
                await dispatcher.SendFirmwareCommandAsync(Convert(presetDescription)).ConfigureAwait(false);
                dispatcher.RenderPreviewOnly(new ZoneColors(
                    presetDescription.Zone1, presetDescription.Zone2,
                    presetDescription.Zone3, presetDescription.Zone4));
                await HandleCustomEffectAsync(presetDescription).ConfigureAwait(false);
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"[DIAG] ResumeAfterTransitionAsync branch=FIRMWARE-PRESET [{presetDescription.Effect}].");
            }
        }

        private async Task HandleCustomEffectAsync(RGBKeyboardBacklightBacklightPresetDescription preset)
        {
            // Stop any running custom effect first
            await customEffectController.StopEffectAsync().ConfigureAwait(false);

            if (!preset.Effect.IsCustomEffect())
                return;

            var customEffectType = preset.Effect.ToCustomEffectType();
            if (customEffectType is null)
                return;

            // Set brightness on the dispatcher (used by all HID writes)
            dispatcher.CurrentBrightness = preset.Brightness switch
            {
                RGBKeyboardBacklightBrightness.Low => 1,
                RGBKeyboardBacklightBrightness.High => 2,
                _ => 2
            };

            // Create zone colors from preset
            var zoneColors = new ZoneColors
            {
                Zone1 = preset.Zone1,
                Zone2 = preset.Zone2,
                Zone3 = preset.Zone3,
                Zone4 = preset.Zone4
            };

            // Map speed (1-4) from RGBKeyboardBacklightSpeed
            var speed = preset.Speed switch
            {
                RGBKeyboardBacklightSpeed.Slowest => 1,
                RGBKeyboardBacklightSpeed.Slow => 2,
                RGBKeyboardBacklightSpeed.Fast => 3,
                RGBKeyboardBacklightSpeed.Fastest => 4,
                _ => 2
            };

            // Create input/screen providers for effects that need them
            IInputSignalProvider? inputProvider = null;
            IScreenColorProvider? screenProvider = null;

            if (preset.Effect.RequiresInputProvider())
                inputProvider = CustomRGBEffectFactory.CreateInputProvider();

            if (preset.Effect.RequiresScreenProvider())
                screenProvider = CustomRGBEffectFactory.CreateScreenProvider();

            try
            {
                var effect = CustomRGBEffectFactory.CreateByType(
                    customEffectType.Value,
                    zoneColors,
                    speed,
                    EffectDirection.Right,
                    sensorsController,
                    inputProvider,
                    screenProvider);

                await customEffectController.StartEffectAsync(effect).ConfigureAwait(false);

                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Started custom effect: {customEffectType}");
            }
            catch (Exception ex)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Failed to start custom effect {customEffectType}", ex);

                // Clean up providers on failure
                inputProvider?.Dispose();
                screenProvider?.Dispose();
            }
        }
    }
}
