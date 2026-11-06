using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using AnimationSystemDOTS.Authoring.Verification;
using AnimationSystemDOTS.Authoring;

namespace AnimationSystemDOTS.Editor.Verification
{
    public enum OracleParameterKind { Float, Integer, Bool, Trigger, ResetTrigger }

    [Serializable]
    public struct OracleParameterWrite
    {
        public int SampleIndex;
        public string Name;
        public OracleParameterKind Kind;
        public float FloatValue;
        public int IntegerValue;
        public bool BoolValue;
    }

    public struct OracleCaptureRequest
    {
        public GameObject Prefab;
        public Avatar Avatar;
        public AnimationClip Clip;
        public RuntimeAnimatorController Controller;
        public double[] Times;
        public OracleParameterWrite[] Parameters;
        public string RootMotionPath;
        public bool CaptureEvents;
        public AnimationValidationSettings Settings;
    }

    [Serializable]
    public sealed class OracleFrame
    {
        public double Time;
        public Matrix4x4[] LocalMatrices;
        public Matrix4x4[] WorldMatrices;
        public Vector3 RootTranslation;
        public Quaternion RootRotation;
        public int[] StateHashes;
        public int[] NextStateHashes;
        public bool[] InTransition;
        public float[] NormalizedTimes;
        public float[] ParameterValues;
        public string[] ScalarBindings;
        public float[] ScalarValues;
        public int[] Events;
    }

    [Serializable]
    public sealed class OracleCaptureResult
    {
        public string UnityVersion;
        public string ConverterVersion;
        public string[] BonePaths;
        public string[] ParameterNames;
        public OracleEventDefinition[] EventDefinitions;
        public bool EventsCaptured;
        public string ValidationSettingsJson;
        public OracleFrame[] Frames;
    }

    [Serializable]
    public sealed class OracleEventDefinition
    {
        public string Clip;
        public float Time;
        public string Function;
        public string StringPayload;
        public int IntegerPayload;
        public float FloatPayload;
    }

