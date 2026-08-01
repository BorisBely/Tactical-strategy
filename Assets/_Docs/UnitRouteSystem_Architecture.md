# Система маршрута юнита — Полная архитектура

## 1. Общий обзор

Система маршрута охватывает все аспекты перемещения юнитов: очередь waypoint'ов, приказы поведения на сегментах маршрута, стрелки поворота, точки ожидания, рисование маршрута, контекстное меню и редактирование. Архитектура распределена между тремя главными файлами:

```
┌─────────────────────────────────────────────────────────────┐
│ RtsUnitSelectionManager (менеджер выделения + ввода)        │
│   • ПКМ на terrain → построение маршрута                     │
│   • Draw Route (зажатый ЛКМ)                                 │
│   • Взаимодействие с сегментами (hover, drag, menu)          │
│   • Управление выделенной машиной                            │
└──────────────────────────┬──────────────────────────────────┘
                           │ команды на юнитов
┌──────────────────────────▼──────────────────────────────────┐
│ RtsUnitMember (состояние маршрута одного юнита)             │
│   • Очередь waypoint'ов и команд                             │
│   • Стрелки поворота (FacingArrow)                           │
│   • Точки ожидания (WaitPoint)                               │
│   • Приказы на сегментах (Reload, Grenade, RPG, ...)         │
│   • Исполнение очереди                                       │
└──────────────────────────┬──────────────────────────────────┘
                           │ NavMeshAgent.SetDestination()
┌──────────────────────────▼──────────────────────────────────┐
│ UnitClickToMove (NavMeshAgent + анимация)                   │
│   • MoveTier: Walk / Run / Sprint                            │
│   • Управление NavMeshAgent (speed, destination, stop)       │
│   • Поворот корпуса (стрелки, engage, путь)                  │
│   • Проброс параметров в Animator                            │
└─────────────────────────────────────────────────────────────┘
```

**Поток данных при ПКМ-клике по земле:**
```
ЛКМ на terrain
 → RtsUnitSelectionManager.HandleRightMouseDown()
   → Hit ground → StartMovePreview() [Walk tier]
   
[зажатый ЛКМ при drag > threshold]
 → UpdateVehicleMovePreviewFacing() / UpdateMovePreviewFacingArrow()

ЛКМ отпущен:
 → HandleRightMouseUp()
   → Walk: BeginAwaitingDoubleClickForRun() [ждёт второго клика]
     → timeout → CommitMovePreviewOrder() → ExecuteWalkOrder()
       → ClearWaypoints → SetDestinationDirect → SetWaypointFacing → IssueMoveOrder → UnitClickToMove.IssueNavOrder()
   → Run (двойной клик): CommitMovePreviewOrder() сразу
   
Shift+клик:
 → ShiftEnqueueMoveOrders() → EnqueueWaypoint() каждого юнита в группе

Alt+клик:
 → IssueDirectMoveOrderWithWait() — немедленное движение с группой ожидания
```

---

## 2. Очередь waypoint'ов и исполнение

### 2.1 Структуры данных (RtsUnitMember)

```csharp
// Waypoints — упорядоченный список позиций
List<Vector3> m_Waypoints;

// Очередь команд — по одной на каждый waypoint после активного
List<QueuedCommand> m_CommandQueue;

// Активная команда (текущая цель движения)
bool m_HasActiveDestination;
Vector3 m_ActiveDestination;
MoveTier m_ActiveMoveTier;
LocomotionStance m_ActiveRouteStance;
int m_ActiveDestinationWaitGroup;  // группа ожидания (0=нет, 1-3)

struct QueuedCommand {
    Vector3 Destination;
    MoveTier MoveTier;
    LocomotionStance Stance;
    int AssignedWaitGroup;
    FacingArrow BoundFacingArrow;  // привязанная стрелка
    // + копии активной стрелки/ожидания для seamless перехода
}
```

### 2.2 Жизненный цикл waypoint'а

