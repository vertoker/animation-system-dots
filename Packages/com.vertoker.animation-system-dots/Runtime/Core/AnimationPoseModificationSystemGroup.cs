using Unity.Entities;
namespace AnimationSystemDOTS
{
    [UpdateInGroup(typeof(AnimationSimulationSystemGroup))]
    [UpdateAfter(typeof(AnimationBlendSystem))]
    [UpdateBefore(typeof(ModelPoseSystem))]
    public partial class AnimationPoseModificationSystemGroup : ComponentSystemGroup { }
}
