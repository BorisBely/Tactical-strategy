# Архитектура движения автомобиля

## 1. Общая схема конвейера

```
                      ┌─────────────────────────────────────────────────────┐
                      │              VehicleNavigation (Entry Point)         │
                      │              MonoBehaviour, FixedUpdate              │
                      └─────────────────────┬───────────────────────────────┘
                                            │
          ┌─────────────────────────────────┼─────────────────────────────────┐
          │                                 │                                 │
          ▼                                 ▼                                 ▼
┌─────────────────┐   ┌─────────────────────────────┐   ┌──────────────────────┐
│  FeedbackSystem │   │       VehicleOrderQueue     │   │     DriverFSM        │
│  (сенсоры)      │   │     (очередь команд)        │   │  (конечный автомат)  │
│  Position       │   │  Move/Stop/Hold/Park...     │   │  Idle→Driving→Arrival│
│  SpeedKmh       │   └─────────────┬───────────────┘   │  →Holding→Recovery   │
│  Forward/Right  │                 │                    │  →EmergencyStop      │
│  IsStuck/IsAir  │                 ▼                    └──────────┬───────────┘
│  Geometry       │   ┌─────────────────────────────┐              │
└────────┬────────┘   │     NavigationRequest       │◄─────────────┘
         │            │  Destination, Heading,      │
         │            │  SpeedMode, FacingMode,     │
         │            │  AllowReverse/TurnAround    │
         │            └─────────────┬───────────────┘
         │                          │
         ▼                          ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                              ПОСТРОЕНИЕ ПЛАНА                               │
│                                                                             │
│  ┌───────────────┐     ┌──────────────────┐     ┌──────────────────────┐    │
│  │  PathPlanner  │     │  DrivingPlanner  │     │  ManeuverPlanner     │    │
│  │  (NavMesh)    │────▶│  (Forward/Rev/U) │────▶│  (waypoint-ы)        │    │
│  │  corners[]    │     │  +ArrivalPlanner │     │  для каждого Maneuver│    │
│  └───────────────┘     └──────────────────┘     └──────────┬───────────┘    │
│                                                            │                │
│                             DrivingPlan (манёвры + waypoint-ы)              │
└─────────────────────────────────────────────────────────────────────────────┘
                                            │
                                            ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                           ИСПОЛНЕНИЕ (каждый FixedUpdate)                   │
│                                                                             │
│  DriverFSM.Tick()                                                           │
│    │                                                                        │
│    ├─ ExecuteManeuver(maneuver)                                             │
│    │   │                                                                    │
│    │   ├─ Forward/Reverse/Parking/Approach...                               │
│    │   │   └─ PursuitController.Tick()    ← pure pursuit                   │
│    │   │       │  Input: waypoints, position, speed, lookAhead             │
│    │   │       │  Output: MotionCommand (speed, curvature, reverse)        │
│    │   │                                                                    │
│    │   ├─ ReverseIntentManeuver                                             │
│    │   │   └─ ReverseDriver.Tick()         ← reverse pure pursuit          │
│    │   │       └─ ReversePursuit.Tick()     (rear axle, lookBehind)         │
│    │   │                                                                    │
│    │   └─ ArrivalManeuver (близко к цели)                                    │
│    │       └─ PrecisionArrivalController.Tick()  ← локальные ошибки         │
│    │                                                                    │
│    ├─ TickArrival()      ← финальная фаза: проверка, GoalLocked, Holding   │
│    └─ TickHolding()      ← HoldInPlace()                                    │
│                                                                             │
│    ↓ MotionCommand                                                          │
│                                                                             │
│  ┌─────────────────────┐                                                    │
│  │  MotionController   │                                                    │
│  │  curvature → steer  │                                                    │
│  │  speed → throttle   │                                                    │
│  │  + сглаживание      │                                                    │
│  │  + SteeringDamping  │                                                    │
│  │  → VehicleCommand   │                                                    │
│  └──────────┬──────────┘                                                    │
│             │                                                               │
│             ▼                                                               │
│  ┌─────────────────────┐     ┌────────────────────┐                         │
│  │ VehicleSafetyCtrl   │────▶│  VehicleController │                         │
│  │ (Dynamics/Stability │     │  Ackermann → руль  │                         │
│  │  CommandSanitizer)  │     │  газ/тормоз → мотор│                         │
│  └─────────────────────┘     └────────┬───────────┘                         │
│                                       │                                     │
│                                       ▼                                     │
│                              WheelCollider × N                              │
│                              Rigidbody physics                              │
│                              → новое Position, Forward, Speed               │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. FeedbackSystem — сбор состояния автомобиля

**Файл:** `FeedbackSystem.cs`  
**Вызов:** каждый `FixedUpdate`, до планирования

```
Transform.position  ─┐
Transform.forward   ─┤
Rigidbody.velocity  ─┤
WheeledMotor        ─┼──▶ FeedbackState (readonly struct)
  (кол-во grounded) ─┤       Position, Forward, Right, Yaw,
