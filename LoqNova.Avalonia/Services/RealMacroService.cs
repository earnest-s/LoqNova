using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LoqNova.Avalonia.Services;
using LoqNova.Lib;
using LoqNova.Lib.Macro;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Adapter over the real <see cref="MacroController"/>.
/// <para>
/// It translates between the UI thread and the controller, and maps a stored Win32
/// virtual-key code back to the key name WPF shows. It holds no macro state of its own:
/// sequences, the enabled flag and persistence all belong to the controller.
/// </para>
/// </summary>
public sealed class RealMacroService : IMacroService
{
    private readonly IMainThreadDispatcher _dispatcher;
    private MacroController? _controller;
    private bool _initialised;

    public RealMacroService(IMainThreadDispatcher dispatcher) => _dispatcher = dispatcher;

    public bool IsEnabled => _controller?.IsEnabled ?? false;

    public bool IsRecording { get; private set; }

    public int[] AllowedRepeatCounts => MacroController.AllowedRepeatCounts;

    public event Action<MacroEvent>? RecorderReceived;

    public event Action<bool>? RecorderStopped;

    public async Task InitializeAsync()
    {
        if (_initialised)
        {
            return;
        }

        // Resolved off the UI thread: IoCContainer.Resolve takes the shared container
        // lock, and MacroController's constructor builds the recorder and player.
        _controller = await Task.Run(() => IoCContainer.Resolve<MacroController>()).ConfigureAwait(false);

        _controller.RecorderReceived += OnRecorderReceived;
        _controller.RecorderStopped += OnRecorderStopped;

        _initialised = true;
    }

    public void SetEnabled(bool enabled) => _controller?.SetEnabled(enabled);

    public MacroSequence GetSequence(MacroIdentifier identifier)
    {
        // GetSequences returns the live store dictionary. A key with no macro is simply
        // absent, so the default is an empty sequence with the backend's own defaults.
        return _controller?.GetSequences().GetValueOrDefault(identifier) ?? new MacroSequence
        {
            RepeatCount = 1,
            IgnoreDelays = false,
            InterruptOnOtherKey = false,
            Events = []
        };
    }

    public void SetSequence(MacroIdentifier identifier, MacroSequence sequence)
    {
        if (_controller is null)
        {
            return;
        }

        // The controller reads the store, writes this key and persists. Going through
        // it is what applies the backend's own CleanUp, which drops key-down events with
        // no matching key-up and removes sequences that ended up empty.
        var sequences = _controller.GetSequences();
        sequences[identifier] = sequence;
        _controller.SetSequences(sequences);
    }

    public void StartRecording(MacroRecorderSettings settings)
    {
        Services.NavDiag.Log("MACRO-START-RECORDING", $"settings={settings}");

        _controller?.StartRecording(settings);
        IsRecording = true;
    }

    public void StopRecording()
    {
        _controller?.StopRecording();
        IsRecording = false;
    }

    private void OnRecorderReceived(object? sender, MacroController.RecorderReceivedEventArgs e) =>
        // The hook procs run on the thread that installed them, so the UI collection is
        // only ever touched from the dispatcher.
        _ = _dispatcher.InvokeAsync(() => RecorderReceived?.Invoke(e.MacroEvent));

    private void OnRecorderStopped(object? sender, MacroController.RecorderStoppedEventArgs e) =>
        _ = _dispatcher.InvokeAsync(() =>
        {
            IsRecording = false;
            RecorderStopped?.Invoke(e.Interrupted);
        });

    /// <summary>
    /// The number-pad keys the controller will play, as Win32 virtual-key codes.
    /// Mirrors the private AllowedKeys set in <see cref="MacroController"/> and the Tag
    /// values in the WPF number pad, which are the same 0x60-0x69 range.
    /// </summary>
    public static readonly ulong[] PadKeyCodes = [0x67, 0x68, 0x69, 0x64, 0x65, 0x66, 0x61, 0x62, 0x63, 0x60];

    /// <summary>
    /// Maps a stored virtual-key code to the key name WPF displays.
    /// WPF uses <c>KeyInterop.KeyFromVirtualKey</c>, which has no Avalonia equivalent, so
    /// the same Win32 code to <c>System.Windows.Input.Key</c> name mapping is kept here.
    /// </summary>
    public static string KeyName(uint virtualKey)
    {
        foreach (var pair in KeyNames)
        {
            if (pair.Value == virtualKey)
            {
                return pair.Key;
            }
        }

        // Left and right modifiers share a code with their NumLock/scroll variants on
        // some layouts; fall back to the raw code rather than showing nothing.
        return virtualKey.ToString();
    }

