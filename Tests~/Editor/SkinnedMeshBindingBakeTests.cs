using System;
using AnimationSystemDOTS.Authoring;
using AnimationSystemDOTS.Editor.Baking;
using NUnit.Framework;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace AnimationSystemDOTS.Tests
{
    public class SkinnedMeshBindingBakeTests
    {
        GameObject source;
        SkinnedMeshRenderer renderer;
        Mesh mesh;
        Material material;
        AnimationClip clip;
        AnimationBakeSettings settings;
        BlobAssetReference<RigDefinitionBlob> rig;
        BlobAssetReference<AnimationClipBlob> bakedClip;
        BlobAssetReference<AnimationLibraryBlob> library;

        [SetUp]
        public void SetUp()
        {
            source = new GameObject("BoundsRig");
            var bone = new GameObject("Bone").transform; bone.SetParent(source.transform, false); bone.localPosition = Vector3.right;
            var go = new GameObject("Mesh"); go.transform.SetParent(source.transform, false); go.transform.localPosition = new Vector3(3, 0, 0);
            renderer = go.AddComponent<SkinnedMeshRenderer>(); renderer.bones = new[] { bone }; renderer.rootBone = bone;
            mesh = new Mesh(); mesh.vertices = new[] { Vector3.zero, new Vector3(0, 1, 0), new Vector3(2, 0, 0) };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.right }; mesh.triangles = new[] { 0, 1, 2 };
            mesh.bindposes = new[] { bone.worldToLocalMatrix * go.transform.localToWorldMatrix };
            mesh.boneWeights = new[] { new BoneWeight { weight0 = 1 }, new BoneWeight { weight0 = 1 }, new BoneWeight { weight0 = 1 } };
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); renderer.sharedMesh = mesh;
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/AnimationSystemDOTS.Sandbox/Fixtures/Materials/ComputeDeformation.shadergraph");
            Assert.That(shader, Is.Not.Null); material = new Material(shader); renderer.sharedMaterials = new[] { material };
            clip = new AnimationClip(); clip.SetCurve("Bone", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 1, 1, 101));
            settings = ScriptableObject.CreateInstance<AnimationBakeSettings>();
            var result = RigBakePipeline.Bake(source); Assert.That(result.Success, Is.True); rig = result.Rig;
            var clipResult = ClipBakePipeline.Bake(new ClipBakeRequest { Source = source, Clip = clip, Rig = rig, Settings = settings });
            Assert.That(clipResult.Success, Is.True, string.Join("\n", clipResult.Diagnostics)); bakedClip = clipResult.Clip;
            var libraryResult = LibraryBakePipeline.Build(rig, new[] { bakedClip }); Assert.That(libraryResult.Success, Is.True); library = libraryResult.Library;
        }
        [Test]
        public void Binding_OfflineEnvelopeContainsWholeClipAndBindPoseVertices()
        {
            var binding = SkinnedMeshBindingBakePipeline.Build(source, renderer, rig, library);
            try
            {
                Assert.That(binding.Value.BoneBounds.Length, Is.EqualTo(1));
                Assert.That(binding.Value.RigBounds.Extents.x, Is.GreaterThanOrEqualTo(105));
                Assert.That(binding.Value.BoneBounds[0].Center.x + binding.Value.BoneBounds[0].Extents.x, Is.GreaterThanOrEqualTo(4));
            }
            finally { binding.Dispose(); }
        }
        [TestCase(200f)]
        [TestCase(-50f)]
        public void Binding_RejectsAuthoredBlendShapeWeightsOutsideSupportedEnvelope(float weight)
        {
            mesh.AddBlendShapeFrame("Expand", 100, new[] { Vector3.right * 10, Vector3.right * 10, Vector3.right * 10 }, null, null);
            renderer.SetBlendShapeWeight(0, weight);
            Assert.Throws<ArgumentException>(() => { var result = SkinnedMeshBindingBakePipeline.Build(source, renderer, rig, library); result.Dispose(); });
        }

        [Test]
        public void Binding_RejectsUnnormalizedWeightsBeforePublishing()
        {
            mesh.boneWeights = new[] { new BoneWeight { weight0 = .5f }, new BoneWeight { weight0 = 1 }, new BoneWeight { weight0 = 1 } };
            Assert.Throws<ArgumentException>(() => { var result = SkinnedMeshBindingBakePipeline.Build(source, renderer, rig, library); result.Dispose(); });
        }
        [Test]
        public void Binding_RejectsMaterialsWithoutComputeDeformation()
        {
            var plain = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            try
            {
                renderer.sharedMaterial = plain;
                Assert.Throws<ArgumentException>(() => { var result = SkinnedMeshBindingBakePipeline.Build(source, renderer, rig, library); result.Dispose(); });
            }
            finally { UnityEngine.Object.DestroyImmediate(plain); }
        }
        [TearDown]
        public void TearDown()
        {
            if (library.IsCreated) library.Dispose(); if (bakedClip.IsCreated) bakedClip.Dispose(); if (rig.IsCreated) rig.Dispose();
            UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(clip); UnityEngine.Object.DestroyImmediate(settings);
        }
    }
}
