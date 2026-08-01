# Vehicle Navigation System v2.4
## Полная документация — июль 2026

---

## 1. Оглавление

1. [Архитектура — общая схема](#2-архитектура--общая-схема)
2. [Поток данных — от клика до колёс](#3-поток-данных--от-клика-до-колёс)
3. [Часть 1: Система приказов и очередь](#4-часть-1-система-приказов-и-очередь)
4. [Часть 2: Heading-aware прибытие](#5-часть-2-heading-aware-прибытие)
5. [Часть 3: Проверка выполнимости](#6-часть-3-проверка-выполнимости)
6. [Часть 4: Умный планировщик движения](#7-часть-4-умный-планировщик-движения)
7. [Часть 5: Восстановление, стратегии, композиты](#8-часть-5-восстановление-стратегии-композиты)
8. [Часть 6: Физический слой и фазы движения](#9-часть-6-физический-слой-и-фазы-движения)
9. [Часть 7: Vehicle Safety System](#10-часть-7-vehicle-safety-system)
10. [Часть 8: Precision Arrival System](#11-часть-8-precision-arrival-system)
11. [Часть 9: SubSystem Reverse — задний ход](#12-часть-9-subsystem-reverse--задний-ход)
12. [Манёвры — полный справочник](#13-манёвры--полный-справочник)
13. [Логирование — все теги и сообщения](#14-логирование--все-теги-и-сообщения)
14. [Тестовый полигон](#15-тестовый-полигон)
15. [Полный список файлов](#16-полный-список-файлов)
16. [Словарь терминов](#17-словарь-терминов)
17. [Исправления v2.3 — TargetSide, GoalLocked, жёсткий Arrival](#18-исправления-v23--targetside-goallocked-жёсткий-arrival)
18. [Исправления v2.4 — Reverse Overhaul + стабильность](#19-исправления-v24--reverse-overhaul--стабильность)
19. [Известные проблемы](#20-известные-проблемы-todo)

---

## 2. Архитектура — общая схема

```
                                 Игрок (клик / Shift+клик / ПКМ+тянуть)
                                    │
                                    ▼
┌──────────────────────────────────────────────────────────┐
│ VehicleController (фасад)                                 │
│   IssueMoveOrder() — замена цели                          │
│   AppendMoveToQueue() — добавить в очередь                 │
│   StopCurrentOrder() / ClearOrders() / HardStop()         │
└────────────────────────┬─────────────────────────────────┘
                         │
                         ▼
┌──────────────────────────────────────────────────────────┐
│ VehicleOrderQueue                                         │
│   m_Queue: [Order₁, Order₂, Order₃]                       │
│   m_Current: Order₀ (Executing)                           │
└────────────────────────┬─────────────────────────────────┘
                         │
                         ▼
┌──────────────────────────────────────────────────────────┐
│ VehicleNavigation (владелец всех подсистем)                │
│   EnqueueOrder() → TryPromoteNextOrder()                  │
│                                                           │
│   ┌──────────┐ ┌───────────┐ ┌───────────────┐           │
│   │PathPlanner│ │DrivingPl. │ │ArrivalPlanner │           │
│   │(NavMesh) │ │(кандид.)  │ │(precision)    │           │
│   └────┬─────┘ └─────┬─────┘ └───────┬───────┘           │
│        │              │               │                    │
│   ┌────┴──────────────┴───────────────┴────┐              │
│   │  ManeuverFeasibilityChecker + Scoring  │              │
│   └────────────────┬───────────────────────┘              │
│                    │                                       │
│   ┌────────────────┴──────────────────────┐               │
│   │  ManeuverPlanner → DriverFSM           │               │
│   │    ┌──────────────┐                   │               │
│   │    │ PursuitCtrl  │  (Forward)        │               │
│   │    │ ReversePurs. │  (Reverse)  v2.4  │               │
│   │    │ SpeedPlanner │                   │               │
│   │    └──────────────┘                   │               │
│   │    MotionController                    │               │
│   └────────────────┬──────────────────────┘               │
└────────────────────┼──────────────────────────────────────┘
                     │
                     ▼
┌──────────────────────────────────────────────────────────┐
│ VehicleSafetyController (последний фильтр)                 │
│   CommandSanitizer → DynamicsLimiter → StabilityLimiter    │
│   → AirborneProtection → RecoveryProtection               │
└────────────────────────┬─────────────────────────────────┘
                         │
                         ▼
┌──────────────────────────────────────────────────────────┐
│ VehicleCommand → VehicleBrain → WheeledMotor               │
│   Steer, Throttle, BrakeMode, HoldPosition, Phase          │
└──────────────────────────────────────────────────────────┘
```

---

## 3. Поток данных — от клика до колёс

### Обычный клик ПКМ (замена цели)
```
1. VehicleController.IssueMoveOrder(position)
2. → VehicleController.IssueMoveOrder(VehicleMoveGoal)
3.   → VehicleNavigation.SetDestination(goal)
4.     → m_OrderQueue.Clear()                    // очистить очередь
5.     → SetDestinationFromGoal(goal)
6.       → NavigationRequest.FromPosition(...)    // создать запрос
7.       → m_FSM.SetDestination(request)          // State = Driving
8.         → RebuildPlan()
9.           → PathPlanner.BuildPath(from, to)     // NavMesh или прямой
10.          → DrivingPlanner.BuildPlan()          // выбор режима
11.            → генерирует 3 кандидата (Forward/Reverse/TurnAround)
12.            → проверяет через FeasibilityChecker
13.            → оценивает через ScoringSystem (единый Score)
14.            → вызывает ArrivalPlanner (если близко к цели)
15.            → возвращает лучший план
16.          → ManeuverPlanner.BuildWaypoints()    // waypoints для не-Reverse манёвров
17.          → m_ReverseDriver = null  // v2.4: сброс состояния
18.        → ExecuteManeuver()
19.          → Если ReverseIntent:
20.            → ReverseDriver.TryStart(ctx, speedFraction)
21.              → ReversePathBuilder.Build(path, ctx)  // 5 Catmull-Rom точек
22.              → ReversePursuit.Reset()
23.            → ReverseDriver.Tick(dt)
24.              → ReversePursuit.Tick(ctx, path, speedFrac)
25.                → GetLookBehind(rearAxle, lookBehind) — точка на пути
26.                → κ = +2·crossTrack / L²  (v2.4: исправлен знак)
27.                → cross использует -forward (направление движения)
28.              → DrivingCommand() → VehicleCommand { Steer, Throttle(-) }
29.         → VehicleSafetyController.Apply()     // фильтр безопасности
30.           → VehicleBrain → WheeledMotor
```

### Shift+клик (добавление в очередь)
```
1. VehicleController.AppendMoveToQueue(pos, speed)
2. → VehicleNavigation.EnqueueOrder(order)
3.   → m_OrderQueue.Enqueue(order)               // в конец очереди
4.   → TryPromoteNextOrder()                     // если нет текущего
5.     → PromoteNext() → MarkStarted()
6.     → SetDestinationFromOrder() → FSM.SetDestination()
```

---

## 4. Часть 1: Система приказов и очередь

### 4.1 VehicleOrderType (enum)
`Assets\_Scripts\Vehicle\Navigation\VehicleOrderType.cs`

| Значение | Смысл |
|----------|-------|
| `Move` | Поехать в точку |
| `Stop` | Мягкая остановка, сохранить очередь |
| `Hold` | Держать позицию |
| `Park` | Приехать и встать по направлению |
| `Reverse` | Ехать задним ходом |
| `Repath` | Перестроить маршрут |
| `EmergencyStop` | Экстренная остановка, очистить всё |

### 4.2 OrderState (enum)
`Assets\_Scripts\Vehicle\Navigation\VehicleMoveOrder.cs`

```
Pending → Executing → Completed
            │
            ├── Aborted
            └── Expired
```

### 4.3 VehicleMoveOrder
`Assets\_Scripts\Vehicle\Navigation\VehicleMoveOrder.cs`

| Поле | Тип | Описание |
|------|-----|----------|
| `OrderId` | long | Уникальный номер (автоинкремент) |
| `ParentOrderId` | long? | Родительский приказ |
| `Type` | VehicleOrderType | Тип приказа |
| `State` | OrderState | Текущее состояние |
| `Destination` | Vector3 | Точка назначения |
| `DesiredHeadingYaw` | float | Желаемый угол корпуса |
| `FacingMode` | ArrivalFacingMode | Как встать в точке |
| `SpeedMode` | VehicleSpeedMode | Slow/Medium/Fast/Max |
| `AllowReverse` | bool | Разрешён задний ход |
| `AllowTurnAround` | bool | Разрешён разворот |
| `Priority` | int | Приоритет |
| `TimeoutSeconds` | float | Таймаут (0=бесконечно) |

### 4.4 VehicleOrderQueue
`Assets\_Scripts\Vehicle\Navigation\VehicleOrderQueue.cs`

- `Enqueue(order)` — EmergencyStop чистит всё, Stop сохраняет очередь
- `CancelAll(reason)` — очистить всё (Emergency)
- `PromoteNext(timeNow)` — завершить текущий, начать следующий

### 4.5 NavigationRequest
`Assets\_Scripts\Vehicle\Navigation\NavigationRequest.cs`

Иммутабельный struct. Планировщик работает только с ним.

| Поле | По умолчанию | Описание |
|------|-------------|----------|
| `Destination` | Vector3.zero | Точка назначения |
| `HeadingYaw` | null | Желаемый угол |
| `SpeedMode` | Medium | Режим скорости |
| `FacingMode` | None | Как встать |
| `AllowReverse` | true | Разрешён ли задний ход |
| `AllowTurnAround` | true | Разрешён ли разворот |
| `MinArrivalDistance` | 0.6f | Допуск расстояния |
| `MinArrivalHeading` | 8f | Допуск угла |

---

## 5. Часть 2: Heading-aware прибытие

### 5.1 ArrivalFacingMode (enum)
`Assets\_Scripts\Vehicle\Navigation\ArrivalFacingMode.cs`

| Значение | Поведение |
|----------|----------|
| `None` | Просто приехать. Fallback: ParkingManeuver если угол > 18° |
| `UsePathFacing` | Встать по направлению последнего сегмента NavMesh-пути |
| `FaceHeading` | Встать капотом по DesiredHeadingYaw |
| `KeepCurrent` | Не менять ориентацию |

### 5.2 ArrivalCriteria
`Assets\_Scripts\Vehicle\Navigation\ArrivalCriteria.cs`

| Поле | По умолчанию | Описание |
|------|-------------|----------|
| `PositionTolerance` | 0.6f | Допуск по расстоянию (м) |
| `HeadingToleranceDeg` | 8f | Допуск по углу (градусы) |
| `RequireFaceHeading` | false | Требуется ли выравнивание |
| `TargetForward` | Vector3 | Вектор желаемого направления |

### 5.3 ArrivalController
`Assets\_Scripts\Vehicle\Navigation\ArrivalController.cs`

Методы:
- `HasArrived(pos, yaw, dest, heading?)` — базовая проверка
- `HasArrived(FeedbackState, dest, ArrivalCriteria)` — расширенная
- `HasCorrectHeading(state, targetYaw, tolerance)` — проверка угла

---

## 6. Часть 3: Проверка выполнимости

### 6.1 FeasibilityResult + FeasibilitySeverity
`Assets\_Scripts\Vehicle\Navigation\FeasibilityResult.cs`

**v2.2 — градация вместо бинарного запрета:**

| Уровень | Смысл | Когда | Штраф в Score |
|---------|-------|------|---------------|
| `Valid` | Безопасно | Все проверки пройдены | +0 |
| `Risky` | Рискованно, но можно | Узкий проход, небольшой недобор зазора | +50 |
| `Unsafe` | Нежелательно | Зазор < порога, но не критично | +200 |
| `Impossible` | Невозможно | Обрыв, зазор < 50% порога | +999999 |

Фабрики: `Valid`, `Risky(risk, reason)`, `Unsafe(reason)`, `Impossible(reason)`.

### 6.2 ManeuverFeasibilityChecker
`Assets\_Scripts\Vehicle\Navigation\ManeuverFeasibilityChecker.cs`

**v2.2 — тирированные пороги:**
- **Forward:** drop → Impossible, clearance < 0.9м → Impossible, < 1.8м → Unsafe, narrow → Risky
- **TurnAround:** clearance < 0.35R → Impossible, < 0.7R → Unsafe

### 6.3 VehicleLocalGeometry (v2.2 — двойной луч)
`Assets\_Scripts\Vehicle\Navigation\VehicleLocalGeometry.cs`

**v2.4 — CRITICAL:** GeometryLayers обязан содержать слой "Ground" (6), иначе оба луча промахиваются → всё становится Impossible.
Проверка обрыва: два луча вперёд (0.5м и 1.5м от носа), оба на 5м вниз. ОБА должны не попасть → `HasDropAhead = true`.

---

## 7. Часть 4: Умный планировщик движения

### 7.1 DrivingPlanner (переработан v2.3–v2.4)
`Assets\_Scripts\Vehicle\Navigation\DrivingPlanner.cs`

Генерирует 3 кандидата:
1. **ForwardCandidate** — всегда
2. **ReverseCandidate** — если `AllowReverse` (v2.4: всегда создаётся, качество — через Scoring)
3. **TurnAroundCandidate** — если `AllowTurnAround`

**v2.4 — ключевые изменения:**
- `BuildReverseCandidate` → всегда `ReverseIntentManeuver` (не `ReverseManeuver`)
- `BuildTurnAroundCandidate` → без лишнего Reverse (был дубликат)
- `BuildForwardCandidate` → цель сзади (angle > 150°) штрафуется в ScoringSystem
- Reverse-кандидат создаётся всегда, не только когда `HasSafeBackingSpace`
- Расстояние Reverse до 15м дешевле TurnAround, дальше — дороже

### 7.2 DrivingPlan
`Assets\_Scripts\Vehicle\Navigation\DrivingPlan.cs`

| Поле | Описание |
|------|----------|
| `Maneuvers` | Список манёвров |
| `DrivingMode` | Forward/Reverse/TurnAround |
| `TotalCost` | Стоимость плана |
| `Feasibility` | Результат проверки |
| `EstimatedDistance` | Общая длина |
| `ReverseDistance` | Длина задним ходом |

### 7.3 ScoringSystem (v2.4)
`Assets\_Scripts\Vehicle\Navigation\ScoringSystem.cs`

**v2.4 изменения:**
- Цель сзади (angle > 150°): Forward получает +40 штраф
- TurnAround: +10 база + штраф за угол, только если цель спереди
- Reverse: +15 база, +20 если >15м (дороже TurnAround)
- Все кандидаты получают реальный Score; Impossible = +999999 естественно проигрывает

### 7.4 ArrivalPlanner
`Assets\_Scripts\Vehicle\Navigation\ArrivalPlanner.cs`

Вызывается из `DrivingPlanner.AppendArrivalManeuver()` когда dist < PlanningDistance.
Генерирует 5 стратегий (Direct, Arc, Reverse, Reposition, TurnAround), выбирает по стоимости.
**v2.4:** Reverse-стратегия гарантированно создаёт `ReverseIntentManeuver`.

---

## 8. Часть 5: Восстановление, стратегии, композиты

### 8.1 DriverRecovery (v2.4)
`Assets\_Scripts\Vehicle\Navigation\DriverRecovery.cs`

**v2.4 — SteeringSaturated fix:**
- Было: угол руля > 95% max в течение 2с → Recovery.ThreePointTurn → FAIL
- Стало: `&& _ctx.SpeedKmh < 2f` — насыщение руля только если машина СТОИТ
- При активном движении (>2 км/ч) насыщение руля — нормальное следование пути

RecoveryReasons: Stuck, SteeringSaturated, AngleTooLarge, PredictionUnsafe, PathBlocked.
RecoveryActions: StopAndReplan, TurnAround, ThreePointTurn, Abort, UnstuckRock, ReverseOut, RebuildPath, AbortAndStop.

### 8.2 RecoveryController + RecoveryStrategyRegistry
`Assets\_Scripts\Vehicle\Navigation\RecoveryController.cs`

Стратегии (по приоритету):
1. AbortIfTooManyAttemptsStrategy (P=1): ≥6 попыток → AbortAndStop
2. RebuildPathAfterAttemptsStrategy (P=2): ≥4 попыток → RebuildPath
3. ReverseOutStrategy (P=3): спереди блок + сзади свободно → ReverseOut
4. UnstuckRockStrategy (P=10): IsStuck → UnstuckRock

---

## 9. Часть 6: Физический слой и фазы движения

### 9.1 DrivingPhase (enum)
`Assets\CombatVehicleSystem\Scripts\Core\VehicleCommand.cs`

| Значение | Когда |
|----------|------|
| `Cruise` | Обычное движение |
| `Precision` | Точное маневрирование (задний ход, развороты) |
| `Parking` | Парковка, подъезд с heading |
| `Recovery` | Выход из застревания |

### 9.2 PursuitController (чистое преследование)
`Assets\_Scripts\Vehicle\Navigation\PursuitController.cs`

```
Tick(ctx, maneuver, speedFrac, topSpeed, lookAhead, override?)
  1. ComputeLookAhead(speed, base) → dynamic lookahead
  2. FindNearestWaypointIndex(waypoints, pos)
  3. FindLookAheadIndex(waypoints, nearest, pos, lookAhead)
  4. Pure pursuit: κ = 2·crossTrack / L²
  5. Clamp curvature (distance-based + speed-based)
  6. AdaptiveLookAhead: detect oscillation
  7. Speed = capKmh * min(curvatureFraction, arrivalScale) * launchRamp
  8. Precision mode: distToEnd < 2m → tighter control
```

Используется для Forward, TurnAround, Parking. **Не используется для Reverse** — у него свой ReversePursuit.

### 9.3 MotionController
`Assets\_Scripts\Vehicle\Navigation\MotionController.cs`

- `Convert()` → VehicleCommand с Phase
- `HoldInPlace()` — активное удержание (Brake=Soft, HoldPosition=true)
- `ResolvePhase()` — dist < 3м + arrival → Phase=Parking

### 9.4 DriverFSM (v2.4)
`Assets\_Scripts\Vehicle\Navigation\DriverFSM.cs`

Конечный автомат: Idle → Driving → Arrival → Holding. Recovery/EmergencyStop — временные.
**v2.4 ключевые изменения:**
- `RebuildPlan()`: `m_ReverseDriver = null` — сброс состояния ReverseDriver
- `ExecuteReverseManeuver()`: speedFraction = Mathf.Max(Fraction, 0.6f) — минимум 60% от max
- Reverse FAILED → `m_PlanDirty = true` → полное перестроение плана (раньше переходил к Parking)

---

## 10. Часть 7: Vehicle Safety System

**Принцип:** последний фильтр перед физикой. Не меняет планы, только не даёт отправить опасную команду.

### 10.1 Архитектура
```
MotionController.Convert() → сырая команда
    │
    ▼
VehicleSafetyController.Apply()
    ├── 1. CommandSanitizer        (логика)
    ├── 2. DynamicsLimiter         (RollLimit + SteeringRate)
    ├── 3. StabilityLimiter        (WheelLift + Slip + RollAngle + Pitch)
    ├── 4. AirborneProtection      (в воздухе)
    └── 5. RecoveryProtection      (recovery в опасности)
```

### 10.2 Лимитеры

| Лимитер | Порог | Эффект |
|---------|-------|--------|
| RollLimiter | a > 6 м/с² | throttle снижается |
| SteeringRateLimiter | Δsteer ≤ 1.2/сек | гасит дёрганье |
| WheelLift | ≥2 колёс борта в воздухе | газ×0.3, руль×0.5 |
| SlipProtection | sidewaysSlip > 0.15 | газ×0.5; >0.3 → газ=0 |
| RollAngleMonitor | 20°/25°/35° | газ×0.5 / BrakeSoft / HardBrake |
| PitchProtection | 20°/30° | газ×0.5 / BrakeSoft |
| AirborneProtection | в воздухе | газ=0, тормоз=0 |

---

## 11. Часть 8: Precision Arrival System

**Принцип:** включается только у цели (< PlanningDistance). 5 стратегий → выбор по стоимости.

### 11.1 ArrivalPlanningSettings
`Assets\_Scripts\Vehicle\Navigation\ArrivalPlanningSettings.cs`

- `PlanningDistance` = max(4R, 6м)
- `PreGoalDistance` = max(0.4R, 2м)
- `RepositionStep` = max(0.55R, 1.5м)

### 11.2 Стратегии (5 штук)

| Стратегия | Когда | Манёвры |
|-----------|------|---------|
| DirectArrival | Цель прямо, dist < planningDist | ApproachWithHeading / Parking |
| ArcArrival | Угол 15°–120°, цель спереди | TurnAround + Arrival |
| ReverseArrival | Цель сзади | ReverseIntent + Arrival |
| RepositionArrival | Отъезд назад + заезд | Reverse + Arrival |
| TurnAroundArrival | Полный разворот | TurnAround + Forward + Arrival |

### 11.3 ArrivalCostEvaluator (v2.3)
`cost = distance*1.0 + headingError*0.3 + lateral*3.0 + reverse*12 + maneuvers*6 + precision*2`

---

## 12. Часть 9: SubSystem Reverse — задний ход

**v2.4 — полностью переработанная подсистема заднего хода.**

### 12.1 Архитектура Reverse SubSystem

```
ReverseIntentManeuver (манёвр в плане)
    │
    ▼
DriverFSM.ExecuteReverseManeuver()
    │
    ├── m_ReverseDriver = new ReverseDriver()   // v2.4: сбрасывается в RebuildPlan
    ├── ReverseDriver.Configure(curves, prediction)
    └── ReverseDriver.TryStart(ctx, speedFraction)
          │
          ├── ReversePathBuilder.Build(navMeshPath, ctx)
          │     └── Catmull-Rom сглаживание (4 подточки на сегмент)
          │     └── 5+ точек с tangent, curvature, distance
          │
          └── ReverseDriver.Tick(dt)
                │
                ├── ReversePath.Advance(rearAxle)  // продвижение сегмента
                ├── ReverseStateMachine.Tick()      // Enter → Reverse → SlowDown → Stop → Finished
                │
                ├── ReversePursuit.Tick(ctx, path, speedFrac)
                │     ├── GetLookBehind(rearAxle, lookBehind)  // точка на пути
                │     ├── travelDir = -forward  (v2.4: направление движения!)
                │     ├── cross = Cross(travelDir, toTargetDir).y
                │     ├── κ = +2 * cross * dist / L²  (v2.4: исправлен знак!)
                │     ├── ReverseSpeedPlanner.Compute()  // желаемая скорость
                │     └── return { curvature, speed, distanceToEnd }
                │
                ├── DriverRecovery.Evaluate()  // Stuck, SteeringSaturated, etc.
                │     └── SteeringSaturated: только если SpeedKmh < 2  (v2.4)
                │
                └── DrivingCommand()
                      ├── steer = Atan(wheelBase * curvature) / maxSteerAngle
                      ├── throttle = PID(speedError) * -1  // отрицательный газ!
                      └── return VehicleCommand { Steer, Throttle(-), Brake }
```

### 12.2 ReversePathBuilder
`Assets\_Scripts\Vehicle\Navigation\Reverse\ReversePathBuilder.cs`

Берёт PathResult (NavMesh-углы, 2+ точек) → Catmull-Rom сглаживание (4 субдивизии на сегмент) → ReversePath с:
- `Points`: List<PathPoint> — позиция, tangent, curvature, DistanceFromStart
- `TotalLength`: общая длина
- `CurrentSegment`: текущий сегмент

### 12.3 ReversePath
`Assets\_Scripts\Vehicle\Navigation\Reverse\ReversePath.cs`

Методы:
- `GetLookBehind(rearAxle, dist)` — точка на пути впереди от rearAxle на `dist` метров
- `Advance(rearAxle)` — продвинуть CurrentSegment когда rearAxle пересёк плоскость через nextPt
- `ClosestPointOnPath(pos)` — проекция точки на путь
- `CurvatureAt(seg)` — кривизна в сегменте
- `RemainingDistance` — оставшееся расстояние от CurrentSegment до конца

### 12.4 ReversePursuit (v2.4 — исправленная формула)
`Assets\_Scripts\Vehicle\Navigation\Reverse\ReversePursuit.cs`

**v2.4 CRITICAL FIX — знак кривизны:**

```csharp
// БЫЛО (неправильно):
float cross = Vector3.Cross(_ctx.Forward, toTargetDir).y;
curvature = -2f * crossTrack / (lookBehind * lookBehind);
// forward машины — НЕ направление движения при reverse!
// Лишний минус инвертировал направление руля.

// СТАЛО (v2.4):
Vector3 travelDir = -_ctx.Forward;  // направление ДВИЖЕНИЯ = противоположно forward
float cross = Vector3.Cross(travelDir, toTargetDir).y;
curvature = 2f * crossTrack / (lookBehind * lookBehind);
// Стандартная формула pure pursuit: κ = 2·cross / L²
```

**Скорость:**
- `ComputeLookBehind(speed)` → clamp(3 + speed*0.25, 2, 8)
- `ReverseSpeedPlanner.Compute(fraction, maxReverseSpeed, currentSpeed, curvature, previewCurv, distToEnd)`

### 12.5 ReverseDriver
`Assets\_Scripts\Vehicle\Navigation\Reverse\ReverseDriver.cs`

Владеет: ReversePath, ReversePursuit, ReverseStateMachine, SteeringLimiter, DriverRecovery.

**v2.4 — сброс состояния:**
- `m_ReverseDriver = null` в `RebuildPlan()` — предотвращает переиспользование Finished-драйвера
- `is null → new → Configure → TryStart` — свежий драйвер для каждого ReverseIntentManeuver

**v2.4 — скорость:**
- `speedFraction = Mathf.Max(VehicleSpeedModeUtil.Fraction(mode), 0.6f)`
- Было: Fraction(Medium) 0.55 × SpeedScale 0.45 = 0.2475 (~2 км/ч)
- Стало: max(0.55, 0.6) = 0.6 от max (~18 км/ч)

### 12.6 ReverseStateMachine
`Assets\_Scripts\Vehicle\Navigation\Reverse\ReverseState.cs`

Состояния: Enter → Reverse → SlowDown → Stop → Finished / Failed.
- **SlowDown:** remaining < 30% от total → мягкое торможение
- **Stop:** подход к концу → жёсткое торможение
- **Finished:** финальный Brake=Hard

### 12.7 ReverseSteeringLimiter
`Assets\_Scripts\Vehicle\Navigation\Reverse\ReverseSteeringLimiter.cs`

Ограничивает угол руля в зависимости от скорости (на высоких скоростях меньше угол).

---

## 13. Манёвры — полный справочник

### Базовый класс Maneuver
`Assets\_Scripts\Vehicle\Navigation\Maneuver.cs`

### Типы (VehicleManeuverType)

| Тип | Класс | Когда | SpeedScale | AllowReverse | v2.4 |
|-----|-------|------|------------|-------------|------|
| Forward | ForwardManeuver | Основной режим | 1.0 | false | — |
| Reverse | ReverseManeuver | Устаревший | 1.0 | true | Не используется |
| **ReverseIntent** | **ReverseIntentManeuver** | **Полный задний ход v2.4** | 0.45 | true | Использует ReverseDriver |
| TurnAround | TurnAroundManeuver | Разворот 180° | 0.70 | false | Без дубликата Reverse |
| Parking | ParkingManeuver | Парковка + heading | 0.22 | true | — |
| ApproachWithHeading | ApproachWithHeadingManeuver | Подъезд с heading | 0.28 | true | — |
| Unstuck | UnstuckManeuver | Раскачка | — | false | — |

### ReverseIntentManeuver
`Assets\_Scripts\Vehicle\Navigation\Reverse\ReverseIntentManeuver.cs`

**v2.4 — ключевое отличие:** не использует ManeuverPlanner для waypoints.
Вейпоинты управляются исключительно ReverseDriver через ReversePath.
PURSUIT (PursuitController) для ReverseIntent не вызывается — используется ReversePursuit.

---

## 14. Логирование — все теги и сообщения

### [RevDriver] (v2.4)
```
[RevDriver] TryStart path.valid=True points=5 length=6.3m ctx=True
[RevDriver] state=Reverse seg=1/5 remaining=5.0m thr=-0.30 steer=0.26 brk=None speed=1.6km/h
[RevDriver] state=SlowDown seg=3/5 remaining=1.3m thr=-0.02 steer=0.87 brk=None speed=2.5km/h
[RevDriver] TICK state=Finished → FinalCommand
[RevDriver] RECOVERY reason=SteeringSaturated action=ThreePointTurn → FAIL
```

### [RevState] (v2.4)
```
[RevState] Reverse→SlowDown: remaining=1.33m < slowdown=1.96m total=6.3m seg=3/5
```

### [DrivingPlanner]
```
[DrivingPlanner] candidates: Forward (always), Reverse=True (available), TurnAround=True (ok), angle=177° dist=6.3m proposed=Reverse
[DrivingPlanner]   Forward: cost=56.6 severity=Valid 
[DrivingPlanner]   Reverse: cost=14.6 severity=Valid 
[DrivingPlanner]   TurnAround: cost=27.1 severity=Valid 
[DrivingPlanner] => CHOSE Reverse cost=14.6 severities=[Forward=Valid Reverse=Valid TurnAround=Valid]
```

### [ArrivalPlanner]
```
[ArrivalPlanner] dist=6.3m angle=-177° lateral=0.3m front=False deadZone=False turnR=5.6
[ArrivalPlanner]   Direct: SKIP (not valid)
[ArrivalPlanner]   Reverse: cost=59.1 maneuvers=2
[ArrivalPlanner] => CHOSE Reverse cost=59.1
```

### [DriverFSM]
```
[DriverFSM] RebuildPlan: mode=Reverse maneuvers=[[0]Reverse [1]Parking ] cost=14.6 dist=6.3m rev=6.3m risk=0.00 reason=...
[DriverFSM] Reverse FAILED — triggering full replan
```

### [Recovery]
```
[RevDriver] RECOVERY reason=SteeringSaturated action=ThreePointTurn → FAIL
```

### [PathPlanner]
```
[PathPlanner] NavMesh failed, using direct fallback [(89,0,18) → (89,0,12)] points=2 fromNav=False toNav=False
```

---

## 15. Тестовый полигон

Меню: **Polygone → Vehicles → Build NAVIGATION Test Track**

Полигон справа от сцены (X=80, Z=5), длина ~370м, 10 секций.

| Секция | Название | Длина | Описание |
|--------|---------|------|----------|
| START | Старт | — | Зелёный столб |
| 1 | Slalom | ~72м | 8 конусов через 9м |
| 2 | Narrow passage | 30м | Стены 5м шириной |
| 3 | Sharp 90° right | 16м | Поворот вправо |
| 4 | Obstacle | 18м | Объезд справа |
| 5 | Ramp / Plateau | 54м | Подъём + плато + спуск |
| 6 | Side slope | 22м | Боковой уклон |
| 7 | Drop edge | 10м | Платформа с краем |
| 8 | Reverse target | 11м | Цель сзади |
| 9 | Heading arrival | 11м | Синий маркер + стрелка |
| 10 | Waypoint chain | 45м | 4 маркера для Shift+клик |
| FINISH | Финиш | — | Красный столб |

Параметры: Ground 80×380м, полоса 14м, камера Y=25.

---

## 16. Полный список файлов

### Reverse SubSystem (v2.4 — новые)

| Файл | Назначение |
|------|-----------|
| `Navigation/Reverse/ReverseIntentManeuver.cs` | Манёвр заднего хода (не использует ManeuverPlanner) |
| `Navigation/Reverse/ReverseDriver.cs` | Оркестратор заднего хода |
| `Navigation/Reverse/ReversePath.cs` | Путь заднего хода (PathPoint + GetLookBehind + Advance) |
| `Navigation/Reverse/ReversePathBuilder.cs` | Catmull-Rom строитель ReversePath |
| `Navigation/Reverse/ReversePursuit.cs` | Pure pursuit для заднего хода (travelDir = -forward) |
| `Navigation/Reverse/ReverseState.cs` | State machine: Enter→Reverse→SlowDown→Stop→Finished |
| `Navigation/Reverse/ReverseSpeedPlanner.cs` | Планировщик скорости заднего хода |
| `Navigation/Reverse/ReverseSteeringLimiter.cs` | Ограничитель угла руля |
| `Navigation/Reverse/ReverseDebugger.cs` | Отладчик (линии пути) |

### Основные файлы навигации

| Файл | Часть |
|------|-------|
| `Navigation/VehicleNavigation.cs` | Владелец всех подсистем |
| `Navigation/DriverFSM.cs` | Конечный автомат (Idle/Driving/Arrival/Holding) |
| `Navigation/PathPlanner.cs` | NavMesh + прямой fallback |
| `Navigation/PathResult.cs` | Результат построения пути |
| `Navigation/DrivingPlanner.cs` | Генератор кандидатов и выбор плана |
| `Navigation/DrivingPlan.cs` | План (манёвры + cost + feasibility) |
| `Navigation/ScoringSystem.cs` | Оценка кандидатов |
| `Navigation/ArrivalPlanner.cs` | Precision Arrival оркестратор |
| `Navigation/ArrivalController.cs` | Проверка прибытия |
| `Navigation/ArrivalCostEvaluator.cs` | Стоимость стратегий |
| `Navigation/ManeuverPlanner.cs` | Построитель waypoints |
| `Navigation/Maneuver.cs` | Базовый класс манёвра |
| `Navigation/PursuitController.cs` | Чистое преследование (для Forward/TurnAround) |
| `Navigation/MotionController.cs` | Преобразование в VehicleCommand |
| `Navigation/Speed/SpeedPlanner.cs` | Планировщик скорости |
| `Navigation/FeasibilityResult.cs` | Вердикт проверки (+ FeasibilitySeverity) |
| `Navigation/ManeuverFeasibilityChecker.cs` | Проверщик выполнимости |
| `Navigation/VehicleLocalGeometry.cs` | Геометрия (лучи, зазоры, обрывы) |
| `Navigation/TrajectoryPrediction.cs` | Предикшн траектории |
| `Navigation/DriverRecovery.cs` | Детектор проблем (Stuck, SteeringSaturated, etc.) |
| `Navigation/RecoveryController.cs` | Recovery оркестратор |
| `Navigation/VehicleDriverMemory.cs` | Память (счётчики попыток) |
| `Navigation/VehicleOrderQueue.cs` | Очередь приказов |
| `Navigation/VehicleMoveOrder.cs` | Приказ |
| `Navigation/NavigationRequest.cs` | Навигационный запрос |
| `Navigation/DriverContext.cs` | Контекст водителя |
| `Navigation/VehicleNavigationDebugDrawer.cs` | Отладчик |
| `Navigation/Safety/VehicleSafetyController.cs` | Диспетчер безопасности |
| `Navigation/Safety/DynamicsLimiter.cs` | RollLimit + SteeringRate |
| `Navigation/Safety/StabilityLimiter.cs` | WheelLift + Slip + Roll + Pitch |
| `Navigation/Safety/AirborneProtection.cs` | Защита в воздухе |
| `Navigation/Safety/RecoveryProtection.cs` | Защита recovery |
| `Navigation/Safety/CommandSanitizer.cs` | Логическая проверка |
| `Editor/VehicleNavTestSceneSetup.cs` | Сборка тестового полигона |

---

## 17. Словарь терминов

| Термин | Определение |
|--------|-----------|
| **Order** | Приказ — высокоуровневая команда (Move, Stop, Hold, Park...) |
| **Request** | Навигационный запрос — иммутабельный struct |
| **Path** | Путь — результат PathPlanner (NavMesh-углы или прямой fallback) |
| **Plan** | План — результат DrivingPlanner (список манёвров + cost) |
| **Maneuver** | Манёвр — атомарная единица плана (Forward, ReverseIntent, TurnAround...) |
| **Candidate** | Кандидат — вариант плана (Forward/Reverse/TurnAround) |
| **Feasibility** | Выполнимость — можно ли физически проехать |
| **Severity** | Градация риска: Valid / Risky / Unsafe / Impossible |
| **Pursuit** | Чистое преследование — κ = 2·crossTrack / L² |
| **ReversePursuit** | v2.4 — отдельный pursuit для заднего хода (travelDir = -forward) |
| **ReverseDriver** | v2.4 — оркестратор заднего хода |
| **ReversePath** | v2.4 — Catmull-Rom путь заднего хода |
| **Curvature** | Кривизна (κ) — обратный радиус поворота, 1/м |
| **LookBehind** | v2.4 — дистанция упреждения для ReversePursuit |
| **FSM** | Конечный автомат водителя — DriverFSM |
| **Safety** | Безопасность — последний фильтр перед физикой |
| **Phase** | Фаза движения — Cruise / Precision / Parking / Recovery |

---

## 18. Исправления v2.3 — TargetSide, GoalLocked, жёсткий Arrival

### 18.1 FeasibilitySeverity — градация вместо бинарного запрета
- `enum FeasibilitySeverity { Valid, Risky, Unsafe, Impossible }`
- ScoringSystem.ApplyRiskPenalty: Impossible+999999, Unsafe+200, Risky+50, Valid+0
- Убран fallback-блок в DrivingPlanner; выбор всегда по минимальному Cost

### 18.2 Тирированные пороги в FeasibilityChecker
- Forward: drop→Impossible, clearance<0.9м→Impossible, <1.8м→Unsafe
- TurnAround: clearance<0.35R→Impossible, <0.7R→Unsafe

### 18.3 Двойной луч обрыва
- VehicleLocalGeometry: два луча (0.5м и 1.5м), оба 5м вниз, оба должны промазать

### 18.4 Промежуточные waypoints для прямых путей
- PathPlanner.BuildDirectPath: >10м → точки каждые 5м

### 18.5 Жёсткий Arrival + штраф за боковое смещение
- TickArrival: BrakeToStop(true), crawl-guard при dist<1.5м и speed>5
- ArrivalCostEvaluator: Weight_Lateral=3.0, Weight_Reverse=12, Weight_Maneuvers=6

---

## 19. Исправления v2.4 — Reverse Overhaul + стабильность

### 19.1 GeometryLayers — критическое исправление
**Проблема:** слой "Ground" (6) отсутствовал в `GeometryLayers` → лучи обрыва никогда не попадали в землю → все кандидаты получали HasDrop = true → Impossible → машина отказывалась ехать вперёд/назад.
**Решение:** добавлен `1 << 6` (Ground) в GeometryLayers.

### 19.2 Reverse — всегда ReverseIntentManeuver
**Проблема:** старый `ReverseManeuver` создавал пустые waypoints → PURSUIT показывал 0 точек → машина не знала куда рулить.
**Решение:** `ReverseIntentManeuver` использует ReverseDriver/ReversePath/ReversePursuit, не зависит от ManeuverPlanner.

### 19.3 ReversePursuit — исправлен знак кривизны
**Проблема:** `Cross(_ctx.Forward, toTargetDir).y` — использовал forward машины, не направление движения. При езде назад направление = -forward. Плюс лишний минус в формуле `-2f * crossTrack`.
**Решение:** `travelDir = -_ctx.Forward`, `curvature = +2f * crossTrack / L²`. Без этого машина рулила ОТ целевой точки вместо К ней.

### 19.4 SteeringSaturated recovery — ложное срабатывание
**Проблема:** при reverse с латеральным смещением цели угол руля >95% max — нормально. Через 2с Recovery видел SteeringSaturated → ThreePointTurn → FAIL → replan.
**Решение:** условие `&& _ctx.SpeedKmh < 2f`. Если машина едет — насыщение руля это норма.

### 19.5 ReverseDriver — сброс состояния при RebuildPlan
**Проблема:** старый ReverseDriver (Finished) переиспользовался → мгновенно возвращал FinalCommand → FSM пропускал Reverse → переходил к Parking → второй клик игнорировался.
**Решение:** `m_ReverseDriver = null` в RebuildPlan, свежий драйвер для каждого ReverseIntentManeuver.

### 19.6 Reverse — скорость увеличена
**Проблема:** скорость заднего хода 0.2475 от max (Fraction 0.55 × SpeedScale 0.45) → ~2 км/ч → порог застревания (1.2 км/ч) → вечный Recovery.
**Решение:** `speedFraction = Mathf.Max(Fraction, 0.6f)` → минимум 60% от max (~18 км/ч).

### 19.7 Reverse FAILED → полное перестроение плана
**Проблема:** при FAILED Reverse FSM продвигался к следующему манёвру (Parking) вместо перестроения.
**Решение:** `m_PlanDirty = true` → RebuildPlan с новыми кандидатами.

### 19.8 Forward — штраф при цели сзади
**Проблема:** Forward был дешевле Reverse даже при цели сзади (angle > 150°).
**Решение:** `if (angle > 150°) forwardCost += 40`.

### 19.9 TurnAround — без лишнего Reverse
**Проблема:** BuildTurnAroundCandidate добавлял Reverse + TurnAround + Forward → 4 манёвра вместо 3.
**Решение:** убран дублирующий Reverse.

### 19.10 Reverse — дистанционно-зависимый скоринг
**Проблема:** Reverse всегда был дешевле TurnAround, даже на 30м.
**Решение:** до 15м Reverse дешевле, дальше получает +20 штраф → TurnAround выигрывает.

### 19.11 ScoringSystem — дубликат return
**Проблема:** два `return` подряд после `if/else` → второй никогда не выполнялся.
**Решение:** удалён дублирующий return.

---

## 20. Известные проблемы (TODO)

### 20.1 PURSUIT debug показывает 0 waypoints для Reverse
**Причина:** `VehicleNavigationDebugDrawer` читает `ctx.CurrentManeuver.Waypoints` — у ReverseIntentManeuver они пусты (управляется через ReverseDriver). PURSUIT debug показывает 0 точек/старые точки Parking.
**Влияние:** только косметическое — отладка. На реальное вождение не влияет (ReversePursuit работает от ReversePath).
**Исправление:** нужно добавить отдельный debug-вывод для ReverseIntent (ReverseDebugger уже есть, но не подключён к периодическому логу).

### 20.2 После Reverse → Parking, машина стоит на месте
**Причина:** Parking-манёвр после Reverse использует PURSUIT с waypoints от ManeuverPlanner. Waypoints строятся от СТАРТОВОЙ позиции плана, а не от текущей. PURSUIT показывает `rev=True` с desired speed -6.8 км/ч, но throttle=0 — машина не может развернуться.
**Влияние:** машина останавливается в 1.5м от цели после успешного Reverse. Прибытие не завершается.
**Исправление:** Parking после Reverse нужно либо заменить на прямую остановку, либо перестраивать waypoints от текущей позиции.

### 20.3 NavMesh всегда недоступен на тестовом полигоне
**Причина:** полигон создаётся в runtime, NavMesh не запекается автоматически.
**Влияние:** всегда используется прямой fallback (2 точки). Catmull-Rom сглаживание даёт прямую линию.
**Исправление:** добавить авто-запекание NavMesh после сборки полигона, или предзапечь NavMesh на Ground.

### 20.4 ReversePath не обновляет waypoints в Maneuver.PlanSegment
**Причина:** ReversePath управляется ReverseDriver отдельно, waypoints манёвра не синхронизируются.
**Влияние:** отладчик показывает неактуальные waypoints.
**Исправление:** перенести waypoints из ReversePath в PlanSegment после TryStart.

### 20.5 Reverse с латеральным смещением требует большой угол руля
**Причина:** если цель сзади и сбоку, PURSUIT требует значительной кривизны для выхода на прямую → угол руля высокий.
**Влияние:** v2.4 SteeringSaturated recovery больше не срабатывает ложно (добавлена проверка SpeedKmh<2). Но визуально машина едет с повёрнутыми колёсами.
**Исправление:** можно добавить Align-фазу перед Reverse (развернуться к цели кормой, потом ехать прямо).

