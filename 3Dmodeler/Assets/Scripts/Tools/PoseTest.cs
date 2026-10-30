using System;
using System.Collections.Generic;
using Builder.Core;
using Builder.View;
using UnityEngine;

namespace Builder.Tools
{
    /// <summary>
    /// Pose test (bones panel): turn bones to check how the model moves and where parts bump into each other.
    /// Only the picture moves: the document (and the saved / exported model) stays in its rest pose.
    /// Bound pieces follow their bone; a bone's children follow it too. Turning off = back to the rest pose.
    /// </summary>
    public class PoseTest : MonoBehaviour
    {
        [SerializeField] BuilderSession session;
        [SerializeField] DocumentView documentView;

        readonly Dictionary<string, Vector3> angles = new Dictionary<string, Vector3>();
        Dictionary<string, Matrix4x4> matrices;
        int matricesFrame = -1;

        public bool Active { get; private set; }

        BuildContent Content => session.Document.content;

        void Start() => session.Replaced += () => SetActive(false);

        public void SetActive(bool on)
        {
            if (on == Active) return;
            Active = on;
            angles.Clear();
            matricesFrame = -1;
            if (!on) ShowRestPose();
        }

        /// <summary>A bone's turn in degrees (X, Y, Z).</summary>
        public Vector3 Angles(string boneId) => boneId != null && angles.TryGetValue(boneId, out var a) ? a : Vector3.zero;

        /// <summary>Sets one axis of a bone's turn (kept inside its limits).</summary>
        public void SetAngle(string boneId, int axis, float degrees)
        {
            var bone = Content.FindBone(boneId);
            if (bone == null) return;
            var a = Angles(boneId);
            a[axis] = Rig.Clamp(bone, axis, degrees);
            angles[boneId] = a;
            matricesFrame = -1;
        }

        /// <summary>All bones back to straight (the rest pose).</summary>
        public void ResetPose()
        {
            angles.Clear();
            matricesFrame = -1;
        }

        /// <summary>This frame's pose matrix of each bone.</summary>
        public Dictionary<string, Matrix4x4> Matrices()
        {
            if (matricesFrame != Time.frameCount || matrices == null)
            {
                matrices = Rig.PoseMatrices(Content, Angles);
                matricesFrame = Time.frameCount;
            }
            return matrices;
        }

        void LateUpdate()
        {
            if (!Active) return;
            var m = Matrices();
            foreach (var p in Content.pieces)
            {
                var view = documentView.Find(p.id);
                if (view == null) continue;
                string bone = Content.BoneOf(p);
                if (bone != null && m.TryGetValue(bone, out var pose))
                    view.transform.SetPositionAndRotation(pose.MultiplyPoint3x4(p.position.ToUnity()), pose.rotation * p.rotation.ToUnity());
                else view.transform.SetPositionAndRotation(p.position.ToUnity(), p.rotation.ToUnity());
            }
        }

        void ShowRestPose()
        {
            if (session == null || session.Document == null) return;
            foreach (var p in Content.pieces)
            {
                var view = documentView.Find(p.id);
                if (view != null) view.transform.SetPositionAndRotation(p.position.ToUnity(), p.rotation.ToUnity());
            }
        }
    }
}
