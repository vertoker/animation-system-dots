# Три сцены проверки Animator: GO, ECS и 100 ботов

Статус: реализовано и проверено 2026-11-07. Три сцены используют один controller с 34 клипами, 21 состоянием и 3 слоями; полная 60-секундная native/ECS трасса посещает все состояния, максимум 7.611 мм / 0°. Sampling/blending работают в Burst; финальный точный Humanoid retarget использует согласованный managed unbound HumanPoseHandler без GameObject/Animator/PlayableGraph на персонажа. Пройдены 263 Editor/Sandbox и 54 Runtime теста, clean UPM/Assets consumers без Input System и D3D12 Player-проверки. 100 ботов: median 3.28 мс / P95 5.91 мс при 1280×720 на RTX 4060 Ti. Детали и границы: [AnimatorValidation.md](../../../Assets/AnimationSystemDOTS/Documentation~/AnimatorValidation.md).

## Цель и согласованные исходные решения

Проверить основные возможности графа Unity Animator в собственном ECS runtime на одном и том же персонаже, наборе клипов и `.controller`. Авторинг остаётся в обычном окне Animator. Первая сцена исполняет граф нативно на GameObject, вторая — на одном управляемом ECS-персонаже, третья — на 100 автономных ECS-персонажах. У каждого ECS-персонажа собственное состояние и параметры, определение графа и библиотека разделяются.

Пользователь просит использовать много предоставленных анимаций, но количество клипов вторично относительно полезной проверки graph blending. Рэгдолл пока только оценивается. Персонаж — уже используемый лысый мужчина. Работать и коммитить только на master, не push. Unity CLI остаётся отключён; работа с Editor через Coplay MCP.

## Что есть сейчас

- HEAD на момент исследования: dec06f7. Рабочее дерево было чистым.
- `Sources~/Mixamo_Bots100/`: 54 FBX, около 43.8 MiB, и `Locomotion Pack.zip`. В FBX входят исходная модель Y Bot и движения; это не заявление о 54 пригодных клипах. Архив содержит дополнительные имена и требует проверки повторов по содержимому, не по имени. SHA-256 всех существующих FBX не выявил побайтовых дублей.
- В пакете уже есть clip baking, target-specific Humanoid baking, Burst sampling, базовое смешивание override/additive с масками, affine model poses и Entities Graphics skinning.
- Текущий контроллер поддерживает один слой, только AnimationClip motions, параметры и атомарные переходы. Trees, дополнительные слои, nested machines, interruption и IK сейчас отвергаются конвертером.
- `AnimationClipBlob.ReferencePose` сейчас заполняется rest pose рига: для авторского Additive Reference Pose это потребуется исправить, с версионированием cache/schema.
- `AnimationPoseModificationSystemGroup` уже расположен после animation blending и до ModelPoseSystem. Physics-пакета в manifest сейчас нет.

## Подход

Сохраняется уже выбранный пользователем подход: один редактируемый Unity `.controller`, который нативно работает в GO-сцене и конвертируется в ECS-данные для двух других сцен.

Отдельный вручную написанный ECS-граф потребовал бы постоянно синхронизировать две реализации авторского графа. Native Animator на каждом ECS-персонаже не соответствует цели собственного ECS runtime. Эти варианты не выбираются. Нового выбора архитектуры от пользователя не требуется.

Это крупное расширение этапа 5, с тремя пользовательскими сценами. Контроллер будет наращиваться с проверкой семантики отдельных функций; наличие красивой демонстрации не закрывает неподтверждённые функции.

## Три сцены

Предлагаемые имена не затрагивают существующие 01/02/04:

| Сцена | Исполнение | Назначение |
|---|---|---|
| `05_AnimatorGO.unity` | Обычный Animator, GameObject-кости, SkinnedMeshRenderer | Управляемый персонаж и нативный эталон |
| `06_AnimatorECS.unity` | ECS motor, ECS graph evaluator, Burst pose pipeline, Entities Graphics | То же управление, модель, граф, арена и камера |
| `07_AnimatorBots100.unity` | 100 ECS motors и графов, одно определение графа | Автономные маршруты, прыжки, варианты действий и нагрузка |

Камера, UI и чтение устройств ввода могут оставаться GO. В сценах 06/07 сам персонаж, его перемещение, состояние графа и поза работают в ECS; runtime Animator, PlayableGraph, bone GameObjects или GO CharacterController на каждого ECS-персонажа не создаются.

