using UnityEngine;

namespace Builder.View
{
    /// <summary>
    /// While mirror building is on, shows the mirror line: a faint red wall standing at X = 0
    /// (everything on one side gets copied to the other side).
    /// </summary>
    public class MirrorPlaneView : MonoBehaviour
    {
        [SerializeField] BuilderSession session;
        [SerializeField] Material material;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        GameObject plane;

        void Start()
        {
            plane = new GameObject("Mirror Plane");
            plane.transform.SetParent(transform, false);
            // A quad only shows its front, so use two facing opposite ways (seen from both sides).
            AddSide(90f);
            AddSide(-90f);
        }

        void AddSide(float turn)
        {
            var side = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(side.GetComponent<Collider>());   // the crosshair ray must go through it
            side.transform.SetParent(plane.transform, false);
            // Standing along the Z axis at X = 0: 60 m long, 30 m high.
            side.transform.SetPositionAndRotation(new Vector3(0f, 15f, 0f), Quaternion.Euler(0f, turn, 0f));
            side.transform.localScale = new Vector3(60f, 30f, 1f);

            var r = side.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var block = new MaterialPropertyBlock();
            block.SetColor(ColorId, new Color(1f, 0.3f, 0.25f, 0.05f));
            r.SetPropertyBlock(block);
        }

        void Update() => plane.SetActive(session.Document.editor.mirrorX);
    }
}
