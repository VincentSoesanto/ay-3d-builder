using System;
using System.Collections.Generic;
using System.Linq;
using Builder.Core;
using Builder.View;
using UnityEngine;

namespace Builder
{
    /// <summary>
    /// Linked blueprint copies (blueprints inside blueprints, and copies placed in projects).
    ///   - A copy is made of normal pieces that carry the same <see cref="PieceLink"/> (so every tool just works).
    ///   - Saving the blueprint again (version + 1) rebuilds every copy of it: in other blueprints (which then
    ///     update their own copies, and so on) and in projects (when they are open).
    ///   - Moving / turning a WHOLE copy keeps it linked. Any other edit (one piece moved, resized, painted, cut,
    ///     deleted ...) unlinks that copy: it keeps its pieces but no longer follows the blueprint.
    ///   - A mirror copy (mirror building) is its own linked copy: the mirror image of the original copy.
    /// </summary>
    public static class LinkedCopies
    {
        const float PositionTolerance = 1e-3f;
        const float AngleTolerance = 0.05f;   // degrees

        /// <summary>A copy of the settings with every size multiplied by <paramref name="s"/>.</summary>
        public static ShapeParams Scaled(ShapeParams p, float s)
        {
            var c = p.Clone();
            c.width *= s;
            c.height *= s;
            c.length *= s;
            c.bevel *= s;
            c.wall *= s;
            return c;
        }

        /// <summary>The pieces of every linked copy in <paramref name="content"/>, by copy id.</summary>
        public static Dictionary<string, List<Piece>> Copies(BuildContent content)
        {
            var copies = new Dictionary<string, List<Piece>>();
            foreach (var p in content.pieces)
            {
                if (p.link?.instanceId == null) continue;
                if (!copies.TryGetValue(p.link.instanceId, out var list)) copies[p.link.instanceId] = list = new List<Piece>();
                list.Add(p);
            }
            return copies;
        }

        /// <summary>Piece number <paramref name="part"/> of a copy, made from the blueprint's piece (its own nested link is dropped).</summary>
        public static Piece MakePart(Piece source, PieceLink link, int part)
        {
            var q = link.flipped ? MirrorEdits.MirrorOf(source, source.id) : source.Clone();
            var rotation = link.rotation.ToUnity();
            q.p = Mathf.Approximately(link.scale, 1f) ? q.p : Scaled(q.p, link.scale);
            q.position = (link.position.ToUnity() + rotation * (q.position.ToUnity() * link.scale)).ToDoc();
            q.rotation = (rotation * q.rotation.ToUnity()).ToDoc();
            q.mirrorPartnerId = null;
            q.groupId = null;
            q.boneId = null;
            q.frame = false;
            q.link = link.Clone();
            q.link.part = part;
            return q;
        }

        /// <summary>The link of the mirror image of a copy (mirrored across X = 0).</summary>
        public static PieceLink MirrorLink(PieceLink link, string instanceId)
        {
            var m = link.Clone();
            m.instanceId = instanceId;
            m.position = MirrorEdits.Position(link.position);
            m.rotation = MirrorEdits.Rotation(link.rotation);
            m.flipped = !link.flipped;
            return m;
        }

        // ---------- updating copies ----------

        /// <summary>
        /// Rebuilds the copies whose blueprint has a newer version. Returns the pieces to remove and to add
        /// (rebuilt pieces keep the old piece ids, groups and mirror partners where the part still exists).
        /// </summary>
        public static (HashSet<string> remove, List<Piece> add, int copies) Rebuild(BuildContent content, Func<string, Blueprint> find) =>
            Rebuild(content, find, out _, out _);

