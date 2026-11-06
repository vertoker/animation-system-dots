using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace AnimationSystemDOTS.Sandbox
{
    // Scene streaming can finish long after the World starts. Give the standard
    // fixed-rate manager a local origin so it does not replay that loading time.
    public sealed class CharacterFixedRateManager : IRateManager
    {
        readonly RateUtils.FixedRateCatchUpManager inner;
        double origin;
        bool initialized, scoped;
        public CharacterFixedRateManager(float timestep) => inner = new RateUtils.FixedRateCatchUpManager(timestep);
        public float Timestep { get => inner.Timestep; set => inner.Timestep = value; }
        public bool ShouldGroupUpdate(ComponentSystemGroup group)
        {
            if (!scoped)
            {
                var time = group.World.Time;
                if (!initialized) { origin = time.ElapsedTime; initialized = true; }
                group.World.PushTime(new TimeData(math.max(0, time.ElapsedTime - origin), time.DeltaTime)); scoped = true;
            }
            var update = inner.ShouldGroupUpdate(group);
            if (!update) { group.World.PopTime(); scoped = false; }
            return update;
        }
    }
}
