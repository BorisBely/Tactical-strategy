# Vehicle Navigation System v2.1
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
11. [Манёвры — полный справочник](#12-манёвры--полный-справочник)
12. [Логирование — все теги и сообщения](#13-логирование--все-теги-и-сообщения)
13. [Тестовый полигон](#14-тестовый-полигон)
14. [Полный список файлов](#15-полный-список-файлов)
15. [Словарь терминов](#16-словарь-терминов)

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
│   │    PursuitController + SpeedPlanner    │               │
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
11.            → генерирует 2-3 кандидата
12.            → проверяет через FeasibilityChecker
13.            → оценивает через ScoringSystem
14.            → вызывает ArrivalPlanner (если близко)
15.            → возвращает лучший план
16.          → ManeuverPlanner.BuildWaypoints()    // waypoints
17.        → ExecuteManeuver()
18.          → PursuitController.Tick()           // curvature + speed
19.          → SpeedPlanner.ComputeTargetSpeed()   // лимиты
20.          → MotionController.Convert()          // → VehicleCommand
21.          → VehicleSafetyController.Apply()     // фильтр безопасности
22.            → VehicleBrain → WheeledMotor
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

### Авто-продвижение очереди
```
1. FSM достигает Idle/Holding
2. → TryPromoteNextOrder()
3.   → MarkCurrentOrderCompleted()
4.   → PromoteNext() → MarkStarted()
5.   → SetDestinationFromOrder(next) → новый FSM.Driving
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
| `HasDestination` | bool | Задана ли точка |
| `DesiredHeadingYaw` | float | Желаемый угол корпуса |
| `HasDesiredHeading` | bool | Задан ли угол |
| `FacingMode` | ArrivalFacingMode | Как встать в точке |
| `SpeedMode` | VehicleSpeedMode | Slow/Medium/Fast/Max |
| `AllowReverse` | bool | Разрешён задний ход |
| `AllowTurnAround` | bool | Разрешён разворот |
| `AllowThreePointTurn` | bool | Разрешён трёхточечный |
| `Priority` | int | Приоритет |
| `IsCancelable` | bool | Можно ли отменить |
| `TimeoutSeconds` | float | Таймаут (0=бесконечно) |
| `CreatedTime` | float | Время создания |
| `SourceTag` | string | Кто создал (user/emergency/legacy) |

Фабрики: `CreateMove(pos, speed)`, `CreateMove(pos, heading, speed)`, `CreateStop()`, `CreateHold()`, `CreateEmergencyStop()`, `FromMoveGoal(goal)`.

### 4.4 VehicleOrderQueue
`Assets\_Scripts\Vehicle\Navigation\VehicleOrderQueue.cs`

Свойства: `Count`, `HasCurrent`, `HasPendingInterrupt`, `CurrentOrder`, `QueuedOrders`.

Методы:
- `Enqueue(order)` — EmergencyStop чистит всё, Stop сохраняет очередь, Move мержит близкие точки
- `EnqueueFront(order)` — вставить в начало
- `CancelAll(reason)` — очистить всё (Emergency)
- `CancelCurrent(reason)` — прервать текущий (Stop, сохраняет очередь)
- `PromoteNext(timeNow)` — завершить текущий, начать следующий
- `TryPromoteInterrupt(timeNow)` — продвинуть прерывание
- `MarkCurrentOrderStarted/Completed/Aborted()`
- `RemoveExpiredOrders(timeNow)` — удалить просроченные

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
| `AllowRepath` | true | Разрешено ли перестроение |
| `MinArrivalDistance` | 0.6f | Допуск расстояния |
| `MinArrivalHeading` | 8f | Допуск угла |

Фабрики: `FromPosition()`, `FromPositionAndHeading()`, `FromOrder(order)`.

---

## 5. Часть 2: Heading-aware прибытие

### 5.1 ArrivalFacingMode (enum)
`Assets\_Scripts\Vehicle\Navigation\ArrivalFacingMode.cs`

| Значение | Поведение |
|----------|----------|
| `None` | Просто приехать. Старый fallback: ParkingManeuver если угол > 18° |
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
| `HeadingBlendStartDistance` | 6f | С какого расстояния подмешивать curvature |
| `HeadingBlendMaxSpeedKmh` | 5f | Макс. скорость при выравнивании |
| `TargetForward` | Vector3 | Вектор желаемого направления |
| `HasTargetForward` | bool | Задан ли вектор |

Фабрика: `FromRequest(NavigationRequest)`.

### 5.3 Как работает прибытие с heading
```
1. ПКМ+тянуть → стрелка → FacingMode = FaceHeading
2. DrivingPlanner.AppendArrivalManeuver() → ApproachWithHeadingManeuver
3. ManeuverPlanner → waypoints с финальным прямым участком по heading
4. PursuitController ведёт по waypoints
5. При dist < 6м: подмешивается curvature для выравнивания
6. При dist < tolerance И angle < tolerance → Holding → HoldInPlace()
```

### 5.4 ArrivalController (расширен)
`Assets\_Scripts\Vehicle\Navigation\ArrivalController.cs`

Методы:
- `HasArrived(pos, yaw, dest, heading?)` — старый, совместимость
- `HasArrived(FeedbackState, dest, ArrivalCriteria)` — новый
- `HasCorrectHeading(state, targetYaw, tolerance)` — проверка угла
- `IsFacingDestination(state, forward)` — смотрит ли на цель

---

## 6. Часть 3: Проверка выполнимости

### 6.1 FeasibilityResult
`Assets\_Scripts\Vehicle\Navigation\FeasibilityResult.cs`

| Поле | Тип | Описание |
|------|-----|----------|
| `IsValid` | bool | Можно ли выполнить вообще |
| `IsFullySafe` | bool | Полностью безопасно |
| `MinClearance` | float | Минимальный зазор (м) |
| `RiskScore` | float | 0=безопасно, 1+=рискованно |
| `HasFrontCollision` | bool | Столкновение спереди |
| `HasRearCollision` | bool | Столкновение сзади |
| `HasSideCollision` | bool | Боковое столкновение |
| `HasCliffRisk` | bool | Риск падения с обрыва |
| `HasSlopeRisk` | bool | Слишком крутой склон |
| `HasNarrowPassage` | bool | Узкий проход |
| `FailureReason` | string | Причина отказа |
| `FailurePoint` | Vector3 | Точка проблемы |
| `RecommendedMaxSpeedKmh` | float | Безопасная скорость |

Фабрики: `Valid`, `Invalid(reason)`, `Invalid(reason, point)`.

### 6.2 ManeuverFeasibilityChecker
`Assets\_Scripts\Vehicle\Navigation\ManeuverFeasibilityChecker.cs`

Проверяет манёвр ДО исполнения. Константы: мин. зазор спереди 1.8м, сзади 1.8м, сбоку 1.0м, коридор 2.5м.

Методы:
- `CheckPlan(plan, ctx, params)` / `CheckPlan(plan, geometry, turnRadius)`
- `CheckForwardPath(geo)` — обрыв? зазор? узко?
- `CheckReversePath(geo)` — обрыв? зазор? коридор?
- `CheckTurnAroundArc(sign, radius, geo)` — диагонали + CanFitTurnRadius
- `CheckParkingSpot(dest, yaw, geo)` — обрыв? тесно?

### 6.3 VehicleLocalGeometry (расширен)
`Assets\_Scripts\Vehicle\Navigation\VehicleLocalGeometry.cs`

Добавлены в Sample: `FrontDiagonalLeftClearance`, `FrontDiagonalRightClearance`, `RearDiagonalLeftClearance`, `RearDiagonalRightClearance`, `HasDropAhead`, `HasDropBehind`, `HasNarrowPassage`.

Новые методы: `CanFitTurnRadius(radius, geo)`, `HasSafeBackingSpace(geo, min)`, `HasSafeForwardSpace(geo, min)`.

### 6.4 TrajectoryPrediction (расширен)
`Assets\_Scripts\Vehicle\Navigation\TrajectoryPrediction.cs`

PredictionResult: + `RiskScore`, `CollisionPoint`, `CollisionStepIndex`.

Новые методы: `PredictForManeuver()`, `PredictForward()`, `PredictReverse()`, `PredictTurnAround()`.

---

## 7. Часть 4: Умный планировщик движения

### 7.1 PathBuildOptions
`Assets\_Scripts\Vehicle\Navigation\PathBuildOptions.cs`

| Поле | По умолчанию | Описание |
|------|-------------|----------|
| `AllowPartialPath` | true | Разрешить частичный NavMesh-путь |
| `AllowDirectFallback` | true | Разрешить прямой fallback |
| `SampleRadiusFrom` | 3f | Радиус поиска NavMesh от начала |
| `SampleRadiusTo` | 4f | Радиус поиска NavMesh до цели |

Статические пресеты: `Default`, `SafeOnly`, `ForReverse`.

### 7.2 PathResult (расширен)
Добавлен `UsedDirectFallback` — был ли использован прямой путь вместо NavMesh.

### 7.3 DrivingPlanner (переработан)
`Assets\_Scripts\Vehicle\Navigation\DrivingPlanner.cs`

КЛЮЧЕВОЕ ИЗМЕНЕНИЕ. Вместо выбора одного режима по первому углу — генерирует 2-3 кандидата:
1. **ForwardCandidate** — всегда
2. **ReverseCandidate** — если геометрия позволяет (`HasSafeBackingSpace`)
3. **TurnAroundCandidate** — если геометрия позволяет (`CanFitTurnRadius`)

Для каждого: строит манёвры → проверяет Feasibility → считает Cost. Выбирает самый дешёвый валидный. Если все невалидны — fallback на Forward.

### 7.4 DrivingPlan (расширен)
`Assets\_Scripts\Vehicle\Navigation\DrivingPlan.cs`

| Поле | Описание |
|------|----------|
| `Maneuvers` | Список манёвров |
| `Reason` | Пояснение выбора |
| `DrivingMode` | Forward/Reverse/TurnAround |
| `TotalCost` | Стоимость плана |
| `Feasibility` | Результат проверки (не пересчитывается!) |
| `Segments` | `IReadOnlyList<ManeuverPlanSegment>` |
| `EstimatedDistance` | Общая длина |
| `ReverseDistance` | Длина задним ходом |
| `TurnCount` | Число разворотов |
| `Risk` | RiskScore из Feasibility |

Метод `BuildSegments()` вычисляет статистику из манёвров.

### 7.5 ScoringSystem (расширен)
`Assets\_Scripts\Vehicle\Navigation\ScoringSystem.cs`

Методы:
- `ScoreCandidate(intent, length, turns, feasibility)` — оценка кандидата
- `ApplyRiskPenalty(base, feasibility)` — штраф за риск
- `Evaluate(ctx)` — старый метод (сохранён)

### 7.6 ManeuverPlanSegment
`Assets\_Scripts\Vehicle\Navigation\ManeuverPlanSegment.cs`

Хранит waypoints и метаданные ОТДЕЛЬНО от логики манёвра.

| Поле | Описание |
|------|----------|
| `ManeuverType` | Тип манёвра |
| `Waypoints` | Маршрутные точки |
| `DesiredHeadingYaw` | Желаемый угол |
| `AllowReverse` | Разрешён задний ход |
| `SpeedScale` | Множитель скорости |
| `LookAheadOverride` | Фикс. lookahead |
| `IsArrivalSegment` | Финальный сегмент |
| `SegmentLength` | Длина сегмента |

---

## 8. Часть 5: Восстановление, стратегии, композиты

### 8.1 RecoveryDecision
`Assets\_Scripts\Vehicle\Navigation\RecoveryDecision.cs`

Data-класс: `Action`, `SuggestedSteerSign`, `SuggestedCruiseSpeedKmh`, `Reason`.

### 8.2 IRecoveryStrategy
Интерфейс: `Priority` + `Evaluate(state, geometry, memory) → RecoveryDecision`.

Реализации (в порядке приоритета):
1. **AbortIfTooManyAttemptsStrategy** (P=1): ≥6 попыток → AbortAndStop
2. **RebuildPathAfterAttemptsStrategy** (P=2): ≥4 попыток → RebuildPath
3. **ReverseOutStrategy** (P=3): спереди блок + сзади свободно → ReverseOut
4. **UnstuckRockStrategy** (P=10): IsStuck → UnstuckRock

`RecoveryStrategyRegistry.Evaluate()` — единая точка входа.

### 8.3 RecoveryAction (enum, расширен)
`Assets\_Scripts\Vehicle\Navigation\DriverRecovery.cs`

None, StopAndReplan, TurnAround, ThreePointTurn, Abort, **UnstuckRock, ReverseOut, CreepAside, RebuildPath, AbortAndStop**.

### 8.4 RecoveryController (обновлён)
`Assets\_Scripts\Vehicle\Navigation\RecoveryController.cs`

`EvaluateAndGetManeuver(fb, memory) → (RecoveryAction, Maneuver?)` — вызывает стратегии через `RecoveryStrategyRegistry`. Recovery больше не строит маршруты сам — только инициирует.

### 8.5 VehicleDriverMemory (расширен)
Добавлены: `FeasibilityFailures`, `RecoveryCycles`. Методы: `RecordFeasibilityFailure()`, `RecordRecoveryCycle()`, `ResetRecoveryCounters()`.

### 8.6 CompositeManeuver
`Assets\_Scripts\Vehicle\Navigation\CompositeManeuver.cs`

Содержит `List<Maneuver> SubManeuvers`. Метод `Flatten()` разворачивает в плоский список. Пример: `TurnAround = [Forward, Reverse, Forward]`.

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

### 9.2 VehicleCommand (расширен)
Добавлены: `HoldPosition` (активное удержание), `Phase` (DrivingPhase).

Фабрики: `Idle` (HoldPosition=false, Phase=Cruise), `SoftPark` (HoldPosition=false, Phase=Parking).

### 9.3 MotionController (расширен)
`Assets\_Scripts\Vehicle\Navigation\MotionController.cs`

Новые методы:
- `HoldInPlace()` — активное удержание: throttle=0, Brake=Soft, HoldPosition=true, Phase=Parking
- `Park()` — вызывает HoldInPlace() + плавно выравнивает руль
- `ResolvePhase(ctx)` — определяет Phase по типу манёвра

`Convert()` — устанавливает Phase через ResolvePhase.

Добавлены:
- **DriverComfortLimiter** — сглаживание throttle (rate 0.15/кадр)
- **SteeringDamping** — экспоненциальное замедление руля при >40 км/ч

### 9.4 GoalLimiter (расширен)
`Assets\_Scripts\Vehicle\Navigation\Speed\GoalLimiter.cs`

При ApproachWithHeading/Parking форсирует creep-скорость независимо от расстояния.

### 9.5 SpeedPlanner (расширен)
Учитывает `FeasibilityResult.RecommendedMaxSpeedKmh` как дополнительный лимит.

### 9.6 DriverFSM.TickHolding()
Упрощён до `return m_Motion.HoldInPlace()`.

---

## 10. Часть 7: Vehicle Safety System

**Принцип:** последний фильтр перед физикой. Не меняет планы, не вмешивается в Pursuit. Только не даёт отправить опасную команду.

### 10.1 Архитектура
```
MotionController.Convert() → сырая команда
    │
    ▼
VehicleSafetyController.Apply()
    │
    ├── 1. CommandSanitizer        (логика)
    ├── 2. DynamicsLimiter         (RollLimit + SteeringRate)
    ├── 3. StabilityLimiter        (WheelLift + Slip + RollAngle + Pitch)
    ├── 4. AirborneProtection      (в воздухе)
    └── 5. RecoveryProtection      (recovery в опасности)
    │
    ▼
VehicleBrain.SetCommand()
```

### 10.2 ISafetyLimiter (интерфейс)
`Assets\_Scripts\Vehicle\Navigation\Safety\ISafetyLimiter.cs`

`SafetyInput`: State, Params, ProposedCommand, DeltaTime, IsRecovering, EulerAngles.
`SafetyOutput`: Command, Triggered, Warning, ShouldAbortRecovery.

### 10.3 CommandSanitizer
Проверяет: Throttle+HardBrake → газ=0, HoldPosition+Throttle → газ=0, HoldPosition без Brake → добавляет Soft.

### 10.4 DynamicsLimiter
- **RollLimiter**: a = V²/R. Если >6 м/с² → throttle снижается. Ограничивает СКОРОСТЬ, не руль.
- **SteeringRateLimiter**: Δsteer ≤ 1.2/сек. Убирает дёрганье на высоких скоростях.

### 10.5 StabilityLimiter
- **WheelLift**: ≥2 колёс одного борта в воздухе → газ×0.3, руль×0.5
- **SlipProtection**: sidewaysSlip>0.15 → газ×0.5; >0.3 → газ=0, руль×0.5
- **RollAngleMonitor**: 20°→газ×0.5, 25°→SoftBrake, 35°→HardBrake+Steer=0
- **PitchProtection**: 20°→газ×0.5, 30°→SoftBrake

### 10.6 AirborneProtection
В воздухе: газ=0, тормоз=0, руль сохранён. Запрещает смену передачи.

### 10.7 RecoveryProtection
Roll>30° или Pitch>35° или airborne → прервать recovery.

### 10.8 PursuitController.AdaptiveLookAhead
Если curvature меняет знак ≥3 раз → lookahead×1.5. Плавно возвращается к 1.0.

### 10.9 PrecisionArrival в PursuitController
При distanceToEnd < 2м: lookahead ≤1.2м, curvature ≤0.25, скорость понижена.

---

## 11. Часть 8: Precision Arrival System

**Принцип:** включается только у цели (< planningDistance). Генерирует 5 стратегий, выбирает по стоимости.

### 11.1 ArrivalPlanningSettings
`Assets\_Scripts\Vehicle\Navigation\ArrivalPlanningSettings.cs`

Все расстояния вычисляются от `TurnRadius`:
- `PlanningDistance` = max(4R, 6м)
- `PreGoalDistance` = max(0.4R, 2м)
- `RepositionStep` = max(0.55R, 1.5м)
- `PrecisionMaxSpeedKmh` = 3
- `PrecisionLookAhead` = 1.2м

### 11.2 ArrivalAnalysis
`Assets\_Scripts\Vehicle\Navigation\ArrivalAnalysis.cs`

Чистая геометрия, не решения:
- `Distance`, `HeadingError`, `LateralOffset`
- `TargetInFront`, `TargetInsideTurningCircle`
- `CanReachForward`, `CanReachReverse`

### 11.3 IArrivalStrategy
Интерфейс: `Generate(analysis, settings, pos, yaw, target, heading) → ArrivalPlan { Maneuvers, Cost, DebugName }`.

### 11.4 Стратегии (5 штук)
- **DirectArrivalStrategy**: цель достижима прямо → ApproachWithHeading/Parking
- **ArcArrivalStrategy**: дуга если угол 15°-120° → TurnAroundManeuver + Arrival
- **ReverseArrivalStrategy**: задний ход если цель сзади близко → ReverseManeuver + Arrival
- **RepositionArrivalStrategy**: отъезд назад → Reverse + Arrival
- **TurnAroundArrivalStrategy**: полный разворот → TurnAround + Forward + Arrival

### 11.5 ArrivalCostEvaluator
`cost = distance*1.2 + headingError*0.3 + reverse*8 + maneuvers*5 + precision*2`

### 11.6 ArrivalPlanner
Главный оркестратор. Вызывается из `DrivingPlanner.AppendArrivalManeuver()` когда расстояние < PlanningDistance.

---

## 12. Манёвры — полный справочник

### Базовый класс Maneuver
`Assets\_Scripts\Vehicle\Navigation\Maneuver.cs`

Абстрактный. Свойства: `Type`, `Waypoints`, `AllowReverse`, `SpeedScale`, `LookAheadOverride`, `IsArrivalManeuver`. Метод: `IsComplete(ctx)`.

### Типы (VehicleManeuverType)
| Тип | Класс | Когда | SpeedScale | AllowReverse |
|-----|-------|------|------------|-------------|
| Forward | ForwardManeuver | Основной режим | 1.0 | false |
| Reverse | ReverseManeuver | Устаревший | 1.0 | true |
| ReverseIntent | ReverseIntentManeuver | Полный задний ход | — | true |
| TurnAround | TurnAroundManeuver | Разворот 180° | 0.5 | false |
| ThreePointTurn | ThreePointTurnManeuver | Узкое место | 0.3 | true |
| Parking | ParkingManeuver | Парковка + heading | 0.22 | true |
| ApproachWithHeading | ApproachWithHeadingManeuver | Подъезд с heading | 0.28 | true |
| Unstuck | UnstuckManeuver | Раскачка | — | false |
| Stop | StopManeuver | Остановка | 0 | false |

### Как работает Pursuit (чистое преследование)
```
PursuitController.Tick(ctx, maneuver, speedFrac, topSpeed, lookAhead, override?)
  1. ComputeLookAhead(speed, base) → dynamic lookahead
  2. FindNearestWaypointIndex(waypoints, pos)
  3. FindLookAheadIndex(waypoints, nearest, pos, lookAhead)
  4. Pure pursuit: κ = 2·crossTrack / L²
  5. Clamp curvature (distance-based + speed-based via SteeringLimitCurve)
  6. AdaptiveLookAhead: detect oscillation, increase lookahead
  7. Speed = capKmh * min(curvatureFraction, arrivalScale) * launchRamp
  8. Precision mode: if distToEnd < 2m → tighter control
  9. Return MotionCommand { DesiredSpeedKmh, DesiredCurvature, Reverse }
```

### Как манёвры создаются
```
DrivingPlanner.BuildPlan()
  ├─ BuildForwardCandidate()    → [Forward] + AppendArrival()
  ├─ BuildReverseCandidate()    → [ReverseIntent] + AppendArrival()
  └─ BuildTurnAroundCandidate() → [TurnAround, Forward] + AppendArrival()

AppendArrivalManeuver():
  → ArrivalPlanner.PlanArrival() (если dist < PlanningDistance)
    → генерирует 5 кандидатов, выбирает лучший
  → fallback: старый switch по FacingMode
```

---

## 13. Логирование — все теги и сообщения

Каждый модуль имеет `public static bool DebugLog = true`. В консоли фильтровать по тегу:

### [DrivingPlanner]
```
[DrivingPlanner] candidates: Forward (always), Reverse=True (ok), TurnAround=False (no space), proposed=Reverse safe=Reverse
[DrivingPlanner]   Forward: cost=45.2 valid=True risk=0.00
[DrivingPlanner]   Reverse: cost=38.1 valid=True risk=0.15
[DrivingPlanner] => CHOSE Reverse cost=38.1 (of 2 candidates), firstAngle=160° dist=8.3m
```
Если Reverse не создался — искать причину: `disabled` (AllowReverse=false), `no space` (HasSafeBackingSpace=false).

### [ArrivalPlanner]
```
[ArrivalPlanner] dist=3.2m angle=25° lateral=1.4m front=True deadZone=False turnR=6.5
[ArrivalPlanner]   Direct: cost=8.3 maneuvers=1
[ArrivalPlanner]   Arc: SKIP (angle out of arc range)
[ArrivalPlanner]   Reverse: cost=15.2 maneuvers=2
[ArrivalPlanner] => CHOSE Direct cost=8.3
```
Если слишком далеко: `too far: 12.3m > 10.0m planning distance`.
Если нет стратегий: `NO valid arrival strategy found` — fallback на старую логику.

### [Feasibility]
```
[Feasibility] Forward REJECTED: front clearance 0.5m < 1.8m
[Feasibility] Reverse REJECTED: drop behind
[Feasibility] TurnAround REJECTED: cannot fit turn radius 6.5m
```
Молча проходит — значит план принят.

### [Recovery]
```
[Recovery] action=UnstuckRock reason=stuck — rocking attempts=1 recovering=False
[Recovery] action=ReverseOut reason=front blocked, rear clear — backing out attempts=3 recovering=True
[Recovery] action=RebuildPath reason=unstuck 4 attempts — replanning attempts=4 recovering=True
[Recovery] action=AbortAndStop reason=unstuck 6 attempts — aborting attempts=6 recovering=True
```

### [PathPlanner]
```
[PathPlanner] NavMesh failed, using direct fallback [ (12,0,5) → (45,0,30) ] fromNav=True toNav=False
[PathPlanner] NavMesh path failed, direct fallback disabled → Invalid
```

### [OrderQueue]
```
[OrderQueue] EmergencyStop — cancel all, queue was 3
[OrderQueue] completed #1 Move
[OrderQueue] CancelAll: hard-stop (queue=2 current=True)
```

### [DriverFSM]
```
[DriverFSM] RebuildPlan: mode=Forward maneuvers=[ [0]Forward [1]ApproachWithHeading ] cost=38.1 dist=42.5m rev=0.0m risk=0.00 reason=mode=Forward cost=38.1...
```

### [Safety]
```
[Safety] DynamicsLimiter: RollLimit: lat=7.2>6 safe=38.5kmh
[Safety] DynamicsLimiter: (steering rate clamped)
[Safety] StabilityLimiter: SlipCritical: 0.35>0.3
[Safety] StabilityLimiter: WheelLift: left=2 right=0 off ground
[Safety] StabilityLimiter: RollCritical: 27.3°>25°
[Safety] AirborneProtection: airborne
[Safety] RecoveryProtection: RecoveryAbort: roll=32° pitch=12° airborne=False
```

### [Polygon] (при сборке трека)
```
[Polygon] Ready. X=80, Z=5, length ~370m, 10 sections.
```

---

## 14. Тестовый полигон

Меню: **Polygone → Vehicles → Build NAVIGATION Test Track**

Создаёт полигон справа от существующей сцены (X=80, Z=5).

| Секция | Название | Длина | Описание |
|--------|---------|------|----------|
| START | Старт | — | Зелёный столб |
| 1 | Slalom | ~72м | 8 оранжевых конусов через 9м, смещение ±6м |
| 2 | Narrow passage | 30м | Стены 5м шириной, 30м длины |
| 3 | Sharp 90° right | 16м | Красная стена, поворот вправо |
| 4 | Obstacle | 18м | Красная стена, объезд справа |
| 5a | Ramp up 10° | 24м | Подъём +4.2м |
| 5b | Plateau | 12м | Плоская вершина |
| 5c | Ramp down | 18м | Спуск |
| 6 | Side slope | 22м | Боковой уклон |
| 7 | Drop edge | 10м | Платформа с краем |
| 8 | Reverse target | 11м | Цель сзади, стены по бокам |
| 9 | Heading arrival | 11м | Синий маркер + стрелка |
| 10 | Waypoint chain | 45м | 4 оранжевых маркера для Shift+клик |
| FINISH | Финиш | — | Красный столб |

Параметры: Ground 80×380м, полоса 14м, камера Y=25 (угол 60°). Машина ставится в начало. NavMesh перезапекается.

---

## 15. Полный список файлов

### Новые файлы (созданы)

| Файл | Часть | Назначение |
|------|-------|-----------|
| `Navigation/VehicleOrderType.cs` | 1 | Enum типов приказов |
| `Navigation/ArrivalFacingMode.cs` | 1 | Enum режимов ориентации |
| `Navigation/VehicleMoveOrder.cs` | 1 | Класс приказа с OrderState |
| `Navigation/VehicleOrderQueue.cs` | 1 | Очередь приказов |
| `Navigation/ArrivalCriteria.cs` | 2 | Критерии прибытия |
| `Navigation/ApproachWithHeadingManeuver.cs` | 2 | Манёвр подъезда с heading |
| `Navigation/FeasibilityResult.cs` | 3 | Вердикт проверки |
| `Navigation/ManeuverFeasibilityChecker.cs` | 3 | Проверщик выполнимости |
| `Navigation/PathBuildOptions.cs` | 4 | Опции построения пути |
| `Navigation/ManeuverPlanSegment.cs` | 4 | Сегмент с waypoints |
| `Navigation/CompositeManeuver.cs` | 5 | Составной манёвр |
| `Navigation/RecoveryDecision.cs` | 5 | Data-класс + стратегии |
| `Navigation/ArrivalPlanningSettings.cs` | 8 | Настройки точного прибытия |
| `Navigation/ArrivalAnalysis.cs` | 8 | Геометрический анализ |
| `Navigation/IArrivalStrategy.cs` | 8 | Интерфейс + ArrivalPlan |
| `Navigation/ArrivalCostEvaluator.cs` | 8 | Оценка стоимости |
| `Navigation/DirectArrivalStrategy.cs` | 8 | Прямой подъезд |
| `Navigation/ArcArrivalStrategy.cs` | 8 | Плавная дуга |
| `Navigation/ReverseArrivalStrategy.cs` | 8 | Задний ход |
| `Navigation/RepositionArrivalStrategy.cs` | 8 | Перепозиционирование |
| `Navigation/TurnAroundArrivalStrategy.cs` | 8 | Полный разворот |
| `Navigation/ArrivalPlanner.cs` | 8 | Главный оркестратор |
| `Navigation/Safety/ISafetyLimiter.cs` | 7 | Интерфейс защит |
| `Navigation/Safety/VehicleSafetyController.cs` | 7 | Диспетчер |
| `Navigation/Safety/CommandSanitizer.cs` | 7 | Логическая проверка |
| `Navigation/Safety/DynamicsLimiter.cs` | 7 | RollLimit + SteeringRate |
| `Navigation/Safety/StabilityLimiter.cs` | 7 | WheelLift + Slip + Roll + Pitch |
| `Navigation/Safety/AirborneProtection.cs` | 7 | Защита в воздухе |
| `Navigation/Safety/RecoveryProtection.cs` | 7 | Защита recovery |
| `Editor/VehicleNavTestSceneSetup.cs` | — | Сборка тестового полигона |

### Изменённые файлы

| Файл | Части |
|------|-------|
| `VehicleController.cs` | 1 |
| `Navigation/NavigationRequest.cs` | 1, 2 |
| `Navigation/VehicleNavigation.cs` | 1, 3, 4, 7, 8 |
| `Navigation/DriverFSM.cs` | 2, 3, 5, 6 |
| `Navigation/PathPlanner.cs` | 4 |
| `Navigation/PathResult.cs` | 4 |
| `Navigation/DrivingPlan.cs` | 4 |
| `Navigation/DrivingPlanner.cs` | 2, 4, 8 |
| `Navigation/ScoringSystem.cs` | 3, 4 |
| `Navigation/ArrivalController.cs` | 2 |
| `Navigation/ManeuverPlanner.cs` | 2 |
| `Navigation/Maneuver.cs` | — |
| `Navigation/PathSmoother.cs` | 2 |
| `Navigation/VehicleManeuverType.cs` | 2 |
| `Navigation/VehicleLocalGeometry.cs` | 3 |
| `Navigation/TrajectoryPrediction.cs` | 3 |
| `Navigation/RecoveryController.cs` | 5 |
| `Navigation/DriverRecovery.cs` | 5 |
| `Navigation/VehicleDriverMemory.cs` | 5 |
| `Navigation/MotionController.cs` | 6 |
| `Navigation/PursuitController.cs` | 7, 8 |
| `Navigation/Speed/SpeedPlanner.cs` | 4, 6 |
| `Navigation/Speed/GoalLimiter.cs` | 6 |
| `Navigation/VehicleNavigationDebugDrawer.cs` | 5 |
| `Core/VehicleCommand.cs` | 6 |

---

## 16. Словарь терминов

| Термин | Определение |
|--------|-----------|
| **Order** | Приказ — высокоуровневая команда (Move, Stop, Hold, Park...) |
| **Request** | Навигационный запрос — иммутабельный struct, мост от приказов к планировщику |
| **Path** | Путь — результат PathPlanner (NavMesh-углы или прямой fallback) |
| **Plan** | План — результат DrivingPlanner (список манёвров + cost + feasibility) |
| **Maneuver** | Манёвр — атомарная единица плана (Forward, Reverse, TurnAround...) |
| **Segment** | Сегмент — waypoints + метаданные одного манёвра |
| **Candidate** | Кандидат — вариант плана (DrivingPlanner генерирует несколько) |
| **Feasibility** | Выполнимость — можно ли физически проехать (геометрия + предикшн) |
| **Pursuit** | Чистое преследование — κ = 2·crossTrack / L² |
| **Curvature** | Кривизна (κ) — обратный радиус поворота, 1/м |
| **Arrival** | Прибытие — финальная фаза подъезда к цели |
| **Recovery** | Восстановление — выход из застревания |
| **Safety** | Безопасность — последний фильтр перед физикой |
| **Phase** | Фаза движения — Cruise / Precision / Parking / Recovery |
| **FSM** | Конечный автомат водителя — DriverFSM |
| **NavMesh** | Навигационная сетка Unity |
| **TTC** | Time To Collision — время до столкновения |
| **PID** | Пропорционально-интегрально-дифференциальный регулятор |
| **Waypoint** | Маршрутная точка |
| **LookAhead** | Дистанция упреждения для Pure Pursuit |
| **Cross-track** | Боковое отклонение от траектории |
| **Slip** | Боковое скольжение колеса |
| **Roll** | Крен (вращение вокруг оси Z) |
| **Pitch** | Тангаж (вращение вокруг оси X) |