        /// <summary>
        /// Same, plus the copies' own bones (the ones that came with the blueprint): they follow the blueprint's
        /// bones (moved, added, removed; names, roll and limits you changed stay). Copies placed before bones were
        /// remembered (or pasted ones without bones) keep their bones as they are.
        /// </summary>
        public static (HashSet<string> remove, List<Piece> add, int copies) Rebuild(BuildContent content, Func<string, Blueprint> find,
                                                                                    out List<Bone> bonesBefore, out List<Bone> bonesAfter)
        {
            var remove = new HashSet<string>();
            var rebuilt = new Dictionary<string, List<Piece>>();
            var outdated = new List<(string instance, List<Piece> old, PieceLink fresh, Blueprint bp)>();
            foreach (var pair in Copies(content))
            {
                var link = pair.Value[0].link;
                var bp = find(link.blueprintId);
                if (bp == null || bp.version == link.version) continue;
                var fresh = link.Clone();
                fresh.version = bp.version;
                outdated.Add((pair.Key, pair.Value, fresh, bp));
            }
            var boneOf = RebuildBones(content, outdated, out bonesBefore, out bonesAfter);
            var gone = new HashSet<string>(bonesBefore.Select(b => b.id));
            gone.ExceptWith(bonesAfter.Select(b => b.id));

            foreach (var (instance, oldPieces, fresh, bp) in outdated)
            {
                var pair = new KeyValuePair<string, List<Piece>>(instance, oldPieces);
                var oldByPart = new Dictionary<int, Piece>();
                foreach (var p in pair.Value)
                    if (!oldByPart.ContainsKey(p.link.part)) oldByPart[p.link.part] = p;

                var pieces = new List<Piece>();
                for (int i = 0; i < bp.content.pieces.Count; i++)
                {
                    if (fresh.skip != null && fresh.skip.Contains(i)) continue;
                    var piece = MakePart(bp.content.pieces[i], fresh, i);
                    if (oldByPart.TryGetValue(i, out var old))
                    {
                        piece.id = old.id;
                        piece.groupId = old.groupId;
                        piece.boneId = old.boneId != null && gone.Contains(old.boneId) ? null : old.boneId;
                        piece.mirrorPartnerId = old.mirrorPartnerId;
                    }
                    else
                    {
                        piece.id = Ids.New();
                        piece.groupId = pair.Value[0].groupId;
                    }
                    // Bound in the blueprint: bound to that bone of this copy.
                    var bpBone = bp.content.pieces[i].boneId;
                    if (bpBone != null && boneOf(instance, fresh.partnerId, bpBone) is string bound) piece.boneId = bound;
                    pieces.Add(piece);
                }
                foreach (var p in pair.Value) remove.Add(p.id);
                rebuilt[pair.Key] = pieces;
            }

            // Mirror pairs: each part is paired with the same part of the partner copy.
            foreach (var pair in rebuilt)
            {
                string partnerId = pair.Value.Count > 0 ? pair.Value[0].link.partnerId : null;
                if (partnerId == null || !rebuilt.TryGetValue(partnerId, out var partner)) continue;
                foreach (var piece in pair.Value)
                    piece.mirrorPartnerId = partner.Find(x => x.link.part == piece.link.part)?.id;
            }

            var add = rebuilt.Values.SelectMany(x => x).ToList();
            var ids = new HashSet<string>(content.pieces.Where(p => !remove.Contains(p.id)).Select(p => p.id));
            ids.UnionWith(add.Select(p => p.id));
            foreach (var p in add)
                if (p.mirrorPartnerId != null && !ids.Contains(p.mirrorPartnerId)) p.mirrorPartnerId = null;
            return (remove, add, rebuilt.Count);
        }

