using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace AnimationSystemDOTS.Sandbox.Tests
{
    public class HumanoidCrowdTests
    {
        [TestCase(1)]
        [TestCase(4)]
        public void Spawn1000_ClonesWholePrefabOnceWithIndependentPlacementClipPhaseAndSpeed(int clipCount)
        {
            BlobAssetReference<AnimationLibraryBlob> library;
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var data = ref builder.ConstructRoot<AnimationLibraryBlob>();
                var clips = builder.Allocate(ref data.Clips, clipCount);
                for (var i = 0; i < clips.Length; i++) clips[i].Duration = 2;
                library = builder.CreateBlobAssetReference<AnimationLibraryBlob>(Allocator.Persistent);
            }
            try
            {
                using (var world = new World("AnimationSystemDOTS Crowd Spawn Test"))
                {
                    var em = world.EntityManager;
                    var prefab = em.CreateEntity(typeof(Prefab), typeof(AnimationRig), typeof(LocalTransform));
                    em.SetComponentData(prefab, new AnimationRig { Library = library });
                    em.SetComponentData(prefab, LocalTransform.Identity); em.AddBuffer<ScriptedPlaybackCommand>(prefab);
                    var child = em.CreateEntity(typeof(Prefab), typeof(Parent)); em.SetComponentData(child, new Parent { Value = prefab });
                    var linked = em.AddBuffer<LinkedEntityGroup>(prefab); linked.Add(prefab); linked.Add(child);
                    var spawner = em.CreateEntity(typeof(HumanoidCrowdSpawn));
                    em.SetComponentData(spawner, new HumanoidCrowdSpawn { Prefab = prefab, Count = 1000, Columns = 40, Spacing = 2.4f });
                    var system = world.GetOrCreateSystemManaged<HumanoidCrowdSystem>(); system.Update(); system.Update();
                    using (var query = em.CreateEntityQuery(typeof(HumanoidCrowdMember), typeof(LocalTransform)))
                    using (var entities = query.ToEntityArray(Allocator.Temp))
                    {
                        Assert.That(entities.Length, Is.EqualTo(1000), "Each complete linked prefab must spawn once.");
                        var clips = new int[clipCount]; var positions = new HashSet<float3>(); var phases = new HashSet<double>(); var speeds = new HashSet<double>();
                        foreach (var entity in entities)
                        {
                            var member = em.GetComponentData<HumanoidCrowdMember>(entity); clips[member.Clip]++;
                            phases.Add(member.Phase); speeds.Add(member.Speed); positions.Add(em.GetComponentData<LocalTransform>(entity).Position);
                            var commands = em.GetBuffer<ScriptedPlaybackCommand>(entity);
                            Assert.That(commands.Length, Is.EqualTo(2)); Assert.That(commands[0].Type, Is.EqualTo(ScriptedCommandType.Play));
                            Assert.That(commands[0].Clip.Value, Is.EqualTo(member.Clip)); Assert.That(commands[0].Time, Is.EqualTo(member.Phase));
                            Assert.That(commands[1].Type, Is.EqualTo(ScriptedCommandType.SetSpeed)); Assert.That(commands[1].Speed, Is.EqualTo(member.Speed));
                            var instanceGroup = em.GetBuffer<LinkedEntityGroup>(entity);
                            Assert.That(instanceGroup.Length, Is.EqualTo(2));
                            Assert.That(em.GetComponentData<Parent>(instanceGroup[1].Value).Value, Is.EqualTo(entity), "Child references must remap to each instance.");
                        }
                        foreach (var count in clips) Assert.That(count, Is.EqualTo(1000 / clipCount));
                        Assert.That(positions.Count, Is.EqualTo(1000)); Assert.That(phases.Count, Is.GreaterThan(100)); Assert.That(speeds.Count, Is.GreaterThan(4));
                    }
                }
            }
            finally { library.Dispose(); }
        }
    }
}
