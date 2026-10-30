using TMPro;
using UnityEngine;

namespace Builder.UI
{
    /// <summary>
    /// Look of the app's UI (one rounded sprite + one font), set up by the scene builder.
    /// Put on the HUD canvas so scripts that create UI at runtime can use the same look.
    /// </summary>
    public class UiTheme : MonoBehaviour
    {
        public Sprite rounded;
        public TMP_FontAsset font;
    }
}