ECS authoring/content сцены импортируются вместе с Sandbox. Все новые исходники sample живут в `Samples~/Sandbox`, core/runtime — в корне пакета, тесты — `Tests~`. Проверяются UPM sample import и Assets/submodule layout без жёстких путей к DevProject или Sources~.

## Управление и арена — предложения исполнителя

WASD — камера-относительное движение, Shift — спринт, Space — прыжок, мышь — обзор. Отдельные клавиши для wave, удара/комбо, реакции и смерти/восстановления с подсказками в UI. Точная раскладка действий остаётся выбором исполнителя.

Движение in-place: перемещение по арене и вертикальную траекторию прыжка задаёт motor. Нативный Animator использует `applyRootMotion=false`; ECS использует ту же политику root/height. Извлечение и применение root motion этим этапом не объявляются реализованными. Для прыжков отдельно проверяется, чтобы motor и таз не удваивали вертикальный подъём.

Для сопоставления используется общий unmanaged алгоритм перемещения и одинаковые настройки ускорения, поворота, гравитации и ground state. GO оборачивает его в MonoBehaviour, ECS исполняет в jobs. Арена имеет аналитически заданный пол, границы и небольшие препятствия/маршруты для прыжка. Это sample motor с явно указанными ограничениями, не универсальный физический character controller; не подменять ECS-движение вызовами GO Physics.

Input System уже установлен в DevProject (legacy Input отключён). Связь ввода с sample оформляется отдельным адаптером/assembly с документированным требованием к Input System, без зависимости core на него. Автоматические тестовые input traces и ботам устройства ввода не нужны. Чистый consumer проверяется и с корректной установкой адаптера, и без него для core/автономных сцен.

Боты используют фиксируемые seed, собственные цели, скорость, таймеры действий и jump state. Они периодически меняют маршрут, переходят walk/run/sprint, прыгают и выполняют действия. Простое локальное разнесение ограничивает наложения; полноценные NavMesh, боевой AI и физическая толпа не являются целью. Фазы и события у ботов не синхронизируются глобальным переключателем.

## Набор анимаций и подготовка

1. Составить машинный inventory: исходный путь, hash, take/clip, длительность, sample rate, skeleton/Avatar validity, loop/root policy, назначение. Просмотреть одноимённые `(1)/(2)` клипы; не считать их автоматически одинаковыми.
2. Проверить архив и распакованную папку: выбрать недостающие движения, избежать повторного включения одной и той же анимации.
3. Импортировать рабочие sample assets с устойчивыми именами/GUID, настроить Humanoid и ретаргет на текущую модель. Приватная `Sources~` не становится обязательной для sample regeneration в потребителе.
4. Выбрать согласованную по стилю и фазам ходьбу/бег из Locomotion Pack. Остальные движения распределить по вариантам, действиям и дополнительным режимам. Количество включённых клипов определяется после просмотра; целевой диапазон 25–40 пригодных движений, а не обязательная квота.
5. Не исправлять несовпадения молчаливой потерей каналов. Проверить общее пространство кривых, root, циклы и reference poses. Консервативное ограничение одинаковых bindings из предыдущего milestone снимается только там, где есть доказанная замена семантики отсутствующих каналов.

## Граф и проверяемые функции

Один `Bots100.controller` и target Avatar используются во всех трёх сценах.

| Функция | Использование / проверка |
|---|---|
| 1D Blend Tree | Idle → Walk → Run → Sprint по скорости |
| 2D Simple Directional | Направленные движения при фиксированном уровне скорости |
| 2D Freeform Directional | Walk/run в одном направлении, несколько радиусов скорости |
| 2D Freeform Cartesian | Альтернативный режим смешивания по двум независимым параметрам; отдельный управляемый режим проверки |
| Direct Blend Tree | Явные веса вариантов/жестов с проверкой нормализации и нулевых весов |
| Вложенные trees | Общая locomotion-композиция скорости/направления/варианта |
| Override layers + Avatar Mask | Wave/удар верхней частью тела поверх walk/run |
| Additive layer | Подготовленная реакция/поворот корпуса относительно явной additive reference pose, с плавным layer weight |
| Несколько состояний / sub-state machines | Locomotion, Jump, Actions и Death; входы/выходы и Any State |
| Условия / параметры | Float, Int, Bool, Trigger; варианты idle, прыжков и действий |
| Переходы | Fixed/normalized duration, Exit Time, offsets, self/AnyState, interruption source и ordered interruption |
| State motion controls | Speed multiplier, cycle offset, motion-time параметр; тестами и подходящим пользовательским режимом |
| Synced layers / timing | Отдельные synthetic/native fixtures; в основной граф добавлять только при полезной роли, не для счётчика функций |

