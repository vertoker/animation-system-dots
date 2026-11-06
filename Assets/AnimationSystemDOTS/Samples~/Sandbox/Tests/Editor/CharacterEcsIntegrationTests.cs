using System.Collections;
using System.Linq;
using AnimationSystemDOTS.Humanoid;
using AnimationSystemDOTS.Rendering;
using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Deformations;
using Unity.Mathematics;
using Unity.Scenes;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace AnimationSystemDOTS.Sandbox.Tests
{
    public class CharacterEcsIntegrationTests
    {
        World world;
        double time;
        void Update()
        {
            world.SetTime(new TimeData(time, AnimatorValidationSettings.Step)); time += AnimatorValidationSettings.Step;
            world.Update(); world.EntityManager.CompleteAllTrackedJobs();
        }
        [TearDown] public void Dispose() { if (world != null && world.IsCreated) world.Dispose(); world = null; }
        [UnityTest] public IEnumerator Player_StreamsRunsAndUnloads() => Run(false, 1);
        [UnityTest] public IEnumerator Bots_OneHundredShareGraphMoveIndependentlyAndRespawn() => Run(true, 100);
        IEnumerator Run(bool bots, int count)
        {
            var root = AssetDatabase.GUIDToAssetPath("90e73d0213914c0caa70529fcbcb2f13").Replace("SandboxMarker.txt", "");
            var path = root + "Scenes/" + (bots ? "07_AnimatorBots100" : "06_AnimatorECS") + "Content.unity";
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(path), Is.Not.Null);
            world = new World("Animator validation ECS integration"); time = 0;
            var allowed = new[] { "Unity.Entities", "Unity.Transforms", "Unity.Scenes", "Unity.Scenes.Hybrid", "AnimationSystemDOTS.Core", "AnimationSystemDOTS.Humanoid", "AnimationSystemDOTS.Rendering", "AnimationSystemDOTS.Sandbox" };
            var systems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.Default)
                .Where(t => allowed.Contains(t.Assembly.GetName().Name) && t.Name != "UpdateWorldTimeSystem").ToArray();
            DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, systems);
            var em = world.EntityManager;
            var initialAnimators = Resources.FindObjectsOfTypeAll<Animator>().Count(a => a.gameObject.scene.IsValid());
            for (var pass = 0; pass < 2; pass++)
            {
                var scene = SceneSystem.LoadSceneAsync(world.Unmanaged, new Unity.Entities.Hash128(AssetDatabase.AssetPathToGUID(path)),
                    new SceneSystem.LoadParameters { Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn });
                using (var members = em.CreateEntityQuery(typeof(CharacterMember)))
                using (var controls = em.CreateEntityQuery(typeof(CharacterSceneControl)))
                {
                    var deadline = EditorApplication.timeSinceStartup + 120;
                    while (members.CalculateEntityCount() != count && EditorApplication.timeSinceStartup < deadline) { Update(); yield return null; }
                    Assert.That(members.CalculateEntityCount(), Is.EqualTo(count)); Assert.That(controls.CalculateEntityCount(), Is.EqualTo(1));
                    var owner = controls.GetSingletonEntity(); var control = em.GetComponentData<CharacterSceneControl>(owner);
                    control.AutomatedTrace = bots ? (byte)0 : (byte)1; em.SetComponentData(owner, control);
                    using (var actors = members.ToEntityArray(Allocator.Persistent))
                    {
                        var graph = em.GetComponentData<AnimationController>(actors[0]).Definition;
                        for (var i = 0; i < actors.Length; i++) Assert.That(em.GetComponentData<AnimationController>(actors[i]).Definition, Is.EqualTo(graph));
                    }
                    for (var tick = 0; tick < 240; tick++) { Update(); if (tick % 30 == 0) yield return null; }
                    using (var actors = members.ToEntityArray(Allocator.Persistent))
                    {
                        var moved = 0;
                        foreach (var entity in actors)
                        {
                            var member = em.GetComponentData<CharacterMember>(entity);
                            if (math.distance(member.Motor.Position, member.ResetPosition) > .1f) moved++;
                            Assert.That(em.GetComponentData<ControllerLayerState>(entity).Result, Is.EqualTo(ControllerEvaluationResult.Success));
                            Assert.That(em.GetComponentData<HumanoidBodyPose>(entity).Result, Is.EqualTo(HumanoidEvaluationResult.Success));
                            var poses = em.GetBuffer<BoneLocalPose>(entity); Assert.That(poses.Length, Is.GreaterThan(60));
                            foreach (var pose in poses) Assert.That(PoseValidation.IsValid(pose), Is.True);
                        }
                        Assert.That(moved, Is.GreaterThan(count / 2));
                        var target = actors[0]; var before = em.GetComponentData<CharacterMember>(target); before.PendingActions |= CharacterActions.Die; em.SetComponentData(target, before);
                        Update(); Assert.That(em.GetComponentData<CharacterMember>(target).Animation.Dead, Is.EqualTo(1));
                        if (bots)
                        {
                            var respawns = before.Respawns;
                            for (var tick = 0; tick < 600; tick++) { Update(); if (tick % 60 == 0) yield return null; }
                            Assert.That(em.GetComponentData<CharacterMember>(target).Respawns, Is.GreaterThan(respawns));
                        }
                    }
                    Assert.That(Resources.FindObjectsOfTypeAll<Animator>().Count(a => a.gameObject.scene.IsValid()), Is.EqualTo(initialAnimators));
                    using (var palettes = em.CreateEntityQuery(typeof(SkinMatrixPublication), typeof(SkinMatrix)))
                    using (var renderEntities = palettes.ToEntityArray(Allocator.Temp))
                    {
                        Assert.That(renderEntities.Length, Is.EqualTo(count * 3));
                        foreach (var entity in renderEntities)
                        {
                            Assert.That(em.GetComponentData<SkinMatrixPublication>(entity).Result, Is.EqualTo(SkinMatrixPublicationResult.Success));
                            var matrices = em.GetBuffer<SkinMatrix>(entity); Assert.That(matrices.Length, Is.GreaterThan(0));
                            foreach (var value in matrices) Assert.That(math.all(math.isfinite(value.Value.c0)) && math.all(math.isfinite(value.Value.c1)) && math.all(math.isfinite(value.Value.c2)) && math.all(math.isfinite(value.Value.c3)), Is.True);
                        }
                    }
                    if (bots && pass == 1) yield break; // TearDown disposes an active scene after the query scopes close.
                    SceneSystem.UnloadScene(world.Unmanaged, scene);
                    var unloadDeadline = EditorApplication.timeSinceStartup + 30;
                    while ((members.CalculateEntityCount() != 0 || !controls.IsEmptyIgnoreFilter) && EditorApplication.timeSinceStartup < unloadDeadline) { Update(); yield return null; }
                    Assert.That(members.CalculateEntityCount(), Is.Zero); Assert.That(controls.IsEmptyIgnoreFilter, Is.True);
                    Update(); Update();
                    using (var steps = em.CreateEntityQuery(typeof(AnimationStepContext))) Assert.That(steps.IsEmptyIgnoreFilter, Is.True);
                }
            }
        }
    }
}