LayerMask geometry  ─┤       SpeedKmh, SpeedSignedKmh, VelocitySqr,
VehicleWidth        ─┤       IsReversing, IsStopped, IsStuck,
stuckTime=3s        ─┘       IsAirborne, IsUpright,
stuckSpeed=1.2km/h           Geometry (LocalGeometry.Sample),
                              Memory (VehicleDriverMemory)
```

### VehicleLocalGeometry.Probe()
Лучевой кастинг вокруг машины: 8 направлений, дистанция до препятствий, clearance слева/справа/спереди/сзади, preferred turn sign, есть ли место для U-turn.

---

## 3. PathPlanner — построение маршрута

**Файл:** `PathPlanner.cs`

```
Destination ──▶ NavMesh.CalculatePath(from, to, AllAreas, NavMeshPath)
                     │
                     ├─ Успех ──▶ corners[] (массив Vector3)
                     │            PathResult.IsValid = true
                     │
                     ├─ Частичный (PathPartial) ──▶ если AllowPartialPath → corners[]
                     │                              иначе → fallback
                     │
                     └─ Провал ──▶ BuildDirectPath(from, to)
                                    │
                                    ├─ dist < 10м ──▶ [from, to] (2 точки)
                                    └─ dist ≥ 10м ──▶ промежуточные точки каждые 5м
```

**PathResult**: corners[], length, IsValid, IsPartial, UsedDirectFallback.

---

## 4. DrivingPlanner — генерация манёвров

**Файл:** `DrivingPlanner.cs`

### 4.1 Принятие решения о режиме

```
1. Вычислить firstAngle — угол между forward машины и направлением на первую точку пути
2. DecideBaseMode(firstAngle, firstSegLen, flatToDest, reverseMaxSegment, ...)
     │
     ├─ firstAngle ≤ 90°   ──▶ Forward
     ├─ firstAngle ≥ 135°  ──▶ Reverse (если сегмент короткий и цель близко)
     ├─ firstAngle > 90°   ──▶ TurnAround
     └─ иначе              ──▶ Forward
3. DecisionEvaluator.ChooseSafeMode() — проверка по геометрии и памяти водителя
```

### 4.2 Кандидаты

| Кандидат | Манёвры | Когда доступен |
|---|---|---|
| Forward | `ForwardManeuver + arrival` | Всегда |
| Reverse | `ReverseIntentManeuver + arrival` | AllowReverse |
| TurnAround | `TurnAroundManeuver + PostTurnAlignment(≤6м)/ForwardManeuver + arrival` | AllowTurnAround + есть место |

### 4.3 Оценка кандидатов

```
1. ManeuverFeasibilityChecker.CheckPlan() — проверка безопасности: препятствия, clearance
   → FeasibilityResult (Valid / Risky / Unsafe / Impossible)
