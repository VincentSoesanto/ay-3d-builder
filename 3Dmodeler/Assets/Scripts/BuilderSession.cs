using System;
using System.Collections.Generic;
using Builder.Core;
using UnityEngine;

namespace Builder
{
    /// <summary>
    /// Holds the open project (the document) and its undo history. Tools send their edits here.
    /// There is one of these in the scene, on the "Builder" GameObject.
    ///
    /// Use <see cref="Place"/>, <see cref="Remove"/> and <see cref="Modify"/> for piece edits: they also do the
    /// mirror side when mirror building is on (all in the same undo step).
    /// </summary>
    public class BuilderSession : MonoBehaviour
    {
        public ProjectDocument Document { get; private set; }
        public UndoStack History { get; private set; }

        /// <summary>Raised after every edit, undo and redo, with what changed (the View listens).</summary>
        public event Action<ChangeSet> Changed;

        /// <summary>Raised when a different project was opened (or a new one started): redraw everything.</summary>
        public event Action Replaced;

        /// <summary>True when there are changes that are not saved yet.</summary>
        public bool Dirty { get; private set; }

        bool MirrorOn => Document.editor.mirrorX;

        void Awake()
        {
            // Open the project from last time, or start a new one.
            ProjectDocument doc = null;
            string last = IO.ProjectStore.LastId;
            if (IO.ProjectStore.Exists(last))
            {
                try { doc = IO.ProjectStore.Load(last); }
                catch (Exception e) { Debug.LogWarning("[Builder] Could not open the last project: " + e.Message); }
            }
            Use(doc ?? NewDocument("Untitled"));
        }

