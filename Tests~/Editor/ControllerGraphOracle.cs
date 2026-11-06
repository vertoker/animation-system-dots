using System;
using System.Collections.Generic;
using System.IO;
using AnimationSystemDOTS.Authoring;
using AnimationSystemDOTS.Editor.Baking;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace AnimationSystemDOTS.Tests
{
    // Native and ECS share clips and explicit delta times. Curved translation and
    // different rotation axes expose frozen/advancing transition-history mistakes.
    internal sealed class ControllerGraphOracle : IDisposable
    {
        internal readonly string Folder;
        internal readonly AnimatorController Source;
        internal readonly AnimationClip[] Clips;
        internal AnimatorStateMachine Root => Source.layers[0].stateMachine;
        internal AnimatorControllerPlayable Native;
        internal World World;
        internal Entity Actor;
        internal BlobAssetReference<AnimatorControllerBlob> Definition;
        readonly UnityEngine.SceneManagement.Scene scene;
        readonly GameObject go;
        readonly Transform bone;
        readonly string reportName;
        readonly System.Text.StringBuilder trace = new System.Text.StringBuilder();
        readonly List<BlobAssetReference<AnimationClipBlob>> baked = new List<BlobAssetReference<AnimationClipBlob>>();
        readonly Dictionary<int, string> names = new Dictionary<int, string>();
        readonly AnimationBakeSettings settings;
        BlobAssetReference<RigDefinitionBlob> rig;
        BlobAssetReference<AnimationLibraryBlob> library;
        PlayableGraph nativeGraph;
        Entity step;
        ControllerAdvanceSystem advance;
        AnimationSampleSystem sample;
        AnimationBlendSystem blend;
        ulong tick;

        internal ControllerGraphOracle(string name)
        {
            reportName = name;
            Folder = "Assets/__GraphOracle_" + Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            scene = EditorSceneManager.NewPreviewScene(); go = new GameObject("Graph Oracle", typeof(Animator));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            bone = new GameObject("Bone").transform; bone.SetParent(go.transform, false);
            Source = AnimatorController.CreateAnimatorControllerAtPath(Folder + "/Graph.controller");
            foreach (var parameter in new[] { "Go", "Other", "Any" }) Source.AddParameter(parameter, AnimatorControllerParameterType.Trigger);
            Source.AddParameter("Enabled", AnimatorControllerParameterType.Bool); Source.AddParameter("Variant", AnimatorControllerParameterType.Int);
            Source.AddParameter(new AnimatorControllerParameter { name = "Speed", type = AnimatorControllerParameterType.Float, defaultFloat = 1 });
            Source.AddParameter("Cycle", AnimatorControllerParameterType.Float); Source.AddParameter("Time", AnimatorControllerParameterType.Float);
            Clips = new AnimationClip[3];
            var rotations = new[] { Quaternion.Euler(60, 0, 0), Quaternion.Euler(0, 90, 0), Quaternion.Euler(10, 0, 110) };
            var lengths = new[] { 1f, 2f, 1.5f };
            for (var i = 0; i < Clips.Length; i++)
            {
                var clip = new AnimationClip { name = "Motion" + i };
                clip.SetCurve("Bone", typeof(Transform), "m_LocalPosition.x", AnimationCurve.EaseInOut(0, i * 2, lengths[i], i * 3 + 1));
                for (var c = 1; c < 3; c++) clip.SetCurve("Bone", typeof(Transform), "m_LocalPosition." + "xyz"[c], AnimationCurve.Constant(0, lengths[i], 0));
                for (var c = 0; c < 4; c++) clip.SetCurve("Bone", typeof(Transform), "m_LocalRotation." + "xyzw"[c], AnimationCurve.Constant(0, lengths[i], rotations[i][c]));
                for (var c = 0; c < 3; c++) clip.SetCurve("Bone", typeof(Transform), "m_LocalScale." + "xyz"[c], AnimationCurve.Constant(0, lengths[i], 1 + i * .15f));
                var clipSettings = AnimationUtility.GetAnimationClipSettings(clip); clipSettings.loopTime = true;
                AnimationUtility.SetAnimationClipSettings(clip, clipSettings); AssetDatabase.CreateAsset(clip, Folder + "/Motion" + i + ".anim"); Clips[i] = clip;
            }
            settings = ScriptableObject.CreateInstance<AnimationBakeSettings>(); settings.SampleRate = 240; settings.PositionTolerance = .0001f; settings.AngleToleranceDegrees = .05f;
            var result = RigBakePipeline.Bake(go); Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics)); rig = result.Rig;
            foreach (var clip in Clips)
            {
                var resultClip = ClipBakePipeline.Bake(new ClipBakeRequest { Source = go, Clip = clip, Rig = rig, Settings = settings });
                Assert.That(resultClip.Success, Is.True, string.Join("\n", resultClip.Diagnostics)); baked.Add(resultClip.Clip);
            }
            library = LibraryBakePipeline.Build(rig, baked).Library;
        }

        internal AnimatorState State(AnimatorStateMachine machine, string name, int clip)
        { var state = machine.AddState(name); state.motion = Clips[clip]; state.writeDefaultValues = false; names[Animator.StringToHash(name)] = name; return state; }
        internal static AnimatorStateTransition Edge(AnimatorState from, AnimatorState to, string trigger, float duration = .3f)
        {
            var edge = from.AddTransition(to); edge.hasExitTime = false; edge.hasFixedDuration = true; edge.duration = duration;
            edge.AddCondition(AnimatorConditionMode.If, 0, trigger); return edge;
        }
        internal void StartNative()
        {
            nativeGraph = PlayableGraph.Create("AnimationSystemDOTS Graph Oracle " + reportName); nativeGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            Native = AnimatorControllerPlayable.Create(nativeGraph, Source);
            AnimationPlayableOutput.Create(nativeGraph, "Native", go.GetComponent<Animator>()).SetSourcePlayable(Native); nativeGraph.Play(); nativeGraph.Evaluate(0);
            Assert.That(Native.GetCurrentAnimatorStateInfo(0).fullPathHash, Is.Not.Zero, "Native fixture must have a valid active state before testing conversion.");
        }
        internal void StartEcs()
        {
            var result = ControllerBakePipeline.Build(Source, Clips, library, go, rig);
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics)); Definition = result.Controller;
            World = new World("AnimationSystemDOTS Graph Oracle " + reportName); var em = World.EntityManager;
            Actor = em.CreateEntity(typeof(AnimationRig), typeof(AnimationController), typeof(ControllerLayerState), typeof(AnimationClock), typeof(AnimationPlaybackMode));
            em.SetComponentData(Actor, new AnimationRig { Rig = rig, Library = library }); em.SetComponentData(Actor, new AnimationController { Definition = Definition });
            em.SetComponentData(Actor, new AnimationPlaybackMode { Value = PlaybackDriver.Controller });
            var values = em.AddBuffer<AnimationParameterValue>(Actor);
            for (var i = 0; i < Definition.Value.Parameters.Length; i++) values.Add(new AnimationParameterValue { Float = Definition.Value.Parameters[i].DefaultFloat, Int = Definition.Value.Parameters[i].DefaultInt });
            em.AddBuffer<AnimationClipRequest>(Actor); em.AddBuffer<SampledBonePose>(Actor); em.AddBuffer<BoneLocalPose>(Actor);
            step = em.CreateEntity(typeof(AnimationStepContext)); advance = World.GetOrCreateSystemManaged<ControllerAdvanceSystem>();
            sample = World.GetOrCreateSystemManaged<AnimationSampleSystem>(); blend = World.GetOrCreateSystemManaged<AnimationBlendSystem>();
        }
        internal void Float(string name, float value)
        {
            Native.SetFloat(name, value);
            if (World != null) Assert.That(AnimationApi.SetFloat(new AnimationController { Definition = Definition }, World.EntityManager.GetBuffer<AnimationParameterValue>(Actor),
                AnimationApi.FindParameter(new AnimationController { Definition = Definition }, name), value), Is.EqualTo(AnimationParameterResult.Success));
        }
        internal void Trigger(string name)
        {
            Native.SetTrigger(name);
            if (World != null) Assert.That(AnimationApi.SetTrigger(new AnimationController { Definition = Definition }, World.EntityManager.GetBuffer<AnimationParameterValue>(Actor),
                AnimationApi.FindParameter(new AnimationController { Definition = Definition }, name)), Is.EqualTo(AnimationParameterResult.Success));
        }
        internal void Tick(float dt)
        {
            nativeGraph.Evaluate(dt); ++tick;
            var em = World.EntityManager; em.SetComponentData(step, new AnimationStepContext { Tick = tick, DeltaTime = dt });
            advance.Update(); sample.Update(); blend.Update(); em.CompleteAllTrackedJobs();
            var state = em.GetComponentData<ControllerLayerState>(Actor); var current = Native.GetCurrentAnimatorStateInfo(0); var next = Native.GetNextAnimatorStateInfo(0);
            var actual = em.GetBuffer<BoneLocalPose>(Actor)[1];
            trace.AppendLine(tick + ";" + current.fullPathHash + ";" + current.normalizedTime + ";" + next.fullPathHash + ";" + next.normalizedTime
                + ";" + Native.GetAnimatorTransitionInfo(0).normalizedTime + ";" + state.CurrentState + ";" + state.NormalizedTime + ";" + state.NextState + ";" + state.NextNormalizedTime
                + ";" + bone.localPosition.ToString("R") + ";" + actual.Position + ";" + Quaternion.Angle(bone.localRotation, actual.Rotation));
            var context = reportName + " tick=" + tick;
            Assert.That(state.Result, Is.EqualTo(ControllerEvaluationResult.Success), context);
            Assert.That(current.fullPathHash, Is.EqualTo(Animator.StringToHash(Definition.Value.States[state.CurrentState].Path.ToString())), context + " current");
            Assert.That(state.NormalizedTime, Is.EqualTo(current.normalizedTime).Within(2e-5), context + " clock");
            Assert.That(state.NextState >= 0, Is.EqualTo(Native.IsInTransition(0)), context + " transition");
            if (state.NextState >= 0)
            {
                Assert.That(next.fullPathHash, Is.EqualTo(Animator.StringToHash(Definition.Value.States[state.NextState].Path.ToString())), context + " next");
                Assert.That(state.NextNormalizedTime, Is.EqualTo(next.normalizedTime).Within(2e-5), context + " next clock");
                Assert.That(state.TransitionTime / state.TransitionDuration, Is.EqualTo(Native.GetAnimatorTransitionInfo(0).normalizedTime).Within(2e-5), context + " progress");
            }
            Assert.That(math.distance(actual.Position, bone.localPosition), Is.LessThanOrEqualTo(1e-4), context + " position");
            Assert.That(Quaternion.Angle(actual.Rotation, bone.localRotation), Is.LessThanOrEqualTo(.05f), context + " rotation");
            Assert.That(math.distance(actual.Scale, bone.localScale), Is.LessThanOrEqualTo(1e-4), context + " scale");
        }
        internal void CaptureNative(float dt)
        {
            nativeGraph.Evaluate(dt); ++tick;
            var current = Native.GetCurrentAnimatorStateInfo(0); var next = Native.GetNextAnimatorStateInfo(0);
            var currentName = names.TryGetValue(current.shortNameHash, out var name) ? name : current.shortNameHash.ToString();
            var nextName = names.TryGetValue(next.shortNameHash, out name) ? name : "-";
            trace.AppendLine(tick + ";" + currentName + ";" + current.normalizedTime + ";" + nextName + ";" + next.normalizedTime
                + ";" + Native.GetAnimatorTransitionInfo(0).normalizedTime + ";" + bone.localPosition.ToString("R") + ";" + bone.localRotation.ToString("R") + ";" + bone.localScale.ToString("R"));
            Assert.That(math.all(math.isfinite((float3)bone.localPosition)), Is.True);
            Assert.That(math.all(math.isfinite(((quaternion)bone.localRotation).value)), Is.True);
        }
        public void Dispose()
        {
            Directory.CreateDirectory("Artifacts/Verification/AnimatorValidation"); File.WriteAllText("Artifacts/Verification/AnimatorValidation/Graph-" + reportName + ".csv", trace.ToString());
            if (nativeGraph.IsValid()) nativeGraph.Destroy(); if (World != null && World.IsCreated) World.Dispose();
            if (Definition.IsCreated) Definition.Dispose(); if (library.IsCreated) library.Dispose(); foreach (var clip in baked) if (clip.IsCreated) clip.Dispose(); if (rig.IsCreated) rig.Dispose();
            Object.DestroyImmediate(settings); Object.DestroyImmediate(go); EditorSceneManager.ClosePreviewScene(scene); AssetDatabase.DeleteAsset(Folder);
        }
    }
}
