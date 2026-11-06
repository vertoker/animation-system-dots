using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using AnimationSystemDOTS.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimationSystemDOTS.Sandbox
{
    // Explicit Player verification only; ordinary sample playback writes nothing.
    public sealed class AnimatorValidationCapture : MonoBehaviour
    {
        string output;
        int sceneIndex;
        [Serializable] sealed class Report
        {
            public string backend, device, scene;
            public int actors, nativeAnimators, palettes, width, height, samples;
            public double medianMs, p95Ms, meanMs, meanMainThreadMs, triangles;
            public double warmupSeconds, measurementSeconds;
            public bool passed;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialize()
        {
            var args = Environment.GetCommandLineArgs(); string folder = null; var index = 0;
            for (var i = 0; i + 1 < args.Length; i++)
            { if (args[i] == "-animatorValidation") folder = Path.GetFullPath(args[i + 1]); if (args[i] == "-animatorScene") int.TryParse(args[i + 1], out index); }
            if (folder == null) return;
            var host = new GameObject("Animator validation Player capture"); DontDestroyOnLoad(host);
            var capture = host.AddComponent<AnimatorValidationCapture>(); capture.output = folder; capture.sceneIndex = index;
        }
        IEnumerator Start()
        {
            if (output == null) yield break;
            Directory.CreateDirectory(output); Application.runInBackground = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            if (SceneManager.GetActiveScene().buildIndex != sceneIndex) { SceneManager.LoadScene(sceneIndex); yield return null; }
            var deadline = Time.realtimeSinceStartup + 120; EcsCharacterView view = null;
            while (Time.realtimeSinceStartup < deadline)
            { view = FindFirstObjectByType<EcsCharacterView>(); if (view != null && view.ActorCount > 0) break; yield return null; }
            if (view == null || view.ActorCount == 0) { Debug.LogError("Animator validation scene did not stream in."); Application.Quit(2); yield break; }
            if (!view.Bots) { view.AutomatedTrace = true; view.ResetCharacter(); }
            var warmupStart = Time.realtimeSinceStartupAsDouble;
            for (var i = 0; i < 180 || Time.realtimeSinceStartupAsDouble - warmupStart < 3; i++) yield return null;
            var warmupSeconds = Time.realtimeSinceStartupAsDouble - warmupStart;
            var samples = new List<double>(); double mainSum = 0, triangleSum = 0, sum = 0;
            var measureStart = Time.realtimeSinceStartupAsDouble;
            using (var main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1))
            using (var triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 1))
            {
                var previous = Time.realtimeSinceStartupAsDouble;
                for (var i = 0; i < 300 || Time.realtimeSinceStartupAsDouble - measureStart < 10; i++)
                {
                    yield return null; var now = Time.realtimeSinceStartupAsDouble; var duration = (now - previous) * 1000; previous = now;
                    samples.Add(duration); sum += duration; if (main.Valid) mainSum += main.LastValue * 1e-6; if (triangles.Valid) triangleSum += triangles.LastValue;
                }
            }
            var report = new Report { backend = SystemInfo.graphicsDeviceType.ToString(), device = SystemInfo.graphicsDeviceName, scene = SceneManager.GetActiveScene().name,
                width = Screen.width, height = Screen.height, samples = samples.Count, nativeAnimators = FindObjectsByType<Animator>(FindObjectsSortMode.None).Length };
            report.warmupSeconds = warmupSeconds; report.measurementSeconds = Time.realtimeSinceStartupAsDouble - measureStart;
            samples.Sort(); report.medianMs = samples[samples.Count / 2]; report.p95Ms = samples[(int)(samples.Count * .95)]; report.meanMs = sum / samples.Count;
            report.meanMainThreadMs = mainSum / samples.Count; report.triangles = triangleSum / samples.Count;
            var em = World.DefaultGameObjectInjectionWorld.EntityManager; var valid = true;
            using (var actors = em.CreateEntityQuery(typeof(CharacterMember))) report.actors = actors.CalculateEntityCount();
            using (var palettes = em.CreateEntityQuery(typeof(SkinMatrixPublication)))
            using (var values = palettes.ToComponentDataArray<SkinMatrixPublication>(Allocator.Temp))
            { report.palettes = values.Length; foreach (var value in values) valid &= value.Result == SkinMatrixPublicationResult.Success; }
            var capture = Camera.main.gameObject.AddComponent<GenericPlaybackCapture>(); capture.enabled = false; capture.Capture(Path.Combine(output, "scene.png"));
            report.passed = report.actors == (view.Bots ? 100 : 1) && report.nativeAnimators == 0 && report.palettes > 0 && valid && report.triangles > 0;
            File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true)); Application.Quit(report.passed ? 0 : 3);
        }
    }
}
