using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace AnimationSystemDOTS.Editor.Baking
{
    internal static class AnimationBlobCache
    {
        [Serializable]
        sealed class Entry
        {
            public int Schema;
            public string ContentKey;
            public string PayloadHash;
            public string UnityVersion;
            public float MaxPositionError;
            public float MaxAngleErrorDegrees;
            public float MaxScaleError;
        }

        public static bool TryLoad(string directory, string key, in FixedString128Bytes rigKey, out ClipBakeResult result)
        {
            result = default;
            var blob = default(BlobAssetReference<AnimationClipBlob>);
            try
            {
                if (!IsDigest(key)) return false;
                var root = Path.GetFullPath(directory);
                var indexPath = Path.Combine(root, key + ".json");
                if (!File.Exists(indexPath)) return false;
                var entry = JsonUtility.FromJson<Entry>(File.ReadAllText(indexPath));
                if (entry == null || entry.Schema != AnimationBakeVersion.Schema || entry.ContentKey != key
                    || entry.UnityVersion != Application.unityVersion || !IsDigest(entry.PayloadHash)) return false;
                var payload = Path.Combine(root, "payload-" + entry.PayloadHash + ".blob");
                // Check bytes before handing any relative blob pointers to Unity's native reader.
                if (!File.Exists(payload) || HashFile(payload) != entry.PayloadHash) return false;
                if (!BlobAssetReference<AnimationClipBlob>.TryRead(payload, AnimationBakeVersion.Schema, out blob)) return false;
                if (!Valid(ref blob.Value, key, rigKey)) return false;
                result = new ClipBakeResult
                {
                    Success = true, FromCache = true, Clip = blob, ContentKey = key, Diagnostics = Array.Empty<string>(),
                    MaxPositionError = entry.MaxPositionError, MaxAngleErrorDegrees = entry.MaxAngleErrorDegrees, MaxScaleError = entry.MaxScaleError
                };
                blob = default;
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (ArgumentException) { return false; }
            finally { if (blob.IsCreated) blob.Dispose(); }
        }

        public static void Store(string directory, string key, BlobBuilder builder, in ClipBakeResult measurements, CancellationToken cancellation)
        {
            if (!IsDigest(key)) throw new ArgumentException("Invalid cache content digest.");
            var root = Path.GetFullPath(directory);
            Directory.CreateDirectory(root);
            var temporaryPayload = Path.Combine(root, ".payload-" + Guid.NewGuid().ToString("N") + ".tmp");
            var temporaryIndex = Path.Combine(root, ".index-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                cancellation.ThrowIfCancellationRequested();
                BlobAssetReference<AnimationClipBlob>.Write(builder, temporaryPayload, AnimationBakeVersion.Schema);
                var hash = HashFile(temporaryPayload);
                var payload = Path.Combine(root, "payload-" + hash + ".blob");
                if (File.Exists(payload) && HashFile(payload) == hash) File.Delete(temporaryPayload);
                else Publish(temporaryPayload, payload);
                var entry = new Entry
                {
                    Schema = AnimationBakeVersion.Schema, ContentKey = key, PayloadHash = hash, UnityVersion = Application.unityVersion,
                    MaxPositionError = measurements.MaxPositionError, MaxAngleErrorDegrees = measurements.MaxAngleErrorDegrees,
                    MaxScaleError = measurements.MaxScaleError
                };
                File.WriteAllText(temporaryIndex, JsonUtility.ToJson(entry, true), new UTF8Encoding(false));
                cancellation.ThrowIfCancellationRequested();
                // A complete immutable payload exists first. This atomic index replacement is the
                // publication point; interruption before it leaves the previous entry untouched.
                Publish(temporaryIndex, Path.Combine(root, key + ".json"));
            }
            finally
            {
                DeleteTemporary(temporaryPayload);
                DeleteTemporary(temporaryIndex);
            }
        }

        static void Publish(string source, string destination)
        {
            if (File.Exists(destination)) File.Replace(source, destination, null);
            else File.Move(source, destination);
        }

        static void DeleteTemporary(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static string HashFile(string path)
        {
            using (var input = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }

        static bool IsDigest(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (var c in value) if ((c < '0' || c > '9') && (c < 'a' || c > 'f')) return false;
            return true;
        }

        static bool Valid(ref AnimationClipBlob blob, string key, in FixedString128Bytes rigKey)
        {
            if (blob.Version != AnimationBakeVersion.Schema || !blob.ContentKey.Equals(new FixedString128Bytes(key))
                || !blob.RigKey.Equals(rigKey) || blob.SampleCount < 1 || blob.Tracks.Length < 1
                || blob.ReferencePose.Length != blob.Tracks.Length || !math.isfinite(blob.Duration) || blob.Duration < 0
                || !math.isfinite(blob.SampleStep) || blob.SampleStep < 0) return false;
            if (blob.Duration > 0 && (blob.SampleCount < 2 || blob.SampleStep <= 0)) return false;
            for (var bone = 0; bone < blob.Tracks.Length; bone++)
                if (blob.Tracks[bone].Samples.Length != 1 && blob.Tracks[bone].Samples.Length != blob.SampleCount) return false;
            return true;
        }
    }
}
