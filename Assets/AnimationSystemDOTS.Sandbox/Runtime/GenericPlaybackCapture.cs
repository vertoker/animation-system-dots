using System;
using System.Collections;
using System.IO;
using AnimationSystemDOTS.Rendering;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimationSystemDOTS.Sandbox
{
    // Standalone acceptance harness. Ordinary scene playback never writes files
    // or exits; the explicit -animationCapture <folder> argument enables this run.
    public class GenericPlaybackCapture : MonoBehaviour
    {
        [Serializable]
        class CaptureReport
        {
            public string backend, device;
            public int rigs, palettes, renderEntities;
            public int capturedFiles;
            public bool paletteSuccess;
            public int pose0ColoredPixels, pose1ColoredPixels, poseChangedPixels, shadowChangedPixels;
            public bool passed;
        }
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs(); string output = null; var twoWorlds = false; var visibility = false; var lod = false;
            for (var i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-animationCapture" || args[i] == "-animationCaptureTwoWorlds" || args[i] == "-animationCaptureVisibility" || args[i] == "-animationCaptureLOD")
                { output = Path.GetFullPath(args[i + 1]); twoWorlds = args[i] == "-animationCaptureTwoWorlds"; visibility = args[i] == "-animationCaptureVisibility"; lod = args[i] == "-animationCaptureLOD"; }
            if (output == null) yield break;
            Application.runInBackground = true; Directory.CreateDirectory(output);
            var deadline = Time.realtimeSinceStartup + 60;
            EntityManager em = default; EntityQuery controls = default;
            while (Time.realtimeSinceStartup < deadline)
            {
                var world = World.DefaultGameObjectInjectionWorld;
                if (world != null && world.IsCreated)
                {
                    em = world.EntityManager;
                    controls = em.CreateEntityQuery(typeof(GenericPlaybackControl));
                    if (controls.CalculateEntityCount() == 1) break;
                    controls.Dispose(); controls = default;
                }
                yield return null;
            }
            if (controls == default(EntityQuery)) { Debug.LogError("Animation capture timed out waiting for baked prefab spawn."); Application.Quit(2); yield break; }
            var control = controls.GetSingletonEntity(); controls.Dispose();
            if (twoWorlds) { yield return TwoRenderingWorldsCapture.Run(this, output); yield break; }
            if (visibility) { yield return VisibilityCapture.Run(this, output); yield break; }
            if (lod) { yield return LodCapture.Run(this, output); yield break; }
            yield return CaptureBasic(em, control, output);
        }
        public IEnumerator CaptureBasic(EntityManager em, Entity control, string output)
        {
            foreach (var time in new[] { 0.0, 1.0 })
            {
                em.SetComponentData(control, new GenericPlaybackControl { Manual = 1, Time = time });
                for (var frame = 0; frame < 6; frame++) yield return new WaitForEndOfFrame();
                Capture(Path.Combine(output, "pose-" + time.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + ".png"));
                yield return new WaitForEndOfFrame(); yield return null;
            }
            // Same evaluated geometry with the light's shadows disabled gives a
            // direct image comparison for the animated shadow-caster pass.
            var light = GameObject.Find("Directional Light").GetComponent<Light>(); light.shadows = LightShadows.None;
            for (var frame = 0; frame < 4; frame++) yield return new WaitForEndOfFrame();
            Capture(Path.Combine(output, "pose-1-no-shadows.png")); yield return new WaitForEndOfFrame();
            var report = new CaptureReport { backend = SystemInfo.graphicsDeviceType.ToString(), device = SystemInfo.graphicsDeviceName, paletteSuccess = true };
            using (var rigs = em.CreateEntityQuery(typeof(AnimationRig))) report.rigs = rigs.CalculateEntityCount();
            using (var renders = em.CreateEntityQuery(typeof(AnimatedRenderBinding))) report.renderEntities = renders.CalculateEntityCount();
            using (var palettes = em.CreateEntityQuery(typeof(SkinMatrixPublication)))
            using (var states = palettes.ToComponentDataArray<SkinMatrixPublication>(Allocator.Temp))
            {
                report.palettes = states.Length;
                foreach (var state in states) if (state.Result != SkinMatrixPublicationResult.Success) report.paletteSuccess = false;
            }
            report.capturedFiles = Directory.GetFiles(output, "*.png").Length;
            var pose0 = Path.Combine(output, "pose-0.png"); var pose1 = Path.Combine(output, "pose-1.png");
            report.pose0ColoredPixels = CapturePixelMetrics.ColoredPixels(pose0);
            report.pose1ColoredPixels = CapturePixelMetrics.ColoredPixels(pose1);
            report.poseChangedPixels = CapturePixelMetrics.ChangedPixels(pose0, pose1);
            report.shadowChangedPixels = CapturePixelMetrics.ChangedPixels(pose1, Path.Combine(output, "pose-1-no-shadows.png"));
            report.passed = report.paletteSuccess && report.rigs == 2 && report.palettes == 4 && report.renderEntities == 8 && report.capturedFiles == 3
                && report.pose0ColoredPixels > 20 && report.pose1ColoredPixels > 20 && report.poseChangedPixels > 20 && report.shadowChangedPixels > 20;
            File.WriteAllText(Path.Combine(output, "capture.json"), JsonUtility.ToJson(report, true));
            for (var frame = 0; frame < 4; frame++) yield return new WaitForEndOfFrame();
            Application.Quit(report.passed ? 0 : 3);
        }
        public void Capture(string path)
        {
            // A hidden standalone window has no capturable swapchain. Render the
            // real camera offscreen, then synchronously read back this test image.
            var camera = GetComponent<Camera>(); var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var target = RenderTexture.GetTemporary(1024, 576, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(1024, 576, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1024, 576), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target); Destroy(image);
            }
        }
    }
}
