using System.Collections.Generic;
using System.Linq;
using Builder.Core;
using Builder.Input;
using Builder.Shapes;
using Builder.UI;
using Builder.View;
using UnityEngine;

namespace Builder.Tools
{
    /// <summary>
    /// B: builds the solid that wraps around the placed points (8 points at the corners of a block = that block),
    /// and removes those points - one undo step. Points lying flat make a thin plate (3 points = a triangle),
    /// points on a line make a thin stick (2 points = a stick).
    ///   - Select tool with points selected: only those points. Otherwise: all points.
    ///   - Mirror on: uses the points on the right side (X >= 0) and adds the mirror copy of the solid.
    /// The solid is a normal piece ("hull"): move, paint, cut, export, resize (it stretches) like any other.
    /// </summary>
    public class PointHull : MonoBehaviour
    {
        [SerializeField] BuilderSession session;
        [SerializeField] BuilderInput input;
        [SerializeField] Selection selection;
        [SerializeField] ToolManager tools;

        void Update()
        {
            if (input.Pressed(input.BuildHull) && !BuilderInput.CtrlHeld) Build();
        }

        /// <summary>How thick a plate (flat points) or stick (points on a line) is, in meters.</summary>
        public const float FlatThickness = 0.05f;

        static bool IsPoint(Piece p) => p.shape == PointShape.ShapeId;

        /// <summary>Builds the solid. Returns false (and says why) if it can't.</summary>
        public bool Build()
        {
            var content = session.Document.content;
            var chosen = tools.Current == ToolKind.Select ? selection.Pieces().Where(IsPoint).ToList() : new List<Piece>();
            bool fromSelection = chosen.Count > 0;
            if (!fromSelection) chosen = content.pieces.Where(IsPoint).ToList();
            if (session.Document.editor.mirrorX && !fromSelection)
                chosen = chosen.Where(p => p.position.x >= -1e-4f).ToList();   // one side; the mirror copy makes the other

            var spots = ConvexHull.MakeSolid(chosen.Select(p => p.position.ToUnity()).ToList(), FlatThickness, out string kind);
            if (spots == null)
            {
                Toast.Show("Place at least 2 points (inventory > Points), then press B");
                return false;
            }

            // The solid sits in the middle of the points; its points are stored inside a 1 x 1 x 1 box.
            var box = new Bounds(spots[0], Vector3.zero);
            foreach (var s in spots) box.Encapsulate(s);
            var size = Vector3.Max(box.size, Vector3.one * 0.001f);
            var hull = new Piece
            {
                id = Ids.New(),
                shape = HullShape.ShapeId,
                p = new ShapeParams
                {
                    width = size.x, height = size.y, length = size.z,
                    points = spots.Select(s => new Vec3((s.x - box.center.x) / size.x, (s.y - box.center.y) / size.y,
                                                        (s.z - box.center.z) / size.z)).ToList(),
                },
                position = box.center.ToDoc(),
                color = "#B0B0B0",
            };
            session.ReplacePieces(chosen.Select(p => p.id).ToList(), new[] { hull }, $"Build solid from {chosen.Count} points");
            Toast.Show($"Built a {kind} from {chosen.Count} points");
            return true;
        }
    }
}
