# Rendering acceptance and stock multi-World repro

## Ручная проверка Humanoid: 1000 персонажей

1. Открой `Assets/AnimationSystemDOTS.Sandbox/Scenes/04_HumanoidCrowd1000.unity`, дождись окончания импорта и нажми Play. Счётчик должен показать **1000 / 1000**. Каждый экземпляр проигрывает очищенную последовательность `HumanMale_Workout_Final`: ходьба, удар, присед, прыжок. Фазы и скорости различаются; исходные сырые GLB не являются анимацией из указанного видео.
2. Нажми **Video: first agent**. У первого персонажа фаза 0 и скорость 1, поэтому ползунок **Pose** задаёт секунды исходного `HumanMale_Workout_Preview_v2.mp4`. Сравни 1.0 с (шаг), 4.0 с (удар), 7.5 с (присед), 10.3 с (прыжок). Сравни сгибание коленей/локтей, кисти и пальцы, поворот корпуса, высоту таза. Перемещение по горизонтали убрано для стационарной сетки; повороты тела и высота сохранены.
3. Нажми **Resume**, посмотри полный цикл около 13 секунд. Не должно быть случайных переворотов рук/ног, внезапных T-поз или смены чужой позы. Проверь **Close: first four** и **Crowd view**. После повторного Stop/Play количество остаётся 1000.
4. Для оценки скорости измеряй после загрузки и Burst-компиляции, с выключенным Deep Profile. Сравни обычный Play и отдельный Player при одинаковом размере окна. Не уменьшай число персонажей; при потере фокуса редактор может ограничивать обновление. Ползунок/Freeze останавливают время намеренно; перед замером нажми Resume.

Модель — тот же лысый мужчина Quaternius из `Universal Base Characters[Standard]`, экспортированный из исходного glTF в согласованные оси +Z и масштаб. Avatar источника строится из **bind poses**, а не из первого кадра движения. Экспорт анимации содержит маленькую техническую skin-сетку для сохранения bind poses; она не создаётся в runtime. В Player не должно быть ни одного Animator персонажей.

Для автоматической проверки выбери **Tools > Animation System DOTS > Build Humanoid Crowd Player**, затем из корня проекта:

```powershell
.\Tools\Run-HumanoidAcceptance.ps1
.\Tools\Run-HumanoidAcceptance.ps1 -Mode Profile
```

Окно Player должно оставаться видимым: скрытый/minimized Player может вообще пропускать рендер. Программа завершится сама. Результаты находятся в `Artifacts/Verification/HumanoidCrowd/Capture-*` и `Profile-*`. В `humanoid-crowd.json` нужны `passed: true`, `characters/activePlayback/advancingPlayback/changedRigs: 1000`, `palettes/renderEntities: 3000`, `nativeAnimators: 0`, `instanceBindings: true`. Проверяются обычное продвижение времени со скоростью каждого экземпляра, принадлежность связей отдельным клонам, конечные матрицы и видимые изменения изображения. Сохраняются девять PNG, включая крупные позы `reference-4.0`, `reference-7.5`, `reference-10.3`.

В `profile.json` основным результатом является **baseline**. Остальные фазы временно отключают тени и GPU-деформацию для диагностики; frozen-gpu-deformation не считается работоспособной анимацией или результатом оптимизации. Нулевой счётчик геометрии означает недействительный графический замер.

Editor-тесты дополнительно сравнивают 64 сустава с координатами из Blender на восьми временах (допуск 2 см, без planar trajectory), проверяют отсутствие поворотов более 45° за 1/60 с по всей последовательности, независимый native Playables oracle и Avatar с другими пропорциями. Это не заменяет визуальную оценку видео и открытые проверки depth/normals/tangents ниже.

Проверено 2026-11-06: **61/61 Editor**, **35/35 Runtime**; Windows Development build — 0 ошибок/0 предупреждений. D3D12/RTX 4060 Ti, 1280×720: Capture `20261106-021423-388` прошёл все условия, все 1000 поз изменились; Profile `20261106-021436-640` baseline median **10.46 мс**, p95 **11.40 мс**. В Editor проверка регистрации тех же 3000 рендереров занимала в среднем **5.43 мс**, после Burst scan — **0.078 мс**. Это ускорение конкретной CPU-системы, а не обещание такого же множителя FPS: рендер и полная GPU-деформация геометрии продолжаются. Финальные Player logs без ошибок; изображения удара/приседа/прыжка просмотрены.

These checks use a real Windows development Player with Direct3D 12. They do not invoke Unity CLI. Open `Assets/AnimationSystemDOTS.Sandbox/Scenes/01_GenericPlayback.unity` for ordinary playback.

## Run the automated captures

The current workspace has a Player at `Artifacts/Builds/Stage3/AnimationSystemDOTS.exe`. Generated builds and captures are ignored by Git. To rebuild on another checkout:

1. Open the sandbox scene, save it, and run **Tools > Animation System DOTS > Create LOD Acceptance Fixture** once if `Stage3LodRig.prefab` is missing.
2. In Unity Build Profiles, select Windows, Development Build, and include only `01_GenericPlayback.unity`. The referenced SubScene and prefabs are included automatically.
3. Build to `Artifacts/Builds/Stage3/AnimationSystemDOTS.exe`.

From the project root in PowerShell:

```powershell
.\Tools\Run-RenderingAcceptance.ps1 -Mode Basic,Visibility,LOD
.\Tools\Run-RenderingAcceptance.ps1 -Mode TwoWorlds
```

