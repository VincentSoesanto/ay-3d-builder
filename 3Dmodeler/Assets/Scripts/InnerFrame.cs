using System.Collections.Generic;
using System.Linq;
using Builder.Core;
using Builder.View;
using UnityEngine;

namespace Builder
{
    /// <summary>
    /// Gunpla-style inner frames (the metal "skeleton" under the armor of PG / RG kits), built from the app's own
    /// shapes on every bone. Each style is one preset in the bones panel's Frame list:
    ///   - Slim (RG style): one beveled strut per bone, joint pins, double knees, calf pistons, fingers.
    ///   - Detailed (PG style): truss frames (two rails + cross braces + a core), gold joints, double knees with
    ///     heat-sink fins, hydraulic cylinders on legs and arms, a "semi-monocoque" chest core with frame rings,
    ///     cockpit hatch, shoulder rail, back thruster mounts and vents, hip block with a slide rail, jointed fingers.
    ///   - Heavy (Sazabi style): the detailed frame but thicker and darker, with red accents and a glowing core.
    ///   - Military (AC style): a Gundam-like shape with a gritty, military look (like Armored Core 6 ACs): the
    ///     detailed frame plus flat angled armor plates (shoulder blocks, forearms, shin guards, chest plates, skirt),
    ///     cable bundles, bolts, hazard stripes, a wide sensor-cluster head with antennas, boosters on the back,
    ///     shoulder weapon mounts. Olive / gunmetal / sand colors.
    ///   - Mannequin: one plain block per bone, for blocking out a shape quickly.
    /// The detailed styles also get cable bundles, bolts and extra actuators (details).
    /// Parts are picked by bone name (MechFrame.Role), sized to the bone and the skeleton's height, and bound to the bone.
    /// </summary>
    public static class InnerFrame
    {
        public class Style
        {
            public string name, about;
            public float bulk = 1f;            // thickness
            public bool truss, doubleKnee, fingers, fins, coreGlow, blocks;
            public bool details;               // cable bundles, bolts, extra actuators
            public bool armor;                 // flat armor plates (Military)
            public bool military;              // sensor-cluster head, boosters, weapon mounts, hazard stripes
            public int hydraulics;             // 0 none, 1 legs, 2 legs + arms
            public string frame, joint, dark, rod, accent, glow;
            public string cable = "#1C1D1F", brass = "#8A6E2F", plate = "#5B6247", hazard = "#D6A41C";
        }

        public static readonly Style[] Styles =
        {
            new Style
            {
                name = "Slim (RG style)", about = "compact struts, joint pins, double knees, calf pistons",
                bulk = 0.85f, doubleKnee = true, fingers = true, hydraulics = 1,
                frame = "#8C929A", joint = "#C8CDD3", dark = "#4A4F57", rod = "#D8DCE0/metal", accent = "#3B6FD0", glow = "#4FE3FF/glow",
            },
            new Style
            {
                name = "Detailed (PG style)", about = "truss frame, gold joints, hydraulics, chest core, fingers",
                bulk = 1f, truss = true, doubleKnee = true, fingers = true, fins = true, hydraulics = 2, details = true,
                frame = "#A8ADB4/metal", joint = "#C9A23A/metal", dark = "#3E434A", rod = "#E0E3E6/metal", accent = "#2E64C8", glow = "#4FE3FF/glow",
            },
            new Style
            {
                name = "Heavy (Sazabi style)", about = "thick dark truss frame, big pistons, red glowing core",
                bulk = 1.45f, truss = true, doubleKnee = true, fingers = true, fins = true, coreGlow = true, hydraulics = 2, details = true,
                frame = "#5C6168/metal", joint = "#8A8F96/metal", dark = "#2B2E33", rod = "#C9CED6/metal", accent = "#A8202A", glow = "#FF3A2E/glow",
            },
            new Style
            {
                name = "Military (AC style)", about = "Gundam shape, gritty military: armor plates, cables, boosters",
                bulk = 1.15f, truss = true, doubleKnee = true, fingers = true, fins = true, hydraulics = 2,
                details = true, armor = true, military = true,
                frame = "#45484C/metal", joint = "#6B6F73/metal", dark = "#26282B", rod = "#B9BDC1/metal", accent = "#8C8466",
                glow = "#FF7A1A/glow", plate = "#5B6247",
            },
            new Style
            {
                name = "Mannequin (blocks)", about = "one plain block per bone, to block out the shape",
                blocks = true, frame = "#B8BEC6", joint = "#8A9099", dark = "#6A7078", rod = "#8A9099", accent = "#8A9099", glow = "#8A9099",
            },
        };

        public static Style Find(string name) => Styles.FirstOrDefault(s => s.name == name);

        /// <summary>Everything one bone needs: where it is, its turn (local Y = along the bone, Z = front), its size.</summary>
        class Ctx
        {
            public Bone bone;
            public Vector3 head, tail, dir;
            public Quaternion rot;
            public float L, u, H;
            public Style st;
            public List<Piece> list;
            public BuildContent content;
        }

        static readonly Quaternion AxisX = Quaternion.Euler(0f, 0f, 90f);   // a cylinder's axis (Y) turned to X
        static readonly Quaternion AxisZ = Quaternion.Euler(90f, 0f, 0f);   // a cylinder's axis (Y) turned to Z

