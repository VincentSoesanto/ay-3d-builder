using System.Collections.Generic;
using System.Linq;
using Builder.Core;
using Builder.Shapes;
using Builder.View;
using UnityEngine;

namespace Builder
{
    /// <summary>
    /// Quick ways to rig a model: ready-made skeletons (templates), binding every piece to its nearest bone,
    /// bones made from the groups, moving joints, and fitting / scaling the whole skeleton.
    /// </summary>
    public static class RigTools
    {
        public static readonly string[] Templates = { "Biped", "Walker", "Quad", "Tank" };

        /// <summary>
        /// A skeleton template, 1 m tall, standing on y = 0 around x = z = 0, front = +Z. Bone names are the standard ones
        /// (Hips, Spine, UpperArm.L ...). Facing +Z (Unity's forward) the model's LEFT side is at -X, so .L bones are
        /// at -X and .R at +X (what Unity's Humanoid system expects).
        /// </summary>
        public static List<Bone> Template(string name)
        {
            var list = new List<Bone>();
            Bone B(string bone, string parent, float hx, float hy, float hz, float tx, float ty, float tz)
            {
                var b = new Bone { id = Ids.New(), name = bone, parentId = list.Find(x => x.name == parent)?.id,
                                   head = new Vec3(hx, hy, hz), tail = new Vec3(tx, ty, tz) };
                list.Add(b);
                return b;
            }
            void Sides(System.Action<string, float> make) { make(".L", -1f); make(".R", 1f); }

            switch (name)
            {
                case "Walker":   // a "chicken walker": legs bend backward, guns on the sides
                    B("Hips", null, 0f, 0.55f, 0f, 0f, 0.62f, 0f);
                    B("Cockpit", "Hips", 0f, 0.62f, 0f, 0f, 0.82f, 0.05f);
                    Sides((s, x) =>
                    {
                        B("UpperLeg" + s, "Hips", 0.14f * x, 0.55f, 0f, 0.16f * x, 0.33f, 0.14f);
                        B("LowerLeg" + s, "UpperLeg" + s, 0.16f * x, 0.33f, 0.14f, 0.16f * x, 0.08f, -0.06f);
                        B("Foot" + s, "LowerLeg" + s, 0.16f * x, 0.08f, -0.06f, 0.16f * x, 0f, 0.1f);
                        B("Gun" + s, "Cockpit", 0.2f * x, 0.72f, 0f, 0.2f * x, 0.72f, 0.25f);
                    });
                    break;
                case "Quad":   // four legs
                    B("Body", null, 0f, 0.6f, -0.35f, 0f, 0.6f, 0.35f);
                    B("Neck", "Body", 0f, 0.6f, 0.35f, 0f, 0.72f, 0.5f);
                    B("Head", "Neck", 0f, 0.72f, 0.5f, 0f, 0.78f, 0.68f);
                    Sides((s, x) =>
                    {
                        foreach (var (leg, z) in new[] { ("Front", 0.3f), ("Back", -0.3f) })
                        {
                            B(leg + "UpperLeg" + s, "Body", 0.2f * x, 0.6f, z, 0.26f * x, 0.32f, z);
                            B(leg + "LowerLeg" + s, leg + "UpperLeg" + s, 0.26f * x, 0.32f, z, 0.26f * x, 0.05f, z);
                            B(leg + "Foot" + s, leg + "LowerLeg" + s, 0.26f * x, 0.05f, z, 0.26f * x, 0f, z + 0.08f);
                        }
                    });
                    break;
                case "Tank":   // hull, turret that turns, gun that goes up / down
                    B("Hull", null, 0f, 0f, 0f, 0f, 0.35f, 0f);
                    B("Turret", "Hull", 0f, 0.35f, 0f, 0f, 0.55f, 0f);
                    B("Gun", "Turret", 0f, 0.45f, 0.2f, 0f, 0.45f, 0.75f);
                    break;
                default:   // "Biped": a human-like mech
                    B("Hips", null, 0f, 0.5f, 0f, 0f, 0.58f, 0f);
                    B("Spine", "Hips", 0f, 0.58f, 0f, 0f, 0.7f, 0f);
                    B("Chest", "Spine", 0f, 0.7f, 0f, 0f, 0.82f, 0f);
                    B("Neck", "Chest", 0f, 0.82f, 0f, 0f, 0.87f, 0f);
                    B("Head", "Neck", 0f, 0.87f, 0f, 0f, 1f, 0f);
                    Sides((s, x) =>
                    {
                        // Wide shoulders (a Gundam-like silhouette: shoulders about a third of the height apart).
                        B("Shoulder" + s, "Chest", 0.05f * x, 0.8f, 0f, 0.17f * x, 0.8f, 0f);
                        B("UpperArm" + s, "Shoulder" + s, 0.17f * x, 0.8f, 0f, 0.17f * x, 0.64f, 0f);
                        B("LowerArm" + s, "UpperArm" + s, 0.17f * x, 0.64f, 0f, 0.17f * x, 0.5f, 0.02f);
                        B("Hand" + s, "LowerArm" + s, 0.17f * x, 0.5f, 0.02f, 0.17f * x, 0.44f, 0.03f);
                        B("UpperLeg" + s, "Hips", 0.07f * x, 0.5f, 0f, 0.07f * x, 0.27f, 0.01f);
                        B("LowerLeg" + s, "UpperLeg" + s, 0.07f * x, 0.27f, 0.01f, 0.07f * x, 0.05f, 0f);
                        B("Foot" + s, "LowerLeg" + s, 0.07f * x, 0.05f, 0f, 0.07f * x, 0f, 0.1f);
                    });
                    break;
            }
            return list;
        }

