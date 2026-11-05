using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using AnimationSystemDOTS.Editor.Verification;
using AnimationSystemDOTS.Authoring;

namespace AnimationSystemDOTS.Tests
{
    public class OracleCaptureTests
    {
        GameObject source;
        AnimationClip clip;

        [SetUp]
        public void SetUp()
        {
            source = new GameObject("OracleSource");
            var left = new GameObject("Left"); left.transform.SetParent(source.transform, false);
            var right = new GameObject("Right"); right.transform.SetParent(source.transform, false);
            new GameObject("Joint").transform.SetParent(left.transform, false);
            new GameObject("Joint").transform.SetParent(right.transform, false);
            clip = new AnimationClip { name = "LinearGeneric", frameRate = 30 };
            clip.SetCurve("Left/Joint", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 0, 1, 2));
            clip.SetCurve("Right/Joint", typeof(Transform), "m_LocalPosition.y", AnimationCurve.Linear(0, 3, 1, 5));
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(clip);
        }

        OracleCaptureRequest Request(params double[] times) => new OracleCaptureRequest
        { Prefab = source, Clip = clip, Times = times, RootMotionPath = "Left/Joint" };

        [Test]
        public void Capture_SamplesKnownCurvesAndKeepsDuplicateBoneNamesDistinct()
        {
            var result = UnityAnimationOracle.Capture(Request(0, 0.25, 0.75));
            var left = Array.IndexOf(result.BonePaths, "Left/Joint");
            var right = Array.IndexOf(result.BonePaths, "Right/Joint");
            Assert.That(left, Is.GreaterThanOrEqualTo(0));
            Assert.That(right, Is.GreaterThanOrEqualTo(0));
            Assert.That(result.Frames[1].LocalMatrices[left].m03, Is.EqualTo(0.5f).Within(1e-5));
            Assert.That(result.Frames[2].LocalMatrices[right].m13, Is.EqualTo(4.5f).Within(1e-5));
            Assert.That(result.Frames[1].RootTranslation.x, Is.EqualTo(0.5f).Within(1e-5));
            Assert.That(result.Frames[2].RootTranslation.x, Is.EqualTo(1).Within(1e-5));
        }