**1. Добавление в очередь:**
- `EnqueueWaypoint(dest, moveTier, facing, waitGroup)` — добавляет waypoint и команду в очередь
- При пустой очереди сразу вызывает `DequeueAndExecuteNextCommand()`

**2. Достижение waypoint'а (в Update — `TryRemoveArrivedDestination`):**
- Проверка: `NavMeshAgent.remainingDistance ≤ stoppingDistance` И скорость почти 0
- Если есть активная стрелка на этом сегменте → сначала поворот (фаза Turning)
- Если есть `ActiveDestinationWaitGroup` → вход в ожидание (WaitGate)
- Иначе → `TryAdvanceRouteQueue()`

**3. Продвижение очереди (`TryAdvanceRouteQueue`):**
- Сдвигает все привязки сегментов вниз:
  - `ShiftFacingArrowSegmentsAfterWaypointRemoved(0)`
  - `ShiftWaitHoldSegmentsAfterWaypointRemoved(0)`
  - `ShiftRouteOrdersAfterWaypointRemoved(0)`
- Удаляет `m_Waypoints[0]`
- Вызывает `TryStartNextQueuedCommand()` → `DequeueAndExecuteNextCommand()`

### 2.3 Планирование команд (ScheduleRtsCommand)

Все RTS-команды проходят через `ScheduleRtsCommand(Action, staggerKey)`:
- Применяет реакцию-задержку (имитация времени реакции юнита)
- Применяет групповой stagger (разнесение старта юнитов в группе)
- Использует version counter для отмены устаревших корутин

---

## 3. Управление NavMeshAgent (UnitClickToMove)

### 3.1 MoveTier (ярус скорости)

```
MoveTier.Walk   (0) — шаг     → 1.5 м/с (стоя), 1.15 м/с (присед), 0.5 м/с (лёжа)
MoveTier.Run    (1) — бег      → 4.6 м/с (с оружием), 3.4 м/с (без оружия)
MoveTier.Sprint (2) — спринт   → 7.25 м/с (только стоя, подавляет ready)
```

**Поведение при смене стойки:** Run/Sprint из приседа/лёжа → автоматический ForceStanding

### 3.2 Выдача приказа (IssueNavOrder)

```csharp
IssueNavOrder(worldPos, MoveTier, cancelStabilizeOther=true)
  → NavMesh.SamplePosition(worldPos, 2м)
  → если Run/Sprint → отмена pending walk
  → если Walk + текущий режим быстрее → отложить (дебаунс двойного клика)
  → IssueNavOrderInternal:
      → отмена стабилизации другого
      → если StanceTransitionBlocked → pending (ждёт конца анимации)
      → m_Agent.SetDestination(destination)
      → ApplyTierSpeed() (множители формации × выносливости)
      → PrimeAnimatorForMoveStart() (floor NavSpeed)
```

### 3.3 Обновление каждый кадр (Update)

1. **PendingNavOrder** — потребление отложенного приказа (после анимации стойки)
2. **StanceTransitionBlocked** — блокировка движения на время анимации вставания/укладки
3. **TryEarlyArrivalStop** — ранняя остановка за `m_EarlyArrivalDistance` (0.15м)
4. **TickPendingSingleRightClick** — дебаунс одиночного/двойного ПКМ
5. **TryRightClick** — обработка прямого ПКМ:
   - Одиночный → отложить на `SingleClickCommitDelay` (0.12с)
   - Двойной (в окне `DoubleClickSeconds` 0.15с) → Run
   - При активном Run/Sprint одиночный ждёт полное окно `DoubleClickSeconds`
6. **UpdateAgentSpeedToTarget** — сглаживание `NavMeshAgent.speed`:
   - Smooth seconds: 0.15с (ускорение), 0.2с (замедление)
   - `acceleration = max(m_AgentAcceleration, speed * 4.5)`
