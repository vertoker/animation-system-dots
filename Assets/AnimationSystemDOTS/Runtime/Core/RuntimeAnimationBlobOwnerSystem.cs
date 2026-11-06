using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Jobs;

namespace AnimationSystemDOTS
{
    // Explicit owner for runtime-created allocations only. Never adopt baked blob-store references.
    [DisableAutoCreation]
    public partial class RuntimeAnimationBlobOwnerSystem : SystemBase
    {
        readonly List<BlobAssetReference<RigDefinitionBlob>> rigs = new List<BlobAssetReference<RigDefinitionBlob>>();
        readonly List<BlobAssetReference<AnimationLibraryBlob>> libraries = new List<BlobAssetReference<AnimationLibraryBlob>>();
        JobHandle readers;

        // Main-thread transfer. ECS readers are tracked by the EntityManager; readers
        // scheduled outside ECS must also be registered explicitly before teardown.
        public void Adopt(ref BlobAssetReference<RigDefinitionBlob> rig, ref BlobAssetReference<AnimationLibraryBlob> library)
        {
            if (!rig.IsCreated || !library.IsCreated || rig.Value.Version != AnimationBakeVersion.Schema
                || library.Value.Version != AnimationBakeVersion.Schema || rig.Value.ContentKey != library.Value.RigKey)
                throw new ArgumentException("Runtime owner requires a valid matching rig/library pair.");
            if (!rigs.Contains(rig)) rigs.Add(rig);
            if (!libraries.Contains(library)) libraries.Add(library);
            rig = default; library = default;
        }
        public void RegisterReader(JobHandle reader) => readers = JobHandle.CombineDependencies(readers, reader);
        protected override void OnUpdate() { }
        protected override void OnDestroy()
        {
            readers.Complete();
            EntityManager.CompleteAllTrackedJobs();
            foreach (var library in libraries) if (library.IsCreated) library.Dispose();
            foreach (var rig in rigs) if (rig.IsCreated) rig.Dispose();
            libraries.Clear(); rigs.Clear();
        }
    }
}
