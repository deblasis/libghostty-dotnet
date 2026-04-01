using UnityEditor;
using UnityEngine;

namespace Ghostty.Unity.Editor
{
public class GhosttyEditorWindow : EditorWindow
{
    private GhosttyBridge _bridge;
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
        if (_initialized && _bridge != null && _bridge.IsValid)
            return;

        Cleanup();

        double scale = EditorGUIUtility.pixelsPerPoint;
        uint width = (uint)(position.width * scale);
        uint height = (uint)(position.height * scale);

        if (width == 0 || height == 0)
            return;

        _bridge = new GhosttyBridge(width, height, scale);

        if (!_bridge.IsValid)
        {
            Debug.LogError("GhosttyEditorWindow: failed to initialize");
            return;
        }

        _bridge.SetFocus(true);
        _initialized = true;
    }

    private void OnEditorUpdate()
    {
        if (_bridge != null && _bridge.IsValid)
        {
            _bridge.Tick();
            Repaint();
        }
    }

    private void OnGUI()
    {
        EnsureInitialized();

        if (_bridge == null || !_bridge.IsValid)
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
        _bridge.Resize(newWidth, newHeight);

        // Draw the terminal texture
        var rect = new Rect(0, 0, position.width, position.height);
        if (_bridge.Texture != null)
            GUI.DrawTexture(rect, _bridge.Texture, ScaleMode.StretchToFill);

        // Route keyboard input
        var evt = Event.current;
        if (evt != null && (evt.type == EventType.KeyDown || evt.type == EventType.KeyUp))
        {
            int action = evt.type == EventType.KeyDown ? 1 : 0;
            uint scanCode = GhosttyKeyMap.KeyCodeToScanCode(evt.keyCode);

            int mods = 0;
            if (evt.shift)   mods |= 1 << 0;
            if (evt.control) mods |= 1 << 1;
            if (evt.alt)     mods |= 1 << 2;
            if (evt.command) mods |= 1 << 3;

            if (scanCode != 0)
                _bridge.SendKey(action, mods, scanCode);

            // Send text for printable characters on key down
            if (action == 1 && evt.character != 0)
                _bridge.SendText(evt.character.ToString());

            evt.Use();
        }

        // Route mouse input
        if (evt != null)
        {
            switch (evt.type)
            {
                case EventType.MouseDown:
                    int button = evt.button switch { 0 => 0, 1 => 1, 2 => 2, _ => 0 };
                    _bridge.SendMouseButton(1, button); // PRESS
                    _bridge.SendMousePos(evt.mousePosition.x * scale, evt.mousePosition.y * scale);
                    evt.Use();
                    break;

                case EventType.MouseUp:
                    int upButton = evt.button switch { 0 => 0, 1 => 1, 2 => 2, _ => 0 };
                    _bridge.SendMouseButton(0, upButton); // RELEASE
                    evt.Use();
                    break;

                case EventType.MouseMove:
                case EventType.MouseDrag:
                    _bridge.SendMousePos(evt.mousePosition.x * scale, evt.mousePosition.y * scale);
                    break;

                case EventType.ScrollWheel:
                    _bridge.SendMouseScroll(0, -evt.delta.y);
                    evt.Use();
                    break;
            }
        }
    }

    private void OnFocus() => _bridge?.SetFocus(true);
    private void OnLostFocus() => _bridge?.SetFocus(false);

    private void Cleanup()
    {
        _bridge?.Dispose();
        _bridge = null;
        _initialized = false;
    }
}
}
