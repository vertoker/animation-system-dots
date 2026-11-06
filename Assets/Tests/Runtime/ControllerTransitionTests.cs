using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace AnimationSystemDOTS.Tests
{
    public class ControllerTransitionTests : RuntimeAnimationFixture
    {
        BlobAssetReference<AnimatorControllerBlob> graph;
        ControllerAdvanceSystem controllerSystem;
        struct Edge
        {
            public int Source, Target;
            public float Duration, ExitTime, Offset;
            public bool Exit, NormalizedDuration;
            public ControllerCondition[] Conditions;
        }
        static ControllerCondition Condition(int id, ControllerConditionMode mode, float threshold = 0)
            => new ControllerCondition { Parameter = id, Mode = mode, Threshold = threshold };
        void Configure(params Edge[] edges)
        {
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<AnimatorControllerBlob>();
                root.Version = AnimatorControllerBlob.CurrentVersion; root.LibraryKey = library.Value.ContentKey;
                var parameters = builder.Allocate(ref root.Parameters, 4);
                parameters[0] = new AnimationParameterDefinition { Name = new FixedString128Bytes("Speed"), Type = AnimationParameterType.Float, DefaultFloat = 1 };
                parameters[1].Type = AnimationParameterType.Int; parameters[2].Type = AnimationParameterType.Bool; parameters[3].Type = AnimationParameterType.Trigger;
                var states = builder.Allocate(ref root.States, 2);
                for (var i = 0; i < 2; i++) states[i] = new ControllerStateDefinition { Clip = new ClipId(i), Duration = i + 1, Speed = 1, SpeedParameter = -1 };
                var transitions = builder.Allocate(ref root.Transitions, edges.Length);
                var conditionCount = 0; foreach (var edge in edges) conditionCount += edge.Conditions?.Length ?? 0;
                var conditions = builder.Allocate(ref root.Conditions, conditionCount);
                var t = 0; var c = 0;
                // Any State first, then Entry, then serialized state edge order.
                for (var source = -2; source < 2; source++)
                {
                    var start = t;
                    foreach (var edge in edges)
                    {
                        if (edge.Source != source) continue;
                        transitions[t++] = new ControllerTransitionDefinition { Destination = edge.Target, Duration = edge.Duration,
                            HasExitTime = edge.Exit ? (byte)1 : (byte)0, ExitTime = edge.ExitTime, Offset = edge.Offset,
                            FixedDuration = edge.NormalizedDuration ? (byte)0 : (byte)1, CanTransitionToSelf = 1,
                            ConditionStart = c, ConditionCount = edge.Conditions?.Length ?? 0 };
                        if (edge.Conditions != null) foreach (var condition in edge.Conditions) conditions[c++] = condition;
                    }
                    if (source == -2) { root.AnyStart = start; root.AnyCount = t - start; }
                    else if (source == -1) { root.EntryStart = start; root.EntryCount = t - start; }
                    else { states[source].TransitionStart = start; states[source].TransitionCount = t - start; }
                }
                graph = builder.CreateBlobAssetReference<AnimatorControllerBlob>(Allocator.Persistent);
            }
            world.EntityManager.SetComponentData(rigEntity, new AnimationPlaybackMode { Value = PlaybackDriver.Controller });
            world.EntityManager.AddComponentData(rigEntity, new AnimationController { Definition = graph });
            world.EntityManager.AddComponentData(rigEntity, new ControllerLayerState());
            var values = world.EntityManager.AddBuffer<AnimationParameterValue>(rigEntity);
            for (var i = 0; i < 4; i++) values.Add(new AnimationParameterValue { Float = i == 0 ? 1 : 0 });
            controllerSystem = world.GetOrCreateSystemManaged<ControllerAdvanceSystem>();
        }
        void Tick(ulong tick, double dt, uint revision = 0, AnimationStepMode mode = AnimationStepMode.Normal)
        {
            world.EntityManager.SetComponentData(stepEntity, new AnimationStepContext { Tick = tick, DeltaTime = dt, Revision = revision, Mode = mode });
            playback.Update(); controllerSystem.Update(); world.EntityManager.CompleteAllTrackedJobs();
        }
        ControllerLayerState Layer => world.EntityManager.GetComponentData<ControllerLayerState>(rigEntity);
        DynamicBuffer<AnimationParameterValue> Values => world.EntityManager.GetBuffer<AnimationParameterValue>(rigEntity);
        AnimationController Controller => new AnimationController { Definition = graph };
        [TearDown]
        public void DisposeController()
        {
            if (world != null && world.IsCreated) world.EntityManager.CompleteAllTrackedJobs();
            if (graph.IsCreated) graph.Dispose();
        }
        [Test]
        public void Parameters_ValidateIdTypeAndFiniteValueWithoutMutation()
        {
            Configure(); var values = Values;
            Assert.That(AnimationApi.SetFloat(Controller, values, new AnimationParameterId(0), 2), Is.EqualTo(AnimationParameterResult.Success));
            Assert.That(AnimationApi.SetFloat(Controller, values, new AnimationParameterId(0), float.NaN), Is.EqualTo(AnimationParameterResult.InvalidValue));
            Assert.That(values[0].Float, Is.EqualTo(2));
            Assert.That(AnimationApi.SetInt(Controller, values, new AnimationParameterId(0), 5), Is.EqualTo(AnimationParameterResult.InvalidType));
            Assert.That(AnimationApi.SetInt(Controller, values, new AnimationParameterId(1), int.MaxValue), Is.EqualTo(AnimationParameterResult.Success));
            Assert.That(values[1].Int, Is.EqualTo(int.MaxValue));
            Assert.That(AnimationApi.SetBool(Controller, values, new AnimationParameterId(2), true), Is.EqualTo(AnimationParameterResult.Success));
            Assert.That(AnimationApi.SetTrigger(Controller, values, new AnimationParameterId(3)), Is.EqualTo(AnimationParameterResult.Success));
            Assert.That(AnimationApi.ResetTrigger(Controller, values, new AnimationParameterId(3)), Is.EqualTo(AnimationParameterResult.Success));
            Assert.That(AnimationApi.SetBool(Controller, values, new AnimationParameterId(-1), false), Is.EqualTo(AnimationParameterResult.InvalidId));
            Assert.That(AnimationApi.FindParameter(Controller, new FixedString128Bytes("Speed")).Value, Is.Zero);
            Assert.That(AnimationApi.FindParameter(Controller, new FixedString128Bytes("Missing")).Value, Is.EqualTo(-1));
        }
        [Test]
        public void Controller_OwnsRequestsAndAdvancesOnlyOncePerTick()
        {
            Configure(); Command(ScriptedPlaybackCommand.Play(new ClipId(1), 1)); Tick(1, .25);
            Assert.That(Layer.Initialized, Is.EqualTo(1)); Assert.That(Layer.NormalizedTime, Is.EqualTo(.25));
            Assert.That(world.EntityManager.GetBuffer<AnimationClipRequest>(rigEntity)[0].Clip.Value, Is.Zero);
            Tick(1, 10); Assert.That(Layer.NormalizedTime, Is.EqualTo(.25));
            Tick(1, 10, 1, AnimationStepMode.Seek); Assert.That(Layer.NormalizedTime, Is.EqualTo(.25));
            Assert.That(world.EntityManager.GetBuffer<ScriptedPlaybackCommand>(rigEntity).Length, Is.EqualTo(1));
        }
        [Test]
        public void Trigger_IsConsumedOnlyWhenAllConditionsPassAndFirstEdgeWins()
        {
            Configure(new Edge { Source = 0, Target = 1, Conditions = new[] { Condition(3, ControllerConditionMode.If), Condition(1, ControllerConditionMode.Greater, 2) } },
                new Edge { Source = 0, Target = 0, Conditions = new[] { Condition(3, ControllerConditionMode.If), Condition(1, ControllerConditionMode.Greater, 2) } });
            AnimationApi.SetTrigger(Controller, Values, new AnimationParameterId(3)); Tick(1, 0);
            Assert.That(Values[3].Int, Is.EqualTo(1)); Assert.That(Layer.CurrentState, Is.Zero);
            AnimationApi.SetInt(Controller, Values, new AnimationParameterId(1), 3); Tick(2, 0);
            Assert.That(Layer.CurrentState, Is.EqualTo(1)); Assert.That(Values[3].Int, Is.Zero);
        }
        [Test]
        public void AnyState_PrecedesStateTransitionsAndEntrySelectsInitialState()
        {
            Configure(new Edge { Source = -2, Target = 0, Conditions = new[] { Condition(3, ControllerConditionMode.If) } },
                new Edge { Source = -1, Target = 1, Conditions = new[] { Condition(2, ControllerConditionMode.If) } },
                new Edge { Source = 1, Target = 1, Conditions = new[] { Condition(3, ControllerConditionMode.If) } });
            AnimationApi.SetBool(Controller, Values, new AnimationParameterId(2), true); Tick(1, 0); Assert.That(Layer.CurrentState, Is.EqualTo(1));
            AnimationApi.SetTrigger(Controller, Values, new AnimationParameterId(3)); Tick(2, 0); Assert.That(Layer.CurrentState, Is.Zero);
        }
        [Test]
        public void CrossFade_AdvancesBothStatesAndPublishesWeightedRequests()
        {
            Configure(new Edge { Source = 0, Target = 1, Duration = .5f, Offset = .25f, Conditions = new[] { Condition(3, ControllerConditionMode.If) } });
            Tick(1, .2); AnimationApi.SetTrigger(Controller, Values, new AnimationParameterId(3)); Tick(2, 0);
            Assert.That(Layer.NextState, Is.EqualTo(1)); Assert.That(Layer.NextNormalizedTime, Is.EqualTo(.25));
            Tick(3, .25);
            var requests = world.EntityManager.GetBuffer<AnimationClipRequest>(rigEntity);
            Assert.That(requests.Length, Is.EqualTo(2)); Assert.That(requests[0].Weight, Is.EqualTo(.5f)); Assert.That(requests[1].Weight, Is.EqualTo(.5f));
            Assert.That(requests[1].Time, Is.EqualTo(.75).Within(1e-6));
            Tick(4, .25); Assert.That(Layer.CurrentState, Is.EqualTo(1)); Assert.That(Layer.NextState, Is.EqualTo(-1));
            Assert.That(ControllerStateQuery.TryGet(Layer, out var snapshot), Is.True); Assert.That(snapshot.IsInTransition, Is.False);
        }
        [Test]
        public void ExitTime_CrossesLoopBoundaryAndSupportsLargeSteps()
        {
            Configure(new Edge { Source = 0, Target = 1, Exit = true, ExitTime = .75f });
            Tick(1, .5); Assert.That(Layer.CurrentState, Is.Zero);
            Tick(2, 2.5); Assert.That(Layer.CurrentState, Is.EqualTo(1));
        }
        [Test]
        public void RootExit_ReentersThroughConditionalEntry()
        {
            Configure(new Edge { Source = -1, Target = 1, Conditions = new[] { Condition(2, ControllerConditionMode.If) } },
                new Edge { Source = 0, Target = -1, Conditions = new[] { Condition(3, ControllerConditionMode.If) } });
            Tick(1, 0); AnimationApi.SetBool(Controller, Values, new AnimationParameterId(2), true);
            AnimationApi.SetTrigger(Controller, Values, new AnimationParameterId(3)); Tick(2, 0);
            Assert.That(Layer.CurrentState, Is.EqualTo(1));
        }
        [Test]
        public void LibraryMismatch_ReportsFailureAndClearsRequests()
        {
            Configure(); var rigComponent = world.EntityManager.GetComponentData<AnimationRig>(rigEntity);
            rigComponent.Library = default; world.EntityManager.SetComponentData(rigEntity, rigComponent); Tick(1, .1);
            Assert.That(Layer.Result, Is.EqualTo(ControllerEvaluationResult.InvalidDefinition));
            Assert.That(world.EntityManager.GetBuffer<AnimationClipRequest>(rigEntity).Length, Is.Zero);
        }
        [Test]
        public void InvalidParameterLayoutAndDirectNonfiniteValues_FailSafely()
        {
            Configure(); var values = Values;
            values.RemoveAt(3);
            Assert.That(AnimationApi.SetFloat(Controller, values, new AnimationParameterId(0), 1), Is.EqualTo(AnimationParameterResult.InvalidLayout));
            Tick(1, .1); Assert.That(Layer.Result, Is.EqualTo(ControllerEvaluationResult.InvalidParameters));
            values = Values; values.Add(default); values[0] = new AnimationParameterValue { Float = float.PositiveInfinity };
            Tick(2, .1); Assert.That(Layer.Result, Is.EqualTo(ControllerEvaluationResult.InvalidParameters));
            Assert.That(world.EntityManager.GetBuffer<AnimationClipRequest>(rigEntity).Length, Is.Zero);
        }
        [Test]
        public void ThousandControllers_ShareDefinitionButKeepIndependentStateAndParameters()
        {
            Configure(new Edge { Source = 0, Target = 1, Duration = .25f, Conditions = new[] { Condition(2, ControllerConditionMode.If) } },
                new Edge { Source = 1, Target = 0, Duration = .25f, Conditions = new[] { Condition(2, ControllerConditionMode.IfNot) } });
            var em = world.EntityManager; em.AddComponent<Prefab>(rigEntity);
            using (var instances = new NativeArray<Entity>(1000, Allocator.Temp))
            {
                em.Instantiate(rigEntity, instances);
                for (var i = 0; i < instances.Length; i++)
                    AnimationApi.SetBool(Controller, em.GetBuffer<AnimationParameterValue>(instances[i]), new AnimationParameterId(2), i % 2 == 0);
                var samples = new double[180]; var watch = new System.Diagnostics.Stopwatch();
                for (var frame = 0; frame < 210; frame++)
                {
                    em.SetComponentData(stepEntity, new AnimationStepContext { Tick = (ulong)frame + 1, DeltaTime = 1.0 / 60 });
                    watch.Restart(); controllerSystem.Update(); em.CompleteAllTrackedJobs(); watch.Stop();
                    if (frame >= 30) samples[frame - 30] = watch.Elapsed.TotalMilliseconds;
                }
                System.Array.Sort(samples);
                TestContext.WriteLine("Controller1000 schedule+complete Editor median_ms=" + samples[90].ToString("F6", System.Globalization.CultureInfo.InvariantCulture)
                    + " p95_ms=" + samples[171].ToString("F6", System.Globalization.CultureInfo.InvariantCulture) + " frames=180 (controller only, no skinning/rendering)");
                for (var i = 0; i < instances.Length; i++)
                {
                    Assert.That(em.GetComponentData<AnimationController>(instances[i]).Definition, Is.EqualTo(graph));
                    Assert.That(em.GetComponentData<ControllerLayerState>(instances[i]).CurrentState, Is.EqualTo(i % 2 == 0 ? 1 : 0));
                }
                AnimationApi.SetBool(Controller, em.GetBuffer<AnimationParameterValue>(instances[0]), new AnimationParameterId(2), false);
                Assert.That(em.GetBuffer<AnimationParameterValue>(instances[2])[2].Int, Is.EqualTo(1));
                Assert.That(em.GetComponentData<ControllerLayerState>(rigEntity).Initialized, Is.Zero, "Prefab state must stay untouched.");
                em.DestroyEntity(instances);
            }
        }
    }
}
