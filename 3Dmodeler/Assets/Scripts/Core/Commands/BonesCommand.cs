using System.Collections.Generic;

namespace Builder.Core
{
    /// <summary>
    /// One undoable change to bones (rigger tubes): add, delete or change any number of them.
    /// A bone only in <c>before</c> gets deleted, only in <c>after</c> gets added, in both gets changed.
    /// </summary>
    public class BonesCommand : IMergeableCommand
    {
        readonly List<Bone> before = new List<Bone>();
        List<Bone> after = new List<Bone>();
        readonly string mergeKey;

        public string Label { get; }

        /// <param name="mergeKey">Edits with the same key in a row (dragging a joint) become one undo step.</param>
        public BonesCommand(IEnumerable<Bone> before, IEnumerable<Bone> after, string label, string mergeKey = null)
        {
            if (before != null) foreach (var b in before) this.before.Add(b.Clone());
            if (after != null) foreach (var b in after) this.after.Add(b.Clone());
            Label = label;
            this.mergeKey = mergeKey;
        }

        public bool TryMerge(ICommand next)
        {
            if (mergeKey == null || !(next is BonesCommand m) || m.mergeKey != mergeKey) return false;
            var ids = new HashSet<string>();
            foreach (var b in after) ids.Add(b.id);
            if (!ids.SetEquals(m.after.ConvertAll(b => b.id))) return false;
            after = m.after.ConvertAll(b => b.Clone());
            return true;
        }

        public ChangeSet Do(BuildContent content) => Apply(content, before, after);
        public ChangeSet Undo(BuildContent content) => Apply(content, after, before);

        static ChangeSet Apply(BuildContent content, List<Bone> from, List<Bone> to)
        {
            var changes = new ChangeSet();
            var keep = new HashSet<string>();
            foreach (var b in to) keep.Add(b.id);
            foreach (var b in from)
            {
                changes.boneIds.Add(b.id);
                if (!keep.Contains(b.id)) content.bones.RemoveAll(x => x.id == b.id);
            }
            foreach (var b in to)
            {
                changes.boneIds.Add(b.id);
                int index = content.bones.FindIndex(x => x.id == b.id);
                if (index >= 0) content.bones[index] = b.Clone();
                else content.bones.Add(b.Clone());
            }
            return changes;
        }
    }

    /// <summary>The preset bone names (from the spec) and the next name to suggest for a new bone.</summary>
    public static class BoneNames
    {
        public static readonly string[] Presets =
        {
            "Hips", "Spine", "Chest", "Neck", "Head",
            "Shoulder.L", "UpperArm.L", "LowerArm.L", "Hand.L",
            "Shoulder.R", "UpperArm.R", "LowerArm.R", "Hand.R",
            "UpperLeg.L", "LowerLeg.L", "Foot.L",
            "UpperLeg.R", "LowerLeg.R", "Foot.R",
        };

        /// <summary>"Hand.L" &lt;-&gt; "Hand.R" (names without a side stay the same).</summary>
        public static string Mirror(string name)
        {
            if (name == null) return null;
            if (name.EndsWith(".L")) return name.Substring(0, name.Length - 2) + ".R";
            if (name.EndsWith(".R")) return name.Substring(0, name.Length - 2) + ".L";
            return name;
        }

        /// <summary>
        /// A good name for a new bone, from its parent: no parent = Hips, Hips -> Spine (then legs), Spine -> Chest,
        /// Chest -> Neck (then shoulders), Neck -> Head, Shoulder -> UpperArm -> LowerArm -> Hand,
        /// UpperLeg -> LowerLeg -> Foot. <paramref name="x"/> picks the side: facing +Z the model's left is at -X, so
        /// X &lt;= 0 = .L, else .R.
        /// Falls back to "Bone N"; never returns a name that is already used.
        /// </summary>
        public static string Suggest(BuildContent content, Bone parent, float x)
        {
            string side = x <= 0f ? ".L" : ".R";
            string p = parent?.name ?? "";
            string baseName = p.Length >= 2 && (p.EndsWith(".L") || p.EndsWith(".R")) ? p.Substring(0, p.Length - 2) : p;
            var candidates = new List<string>();
            if (parent == null) candidates.Add("Hips");
            else if (p == "Hips") { candidates.Add("Spine"); candidates.Add("UpperLeg" + side); }
            else if (p == "Spine") candidates.Add("Chest");
            else if (p == "Chest") { candidates.Add("Neck"); candidates.Add("Shoulder" + side); }
            else if (p == "Neck") candidates.Add("Head");
            else if (baseName == "Shoulder") candidates.Add("UpperArm" + p.Substring(p.Length - 2));
            else if (baseName == "UpperArm") candidates.Add("LowerArm" + p.Substring(p.Length - 2));
            else if (baseName == "LowerArm") candidates.Add("Hand" + p.Substring(p.Length - 2));
            else if (baseName == "UpperLeg") candidates.Add("LowerLeg" + p.Substring(p.Length - 2));
            else if (baseName == "LowerLeg") candidates.Add("Foot" + p.Substring(p.Length - 2));

            foreach (var c in candidates)
                if (!content.bones.Exists(b => b.name == c)) return c;
            int n = content.bones.Count + 1;
            while (content.bones.Exists(b => b.name == "Bone " + n)) n++;
            return "Bone " + n;
        }
    }
}
