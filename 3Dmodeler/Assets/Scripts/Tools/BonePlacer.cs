using Builder.Core;
using Builder.UI;
using Builder.View;
using UnityEngine;

namespace Builder.Tools
{
    /// <summary>
    /// Placing bones with the "Rigger tube" item (the build tool calls this):
    ///   - Click 1 = the start joint (head), click 2 = the end joint (tail): the bone is made.
    ///   - Then the next bone starts at that end (a chain: Hips -> Spine -> Chest ...); right click stops the chain.
    ///   - Not in a chain: right click on a bone (tube or joint ball) deletes it.
    ///   - Starting on an existing bone's END joint makes the new bone its child; on its START joint, a brother
    ///     (same parent). Joint balls are easy to aim at while placing.
    ///   - The name is guessed from the parent (Hips, Spine, UpperArm.L ...) and can be changed in the bones panel.
    ///   - With mirror on, bones off the middle line get a mirror bone too (.L / .R).
    /// </summary>
    public class BonePlacer : MonoBehaviour
    {
        const float MinLength = 0.02f;

        [SerializeField] BuilderSession session;
        [SerializeField] BoneView view;

        Vector3? head;
        string parentId;

        public bool Pending => head != null;

        public void Click(Vector3 spot, BoneJointView joint)
        {
            var content = session.Document.content;
            if (joint != null && joint.IsTube) joint = null;   // the tube is not a joint: place at the aimed spot
            if (joint != null) spot = joint.transform.position;
            if (head == null)
            {
                head = spot;
                var aimed = joint != null ? content.FindBone(joint.BoneId) : null;
                parentId = aimed == null ? null : joint.IsTail ? aimed.id : aimed.parentId;
                Toast.Show("Start joint set - now click where this bone ends  (right click = stop)");
                return;
            }

            var tail = spot;
            if (Vector3.Distance(head.Value, tail) < MinLength)
            {
                Toast.Show("Click a bit further away for the end joint");
                return;
            }
            var parent = content.FindBone(parentId);
            var bone = new Bone
            {
                id = Ids.New(), parentId = parent?.id,
                name = BoneNames.Suggest(content, parent, (head.Value.x + tail.x) * 0.5f),
                head = head.Value.ToDoc(), tail = tail.ToDoc(),
            };
            session.AddBones(new[] { bone }, "Add bone " + bone.name);
            Toast.Show($"Bone \"{bone.name}\" added - click again for the next one, right click to stop.  Rename / bind: I, then K");
            head = tail;   // the next bone continues the chain
            parentId = bone.id;
        }

        /// <summary>Ends the chain (right click), or quietly when switching items / tools.</summary>
        public void Cancel(bool quiet = false)
        {
            if (head != null && !quiet) Toast.Show("Bone chain ended");
            head = null;
            parentId = null;
            if (view != null) view.HidePreview();
        }

        /// <summary>Shows the bone being placed, from its start joint to the aimed spot (null = nothing aimed).</summary>
        public void Preview(Vector3? spot)
        {
            if (head == null || spot == null) view.HidePreview();
            else view.ShowPreview(head.Value, spot.Value);
        }
    }
}
