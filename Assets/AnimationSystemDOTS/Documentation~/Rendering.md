# Generic mesh rendering

The stage 3 skinning milestone is accepted. CPU palette, transform export, bounds and baking integration are verified. The user additionally confirmed scene rendering identical to a conventional SkinnedMeshRenderer, including depth, normals and tangents (2026-11-06); this is manual acceptance.

## Public Entities Graphics boundary

The stock `SkinnedMeshRendererBaker` creates the deformation entity, its `SkinMatrix` buffer and the material render entities. `RigBaker` builds a separate immutable `SkinnedMeshBindingBlob` for each mesh. A post-baking system attaches bindings to that stock output through public `AdditionalEntitiesBakingData`. It does not duplicate the native renderer or access internal deformation indices.

Bindings contain the rig content key, bone remap, full affine bindposes, actual deformation-root bone, bone influence boxes and an offline rig-space envelope. Mesh, hierarchy, root, clip-library, settings and material dependencies participate in baking. Removing or changing authoring retracts the derived skin, root-export and material-bounds links during live conversion.

`BoneExportBinding` owns an exported root's `LocalToWorld` through a write group. Before stock transforms, `BoneExportSystem` composes the current rig placement from transform inputs and the evaluated affine bone model matrix. Stock transforms then propagate that world matrix to the render children. The exported entity's authoring local transform is not the evaluated animation pose: consume `LocalToWorld` or the rig's pose buffers.

`SkinMatrixPublishSystem` schedules tracked ECS writers before `DeformationsInPresentation`:

```
palette[j] = inverse(deformationRoot.LocalToWorld)
           * rig.LocalToWorld * boneModel[remap[j]] * bindpose[j]
```

Packing preserves all three affine basis columns and translation. Invalid/missing bindings or a singular root retain the last palette and report a `SkinMatrixPublicationResult`; baking rejects invalid source data before publication.

## Entities 6.6 registration compatibility

In the installed Entities/Entities Graphics 6.6 combination, unmanaged `RenderMeshArray` shared indices have their sign bit set. The stock deformation lookup accepts only nonnegative indices. The minimal sandbox produced valid CPU palettes and bounds but an empty image; replacing its asset indices with public runtime IDs immediately restored the deformed meshes.

`AnimationRenderRegistrationSystem` resolves the stock render assets using public `RenderMeshArray` accessors and registers them through public `EntitiesGraphicsSystem.RegisterMaterial`/`RegisterMesh`. It publishes public `MaterialMeshInfo` runtime IDs before deformation. Native deformation, GPU buffers, material links and shader nodes remain stock Unity implementations.

Each render entity owns its registrations with a cleanup component. Cloneable provenance lets an already animated entity acquire independent registrations after `Instantiate`, which omits cleanup components. Material and mesh ownership are independent: a submesh change retains both handles; a partial external asset replacement retains the unchanged owned handle. Despawn, authoring removal and World disposal release only this adapter's references. The adapter remains disabled when public graphics support is unavailable. It does not take ownership of IDs supplied wholly by an external caller.

## Conservative bounds

Offline bounds use all baked clips and rest poses. Endpoint translation norms and maximum absolute scale components bound interpolated local TRS; parent-before-child scalar radius composition also bounds rotations, negative scales and affine shear between samples. Each mesh bone stores the box of its influenced bindpose-space vertices. Blend shape deltas expand those boxes for the supported weight range **0–100**; out-of-range authored weights are rejected.

Every presentation step additionally reduces the current model-space bone boxes. This covers finite additive/procedural poses beyond the offline envelope. The union is transformed into each material entity's actual render space and rounded outward before writing `RenderBounds`, ahead of `UpdatePresentationSystemGroup`. Bounds publication does not depend on visibility.

## Sandbox and current limits

Open `Assets/AnimationSystemDOTS.Sandbox/Scenes/01_GenericPlayback.unity`. It loads a SubScene with a baked prefab spawner and creates two independent rig instances. Each has two meshes, two materials per mesh, animated deformation roots and a moving ancestor. The second instance uses a different animation time and negative nonuniform rig scale. Its explicit sample clock lives outside the package runtime.

The material uses the official Unity sample Shader Graph with Compute Deformation connected to vertex position, normal and tangent. The copied graph's pinned upstream source and Unity Companion License are beside the asset. Unity describes this deformation path as [experimental](https://docs.unity3d.com/Packages/com.unity.entities.graphics@6.6/manual/mesh_deformations.html).

Windows development Player build: 0 errors and 0 warnings; Direct3D 12 on RTX 4060 Ti. The final acceptance capture at times 0 and 1 differs in 18,181 pixels above an 8-level RGB threshold. Disabling shadows at the same evaluated pose changes 1,040 floor pixels. The Player reports two rigs, four successful palettes and eight material render entities. Reports and captures are in `Artifacts/Verification/Stage3/Acceptance-20261105-232627-252` (generated, ignored by Git).

Additional D3D12 Player acceptance confirms screen-edge geometry and an offscreen caster's visible shadow with negative nonuniform scale: four exact deformed vertices inside and four outside at the edge, zero caster vertices/colored geometry in the shadow-only view, and 25,633 shadow-toggle pixels. A classic `LODGroup` switches between meshes with different deformation roots; the far mesh remains animated. Both use stock material render entities and public palette/bounds publication.

Two simultaneous rendering Worlds were tested and **fail independence in stock Entities Graphics 6.6**: the primary CPU palette is unchanged while 9,123 of its geometry pixels change when the other World's time changes. The reproducible public-API harness and the stock global-buffer cause are recorded in [RenderingChecks.md](RenderingChecks.md). This is an explicit unsupported configuration, not a passing gate.

Depth and normal/tangent parity was accepted by the user after a scene comparison with a conventional SkinnedMeshRenderer. [RenderingChecks.md](RenderingChecks.md) provides standalone commands and manual checks against a native rotated reference. URP per-vertex deformation motion vectors and new intra-mesh LOD ranges are not claimed. Controller-driven animation, scalar/shape animation and general attachments remain later plan stages.