    public static class UnityAnimationOracle
    {
        public static OracleCaptureResult Capture(in OracleCaptureRequest request)
        {
            Validate(request);
            // Native event dispatch is suppressed in preview scenes even in Play mode.
            var scene = request.CaptureEvents
                ? UnityEngine.SceneManagement.SceneManager.CreateScene("AnimationSystemDOTS Oracle Events " + Guid.NewGuid().ToString("N"))
                : EditorSceneManager.NewPreviewScene();
            var graph = default(PlayableGraph);
            GameObject instance = null;
            var temporaryAssets = new List<UnityEngine.Object>();
            try
            {
                instance = UnityEngine.Object.Instantiate(request.Prefab, new InstantiateParameters { scene = scene });
                instance.hideFlags = HideFlags.HideAndDontSave;
                instance.SetActive(true);
                var animator = instance.GetComponent<Animator>();
                if (animator == null) animator = instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = null;
                if (request.Avatar != null) animator.avatar = request.Avatar;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
                animator.fireEvents = request.CaptureEvents;
                var relay = instance.AddComponent<OracleEventRelay>();
                var bones = instance.GetComponentsInChildren<Transform>(true);
                var root = string.IsNullOrEmpty(request.RootMotionPath) ? instance.transform : instance.transform.Find(request.RootMotionPath);
                if (root == null) throw new ArgumentException("Root-motion bone not found: " + request.RootMotionPath);
                graph = PlayableGraph.Create("AnimationSystemDOTS Oracle");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "Pose", animator);
                var sources = request.Controller != null ? request.Controller.animationClips.Distinct().ToArray() : new[] { request.Clip };
                var definitions = new List<OracleEventDefinition>();
                var clips = sources.Select(source => CloneClip(source, definitions, temporaryAssets)).ToArray();
                var controller = default(AnimatorControllerPlayable);
                if (request.Controller != null)
                {
                    var overrides = new AnimatorOverrideController(request.Controller);
                    temporaryAssets.Add(overrides);
                    var mappings = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                    for (var i = 0; i < sources.Length; i++) mappings.Add(new KeyValuePair<AnimationClip, AnimationClip>(sources[i], clips[i]));
                    overrides.ApplyOverrides(mappings);
                    controller = AnimatorControllerPlayable.Create(graph, overrides);
                    output.SetSourcePlayable(controller);
                }
                else
                {
                    var playable = AnimationClipPlayable.Create(graph, clips[0]);
                    playable.SetApplyFootIK(false);
                    playable.SetApplyPlayableIK(false);
                    output.SetSourcePlayable(playable);
                }
                graph.Play();
                var parameters = controller.IsValid()
                    ? Enumerable.Range(0, controller.GetParameterCount()).Select(controller.GetParameter).ToArray()
                    : Array.Empty<AnimatorControllerParameter>();
                var bindings = clips.SelectMany(AnimationUtility.GetCurveBindings).Distinct().ToArray();
                var result = new OracleCaptureResult
                {
                    UnityVersion = Application.unityVersion,
                    ConverterVersion = "oracle-1",
                    BonePaths = bones.Select(t => AnimationUtility.CalculateTransformPath(t, instance.transform)).ToArray(),
                    ParameterNames = parameters.Select(p => p.name).ToArray(),
                    EventDefinitions = definitions.ToArray(),
                    EventsCaptured = request.CaptureEvents,
                    ValidationSettingsJson = request.Settings != null ? JsonUtility.ToJson(request.Settings) : null,
                    Frames = new OracleFrame[request.Times.Length]
                };
                var previousPosition = Vector3.zero;
                var previousRotation = Quaternion.identity;
                var previousTime = 0.0;
                for (var index = 0; index < request.Times.Length; index++)
                {
                    relay.Events.Clear();
                    if (request.Parameters != null)
                        foreach (var write in request.Parameters)
                            if (write.SampleIndex == index) WriteParameter(controller, parameters, write);
                    graph.Evaluate((float)(request.Times[index] - previousTime));
                    previousTime = request.Times[index];
                    var position = root == instance.transform ? root.localPosition : instance.transform.InverseTransformPoint(root.position);
                    var rotation = root == instance.transform ? root.localRotation : Quaternion.Inverse(instance.transform.rotation) * root.rotation;
                    var layerCount = controller.IsValid() ? controller.GetLayerCount() : 0;
                    var layers = Enumerable.Range(0, layerCount).ToArray();
                    var values = new float[bindings.Length];
                    for (var b = 0; b < bindings.Length; b++)
                        if (!AnimationUtility.GetFloatValue(instance, bindings[b], out values[b]))
                            throw new ArgumentException("Cannot capture scalar binding: " + bindings[b].path + "/" + bindings[b].propertyName);
                    result.Frames[index] = new OracleFrame
                    {
                        Time = request.Times[index],
                        LocalMatrices = bones.Select(t => Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale)).ToArray(),
                        WorldMatrices = bones.Select(t => t.localToWorldMatrix).ToArray(),
                        RootTranslation = index == 0 ? Vector3.zero : Quaternion.Inverse(previousRotation) * (position - previousPosition),
                        RootRotation = index == 0 ? Quaternion.identity : Quaternion.Inverse(previousRotation) * rotation,
                        StateHashes = layers.Select(l => controller.GetCurrentAnimatorStateInfo(l).fullPathHash).ToArray(),
                        NextStateHashes = layers.Select(l => controller.GetNextAnimatorStateInfo(l).fullPathHash).ToArray(),
                        InTransition = layers.Select(controller.IsInTransition).ToArray(),
                        NormalizedTimes = layers.Select(l => controller.GetCurrentAnimatorStateInfo(l).normalizedTime).ToArray(),
                        ParameterValues = parameters.Select(p => ReadParameter(controller, p)).ToArray(),
                        ScalarBindings = bindings.Select(b => b.path + ":" + b.type.FullName + ":" + b.propertyName).ToArray(),
                        ScalarValues = values, Events = relay.Events.ToArray()
                    };
                    previousPosition = position;
                    previousRotation = rotation;
                }
                return result;
            }
            finally
            {
                if (graph.IsValid())
                {
                    graph.Stop();
                    graph.Destroy();
                    // Unity destroys graphs during the player loop, including in Edit mode.
                    EditorApplication.QueuePlayerLoopUpdate();
                }
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                if (request.CaptureEvents) UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
                else EditorSceneManager.ClosePreviewScene(scene);
                foreach (var asset in temporaryAssets) if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        static void Validate(in OracleCaptureRequest request)
        {
            if (request.Prefab == null) throw new ArgumentException("Capture requires a prefab or hierarchy.");
            if (request.CaptureEvents && !Application.isPlaying)
                throw new ArgumentException("Native event capture requires Play mode; preview scenes suppress AnimationEvent dispatch.");
            if ((request.Clip == null) == (request.Controller == null))
                throw new ArgumentException("Capture requires exactly one clip or controller.");
            if (request.Clip != null && request.Clip.legacy) throw new ArgumentException("Legacy clips are unsupported: " + request.Clip.name);
            if (request.Times == null || request.Times.Length == 0) throw new ArgumentException("Explicit capture times are required.");
            if (request.Parameters != null)
                foreach (var write in request.Parameters)
                    if (write.SampleIndex < 0 || write.SampleIndex >= request.Times.Length)
                        throw new ArgumentException("Parameter SampleIndex is outside the capture sequence: " + write.Name + " [" + write.SampleIndex + "]");
            var previous = 0.0;
            foreach (var time in request.Times)
            {
                if (double.IsNaN(time) || double.IsInfinity(time) || time < previous || (request.Clip != null && time > request.Clip.length))
                    throw new ArgumentException("Capture times must be finite, non-negative, ordered and within the clip.");
                previous = time;
            }
        }

        static AnimationClip CloneClip(AnimationClip source, List<OracleEventDefinition> definitions, List<UnityEngine.Object> assets)
        {
            var clip = UnityEngine.Object.Instantiate(source);
            clip.hideFlags = HideFlags.HideAndDontSave;
            assets.Add(clip);
            var events = AnimationUtility.GetAnimationEvents(source);
            foreach (var e in events)
            {
                var id = definitions.Count;
                definitions.Add(new OracleEventDefinition { Clip = source.name, Time = e.time, Function = e.functionName,
                    StringPayload = e.stringParameter, IntegerPayload = e.intParameter, FloatPayload = e.floatParameter });
                e.functionName = nameof(OracleEventRelay.RecordOracleEvent);
                e.intParameter = id;
                e.messageOptions = SendMessageOptions.RequireReceiver;
            }
            AnimationUtility.SetAnimationEvents(clip, events);
            return clip;
        }

        static void WriteParameter(AnimatorControllerPlayable controller, AnimatorControllerParameter[] parameters, OracleParameterWrite write)
        {
            if (!controller.IsValid()) throw new ArgumentException("Parameter writes require a controller.");
            var parameter = Array.Find(parameters, p => p.name == write.Name);
            if (parameter == null) throw new ArgumentException("Unknown oracle parameter: " + write.Name);
            switch (write.Kind)
            {
                case OracleParameterKind.Float:
                    if (parameter.type != AnimatorControllerParameterType.Float || !float.IsFinite(write.FloatValue)) throw new ArgumentException("Invalid float parameter: " + write.Name);
                    controller.SetFloat(write.Name, write.FloatValue); break;
                case OracleParameterKind.Integer:
                    if (parameter.type != AnimatorControllerParameterType.Int) throw new ArgumentException("Invalid integer parameter: " + write.Name);
                    controller.SetInteger(write.Name, write.IntegerValue); break;
                case OracleParameterKind.Bool:
                    if (parameter.type != AnimatorControllerParameterType.Bool) throw new ArgumentException("Invalid bool parameter: " + write.Name);
                    controller.SetBool(write.Name, write.BoolValue); break;
                case OracleParameterKind.Trigger:
                case OracleParameterKind.ResetTrigger:
                    if (parameter.type != AnimatorControllerParameterType.Trigger) throw new ArgumentException("Invalid trigger parameter: " + write.Name);
                    if (write.Kind == OracleParameterKind.Trigger) controller.SetTrigger(write.Name); else controller.ResetTrigger(write.Name);
                    break;
                default: throw new ArgumentException("Unknown parameter write kind.");
            }
        }

        static float ReadParameter(AnimatorControllerPlayable controller, AnimatorControllerParameter parameter)
        {
            switch (parameter.type)
            {
                case AnimatorControllerParameterType.Float: return controller.GetFloat(parameter.name);
                case AnimatorControllerParameterType.Int: return controller.GetInteger(parameter.name);
                default: return controller.GetBool(parameter.name) ? 1 : 0;
            }
        }
    }
}
