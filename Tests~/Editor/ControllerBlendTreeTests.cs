using System;
using System.Collections.Generic;
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
    [SetCulture("en-US")]
    public class ControllerBlendTreeTests
    {
        string folder;
        AnimatorController controller;
        readonly List<AnimationClip> clips = new List<AnimationClip>();
        BlobAssetReference<AnimationLibraryBlob> library;
        BlobAssetReference<RigDefinitionBlob> rig;

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/__MotionTreeTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(folder));
            controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Motion.controller");
            controller.AddParameter("X", AnimatorControllerParameterType.Float);
            controller.AddParameter("Y", AnimatorControllerParameterType.Float);
            for (var i = 0; i < 5; i++) controller.AddParameter("W" + i, AnimatorControllerParameterType.Float);
            for (var i = 0; i < 5; i++)
            {
                var clip = new AnimationClip { name = "Motion" + i };
                var duration = 1 + i * .25f;
                for (var j = 0; j < 5; j++)
                {
                    clip.SetCurve("Weight" + j, typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0, duration, i == j ? 1 : 0));
                    clip.SetCurve("Phase" + j, typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 0, duration, i == j ? 1 : 0));
                }
                var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                AssetDatabase.CreateAsset(clip, folder + "/Motion" + i + ".anim"); clips.Add(clip);
            }
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<AnimationLibraryBlob>();
                root.Version = AnimationBakeVersion.Schema; root.ContentKey = "motion-tree-tests"; root.RigKey = "tree-rig";
                var target = builder.Allocate(ref root.Clips, clips.Count);
                for (var i = 0; i < clips.Count; i++) { target[i].Duration = clips[i].length; target[i].IsLooping = 1; }
                for (var i = 0; i < clips.Count; i++)
                {
                    target[i].Version = AnimationBakeVersion.Schema; target[i].RigKey = "tree-rig";
                    target[i].SampleCount = 2; target[i].SampleStep = target[i].Duration;
                    var reference = builder.Allocate(ref target[i].ReferencePose, 11);
                    for (var b = 0; b < 11; b++) reference[b] = BoneLocalPose.Identity;
                    var tracks = builder.Allocate(ref target[i].Tracks, 11);
                    for (var b = 0; b < 11; b++)
                    {
                        var samples = builder.Allocate(ref tracks[b].Samples, 2);
                        samples[0] = samples[1] = BoneLocalPose.Identity;
                        if (b == i + 1) samples[0].Position.x = samples[1].Position.x = 1;
                        if (b == i + 6) samples[1].Position.x = 1;
                    }
                }
                library = builder.CreateBlobAssetReference<AnimationLibraryBlob>(Allocator.Persistent);
            }
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<RigDefinitionBlob>(); root.Version = AnimationBakeVersion.Schema; root.ContentKey = "tree-rig";
                var rest = builder.Allocate(ref root.RestLocalPoses, 11);
                for (var i = 0; i < rest.Length; i++) rest[i] = BoneLocalPose.Identity;
                rig = builder.CreateBlobAssetReference<RigDefinitionBlob>(Allocator.Persistent);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (library.IsCreated) library.Dispose();
            if (rig.IsCreated) rig.Dispose();
            clips.Clear(); AssetDatabase.DeleteAsset(folder);
        }

        BlendTree Tree(BlendTreeType type)
        {
            var tree = new BlendTree { name = type.ToString(), blendType = type, blendParameter = "X", blendParameterY = "Y", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, controller);
            var positions = new[] { Vector2.zero, Vector2.up, Vector2.left, Vector2.right, Vector2.down };
            var children = new ChildMotion[5];
            for (var i = 0; i < children.Length; i++) children[i] = new ChildMotion
            { motion = clips[i], position = positions[i], threshold = i - 2, timeScale = 1, directBlendParameter = "W" + i };
            tree.children = children;
            if (type == BlendTreeType.Direct)
            {
                var serialized = new SerializedObject(tree); serialized.FindProperty("m_NormalizedBlendValues").boolValue = true; serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            return tree;
        }

        [TestCase(BlendTreeType.Simple1D)]
        [TestCase(BlendTreeType.SimpleDirectional2D)]
        [TestCase(BlendTreeType.FreeformDirectional2D)]
        [TestCase(BlendTreeType.FreeformCartesian2D)]
        [TestCase(BlendTreeType.Direct)]
        public void AllTreeModes_WeightsDurationClockAndLeafPhaseMatchNative(BlendTreeType type)
        {
            var tree = Tree(type);
            var state = controller.layers[0].stateMachine.AddState("Tree"); state.motion = tree; state.writeDefaultValues = false;
            controller.layers[0].stateMachine.defaultState = state;
            Compare(tree, false);
        }

        [Test]
        public void NestedTrees_ChildSpeedAndCycleOffsetMatchNative()
        {
            var tree = Tree(BlendTreeType.Simple1D);
            var nested = Tree(BlendTreeType.FreeformCartesian2D);
            var children = tree.children;
            children[1].motion = nested; children[1].timeScale = .75f; children[1].cycleOffset = .2f;
            children[3].timeScale = 2; children[3].cycleOffset = .15f;
            children[4].timeScale = -1.5f; children[4].cycleOffset = .3f;
            tree.children = children;
            var state = controller.layers[0].stateMachine.AddState("Tree"); state.motion = tree; state.writeDefaultValues = false;
            controller.layers[0].stateMachine.defaultState = state;
            Compare(tree, true);
        }

        [Test]
        public void DirectWithoutNormalization_ReportsUnsupportedChannelSemantics()
        {
            var tree = Tree(BlendTreeType.Direct);
            var serialized = new SerializedObject(tree); serialized.FindProperty("m_NormalizedBlendValues").boolValue = false; serialized.ApplyModifiedPropertiesWithoutUndo();
            var state = controller.layers[0].stateMachine.AddState("Direct"); state.motion = tree; state.writeDefaultValues = false;
            controller.layers[0].stateMachine.defaultState = state;
            var result = ControllerBakePipeline.Build(controller, clips, library);
            try
            {
                Assert.That(result.Success, Is.False);
                Assert.That(string.Join("\n", result.Diagnostics), Does.Contain(folder).And.Contain("/Direct").And.Contain("Normalize Blend Values"));
            }
            finally { if (result.Controller.IsCreated) result.Controller.Dispose(); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TreeTransitions_DynamicDurationAndLeafRequestsMatchNative(bool normalized)
        {
            var tree = Tree(BlendTreeType.Simple1D);
            var machine = controller.layers[0].stateMachine;
            var first = machine.AddState("First"); first.motion = tree; first.writeDefaultValues = false; machine.defaultState = first;
            var other = Tree(BlendTreeType.FreeformCartesian2D);
            var second = machine.AddState("Second"); second.motion = other; second.writeDefaultValues = false;
            var edge = first.AddTransition(second); edge.hasExitTime = false; edge.hasFixedDuration = !normalized; edge.duration = .6f; edge.offset = .2f;
            edge.AddCondition(AnimatorConditionMode.Greater, -1.1f, "X");
            Compare(tree, false, normalized ? "normalized-transition" : "fixed-transition", true);
        }

        [TestCase("simpleNoIdle")]
        [TestCase("simpleRadii")]
        [TestCase("directionalSpeeds")]
        [TestCase("directionalNoIdle")]
        [TestCase("cartesianIrregular")]
        [TestCase("oneChild")]
        public void TreeBoundaries_WeightsAndTimingMatchNative(string configuration)
        {
            var type = configuration.StartsWith("simple") || configuration == "oneChild" ? BlendTreeType.SimpleDirectional2D
                : configuration.StartsWith("directional") ? BlendTreeType.FreeformDirectional2D : BlendTreeType.FreeformCartesian2D;
            var tree = Tree(type); var children = new List<ChildMotion>(tree.children);
            if (configuration.EndsWith("NoIdle")) children.RemoveAt(0);
            if (configuration == "simpleRadii")
            { var c = children[1]; c.position *= .7f; children[1] = c; c = children[2]; c.position *= 1.6f; children[2] = c; }
            if (configuration == "simpleCone")
            { children.RemoveAt(4); var c = children[2]; c.position = new Vector2(-.8f, .6f); children[2] = c; c = children[3]; c.position = new Vector2(.8f, .6f); children[3] = c; }
            if (configuration == "simpleTwo") children = new List<ChildMotion> { children[1], children[4] };
            if (configuration == "oneChild") children = new List<ChildMotion> { children[1] };
            if (configuration == "directionalSpeeds")
            { var c = children[4]; c.position = Vector2.up * 2; children[4] = c; }
            if (configuration == "cartesianIrregular")
            { var c = children[0]; c.position = new Vector2(.2f, -.3f); children[0] = c; c = children[1]; c.position = new Vector2(.6f, 1.8f); children[1] = c; }
            tree.children = children.ToArray();
            var state = controller.layers[0].stateMachine.AddState("Tree"); state.motion = tree; state.writeDefaultValues = false;
            controller.layers[0].stateMachine.defaultState = state;
            Compare(tree, false, configuration);
        }

        [TestCase("duplicate1D", "Duplicate 1D")]
        [TestCase("duplicate2D", "Duplicate 2D")]
        [TestCase("missingParameter", "Unknown Blend Tree parameter")]
        [TestCase("boolParameter", "Float parameter")]
        [TestCase("mirror", "Mirrored")]
        [TestCase("collinear", "angular gaps")]
        [TestCase("openCone", "angular gaps")]
        [TestCase("nanPosition", "finite")]
        [TestCase("sameDirection", "one motion per direction")]
        public void InvalidTrees_ReportAssetAndMotionPath(string configuration, string message)
        {
            var tree = Tree(configuration == "duplicate1D" ? BlendTreeType.Simple1D : BlendTreeType.SimpleDirectional2D);
            var children = tree.children;
            if (configuration == "duplicate1D") children[1].threshold = children[0].threshold;
            if (configuration == "duplicate2D") children[1].position = children[0].position;
            if (configuration == "mirror") children[1].mirror = true;
            if (configuration == "collinear") children = new[] { children[1], children[4] };
            if (configuration == "openCone") children = new[] { children[0], children[1], children[3] };
            if (configuration == "nanPosition") children[1].position = new Vector2(float.NaN, 0);
            if (configuration == "sameDirection") children[4].position = Vector2.up * 2;
            if (configuration == "missingParameter") tree.blendParameter = "Absent";
            if (configuration == "boolParameter") { controller.AddParameter("Flag", AnimatorControllerParameterType.Bool); tree.blendParameter = "Flag"; }
            tree.children = children;
            var state = controller.layers[0].stateMachine.AddState("Invalid"); state.motion = tree; state.writeDefaultValues = false;
            controller.layers[0].stateMachine.defaultState = state;
            var result = ControllerBakePipeline.Build(controller, clips, library);
            try
            {
                Assert.That(result.Success, Is.False);
                Assert.That(string.Join("\n", result.Diagnostics), Does.Contain(folder).And.Contain("/Invalid").And.Contain(message));
            }
            finally { if (result.Controller.IsCreated) result.Controller.Dispose(); }
        }

        void Compare(BlendTree tree, bool nested, string configuration = "", bool transitions = false)
        {
            var baked = ControllerBakePipeline.Build(controller, clips, library);
            Assert.That(baked.Success, Is.True, string.Join("\n", baked.Diagnostics));
            var scene = EditorSceneManager.NewPreviewScene();
            var go = new GameObject("Native Blend Tree Oracle", typeof(Animator));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            var markers = new Transform[5]; var phases = new Transform[5];
            for (var i = 0; i < 5; i++)
            {
                markers[i] = new GameObject("Weight" + i).transform; markers[i].SetParent(go.transform, false);
                phases[i] = new GameObject("Phase" + i).transform; phases[i].SetParent(go.transform, false);
            }
            var native = PlayableGraph.Create("AnimationSystemDOTS.BlendTreeOracle");
            World world = null;
            try
            {
                var playable = AnimatorControllerPlayable.Create(native, controller);
                AnimationPlayableOutput.Create(native, "Oracle", go.GetComponent<Animator>()).SetSourcePlayable(playable);
                native.SetTimeUpdateMode(DirectorUpdateMode.Manual); native.Play();
                world = new World("AnimationSystemDOTS Motion Tree Oracle"); var em = world.EntityManager;
                var actor = em.CreateEntity(typeof(AnimationRig), typeof(AnimationController), typeof(ControllerLayerState), typeof(AnimationClock), typeof(AnimationPlaybackMode));
                em.SetComponentData(actor, new AnimationRig { Rig = rig, Library = library });
                em.SetComponentData(actor, new AnimationController { Definition = baked.Controller });
                em.SetComponentData(actor, new AnimationPlaybackMode { Value = PlaybackDriver.Controller });
                var values = em.AddBuffer<AnimationParameterValue>(actor); values.ResizeUninitialized(7);
                em.AddBuffer<AnimationClipRequest>(actor);
                em.AddBuffer<SampledBonePose>(actor); em.AddBuffer<BoneLocalPose>(actor);
                var step = em.CreateEntity(typeof(AnimationStepContext)); var system = world.GetOrCreateSystemManaged<ControllerAdvanceSystem>();
                var sampleSystem = world.GetOrCreateSystemManaged<AnimationSampleSystem>(); var blendSystem = world.GetOrCreateSystemManaged<AnimationBlendSystem>();
                var infos = new List<AnimatorClipInfo>(); var expectedWeights = new float[5];
                var observedWeights = new float[5]; var observedPhases = new float[5];
                var trace = new System.Text.StringBuilder("x;y;nativeDuration;ecsDuration;nativeTime;ecsTime;nativeWeights;ecsWeights;nativePhases;ecsPhases;nativeMarkers\n");
                double maxClock = 0, maxWeight = 0, maxPhase = 0, maxMarker = 0;
                string clockContext = "", weightContext = "", phaseContext = "", markerContext = "";
                ulong tick = 0;
                for (var x = -8; x <= 8; x++) for (var y = -8; y <= 8; y++)
                {
                    values = em.GetBuffer<AnimationParameterValue>(actor);
                    for (var p = 0; p < 7; p++)
                    {
                        var value = p == 0 ? x * .25f : p == 1 ? y * .25f : math.max(0, (x + 2 * y + p * 3) * .125f);
                        playable.SetFloat(controller.parameters[p].name, value); values[p] = new AnimationParameterValue { Float = value };
                    }
                    var dt = tick == 0 ? 0 : .037f;
                    native.Evaluate(dt);
                    em.SetComponentData(step, new AnimationStepContext { Tick = ++tick, DeltaTime = dt }); system.Update(); em.CompleteAllTrackedJobs();
                    sampleSystem.Update(); blendSystem.Update(); em.CompleteAllTrackedJobs();
                    var status = em.GetComponentData<ControllerLayerState>(actor);
                    var context = tree.blendType + " at " + x * .25f + "," + y * .25f;
                    Assert.That(status.Result, Is.EqualTo(ControllerEvaluationResult.Success), context);
                    var nativeState = playable.GetCurrentAnimatorStateInfo(0);
                    Assert.That(nativeState.fullPathHash, Is.EqualTo(Animator.StringToHash(baked.Controller.Value.States[status.CurrentState].Path.ToString())), context + " state");
                    var clockError = Math.Abs(status.NormalizedTime - nativeState.normalizedTime);
                    Assert.That(status.NextState >= 0, Is.EqualTo(playable.IsInTransition(0)), context + " transition");
                    if (status.NextState >= 0)
                    {
                        clockError = Math.Max(clockError, Math.Abs(status.NextNormalizedTime - playable.GetNextAnimatorStateInfo(0).normalizedTime));
                        clockError = Math.Max(clockError, Math.Abs(status.TransitionTime / status.TransitionDuration - playable.GetAnimatorTransitionInfo(0).normalizedTime));
                    }
                    if (clockError > maxClock) { maxClock = clockError; clockContext = context; }
                    Array.Clear(expectedWeights, 0, 5); Array.Clear(observedWeights, 0, 5); Array.Clear(observedPhases, 0, 5);
                    infos.Clear(); playable.GetCurrentAnimatorClipInfo(0, infos);
                    foreach (var info in infos) expectedWeights[clips.IndexOf(info.clip)] += info.weight;
                    if (transitions) for (var i = 0; i < 5; i++) expectedWeights[i] = markers[i].localPosition.x;
                    foreach (var request in em.GetBuffer<AnimationClipRequest>(actor))
                    {
                        var id = request.Clip.Value; observedWeights[id] += request.Weight;
                        var phase = request.Time / library.Value.Clips[id].Duration;
                        observedPhases[id] += (float)(phase - Math.Floor(phase)) * request.Weight;
                    }
                    var local = em.GetBuffer<BoneLocalPose>(actor);
                    for (var i = 0; i < 5; i++)
                    {
                        var error = Math.Abs(observedWeights[i] - expectedWeights[i]);
                        if (error > maxWeight) { maxWeight = error; weightContext = context + " clip " + i; }
                        var markerError = Math.Abs(markers[i].localPosition.x - local[i + 1].Position.x);
                        if (markerError > maxMarker) { maxMarker = markerError; markerContext = context + " clip " + i + " actual=" + markers[i].localPosition.x + " expected=" + expectedWeights[i]; }
                        error = Math.Abs(local[i + 6].Position.x - phases[i].localPosition.x);
                        if (error > maxPhase) { maxPhase = error; phaseContext = context + " clip " + i; }
                    }
                    var scratch = em.GetBuffer<ControllerMotionValue>(actor);
                    trace.AppendLine(string.Join(";", Array.ConvertAll(new object[] { x * .25f, y * .25f, nativeState.length, scratch[baked.Controller.Value.States[status.CurrentState].Motion].Duration,
                        nativeState.normalizedTime, status.NormalizedTime, Numbers(expectedWeights), Numbers(observedWeights),
                        Numbers(Array.ConvertAll(phases, p => p.localPosition.x)), Numbers(observedPhases), Numbers(Array.ConvertAll(markers, p => p.localPosition.x)) }, v => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture))));
                }
                var directory = System.IO.Path.Combine("Artifacts/Verification/AnimatorValidation"); System.IO.Directory.CreateDirectory(directory);
                System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "Tree-" + tree.blendType + (nested ? "-nested" : "") + configuration + ".csv"), trace.ToString());
                var summary = "weight=" + maxWeight + " " + weightContext + "; position=" + maxMarker + " " + markerContext + "; clock=" + maxClock + " " + clockContext + "; phase=" + maxPhase + " " + phaseContext;
                System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "Tree-" + tree.blendType + (nested ? "-nested" : "") + configuration + ".txt"), summary);
                Assert.That(maxWeight <= 3e-5 && maxMarker <= 1e-4 && maxClock <= 2e-5 && maxPhase <= 1e-4, Is.True, summary);
            }
            finally
            {
                if (native.IsValid()) native.Destroy(); if (world != null && world.IsCreated) world.Dispose();
                Object.DestroyImmediate(go); EditorSceneManager.ClosePreviewScene(scene); baked.Controller.Dispose();
            }
        }
        static string Numbers(float[] values) => string.Join(",", Array.ConvertAll(values, v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
    }
}
