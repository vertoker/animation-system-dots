using System;
using System.Collections;
using System.IO;
using AnimationSystemDOTS.Rendering;
using Unity.Collections;
using Unity.Deformations;
using Unity.Entities;
using Unity.Scenes;
using Unity.Transforms;
using UnityEngine;

namespace AnimationSystemDOTS.Sandbox
{
    // A minimal public-API repro, not a workaround for stock global shader buffers.
    // Both Worlds render the same baked SubScene; only the upper World's time changes.
    public static class TwoRenderingWorldsCapture
    {
        [Serializable]
        class Report
        {
            public string backend, device;
            public uint primaryPaletteBefore, primaryPaletteAfter;
            public double primaryTime, secondaryTime;
            public int primaryPalettes, secondaryPalettes, changedPrimaryPixels;
            public int primaryColoredPixels, secondaryColoredPixels, secondaryChangedPixels, exitCode;
            public bool allPalettesSuccessful, reproSetupValid, independentRenderingPassed;
        }

        public static IEnumerator Run(GenericPlaybackCapture capture, string output)
        {
            var primary = World.DefaultGameObjectInjectionWorld;
            SetTime(primary, 0);
            // Remove shadow changes from this image comparison: only colored
            // primary geometry in the lower row participates in the assertion.
            var light = GameObject.Find("Directional Light").GetComponent<Light>();
            var oldShadows = light.shadows; light.shadows = LightShadows.None;
            World secondary = null;
            var report = new Report { backend = SystemInfo.graphicsDeviceType.ToString(), device = SystemInfo.graphicsDeviceName };
            try
            {
                for (var i = 0; i < 6; i++) yield return new WaitForEndOfFrame();
                report.primaryPaletteBefore = PaletteHash(primary);
                var before = Path.Combine(output, "primary-before.png"); capture.Capture(before);
                secondary = new World("AnimationSystemDOTS Second Rendering World");
                DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(secondary,
                    DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.Default));
                ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(secondary);
                SceneSystem.LoadSceneAsync(secondary.Unmanaged, UnityEngine.Object.FindAnyObjectByType<SubScene>().SceneGUID);
                var deadline = Time.realtimeSinceStartup + 60;
                while (!HasControl(secondary) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!HasControl(secondary)) throw new InvalidOperationException("Second rendering World did not load the baked spawner.");
                SetTime(secondary, 1); MoveUpperRow(secondary);
                for (var i = 0; i < 8; i++) yield return new WaitForEndOfFrame();
                var after = Path.Combine(output, "both-secondary-time-1.png"); capture.Capture(after);
                report.primaryPaletteAfter = PaletteHash(primary);
                report.primaryTime = GetTime(primary); report.secondaryTime = GetTime(secondary);
                report.primaryPalettes = PaletteCount(primary); report.secondaryPalettes = PaletteCount(secondary);
                report.allPalettesSuccessful = PalettesSuccessful(primary) && PalettesSuccessful(secondary);
                report.changedPrimaryPixels = CapturePixelMetrics.ChangedPixels(before, after, 190, 330, true);
                report.primaryColoredPixels = CapturePixelMetrics.ColoredPixels(before, 190, 330);
                report.secondaryColoredPixels = CapturePixelMetrics.ColoredPixels(after, 350, 560);
                SetTime(secondary, 0);
                for (var i = 0; i < 6; i++) yield return new WaitForEndOfFrame();
                var secondaryZero = Path.Combine(output, "both-secondary-time-0.png"); capture.Capture(secondaryZero);
                report.secondaryChangedPixels = CapturePixelMetrics.ChangedPixels(after, secondaryZero, 350, 560, true);
                report.reproSetupValid = report.primaryPaletteBefore == report.primaryPaletteAfter
                    && report.primaryTime == 0 && report.secondaryTime == 1 && report.allPalettesSuccessful
                    && report.primaryPalettes == 4 && report.secondaryPalettes == 4
                    && report.primaryColoredPixels > 20 && report.secondaryColoredPixels > 20 && report.secondaryChangedPixels > 20;
                report.independentRenderingPassed = report.reproSetupValid && report.changedPrimaryPixels == 0;
                report.exitCode = !report.reproSetupValid ? 7 : report.independentRenderingPassed ? 0 : 4;
                File.WriteAllText(Path.Combine(output, "two-worlds.json"), JsonUtility.ToJson(report, true));
            }
            finally
            {
                if (secondary != null && secondary.IsCreated)
                { ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(secondary); secondary.Dispose(); }
                light.shadows = oldShadows;
            }
            // Exit 4 means the reproducible stock limitation was observed.
            Application.Quit(report.exitCode);
        }

        static bool HasControl(World world)
        { using (var q = world.EntityManager.CreateEntityQuery(typeof(GenericPlaybackControl))) return q.CalculateEntityCount() == 1; }
        static void SetTime(World world, double time)
        { using (var q = world.EntityManager.CreateEntityQuery(typeof(GenericPlaybackControl))) world.EntityManager.SetComponentData(q.GetSingletonEntity(), new GenericPlaybackControl { Manual = 1, Time = time }); }
        static double GetTime(World world)
        { using (var q = world.EntityManager.CreateEntityQuery(typeof(GenericPlaybackControl))) return q.GetSingleton<GenericPlaybackControl>().Time; }
        static void MoveUpperRow(World world)
        {
            var em = world.EntityManager;
            using (var q = em.CreateEntityQuery(typeof(GenericPlaybackSpawn)))
            {
                var spawn = q.GetSingleton<GenericPlaybackSpawn>();
                foreach (var entity in new[] { spawn.First, spawn.Second })
                { var transform = em.GetComponentData<LocalTransform>(entity); transform.Position.y = 3; em.SetComponentData(entity, transform); }
            }
        }
        static int PaletteCount(World world)
        { using (var q = world.EntityManager.CreateEntityQuery(typeof(SkinMatrixPublication))) return q.CalculateEntityCount(); }
        static bool PalettesSuccessful(World world)
        {
            using (var q = world.EntityManager.CreateEntityQuery(typeof(SkinMatrixPublication)))
            using (var values = q.ToComponentDataArray<SkinMatrixPublication>(Allocator.Temp))
            { foreach (var value in values) if (value.Result != SkinMatrixPublicationResult.Success) return false; return true; }
        }
        static uint PaletteHash(World world)
        {
            var hash = 2166136261u;
            using (var q = world.EntityManager.CreateEntityQuery(typeof(SkinMatrixPublication), typeof(SkinMatrix)))
            using (var entities = q.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities)
                    foreach (var matrix in world.EntityManager.GetBuffer<SkinMatrix>(entity))
                        unchecked { hash = (hash ^ (uint)matrix.Value.GetHashCode()) * 16777619u; }
            return hash;
        }
    }
}