        /// <summary>
        /// The new bones of the copies whose bones came with the blueprint. Returns a lookup: (copy, its mirror
        /// partner, blueprint bone id) -> the document bone (bones on the mirror line are shared by a mirror pair).
        /// </summary>
        static Func<string, string, string, string> RebuildBones(BuildContent content,
            List<(string instance, List<Piece> old, PieceLink fresh, Blueprint bp)> outdated, out List<Bone> before, out List<Bone> after)
        {
            before = new List<Bone>();
            after = new List<Bone>();
            var ids = new Dictionary<(string, string), string>();   // (copy, blueprint bone) -> document bone id
            var managed = new HashSet<string>(outdated
                .Where(c => c.bp.content.bones.Count > 0 && content.bones.Any(b => b.copyId == c.instance))
                .Select(c => c.instance));
            string Lookup(string instance, string partner, string bpBone) =>
                ids.TryGetValue((instance, bpBone), out var id) ? id
                : partner != null && ids.TryGetValue((partner, bpBone), out var shared) ? shared : null;
            if (managed.Count == 0) return Lookup;

            Bone Placed(Bone b, PieceLink link) => Rig.Placed(b, link.flipped, link.scale, link.rotation.ToUnity(), link.position.ToUnity());
            bool OnMirrorLine(Bone b) => Mathf.Abs(b.head.x) < 1e-3f && Mathf.Abs(b.tail.x) < 1e-3f;

            var copies = outdated.Where(c => managed.Contains(c.instance)).ToList();
            foreach (var c in copies)   // bones the copy already has keep their ids
                foreach (var b in content.bones.Where(b => b.copyId == c.instance))
                {
                    before.Add(b);
                    if (b.copyBone != null && c.bp.content.bones.Any(x => x.id == b.copyBone)) ids[(c.instance, b.copyBone)] = b.id;
                }
            // New blueprint bones get new ids. One on the mirror line is made once for a mirror pair.
            var later = new List<(string instance, string partner, string bpBone)>();
            foreach (var c in copies)
                foreach (var b in c.bp.content.bones)
                {
                    if (ids.ContainsKey((c.instance, b.id))) continue;
                    bool pair = c.fresh.partnerId != null && managed.Contains(c.fresh.partnerId);
                    if (pair && OnMirrorLine(Placed(b, c.fresh))) later.Add((c.instance, c.fresh.partnerId, b.id));
                    else ids[(c.instance, b.id)] = Ids.New();
                }
            foreach (var (instance, partner, bpBone) in later)
                if (!ids.ContainsKey((partner, bpBone))) ids[(instance, bpBone)] = Ids.New();

            foreach (var c in copies)
                foreach (var b in c.bp.content.bones)
                {
                    if (!ids.TryGetValue((c.instance, b.id), out var id)) continue;   // shared: the partner makes it
                    var existing = content.FindBone(id);
                    var placed = Placed(b, c.fresh);
                    placed.id = id;
                    placed.copyId = c.instance;
                    placed.copyBone = b.id;
                    placed.parentId = b.parentId != null ? Lookup(c.instance, c.fresh.partnerId, b.parentId) : existing?.parentId;
                    if (existing != null)
                    {
                        placed.name = existing.name;
                        placed.roll = existing.roll;
                        placed.limits = existing.limits?.Clone();
                    }
                    after.Add(placed);
                }
            return Lookup;
        }

        /// <summary>Rebuilds the out-of-date copies straight in <paramref name="content"/> (no undo). Returns how many.</summary>
        public static int RebuildInPlace(BuildContent content, Func<string, Blueprint> find)
        {
            var (remove, add, copies) = Rebuild(content, find, out var bonesBefore, out var bonesAfter);
            if (copies == 0) return 0;
            new BonesCommand(bonesBefore, bonesAfter, "").Do(content);
            int at = content.pieces.FindIndex(p => remove.Contains(p.id));
            content.pieces.RemoveAll(p => remove.Contains(p.id));
            content.pieces.InsertRange(Mathf.Clamp(at, 0, content.pieces.Count), add);
            return copies;
        }

        /// <summary>True if <paramref name="bp"/> uses blueprint <paramref name="targetId"/> somewhere inside (at any depth).</summary>
        public static bool Uses(Blueprint bp, string targetId, Func<string, Blueprint> find, int depth = 0)
        {
            if (bp == null || depth > 32) return depth > 32;
            foreach (var id in bp.content.pieces.Where(p => p.link != null).Select(p => p.link.blueprintId).Distinct())
                if (id == targetId || Uses(find(id), targetId, find, depth + 1)) return true;
            return false;
        }

        // ---------- edit rules (called by BuilderSession) ----------

        /// <summary>
        /// For a piece edit: copies moved / turned as a whole stay linked (their link moves along);
        /// copies edited in any other way get unlinked (all their pieces). Returns how many copies were unlinked.
        /// </summary>
        public static int ApplyEditRules(BuildContent content, List<Piece> before, List<Piece> after)
        {
            var copies = Copies(content);
            var touched = new HashSet<string>();
            foreach (var p in after)
            {
                var current = content.FindPiece(p.id);
                if (current?.link != null) touched.Add(current.link.instanceId);
            }

            int unlinked = 0;
            foreach (var id in touched)
            {
                var members = copies[id];
                if (TryWholeMove(members, after, out var turn, out var move))
                {
                    foreach (var m in members)
                    {
                        var a = after.Find(x => x.id == m.id);
                        a.link = m.link.Clone();
                        a.link.position = (turn * m.link.position.ToUnity() + move).ToDoc();
                        a.link.rotation = (turn * m.link.rotation.ToUnity()).ToDoc();
                    }
                    continue;
                }
                unlinked++;
                foreach (var m in members) Unlink(m, before, after);
            }
            return unlinked;
        }

        /// <summary>For deleting: copies that lose only some of their pieces get unlinked. Returns how many.</summary>
        public static int ApplyRemoveRules(BuildContent content, ICollection<string> removed, List<Piece> before, List<Piece> after)
        {
            int unlinked = 0;
            foreach (var members in Copies(content).Values)
            {
                if (!members.Any(m => removed.Contains(m.id))) continue;
                var rest = members.Where(m => !removed.Contains(m.id)).ToList();
                if (rest.Count == 0) continue;
                unlinked++;
                foreach (var m in rest) Unlink(m, before, after);
            }
            return unlinked;
        }

