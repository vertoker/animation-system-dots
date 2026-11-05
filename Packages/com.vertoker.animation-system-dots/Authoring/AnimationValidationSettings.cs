using UnityEngine;

namespace AnimationSystemDOTS.Authoring
{
    [CreateAssetMenu(menuName = "Animation System DOTS/Validation Settings")]
    public sealed class AnimationValidationSettings : ScriptableObject
    {
        [Min(1)] public int SampleRate = 30;
        [Min(0)] public float PositionTolerance = 0.0001f;
        [Min(0)] public float AngleToleranceDegrees = 0.01f;
        [Min(0)] public float ScaleTolerance = 0.0001f;
        [Min(0)] public float ScalarTolerance = 0.0001f;
        public double[] CaptureTimes = { 0, 0.25, 0.5, 0.75, 1 };
        public string FixtureRevision = "generic-two-bone-1";
    }
}
