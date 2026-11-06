using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AnimationSystemDOTS.Sandbox.Tests
{
    public class AnimatorValidationAssetTests
    {
        [Test]
        public void JumpSources_ExtractRootHeightBecauseTheMotorOwnsJumpTrajectory()
        {
            var inventory = JsonUtility.FromJson<Inventory>(File.ReadAllText(Root + "MotionInventory.json"));
            foreach (var motion in inventory.motions.Where(x => x.role == "Jump"))
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(motion.asset);
                Assert.That(importer.clipAnimations.All(x => !x.lockRootHeightY), Is.True, motion.source + " would bake jump height into the pose twice.");
            }
        }
        const string MarkerGuid = "90e73d0213914c0caa70529fcbcb2f13";
        static string Root => AssetDatabase.GUIDToAssetPath(MarkerGuid).Replace("SandboxMarker.txt", "Humanoid/Mixamo/");

        [Test]
        public void MissingPrivateSourceDirectory_PreservesPublishedInventoryAndReportsLocation()
        {
            var path = Root + "MotionInventory.json";
            Assert.That(File.Exists(path), Is.True);
            var before = File.ReadAllBytes(path);
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("AnimationSystemDOTS.Sandbox.Editor.AnimatorValidationAssets"))
                .FirstOrDefault(x => x != null);
            Assert.That(type, Is.Not.Null);
            var method = type.GetMethod("ImportFrom", new[] { typeof(string) });
            Assert.That(method, Is.Not.Null, "Importer needs an explicit source location for reproducible external imports.");
            var missing = Path.Combine(Path.GetTempPath(), "AnimationSystemDOTS-Missing-" + Guid.NewGuid().ToString("N"));
            var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(null, new object[] { missing }));
            Assert.That(exception.InnerException, Is.TypeOf<DirectoryNotFoundException>());
            Assert.That(exception.InnerException.Message, Does.Contain(missing));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
        }

        [Test]
        public void ImportedMixamoMotions_HaveDistinctSourceIdentitiesAndValidHumanoidTakes()
        {
            var path = Root + "MotionInventory.json";
            Assert.That(File.Exists(path), Is.True, "Run the Animator Validation source importer in the development host.");
            var inventory = JsonUtility.FromJson<Inventory>(File.ReadAllText(path));
            Assert.That(inventory.motions.Length, Is.GreaterThanOrEqualTo(25));
            Assert.That(inventory.motions.Select(x => x.source).Distinct().Count(), Is.EqualTo(inventory.motions.Length));
            Assert.That(inventory.motions.Select(x => x.asset).Distinct().Count(), Is.EqualTo(inventory.motions.Length));
            foreach (var motion in inventory.motions)
            {
                Assert.That(motion.sha256, Does.Match("^[0-9a-f]{64}$"), motion.source);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(motion.asset);
                Assert.That(model, Is.Not.Null, motion.source);
                var avatar = model.GetComponent<Animator>().avatar;
                Assert.That(avatar != null && avatar.isValid && avatar.isHuman, Is.True, motion.source);
                var clips = AssetDatabase.LoadAllAssetsAtPath(motion.asset).OfType<AnimationClip>()
                    .Where(x => !x.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
                Assert.That(clips.Length, Is.EqualTo(1), motion.source);
                Assert.That(clips[0].isHumanMotion, Is.True, motion.source);
                Assert.That(clips[0].length, Is.GreaterThan(0), motion.source);
                Assert.That(float.IsFinite(clips[0].length), Is.True, motion.source);
                Assert.That(clips[0].frameRate, Is.GreaterThan(0), motion.source);
                Assert.That(AnimationUtility.GetAnimationClipSettings(clips[0]).loopTime, Is.EqualTo(motion.loop), motion.source);
            }
            Assert.That(inventory.motions.Any(x => x.role == "Run"), Is.True);
            Assert.That(inventory.motions.Any(x => x.role == "Idle"), Is.True);
            Assert.That(inventory.motions.Any(x => x.role == "Jump" && !x.loop), Is.True);
            Assert.That(inventory.motions.Any(x => x.role == "Death" && !x.loop), Is.True);
        }

        [Serializable] class Inventory { public Motion[] motions; }
        [Serializable] class Motion { public string source, asset, sha256, role; public bool loop; }
    }
}