    private static readonly Dictionary<string, uint> KeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["None"] = 0x00, ["Cancel"] = 0x03, ["Back"] = 0x08, ["Tab"] = 0x09, ["LineFeed"] = 0x0A,
        ["Clear"] = 0x0C, ["Enter"] = 0x0D, ["Pause"] = 0x13, ["Capital"] = 0x14, ["Escape"] = 0x1B,
        ["Space"] = 0x20, ["PageUp"] = 0x21, ["PageDown"] = 0x22, ["End"] = 0x23, ["Home"] = 0x24,
        ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28, ["Select"] = 0x29,
        ["Print"] = 0x2A, ["Execute"] = 0x2B, ["PrintScreen"] = 0x2C, ["Insert"] = 0x2D, ["Delete"] = 0x2E,
        ["Help"] = 0x2F, ["D0"] = 0x30, ["D1"] = 0x31, ["D2"] = 0x32, ["D3"] = 0x33, ["D4"] = 0x34,
        ["D5"] = 0x35, ["D6"] = 0x36, ["D7"] = 0x37, ["D8"] = 0x38, ["D9"] = 0x39, ["A"] = 0x41,
        ["B"] = 0x42, ["C"] = 0x43, ["D"] = 0x44, ["E"] = 0x45, ["F"] = 0x46, ["G"] = 0x47,
        ["H"] = 0x48, ["I"] = 0x49, ["J"] = 0x4A, ["K"] = 0x4B, ["L"] = 0x4C, ["M"] = 0x4D,
        ["N"] = 0x4E, ["O"] = 0x4F, ["P"] = 0x50, ["Q"] = 0x51, ["R"] = 0x52, ["S"] = 0x53,
        ["T"] = 0x54, ["U"] = 0x55, ["V"] = 0x56, ["W"] = 0x57, ["X"] = 0x58, ["Y"] = 0x59,
        ["Z"] = 0x5A, ["LeftWindows"] = 0x5B, ["RightWindows"] = 0x5C, ["Apps"] = 0x5D, ["Sleep"] = 0x5F,
        ["NumPad0"] = 0x60, ["NumPad1"] = 0x61, ["NumPad2"] = 0x62, ["NumPad3"] = 0x63,
        ["NumPad4"] = 0x64, ["NumPad5"] = 0x65, ["NumPad6"] = 0x66, ["NumPad7"] = 0x67,
        ["NumPad8"] = 0x68, ["NumPad9"] = 0x69, ["Multiply"] = 0x6A, ["Add"] = 0x6B,
        ["Separator"] = 0x6C, ["Subtract"] = 0x6D, ["Decimal"] = 0x6E, ["Divide"] = 0x6F,
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73, ["F5"] = 0x74, ["F6"] = 0x75,
        ["F7"] = 0x76, ["F8"] = 0x77, ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
        ["F13"] = 0x7C, ["F14"] = 0x7D, ["F15"] = 0x7E, ["F16"] = 0x7F, ["F17"] = 0x80, ["F18"] = 0x81,
        ["F19"] = 0x82, ["F20"] = 0x83, ["F21"] = 0x84, ["F22"] = 0x85, ["F23"] = 0x86, ["F24"] = 0x87,
        ["NumLock"] = 0x90, ["Scroll"] = 0x91, ["LeftShift"] = 0xA0, ["RightShift"] = 0xA1,
        ["LeftCtrl"] = 0xA2, ["RightCtrl"] = 0xA3, ["LeftAlt"] = 0xA4, ["RightAlt"] = 0xA5,
        ["OemQuestion"] = 0xBF, ["OemComma"] = 0xBC, ["OemMinus"] = 0xBD, ["OemPeriod"] = 0xBE,
        ["OemPlus"] = 0xBB, ["Oem1"] = 0xBA, ["Oem2"] = 0xBF, ["Oem3"] = 0xC0, ["Oem4"] = 0xDB,
        ["Oem5"] = 0xDC, ["Oem6"] = 0xDD, ["Oem7"] = 0xDE, ["Oem8"] = 0xDF
    };
}