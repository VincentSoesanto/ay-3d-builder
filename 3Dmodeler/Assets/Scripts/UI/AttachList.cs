using System.Text;
using Builder.Tools;
using TMPro;
using UnityEngine;

namespace Builder.UI
{
    /// <summary>
    /// The attach point list next to the crosshair, shown while Q is held in SNAP mode.
    /// Shows a window of entries around the current one (the full list is long); the current one is orange.
    /// </summary>
    public class AttachList : MonoBehaviour
    {
        const int Visible = 9;   // entries shown at once (odd, so the current one can sit in the middle)

        [SerializeField] BuildTool buildTool;
        [SerializeField] GameObject panel;
        [SerializeField] TextMeshProUGUI text;

        int shownIndex = int.MinValue;
        readonly StringBuilder sb = new StringBuilder();

        void Update()
        {
            bool show = buildTool.AttachListVisible;
            if (panel.activeSelf != show) panel.SetActive(show);
            if (!show || buildTool.AttachIndex == shownIndex) return;

            shownIndex = buildTool.AttachIndex;
            int count = PlacementMath.AttachPoints.Length + 1;   // Auto + 26 points
            int current = shownIndex + 1;                        // entry number, 0 = Auto
            int first = Mathf.Clamp(current - Visible / 2, 0, count - Visible);

            sb.Clear();
            sb.Append($"<color={UiKit.SubtleHex}><size=85%>Attach point   ·   wheel to choose   ·   {current + 1}/{count}</size></color>\n");
            for (int entry = first; entry < first + Visible; entry++)
            {
                string name = BuildTool.AttachNameOf(entry - 1);
                if (entry == current) sb.Append($"<color=#FF9A26><b>>  {name}</b></color>");
                else sb.Append($"<color={UiKit.SubtleHex}>    {name}</color>");
                if (entry < first + Visible - 1) sb.Append('\n');
            }
            text.text = sb.ToString();
        }
    }
}