        public static List<Piece> Build(BuildContent content, Style st)
        {
            var list = new List<Piece>();
            var bones = content.bones;
            if (bones.Count == 0) return list;
            float H = Mathf.Max(0.2f, bones.Max(b => Mathf.Max(b.head.y, b.tail.y)) - bones.Min(b => Mathf.Min(b.head.y, b.tail.y)));
            foreach (var bone in bones)
            {
                var c = new Ctx { bone = bone, head = bone.head.ToUnity(), tail = bone.tail.ToUnity(), st = st, list = list, content = content, H = H };
                var along = c.tail - c.head;
                c.L = along.magnitude;
                c.dir = c.L > 1e-4f ? along / c.L : Vector3.up;
                c.rot = Quaternion.LookRotation(Perpendicular(Vector3.forward, c.dir), c.dir);
                c.u = H * 0.01f * st.bulk;
                string role = MechFrame.Role(bone.name);
                if (st.blocks) Blocks(c, role);
                else Frame(c, role);
            }
            foreach (var p in list) p.frame = true;
            return list;
        }

        // ---------- the styles ----------

        static void Frame(Ctx c, string role)
        {
            switch (role)
            {
                case "UpperArm": Limb(c, 1f, ball: true); break;
                case "LowerArm": Limb(c, 0.95f, ball: false); break;
                case "UpperLeg": Limb(c, 1.25f, ball: true); break;
                case "LowerLeg": Limb(c, 1.2f, ball: false); break;
                case "Neck": Limb(c, 0.6f, ball: true); break;
                case "Shoulder": Shoulder(c); break;
                case "Hips": Hips(c); break;
                case "Spine": Spine(c); break;
                case "Chest": case "Body": case "Cockpit": case "Hull": Core(c); break;
                case "Head": Head(c); break;
                case "Hand": Hand(c); break;
                case "Foot": Foot(c); break;
                case "Turret": Turret(c); break;
                default: Limb(c, 0.8f, ball: true); break;
            }
        }

        /// <summary>An arm / leg segment: a strut (Slim) or a truss (two rails, braces, core), its joint, pistons.</summary>
        static void Limb(Ctx c, float t, bool ball)
        {
            var st = c.st;
            float u = c.u * t, L = c.L;
            if (L < 1e-3f) return;
            string role = MechFrame.Role(c.bone.name);
            bool lower = role == "LowerArm" || role == "LowerLeg";
            bool leg = role.Contains("Leg");
            float gap = 1.7f * u, railW = 1.0f * u, depth = 2.6f * u;

            // The joint at the head: a ball (shoulder, hip, neck) or a pin; lower arm / leg: a double joint.
            if (ball) Part(c, "sphere", Vector3.one * 4f * u, Vector3.zero, Quaternion.identity, st.joint);
            float start = 0.06f * L;
            if (!ball)
            {
                float pinWidth = 2f * (gap + railW) + 0.6f * u;
                Part(c, "cylinder", new Vector3(3.2f * u, pinWidth, 3.2f * u), Vector3.zero, AxisX, st.joint);
                if (st.doubleKnee)
                {
                    float link = Mathf.Min(0.14f * L, 5f * u);
                    Part(c, "cylinder", new Vector3(2.8f * u, pinWidth, 2.8f * u), new Vector3(0f, link, 0f), AxisX, st.joint);
                    foreach (float side in new[] { -1f, 1f })   // the link plates between the two pins
                        Part(c, "box", new Vector3(0.6f * u, link + 2.6f * u, 2.6f * u), new Vector3(side * (gap + railW + 0.3f * u), link * 0.5f, 0f),
                             Quaternion.identity, st.frame, 0.15f * u);
                    start = link + 1.6f * u;
                }
                if (st.fins && role == "LowerLeg")   // heat sink on the knee
                    for (int i = 0; i < 3; i++)
                        Part(c, "box", new Vector3(2f * gap, 0.35f * u, 1.6f * u),
                             new Vector3(0f, 0.6f * u + i * 1.1f * u, depth * 0.5f + 1.4f * u), Quaternion.identity, st.dark);
            }
            float end = 0.94f * L;
            float len = Mathf.Max(end - start, 0.2f * L), mid = start + len * 0.5f;

            if (!st.truss)
            {
                Part(c, "box", new Vector3(2.6f * u, len, 2.4f * u), new Vector3(0f, mid, 0f), Quaternion.identity, st.frame, 0.4f * u);
                Part(c, "box", new Vector3(1.4f * u, len * 0.7f, 0.5f * u), new Vector3(0f, mid, 1.3f * u), Quaternion.identity, st.dark);
            }
            else
            {
                foreach (float side in new[] { -1f, 1f })
                    Part(c, "box", new Vector3(railW, len, depth), new Vector3(side * gap, mid, 0f), Quaternion.identity, st.frame, 0.2f * u);
                // Cross braces between the rails (a zig-zag), and a dark core between them.
                int n = Mathf.Clamp(Mathf.RoundToInt(len / (4.5f * u)), 2, 8);
                float step = len / n;
                for (int i = 0; i < n; i++)
                {
                    float y0 = start + i * step, y1 = y0 + step;
                    float x0 = (i % 2 == 0 ? -gap : gap), x1 = -x0;
                    var a = new Vector3(x0, y0, -depth * 0.25f);
                    var b = new Vector3(x1, y1, -depth * 0.25f);
                    var d = b - a;
                    Part(c, "box", new Vector3(0.55f * u, d.magnitude, 0.6f * u), (a + b) * 0.5f,
                         Quaternion.FromToRotation(Vector3.up, d.normalized), st.frame);
                }
                Part(c, "box", new Vector3(2f * gap - railW, len * 0.62f, depth * 0.6f), new Vector3(0f, mid, 0.2f * u), Quaternion.identity, st.dark, 0.2f * u);
            }

            // Hydraulic cylinders: calf (behind), thigh (in front); with 2 also the forearm (in front).
            bool hydraulic = st.hydraulics >= 1 && leg || st.hydraulics >= 2 && role.Contains("Arm");
            if (hydraulic && len > 6f * u)
            {
                float z = (role == "LowerLeg" ? -1f : 1f) * (depth * 0.5f + 1.6f * u);
                Hydraulic(c, start + 0.05f * len, end - 0.08f * len, 0f, z, 1.6f * u);
                if (st.bulk > 1.2f && leg)   // heavy: a second, outer one
                    Hydraulic(c, start + 0.1f * len, end - 0.15f * len, 0f, -z, 1.3f * u);
            }
            if (len < 4f * u) return;
            var outward = Outward(c);   // away from the body, in the bone's own space

            if (st.details && role != "Neck")
            {
                // Cable bundle along the outside (one black, one brass), held by clamps; an actuator on the side.
                var side = outward * (gap + railW + 1.1f * u);
                for (int i = 0; i < 2; i++)
                    Part(c, "cylinder", new Vector3(0.6f * u, len * 0.92f, 0.6f * u), side + new Vector3(0f, mid, (i - 0.5f) * 0.9f * u),
                         Quaternion.identity, i == 0 ? st.cable : st.brass, 0f);
                for (int i = 0; i < 3; i++)
                    Part(c, "box", new Vector3(1.2f * u, 0.5f * u, 2.2f * u), side + new Vector3(0f, start + len * (0.2f + 0.3f * i), 0f),
                         Quaternion.identity, st.dark);
                if (role == "UpperLeg" || role == "LowerArm")
                    Hydraulic(c, start + 0.15f * len, end - 0.12f * len, -outward.x * (gap + railW + 1.2f * u), 0f, 1.1f * u);
            }

            if (st.armor) Armor(c, role, outward, gap + railW, depth, start, len, mid);
        }

