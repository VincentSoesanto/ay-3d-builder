using Builder.Core;
using UnityEngine;

namespace Builder.Shapes
{
    /// <summary>
    /// A simple low-poly person, 1.8 m tall by default, to judge sizes while building (a mech next to a human).
    /// Made of boxes: legs, body, arms, head; it faces front (-Z). Not exported (it is only a reference).
    /// Width / height / length stretch it like other shapes.
    /// </summary>
    public class HumanShape : IShape
    {
        public const string ShapeId = "human";
        public const string Color = "#4FC3F7";

        // The person's size before stretching (meters).
        const float BaseWidth = 0.5f, BaseHeight = 1.8f, BaseLength = 0.3f;

        public string Id => ShapeId;
        public string DisplayName => "Human 1.8 m";
        public ShapeFeatures Features => ShapeFeatures.None;

        public ShapeParams CreateDefaults() => new ShapeParams { width = BaseWidth, height = BaseHeight, length = BaseLength };

        public void Build(ShapeParams p, MeshBuilder mb)
        {
            var s = new Vector3(p.width / BaseWidth, p.height / BaseHeight, p.length / BaseLength);
            // (x, y from feet = -0.9 up to the top of the head = 0.9, z): middle and size of each box.
            Box(mb, s, new Vector3(-0.1f, -0.475f, 0f), new Vector3(0.15f, 0.85f, 0.18f));   // left leg
            Box(mb, s, new Vector3(0.1f, -0.475f, 0f), new Vector3(0.15f, 0.85f, 0.18f));    // right leg
            Box(mb, s, new Vector3(0f, 0.25f, 0f), new Vector3(0.40f, 0.62f, 0.24f));         // body
            Box(mb, s, new Vector3(-0.25f, 0.21f, 0f), new Vector3(0.10f, 0.66f, 0.12f));     // left arm
            Box(mb, s, new Vector3(0.25f, 0.21f, 0f), new Vector3(0.10f, 0.66f, 0.12f));      // right arm
            Box(mb, s, new Vector3(0f, 0.585f, 0f), new Vector3(0.10f, 0.05f, 0.10f));        // neck
            Box(mb, s, new Vector3(0f, 0.76f, 0f), new Vector3(0.22f, 0.28f, 0.24f));         // head (top at 0.9 = 1.8 m)
            Box(mb, s, new Vector3(0f, 0.75f, -0.13f), new Vector3(0.14f, 0.05f, 0.03f));     // visor: shows the front
        }

        static void Box(MeshBuilder mb, Vector3 stretch, Vector3 center, Vector3 size)
        {
            center = Vector3.Scale(center, stretch);
            size = Vector3.Scale(size, stretch);
            var outline = Outlines.Rect(size.x, size.z);
            var rings = new[] { new Loft.Ring(outline, -size.y / 2f), new Loft.Ring(outline, size.y / 2f) };
            Loft.Solid(mb, rings, (pt, y) => center + new Vector3(pt.x, y, pt.y));
        }
    }
}
