using AnimationSystemDOTS;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace AnimationSystemDOTS.Sandbox
{
    public class GenericPlaybackDemo : MonoBehaviour
    {
        public GameObject RigPrefab;
        public GameObject LodRigPrefab;
    }

    public struct GenericPlaybackSpawn : IComponentData
    {
        public Entity Prefab;
        public Entity First;
        public Entity Second;
        public byte Spawned;
    }

    public struct GenericPlaybackControl : IComponentData
    {
        public double Time;
        public byte Manual;
    }
    public struct GenericPlaybackLODSource : IComponentData { public Entity Prefab; }

    // This sample owns the external clock. The animation package itself never
    // derives simulation ticks from UnityEngine.Time or a rendering frame.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(AnimationSimulationSystemGroup))]
    public partial class GenericPlaybackDemoSystem : SystemBase
    {
        ulong tick;
        protected override void OnCreate() => RequireForUpdate<GenericPlaybackSpawn>();
        protected override void OnUpdate()
        {
            var em = EntityManager;
            var spawnerEntity = SystemAPI.GetSingletonEntity<GenericPlaybackSpawn>();
            var spawn = em.GetComponentData<GenericPlaybackSpawn>(spawnerEntity);
            if (spawn.Spawned == 0)
            {
                if (SystemAPI.HasSingleton<GenericPlaybackLODSource>() && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-animationCaptureLOD") >= 0)
                    spawn.Prefab = SystemAPI.GetSingleton<GenericPlaybackLODSource>().Prefab;
                if (!em.HasComponent<AnimationRig>(spawn.Prefab) || !em.HasBuffer<ScriptedPlaybackCommand>(spawn.Prefab)) return;
                spawn.First = em.Instantiate(spawn.Prefab); spawn.Second = em.Instantiate(spawn.Prefab); spawn.Spawned = 1;
                em.SetComponentData(spawn.First, LocalTransform.FromPosition(-2.8f, 0, 0));
                em.SetComponentData(spawn.Second, LocalTransform.FromPosition(2.8f, 0, 0));
                em.AddComponentData(spawn.Second, new PostTransformMatrix { Value = float4x4.Scale(new float3(-1, 1.3f, 1)) });
                em.SetComponentData(spawnerEntity, spawn);
                em.GetBuffer<ScriptedPlaybackCommand>(spawn.First).Add(ScriptedPlaybackCommand.Play(new ClipId(0), 1));
                em.GetBuffer<ScriptedPlaybackCommand>(spawn.Second).Add(ScriptedPlaybackCommand.Play(new ClipId(0), 1, .65));
                if (!SystemAPI.HasSingleton<AnimationStepContext>()) em.CreateEntity(typeof(AnimationStepContext));
                if (!SystemAPI.HasSingleton<GenericPlaybackControl>()) em.CreateEntity(typeof(GenericPlaybackControl));
            }
            var control = SystemAPI.GetSingleton<GenericPlaybackControl>();
            tick++;
            if (control.Manual != 0)
            {
                em.GetBuffer<ScriptedPlaybackCommand>(spawn.First).Add(ScriptedPlaybackCommand.Seek(control.Time, tick + 1));
                em.GetBuffer<ScriptedPlaybackCommand>(spawn.Second).Add(ScriptedPlaybackCommand.Seek(control.Time + .65, tick + 1));
            }
            SystemAPI.SetSingleton(new AnimationStepContext { Tick = tick, DeltaTime = control.Manual != 0 ? 0 : SystemAPI.Time.DeltaTime });
        }
    }
}
