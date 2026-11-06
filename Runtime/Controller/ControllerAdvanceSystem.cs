using Unity.Entities;
using Unity.Burst;
using Unity.Mathematics;

namespace AnimationSystemDOTS
{
    [UpdateInGroup(typeof(AnimationSimulationSystemGroup))]
    [UpdateBefore(typeof(AnimationSampleSystem))]
    public partial class ControllerAdvanceSystem : SystemBase
    {
        EntityQuery missingScratch, missingLayers, missingCache;
        protected override void OnCreate()
        {
            RequireForUpdate<AnimationStepContext>();
            missingScratch = GetEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<AnimationController>() }, None = new[] { ComponentType.ReadOnly<ControllerMotionValue>() } });
            missingLayers = GetEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<AnimationController>() }, None = new[] { ComponentType.ReadOnly<ControllerLayerRuntime>() } });
            missingCache = GetEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<AnimationController>() }, None = new[] { ComponentType.ReadOnly<ControllerCachedRequest>() } });
        }
        protected override void OnUpdate()
        {
            var step = SystemAPI.GetSingleton<AnimationStepContext>();
            if (!math.isfinite(step.DeltaTime) || step.DeltaTime < 0)
                throw new System.ArgumentException("Animation delta time must be finite and nonnegative.");
            if (!missingScratch.IsEmptyIgnoreFilter) EntityManager.AddComponent<ControllerMotionValue>(missingScratch);
            if (!missingLayers.IsEmptyIgnoreFilter) EntityManager.AddComponent<ControllerLayerRuntime>(missingLayers);
            if (!missingCache.IsEmptyIgnoreFilter) EntityManager.AddComponent<ControllerCachedRequest>(missingCache);
            Dependency = new AdvanceJob { Step = step }.ScheduleParallel(Dependency);
        }
        [BurstCompile]
        partial struct AdvanceJob : IJobEntity
        {
            public AnimationStepContext Step;
            void Execute(in AnimationRig rig, in AnimationController controller, in AnimationPlaybackMode mode,
                ref AnimationClock clock, ref ControllerLayerState state, DynamicBuffer<AnimationParameterValue> parameters,
                DynamicBuffer<AnimationClipRequest> requests, DynamicBuffer<ControllerMotionValue> scratch, DynamicBuffer<ControllerLayerRuntime> layers,
                DynamicBuffer<ControllerCachedRequest> cache)
            {
                if (mode.Value != PlaybackDriver.Controller) return;
                var changed = state.Definition != controller.Definition;
                var repeated = clock.HasTick != 0 && ((Step.Mode == AnimationStepMode.Normal && Step.Tick <= clock.Tick)
                    || Step.Tick == clock.Tick && Step.Revision == clock.Revision && Step.Mode == clock.Mode);
                if (!changed && repeated) return;
                if (!controller.Definition.IsCreated || !rig.Library.IsCreated
                    || controller.Definition.Value.Version != AnimatorControllerBlob.CurrentVersion
                    || rig.Library.Value.Version != AnimationBakeVersion.Schema
                    || controller.Definition.Value.LibraryKey != rig.Library.Value.ContentKey)
                { state.Result = ControllerEvaluationResult.InvalidDefinition; requests.Clear(); return; }
                ref var graph = ref controller.Definition.Value;
                if (changed && state.Definition.IsCreated)
                {
                    state = default; layers.Clear(); cache.Clear(); parameters.ResizeUninitialized(graph.Parameters.Length);
                    for (var p = 0; p < parameters.Length; p++) parameters[p] = new AnimationParameterValue
                    { Float = graph.Parameters[p].DefaultFloat, Int = graph.Parameters[p].DefaultInt };
                }
                state.Definition = controller.Definition;
                if (parameters.Length != graph.Parameters.Length)
                { state.Result = ControllerEvaluationResult.InvalidParameters; requests.Clear(); return; }
                for (var p = 0; p < parameters.Length; p++)
                    if (graph.Parameters[p].Type == AnimationParameterType.Float && !math.isfinite(parameters[p].Float))
                    { state.Result = ControllerEvaluationResult.InvalidParameters; requests.Clear(); return; }
                for (var p = 0; p < parameters.Length; p++)
                { var value = parameters[p]; value.Consumed = 0; parameters[p] = value; }
                var count = math.max(graph.Layers.Length, 1);
                if (layers.Length != count)
                {
                    cache.Clear();
                    layers.ResizeUninitialized(count);
                    for (var l = 0; l < count; l++) layers[l] = new ControllerLayerRuntime { Weight = Layer(ref graph, l).DefaultWeight };
                }
                for (var l = 0; l < count; l++)
                    if (!math.isfinite(layers[l].Weight) || layers[l].Weight < 0 || layers[l].Weight > 1)
                    { state.Result = ControllerEvaluationResult.InvalidParameters; requests.Clear(); return; }
                if (graph.Motions.Length > 0) ControllerMotionSolver.Evaluate(ref graph, ref rig.Library.Value, parameters, scratch);
                var baseLayer = layers[0]; baseLayer.State = state; layers[0] = baseLayer;
                requests.Clear(); var dt = Step.Mode == AnimationStepMode.Seek || repeated ? 0 : Step.DeltaTime;
                for (var l = 0; l < count; l++)
                {
                    var definition = Layer(ref graph, l); var item = layers[l]; item.State.Definition = controller.Definition;
                    if (definition.SyncedLayer >= 0)
                    {
                        var source = layers[definition.SyncedLayer].State;
                        var sourceDefinition = Layer(ref graph, definition.SyncedLayer);
                        item.State = source;
                        item.State.CurrentState = definition.StateStart + source.CurrentState - sourceDefinition.StateStart;
                        if (source.NextState >= 0) item.State.NextState = definition.StateStart + source.NextState - sourceDefinition.StateStart;
                        var previous = source.NormalizedTime;
                        var nextPrevious = source.NextNormalizedTime;
                        // Current and previous clocks are recovered from this layer's
                        // last authoritative state, before copying the synced source.
                        if (layers[l].State.Initialized != 0)
                        { previous = layers[l].State.NormalizedTime; nextPrevious = layers[l].State.NextNormalizedTime; }
                        Emit(ref graph, ref rig.Library.Value, controller.Definition, ref item.State, definition, l, item.Weight, parameters, scratch, previous, nextPrevious, cache, requests);
                    }
                    else AdvanceLayer(ref graph, ref rig.Library.Value, controller.Definition, ref item.State, definition, l, dt,
                        l == 0 ? 1 : item.Weight, parameters, scratch, layers, cache, requests);
                    layers[l] = item;
                    if (item.State.Result != ControllerEvaluationResult.Success)
                    { state.Result = item.State.Result; requests.Clear(); return; }
                }
                state = layers[0].State; state.RetainPose = requests.Length == 0 ? (byte)1 : (byte)0;
                for (var p = 0; p < parameters.Length; p++)
                {
                    var value = parameters[p];
                    if (value.Consumed != 0) { value.Int = 0; value.Consumed = 0; parameters[p] = value; }
                }
                baseLayer = layers[0]; baseLayer.State = state; layers[0] = baseLayer;
                if (!repeated)
                {
                    clock.Tick = Step.Tick; clock.Revision = Step.Revision; clock.Mode = Step.Mode; clock.HasTick = 1;
                    if (Step.Mode != AnimationStepMode.Seek && math.isfinite(clock.Time + dt)) clock.Time += dt;
                }
            }

            static ControllerLayerDefinition Layer(ref AnimatorControllerBlob graph, int index)
                => graph.Layers.Length > 0 ? graph.Layers[index] : new ControllerLayerDefinition
                { StateCount = graph.States.Length, DefaultState = graph.DefaultState, AnyStart = graph.AnyStart, AnyCount = graph.AnyCount,
                    EntryStart = graph.EntryStart, EntryCount = graph.EntryCount, DefaultWeight = 1, SyncedLayer = -1, Mask = -1 };

            static void AdvanceLayer(ref AnimatorControllerBlob graph, ref AnimationLibraryBlob library, BlobAssetReference<AnimatorControllerBlob> owner,
                ref ControllerLayerState state, ControllerLayerDefinition layer, int layerIndex, double dt, float weight,
                DynamicBuffer<AnimationParameterValue> parameters, DynamicBuffer<ControllerMotionValue> scratch,
                DynamicBuffer<ControllerLayerRuntime> layers, DynamicBuffer<ControllerCachedRequest> cache, DynamicBuffer<AnimationClipRequest> requests)
            {
                state.Result = ControllerEvaluationResult.Success; state.RetainPose = 0;
                if (state.Initialized == 0)
                {
                    state.CurrentState = Entry(ref graph, layer, parameters); state.NextState = -1;
                    state.ActiveTransition = -1; state.FrozenSource = 0;
                    state.NormalizedTime = 0; state.CurrentSequence = ++state.Sequence; state.Initialized = 1;
                }
                if (state.CurrentState < layer.StateStart || state.CurrentState >= layer.StateStart + layer.StateCount
                    || state.NextState >= layer.StateStart + layer.StateCount || state.NextState >= 0 && state.NextState < layer.StateStart)
                { state.Result = ControllerEvaluationResult.InvalidDefinition; return; }
                var previous = state.NormalizedTime; var nextPrevious = state.NextNormalizedTime;
                ref var current = ref graph.States[state.CurrentState];
                var currentDuration = Duration(ref graph, layerIndex, state.CurrentState, scratch, layers);
                if (state.FrozenSource == 0) state.NormalizedTime = Advance(previous, dt, ref current, parameters, currentDuration);
                var edge = -1; var candidateSource = state.CurrentState; var remainingFraction = 0d;
                var interrupted = state.NextState >= 0;
                if (state.NextState >= 0)
                {
                    state.NextNormalizedTime = Advance(nextPrevious, dt, ref graph.States[state.NextState], parameters,
                        Duration(ref graph, layerIndex, state.NextState, scratch, layers));
                    edge = FindInterruption(ref graph, layer, ref state, previous, nextPrevious, parameters, out candidateSource, out remainingFraction);
                    if (edge < 0)
                    {
                        // A captured interruption source has no advancing motion.
                        // Native normalizes its duration using the new destination.
                        var source = state.FrozenSource != 0 ? state.NextState : state.CurrentState;
                        var sourceDuration = Duration(ref graph, layerIndex, source, scratch, layers);
                        var progressStep = dt * TransitionRate(state.NormalizedTransition, sourceDuration, ref graph.States[source], parameters);
                        state.TransitionTime = math.min(state.TransitionDuration, state.TransitionTime + progressStep);
                        if (state.TransitionTime >= state.TransitionDuration) Promote(ref state, ref previous, nextPrevious);
                    }
                }
                else
                {
                    edge = FindAny(ref graph, layer, state.CurrentState, previous, state.NormalizedTime, parameters, out remainingFraction);
                    if (edge < 0) edge = Find(ref graph, current.TransitionStart, current.TransitionCount,
                        layer.RootMachine, state.CurrentState, previous, state.NormalizedTime, parameters, out remainingFraction);
                }
                if (edge >= 0)
                {
                        ref var transition = ref graph.Transitions[edge]; Consume(ref graph, ref transition, parameters);
                        var target = graph.Machines.Length > 0 ? ControllerMachineRuntime.Destination(ref graph, ref transition, layer.RootMachine, parameters, true)
                            : transition.Destination < 0 ? Entry(ref graph, layer, parameters) : transition.Destination;
                        if ((uint)target >= (uint)graph.States.Length) { state.Result = ControllerEvaluationResult.InvalidDefinition; return; }
                        state.FrozenSource = interrupted ? (byte)1 : (byte)0;
                        state.ActiveTransition = edge;
                        state.NextState = target; state.NextSequence = ++state.Sequence;
                        nextPrevious = state.NextNormalizedTime = transition.Offset;
                        state.NormalizedTransition = transition.FixedDuration == 0 ? (byte)1 : (byte)0;
                        var timingSource = interrupted ? target : candidateSource;
                        var sourceDuration = Duration(ref graph, layerIndex, timingSource, scratch, layers);
                        state.TransitionDuration = transition.FixedDuration != 0 || sourceDuration > 0 ? transition.Duration : 0;
                        var remaining = dt * remainingFraction;
                        state.NextNormalizedTime = Advance(nextPrevious, remaining, ref graph.States[target], parameters,
                            Duration(ref graph, layerIndex, target, scratch, layers));
                        state.TransitionTime = math.min(state.TransitionDuration,
                            remaining * TransitionRate(state.NormalizedTransition, sourceDuration, ref graph.States[timingSource], parameters));
                        if (state.TransitionDuration <= state.TransitionTime) Promote(ref state, ref previous, nextPrevious);
                }
                Emit(ref graph, ref library, owner, ref state, layer, layerIndex, weight, parameters, scratch, previous, nextPrevious, cache, requests);
            }
            static double Duration(ref AnimatorControllerBlob graph, int layerIndex, int stateIndex, DynamicBuffer<ControllerMotionValue> scratch,
                DynamicBuffer<ControllerLayerRuntime> layers)
            {
                var duration = ControllerMotionSolver.Duration(ref graph, ref graph.States[stateIndex], scratch);
                if (graph.Layers.Length == 0) return duration;
                var localState = stateIndex - graph.Layers[layerIndex].StateStart;
                for (var i = layerIndex + 1; i < graph.Layers.Length; i++)
                {
                    ref var layer = ref graph.Layers[i];
                    if (layer.SyncedLayer != layerIndex || layer.SyncedTiming == 0) continue;
                    var other = ControllerMotionSolver.Duration(ref graph, ref graph.States[layer.StateStart + localState], scratch);
                    duration = math.lerp(duration, other, layers[i].Weight);
                }
                return duration;
            }
            static void Emit(ref AnimatorControllerBlob graph, ref AnimationLibraryBlob library, BlobAssetReference<AnimatorControllerBlob> owner,
                ref ControllerLayerState state, ControllerLayerDefinition layer, int layerIndex, float weight, DynamicBuffer<AnimationParameterValue> parameters, DynamicBuffer<ControllerMotionValue> scratch,
                double previous, double nextPrevious, DynamicBuffer<ControllerCachedRequest> cache, DynamicBuffer<AnimationClipRequest> requests)
            {
                var start = requests.Length;
                if (state.NextState >= 0)
                {
                    var blend = state.TransitionDuration > 0 ? (float)(state.TransitionTime / state.TransitionDuration) : 1;
                    if (state.FrozenSource != 0)
                        requests.Add(new AnimationClipRequest { Clip = new ClipId(-1), PoseSource = ControllerPoseSource.Captured, StateSlot = 0,
                            MotionWeight = 1, StateWeight = 1 - blend, Weight = 1 - blend, InstanceSequence = state.NextSequence });
                    else Request(ref graph, ref library, ref graph.States[state.CurrentState], parameters, scratch, previous, state.NormalizedTime, 1 - blend, 0, state.CurrentSequence, true, requests);
                    Request(ref graph, ref library, ref graph.States[state.NextState], parameters, scratch, nextPrevious, state.NextNormalizedTime, blend, 1, state.NextSequence, true, requests);
                }
                else Request(ref graph, ref library, ref graph.States[state.CurrentState], parameters, scratch, previous, state.NormalizedTime, 1, 0, state.CurrentSequence, false, requests);
                if (requests.Length == start)
                {
                    // A lone base layer retains its final pose. Other layers hold
                    // their last evaluated raw stream; a never-evaluated empty
                    // layer contributes nothing to the lower layers.
                    if (graph.Layers.Length > 1)
                        for (var i = 0; i < cache.Length; i++)
                            if (cache[i].Request.Layer == layerIndex)
                            {
                                var held = cache[i].Request; held.PreviousTime = held.Time; held.Clip = new ClipId(-1);
                                held.PoseSource = ControllerPoseSource.PreviousLayer; held.StateSlot = 0; held.StateWeight = held.MotionWeight = held.Weight = 1;
                                requests.Add(held); break;
                            }
                }
                else if (weight > 0)
                {
                    for (var i = cache.Length - 1; i >= 0; i--) if (cache[i].Request.Layer == layerIndex) cache.RemoveAt(i);
                    for (var i = start; i < requests.Length; i++)
                    { var held = requests[i]; held.Layer = layerIndex; cache.Add(new ControllerCachedRequest { Request = held }); }
                }
                for (var i = start; i < requests.Length; i++)
                {
                    var request = requests[i]; request.Layer = layerIndex; request.BlendMode = layer.BlendMode; request.LayerWeight = weight; request.Weight *= weight;
                    if (layer.Mask >= 0) { request.MaskController = owner; request.ControllerMask = layer.Mask; }
                    requests[i] = request;
                }
            }
            static double Speed(ref ControllerStateDefinition state, DynamicBuffer<AnimationParameterValue> parameters)
                => (double)state.Speed * (state.SpeedParameter >= 0 ? parameters[state.SpeedParameter].Float : 1);
            static double TransitionRate(byte normalized, double duration, ref ControllerStateDefinition state, DynamicBuffer<AnimationParameterValue> parameters)
                => normalized == 0 ? 1 : duration > 0 ? math.abs(Speed(ref state, parameters)) / duration : 0;
            static double Advance(double time, double dt, ref ControllerStateDefinition state, DynamicBuffer<AnimationParameterValue> parameters, double duration)
            {
                var rate = math.abs((double)state.Speed) * (state.SpeedParameter >= 0 ? parameters[state.SpeedParameter].Float : 1);
                var value = duration > 0 ? time + dt * rate / duration : 0; return math.isfinite(value) ? value : time;
            }
            static void Promote(ref ControllerLayerState state, ref double previous, double nextPrevious)
            {
                state.CurrentState = state.NextState; state.NextState = -1; state.NormalizedTime = state.NextNormalizedTime;
                state.CurrentSequence = state.NextSequence; previous = nextPrevious;
                state.FrozenSource = 0; state.ActiveTransition = -1;
                state.NextNormalizedTime = state.TransitionTime = state.TransitionDuration = 0;
            }
            static void Request(ref AnimatorControllerBlob graph, ref AnimationLibraryBlob library, ref ControllerStateDefinition source, DynamicBuffer<AnimationParameterValue> parameters, DynamicBuffer<ControllerMotionValue> scratch,
                double previous, double time, float weight, byte slot, ulong sequence, bool fading, DynamicBuffer<AnimationClipRequest> requests)
            {
                // Motion Time selects the sampled pose, not the state's clock or
                // Exit Time gate. A cycle parameter replaces the authored offset.
                var state = source;
                if (state.HasTimeParameter != 0) previous = time = parameters[state.TimeParameter].Float;
                if (state.HasCycleParameter != 0) state.CycleOffset = math.saturate(parameters[state.CycleParameter].Float);
                if (graph.Motions.Length > 0)
                {
                    var start = requests.Length;
                    ControllerMotionSolver.Emit(ref graph, ref library, ref state, scratch, previous, time, 1, sequence, requests);
                    if (requests.Length == start)
                    {
                        // Native's empty source stream defaults to rest during a
                        // fade. An empty destination reads the prior layer pose.
                        if (fading) requests.Add(new AnimationClipRequest { Clip = new ClipId(-1), PoseSource = slot == 0 ? ControllerPoseSource.Rest : ControllerPoseSource.PreviousLayer,
                            Weight = 1, InstanceSequence = sequence });
                    }
                    for (var i = start; i < requests.Length; i++)
                    { var item = requests[i]; item.MotionWeight = item.Weight; item.StateWeight = weight; item.StateSlot = slot; item.Weight *= weight; requests[i] = item; }
                    return;
                }
                if ((uint)state.Clip.Value < (uint)library.Clips.Length && library.Clips[state.Clip.Value].IsLooping == 0) state.CycleOffset = 0;
                requests.Add(new AnimationClipRequest { Clip = state.Clip,
                    PreviousTime = (state.Speed < 0 ? 1 - previous - state.CycleOffset : previous + state.CycleOffset) * state.Duration,
                    Time = (state.Speed < 0 ? 1 - time - state.CycleOffset : time + state.CycleOffset) * state.Duration,
                    LoopBoundary = state.Speed < 0 ? ClipLoopBoundary.End : ClipLoopBoundary.Start, Weight = weight, StateWeight = weight, MotionWeight = 1, StateSlot = slot, InstanceSequence = sequence });
            }
            static int Entry(ref AnimatorControllerBlob graph, ControllerLayerDefinition layer, DynamicBuffer<AnimationParameterValue> parameters)
            {
                if (graph.Machines.Length > 0) return ControllerMachineRuntime.Entry(ref graph, layer.RootMachine, parameters, true);
                for (var i = 0; i < layer.EntryCount; i++)
                {
                    ref var edge = ref graph.Transitions[layer.EntryStart + i];
                    if (!Matches(ref graph, ref edge, parameters)) continue;
                    Consume(ref graph, ref edge, parameters); return edge.Destination;
                }
                return layer.DefaultState;
            }
            static int FindInterruption(ref AnimatorControllerBlob graph, ControllerLayerDefinition layer, ref ControllerLayerState state,
                double previous, double nextPrevious, DynamicBuffer<AnimationParameterValue> parameters, out int source, out double remaining)
            {
                source = state.CurrentState; remaining = 0;
                if ((uint)state.ActiveTransition >= (uint)graph.Transitions.Length) return -1;
                ref var active = ref graph.Transitions[state.ActiveTransition];
                if (active.Interruption == ControllerInterruptionSource.None) return -1;
                source = state.CurrentState;
                var sourceTime = state.NormalizedTime;
                var sourcePrevious = state.FrozenSource != 0 ? sourceTime : previous;
                var stop = active.OrderedInterruption != 0 ? state.ActiveTransition : -1;
                var edge = FindAny(ref graph, layer, source, sourcePrevious, sourceTime, parameters, out remaining, stop);
                if (edge >= 0) return edge;
                if (Contains(layer.AnyStart, layer.AnyCount, stop)) return -1;
                for (var m = 0; m < graph.Machines.Length; m++)
                    if (Contains(graph.Machines[m].AnyStart, graph.Machines[m].AnyCount, stop)) return -1;
                var destinationFirst = active.Interruption == ControllerInterruptionSource.Destination || active.Interruption == ControllerInterruptionSource.DestinationThenSource;
                var both = active.Interruption == ControllerInterruptionSource.SourceThenDestination || active.Interruption == ControllerInterruptionSource.DestinationThenSource;
                for (var pass = 0; pass < (both ? 2 : 1); pass++)
                {
                    var destination = pass == 0 ? destinationFirst : !destinationFirst;
                    var candidate = destination ? state.NextState : source;
                    ref var definition = ref graph.States[candidate];
                    edge = Find(ref graph, definition.TransitionStart, definition.TransitionCount, layer.RootMachine, candidate,
                        destination ? nextPrevious : sourcePrevious, destination ? state.NextNormalizedTime : sourceTime, parameters, out remaining, stop);
                    if (edge >= 0) { source = candidate; return edge; }
                    // Ordered interruption stops the whole candidate queue at the
                    // active edge, including subsequent destination/source lists.
                    if (Contains(definition.TransitionStart, definition.TransitionCount, stop)) return -1;
                }
                return -1;
            }
            static int FindAny(ref AnimatorControllerBlob graph, ControllerLayerDefinition layer, int current, double previous, double time,
                DynamicBuffer<AnimationParameterValue> parameters, out double remainingFraction, int stop = -1)
            {
                if (graph.Machines.Length == 0) return Find(ref graph, layer.AnyStart, layer.AnyCount, layer.RootMachine, current, previous, time, parameters, out remainingFraction, stop);
                for (var m = 0; m < graph.Machines.Length; m++)
                {
                    if (!ControllerMachineRuntime.IsAncestor(ref graph, m, graph.States[current].Machine)) continue;
                    ref var machine = ref graph.Machines[m];
                    var edge = Find(ref graph, machine.AnyStart, machine.AnyCount, layer.RootMachine, current, previous, time, parameters, out remainingFraction, stop);
                    if (edge >= 0) return edge;
                    if (Contains(machine.AnyStart, machine.AnyCount, stop)) return -1;
                }
                remainingFraction = 0; return -1;
            }
            static bool Contains(int start, int count, int index) => index >= start && index - start < count;
            static int Find(ref AnimatorControllerBlob graph, int start, int count, int root, int current, double previous, double time,
                DynamicBuffer<AnimationParameterValue> parameters, out double remainingFraction, int stop = -1)
            {
                remainingFraction = 0;
                for (var i = 0; i < count; i++)
                {
                    if (start + i == stop) break;
                    ref var edge = ref graph.Transitions[start + i];
                    if (!Matches(ref graph, ref edge, parameters)) continue;
                    var destination = graph.Machines.Length > 0 ? ControllerMachineRuntime.Destination(ref graph, ref edge, root, parameters, false) : edge.Destination;
                    if (graph.Machines.Length > 0 && destination < 0 || destination == current && edge.CanTransitionToSelf == 0) continue;
                    if (edge.HasExitTime == 0) { if (edge.ConditionCount > 0) return start + i; continue; }
                    var gate = (double)edge.ExitTime;
                    if (gate < 1)
                    {
                        if (time > previous) gate += math.floor(previous - gate) + 1;
                        else if (time < previous) gate += math.ceil(previous - gate) - 1;
                    }
                    if (time > previous && previous < gate && time >= gate || time < previous && previous > gate && time <= gate)
                    { remainingFraction = math.saturate((time - gate) / (time - previous)); return start + i; }
                }
                return -1;
            }
            static bool Matches(ref AnimatorControllerBlob graph, ref ControllerTransitionDefinition edge, DynamicBuffer<AnimationParameterValue> parameters)
                => ControllerConditions.Matches(ref graph, ref edge, parameters);
            static void Consume(ref AnimatorControllerBlob graph, ref ControllerTransitionDefinition edge, DynamicBuffer<AnimationParameterValue> parameters)
                => ControllerConditions.Consume(ref graph, ref edge, parameters);
        }
    }
}
