using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AnimationSystemDOTS.Sandbox.Editor
{
    public static class HumanoidCrowdAssets
    {
        public static readonly string Root = (SandboxPaths.Root + "Humanoid/");
        public static readonly string ModelPath = Root + "Model/BaldMale.fbx";
        public static readonly string[] Motions = { "Workout" };

        [MenuItem("Tools/Animation System DOTS/Configure Humanoid Crowd Imports")]
        public static void ConfigureImports()
        {
            Configure(ModelPath, null);
            foreach (var path in System.IO.Directory.GetFiles(Root + "Motions", "*.fbx"))
                Configure(path.Replace('\\', '/'), System.IO.Path.GetFileNameWithoutExtension(path));
            var normal = (TextureImporter)AssetImporter.GetAtPath(Root + "Textures/MaleNormal.png");
            normal.textureType = TextureImporterType.NormalMap; normal.SaveAndReimport();
        }
        static void Configure(string path, string motion)
        {
            // Reconstruct the Avatar from the FBX bind pose. Reusing the current
            // Human import also reuses a previously miscalibrated skeleton.
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) throw new InvalidOperationException("Missing imported source: " + path);
            var human = new List<HumanBone>();
            var bindWorlds = new Dictionary<Transform, Matrix4x4>();
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var bindPoses = skin.sharedMesh.bindposes;
                for (var i = 0; i < skin.bones.Length; i++)
                    bindWorlds[skin.bones[i]] = skin.transform.localToWorldMatrix * bindPoses[i].inverse;
            }
            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;
                var name = BoneName(bone); if (name == null) continue;
                if (!model.GetComponentsInChildren<Transform>().Any(t => t.name == name))
                    throw new InvalidOperationException(path + ": missing bone " + name);
                human.Add(new HumanBone { humanName = HumanTrait.BoneName[(int)bone], boneName = name, limit = new HumanLimit { useDefaultValues = true } });
            }
            var description = new HumanDescription
            {
                human = human.ToArray(),
                skeleton = model.GetComponentsInChildren<Transform>().Select(t =>
                {
                    if (!bindWorlds.TryGetValue(t, out var world))
                        return new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale };
                    var parent = t.parent == null ? Matrix4x4.identity
                        : bindWorlds.TryGetValue(t.parent, out var parentBind) ? parentBind : t.parent.localToWorldMatrix;
                    var local = parent.inverse * world;
                    return new SkeletonBone { name = t.name, position = local.GetColumn(3), rotation = local.rotation, scale = local.lossyScale };
                }).ToArray(),
                armStretch = .05f, legStretch = .05f, upperArmTwist = .5f, lowerArmTwist = .5f,
                upperLegTwist = .5f, lowerLegTwist = .5f
            };
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.humanDescription = description;
            importer.optimizeGameObjects = false; importer.isReadable = true;
            importer.importCameras = false; importer.importLights = false;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            if (motion != null)
            {
                importer.importAnimation = true;
                var clips = importer.defaultClipAnimations;
                foreach (var clip in clips)
                {
                    clip.name = motion; clip.loopTime = true; clip.loopPose = false;
                    // Preserve authored body turns. Only the planar trajectory is
                    // extracted for the stationary crowd; height stays in the pose.
                    clip.lockRootRotation = true; clip.lockRootPositionXZ = false; clip.lockRootHeightY = true;
                    clip.keepOriginalOrientation = true; clip.keepOriginalPositionXZ = true; clip.keepOriginalPositionY = true;
                }
                importer.clipAnimations = clips;
            }
            importer.SaveAndReimport();
            model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var animator = model.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException(path + ": imported Avatar is not a valid Humanoid.");
            Debug.Log(path + ": Humanoid Avatar valid; human scale=" + animator.humanScale);
        }
        static string BoneName(HumanBodyBones bone)
        {
            var names = new Dictionary<HumanBodyBones, string>
            {
                { HumanBodyBones.Hips, "pelvis" }, { HumanBodyBones.Spine, "spine_01" },
                { HumanBodyBones.Chest, "spine_02" }, { HumanBodyBones.UpperChest, "spine_03" },
                { HumanBodyBones.Neck, "neck_01" }, { HumanBodyBones.Head, "Head" }
            };
            if (names.TryGetValue(bone, out var name)) return name;
            var value = bone.ToString(); var left = value.StartsWith("Left", StringComparison.Ordinal);
            if (!left && !value.StartsWith("Right", StringComparison.Ordinal)) return null;
            var part = value.Substring(left ? 4 : 5); var side = left ? "_l" : "_r";
            var limbs = new Dictionary<string, string> { { "Shoulder", "clavicle" }, { "UpperArm", "upperarm" },
                { "LowerArm", "lowerarm" }, { "Hand", "hand" }, { "UpperLeg", "thigh" },
                { "LowerLeg", "calf" }, { "Foot", "foot" }, { "Toes", "ball" } };
            if (limbs.TryGetValue(part, out name)) return name + side;
            foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                if (part.StartsWith(finger, StringComparison.Ordinal))
                {
                    var joint = part.Substring(finger.Length);
                    var index = joint == "Proximal" ? "01" : joint == "Intermediate" ? "02" : joint == "Distal" ? "03" : null;
                    return index == null ? null : (finger == "Little" ? "pinky" : finger.ToLowerInvariant()) + "_" + index + side;
                }
            return null;
        }
    }
}
