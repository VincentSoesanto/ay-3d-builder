using System.Collections.Generic;
using System.Linq;
using Builder.Core;
using Builder.IO;
using Builder.View;
using UnityEngine;

namespace Builder
{
    /// <summary>
    /// "Frame parts" (bones panel): puts an inner frame on every bone of the skeleton. Presets: the Gunpla-style
    /// frames of <see cref="InnerFrame"/> (Slim / Detailed / Heavy / Mannequin) and "Gears &amp; pistons", made from the
    /// built-in library parts - ball joints at shoulders / hips / wrists / ankles, gears at elbows and knees,
    /// hydraulics and pistons along the arms and legs, a spring spine, a gear core with a vent in the chest, an axle
    /// through the hips, a sensor eye on the head, foot plates. Each piece is sized to its bone and bound to it, so it
    /// moves in the Pose test and in the game. You only add the armor and extra parts.
    /// Works for every skeleton (the parts are picked by the bone names, unknown bones get a piston + joint).
    /// The pieces are marked <see cref="Piece.frame"/>: pressing the button again fits them to the bones again.
    /// </summary>
    public static class MechFrame
    {
        /// <summary>The preset made of library parts (gears, pistons, springs ...).</summary>
        public const string Gears = "Gears & pistons";

        /// <summary>All frame presets, in the order of the bones panel's Frame list.</summary>
        public static IEnumerable<string> Presets => InnerFrame.Styles.Select(s => s.name).Append(Gears);

        public static string Default => InnerFrame.Styles[1].name;

        /// <summary>A short line about a preset (for the Frame list).</summary>
        public static string About(string preset) =>
            preset == Gears ? "library parts: gears, ball joints, springs, pistons" : InnerFrame.Find(preset)?.about ?? "";

        /// <summary>The frame pieces of a preset for the skeleton in <paramref name="content"/> (new ids, bound to their bones).</summary>
        public static List<Piece> Build(BuildContent content, string preset = null)
        {
            preset ??= Default;
            if (preset == Gears) return BuildGears(content);
            return InnerFrame.Build(content, InnerFrame.Find(preset) ?? InnerFrame.Styles[1]);
        }