        static void Unlink(Piece member, List<Piece> before, List<Piece> after)
        {
            var a = after.Find(x => x.id == member.id);
            if (a == null)
            {
                before.Add(member.Clone());
                a = member.Clone();
                after.Add(a);
            }
            a.link = null;
        }

        /// <summary>True if every piece of the copy is in <paramref name="after"/>, moved by the same turn + move, and nothing else changed.</summary>
        static bool TryWholeMove(List<Piece> members, List<Piece> after, out Quaternion turn, out Vector3 move)
        {
            turn = Quaternion.identity;
            move = Vector3.zero;
            bool first = true;
            foreach (var m in members)
            {
                var a = after.Find(x => x.id == m.id);
                if (a == null || !SameExceptPlacement(m, a)) return false;
                var b0 = m.rotation.ToUnity();
                var a0 = a.rotation.ToUnity();
                if (first)
                {
                    turn = a0 * Quaternion.Inverse(b0);
                    move = a.position.ToUnity() - turn * m.position.ToUnity();
                    first = false;
                }
                if (Vector3.Distance(a.position.ToUnity(), turn * m.position.ToUnity() + move) > PositionTolerance) return false;
                if (Quaternion.Angle(a0, turn * b0) > AngleTolerance) return false;
            }
            return true;
        }

        static bool SameExceptPlacement(Piece a, Piece b)
        {
            if (a.shape != b.shape || a.color != b.color || a.mirrored != b.mirrored) return false;
            if ((a.cuts?.Count ?? 0) != (b.cuts?.Count ?? 0)) return false;
            if (!SamePaint(a, b)) return false;
            var p = a.p;
            var q = b.p;
            return Mathf.Approximately(p.width, q.width) && Mathf.Approximately(p.height, q.height) && Mathf.Approximately(p.length, q.length)
                && Mathf.Approximately(p.bevel, q.bevel) && Mathf.Approximately(p.taper, q.taper) && p.hollow == q.hollow
                && Mathf.Approximately(p.wall, q.wall) && p.segments == q.segments && (p.points?.Count ?? 0) == (q.points?.Count ?? 0);
        }

        static bool SamePaint(Piece a, Piece b)
        {
            int n = a.paint?.Count ?? 0;
            if (n != (b.paint?.Count ?? 0) || !Mathf.Approximately(a.paintCell, b.paintCell)) return false;
            for (int i = 0; i < n; i++)
                if (!a.paint[i].Equals(b.paint[i])) return false;
            return true;
        }

        // ---------- placing / copying ----------

        /// <summary>
        /// After <see cref="MirrorEdits.WithMirrorCopies"/>: the mirror copies of linked pieces become their own linked
        /// copy (the mirror image of the original copy), paired with it. Parts on the mirror line have no mirror piece,
        /// so the mirror copy skips them.
        /// </summary>
        public static void LinkMirrorCopies(IReadOnlyList<Piece> originals, IEnumerable<Piece> mirrorCopies)
        {
            var byId = originals.ToDictionary(p => p.id);
            var newIds = new Dictionary<string, string>();
            var copied = new Dictionary<string, HashSet<int>>();
            var made = new List<Piece>();
            foreach (var copy in mirrorCopies)
            {
                if (copy.link == null || copy.mirrorPartnerId == null || !byId.TryGetValue(copy.mirrorPartnerId, out var source)) continue;
                string key = source.link.instanceId;
                if (!newIds.TryGetValue(key, out var mirrorId)) newIds[key] = mirrorId = Ids.New();
                copy.link = MirrorLink(source.link, mirrorId);
                copy.link.partnerId = key;
                if (!copied.TryGetValue(key, out var parts)) copied[key] = parts = new HashSet<int>();
                parts.Add(source.link.part);
                made.Add(copy);
            }
            foreach (var p in originals)
                if (p.link != null && newIds.TryGetValue(p.link.instanceId, out var mirrorId)) p.link.partnerId = mirrorId;
            foreach (var copy in made)
            {
                var key = copy.link.partnerId;
                var skip = new HashSet<int>(copy.link.skip ?? new List<int>());
                skip.UnionWith(originals.Where(p => p.link?.instanceId == key && !copied[key].Contains(p.link.part)).Select(p => p.link.part));
                copy.link.skip = skip.Count > 0 ? skip.OrderBy(x => x).ToList() : null;
            }
        }

