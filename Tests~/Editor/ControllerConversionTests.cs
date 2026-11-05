using NUnit.Framework;
using AnimationSystemDOTS.Authoring;
using AnimationSystemDOTS.Editor.Baking;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace AnimationSystemDOTS.Tests
{
    public class ControllerConversionTests
    {
        string folder;
        AnimatorController source;
        AnimationClip a, b;
        BlobAssetReference<AnimationLibraryBlob> library;
        [SetUp]
        public void SetUp()
        {
            folder = "Assets/__ControllerTests_" + System.Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(folder));
            a = Clip("A", 1); b = Clip("B", 2);
            source = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Test.controller");
            var machine = source.layers[0].stateMachine;
            var first = machine.AddState("A"); first.motion = a; first.writeDefaultValues = false;
            var second = machine.AddState("B"); second.motion = b; second.writeDefaultValues = false;
            machine.defaultState = first;
            source.AddParameter("Speed", AnimatorControllerParameterType.Float);
            source.AddParameter("Count", AnimatorControllerParameterType.Int);
            source.AddParameter("Enabled", AnimatorControllerParameterType.Bool);
            source.AddParameter("Go", AnimatorControllerParameterType.Trigger);
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<AnimationLibraryBlob>();
                root.Version = AnimationBakeVersion.Schema; root.ContentKey = new FixedString128Bytes("controller-tests");
                var clips = builder.Allocate(ref root.Clips, 2); clips[0].Duration = 1; clips[1].Duration = 2;
                clips[0].IsLooping = clips[1].IsLooping = 1;
                for (var i = 0; i < clips.Length; i++)
                {
                    clips[i].SampleCount = 2; clips[i].SampleStep = clips[i].Duration;
                    var tracks = builder.Allocate(ref clips[i].Tracks, 2);
                    var rest = builder.Allocate(ref tracks[0].Samples, 1); rest[0] = BoneLocalPose.Identity;
                    var samples = builder.Allocate(ref tracks[1].Samples, 2); samples[0] = samples[1] = BoneLocalPose.Identity;
                    samples[1] = new BoneLocalPose { Position = new Unity.Mathematics.float3((float)clips[i].Duration, 0, 0), Rotation = Unity.Mathematics.quaternion.identity, Scale = new Unity.Mathematics.float3(1) };
                }
                library = builder.CreateBlobAssetReference<AnimationLibraryBlob>(Allocator.Persistent);
            }
        }
        AnimationClip Clip(string name, float duration)
        {
            var clip = new AnimationClip { name = name };
            clip.SetCurve("Bone", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 0, duration, duration));
            var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, folder + "/" + name + ".anim"); return clip;
        }
        [TearDown]
        public void TearDown()
        {
            if (library.IsCreated) library.Dispose();
            AssetDatabase.DeleteAsset(folder);
        }
        [Test]
        public void Converter_PreservesParametersStatesAndSerializedTransitionPriority()
        {
            var machine = source.layers[0].stateMachine;
            var first = machine.states[0].state; var second = machine.states[1].state;
            first.speed = 2; first.speedParameterActive = true; first.speedParameter = "Speed"; first.cycleOffset = .25f;
            var one = first.AddTransition(second); one.hasExitTime = false; one.hasFixedDuration = true; one.duration = .2f;
            one.AddCondition(AnimatorConditionMode.If, 0, "Go");
            var two = first.AddTransition(first); two.hasExitTime = true; two.exitTime = 2.5f; two.duration = .4f; two.offset = .75f;
            var result = ControllerBakePipeline.Build(source, new[] { a, b }, library);
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
            try
            {
                ref var graph = ref result.Controller.Value;
                Assert.That(graph.LibraryKey, Is.EqualTo(library.Value.ContentKey));
                Assert.That(graph.Parameters.Length, Is.EqualTo(4)); Assert.That(graph.DefaultState, Is.Zero);
                Assert.That(graph.States[0].Path.ToString(), Is.EqualTo("Base Layer.A"));
                Assert.That(graph.States[0].SpeedParameter, Is.Zero); Assert.That(graph.States[0].CycleOffset, Is.EqualTo(.25f));
                Assert.That(graph.Transitions[graph.States[0].TransitionStart].Destination, Is.EqualTo(1));
                Assert.That(graph.Transitions[graph.States[0].TransitionStart + 1].Destination, Is.Zero);
                Assert.That(graph.Conditions[0].Parameter, Is.EqualTo(3));
                Assert.That(graph.Transitions[graph.States[0].TransitionStart + 1].ExitTime, Is.EqualTo(2.5f));
            }
            finally { result.Controller.Dispose(); }
        }
        [TestCase("writeDefaults")]
        [TestCase("blendTree")]
        [TestCase("nested")]
        [TestCase("layers")]
        [TestCase("behaviour")]
        [TestCase("interruption")]
        [TestCase("missingParameter")]
        [TestCase("intIf")]
        [TestCase("floatEquals")]
        [TestCase("boolGreater")]
        public void Converter_RejectsUnsupportedInputWithAssetAndStatePath(string feature)
        {
            var machine = source.layers[0].stateMachine; var first = machine.states[0].state;
            switch (feature)
            {
                case "writeDefaults": first.writeDefaultValues = true; break;
                case "blendTree": first.motion = new BlendTree(); break;
                case "nested": machine.AddStateMachine("Nested"); break;
                case "layers": source.AddLayer("Extra"); break;
                case "behaviour":
                    var behaviour = ScriptableObject.CreateInstance<ControllerTestBehaviour>();
                    AssetDatabase.AddObjectToAsset(behaviour, source);
                    first.behaviours = new StateMachineBehaviour[] { behaviour }; break;
                case "interruption": first.AddTransition(machine.states[1].state).interruptionSource = TransitionInterruptionSource.Source; break;
                case "missingParameter": first.AddTransition(machine.states[1].state).AddCondition(AnimatorConditionMode.If, 0, "Missing"); break;
                case "intIf": first.AddTransition(machine.states[1].state).AddCondition(AnimatorConditionMode.If, 0, "Count"); break;
                case "floatEquals": first.AddTransition(machine.states[1].state).AddCondition(AnimatorConditionMode.Equals, 0, "Speed"); break;
                case "boolGreater": first.AddTransition(machine.states[1].state).AddCondition(AnimatorConditionMode.Greater, 0, "Enabled"); break;
            }
            var result = ControllerBakePipeline.Build(source, new[] { a, b }, library);
            try
            {
                Assert.That(result.Success, Is.False);
                Assert.That(result.Controller.IsCreated, Is.False);
                var text = string.Join("\n", result.Diagnostics);
                Assert.That(text, Does.Contain("Test.controller")); Assert.That(text, Does.Contain("Base Layer"));
            }
            finally { if (result.Controller.IsCreated) result.Controller.Dispose(); }
        }
        [Test]
        public void ControllerAuthoring_AcceptsAnimatorControllerAsSource()
        {
            var type = typeof(AnimationRigAuthoring).Assembly.GetType("AnimationSystemDOTS.Authoring.AnimationControllerAuthoring");
            Assert.That(type, Is.Not.Null, "An ECS rig needs controller authoring to select its .controller source.");
            var field = type.GetField("Controller");
            Assert.That(field, Is.Not.Null);
            Assert.That(field.FieldType, Is.EqualTo(typeof(UnityEngine.RuntimeAnimatorController)));
        }

        [Test]
        public void SandboxController_HasEditableSourcePrefabAndScene()
        {
            var root = AnimationSystemDOTS.Sandbox.Editor.SandboxPaths.Root;
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(root + "Scenes/02_Controller.unity"), Is.Not.Null);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(root + "Fixtures/ControllerHumanoid.prefab");
            Assert.That(prefab, Is.Not.Null);
            var authoring = prefab.GetComponent<AnimationControllerAuthoring>(); Assert.That(authoring, Is.Not.Null);
            var controller = authoring.Controller as AnimatorController; Assert.That(controller, Is.Not.Null);
            Assert.That(controller.layers[0].stateMachine.states.Length, Is.EqualTo(2));
            foreach (var state in controller.layers[0].stateMachine.states) Assert.That(state.state.writeDefaultValues, Is.False);
        }

        [Test]
        public void Converter_RejectsSparseGenericPropertyLayoutsThatNativePreserves()
        {
            b.ClearCurves();
            b.SetCurve("OtherBone", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 0, 2, 2));
            var machine = source.layers[0].stateMachine; var edge = machine.states[0].state.AddTransition(machine.states[1].state);
            edge.hasExitTime = false; edge.duration = 0; edge.AddCondition(AnimatorConditionMode.If, 0, "Go");
            Native((playable, graph, bone) =>
            {
                graph.Evaluate(0); graph.Evaluate(.25f); var x = bone.localPosition.x;
                playable.SetTrigger("Go"); graph.Evaluate(0); graph.Evaluate(.25f);
                Assert.That(bone.localPosition.x, Is.EqualTo(x).Within(1e-5), "Native Write Defaults=false preserves a channel absent from the incoming clip.");
                Assert.That(x, Is.GreaterThan(.1f));
            });
            var result = ControllerBakePipeline.Build(source, new[] { a, b }, library);
            try
            {
                Assert.That(result.Success, Is.False, "Different sparse property layouts cannot be represented by fully baked rest-completed clips.");
                Assert.That(string.Join("\n", result.Diagnostics), Does.Contain("property layout"));
            }
            finally { if (result.Controller.IsCreated) result.Controller.Dispose(); }
        }
        [Test]
        public void Converter_AcceptsAnyStateWithNativeNonInterruptibleCrossfades()
        {
            source.AddParameter("Reset", AnimatorControllerParameterType.Trigger);
            var machine = source.layers[0].stateMachine; var first = machine.states[0].state; var second = machine.states[1].state;
            var fade = first.AddTransition(second); fade.hasExitTime = false; fade.hasFixedDuration = true; fade.duration = 1;
            fade.interruptionSource = TransitionInterruptionSource.None; fade.AddCondition(AnimatorConditionMode.If, 0, "Go");
            var third = machine.AddState("C"); third.motion = b; third.writeDefaultValues = false;
            var any = machine.AddAnyStateTransition(third); any.canTransitionToSelf = true; any.hasExitTime = false; any.hasFixedDuration = true; any.duration = .25f;
            any.AddCondition(AnimatorConditionMode.If, 0, "Reset");
            Native((playable, graph, bone) =>
            {
                graph.Evaluate(0); playable.SetTrigger("Go"); graph.Evaluate(0); graph.Evaluate(.25f);
                Assert.That(playable.GetNextAnimatorStateInfo(0).shortNameHash, Is.EqualTo(Animator.StringToHash("B")));
                playable.SetTrigger("Reset"); graph.Evaluate(.125f);
                Assert.That(playable.IsInTransition(0), Is.True);
                Assert.That(playable.GetNextAnimatorStateInfo(0).shortNameHash, Is.EqualTo(Animator.StringToHash("B")), "Unity 6000.6 None keeps the active fade atomic, including Any State.");
                graph.Evaluate(1); graph.Evaluate(0);
                Assert.That(playable.GetNextAnimatorStateInfo(0).shortNameHash, Is.EqualTo(Animator.StringToHash("C")), "Pending Any State trigger is evaluated after the atomic fade.");
            });
            var result = ControllerBakePipeline.Build(source, new[] { a, b }, library);
            try
            {
                Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
            }
            finally { if (result.Controller.IsCreated) result.Controller.Dispose(); }
        }
        void Native(System.Action<AnimatorControllerPlayable, PlayableGraph, Transform> action)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var go = new GameObject("Controller Contract Oracle", typeof(Animator));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            var bone = new GameObject("Bone").transform; bone.SetParent(go.transform, false);
            new GameObject("OtherBone").transform.SetParent(go.transform, false);
            var graph = PlayableGraph.Create("AnimationSystemDOTS.ControllerContractOracle");
            try
            {
                var playable = AnimatorControllerPlayable.Create(graph, source);
                AnimationPlayableOutput.Create(graph, "Oracle", go.GetComponent<Animator>()).SetSourcePlayable(playable);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual); graph.Play(); action(playable, graph, bone);
            }
            finally { if (graph.IsValid()) graph.Destroy(); Object.DestroyImmediate(go); UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
        }

        [TestCase("fixed")]
        [TestCase("normalized")]
        [TestCase("offset")]
        [TestCase("self")]
        [TestCase("any")]
        [TestCase("exit")]
        [TestCase("entry")]
        [TestCase("exitTime")]
        [TestCase("speedParameter")]
        [TestCase("cycle")]
        [TestCase("blockedTrigger")]
        [TestCase("reverse")]
        [TestCase("nonloopCycle")]
        [TestCase("negativeMultiplier")]
        [TestCase("reverseCycle")]
        [TestCase("exitTimeZero")]
        [TestCase("exitTimeManyLoops")]
        [TestCase("zeroDuration")]
        [TestCase("normalizedSpeedChange")]
        [TestCase("zeroSpeed")]
        [TestCase("lateExitCondition")]
        [TestCase("lateNonloopExitCondition")]
        [TestCase("normalizedSpeedChangeLate")]
        [TestCase("normalizedNegativeMultiplier")]
        public void ControllerTrace_MatchesNativeAnimatorWithManualSteps(string scenario)
        {
            var machine = source.layers[0].stateMachine;
            var first = machine.states[0].state; var second = machine.states[1].state;
            var edge = scenario == "exit" ? first.AddExitTransition() : scenario == "any" ? machine.AddAnyStateTransition(second)
                : first.AddTransition(scenario == "self" ? first : second);
            edge.hasExitTime = scenario == "exitTime" || scenario == "reverse" || scenario.StartsWith("exitTime") || scenario.StartsWith("late");
            edge.exitTime = scenario == "exitTimeZero" ? 0 : scenario == "exitTimeManyLoops" ? 2.5f : .75f;
            edge.hasFixedDuration = !scenario.StartsWith("normalized");
            edge.duration = scenario == "zeroDuration" ? 0 : .5f; edge.offset = scenario == "offset" ? .25f : 0;
            if (!edge.hasExitTime) edge.AddCondition(AnimatorConditionMode.If, 0, "Go");
            if (scenario.StartsWith("late")) edge.AddCondition(AnimatorConditionMode.If, 0, "Enabled");
            if (scenario == "blockedTrigger") edge.AddCondition(AnimatorConditionMode.Greater, 2, "Count");
            if (scenario == "normalized") first.speed = 2;
            if (scenario == "reverse" || scenario == "reverseCycle") first.speed = -1;
            if (scenario == "cycle" || scenario == "nonloopCycle" || scenario == "reverseCycle") first.cycleOffset = .25f;
            if (scenario == "nonloopCycle" || scenario == "lateNonloopExitCondition")
            {
                var settings = AnimationUtility.GetAnimationClipSettings(a); settings.loopTime = false; AnimationUtility.SetAnimationClipSettings(a, settings);
                library.Value.Clips[0].IsLooping = 0;
            }
            if (scenario == "speedParameter" || scenario == "negativeMultiplier" || scenario.StartsWith("normalizedSpeed") || scenario == "normalizedNegativeMultiplier" || scenario == "zeroSpeed")
            { first.speedParameterActive = true; first.speedParameter = "Speed"; }
            if (scenario == "exit" || scenario == "entry") machine.AddEntryTransition(second).AddCondition(AnimatorConditionMode.If, 0, "Enabled");
            if (scenario == "any") first.AddTransition(first).AddCondition(AnimatorConditionMode.If, 0, "Go");
            var defaults = source.parameters; defaults[0].defaultFloat = 1; defaults[2].defaultBool = scenario == "entry"; source.parameters = defaults;
            var result = ControllerBakePipeline.Build(source, new[] { a, b }, library);
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var go = new GameObject("Controller Oracle", typeof(Animator));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            var bone = new GameObject("Bone").transform; bone.SetParent(go.transform, false);
            var native = PlayableGraph.Create("AnimationSystemDOTS.ControllerOracle");
            World world = null;
            try
            {
                var playable = AnimatorControllerPlayable.Create(native, source);
                var output = AnimationPlayableOutput.Create(native, "Oracle", go.GetComponent<Animator>()); output.SetSourcePlayable(playable);
                native.SetTimeUpdateMode(DirectorUpdateMode.Manual); native.Play();
                world = new World("AnimationSystemDOTS Controller Oracle"); var em = world.EntityManager;
                var entity = em.CreateEntity(typeof(AnimationRig), typeof(AnimationController), typeof(ControllerLayerState), typeof(AnimationClock), typeof(AnimationPlaybackMode));
                em.SetComponentData(entity, new AnimationRig { Library = library });
                em.SetComponentData(entity, new AnimationController { Definition = result.Controller });
                em.SetComponentData(entity, new AnimationPlaybackMode { Value = PlaybackDriver.Controller });
                var values = em.AddBuffer<AnimationParameterValue>(entity);
                for (var i = 0; i < 4; i++) values.Add(new AnimationParameterValue { Float = result.Controller.Value.Parameters[i].DefaultFloat, Int = result.Controller.Value.Parameters[i].DefaultInt });
                em.AddBuffer<AnimationClipRequest>(entity);
                var step = em.CreateEntity(typeof(AnimationStepContext)); var system = world.GetOrCreateSystemManaged<ControllerAdvanceSystem>();
                values = em.GetBuffer<AnimationParameterValue>(entity);
                var controller = new AnimationController { Definition = result.Controller };
                playable.SetFloat("Speed", 1); AnimationApi.SetFloat(controller, values, new AnimationParameterId(0), 1);
                if (scenario == "entry") { playable.SetBool("Enabled", true); AnimationApi.SetBool(controller, values, new AnimationParameterId(2), true); }
                for (var tick = 0; tick < 30; tick++)
                {
                    values = em.GetBuffer<AnimationParameterValue>(entity);
                    var dt = tick == 0 ? 0f : .125f;
                    if (tick == 2 && !edge.hasExitTime)
                    {
                        playable.SetTrigger("Go"); AnimationApi.SetTrigger(controller, values, new AnimationParameterId(3));
                        playable.SetBool("Enabled", true); AnimationApi.SetBool(controller, values, new AnimationParameterId(2), true);
                    }
                    if (tick == 4 && scenario == "blockedTrigger") { playable.SetInteger("Count", 3); AnimationApi.SetInt(controller, values, new AnimationParameterId(1), 3); }
                    if (tick == 3 && scenario == "speedParameter") { playable.SetFloat("Speed", 2); AnimationApi.SetFloat(controller, values, new AnimationParameterId(0), 2); }
                    if (tick == 3 && scenario == "normalizedSpeedChange") { playable.SetFloat("Speed", 2); AnimationApi.SetFloat(controller, values, new AnimationParameterId(0), 2); }
                    if (tick == 4 && scenario == "normalizedSpeedChangeLate") { playable.SetFloat("Speed", 2); AnimationApi.SetFloat(controller, values, new AnimationParameterId(0), 2); }
                    if (tick == 3 && scenario == "normalizedNegativeMultiplier") { playable.SetFloat("Speed", -1); AnimationApi.SetFloat(controller, values, new AnimationParameterId(0), -1); }
                    if (tick == 3 && scenario == "zeroSpeed") { playable.SetFloat("Speed", 0); AnimationApi.SetFloat(controller, values, new AnimationParameterId(0), 0); }
                    if (tick == 8 && scenario.StartsWith("late")) { playable.SetBool("Enabled", true); AnimationApi.SetBool(controller, values, new AnimationParameterId(2), true); }
                    if (tick == 1 && scenario == "negativeMultiplier") { playable.SetFloat("Speed", -1); AnimationApi.SetFloat(controller, values, new AnimationParameterId(0), -1); }
                    native.Evaluate(dt);
                    em.SetComponentData(step, new AnimationStepContext { Tick = (ulong)tick + 1, DeltaTime = dt }); system.Update(); em.CompleteAllTrackedJobs();
                    var layer = em.GetComponentData<ControllerLayerState>(entity); var current = playable.GetCurrentAnimatorStateInfo(0); var next = playable.GetNextAnimatorStateInfo(0);
                    var context = scenario + " tick " + tick;
                    Assert.That(ControllerStateQuery.TryGet(layer, out var snapshot), Is.True, context);
                    Assert.That(current.fullPathHash, Is.EqualTo(Animator.StringToHash(result.Controller.Value.States[snapshot.CurrentState].Path.ToString())), context);
                    Assert.That(snapshot.NormalizedTime, Is.EqualTo(current.normalizedTime).Within(2e-5), context + " current time");
                    Assert.That(snapshot.IsInTransition, Is.EqualTo(playable.IsInTransition(0)), context + " transition");
                    if (snapshot.IsInTransition)
                    {
                        Assert.That(next.fullPathHash, Is.EqualTo(Animator.StringToHash(result.Controller.Value.States[snapshot.NextState].Path.ToString())), context + " next");
                        Assert.That(snapshot.NextNormalizedTime, Is.EqualTo(next.normalizedTime).Within(2e-5), context + " next time");
                        Assert.That(snapshot.TransitionProgress, Is.EqualTo(playable.GetAnimatorTransitionInfo(0).normalizedTime).Within(2e-5), context + " progress");
                    }
                    var requests = em.GetBuffer<AnimationClipRequest>(entity); var position = 0f;
                    foreach (var request in requests) position += ClipSampler.SampleBone(ref library.Value.Clips[request.Clip.Value], 1, request.Time, request.LoopBoundary).Position.x * request.Weight;
                    Assert.That(position, Is.EqualTo(bone.localPosition.x).Within(2e-4), context + " weighted pose");
                }
            }
            finally
            {
                if (native.IsValid()) native.Destroy(); if (world != null && world.IsCreated) world.Dispose();
                Object.DestroyImmediate(go); UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
                result.Controller.Dispose();
            }
        }
    }
}
