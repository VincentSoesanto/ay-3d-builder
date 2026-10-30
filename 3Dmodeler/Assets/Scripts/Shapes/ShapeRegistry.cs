using System.Collections.Generic;
using System.Globalization;
using Builder.Core;
using UnityEngine;

namespace Builder.Shapes
{
    /// <summary>
    /// All shape types by id, plus a mesh cache: pieces with exactly the same shape and parameters share one mesh.
    /// </summary>
    public static class ShapeRegistry
    {
        static readonly List<IShape> all = new List<IShape>();
        static readonly List<IShape> points = new List<IShape>();
        static readonly List<IShape> reference = new List<IShape>();
        static readonly List<IShape> models = new List<IShape>();
        static readonly List<IShape> bones = new List<IShape>();
        static readonly Dictionary<string, IShape> byId = new Dictionary<string, IShape>();
        static readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();

        /// <summary>
        /// How many meshes the cache keeps. Every new size / cut makes a new mesh (turning the wheel to resize makes
        /// many), so when there are more, the cache forgets them all and Unity frees the ones no piece shows any more.
        /// </summary>
        public static int MaxCachedMeshes { get; set; } = 3000;

        public static int CachedMeshCount => meshCache.Count;

        static float lastFree = -100f;

        /// <summary>
        /// Lets Unity free meshes nothing uses any more (pieces, ghosts and other objects keep theirs). Never destroys a
        /// mesh that is still shown. At most every 10 seconds (Unity checks every object, which takes a moment).
        /// </summary>
        public static void FreeUnusedMeshes()
        {
            if (!Application.isPlaying || Time.realtimeSinceStartup - lastFree < 10f) return;
            lastFree = Time.realtimeSinceStartup;
            Resources.UnloadUnusedAssets();
        }

        static void Remember(string key, Mesh mesh)
        {
            if (meshCache.Count >= MaxCachedMeshes)
            {
                meshCache.Clear();   // forgotten, not destroyed: pieces still showing them keep them
                FreeUnusedMeshes();
            }
            meshCache[key] = mesh;
        }

        static ShapeRegistry()
        {
            // This order is the order in the inventory and the default hotbar (exactly 9 shapes = 9 slots).
            Register(new BoxShape());
            Register(new WedgeShape());
            Register(new CornerWedgeShape());
            Register(new CylinderShape());
            Register(new HalfCylinderShape());
            Register(new ConeShape());
            Register(new SphereShape());
            Register(new BeamShape());
            Register(new PlateShape());

            // Not in the shape list: the Point has its own inventory category, the Solid is made by B from points.
            var point = new PointShape();
            byId[point.Id] = point;
            points.Add(point);
            var bone = new BoneJointShape();
            byId[bone.Id] = bone;
            bones.Add(bone);
            var hull = new HullShape();
            byId[hull.Id] = hull;
            var human = new HumanShape();
            byId[human.Id] = human;
            reference.Add(human);
        }

        static void Register(IShape shape)
        {
            all.Add(shape);
            byId[shape.Id] = shape;
        }

        /// <summary>The normal shapes (inventory "Shapes" and the default hotbar).</summary>
        public static IReadOnlyList<IShape> All => all;

        /// <summary>The inventory's "Bones" category (the Rigger tube).</summary>
        public static IReadOnlyList<IShape> Bones => bones;

        /// <summary>The inventory's "Points" category.</summary>
        public static IReadOnlyList<IShape> Points => points;

        /// <summary>The inventory's "Reference" category (size guides that are not exported).</summary>
        public static IReadOnlyList<IShape> Reference => reference;

        /// <summary>Imported 3D models (inventory "Models"), added by the model library.</summary>
        public static IReadOnlyList<IShape> Models => models;

        /// <summary>
        /// Adds (or replaces) an imported model's shape. <paramref name="listed"/> = false: a deleted model, not in the
        /// inventory any more, but pieces already made from it still work.
        /// </summary>
        public static void RegisterModel(IShape shape, bool listed = true)
        {
            models.RemoveAll(m => m.Id == shape.Id);
            if (listed) models.Add(shape);
            byId[shape.Id] = shape;
        }

