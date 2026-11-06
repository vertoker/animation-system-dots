using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace AnimationSystemDOTS.Sandbox
{
    public class HumanoidCrowd : MonoBehaviour
    {
        public GameObject Prefab;
        public int Count = 1000;
        public int Columns = 40;
        public float Spacing = 2.4f;
    }
    public struct HumanoidCrowdSpawn : IComponentData
    {
        public Entity Prefab;
        public int Count, Columns;
        public float Spacing;
        public byte Spawned;
    }
    public struct HumanoidCrowdMember : IComponentData
    {
        public int Index, Clip;
        public double Phase, Speed;
    }
    public struct HumanoidCrowdControl : IComponentData
    {
        public byte Manual;
        public double Time;
    }
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(AnimationSimulationSystemGroup))]
    public partial class HumanoidCrowdSystem : SystemBase
    {
        ulong tick;
        protected override void OnCreate() => RequireForUpdate<HumanoidCrowdSpawn>();
        protected override void OnUpdate()
        {
            var em = EntityManager; var spawnerEntity = SystemAPI.GetSingletonEntity<HumanoidCrowdSpawn>();
            var spawn = em.GetComponentData<HumanoidCrowdSpawn>(spawnerEntity);
            if (spawn.Spawned == 0)
            {
                if (!em.HasComponent<AnimationRig>(spawn.Prefab) || !em.HasBuffer<ScriptedPlaybackCommand>(spawn.Prefab)) return;
                var rig = em.GetComponentData<AnimationRig>(spawn.Prefab);
                if (!rig.Library.IsCreated || rig.Library.Value.Clips.Length == 0 || spawn.Count <= 0 || spawn.Columns <= 0) return;
                using (var instances = new NativeArray<Entity>(spawn.Count, Allocator.Temp))
                {
                    em.Instantiate(spawn.Prefab, instances); em.AddComponent<HumanoidCrowdMember>(instances);
                    var rows = (spawn.Count + spawn.Columns - 1) / spawn.Columns;
                    for (var i = 0; i < instances.Length; i++)
                    {
                        var clip = i % rig.Library.Value.Clips.Length;
                        var phase = ((i * 73) % 997) / 997.0 * rig.Library.Value.Clips[clip].Duration;
                        var speed = i == 0 ? 1 : .8 + (i % 17) * .025;
                        em.SetComponentData(instances[i], new HumanoidCrowdMember { Index = i, Clip = clip, Phase = phase, Speed = speed });
                        em.SetComponentData(instances[i], LocalTransform.FromPosition(new float3(
                            (i % spawn.Columns - (spawn.Columns - 1) * .5f) * spawn.Spacing, 0,
                            ((rows - 1) * .5f - i / spawn.Columns) * spawn.Spacing)));
                        var commands = em.GetBuffer<ScriptedPlaybackCommand>(instances[i]);
                        commands.Add(ScriptedPlaybackCommand.Play(new ClipId(clip), 1, phase));
                        commands.Add(ScriptedPlaybackCommand.SetSpeed(speed, 2));
                    }
                }
                spawn.Spawned = 1; em.SetComponentData(spawnerEntity, spawn);
                if (!SystemAPI.HasSingleton<AnimationStepContext>()) em.CreateEntity(typeof(AnimationStepContext));
                if (!SystemAPI.HasSingleton<HumanoidCrowdControl>()) em.CreateEntity(typeof(HumanoidCrowdControl));
            }
            var control = SystemAPI.GetSingleton<HumanoidCrowdControl>(); tick++;
            if (control.Manual != 0)
            {
                foreach (var (member, commands) in SystemAPI.Query<RefRO<HumanoidCrowdMember>, DynamicBuffer<ScriptedPlaybackCommand>>())
                    commands.Add(ScriptedPlaybackCommand.Seek(control.Time * member.ValueRO.Speed + member.ValueRO.Phase, tick + 3));
            }
            SystemAPI.SetSingleton(new AnimationStepContext { Tick = tick, DeltaTime = control.Manual != 0 ? 0 : SystemAPI.Time.DeltaTime });
        }
    }
}
