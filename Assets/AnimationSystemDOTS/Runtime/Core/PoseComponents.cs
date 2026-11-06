using Unity.Entities;
using Unity.Mathematics;

namespace AnimationSystemDOTS
{
    public struct BoneLocalPose : IBufferElementData
    {
        public float3 Position;
        public quaternion Rotation;
        public float3 Scale;
        public static BoneLocalPose Identity => new BoneLocalPose { Rotation = quaternion.identity, Scale = new float3(1) };
    }

    public struct BoneModelPose : IBufferElementData { public float3x4 Matrix; }
    // Per-rig scratch, request-major then bone-major. Never shared between rig entities.
    public struct SampledBonePose : IBufferElementData
    {
        public BoneLocalPose Pose;
        public byte IsValid;
    }

    public static class PoseValidation
    {
        public static bool IsValid(in BoneLocalPose pose)
        {
            var norm = math.lengthsq(pose.Rotation.value);
            return math.all(math.isfinite(pose.Position)) && math.all(math.isfinite(pose.Scale))
                && math.all(math.abs(pose.Scale) > 1e-8f) && math.isfinite(norm) && math.abs(norm - 1) < 1e-3f;
        }
    }
}
