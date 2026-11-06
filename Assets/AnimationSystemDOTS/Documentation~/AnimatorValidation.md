# Проверка Animator: GameObject, ECS и 100 ботов

Импортируй **Validation Sandbox** через Package Manager Samples или **Tools > Animation System DOTS > Import Validation Sandbox**. В корневом Unity-проекте файлы находятся в обычной редактируемой папке `Assets/AnimationSystemDOTS.Sandbox`. Для UPM её снимок публикуется в `Assets/AnimationSystemDOTS/Samples~/Sandbox` командой `python Tools/SyncSandbox.py`. В consumer работай с импортированной копией в Assets.

| Сцена в Sandbox/Scenes | Назначение |
|---|---|
| `05_AnimatorGO.unity` | Один управляемый GameObject с обычным Unity Animator |
| `06_AnimatorECS.unity` | Тот же персонаж и управление; граф и поза исполняются ECS |
| `07_AnimatorBots100.unity` | 100 ECS-ботов: цели, локальное расхождение, бег, прыжки, действия, смерть и respawn |

Все три используют редактируемый `Fixtures/Bots100.controller`: 34 разных клипа, 21 состояние, 6 машин состояний, 3 слоя. Есть пять типов Blend Tree, вложенные деревья вариантов, Entry/Exit/Any State, fixed/normalized fades, interruption, параметры скорости/цикла/времени позы, upper-body override и additive reaction. Это проверка поддерживаемой части Mecanim; IK Pass, StateMachineBehaviour, mirror, Write Defaults и root-motion gameplay здесь не реализованы. Точные ограничения: [Controllers.md](Controllers.md).

## Управление и сравнение

1. Открой 05, включи Play. WASD — движение, Shift — sprint, Alt — strafe, Space — прыжок, правая кнопка мыши — камера. `1` — wave, `2` — punch, `3` — reaction, `4` — смерть. `R` — reset. Для клавиатуры нужен optional Input System adapter; без него работают экранные кнопки и автоматическая трасса.
2. `Tab` переключает пять деревьев, `V` — вариант действия и locomotion variation, `C` — cycle offset. `F` включает Motion Time: двигай экранный slider. Для обычного движения лучше режим 0; остальные режимы специально демонстрируют разные способы смешивания, включая strafe.
3. Нажми **Run deterministic trace** или `T`. Трасса использует фиксированный шаг 1/60 с и повторяется через 60 секунд. Сравни с 06, запустив трассу с reset в обеих сценах. Overlay показывает tick, текущие/следующие состояния, normalized time и веса слоёв. Сравни одинаковый tick, а не одинаковое время после нажатия Play: загрузка SubScene может занять дольше.
4. Смотри на руки при wave/punch поверх бега, reaction во время crossfade и прыжках, а также на возвращение в locomotion после приземления. Не должно быть случайных разворотов тела, рывков кистей или остатков предыдущей позы после reset. Проверяй варианты, а не только первое действие.
5. В 07 дождись `Actors 100`. Боты расходятся по целям, используют разные деревья/варианты/фазы, периодически прыгают и выполняют действия. Умерший бот должен вернуться в исходную область после завершения death-клипа. В Hierarchy нет 100 Animator-компонентов: персонажи находятся в Entities Hierarchy.
6. Stop/Play, выгрузка/загрузка SubScene и режим **Enter Play Mode Options > Reload Domain disabled** не должны удваивать количество персонажей или оставлять нативные pose handlers. Console не должна содержать ошибок.
7. Открой общий controller в Animator. Измени длительность перехода и повтори 05/06. После ECS baking должно примениться изменение того же графа. Source FBX и authoring Animator нужны при baking; runtime ECS не создаёт Animator или PlayableGraph на персонажа.

## Более спокойные руки

`Fixtures/AnimatorValidationSettings.asset` управляет общим профилем: `Upper Action Weight = 0.7`, `Additive Reaction Weight = 0.2`, `Airborne Action Influence = 0.35`. В воздухе слои действий ослабляются, затем плавно возвращаются. Additive reaction использует собственный первый кадр как reference pose, поэтому её начальная поза не добавляет лишний изгиб плеч. Маска сохраняет корень нижнего слоя. GO читает этот профиль напрямую, ECS получает его через baker; после изменения дождись rebake SubScene. Сами source-анимации не обрезаются и не исправляются ограничением суставов.

## Автоматическая проверка