        /// <summary>Military: flat, angled armor plates with bolts (and hazard stripes on the shins).</summary>
        static void Armor(Ctx c, string role, Vector3 outward, float half, float depth, float start, float len, float mid)
        {
            var st = c.st;
            float u = c.u;
            var tilt = Quaternion.AngleAxis(-8f * Mathf.Sign(outward.x == 0f ? 1f : outward.x), Vector3.forward);
            switch (role)
            {
                case "UpperArm":
                    Plate(c, new Vector3(1f * u, len * 0.75f, 6f * u), outward * (half + 2.4f * u) + new Vector3(0f, mid, 0f), tilt, st.plate);
                    break;
                case "LowerArm":   // a bulky forearm block (where a weapon is held)
                    Plate(c, new Vector3(5f * u, len * 0.62f, 6.5f * u), outward * (half * 0.6f) + new Vector3(0f, mid + len * 0.08f, 0.5f * u),
                          Quaternion.identity, st.plate, bolts: false);
                    Part(c, "box", new Vector3(5.2f * u, 0.8f * u, 6.7f * u), outward * (half * 0.6f) + new Vector3(0f, mid - len * 0.2f, 0.5f * u),
                         Quaternion.identity, st.accent, 0.2f * u);
                    break;
                case "UpperLeg":
                    Plate(c, new Vector3(1.1f * u, len * 0.7f, 7f * u), outward * (half + 2.6f * u) + new Vector3(0f, mid, 0f), tilt, st.plate);
                    Plate(c, new Vector3(5.5f * u, len * 0.45f, 1f * u), new Vector3(0f, start + len * 0.3f, depth * 0.5f + 3.6f * u),
                          Quaternion.AngleAxis(-6f, Vector3.right), st.plate);
                    break;
                case "LowerLeg":
                {
                    // Shin guard (tilted back a little), knee block, hazard stripes at the bottom.
                    float z = depth * 0.5f + 3.2f * u;
                    Plate(c, new Vector3(6f * u, len * 0.7f, 1.1f * u), new Vector3(0f, mid + len * 0.05f, z), Quaternion.AngleAxis(5f, Vector3.right), st.plate);
                    Part(c, "box", new Vector3(5f * u, 3.5f * u, 3f * u), new Vector3(0f, 0.6f * u, z - 0.6f * u), Quaternion.identity, st.plate, 0.6f * u);
                    for (int i = 0; i < 5; i++)
                        Part(c, "box", new Vector3(1.15f * u, 1.1f * u, 0.4f * u), new Vector3((i - 2) * 1.15f * u, mid - len * 0.27f, z + 0.75f * u),
                             Quaternion.identity, i % 2 == 0 ? st.hazard : st.dark);
                    Plate(c, new Vector3(1.1f * u, len * 0.6f, 6f * u), outward * (half + 2.2f * u) + new Vector3(0f, mid, -0.5f * u), tilt, st.plate);
                    break;
                }
            }
        }

