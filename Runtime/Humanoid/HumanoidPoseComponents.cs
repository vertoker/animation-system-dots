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
        UnsupportedMask, UnsupportedLayer, MissingAvatar, InvalidPose
    }

    public struct HumanoidBodyPose : IComponentData
    {
        public float3 Position;
        public quaternion Rotation;
        public HumanoidEvaluationResult Result;
    }

    public struct HumanoidMuscleValue : IBufferElementData { public float Value; }
}
