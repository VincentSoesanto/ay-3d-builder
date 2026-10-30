using System;
using System.Collections.Generic;
using Builder.Core;
using Builder.View;
using UnityEngine;

namespace Builder
{
    /// <summary>
    /// Bone (rigger tube) math, shared by the bone placer, the pose test, blueprints and the .glb export.
    ///   - A bone turns around its head (start joint). Its children and its bound pieces turn with it.
    ///   - Bones are not turned in their rest pose: their axes are the world X / Y / Z (also in the export),
    ///     so the pose test and rotation limits use plain X / Y / Z angles.
    /// </summary>
    public static class Rig
    {
        const float Tolerance = 1e-3f;

        static bool OnPlane(Bone b) => Mathf.Abs(b.head.x) < Tolerance && Mathf.Abs(b.tail.x) < Tolerance;

        static bool Near(Vec3 a, Vec3 b) => Vector3.Distance(a.ToUnity(), b.ToUnity()) < Tolerance;

        /// <summary>The bone that is the mirror image of <paramref name="bone"/> (itself if it lies on the mirror line).</summary>
        public static Bone MirrorPartner(Bone bone, BuildContent content)
        {
            if (bone == null) return null;
            if (OnPlane(bone)) return bone;
            var head = MirrorEdits.Position(bone.head);
            var tail = MirrorEdits.Position(bone.tail);
            return content.bones.Find(b => b.id != bone.id && Near(b.head, head) && Near(b.tail, tail));
        }

        /// <summary>
        /// For placing with mirror on: the bones + a mirror bone for each one that is off the mirror line
        /// (mirrored joints, .L/.R swapped, parent = the mirror of the parent).
        /// </summary>
        public static List<Bone> WithMirrorBones(List<Bone> bones, BuildContent content, Dictionary<string, string> mirrorIds = null)
        {
            var all = new List<Bone>(bones);
            mirrorIds ??= new Dictionary<string, string>();
            foreach (var b in bones)
                if (!OnPlane(b)) mirrorIds[b.id] = Ids.New();
            foreach (var b in bones)
            {
                if (!mirrorIds.TryGetValue(b.id, out var id)) continue;
                var m = b.Clone();
                m.id = id;
                m.name = BoneNames.Mirror(b.name);
                if (m.name == b.name) m.name = b.name + " (mirror)";
                m.head = MirrorEdits.Position(b.head);
                m.tail = MirrorEdits.Position(b.tail);
                m.roll = -b.roll;
                if (b.limits != null)   // turning around Y or Z goes the other way on the mirror side
                    m.limits = new AxisLimits
                    {
                        min = new Vec3(b.limits.min.x, -b.limits.max.y, -b.limits.max.z),
                        max = new Vec3(b.limits.max.x, -b.limits.min.y, -b.limits.min.z),
                    };
                if (b.parentId != null && mirrorIds.TryGetValue(b.parentId, out var parentMirror)) m.parentId = parentMirror;
                else m.parentId = MirrorPartner(content.FindBone(b.parentId), content)?.id ?? b.parentId;
                all.Add(m);
            }
            return all;
        }

        /// <summary>Moves a bone the same way the build tool moves a stamp: optional flip across X, scale, turn, move.</summary>
        public static Bone Placed(Bone b, bool flipped, float scale, Quaternion rotation, Vector3 position)
        {
            var c = b.Clone();
            Vec3 Move(Vec3 v)
            {
                var p = v.ToUnity();
                if (flipped) p.x = -p.x;
                return (position + rotation * (p * scale)).ToDoc();
            }
            c.head = Move(b.head);
            c.tail = Move(b.tail);
            if (flipped) c.name = BoneNames.Mirror(b.name);
            return c;
        }

        /// <summary>Order with parents before children (bones whose parent is missing count as roots).</summary>
        public static List<Bone> ParentsFirst(BuildContent content)
        {
            var result = new List<Bone>();
            var done = new HashSet<string>();
            void Add(Bone b, int depth)
            {
                if (b == null || done.Contains(b.id) || depth > 64) return;
                Add(content.FindBone(b.parentId), depth + 1);
                if (done.Add(b.id)) result.Add(b);
            }
            foreach (var b in content.bones) Add(b, 0);
            return result;
        }

        /// <summary>How deep a bone is in the tree (root = 0).</summary>
        public static int Depth(Bone b, BuildContent content)
        {
            int depth = 0;
            for (var p = content.FindBone(b.parentId); p != null && depth < 64; p = content.FindBone(p.parentId)) depth++;
            return depth;
        }

        /// <summary>True if <paramref name="boneId"/> is <paramref name="ancestorId"/> or below it.</summary>
        public static bool IsInside(string boneId, string ancestorId, BuildContent content)
        {
            for (int i = 0; boneId != null && i < 64; i++, boneId = content.FindBone(boneId)?.parentId)
                if (boneId == ancestorId) return true;
            return false;
        }

        /// <summary>
        /// Pose test: the matrix that moves things bound to each bone from the rest pose to the posed spot.
        /// <paramref name="angles"/> = each bone's turn (X, Y, Z degrees), turned around its head, inside its parent's pose.
        /// </summary>
        public static Dictionary<string, Matrix4x4> PoseMatrices(BuildContent content, Func<string, Vector3> angles)
        {
            var result = new Dictionary<string, Matrix4x4>();
            foreach (var b in ParentsFirst(content))
            {
                var parent = b.parentId != null && result.TryGetValue(b.parentId, out var m) ? m : Matrix4x4.identity;
                var head = b.head.ToUnity();
                result[b.id] = parent * Matrix4x4.Translate(head) * Matrix4x4.Rotate(Quaternion.Euler(angles(b.id))) * Matrix4x4.Translate(-head);
            }
            return result;
        }

        /// <summary>An angle kept inside the bone's limits for that axis (no limits = -180 .. 180).</summary>
        public static float Clamp(Bone b, int axis, float degrees)
        {
            var (min, max) = Range(b, axis);
            return Mathf.Clamp(degrees, min, max);
        }

        public static (float min, float max) Range(Bone b, int axis)
        {
            if (b.limits == null) return (-180f, 180f);
            float min = axis == 0 ? b.limits.min.x : axis == 1 ? b.limits.min.y : b.limits.min.z;
            float max = axis == 0 ? b.limits.max.x : axis == 1 ? b.limits.max.y : b.limits.max.z;
            return (Mathf.Min(min, max), Mathf.Max(min, max));
        }
    }
}