        /// <summary>
        /// Copies of <paramref name="pieces"/> measured from <paramref name="origin"/> (for the clipboard and for
        /// blueprints). Copies that are completely in the set stay linked; partly copied ones become normal pieces.
        /// </summary>
        public static List<Piece> CopyRelative(IEnumerable<Piece> pieces, BuildContent content, Vector3 origin)
        {
            var list = pieces.ToList();
            var ids = new HashSet<string>(list.Select(p => p.id));
            var copies = content != null ? Copies(content) : new Dictionary<string, List<Piece>>();
            var result = new List<Piece>();
            foreach (var p in list)
            {
                var c = p.Clone();
                c.position = (p.position.ToUnity() - origin).ToDoc();
                c.mirrorPartnerId = null;
                if (c.link != null)
                {
                    bool whole = copies.TryGetValue(c.link.instanceId, out var members) && members.All(m => ids.Contains(m.id));
                    if (whole)
                    {
                        c.link.position = (c.link.position.ToUnity() - origin).ToDoc();
                        c.link.partnerId = null;
                    }
                    else c.link = null;
                }
                result.Add(c);
            }
            return result;
        }

        /// <summary>
        /// Moves the links of stamped (pasted) pieces the same way as the pieces: optional flip across the stamp's
        /// own X = 0, scale, turn, move. Gives each pasted copy a new copy id.
        /// </summary>
        public static void PlaceLinks(IEnumerable<Piece> pieces, bool flipped, float scale, Quaternion rotation, Vector3 position)
        {
            var newIds = new Dictionary<string, string>();
            foreach (var p in pieces)
            {
                if (p.link == null) continue;
                var l = p.link;
                if (!newIds.TryGetValue(l.instanceId, out var id)) newIds[l.instanceId] = id = Ids.New();
                if (flipped) l = MirrorLink(l, l.instanceId);
                l.instanceId = id;
                l.partnerId = null;
                l.position = (position + rotation * (l.position.ToUnity() * scale)).ToDoc();
                l.rotation = (rotation * l.rotation.ToUnity()).ToDoc();
                l.scale *= scale;
                p.link = l;
            }
        }

        /// <summary>
        /// After saving pieces as a blueprint: those pieces become a linked copy of it (and their mirror partners,
        /// when they are an exact mirror image, become its mirror copy). Fills the before / after lists.
        /// </summary>
        public static void LinkSaved(IReadOnlyList<Piece> saved, BuildContent content, Blueprint bp, Vector3 origin, List<Piece> before, List<Piece> after)
        {
            string id = Ids.New();
            var link = new PieceLink { instanceId = id, blueprintId = bp.id, version = bp.version, position = origin.ToDoc() };
            var ids = new HashSet<string>(saved.Select(p => p.id));

            // Mirror partners outside the saved pieces that are exact mirror images.
            var partners = new Dictionary<int, Piece>();
            for (int i = 0; i < saved.Count; i++)
            {
                var partner = content.FindPiece(saved[i].mirrorPartnerId);
                if (partner == null || ids.Contains(partner.id)) continue;
                var mirror = MirrorEdits.MirrorOf(saved[i], partner.id);
                if (SameExceptPlacement(mirror, partner)
                    && Vector3.Distance(mirror.position.ToUnity(), partner.position.ToUnity()) < PositionTolerance
                    && Quaternion.Angle(mirror.rotation.ToUnity(), partner.rotation.ToUnity()) < AngleTolerance)
                    partners[i] = partner;
            }
            string mirrorId = partners.Count > 0 ? Ids.New() : null;
            link.partnerId = mirrorId;

            for (int i = 0; i < saved.Count; i++)
            {
                before.Add(saved[i].Clone());
                var a = saved[i].Clone();
                a.link = link.Clone();
                a.link.part = i;
                after.Add(a);
            }
            if (mirrorId == null) return;
            var mirrorLink = MirrorLink(link, mirrorId);
            mirrorLink.partnerId = id;
            var skip = Enumerable.Range(0, saved.Count).Where(i => !partners.ContainsKey(i)).ToList();
            mirrorLink.skip = skip.Count > 0 ? skip : null;
            foreach (var pair in partners)
            {
                before.Add(pair.Value.Clone());
                var a = pair.Value.Clone();
                a.link = mirrorLink.Clone();
                a.link.part = pair.Key;
                after.Add(a);
            }
        }
    }
}
