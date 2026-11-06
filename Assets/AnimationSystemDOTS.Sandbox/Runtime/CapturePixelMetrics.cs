using System;
using System.IO;
using UnityEngine;

namespace AnimationSystemDOTS.Sandbox
{
    // Predicates for this fixture's bright cyan/red meshes, not arbitrary scenes.
    public static class CapturePixelMetrics
    {
        static Color32[] Read(string path, out int width, out int height)
        {
            var image = new Texture2D(2, 2);
            try
            {
                if (!image.LoadImage(File.ReadAllBytes(path))) throw new InvalidDataException("Invalid capture: " + path);
                width = image.width; height = image.height; return image.GetPixels32();
            }
            finally { UnityEngine.Object.Destroy(image); }
        }
        public static int ColoredPixels(string path, int firstRow = 0, int lastRow = int.MaxValue)
        {
            var pixels = Read(path, out var width, out var height); var count = 0;
            // PNG readback coordinates start at the bottom.
            for (var row = Math.Max(0, firstRow); row < Math.Min(height, lastRow); row++)
                for (var col = 0; col < width; col++) if (Colored(pixels[row * width + col])) count++;
            return count;
        }
        public static int ChangedPixels(string first, string second, int firstRow = 0, int lastRow = int.MaxValue, bool coloredOnly = false)
        {
            var a = Read(first, out var width, out var height); var b = Read(second, out var otherWidth, out var otherHeight);
            if (width != otherWidth || height != otherHeight) throw new InvalidDataException("Capture dimensions differ.");
            var count = 0;
            for (var row = Math.Max(0, firstRow); row < Math.Min(height, lastRow); row++) for (var col = 0; col < width; col++)
            {
                var i = row * width + col; if (coloredOnly && !Colored(a[i]) && !Colored(b[i])) continue;
                if (Math.Abs(a[i].r - b[i].r) > 8 || Math.Abs(a[i].g - b[i].g) > 8 || Math.Abs(a[i].b - b[i].b) > 8) count++;
            }
            return count;
        }
        static bool Colored(Color32 c) => (c.r > 128 || c.b > 128) && (c.r > c.b * 1.5f || c.b > c.r * 1.5f);
    }
}