`AnimatorValidationControllerTests` печатает ошибки полного фиксированного native/ECS replay в `Artifacts/Verification/AnimatorValidation/SharedController-trace.txt`. Отдельный отчёт `EveryClip-precise-vs-native.txt` проверяет все 34 клипа в 16 фазах; он сам по себе не доказывает правильность смешивания графа. Порог полного Humanoid сравнения — 2 см / 2°, motor — 0.1 мм, clock — 2e-5 normalized units. Расхождения не считаются пройденными только потому, что визуально приемлемы.

`CharacterEcsIntegrationTests` проверяет реальную загрузку content SubScene, число акторов, общий blob графа, движение, finite bone poses/skin palettes, death/respawn, unload/reload и уничтожение World с активной сценой. Render Worlds не дублируются: ограничение штатного deformation path описано в [RenderingChecks.md](RenderingChecks.md).

Итоговая регрессия: **263/263 Editor/Sandbox и 54/54 Runtime**, без пропусков. Чистые UPM и Assets/submodule consumers без Input System проверили прежние 1000 персонажей, нового игрока и 100 ботов. Сцены 02/04 повторно дали по 1000 rigs и 3000 успешных palettes; 06 дважды запущена без Domain Reload. Независимое ревью исходников не нашло Critical/Important замечаний. В importer остаётся minor-ограничение: при прямом вызове `ImportFrom` передавай source directory без завершающего разделителя; штатное меню и опубликованный inventory не затронуты.

Для отдельного Windows Player используй **Tools > Animation System DOTS > Build Animator Validation Player**, затем `Tools/Run-AnimatorValidation.ps1 -Scene Player` или `-Scene Bots`. В сборку входят 06 и 07. Скрипт запускает видимый D3D12 Player в 1280×720. Harness ждёт загрузку, прогревает минимум 180 кадров и 3 секунды, измеряет минимум 300 кадров и 10 секунд, сохраняет `scene.png` и `report.json`, затем завершает Player. Без аргумента `-animatorValidation <абсолютная папка>` sample не пишет файлы и не завершает приложение. Изолированное время retarget-вызовов и FPS старой 1000-instance сцены не являются измерениями этого controller.

Windows Development Player, D3D12, RTX 4060 Ti, 1280×720, анимация 60 Гц, рендер без VSync: после трёх секунд прогрева измерено десять секунд. Это замер текущего desktop-окружения, без обещания такой же скорости на другом устройстве.

| Сцена | Кадров в замере | Median frame | P95 frame | Среднее | Palettes / Animator |
|---|---:|---:|---:|---:|---|
| ECS игрок | 3481 | 2.51 мс | 5.28 мс | 2.87 мс | 3 / 0 |
| 100 ECS ботов | 2772 | 3.28 мс | 5.91 мс | 3.61 мс | 300 / 0 |

Полные отчёты и снимки находятся в `Artifacts/Verification/AnimatorValidation/Player-20261107-004632-646` и `Bots-20261107-004612-709`. Ранние короткие 300-frame прогоны в соседних папках не являются итоговым замером. Геометрия действительно отрисовывалась; средний render counter для 100 ботов — около 4.95 млн triangles, включая проходы рендера.

## Ragdoll: следующий отдельный этап

Полный ECS ragdoll возможен через optional Unity Physics adapter: collider/body entities примерно для 15 основных костей, joint limits, масса и центр масс, переход из animated kinematic targets в dynamic simulation. Physics-поза должна записываться в bone local buffers после Avatar retarget и до ModelPose/SkinMatrix; в каждом режиме у позы и корня должен быть один авторитетный writer. При включении нужно передать текущую позу и скорости, при выключении — согласовать корень с тазом и плавно перейти к recovery-анимации.

Первый полный takeover без вставания — отдельная задача средней сложности. Частичный ragdoll, устойчивые ограничения, вставание из разных положений и rollback/сетевое восстановление существенно сложнее: они требуют смешивания animation/physics, корректных fixed-step фаз и дополнительного состояния. Generic-монстры и hard-surface риги тоже подходят, но физический rig и ограничения суставов для них задаются отдельно от Humanoid mapping. Нельзя переносить производительность 1000 animated entities на 1000 активных ragdolls: число тел/контактов/joints требует отдельного benchmark, sleeping и ограниченного количества активных ragdolls. Physics dependency в текущий core не добавлена.
