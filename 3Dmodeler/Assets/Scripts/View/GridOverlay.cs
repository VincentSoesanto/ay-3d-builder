using UnityEngine;

namespace Builder.View
{
    /// <summary>Keeps the floor grid's line spacing equal to the current grid size.</summary>
    [RequireComponent(typeof(Renderer))]
    public class GridOverlay : MonoBehaviour
    {
        static readonly int GridSizeId = Shader.PropertyToID("_GridSize");

        [SerializeField] BuilderSession session;

        Material material;
        float shownSize = -1f;

        void Start() => material = GetComponent<Renderer>().material;   // our own copy, so the asset is not changed

        void Update()
        {
            float size = session.Document.editor.gridSize;
            if (Mathf.Approximately(size, shownSize)) return;
            material.SetFloat(GridSizeId, size);
            shownSize = size;
        }
    }
}
