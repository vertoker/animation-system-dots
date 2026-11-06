using System;
using System.Collections.Generic;
using UnityEditor.Animations;

namespace AnimationSystemDOTS.Editor.Baking
{
    internal sealed class ControllerMachineBake
    {
        internal readonly List<AnimatorState> States = new List<AnimatorState>();
        internal readonly Dictionary<AnimatorState, int> StateIds = new Dictionary<AnimatorState, int>();
        internal readonly Dictionary<AnimatorStateMachine, int> MachineIds = new Dictionary<AnimatorStateMachine, int>();
        internal readonly Dictionary<AnimatorState, int> StateMachines = new Dictionary<AnimatorState, int>();
        internal readonly Dictionary<AnimatorState, string> StatePaths = new Dictionary<AnimatorState, string>();
        internal readonly List<ControllerMachineDefinition> Definitions = new List<ControllerMachineDefinition>();
        readonly List<AnimatorStateMachine> machines = new List<AnimatorStateMachine>();
        readonly List<int> parents = new List<int>();
        readonly List<string> paths = new List<string>();
        readonly int stateOffset, machineOffset;
        readonly string diagnostic;

        internal ControllerMachineBake(AnimatorStateMachine root, string layerName, string path, int stateOffset, int machineOffset)
        {
            this.stateOffset = stateOffset; this.machineOffset = machineOffset; diagnostic = path;
            Collect(root, -1, layerName);
        }
        void Collect(AnimatorStateMachine source, int parent, string path)
        {
            Require(source != null, path, "Missing state machine.");
            Require(!MachineIds.ContainsKey(source), path, "Repeated or cyclic state machine reference.");
            Require(source.behaviours.Length == 0, path, "StateMachineBehaviour callbacks are unsupported.");
            Require(source.states.Length > 0 || source.stateMachines.Length > 0, path, "Empty state machines are unsupported.");
            var id = machineOffset + machines.Count; MachineIds.Add(source, id); machines.Add(source); parents.Add(parent); paths.Add(path);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var child in source.states)
            {
                Require(child.state != null && names.Add(child.state.name), path, "Missing or duplicate state name.");
                StateIds.Add(child.state, stateOffset + States.Count); States.Add(child.state); StateMachines.Add(child.state, id); StatePaths.Add(child.state, path + "." + child.state.name);
            }
            foreach (var child in source.stateMachines)
            {
                Require(child.stateMachine != null && names.Add(child.stateMachine.name), path, "Missing or duplicate state-machine name.");
                Collect(child.stateMachine, id, path + "." + child.stateMachine.name);
            }
        }
        internal void Compile(Dictionary<string, int> parameters, AnimationParameterDefinition[] definitions,
            List<ControllerTransitionDefinition> transitions, List<ControllerCondition> conditions)
        {
            for (var i = 0; i < machines.Count; i++)
            {
                var source = machines[i]; var path = diagnostic + "/" + paths[i]; var id = machineOffset + i;
                var anyStart = transitions.Count;
                ControllerBakePipeline.AddTransitions(source.anyStateTransitions, true, StateIds, MachineIds, id, parameters, definitions, transitions, conditions, path + "/Any State");
                var anyCount = transitions.Count - anyStart; var entryStart = transitions.Count;
                foreach (var edge in ControllerBakePipeline.Active(source.entryTransitions))
                {
                    Require(!edge.isExit, path + "/Entry", "Entry cannot target Exit.");
                    AddDecision(edge, id, parameters, definitions, transitions, conditions, path + "/Entry");
                }
                var entryCount = transitions.Count - entryStart; var exitStart = transitions.Count;
                if (parents[i] >= 0)
                {
                    var parent = machines[parents[i] - machineOffset];
                    foreach (var edge in ControllerBakePipeline.Active(parent.GetStateMachineTransitions(source)))
                        AddDecision(edge, id, parameters, definitions, transitions, conditions, path + "/Exit");
                }
                var defaultState = source.defaultState != null && StateIds.TryGetValue(source.defaultState, out var state) ? state : -1;
                Require(defaultState >= 0 || entryCount > 0, path, "A machine needs a valid default state or Entry transition.");
                Definitions.Add(new ControllerMachineDefinition { Path = ControllerBakePipeline.Name(paths[i], path), Parent = parents[i], DefaultState = defaultState,
                    AnyStart = anyStart, AnyCount = anyCount, EntryStart = entryStart, EntryCount = entryCount, ExitStart = exitStart, ExitCount = transitions.Count - exitStart });
            }
        }
        void AddDecision(AnimatorTransition edge, int machine, Dictionary<string, int> parameters, AnimationParameterDefinition[] definitions,
            List<ControllerTransitionDefinition> transitions, List<ControllerCondition> conditions, string path)
        {
            var destination = ControllerBakePipeline.Destination(edge, StateIds, MachineIds, path, out var kind);
            var start = conditions.Count; ControllerBakePipeline.AddConditions(edge.conditions, parameters, definitions, conditions, path);
            transitions.Add(new ControllerTransitionDefinition { Destination = destination, DestinationKind = kind, SourceMachine = machine,
                ConditionStart = start, ConditionCount = conditions.Count - start, CanTransitionToSelf = 1 });
        }
        void Require(bool condition, string path, string message)
        { if (!condition) throw new ArgumentException(diagnostic + "/" + path + ": " + message); }
    }
}
