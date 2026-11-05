using Unity.Entities;
using Unity.Transforms;

namespace AnimationSystemDOTS
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial class AnimationSimulationSystemGroup : ComponentSystemGroup { }
}