7. **UpdateFacing** — приоритеты поворота корпуса:
   - Граната/RPG → не крутить
   - ManualFacing → OverrideFacingAngle
   - Run/Sprint → по направлению движения
   - Engage (видимая цель + ready) → SmoothDamp на цель
   - Path movement → Slerp к направлению движения
   - Без движения + ready → не крутить (держать ready)

### 3.4 Параметры аниматора

```csharp
NavSpeed          — 0..1 интенсивность локомоции (relative to sprint)
NavStrafe         — -1..1 стрейф по X
NavForward         — -1..1 движение вперёд/назад по Z
LocomotionTier     — 0=walk, 1=run, 2=sprint (0 в лёже)
LocomotionTierBlend — float дубликат для blend tree
```

---

## 4. Система стрелок поворота (FacingArrow)

### 4.1 Типы стрелок (FacingArrowMode)

```
TurnOverDistance — развернуться на угол на расстоянии от якоря
HoldToEnd        — синяя стрелка: держать угол до конца сегмента
LookAtPoint      — зелёная стрелка: смотреть на world-точку
TurnOnArrival    — жёлтая стрелка: развернуться по прибытии
```

### 4.2 Приоритетная фаза (ArrowPriorityPhase)

```
None
  │ стрелка активирована в зоне досягаемости
  ▼
Turning — юнит разворачивается на стрелку
  │ разворот завершён
  ▼
BlueHold / GreenHold — удержание направления (HoldToEnd / LookAtPoint)
  │ конец сегмента / уход цели из FOV
  ▼
YellowReturning — жёлтая стрелка: возврат к сканированию
  │ Rescan / новая цель найдена
  ▼
None — возврат к нормальному поведению
```

### 4.3 Визуализация стрелок

- **Цвета:** Жёлтый `#FFD933`, Синий `#33B2FF` (HoldToEnd), Зелёный `#4DF24D` (LookAtPoint)
- **Рендеринг:** LineRenderer на дочернем GameObject
- **Позиционирование:** `GetFacingArrowShaftEndpoints()` вычисляет shaft от AnchorWorld на указанный Angle
- **Ограничение:** минимум 28 пикселей между соседними стрелками
- **Синхронизация:** `SyncFacingArrows()` создаёт/уничтожает visual GameObjects

### 4.4 Активация и деактивация

- `TryActivateClosestFacingArrowInRange()` — находит ближайшую активную стрелку в зоне досягаемости
- `StartFacingTurn(angle)` — начинает разворот, входит в фазу Turning, устанавливает `OverrideFacingAngle`
- `ClearFacingTurn()` — сбрасывает разворот
- `ResetActiveArrowFacingHold()` — сбрасывает удержание стрелки

---

## 5. Система точек ожидания (WaitPoint)

### 5.1 Группы ожидания

```
Группа 0 — нет ожидания
Группа 1 — первая группа (продолжить: F1)
Группа 2 — вторая группа (продолжить: F2)
Группа 3 — третья группа (продолжить: F3)
```

### 5.2 Жизненный цикл ожидания

1. **Установка:** `TrySetWaitAtRouteSegment(segmentIndex, segmentT)` → циклический перебор групп 1→2→3
2. **Привязка к сегменту:** `BindWaitHoldToRoute()` хранит `WaypointIndex + SegmentT` для пересчёта world-позиции при перемещении
3. **Вход в ожидание:** при достижении waypoint'а с `ActiveDestinationWaitGroup` → `EnterWaitAfterArrival()`
   - Устанавливает `m_IsWaitingAtRouteGate = true`
   - Принудительный `Walk` (сброс run/sprint)
4. **Продолжение:** клавиши F1/F2/F3 → `ContinueSelectedRouteWaitGroup(group)` на всех выделенных юнитах
   - Сбрасывает `m_IsWaitingAtRouteGate`
   - Вызывает `TryStartNextQueuedCommand()` — продолжение по маршруту
5. **Удаление:** `TryRemoveWaitPointAtWaypoint(waypointIndex)` — сбрасывает группу в 0

