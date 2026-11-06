using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using AnimationSystemDOTS.Rendering;
using Unity.Collections;
using Unity.Deformations;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
namespace AnimationSystemDOTS.Sandbox
{
    public class HumanoidCrowdCapture : MonoBehaviour
    {
        [Serializable]
        class Report
        {
            public string backend, device;
            public int characters, activePlayback, advancingPlayback, changedRigs, palettes, renderEntities, nativeAnimators;
            public int[] clipCounts;
            public int closeVisiblePixels, crowdVisiblePixels, closePoseChangedPixels;
            public bool finitePoses = true, successfulPalettes = true, finitePalettes = true, instanceBindings = true, passed;
        }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); string output = null; var profile = false;
            for (var i = 0; i + 1 < args.Length; i++) if (args[i] == "-humanoidCrowdCapture" || args[i] == "-humanoidCrowdProfile")
            { output = Path.GetFullPath(args[i + 1]); profile = args[i] == "-humanoidCrowdProfile"; }
            if (output == null) yield break;
            Application.runInBackground = true; Directory.CreateDirectory(output);
            var view = GetComponent<HumanoidCrowdView>(); view.ShowOverlay = false;
            var deadline = Time.realtimeSinceStartup + 120;
            EntityManager em = default; Entity control = Entity.Null;
            while (Time.realtimeSinceStartup < deadline)
            {
                var world = World.DefaultGameObjectInjectionWorld;
                if (world != null && world.IsCreated)
                {
                    em = world.EntityManager;
                    using (var q = em.CreateEntityQuery(typeof(HumanoidCrowdControl)))
                    using (var members = em.CreateEntityQuery(typeof(HumanoidCrowdMember)))
                        if (q.CalculateEntityCount() == 1 && members.CalculateEntityCount() == 1000) { control = q.GetSingletonEntity(); break; }
                }
                yield return null;
            }
            if (control == Entity.Null) { Debug.LogError("Humanoid crowd capture timed out before all 1000 instances spawned."); Application.Quit(2); yield break; }
            if (profile) { yield return HumanoidCrowdProfiler.Run(output, view); yield break; }
            // Exercise the ordinary scene clock before any deterministic seeks.
            em.SetComponentData(control, new HumanoidCrowdControl());
            for (var i = 0; i < 4; i++) yield return new WaitForEndOfFrame();
            var playbackBefore = PlaybackTimes(em);
            var worldBefore = World.DefaultGameObjectInjectionWorld.Time.ElapsedTime;
            for (var i = 0; i < 30; i++) yield return new WaitForEndOfFrame();
            var elapsed = World.DefaultGameObjectInjectionWorld.Time.ElapsedTime - worldBefore;
            var advancing = 0;
            using (var query = em.CreateEntityQuery(typeof(HumanoidCrowdMember), typeof(ClipPlaybackState)))
            using (var entities = query.ToEntityArray(Allocator.Temp)) foreach (var entity in entities)
            {
                var member = em.GetComponentData<HumanoidCrowdMember>(entity);
                var actual = em.GetComponentData<ClipPlaybackState>(entity).Time - playbackBefore[member.Index];
                if (elapsed > .005 && actual > 0 && math.abs(actual - elapsed * member.Speed) < 1e-4) advancing++;
            }
            var helper = gameObject.AddComponent<GenericPlaybackCapture>(); helper.enabled = false;
            em.SetComponentData(control, new HumanoidCrowdControl { Manual = 1, Time = 0 }); view.SetView(false);
            for (var i = 0; i < 8; i++) yield return new WaitForEndOfFrame();
            helper.Capture(Path.Combine(output, "crowd-t0.png"));
            var before = Snapshot(em);
            view.SetView(true); for (var i = 0; i < 6; i++) yield return new WaitForEndOfFrame();
            var close0 = Path.Combine(output, "close-t0.png"); helper.Capture(close0);
            em.SetComponentData(control, new HumanoidCrowdControl { Manual = 1, Time = 1 });
            for (var i = 0; i < 6; i++) yield return new WaitForEndOfFrame();
            var close1 = Path.Combine(output, "close-t1.png"); helper.Capture(close1);
            var after = Snapshot(em);
            view.SetView(false); for (var i = 0; i < 6; i++) yield return new WaitForEndOfFrame();
            var crowd1 = Path.Combine(output, "crowd-t1.png"); helper.Capture(crowd1);
            var report = Inspect(em, before, after);
            report.advancingPlayback = advancing;
            report.closePoseChangedPixels = CapturePixelMetrics.ChangedPixels(close0, close1);
            view.SetReferenceView();
            foreach (var time in new[] { 4.0, 7.5, 10.3 })
            {
                em.SetComponentData(control, new HumanoidCrowdControl { Manual = 1, Time = time });
                for (var i = 0; i < 6; i++) yield return new WaitForEndOfFrame();
                helper.Capture(Path.Combine(output, "reference-" + time.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ".png"));
            }
            em.SetComponentData(control, new HumanoidCrowdControl { Manual = 1, Time = 1 });
            view.SetView(false); for (var i = 0; i < 6; i++) yield return new WaitForEndOfFrame();
            // A same-camera, same-pose empty-render reference rejects blank output.
            Entity[] renders;
            using (var q = em.CreateEntityQuery(typeof(AnimatedRenderBinding), typeof(MaterialMeshInfo)))
            using (var entities = q.ToEntityArray(Allocator.Temp)) renders = entities.ToArray();
            try
            {
                foreach (var entity in renders) em.SetComponentEnabled<MaterialMeshInfo>(entity, false);
                for (var i = 0; i < 6; i++) yield return new WaitForEndOfFrame();
                var emptyCrowd = Path.Combine(output, "crowd-empty.png"); helper.Capture(emptyCrowd);
                report.crowdVisiblePixels = CapturePixelMetrics.ChangedPixels(crowd1, emptyCrowd);
                view.SetView(true); for (var i = 0; i < 6; i++) yield return new WaitForEndOfFrame();
                var emptyClose = Path.Combine(output, "close-empty.png"); helper.Capture(emptyClose);
                report.closeVisiblePixels = CapturePixelMetrics.ChangedPixels(close1, emptyClose);
            }
            finally { foreach (var entity in renders) if (em.Exists(entity)) em.SetComponentEnabled<MaterialMeshInfo>(entity, true); }
            report.passed = report.characters == 1000 && report.activePlayback == 1000 && report.changedRigs >= 990
                && report.palettes == 3000 && report.renderEntities == 3000 && report.nativeAnimators == 0
                && report.advancingPlayback == 1000 && report.instanceBindings
                && report.finitePoses && report.finitePalettes && report.successfulPalettes
                && report.closeVisiblePixels > 1000 && report.crowdVisiblePixels > 1000 && report.closePoseChangedPixels > 1000;
            foreach (var count in report.clipCounts) if (count != 1000 / report.clipCounts.Length) report.passed = false;
            File.WriteAllText(Path.Combine(output, "humanoid-crowd.json"), JsonUtility.ToJson(report, true));
            Application.Quit(report.passed ? 0 : 8);
        }
        static Dictionary<int, double> PlaybackTimes(EntityManager em)
        {
            var result = new Dictionary<int, double>();
            using (var query = em.CreateEntityQuery(typeof(HumanoidCrowdMember), typeof(ClipPlaybackState)))
            using (var entities = query.ToEntityArray(Allocator.Temp)) foreach (var entity in entities)
                result.Add(em.GetComponentData<HumanoidCrowdMember>(entity).Index, em.GetComponentData<ClipPlaybackState>(entity).Time);
            return result;
        }
        static Dictionary<int, float3x4[]> Snapshot(EntityManager em)
        {
            em.CompleteAllTrackedJobs(); var poses = new Dictionary<int, float3x4[]>();
            using (var q = em.CreateEntityQuery(typeof(HumanoidCrowdMember), typeof(BoneModelPose)))
            using (var entities = q.ToEntityArray(Allocator.Temp)) foreach (var entity in entities)
            {
                var bones = em.GetBuffer<BoneModelPose>(entity); var values = new float3x4[bones.Length];
                for (var i = 0; i < values.Length; i++) values[i] = bones[i].Matrix;
                poses.Add(em.GetComponentData<HumanoidCrowdMember>(entity).Index, values);
            }
            return poses;
        }
        static Report Inspect(EntityManager em, Dictionary<int, float3x4[]> before, Dictionary<int, float3x4[]> after)
        {
            var report = new Report { backend = SystemInfo.graphicsDeviceType.ToString(), device = SystemInfo.graphicsDeviceName };
            report.nativeAnimators = UnityEngine.Object.FindObjectsByType<Animator>().Length;
            var groups = new Dictionary<Entity, HashSet<Entity>>();
            var paletteCounts = new Dictionary<Entity, int>(); var renderCounts = new Dictionary<Entity, int>();
            using (var q = em.CreateEntityQuery(typeof(HumanoidCrowdMember), typeof(ClipPlaybackState)))
            using (var entities = q.ToEntityArray(Allocator.Temp)) foreach (var entity in entities)
            {
                if (report.clipCounts == null) report.clipCounts = new int[em.GetComponentData<AnimationRig>(entity).Library.Value.Clips.Length];
                var group = new HashSet<Entity>();
                foreach (var linked in em.GetBuffer<LinkedEntityGroup>(entity)) group.Add(linked.Value);
                groups.Add(entity, group); paletteCounts.Add(entity, 0); renderCounts.Add(entity, 0);
                report.characters++; var member = em.GetComponentData<HumanoidCrowdMember>(entity); report.clipCounts[member.Clip]++;
                var playback = em.GetComponentData<ClipPlaybackState>(entity);
                if (playback.Clip.Value == member.Clip && playback.IsPaused == 0 && playback.Speed > 0 && playback.LastCommandResult == PlaybackCommandResult.Success) report.activePlayback++;
                if (!before.TryGetValue(member.Index, out var a) || !after.TryGetValue(member.Index, out var b) || a.Length == 0 || a.Length != b.Length) { report.finitePoses = false; continue; }
                var changed = false;
                for (var i = 0; i < a.Length; i++)
                {
                    if (!Finite(a[i]) || !Finite(b[i])) report.finitePoses = false;
                    if (math.cmax(math.abs(a[i].c0 - b[i].c0)) > 1e-5 || math.cmax(math.abs(a[i].c1 - b[i].c1)) > 1e-5
                        || math.cmax(math.abs(a[i].c2 - b[i].c2)) > 1e-5 || math.cmax(math.abs(a[i].c3 - b[i].c3)) > 1e-5) changed = true;
                }
                if (changed) report.changedRigs++;
            }
            using (var q = em.CreateEntityQuery(typeof(AnimatedRenderBinding)))
            using (var entities = q.ToEntityArray(Allocator.Temp)) foreach (var entity in entities)
            {
                report.renderEntities++; var binding = em.GetComponentData<AnimatedRenderBinding>(entity);
                if (!groups.TryGetValue(binding.Rig, out var group) || !group.Contains(entity)
                    || !binding.Binding.IsCreated || binding.Binding.Value.RigKey != em.GetComponentData<AnimationRig>(binding.Rig).Rig.Value.ContentKey)
                    report.instanceBindings = false;
                else renderCounts[binding.Rig]++;
            }
            using (var q = em.CreateEntityQuery(typeof(SkinMatrixPublication), typeof(SkinMatrix)))
            using (var entities = q.ToEntityArray(Allocator.Temp)) foreach (var entity in entities)
            {
                var binding = em.GetComponentData<SkinnedMeshBinding>(entity);
                if (!groups.TryGetValue(binding.Rig, out var group) || !group.Contains(entity) || !group.Contains(binding.DeformationRoot)
                    || !binding.Binding.IsCreated || binding.Binding.Value.RigKey != em.GetComponentData<AnimationRig>(binding.Rig).Rig.Value.ContentKey)
                    report.instanceBindings = false;
                else paletteCounts[binding.Rig]++;
                report.palettes++; if (em.GetComponentData<SkinMatrixPublication>(entity).Result != SkinMatrixPublicationResult.Success) report.successfulPalettes = false;
                foreach (var value in em.GetBuffer<SkinMatrix>(entity)) if (!Finite(value.Value)) report.finitePalettes = false;
            }
            foreach (var rig in groups.Keys) if (paletteCounts[rig] != 3 || renderCounts[rig] != 3) report.instanceBindings = false;
            if (report.clipCounts == null) report.clipCounts = Array.Empty<int>();
            return report;
        }
        static bool Finite(float3x4 m) => math.all(math.isfinite(m.c0)) && math.all(math.isfinite(m.c1)) && math.all(math.isfinite(m.c2)) && math.all(math.isfinite(m.c3));
    }
}
