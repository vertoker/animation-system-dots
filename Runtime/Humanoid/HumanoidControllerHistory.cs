using Unity.Entities;
using Unity.Mathematics;

namespace AnimationSystemDOTS.Humanoid
{
    public struct HumanoidControllerMuscleHistory : IBufferElementData
    {
        public float Previous, BeforeStep, Frozen;
    }
    public struct HumanoidControllerBodyHistory : IBufferElementData
    {
        public BlobAssetReference<AnimatorControllerBlob> Definition;
        public float3 PreviousPosition, BeforePosition, FrozenPosition;
        public quaternion PreviousRotation, BeforeRotation, FrozenRotation;
        public ulong FrozenSequence;
        public byte Initialized;
        public ControllerHistoryStep Step;
    }
}