### 5.3 Сдвиг при редактировании

При вставке/удалении waypoint'ов привязки ожидания сдвигаются:
- `ShiftWaitHoldSegmentsForWaypointInsert(insertedWaypointIndex)`
- `ShiftWaitHoldSegmentsAfterWaypointRemoved(removedWaypointIndex)`

---

## 6. Приказы на сегментах маршрута (Route Orders)

### 6.1 Типы приказов

```csharp
List<GrenadeRouteOrder>       m_GrenadeOrders;
List<RocketLauncherRouteOrder> m_RocketLauncherOrders;
List<ReloadRouteOrder>        m_ReloadOrders;
List<MagazineRefillRouteOrder> m_RefillOrders;
List<LocomotionRouteOrder>    m_LocomotionOrders;
```

**Каждый приказ содержит:**
- `RouteSegmentIndex` — к какому сегменту привязан
- `RouteSegmentT` — позиция на сегменте (0..1)
- `WaypointPosition` — world-позиция якоря

### 6.2 Активация и исполнение

**Update → `UpdateActiveRouteOrders()` (каждый кадр):**
1. Проверка: `!m_IsExecutingGrenadeOrder && !m_IsExecutingRouteOrder`
2. Проверка: `HasActiveDestination && !IsWaitingAtRouteGate`
3. Приоритет проверки:
   - **Grenade** → `TryActivateReachedGrenadeOrder()` → `RouteSegmentIndex == 0` (на активном сегменте) + позиция в пределах досягаемости
   - **RocketLauncher** → аналогично, подписка на `OrderStateChanged`
   - **Reload** → аналогично, корутина `CoWaitReloadThenResume()`
   - **MagazineRefill** → подписка на `AllMagazinesLoadingCompleted`
   - **Locomotion** → `ApplyLocomotionRouteOrder()` — мгновенное применение

4. **При активации любого блокирующего приказа:**
   - `EndRouteRunForBlockingOrder()` — сбрасывает Run→Walk на ВЕСЬ оставшийся маршрут
   - `StopAgentForRouteOrder()` — останавливает NavMeshAgent
   - Устанавливается `m_IsExecutingRouteOrder = true`

5. **После завершения:** `ResumeRouteAfterOrder()`
   - Сбрасывает `m_IsExecutingRouteOrder`
   - Возобновляет NavMeshAgent
   - Перевыдаёт `IssueMoveOrder` к текущему waypoint'у

### 6.3 LocomotionRouteOrder

**Данные:** `MoveTier`, `LocomotionStance`, `RouteSegmentIndex`, `RouteSegmentT`, `WaypointPosition`

**Применение:** мгновенно меняет активный MoveTier и Stance + `PropagateLocomotionToRemainingRoute()` копирует их во все последующие `QueuedCommand`'ы.

### 6.4 Сдвиг при редактировании

При вставке/удалении waypoint'ов:
- `ShiftRouteOrdersForWaypointInsert(insertedWaypointIndex)`
- `ShiftRouteOrdersAfterWaypointRemoved(removedWaypointIndex)`

---

## 7. Контекстное меню маршрута (ПКМ на сегмент)

### 7.1 RouteInteractionMenuController

**Singleton** (DontDestroyOnLoad), sorting order 31600.

**Меню строится динамически на Canvas:**
- Панель с `VerticalLayoutGroup` + `ContentSizeFitter`
- Кнопки с `RouteMenuItemHoverRelay` — ховер открывает подменю
- Отступ от курсора: (+6px, -6px)
- Автоматический clamp к краям экрана

**Элементы меню:**
| Label | Action | Подменю |
|---|---|---|
| "Перезарядка" | Reload | Нет |
| "Граната" | Grenade | Подменю по типам гранат |
| "Гранатомёт" | RocketLauncher | Подменю по индексу рюкзака |
| "Режим движения" | Locomotion | "Бег", "Шаг", "Присед" |
| "Поворот" | Facing | "Разворот", "Смотреть на точку", "Удержание" |
| "Пополнить магазины" | MagazineRefill | Нет |
| "Остановка" | WaitPoint | Нет |

