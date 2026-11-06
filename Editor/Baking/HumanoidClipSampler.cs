using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace AnimationSystemDOTS.Editor.Baking
{
    // The isolated target supplies legacy local TRS, or authored muscle/body
    // inputs for the precise runtime adapter. Source scene placement is not
    // part of either pose representation; the entity owns that transform.
    internal sealed class HumanoidClipSampler : IEditorClipSampler
    {
        Scene scene;
        GameObject instance;
        PlayableGraph graph;
        AnimationClipPlayable playable;
        Transform[] bones;
        BoneLocalPose[] rest;
        HumanPoseHandler humanReader;
        HumanPose humanRead;
        AnimationCurve[] muscleCurves;
        public AnimationClip Clip { get; private set; }
        public EditorCurveBinding[] Bindings { get; private set; }
        public string RootNormalizationIdentity { get; private set; }
        public bool IsLooping { get; private set; }

        public HumanoidClipSampler(GameObject source, AnimationClip clip, BlobAssetReference<RigDefinitionBlob> rig)
        {
            HumanoidRigValidator.Validate(source);
            scene = EditorSceneManager.NewPreviewScene();
            try
            {
                RootNormalizationIdentity = HumanoidRigValidator.Identity(source);
                RootNormalizationIdentity += "|authoring-root-normalized-1";
                instance = UnityEngine.Object.Instantiate(source, new InstantiateParameters { scene = scene });
                instance.hideFlags = HideFlags.HideAndDontSave; instance.SetActive(true);
                // GetHumanPose reports normalized world-space COM. Bake in the
                // canonical root frame so authoring position/rotation/scale are
                // not embedded a second time into the entity's local pose.
                instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;
                Clip = UnityEngine.Object.Instantiate(clip); Clip.name = clip.name; Clip.hideFlags = HideFlags.HideAndDontSave;
                // Bake the endpoint of one cycle, not the wrapped first pose.
                // Playback owns wrapping; retaining it here corrupts interpolation
                // throughout the last sample interval when the source is discontinuous.
                var clipSettings = AnimationUtility.GetAnimationClipSettings(Clip); IsLooping = clipSettings.loopTime;
                RootNormalizationIdentity += "|loop:" + IsLooping;
                clipSettings.loopTime = false; AnimationUtility.SetAnimationClipSettings(Clip, clipSettings);
                Bindings = AnimationUtility.GetCurveBindings(Clip);
                ValidateCurves();
                bones = new Transform[rig.Value.Paths.Length]; rest = new BoneLocalPose[bones.Length];
                for (var i = 0; i < bones.Length; i++)
                {
                    var path = rig.Value.Paths[i].ToString(); bones[i] = i == 0 ? instance.transform : instance.transform.Find(path);
                    if (bones[i] == null) throw new ArgumentException(clip.name + ": Humanoid target bone is missing: " + path);
                    rest[i] = new BoneLocalPose { Position = bones[i].localPosition, Rotation = bones[i].localRotation, Scale = bones[i].localScale };
                }
                var animator = instance.GetComponent<Animator>(); animator.enabled = true; animator.runtimeAnimatorController = null;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.applyRootMotion = false; animator.fireEvents = false;
                humanReader = new HumanPoseHandler(animator.avatar, instance.transform);
                muscleCurves = new AnimationCurve[HumanTrait.MuscleCount];
                var curves = Bindings.ToDictionary(b => b.propertyName, b => AnimationUtility.GetEditorCurve(Clip, b));
                for (var m = 0; m < muscleCurves.Length; m++)
                {
                    var name = HumanTrait.MuscleName[m];
                    if (m >= 55)
                    {
                        var parts = name.Split(' ');
                        name = parts[0] + "Hand." + parts[1] + "." + string.Join(" ", parts.Skip(2));
                    }
                    curves.TryGetValue(name, out muscleCurves[m]);
                }
                graph = PlayableGraph.Create("AnimationSystemDOTS Humanoid Bake"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                playable = AnimationClipPlayable.Create(graph, Clip); playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false);
                AnimationPlayableOutput.Create(graph, "Retargeted target pose", animator).SetSourcePlayable(playable);
                graph.Play(); graph.Evaluate(0);
            }
            catch { Dispose(); throw; }
        }

        // Call immediately after Sample(time), while the isolated native target
        // holds that pose. Body normalization belongs to Unity's Avatar solver;
        // muscle curves are the authored inputs, not the reader's inverse solve.
        public BoneLocalPose ReadHumanPose(double time, float[] muscles)
        {
            if (muscles.Length != muscleCurves.Length) throw new ArgumentException("Invalid muscle output layout.");
            humanReader.GetHumanPose(ref humanRead);
            for (var m = 0; m < muscles.Length; m++)
            {
                muscles[m] = muscleCurves[m] != null ? muscleCurves[m].Evaluate((float)time) : 0;
                if (!float.IsFinite(muscles[m])) throw new ArgumentException(Clip.name + ": non-finite evaluated Humanoid muscle " + m + " at " + time);
            }
            var body = new BoneLocalPose { Position = humanRead.bodyPosition, Rotation = humanRead.bodyRotation, Scale = Unity.Mathematics.math.float3(1) };
            if (!PoseValidation.IsValid(body)) throw new ArgumentException(Clip.name + ": invalid normalized Humanoid body pose at " + time);
            return body;
        }

        public BoneLocalPose[] Sample(double time)
        {
            for (var i = 0; i < bones.Length; i++)
            { bones[i].localPosition = rest[i].Position; bones[i].localRotation = rest[i].Rotation; bones[i].localScale = rest[i].Scale; }
            playable.SetTime(time); playable.SetDone(false); graph.Evaluate(0);
            var result = new BoneLocalPose[bones.Length];
            for (var i = 0; i < bones.Length; i++)
            {
                result[i] = i == 0 ? BoneLocalPose.Identity : new BoneLocalPose
                { Position = bones[i].localPosition, Rotation = bones[i].localRotation, Scale = bones[i].localScale };
                if (!PoseValidation.IsValid(result[i])) throw new ArgumentException(Clip.name + ": invalid Humanoid target TRS at " + bones[i].name + ", time " + time);
            }
            return result;
        }

        void ValidateCurves()
        {
            if (AnimationUtility.GetObjectReferenceCurveBindings(Clip).Length != 0 || AnimationUtility.GetAnimationEvents(Clip).Length != 0)
                throw new ArgumentException(Clip.name + ": Humanoid object-reference curves and animation events are not supported by this converter.");
            var muscles = new HashSet<string>(HumanTrait.MuscleName);
            foreach (var side in new[] { "Left", "Right" }) foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
            {
                muscles.Add(side + "Hand." + finger + ".Spread");
                for (var joint = 1; joint <= 3; joint++) muscles.Add(side + "Hand." + finger + "." + joint + " Stretched");
            }
            foreach (var prefix in new[] { "Root", "Motion", "LeftFoot", "RightFoot", "LeftHand", "RightHand" })
            { foreach (var axis in "xyz") muscles.Add(prefix + "T." + axis); foreach (var axis in "xyzw") muscles.Add(prefix + "Q." + axis); }
            foreach (var binding in Bindings)
            {
                if (binding.type != typeof(Animator) || binding.path != "" || !muscles.Contains(binding.propertyName))
                    throw new ArgumentException(Clip.name + ": unsupported Humanoid binding " + binding.path + "/" + binding.propertyName);
                foreach (var key in AnimationUtility.GetEditorCurve(Clip, binding).keys)
                    if (!float.IsFinite(key.time) || !float.IsFinite(key.value) || float.IsNaN(key.inTangent) || float.IsNaN(key.outTangent)
                        || !float.IsFinite(key.inWeight) || !float.IsFinite(key.outWeight))
                        throw new ArgumentException(Clip.name + ": non-finite Humanoid curve data: " + binding.propertyName);
            }
        }

        public void Dispose()
        {
            humanReader?.Dispose(); humanReader = null;
            if (graph.IsValid()) { graph.Stop(); graph.Destroy(); EditorApplication.QueuePlayerLoopUpdate(); }
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            if (Clip != null) UnityEngine.Object.DestroyImmediate(Clip);
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