        /// <summary>Takes a model out of the inventory (pieces already made from it still work).</summary>
        public static void UnlistModel(string shapeId) => models.RemoveAll(m => m.Id == shapeId);

        /// <summary>Helpers that are only for building and never go into the exported .glb.</summary>
        public static bool IsHelper(string shapeId) =>
            shapeId == PointShape.ShapeId || shapeId == HumanShape.ShapeId || shapeId == BoneJointShape.ShapeId;

        /// <summary>The color a new piece of this shape gets (helpers have their own bright colors).</summary>
        public static string StartColor(string shapeId, string normal) =>
            shapeId == PointShape.ShapeId ? PointShape.Color : shapeId == HumanShape.ShapeId ? HumanShape.Color
            : shapeId == BoneJointShape.ShapeId ? BoneJointShape.Color : normal;

        public static IShape Get(string id) => id != null && byId.TryGetValue(id, out var s) ? s : null;

        /// <summary>The mesh for this shape + parameters (built once, then reused). Falls back to a box for unknown shapes.</summary>
        public static Mesh GetMesh(string shapeId, ShapeParams p, bool mirrored)
        {
            var shape = Get(shapeId) ?? byId["box"];
            string key = MakeKey(shape.Id, p, mirrored);
            if (meshCache.TryGetValue(key, out var cached) && cached != null) return cached;

            var mb = new MeshBuilder();
            shape.Build(p, mb);
            var mesh = mb.ToMesh(key, mirrored);
            Remember(key, mesh);
            return mesh;
        }

        /// <summary>The mesh of a placed piece: its shape, minus its cuts (if any), mirrored if needed.</summary>
        public static Mesh GetPieceMesh(Piece piece) => GetPieceMesh(piece, piece.mirrored);

        /// <summary>Same, but with the mirroring chosen by the caller (e.g. the mirror ghost uses the opposite).</summary>
        public static Mesh GetPieceMesh(Piece piece, bool mirrored)
        {
            if (!piece.HasCuts()) return GetMesh(piece.shape, piece.p, mirrored);

            var shape = Get(piece.shape) ?? byId["box"];
            var keyBuilder = new System.Text.StringBuilder(MakeKey(shape.Id, piece.p, mirrored));
            foreach (var c in piece.cuts)
                keyBuilder.Append("|cut:").Append(MakeKey(c.shape, c.p, c.mirrored))
                          .Append(F(c.position.x)).Append(',').Append(F(c.position.y)).Append(',').Append(F(c.position.z)).Append(',')
                          .Append(F(c.rotation.x)).Append(',').Append(F(c.rotation.y)).Append(',').Append(F(c.rotation.z)).Append(',').Append(F(c.rotation.w));
            string key = keyBuilder.ToString();
            if (meshCache.TryGetValue(key, out var cached) && cached != null) return cached;

            // Cut in the piece's own (not mirrored) space, then mirror the result if needed.
            var polygons = Csg.FromMesh(GetMesh(shape.Id, piece.p, false), Matrix4x4.identity);
            foreach (var c in piece.cuts)
            {
                var cutter = Csg.FromMesh(GetMesh(c.shape, c.p, c.mirrored),
                    Matrix4x4.TRS(new Vector3(c.position.x, c.position.y, c.position.z),
                                  new Quaternion(c.rotation.x, c.rotation.y, c.rotation.z, c.rotation.w), Vector3.one));
                polygons = Csg.Subtract(polygons, cutter);
            }
            var mb = new MeshBuilder();
            Csg.AddTo(mb, polygons);
            var mesh = mb.ToMesh(key, mirrored);
            Remember(key, mesh);
            return mesh;
        }

        static string MakeKey(string id, ShapeParams p, bool mirrored)
        {
            string key = string.Join("|",
                id, F(p.width), F(p.height), F(p.length), F(p.bevel), F(p.taper),
                p.hollow ? "h" : "s", F(p.wall), p.segments.ToString(CultureInfo.InvariantCulture), mirrored ? "m" : "n");
            if (p.points != null)
                foreach (var v in p.points) key += "|" + F(v.x) + "," + F(v.y) + "," + F(v.z);
            return key;
        }

        static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    }
}
