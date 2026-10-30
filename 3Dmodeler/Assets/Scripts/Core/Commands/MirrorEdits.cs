using System.Collections.Generic;

namespace Builder.Core
{
    /// <summary>
    /// Mirror building across the X = 0 plane (left/right). A mirrored copy has its X position flipped,
    /// its rotation reflected and its mesh built left-handed (<see cref="Piece.mirrored"/>), so it is an exact
    /// mirror image for every shape. The two pieces point at each other with <see cref="Piece.mirrorPartnerId"/>.
    /// </summary>
    public static class MirrorEdits
    {
        const float OnPlaneTolerance = 1e-4f;

        public static Vec3 Position(Vec3 p) => new Vec3(-p.x, p.y, p.z);

        /// <summary>Reflecting a rotation across the X = 0 plane: (x, y, z, w) -> (x, -y, -z, w).</summary>
        public static Quat Rotation(Quat q) => new Quat(q.x, -q.y, -q.z, q.w);

        /// <summary>True if the piece sits on the mirror line, so its mirror copy would land on itself.</summary>
        public static bool OnPlane(Piece p) => System.Math.Abs(p.position.x) < OnPlaneTolerance;

        /// <summary>The mirror image of <paramref name="source"/>, with the given id, linked back to the source.</summary>
        public static Piece MirrorOf(Piece source, string id)
        {
            var m = source.Clone();
            m.id = id;
            m.position = Position(source.position);
            m.rotation = Rotation(source.rotation);
            m.mirrored = !source.mirrored;
            m.mirrorPartnerId = source.id;
            // Spray paint is stored in the piece's own (mesh) space, which is mirrored too: cell x -> -x - 1.
            if (m.paint != null) m.paint = m.paint.ConvertAll(d => new PaintDot(-d.x - 1, d.y, d.z, d.c));
            return m;
        }

        /// <summary>
        /// For placing: returns the new pieces plus a mirror copy of each (except pieces on the mirror line).
        /// The originals get linked to their copies.
        /// </summary>
        public static List<Piece> WithMirrorCopies(IEnumerable<Piece> pieces)
        {
            var all = new List<Piece>();
            var copies = new List<Piece>();
            foreach (var p in pieces)
            {
                all.Add(p);
                if (OnPlane(p)) continue;
                var copy = MirrorOf(p, Ids.New());
                p.mirrorPartnerId = copy.id;
                copies.Add(copy);
            }
            all.AddRange(copies);
            return all;
        }

        /// <summary>For deleting: the ids plus the ids of their mirror partners that still exist.</summary>
        public static HashSet<string> WithPartners(IEnumerable<string> ids, BuildContent content)
        {
            var all = new HashSet<string>(ids);
            foreach (var id in new List<string>(all))
            {
                var partner = content.FindPiece(content.FindPiece(id)?.mirrorPartnerId);
                if (partner != null) all.Add(partner.id);
            }
            return all;
        }

        /// <summary>
        /// For editing: adds the mirror partners of the edited pieces to the before/after lists, with the
        /// partner's "after" being the mirror image of the edited piece (keeping the partner's own id and group).
        /// Partners that are themselves being edited are left alone.
        /// </summary>
        public static void AddPartnerEdits(List<Piece> before, List<Piece> after, BuildContent content)
        {
            var edited = new HashSet<string>();
            foreach (var p in after) edited.Add(p.id);
            int count = after.Count;
            for (int i = 0; i < count; i++)
            {
                var piece = after[i];
                var partner = content.FindPiece(piece.mirrorPartnerId);
                if (partner == null || edited.Contains(partner.id)) continue;
                var mirrored = MirrorOf(piece, partner.id);
                mirrored.groupId = partner.groupId;
                mirrored.boneId = partner.boneId;
                mirrored.link = partner.link?.Clone();   // the partner stays part of its own linked copy
                before.Add(partner.Clone());
                after.Add(mirrored);
                edited.Add(partner.id);
            }
        }
    }
}
