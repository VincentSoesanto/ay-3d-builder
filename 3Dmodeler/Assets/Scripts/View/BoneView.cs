using System.Collections.Generic;
using System.Linq;
using Builder.Core;
using Builder.Tools;
using UnityEngine;

namespace Builder.View
{
    /// <summary>
    /// Put on a bone's joint balls and tube, so the build tool can aim at it: snap a new bone to a joint,
    /// or right-click to delete the bone.
    /// </summary>
    public class BoneJointView : MonoBehaviour
    {
        public string BoneId;
        public bool IsTail;
        public bool IsTube;   // the tube between the joints (aimable for deleting, not a joint to snap to)
    }

    /// <summary>
    /// Draws the bones (rigger tubes): a glowing tube from the head (start joint) to the tail (end joint), with a
    /// ball on each joint, drawn on top of the model so they show through it. Never edits the document.
    ///   - The selected bone (bones panel) is orange.
    ///   - In the pose test the tubes follow the pose.
    ///   - While placing bones the joint balls can be aimed at (a new bone snaps to them).
    /// </summary>
    public class BoneView : MonoBehaviour
    {
        const float TubeRadius = 0.018f;
        const float JointSize = 0.07f;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly Color Normal = new Color(0.25f, 0.88f, 1f, 0.85f);
        static readonly Color Selected = new Color(1f, 0.62f, 0.15f, 0.95f);
        static readonly Color Preview = new Color(0.25f, 0.88f, 1f, 0.45f);
        static readonly Color TouchedColor = new Color(0.3f, 1f, 0.35f, 1f);

        [SerializeField] BuilderSession session;
        [SerializeField] PoseTest pose;
        [SerializeField] Material overlayMaterial;

        class Drawn
        {
            public Transform tube, head, tail;
            public Renderer[] renderers;
            public Collider[] colliders;
        }

        readonly Dictionary<string, Drawn> drawn = new Dictionary<string, Drawn>();
        MaterialPropertyBlock block;   // made in Start (Unity does not allow it in a field initializer)
        Mesh cylinder, sphere;
        Drawn preview;
        Transform root;

        /// <summary>Show the bones at all (bones panel button).</summary>
        public bool Visible { get; set; } = true;

        /// <summary>True while placing bones: the joint balls can be aimed at.</summary>
        public bool JointsAimable { get; set; }

        /// <summary>The bone drawn orange (the one chosen in the bones panel), or null.</summary>
        public string Highlighted
        {
            get => highlighted.FirstOrDefault();
            set { highlighted.Clear(); if (value != null) highlighted.Add(value); }
        }

        readonly HashSet<string> highlighted = new HashSet<string>();

        /// <summary>The bones drawn orange (the chosen ones in the bones panel).</summary>
        public void SetHighlighted(IEnumerable<string> ids)
        {
            highlighted.Clear();
            if (ids != null) highlighted.UnionWith(ids);
        }

        /// <summary>The bone the piece being placed touches (drawn green, thicker), or null.</summary>
        public string Touched { get; set; }

        public int Count => drawn.Count;

        void Start()
        {
            block = new MaterialPropertyBlock();
            root = new GameObject("Bones").transform;
            root.SetParent(transform, false);
            cylinder = BuiltinMesh(PrimitiveType.Cylinder);
            sphere = BuiltinMesh(PrimitiveType.Sphere);
            preview = Make("Bone Preview", null);
            preview.tube.parent.gameObject.SetActive(false);
            session.Changed += OnChanged;
            session.Replaced += RebuildAll;
            RebuildAll();
        }

        void OnDestroy()
        {
            if (session == null) return;
            session.Changed -= OnChanged;
            session.Replaced -= RebuildAll;
        }

        static Mesh BuiltinMesh(PrimitiveType type)
        {
            var temp = GameObject.CreatePrimitive(type);
            var mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(temp);   // right away, so its collider is never aimed at
            return mesh;
        }

        void OnChanged(ChangeSet changes)
        {
            if (changes.boneIds.Count > 0) RebuildAll();
        }

        void RebuildAll()
        {
            foreach (var d in drawn.Values) Destroy(d.tube.parent.gameObject);
            drawn.Clear();
            foreach (var b in session.Document.content.bones) drawn[b.id] = Make(b.name, b.id);
        }

        Drawn Make(string name, string boneId)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var d = new Drawn
            {
                tube = Part(go.transform, "Tube", cylinder, boneId, false, tube: true),
                head = Part(go.transform, "Head", sphere, boneId, false),
                tail = Part(go.transform, "Tail", sphere, boneId, true),
            };
            d.renderers = go.GetComponentsInChildren<Renderer>();
            d.colliders = go.GetComponentsInChildren<Collider>();
            return d;
        }

        Transform Part(Transform parent, string name, Mesh mesh, string boneId, bool isTail, bool tube = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = overlayMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (boneId != null)
            {
                if (tube)
                {
                    var capsule = go.AddComponent<CapsuleCollider>();   // fatter than the thin tube: easy to aim at
                    capsule.radius = 1.5f;
                    capsule.height = 2f;
                }
                else go.AddComponent<SphereCollider>().radius = 0.8f;   // a bit bigger than the ball
                var joint = go.AddComponent<BoneJointView>();
                joint.BoneId = boneId;
                joint.IsTail = isTail;
                joint.IsTube = tube;
            }
            return go.transform;
        }

        void LateUpdate()
        {
            var content = session.Document.content;
            var matrices = pose != null && pose.Active ? pose.Matrices() : null;
            foreach (var b in content.bones)
            {
                if (!drawn.TryGetValue(b.id, out var d)) continue;
                d.tube.parent.gameObject.SetActive(Visible);
                if (!Visible) continue;
                var head = b.head.ToUnity();
                var tail = b.tail.ToUnity();
                if (matrices != null && matrices.TryGetValue(b.id, out var m))
                {
                    head = m.MultiplyPoint3x4(head);
                    tail = m.MultiplyPoint3x4(tail);
                }
                bool touched = b.id == Touched;
                Place(d, head, tail, touched ? TouchedColor : highlighted.Contains(b.id) ? Selected : Normal, touched ? 2.2f : 1f);
                foreach (var c in d.colliders) c.enabled = JointsAimable;
            }
        }

        void Place(Drawn d, Vector3 head, Vector3 tail, Color color, float thick = 1f)
        {
            var dir = tail - head;
            float length = Mathf.Max(dir.magnitude, 0.001f);
            d.tube.SetPositionAndRotation((head + tail) * 0.5f, Quaternion.FromToRotation(Vector3.up, dir / length));
            d.tube.localScale = new Vector3(TubeRadius * 2f * thick, length * 0.5f, TubeRadius * 2f * thick);   // the cylinder is 2 high
            d.head.position = head;
            d.tail.position = tail;
            d.head.localScale = d.tail.localScale = Vector3.one * JointSize * (thick > 1f ? 1.5f : 1f);
            block.SetColor(ColorId, color);
            foreach (var r in d.renderers) r.SetPropertyBlock(block);
        }

        /// <summary>The bone being placed: from its start joint to where you aim now.</summary>
        public void ShowPreview(Vector3 head, Vector3 tail)
        {
            if (preview == null) return;
            preview.tube.parent.gameObject.SetActive(true);
            Place(preview, head, tail, Preview);
        }

        public void HidePreview()
        {
            if (preview != null) preview.tube.parent.gameObject.SetActive(false);
        }
    }
}