### 7.2 RouteInteractionMenuAction (enum)

```csharp
Reload = 0, Grenade = 1, RocketLauncher = 2,
Locomotion = 3, Facing = 4, MagazineRefill = 5, WaitPoint = 6
```

### 7.3 Обработка действий (RtsUnitSelectionManager)

**`HandleRouteInteractionMenuAction(action, unit, segmentIndex, worldPoint, payload)`:**
- **Reload** → `Unit.AddReloadOrder(segmentIndex, worldPoint)`
- **Grenade** → `Unit.AddGrenadeOrder(segmentIndex, worldPoint, grenadeType)`
- **RocketLauncher** → `Unit.AddRocketLauncherOrder(segmentIndex, worldPoint, bagIndex)`
- **Locomotion** → `Unit.AddLocomotionOrder(segmentIndex, MoveTier, Stance)`
- **Facing** → зависит от `FacingArrowMode`:
  - `TurnOverDistance` → `SetWaypointFacing(segmentIndex, angle, TurnOverDistance)`
  - `HoldToEnd` → `SetWaypointFacing(segmentIndex, angle, HoldToEnd)`
  - `LookAtPoint` → следующий клик ставит LookPoint
- **MagazineRefill** → `Unit.AddRefillOrder(segmentIndex, worldPoint)`
- **WaitPoint** → `Unit.TrySetWaitAtRouteSegment(segmentIndex, segmentT)`

### 7.4 Жесты мыши для меню

1. **Короткий ПКМ (без движения):** открывает меню по `ShowForRoute()`
2. **ПКМ + drag > threshold (5px):** режим редактирования стрелки поворота (facing edit)
3. **ЛКМ вне меню / Escape:** `HideImmediate()`
4. **ЛКМ внутри меню:** `HandleItemClicked()` → вызывает `ActionClicked` → `HandleRouteInteractionMenuAction()`

---

## 8. Редактирование маршрута (Drag Waypoints)

### 8.1 Определение точки редактирования

**`TryPickRouteSegment(unit, screenPoint)` (RtsUnitMember):**
- Перебирает полилинию NavMesh-пути каждого сегмента
- Ищет ближайшую точку на полилинии к screenPoint
- Возвращает `segmentIndex, segmentT, worldPoint`

**`TryPickRouteEditTarget()` (RtsUnitSelectionManager):**
- Сначала проверяет vertex'ы (waypoint'ы) — радиус захвата ×1.8
- Если не попал в vertex → проверяет точки на сегменте

### 8.2 Режим редактирования

1. **Начало:** `TryBeginRouteDragOnPress()` — вставляет waypoint в сегмент (если нужно), переводит в режим `m_SuppressLiveAgentRoutePathVisual`
2. **Движение:** `UpdateRouteDrag(screenPoint)` → `UpdateRouteEditWaypoint(screenPoint)` на юните
3. **Конец:** `EndRouteDrag()` — сбрасывает `m_SuppressLiveAgentRoutePathVisual`

### 8.3 Сдвиг привязок при топологических изменениях

При вставке/удалении waypoint'а все привязки сдвигаются:
- **FacingArrow:** `ShiftFacingArrowSegmentsForWaypointInsert` / `ShiftFacingArrowSegmentsAfterWaypointRemoved`
- **WaitPoint:** `ShiftWaitHoldSegmentsForWaypointInsert` / `ShiftWaitHoldSegmentsAfterWaypointRemoved`
- **RouteOrders:** `ShiftRouteOrdersForWaypointInsert` / `ShiftRouteOrdersAfterWaypointRemoved`
- **Перепривязка:** `RebindFacingArrowsAfterRouteTopologyChange()` пересчитывает мировые позиции якорей

---

