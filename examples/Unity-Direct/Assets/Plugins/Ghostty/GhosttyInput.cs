using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Ghostty.Unity
{
    public class GhosttyInput
    {
        private readonly IntPtr _surface;

        public GhosttyInput(IntPtr surface)
        {
            _surface = surface;
        }

        // ---- Keyboard (New Input System) ----

#if ENABLE_INPUT_SYSTEM
        public void ProcessKeyboard()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            foreach (var key in keyboard.allKeys)
            {
                if (key.wasPressedThisFrame)
                    SendKey(key.keyCode, ghostty_input_action_e.GHOSTTY_ACTION_PRESS);
                else if (key.wasReleasedThisFrame)
                    SendKey(key.keyCode, ghostty_input_action_e.GHOSTTY_ACTION_RELEASE);
            }
        }

        public void EnableTextInput(Action<char> onChar)
        {
            Keyboard.current.onTextInput += onChar;
        }

        public void DisableTextInput(Action<char> onChar)
        {
            if (Keyboard.current != null)
                Keyboard.current.onTextInput -= onChar;
        }

        private void SendKey(UnityEngine.InputSystem.Key key, ghostty_input_action_e action)
        {
            uint scanCode = KeyToScanCode(key);
            if (scanCode == 0) return;

            var mods = GetModsInputSystem();
            var keyEvent = new ghostty_input_key_s
            {
                action = action,
                mods = mods,
                consumed_mods = ghostty_input_mods_e.GHOSTTY_MODS_NONE,
                keycode = scanCode,
                text = IntPtr.Zero,
                unshifted_codepoint = 0,
                composing = 0,
            };

            GhosttyNative.ghostty_surface_key(_surface, ref keyEvent);
        }

        private static ghostty_input_mods_e GetModsInputSystem()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return ghostty_input_mods_e.GHOSTTY_MODS_NONE;

            var mods = ghostty_input_mods_e.GHOSTTY_MODS_NONE;
            if (keyboard.shiftKey.isPressed) mods |= ghostty_input_mods_e.GHOSTTY_MODS_SHIFT;
            if (keyboard.ctrlKey.isPressed) mods |= ghostty_input_mods_e.GHOSTTY_MODS_CTRL;
            if (keyboard.altKey.isPressed) mods |= ghostty_input_mods_e.GHOSTTY_MODS_ALT;
            return mods;
        }
#endif

        // ---- Keyboard (Legacy Input Manager, for Editor) ----

        public void ProcessEditorKeyEvent(Event evt)
        {
            if (evt == null) return;

            ghostty_input_action_e action;
            if (evt.type == EventType.KeyDown)
                action = ghostty_input_action_e.GHOSTTY_ACTION_PRESS;
            else if (evt.type == EventType.KeyUp)
                action = ghostty_input_action_e.GHOSTTY_ACTION_RELEASE;
            else
                return;

            uint scanCode = KeyCodeToScanCode(evt.keyCode);
            if (scanCode == 0 && evt.character == 0) return;

            var mods = GetModsFromEvent(evt);

            // If we have a printable character, send it as text
            IntPtr textPtr = IntPtr.Zero;
            if (action == ghostty_input_action_e.GHOSTTY_ACTION_PRESS && evt.character != 0)
            {
                var charStr = evt.character.ToString();
                textPtr = Marshal.StringToCoTaskMemUTF8(charStr);
            }

            var keyEvent = new ghostty_input_key_s
            {
                action = action,
                mods = mods,
                consumed_mods = ghostty_input_mods_e.GHOSTTY_MODS_NONE,
                keycode = scanCode,
                text = textPtr,
                unshifted_codepoint = (uint)evt.character,
                composing = 0,
            };

            GhosttyNative.ghostty_surface_key(_surface, ref keyEvent);

            if (textPtr != IntPtr.Zero)
                Marshal.FreeCoTaskMem(textPtr);
        }

        private static ghostty_input_mods_e GetModsFromEvent(Event evt)
        {
            var mods = ghostty_input_mods_e.GHOSTTY_MODS_NONE;
            if (evt.shift) mods |= ghostty_input_mods_e.GHOSTTY_MODS_SHIFT;
            if (evt.control) mods |= ghostty_input_mods_e.GHOSTTY_MODS_CTRL;
            if (evt.alt) mods |= ghostty_input_mods_e.GHOSTTY_MODS_ALT;
            if (evt.command) mods |= ghostty_input_mods_e.GHOSTTY_MODS_SUPER;
            return mods;
        }

        // ---- Mouse ----

        public void SendMousePosition(double x, double y)
        {
            var mods = GetCurrentMods();
            GhosttyNative.ghostty_surface_mouse_pos(_surface, x, y, mods);
        }

        public bool SendMouseButton(
            ghostty_input_mouse_state_e state,
            ghostty_input_mouse_button_e button)
        {
            var mods = GetCurrentMods();
            return GhosttyNative.ghostty_surface_mouse_button(_surface, state, button, mods);
        }

        public void SendMouseScroll(double x, double y)
        {
            var mods = GetCurrentMods();
            GhosttyNative.ghostty_surface_mouse_scroll(_surface, x, y, mods);
        }

        private static ghostty_input_mods_e GetCurrentMods()
        {
            var mods = ghostty_input_mods_e.GHOSTTY_MODS_NONE;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                mods |= ghostty_input_mods_e.GHOSTTY_MODS_SHIFT;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                mods |= ghostty_input_mods_e.GHOSTTY_MODS_CTRL;
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
                mods |= ghostty_input_mods_e.GHOSTTY_MODS_ALT;
            return mods;
        }

        // ---- Key mapping tables ----

