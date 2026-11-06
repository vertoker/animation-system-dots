using NUnit.Framework;
using UnityEditor.Animations;

namespace AnimationSystemDOTS.Tests
{
    public class ControllerInterruptionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void OrderedAnyInterruption_QueueStopsAtTheActiveTransition(bool candidateBefore)
        {
            using (var oracle = new ControllerGraphOracle("interrupt-ordered-any-" + candidateBefore))
            {
                var a = oracle.State(oracle.Root, "A", 0); var b = oracle.State(oracle.Root, "B", 1); var c = oracle.State(oracle.Root, "C", 2); oracle.Root.defaultState = a;
                if (candidateBefore) Any(oracle.Root, c, "Other", .7f);
                var first = Any(oracle.Root, b, "Go", .8f); first.interruptionSource = TransitionInterruptionSource.Source; first.orderedInterruption = true;
                if (!candidateBefore) Any(oracle.Root, c, "Other", .7f);
                oracle.StartNative(); oracle.StartEcs();
                for (var frame = 0; frame < 60; frame++)
                { if (frame == 2) oracle.Trigger("Go"); if (frame == 6) oracle.Trigger("Other"); oracle.Tick(frame == 0 ? 0 : .071f); }
            }
        }
        static AnimatorStateTransition Any(AnimatorStateMachine machine, AnimatorState target, string parameter, float duration)
        {
            var edge = machine.AddAnyStateTransition(target); edge.hasExitTime = false; edge.hasFixedDuration = true; edge.duration = duration; edge.canTransitionToSelf = false;
            edge.AddCondition(AnimatorConditionMode.If, 0, parameter); return edge;
        }
        [TestCase("synced")]
        [TestCase("syncedTiming")]
        [TestCase("hiddenSynced")]
        [TestCase("hiddenDirect")]
        [TestCase("independent")]
        [TestCase("additive")]
        public void LayeredInterruptions_WeightsMasksAndSyncMatchNative(string scenario)
        {
            using (var oracle = new ControllerGraphOracle("interrupt-layer-" + scenario, scenario == "additive"))
            {
                var a = oracle.State(oracle.Root, "A", 0); var b = oracle.State(oracle.Root, "B", 1); var c = oracle.State(oracle.Root, "C", 2); oracle.Root.defaultState = a;
                var first = ControllerGraphOracle.Edge(a, b, "Go", .8f); first.interruptionSource = TransitionInterruptionSource.Source; first.orderedInterruption = false;
                ControllerGraphOracle.Edge(a, c, "Other", .7f);
                oracle.Source.AddLayer("Upper"); var layers = oracle.Source.layers; layers[1].defaultWeight = scenario.StartsWith("hidden") ? 0 : .4f;
                if (scenario.StartsWith("synced") || scenario == "hiddenSynced")
                {
                    layers[1].syncedLayerIndex = 0; layers[1].syncedLayerAffectsTiming = scenario == "syncedTiming";
                    layers[1].SetOverrideMotion(a, oracle.Clips[1]); layers[1].SetOverrideMotion(b, oracle.Clips[2]); layers[1].SetOverrideMotion(c, oracle.Clips[0]);
                }
                else
                {
                    var machine = layers[1].stateMachine;
                    var x = oracle.State(machine, "X", 1); var y = oracle.State(machine, "Y", 2); var z = oracle.State(machine, "Z", 0); machine.defaultState = x;
                    var upper = ControllerGraphOracle.Edge(x, y, "Go", .9f); upper.interruptionSource = TransitionInterruptionSource.Source; upper.orderedInterruption = false;
                    ControllerGraphOracle.Edge(x, z, "Other", .6f);
                    if (scenario == "additive") layers[1].blendingMode = AnimatorLayerBlendingMode.Additive;
                    if (scenario == "hiddenDirect")
                    {
                        x.transitions = new AnimatorStateTransition[0];
                        var tree = new BlendTree { blendType = BlendTreeType.Direct };
                        tree.children = new[] { new ChildMotion { motion = oracle.Clips[1], directBlendParameter = "Cycle", timeScale = 1 } };
                        UnityEditor.AssetDatabase.AddObjectToAsset(tree, oracle.Source); x.motion = tree;
                        var serialized = new UnityEditor.SerializedObject(tree); serialized.FindProperty("m_NormalizedBlendValues").boolValue = true; serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
                oracle.Source.layers = layers; oracle.StartNative(); oracle.StartEcs();
                for (var frame = 0; frame < 70; frame++)
                {
                    if (frame == 2) oracle.Trigger("Go"); if (frame == 6) oracle.Trigger("Other");
                    if (scenario == "hiddenDirect") oracle.Float("Cycle", frame < 8 ? 1 : 0);
                    if (frame == 10 && scenario.StartsWith("hidden")) oracle.LayerWeight(1, .6f);
                    if (frame == 12 && scenario == "syncedTiming") oracle.LayerWeight(1, .7f);
                    oracle.Tick(frame == 0 ? 0 : .071f);
                }
            }
        }
        [TestCase(TransitionInterruptionSource.Source)]
        [TestCase(TransitionInterruptionSource.Destination)]
        public void NativeNormalizedInterruption_DurationContract(TransitionInterruptionSource source)
        {
            using (var oracle = Create("native-normalized-interrupt-", source, false))
            {
                foreach (var child in oracle.Root.states)
                    foreach (var edge in child.state.transitions)
                        if (edge.conditions[0].parameter == "Other") { edge.hasFixedDuration = false; edge.duration = .65f; }
                oracle.StartNative();
                for (var frame = 0; frame < 30; frame++)
                {
                    if (frame == 2) oracle.Trigger("Go"); if (frame == 6) oracle.Trigger("Other");
                    oracle.CaptureNative(frame == 0 ? 0 : .071f);
                }
            }
        }
        [TestCase("repeatedSource")]
        [TestCase("repeatedDestination")]
        [TestCase("normalizedDestination")]
        [TestCase("normalizedSource")]
        [TestCase("any")]
        [TestCase("self")]
        [TestCase("orderedBefore")]
        [TestCase("logicalSource")]
        public void Interruptions_RepeatedAndPriorityCasesMatchNative(string scenario)
        {
            using (var oracle = new ControllerGraphOracle("interrupt-extra-" + scenario))
            {
                var a = oracle.State(oracle.Root, "A", 0); var b = oracle.State(oracle.Root, "B", 1); var c = oracle.State(oracle.Root, "C", 2); oracle.Root.defaultState = a;
                var destination = scenario == "repeatedDestination" || scenario == "normalizedDestination" || scenario == "logicalSource";
                var first = ControllerGraphOracle.Edge(a, b, "Go", .8f);
                first.interruptionSource = destination ? TransitionInterruptionSource.Destination : TransitionInterruptionSource.Source;
                first.orderedInterruption = scenario == "orderedBefore";
                var second = scenario == "any" ? oracle.Root.AddAnyStateTransition(c)
                    : ControllerGraphOracle.Edge(destination ? b : a, scenario == "self" ? a : c, "Other", .7f);
                if (scenario == "any")
                { second.hasExitTime = false; second.hasFixedDuration = true; second.duration = .7f; second.canTransitionToSelf = false; second.AddCondition(AnimatorConditionMode.If, 0, "Other"); }
                second.interruptionSource = destination ? TransitionInterruptionSource.Destination : TransitionInterruptionSource.Source;
                if (scenario == "logicalSource") second.interruptionSource = TransitionInterruptionSource.Source;
                second.orderedInterruption = false;
                if (scenario.StartsWith("normalized"))
                { second.hasFixedDuration = false; second.duration = .65f; b.speedParameterActive = true; b.speedParameter = "Speed"; }
                if (scenario == "orderedBefore") a.transitions = new[] { second, first };
                ControllerGraphOracle.Edge(destination ? c : a, destination ? a : b, "Any", .6f);
                if (scenario == "logicalSource")
                { ControllerGraphOracle.Edge(b, a, "Any", .43f); ControllerGraphOracle.Edge(a, b, "Any", .57f); }
                oracle.StartNative(); oracle.StartEcs();
                for (var frame = 0; frame < 80; frame++)
                {
                    if (frame == 2) oracle.Trigger("Go"); if (frame == 6) oracle.Trigger("Other");
                    if (frame == 9 && (scenario.StartsWith("repeated") || scenario == "logicalSource")) oracle.Trigger("Any");
                    if (scenario == "normalizedDestination") oracle.Float("Speed", frame < 11 ? 1 : 1.4f);
                    oracle.Tick(frame == 0 ? 0 : .071f);
                }
            }
        }
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        [TestCase(false, false, .25f)]
        [TestCase(true, false, .25f)]
        public void ZeroDirectDuringFade_RetainsItsStateStream(bool destination, bool interrupt, float emptyWeight = 0)
        {
            using (var oracle = new ControllerGraphOracle("interrupt-zero-direct-" + destination + "-" + interrupt + "-" + emptyWeight))
            {
                var a = oracle.State(oracle.Root, "A", 0); var b = oracle.State(oracle.Root, "B", 1); var c = oracle.State(oracle.Root, "C", 2); oracle.Root.defaultState = a;
                var tree = new BlendTree { name = "ZeroDirect", blendType = BlendTreeType.Direct };
                tree.children = new[] { new ChildMotion { motion = oracle.Clips[destination ? 1 : 0], directBlendParameter = "Cycle", timeScale = 1 } };
                UnityEditor.AssetDatabase.AddObjectToAsset(tree, oracle.Source);
                var serialized = new UnityEditor.SerializedObject(tree); serialized.FindProperty("m_NormalizedBlendValues").boolValue = true; serialized.ApplyModifiedPropertiesWithoutUndo();
                if (destination) b.motion = tree; else a.motion = tree;
                var edge = ControllerGraphOracle.Edge(a, b, "Go", .8f); edge.interruptionSource = TransitionInterruptionSource.Source; edge.orderedInterruption = false;
                ControllerGraphOracle.Edge(a, c, "Other", .5f);
                oracle.StartNative(); oracle.StartEcs();
                for (var frame = 0; frame < 45; frame++)
                {
                    oracle.Float("Cycle", frame < 4 ? 1 : frame < 15 ? emptyWeight : 1);
                    if (frame == 2) oracle.Trigger("Go"); if (frame == 6 && interrupt) oracle.Trigger("Other");
                    oracle.Tick(frame == 0 ? 0 : .071f);
                    if (frame == 5 || frame == 9) oracle.RepeatEcs();
                }
            }
        }
        static ControllerGraphOracle Create(string prefix, TransitionInterruptionSource source, bool ordered)
        {
            var oracle = new ControllerGraphOracle(prefix + source + "-" + ordered);
            var a = oracle.State(oracle.Root, "A", 0); var b = oracle.State(oracle.Root, "B", 1); var c = oracle.State(oracle.Root, "C", 2); oracle.Root.defaultState = a;
            var edge = ControllerGraphOracle.Edge(a, b, "Go", .8f); edge.interruptionSource = source; edge.orderedInterruption = ordered;
            ControllerGraphOracle.Edge(a, c, "Other", .5f); ControllerGraphOracle.Edge(b, c, "Other", .4f);
            return oracle;
        }

        [TestCase(TransitionInterruptionSource.Source, false)]
        [TestCase(TransitionInterruptionSource.Destination, false)]
        [TestCase(TransitionInterruptionSource.SourceThenDestination, false)]
        [TestCase(TransitionInterruptionSource.DestinationThenSource, false)]
        [TestCase(TransitionInterruptionSource.Source, true)]
        public void NativeInterruptionContract_CapturesSourceClockAndCompositePose(TransitionInterruptionSource source, bool ordered)
        {
            using (var oracle = Create("native-interrupt-", source, ordered))
            {
                oracle.StartNative();
                for (var frame = 0; frame < 70; frame++)
                {
                    if (frame == 2) oracle.Trigger("Go"); if (frame == 6) oracle.Trigger("Other");
                    oracle.CaptureNative(frame == 0 ? 0 : .071f);
                }
                Assert.That(oracle.Native.GetCurrentAnimatorStateInfo(0).shortNameHash, Is.EqualTo(UnityEngine.Animator.StringToHash("C")));
            }
        }
        [TestCase(TransitionInterruptionSource.Source, false)]
        [TestCase(TransitionInterruptionSource.Destination, false)]
        [TestCase(TransitionInterruptionSource.SourceThenDestination, false)]
        [TestCase(TransitionInterruptionSource.DestinationThenSource, false)]
        [TestCase(TransitionInterruptionSource.Source, true)]
        [TestCase(TransitionInterruptionSource.SourceThenDestination, true)]
        [TestCase(TransitionInterruptionSource.DestinationThenSource, true)]
        public void ActiveFade_InterruptionSelectionAndPoseHistoryMatchNative(TransitionInterruptionSource source, bool ordered)
        {
            using (var oracle = Create("interrupt-", source, ordered))
            {
                oracle.StartNative(); oracle.StartEcs();
                for (var frame = 0; frame < 70; frame++)
                {
                    if (frame == 2) oracle.Trigger("Go"); if (frame == 6) oracle.Trigger("Other");
                    oracle.Tick(frame == 0 ? 0 : .071f);
                }
            }
        }
    }
}