Пять solver modes реализуются раздельно. Нельзя заменить все 2D-алгоритмы одним inverse-distance приближением. Разные режимы можно выбирать через debug panel; одновременное проигрывание всех деревьев не требуется.

Базовые состояния без IK должны работать во всех трёх сценах. IK не является условием завершения этого graph milestone. Если позже добавляется foot/hand IK, он оформляется как отдельный ECS pose modifier с отдельным нативным эталоном, а не как обещание автоматического исполнения OnAnimatorIK/Animation Rigging.

Write Defaults=true и arbitrary StateMachineBehaviour callbacks не включаются в обещание совместимости. Основной граф использует Write Defaults=false, с явными нейтральными motions/слойными весами. Legacy Animation, Timeline, object-reference curves, animation events и произвольные скриптовые callbacks этим этапом не закрываются.

## Изменения runtime и baking

- Версионируемое дерево motion nodes/children и слойные определения в controller blob, с детерминированным порядком обхода.
- Собственное mutable состояние каждого слоя на entity: current/next clocks, transition/interruption history, layer weight. Существующий single-layer API сохраняется либо получает документированную миграцию; нет двух писателей request/clock.
- Смешивание учитывает дерево/слой и их порядок. Простое последовательное quaternion slerp не считается доказательством native multi-way blending; solver и pose mixer проверяются раздельно.
- Target-specific mask conversion: Humanoid body parts и transform paths, metadata и invalidation по Avatar/rig/controller/mask/clip/reference pose.
- Additive reference pose действительно запекается из настройки клипа, а не неизменно берётся из rig rest pose.
- Nested machine entry/exit и transition interruption работают по native traces, включая приоритет и источник прерывания. Историю смешиваемой позы нельзя терять при прерывании.
- Явный AnimationStepContext остаётся единственным источником времени. Seek/replay/repeated ticks сохраняют контракт; новое mutable состояние входит в reset/rollback snapshot.
- Scratch buffers принадлежат instance/World, заранее планируются по верхней границе graph complexity; нет managed allocation или Animator/PlayableGraph per entity в steady state.
- Unsupported/degenerate input выдаёт точный diagnostic с asset/state/tree path. Непрошедшую функцию нельзя автоматически отключить, оставить работающую только GO-сцену и объявить ECS parity.

## Точность Humanoid — риск, который нужно разрешить измерением

Сейчас Humanoid-клип ретаргетится в Editor отдельно в local TRS, затем runtime смешивает эти позы. Unity может смешивать Humanoid-данные в ином пространстве и с иной семантикой масок. Совпадение одного клипа не доказывает совпадения trees, additive или нескольких слоёв.

В начале реализации создаётся нативный differential fixture на реальных Mixamo-клипах и текущем target Avatar: 1D, multi-way directional blend, upper-body mask, additive. Сравниваются кисти/ступни/таз и все значимые кости. При превышении погрешности сначала устанавливается причина, затем меняются данные/алгоритм, а не скрывается расхождение ослаблением допуска.

Для synthetic Generic fixtures стартовый допуск: веса 1e-4, времена 1e-5 s, позиция 1e-4 m, угол 0.05°. Для target Humanoid — контрольная граница 0.02 m по мировым суставам и 2° по локальным вращениям, с отдельно записанным отклонением single-clip baseline и максимальным отклонением каждого blend scenario. Это критерии для расследования, не уже полученные результаты. Более узкие установленные baseline-проверки не ослабляются. Если архитектурная причина требует отдельного большого изменения, оно описывается явно до обещания завершённой совместимости.

## Проверка и критерии завершения