#if ENABLE_INPUT_SYSTEM
        private static uint KeyToScanCode(UnityEngine.InputSystem.Key key)
        {
            return key switch
            {
                UnityEngine.InputSystem.Key.A => 0x1E,
                UnityEngine.InputSystem.Key.B => 0x30,
                UnityEngine.InputSystem.Key.C => 0x2E,
                UnityEngine.InputSystem.Key.D => 0x20,
                UnityEngine.InputSystem.Key.E => 0x12,
                UnityEngine.InputSystem.Key.F => 0x21,
                UnityEngine.InputSystem.Key.G => 0x22,
                UnityEngine.InputSystem.Key.H => 0x23,
                UnityEngine.InputSystem.Key.I => 0x17,
                UnityEngine.InputSystem.Key.J => 0x24,
                UnityEngine.InputSystem.Key.K => 0x25,
                UnityEngine.InputSystem.Key.L => 0x26,
                UnityEngine.InputSystem.Key.M => 0x32,
                UnityEngine.InputSystem.Key.N => 0x31,
                UnityEngine.InputSystem.Key.O => 0x18,
                UnityEngine.InputSystem.Key.P => 0x19,
                UnityEngine.InputSystem.Key.Q => 0x10,
                UnityEngine.InputSystem.Key.R => 0x13,
                UnityEngine.InputSystem.Key.S => 0x1F,
                UnityEngine.InputSystem.Key.T => 0x14,
                UnityEngine.InputSystem.Key.U => 0x16,
                UnityEngine.InputSystem.Key.V => 0x2F,
                UnityEngine.InputSystem.Key.W => 0x11,
                UnityEngine.InputSystem.Key.X => 0x2D,
                UnityEngine.InputSystem.Key.Y => 0x15,
                UnityEngine.InputSystem.Key.Z => 0x2C,
                UnityEngine.InputSystem.Key.Digit0 => 0x0B,
                UnityEngine.InputSystem.Key.Digit1 => 0x02,
                UnityEngine.InputSystem.Key.Digit2 => 0x03,
                UnityEngine.InputSystem.Key.Digit3 => 0x04,
                UnityEngine.InputSystem.Key.Digit4 => 0x05,
                UnityEngine.InputSystem.Key.Digit5 => 0x06,
                UnityEngine.InputSystem.Key.Digit6 => 0x07,
                UnityEngine.InputSystem.Key.Digit7 => 0x08,
                UnityEngine.InputSystem.Key.Digit8 => 0x09,
                UnityEngine.InputSystem.Key.Digit9 => 0x0A,
                UnityEngine.InputSystem.Key.Space => 0x39,
                UnityEngine.InputSystem.Key.Enter => 0x1C,
                UnityEngine.InputSystem.Key.Tab => 0x0F,
                UnityEngine.InputSystem.Key.Backspace => 0x0E,
                UnityEngine.InputSystem.Key.Escape => 0x01,
                UnityEngine.InputSystem.Key.LeftArrow => 0x4B,
                UnityEngine.InputSystem.Key.RightArrow => 0x4D,
                UnityEngine.InputSystem.Key.UpArrow => 0x48,
                UnityEngine.InputSystem.Key.DownArrow => 0x50,
                UnityEngine.InputSystem.Key.Home => 0x47,
                UnityEngine.InputSystem.Key.End => 0x4F,
                UnityEngine.InputSystem.Key.PageUp => 0x49,
                UnityEngine.InputSystem.Key.PageDown => 0x51,
                UnityEngine.InputSystem.Key.Insert => 0x52,
                UnityEngine.InputSystem.Key.Delete => 0x53,
                UnityEngine.InputSystem.Key.F1 => 0x3B,
                UnityEngine.InputSystem.Key.F2 => 0x3C,
                UnityEngine.InputSystem.Key.F3 => 0x3D,
                UnityEngine.InputSystem.Key.F4 => 0x3E,
                UnityEngine.InputSystem.Key.F5 => 0x3F,
                UnityEngine.InputSystem.Key.F6 => 0x40,
                UnityEngine.InputSystem.Key.F7 => 0x41,
                UnityEngine.InputSystem.Key.F8 => 0x42,
                UnityEngine.InputSystem.Key.F9 => 0x43,
                UnityEngine.InputSystem.Key.F10 => 0x44,
                UnityEngine.InputSystem.Key.F11 => 0x57,
                UnityEngine.InputSystem.Key.F12 => 0x58,
                UnityEngine.InputSystem.Key.Minus => 0x0C,
                UnityEngine.InputSystem.Key.Equals => 0x0D,
                UnityEngine.InputSystem.Key.LeftBracket => 0x1A,
                UnityEngine.InputSystem.Key.RightBracket => 0x1B,
                UnityEngine.InputSystem.Key.Backslash => 0x2B,
                UnityEngine.InputSystem.Key.Semicolon => 0x27,
                UnityEngine.InputSystem.Key.Quote => 0x28,
                UnityEngine.InputSystem.Key.Comma => 0x33,
                UnityEngine.InputSystem.Key.Period => 0x34,
                UnityEngine.InputSystem.Key.Slash => 0x35,
                UnityEngine.InputSystem.Key.Backquote => 0x29,
                _ => 0,
            };
        }
