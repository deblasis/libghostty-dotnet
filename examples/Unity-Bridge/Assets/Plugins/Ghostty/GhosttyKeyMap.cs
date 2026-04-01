using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Ghostty.Unity
{
    public static class GhosttyKeyMap
    {
#if ENABLE_INPUT_SYSTEM
        public static int GetModsInputSystem()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return 0;

            int mods = 0;
            if (keyboard.shiftKey.isPressed) mods |= 1 << 0; // SHIFT
            if (keyboard.ctrlKey.isPressed)  mods |= 1 << 1; // CTRL
            if (keyboard.altKey.isPressed)   mods |= 1 << 2; // ALT
            return mods;
        }

        public static uint KeyToScanCode(Key key)
        {
            return key switch
            {
                Key.A => 0x1E, Key.B => 0x30, Key.C => 0x2E, Key.D => 0x20,
                Key.E => 0x12, Key.F => 0x21, Key.G => 0x22, Key.H => 0x23,
                Key.I => 0x17, Key.J => 0x24, Key.K => 0x25, Key.L => 0x26,
                Key.M => 0x32, Key.N => 0x31, Key.O => 0x18, Key.P => 0x19,
                Key.Q => 0x10, Key.R => 0x13, Key.S => 0x1F, Key.T => 0x14,
                Key.U => 0x16, Key.V => 0x2F, Key.W => 0x11, Key.X => 0x2D,
                Key.Y => 0x15, Key.Z => 0x2C,
                Key.Digit0 => 0x0B, Key.Digit1 => 0x02, Key.Digit2 => 0x03,
                Key.Digit3 => 0x04, Key.Digit4 => 0x05, Key.Digit5 => 0x06,
                Key.Digit6 => 0x07, Key.Digit7 => 0x08, Key.Digit8 => 0x09,
                Key.Digit9 => 0x0A,
                Key.Space => 0x39, Key.Enter => 0x1C, Key.Tab => 0x0F,
                Key.Backspace => 0x0E, Key.Escape => 0x01,
                Key.LeftArrow => 0x4B, Key.RightArrow => 0x4D,
                Key.UpArrow => 0x48, Key.DownArrow => 0x50,
                Key.Home => 0x47, Key.End => 0x4F,
                Key.PageUp => 0x49, Key.PageDown => 0x51,
                Key.Insert => 0x52, Key.Delete => 0x53,
                Key.F1 => 0x3B, Key.F2 => 0x3C, Key.F3 => 0x3D, Key.F4 => 0x3E,
                Key.F5 => 0x3F, Key.F6 => 0x40, Key.F7 => 0x41, Key.F8 => 0x42,
                Key.F9 => 0x43, Key.F10 => 0x44, Key.F11 => 0x57, Key.F12 => 0x58,
                Key.Minus => 0x0C, Key.Equals => 0x0D,
                Key.LeftBracket => 0x1A, Key.RightBracket => 0x1B,
                Key.Backslash => 0x2B, Key.Semicolon => 0x27, Key.Quote => 0x28,
                Key.Comma => 0x33, Key.Period => 0x34, Key.Slash => 0x35,
                Key.Backquote => 0x29,
                _ => 0,
            };
        }
#endif

        public static uint KeyCodeToScanCode(KeyCode keyCode)
        {
            return keyCode switch
            {
                KeyCode.A => 0x1E, KeyCode.B => 0x30, KeyCode.C => 0x2E, KeyCode.D => 0x20,
                KeyCode.E => 0x12, KeyCode.F => 0x21, KeyCode.G => 0x22, KeyCode.H => 0x23,
                KeyCode.I => 0x17, KeyCode.J => 0x24, KeyCode.K => 0x25, KeyCode.L => 0x26,
                KeyCode.M => 0x32, KeyCode.N => 0x31, KeyCode.O => 0x18, KeyCode.P => 0x19,
                KeyCode.Q => 0x10, KeyCode.R => 0x13, KeyCode.S => 0x1F, KeyCode.T => 0x14,
                KeyCode.U => 0x16, KeyCode.V => 0x2F, KeyCode.W => 0x11, KeyCode.X => 0x2D,
                KeyCode.Y => 0x15, KeyCode.Z => 0x2C,
                KeyCode.Alpha0 => 0x0B, KeyCode.Alpha1 => 0x02, KeyCode.Alpha2 => 0x03,
                KeyCode.Alpha3 => 0x04, KeyCode.Alpha4 => 0x05, KeyCode.Alpha5 => 0x06,
                KeyCode.Alpha6 => 0x07, KeyCode.Alpha7 => 0x08, KeyCode.Alpha8 => 0x09,
                KeyCode.Alpha9 => 0x0A,
                KeyCode.Space => 0x39, KeyCode.Return => 0x1C, KeyCode.Tab => 0x0F,
                KeyCode.Backspace => 0x0E, KeyCode.Escape => 0x01,
                KeyCode.LeftArrow => 0x4B, KeyCode.RightArrow => 0x4D,
                KeyCode.UpArrow => 0x48, KeyCode.DownArrow => 0x50,
                KeyCode.Home => 0x47, KeyCode.End => 0x4F,
                KeyCode.PageUp => 0x49, KeyCode.PageDown => 0x51,
                KeyCode.Insert => 0x52, KeyCode.Delete => 0x53,
                KeyCode.F1 => 0x3B, KeyCode.F2 => 0x3C, KeyCode.F3 => 0x3D, KeyCode.F4 => 0x3E,
                KeyCode.F5 => 0x3F, KeyCode.F6 => 0x40, KeyCode.F7 => 0x41, KeyCode.F8 => 0x42,
                KeyCode.F9 => 0x43, KeyCode.F10 => 0x44, KeyCode.F11 => 0x57, KeyCode.F12 => 0x58,
                KeyCode.Minus => 0x0C, KeyCode.Equals => 0x0D,
                KeyCode.LeftBracket => 0x1A, KeyCode.RightBracket => 0x1B,
                KeyCode.Backslash => 0x2B, KeyCode.Semicolon => 0x27, KeyCode.Quote => 0x28,
                KeyCode.Comma => 0x33, KeyCode.Period => 0x34, KeyCode.Slash => 0x35,
                KeyCode.BackQuote => 0x29,
                _ => 0,
            };
        }
    }
}
