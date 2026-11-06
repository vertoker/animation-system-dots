using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimationSystemDOTS.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;
using Unity.Rendering;
using UnityEngine;

namespace AnimationSystemDOTS.Sandbox
{
    // Explicit standalone diagnostic. Ablations are restored; they are not scene optimizations.
    public static class HumanoidCrowdProfiler
    {
        [Serializable] class Phase
        {
            public string name;
            public double medianMs, averageMs, p95Ms, averageMainThreadMs, triangles, vertices;
        }
        [Serializable] class Report
        {
            public string backend, device;
            public int characters, width, height;
            public Phase[] phases;
        }
        public static IEnumerator Run(string output, HumanoidCrowdView view)
        {
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            var world = World.DefaultGameObjectInjectionWorld; var em = world.EntityManager;
            var light = GameObject.Find("Directional Light").GetComponent<Light>(); var shadows = light.shadows;
            var deformation = world.GetExistingSystemManaged<DeformationsInPresentation>();
            var phases = new List<Phase>(); var report = new Report
            { backend = SystemInfo.graphicsDeviceType.ToString(), device = SystemInfo.graphicsDeviceName, width = Screen.width, height = Screen.height };
            using (var q = em.CreateEntityQuery(typeof(HumanoidCrowdMember))) report.characters = q.CalculateEntityCount();
            using (var main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 128))
            using (var triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 128))
            using (var vertices = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count", 128))
            {
                var deformationsEnabled = deformation != null && deformation.Enabled;
                try
                {
                    foreach (var phase in new[] { "baseline", "no-shadows", "frozen-gpu-deformation" })
                    {
                        light.shadows = phase == "baseline" ? shadows : LightShadows.None;
                        if (deformation != null) deformation.Enabled = phase != "frozen-gpu-deformation" && deformationsEnabled;
                        view.SetView(false);
                        for (var i = 0; i < 30; i++) yield return new WaitForEndOfFrame();
                        var samples = new List<double>(); double mainSum = 0, triangleSum = 0, vertexSum = 0;
                        var previous = Time.realtimeSinceStartupAsDouble;
                        for (var i = 0; i < 90; i++)
                        {
                            yield return new WaitForEndOfFrame(); var now = Time.realtimeSinceStartupAsDouble;
                            samples.Add((now - previous) * 1000); previous = now;
                            if (main.Valid) mainSum += main.LastValue * 1e-6;
                            if (triangles.Valid) triangleSum += triangles.LastValue;
                            if (vertices.Valid) vertexSum += vertices.LastValue;
                        }
                        samples.Sort(); phases.Add(new Phase { name = phase, medianMs = samples[samples.Count / 2], averageMs = samples.Average(),
                            p95Ms = samples[(int)(samples.Count * .95)], averageMainThreadMs = mainSum / samples.Count,
                            triangles = triangleSum / samples.Count, vertices = vertexSum / samples.Count });
                    }
                }
                finally { light.shadows = shadows; if (deformation != null) deformation.Enabled = deformationsEnabled; }
            }
            report.phases = phases.ToArray(); File.WriteAllText(Path.Combine(output, "profile.json"), JsonUtility.ToJson(report, true));
            Application.Quit(report.characters == 1000 && phases[0].triangles > 0 ? 0 : 9);
        }
    }
}
