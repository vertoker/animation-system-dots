using System;
using System.IO;
using System.Linq;
using AnimationSystemDOTS.Authoring;
using AnimationSystemDOTS.Editor.Baking;
using NUnit.Framework;
using Unity.Entities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AnimationSystemDOTS.Tests
{
    public class HumanoidBakeDataTests
    {
        [Test]
        public void PreciseReference_CacheTracksReferenceEditsAndLibraryOwnsDeepCopies()
        {
            var sampleRoot = Sandbox.Editor.SandboxPaths.Root + "Humanoid/";
            var scene = EditorSceneManager.NewPreviewScene();
            var source = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(sampleRoot + "Model/BaldMale.fbx"), new InstantiateParameters { scene = scene });
            var settings = ScriptableObject.CreateInstance<AnimationBakeSettings>();
            settings.HumanoidPoseMode = HumanoidPoseMode.PreciseMuscles;
            settings.SampleRate = 960; settings.PositionTolerance = .002f; settings.AngleToleranceDegrees = .25f;
            var rig = RigBakePipeline.Bake(source).Rig;
            var first = default(ClipBakeResult); var cached = default(ClipBakeResult); var changed = default(ClipBakeResult);
            var library = default(BlobAssetReference<AnimationLibraryBlob>);
            AnimationClip reference = null, motion = null;
            var cacheParent = Path.GetFullPath("Artifacts/CacheTests") + Path.DirectorySeparatorChar;
            var directory = Path.Combine(cacheParent, Guid.NewGuid().ToString("N"));
            try
            {
                AnimationClip Find(string prefix) => AssetDatabase.FindAssets("t:Model", new[] { sampleRoot + "Mixamo" })
                    .Select(AssetDatabase.GUIDToAssetPath).Where(p => Path.GetFileName(p).StartsWith(prefix, StringComparison.Ordinal))
                    .OrderBy(p => p.Length).ThenBy(p => p).SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
                reference = UnityEngine.Object.Instantiate(Find("Y_Bot_Running_"));
                motion = UnityEngine.Object.Instantiate(Find("Y_Bot_Waving_"));
                var binding = AnimationUtility.GetCurveBindings(reference).First(b => b.propertyName == "Spine Front-Back");
                AnimationUtility.SetEditorCurve(reference, binding, AnimationCurve.Constant(0, reference.length, .25f));
                var clipSettings = AnimationUtility.GetAnimationClipSettings(motion);
                clipSettings.hasAdditiveReferencePose = true; clipSettings.additiveReferencePoseClip = reference; clipSettings.additiveReferencePoseTime = 0;
                AnimationUtility.SetAnimationClipSettings(motion, clipSettings);
                var request = new ClipBakeRequest { Source = source, Clip = motion, Rig = rig, Settings = settings, CacheDirectory = directory };
                first = ClipBakePipeline.Bake(request); Assert.That(first.Success, Is.True, string.Join("\n", first.Diagnostics));
                Assert.That(first.Clip.Value.HumanReferenceMuscles[0], Is.EqualTo(.25f));
                cached = ClipBakePipeline.Bake(request); Assert.That(cached.Success && cached.FromCache, Is.True);
                Assert.That(cached.Clip.Value.HumanReferenceMuscles[0], Is.EqualTo(.25f));
                library = LibraryBakePipeline.Build(rig, new[] { first.Clip }).Library;
                Assert.That(library.IsCreated, Is.True);
                first.Clip.Dispose(); first = default; cached.Clip.Dispose(); cached = default;
                Assert.That(library.Value.Clips[0].HumanReferenceMuscles.Length, Is.EqualTo(95));
                Assert.That(library.Value.Clips[0].HumanReferenceMuscles[0], Is.EqualTo(.25f));
                Assert.That(library.Value.Clips[0].HumanMuscles[0].Samples.Length, Is.GreaterThan(0));
                Assert.That(library.Value.Clips[0].HumanBody.Length, Is.EqualTo(library.Value.Clips[0].SampleCount));
                var previousKey = library.Value.Clips[0].ContentKey;
                AnimationUtility.SetEditorCurve(reference, binding, AnimationCurve.Constant(0, reference.length, .5f));
                changed = ClipBakePipeline.Bake(request); Assert.That(changed.Success, Is.True, string.Join("\n", changed.Diagnostics));
                Assert.That(changed.FromCache, Is.False); Assert.That(changed.Clip.Value.ContentKey, Is.Not.EqualTo(previousKey));
                Assert.That(changed.Clip.Value.HumanReferenceMuscles[0], Is.EqualTo(.5f));
                Assert.That(library.Value.Clips[0].HumanReferenceMuscles[0], Is.EqualTo(.25f), "Editing a source reference must not mutate an already published library.");
            }
            finally
            {
                if (first.Clip.IsCreated) first.Clip.Dispose(); if (cached.Clip.IsCreated) cached.Clip.Dispose(); if (changed.Clip.IsCreated) changed.Clip.Dispose();
                if (library.IsCreated) library.Dispose(); if (rig.IsCreated) rig.Dispose();
                if (motion != null) UnityEngine.Object.DestroyImmediate(motion); if (reference != null) UnityEngine.Object.DestroyImmediate(reference);
                UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(settings);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                Assert.That(Path.GetFullPath(directory).StartsWith(cacheParent, StringComparison.OrdinalIgnoreCase), Is.True);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
