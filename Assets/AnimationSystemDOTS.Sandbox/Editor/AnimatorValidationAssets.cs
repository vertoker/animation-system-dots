using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AnimationSystemDOTS.Sandbox.Editor
{
    public static class AnimatorValidationAssets
    {
        public static string Root => SandboxPaths.Root + "Humanoid/Mixamo/";

        [Serializable] public class Inventory { public int version = 1; public Motion[] motions; }
        [Serializable] public class Motion
        {
            public string source, sha256, asset, take, role;
            public float duration, frameRate;
            public bool loop, human;
        }

        [MenuItem("Tools/Animation System DOTS/Import Animator Validation Mixamo Sources")]
        public static void ImportSources()
        {
            // Private source files belong to the host project, outside the
            // installable package, in both Assets and UPM installations.
            var sourceRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../Sources~/Mixamo_Bots100"));
            ImportFrom(sourceRoot);
        }

        public static void ImportFrom(string sourceRoot)
        {
            if (!Directory.Exists(sourceRoot))
                throw new DirectoryNotFoundException("Place private Mixamo FBX sources in " + sourceRoot + ". Existing imported sample assets are preserved.");
            // Enumerate first so missing/empty private input cannot erase published assets.
            var sources = Directory.GetFiles(sourceRoot, "*.fbx", SearchOption.AllDirectories)
                .OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (sources.Length == 0) throw new InvalidOperationException("No FBX files found in " + sourceRoot);
            Directory.CreateDirectory(Root);
            var inventory = new List<Motion>();
            foreach (var source in sources)
            {
                var relative = source.Substring(sourceRoot.Length + 1).Replace('\\', '/');
                // Preserve folder/name variants; flatten only with a stable path hash.
                var basename = Path.GetFileNameWithoutExtension(source);
                var name = string.Concat(basename.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
                var asset = Root + name + "_" + Hash(Encoding.UTF8.GetBytes(relative)).Substring(0, 10) + ".fbx";
                var bytes = File.ReadAllBytes(source);
                if (!File.Exists(asset) || Hash(File.ReadAllBytes(asset)) != Hash(bytes)) File.WriteAllBytes(asset, bytes);
                AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceSynchronousImport);
                Configure(asset, basename);
                var clips = AssetDatabase.LoadAllAssetsAtPath(asset).OfType<AnimationClip>()
                    .Where(x => !x.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
                foreach (var clip in clips)
                    inventory.Add(new Motion { source = relative, sha256 = Hash(bytes), asset = asset,
                        take = clip.name, duration = clip.length, frameRate = clip.frameRate, human = clip.isHumanMotion,
                        loop = AnimationUtility.GetAnimationClipSettings(clip).loopTime, role = Role(basename) });
            }
            File.WriteAllText(Root + "MotionInventory.json", JsonUtility.ToJson(new Inventory { motions = inventory.ToArray() }, true), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(Root + "MotionInventory.json");
            AssetDatabase.SaveAssets();
            Debug.Log("Animator validation: imported " + sources.Length + " FBX sources, " + inventory.Count + " animation takes.");
        }

        static string Hash(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        static void Configure(string path, string basename)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.optimizeGameObjects = false;
            importer.importCameras = false; importer.importLights = false;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.importAnimation = true;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.name = basename;
                clip.loopTime = IsLoop(Role(basename)); clip.loopPose = false;
                clip.lockRootRotation = true; clip.keepOriginalOrientation = true;
                clip.lockRootPositionXZ = false; clip.keepOriginalPositionXZ = true;
                // The sample motor owns the full jump trajectory. Extracting Y
                // prevents the pose from adding the clip's jump height again.
                clip.lockRootHeightY = Role(basename) != "Jump"; clip.keepOriginalPositionY = true;
                clip.heightFromFeet = false;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var animator = model == null ? null : model.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException(path + ": source Mixamo Avatar could not be configured as Humanoid.");
        }

        static bool IsLoop(string role) => role == "Idle" || role == "Walk" || role == "Run" || role == "Sprint" || role == "Strafe";
        static string Role(string name)
        {
            var n = name.ToLowerInvariant();
            if (n.Contains("dying") || n.Contains("death") || n.Contains("knocked")) return "Death";
            if (n.Contains("jump")) return "Jump";
            if (n.Contains("waving")) return "Wave";
            if (n.Contains("punch")) return "Punch";
            if (n.Contains("reaction")) return "Reaction";
            if (n.Contains("start walking")) return "Start";
            if (n.Contains("stop walking")) return "Stop";
            if (n.Contains("turn") || n.Contains("direction")) return "Turn";
            if (n.Contains("idle")) return "Idle";
            if (n.Contains("strafe")) return "Strafe";
            if (n.Contains("sprint")) return "Sprint";
            if (n.Contains("run")) return "Run";
            if (n.Contains("walk")) return "Walk";
            return "Model";
        }
    }
}
