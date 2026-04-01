using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

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
            bool releasedThisFrame = false;
            Vector2 mousePos = Vector2.zero;
            float scrollDelta = 0f;

#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
            {
                clickedThisFrame = mouse.leftButton.wasPressedThisFrame;
                releasedThisFrame = mouse.leftButton.wasReleasedThisFrame;
                mousePos = mouse.position.ReadValue();
                scrollDelta = mouse.scroll.ReadValue().y / 120f;
            }
#else
            clickedThisFrame = Input.GetMouseButtonDown(0);
            releasedThisFrame = Input.GetMouseButtonUp(0);
            mousePos = Input.mousePosition;
            scrollDelta = Input.mouseScrollDelta.y;
#endif

            if (clickedThisFrame)
                HandleClick(mousePos);

            if (releasedThisFrame && _terminal.IsFocused)
                _terminal.SendMouseButton(0, 0); // RELEASE, LEFT

#if ENABLE_INPUT_SYSTEM
            if (_terminal.IsFocused && Keyboard.current != null
                && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                _terminal.SetFocus(false);
            }
#else
            if (_terminal.IsFocused && Input.GetKeyDown(KeyCode.Escape))
                _terminal.SetFocus(false);
#endif

            if (_terminal.IsFocused && scrollDelta != 0f)
                _terminal.SendMouseScroll(scrollDelta);

            if (_terminal.IsFocused && _collider != null)
            {
                Ray ray = mainCamera.ScreenPointToRay(mousePos);
                if (_collider.Raycast(ray, out RaycastHit hit, 100f))
                    _terminal.SendMousePosition(hit.textureCoord);
            }
        }

        private void HandleClick(Vector2 screenPos)
        {
            if (mainCamera == null) return;

            Ray ray = mainCamera.ScreenPointToRay(screenPos);
            if (_collider != null && _collider.Raycast(ray, out RaycastHit hit, 100f))
            {
                _terminal.SetFocus(true);
                _terminal.SendMouseButton(1, 0); // PRESS, LEFT
                _terminal.SendMousePosition(hit.textureCoord);
            }
            else
            {
                _terminal.SetFocus(false);
            }
        }
    }
}
