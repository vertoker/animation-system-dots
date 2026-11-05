using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace AnimationSystemDOTS.Editor.Baking
{
    internal static class MeshBoundsBakePipeline
    {
        internal static AnimationBounds[] Build(Mesh mesh, Matrix4x4[] bindposes, int[] remap,
            BlobAssetReference<RigDefinitionBlob> rig, BlobAssetReference<AnimationLibraryBlob> library, out AnimationBounds envelope)
        {
            if (library.IsCreated && library.Value.RigKey != rig.Value.ContentKey) throw new ArgumentException("Mesh bounds require the matching animation library.");
            var minimum = new float3[remap.Length]; var maximum = new float3[remap.Length]; var used = new bool[remap.Length];
            var vertices = mesh.vertices;
            var legacyWeights = mesh.boneWeights;
            var shapeExpansion = new float3[vertices.Length];
            var deltas = new Vector3[vertices.Length];
            for (var shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                var shapeMaximum = new float3[vertices.Length];
                for (var frame = 0; frame < mesh.GetBlendShapeFrameCount(shape); frame++)
                {
                    mesh.GetBlendShapeFrameVertices(shape, frame, deltas, null, null);
                    for (var v = 0; v < vertices.Length; v++) shapeMaximum[v] = math.max(shapeMaximum[v], math.abs((float3)deltas[v]));
                }
                for (var v = 0; v < vertices.Length; v++) shapeExpansion[v] += shapeMaximum[v];
            }
            using (var counts = mesh.GetBonesPerVertex())
            using (var weights = mesh.GetAllBoneWeights())
            {
                var cursor = 0;
                for (var vertex = 0; vertex < vertices.Length; vertex++)
                {
                    // Unity retains the authored four-weight values here, while
                    // GetAllBoneWeights normalizes them even for malformed input.
                    if (counts[vertex] <= 4 && legacyWeights.Length == vertices.Length)
                    {
                        var legacy = legacyWeights[vertex];
                        var total = legacy.weight0 + legacy.weight1 + legacy.weight2 + legacy.weight3;
                        if (!math.isfinite(total) || math.abs(total - 1) > 1e-4f)
                            throw new ArgumentException("Each skinned vertex requires weights summing to one before Unity normalization.");
                    }
                    if (!math.all(math.isfinite((float3)vertices[vertex])) || !math.all(math.isfinite(shapeExpansion[vertex])))
                        throw new ArgumentException("Mesh vertices and blend shape deltas must be finite.");
                    var sum = 0f;
                    for (var influence = 0; influence < counts[vertex]; influence++)
                    {
                        var weight = weights[cursor++];
                        if (!math.isfinite(weight.weight) || weight.weight < 0 || (uint)weight.boneIndex >= (uint)remap.Length)
                            throw new ArgumentException("Mesh contains invalid bone indices or skin weights.");
                        sum += weight.weight;
                        if (weight.weight == 0) continue;
                        var bone = weight.boneIndex;
                        float4x4 bindpose = bindposes[bone];
                        var point = math.transform(bindpose, (float3)vertices[vertex]);
                        var expansion = math.mul(new float3x3(math.abs(bindpose.c0.xyz), math.abs(bindpose.c1.xyz), math.abs(bindpose.c2.xyz)), shapeExpansion[vertex]);
                        if (!used[bone]) { minimum[bone] = point - expansion; maximum[bone] = point + expansion; used[bone] = true; }
                        else { minimum[bone] = math.min(minimum[bone], point - expansion); maximum[bone] = math.max(maximum[bone], point + expansion); }
                    }
                    if (!math.isfinite(sum) || math.abs(sum - 1) > 1e-4f) throw new ArgumentException("Each skinned vertex requires nonnegative weights summing to one.");
                }
            }
            var bounds = new AnimationBounds[remap.Length];
            for (var bone = 0; bone < bounds.Length; bone++)
                bounds[bone] = new AnimationBounds { Center = (minimum[bone] + maximum[bone]) * .5f, Extents = (maximum[bone] - minimum[bone]) * .5f };

            // Linear translation/scale interpolation stays within endpoint norms;
            // normalized rotation has operator norm one. Recursing scalar radii is
            // conservative even between samples, through negative scales and shear.
            var count = rig.Value.Parents.Length;
            var position = new float[count]; var scale = new float[count];
            for (var bone = 0; bone < count; bone++)
            {
                Include(rig.Value.RestLocalPoses[bone], ref position[bone], ref scale[bone]);
                if (!library.IsCreated) continue;
                for (var clip = 0; clip < library.Value.Clips.Length; clip++)
                {
                    ref var samples = ref library.Value.Clips[clip].Tracks[bone].Samples;
                    for (var sample = 0; sample < samples.Length; sample++) Include(samples[sample], ref position[bone], ref scale[bone]);
                }
            }
            for (var bone = 0; bone < count; bone++)
            {
                var parent = rig.Value.Parents[bone];
                if (parent < 0) continue;
                position[bone] = position[parent] + scale[parent] * position[bone]; scale[bone] *= scale[parent];
            }
            var radius = 0f;
            for (var bone = 0; bone < bounds.Length; bone++)
                if (used[bone]) radius = math.max(radius, position[remap[bone]] + scale[remap[bone]] * math.length(math.abs(bounds[bone].Center) + bounds[bone].Extents));
            radius += math.max(1, radius) * 1e-4f;
            if (!math.isfinite(radius)) throw new ArgumentException("Animated mesh envelope exceeds finite float bounds.");
            envelope = new AnimationBounds { Center = float3.zero, Extents = new float3(radius) };
            return bounds;
        }
        static void Include(BoneLocalPose pose, ref float position, ref float scale)
        {
            position = math.max(position, math.length(pose.Position)); scale = math.max(scale, math.cmax(math.abs(pose.Scale)));
        }
    }
}