        /// <summary>The template scaled and moved to stand in the box (as tall as it, on its floor, in its middle).</summary>
        public static List<Bone> Fitted(string name, Bounds box)
        {
            float height = Mathf.Max(box.size.y, 0.5f);
            var floor = new Vector3(box.center.x, box.min.y, box.center.z);
            return Template(name).ConvertAll(b =>
            {
                b.head = (floor + b.head.ToUnity() * height).ToDoc();
                b.tail = (floor + b.tail.ToUnity() * height).ToDoc();
                return b;
            });
        }

        /// <summary>The box around the model's real pieces (helpers like points and the human left out), or null.</summary>
        public static Bounds? ModelBounds(BuildContent content)
        {
            Bounds? total = null;
            foreach (var p in content.pieces.Where(p => !ShapeRegistry.IsHelper(p.shape)))
            {
                var mesh = ShapeRegistry.GetPieceMesh(p);
                var center = p.position.ToUnity() + p.rotation.ToUnity() * mesh.bounds.center;
                var b = new Bounds(center, PlacementMathExtents(mesh.bounds.extents, p.rotation.ToUnity()) * 2f);
                if (total == null) total = b;
                else { var t = total.Value; t.Encapsulate(b); total = t; }
            }
            return total;
        }

        static Vector3 PlacementMathExtents(Vector3 e, Quaternion r)
        {
            var m = Matrix4x4.Rotate(r);
            return new Vector3(
                Mathf.Abs(m.m00) * e.x + Mathf.Abs(m.m01) * e.y + Mathf.Abs(m.m02) * e.z,
                Mathf.Abs(m.m10) * e.x + Mathf.Abs(m.m11) * e.y + Mathf.Abs(m.m12) * e.z,
                Mathf.Abs(m.m20) * e.x + Mathf.Abs(m.m21) * e.y + Mathf.Abs(m.m22) * e.z);
        }

        /// <summary>Distance from a point to a bone (the line from its head to its tail).</summary>
        public static float Distance(Bone b, Vector3 p)
        {
            var a = b.head.ToUnity();
            var ab = b.tail.ToUnity() - a;
            float t = ab.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(p, a + ab * t);
        }

