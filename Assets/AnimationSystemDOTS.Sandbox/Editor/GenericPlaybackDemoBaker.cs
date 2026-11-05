using Unity.Entities;

namespace AnimationSystemDOTS.Sandbox.Editor
{
    public class GenericPlaybackDemoBaker : Baker<GenericPlaybackDemo>
    {
        public override void Bake(GenericPlaybackDemo authoring)
        {
            if (authoring.RigPrefab == null) return;
            AddComponent(GetEntity(TransformUsageFlags.None), new GenericPlaybackSpawn { Prefab = GetEntity(authoring.RigPrefab, TransformUsageFlags.Dynamic) });
        }
    }
}