Add `-Visible` to show the standalone window. A different executable can be supplied with `-PlayerPath`. Each run gets a new folder under `Artifacts/Verification/Stage3/Acceptance-*`; it contains PNGs, the Player log and JSON results. Exit zero means every requested check passed. The two-World repro currently fails intentionally with Player exit 4 and script exit 1; do not count it as a passing test.

| Mode | Evidence and assertion |
| --- | --- |
| Basic | Two distinct times, two rigs, four successful palettes, eight material entities; visible colored geometry, changed pose pixels and a same-pose shadow difference are required. Three PNGs and `capture.json` with `passed: true`. |
| Visibility | Negative nonuniform scale, animated root different from rig root. Exact CPU-deformed vertices straddle the screen edge; colored geometry remains visible. Then **all caster vertices are outside the camera**, colored geometry count is zero, but a shadow toggle changes visible floor pixels. `visibility.json` must have `passed: true`. |
| LOD | A stock `LODGroup` switches between the left mesh at LOD0 and right mesh at LOD1. The two meshes have distinct deformation roots and two material slots each. Both palettes remain valid, and the selected far mesh changes at time 1. `lod.json` must have `passed: true`. This is classic per-renderer LOD, not the new intra-mesh LOD range path. |
| TwoWorlds | Two full rendering Worlds independently load the same baked SubScene. Lower World is frozen at time 0; upper World changes to time 1. Both rows must contain visible geometry and the upper row must change when its time changes. Lower CPU palette hash must remain identical and its colored geometry must not change. Invalid CPU/setup/blank-image cases produce exit 7; only a valid unchanged-CPU repro with changing lower geometry produces exit 4. See the limitation below. |

The image predicates are specific to the cyan/red sandbox, its 1024×576 camera and light. They are acceptance fixtures, not generic image-quality metrics. The scripts time out if loading fails; logs and report counts must also be inspected. Do not use `-batchmode -nographics` for graphical captures.

## Two rendering Worlds: reproduced stock limitation

Verified in Unity 6000.6.8f1, Entities/Entities Graphics 6.6.0, D3D12, RTX 4060 Ti. The first Player repro reported:

- Primary time 0, secondary time 1, four successful palettes per World.
- Primary CPU palette hash **384198198 before and after**.
- **9,123 changed lower-row geometry pixels** despite the unchanged primary pose.
- Changing the upper World back to time 0 changes the lower row back as well.

`TwoRenderingWorldsCapture.cs` is the minimal reproducible public-API harness. It creates a second World with the default systems, appends it to the player loop, and loads the same SubScene through `SceneSystem.LoadSceneAsync`. It never reads private deformation offsets or GPU buffer contents. Its `finally` removes and disposes the second World.

The installed stock source explains the coupling: each World creates its own `MeshBufferManager`, whose constructor calls `PushDataToGlobal`. `ComputeBufferWrapper.PushDataToGlobal` calls `Shader.SetGlobalBuffer` for **the same `_DeformedMeshData` name**. `ComputeDeformNode` reads that global buffer using each entity's World-local deformation offset. Creating/resizing another World's buffer therefore changes what the first World's draw reads. Those manager types are internal; there is no public per-World buffer binding in this path. The package does not fork Unity or access the internal objects. **Simultaneous independent rendering Worlds remain unsupported in this stock path.** Keep animated render entities in one rendering World; independent simulation Worlds can still supply data to it.

## Depth, normals and tangents: remaining acceptance

These checks are still open. Position animation and a lit/shadow image do not independently prove the normal/tangent streams. The current clip translates bones; a meaningful normal/tangent test also needs bone rotation and a reference pose. Do not mark stage 3 complete from the current captures.

For a manual check in Unity:

1. Duplicate the sandbox scene/prefab for the check. Give the clip an obvious 3D bone rotation, for example 45° about X on `Skeleton/Left/Joint`, and retain the negative/nonuniform instance. Keep Compute Deformation connected to **Position, Normal and Tangent** in every material's Shader Graph.
2. For a native reference, place a copy of the same rig outside the SubScene, remove `AnimationLibraryAuthoring` before `AnimationRigAuthoring`, and drive its `SkinnedMeshRenderer` with a native Animator playing that clip. Match placement, scale and sample time. Compare both at the start and middle of the clip.
3. Open **Window > Analysis > Rendering Debugger** and enable the camera-depth fullscreen view available in the installed URP. The depth silhouette must follow the deformed pose at both times, including the screen edge; no rest-pose ghost is acceptable. In **Window > Analysis > Frame Debugger**, identify the actual DepthOnly/DepthNormals draw or depth-copy route, and record which route was exercised. A forward screenshot alone is insufficient.
4. Make a diagnostic Shader Graph copy: retain all three Compute Deformation vertex connections, use **Normal Vector (World)**, multiply by 0.5 and add 0.5, and connect it to Emission with Base Color black. Compare ECS and the native reference. Repeat using **Tangent Vector (World)**. Check both material slots, both root choices, and the negative/nonuniform scale. Save captures and matching time values; check orientation, not just that colors change.
5. Repeat in a Windows Player on the intended graphics backend, saving the backend/device and the frames. The Editor result alone does not close the Player gate. Motion vectors are a separate unclaimed feature.

The diagnostic graph/material and rotated native reference should become an automated golden-image fixture before this gate is considered complete. New intra-mesh LOD ranges also need separate validation; the classic LOD capture does not establish their support.
