using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;

namespace AnimationSystemDOTS.Editor.Baking
{
    public struct LibraryBakeResult
    {
        public bool Success;
        public BlobAssetReference<AnimationLibraryBlob> Library;
        public string[] Diagnostics;
    }

    public static class LibraryBakePipeline
    {
        public static LibraryBakeResult Build(BlobAssetReference<RigDefinitionBlob> rig, IReadOnlyList<BlobAssetReference<AnimationClipBlob>> clips)
        {
            if (!rig.IsCreated || rig.Value.Version != AnimationBakeVersion.Schema)
                return Failure("Library requires a valid, current rig.");
            if (clips == null) return Failure("Library clip list is null.");
            var identity = new StringBuilder("library:").Append(AnimationBakeVersion.Schema).Append(':').Append(rig.Value.ContentKey.ToString());
            for (var i = 0; i < clips.Count; i++)
            {
                if (!clips[i].IsCreated) return Failure($"Library clip {i} is not created.");
                ref var clip = ref clips[i].Value;
                if (clip.Version != AnimationBakeVersion.Schema || clip.RigKey != rig.Value.ContentKey)
                    return Failure($"Library clip {i} has an incompatible version or rig.");
                if (clip.Tracks.Length != rig.Value.Parents.Length || clip.ReferencePose.Length != clip.Tracks.Length || clip.SampleCount < 1)
                    return Failure($"Library clip {i} has an invalid track layout.");
                for (var bone = 0; bone < clip.Tracks.Length; bone++)
                    if (clip.Tracks[bone].Samples.Length != 1 && clip.Tracks[bone].Samples.Length != clip.SampleCount)
                        return Failure($"Library clip {i}, bone {bone} has an invalid sample count.");
                identity.Append(':').Append(clip.ContentKey.ToString());
            }

            var builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref var root = ref builder.ConstructRoot<AnimationLibraryBlob>();
                root.Version = AnimationBakeVersion.Schema;
                root.ContentKey = new FixedString128Bytes(BakeContentKey.Hash(identity.ToString()));
                root.RigKey = rig.Value.ContentKey;
                var targets = builder.Allocate(ref root.Clips, clips.Count);
                for (var i = 0; i < clips.Count; i++) CopyClip(ref builder, ref clips[i].Value, ref targets[i]);
                return new LibraryBakeResult { Success = true, Library = builder.CreateBlobAssetReference<AnimationLibraryBlob>(Allocator.Persistent), Diagnostics = Array.Empty<string>() };
            }
            finally { builder.Dispose(); }
        }

        static void CopyClip(ref BlobBuilder builder, ref AnimationClipBlob source, ref AnimationClipBlob target)
        {
            target.Version = source.Version;
            target.ContentKey = source.ContentKey;
            target.RigKey = source.RigKey;
            target.Duration = source.Duration;
            target.SampleStep = source.SampleStep;
            target.SampleCount = source.SampleCount;
            target.IsLooping = source.IsLooping;
            var reference = builder.Allocate(ref target.ReferencePose, source.ReferencePose.Length);
            for (var i = 0; i < reference.Length; i++) reference[i] = source.ReferencePose[i];
            var tracks = builder.Allocate(ref target.Tracks, source.Tracks.Length);
            for (var i = 0; i < tracks.Length; i++)
            {
                var samples = builder.Allocate(ref tracks[i].Samples, source.Tracks[i].Samples.Length);
                for (var j = 0; j < samples.Length; j++) samples[j] = source.Tracks[i].Samples[j];
            }
        }

        static LibraryBakeResult Failure(string diagnostic) => new LibraryBakeResult { Diagnostics = new[] { diagnostic } };
    }
}
