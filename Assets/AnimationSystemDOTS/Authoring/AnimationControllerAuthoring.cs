using UnityEngine;

namespace AnimationSystemDOTS.Authoring
{
    [DisallowMultipleComponent, RequireComponent(typeof(AnimationRigAuthoring))]
    public sealed class AnimationControllerAuthoring : MonoBehaviour
    {
        public RuntimeAnimatorController Controller;
    }
}