        /// <summary>A flat armor plate (beveled), with four bolts on its face.</summary>
        static void Plate(Ctx c, Vector3 size, Vector3 local, Quaternion turn, string color, bool bolts = true)
        {
            float u = c.u;
            Part(c, "box", size, local, turn, color, Mathf.Min(size.x, size.z) * 0.3f);
            if (!bolts || !c.st.details) return;
            // Bolts on the thin side's face: the face is the side with the smallest size.
            bool faceX = size.x < size.z;
            var normal = faceX ? Vector3.right * Mathf.Sign(local.x == 0f ? 1f : local.x) : Vector3.forward;
            var across = faceX ? Vector3.forward * size.z : Vector3.right * size.x;
            foreach (float a in new[] { -0.36f, 0.36f })
                foreach (float b in new[] { -0.4f, 0.4f })
                {
                    var at = local + turn * (normal * ((faceX ? size.x : size.z) * 0.5f) + across * a + Vector3.up * size.y * b);
                    Part(c, "cylinder", new Vector3(0.7f * u, 0.4f * u, 0.7f * u), at, turn * Quaternion.FromToRotation(Vector3.up, normal), c.st.dark, 0f, 6);
                }
        }

        /// <summary>The direction away from the body (left side: -X), in the bone's own space, flat (no Y).</summary>
        static Vector3 Outward(Ctx c)
        {
            float x = Mathf.Abs(c.head.x) > 1e-3f ? c.head.x : c.tail.x;
            var o = Quaternion.Inverse(c.rot) * (x < 0f ? Vector3.left : Vector3.right);
            o.y = 0f;
            return o.sqrMagnitude > 1e-4f ? o.normalized : Vector3.right;
        }

        /// <summary>A hydraulic cylinder from y0 to y1 (a thick barrel, a shiny rod, ball ends).</summary>
        static void Hydraulic(Ctx c, float y0, float y1, float x, float z, float dia)
        {
            float len = y1 - y0;
            if (len <= 0f) return;
            float barrel = len * 0.55f;
            Part(c, "cylinder", new Vector3(dia, barrel, dia), new Vector3(x, y0 + barrel * 0.5f, z), Quaternion.identity, c.st.dark);
            Part(c, "cylinder", new Vector3(dia * 0.5f, len - barrel * 0.6f, dia * 0.5f), new Vector3(x, y1 - (len - barrel * 0.6f) * 0.5f, z),
                 Quaternion.identity, c.st.rod);
            Part(c, "sphere", Vector3.one * dia * 1.1f, new Vector3(x, y0, z), Quaternion.identity, c.st.joint);
            Part(c, "sphere", Vector3.one * dia * 0.9f, new Vector3(x, y1, z), Quaternion.identity, c.st.joint);
        }

        static void Shoulder(Ctx c)
        {
            float u = c.u;
            Part(c, "box", new Vector3(3f * u, c.L * 0.85f, 3f * u), new Vector3(0f, c.L * 0.5f, 0f), Quaternion.identity, c.st.frame, 0.4f * u);
            if (c.st.truss)   // the slide rail the shoulder moves out on
                Part(c, "box", new Vector3(1.2f * u, c.L * 0.95f, 1.2f * u), new Vector3(0f, c.L * 0.5f, 2.2f * u), Quaternion.identity, c.st.joint);
            if (c.st.details)   // an actuator from the chest out to the shoulder joint
                Part(c, "cylinder", new Vector3(1.2f * u, c.L * 0.8f, 1.2f * u), new Vector3(0f, c.L * 0.5f, -2.2f * u), Quaternion.identity, c.st.rod);
            if (!c.st.military) return;
            // The big angled shoulder block over the shoulder joint, a weapon mount on top.
            float x = Mathf.Sign(c.tail.x == 0f ? 1f : c.tail.x);
            var top = c.tail + Vector3.up * 2.2f * u;
            World(c, "box", new Vector3(8f * u, 5.5f * u, 8.5f * u), top + Vector3.right * x * 1.2f * u,
                  Quaternion.AngleAxis(-14f * x, Vector3.forward), c.st.plate, 1f * u);
            World(c, "box", new Vector3(8.4f * u, 0.9f * u, 8.9f * u), top + Vector3.right * x * 1.2f * u + Vector3.down * 2.2f * u,
                  Quaternion.AngleAxis(-14f * x, Vector3.forward), c.st.accent, 0.3f * u);
            World(c, "box", new Vector3(3.2f * u, 1.6f * u, 4.5f * u), top + Vector3.up * 3.4f * u, Quaternion.identity, c.st.dark, 0.3f * u);
            World(c, "cylinder", new Vector3(1.2f * u, 3.6f * u, 1.2f * u), top + Vector3.up * 4.4f * u, AxisX, c.st.joint);
        }

