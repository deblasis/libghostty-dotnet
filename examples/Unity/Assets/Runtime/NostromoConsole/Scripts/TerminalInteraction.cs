using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Ghostty.Unity;

namespace Ghostty.Unity.NostromoConsole
{
    [RequireComponent(typeof(GhosttyTerminal))]
    public class TerminalInteraction : MonoBehaviour
    {
        [SerializeField] private Camera mainCamera;
        private GhosttyTerminal _terminal;
        private Collider _collider;

        private void Start()
        {
            _terminal = GetComponent<GhosttyTerminal>();
            _collider = GetComponent<Collider>();

            if (mainCamera == null)
                mainCamera = Camera.main;
        }

        private void Update()
        {
            bool clickedThisFrame = false;
            Vector2 mousePos = Vector2.zero;
            float scrollDelta = 0f;

#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
            {
                clickedThisFrame = mouse.leftButton.wasPressedThisFrame;
                mousePos = mouse.position.ReadValue();
                scrollDelta = mouse.scroll.ReadValue().y / 120f;
            }
#else
            clickedThisFrame = Input.GetMouseButtonDown(0);
            mousePos = Input.mousePosition;
            scrollDelta = Input.mouseScrollDelta.y;
#endif

            if (clickedThisFrame)
            {
                HandleClick(mousePos);
            }

            // Send mouse release when button is released
            bool releasedThisFrame = false;
#if ENABLE_INPUT_SYSTEM
            if (mouse != null)
                releasedThisFrame = mouse.leftButton.wasReleasedThisFrame;
#else
            releasedThisFrame = Input.GetMouseButtonUp(0);
#endif
            if (releasedThisFrame && _terminal.IsFocused)
            {
                _terminal.SendMouseButton(
                    ghostty_input_mouse_state_e.GHOSTTY_MOUSE_RELEASE,
                    ghostty_input_mouse_button_e.GHOSTTY_MOUSE_BUTTON_LEFT);
            }

#if ENABLE_INPUT_SYSTEM
            // Escape to unfocus
            if (_terminal.IsFocused && Keyboard.current != null
                && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                _terminal.SetFocus(false);
            }
#else
            if (_terminal.IsFocused && Input.GetKeyDown(KeyCode.Escape))
            {
                _terminal.SetFocus(false);
            }
#endif

            // Forward scroll when focused
            if (_terminal.IsFocused && scrollDelta != 0f)
            {
                _terminal.SendMouseScroll(scrollDelta);
            }

            // Forward mouse position when focused and hovering
            if (_terminal.IsFocused && _collider != null)
            {
                Ray ray = mainCamera.ScreenPointToRay(mousePos);
                if (_collider.Raycast(ray, out RaycastHit hit, 100f))
                {
                    _terminal.SendMousePosition(hit.textureCoord);
                }
            }
        }

        private void HandleClick(Vector2 screenPos)
        {
            if (mainCamera == null) return;

            Ray ray = mainCamera.ScreenPointToRay(screenPos);
            if (_collider != null && _collider.Raycast(ray, out RaycastHit hit, 100f))
            {
                // Clicked on the terminal screen
                _terminal.SetFocus(true);
                _terminal.SendMouseButton(
                    ghostty_input_mouse_state_e.GHOSTTY_MOUSE_PRESS,
                    ghostty_input_mouse_button_e.GHOSTTY_MOUSE_BUTTON_LEFT);
                _terminal.SendMousePosition(hit.textureCoord);
            }
            else
            {
                // Clicked elsewhere
                _terminal.SetFocus(false);
            }
        }
    }
}
