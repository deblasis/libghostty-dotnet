using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Ghostty.Unity.NostromoConsole
{
    public class PhosphorToggle : MonoBehaviour
    {
        [SerializeField] private Renderer terminalRenderer;
        [SerializeField] private Color greenPhosphor = new Color(0.0f, 1.0f, 0.3f, 1.0f);
        [SerializeField] private Color amberPhosphor = new Color(1.0f, 0.7f, 0.0f, 1.0f);
        [SerializeField] private float transitionSpeed = 3.0f;

        private bool _isAmber;
        private Color _currentColor;
        private Color _targetColor;
        private Material _material;

        private static readonly int PhosphorColorId = Shader.PropertyToID("_PhosphorColor");

        private void Start()
        {
            if (terminalRenderer != null)
                _material = terminalRenderer.material;

            _currentColor = greenPhosphor;
            _targetColor = greenPhosphor;
        }

        private void Update()
        {
            bool togglePressed = false;

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
                togglePressed = Keyboard.current.f1Key.wasPressedThisFrame;
#else
            togglePressed = Input.GetKeyDown(KeyCode.F1);
#endif

            if (togglePressed)
            {
                _isAmber = !_isAmber;
                _targetColor = _isAmber ? amberPhosphor : greenPhosphor;
            }

            // Smooth color transition
            _currentColor = Color.Lerp(_currentColor, _targetColor, Time.deltaTime * transitionSpeed);

            if (_material != null)
                _material.SetColor(PhosphorColorId, _currentColor);
        }

        private void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
        }
    }
}
