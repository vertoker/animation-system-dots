using System.Collections.Generic;
using NUnit.Framework;
using Unity.Core;
using Unity.Entities;

namespace AnimationSystemDOTS.Sandbox.Tests
{
    [DisableAutoCreation]
    public partial class CharacterRateProbe : SystemBase
    {
        public readonly List<double> Times = new List<double>();
        public readonly List<float> Deltas = new List<float>();
        protected override void OnUpdate() { Times.Add(World.Time.ElapsedTime); Deltas.Add(World.Time.DeltaTime); }
    }
    public class CharacterRateTests
    {
        [Test]
        public void FixedScope_StartsAtSceneArrivalAndRestoresTheOuterWorldClock()
        {
            using (var world = new World("Character fixed scope"))
            {
                var group = world.GetOrCreateSystemManaged<AnimationSimulationSystemGroup>(); var probe = world.GetOrCreateSystemManaged<CharacterRateProbe>();
                group.AddSystemToUpdateList(probe); group.RateManager = new CharacterFixedRateManager(AnimatorValidationSettings.Step);
                world.SetTime(new TimeData(100, .02f)); group.Update();
                Assert.That(probe.Times.Count, Is.EqualTo(1)); Assert.That(probe.Times[0], Is.Zero);
                Assert.That(world.Time.ElapsedTime, Is.EqualTo(100)); Assert.That(world.Time.DeltaTime, Is.EqualTo(.02f));
                world.SetTime(new TimeData(100.051, .051f)); group.Update();
                Assert.That(probe.Times.Count, Is.EqualTo(4));
                foreach (var dt in probe.Deltas) Assert.That(dt, Is.EqualTo(AnimatorValidationSettings.Step));
                Assert.That(world.Time.ElapsedTime, Is.EqualTo(100.051)); Assert.That(world.Time.DeltaTime, Is.EqualTo(.051f));
                group.Update(); Assert.That(probe.Times.Count, Is.EqualTo(4), "An outer update at the same time must not run another simulation tick.");
            }
        }
    }
}
