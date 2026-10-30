using System.Collections.Generic;
using System.Linq;
using Builder.Core;
using Builder.Shapes;
using Builder.View;
using UnityEngine;

namespace Builder.Tools
{
    /// <summary>
    /// The eraser's "what does this shape catch?" part. The Eraser tool (X) is the build tool in erase mode:
    /// same ghost, rotation, scale, GRID / SNAP / FREE and mirror, but the red ghost goes INTO the aimed surface
    /// and a click deletes what it catches. This helper finds those pieces and makes them glow red.
    ///   C cycles what a click does:
    ///   - Overlap (default): deletes every piece the eraser overlaps.
    ///   - Inside: deletes only pieces completely inside it.
    ///   - Cut: cuts the eraser's shape OUT of the pieces it overlaps (pieces completely inside are deleted).
    /// How it checks: the eraser gets a hidden "convex" physics collider, and Unity's physics tells which piece
    /// colliders overlap it (ComputePenetration + corner tests) or have all their corners inside it (ClosestPoint).
    /// </summary>
    public class EraseTool : MonoBehaviour
    {
        [SerializeField] BuilderSession session;
        [SerializeField] Material ghostMaterial;

        const float Touch = 0.002f;   // overlaps smaller than 2 mm don't count (pieces just touching the eraser)

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly Color VictimColor = new Color(1f, 0.2f, 0.15f, 0.45f);

        public enum Catch { Overlap, Inside, Cut }

        /// <summary>What a click does (C cycles it).</summary>
        public Catch Mode { get; set; } = Catch.Overlap;

        /// <summary>Kept for older code/tests: true = only pieces completely inside the eraser.</summary>
        public bool InsideOnly
        {
            get => Mode == Catch.Inside;
            set => Mode = value ? Catch.Inside : Catch.Overlap;
        }

        /// <summary>The eraser shape that caught a piece (needed to cut that shape out of it).</summary>
        public class Cutter
        {
            public string shape;
            public ShapeParams p;
            public bool mirrored;
            public Vector3 position;
            public Quaternion rotation;
        }

        /// <summary>Filled by <see cref="Collect"/>: which cutter caught each piece, and which pieces are completely inside.</summary>
        public readonly Dictionary<string, Cutter> CutterOf = new Dictionary<string, Cutter>();
        public readonly HashSet<string> FullyInside = new HashSet<string>();

        /// <summary>Forget what the last checks found (call before collecting again).</summary>
        public void ClearFindings()
        {
            CutterOf.Clear();
            FullyInside.Clear();
        }

        MeshCollider probe;
        readonly List<MeshRenderer> glows = new List<MeshRenderer>();
        MaterialPropertyBlock block;
        readonly Dictionary<Mesh, Vector3[]> cornerCache = new Dictionary<Mesh, Vector3[]>();
        readonly Collider[] overlapBuffer = new Collider[512];

        void Awake()
        {
            block = new MaterialPropertyBlock();
            // Hidden physics shape of the eraser. "Ignore Raycast" layer: the crosshair ray passes through it.
            var go = new GameObject("Eraser Probe") { layer = 2 };
            go.transform.SetParent(transform, false);
            probe = go.AddComponent<MeshCollider>();
            probe.convex = true;
        }

        /// <summary>
        /// Adds the ids of the pieces an eraser of this mesh at this spot catches to <paramref name="result"/>.
        /// <paramref name="cutter"/> describes the eraser shape (remembered per piece for Cut mode).
        /// </summary>
        public void Collect(Mesh mesh, Vector3 position, Quaternion rotation, HashSet<string> result, Cutter cutter = null)
        {
            if (probe.sharedMesh != mesh) probe.sharedMesh = mesh;
            probe.transform.SetPositionAndRotation(position, rotation);
            Physics.SyncTransforms();   // make physics see pieces/eraser moved this frame

            // First a quick box check, then the exact test for each piece in the box.
            var box = new Bounds(position + rotation * mesh.bounds.center, PlacementMath.RotatedExtents(mesh.bounds.extents, rotation) * 2f);
            int count = Physics.OverlapBoxNonAlloc(box.center, box.extents, overlapBuffer);
            for (int i = 0; i < count; i++)
            {
                var other = overlapBuffer[i];
                var piece = other.GetComponent<PieceView>();
                if (piece == null || result.Contains(piece.PieceId)) continue;
                var t = other.transform;
                bool caught = Mode == Catch.Inside ? AllCornersInside(other, t) : Overlaps(other, t, position, rotation);
                if (!caught) continue;
                result.Add(piece.PieceId);
                if (cutter != null) CutterOf[piece.PieceId] = cutter;
                if (Mode == Catch.Cut && AllCornersInside(other, t)) FullyInside.Add(piece.PieceId);
            }
        }

