using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace AnimationSystemDOTS.Editor.Baking
{
    public static class SkinnedMeshBindingBakePipeline
    {
        public static BlobAssetReference<SkinnedMeshBindingBlob> Build(GameObject source, SkinnedMeshRenderer renderer,
            BlobAssetReference<RigDefinitionBlob> rig, BlobAssetReference<AnimationLibraryBlob> library = default)
        {
            if (source == null || renderer == null || !rig.IsCreated) throw new ArgumentException("A source rig and skinned renderer are required.");
            var mesh = renderer.sharedMesh;
            if (mesh == null || !mesh.isReadable || mesh.vertexCount == 0) throw new ArgumentException("Skinned mesh must contain readable vertex data for binding and conservative bounds.");
            var bones = renderer.bones; var poses = mesh.bindposes;
            if (bones.Length == 0 || bones.Length != poses.Length || mesh.boneWeights.Length != mesh.vertexCount)
                throw new ArgumentException("Skinned mesh requires matching bones, bindposes and vertex weights.");
            if (renderer.gameObject.isStatic) throw new ArgumentException("Animated skinned renderers must not be marked Static.");
            if (mesh.normals.Length != mesh.vertexCount || mesh.tangents.Length != mesh.vertexCount)
                throw new ArgumentException("Compute Deformation requires normals and tangents on every vertex.");
            var materials = renderer.sharedMaterials;
            if (materials.Length != mesh.subMeshCount) throw new ArgumentException("Every skinned submesh requires a Compute Deformation material.");
            foreach (var material in materials)
                if (material == null || (!material.HasProperty("_ComputeMeshIndex") && !material.HasProperty("_DotsDeformationParams")))
                    throw new ArgumentException("Every skinned material must use a Shader Graph with Compute Deformation position, normal and tangent outputs.");
            for (var shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                var weight = renderer.GetBlendShapeWeight(shape);
                if (!math.isfinite(weight) || weight < 0 || weight > 100)
                    throw new ArgumentException("Blend shape weights must remain in the supported conservative envelope [0, 100].");
            }
            var root = renderer.rootBone != null ? renderer.rootBone : renderer.transform;
            var rootIndex = Find(source.transform, root, ref rig.Value);
            var remap = new int[bones.Length];
            var key = new StringBuilder("skin-binding-1|").Append(rig.Value.ContentKey).Append('|').Append(BakeContentKey.Asset(mesh)).Append('|').Append(rootIndex);
            for (var i = 0; i < bones.Length; i++)
            {
                remap[i] = Find(source.transform, bones[i], ref rig.Value); key.Append('|').Append(remap[i]);
                for (var row = 0; row < 4; row++) for (var column = 0; column < 4; column++)
                {
                    var value = poses[i][row, column];
                    if (!math.isfinite(value)) throw new ArgumentException("Mesh bindposes must be finite.");
                    key.Append('|').Append(BakeContentKey.Float(value));
                }
            }
            var boneBounds = MeshBoundsBakePipeline.Build(mesh, poses, remap, rig, library, out var rigBounds);
            key.Append('|').Append(library.IsCreated ? library.Value.ContentKey.ToString() : "rest");
            foreach (var bounds in boneBounds) AppendBounds(key, bounds);
            AppendBounds(key, rigBounds);
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var blob = ref builder.ConstructRoot<SkinnedMeshBindingBlob>();
                blob.Version = AnimationBakeVersion.Schema; blob.RigKey = rig.Value.ContentKey;
                blob.ContentKey = new FixedString128Bytes(BakeContentKey.Hash(key.ToString())); blob.DeformationRootBone = rootIndex;
                var indices = builder.Allocate(ref blob.BoneRemap, bones.Length);
                var bindposes = builder.Allocate(ref blob.BindPoses, bones.Length);
                var bounds = builder.Allocate(ref blob.BoneBounds, bones.Length);
                for (var i = 0; i < bones.Length; i++) { indices[i] = remap[i]; bindposes[i] = poses[i]; bounds[i] = boneBounds[i]; }
                blob.RigBounds = rigBounds;
                return builder.CreateBlobAssetReference<SkinnedMeshBindingBlob>(Allocator.Persistent);
            }
        }

        static void AppendBounds(StringBuilder key, AnimationBounds bounds)
        {
            for (var axis = 0; axis < 3; axis++) key.Append('|').Append(BakeContentKey.Float(bounds.Center[axis])).Append('|').Append(BakeContentKey.Float(bounds.Extents[axis]));
        }

        static int Find(Transform source, Transform bone, ref RigDefinitionBlob rig)
        {
            if (bone == null) throw new ArgumentException("Skinned mesh contains a missing bone.");
            var parts = new List<string>(); var current = bone;
            while (current != source && current != null) { parts.Add(current.name); current = current.parent; }
            if (current != source) throw new ArgumentException("Skinned mesh bones and deformation root must belong to the same animation rig.");
            parts.Reverse(); var path = new FixedString512Bytes(string.Join("/", parts));
            var index = RigPathLookup.Find(ref rig, path);
            if (index < 0) throw new ArgumentException("Skinned mesh bone path was not found in its rig: " + path);
            return index;
        }
    }
}