        static void Hips(Ctx c)
        {
            float u = c.u;
            var legs = c.content.bones.Where(b => b.parentId == c.bone.id && MechFrame.Role(b.name) == "UpperLeg").ToList();
            float spread = legs.Count >= 2 ? legs.Max(b => b.head.x) - legs.Min(b => b.head.x) : 6f * u;
            float w = spread + 5f * u, h = Mathf.Max(c.L * 1.3f, 4f * u);
            var at = c.head + Vector3.up * (c.L * 0.3f);
            World(c, "box", new Vector3(w * 0.7f, h, 4.5f * u), at, Quaternion.identity, c.st.dark, 0.5f * u);
            World(c, "box", new Vector3(w * 0.45f, h * 0.7f, 5.2f * u), at, Quaternion.identity, c.st.frame, 0.4f * u);
            if (c.st.truss)   // the hip slide rail (pantograph) across, under the block
                World(c, "box", new Vector3(w * 1.05f, 1.2f * u, 1.8f * u), at + Vector3.down * (h * 0.5f + 0.4f * u), Quaternion.identity, c.st.joint, 0.2f * u);
            if (!c.st.armor) return;
            // Skirt armor: a front plate and side plates hanging over the hip joints.
            World(c, "box", new Vector3(w * 0.42f, h * 1.3f, 1f * u), at + new Vector3(0f, -h * 0.25f, 3.6f * u), Quaternion.AngleAxis(-10f, Vector3.right), c.st.plate, 0.3f * u);
            foreach (float side in new[] { -1f, 1f })
                World(c, "box", new Vector3(1f * u, h * 0.8f, 4f * u), at + new Vector3(side * (w * 0.42f + 1.6f * u), -h * 0.15f, 0f),
                      Quaternion.AngleAxis(side * 10f, Vector3.forward), c.st.plate, 0.3f * u);
        }

        static void Spine(Ctx c)
        {
            float u = c.u;
            Part(c, "cylinder", new Vector3(3.2f * u, c.L * 0.9f, 3.2f * u), new Vector3(0f, c.L * 0.5f, -0.5f * u), Quaternion.identity, c.st.frame);
            if (c.st.truss)
                foreach (float side in new[] { -1f, 1f })   // ribs on both sides
                    for (int i = 0; i < 3; i++)
                        Part(c, "box", new Vector3(3.5f * u, 0.6f * u, 4.5f * u), new Vector3(side * 2.6f * u, c.L * (0.2f + 0.3f * i), 0f),
                             Quaternion.identity, c.st.dark, 0.1f * u);
            else
                Part(c, "box", new Vector3(5f * u, c.L * 0.6f, 3.5f * u), new Vector3(0f, c.L * 0.5f, 0f), Quaternion.identity, c.st.dark, 0.4f * u);
            if (c.st.details)   // cables down the back
                for (int i = 0; i < 3; i++)
                    Part(c, "cylinder", new Vector3(0.7f * u, c.L * 1.05f, 0.7f * u), new Vector3((i - 1) * 1f * u, c.L * 0.5f, -2.6f * u),
                         Quaternion.identity, i == 1 ? c.st.brass : c.st.cable);
        }

