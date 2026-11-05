using System;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace AnimationSystemDOTS.Editor
{
    public static class PackageSamples
    {
        const string MarkerGuid = "90e73d0213914c0caa70529fcbcb2f13";
        const string ScriptGuid = "8a3ce09239b54d29b063b1c807b9fa11";
        [MenuItem("Tools/Animation System DOTS/Import Validation Sandbox")]
        public static void ImportSandbox()
        {
            var importedMarker = AssetDatabase.GUIDToAssetPath(MarkerGuid);
            if (importedMarker.StartsWith("Assets/", StringComparison.Ordinal))
            { Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(importedMarker); return; }
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PackageSamples).Assembly);
            var packageRoot = info != null ? info.resolvedPath : Path.GetDirectoryName(Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(ScriptGuid)));
            if (string.IsNullOrEmpty(packageRoot)) throw new InvalidOperationException("Cannot locate Animation System DOTS sources.");
            var source = Path.Combine(packageRoot, "Samples~/Sandbox");
            const string destination = "Assets/AnimationSystemDOTS.Sandbox";
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
            if (Directory.Exists(destination)) throw new InvalidOperationException("The sample destination already exists; its contents will not be overwritten.");
            FileUtil.CopyFileOrDirectory(source, destination);
            var meta = Path.Combine(packageRoot, "Samples~/Sandbox.meta");
            if (File.Exists(meta)) File.Copy(meta, destination + ".meta", false);
            AssetDatabase.Refresh();
            Debug.Log("Imported Validation Sandbox into " + destination + ". Open its Scenes folder. Requires URP 17.6.");
        }
    }
}
