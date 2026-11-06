using AnimationSystemDOTS.Humanoid;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace AnimationSystemDOTS.Sandbox
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class CharacterBootstrapSystem : SystemBase
    {
        EntityQuery configs, controls, members;
        Entity ownedStep;
        IRateManager previousRate;
        CharacterFixedRateManager rate;
        AnimationSimulationSystemGroup animationGroup;
        byte idleFrames;
        protected override void OnCreate()
        {
            configs = GetEntityQuery(typeof(CharacterSceneConfig)); controls = GetEntityQuery(typeof(CharacterSceneControl)); members = GetEntityQuery(typeof(CharacterMember));
        }
        protected override void OnUpdate()
        {
            var em = EntityManager; em.CompleteAllTrackedJobs();
            using (var actors = members.ToEntityArray(Allocator.Temp))
                foreach (var actor in actors)
                { var owner = em.GetComponentData<CharacterMember>(actor).Owner; if (!em.Exists(owner) || !em.HasComponent<CharacterSceneConfig>(owner)) em.DestroyEntity(actor); }
            using (var owners = controls.ToEntityArray(Allocator.Temp))
                foreach (var owner in owners) if (!em.HasComponent<CharacterSceneConfig>(owner)) em.RemoveComponent<CharacterSceneControl>(owner);
            using (var owners = configs.ToEntityArray(Allocator.Temp))
                foreach (var owner in owners)
                {
                    var config = em.GetComponentData<CharacterSceneConfig>(owner);
                    if (!em.Exists(config.Prefab) || !em.HasComponent<AnimationController>(config.Prefab) || !em.HasComponent<AnimationRig>(config.Prefab)) continue;
                    var controller = em.GetComponentData<AnimationController>(config.Prefab); if (!controller.Definition.IsCreated) continue;
                    var exists = em.HasComponent<CharacterSceneControl>(owner); var control = exists ? em.GetComponentData<CharacterSceneControl>(owner) : default;
                    if (exists && (control.Prefab != config.Prefab || control.Definition != controller.Definition || control.Count != math.clamp(config.Count, 1, 1000) || control.Bots != config.Bots || control.Seed != config.Seed))
                    {
                        using (var actors = members.ToEntityArray(Allocator.Temp)) foreach (var actor in actors) if (em.GetComponentData<CharacterMember>(actor).Owner == owner) em.DestroyEntity(actor);
                        exists = false;
                    }
                    if (!exists)
                    {
                        var ids = CharacterParameters.Resolve(controller); var count = math.clamp(config.Count, 1, 1000); var columns = (int)math.ceil(math.sqrt(count));
                        var death = new float3(4);
                        ref var graph = ref controller.Definition.Value;
                        for (var s = 0; s < graph.States.Length; s++)
                            for (var d = 0; d < 3; d++) if (graph.States[s].Path.ToString().EndsWith("Death" + d, System.StringComparison.Ordinal)) death[d] = (float)graph.States[s].Duration / math.max(.001f, math.abs(graph.States[s].Speed));
                        using (var actors = new NativeArray<Entity>(count, Allocator.Temp))
                        {
                            em.Instantiate(config.Prefab, actors);
                            for (var i = 0; i < count; i++)
                            {
                                var position = config.Bots != 0 ? new float3((i % columns - (columns - 1) * .5f) * 3, 0, (i / columns - (columns - 1) * .5f) * 3) : float3.zero;
                                var random = Unity.Mathematics.Random.CreateFromIndex(math.hash(new uint2(config.Seed, (uint)i)));
                                em.AddComponentData(actors[i], new CharacterMember { Owner = owner, Index = i, Bot = config.Bots, ResetRequested = 1, ResetPosition = position,
                                    DeathDurations = death, Durations = config.Durations, Weights = config.Weights });
                                em.AddComponentData(actors[i], ids);
                                em.AddComponentData(actors[i], new CharacterBotState { Random = random, Jump = random.NextFloat(1, 5), Action = random.NextFloat(.5f, 4), Goal = position.xz, Speed = 3 });
                                Ensure<ControllerLayerRuntime>(actors[i]); Ensure<ControllerCachedRequest>(actors[i]);
                                Ensure<ControllerBoneHistory>(actors[i]); Ensure<HumanoidControllerBodyHistory>(actors[i]); Ensure<HumanoidControllerMuscleHistory>(actors[i]);
                            }
                            control = new CharacterSceneControl { Primary = actors[0], Prefab = config.Prefab, Definition = controller.Definition, Count = count, Bots = config.Bots, Seed = config.Seed };
                        }
                        if (em.HasComponent<CharacterSceneControl>(owner)) em.SetComponentData(owner, control); else em.AddComponentData(owner, control);
                    }
                    else
                    {
                        using (var actors = members.ToEntityArray(Allocator.Temp)) foreach (var actor in actors)
                        {
                            var member = em.GetComponentData<CharacterMember>(actor); if (member.Owner != owner) continue;
                            member.Durations = config.Durations; member.Weights = config.Weights; em.SetComponentData(actor, member);
                        }
                    }
                }
            if (!controls.IsEmptyIgnoreFilter)
            {
                idleFrames = 0;
                using (var stepQuery = em.CreateEntityQuery(typeof(AnimationStepContext))) if (stepQuery.IsEmptyIgnoreFilter) ownedStep = em.CreateEntity(typeof(AnimationStepContext));
                if (rate == null)
                { animationGroup = World.GetOrCreateSystemManaged<AnimationSimulationSystemGroup>(); previousRate = animationGroup.RateManager; rate = new CharacterFixedRateManager(AnimatorValidationSettings.Step); animationGroup.RateManager = rate; }
            }
            else
            {
                if (rate != null) { if (animationGroup != null && animationGroup.RateManager == rate) animationGroup.RateManager = previousRate; rate = null; }
                // One empty animation update releases native retarget solver resources.
                if (++idleFrames > 1 && em.Exists(ownedStep)) { em.DestroyEntity(ownedStep); ownedStep = Entity.Null; }
                if (idleFrames > 2) idleFrames = 2;
            }
        }
        void Ensure<T>(Entity entity) where T : unmanaged, IBufferElementData { if (!EntityManager.HasBuffer<T>(entity)) EntityManager.AddBuffer<T>(entity); }
        protected override void OnDestroy()
        { if (rate != null && animationGroup != null && animationGroup.RateManager == rate) animationGroup.RateManager = previousRate; }
    }
}
