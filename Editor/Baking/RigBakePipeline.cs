using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace AnimationSystemDOTS.Editor.Baking
{
    public struct RigBakeResult
    {
        public bool Success;
        // Standalone callers own this allocation. Bakers transfer ownership to the blob store.
        public BlobAssetReference<RigDefinitionBlob> Rig;
        public string[] Diagnostics;
    }

    public static class RigBakePipeline
    {
        public static RigBakeResult Bake(GameObject source)
        {
            if (source == null) return Failure("<null rig>: source hierarchy is required.");
            var builder = default(BlobBuilder);
            var ownsBuilder = false;
            try
            {
                var animator = source.GetComponent<Animator>();
                if (animator != null && !animator.hasTransformHierarchy)
                    return Failure(source.name + ": <root>: Optimize Game Objects removed the accessible skeleton; supply an unoptimized source rig.");
                var bones = new List<Transform>();
                var parents = new List<int>();
                var paths = new List<string>();
                var rest = new List<BoneLocalPose>();
                var visited = new HashSet<string>(StringComparer.Ordinal);
                Collect(source.transform, -1, "", source.name, bones, parents, paths, rest, visited);
                var identity = new StringBuilder(AnimationBakeVersion.Converter).Append('|').Append(AnimationBakeVersion.Schema)
                    .Append('|').Append(BakeContentKey.Asset(source)).Append('|').Append(BakeContentKey.Asset(animator != null ? animator.avatar : null));
                for (var i = 0; i < paths.Count; i++)
                {
                    var pose = rest[i];
                    identity.Append('|').Append(paths[i].Length).Append(':').Append(paths[i]).Append(':').Append(parents[i]);
                    for (var axis = 0; axis < 3; axis++) identity.Append(':').Append(BakeContentKey.Float(pose.Position[axis]));
                    for (var axis = 0; axis < 4; axis++) identity.Append(':').Append(BakeContentKey.Float(pose.Rotation.value[axis]));
                    for (var axis = 0; axis < 3; axis++) identity.Append(':').Append(BakeContentKey.Float(pose.Scale[axis]));
                }
                builder = new BlobBuilder(Allocator.Temp); ownsBuilder = true;
                ref var rig = ref builder.ConstructRoot<RigDefinitionBlob>();
                rig.Version = AnimationBakeVersion.Schema;
                rig.ContentKey = new FixedString128Bytes(BakeContentKey.Hash(identity.ToString()));
                var parentArray = builder.Allocate(ref rig.Parents, bones.Count);
                var restArray = builder.Allocate(ref rig.RestLocalPoses, bones.Count);
                var pathArray = builder.Allocate(ref rig.Paths, bones.Count);
                var hashArray = builder.Allocate(ref rig.PathHashes, bones.Count);
                for (var i = 0; i < bones.Count; i++)
                {
                    parentArray[i] = parents[i]; restArray[i] = rest[i];
                    builder.AllocateString(ref pathArray[i], paths[i]);
                    hashArray[i] = RigPathLookup.Hash(new FixedString512Bytes(paths[i]));
                }
                return new RigBakeResult { Success = true, Rig = builder.CreateBlobAssetReference<RigDefinitionBlob>(Allocator.Persistent), Diagnostics = Array.Empty<string>() };
            }
            catch (ArgumentException exception) { return Failure(exception.Message); }
            finally { if (ownsBuilder) builder.Dispose(); }
        }

        static RigBakeResult Failure(string message) => new RigBakeResult { Diagnostics = new[] { message } };

        static void Collect(Transform bone, int parent, string path, string sourceName,
            List<Transform> bones, List<int> parents, List<string> paths, List<BoneLocalPose> rest, HashSet<string> visited)
        {
            if (Encoding.UTF8.GetByteCount(path) > new FixedString512Bytes().Capacity)
                throw new ArgumentException(sourceName + ": " + path + ": bone path exceeds UTF-8 path capacity.");
            if (!visited.Add(path)) throw new ArgumentException(sourceName + ": " + path + ": duplicate full bone path.");
            var pose = new BoneLocalPose { Position = bone.localPosition, Rotation = bone.localRotation, Scale = bone.localScale };
            if (!PoseValidation.IsValid(pose)) throw new ArgumentException(sourceName + ": " + path + ": invalid position, scale or quaternion.");
            var index = bones.Count;
            bones.Add(bone); parents.Add(parent); paths.Add(path);
            // The rig Entity already owns the source root transform. Model poses start in its space.
            rest.Add(parent < 0 ? BoneLocalPose.Identity : pose);
            for (var child = 0; child < bone.childCount; child++)
            {
                var transform = bone.GetChild(child);
                if (transform.name.Contains("/")) throw new ArgumentException(sourceName + ": " + path + ": bone names cannot contain '/'.");
                Collect(transform, index, path.Length == 0 ? transform.name : path + "/" + transform.name,
                    sourceName, bones, parents, paths, rest, visited);
            }
        }
    }
}