        /// <summary>
        /// For placing: adds the pieces a ghost of this mesh at this spot goes into (always the "overlap" check,
        /// whatever the eraser's C setting is, and without touching what the eraser found).
        /// </summary>
        public void CollectOverlaps(Mesh mesh, Vector3 position, Quaternion rotation, HashSet<string> result)
        {
            var mode = Mode;
            Mode = Catch.Overlap;
            Collect(mesh, position, rotation, result);
            Mode = mode;
        }

        /// <summary>
        /// Does the eraser overlap this piece? Three checks, because the physics check alone misses a piece that is
        /// completely inside the eraser (their surfaces never cross):
        /// 1) the surfaces cross, 2) a corner of the piece is inside the eraser, 3) the eraser's middle is inside
        /// the piece's box (a small eraser inside a big piece).
        /// </summary>
        bool Overlaps(Collider other, Transform t, Vector3 position, Quaternion rotation)
        {
            if (Physics.ComputePenetration(probe, position, rotation, other, t.position, t.rotation, out _, out float depth)
                && depth > Touch)
                return true;

            // Corners are pulled 1 cm toward the piece's middle first, so a piece that only touches the eraser's
            // side (a neighbor on the grid) doesn't count.
            var mesh = other.GetComponent<MeshFilter>().sharedMesh;
            var pieceMiddle = t.position + t.rotation * mesh.bounds.center;
            foreach (var c in CornersOf(mesh))
            {
                var corner = t.position + t.rotation * c;
                var pulled = Vector3.MoveTowards(corner, pieceMiddle, 0.01f);
                if (InsideEraser(pulled)) return true;
            }

            var eraserMiddle = position + rotation * probe.sharedMesh.bounds.center;
            var local = Quaternion.Inverse(t.rotation) * (eraserMiddle - t.position);
            var box = mesh.bounds;
            box.Expand(-2f * Touch);
            return box.Contains(local);
        }

        bool AllCornersInside(Collider other, Transform t)
        {
            var mesh = other.GetComponent<MeshFilter>().sharedMesh;
            foreach (var c in CornersOf(mesh))
                if (!InsideEraser(t.position + t.rotation * c)) return false;
            return true;
        }

        /// <summary>ClosestPoint returns the point itself when it is inside the (convex) eraser.</summary>
        bool InsideEraser(Vector3 world) =>
            (Physics.ClosestPoint(world, probe, probe.transform.position, probe.transform.rotation) - world).sqrMagnitude
            < Touch * Touch;

        Vector3[] CornersOf(Mesh mesh)
        {
            if (!cornerCache.TryGetValue(mesh, out var corners))
            {
                corners = new HashSet<Vector3>(mesh.vertices).ToArray();
                if (cornerCache.Count > 500) cornerCache.Clear();   // don't keep old meshes alive
                cornerCache[mesh] = corners;
            }
            return corners;
        }

        /// <summary>Red glow over every piece that will be erased (hides the rest).</summary>
        public void ShowVictims(IEnumerable<string> ids)
        {
            int shown = 0;
            foreach (var id in ids)
            {
                var piece = session.Document.content.FindPiece(id);
                if (piece == null) continue;
                while (glows.Count <= shown) glows.Add(MakeGlow());
                var glow = glows[shown++];
                glow.GetComponent<MeshFilter>().sharedMesh = ShapeRegistry.GetPieceMesh(piece);
                glow.transform.SetPositionAndRotation(piece.position.ToUnity(), piece.rotation.ToUnity());
                glow.transform.localScale = Vector3.one * 1.01f;
                glow.gameObject.SetActive(true);
            }
            for (int i = shown; i < glows.Count; i++) glows[i].gameObject.SetActive(false);
        }

        public void HideVictims() => ShowVictims(System.Array.Empty<string>());

        MeshRenderer MakeGlow()
        {
            var go = new GameObject("Erase Glow");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = ghostMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            block.SetColor(ColorId, VictimColor);
            r.SetPropertyBlock(block);
            return r;
        }
    }
}