1. Синтетический native oracle для каждого tree mode: сетка внутри/снаружи hull, начало координат, границы, нулевые/крайние веса, nested nodes, children speed/cycle offsets, duration и временная синхронизация. Выражать clip weights через специально подобранные независимые motion channels, а не недоступные private APIs.
2. Дифференциальные traces для layers/masks/additive/reference poses, nested state machines, synced layers и всех заявленных вариантов interruption. Порядок слоёв, trigger consumption, текущие/следующие состояния, веса/время и итоговая поза проверяются отдельно.
3. Одинаковая фиксированная последовательность input/parameters в GO и ECS: разгон, диагонали, sprint, несколько прыжков, wave во время бега, удар, реакция во время перехода, смерть/сброс. Motor и animation clock проверяются отдельно. Сохраняются отчёты максимальных pose errors, не только screenshots.
4. Вручную пройти 05 и 06 с одинаковой раскладкой. Панель показывает активные слои/состояния, parameters, tree weights, действия, reset и режим воспроизведения записанного trace.
5. 07 действительно создаёт ровно 100 управляемых ботами ECS-персонажей: различные параметры и состояния, одно shared graph definition, конечные успешные skin palettes, ноль runtime Animator/PlayableGraph. Прогон включает все action categories; смерть возвращает бота через явный respawn/reset.
6. Проверить reload SubScene, rebake графа/маски/reference pose, despawn/respawn и Disable Domain Reload. Очистка graphs/preview scenes/test worlds доказана.
7. Полные подходящие Editor/Runtime suites плюс существующий 02 controller и 04 Humanoid regression. Затем реальные UPM и Assets/submodule consumers с новыми scenes и sample dependency setup.
8. Editor и отдельный видимый Player benchmark: build/configuration/GPU/resolution, warmup/frame count, median/p95 frame time; отдельно controller, sampling/blending и skinning. Не переносить прежние 1000-controller/04 цифры на новый граф.
9. Обновить Controllers.md, SupportMatrix.md и русскую ручную проверку: управление, ожидаемое поведение, доступные режимы, конкретные ограничения, как воспроизвести сравнение. Отметить каждый поддержанный режим отдельно. Этап 5 остаётся открытым, если обязательная функция не прошла.

## Рэгдолл: оценка, без реализации в этом этапе

Возможен отдельным optional Unity.Physics adapter без зависимости core на Physics. В текущем core есть данные родителей, local/model poses и точка модификации перед skinning; это полезная основа, но готовых rigid bodies, joints, bone-body mapping или recovery ещё нет.

Предлагаемый поток: анимируемые bone targets до physics simulation → dynamic bodies/joints в ragdoll mode → чтение симуляции после physics → восстановление local poses через bone/body bind offsets → окончательные model poses и skinning. Для несимулируемых костей сохраняются локальные анимационные позы. Владелец root transform и каждой кости определён явно, animation и physics не перезаписывают друг друга. Fixed-step physics и render interpolation согласуются отдельно.

| Объём | Оценка сложности |
|---|---|
| Полный passive ragdoll: переключить анимацию в падение без подъёма | Средняя, самостоятельный milestone: collider/joint authoring, массы/limits, takeover с начальной позой и скоростями, pose readback, root ownership |
| Плавно вернуться к анимации и встать | Высокая: выбрать face-up/down get-up, выровнять root, избежать penetration и рывка, смешать позу, сохранить momentum |
| Частичный ragdoll / hit reactions | Высокая: выбор цепей, граница physical/animated bones, веса и устойчивость constraints |
| Active ragdoll с motor targets | Очень высокая, отдельная система: настройка drives, устойчивость, контакты, energy limits и нагрузка |

Для 100 персонажей ориентировочно 12–16 simulated bodies на активного ragdoll дают 1200–1600 bodies плюс joints/контакты; это оценка конфигурации, не benchmark. Нужны budget активных ragdolls, sleeping и отдельный замер. Рисовать 100 анимированных персонажей и симулировать 100 сталкивающихся ragdolls — разные нагрузки.

Сейчас достаточно не нарушить modifier order, явное pose/root ownership и возможность внешнего writer. Пустые Physics-компоненты и неиспользуемая зависимость заранее не добавляются. Death animations в текущем наборе подходят для graph/вариантов смерти; для recovery позднее понадобятся get-up clips.

Основания: текущая архитектура репозитория; Unity 6.6 [Unity Physics](https://docs.unity.com/en-us/engine/6000.6/manual/packages-list/packages-all/pack-core/com-unity-physics) — ECS/Burst physics; [joints overview](https://docs.unity.cn/Packages/com.unity.physics@1.3/manual/custom-joints.html) подтверждает ragdoll constraints, но точные API/version будущего adapter проверяются в установленной версии, а не копируются из старого overview.

## Что остаётся выбором исполнителя

Конкретные пригодные takes и имена действий после просмотра; камера/раскладка и оформление простого UI; порядок исправлений, buffer layout и алгоритмическая декомпозиция; расположение маршрутов и простые границы арены. Выбор не меняет количество сцен, общий исходный граф, ECS-владение персонажем, перечень проверяемых функций или отложенный статус рэгдолла.
