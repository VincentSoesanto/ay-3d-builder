using System.Collections.Generic;
using UnityEngine;

namespace AY3DBuilder
{
    /// <summary>
    /// For your Unity game: makes a mech exported from AY's 3D Builder (.glb, loaded with glTFast) play HUMANOID
    /// animations - for example walk / run / punch clips from Mixamo - while its parts stay hard pieces that turn at
    /// the joints (no rubber bending).
    ///
    /// How: put this component on the mech (the object you dragged into the scene). At start it finds the bones by
    /// their names (Hips, Spine, Chest, Neck, Head, Shoulder / UpperArm / LowerArm / Hand .L .R, UpperLeg / LowerLeg /
    /// Foot .L .R), builds a Humanoid "Avatar" for them (with the arms out to the sides, the T-pose Unity expects) and
    /// gives the Animator your controller. Left / right is checked by where the bones are, so older skeletons work too.
    ///
    /// Needs at least: Hips, Spine, Head, and both arms (UpperArm, LowerArm, Hand) and legs (UpperLeg, LowerLeg, Foot).
    /// The "Biped" skeleton template in AY's 3D Builder has all of them.
    /// </summary>
    [DisallowMultipleComponent]
    public class AYMechHumanoid : MonoBehaviour
    {
        [Tooltip("An Animator Controller with Humanoid animations (e.g. Mixamo clips set to Rig > Humanoid).")]
        public RuntimeAnimatorController controller;

        [Tooltip("Unity's Humanoid system expects a T-pose (arms straight out). Leave on unless your arms already point out.")]
        public bool armsOutForTPose = true;

        /// <summary>The Humanoid avatar that was built (null until Setup worked).</summary>
        public Avatar Avatar { get; private set; }

        /// <summary>What went wrong, in plain words (empty when it worked).</summary>
        public string Problem { get; private set; } = "";

        void Awake() => Setup();

        // Our bone names -> Unity's human bones (".L" / ".R" become Left / Right below).
        static readonly (string ours, HumanBodyBones left, HumanBodyBones right)[] Sided =
        {
            ("Shoulder", HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder),
            ("UpperArm", HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm),
            ("LowerArm", HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm),
            ("Hand", HumanBodyBones.LeftHand, HumanBodyBones.RightHand),
            ("UpperLeg", HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg),
            ("LowerLeg", HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg),
            ("Foot", HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot),
            ("Toe", HumanBodyBones.LeftToes, HumanBodyBones.RightToes),
        };

        static readonly (string ours, HumanBodyBones bone)[] Middle =
        {
            ("Hips", HumanBodyBones.Hips), ("Spine", HumanBodyBones.Spine), ("Chest", HumanBodyBones.Chest),
            ("Neck", HumanBodyBones.Neck), ("Head", HumanBodyBones.Head),
        };

        static readonly HumanBodyBones[] Required =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
        };

        /// <summary>Builds the avatar and sets up the Animator. Returns false (and fills Problem) if it can't.</summary>
        public bool Setup()
        {
            var bones = FindBones();
            var missing = new List<string>();
            foreach (var b in Required)
                if (!bones.ContainsKey(b)) missing.Add(b.ToString());
            if (missing.Count > 0)
            {
                Problem = "Missing bones: " + string.Join(", ", missing) + " (use the Biped skeleton in AY's 3D Builder)";
                Debug.LogWarning("[AYMechHumanoid] " + Problem, this);
                return false;
            }

            var description = new HumanDescription
            {
                skeleton = Skeleton(bones),
                human = Human(bones),
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
            };
            Avatar = AvatarBuilder.BuildHumanAvatar(gameObject, description);
            if (Avatar == null || !Avatar.isValid || !Avatar.isHuman)
            {
                Problem = "Unity could not build a Humanoid avatar from these bones (see the Console)";
                Debug.LogWarning("[AYMechHumanoid] " + Problem, this);
                return false;
            }
            Avatar.name = name + " Avatar";

            var animator = GetComponent<Animator>();
            if (animator == null) animator = gameObject.AddComponent<Animator>();
            animator.avatar = Avatar;
            animator.applyRootMotion = false;
            if (controller != null) animator.runtimeAnimatorController = controller;
            animator.Rebind();
            Problem = "";
            return true;
        }

