using Unity.Entities;

namespace AnimationSystemDOTS
{
    public readonly struct AnimationParameterId
    {
        public readonly int Value;
        public AnimationParameterId(int value) => Value = value;
        public static AnimationParameterId Invalid => new AnimationParameterId(-1);
    }
    public struct AnimationParameterValue : IBufferElementData
    {
        public float Float;
        public int Int;
    }
    public enum AnimationParameterResult : byte { Success, InvalidId, InvalidType, InvalidValue, InvalidLayout }
}