## 9. Draw Route (рисование маршрута зажатым ЛКМ)

### 9.1 Активация

**`TryDetectDrawRouteStart()` (RtsUnitSelectionManager):**
- ЛКМ зажат на выделенном юните/точке пути
- Первая точка (drag start) — это позиция юнита или его текущая цель
- `EnterDrawRouteMode()` — включает визуализацию линии

### 9.2 Сбор точек

- Каждые `DrawRouteSampleInterval` секунд семплируется позиция мыши
- Точка добавляется в список если расстояние от предыдущей > `DrawRouteMinSampleDistance`

### 9.3 Финализация

**`CommitDrawRoute()`:**
- Упрощение Дугласа-Пекера (Douglas-Peucker) — удаляет коллинеарные точки
- Для каждого waypoint'а (кроме первого — он активный): `EnqueueWaypoint()` на каждом юните
- Если выделено несколько юнитов: `ShiftEnqueueMoveOrders()` с формационным смещением

---

## 10. Система превью (Move Preview)

### 10.1 Этапы превью

1. **StartMovePreview:** зажатый ПКМ на terrain → визуализация линии к курсору
2. **UpdateMovePreview:** обновление линии + стрелки поворота при каждом движении мыши
3. **MovePreviewFacingArrow:** если мышь отведена от точки назначения > порог — вычисляется heading
4. **CommitMovePreviewOrder:** ПКМ отпущен → исполнение приказа

### 10.2 Режимы исполнения

| Модификатор | Поведение |
|---|---|
| Обычный клик | `ClearWaypoints` → новый маршрут |
| Shift + клик | `ShiftEnqueueMoveOrders` → добавить в конец очереди |
| Alt + клик | `IssueDirectMoveOrderWithWait` → маршрут с точкой ожидания |
| Двойной клик | Run (бег) вместо Walk |
| Ctrl + клик (машина) | Slow speed mode |

---

## 11. Логирование

### 11.1 RouteMovementDebug

Статический класс логов (только в Editor/DevBuild):

```csharp
RouteMovementDebug.Log(RtsUnitMember, message)       → [RouteDbg:юнит]
RouteMovementDebug.LogOrder(RtsUnitMember, message)   → [RouteOrder:юнит]
RouteMovementDebug.LogWait(RtsUnitMember, message)    → [RouteWait:юнит]
RouteMovementDebug.LogMove(RtsUnitMember, message)    → [RouteMove:юнит]
RouteMovementDebug.LogManager(message)                → [RouteDbg:Manager]
```

**Переключатели:**
- `LoggingEnabled` (default false) — мастер-выключатель
- `OrderLoggingEnabled` (default true) — очереди/исполнение приказов
- `WaitLoggingEnabled` (default true) — вход/выход из ожидания
- `MoveLoggingEnabled` (default true) — старт/стоп/возобновление движения
- `PeriodicStateLoggingEnabled` (default true) — периодический STATE/STUCK

### 11.2 UnitClickToMove (NAV_CLICK)

```csharp
[RouteDbg:юнит] NAV_CLICK set_destination mode=Walk dest=(x,z)
[RouteDbg:юнит] NAV_CLICK set_destination mode=Run dest=(x,z)
[RouteDbg:юнит] NAV_CLICK deferred_walk currentMode=Run dest=(x,z)
[RouteDbg:юнит] NAV_CLICK pending_stance mode=Run dest=(x,z)
[RouteDbg:юнит] NAV_CLICK no_agent / unconscious / navmesh_sample_failed
```

### 11.3 ReadyMoveFacing (опционально)

```csharp
[ReadyMoveFacing] unit=XXX mode=engage tier=Walk bodyYaw=45° barrelYaw=52° body↔barrel=7°
  moveYaw=38° body↔move=-7° move↔barrel=14°
  targetYaw=49° body↔target=4° barrel↔target=-3° target=EnemySoldier
  navFwd=0.85 navStrafe=0.12
```

