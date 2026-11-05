using System;
using UnityEngine;

namespace AnimationSystemDOTS.Authoring
{
    [DisallowMultipleComponent, RequireComponent(typeof(AnimationRigAuthoring))]
    public sealed class AnimationLibraryAuthoring : MonoBehaviour
    {
        public AnimationClip[] Clips = Array.Empty<AnimationClip>();
    }
}
