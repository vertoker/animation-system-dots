using Unity.Collections;
using Unity.Entities;

namespace AnimationSystemDOTS
{
    public static class AnimationBakeVersion
    {
        public const int Schema = 3;
        public const string Converter = "humanoid-reference-3";
    }

    public struct RigDefinitionBlob
    {
        public int Version;
        public FixedString128Bytes ContentKey;
        public BlobArray<int> Parents;
        public BlobArray<BoneLocalPose> RestLocalPoses;
        public BlobArray<BlobString> Paths;
        public BlobArray<uint> PathHashes;
    }

    public static class RigPathLookup
    {
        public static uint Hash(in FixedString512Bytes path)
        {
            var hash = 2166136261u;
            for (var i = 0; i < path.Length; i++) hash = unchecked((hash ^ path[i]) * 16777619u);
            return hash;
        }

        public static int Find(ref RigDefinitionBlob rig, in FixedString512Bytes path)
        {
            var hash = Hash(path);
            for (var index = 0; index < rig.Paths.Length; index++)
            {
                if (rig.PathHashes[index] != hash || rig.Paths[index].Length != path.Length) continue;
                var bytes = rig.Paths[index].AsSpan();
                var equal = true;
                for (var i = 0; i < bytes.Length; i++) if (bytes[i] != path[i]) { equal = false; break; }
                if (equal) return index;
            }
            return -1;
        }
    }
}
