using UnityEngine;

namespace AnimationSystemDOTS.Authoring
{
    [CreateAssetMenu(menuName = "Animation System DOTS/Bake Settings")]
    public sealed class AnimationBakeSettings : AnimationValidationSettings
    {
        [Min(1)] public int MaximumSamplesPerClip = 65536;
        [Tooltip("Precise Muscles blends before retargeting and requires the Humanoid runtime adapter. Baked Target TRS preserves the previous fast approximate blending path.")]
        public HumanoidPoseMode HumanoidPoseMode;
    }
}
