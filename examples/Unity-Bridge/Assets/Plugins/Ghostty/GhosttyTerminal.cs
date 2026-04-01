using System;
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

        private GhosttyBridge _bridge;
        private Renderer _renderer;
        private bool _focused;

        public Texture2D Texture => _bridge?.Texture;
        public bool IsFocused => _focused;
        public GhosttyBridge Bridge => _bridge;

        public event Action<bool> OnFocusChanged;

        private void Start()
        {
            double scale = Screen.dpi > 0 ? Screen.dpi / 96.0 : 1.0;

            _bridge = new GhosttyBridge(
                (uint)textureWidth,
                (uint)textureHeight,
                scale);

            if (!_bridge.IsValid)
            {
                Debug.LogError("GhosttyTerminal: failed to initialize");
                return;
            }

            _renderer = GetComponent<Renderer>();
            if (_renderer != null && _bridge.Texture != null)
                _renderer.material.SetTexture(materialProperty, _bridge.Texture);

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
                Keyboard.current.onTextInput += OnTextInput;
#endif
        }

        private void Update()
        {
            if (_bridge == null || !_bridge.IsValid) return;

            _bridge.Tick();

#if ENABLE_INPUT_SYSTEM
            if (_focused && Keyboard.current != null)
            {
                foreach (var key in Keyboard.current.allKeys)
                {
                    if (key.wasPressedThisFrame)
                        SendKey(key.keyCode, 1); // PRESS
                    else if (key.wasReleasedThisFrame)
                        SendKey(key.keyCode, 0); // RELEASE
                }
            }
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private void SendKey(Key key, int action)
        {
            uint scanCode = GhosttyKeyMap.KeyToScanCode(key);
            if (scanCode == 0) return;
            int mods = GhosttyKeyMap.GetModsInputSystem();
            _bridge.SendKey(action, mods, scanCode);
        }

        private unsafe void OnTextInput(char c)
        {
            if (!_focused || _bridge == null) return;
            // Filter control characters -- they are handled as key events
            if (c < 0x20 || c == 0x7F) return;
            _bridge.SendText(c.ToString());
        }
#endif

        public void SetFocus(bool focused)
        {
            if (_focused == focused) return;
            _focused = focused;
            _bridge?.SetFocus(focused);
            OnFocusChanged?.Invoke(focused);
        }

        public void SendMousePosition(Vector2 uvPosition)
        {
            if (!_focused || _bridge == null) return;
            _bridge.SendMousePos(
                uvPosition.x * textureWidth,
                uvPosition.y * textureHeight);
        }

        public void SendMouseButton(int state, int button)
        {
            _bridge?.SendMouseButton(state, button);
        }

        public void SendMouseScroll(float scrollDelta)
        {
            if (!_focused || _bridge == null) return;
            _bridge.SendMouseScroll(0, scrollDelta);
        }

        private void OnDestroy()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
                Keyboard.current.onTextInput -= OnTextInput;
#endif
            _bridge?.Dispose();
            _bridge = null;
        }
    }
}