2. ScoringSystem.ScoreCandidate() — стоимость по intent, дистанции, числу поворотов, severity
3. Выбор: минимальная стоимость
```

### 4.4 AppendArrivalManeuver()

```
Если ArrivalPlanner.PlanArrival() вернул манёвры ──▶ добавить их к плану
Иначе ──▶ fallback для простых фронтальных случаев:
         FaceHeading → ApproachWithHeadingManeuver
         UsePathFacing → ParkingManeuver
         KeepCurrent → без добавления
         None (heading задан, delta > 18°) → ParkingManeuver
```

### 4.5 ArrivalPlanner — выбор стратегии подъезда

**5 стратегий прибытия:**
- `DirectArrivalStrategy` — строго фронтальная, |angle| ≤ 45°, lateral ≤ 1.5м
- `ArcArrivalStrategy` — плавная дуга, 15° < |angle| < 120°
- `ReverseArrivalStrategy` — только задняя полусфера
- `RepositionArrivalStrategy` — отъезд назад + подъезд
- `TurnAroundArrivalStrategy` — полный разворот

**Приоритетные группы по TargetSide:**

| TargetSide | Группа 1 (приоритет) | Группа 2 (запасная) |
|---|---|---|
| Front | Direct | Arc |
| Rear | Reverse | TurnAround |
| Left/Right | Arc, Reposition | TurnAround |

AtGoal (dist < 0.15м, |angle| < 3°) → немедленное завершение без плана.

---

## 5. ManeuverPlanner — waypoint-ы для манёвров

**Файл:** `ManeuverPlanner.cs`

| Тип манёвра | Waypoint-ы |
|---|---|
| `Forward` | NavMesh corners |
| `Reverse` (старый) | 2 точки: позиция → первая corner |
| `TurnAround` | `PathSmoother.GenerateTurnaroundTrajectory()` |
| `ThreePointTurn` | `PathSmoother.GenerateThreePointWaypoints()` |
| `Parking` | `PathSmoother.GenerateParkingWaypoints()` |
| `ApproachWithHeading` | `PathSmoother.GenerateApproachWithHeadingArc()` |
| `Unstuck` | `PathSmoother.GenerateUnstuckWaypoints()` |
| `Stop` | [текущая позиция] |
| `PostTurnAlignment` | [текущая позиция, destination] (прямая линия) |
| `ReverseIntent` | **пропускается** (свой ReversePathBuilder) |

---

## 6. DriverFSM — конечный автомат

**Файл:** `DriverFSM.cs`

### 6.1 Состояния

```
        ┌──────────┐
        │   Idle   │◄──────────────────────────────────────┐
        └────┬─────┘                                       │
             │ SetDestination()                             │
             ▼                                              │
        ┌──────────┐     RecoveryAction.AbortAndStop ──────┘
        │ Driving  │──────────────────────────────────────┐
        └────┬─────┘                                      │
             │ манёвр завершён                             │
             ▼                                              │
        ┌──────────┐     HasArrived                       │
        │ Arrival  │──────────────────────┐               │
        └────┬─────┘                      │               │
             │ HasArrived + GoalLocked     │               │
             ▼                            ▼               │
        ┌──────────┐                ┌──────────┐         │
        │ Holding  │                │ Recovery │─────────┘
        └──────────┘                └──────────┘
                                       │
        EmergencyStop ──▶ ┌────────────────┐
                           │ EmergencyStop  │
                           └────────────────┘
