using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Builder.Core;
using Builder.Shapes;
using Builder.View;
using GLTFast.Export;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Builder.IO
{
    /// <summary>
    /// Exports the model as a .glb file (glTF binary, the format most 3D tools and Unity's glTFast read).
    ///   - Each group becomes one named object with its origin at the group's pivot, holding ONE mesh made of all
    ///     its pieces (one material per color, so colors survive in Unity / URP).
    ///   - Pieces without a group go into an object called "Ungrouped" (origin at 0, 0, 0).
    ///   - 1 unit = 1 meter. The mesh keeps the flat, low-poly look (hard edges).
    ///   - Bones (rigger tubes): an "Armature" object holds one object per bone (named like the bone, at its start
    ///     joint, children inside parents, not turned: X / Y / Z = the world's). A group bound to a bone goes inside
    ///     that bone; pieces bound to a bone on their own go into "&lt;bone&gt; Mesh" inside it. So turning a bone in
    ///     your game turns everything bound to it. The tubes themselves are not exported.
    /// </summary>
    public static class GlbExporter
    {
        /// <summary>Where exports go: the Exports folder next to the exe (see ProjectStore.UserFolder).</summary>
        public static string DefaultFolder
        {
            get
            {
                return Path.Combine(ProjectStore.UserFolder, "Exports");
            }
        }

        /// <summary>A file name like "Untitled_2026-10-15_14-30-05.glb".</summary>
        public static string DefaultFileName(ProjectDocument doc) =>
            SafeName(doc.name) + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".glb";

        /// <summary>Writes the model to <paramref name="path"/>. Returns true when the file was written.</summary>
        /// <param name="baseMaterial">A URP Lit material; each color gets a copy of it.</param>
        /// <param name="forceSync">Write in one go (needed in the Unity editor outside play mode, e.g. editor tests).</param>
        public static async Task<bool> ExportAsync(ProjectDocument doc, Material baseMaterial, string path, bool forceSync = false)
        {
            var made = new List<Object>();
            var root = BuildModel(doc, baseMaterial, made);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var export = new GameObjectExport(
                    new ExportSettings { Format = GltfFormat.Binary, FileConflictResolution = FileConflictResolution.Overwrite },
                    // The temporary model is kept switched off (invisible), so export switched-off objects too.
                    new GameObjectExportSettings { OnlyActiveInHierarchy = false, DisabledComponents = true });
                export.AddScene(new[] { root }, doc.name);
                return await export.SaveToFileAndDispose(path, forceSync);
            }
            finally
            {
                made.Add(root);
                foreach (var o in made) DestroyNow(o);
            }
        }

        /// <summary>
        /// Builds the (switched-off) GameObjects that get exported. Everything created is added to
        /// <paramref name="made"/> so it can be cleaned up afterwards.
        /// </summary>
        public static GameObject BuildModel(ProjectDocument doc, Material baseMaterial, List<Object> made)
        {
            var root = new GameObject(SafeName(doc.name));
            root.SetActive(false);
            // Points and the reference human are only building helpers: they are not exported.
            var content = new BuildContent
            {
                groups = doc.content.groups, bones = doc.content.bones,
                pieces = doc.content.pieces.Where(p => !ShapeRegistry.IsHelper(p.shape)).ToList(),
            };
            var usedNames = new HashSet<string>();

            // The bones first, as empty objects inside each other.
            var boneObjects = new Dictionary<string, Transform>();
            if (content.bones.Count > 0)
            {
                var armature = new GameObject(UniqueName("Armature", usedNames)).transform;
                armature.SetParent(root.transform, false);
                foreach (var b in Rig.ParentsFirst(content))
                {
                    var bone = new GameObject(UniqueName(b.name, usedNames)).transform;
                    bone.SetParent(b.parentId != null && boneObjects.TryGetValue(b.parentId, out var parent) ? parent : armature, false);
                    bone.position = b.head.ToUnity();
                    boneObjects[b.id] = bone;
                }
            }
            Transform Holder(string boneId) => boneId != null && boneObjects.TryGetValue(boneId, out var t) ? t : root.transform;

            // Each group (inside its bone, if it has one) with the pieces that move with the group.
            var placed = new HashSet<Piece>();
            var groupObjects = new Dictionary<string, Transform>();
            foreach (var group in content.groups)
            {
                string groupBone = content.FindBone(group.boneId)?.id;
                var members = content.pieces.Where(p => p.groupId == group.id && content.BoneOf(p) == groupBone).ToList();
                placed.UnionWith(members);
                groupObjects[group.id] = AddPart(Holder(groupBone), UniqueName(group.name, usedNames), group.pivot.ToUnity(), members, baseMaterial, made);
            }
            // A group that hangs on another group (and has no bone of its own) goes inside it, e.g. LeftArm inside Body.
            foreach (var group in content.groups)
                if (content.FindBone(group.boneId) == null && group.parentId != null && groupObjects.TryGetValue(group.parentId, out var parentObject)
                    && !parentObject.IsChildOf(groupObjects[group.id]))
                    groupObjects[group.id].SetParent(parentObject, worldPositionStays: true);

            // Pieces bound to a bone on their own: "<bone> Mesh" inside that bone.
            foreach (var byBone in content.pieces.Where(p => !placed.Contains(p) && content.BoneOf(p) != null).GroupBy(p => content.BoneOf(p)))
            {
                var bone = content.FindBone(byBone.Key);
                placed.UnionWith(byBone);
                AddPart(Holder(bone.id), UniqueName(bone.name + " Mesh", usedNames), bone.head.ToUnity(), byBone.ToList(), baseMaterial, made);
            }

            var ungrouped = content.pieces.Where(p => !placed.Contains(p)).ToList();
            if (ungrouped.Count > 0)
                AddPart(root.transform, UniqueName("Ungrouped", usedNames), Vector3.zero, ungrouped, baseMaterial, made);

            return root;
        }

        /// <summary>One exported object: placed at <paramref name="pivot"/>, with all the pieces merged into one mesh.</summary>
        static Transform AddPart(Transform parent, string name, Vector3 pivot, List<Piece> pieces,
                                 Material baseMaterial, List<Object> made)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pivot;   // the model's root sits at 0, 0, 0 and nothing is turned, so this is exact
            if (pieces.Count == 0) return go.transform;   // an empty group still exports as an (empty) object

            // Step 1: one mesh per color (+ finish), with every piece moved to its place (measured from the pivot).
            // A piece with spray paint has one sub-mesh per color; each goes to its own color.
            var entries = new List<(string color, CombineInstance part)>();
            foreach (var p in pieces)
            {
                var mesh = PaintedMesh.Get(p, out var pieceColors);
                var matrix = Matrix4x4.TRS(p.position.ToUnity() - pivot, p.rotation.ToUnity(), Vector3.one);
                for (int i = 0; i < pieceColors.Length && i < mesh.subMeshCount; i++)
                    if (mesh.GetIndexCount(i) > 0)
                        entries.Add((pieceColors[i].ToUpperInvariant(), new CombineInstance { mesh = mesh, subMeshIndex = i, transform = matrix }));
            }
            var colors = new List<string>();
            var perColor = new List<Mesh>();
            foreach (var byColor in entries.GroupBy(e => e.color))
            {
                var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(byColor.Select(e => e.part).ToArray(), mergeSubMeshes: true, useMatrices: true);
                made.Add(mesh);
                perColor.Add(mesh);
                colors.Add(byColor.Key);
            }

            // Step 2: put the color meshes together as parts ("sub-meshes") of one mesh, one material each.
            var combined = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            combined.CombineMeshes(perColor.Select(m => new CombineInstance { mesh = m, transform = Matrix4x4.identity }).ToArray(),
                                   mergeSubMeshes: false, useMatrices: false);
            combined.RecalculateBounds();
            made.Add(combined);

            var materials = colors.Select(color =>
            {
                var m = ColorFinish.MaterialFor(baseMaterial, color);   // metal / glow go into the file too
                made.Add(m);
                return m;
            }).ToArray();

            go.AddComponent<MeshFilter>().sharedMesh = combined;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            return go.transform;
        }

        static string UniqueName(string name, HashSet<string> used)
        {
            string result = string.IsNullOrWhiteSpace(name) ? "Group" : name.Trim();
            for (int n = 2; !used.Add(result); n++) result = name + " " + n;
            return result;
        }

        /// <summary>A name that is safe for files and object names (no \ / : * ? " &lt; &gt; |).</summary>
        public static string SafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Model";
            var bad = Path.GetInvalidFileNameChars();
            return new string(name.Trim().Select(c => bad.Contains(c) ? '_' : c).ToArray());
        }

        static void DestroyNow(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}
