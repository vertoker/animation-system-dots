using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace AnimationSystemDOTS.Sandbox
{
    public sealed class ControllerCrowd : MonoBehaviour
    {
        public GameObject Prefab;
        public int Count = 1000;
    }
    public struct ControllerCrowdSpawn : IComponentData { public Entity Prefab; public int Count; public byte Spawned; }
    public struct ControllerCrowdMember : IComponentData { public int Index; public AnimationParameterId Workout, Speed; }
    public struct ControllerCrowdControl : IComponentData { public byte Automatic, Workout; public float Speed; }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(AnimationSimulationSystemGroup))]
    public partial class ControllerCrowdSystem : SystemBase
    {
        ulong tick;
        double elapsed;
        protected override void OnCreate() => RequireForUpdate<ControllerCrowdSpawn>();
        protected override void OnUpdate()
        {
            var em = EntityManager; var spawner = SystemAPI.GetSingletonEntity<ControllerCrowdSpawn>();
            var spawn = em.GetComponentData<ControllerCrowdSpawn>(spawner);
            if (spawn.Spawned == 0)
            {
                if (!em.HasComponent<AnimationController>(spawn.Prefab) || spawn.Count <= 0) return;
                var controller = em.GetComponentData<AnimationController>(spawn.Prefab);
                var workout = AnimationApi.FindParameter(controller, new FixedString128Bytes("Workout"));
                var speed = AnimationApi.FindParameter(controller, new FixedString128Bytes("Speed"));
                if (workout.Value < 0 || speed.Value < 0) return;
                using (var instances = new NativeArray<Entity>(spawn.Count, Allocator.Temp))
                {
                    em.Instantiate(spawn.Prefab, instances); em.AddComponent<ControllerCrowdMember>(instances);
                    var rows = (spawn.Count + 39) / 40;
                    for (var i = 0; i < instances.Length; i++)
                    {
                        em.SetComponentData(instances[i], new ControllerCrowdMember { Index = i, Workout = workout, Speed = speed });
                        em.SetComponentData(instances[i], LocalTransform.FromPosition(new float3((i % 40 - 19.5f) * 2.4f, 0, ((rows - 1) * .5f - i / 40) * 2.4f)));
                    }
                }
                if (!SystemAPI.HasSingleton<AnimationStepContext>()) em.CreateEntity(typeof(AnimationStepContext));
                if (!SystemAPI.HasSingleton<ControllerCrowdControl>())
                {
                    var controlEntity = em.CreateEntity(typeof(ControllerCrowdControl));
                    em.SetComponentData(controlEntity, new ControllerCrowdControl { Automatic = 1, Speed = 1 });
                }
                spawn.Spawned = 1; em.SetComponentData(spawner, spawn);
            }
            var dt = SystemAPI.Time.DeltaTime; elapsed += dt;
            Dependency = new DriveParameters { Elapsed = elapsed, Control = SystemAPI.GetSingleton<ControllerCrowdControl>() }.ScheduleParallel(Dependency);
            SystemAPI.SetSingleton(new AnimationStepContext { Tick = ++tick, DeltaTime = dt });
        }
        [BurstCompile]
        partial struct DriveParameters : IJobEntity
        {
            public double Elapsed;
            public ControllerCrowdControl Control;
            void Execute(in ControllerCrowdMember member, in AnimationController controller, DynamicBuffer<AnimationParameterValue> parameters)
            {
                var workout = Control.Automatic != 0 ? (int)math.floor((Elapsed + member.Index * .037) / 6) % 2 != 0 : Control.Workout != 0;
                AnimationApi.SetBool(controller, parameters, member.Workout, workout);
                AnimationApi.SetFloat(controller, parameters, member.Speed, Control.Speed * (.8f + member.Index % 17 * .025f));
            }
        }
    }
}