        /// <summary>
        /// For every piece (helpers left out): the nearest bone (by the middle of the piece). onlyUnbound = keep pieces
        /// that already move with a bone. Returns (piece id, bone id) for the ones that change.
        /// </summary>
        public static List<(string piece, string bone)> AutoBind(BuildContent content, bool onlyUnbound)
        {
            var result = new List<(string, string)>();
            if (content.bones.Count == 0) return result;
            foreach (var p in content.pieces)
            {
                if (ShapeRegistry.IsHelper(p.shape)) continue;
                if (onlyUnbound && content.BoneOf(p) != null) continue;
                var mesh = ShapeRegistry.GetPieceMesh(p);
                var middle = p.position.ToUnity() + p.rotation.ToUnity() * mesh.bounds.center;
                var nearest = content.bones.OrderBy(b => Distance(b, middle)).First();
                if (p.boneId != nearest.id) result.Add((p.id, nearest.id));
            }
            return result;
        }

        /// <summary>
        /// One bone per group: from the group's pivot toward (and past) the middle of its pieces, parent = the bone of
        /// the group's parent group. Groups get bound to their bone.
        /// </summary>
        public static List<Bone> FromGroups(BuildContent content, out List<(Group before, Group after)> groupChanges)
        {
            var bones = new List<Bone>();
            var boneOf = new Dictionary<string, Bone>();
            groupChanges = new List<(Group, Group)>();
            foreach (var g in content.groups)
            {
                var members = content.pieces.Where(p => p.groupId == g.id).ToList();
                var pivot = g.pivot.ToUnity();
                var tail = pivot + Vector3.up * 0.5f;
                if (members.Count > 0)
                {
                    var mid = Vector3.zero;
                    foreach (var p in members) mid += p.position.ToUnity();
                    mid /= members.Count;
                    if (Vector3.Distance(mid, pivot) > 0.05f) tail = pivot + (mid - pivot) * 2f;
                }
                var bone = new Bone { id = Ids.New(), name = g.name, head = pivot.ToDoc(), tail = tail.ToDoc() };
                bones.Add(bone);
                boneOf[g.id] = bone;
            }
            foreach (var g in content.groups)
            {
                if (g.parentId != null && boneOf.TryGetValue(g.parentId, out var parent) && parent != boneOf[g.id])
                    boneOf[g.id].parentId = parent.id;
                var changed = g.Clone();
                changed.boneId = boneOf[g.id].id;
                groupChanges.Add((g, changed));
            }
            return bones;
        }

        /// <summary>
        /// Moving one joint moves every joint at the same spot with it (a bone's tail and its children's heads stay joined).
        /// Returns changed copies of the bones that move.
        /// </summary>
        public static List<Bone> MoveJoint(IEnumerable<Bone> bones, Vector3 from, Vector3 to)
        {
            const float Same = 1e-3f;
            var changed = new List<Bone>();
            foreach (var b in bones)
            {
                bool head = Vector3.Distance(b.head.ToUnity(), from) < Same;
                bool tail = Vector3.Distance(b.tail.ToUnity(), from) < Same;
                if (!head && !tail) continue;
                var c = b.Clone();
                if (head) c.head = to.ToDoc();
                if (tail) c.tail = to.ToDoc();
                changed.Add(c);
            }
            return changed;
        }

        /// <summary>The whole skeleton scaled around the bottom middle of its root bones' heads.</summary>
        public static List<Bone> Scaled(List<Bone> bones, float factor)
        {
            if (bones.Count == 0) return bones;
            var all = bones.SelectMany(b => new[] { b.head.ToUnity(), b.tail.ToUnity() }).ToList();
            var min = all.Aggregate(Vector3.Min);
            var max = all.Aggregate(Vector3.Max);
            var anchor = new Vector3((min.x + max.x) / 2f, min.y, (min.z + max.z) / 2f);
            return bones.ConvertAll(b =>
            {
                var c = b.Clone();
                c.head = (anchor + (b.head.ToUnity() - anchor) * factor).ToDoc();
                c.tail = (anchor + (b.tail.ToUnity() - anchor) * factor).ToDoc();
                return c;
            });
        }
    }
}
