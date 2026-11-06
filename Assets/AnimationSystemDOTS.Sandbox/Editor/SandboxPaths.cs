using System;
using UnityEditor;

namespace AnimationSystemDOTS.Sandbox.Editor
{
    // Package Manager can import samples into a versioned Assets/Samples folder.
    // Resolve the marker GUID instead of assuming a development-project path.
    public static class SandboxPaths
    {
        public const string MarkerGuid = "90e73d0213914c0caa70529fcbcb2f13";
        public static string Root
        {
            get
            {
                var path = AssetDatabase.GUIDToAssetPath(MarkerGuid);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Import the Animation System DOTS Validation Sandbox sample into Assets first.");
                return path.Substring(0, path.LastIndexOf('/') + 1);
            }
        }
    }
}