        /// <summary>The "Gears &amp; pistons" preset: built-in library parts on every bone.</summary>
        public static List<Piece> BuildGears(BuildContent content)
        {
            var result = new List<Piece>();
            var bones = content.bones;
            if (bones.Count == 0) return result;
            float height = Mathf.Max(0.2f, bones.Max(b => Mathf.Max(b.head.y, b.tail.y)) - bones.Min(b => Mathf.Min(b.head.y, b.tail.y)));

            foreach (var bone in bones)
            {
                Vector3 head = bone.head.ToUnity(), tail = bone.tail.ToUnity();
                Vector3 along = tail - head;
                float length = along.magnitude;
                Vector3 dir = length > 1e-4f ? along / length : Vector3.up;
                Vector3 middle = (head + tail) * 0.5f;
                string role = Role(bone.name);
                bool lower = role == "LowerArm" || role == "LowerLeg";

                void Add(string item, Vector3 at, Quaternion rotation, float scale)
                {
                    foreach (var p in Place(item, at, rotation, scale))
                    {
                        p.boneId = bone.id;
                        result.Add(p);
                    }
                }
                // Along the bone (the item's long axis on the bone), from a bit after the head to a bit before the tail.
                void Along(string item, float part, Vector3 itemAxis)
                {
                    var size = Size(item);
                    float itemLength = Mathf.Abs(Vector3.Dot(size, itemAxis));
                    if (itemLength < 1e-4f || length < 1e-3f) return;
                    Add(item, middle, Turn(itemAxis, dir, Front(itemAxis), Perpendicular(Vector3.forward, dir)), length * part / itemLength);
                }
                void Joint(string item, Vector3 at, float diameter, Vector3 itemAxis, Vector3 worldAxis, Vector3 worldFront)
                {
                    var size = Size(item);
                    float biggest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                    if (biggest < 1e-4f) return;
                    Add(item, at, Turn(itemAxis, worldAxis, Front(itemAxis), Perpendicular(worldFront, worldAxis)), diameter / biggest);
                }
                Quaternion Upright(Vector3 front) => Turn(Vector3.up, Vector3.up, Vector3.forward, Perpendicular(front, Vector3.up));

                switch (role)
                {
                    case "UpperArm":
                    case "UpperLeg":
                        Joint("ay.ball-joint", head, height * (role == "UpperLeg" ? 0.06f : 0.052f), Vector3.up, dir, Vector3.forward);
                        Along("ay.hydraulic", 0.8f, Vector3.up);
                        break;
                    case "LowerArm":
                    case "LowerLeg":
                        // The knee / elbow gear stands like a wheel: it turns around the sideways (X) axis.
                        Joint("ay.gear", head, height * 0.075f, Vector3.forward, Vector3.right, dir);
                        Along("ay.piston", 0.75f, Vector3.up);
                        break;
                    case "Shoulder":
                        Along("ay.pipe", 0.9f, Vector3.right);
                        break;
                    case "Hand":
                        Joint("ay.ball-joint", head, height * 0.04f, Vector3.up, dir, Vector3.forward);
                        Joint("ay.hinge", middle, Mathf.Max(length, height * 0.05f), Vector3.up, Vector3.right, dir);
                        break;
                    case "Foot":
                    {
                        Joint("ay.ball-joint", head, height * 0.045f, Vector3.up, Vector3.up, Vector3.forward);
                        // A flat sole on the floor under the foot, as long as the foot.
                        var flat = Vector3.ProjectOnPlane(along, Vector3.up);
                        var front = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
                        var size = Size("ay.hinge");
                        float scale = Mathf.Max(flat.magnitude * 1.4f, height * 0.08f) / Mathf.Max(size.z, 1e-4f);
                        var sole = new Vector3(middle.x, size.y * scale * 0.5f, middle.z);
                        Add("ay.hinge", sole, Upright(front), scale);
                        break;
                    }
                    case "Hips":
                    {
                        // An axle through the hips, as wide as the legs are apart (or a part of the height).
                        var legs = bones.Where(b => b.parentId == bone.id && Role(b.name) == "UpperLeg").ToList();
                        float width = legs.Count >= 2 ? legs.Max(b => b.head.x) - legs.Min(b => b.head.x) : 0f;
                        width = Mathf.Max(width * 1.5f, height * 0.12f);
                        Joint("ay.axle", head, width, Vector3.right, Vector3.right, Vector3.forward);
                        break;
                    }
                    case "Spine":
                        Along("ay.spring", 0.9f, Vector3.up);
                        break;
                    case "Chest":
                    case "Body":
                    case "Cockpit":
                    case "Hull":
                    {
                        // A gear core in the middle, a vent on the back.
                        float core = Mathf.Max(length * 0.8f, height * 0.09f);
                        Joint("ay.gear-stack", middle, core, Vector3.forward, Vector3.forward, Vector3.up);
                        var vent = Size("ay.vent");
                        float ventScale = core * 0.8f / Mathf.Max(vent.x, 1e-4f);
                        var back = middle + Vector3.back * (core * 0.25f + vent.z * ventScale);
                        Add("ay.vent", back, Turn(Vector3.forward, Vector3.back, Vector3.up, Vector3.up), ventScale);
                        break;
                    }
                    case "Neck":
                        Joint("ay.ball-joint", head, height * 0.045f, Vector3.up, dir, Vector3.forward);
                        Along("ay.piston", 0.7f, Vector3.up);
                        break;
                    case "Head":
                    {
                        // A power cell as the head's core, the sensor eye on its front.
                        Along("ay.battery", 0.8f, Vector3.up);
                        var cell = Size("ay.battery");
                        float radius = cell.x * 0.5f * (length * 0.8f / Mathf.Max(cell.y, 1e-4f));
                        float eye = Mathf.Max(length * 0.4f, height * 0.035f);
                        var size = Size("ay.sensor-eye");
                        float eyeScale = eye / Mathf.Max(size.y, 1e-4f);
                        Add("ay.sensor-eye", middle + Vector3.forward * (radius + size.z * eyeScale * 0.5f), Upright(Vector3.forward), eyeScale);
                        break;
                    }
                    case "Turret":
                        // A ring the turret turns on: a big gear lying flat.
                        Joint("ay.gear-large", head, Mathf.Max(height * 0.35f, length), Vector3.forward, Vector3.up, Vector3.forward);
                        break;
                    default:   // Gun, Tail, Bone 3 ...: a joint and a piston
                        Joint("ay.ball-joint", head, height * 0.045f, Vector3.up, dir, Vector3.forward);
                        if (length > height * 0.06f) Along("ay.piston", 0.7f, Vector3.up);
                        break;
                }
            }
            foreach (var p in result) p.frame = true;
            return result;
        }

