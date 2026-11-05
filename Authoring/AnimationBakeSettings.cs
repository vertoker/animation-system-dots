using UnityEngine;

namespace AnimationSystemDOTS.Authoring
{
    [CreateAssetMenu(menuName = "Animation System DOTS/Bake Settings")]
    public sealed class AnimationBakeSettings : AnimationValidationSettings
    {
        [Min(1)] public int MaximumSamplesPerClip = 65536;
    }
}
