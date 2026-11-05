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
using Unity.Transforms;
using UnityEngine;

namespace AnimationSystemDOTS.Sandbox
{
    // Standalone acceptance only: derives exact deformed vertex positions from
    // public palettes, never from internal deformation indices or GPU buffers.
    public static class VisibilityCapture
    {
        [Serializable]
        class Report
        {
            public string backend;
            public int edgeInsideVertices, edgeOutsideVertices, edgeColoredPixels;
            public int shadowInsideVertices, shadowColoredPixels, shadowChangedPixels;
            public bool passed;
            public Vector3 shadowCameraPosition, shadowCameraEuler;
        }
        public static IEnumerator Run(GenericPlaybackCapture capture, string output)
        {
            var world = World.DefaultGameObjectInjectionWorld; var em = world.EntityManager;
            Entity first, second;
            using (var q = em.CreateEntityQuery(typeof(GenericPlaybackSpawn)))
            { var s = q.GetSingleton<GenericPlaybackSpawn>(); first = s.First; second = s.Second; }
            using (var q = em.CreateEntityQuery(typeof(GenericPlaybackControl)))
                em.SetComponentData(q.GetSingletonEntity(), new GenericPlaybackControl { Manual = 1, Time = 1 });
            // Keep the negative/nonuniform instance as the subject of this run.
            em.SetComponentData(first, LocalTransform.FromPosition(-1000, 0, 0));
            em.SetComponentData(second, LocalTransform.Identity);
            for (var i = 0; i < 6; i++) yield return new WaitForEndOfFrame();
            var points = Vertices(world, second); var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var point in points) bounds.Encapsulate(point);
            var camera = capture.GetComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 1.8f;
            // Put the right frustum plane through the exact geometry's center.
            var halfWidth = camera.orthographicSize * (1024f / 576);
            camera.transform.SetPositionAndRotation(new Vector3(bounds.center.x - halfWidth, bounds.center.y, -12), Quaternion.identity);
            var report = new Report { backend = SystemInfo.graphicsDeviceType.ToString() };
            CountInside(camera, points, out report.edgeInsideVertices, out report.edgeOutsideVertices);
            for (var i = 0; i < 6; i++) yield return new WaitForEndOfFrame();
            var edge = Path.Combine(output, "screen-edge.png"); capture.Capture(edge);
            report.edgeColoredPixels = ColoredPixels(edge);

            // The light projects to +X. Its shallow angle leaves an entirely
            // offscreen caster's shadow on the visible floor to the right.
            var light = GameObject.Find("Directional Light").GetComponent<Light>();
            light.transform.rotation = Quaternion.Euler(20, 75, 0); light.shadows = LightShadows.Hard;
            camera.orthographicSize = 1.6f; halfWidth = camera.orthographicSize * (1024f / 576);
            var x = bounds.max.x + halfWidth + .15f;
            camera.transform.position = new Vector3(x, 10, 0);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            report.shadowCameraPosition = camera.transform.position; report.shadowCameraEuler = camera.transform.eulerAngles;
            CountInside(camera, points, out report.shadowInsideVertices, out _);
            for (var i = 0; i < 8; i++) yield return new WaitForEndOfFrame();
            var shadow = Path.Combine(output, "shadow-only.png"); capture.Capture(shadow);
            report.shadowColoredPixels = ColoredPixels(shadow);
            light.shadows = LightShadows.None;
            for (var i = 0; i < 4; i++) yield return new WaitForEndOfFrame();
            var noShadow = Path.Combine(output, "shadow-only-disabled.png"); capture.Capture(noShadow);
            report.shadowChangedPixels = ChangedPixels(shadow, noShadow);
            report.passed = report.edgeInsideVertices > 0 && report.edgeOutsideVertices > 0 && report.edgeColoredPixels > 20
                && report.shadowInsideVertices == 0 && report.shadowColoredPixels == 0 && report.shadowChangedPixels > 20;
            File.WriteAllText(Path.Combine(output, "visibility.json"), JsonUtility.ToJson(report, true));
            Application.Quit(report.passed ? 0 : 5);
        }

        static List<Vector3> Vertices(World world, Entity rig)
        {
            var em = world.EntityManager; var points = new List<Vector3>();
            var graphics = world.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            using (var skins = em.CreateEntityQuery(typeof(SkinnedMeshBinding), typeof(SkinMatrix)))
            using (var skinEntities = skins.ToEntityArray(Allocator.Temp))
            using (var renders = em.CreateEntityQuery(typeof(AnimatedRenderBinding), typeof(MaterialMeshInfo)))
            using (var renderEntities = renders.ToEntityArray(Allocator.Temp))
            {
                foreach (var entity in skinEntities)
                {
                    var binding = em.GetComponentData<SkinnedMeshBinding>(entity); if (binding.Rig != rig) continue;
                    Mesh mesh = null;
                    foreach (var render in renderEntities)
                    {
                        var link = em.GetComponentData<AnimatedRenderBinding>(render);
                        if (link.Rig == rig && link.Binding == binding.Binding)
                        { mesh = graphics.GetMesh(em.GetComponentData<MaterialMeshInfo>(render).MeshID); break; }
                    }
                    if (mesh == null) throw new InvalidOperationException("Public registered source mesh is missing.");
                    var palette = em.GetBuffer<SkinMatrix>(entity);
                    var root = em.GetComponentData<LocalToWorld>(binding.DeformationRoot).Value;
                    var vertices = mesh.vertices; var offset = 0;
                    using (var counts = mesh.GetBonesPerVertex()) using (var weights = mesh.GetAllBoneWeights())
                    for (var v = 0; v < vertices.Length; v++)
                    {
                        float3 value = 0;
                        for (var w = 0; w < counts[v]; w++)
                        {
                            var weight = weights[offset++]; var m = palette[weight.boneIndex].Value;
                            var p = (float3)vertices[v];
                            value += (m.c0 * p.x + m.c1 * p.y + m.c2 * p.z + m.c3) * weight.weight;
                        }
                        points.Add(math.transform(root, value));
                    }
                }
            }
            if (points.Count == 0) throw new InvalidOperationException("The visibility subject has no deformed vertices.");
            return points;
        }
        static void CountInside(Camera camera, List<Vector3> points, out int inside, out int outside)
        {
            inside = outside = 0;
            foreach (var point in points)
            {
                var p = camera.WorldToViewportPoint(point);
                if (p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1 && p.z >= camera.nearClipPlane && p.z <= camera.farClipPlane) inside++;
                else outside++;
            }
        }
        static int ColoredPixels(string path)
        {
            var image = new Texture2D(2, 2);
            try { image.LoadImage(File.ReadAllBytes(path)); var count = 0; foreach (var c in image.GetPixels32()) if ((c.r > 128 || c.b > 128) && (c.r > c.b * 1.5f || c.b > c.r * 1.5f)) count++; return count; }
            finally { UnityEngine.Object.Destroy(image); }
        }
        static int ChangedPixels(string first, string second)
        {
            var a = new Texture2D(2, 2); var b = new Texture2D(2, 2);
            try
            {
                a.LoadImage(File.ReadAllBytes(first)); b.LoadImage(File.ReadAllBytes(second));
                var x = a.GetPixels32(); var y = b.GetPixels32(); var count = 0;
                for (var i = 0; i < x.Length; i++) if (Math.Abs(x[i].r - y[i].r) > 8 || Math.Abs(x[i].g - y[i].g) > 8 || Math.Abs(x[i].b - y[i].b) > 8) count++;
                return count;
            }
            finally { UnityEngine.Object.Destroy(a); UnityEngine.Object.Destroy(b); }
        }
    }
}