        public static ProjectDocument NewDocument(string name) =>
            ProjectDocument.CreateNew(name, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        /// <summary>Makes <paramref name="doc"/> the open project, with a fresh undo history.</summary>
        public void Use(ProjectDocument doc)
        {
            if (History != null) History.Changed -= OnHistoryChanged;
            Document = doc;
            History = new UndoStack(doc.content);
            History.Changed += OnHistoryChanged;
            Dirty = false;
            Replaced?.Invoke();
        }

        void OnHistoryChanged(ChangeSet changes)
        {
            Dirty = true;
            Changed?.Invoke(changes);
        }

        public void MarkSaved() => Dirty = false;

        /// <summary>For changes outside the undo history (e.g. the project's color palette).</summary>
        public void MarkDirty() => Dirty = true;

        public void Execute(ICommand command) => History.Execute(command);

        /// <summary>Adds new pieces (+ their mirror copies when mirror is on).</summary>
        public void Place(IEnumerable<Piece> pieces, string label)
        {
            var originals = new List<Piece>(pieces);
            var all = WithMirror(originals);
            // Mirror copies of pieces bound to a bone go on the mirror bone (Hand.L -> Hand.R), if there is one.
            for (int i = originals.Count; i < all.Count; i++)
                if (all[i].boneId != null)
                    all[i].boneId = Rig.MirrorPartner(Document.content.FindBone(all[i].boneId), Document.content)?.id;
            if (all.Count > 0) Execute(new AddPiecesCommand(all, label));
        }

        /// <summary>
        /// Adds new pieces together with new bones (a blueprint that has bones), as one undo step. With mirror on, the
        /// mirror copies of the pieces are bound to the mirror bones.
        /// </summary>
        public void Place(IEnumerable<Piece> pieces, string label, List<Bone> bones)
        {
            if (bones == null || bones.Count == 0)
            {
                Place(pieces, label);
                return;
            }
            var originals = new List<Piece>(pieces);
            var mirrorIds = new Dictionary<string, string>();
            var allBones = MirrorOn ? Rig.WithMirrorBones(bones, Document.content, mirrorIds) : bones;
            var all = WithMirror(originals);
            for (int i = originals.Count; i < all.Count; i++)
                if (all[i].boneId != null && mirrorIds.TryGetValue(all[i].boneId, out var mirrorBone)) all[i].boneId = mirrorBone;
            // Mirror bones of a linked copy belong to the mirror copy (its own instance id).
            var mirrorCopy = new Dictionary<string, string>();
            for (int i = originals.Count; i < all.Count; i++)
                if (all[i].link?.partnerId != null) mirrorCopy[all[i].link.partnerId] = all[i].link.instanceId;
            for (int i = bones.Count; i < allBones.Count; i++)
                if (allBones[i].copyId != null)
                    allBones[i].copyId = mirrorCopy.TryGetValue(allBones[i].copyId, out var mirrorId) ? mirrorId : null;
            Execute(new CompositeCommand(new ICommand[] { new BonesCommand(null, allBones, label), new AddPiecesCommand(all, label) }, label));
        }

        /// <summary>The new pieces + their mirror copies when mirror is on (a linked copy gets a linked mirror copy).</summary>
        List<Piece> WithMirror(IEnumerable<Piece> pieces)
        {
            var originals = new List<Piece>(pieces);
            if (!MirrorOn) return originals;
            var all = MirrorEdits.WithMirrorCopies(originals);
            LinkedCopies.LinkMirrorCopies(originals, all.GetRange(originals.Count, all.Count - originals.Count));
            return all;
        }

        /// <summary>Deletes pieces (+ their mirror partners when mirror is on).</summary>
        public void Remove(IEnumerable<string> ids, string label)
        {
            var all = MirrorOn ? MirrorEdits.WithPartners(ids, Document.content) : new HashSet<string>(ids);
            if (all.Count == 0) return;
            var parts = UnlinkRest(all, new List<Piece>(), new List<Piece>(), label);
            parts.Add(new RemovePiecesCommand(all, label));
            Execute(parts.Count == 1 ? parts[0] : new CompositeCommand(parts, label));
        }

        /// <summary>Linked copies that lose only some pieces get unlinked first (adds that edit to the list).</summary>
        List<ICommand> UnlinkRest(ICollection<string> removed, List<Piece> before, List<Piece> after, string label)
        {
            int n = LinkedCopies.ApplyRemoveRules(Document.content, removed, before, after);
            TellUnlinked(n);
            var parts = new List<ICommand>();
            if (after.Count > 0) parts.Add(new ModifyPiecesCommand(before, after, label));
            return parts;
        }

        static void TellUnlinked(int copies)
        {
            if (copies > 0)
                UI.Toast.Show(copies == 1 ? "That blueprint copy is now unlinked (edited on its own)"
                                          : $"{copies} blueprint copies are now unlinked (edited on their own)");
        }

        /// <summary>
        /// Eraser Cut mode, as one undo step: adds a cut to each piece in <paramref name="cuts"/> and removes the
        /// pieces in <paramref name="remove"/> (they were completely inside the eraser). Both sides of a mirror are
        /// already in the lists when mirror is on (the mirror eraser collected them).
        /// </summary>
        public void CutPieces(IReadOnlyDictionary<string, Cut> cuts, IReadOnlyCollection<string> remove, string label)
        {
            var parts = new List<ICommand>();
            var before = new List<Piece>();
            var after = new List<Piece>();
            foreach (var pair in cuts)
            {
                var piece = Document.content.FindPiece(pair.Key);
                if (piece == null) continue;
                before.Add(piece);
                var cut = piece.Clone();
                (cut.cuts ??= new List<Cut>()).Add(pair.Value.Clone());
                after.Add(cut);
            }
            int unlinked = LinkedCopies.ApplyEditRules(Document.content, before, after);
            unlinked += LinkedCopies.ApplyRemoveRules(Document.content, remove as ICollection<string> ?? new List<string>(remove), before, after);
            TellUnlinked(unlinked);
            if (after.Count > 0) parts.Add(new ModifyPiecesCommand(before, after, label));
            if (remove.Count > 0) parts.Add(new RemovePiecesCommand(remove, label));
            if (parts.Count > 0) Execute(new CompositeCommand(parts, label));
        }

        /// <summary>
        /// Removes some pieces and adds others as ONE undo step (e.g. points -> the solid built from them).
        /// With mirror on, the mirror partners are removed too and the new pieces get mirror copies.
        /// </summary>
        public void ReplacePieces(IReadOnlyCollection<string> remove, IEnumerable<Piece> add, string label)
        {
            var removeAll = MirrorOn ? MirrorEdits.WithPartners(remove, Document.content) : new HashSet<string>(remove);
            var addAll = WithMirror(add);
            var parts = UnlinkRest(removeAll, new List<Piece>(), new List<Piece>(), label);
            parts.Add(new RemovePiecesCommand(removeAll, label));
            parts.Add(new AddPiecesCommand(addAll, label));
            Execute(new CompositeCommand(parts, label));
        }

        /// <summary>Rebuilds linked blueprint copies (one undo step): removes their old pieces, adds the new ones.</summary>
        public void UpdateCopies(IReadOnlyCollection<string> remove, IEnumerable<Piece> add, string label,
                                 List<Bone> bonesBefore = null, List<Bone> bonesAfter = null)
        {
            var parts = new List<ICommand>();
            if (bonesBefore != null && (bonesBefore.Count > 0 || bonesAfter.Count > 0))
                parts.Add(new BonesCommand(bonesBefore, bonesAfter, label));
            parts.Add(new RemovePiecesCommand(remove, label));
            parts.Add(new AddPiecesCommand(add, label));
            Execute(new CompositeCommand(parts, label));
        }

        /// <summary>
        /// Swaps the frame parts (pieces marked <see cref="Piece.frame"/>) for <paramref name="frame"/>, as one undo
        /// step. An empty list just removes them.
        /// </summary>
        public void ReplaceFrame(List<Piece> frame, string label)
        {
            var old = Document.content.pieces.FindAll(p => p.frame).ConvertAll(p => p.id);
            if (old.Count == 0 && frame.Count == 0) return;
            var parts = new List<ICommand>();
            if (old.Count > 0) parts.Add(new RemovePiecesCommand(old, label));
            if (frame.Count > 0) parts.Add(new AddPiecesCommand(frame, label));
            Execute(new CompositeCommand(parts, label));
        }

        // ---------- bones (rigger tubes) ----------

        /// <summary>
        /// Adds new bones (+ their mirror bones when mirror is on: mirrored joints, .L/.R swapped, parented to the
        /// mirror of the parent). Returns the bones added.
        /// </summary>
        public List<Bone> AddBones(IEnumerable<Bone> bones, string label, IEnumerable<ICommand> also = null)
        {
            var all = MirrorOn ? Rig.WithMirrorBones(new List<Bone>(bones), Document.content) : new List<Bone>(bones);
            var parts = new List<ICommand> { new BonesCommand(null, all, label) };
            if (also != null) parts.AddRange(also);
            Execute(parts.Count == 1 ? parts[0] : new CompositeCommand(parts, label));
            return all;
        }

        /// <summary>Changes bones (pass changed copies; one undo step, plus any extra commands).</summary>
        public void ChangeBones(IEnumerable<Bone> before, IEnumerable<Bone> after, string label, IEnumerable<ICommand> also = null,
                                string mergeKey = null)
        {
            var parts = new List<ICommand> { new BonesCommand(before, after, label, mergeKey) };
            if (also != null) parts.AddRange(also);
            Execute(parts.Count == 1 ? parts[0] : new CompositeCommand(parts, label));
        }

        /// <summary>
        /// Deletes a bone: its child bones move up to its parent, pieces and groups bound to it become unbound.
        /// </summary>
        public void DeleteBone(string boneId)
        {
            var content = Document.content;
            var bone = content.FindBone(boneId);
            if (bone == null) return;
            var before = new List<Bone> { bone };
            var after = new List<Bone>();
            foreach (var child in content.bones.FindAll(b => b.parentId == boneId))
            {
                before.Add(child);
                var c = child.Clone();
                c.parentId = bone.parentId;
                after.Add(c);
            }
            var also = new List<ICommand>();
            var pb = content.pieces.FindAll(p => p.boneId == boneId);
            if (pb.Count > 0)
                also.Add(new ModifyPiecesCommand(pb, pb.ConvertAll(p => { var c = p.Clone(); c.boneId = null; return c; }), "Unbind"));
            foreach (var g in content.groups.FindAll(g => g.boneId == boneId))
            {
                var c = g.Clone();
                c.boneId = null;
                also.Add(new GroupCommand(g, c, "Unbind"));
            }
            ChangeBones(before, after, "Delete bone " + bone.name, also);
        }

        /// <summary>
        /// Puts a whole new skeleton in place of the old one (one undo step): pieces / groups bound to bones that are gone
        /// become unbound, then (autoBind) every loose piece is bound to its nearest bone.
        /// <paramref name="groupBones"/>: groups to bind to the new bones (from "bones from groups").
        /// </summary>
        public void ReplaceSkeleton(List<Bone> bones, string label, bool autoBind, List<(Group before, Group after)> groupBones = null)
        {
            var content = Document.content;
            var keep = new HashSet<string>();
            foreach (var b in bones) keep.Add(b.id);

            var groupsAfter = content.groups.ConvertAll(g =>
            {
                var set = groupBones?.Find(x => x.before.id == g.id).after;
                var c = (set ?? g).Clone();
                if (c.boneId != null && !keep.Contains(c.boneId)) c.boneId = null;
                return c;
            });
            var piecesAfter = content.pieces.ConvertAll(p =>
            {
                var c = p.Clone();
                if (c.boneId != null && !keep.Contains(c.boneId)) c.boneId = null;
                return c;
            });
            if (autoBind)
            {
                var future = new BuildContent { pieces = piecesAfter, groups = groupsAfter, bones = bones };
                foreach (var (pieceId, boneId) in RigTools.AutoBind(future, onlyUnbound: true))
                    piecesAfter.Find(x => x.id == pieceId).boneId = boneId;
            }

            var parts = new List<ICommand> { new BonesCommand(content.bones, bones, label) };
            var before = new List<Piece>();
            var after = new List<Piece>();
            for (int i = 0; i < content.pieces.Count; i++)
                if (content.pieces[i].boneId != piecesAfter[i].boneId)
                {
                    before.Add(content.pieces[i]);
                    after.Add(piecesAfter[i]);
                }
            if (after.Count > 0) parts.Add(new ModifyPiecesCommand(before, after, label));
            for (int i = 0; i < content.groups.Count; i++)
                if (content.groups[i].boneId != groupsAfter[i].boneId)
                    parts.Add(new GroupCommand(content.groups[i], groupsAfter[i], label));
            Execute(new CompositeCommand(parts, label));
        }

        /// <summary>Binds every piece to its nearest bone (onlyUnbound: just the loose ones). Returns how many changed.</summary>
        public int AutoBind(bool onlyUnbound)
        {
            var changes = RigTools.AutoBind(Document.content, onlyUnbound);
            var before = new List<Piece>();
            var after = new List<Piece>();
            foreach (var (pieceId, boneId) in changes)
            {
                var p = Document.content.FindPiece(pieceId);
                before.Add(p.Clone());
                var c = p.Clone();
                c.boneId = boneId;
                after.Add(c);
            }
            ModifyExactly(before, after, "Auto-bind");
            return after.Count;
        }

        /// <summary>Binds pieces to a bone (null = unbind). Exactly these pieces, one undo step.</summary>
        public void BindPieces(IReadOnlyList<string> pieceIds, string boneId, string label)
        {
            var before = new List<Piece>();
            var after = new List<Piece>();
            foreach (var id in pieceIds)
            {
                var p = Document.content.FindPiece(id);
                if (p == null || p.boneId == boneId) continue;
                before.Add(p.Clone());
                var c = p.Clone();
                c.boneId = boneId;
                after.Add(c);
            }
            ModifyExactly(before, after, label);
        }

        /// <summary>Changes pieces exactly as given: no mirror side, no linked-copy rules (e.g. linking saved pieces).</summary>
        public void ModifyExactly(List<Piece> before, List<Piece> after, string label)
        {
            if (after.Count > 0) Execute(new ModifyPiecesCommand(before, after, label));
        }

        // ---------- groups ----------

        /// <summary>Names offered for new groups, in order (the usual mech parts).</summary>
        public static readonly string[] GroupNames = { "Body", "Head", "LeftArm", "RightArm", "LeftLeg", "RightLeg", "Hips", "Backpack" };

        /// <summary>The first preset name not used yet, else "Group N".</summary>
        public string NextGroupName()
        {
            var groups = Document.content.groups;
            foreach (var name in GroupNames)
                if (!groups.Exists(g => g.name == name)) return name;
            int n = groups.Count + 1;
            while (groups.Exists(g => g.name == "Group " + n)) n++;
            return "Group " + n;
        }

        /// <summary>Makes a new group with the given pieces in it. Its pivot starts at the pieces' bottom-middle.</summary>
        public Group CreateGroup(string name, IReadOnlyList<string> pieceIds, Vec3 pivot)
        {
            var group = new Group { id = Ids.New(), name = name, pivot = pivot };
            Execute(new GroupCommand(null, group, "New group " + name, Moves(pieceIds, group.id)));
            return group;
        }

        /// <summary>Deletes a group; its pieces stay, but become ungrouped.</summary>
        public void DeleteGroup(string groupId)
        {
            var group = Document.content.FindGroup(groupId);
            if (group == null) return;
            var members = Document.content.pieces.FindAll(p => p.groupId == groupId).ConvertAll(p => p.id);
            Execute(new GroupCommand(group, null, "Delete group " + group.name, Moves(members, null)));
        }

        /// <summary>Changes a group's name / pivot / visibility (pass a changed copy).</summary>
        public void ChangeGroup(Group changed, string label)
        {
            var current = Document.content.FindGroup(changed.id);
            if (current != null) Execute(new GroupCommand(current, changed, label));
        }

        /// <summary>Puts pieces into a group (null = ungrouped).</summary>
        public void AssignToGroup(IReadOnlyList<string> pieceIds, string groupId)
        {
            var group = Document.content.FindGroup(groupId);
            if (group == null && groupId != null) return;
            var moves = Moves(pieceIds, groupId);
            if (moves.Count == 0) return;
            Execute(new GroupCommand(group, group, group != null ? "Add to " + group.name : "Ungroup", moves));
        }

        List<(string, string, string)> Moves(IEnumerable<string> pieceIds, string toGroup)
        {
            var moves = new List<(string, string, string)>();
            foreach (var id in pieceIds)
            {
                var p = Document.content.FindPiece(id);
                if (p != null && p.groupId != toGroup) moves.Add((id, p.groupId, toGroup));
            }
            return moves;
        }

        /// <summary>
        /// Changes pieces (+ their mirror partners when mirror is on). Edits with the same
        /// <paramref name="mergeKey"/> in a row (a drag) become one undo step until the mouse is released.
        /// </summary>
        public void Modify(IEnumerable<Piece> before, IEnumerable<Piece> after, string label, string mergeKey = null)
        {
            var b = new List<Piece>(before);
            var a = new List<Piece>(after);
            if (MirrorOn) MirrorEdits.AddPartnerEdits(b, a, Document.content);
            TellUnlinked(LinkedCopies.ApplyEditRules(Document.content, b, a));
            Execute(new ModifyPiecesCommand(b, a, label, mergeKey));
        }
    }
}
