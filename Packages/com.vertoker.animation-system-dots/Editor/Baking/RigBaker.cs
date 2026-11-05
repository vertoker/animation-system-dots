using System;
using System.Collections.Generic;
using System.IO;
using Unity.Entities;
using UnityEngine;
using AnimationSystemDOTS.Authoring;

namespace AnimationSystemDOTS.Editor.Baking
{
    public sealed class RigBaker : Baker<AnimationRigAuthoring>
    {
        public override void Bake(AnimationRigAuthoring authoring)
        {
            // Every hierarchy/asset read must have a corresponding Baker dependency.
            GetComponentsInChildren<Transform>();
            var animator = GetComponent<Animator>();
            if (animator != null) DependsOn(animator.avatar);
            var libraryAuthoring = GetComponent<AnimationLibraryAuthoring>();
            DependsOn(authoring.Settings);
            var rig = default(BlobAssetReference<RigDefinitionBlob>);
            var library = default(BlobAssetReference<AnimationLibraryBlob>);
            var clips = new List<BlobAssetReference<AnimationClipBlob>>();
            var ownsRig = false;
            var ownsLibrary = false;
            try
            {
                if (authoring.Settings == null) throw new ArgumentException("Explicit AnimationBakeSettings are required.");
                var rigResult = RigBakePipeline.Bake(authoring.gameObject);
                if (!rigResult.Success) throw new ArgumentException(string.Join("\n", rigResult.Diagnostics));
                rig = rigResult.Rig; ownsRig = true;
                var inputs = libraryAuthoring != null ? libraryAuthoring.Clips : Array.Empty<AnimationClip>();
                if (inputs == null) throw new ArgumentException("Animation library clip list is null.");
                foreach (var input in inputs)
                {
                    DependsOn(input);
                    var result = ClipBakePipeline.Bake(new ClipBakeRequest
                    {
                        Source = authoring.gameObject, Clip = input, Rig = rig, Settings = authoring.Settings,
                        CacheDirectory = Path.GetFullPath("Library/AnimationSystemDOTS/ClipCache")
                    });
                    if (!result.Success) throw new ArgumentException(string.Join("\n", result.Diagnostics));
                    clips.Add(result.Clip);
                }
                var libraryResult = LibraryBakePipeline.Build(rig, clips);
                if (!libraryResult.Success) throw new ArgumentException(string.Join("\n", libraryResult.Diagnostics));
                library = libraryResult.Library; ownsLibrary = true;
                // AddBlobAsset may deduplicate and replace these references. From this
                // point Unity's blob store owns them, including across incremental rebakes.
                AddBlobAsset(ref rig, out _); ownsRig = false;
                AddBlobAsset(ref library, out _); ownsLibrary = false;
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new AnimationRig { Rig = rig, Library = library });
                AddComponent(entity, new AnimationClock());
                AddComponent(entity, new AnimationPlaybackMode());
                AddComponent(entity, ClipPlaybackState.Initial);
                AddBuffer<ScriptedPlaybackCommand>(entity);
                AddBuffer<AnimationClipRequest>(entity);
                AddBuffer<SampledBonePose>(entity);
                var local = AddBuffer<BoneLocalPose>(entity);
                for (var bone = 0; bone < rig.Value.RestLocalPoses.Length; bone++) local.Add(rig.Value.RestLocalPoses[bone]);
                AddBuffer<BoneModelPose>(entity);
            }
            catch (ArgumentException exception)
            {
                Debug.LogError(authoring.name + ": animation bake failed: " + exception.Message, authoring);
            }
            finally
            {
                foreach (var clip in clips) if (clip.IsCreated) clip.Dispose();
                if (ownsLibrary && library.IsCreated) library.Dispose();
                if (ownsRig && rig.IsCreated) rig.Dispose();
            }
        }
    }
}