---

## 12. Контекстная панель действий (ActionPanelController)

Нижняя панель с кнопками, появляется при наведении курсора на низ экрана.

**Кнопки (16 штук):**
| # | Label | Клавиша | Действие |
|---|---|---|---|
| 0 | Граната | G | CycleGrenadeThrowType |
| 1 | Гранатомёт | H | CommandSelectedRocketLauncher |
| 2 | Готовность | E | ToggleSelectedReady |
| 3 | Присед | C | CommandSelectedCrouchToggle |
| 4 | Зарядка | T | CommandSelectedManualMagazineLoading |
| 5 | Перезарядка | R | CommandSelectedWeaponReload |
| 6 | Реж.прицел | B | CommandSelectedCycleWeaponAimMode |
| 7 | Реж.огня | V | CommandSelectedCycleWeaponFireMode |
| 8 | Наведение | Q | ToggleRotateToPointMode |
| 9 | Построение | X | CycleSelectedFormation |
| 10 | Инвентарь | I | ToggleInventoryWindow |
| 11 | На турель | P | CommandSelectedVehicleToggleGunner |
| 12 | Высадка | U | CommandSelectedVehicleDisembarkExceptDriver |
| 13 | Погр.раненого | — | CommandLoadWounded |
| 14 | Завести/Заглушить | — | CommandSelectedVehicleToggleEngine |
| 15 | Скор: X | — | CommandSelectedVehicleCycleSpeedCeiling |

**Условное отображение:**
- Гранатомёт → только если есть гранатомёт в инвентаре
- Построение → только если выделено >1 юнитов
- Инвентарь → только если выделен 1 юнит
- На турель/Высадка/Завести/Скорость → только при выделенной машине
- Погр.раненого → только если выделенный юнит несёт раненого

**Поведение:**
- `hover → fade in` (0.2с)
- `mouse leaves → fade out` (0.2с)
- `pause menu active → hide`
- ПКМ на кнопку "Высадка" → `VehicleDisembarkMenuController.ShowForVehicle()` (контекстное меню высадки)

---

## 13. Взаимодействие с машиной (RtsUnitSelectionManager.Vehicle.cs)

### 13.1 Движение машины (ПКМ)

```
RMB press → raycast ground
  → Double-click → Fast speed mode
  → Single click → preview, ждёт double-click window
    → timeout → CommitVehicleMovePreview() (Medium speed)
  → Ctrl+click → Slow speed mode

RMB hold + drag > threshold → heading facing preview
RMB release (с heading) → VehicleMoveGoal.FromPositionAndHeading()
RMB release (без heading) → корутина PendingVehicleMediumMove
```

### 13.2 Посадка в машину (ЛКМ)

```
LMB double-click на машину с выделенными юнитами → BoardUnits()
LMB single-click на машину → корутина CommitVehicleSelectionAfterDoubleClickWindow()
  → ждёт второй клик
  → timeout → выделение машины
  → double-click → BoardUnits()
```

---

## 14. Структуры данных — полный справочник

### 14.1 FacingArrow (RtsUnitMember)

| Поле | Тип | Описание |
|---|---|---|
| Angle | float | World yaw угол |
| Mode | FacingArrowMode | TurnOverDistance / HoldToEnd / LookAtPoint / TurnOnArrival |
| ForceReadyOnActivation | bool | Принудительный HighReady при активации |
| ActivateAtSegmentStart | bool | Активировать в начале сегмента |
| HasLookPoint | bool | Есть ли точка взгляда |
| RouteSegmentIndex | int | Индекс привязанного сегмента |
| RouteSegmentT | float | Позиция на сегменте (0..1) |
| LookOffsetFromAnchor | Vector3 | Смещение точки взгляда от якоря |
| LookPointWorld | Vector3 | World-позиция для LookAtPoint |
| AnchorWorld | Vector3 | World-позиция якоря стрелки |

### 14.2 FacingArrowMode (enum)