```

### 6.2 Tick() — главный цикл

```csharp
VehicleCommand Tick()
{
    1. Проверить EmergencyStop
    2. Если Idle → Idle()
    3. Если Holding → TickHolding()
    4. Если PlanDirty или !HasPlan → RebuildPlan()
    5. Recovery check (m_Recovery.EvaluateAndGetManeuver)
    6. Если Recovery → выполнить recovery-манёвр или RebuildPath/Abort/ReverseOut
    7. Проверка завершения манёвра:
       - Если ArrivalManeuver и IsComplete → Arrival
       - AdvanceManeuverIfComplete → следующий манёвр или Arrival
    8. Если Arrival → TickArrival()
    9. Иначе → ExecuteManeuver(maneuver)
}
```

### 6.3 TickArrival()

```
1. Если GoalLocked → HoldInPlace()
2. HasArrived → GoalLocked=true, Holding, Park()
3. dist < 1.5м && speed > 5км/ч → BrakeToStop (crawl)
4. dist < 0.6м && speed > 2км/ч → BrakeToStop (slow-cap)
5. dist < 0.3м && speed < 1км/ч → GoalLocked=true, Holding, Park()
6. dist < ActivationDistance → PrecisionArrivalController.Tick()
7. Иначе → стандартный PursuitController на arrival-манёвре
```

### 6.4 GoalLocked

**Единственный источник:** `TickArrival()` → `HasArrived == true` ИЛИ `PrecisionArrivalController.IsComplete == true`.  
**ReverseDriver НЕ ставит GoalLocked.** Только Arrival/Holding.

---

## 7. PursuitController — Pure Pursuit (движение вперёд)

**Файл:** `PursuitController.cs`

### 7.1 Алгоритм

```
Input:  waypoints[], position, forward, speedKmh, lookAhead, speedCapFraction

1. lookAhead = base + speed * 0.35    (clamp 3–16м)
   + адаптивный множитель (при осцилляции ×1.5)

2. Найти ближайший waypoint (FindNearestWaypointIndex)

3. Найти точку опережения (FindLookAheadIndex)
   → накопление расстояний по waypoint-ам до ≥ lookAhead

4. Pure Pursuit:
   toTarget = targetPoint - position
   crossTrack = cross(forward, toTarget_normalized) * distance
   curvature = 2 * crossTrack / lookAhead²

5. Clamp curvature:
   - близко к концу (distanceToEnd < 6м) → maxCurv плавно снижается 0.35 → 0.12
   - ограничение от SteeringLimitCurve(speed)

6. Speed:
   curvatureFraction = CurvatureSpeedCurve(maxCurvature)
   arrivalScale = clamp(distanceToEnd / 15, 0, 1)
   targetSpeed = speedCap * min(curvatureFraction, arrivalScale)
   + launchRamp (плавный разгон)
   + финальное замедление:
     - dist < 2м → cap 3km/h + lookAhead ≤ 1.2
     - dist < 1.5м → cap 3km/h + lookAhead ≤ 0.8м
     - dist < 0.6м → cap 1km/h + lookAhead ≤ 0.4м

7. Preview curvature: заглядывание вперёд на 4 сегмента для раннего торможения

Output: MotionCommand(DesiredSpeedKmh, DesiredCurvature, isReversing)
```

### 7.2 Адаптивная адаптация

При 3+ флипах знака кривизны (осцилляция) → `lookAhead *= 1.5`  
При затухании → плавный возврат к 1.0 через `Lerp(..., 0.3)`.

---

## 8. ReversePursuit + ReverseDriver — Pure Pursuit задом

### 8.1 ReversePathBuilder

**Файл:** `Reverse\ReversePathBuilder.cs`

```
NavMesh corners
    │
    ├─ maxAngle между сегментами ≤ 10° ──▶ без сглаживания (ломаная)
    └─ maxAngle > 10° ──▶ CatmullRomSmooth (4 подточки на сегмент)
                           │
                           └─ Проверка: изменился ли финальный heading > 5°?
                              └─ Да → корректировать ПРЕДпоследнюю точку
                                 (destination НЕ трогать)
    │
    ▼
ReversePath (List<PathPoint>)
  PathPoint: Position, Tangent, Curvature, DistanceFromStart
```

### 8.2 ReversePursuit

**Файл:** `Reverse\ReversePursuit.cs`

```
Отличие от ForwardPursuit: работает от REAR AXLE, смотрит НАЗАД по пути.

1. Контрольная точка: ctx.GetControlPoint(Reverse) = RearAxlePosition
   = Position - Forward * (WheelBase * 0.5)

2. lookBehind = max(
       LookByDistCurve(remainingDistance),  // 0.35–3 м
       LookBySpeedCurve(speedKmh)           // 1–6 м
   )
   Гарантирует: даже на высокой скорости lookBehind не обваливается в 0.35