        /// <summary>The chest ("semi-monocoque" core): a dark shell block, frame rings, hatch, shoulder rail, back.</summary>
        static void Core(Ctx c)
        {
            float u = c.u;
            var st = c.st;
            bool vertical = Mathf.Abs(c.dir.y) > 0.6f;
            var front = vertical ? Vector3.forward : Vector3.ProjectOnPlane(c.dir, Vector3.up).normalized;
            if (front.sqrMagnitude < 1e-4f) front = Vector3.forward;
            var turn = Quaternion.LookRotation(front, Vector3.up);
            var children = c.content.bones.Where(b => b.parentId == c.bone.id).ToList();
            float span = children.Count > 0 ? children.Max(b => Mathf.Max(Mathf.Abs(b.head.x), Mathf.Abs(b.tail.x))) * 2f : 0f;
            float w, h, d;
            if (vertical)
            {
                h = Mathf.Max(c.L * 1.15f, 6f * u);
                w = span > 0f ? Mathf.Clamp(span * 0.42f, h * 0.7f, h * 1.6f) : h;
                d = w * 0.68f;
            }
            else
            {
                d = Mathf.Max(c.L * 1.05f, 6f * u);
                w = Mathf.Max(d * 0.42f, span * 0.55f);
                h = d * 0.38f;
            }
            var mid = (c.head + c.tail) * 0.5f;
            Vector3 P(float x, float y, float z) => mid + turn * new Vector3(x, y, z);

            World(c, "box", new Vector3(w, h, d), mid, turn, st.dark, 0.08f * Mathf.Min(w, d));
            // The cockpit hatch on the front.
            World(c, "box", new Vector3(w * 0.38f, h * 0.36f, 0.8f * u), P(0f, h * 0.05f, d * 0.5f + 0.3f * u), turn, st.accent, 0.2f * u);
            if (vertical && span > 0f)   // the rail the shoulders hang on
                World(c, "box", new Vector3(span * 0.95f, 2.2f * u, 2.6f * u), P(0f, h * 0.38f, 0f), turn, st.frame, 0.3f * u);
            if (!st.truss) return;

            // Frame rings around the shell (hollow boxes), thrusters + vents on the back.
            for (int i = 0; i < 3; i++)
            {
                float y = h * (-0.3f + 0.3f * i);
                World(c, "box", new Vector3(w * 1.08f, 0.9f * u, d * 1.1f), P(0f, y, 0f), turn, st.frame, 0f, hollow: 0.9f * u);
            }
            foreach (float side in new[] { -1f, 1f })
                World(c, "cylinder", new Vector3(2.8f * u, 3.2f * u, 2.8f * u), P(side * w * 0.25f, h * 0.15f, -d * 0.5f - 1.6f * u), turn * AxisZ, st.frame);
            if (st.fins)
                for (int i = 0; i < 4; i++)
                    World(c, "box", new Vector3(w * 0.3f, 0.35f * u, 1.4f * u), P(0f, -h * 0.25f + i * 1.0f * u, -d * 0.5f - 0.7f * u), turn, st.dark);
            if (st.armor)
            {
                // Two angled chest plates, a collar, and boosters on the back.
                foreach (float side in new[] { -1f, 1f })
                    World(c, "box", new Vector3(w * 0.46f, h * 0.62f, 1.4f * u), P(side * w * 0.26f, h * 0.12f, d * 0.5f + 1.6f * u),
                          turn * Quaternion.AngleAxis(side * 14f, Vector3.up) * Quaternion.AngleAxis(-8f, Vector3.right), st.plate, 0.4f * u);
                World(c, "box", new Vector3(w * 0.5f, 2.2f * u, d * 0.75f), P(0f, h * 0.5f + 1f * u, 0f), turn, st.plate, 0.5f * u);
                foreach (float side in new[] { -1f, 1f })
                {
                    var booster = P(side * w * 0.3f, h * 0.05f, -d * 0.5f - 4.2f * u);
                    World(c, "box", new Vector3(4.6f * u, h * 0.75f, 4.6f * u), booster, turn, st.plate, 0.6f * u);
                    World(c, "cone", new Vector3(3.8f * u, 3.4f * u, 3.8f * u), booster + Vector3.down * (h * 0.375f + 1.5f * u), Quaternion.identity, st.dark);
                    World(c, "cylinder", new Vector3(2.4f * u, 0.4f * u, 2.4f * u), booster + Vector3.down * (h * 0.375f + 3.1f * u), Quaternion.identity, st.glow);
                }
                for (int i = 0; i < 5; i++)   // hazard stripe across the collar's front
                    World(c, "box", new Vector3(1.2f * u, 0.9f * u, 0.4f * u), P((i - 2) * 1.2f * u, h * 0.5f + 1f * u, d * 0.375f + 0.2f * u), turn,
                          i % 2 == 0 ? st.hazard : st.dark);
            }
            if (st.details)   // cables from the core down into the waist
                foreach (float side in new[] { -1f, 1f })
                    World(c, "cylinder", new Vector3(0.8f * u, h * 0.7f, 0.8f * u), P(side * w * 0.42f, -h * 0.5f, -d * 0.3f), turn, side < 0 ? st.cable : st.brass);
            if (st.coreGlow)   // the glowing mechanical core (Sazabi)
            {
                World(c, "sphere", Vector3.one * w * 0.22f, P(0f, -h * 0.12f, d * 0.5f), turn, st.glow);
                foreach (float side in new[] { -1f, 1f })
                    World(c, "box", new Vector3(w * 0.12f, h * 0.6f, 1f * u), P(side * w * 0.36f, 0f, d * 0.5f + 0.4f * u), turn, st.accent, 0.2f * u);
            }
        }

        static void Head(Ctx c)
        {
            float u = c.u;
            var st = c.st;
            float s = Mathf.Max(c.L * 0.78f, 4f * u);
            var mid = c.head + c.dir * (c.L * 0.5f);
            if (st.military)
            {
                // A wide, flat sensor head: a lens cluster in front, a visor slit, antennas sweeping back.
                World(c, "box", new Vector3(s * 1.15f, s * 0.62f, s * 1.05f), mid, Quaternion.identity, st.plate, s * 0.1f);
                World(c, "box", new Vector3(s * 0.9f, s * 0.14f, 0.6f * u), mid + new Vector3(0f, s * 0.1f, s * 0.53f), Quaternion.identity, st.dark);
                var lenses = new[] { new Vector2(0f, -0.08f), new Vector2(-0.2f, -0.12f), new Vector2(0.2f, -0.12f) };
                for (int i = 0; i < lenses.Length; i++)
                {
                    float size = i == 0 ? 0.26f : 0.16f;
                    var at = mid + new Vector3(lenses[i].x * s, lenses[i].y * s, s * 0.53f + 0.4f * u);
                    World(c, "cylinder", new Vector3(s * size, 1f * u, s * size), at, AxisZ, st.dark, 0f, 8);
                    World(c, "cylinder", new Vector3(s * size * 0.6f, 0.3f * u, s * size * 0.6f), at + Vector3.forward * 0.6f * u, AxisZ, st.glow, 0f, 8);
                }
                foreach (float side in new[] { -1f, 1f })
                    World(c, "cylinder", new Vector3(0.5f * u, s * 0.9f, 0.5f * u), mid + new Vector3(side * s * 0.45f, s * 0.45f, -s * 0.2f),
                          Quaternion.Euler(-55f, 0f, side * -15f), st.rod, 0f, 6);
                World(c, "box", new Vector3(s * 0.5f, s * 0.3f, s * 0.5f), mid + new Vector3(0f, -s * 0.35f, -s * 0.15f), Quaternion.identity, st.frame, 0.2f * u);
                return;
            }
            World(c, "box", new Vector3(s * 0.85f, s, s * 0.9f), mid, Quaternion.identity, st.dark, s * 0.12f);
            World(c, "box", new Vector3(s * 0.62f, s * 0.16f, 0.5f * u), mid + new Vector3(0f, s * 0.12f, s * 0.45f), Quaternion.identity, st.glow);
            foreach (float side in new[] { -1f, 1f })   // "ear" pins
                World(c, "cylinder", new Vector3(s * 0.35f, 1.2f * u, s * 0.35f), mid + new Vector3(side * (s * 0.425f + 0.6f * u), 0f, 0f), AxisX, st.joint);
            if (st.truss)   // frame bar over the top
                World(c, "box", new Vector3(1.2f * u, 1.2f * u, s * 1.05f), mid + new Vector3(0f, s * 0.5f + 0.4f * u, 0f), Quaternion.identity, st.frame, 0.2f * u);
        }

