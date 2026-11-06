using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimationSystemDOTS.Authoring;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AnimationSystemDOTS.Sandbox.Editor
{
    public static class AnimatorValidationFixtures
    {
        public static string ControllerPath => SandboxPaths.Root + "Fixtures/Bots100.controller";
        public static string SettingsPath => SandboxPaths.Root + "Fixtures/AnimatorValidationSettings.asset";
        public static string BakeSettingsPath => SandboxPaths.Root + "Fixtures/AnimatorValidationBake.asset";
        public static string NativePrefabPath => SandboxPaths.Root + "Fixtures/AnimatorValidationGO.prefab";
        public static string EcsPrefabPath => SandboxPaths.Root + "Fixtures/AnimatorValidationECS.prefab";
        public static string ScenePath => SandboxPaths.Root + "Scenes/05_AnimatorGO.unity";

        [MenuItem("Tools/Animation System DOTS/Create Animator Validation GO Scene")]
        public static void Create()
        {
            var original = SceneManager.GetActiveScene();
            if (original.isDirty || string.IsNullOrEmpty(original.path)) throw new InvalidOperationException("Save the current scene before generating validation assets.");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) ?? BuildController();
            var settings = AssetDatabase.LoadAssetAtPath<AnimatorValidationSettings>(SettingsPath);
            if (settings == null) throw new InvalidOperationException("Animator validation settings are missing.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NativePrefabPath) ?? BuildNativePrefab(controller, settings);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(EcsPrefabPath) == null) BuildEcsPrefab(controller);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            try
            {
                var actor = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene); actor.name = "Native Animator Player";
                var character = actor.GetComponent<NativeAnimatorCharacter>();
                var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(AnimatorValidationOrbit)).GetComponent<Camera>();
                camera.tag = "MainCamera"; camera.nearClipPlane = .03f; camera.farClipPlane = 150;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .09f, .13f);
                var orbit = camera.GetComponent<AnimatorValidationOrbit>(); orbit.Target = actor.transform; character.Orbit = orbit;
                camera.transform.SetPositionAndRotation(new Vector3(0, 2.4f, -4.5f), Quaternion.Euler(18, 0, 0));
                var adapter = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("AnimationSystemDOTS.Sandbox.Input.NativeAnimatorPlayer")).FirstOrDefault(t => t != null);
                if (adapter != null)
                {
                    var input = actor.AddComponent(adapter); var serialized = new SerializedObject(input);
                    serialized.FindProperty("Character").objectReferenceValue = character; serialized.FindProperty("Orbit").objectReferenceValue = orbit; serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                var light = new GameObject("Directional Light", typeof(Light)).GetComponent<Light>();
                light.type = LightType.Directional; light.intensity = 2; light.shadows = LightShadows.Soft; light.transform.rotation = Quaternion.Euler(45, -30, 0);
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.35f, .38f, .45f);
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Arena — analytic floor and bounds ±24m";
                floor.transform.position = new Vector3(0, -.05f, 0); floor.transform.localScale = new Vector3(50, .1f, 50);
                floor.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(SandboxPaths.Root + "Fixtures/Materials/Stage3Floor.mat");
                for (var i = -4; i <= 4; i++)
                {
                    var marker = GameObject.CreatePrimitive(PrimitiveType.Cube); marker.name = "Arena distance marker " + i * 5;
                    marker.transform.position = new Vector3(i * 5, .005f, 0); marker.transform.localScale = new Vector3(.035f, .01f, 48);
                    Object.DestroyImmediate(marker.GetComponent<Collider>());
                }
                EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            }
        }

        static AnimatorController BuildController()
        {
            var inventory = JsonUtility.FromJson<AnimatorValidationAssets.Inventory>(File.ReadAllText(AnimatorValidationAssets.Root + "MotionInventory.json"));
            AnimationClip Clip(string filename)
            {
                var entry = inventory.motions.Single(m => Path.GetFileName(m.source) == filename);
                return AssetDatabase.LoadAllAssetsAtPath(AnimatorValidationAssets.Root + Path.GetFileName(entry.asset)).OfType<AnimationClip>().Single(c => !c.name.StartsWith("__preview", StringComparison.Ordinal));
            }
            var idle = new[] { Clip("idle.fbx"), Clip("Y Bot@Idle.fbx"), Clip("Y Bot@Fighting Idle.fbx"), Clip("Y Bot@Idle Fight.fbx") };
            var walk = new[] { Clip("walking.fbx"), Clip("Y Bot@Walking.fbx"), Clip("Y Bot@Walking (1).fbx") };
            var run = new[] { Clip("standard run.fbx"), Clip("Y Bot@Running.fbx"), Clip("Y Bot@Running (1).fbx"), Clip("Y Bot@Slow Run.fbx"), Clip("Y Bot@Fast Run.fbx") };
            var sprint = new[] { Clip("Y Bot@Sprint.fbx"), Clip("Y Bot@Sprint Forward.fbx"), Clip("Y Bot@Sprint Backward.fbx") };
            var leftWalk = Clip("left strafe walking.fbx"); var rightWalk = Clip("right strafe walking.fbx");
            var leftRun = Clip("left strafe.fbx"); var rightRun = Clip("right strafe.fbx");
            var backward = new[] { Clip("Y Bot@Run Backward.fbx"), Clip("Y Bot@Running Backward.fbx") };
            var jump = new[] { Clip("Y Bot@Jump.fbx"), Clip("Y Bot@Jump (1).fbx"), Clip("Y Bot@Jump (2).fbx"), Clip("Y Bot@Running Jump.fbx") };
            var wave = new[] { Clip("Y Bot@Waving.fbx"), Clip("Y Bot@Waving (1).fbx"), Clip("Y Bot@Waving (2).fbx") };
            var punch = new[] { Clip("Y Bot@Punching.fbx"), Clip("Y Bot@Punch Combo.fbx") };
            var death = new[] { Clip("Y Bot@Dying.fbx"), Clip("Y Bot@Dying (1).fbx"), Clip("Y Bot@Standing Death Left 02.fbx") };
            var reaction = Clip("Y Bot@Reaction.fbx");
            var additiveReaction = AdditiveCopy(reaction, reaction, "AnimatorValidationReaction");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            foreach (var name in new[] { "Speed", "MoveX", "MoveY", "Variation", "Cycle", "PoseTime", "DirectIdle", "DirectWalk", "DirectRun" }) controller.AddParameter(name, AnimatorControllerParameterType.Float);
            controller.AddParameter(new AnimatorControllerParameter { name = "PlaybackSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1 });
            controller.AddParameter("TreeMode", AnimatorControllerParameterType.Int); controller.AddParameter("Variant", AnimatorControllerParameterType.Int);
            controller.AddParameter(new AnimatorControllerParameter { name = "Grounded", type = AnimatorControllerParameterType.Bool, defaultBool = true });
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool); controller.AddParameter("Scrub", AnimatorControllerParameterType.Bool);
            foreach (var name in new[] { "Jump", "Wave", "Punch", "React", "Die" }) controller.AddParameter(name, AnimatorControllerParameterType.Trigger);
            var root = controller.layers[0].stateMachine; var locomotion = root.AddStateMachine("Locomotion"); var jumps = root.AddStateMachine("Jump");
            var idleTree = Variants(controller, "Idle Variants", idle); var walkTree = Variants(controller, "Walk Variants", walk); var runTree = Variants(controller, "Run Variants", run);
            var sprintTree = Variants(controller, "Sprint Variants", sprint.Take(2).ToArray()); var backwardTree = Variants(controller, "Backward Variants", backward);
            var speed = Tree(controller, "Nested Speed Locomotion", BlendTreeType.Simple1D, "Speed");
            speed.AddChild(idleTree, 0); speed.AddChild(walkTree, 1.5f); speed.AddChild(runTree, 3); speed.AddChild(sprintTree, 6);
            var simple = Tree(controller, "Simple Directional Locomotion", BlendTreeType.SimpleDirectional2D);
            simple.AddChild(idleTree, Vector2.zero); simple.AddChild(runTree, new Vector2(0, 3)); simple.AddChild(backwardTree, new Vector2(0, -3));
            simple.AddChild(leftRun, new Vector2(-3, 0)); simple.AddChild(rightRun, new Vector2(3, 0));
            var directional = Tree(controller, "Freeform Directional Locomotion", BlendTreeType.FreeformDirectional2D);
            var cartesian = Tree(controller, "Freeform Cartesian Locomotion", BlendTreeType.FreeformCartesian2D);
            foreach (var tree in new[] { directional, cartesian })
            {
                tree.AddChild(idleTree, Vector2.zero); tree.AddChild(walkTree, new Vector2(0, 1.5f)); tree.AddChild(runTree, new Vector2(0, 3)); tree.AddChild(sprintTree, new Vector2(0, 6));
                tree.AddChild(leftWalk, new Vector2(-1.5f, 0)); tree.AddChild(leftRun, new Vector2(-3, 0)); tree.AddChild(rightWalk, new Vector2(1.5f, 0)); tree.AddChild(rightRun, new Vector2(3, 0));
                tree.AddChild(backwardTree, new Vector2(0, -3)); tree.AddChild(sprint[2], new Vector2(0, -6));
            }
            var direct = Tree(controller, "Normalized Direct Locomotion", BlendTreeType.Direct);
            direct.children = new[] { new ChildMotion { motion = idleTree, directBlendParameter = "DirectIdle", timeScale = 1 },
                new ChildMotion { motion = walkTree, directBlendParameter = "DirectWalk", timeScale = 1 }, new ChildMotion { motion = runTree, directBlendParameter = "DirectRun", timeScale = 1 } };
            var directSettings = new SerializedObject(direct); directSettings.FindProperty("m_NormalizedBlendValues").boolValue = true; directSettings.ApplyModifiedPropertiesWithoutUndo();
            var modes = new Motion[] { speed, simple, directional, cartesian, direct }.Select((motion, index) => State(locomotion, "Tree" + index, motion, true)).ToArray();
            var scrub = State(locomotion, "PoseScrub", wave[0]); scrub.timeParameterActive = true; scrub.timeParameter = "PoseTime";
            locomotion.defaultState = modes[0]; root.defaultState = modes[0];
            var scrubEntry = locomotion.AddEntryTransition(scrub); scrubEntry.AddCondition(AnimatorConditionMode.If, 0, "Scrub");
            for (var i = 1; i < modes.Length; i++)
            { var entry = locomotion.AddEntryTransition(modes[i]); entry.AddCondition(AnimatorConditionMode.Equals, i, "TreeMode"); entry.AddCondition(AnimatorConditionMode.IfNot, 0, "Scrub"); }
            foreach (var from in modes)
            {
                var takeoff = from.AddTransition(jumps); Configure(takeoff, .12f); takeoff.AddCondition(AnimatorConditionMode.If, 0, "Jump"); takeoff.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
                var pose = Edge(from, scrub, .18f); pose.AddCondition(AnimatorConditionMode.If, 0, "Scrub");
                for (var i = 0; i < modes.Length; i++) if (modes[i] != from)
                {
                    var edge = Edge(from, modes[i], .25f); edge.hasFixedDuration = false; edge.interruptionSource = TransitionInterruptionSource.SourceThenDestination; edge.orderedInterruption = true;
                    edge.AddCondition(AnimatorConditionMode.Equals, i, "TreeMode"); edge.AddCondition(AnimatorConditionMode.IfNot, 0, "Scrub"); edge.AddCondition(AnimatorConditionMode.If, 0, "Grounded"); edge.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
                }
            }
            var resume = scrub.AddTransition(locomotion); Configure(resume, .18f); resume.AddCondition(AnimatorConditionMode.IfNot, 0, "Scrub");
            var jumpStates = jump.Select((clip, index) => State(jumps, "Jump" + index, clip)).ToArray(); jumps.defaultState = jumpStates[0];
            var runningEntry = jumps.AddEntryTransition(jumpStates[3]); runningEntry.AddCondition(AnimatorConditionMode.Greater, 3, "Speed");
            for (var i = 1; i < 3; i++) jumps.AddEntryTransition(jumpStates[i]).AddCondition(AnimatorConditionMode.Equals, i, "Variant");
            for (var i = 0; i < jumpStates.Length; i++)
            {
                jumpStates[i].speed = jump[i].length / (2 * 6f / 18);
                var landing = jumpStates[i].AddTransition(locomotion); Configure(landing, .12f); landing.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            }
            for (var i = 0; i < death.Length; i++)
            { var state = State(root, "Death" + i, death[i]); var edge = Any(root, state, "Die", i, .12f); edge.interruptionSource = TransitionInterruptionSource.None; edge.canTransitionToSelf = false; }
            root.defaultState = modes[0];
            controller.AddLayer("Upper Body"); controller.AddLayer("Additive Reaction"); var layers = controller.layers;
            layers[1].avatarMask = UpperMask("AnimatorValidationUpper"); layers[1].defaultWeight = 0;
            layers[2].avatarMask = layers[1].avatarMask; layers[2].blendingMode = AnimatorLayerBlendingMode.Additive; layers[2].defaultWeight = 0;
            var upper = layers[1].stateMachine; var upperIdle = State(upper, "UpperIdle", idle[0]); upperIdle.speed = 0; upper.defaultState = upperIdle;
            var actions = upper.AddStateMachine("Actions");
            for (var i = 0; i < wave.Length; i++)
            {
                var state = State(actions, "Wave" + i, wave[i]); Any(upper, state, "Wave", i, .16f); Exit(state, .95f, .18f);
            }
            for (var i = 0; i < punch.Length; i++)
            { var state = State(actions, "Punch" + i, punch[i]); Any(upper, state, "Punch", i, .12f); if (i == 0) Any(upper, state, "Punch", 2, .12f); Exit(state, .95f, .18f); }
            upper.AddStateMachineTransition(actions, upperIdle);
            var recoil = layers[2].stateMachine; var zero = State(recoil, "AdditiveIdle", additiveReaction); zero.speed = 0; recoil.defaultState = zero;
            var react = State(recoil, "React", additiveReaction); var reactEdge = recoil.AddAnyStateTransition(react); Configure(reactEdge, .12f);
            reactEdge.interruptionSource = TransitionInterruptionSource.SourceThenDestination; reactEdge.orderedInterruption = false; reactEdge.AddCondition(AnimatorConditionMode.If, 0, "React");
            var release = Edge(react, zero, .18f); release.hasExitTime = true; release.exitTime = .95f;
            controller.layers = layers;
            var settings = ScriptableObject.CreateInstance<AnimatorValidationSettings>(); settings.Controller = controller;
            settings.WaveDurations = new Vector3(wave[0].length, wave[1].length, wave[2].length); settings.PunchDurations = new Vector2(punch[0].length, punch[1].length); settings.ReactionDuration = reaction.length;
            var allStates = layers.SelectMany(l => States(l.stateMachine)).ToArray(); settings.StateNames = allStates.Select(s => s.name).ToArray(); settings.StateHashes = allStates.Select(s => Animator.StringToHash(s.name)).ToArray();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            var bake = ScriptableObject.CreateInstance<AnimationBakeSettings>(); bake.SampleRate = 960; bake.PositionTolerance = .002f; bake.AngleToleranceDegrees = .25f;
            bake.HumanoidPoseMode = HumanoidPoseMode.PreciseMuscles; bake.FixtureRevision = "animator-validation-1"; AssetDatabase.CreateAsset(bake, BakeSettingsPath);
            EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets(); return controller;
        }
        static BlendTree Tree(AnimatorController controller, string name, BlendTreeType type, string parameter = "MoveX")
        {
            var tree = new BlendTree { name = name, blendType = type, blendParameter = parameter, blendParameterY = "MoveY", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, controller); return tree;
        }
        static BlendTree Variants(AnimatorController controller, string name, AnimationClip[] clips)
        { var tree = Tree(controller, name, BlendTreeType.Simple1D, "Variation"); for (var i = 0; i < clips.Length; i++) tree.AddChild(clips[i], i * 4f / (clips.Length - 1)); return tree; }
        static AnimatorState State(AnimatorStateMachine machine, string name, Motion motion, bool cycle = false)
        {
            var index = machine.states.Length;
            var state = machine.AddState(name, new Vector3(240 + index % 3 * 260, 80 + index / 3 * 130)); state.motion = motion; state.writeDefaultValues = false; state.speedParameterActive = true; state.speedParameter = "PlaybackSpeed";
            state.cycleOffsetParameterActive = cycle; state.cycleOffsetParameter = "Cycle"; return state;
        }
        static void Configure(AnimatorStateTransition edge, float duration)
        { edge.hasExitTime = false; edge.hasFixedDuration = true; edge.duration = duration; }
        static AnimatorStateTransition Edge(AnimatorState from, AnimatorState to, float duration)
        { var edge = from.AddTransition(to); Configure(edge, duration); return edge; }
        static AnimatorStateTransition Any(AnimatorStateMachine machine, AnimatorState state, string trigger, int variant, float duration)
        {
            var edge = machine.AddAnyStateTransition(state); Configure(edge, duration); edge.interruptionSource = TransitionInterruptionSource.SourceThenDestination; edge.orderedInterruption = false;
            edge.AddCondition(AnimatorConditionMode.If, 0, trigger); edge.AddCondition(AnimatorConditionMode.Equals, variant, "Variant"); return edge;
        }
        static void Exit(AnimatorState state, float phase, float duration)
        { var edge = state.AddExitTransition(); Configure(edge, duration); edge.hasExitTime = true; edge.exitTime = phase; }
        static AvatarMask UpperMask(string name)
        {
            var mask = new AvatarMask { name = name };
            for (var i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            foreach (var part in new[] { AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers }) mask.SetHumanoidBodyPartActive(part, true);
            AssetDatabase.CreateAsset(mask, SandboxPaths.Root + "Fixtures/" + name + ".mask"); return mask;
        }
        static AnimationClip AdditiveCopy(AnimationClip source, AnimationClip reference, string name)
        {
            var path = SandboxPaths.Root + "Fixtures/" + name + ".anim"; var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path); if (existing != null) return existing;
            var clip = Object.Instantiate(source); clip.name = name; var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.hasAdditiveReferencePose = true; settings.additiveReferencePoseClip = reference; settings.additiveReferencePoseTime = 0; AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, path); return clip;
        }
        static GameObject BuildNativePrefab(AnimatorController controller, AnimatorValidationSettings settings)
        {
            var scene = EditorSceneManager.NewPreviewScene(); GameObject actor = null;
            try
            {
                actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HumanoidCrowdAssets.ModelPath), new InstantiateParameters { scene = scene }); actor.name = "Animator Validation Character";
                var animator = actor.GetComponent<Animator>(); animator.runtimeAnimatorController = controller; animator.applyRootMotion = false; animator.updateMode = AnimatorUpdateMode.Fixed; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader");
                if (shader == null) throw new InvalidOperationException("URP Lit is required for the native validation scene.");
                var material = new Material(shader) { name = "BaldMaleNative" }; material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(HumanoidCrowdAssets.Root + "Textures/MaleBaseColor.png"));
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(HumanoidCrowdAssets.Root + "Textures/MaleNormal.png")); material.EnableKeyword("_NORMALMAP"); material.SetFloat("_Smoothness", .25f);
                AssetDatabase.CreateAsset(material, SandboxPaths.Root + "Fixtures/Materials/BaldMaleNative.mat");
                foreach (var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>()) renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMesh.subMeshCount).ToArray();
                var driver = actor.AddComponent<NativeAnimatorCharacter>(); driver.Animator = animator; driver.Settings = settings;
                return PrefabUtility.SaveAsPrefabAsset(actor, NativePrefabPath);
            }
            finally { if (actor != null) Object.DestroyImmediate(actor); EditorSceneManager.ClosePreviewScene(scene); }
        }
        static void BuildEcsPrefab(AnimatorController controller)
        {
            var actor = PrefabUtility.LoadPrefabContents(NativePrefabPath);
            try
            {
                Object.DestroyImmediate(actor.GetComponent<NativeAnimatorCharacter>());
                actor.GetComponent<Animator>().runtimeAnimatorController = null;
                var material = AssetDatabase.LoadAssetAtPath<Material>(HumanoidCrowdAssets.Root + "Materials/BaldMaleCompute.mat");
                foreach (var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>()) renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMesh.subMeshCount).ToArray();
                actor.AddComponent<AnimationRigAuthoring>().Settings = AssetDatabase.LoadAssetAtPath<AnimationBakeSettings>(BakeSettingsPath);
                actor.AddComponent<AnimationControllerAuthoring>().Controller = controller;
                PrefabUtility.SaveAsPrefabAsset(actor, EcsPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(actor); }
        }
        static IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
        { foreach (var child in machine.states) yield return child.state; foreach (var child in machine.stateMachines) foreach (var state in States(child.stateMachine)) yield return state; }
    }
}
