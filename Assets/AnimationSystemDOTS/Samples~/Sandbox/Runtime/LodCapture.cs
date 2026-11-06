using System;
using System.Collections;
using System.IO;
using AnimationSystemDOTS.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace AnimationSystemDOTS.Sandbox
{
    public static class LodCapture
    {
        [Serializable]
        class Report
        {
            public string backend;
            public int lodRenderEntities, palettes, nearPixels, farPixels, movingFarPixels;
            public float nearCentroidX, farCentroidX;
            public bool palettesSuccessful, passed;
        }
        public static IEnumerator Run(GenericPlaybackCapture capture, string output)
        {
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            using (var q = em.CreateEntityQuery(typeof(GenericPlaybackSpawn)))
            {
                var spawn = q.GetSingleton<GenericPlaybackSpawn>();
                em.SetComponentData(spawn.First, LocalTransform.Identity);
                em.SetComponentData(spawn.Second, LocalTransform.FromPosition(1000, 0, 0));
            }
            Entity control;
            using (var q = em.CreateEntityQuery(typeof(GenericPlaybackControl))) control = q.GetSingletonEntity();
            em.SetComponentData(control, new GenericPlaybackControl { Manual = 1, Time = 0 });
            GameObject.Find("Directional Light").GetComponent<Light>().shadows = LightShadows.None;
            var camera = capture.GetComponent<Camera>(); camera.orthographic = true;
            camera.transform.SetPositionAndRotation(new Vector3(0, 1, -12), Quaternion.identity);
            camera.orthographicSize = 2;
            for (var i = 0; i < 8; i++) yield return new WaitForEndOfFrame();
            var near = Path.Combine(output, "lod-near.png"); capture.Capture(near);
            camera.orthographicSize = 20;
            for (var i = 0; i < 8; i++) yield return new WaitForEndOfFrame();
            var far = Path.Combine(output, "lod-far.png"); capture.Capture(far);
            em.SetComponentData(control, new GenericPlaybackControl { Manual = 1, Time = 1 });
            for (var i = 0; i < 6; i++) yield return new WaitForEndOfFrame();
            var moving = Path.Combine(output, "lod-far-time-1.png"); capture.Capture(moving);
            var report = new Report { backend = SystemInfo.graphicsDeviceType.ToString(), palettesSuccessful = true };
            using (var q = em.CreateEntityQuery(typeof(AnimatedRenderBinding), typeof(MeshLODComponent))) report.lodRenderEntities = q.CalculateEntityCount();
            using (var q = em.CreateEntityQuery(typeof(SkinMatrixPublication)))
            using (var values = q.ToComponentDataArray<SkinMatrixPublication>(Allocator.Temp))
            { report.palettes = values.Length; foreach (var value in values) if (value.Result != SkinMatrixPublicationResult.Success) report.palettesSuccessful = false; }
            Centroid(near, out report.nearPixels, out report.nearCentroidX);
            Centroid(far, out report.farPixels, out report.farCentroidX);
            report.movingFarPixels = ChangedPixels(far, moving);
            // LOD0 is the left mesh, LOD1 the right mesh. The selection must
            // change geometrically, and the selected far mesh must keep animating.
            report.passed = report.lodRenderEntities == 8 && report.palettes == 4 && report.palettesSuccessful
                && report.nearPixels > 20 && report.farPixels > 20 && report.nearCentroidX < 512
                && report.farCentroidX > 512 && report.movingFarPixels > 20;
            File.WriteAllText(Path.Combine(output, "lod.json"), JsonUtility.ToJson(report, true));
            Application.Quit(report.passed ? 0 : 6);
        }
        static void Centroid(string path, out int count, out float x)
        {
            var image = new Texture2D(2, 2); count = 0; double sum = 0;
            try
            {
                image.LoadImage(File.ReadAllBytes(path)); var pixels = image.GetPixels32();
                for (var i = 0; i < pixels.Length; i++)
                {
                    var c = pixels[i];
                    if ((c.r > 128 || c.b > 128) && (c.r > c.b * 1.5f || c.b > c.r * 1.5f)) { count++; sum += i % image.width; }
                }
                x = count == 0 ? -1 : (float)(sum / count);
            }
            finally { UnityEngine.Object.Destroy(image); }
        }
        static int ChangedPixels(string first, string second)
        {
            var a = new Texture2D(2, 2); var b = new Texture2D(2, 2);
            try
            {
                a.LoadImage(File.ReadAllBytes(first)); b.LoadImage(File.ReadAllBytes(second)); var x = a.GetPixels32(); var y = b.GetPixels32(); var count = 0;
                for (var i = 0; i < x.Length; i++) if (Math.Abs(x[i].r - y[i].r) > 8 || Math.Abs(x[i].g - y[i].g) > 8 || Math.Abs(x[i].b - y[i].b) > 8) count++;
                return count;
            }
            finally { UnityEngine.Object.Destroy(a); UnityEngine.Object.Destroy(b); }
        }
    }
}
