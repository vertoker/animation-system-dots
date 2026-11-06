using Unity.Entities;

namespace AnimationSystemDOTS
{
    internal static class ControllerMachineRuntime
    {
        internal static ControllerLayerDefinition Layer(ref AnimatorControllerBlob graph, int index)
            => graph.Layers.Length > 0 ? graph.Layers[index] : new ControllerLayerDefinition
            { StateCount = graph.States.Length, DefaultState = graph.DefaultState, AnyStart = graph.AnyStart, AnyCount = graph.AnyCount,
                EntryStart = graph.EntryStart, EntryCount = graph.EntryCount, DefaultWeight = 1, SyncedLayer = -1, Mask = -1 };

        internal static int EntryLayer(ref AnimatorControllerBlob graph, ControllerLayerDefinition layer, DynamicBuffer<AnimationParameterValue> parameters)
        {
            if (graph.Machines.Length > 0) return Entry(ref graph, layer.RootMachine, parameters, true);
            for (var i = 0; i < layer.EntryCount; i++)
            {
                ref var edge = ref graph.Transitions[layer.EntryStart + i];
                if (!ControllerConditions.Matches(ref graph, ref edge, parameters)) continue;
                ControllerConditions.Consume(ref graph, ref edge, parameters); return edge.Destination;
            }
            return layer.DefaultState;
        }
        internal static bool IsAncestor(ref AnimatorControllerBlob graph, int ancestor, int machine)
        {
            for (var steps = 0; machine >= 0 && steps <= graph.Machines.Length; steps++)
            {
                if (machine == ancestor) return true;
                if ((uint)machine >= (uint)graph.Machines.Length) return false;
                machine = graph.Machines[machine].Parent;
            }
            return false;
        }
        internal static int Entry(ref AnimatorControllerBlob graph, int machine, DynamicBuffer<AnimationParameterValue> parameters, bool consume)
            => Resolve(ref graph, ControllerDestinationKind.Machine, machine, machine, machine, parameters, consume);
        internal static int Destination(ref AnimatorControllerBlob graph, ref ControllerTransitionDefinition transition, int root,
            DynamicBuffer<AnimationParameterValue> parameters, bool consume)
            => Resolve(ref graph, transition.DestinationKind, transition.Destination, transition.SourceMachine, root, parameters, consume);

        static int Resolve(ref AnimatorControllerBlob graph, ControllerDestinationKind kind, int destination, int sourceMachine, int root,
            DynamicBuffer<AnimationParameterValue> parameters, bool consume)
        {
            var budget = graph.Machines.Length + graph.Transitions.Length + graph.States.Length + 1;
            while (budget-- > 0)
            {
                if (kind == ControllerDestinationKind.State)
                    return (uint)destination < (uint)graph.States.Length ? destination : -1;
                if (kind == ControllerDestinationKind.Machine)
                {
                    if ((uint)destination >= (uint)graph.Machines.Length) return -1;
                    ref var machine = ref graph.Machines[destination];
                    var edge = Decision(ref graph, machine.EntryStart, machine.EntryCount, parameters);
                    if (edge >= 0)
                    {
                        ref var transition = ref graph.Transitions[edge]; if (consume) ControllerConditions.Consume(ref graph, ref transition, parameters);
                        sourceMachine = destination; destination = transition.Destination; kind = transition.DestinationKind; continue;
                    }
                    var state = machine.DefaultState;
                    if ((uint)state >= (uint)graph.States.Length) return -1;
                    var owner = graph.States[state].Machine;
                    if (owner != destination)
                    {
                        var child = owner;
                        while ((uint)child < (uint)graph.Machines.Length && graph.Machines[child].Parent != destination)
                        { child = graph.Machines[child].Parent; if (--budget <= 0) return -1; }
                        if (child < 0) return -1;
                        destination = child; continue;
                    }
                    return state;
                }
                if ((uint)sourceMachine >= (uint)graph.Machines.Length) return -1;
                ref var exiting = ref graph.Machines[sourceMachine];
                var outgoing = Decision(ref graph, exiting.ExitStart, exiting.ExitCount, parameters);
                if (outgoing >= 0)
                {
                    ref var transition = ref graph.Transitions[outgoing]; if (consume) ControllerConditions.Consume(ref graph, ref transition, parameters);
                    destination = transition.Destination; kind = transition.DestinationKind;
                    if (kind == ControllerDestinationKind.Exit) sourceMachine = exiting.Parent;
                    continue;
                }
                // A machine with outgoing decisions falls back to its parent's
                // default when no condition matches. This differs from an
                // explicit machine Exit, which propagates through parent exits.
                if (exiting.ExitCount > 0 && (uint)exiting.Parent < (uint)graph.Machines.Length)
                {
                    var fallback = graph.Machines[exiting.Parent].DefaultState;
                    if ((uint)fallback >= (uint)graph.States.Length) return -1;
                    return fallback;
                }
                if (exiting.Parent >= 0) { sourceMachine = exiting.Parent; continue; }
                kind = ControllerDestinationKind.Machine; destination = root;
            }
            return -1;
        }
        static int Decision(ref AnimatorControllerBlob graph, int start, int count, DynamicBuffer<AnimationParameterValue> parameters)
        {
            for (var i = 0; i < count; i++)
            {
                ref var edge = ref graph.Transitions[start + i];
                if (ControllerConditions.Matches(ref graph, ref edge, parameters)) return start + i;
            }
            return -1;
        }
    }
}