        static void Hand(Ctx c)
        {
            float L = Mathf.Max(c.L, 3f * c.u);
            var st = c.st;
            Part(c, "sphere", Vector3.one * L * 0.42f, Vector3.zero, Quaternion.identity, st.joint);
            Part(c, "box", new Vector3(L * 0.8f, L * 0.5f, L * 0.36f), new Vector3(0f, L * 0.32f, 0f), Quaternion.identity, st.frame, L * 0.05f);
            if (!st.fingers)
            {
                Part(c, "box", new Vector3(L * 0.78f, L * 0.4f, L * 0.3f), new Vector3(0f, L * 0.8f, 0f), Quaternion.identity, st.dark, L * 0.04f);
                return;
            }
            for (int f = 0; f < 4; f++)   // four fingers, two segments each, with knuckle pins
            {
                float x = (-0.3f + 0.2f * f) * L;
                for (int s = 0; s < 2; s++)
                {
                    float y = L * (0.66f + 0.24f * s);
                    Part(c, "box", new Vector3(L * 0.15f, L * 0.2f, L * 0.2f), new Vector3(x, y, 0f), Quaternion.identity, s == 0 ? st.frame : st.dark, L * 0.02f);
                }
            }
            Part(c, "cylinder", new Vector3(L * 0.12f, L * 0.82f, L * 0.12f), new Vector3(0f, L * 0.58f, 0f), AxisX, st.joint);
            // Thumb on the inner side (toward the body).
            var inner = Quaternion.Inverse(c.rot) * (c.head.x > 0f ? Vector3.left : Vector3.right);
            inner.y = 0f;
            inner = inner.sqrMagnitude > 1e-4f ? inner.normalized : Vector3.right;
            Part(c, "box", new Vector3(L * 0.17f, L * 0.36f, L * 0.2f), inner * (L * 0.46f) + new Vector3(0f, L * 0.36f, L * 0.08f),
                 Quaternion.AngleAxis(25f, Vector3.Cross(Vector3.up, inner)), st.frame, L * 0.02f);
        }

        static void Foot(Ctx c)
        {
            float u = c.u;
            var st = c.st;
            var flat = Vector3.ProjectOnPlane(c.tail - c.head, Vector3.up);
            var front = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
            var turn = Quaternion.LookRotation(front, Vector3.up);
            float FL = Mathf.Max(flat.magnitude * 1.8f, 7f * u), w = FL * 0.42f;
            var ankle = c.head;
            var floor = new Vector3(ankle.x, 0f, ankle.z);
            Vector3 P(float x, float y, float z) => floor + turn * new Vector3(x, y, z);
            float heel = -0.3f * FL;

            World(c, "sphere", Vector3.one * 3.6f * u, ankle, Quaternion.identity, st.joint);
            // Sole (heel to the toe joint), toe plate, toe pin.
            float toeJoint = heel + FL * 0.7f;
            World(c, "box", new Vector3(w, 1.4f * u, toeJoint - heel), P(0f, 0.7f * u, (heel + toeJoint) * 0.5f), turn, st.frame, 0.3f * u);
            World(c, "box", new Vector3(w * 0.9f, 1.1f * u, FL * 0.3f), P(0f, 0.55f * u, toeJoint + FL * 0.15f), turn, st.dark, 0.3f * u);
            World(c, "cylinder", new Vector3(1.4f * u, w * 0.95f, 1.4f * u), P(0f, 1.3f * u, toeJoint), turn * AxisX, st.joint);
            // Ankle armature: side plates from the ankle down to the sole.
            float up = Mathf.Max(ankle.y - 1.4f * u, u);
            foreach (float side in new[] { -1f, 1f })
                World(c, "box", new Vector3(0.7f * u, up, FL * 0.32f), P(side * w * 0.32f, 1.4f * u + up * 0.5f, 0f), turn, st.frame, 0.2f * u);
            if (st.truss)
                World(c, "box", new Vector3(w * 0.7f, 2f * u, 1.6f * u), P(0f, 1.4f * u, heel + 0.8f * u), turn, st.dark, 0.3f * u);
            if (st.armor)   // toe cap and ankle guard
            {
                World(c, "box", new Vector3(w * 1.02f, 2.4f * u, FL * 0.34f), P(0f, 2.2f * u, toeJoint + FL * 0.14f),
                      turn * Quaternion.AngleAxis(8f, Vector3.right), st.plate, 0.6f * u);
                World(c, "box", new Vector3(w * 0.8f, Mathf.Min(up, 6f * u), 1.2f * u), P(0f, 1.4f * u + Mathf.Min(up, 6f * u) * 0.5f, FL * 0.12f),
                      turn * Quaternion.AngleAxis(-12f, Vector3.right), st.plate, 0.3f * u);
            }
        }

