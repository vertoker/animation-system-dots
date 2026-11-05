using System;
using System.Linq;
using AnimationSystemDOTS.Authoring;
using AnimationSystemDOTS.Editor.Baking;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace AnimationSystemDOTS.Tests
{
    public class HumanoidBakingTests
    {
        [Serializable] class BlenderReference { public ReferenceFrame[] frames; }
        [Serializable] class ReferenceFrame { public double time; public ReferenceBone[] bones; }
        [Serializable] class ReferenceBone { public string name; public Vector3 position; }
        const string Root = "Assets/AnimationSystemDOTS.Sandbox/Humanoid/";
        GameObject source;
        AnimationBakeSettings settings;
        AnimationClip clip;
        BlobAssetReference<RigDefinitionBlob> rig;
        UnityEngine.SceneManagement.Scene scene;

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            source = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Model/BaldMale.fbx"), new InstantiateParameters { scene = scene });
            clip = AssetDatabase.LoadAllAssetsAtPath(Root + "Motions/Walk.fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
            Assert.That(clip.isHumanMotion, Is.True, "The fixture must exercise actual imported muscle tracks.");
            Assert.That(source.GetComponent<Animator>().avatar.isHuman, Is.True);
            settings = ScriptableObject.CreateInstance<AnimationBakeSettings>(); settings.SampleRate = 240;
            settings.PositionTolerance = .002f; settings.AngleToleranceDegrees = .25f;
            var baked = RigBakePipeline.Bake(source); Assert.That(baked.Success, Is.True, string.Join("\n", baked.Diagnostics)); rig = baked.Rig;
        }
        [TearDown]
        public void TearDown()
        {
            if (rig.IsCreated) rig.Dispose();
            if (source != null) UnityEngine.Object.DestroyImmediate(source);
            if (settings != null) UnityEngine.Object.DestroyImmediate(settings);
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }

        [Test]
        public void MotionAvatar_IsCalibratedFromBindPoseRatherThanFirstAnimatedFrame()
        {
            var motion = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Motions/Walk.fbx"), new InstantiateParameters { scene = scene });
            var bones = motion.GetComponentsInChildren<Transform>();
            foreach (var rest in motion.GetComponent<Animator>().avatar.humanDescription.skeleton)
            {
                var bone = bones.FirstOrDefault(t => t.name == rest.name);
                if (bone == null) continue;
                bone.localPosition = rest.position; bone.localRotation = rest.rotation; bone.localScale = rest.scale;
            }
            var shoulder = bones.First(t => t.name == "upperarm_l");
            var hand = bones.First(t => t.name == "hand_l");
            Assert.That(Mathf.Abs(hand.position.y - shoulder.position.y), Is.LessThan(.05f),
                "The source Avatar must use the horizontal-arm bind pose, not the walking pose at frame zero.");
            Assert.That(motion.transform.InverseTransformPoint(hand.position).x, Is.LessThan(0),
                "The motion Avatar must face +Z, with its left hand on local -X.");
            Assert.That(source.transform.InverseTransformPoint(source.GetComponentsInChildren<Transform>().First(t => t.name == "hand_l").position).x, Is.LessThan(0),
                "The target must also face +Z before Avatar construction.");
        }

        [Test]
        public void Workout_EndToEndJointPositionsMatchTheBlenderVideoSource()
        {
            var reference = JsonUtility.FromJson<BlenderReference>(System.IO.File.ReadAllText(Root + "Motions/Workout.reference.json"));
            var workout = AssetDatabase.LoadAllAssetsAtPath(Root + "Motions/Workout.fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
            var result = ClipBakePipeline.Bake(new ClipBakeRequest { Source = source, Clip = workout, Rig = rig, Settings = settings });
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
            try
            {
                foreach (var frame in reference.frames)
                {
                    var matrices = new Matrix4x4[rig.Value.Paths.Length];
                    for (var bone = 0; bone < matrices.Length; bone++)
                    {
                        var pose = ClipSampler.SampleBone(ref result.Clip.Value, bone, frame.time);
                        var local = Matrix4x4.TRS(pose.Position, pose.Rotation, pose.Scale);
                        var parent = rig.Value.Parents[bone]; matrices[bone] = parent < 0 ? local : matrices[parent] * local;
                    }
                    var hipIndex = Enumerable.Range(0, matrices.Length).First(i => rig.Value.Paths[i].ToString().EndsWith("/pelvis"));
                    Vector3 hip = matrices[hipIndex].GetColumn(3);
                    var refHip = frame.bones.First(b => b.name == "pelvis").position;
                    var matched = 0;
                    foreach (var expected in frame.bones)
                    {
                        if (expected.name == "root") continue; // In-place trajectory is intentionally outside the pose comparison.
                        var index = Enumerable.Range(0, matrices.Length).FirstOrDefault(i => rig.Value.Paths[i].ToString().EndsWith("/" + expected.name));
                        if (index == 0) continue;
                        Vector3 actual = matrices[index].GetColumn(3);
                        actual -= new Vector3(hip.x, 0, hip.z);
                        var wanted = expected.position - new Vector3(refHip.x, 0, refHip.z);
                        Assert.That(Vector3.Distance(actual, wanted), Is.LessThan(.02f), expected.name + " at " + frame.time);
                        matched++;
                    }
                    Assert.That(matched, Is.GreaterThanOrEqualTo(60));
                }
            }
            finally { result.Clip.Dispose(); }
        }

        [Test]
        public void CleanedWorkout_MatchesNativeAndHasNoRandomLimbFlipsAt60Hz()
        {
            var workout = AssetDatabase.LoadAllAssetsAtPath(Root + "Motions/Workout.fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
            var result = ClipBakePipeline.Bake(new ClipBakeRequest { Source = source, Clip = workout, Rig = rig, Settings = settings });
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
            try
            {
                for (var bone = 1; bone < rig.Value.Paths.Length; bone++)
                {
                    var previous = ClipSampler.SampleBone(ref result.Clip.Value, bone, 0);
                    for (var frame = 1; frame / 60.0 < workout.length; frame++)
                    {
                        var current = ClipSampler.SampleBone(ref result.Clip.Value, bone, frame / 60.0);
                        Assert.That(Quaternion.Angle(previous.Rotation, current.Rotation), Is.LessThan(45),
                            rig.Value.Paths[bone].ToString() + " flips at " + frame / 60.0);
                        previous = current;
                    }
                }
                foreach (var time in new[] { .137, 1.0, 3.5, 4.0, 7.5, 8.5, 10.3, 11.5 })
                {
                    var expected = NativePose(source, workout, time);
                    for (var bone = 0; bone < expected.Length; bone++)
                    {
                        var actual = ClipSampler.SampleBone(ref result.Clip.Value, bone, time);
                        Assert.That(math.distance(actual.Position, expected[bone].Position), Is.LessThan(.002f));
                        Assert.That(Quaternion.Angle(actual.Rotation, expected[bone].Rotation), Is.LessThan(.3f));
                    }
                }
            }
            finally { result.Clip.Dispose(); }
        }

        [Test]
        public void DisabledAuthoringAnimator_StillBakesMotionAndRemainsDisabled()
        {
            source.GetComponent<Animator>().enabled = false;
            var result = ClipBakePipeline.Bake(new ClipBakeRequest { Source = source, Clip = clip, Rig = rig, Settings = settings });
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
            try
            {
                var expected = NativePose(source, clip, .811);
                var maxMovement = 0f;
                for (var bone = 0; bone < rig.Value.Paths.Length; bone++)
                {
                    var actual = ClipSampler.SampleBone(ref result.Clip.Value, bone, .811);
                    Assert.That(Quaternion.Angle(actual.Rotation, expected[bone].Rotation), Is.LessThan(.3f));
                    maxMovement = math.max(maxMovement, Quaternion.Angle(actual.Rotation, rig.Value.RestLocalPoses[bone].Rotation));
                }
                Assert.That(maxMovement, Is.GreaterThan(15));
                Assert.That(source.GetComponent<Animator>().enabled, Is.False);
            }
            finally { result.Clip.Dispose(); }
        }

        [Test]
        public void ImportedMuscleClip_BakesTargetLocalTracksMatchingNativePoseAndMovesLimbs()
        {
            var result = ClipBakePipeline.Bake(new ClipBakeRequest { Source = source, Clip = clip, Rig = rig, Settings = settings });
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
            try
            {
                Assert.That(result.Clip.Value.RigKey, Is.EqualTo(rig.Value.ContentKey));
                Assert.That(result.Clip.Value.Tracks.Length, Is.EqualTo(rig.Value.Paths.Length));
                var maxMovement = 0f;
                foreach (var time in new[] { .137, .811, 1.25, clip.length - .001, clip.length + .137, 0.0 })
                {
                    var expected = NativePose(source, clip, time);
                    for (var bone = 0; bone < rig.Value.Paths.Length; bone++)
                    {
                        var actual = ClipSampler.SampleBone(ref result.Clip.Value, bone, time);
                        var path = rig.Value.Paths[bone].ToString();
                        Assert.That(math.distance(actual.Position, expected[bone].Position), Is.LessThanOrEqualTo(.002f), path + " position at " + time);
                        Assert.That(Quaternion.Angle(actual.Rotation, expected[bone].Rotation), Is.LessThanOrEqualTo(.3f), path + " rotation at " + time);
                        Assert.That(math.cmax(math.abs(actual.Scale - expected[bone].Scale)), Is.LessThanOrEqualTo(.0001f), path + " scale");
                        maxMovement = math.max(maxMovement, Quaternion.Angle(actual.Rotation, rig.Value.RestLocalPoses[bone].Rotation));
                    }
                }
                Assert.That(maxMovement, Is.GreaterThan(15), "A rest-pose-only conversion is not Humanoid playback.");
            }
            finally { if (result.Clip.IsCreated) result.Clip.Dispose(); }
        }

        [Test]
        public void HumanoidWithoutTargetAvatar_IsRejectedWithActionableDiagnostic()
        {
            source.GetComponent<Animator>().avatar = null;
            var result = ClipBakePipeline.Bake(new ClipBakeRequest { Source = source, Clip = clip, Rig = rig, Settings = settings });
            if (result.Clip.IsCreated) result.Clip.Dispose();
            Assert.That(result.Success, Is.False);
            Assert.That(string.Join("\n", result.Diagnostics), Does.Contain("Avatar").And.Contain("Humanoid"));
        }

        [Test]
        public void SameMuscleClip_RetargetsToDifferentProportionsWithSeparateRigAndClipKeys()
        {
            var first = ClipBakePipeline.Bake(new ClipBakeRequest { Source = source, Clip = clip, Rig = rig, Settings = settings });
            Assert.That(first.Success, Is.True, string.Join("\n", first.Diagnostics));
            Avatar avatar = null; var secondRig = default(BlobAssetReference<RigDefinitionBlob>);
            var second = default(ClipBakeResult);
            try
            {
                var originalDescription = source.GetComponent<Animator>().avatar.humanDescription;
                foreach (var transform in source.GetComponentsInChildren<Transform>())
                    if (transform.name == "calf_l" || transform.name == "calf_r" || transform.name == "foot_l" || transform.name == "foot_r")
                        transform.localPosition *= 1.2f;
                originalDescription.skeleton = source.GetComponentsInChildren<Transform>().Select(t => new SkeletonBone
                { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
                avatar = AvatarBuilder.BuildHumanAvatar(source, originalDescription);
                Assert.That(avatar.isValid && avatar.isHuman, Is.True, "The second target must be a real valid Avatar.");
                source.GetComponent<Animator>().avatar = avatar;
                var newRig = RigBakePipeline.Bake(source); Assert.That(newRig.Success, Is.True); secondRig = newRig.Rig;
                second = ClipBakePipeline.Bake(new ClipBakeRequest { Source = source, Clip = clip, Rig = secondRig, Settings = settings });
                Assert.That(second.Success, Is.True, string.Join("\n", second.Diagnostics));
                Assert.That(second.ContentKey, Is.Not.EqualTo(first.ContentKey));
                Assert.That(secondRig.Value.ContentKey, Is.Not.EqualTo(rig.Value.ContentKey));
                var expected = NativePose(source, clip, .811);
                var differs = 0f;
                for (var bone = 1; bone < expected.Length; bone++)
                {
                    var actual = ClipSampler.SampleBone(ref second.Clip.Value, bone, .811);
                    var old = ClipSampler.SampleBone(ref first.Clip.Value, bone, .811);
                    Assert.That(math.distance(actual.Position, expected[bone].Position), Is.LessThanOrEqualTo(.002f), secondRig.Value.Paths[bone].ToString());
                    Assert.That(Quaternion.Angle(actual.Rotation, expected[bone].Rotation), Is.LessThanOrEqualTo(.3f));
                    differs = math.max(differs, math.distance(actual.Position, old.Position));
                }
                Assert.That(differs, Is.GreaterThan(.01f), "A second Avatar must not reuse the first skeleton's baked pose.");
            }
            finally
            {
                if (first.Clip.IsCreated) first.Clip.Dispose(); if (second.Clip.IsCreated) second.Clip.Dispose();
                if (secondRig.IsCreated) secondRig.Dispose(); if (avatar != null) UnityEngine.Object.DestroyImmediate(avatar);
            }
        }

        static BoneLocalPose[] NativePose(GameObject source, AnimationClip clip, double time)
        {
            // Independent native oracle: a freshly created graph is advanced by
            // elapsed time instead of the converter's random-access seeking.
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject instance = null; var graph = default(PlayableGraph);
            try
            {
                instance = UnityEngine.Object.Instantiate(source, new InstantiateParameters { scene = scene });
                var animator = instance.GetComponent<Animator>(); animator.enabled = true; animator.runtimeAnimatorController = null;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.applyRootMotion = false; animator.fireEvents = false;
                graph = PlayableGraph.Create("AnimationSystemDOTS Oracle Humanoid"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clip); playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false);
                AnimationPlayableOutput.Create(graph, "Native reference", animator).SetSourcePlayable(playable);
                graph.Play(); graph.Evaluate((float)time);
                var bones = instance.GetComponentsInChildren<Transform>(true);
                return bones.Select((t, i) => i == 0 ? BoneLocalPose.Identity : new BoneLocalPose { Position = t.localPosition, Rotation = t.localRotation, Scale = t.localScale }).ToArray();
            }
            finally
            {
                if (graph.IsValid()) { graph.Destroy(); EditorApplication.QueuePlayerLoopUpdate(); }
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