        /// <summary>"FrontUpperLeg.L" -> "UpperLeg", "Gun.R" -> "Gun".</summary>
        public static string Role(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string n = name.EndsWith(".L") || name.EndsWith(".R") ? name.Substring(0, name.Length - 2) : name;
            foreach (var prefix in new[] { "Front", "Back", "Rear" })
                if (n.StartsWith(prefix) && n.Length > prefix.Length && char.IsUpper(n[prefix.Length])) n = n.Substring(prefix.Length);
            return n;
        }

        /// <summary>The library part's pieces, turned, scaled and moved so the middle of the part is at <paramref name="at"/>.</summary>
        static IEnumerable<Piece> Place(string item, Vector3 at, Quaternion rotation, float scale)
        {
            var bp = PartLibrary.Get(item)?.blueprint;
            if (bp == null || scale <= 0f) yield break;
            var middle = Bounds(bp).center;
            var link = new PieceLink { position = (at - rotation * (middle * scale)).ToDoc(), rotation = rotation.ToDoc(), scale = scale };
            for (int i = 0; i < bp.content.pieces.Count; i++)
            {
                var piece = LinkedCopies.MakePart(bp.content.pieces[i], link, i);
                piece.id = Ids.New();
                piece.link = null;   // plain pieces, like other library parts
                yield return piece;
            }
        }

        static Vector3 Size(string item)
        {
            var bp = PartLibrary.Get(item)?.blueprint;
            return bp == null ? Vector3.zero : Bounds(bp).size;
        }

        /// <summary>The part's box (library pieces are not turned, so position +- half the size is exact).</summary>
        static Bounds Bounds(Blueprint bp)
        {
            var first = bp.content.pieces[0];
            var box = new Bounds(first.position.ToUnity(), Vector3.zero);
            foreach (var p in bp.content.pieces)
            {
                var half = new Vector3(p.p.width, p.p.height, p.p.length) * 0.5f;
                box.Encapsulate(p.position.ToUnity() - half);
                box.Encapsulate(p.position.ToUnity() + half);
            }
            return box;
        }

        /// <summary>The turn that puts the item's axis on <paramref name="worldAxis"/> and its front toward <paramref name="worldFront"/>.</summary>
        static Quaternion Turn(Vector3 itemAxis, Vector3 worldAxis, Vector3 itemFront, Vector3 worldFront) =>
            Quaternion.LookRotation(worldFront, worldAxis) * Quaternion.Inverse(Quaternion.LookRotation(itemFront, itemAxis));

        /// <summary>A front direction for an item axis (any direction at a right angle to it).</summary>
        static Vector3 Front(Vector3 itemAxis) => Mathf.Abs(itemAxis.z) > 0.9f ? Vector3.up : Vector3.forward;

        /// <summary><paramref name="want"/> made at a right angle to <paramref name="axis"/> (another direction if they are the same).</summary>
        static Vector3 Perpendicular(Vector3 want, Vector3 axis)
        {
            var p = Vector3.ProjectOnPlane(want, axis);
            if (p.sqrMagnitude < 1e-4f) p = Vector3.ProjectOnPlane(Vector3.up, axis);
            if (p.sqrMagnitude < 1e-4f) p = Vector3.ProjectOnPlane(Vector3.right, axis);
            return p.normalized;
        }
    }
}