        /// <summary>Finds our bones under this object. Left / right decided by position (facing +Z, left = -X).</summary>
        Dictionary<HumanBodyBones, Transform> FindBones()
        {
            var byName = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(t.name)) byName[t.name] = t;

            var result = new Dictionary<HumanBodyBones, Transform>();
            foreach (var (ours, bone) in Middle)
                if (byName.TryGetValue(ours, out var t)) result[bone] = t;

            // Arms and legs: which of the .L / .R chains is really on the left (-X, seen from this object)?
            bool SwapChain(string first)
            {
                if (!byName.TryGetValue(first + ".L", out var l) || !byName.TryGetValue(first + ".R", out var r)) return false;
                return transform.InverseTransformPoint(l.position).x > transform.InverseTransformPoint(r.position).x;
            }
            bool swapArms = SwapChain("UpperArm"), swapLegs = SwapChain("UpperLeg");
            foreach (var (ours, left, right) in Sided)
            {
                bool swap = ours.Contains("Leg") || ours == "Foot" || ours == "Toe" ? swapLegs : swapArms;
                if (byName.TryGetValue(ours + ".L", out var l)) result[swap ? right : left] = l;
                if (byName.TryGetValue(ours + ".R", out var r)) result[swap ? left : right] = r;
            }
            return result;
        }

        static HumanBone[] Human(Dictionary<HumanBodyBones, Transform> bones)
        {
            var list = new List<HumanBone>();
            foreach (var pair in bones)
            {
                var hb = new HumanBone { humanName = HumanTrait.BoneName[(int)pair.Key], boneName = pair.Value.name };
                hb.limit.useDefaultValues = true;
                list.Add(hb);
            }
            return list.ToArray();
        }

        /// <summary>
        /// Every object from this one down, with its rest position - and the arms turned straight out to the sides
        /// (the T-pose Unity measures animations against).
        /// </summary>
        SkeletonBone[] Skeleton(Dictionary<HumanBodyBones, Transform> bones)
        {
            // Rotations in this object's space: rest = as built; T-pose = arms out.
            var rest = new Dictionary<Transform, Quaternion>();
            var tpose = new Dictionary<Transform, Quaternion>();
            var all = GetComponentsInChildren<Transform>(true);
            foreach (var t in all) rest[t] = Quaternion.Inverse(transform.rotation) * t.rotation;

            var arms = new Dictionary<Transform, Vector3>();   // arm bone -> the way it must point
            void Arm(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand, Vector3 side)
            {
                if (bones.TryGetValue(upper, out var u) && bones.TryGetValue(lower, out var l) && bones.TryGetValue(hand, out var h))
                {
                    arms[u] = side;
                    arms[l] = side;
                }
            }
            if (armsOutForTPose)
            {
                Arm(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, Vector3.left);
                Arm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, Vector3.right);
            }

            Transform ChildOnChain(Transform t)
            {
                foreach (var pair in bones)
                    if (pair.Value.parent == t && arms.ContainsKey(t) &&
                        (pair.Key == HumanBodyBones.LeftLowerArm || pair.Key == HumanBodyBones.RightLowerArm ||
                         pair.Key == HumanBodyBones.LeftHand || pair.Key == HumanBodyBones.RightHand))
                        return pair.Value;
                return null;
            }

            var skeleton = new List<SkeletonBone>();
            foreach (var t in all)   // parents come before their children
            {
                var parentT = t == transform ? null : t.parent;
                var parentPose = parentT != null && tpose.TryGetValue(parentT, out var pp) ? pp : Quaternion.identity;
                var parentRest = parentT != null && rest.TryGetValue(parentT, out var pr) ? pr : Quaternion.identity;
                Quaternion pose;
                var child = ChildOnChain(t);
                if (arms.TryGetValue(t, out var side) && child != null)
                {
                    var dir = transform.InverseTransformDirection(child.position - t.position);
                    pose = Quaternion.FromToRotation(dir, side) * rest[t];
                }
                else
                {
                    // Keeps its rest turn relative to its parent (the hand follows the lower arm, etc.).
                    pose = parentPose * (Quaternion.Inverse(parentRest) * rest[t]);
                }
                tpose[t] = pose;
                skeleton.Add(new SkeletonBone
                {
                    name = t.name,
                    position = t.localPosition,
                    rotation = t == transform ? t.localRotation : Quaternion.Inverse(parentPose) * pose,
                    scale = t.localScale,
                });
            }
            return skeleton.ToArray();
        }
    }
}
