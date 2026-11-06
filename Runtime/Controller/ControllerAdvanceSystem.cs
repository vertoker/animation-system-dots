using Unity.Entities;
using Unity.Burst;
using Unity.Mathematics;

namespace AnimationSystemDOTS
{
    [UpdateInGroup(typeof(AnimationSimulationSystemGroup))]
    [UpdateBefore(typeof(AnimationSampleSystem))]
    public partial class ControllerAdvanceSystem : SystemBase
    {
        EntityQuery missingScratch;
        protected override void OnCreate()
        {
            RequireForUpdate<AnimationStepContext>();
            missingScratch = GetEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<AnimationController>() }, None = new[] { ComponentType.ReadOnly<ControllerMotionValue>() } });
        }
        protected override void OnUpdate()
        {
            var step = SystemAPI.GetSingleton<AnimationStepContext>();
            if (!math.isfinite(step.DeltaTime) || step.DeltaTime < 0)
                throw new System.ArgumentException("Animation delta time must be finite and nonnegative.");
            if (!missingScratch.IsEmptyIgnoreFilter) EntityManager.AddComponent<ControllerMotionValue>(missingScratch);
            Dependency = new AdvanceJob { Step = step }.ScheduleParallel(Dependency);
        }
        [BurstCompile]
        partial struct AdvanceJob : IJobEntity
        {
            public AnimationStepContext Step;
            void Execute(in AnimationRig rig, in AnimationController controller, in AnimationPlaybackMode mode,
                ref AnimationClock clock, ref ControllerLayerState state, DynamicBuffer<AnimationParameterValue> parameters,
                DynamicBuffer<AnimationClipRequest> requests, DynamicBuffer<ControllerMotionValue> scratch)
            {
                if (mode.Value != PlaybackDriver.Controller) return;
                if (clock.HasTick != 0 && ((Step.Mode == AnimationStepMode.Normal && Step.Tick <= clock.Tick)
                    || Step.Tick == clock.Tick && Step.Revision == clock.Revision && Step.Mode == clock.Mode)) return;
                if (!controller.Definition.IsCreated || !rig.Library.IsCreated
                    || controller.Definition.Value.Version != AnimatorControllerBlob.CurrentVersion
                    || rig.Library.Value.Version != AnimationBakeVersion.Schema
                    || controller.Definition.Value.LibraryKey != rig.Library.Value.ContentKey)
                { state.Result = ControllerEvaluationResult.InvalidDefinition; requests.Clear(); return; }
                ref var graph = ref controller.Definition.Value;
                if (parameters.Length != graph.Parameters.Length)
                { state.Result = ControllerEvaluationResult.InvalidParameters; requests.Clear(); return; }
                for (var p = 0; p < parameters.Length; p++)
                    if (graph.Parameters[p].Type == AnimationParameterType.Float && !math.isfinite(parameters[p].Float))
                    { state.Result = ControllerEvaluationResult.InvalidParameters; requests.Clear(); return; }
                state.Result = ControllerEvaluationResult.Success;
                if (graph.Motions.Length > 0) ControllerMotionSolver.Evaluate(ref graph, ref rig.Library.Value, parameters, scratch);
                if (state.Initialized == 0)
                {
                    state.CurrentState = Entry(ref graph, parameters); state.NextState = -1;
                    state.NormalizedTime = 0; state.CurrentSequence = ++state.Sequence; state.Initialized = 1;
                }
                if ((uint)state.CurrentState >= (uint)graph.States.Length || state.NextState >= graph.States.Length)
                { state.Result = ControllerEvaluationResult.InvalidDefinition; requests.Clear(); return; }
                var dt = Step.Mode == AnimationStepMode.Seek ? 0 : Step.DeltaTime;
                var previous = state.NormalizedTime;
                var nextPrevious = state.NextNormalizedTime;
                ref var current = ref graph.States[state.CurrentState];
                var currentDuration = ControllerMotionSolver.Duration(ref graph, ref current, scratch);
                state.NormalizedTime = Advance(previous, dt, ref current, parameters, currentDuration);
                if (state.NextState >= 0)
                {
                    state.NextNormalizedTime = Advance(nextPrevious, dt, ref graph.States[state.NextState], parameters, ControllerMotionSolver.Duration(ref graph, ref graph.States[state.NextState], scratch));
                    var progressStep = dt * TransitionRate(state.NormalizedTransition, currentDuration, ref current, parameters);
                    state.TransitionTime = math.min(state.TransitionDuration, state.TransitionTime + progressStep);
                    if (state.TransitionTime >= state.TransitionDuration) Promote(ref state, ref previous, nextPrevious);
                }
                else
                {
                    var edge = Find(ref graph, graph.AnyStart, graph.AnyCount, state.CurrentState, previous, state.NormalizedTime, parameters, out var remainingFraction);
                    if (edge < 0) edge = Find(ref graph, current.TransitionStart, current.TransitionCount,
                        state.CurrentState, previous, state.NormalizedTime, parameters, out remainingFraction);
                    if (edge >= 0)
                    {
                        ref var transition = ref graph.Transitions[edge];
                        Consume(ref graph, ref transition, parameters);
                        var target = transition.Destination < 0 ? Entry(ref graph, parameters) : transition.Destination;
                        state.NextState = target; state.NextSequence = ++state.Sequence;
                        nextPrevious = state.NextNormalizedTime = transition.Offset;
                        state.NormalizedTransition = transition.FixedDuration == 0 ? (byte)1 : (byte)0;
                        state.TransitionDuration = transition.FixedDuration != 0 || currentDuration > 0 ? transition.Duration : 0;
                        // Exit time may fall inside this step. Native Mecanim advances the
                        // newly entered state and fade by the remaining part of the step.
                        var remaining = dt * remainingFraction;
                        state.NextNormalizedTime = Advance(nextPrevious, remaining, ref graph.States[target], parameters, ControllerMotionSolver.Duration(ref graph, ref graph.States[target], scratch));
                        state.TransitionTime = math.min(state.TransitionDuration,
                            remaining * TransitionRate(state.NormalizedTransition, currentDuration, ref current, parameters));
                        if (state.TransitionDuration <= state.TransitionTime) Promote(ref state, ref previous, nextPrevious);
                    }
                }
                requests.Clear();
                state.RetainPose = 0;
                if (state.NextState >= 0)
                {
                    var weight = state.TransitionDuration > 0 ? (float)(state.TransitionTime / state.TransitionDuration) : 1;
                    Request(ref graph, ref rig.Library.Value, ref graph.States[state.CurrentState], scratch, previous, state.NormalizedTime, 1 - weight, state.CurrentSequence, requests);
                    Request(ref graph, ref rig.Library.Value, ref graph.States[state.NextState], scratch, nextPrevious, state.NextNormalizedTime, weight, state.NextSequence, requests);
                }
                else Request(ref graph, ref rig.Library.Value, ref graph.States[state.CurrentState], scratch, previous, state.NormalizedTime, 1, state.CurrentSequence, requests);
                if (graph.Motions.Length > 0 && requests.Length == 0) state.RetainPose = 1;
                clock.Tick = Step.Tick; clock.Revision = Step.Revision; clock.Mode = Step.Mode; clock.HasTick = 1;
                if (Step.Mode != AnimationStepMode.Seek && math.isfinite(clock.Time + dt)) clock.Time += dt;
            }
            static double Speed(ref ControllerStateDefinition state, DynamicBuffer<AnimationParameterValue> parameters)
                => (double)state.Speed * (state.SpeedParameter >= 0 ? parameters[state.SpeedParameter].Float : 1);
            static double TransitionRate(byte normalized, double duration, ref ControllerStateDefinition state, DynamicBuffer<AnimationParameterValue> parameters)
                => normalized == 0 ? 1 : duration > 0 ? math.abs(Speed(ref state, parameters)) / duration : 0;
            static double Advance(double time, double dt, ref ControllerStateDefinition state, DynamicBuffer<AnimationParameterValue> parameters, double duration)
            {
                // Mecanim's constant negative state speed reverses the clip, while its
                // reported normalized clock advances using the absolute constant speed.
                var rate = math.abs((double)state.Speed) * (state.SpeedParameter >= 0 ? parameters[state.SpeedParameter].Float : 1);
                var value = duration > 0 ? time + dt * rate / duration : 0;
                return math.isfinite(value) ? value : time;
            }
            static void Promote(ref ControllerLayerState state, ref double previous, double nextPrevious)
            {
                state.CurrentState = state.NextState; state.NextState = -1; state.NormalizedTime = state.NextNormalizedTime;
                state.CurrentSequence = state.NextSequence; previous = nextPrevious;
                state.NextNormalizedTime = state.TransitionTime = state.TransitionDuration = 0;
            }
            static void Request(ref AnimatorControllerBlob graph, ref AnimationLibraryBlob library, ref ControllerStateDefinition state, DynamicBuffer<ControllerMotionValue> scratch, double previous, double time, float weight, ulong sequence,
                DynamicBuffer<AnimationClipRequest> requests)
            {
                if (graph.Motions.Length > 0) { ControllerMotionSolver.Emit(ref graph, ref library, ref state, scratch, previous, time, weight, sequence, requests); return; }
                requests.Add(new AnimationClipRequest { Clip = state.Clip,
                    PreviousTime = (state.Speed < 0 ? 1 - previous - state.CycleOffset : previous + state.CycleOffset) * state.Duration,
                    Time = (state.Speed < 0 ? 1 - time - state.CycleOffset : time + state.CycleOffset) * state.Duration,
                    LoopBoundary = state.Speed < 0 ? ClipLoopBoundary.End : ClipLoopBoundary.Start, Weight = weight, InstanceSequence = sequence });
            }
            static int Entry(ref AnimatorControllerBlob graph, DynamicBuffer<AnimationParameterValue> parameters)
            {
                for (var i = 0; i < graph.EntryCount; i++)
                {
                    ref var edge = ref graph.Transitions[graph.EntryStart + i];
                    if (!Matches(ref graph, ref edge, parameters)) continue;
                    Consume(ref graph, ref edge, parameters); return edge.Destination;
                }
                return graph.DefaultState;
            }
            static int Find(ref AnimatorControllerBlob graph, int start, int count, int current, double previous, double time,
                DynamicBuffer<AnimationParameterValue> parameters, out double remainingFraction)
            {
                remainingFraction = 0;
                for (var i = 0; i < count; i++)
                {
                    ref var edge = ref graph.Transitions[start + i];
                    if (edge.Destination == current && edge.CanTransitionToSelf == 0) continue;
                    if (!Matches(ref graph, ref edge, parameters)) continue;
                    if (edge.HasExitTime == 0)
                    {
                        if (edge.ConditionCount > 0) return start + i;
                        continue; // Mecanim ignores an unconditional edge without Exit Time.
                    }
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
            {
                for (var i = 0; i < edge.ConditionCount; i++)
                {
                    ref var condition = ref graph.Conditions[edge.ConditionStart + i]; var value = parameters[condition.Parameter];
                    var type = graph.Parameters[condition.Parameter].Type;
                    var number = type == AnimationParameterType.Float ? (double)value.Float : value.Int;
                    switch (condition.Mode)
                    {
                        case ControllerConditionMode.If: if (value.Int == 0) return false; break;
                        case ControllerConditionMode.IfNot: if (value.Int != 0) return false; break;
                        case ControllerConditionMode.Greater: if (!(number > condition.Threshold)) return false; break;
                        case ControllerConditionMode.Less: if (!(number < condition.Threshold)) return false; break;
                        case ControllerConditionMode.Equals: if (number != condition.Threshold) return false; break;
                        case ControllerConditionMode.NotEqual: if (number == condition.Threshold) return false; break;
                        default: return false;
                    }
                }
                return true;
            }
            static void Consume(ref AnimatorControllerBlob graph, ref ControllerTransitionDefinition edge, DynamicBuffer<AnimationParameterValue> parameters)
            {
                for (var i = 0; i < edge.ConditionCount; i++)
                {
                    var id = graph.Conditions[edge.ConditionStart + i].Parameter;
                    if (graph.Parameters[id].Type != AnimationParameterType.Trigger) continue;
                    var value = parameters[id]; value.Int = 0; parameters[id] = value;
                }
            }
        }
    }
}
