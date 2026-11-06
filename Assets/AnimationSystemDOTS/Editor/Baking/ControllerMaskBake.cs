using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace AnimationSystemDOTS.Editor.Baking
{
    internal sealed class ControllerMaskBake : IDisposable
    {
        readonly List<BlobAssetReference<BoneMaskBlob>> masks = new List<BlobAssetReference<BoneMaskBlob>>();
        internal int Add(AvatarMask mask, GameObject source, BlobAssetReference<RigDefinitionBlob> rig, string path)
        {
            if (mask == null) return -1;
            if (source == null || !rig.IsCreated) throw new ArgumentException(path + ": A target hierarchy and baked rig are required to compile an Avatar Mask.");
            masks.Add(AvatarMaskBakePipeline.Build(mask, source, rig)); return masks.Count - 1;
        }
        internal void CopyTo(ref BlobBuilder builder, ref AnimatorControllerBlob root)
        {
            var target = builder.Allocate(ref root.Masks, masks.Count);
            for (var i = 0; i < masks.Count; i++)
            {
                ref var source = ref masks[i].Value;
                target[i].Version = source.Version; target[i].ContentKey = source.ContentKey; target[i].RigKey = source.RigKey; target[i].HumanRootWeight = source.HumanRootWeight;
                var bones = builder.Allocate(ref target[i].Weights, source.Weights.Length);
                for (var b = 0; b < bones.Length; b++) bones[b] = source.Weights[b];
                var muscles = builder.Allocate(ref target[i].HumanMuscleWeights, source.HumanMuscleWeights.Length);
                for (var m = 0; m < muscles.Length; m++) muscles[m] = source.HumanMuscleWeights[m];
            }
        }
        public void Dispose() { foreach (var mask in masks) if (mask.IsCreated) mask.Dispose(); }
    }
}
