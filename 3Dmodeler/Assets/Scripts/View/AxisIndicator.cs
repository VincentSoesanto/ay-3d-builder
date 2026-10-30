using Builder.Core;
using Builder.Shapes;
using UnityEngine;

namespace Builder.View
{
    /// <summary>
    /// Shows the ACTIVE rotation axis on the ghost: a curved arrow around the axis (like an undo/redo icon) that
    /// points the way the piece turns on the next wheel-up, plus a faint straight line along the axis itself.
    /// Colors: X red, Y green, Z blue. The arrow is drawn on the side facing the camera so it is always visible.
    /// </summary>
    public class AxisIndicator : MonoBehaviour
    {
        // Arrow shape, for a circle of radius 1 (the whole arrow is scaled to the piece's size).
        const float ArcStart = -100f, ArcEnd = 80f;   // degrees; 0 = the side facing the camera
        const float TubeRadius = 0.022f;
        const float HeadRadius = 0.065f;
        const float HeadLength = 0.17f;
        const float LineThickness = 0.012f;
        const float ArrowAlpha = 0.5f;
        const float LineAlpha = 0.18f;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        // Same order as BuildTool's axes: 0 = Y, 1 = X, 2 = Z.
        static readonly Vector3[] Directions = { Vector3.up, Vector3.right, Vector3.forward };
        static readonly Color[] Colors = { new Color(0.48f, 0.88f, 0.48f), new Color(1f, 0.42f, 0.42f), new Color(0.42f, 0.66f, 1f) };

        static Mesh arrowMesh;

        Transform arrow, line;
        MeshRenderer arrowRenderer, lineRenderer;
        MaterialPropertyBlock block;

        /// <summary>Makes the indicator as a child of <paramref name="parent"/>, drawn with <paramref name="material"/>.</summary>
        public static AxisIndicator Create(Transform parent, Material material)
        {
            var go = new GameObject("Axis Indicator");
            go.transform.SetParent(parent, false);
            var indicator = go.AddComponent<AxisIndicator>();
            indicator.Build(material);
            go.SetActive(false);
            return indicator;
        }

        void Build(Material material)
        {
            if (arrowMesh == null) arrowMesh = GizmoMeshes.Arc(ArcStart, ArcEnd, TubeRadius, HeadRadius, HeadLength, "Turn Arrow");
            var lineMesh = ShapeRegistry.GetMesh("box", new ShapeParams { width = LineThickness, height = 1f, length = LineThickness }, false);
            arrow = Part("Turn Arrow", arrowMesh, material, out arrowRenderer);
            line = Part("Axis Line", lineMesh, material, out lineRenderer);
            block = new MaterialPropertyBlock();
        }

        Transform Part(string name, Mesh mesh, Material material, out MeshRenderer renderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        /// <summary>
        /// Shows the arrow for <paramref name="axis"/> around <paramref name="center"/>, sized to a piece of
        /// <paramref name="pieceSize"/>, turned toward <paramref name="cameraPosition"/>.
        /// </summary>
        public void Show(Vector3 center, Vector3 pieceSize, int axis, Vector3 cameraPosition)
        {
            gameObject.SetActive(true);
            transform.position = center;
            Vector3 dir = Directions[axis];

            // The arrow mesh circles the +Y axis; turn it to circle this axis (this keeps the turning direction right).
            var toAxis = Quaternion.FromToRotation(Vector3.up, dir);
            // Then spin it around the axis so its middle faces the camera.
            Vector3 toCamera = Quaternion.Inverse(toAxis) * (cameraPosition - center);
            float facing = Mathf.Atan2(toCamera.x, toCamera.z) * Mathf.Rad2Deg;
            float radius = Mathf.Max(pieceSize.x, pieceSize.y, pieceSize.z) * 0.5f + 0.25f;
            arrow.localRotation = toAxis * Quaternion.AngleAxis(facing, Vector3.up);
            arrow.localScale = Vector3.one * radius;

            float half = Mathf.Abs(Vector3.Dot(pieceSize, dir)) * 0.5f + 0.3f;
            line.localRotation = toAxis;
            line.localScale = new Vector3(1f, half * 2f, 1f);

            var color = Colors[axis];
            color.a = ArrowAlpha;
            block.SetColor(ColorId, color);
            arrowRenderer.SetPropertyBlock(block);
            color.a = LineAlpha;
            block.SetColor(ColorId, color);
            lineRenderer.SetPropertyBlock(block);
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