        [UnityTest]
        public IEnumerator Capture_RepeatsExactlyWithoutMutatingSourceOrLeakingGraphsOrScenes()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            yield return null;
            var scene = SceneManager.GetActiveScene();
            var dirty = scene.isDirty;
            var roots = scene.GetRootGameObjects().Select(x => x.GetEntityId()).ToArray();
            var graphs = UnityEditor.Playables.Utility.GetAllGraphs().Length;
            var scenes = SceneManager.sceneCount;
            var previews = EditorSceneManager.previewSceneCount;
            var before = EditorJsonUtility.ToJson(source.transform);
            var a = UnityAnimationOracle.Capture(Request(0, 0.25, 0.75));
            var b = UnityAnimationOracle.Capture(Request(0, 0.25, 0.75));
            Assert.That(JsonUtility.ToJson(b), Is.EqualTo(JsonUtility.ToJson(a)));
            Assert.That(EditorJsonUtility.ToJson(source.transform), Is.EqualTo(before));
            Assert.That(source.GetComponent<Animator>(), Is.Null);
            Assert.That(scene.isDirty, Is.EqualTo(dirty));
            Assert.That(scene.GetRootGameObjects().Select(x => x.GetEntityId()), Is.EqualTo(roots));
            Assert.That(SceneManager.GetActiveScene().handle, Is.EqualTo(scene.handle));
            Assert.That(SceneManager.sceneCount, Is.EqualTo(scenes));
            Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(previews));
            yield return null; // PlayableGraph.Destroy is deferred by Unity until the player loop.
            Assert.That(UnityEditor.Playables.Utility.GetAllGraphs().Count(g => g.IsPlaying() && g.GetEditorName() == "AnimationSystemDOTS Oracle"), Is.Zero);
        }

        [Test]
        public void Capture_RejectsNonMonotonicTimesBeforeChangingScene()
        {
            Assert.Throws<ArgumentException>(() => UnityAnimationOracle.Capture(Request(0.5, 0.1)));
            Assert.Throws<ArgumentException>(() => UnityAnimationOracle.Capture(Request(double.NaN)));
        }

        [TestCase(-1)]
        [TestCase(2)]
        public void Capture_RejectsParameterWritesOutsideSampleSequence(int sampleIndex)
        {
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects().Select(x => x.GetEntityId()).ToArray();
            var previews = EditorSceneManager.previewSceneCount;
            var request = Request(0, 0.25);
            request.Parameters = new[] { new OracleParameterWrite { SampleIndex = sampleIndex, Name = "MisspelledParameter", Kind = OracleParameterKind.Bool } };
            var exception = Assert.Throws<ArgumentException>(() => UnityAnimationOracle.Capture(request));
            Assert.That(exception.Message, Does.Contain("SampleIndex"));
            Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(previews));
            Assert.That(SceneManager.GetActiveScene().handle, Is.EqualTo(scene.handle));
            Assert.That(scene.GetRootGameObjects().Select(x => x.GetEntityId()), Is.EqualTo(roots));
        }

        [Test]
        public void Capture_RecordsExactValidationSettingsAndEditorVersion()
        {
            var settings = AssetDatabase.LoadAssetAtPath<AnimationValidationSettings>("Assets/AnimationSystemDOTS.Sandbox/Settings/AnimationValidationSettings.asset");
            var request = Request(0, 0.25); request.Settings = settings;
            var result = UnityAnimationOracle.Capture(request);
            Assert.That(result.UnityVersion, Is.EqualTo(Application.unityVersion));
            Assert.That(result.ValidationSettingsJson, Is.EqualTo(JsonUtility.ToJson(settings)));
            Assert.That(result.ValidationSettingsJson, Does.Contain("generic-two-bone-1"));
        }

        [UnityTest]
        public IEnumerator Capture_FailureDoesNotLeakPreviewSceneOrGraph()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            yield return null;
            var scenes = SceneManager.sceneCount;
            var previews = EditorSceneManager.previewSceneCount;
            var graphs = UnityEditor.Playables.Utility.GetAllGraphs().Length;
            var request = Request(0, 0.5); request.RootMotionPath = "Missing/Bone";
            Assert.Throws<ArgumentException>(() => UnityAnimationOracle.Capture(request));
            Assert.That(SceneManager.sceneCount, Is.EqualTo(scenes));
            Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(previews));
            yield return null;
            Assert.That(UnityEditor.Playables.Utility.GetAllGraphs().Length, Is.EqualTo(graphs));
        }

        [Test]
        public void Capture_ControllerManualSteppingRecordsParametersStatesAndTransitions()
        {
            var controller = new AnimatorController();
            var other = new AnimationClip { name = "Run" };
            try
            {
                other.SetCurve("Left/Joint", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 10, 1, 10));
                controller.AddLayer("Base");
                controller.AddParameter("Go", AnimatorControllerParameterType.Bool);
                var machine = controller.layers[0].stateMachine;
                var idle = machine.AddState("Idle"); idle.motion = clip;
                var run = machine.AddState("Run"); run.motion = other;
                machine.defaultState = idle;
                var transition = idle.AddTransition(run);
                transition.hasExitTime = false; transition.duration = 0;
                transition.AddCondition(AnimatorConditionMode.If, 0, "Go");
                var request = Request(0, 0.25, 0.5, 0.75);
                request.Clip = null; request.Controller = controller;
                request.Parameters = new[] { new OracleParameterWrite { SampleIndex = 2, Name = "Go", Kind = OracleParameterKind.Bool, BoolValue = true } };
                var result = UnityAnimationOracle.Capture(request);
                var repeat = UnityAnimationOracle.Capture(request);
                Assert.That(result.Frames[0].StateHashes[0], Is.EqualTo(Animator.StringToHash("Base.Idle")));
                Assert.That(result.Frames[3].StateHashes[0], Is.EqualTo(Animator.StringToHash("Base.Run")));
                Assert.That(result.Frames[3].ParameterValues[0], Is.EqualTo(1));
                Assert.That(result.Frames[3].LocalMatrices[Array.IndexOf(result.BonePaths, "Left/Joint")].m03, Is.EqualTo(10).Within(1e-5));
                Assert.That(JsonUtility.ToJson(repeat), Is.EqualTo(JsonUtility.ToJson(result)));
            }
            finally
            {
                // State machine subobjects are created in memory by UnityEditor.Animations.
                foreach (var layer in controller.layers)
                {
                    foreach (var state in layer.stateMachine.states)
                    {
                        foreach (var transition in state.state.transitions) UnityEngine.Object.DestroyImmediate(transition);
                        UnityEngine.Object.DestroyImmediate(state.state);
                    }
                    UnityEngine.Object.DestroyImmediate(layer.stateMachine);
                }
                UnityEngine.Object.DestroyImmediate(controller);
                UnityEngine.Object.DestroyImmediate(other);
            }
        }

        [UnityTest, Order(int.MaxValue)]
        public IEnumerator Capture_ReportsScalarValuesAndNativeEventTraceWithoutChangingClip()
        {
            // Unity suppresses native AnimationEvent dispatch in Edit mode.
            TearDown();
            yield return new EnterPlayMode();
            SetUp();
            AnimationUtility.SetAnimationEvents(clip, new[]
            {
                new AnimationEvent { time = 0.25f, functionName = "AnyOriginalCallback", intParameter = 17 },
                new AnimationEvent { time = 0.75f, functionName = "AnotherCallback", intParameter = 23 }
            });
            var before = EditorJsonUtility.ToJson(clip);
            var request = Request(0, 0.3, 0.6, 0.9); request.CaptureEvents = true;
            var result = UnityAnimationOracle.Capture(request);
            Assert.That(result.Frames[1].Events, Is.EqualTo(new[] { 0 }));
            Assert.That(result.Frames[2].Events, Is.Empty);
            Assert.That(result.Frames[3].Events, Is.EqualTo(new[] { 1 }));
            var binding = Array.FindIndex(result.Frames[1].ScalarBindings, x => x.Contains("Left/Joint") && x.EndsWith("m_LocalPosition.x"));
            Assert.That(binding, Is.GreaterThanOrEqualTo(0));
            Assert.That(result.Frames[1].ScalarValues[binding], Is.EqualTo(0.6f).Within(1e-5));
            Assert.That(EditorJsonUtility.ToJson(clip), Is.EqualTo(before));
            Assert.That(result.EventsCaptured, Is.True);
            Assert.That(JsonUtility.ToJson(UnityAnimationOracle.Capture(request)), Is.EqualTo(JsonUtility.ToJson(result)));
            // Scene unloading and graph destruction may complete on different native frames.
            for (var frame = 0; frame < 120; frame++)
            {
                var pendingGraphs = UnityEditor.Playables.Utility.GetAllGraphs().Count(g => g.GetEditorName() == "AnimationSystemDOTS Oracle");
                var pendingScenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Count(s => s.name.StartsWith("AnimationSystemDOTS Oracle Events"));
                if (pendingGraphs == 0 && pendingScenes == 0) break;
                yield return null;
            }
            Assert.That(UnityEditor.Playables.Utility.GetAllGraphs().Count(g => g.GetEditorName() == "AnimationSystemDOTS Oracle"), Is.Zero);
            Assert.That(Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Count(s => s.name.StartsWith("AnimationSystemDOTS Oracle Events")), Is.Zero);
            TearDown();
            yield return new ExitPlayMode();
        }
    }
}
