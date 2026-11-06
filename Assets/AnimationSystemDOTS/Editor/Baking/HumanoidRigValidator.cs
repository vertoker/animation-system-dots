using System;
using System.Linq;
using UnityEngine;

namespace AnimationSystemDOTS.Editor.Baking
{
    public static class HumanoidRigValidator
    {
        public static void Validate(GameObject source)
        {
            var animator = source != null ? source.GetComponent<Animator>() : null;
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new ArgumentException("Humanoid baking requires an Animator with a valid Humanoid target Avatar on the rig root.");
            if (!animator.hasTransformHierarchy)
                throw new ArgumentException("Humanoid target Avatar requires an accessible skeleton; disable Optimize Game Objects before baking.");
            var description = animator.avatar.humanDescription;
            var names = source.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToHashSet();
            foreach (var bone in description.human)
                if (!names.Contains(bone.boneName))
                    throw new ArgumentException("Humanoid target Avatar bone is absent from the hierarchy: " + bone.boneName);
        }

        internal static string Identity(GameObject source)
        {
            var avatar = source.GetComponent<Animator>().avatar;
            // Includes imported asset dependency hash and in-memory Avatar proportions,
            // human limits and mapping. Retargeted tracks are specific to this target.
            return "|humanoid-editor-1|in-place-no-extraction|" + BakeContentKey.Asset(avatar)
                + "|" + JsonUtility.ToJson(avatar.humanDescription);
        }
    }
}
