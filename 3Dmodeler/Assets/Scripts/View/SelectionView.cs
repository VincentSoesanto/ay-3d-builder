using System.Collections.Generic;
using Builder.Tools;
using UnityEngine;

namespace Builder.View
{
    /// <summary>
    /// Draws a soft orange glow over every selected piece (a slightly bigger see-through copy of it).
    /// Only shown while the Select tool is active.
    /// </summary>
    public class SelectionView : MonoBehaviour
    {
        [SerializeField] Selection selection;
        [SerializeField] DocumentView documentView;
        [SerializeField] ToolManager tools;
        [SerializeField] Material selectedMaterial;

        readonly List<MeshFilter> glows = new List<MeshFilter>();

        void LateUpdate()
        {
            int shown = 0;
            if (tools.Current == ToolKind.Select)
            {
                foreach (var id in selection.Ids)
                {
                    var view = documentView.Find(id);
                    if (view == null) continue;
                    var glow = GetGlow(shown++);
                    var t = view.transform;
                    glow.sharedMesh = view.GetComponent<MeshFilter>().sharedMesh;
                    glow.transform.SetPositionAndRotation(t.position, t.rotation);
                    glow.transform.localScale = Vector3.one * 1.015f;
                    glow.gameObject.SetActive(true);
                }
            }
            for (int i = shown; i < glows.Count; i++) glows[i].gameObject.SetActive(false);
        }

        MeshFilter GetGlow(int index)
        {
            while (glows.Count <= index)
            {
                var go = new GameObject("Selected Glow");
                go.transform.SetParent(transform, false);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = selectedMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                glows.Add(go.AddComponent<MeshFilter>());
            }
            return glows[index];
        }
    }
}
