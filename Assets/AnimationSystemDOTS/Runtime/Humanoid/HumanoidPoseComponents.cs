using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace AnimationSystemDOTS.Humanoid
{
    // Asset reference only. There is no runtime Animator or bone hierarchy.
    public struct HumanoidRigBinding : IComponentData
    {
        public UnityObjectRef<Avatar> Avatar;
    }

    public enum HumanoidEvaluationResult : byte
    {
        Success, NoRequests, InvalidDefinition, InvalidChannels, UnsupportedBlendMode,
        UnsupportedMask, UnsupportedLayer, MissingAvatar, InvalidPose, RetainedPose
    }

    public struct HumanoidBodyPose : IComponentData
    {
        public float3 Position;
        public quaternion Rotation;
        public HumanoidEvaluationResult Result;
    }

    public struct HumanoidMuscleValue : IBufferElementData { public float Value; }
    // Separate muscle state of layers that own the hips/root. Retargeting
    // all masked muscle channels together must not counter-rotate that root.
    public struct HumanoidRootMuscleValue : IBufferElementData { public float Value; }
}
