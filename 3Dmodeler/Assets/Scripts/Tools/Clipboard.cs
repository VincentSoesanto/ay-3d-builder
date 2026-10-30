using System.Collections.Generic;
using Builder.Core;
using Builder.View;
using UnityEngine;

namespace Builder.Tools
{
    /// <summary>
    /// Copied pieces (Ctrl+C), stored relative to the bottom-middle of the copied group, so a paste can put the
    /// group down like one big piece. Their rotations stay as they were.
    /// </summary>
    public class Clipboard : MonoBehaviour
    {
        readonly List<Piece> parts = new List<Piece>();

        public int Count => parts.Count;

        /// <summary>Copies of the stored pieces (position = offset from the group's bottom-middle).</summary>
        public List<Piece> Parts => parts.ConvertAll(p => p.Clone());

        /// <param name="worldBounds">The box around the pieces (sets the group's bottom-middle point).</param>
        /// <param name="content">The project the pieces are in (whole linked blueprint copies stay linked when pasted).</param>
        public void Copy(IEnumerable<Piece> pieces, Bounds worldBounds, BuildContent content = null)
        {
            parts.Clear();
            var origin = new Vector3(worldBounds.center.x, worldBounds.min.y, worldBounds.center.z);
            // A pasted copy is not linked to the old mirror partner.
            parts.AddRange(LinkedCopies.CopyRelative(pieces, content, origin));
        }
    }
}
