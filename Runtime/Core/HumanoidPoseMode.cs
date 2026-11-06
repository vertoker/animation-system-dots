using Unity.Entities;

namespace AnimationSystemDOTS
{
    public enum HumanoidPoseMode : byte
    {
        // Target-local TRS is fast, but blending differs from native Humanoid.
        BakedTargetTRS,
        PreciseMuscles
    }

    // Core pose jobs exclude these entities; the optional Humanoid assembly
    // owns their final local pose instead of sampling/blending TRS twice.
    public struct PreciseHumanoidPose : IComponentData { }
}
