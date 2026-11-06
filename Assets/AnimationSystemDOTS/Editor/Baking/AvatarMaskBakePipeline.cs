using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace AnimationSystemDOTS.Editor.Baking
{
    public static class AvatarMaskBakePipeline
    {
        public static BlobAssetReference<BoneMaskBlob> Build(AvatarMask mask, GameObject source, BlobAssetReference<RigDefinitionBlob> rig)
        {
            if (mask == null || source == null || !rig.IsCreated) throw new ArgumentException("Avatar mask bake requires a mask, target hierarchy and rig.");
            var animator = source.GetComponent<Animator>();
            var human = animator != null && animator.avatar != null && animator.avatar.isHuman && animator.avatar.isValid;
            var humanParts = new Dictionary<string, AvatarMaskBodyPart>(StringComparer.Ordinal);
            if (human)
                foreach (var bone in animator.avatar.humanDescription.human)
                {
                    var part = AvatarMaskBodyPart.Body;
                    var name = bone.humanName;
                    if (name.Contains("Leg") || name.Contains("Foot") || name.Contains("Toes")) part = name.StartsWith("Left", StringComparison.Ordinal) ? AvatarMaskBodyPart.LeftLeg : AvatarMaskBodyPart.RightLeg;
                    else if (name.Contains("Thumb") || name.Contains("Index") || name.Contains("Middle") || name.Contains("Ring") || name.Contains("Little")) part = name.StartsWith("Left", StringComparison.Ordinal) ? AvatarMaskBodyPart.LeftFingers : AvatarMaskBodyPart.RightFingers;
                    else if (name.Contains("Arm") || name.Contains("Hand") || name.Contains("Shoulder")) part = name.StartsWith("Left", StringComparison.Ordinal) ? AvatarMaskBodyPart.LeftArm : AvatarMaskBodyPart.RightArm;
                    else if (name.Contains("Head") || name.Contains("Neck") || name.Contains("Eye") || name.Contains("Jaw")) part = AvatarMaskBodyPart.Head;
                    humanParts[bone.boneName] = part;
                }
            var transforms = new Dictionary<string, bool>(StringComparer.Ordinal);
            for (var i = 0; i < mask.transformCount; i++) transforms[mask.GetTransformPath(i)] = mask.GetTransformActive(i);
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<BoneMaskBlob>(); root.Version = AnimationBakeVersion.Schema; root.RigKey = rig.Value.ContentKey;
                root.ContentKey = new FixedString128Bytes(BakeContentKey.Hash("mask:" + AnimationBakeVersion.Schema + ":" + rig.Value.ContentKey + ":" + BakeContentKey.Asset(mask) + ":" + EditorJsonUtility.ToJson(mask)));
                var weights = builder.Allocate(ref root.Weights, rig.Value.Paths.Length);
                for (var b = 0; b < weights.Length; b++)
                {
                    var path = rig.Value.Paths[b].ToString();
                    if (human)
                    {
                        var name = path.Substring(path.LastIndexOf('/') + 1);
                        weights[b] = humanParts.TryGetValue(name, out var part) ? (mask.GetHumanoidBodyPartActive(part) ? 1 : 0)
                            : b == 0 ? (mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Root) ? 1 : 0) : weights[rig.Value.Parents[b]];
                    }
                    else weights[b] = transforms.TryGetValue(path, out var active) && active ? 1 : 0;
                }
                if (human)
                {
                    var muscles = builder.Allocate(ref root.HumanMuscleWeights, HumanTrait.MuscleCount);
                    for (var m = 0; m < muscles.Length; m++)
                    {
                        var part = m < 9 ? AvatarMaskBodyPart.Body : m < 21 ? AvatarMaskBodyPart.Head
                            : m < 29 ? AvatarMaskBodyPart.LeftLeg : m < 37 ? AvatarMaskBodyPart.RightLeg
                            : m < 46 ? AvatarMaskBodyPart.LeftArm : m < 55 ? AvatarMaskBodyPart.RightArm
                            : m < 75 ? AvatarMaskBodyPart.LeftFingers : AvatarMaskBodyPart.RightFingers;
                        muscles[m] = mask.GetHumanoidBodyPartActive(part) ? 1 : 0;
                    }
                    // Root-off layers preserve the prior hips pose. Their masked
                    // torso changes still alter the inverse-read body COM; that
                    // is not a request to recompute hips from the new torso.
                    root.HumanRootWeight = mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Root) ? 1 : 0;
                }
                return builder.CreateBlobAssetReference<BoneMaskBlob>(Allocator.Persistent);
            }
        }
    }
}