3. GetLookBehind(rearAxle, lookBehind) → точка на пути ПОЗАДИ машины

4. Pure Pursuit (задний ход):
   travelDir = -forward
   cross = cross(travelDir, toTargetDir).y
   crossTrack = cross * distance
   rawCurvature = 2 * crossTrack / lookBehind²

5. Clamp curvature:
   - близко к концу → maxCurv 0.35 → 0.12
   - ReverseSteeringLimiter по скорости

6. Сглаживание: smoothedCurvature = Lerp(prev, new, 0.3)

Output: DesiredCurvature, DistanceToEnd (БЕЗ DesiredSpeedKmh)
```

### 8.3 ReverseDriver

**Файл:** `Reverse\ReverseDriver.cs`

Оркестратор заднего хода:

```
TryStart:
  ReversePathBuilder.Build() — построить путь
  ReverseStateMachine.Reset()
  ReversePursuit.Reset()

Tick:
  1. Path.Advance(rearAxlePosition) — обновить CurrentSegment
  2. ReverseStateMachine.Tick() — определить фазу (Enter→Align→Reverse→SlowDown→Stop)
  3. Recovery check:
     - SteeringSaturated: разрешён до 0.5с в фазах Align/Reverse при движении
     - Иначе → ForceFail
  4. Выбор команды:
     - Enter/Align → AlignCommand (руль к первой точке, газ 0)
     - SlowDown/Stop → SlowStopCommand (тормоз при speed<0.1)
     - Reverse → DrivingCommand:
         a. ReversePursuit.Tick() → DesiredCurvature
         b. steerTarget = atan(wheelBase * curvature) / maxSteeringAngleRad
         c. currentSteer = MoveTowards(current, target, rate)
         d. SteeringLimiter.ClampSteer(steer, speed)
         e. Speed: distance-based caps
            - dist<2м → max 3км/ч
            - dist<0.8м → max 1.5км/ч
            - dist<0.3м → max 0.5км/ч
            - curvature penalty: absCurv>0.15 → speed *= 1-(curv-0.15)*3
         f. throttle = PID(speedError)
            negative throttle для reverse
```

### 8.4 ReverseStateMachine

**Файл:** `Reverse\ReverseState.cs`

```
Enter ──▶ скорость < 0.3км/ч ──▶ Align
Align ──▶ угол к пути < 20° && скорость < 1км/ч ──▶ Reverse
          ИЛИ TimeInState > 1.5с ──▶ Reverse (принудительно)

Reverse ──▶ remaining < 30% длины пути ──▶ SlowDown

SlowDown ──▶ скорость < 0.5км/ч && remaining < 0.8м ──▶ Stop

Stop ──▶ скорость < 0.1км/ч && remaining < 0.6м && heading OK ──▶ Finished
         │  (heading проверяется только если RequestedHeading задан)
         │
         └─ TimeInState > 1с && remaining > 0.6м && NoProgressTimer > 1с ──▶ Reverse (retry)

Progress tracking:
  - bestRemaining обновляется когда remaining уменьшился на ≥ 5см
  - noProgressTimer считает время без прогресса
  - предотвращает цикл Reverse↔Stop↔Reverse
```

### 8.5 ReverseSteeringLimiter

**Файл:** `Reverse\ReverseSteeringLimiter.cs`

```
LimitCurve:
  0 км/ч → 1.00 (полный руль)
  5 км/ч → 0.90
  10 км/ч → 0.70
  15 км/ч → 0.50
  20 км/ч → 0.30

Логирует при ограничении > 20%.
```

---

## 9. PrecisionArrivalController — точный подъезд

**Файл:** `PrecisionArrivalController.cs`

Активируется в радиусе **2–6 м** от цели. Заменяет PursuitController.

```
НЕ использует lookahead / waypoints / pure pursuit.
Работает от локальных векторов ошибок.

