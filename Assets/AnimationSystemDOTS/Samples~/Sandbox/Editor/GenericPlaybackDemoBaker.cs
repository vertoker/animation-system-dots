using Unity.Entities;

namespace AnimationSystemDOTS.Sandbox.Editor
{
    public class GenericPlaybackDemoBaker : Baker<GenericPlaybackDemo>
    {
        public override void Bake(GenericPlaybackDemo authoring)
        {
            if (authoring.RigPrefab == null) return;
            AddComponent(GetEntity(TransformUsageFlags.None), new GenericPlaybackSpawn { Prefab = GetEntity(authoring.RigPrefab, TransformUsageFlags.Dynamic) });
            if (authoring.LodRigPrefab != null)
                AddComponent(GetEntity(TransformUsageFlags.None), new GenericPlaybackLODSource { Prefab = GetEntity(authoring.LodRigPrefab, TransformUsageFlags.Dynamic) });
        }
    }
}
