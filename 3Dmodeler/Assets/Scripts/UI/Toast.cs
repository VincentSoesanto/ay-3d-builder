using TMPro;
using UnityEngine;

namespace Builder.UI
{
    /// <summary>
    /// A short message above the hotbar that fades away ("Undo: Place box", "Mirror on", ...).
    /// Any script can call <see cref="Show"/>.
    /// </summary>
    public class Toast : MonoBehaviour
    {
        const float ShowSeconds = 1.6f;
        const float FadeSeconds = 0.4f;

        [SerializeField] TextMeshProUGUI label;

        static Toast instance;
        float shownAt = -100f;

        void Awake()
        {
            instance = this;
            label.alpha = 0f;
        }

        public static void Show(string message)
        {
            if (instance == null) return;
            instance.label.text = message;
            instance.shownAt = Time.unscaledTime;
        }

        void Update()
        {
            float age = Time.unscaledTime - shownAt;
            label.alpha = age < ShowSeconds ? 1f : Mathf.Clamp01(1f - (age - ShowSeconds) / FadeSeconds);
        }
    }
}
