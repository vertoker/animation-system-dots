using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;

namespace AnimationSystemDOTS.Tests
{
    public class ControllerHierarchyTests
    {
        [TestCase("entry")]
        [TestCase("stateToMachine")]
        [TestCase("exitToState")]
        [TestCase("machineToMachine")]
        [TestCase("anyPriority")]
        [TestCase("deepEntry")]
        [TestCase("nestedDefault")]
        [TestCase("conditionalExit")]
        [TestCase("unmatchedExit")]
        [TestCase("rootExit")]
        public void NestedMachines_StatesClocksAndPosesMatchNative(string scenario)
        {
            using (var oracle = new ControllerGraphOracle("hierarchy-" + scenario))
            {
                var root = oracle.Root; var a = oracle.State(root, "A", 0); root.defaultState = a;
                var group = root.AddStateMachine("Group"); var b = oracle.State(group, "B", 1); var c = oracle.State(group, "C", 2); group.defaultState = b;
                var parameters = oracle.Source.parameters;
                if (scenario != "stateToMachine")
                {
                    for (var i = 0; i < parameters.Length; i++) if (parameters[i].name == "Enabled") parameters[i].defaultBool = true;
                    oracle.Source.parameters = parameters;
                    root.AddEntryTransition(group).AddCondition(AnimatorConditionMode.If, 0, "Enabled");
                }
                if (scenario == "entry")
                {
                    group.AddEntryTransition(c).AddCondition(AnimatorConditionMode.If, 0, "Enabled");
                }
                if (scenario == "deepEntry" || scenario == "nestedDefault")
                {
                    var deep = group.AddStateMachine("Deep"); var d = oracle.State(deep, "D", 2); deep.defaultState = d;
                    if (scenario == "deepEntry") group.AddEntryTransition(deep).AddCondition(AnimatorConditionMode.If, 0, "Enabled");
                    else group.defaultState = d;
                }
                if (scenario == "conditionalExit" || scenario == "unmatchedExit")
                {
                    var exit = b.AddExitTransition(); exit.hasExitTime = false; exit.hasFixedDuration = true; exit.duration = .25f;
                    exit.AddCondition(AnimatorConditionMode.If, 0, "Go");
                    root.AddStateMachineTransition(group, c).AddCondition(AnimatorConditionMode.Equals, 1, "Variant");
                    if (scenario == "conditionalExit") root.AddStateMachineTransition(group, a).AddCondition(AnimatorConditionMode.Equals, 0, "Variant");
                }
                if (scenario == "rootExit")
                {
                    var exit = b.AddExitTransition(); exit.hasExitTime = false; exit.hasFixedDuration = true; exit.duration = .25f;
                    exit.AddCondition(AnimatorConditionMode.If, 0, "Go"); root.AddStateMachineExitTransition(group);
                }
                if (scenario == "stateToMachine")
                {
                    var edge = a.AddTransition(group); edge.hasExitTime = false; edge.hasFixedDuration = true; edge.duration = .3f; edge.offset = .15f;
                    edge.AddCondition(AnimatorConditionMode.If, 0, "Go");
                }
                if (scenario == "exitToState" || scenario == "machineToMachine")
                {
                    var exit = b.AddExitTransition(); exit.hasExitTime = true; exit.exitTime = .4f; exit.hasFixedDuration = true; exit.duration = .25f;
                    if (scenario == "exitToState") root.AddStateMachineTransition(group, a);
                    else
                    {
                        var other = root.AddStateMachine("OtherGroup"); other.defaultState = oracle.State(other, "D", 2);
                        root.AddStateMachineTransition(group, other);
                    }
                }
                if (scenario == "anyPriority")
                {
                    var outer = root.AddAnyStateTransition(a); outer.hasExitTime = false; outer.hasFixedDuration = true; outer.duration = .25f; outer.canTransitionToSelf = false;
                    outer.AddCondition(AnimatorConditionMode.If, 0, "Go");
                    var inner = group.AddAnyStateTransition(c); inner.hasExitTime = false; inner.hasFixedDuration = true; inner.duration = .25f;
                    inner.AddCondition(AnimatorConditionMode.If, 0, "Go"); ControllerGraphOracle.Edge(b, c, "Go");
                }
                oracle.StartNative(); oracle.StartEcs();
                for (var frame = 0; frame < 80; frame++)
                { if (frame == 3) oracle.Trigger("Go"); oracle.Tick(frame == 0 ? 0 : .071f); }
            }
        }
    }
}