Для forward:
  toGoal = destination - position
  signedAngle = angle(forward, toGoal)

Для reverse:
  rearAxle = position - forward * (wheelBase * 0.5)   ← тот же offset, что DriverContext
  toGoal = destination - rearAxle
  signedAngle = angle(-forward, toGoal)

Steer:
  steerFromLateral = clamp(signedAngle / 60°, -1, 1) * 0.7
  steerFromHeading = clamp(headingErr / 45°, -1, 1) * 0.5   (если задан heading)
  blend: lateral доминирует вдали, heading — вблизи
  curvature = tan(steer * maxSteerRad) / wheelBase

Speed (агрессивные капы):
  dist < 0.3м → max 0.8 км/ч
  dist < 0.6м → max 1.5 км/ч
  dist < 1.5м → max 3.0 км/ч
  curvature penalty: absSteer > 0.3 → speed *= 1-(steer-0.3)*0.6

Completion:
  dist < 0.3м && speed < 1км/ч && heading OK → IsComplete = true
  → DriverFSM ставит GoalLocked, Holding, Park()
```

---

## 10. MotionController — curvature → steer/throttle

**Файл:** `MotionController.cs`

### 10.1 Преобразование кривизны в угол руля (Ackermann)

```
rawSteerRad = atan(wheelBase * curvature)

          wheelBase
    ┌──────────────┐
    │              │
    │  ●           │  ● = rear axle reference point
    │   \          │
    │    \         │  R = поворотный радиус = 1 / curvature
    │     \        │
    │      \       │  tan(δ) = wheelBase / R = wheelBase * curvature
    │       \      │
    ▼        ●────▶  ● = front axle
    δ = atan(wheelBase * curvature)
```

### 10.2 Цепочка управления

```
curvature → rawSteerRad = atan(wheelBase * curvature)
         → steerTarget = clamp(rawSteerRad / maxSteeringAngleRad, -1, 1)
         → steerRate = steeringRateDegPerSec / 90 * dt
         → smoothedSteer = MoveTowards(prevSteer, steerTarget, steerRate)
         → SteeringDamping: absSpeed > 40 → dampFactor, steer *= (1 - damp*0.6)
         → VehicleCommand.Steer = clamp(smoothedSteer, -1, 1)

speed → speedError = desiredSpeed - signedCurrent
      → throttle/speed pid:
          error > 0.3  → throttle = error*0.04, brake=None
          error < -20  → throttle = 0, brake=Hard
          error < -8   → throttle = 0, brake=Soft
          error < -2   → throttle = 0, brake=Coast
          иначе        → throttle = error*0.02+0.02, brake=Coast
      → smoothedThrottle = MoveTowards(prev, target, 0.15)
      → reverse: throttle = -throttle
```

### 10.3 ResolvePhase

```
remainingDistance < 3м && IsArrivalManeuver → Parking
Parking / ApproachWithHeading / PostTurnAlignment → Parking
Unstuck → Recovery
Reverse / TurnAround / ThreePointTurn → Precision
default → Cruise
```

---

## 11. Safety — последний фильтр

### 11.1 VehicleSafetyController

Компонует лимитеры в цепочку:

```
MotionCommand → CommandSanitizer → DynamicsLimiter → StabilityLimiter
             → AirborneProtection → RecoveryProtection
             → VehicleCommand (финальный)
```

### 11.2 Лимитеры

- **CommandSanitizer** — санитизация значений (NaN, infinity → 0)
- **DynamicsLimiter** — ограничение ускорения/замедления по физическим возможностям
- **StabilityLimiter** — предотвращение опрокидывания: ограничение руля на высокой скорости/крене
- **AirborneProtection** — в воздухе: руль=0, газ=0
- **RecoveryProtection** — при опрокидывании: специальная команда восстановления

---

## 12. Критерии завершения

### 12.1 Завершение манёвра

```csharp
Maneuver.IsComplete(ManeuverContext ctx):
  - waypoints.Length == 0 → всегда true
  - иначе: flatDistance(position, lastWaypoint) ≤ completionDistance

