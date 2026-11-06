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
            var controllerAuthoring = GetComponent<AnimationControllerAuthoring>();
            DependsOn(authoring.Settings);
            var rig = default(BlobAssetReference<RigDefinitionBlob>);
            var library = default(BlobAssetReference<AnimationLibraryBlob>);
            var controller = default(BlobAssetReference<AnimatorControllerBlob>);
            var clips = new List<BlobAssetReference<AnimationClipBlob>>();
            var meshBindings = new List<BlobAssetReference<SkinnedMeshBindingBlob>>();
            var meshInputs = new List<SkinnedMeshBindingBakingData>();
            var ownsRig = false;
            var ownsLibrary = false;
            var ownsController = false;
            try
            {
                if (authoring.Settings == null) throw new ArgumentException("Explicit AnimationBakeSettings are required.");
                var rigResult = RigBakePipeline.Bake(authoring.gameObject);
                if (!rigResult.Success) throw new ArgumentException(string.Join("\n", rigResult.Diagnostics));
                rig = rigResult.Rig; ownsRig = true;
                var declared = libraryAuthoring != null ? libraryAuthoring.Clips : Array.Empty<AnimationClip>();
                if (declared == null) throw new ArgumentException("Animation library clip list is null.");
                var inputs = new List<AnimationClip>(declared);
                if (controllerAuthoring != null)
                {
                    if (controllerAuthoring.Controller == null) throw new ArgumentException("AnimationControllerAuthoring requires a .controller source.");
                    DependsOn(controllerAuthoring.Controller);
                    foreach (var clip in controllerAuthoring.Controller.animationClips) if (!inputs.Contains(clip)) inputs.Add(clip);
                }
                var preciseHumanoid = authoring.Settings.HumanoidPoseMode == HumanoidPoseMode.PreciseMuscles && animator != null
                    && animator.avatar != null && animator.avatar.isHuman;
                if (preciseHumanoid)
                    foreach (var input in inputs)
                        if (input != null && !input.isHumanMotion) throw new ArgumentException("Precise Humanoid playback requires Humanoid clips; Generic transform motions cannot be mixed into this library.");
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
                if (controllerAuthoring != null)
                {
                    var result = ControllerBakePipeline.Build(controllerAuthoring.Controller, inputs, library);
                    if (!result.Success) throw new ArgumentException(string.Join("\n", result.Diagnostics));
                    controller = result.Controller; ownsController = true;
                }
                foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    DependsOn(renderer); DependsOn(renderer.sharedMesh);
                    var root = renderer.rootBone != null ? renderer.rootBone : renderer.transform;
                    GetComponent<Transform>(root);
                    foreach (var bone in renderer.bones) if (bone != null) GetComponent<Transform>(bone);
                    foreach (var material in renderer.sharedMaterials) DependsOn(material);
                    var binding = SkinnedMeshBindingBakePipeline.Build(authoring.gameObject, renderer, rig, library);
                    meshBindings.Add(binding);
                    meshInputs.Add(new SkinnedMeshBindingBakingData
                    {
                        Mesh = GetEntity(renderer.gameObject, TransformUsageFlags.Dynamic),
                        DeformationRoot = GetEntity(root.gameObject, TransformUsageFlags.Dynamic),
                        RendererId = renderer.GetEntityId(), Binding = binding
                    });
                }
                // AddBlobAsset may deduplicate and replace these references. From this
                // point Unity's blob store owns them, including across incremental rebakes.
                AddBlobAsset(ref rig, out _); ownsRig = false;
                AddBlobAsset(ref library, out _); ownsLibrary = false;
                if (controller.IsCreated) { AddBlobAsset(ref controller, out _); ownsController = false; }
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new AnimationRig { Rig = rig, Library = library });
                AddComponent(entity, new AnimationClock());
                AddComponent(entity, new AnimationPlaybackMode { Value = controller.IsCreated ? PlaybackDriver.Controller : PlaybackDriver.Scripted });
                if (controller.IsCreated)
                {
                    AddComponent(entity, new AnimationController { Definition = controller });
                    AddComponent(entity, new ControllerLayerState());
                    var parameters = AddBuffer<AnimationParameterValue>(entity);
                    for (var p = 0; p < controller.Value.Parameters.Length; p++)
                    {
                        var definition = controller.Value.Parameters[p];
                        parameters.Add(new AnimationParameterValue { Float = definition.DefaultFloat, Int = definition.DefaultInt });
                    }
                }
                if (preciseHumanoid)
                {
                    AddComponent(entity, new PreciseHumanoidPose());
                    AddComponent(entity, new Humanoid.HumanoidRigBinding { Avatar = animator.avatar });
                    AddComponent(entity, new Humanoid.HumanoidBodyPose());
                    AddBuffer<Humanoid.HumanoidMuscleValue>(entity);
                }
                AddComponent(entity, ClipPlaybackState.Initial);
                AddBuffer<ScriptedPlaybackCommand>(entity);
                AddBuffer<AnimationClipRequest>(entity);
                AddBuffer<SampledBonePose>(entity);
                var local = AddBuffer<BoneLocalPose>(entity);
                for (var bone = 0; bone < rig.Value.RestLocalPoses.Length; bone++) local.Add(rig.Value.RestLocalPoses[bone]);
                AddBuffer<BoneModelPose>(entity);
                var skinInputs = AddBuffer<SkinnedMeshBindingBakingData>(entity);
                for (var i = 0; i < meshInputs.Count; i++)
                {
                    var input = meshInputs[i]; var blob = input.Binding;
                    AddBlobAsset(ref blob, out _); meshBindings[i] = default;
                    input.Binding = blob; skinInputs.Add(input);
                }
            }
            catch (ArgumentException exception)
            {
                Debug.LogError(authoring.name + ": animation bake failed: " + exception.Message, authoring);
            }
            finally
            {
                foreach (var clip in clips) if (clip.IsCreated) clip.Dispose();
                foreach (var binding in meshBindings) if (binding.IsCreated) binding.Dispose();
                if (ownsLibrary && library.IsCreated) library.Dispose();
                if (ownsController && controller.IsCreated) controller.Dispose();
                if (ownsRig && rig.IsCreated) rig.Dispose();
            }
        }
    }
}
