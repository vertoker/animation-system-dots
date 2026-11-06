using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AnimationSystemDOTS.Editor.Baking
{
    public struct ControllerBakeResult
    {
        public bool Success;
        public BlobAssetReference<AnimatorControllerBlob> Controller;
        public string[] Diagnostics;
    }
    public static class ControllerBakePipeline
    {
        public static ControllerBakeResult Build(RuntimeAnimatorController source, IReadOnlyList<AnimationClip> clips,
            BlobAssetReference<AnimationLibraryBlob> library, GameObject target = null, BlobAssetReference<RigDefinitionBlob> rig = default)
        {
            var path = source != null ? AssetDatabase.GetAssetPath(source) : "<null controller>";
            if (string.IsNullOrEmpty(path)) path = source.name;
            var compiledMasks = new ControllerMaskBake();
            try
            {
                var controller = source as AnimatorController;
                Require(controller != null, path, "Only AnimatorController assets are supported; override controllers are not converted.");
                Require(library.IsCreated && library.Value.Version == AnimationBakeVersion.Schema, path, "A current animation library is required.");
                Require(clips != null && clips.Count == library.Value.Clips.Length, path, "Clip mapping does not match the library.");
                var layers = controller.layers;
                Require(layers.Length > 0, path, "At least one layer is required.");
                Require(!rig.IsCreated || rig.Value.ContentKey == library.Value.RigKey, path, "Mask target rig differs from the animation library.");
                var assetPath = path;
                var definitions = Parameters(controller, path, out var parameterIds);
                var motions = new ControllerMotionBake(clips, parameterIds, definitions);
                var states = new List<ControllerStateDefinition>();
                var transitions = new List<ControllerTransitionDefinition>();
                var conditions = new List<ControllerCondition>();
                var layerDefinitions = new List<ControllerLayerDefinition>();
                var layerNames = new HashSet<string>(StringComparer.Ordinal);
                for (var layerIndex = 0; layerIndex < layers.Length; layerIndex++)
                {
                    var layer = layers[layerIndex]; path = assetPath + "/" + layer.name;
                    Require(layerNames.Add(layer.name), path, "Duplicate layer name.");
                    Require(!layer.iKPass, path, "Animator IK Pass is unsupported.");
                    Require(layer.syncedLayerIndex < layerIndex && layer.syncedLayerIndex >= -1, path, "Synced layers must reference an earlier layer.");
                    Require(math.isfinite(layer.defaultWeight) && layer.defaultWeight >= 0 && layer.defaultWeight <= 1, path, "Layer weight must be finite and within [0,1].");
                    Require(layerIndex != 0 || layer.blendingMode == AnimatorLayerBlendingMode.Override && layer.syncedLayerIndex < 0, path, "Base layer must be an unsynced Override layer.");
                    var sourceLayer = layerIndex;
                    while (layers[sourceLayer].syncedLayerIndex >= 0) sourceLayer = layers[sourceLayer].syncedLayerIndex;
                    var machine = layers[sourceLayer].stateMachine;
                    Require(machine != null, path, "Missing state machine.");
                    Require(machine.stateMachines.Length == 0, path, "Nested state machines are unsupported.");
                    Require(machine.behaviours.Length == 0, path, "StateMachineBehaviour callbacks are unsupported.");
                    var children = machine.states;
                    Require(children.Length > 0, path, "At least one clip state is required.");
                    var stateStart = states.Count;
                    var stateIds = new Dictionary<AnimatorState, int>();
                    for (var i = 0; i < children.Length; i++) stateIds.Add(children[i].state, stateStart + i);
                    Require(machine.defaultState != null && stateIds.ContainsKey(machine.defaultState), path, "Default state is outside the root state machine.");
                    var anyStart = transitions.Count;
                    AddTransitions(machine.anyStateTransitions, true, stateIds, parameterIds, definitions, transitions, conditions, path + "/Any State");
                    var anyCount = transitions.Count - anyStart;
                    var entryStart = transitions.Count;
                    foreach (var edge in Active(machine.entryTransitions))
                    {
                        Require(edge.destinationStateMachine == null && !edge.isExit && edge.destinationState != null && stateIds.ContainsKey(edge.destinationState), path + "/Entry", "Entry must target a root clip state.");
                        var start = conditions.Count;
                        AddConditions(edge.conditions, parameterIds, definitions, conditions, path + "/Entry");
                        transitions.Add(new ControllerTransitionDefinition { Destination = stateIds[edge.destinationState], ConditionStart = start, ConditionCount = conditions.Count - start });
                    }
                    var entryCount = transitions.Count - entryStart;
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    for (var i = 0; i < children.Length; i++)
                    {
                        var state = children[i].state; var statePath = path + "/" + state.name;
                        Require(names.Add(state.name), statePath, "Duplicate state name.");
                        Require(state.behaviours.Length == 0, statePath, "StateMachineBehaviour callbacks are unsupported.");
                        Require(!state.writeDefaultValues, statePath, "Write Defaults is unsupported; disable it on this state.");
                        Require(!state.iKOnFeet && !state.mirror && !state.mirrorParameterActive && !state.timeParameterActive
                            && !state.cycleOffsetParameterActive, statePath, "IK, mirror, motion-time and cycle-offset parameters are unsupported.");
                        Require(math.isfinite(state.speed) && math.isfinite(state.cycleOffset), statePath, "Speed and cycle offset must be finite.");
                        var effectiveMotion = EffectiveMotion(layers, layerIndex, state);
                        if (layer.syncedLayerIndex >= 0) Require((layer.GetOverrideBehaviours(state)?.Length ?? 0) == 0, statePath, "Override StateMachineBehaviour callbacks are unsupported.");
                        var motion = effectiveMotion as AnimationClip;
                        var additive = layer.blendingMode == AnimatorLayerBlendingMode.Additive;
                        var motionId = motions.Add(effectiveMotion, statePath, additive,
                            additive && (layer.avatarMask == null || layer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Root)));
                        var clipId = -1;
                        for (var c = 0; c < clips.Count; c++) if (clips[c] == motion) { clipId = c; break; }
                        var speedParameter = -1;
                        if (state.speedParameterActive)
                        {
                            Require(parameterIds.TryGetValue(state.speedParameter, out speedParameter)
                                && definitions[speedParameter].Type == AnimationParameterType.Float, statePath, "Speed multiplier requires an existing Float parameter.");
                        }
                        var start = transitions.Count;
                        AddTransitions(state.transitions, false, stateIds, parameterIds, definitions, transitions, conditions, statePath);
                        states.Add(new ControllerStateDefinition { Path = Name(layer.name + "." + state.name, statePath), Clip = new ClipId(clipId), Motion = motionId,
                            Duration = clipId >= 0 ? library.Value.Clips[clipId].Duration : 0, Speed = state.speed, CycleOffset = clipId < 0 || library.Value.Clips[clipId].IsLooping != 0 ? state.cycleOffset : 0,
                            SpeedParameter = speedParameter, TransitionStart = start, TransitionCount = transitions.Count - start });
                    }
                    layerDefinitions.Add(new ControllerLayerDefinition { Name = Name(layer.name, path), StateStart = stateStart, StateCount = children.Length,
                        DefaultState = stateIds[machine.defaultState], AnyStart = anyStart, AnyCount = anyCount, EntryStart = entryStart, EntryCount = entryCount,
                        SyncedLayer = layer.syncedLayerIndex, SyncedTiming = layer.syncedLayerAffectsTiming ? (byte)1 : (byte)0,
                        Mask = compiledMasks.Add(layer.avatarMask, target, rig, path), DefaultWeight = layerIndex == 0 ? 1 : layer.defaultWeight,
                        BlendMode = layer.blendingMode == AnimatorLayerBlendingMode.Additive ? PoseBlendMode.Additive : PoseBlendMode.Override });
                }
                var builder = new BlobBuilder(Allocator.Temp);
                try
                {
                    ref var root = ref builder.ConstructRoot<AnimatorControllerBlob>();
                    root.Version = AnimatorControllerBlob.CurrentVersion; root.LibraryKey = library.Value.ContentKey;
                    var first = layerDefinitions[0];
                    root.DefaultState = first.DefaultState; root.AnyStart = first.AnyStart; root.AnyCount = first.AnyCount;
                    root.EntryStart = first.EntryStart; root.EntryCount = first.EntryCount;
                    var targetParameters = builder.Allocate(ref root.Parameters, definitions.Length);
                    for (var i = 0; i < definitions.Length; i++) targetParameters[i] = definitions[i];
                    var targetStates = builder.Allocate(ref root.States, states.Count);
                    for (var i = 0; i < states.Count; i++) targetStates[i] = states[i];
                    var targetTransitions = builder.Allocate(ref root.Transitions, transitions.Count);
                    for (var i = 0; i < transitions.Count; i++) targetTransitions[i] = transitions[i];
                    var targetConditions = builder.Allocate(ref root.Conditions, conditions.Count);
                    for (var i = 0; i < conditions.Count; i++) targetConditions[i] = conditions[i];
                    var targetMotions = builder.Allocate(ref root.Motions, motions.Nodes.Count);
                    for (var i = 0; i < motions.Nodes.Count; i++) targetMotions[i] = motions.Nodes[i];
                    var targetChildren = builder.Allocate(ref root.MotionChildren, motions.Children.Count);
                    for (var i = 0; i < motions.Children.Count; i++) targetChildren[i] = motions.Children[i];
                    var targetLayers = builder.Allocate(ref root.Layers, layerDefinitions.Count);
                    for (var i = 0; i < layerDefinitions.Count; i++) targetLayers[i] = layerDefinitions[i];
                    compiledMasks.CopyTo(ref builder, ref root);
                    return new ControllerBakeResult { Success = true, Controller = builder.CreateBlobAssetReference<AnimatorControllerBlob>(Allocator.Persistent), Diagnostics = Array.Empty<string>() };
                }
                finally { builder.Dispose(); }
            }
            catch (ArgumentException exception) { return new ControllerBakeResult { Diagnostics = new[] { exception.Message } }; }
            finally { compiledMasks.Dispose(); }
        }

        static Motion EffectiveMotion(AnimatorControllerLayer[] layers, int layer, AnimatorState state)
        {
            while (layers[layer].syncedLayerIndex >= 0)
            {
                var motion = layers[layer].GetOverrideMotion(state);
                if (motion != null) return motion;
                layer = layers[layer].syncedLayerIndex;
            }
            return state.motion;
        }

        static AnimationParameterDefinition[] Parameters(AnimatorController source, string path, out Dictionary<string, int> ids)
        {
            ids = new Dictionary<string, int>(StringComparer.Ordinal); var parameters = source.parameters;
            var output = new AnimationParameterDefinition[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                Require(!ids.ContainsKey(parameter.name), path, "Duplicate parameter: " + parameter.name); ids.Add(parameter.name, i);
                AnimationParameterType type;
                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Float: type = AnimationParameterType.Float; break;
                    case AnimatorControllerParameterType.Int: type = AnimationParameterType.Int; break;
                    case AnimatorControllerParameterType.Bool: type = AnimationParameterType.Bool; break;
                    case AnimatorControllerParameterType.Trigger: type = AnimationParameterType.Trigger; break;
                    default: throw new ArgumentException(path + ": Unsupported parameter type: " + parameter.name);
                }
                Require(math.isfinite(parameter.defaultFloat), path, "Nonfinite parameter default: " + parameter.name);
                output[i] = new AnimationParameterDefinition { Name = Name(parameter.name, path), Type = type, DefaultFloat = parameter.defaultFloat,
                    DefaultInt = type == AnimationParameterType.Int ? parameter.defaultInt : type == AnimationParameterType.Bool && parameter.defaultBool ? 1 : 0 };
            }
            return output;
        }
        static void AddTransitions(AnimatorStateTransition[] edges, bool any, Dictionary<AnimatorState, int> states,
            Dictionary<string, int> parameters, AnimationParameterDefinition[] definitions,
            List<ControllerTransitionDefinition> output, List<ControllerCondition> conditions, string path)
        {
            foreach (var edge in Active(edges))
            {
                Require(edge.interruptionSource == TransitionInterruptionSource.None, path, "Transition interruption is unsupported.");
                Require(edge.destinationStateMachine == null && (edge.isExit || edge.destinationState != null && states.ContainsKey(edge.destinationState)), path, "Transition must target a root clip state or Exit.");
                Require(!any || !edge.isExit, path, "Any State to Exit is unsupported.");
                Require(math.isfinite(edge.duration) && edge.duration >= 0 && math.isfinite(edge.offset) && math.isfinite(edge.exitTime) && edge.exitTime >= 0,
                    path, "Transition duration/offset/exit time must be finite and duration/exit time nonnegative.");
                var start = conditions.Count; AddConditions(edge.conditions, parameters, definitions, conditions, path);
                output.Add(new ControllerTransitionDefinition { Destination = edge.isExit ? -1 : states[edge.destinationState],
                    ConditionStart = start, ConditionCount = conditions.Count - start, Duration = edge.duration, Offset = edge.offset, ExitTime = edge.exitTime,
                    HasExitTime = edge.hasExitTime ? (byte)1 : (byte)0, FixedDuration = edge.hasFixedDuration ? (byte)1 : (byte)0,
                    CanTransitionToSelf = !any || edge.canTransitionToSelf ? (byte)1 : (byte)0 });
            }
        }
        static void AddConditions(AnimatorCondition[] source, Dictionary<string, int> ids, AnimationParameterDefinition[] definitions,
            List<ControllerCondition> output, string path)
        {
            foreach (var condition in source)
            {
                Require(ids.TryGetValue(condition.parameter, out var id), path, "Unknown condition parameter: " + condition.parameter);
                Require(math.isfinite(condition.threshold), path, "Nonfinite threshold: " + condition.parameter);
                var type = definitions[id].Type; ControllerConditionMode mode;
                switch (condition.mode)
                {
                    case AnimatorConditionMode.If: mode = ControllerConditionMode.If; break;
                    case AnimatorConditionMode.IfNot: mode = ControllerConditionMode.IfNot; break;
                    case AnimatorConditionMode.Greater: mode = ControllerConditionMode.Greater; break;
                    case AnimatorConditionMode.Less: mode = ControllerConditionMode.Less; break;
                    case AnimatorConditionMode.Equals: mode = ControllerConditionMode.Equals; break;
                    case AnimatorConditionMode.NotEqual: mode = ControllerConditionMode.NotEqual; break;
                    default: throw new ArgumentException(path + ": Unknown condition mode.");
                }
                var boolean = type == AnimationParameterType.Bool || type == AnimationParameterType.Trigger;
                Require(boolean ? mode == ControllerConditionMode.If || mode == ControllerConditionMode.IfNot
                    : mode == ControllerConditionMode.Greater || mode == ControllerConditionMode.Less
                        || type == AnimationParameterType.Int && (mode == ControllerConditionMode.Equals || mode == ControllerConditionMode.NotEqual),
                    path, "Condition mode does not match parameter type: " + condition.parameter);
                output.Add(new ControllerCondition { Parameter = id, Mode = mode, Threshold = condition.threshold });
            }
        }
        // Solo/mute are authoring switches; preserve the serialized order of enabled edges.
        static IEnumerable<T> Active<T>(T[] edges) where T : AnimatorTransitionBase
        {
            var solo = false; foreach (var edge in edges) if (edge.solo && !edge.mute) solo = true;
            foreach (var edge in edges) if (!edge.mute && (!solo || edge.solo)) yield return edge;
        }
        static FixedString128Bytes Name(string value, string path)
        {
            Require(!string.IsNullOrEmpty(value) && System.Text.Encoding.UTF8.GetByteCount(value) <= 125, path, "Names/paths must contain 1–125 UTF-8 bytes.");
            return new FixedString128Bytes(value);
        }
        static void Require(bool condition, string path, string message) { if (!condition) throw new ArgumentException(path + ": " + message); }
    }
}