#endif

        private static uint KeyCodeToScanCode(KeyCode keyCode)
        {
            return keyCode switch
            {
                KeyCode.A => 0x1E,
                KeyCode.B => 0x30,
                KeyCode.C => 0x2E,
                KeyCode.D => 0x20,
                KeyCode.E => 0x12,
                KeyCode.F => 0x21,
                KeyCode.G => 0x22,
                KeyCode.H => 0x23,
                KeyCode.I => 0x17,
                KeyCode.J => 0x24,
                KeyCode.K => 0x25,
                KeyCode.L => 0x26,
                KeyCode.M => 0x32,
                KeyCode.N => 0x31,
                KeyCode.O => 0x18,
                KeyCode.P => 0x19,
                KeyCode.Q => 0x10,
                KeyCode.R => 0x13,
                KeyCode.S => 0x1F,
                KeyCode.T => 0x14,
                KeyCode.U => 0x16,
                KeyCode.V => 0x2F,
                KeyCode.W => 0x11,
                KeyCode.X => 0x2D,
                KeyCode.Y => 0x15,
                KeyCode.Z => 0x2C,
                KeyCode.Alpha0 => 0x0B,
                KeyCode.Alpha1 => 0x02,
                KeyCode.Alpha2 => 0x03,
                KeyCode.Alpha3 => 0x04,
                KeyCode.Alpha4 => 0x05,
                KeyCode.Alpha5 => 0x06,
                KeyCode.Alpha6 => 0x07,
                KeyCode.Alpha7 => 0x08,
                KeyCode.Alpha8 => 0x09,
                KeyCode.Alpha9 => 0x0A,
                KeyCode.Space => 0x39,
                KeyCode.Return => 0x1C,
                KeyCode.Tab => 0x0F,
                KeyCode.Backspace => 0x0E,
                KeyCode.Escape => 0x01,
                KeyCode.LeftArrow => 0x4B,
                KeyCode.RightArrow => 0x4D,
                KeyCode.UpArrow => 0x48,
                KeyCode.DownArrow => 0x50,
                KeyCode.Home => 0x47,
                KeyCode.End => 0x4F,
                KeyCode.PageUp => 0x49,
                KeyCode.PageDown => 0x51,
                KeyCode.Insert => 0x52,
                KeyCode.Delete => 0x53,
                KeyCode.F1 => 0x3B,
                KeyCode.F2 => 0x3C,
                KeyCode.F3 => 0x3D,
                KeyCode.F4 => 0x3E,
                KeyCode.F5 => 0x3F,
                KeyCode.F6 => 0x40,
                KeyCode.F7 => 0x41,
                KeyCode.F8 => 0x42,
                KeyCode.F9 => 0x43,
                KeyCode.F10 => 0x44,
                KeyCode.F11 => 0x57,
                KeyCode.F12 => 0x58,
                KeyCode.Minus => 0x0C,
                KeyCode.Equals => 0x0D,
                KeyCode.LeftBracket => 0x1A,
                KeyCode.RightBracket => 0x1B,
                KeyCode.Backslash => 0x2B,
                KeyCode.Semicolon => 0x27,
                KeyCode.Quote => 0x28,
                KeyCode.Comma => 0x33,
                KeyCode.Period => 0x34,
                KeyCode.Slash => 0x35,
                KeyCode.BackQuote => 0x29,
                _ => 0,
            };
        }
    }
}
