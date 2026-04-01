using UnityEngine;

namespace Ghostty.Unity.NostromoConsole
{
    public class DecorativeScreenPulse : MonoBehaviour
    {
        [SerializeField] private Color baseColor = new Color(0f, 0.6f, 0.5f, 1f);
        [SerializeField] private float pulseSpeed = 0.8f;
        [SerializeField] private float minIntensity = 0.3f;
        [SerializeField] private float maxIntensity = 1.0f;

        private Material _material;
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private void Start()
        {
            var r = GetComponent<Renderer>();
            if (r != null)
                _material = r.material;
        }

        private void Update()
        {
            if (_material == null) return;

            float t = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;
            float intensity = Mathf.Lerp(minIntensity, maxIntensity, t);
            Color c = baseColor * intensity;

            _material.SetColor(BaseColorId, c);
            _material.SetColor(EmissionColor, c);
        }

        private void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
        }
    }
}
