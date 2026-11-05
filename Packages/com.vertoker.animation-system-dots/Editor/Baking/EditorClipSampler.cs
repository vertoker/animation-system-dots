using System;
using System.Linq;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimationSystemDOTS.Editor.Baking
{
    // Independent from the PlayableGraph oracle: direct native clip sampling on a private hierarchy.
    internal sealed class EditorClipSampler : IDisposable
    {
        Scene scene;
        GameObject instance;
        public AnimationClip Clip { get; private set; }
        public EditorCurveBinding[] Bindings { get; private set; }
        public string RootNormalizationIdentity { get; private set; } = "";
        Transform[] bones;
        Vector3[] restPositions;
        Quaternion[] restRotations;
        Vector3[] restScales;
        Matrix4x4 rootInverse;
        bool rootAnimated;

        public EditorClipSampler(GameObject source, AnimationClip clip, BlobAssetReference<RigDefinitionBlob> rig)
        {
            scene = EditorSceneManager.NewPreviewScene();
            try
            {
                instance = UnityEngine.Object.Instantiate(source, new InstantiateParameters { scene = scene });
                instance.hideFlags = HideFlags.HideAndDontSave;
                var animator = instance.GetComponent<Animator>();
                if (animator != null) { animator.runtimeAnimatorController = null; animator.applyRootMotion = false; animator.fireEvents = false; }
                Clip = UnityEngine.Object.Instantiate(clip);
                Clip.name = clip.name; Clip.hideFlags = HideFlags.HideAndDontSave;
                Bindings = AnimationUtility.GetCurveBindings(Clip);
                if (AnimationUtility.GetObjectReferenceCurveBindings(Clip).Length != 0)
                    throw new ArgumentException(clip.name + ": object-reference curves are unsupported.");
                if (AnimationUtility.GetAnimationEvents(Clip).Length != 0)
                    throw new ArgumentException(clip.name + ": animation event baking is pending stage 7.");
                bones = new Transform[rig.Value.Paths.Length];
                for (var i = 0; i < bones.Length; i++)
                {
                    var path = rig.Value.Paths[i].ToString();
                    bones[i] = i == 0 ? instance.transform : instance.transform.Find(path);
                    if (bones[i] == null) throw new ArgumentException(clip.name + ": missing rig bone " + path);
                }
                restPositions = bones.Select(b => b.localPosition).ToArray();
                restRotations = bones.Select(b => b.localRotation).ToArray();
                restScales = bones.Select(b => b.localScale).ToArray();
                rootInverse = Matrix4x4.TRS(restPositions[0], restRotations[0], restScales[0]).inverse;
                rootAnimated = Bindings.Any(b => string.IsNullOrEmpty(b.path));
                if (rootAnimated)
                {
                    var p = restPositions[0]; var r = restRotations[0]; var s = restScales[0];
                    RootNormalizationIdentity = "|root-basis:" + string.Join(",", new[]
                    {
                        p.x, p.y, p.z, r.x, r.y, r.z, r.w, s.x, s.y, s.z
                    }.Select(BakeContentKey.Float));
                }
                // Native SampleAnimation supplies defaults for missing vector channels, rather
                // than our rig's rest values. Complete the private clip's TRS bindings explicitly.
                CompleteRestBindings(rig);
            }
            catch { Dispose(); throw; }
        }

        public BoneLocalPose[] Sample(double time)
        {
            for (var bone = 0; bone < bones.Length; bone++)
            {
                bones[bone].localPosition = restPositions[bone];
                bones[bone].localRotation = restRotations[bone];
                bones[bone].localScale = restScales[bone];
            }
            Clip.SampleAnimation(instance, (float)time);
            var result = new BoneLocalPose[bones.Length];
            for (var bone = 0; bone < bones.Length; bone++)
            {
                result[bone] = new BoneLocalPose { Position = bones[bone].localPosition, Rotation = bones[bone].localRotation, Scale = bones[bone].localScale };
                if (bone == 0)
                {
                    if (!rootAnimated) result[bone] = BoneLocalPose.Identity;
                    else
                    {
                        var relative = rootInverse * Matrix4x4.TRS(bones[0].localPosition, bones[0].localRotation, bones[0].localScale);
                        var scale = bones[0].localScale;
                        var rootPose = new BoneLocalPose
                        {
                            Position = new float3(relative.m03, relative.m13, relative.m23),
                            Rotation = Quaternion.Inverse(restRotations[0]) * bones[0].localRotation,
                            Scale = new float3(scale.x / restScales[0].x, scale.y / restScales[0].y, scale.z / restScales[0].z)
                        };
                        var reconstructed = Matrix4x4.TRS(rootPose.Position, rootPose.Rotation, rootPose.Scale);
                        for (var row = 0; row < 4; row++) for (var col = 0; col < 4; col++)
                            if (Mathf.Abs(relative[row, col] - reconstructed[row, col]) > 1e-4f)
                                throw new ArgumentException(Clip.name + ": <root>: animated root relative to nonuniform scale contains shear and cannot be represented as local TRS.");
                        result[bone] = rootPose;
                    }
                }
                if (!PoseValidation.IsValid(result[bone]))
                    throw new ArgumentException(Clip.name + ": " + bones[bone].name + ": invalid sampled TRS at time " + time);
            }
            return result;
        }

        void CompleteRestBindings(BlobAssetReference<RigDefinitionBlob> rig)
        {
            var bound = new System.Collections.Generic.HashSet<string>(Bindings.Select(b => b.path + "|" + b.propertyName));
            for (var bone = 0; bone < bones.Length; bone++)
            {
                var path = rig.Value.Paths[bone].ToString();
                for (var axis = 0; axis < 3; axis++)
                {
                    var suffix = "." + "xyz"[axis];
                    Complete(path, "m_LocalPosition" + suffix, restPositions[bone][axis], bound);
                    Complete(path, "m_LocalScale" + suffix, restScales[bone][axis], bound);
                }
                var eulerBinding = Bindings.FirstOrDefault(b => b.path == path && b.propertyName.StartsWith("localEulerAngles", StringComparison.Ordinal));
                if (!string.IsNullOrEmpty(eulerBinding.propertyName))
                {
                    var prefix = eulerBinding.propertyName.Substring(0, eulerBinding.propertyName.LastIndexOf('.') + 1);
                    var euler = restRotations[bone].eulerAngles;
                    for (var axis = 0; axis < 3; axis++) Complete(path, prefix + "xyz"[axis], euler[axis], bound);
                }
                else
                    for (var axis = 0; axis < 4; axis++)
                        Complete(path, "m_LocalRotation." + "xyzw"[axis], restRotations[bone][axis], bound);
            }
        }

        void Complete(string path, string property, float value, System.Collections.Generic.HashSet<string> bound)
        {
            if (bound.Contains(path + "|" + property)) return;
            AnimationUtility.SetEditorCurve(Clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), new AnimationCurve(new Keyframe(0, value)));
        }

        public void Dispose()
        {
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            if (Clip != null) UnityEngine.Object.DestroyImmediate(Clip);
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