```csharp
TurnOverDistance = 0  // разворот на дистанции
HoldToEnd = 1         // держать до конца сегмента (синяя)
LookAtPoint = 2       // смотреть на точку (зелёная)
TurnOnArrival = 3     // разворот по прибытии (жёлтая)
```

### 14.3 ArrowPriorityPhase (enum)

```csharp
None = 0
Turning = 1           // активный разворот
YellowReturning = 2   // возврат после жёлтой стрелки
BlueHold = 3          // удержание синей стрелки
GreenHold = 4         // удержание зелёной стрелки
```

### 14.4 QueuedCommand (RtsUnitMember)

| Поле | Тип | Описание |
|---|---|---|
| Destination | Vector3 | Точка назначения |
| MoveTier | MoveTier | Ярус скорости |
| Stance | LocomotionStance | Стойка |
| AssignedWaitGroup | int | Группа ожидания (0-3) |
| BoundFacingArrow | FacingArrow? | Привязанная стрелка поворота |

### 14.5 MoveTier (UnitClickToMove)

```csharp
Walk = 0    // 1.5 м/с стоя, 1.15 присед, 0.5 лёжа
Run = 1     // 4.6 м/с с оружием, 3.4 без оружия
Sprint = 2  // 7.25 м/с (подавляет HighReady)
```

### 14.6 LocomotionStance (UnitAnimatorStance)

```csharp
Standing = 0
Crouch = 1
Prone = 2
```

### 14.7 RouteInteractionMenuAction (enum)

```csharp
Reload = 0
Grenade = 1
RocketLauncher = 2
Locomotion = 3
Facing = 4
MagazineRefill = 5
WaitPoint = 6
```

### 14.8 LocomotionRouteOrder

```csharp
struct LocomotionRouteOrder {
    MoveTier MoveTier;
    LocomotionStance Stance;
    int RouteSegmentIndex;
    float RouteSegmentT;
    Vector3 WaypointPosition;
}
```

---

## 15. Слабые места и потенциальные проблемы

### 15.1 Очередь маршрута
- `TryAdvanceRouteQueue` не проверяет валидность нового waypoint'а на NavMesh
- При добавлении waypoint'а в середину очереди не пересчитывается NavMesh-путь
- Нет валидации суммарной длины маршрута

### 15.2 Стрелки поворота
- Несколько стрелок на одном сегменте могут конфликтовать (разные углы)
- `OverrideFacingAngle` может конфликтовать с engage (видимой целью)
- Визуализация LineRenderer не очищается при уничтожении юнита

### 15.3 Ожидание (WaitPoint)
- При входе в ожидание юнит принудительно переводится в Walk, но не запоминает предыдущий MoveTier
- F1/F2/F3 продолжают ВСЕХ юнитов группы одновременно — нет выборочного продолжения

### 15.4 Приказы на сегментах
- Приказы исполняются строго последовательно — нельзя иметь гранату + перезарядку на одном сегменте
- `EndRouteRunForBlockingOrder` сбрасывает Run на ВЕСЬ маршрут, а не только на текущий сегмент
- Нет таймаута для приказов — если граната не бросается, маршрут вечно заблокирован

### 15.5 Редактирование маршрута
- При перетаскивании waypoint'а не обновляется NavMesh-путь до отпускания
- Сдвиг привязок происходит только по индексу — если waypoint переместился за пределы видимости, привязка может указывать в воздух

### 15.6 Draw Route
- Упрощение Дугласа-Пекера может удалить важные углы маршрута
- Нет ограничения на количество waypoint'ов в очереди

### 15.7 Дебаунс кликов
- Одиночный Walk-клик задерживается на `SingleClickCommitDelay` (0.12с) — ощутимая задержка
- Если Run/Sprint активен, одиночный клик ждёт ПОЛНОЕ окно DoubleClick (0.15с) — может быть воспринято как игнорирование команды
- Нет визуальной индикации отложенного клика