        static void Turret(Ctx c)
        {
            float u = c.u;
            float ring = Mathf.Max(c.L * 2.4f, 10f * u);
            World(c, "cylinder", new Vector3(ring, 1.6f * u, ring), c.head, Quaternion.identity, c.st.frame, 0f, segments: 16);
            World(c, "box", new Vector3(ring * 0.55f, Mathf.Max(c.L * 0.8f, 3f * u), ring * 0.5f), c.head + c.dir * (c.L * 0.5f), Quaternion.identity, c.st.dark, 0.4f * u);
        }

        /// <summary>Mannequin: one plain block per bone (and a ball at its joint).</summary>
        static void Blocks(Ctx c, string role)
        {
            float u = c.u, L = c.L;
            var st = c.st;
            switch (role)
            {
                case "Chest": case "Body": case "Cockpit": case "Hull": case "Hips":
                {
                    float w = role == "Hips" ? 13f * u : 18f * u;
                    var mid = (c.head + c.tail) * 0.5f;
                    bool vertical = Mathf.Abs(c.dir.y) > 0.6f;
                    var size = vertical ? new Vector3(w, Mathf.Max(L, 4f * u), w * 0.6f) : new Vector3(w * 0.8f, w * 0.5f, Mathf.Max(L, 4f * u));
                    World(c, "box", size, mid, Quaternion.identity, st.frame, 0.6f * u);
                    return;
                }
                case "Head":
                    World(c, "box", Vector3.one * Mathf.Max(L, 4f * u), (c.head + c.tail) * 0.5f, Quaternion.identity, st.frame, 0.6f * u);
                    return;
                case "Foot":
                {
                    var flat = Vector3.ProjectOnPlane(c.tail - c.head, Vector3.up);
                    var front = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
                    float FL = Mathf.Max(flat.magnitude * 1.8f, 7f * u);
                    var at = new Vector3(c.head.x, 1.5f * u, c.head.z) + front * (FL * 0.2f);
                    World(c, "box", new Vector3(FL * 0.45f, 3f * u, FL), at, Quaternion.LookRotation(front, Vector3.up), st.frame, 0.6f * u);
                    return;
                }
            }
            if (L < 1e-3f) return;
            float t = role.Contains("Leg") ? 6f : role.Contains("Arm") ? 4.5f : role == "Hand" ? 4f : 3f;
            Part(c, "box", new Vector3(t * u, L * 0.9f, t * u), new Vector3(0f, L * 0.5f, 0f), Quaternion.identity, st.frame, 0.5f * u);
            Part(c, "sphere", Vector3.one * t * 0.8f * u, Vector3.zero, Quaternion.identity, st.joint);
        }

        // ---------- making pieces ----------

        /// <summary>A piece in the bone's own space (Y along the bone, Z front).</summary>
        static void Part(Ctx c, string shape, Vector3 size, Vector3 local, Quaternion localTurn, string color, float bevel = 0f, int segments = 12)
            => World(c, shape, size, c.head + c.rot * local, c.rot * localTurn, color, bevel, 0f, segments);

        /// <summary>A piece in world space.</summary>
        static void World(Ctx c, string shape, Vector3 size, Vector3 at, Quaternion turn, string color, float bevel = 0f,
                          float hollow = 0f, int segments = 12)
        {
            if (size.x <= 1e-5f || size.y <= 1e-5f || size.z <= 1e-5f) return;
            c.list.Add(new Piece
            {
                id = Ids.New(), shape = shape, color = color, boneId = c.bone.id,
                position = at.ToDoc(), rotation = turn.ToDoc(),
                p = new ShapeParams
                {
                    width = size.x, height = size.y, length = size.z, segments = segments,
                    bevel = bevel, hollow = hollow > 0f, wall = hollow > 0f ? hollow : 0.1f,
                },
            });
        }

        static Vector3 Perpendicular(Vector3 want, Vector3 axis)
        {
            var p = Vector3.ProjectOnPlane(want, axis);
            if (p.sqrMagnitude < 1e-4f) p = Vector3.ProjectOnPlane(Vector3.up, axis);
            if (p.sqrMagnitude < 1e-4f) p = Vector3.ProjectOnPlane(Vector3.right, axis);
            return p.normalized;
        }
    }
}
