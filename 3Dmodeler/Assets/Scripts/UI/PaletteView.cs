using System.Collections.Generic;
using Builder.Tools;
using Builder.View;
using UnityEngine;
using UnityEngine.UI;

namespace Builder.UI
{
    /// <summary>
    /// The color palette row above the hotbar, shown while the Paint tool is on. The chosen color has a frame;
    /// M / G in the corner = metal / glow finish.
    /// </summary>
    public class PaletteView : MonoBehaviour
    {
        const float SwatchSize = 40f;

        [SerializeField] PaintTool paintTool;
        [SerializeField] ToolManager tools;
        [SerializeField] UiTheme theme;
        [SerializeField] RectTransform row;   // the panel the swatches go into

        readonly List<(Image swatch, Outline frame)> swatches = new List<(Image, Outline)>();
        string shownPalette;
        int shownIndex = -1;

        void Update()
        {
            bool show = tools.Current == ToolKind.Paint;
            if (row.gameObject.activeSelf != show) row.gameObject.SetActive(show);
            if (!show) return;

            var palette = paintTool.Palette;
            string signature = string.Join("|", palette);
            if (signature != shownPalette) Rebuild(palette, signature);
            if (paintTool.ColorIndex != shownIndex)
            {
                shownIndex = paintTool.ColorIndex;
                for (int i = 0; i < swatches.Count; i++)
                {
                    swatches[i].frame.enabled = i == shownIndex;
                    swatches[i].swatch.transform.localScale = i == shownIndex ? Vector3.one * 1.15f : Vector3.one;
                }
            }
        }

        void Rebuild(List<string> palette, string signature)
        {
            foreach (Transform child in row) Destroy(child.gameObject);
            swatches.Clear();
            for (int i = 0; i < palette.Count; i++)
            {
                var swatch = UiKit.Box(row, "Color " + (i + 1), theme, Conversions.HexToColor(palette[i]));
                var layout = swatch.gameObject.AddComponent<LayoutElement>();
                layout.preferredWidth = layout.preferredHeight = SwatchSize;
                if (i < 9)
                {
                    var number = UiKit.Label(swatch.transform, "Number", theme, 13f, Color.white,
                                             TMPro.TextAlignmentOptions.TopLeft, (i + 1).ToString());
                    UiKit.Fill(number.rectTransform, 3f);
                    number.outlineWidth = 0.25f;   // readable on light colors too
                }
                var finish = ColorFinish.Of(palette[i]);
                if (finish != ColorFinish.Finish.Matte)
                {
                    // M = metal, G = glow (bottom right corner)
                    var mark = UiKit.Label(swatch.transform, "Finish", theme, 12f, Color.white,
                                           TMPro.TextAlignmentOptions.BottomRight, finish == ColorFinish.Finish.Metal ? "M" : "G");
                    UiKit.Fill(mark.rectTransform, 3f);
                    mark.outlineWidth = 0.25f;
                }
                var frame = swatch.gameObject.AddComponent<Outline>();
                frame.effectColor = Color.white;
                frame.effectDistance = new Vector2(3f, -3f);
                frame.enabled = false;
                swatches.Add((swatch, frame));
            }
            shownPalette = signature;
            shownIndex = -1;
        }
    }
}
