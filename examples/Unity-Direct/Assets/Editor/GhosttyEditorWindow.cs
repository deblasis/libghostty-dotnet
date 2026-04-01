using UnityEditor;
using UnityEngine;
using Ghostty.Unity;

namespace Ghostty.Unity.Editor
{
public class GhosttyEditorWindow : EditorWindow
{
    private GhosttyTexture _ghosttyTexture;
    private GhosttyInput _input;
    private bool _initialized;

    [MenuItem("Window/Ghostty Terminal")]
    public static void ShowDockable()
    {
        var window = GetWindow<GhosttyEditorWindow>();
        window.titleContent = new GUIContent("Ghostty");
        window.Show();
    }

    [MenuItem("Window/Ghostty Terminal (Floating)")]
    public static void ShowFloating()
    {
        var window = CreateInstance<GhosttyEditorWindow>();
        window.titleContent = new GUIContent("Ghostty");
        window.ShowUtility();
    }

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        Cleanup();
    }

    private void EnsureInitialized()
    {
        if (_initialized && _ghosttyTexture != null && _ghosttyTexture.IsValid)
            return;

        Cleanup();

        double scale = EditorGUIUtility.pixelsPerPoint;
        uint width = (uint)(position.width * scale);
        uint height = (uint)(position.height * scale);

        if (width == 0 || height == 0)
            return;

        _ghosttyTexture = new GhosttyTexture(width, height, scale);

        if (!_ghosttyTexture.IsValid)
        {
            Debug.LogError("GhosttyEditorWindow: failed to initialize");
            return;
        }

        _input = new GhosttyInput(_ghosttyTexture.Surface);
        _ghosttyTexture.SetOcclusion(true);
        _ghosttyTexture.SetFocus(true);
        _initialized = true;
    }

    private void OnEditorUpdate()
    {
        if (_ghosttyTexture != null && _ghosttyTexture.IsValid)
        {
            _ghosttyTexture.Tick();
            Repaint();
        }
    }

    private void OnGUI()
    {
        EnsureInitialized();

        if (_ghosttyTexture == null || !_ghosttyTexture.IsValid)
        {
            EditorGUILayout.HelpBox(
                "Ghostty terminal not available. Check console for errors.",
                MessageType.Warning);
            return;
        }

        // Handle resize
        double scale = EditorGUIUtility.pixelsPerPoint;
        uint newWidth = (uint)(position.width * scale);
        uint newHeight = (uint)(position.height * scale);
        _ghosttyTexture.Resize(newWidth, newHeight);

        // Draw the terminal texture
        var rect = new Rect(0, 0, position.width, position.height);
        if (_ghosttyTexture.Texture != null)
        {
            GUI.DrawTexture(rect, _ghosttyTexture.Texture, ScaleMode.StretchToFill);
        }

        // Route keyboard input
        var evt = Event.current;
        if (evt != null && (evt.type == EventType.KeyDown || evt.type == EventType.KeyUp))
        {
            _input?.ProcessEditorKeyEvent(evt);
            evt.Use();
        }

        // Route mouse input
        if (evt != null)
        {
            switch (evt.type)
            {
                case EventType.MouseDown:
                    var button = evt.button switch
                    {
                        0 => ghostty_input_mouse_button_e.GHOSTTY_MOUSE_BUTTON_LEFT,
                        1 => ghostty_input_mouse_button_e.GHOSTTY_MOUSE_BUTTON_RIGHT,
                        2 => ghostty_input_mouse_button_e.GHOSTTY_MOUSE_BUTTON_MIDDLE,
                        _ => ghostty_input_mouse_button_e.GHOSTTY_MOUSE_BUTTON_LEFT,
                    };
                    _input?.SendMouseButton(
                        ghostty_input_mouse_state_e.GHOSTTY_MOUSE_PRESS, button);
                    _input?.SendMousePosition(
                        evt.mousePosition.x * scale,
                        evt.mousePosition.y * scale);
                    evt.Use();
                    break;

                case EventType.MouseUp:
                    var upButton = evt.button switch
                    {
                        0 => ghostty_input_mouse_button_e.GHOSTTY_MOUSE_BUTTON_LEFT,
                        1 => ghostty_input_mouse_button_e.GHOSTTY_MOUSE_BUTTON_RIGHT,
                        2 => ghostty_input_mouse_button_e.GHOSTTY_MOUSE_BUTTON_MIDDLE,
                        _ => ghostty_input_mouse_button_e.GHOSTTY_MOUSE_BUTTON_LEFT,
                    };
                    _input?.SendMouseButton(
                        ghostty_input_mouse_state_e.GHOSTTY_MOUSE_RELEASE, upButton);
                    evt.Use();
                    break;

                case EventType.MouseMove:
                case EventType.MouseDrag:
                    _input?.SendMousePosition(
                        evt.mousePosition.x * scale,
                        evt.mousePosition.y * scale);
                    break;

                case EventType.ScrollWheel:
                    _input?.SendMouseScroll(0, -evt.delta.y);
                    evt.Use();
                    break;
            }
        }
    }

    private void OnFocus()
    {
        _ghosttyTexture?.SetFocus(true);
    }

    private void OnLostFocus()
    {
        _ghosttyTexture?.SetFocus(false);
    }

    private void Cleanup()
    {
        _ghosttyTexture?.Dispose();
        _ghosttyTexture = null;
        _input = null;
        _initialized = false;
    }
}
}
