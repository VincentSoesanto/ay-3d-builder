using System.Collections.Generic;

namespace Builder.Core
{
    /// <summary>Several commands done together as ONE undo step (e.g. cutting some pieces and removing others).</summary>
    public class CompositeCommand : ICommand
    {
        readonly List<ICommand> parts;

        public string Label { get; }

        public CompositeCommand(IEnumerable<ICommand> parts, string label)
        {
            this.parts = new List<ICommand>(parts);
            Label = label;
        }

        public ChangeSet Do(BuildContent content)
        {
            var all = new ChangeSet();
            foreach (var c in parts) Merge(all, c.Do(content));
            return all;
        }

        public ChangeSet Undo(BuildContent content)
        {
            var all = new ChangeSet();
            for (int i = parts.Count - 1; i >= 0; i--) Merge(all, parts[i].Undo(content));   // undo in reverse order
            return all;
        }

        static void Merge(ChangeSet into, ChangeSet from)
        {
            into.pieceIds.UnionWith(from.pieceIds);
            into.groupIds.UnionWith(from.groupIds);
            into.boneIds.UnionWith(from.boneIds);
        }
    }
}
