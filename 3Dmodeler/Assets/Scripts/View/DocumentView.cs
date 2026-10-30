using System.Collections.Generic;
using Builder.Core;
using Builder.Shapes;
using UnityEngine;

namespace Builder.View
{
    /// <summary>
    /// Keeps one GameObject per document piece. It never edits the document: it only listens for changes
    /// and rebuilds the GameObjects of the pieces that changed.
    /// </summary>
    public class DocumentView : MonoBehaviour
    {
        [SerializeField] BuilderSession session;
        [Tooltip("Base material for pieces; each color gets its own copy.")]
        [SerializeField] Material pieceMaterial;

        readonly Dictionary<string, PieceView> views = new Dictionary<string, PieceView>();
        readonly Dictionary<string, Material> materialsByColor = new Dictionary<string, Material>();
        Transform piecesRoot;

        void Start()
        {
            piecesRoot = new GameObject("Pieces").transform;
            piecesRoot.SetParent(transform, false);
            session.Changed += OnChanged;
            session.Replaced += RebuildAll;   // another project was opened
            RebuildAll();
        }

        void OnDestroy()
        {
            if (session == null) return;
            session.Changed -= OnChanged;
            session.Replaced -= RebuildAll;
        }

        public int PieceCount => views.Count;

        /// <summary>The GameObject showing piece <paramref name="id"/>, or null.</summary>
        public PieceView Find(string id) => id != null && views.TryGetValue(id, out var v) ? v : null;

        /// <summary>Bounds around the given pieces, or null if none of them exist.</summary>
        public Bounds? GetBounds(System.Collections.Generic.IEnumerable<string> ids)
        {
            Bounds? total = null;
            foreach (var id in ids)
            {
                var v = Find(id);
                if (v == null) continue;
                var b = v.GetComponent<Renderer>().bounds;
                if (total == null) total = b;
                else { var t = total.Value; t.Encapsulate(b); total = t; }
            }
            return total;
        }

        /// <summary>Bounds of the whole model, or null when there are no pieces.</summary>
        public Bounds? GetModelBounds()
        {
            Bounds? total = null;
            foreach (var v in views.Values)
            {
                var b = v.GetComponent<Renderer>().bounds;
                if (total == null) total = b;
                else { var t = total.Value; t.Encapsulate(b); total = t; }
            }
            return total;
        }

        /// <summary>Throws away all piece GameObjects and makes them again from the document (used after loading).</summary>
        public void RebuildAll()
        {
            foreach (var v in views.Values) Destroy(v.gameObject);
            views.Clear();
            foreach (var p in session.Document.content.pieces) CreateOrUpdate(p);
        }

        void OnChanged(ChangeSet changes)
        {
            var content = session.Document.content;
            foreach (var id in changes.pieceIds)
            {
                var piece = content.FindPiece(id);
                if (piece != null) CreateOrUpdate(piece);
                else if (views.TryGetValue(id, out var gone))
                {
                    Destroy(gone.gameObject);
                    views.Remove(id);
                }
            }
            // A group was shown/hidden (or deleted): update which pieces are visible.
            if (changes.groupIds.Count > 0)
                foreach (var p in content.pieces)
                    if (views.TryGetValue(p.id, out var v)) ApplyVisibility(v, p);
        }

        /// <summary>Pieces in a hidden group are not drawn and can't be aimed at.</summary>
        void ApplyVisibility(PieceView view, Piece p)
        {
            var group = session.Document.content.FindGroup(p.groupId);
            bool visible = group == null || group.visible;
            view.GetComponent<MeshRenderer>().enabled = visible;
            view.GetComponent<MeshCollider>().enabled = visible;
        }

        void CreateOrUpdate(Piece p)
        {
            if (!views.TryGetValue(p.id, out var view))
            {
                var go = new GameObject(p.shape);
                go.transform.SetParent(piecesRoot, false);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                go.AddComponent<MeshCollider>();   // so the crosshair raycast can hit it
                view = go.AddComponent<PieceView>();
                view.PieceId = p.id;
                views[p.id] = view;
            }

            var mesh = ShapeRegistry.GetPieceMesh(p);
            // Drawn with its spray paint (one sub-mesh per color); aimed at with the plain mesh.
            var painted = PaintedMesh.Get(p, out var colors);
            view.transform.SetLocalPositionAndRotation(p.position.ToUnity(), p.rotation.ToUnity());
            view.GetComponent<MeshFilter>().sharedMesh = painted;
            view.GetComponent<MeshCollider>().sharedMesh = mesh;
            var materials = new Material[colors.Length];
            for (int i = 0; i < colors.Length; i++) materials[i] = MaterialFor(colors[i]);
            view.GetComponent<MeshRenderer>().sharedMaterials = materials;
            ApplyVisibility(view, p);
        }

        /// <summary>One material per color + finish ("#RRGGBB", "#RRGGBB/metal", "#RRGGBB/glow"), shared by all pieces.</summary>
        Material MaterialFor(string color)
        {
            if (!materialsByColor.TryGetValue(color, out var m))
            {
                m = ColorFinish.MaterialFor(pieceMaterial, color);
                materialsByColor[color] = m;
            }
            return m;
        }
    }
}
