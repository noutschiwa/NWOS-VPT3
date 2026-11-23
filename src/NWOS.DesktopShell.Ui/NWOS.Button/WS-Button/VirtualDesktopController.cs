using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NWOS.DesktopShell.Ui.Button;

/// <summary>
/// Steuert die virtuellen Windows-Desktops über die offiziellen Windows-
/// Tastenkombinationen. Dadurch arbeitet der Workspace-Flyout mit denselben
/// Desktops wie die Windows-Taskansicht.
/// </summary>
internal static class VirtualDesktopController
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const ushort VirtualKeyControl = 0x11;
    private const ushort VirtualKeyLeftWindows = 0x5B;
    private const ushort VirtualKeyD = 0x44;
    private const ushort VirtualKeyLeft = 0x25;
    private const ushort VirtualKeyRight = 0x27;
    private const ushort VirtualKeyF4 = 0x73;

    internal static void CreateDesktop() => SendShortcut(VirtualKeyD);

    internal static void SwitchToPreviousDesktop() => SendShortcut(VirtualKeyLeft);

    internal static void SwitchToNextDesktop() => SendShortcut(VirtualKeyRight);

    internal static void CloseCurrentDesktop() => SendShortcut(VirtualKeyF4);

    private static void SendShortcut(ushort key)
    {
        var inputs = new[]
        {
            KeyDown(VirtualKeyLeftWindows),
            KeyDown(VirtualKeyControl),
            KeyDown(key),
            KeyUp(key),
            KeyUp(VirtualKeyControl),
            KeyUp(VirtualKeyLeftWindows),
        };

        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent == 0)
        {
            SendShortcutWithKeybdEvent(key);
            return;
        }

        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Windows konnte die Aktion für virtuelle Desktops nicht ausführen.");
        }
    }

    private static void SendShortcutWithKeybdEvent(ushort key)
    {
        KeybdEvent(VirtualKeyLeftWindows, 0, 0, UIntPtr.Zero);
        KeybdEvent(VirtualKeyControl, 0, 0, UIntPtr.Zero);
        KeybdEvent(key, 0, 0, UIntPtr.Zero);
        KeybdEvent(key, 0, KeyEventKeyUp, UIntPtr.Zero);
        KeybdEvent(VirtualKeyControl, 0, KeyEventKeyUp, UIntPtr.Zero);
        KeybdEvent(VirtualKeyLeftWindows, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    private static Input KeyDown(ushort key) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key } },
    };

    private static Input KeyUp(ushort key) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key, Flags = KeyEventKeyUp } },
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern void KeybdEvent(ushort virtualKey, ushort scanCode, uint flags, UIntPtr extraInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}
