using Builder.Shapes;
using Builder.Tools;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Builder.UI
{
    /// <summary>
    /// Draws the hotbar at the bottom of the screen: 9 slots with the shape pictures, slot numbers,
    /// and the selected slot lit up. The shape's name shows above it.
    /// </summary>
    public class HotbarView : MonoBehaviour
    {
        const float SlotSize = 76f;

        [SerializeField] Hotbar hotbar;
        [SerializeField] ShapeIcons icons;
        [SerializeField] UiTheme theme;
        [SerializeField] RectTransform slotRow;      // gets one child per slot
        [SerializeField] TextMeshProUGUI shapeName;

        Image[] backgrounds;
        RawImage[] pictures;
        Outline[] frames;

        void Start()
        {
            backgrounds = new Image[Hotbar.SlotCount];
            pictures = new RawImage[Hotbar.SlotCount];
            frames = new Outline[Hotbar.SlotCount];
            for (int i = 0; i < Hotbar.SlotCount; i++) MakeSlot(i);

            hotbar.Changed += Refresh;
            Refresh();
        }

        void OnDestroy()
        {
            if (hotbar != null) hotbar.Changed -= Refresh;
        }

        void MakeSlot(int i)
        {
            var bg = UiKit.Box(slotRow, "Slot " + (i + 1), theme, UiKit.Slot);
            var layout = bg.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = layout.preferredHeight = SlotSize;

            var picture = UiKit.Rect(bg.transform, "Picture").gameObject.AddComponent<RawImage>();
            UiKit.Fill(picture.rectTransform, 7f);
            picture.raycastTarget = false;

            var number = UiKit.Label(bg.transform, "Number", theme, 15f, UiKit.SubtleText,
                                     TextAlignmentOptions.TopLeft, (i + 1).ToString());
            UiKit.Fill(number.rectTransform, 5f);

            // The bright border around the selected slot.
            var frame = bg.gameObject.AddComponent<Outline>();
            frame.effectColor = UiKit.Accent;
            frame.effectDistance = new Vector2(3f, -3f);

            backgrounds[i] = bg;
            pictures[i] = picture;
            frames[i] = frame;
        }

        void Refresh()
        {
            for (int i = 0; i < Hotbar.SlotCount; i++)
            {
                bool selected = i == hotbar.Selected;
                string item = hotbar.ItemAt(i);
                pictures[i].texture = icons.Get(item);
                pictures[i].enabled = pictures[i].texture != null;
                frames[i].enabled = selected;
                backgrounds[i].color = selected ? UiKit.SlotHover : UiKit.Slot;
                backgrounds[i].transform.localScale = selected ? Vector3.one * 1.08f : Vector3.one;
            }
            shapeName.text = icons.NameOf(hotbar.ItemAt(hotbar.Selected));
        }
    }
}
