using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AnimationSystemDOTS.Editor.Baking
{
    internal static class BakeContentKey
    {
        public static string Hash(string text)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }

        public static string Asset(UnityEngine.Object asset)
        {
            if (asset == null) return "none";
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId)) return "memory:" + asset.GetType().FullName;
            var path = AssetDatabase.GetAssetPath(asset);
            return guid + ":" + localId.ToString(CultureInfo.InvariantCulture) + ":" + AssetDatabase.GetAssetDependencyHash(path);
        }

        public static string Float(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
