using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Ghostty.Unity
{
    public class GhosttyTerminal : MonoBehaviour
    {
        [Header("Terminal Settings")]
        [SerializeField] private int textureWidth = 1280;
        [SerializeField] private int textureHeight = 720;
        [SerializeField] private string materialProperty = "_MainTex";

        private GhosttyTexture _ghosttyTexture;
        private GhosttyInput _input;
        private Renderer _renderer;
        private bool _focused;

        public Texture2D Texture => _ghosttyTexture?.Texture;
        public bool IsFocused => _focused;
        public GhosttyTexture GhosttyTextureInstance => _ghosttyTexture;

        public event Action<bool> OnFocusChanged;

        private void Start()
        {
            double scale = Screen.dpi > 0 ? Screen.dpi / 96.0 : 1.0;

            _ghosttyTexture = new GhosttyTexture(
                (uint)textureWidth,
                (uint)textureHeight,
                scale);

            if (!_ghosttyTexture.IsValid)
            {
                Debug.LogError("GhosttyTerminal: failed to initialize ghostty texture");
                return;
            }

            _input = new GhosttyInput(_ghosttyTexture.Surface);
            _renderer = GetComponent<Renderer>();

            if (_renderer != null && _ghosttyTexture.Texture != null)
            {
                _renderer.material.SetTexture(materialProperty, _ghosttyTexture.Texture);
            }

            _ghosttyTexture.SetOcclusion(true);
            _ghosttyTexture.SetFocus(false);

#if ENABLE_INPUT_SYSTEM
            _input.EnableTextInput(OnTextInput);
#endif
        }

        private void Update()
        {
            if (_ghosttyTexture == null || !_ghosttyTexture.IsValid)
                return;

            _ghosttyTexture.Tick();

#if ENABLE_INPUT_SYSTEM
            if (_focused)
            {
                _input.ProcessKeyboard();
            }
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private unsafe void OnTextInput(char c)
        {
            if (!_focused || _ghosttyTexture == null) return;

            var text = c.ToString();
            var maxBytes = Encoding.UTF8.GetMaxByteCount(text.Length);
            byte* buf = stackalloc byte[maxBytes];
            int len;
            fixed (char* chars = text)
                len = Encoding.UTF8.GetBytes(chars, text.Length, buf, maxBytes);
            GhosttyNative.ghostty_surface_text(_ghosttyTexture.Surface, (IntPtr)buf, (nuint)len);
        }
#endif

        public void SetFocus(bool focused)
        {
            if (_focused == focused) return;
            _focused = focused;
            _ghosttyTexture?.SetFocus(focused);
            OnFocusChanged?.Invoke(focused);
        }

        public void SendMousePosition(Vector2 uvPosition)
        {
            if (!_focused || _input == null) return;

            double x = uvPosition.x * textureWidth;
            double y = uvPosition.y * textureHeight;
            _input.SendMousePosition(x, y);
        }

        public void SendMouseButton(
            ghostty_input_mouse_state_e state,
            ghostty_input_mouse_button_e button)
        {
            if (_input == null) return;
            _input.SendMouseButton(state, button);
        }

        public void SendMouseScroll(float scrollDelta)
        {
            if (!_focused || _input == null) return;
            _input.SendMouseScroll(0, scrollDelta);
        }

        private void OnDestroy()
        {
#if ENABLE_INPUT_SYSTEM
            _input?.DisableTextInput(OnTextInput);
#endif
            _ghosttyTexture?.Dispose();
            _ghosttyTexture = null;
        }
    }
}