ManeuverContext:
  Position, Forward, SpeedKmh, CompletionDistance, IsReversing
```

**CompletionDistance** зависит от контекста:
- Driving: `max(4, lookAhead * 0.6)`
- Arrival: `lookAhead * 0.35`

### 12.2 Завершение Reverse

```csharp
ReverseStateMachine:
  Stop: speed < 0.1км/ч && remaining < 0.6м && headingOK → Finished
```

Затем в `AdvanceManeuverIfComplete`: `isComplete = !m_ReverseDriver.IsActive`

### 12.3 Завершение прибытия (Holding)

```csharp
ArrivalController.HasArrived(position, yaw, destination, heading):
  flatDistance(position, destination) ≤ positionTolerance (0.6м)
  И (если heading задан) |deltaAngle(yaw, targetHeading)| ≤ headingTolerance (8°)

PrecisionArrivalController:
  dist < 0.3м && speed < 1км/ч && headingOK → IsComplete

После любого из условий:
  GoalLocked = true
  CurrentState = Holding
  ActiveStopReason = StopReason.Goal
  команда: Park() → HoldPosition = true, BrakeMode = Soft, Phase = Parking
```

---

## 13. Ключевые структуры данных

### NavigationRequest
```
Destination: Vector3
HeadingYaw: float?          (желаемый угол при прибытии)
SpeedMode: enum             (Creep/Slow/Medium/Fast)
FacingMode: enum            (None/UsePathFacing/FaceHeading/KeepCurrent)
AllowReverse: bool
AllowTurnAround: bool
AllowRepath: bool
MinArrivalDistance: float   (допуск позиции, по умолчанию 0.6м)
MinArrivalHeading: float    (допуск угла, по умолчанию 8°)
```

### DrivingPlan
```
Maneuvers: IReadOnlyList<Maneuver>
DrivingMode: Forward/Reverse/TurnAround
TotalCost: float
Feasibility: FeasibilityResult
FallbackDecision: ArrivalFallbackDecision
Segments: IReadOnlyList<ManeuverPlanSegment>
EstimatedDistance: float
ReverseDistance: float
TurnCount: int
Risk: float
```

### Maneuver (абстрактный базовый класс)
```
Type: VehicleManeuverType
Waypoints: Vector3[]
AllowReverse: bool
SpeedScale: float
LookAheadOverride: float?
IsArrivalManeuver: bool
IsComplete(ManeuverContext): bool
```

### VehicleCommand (результат конвейера)
```
Steer: float         (-1..+1, лево..право)
Throttle: float      (-1..+1, назад..вперёд)
BrakeMode: enum      (None/Coast/Soft/Hard)
FireHeld: bool
AimWorldPoint: Vector3
HoldPosition: bool   (удерживать позицию)
Phase: DrivingPhase  (Cruise/Precision/Parking/Recovery)
```

---

## 14. Диагностические логи

| Тег | Частота | Содержание |
|---|---|---|
| `[PathPlanner]` | при построении | NavMesh статус, fallback, точки |
| `[DrivingPlanner]` | при BuildPlan | кандидаты, cost, severity, выбор |
| `[ArrivalPlanner]` | при PlanArrival | dist, angle, side, status, стратегии |
| `[DriverFSM]` | при RebuildPlan/TickArrival | mode, маневры, GoalLocked |
| `[RevPathBuilder]` | при Build | maxAngle, smoothed?, length, start/end + DrawLine |
| `[RevPursuit]` | каждые 30 кадров | rearAxle, target, crossTrack, curv, smoothed, steerFrac/limit, expectedR/actualR |
| `[RevChain]` | каждые 30 кадров | desiredCurv → steerTarget → steerLimited → actualSteer |
| `[RevDriver]` | каждые 30 кадров | state, seg, remaining, pos, latErr, steer, speed, steerSatDur |
| `[RevSteerLimit]` | при clamp >20% | до/после, limit, speed |
| `[RevState]` | при переходах | переходы фаз |
