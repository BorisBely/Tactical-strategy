# Документация — Новая система навигации машины
# Версия 2.0 (реализовано: июль 2026)

---

## 1. Оглавление

1. [Обзор архитектуры](#2-обзор-архитектуры)
2. [Поток данных: от клика до колёс](#3-поток-данных-от-клика-до-колёс)
3. [Система приказов](#4-система-приказов)
4. [Очередь приказов](#5-очередь-приказов)
5. [Навигационный запрос](#6-навигационный-запрос)
6. [Планировщик пути](#7-планировщик-пути)
7. [Планировщик движения](#8-планировщик-движения)
8. [Проверка выполнимости](#9-проверка-выполнимости)
9. [Манёвры](#10-манёвры)
10. [Прибытие с направлением](#11-прибытие-с-направлением)
11. [Конечный автомат водителя](#12-конечный-автомат-водителя)
12. [Система восстановления](#13-система-восстановления)
13. [Визуализация и отладка](#14-визуализация-и-отладка)
14. [Физический слой](#15-физический-слой)
15. [Полный список файлов](#16-полный-список-файлов)
16. [Отчёт: что сделано и что нет](#17-отчёт-что-сделано-и-что-нет)

---

## 2. Обзор архитектуры

Система построена по принципу конвейера. Каждый слой отвечает за одну задачу и ничего не знает о слоях выше или ниже, кроме соседних.

```
Игрок (клик ПКМ, Shift+клик, тянуть с зажатой кнопкой)
  │
  ▼
VehicleController          — фасад. Принимает ввод, формирует приказ.
  │
  ▼
VehicleOrderQueue          — диспетчер. Хранит очередь приказов, продвигает их.
  │
  ▼
VehicleNavigation          — точка входа виртуального водителя. Владеет всеми подсистемами.
  │
  ├─► PathPlanner          — строит путь по NavMesh
  ├─► DrivingPlanner       — решает, как ехать (вперёд/назад/разворот)
  ├─► ManeuverFeasibilityChecker — проверяет, можно ли выполнить манёвр
  ├─► ScoringSystem         — оценивает варианты по стоимости и риску
  ├─► ManeuverPlanner       — генерирует waypoint-ы для каждого манёвра
  ├─► DriverFSM             — исполняет план: ведёт машину по манёврам
  │     ├─► PursuitController   — чистое преследование (руль + скорость)
  │     ├─► SpeedPlanner        — ограничения скорости
  │     ├─► MotionController    — переводит желаемое движение в газ/тормоз
  │     ├─► ArrivalController   — проверяет, приехали ли
  │     ├─► RecoveryController  — выход из застревания
  │     └─► ReverseDriver       — езда задним ходом
  │
  ▼
VehicleCommand             — пакет управления (руль, газ, тормоз, фаза движения)
  │
  ▼
VehicleBrain               — маршрутизатор команд
  │
  ▼
WheeledMotor               — физика колёс
```

**Ключевой принцип:** каждый слой получает данные от верхнего, обрабатывает и передаёт нижнему. Нижние слои ничего не знают о верхних.

---

## 3. Поток данных: от клика до колёс

### 3.1 Обычный клик ПКМ (замена цели)

```
1. VehicleController.IssueMoveOrder(Vector3 position)
2. → VehicleController.IssueMoveOrder(VehicleMoveGoal)
3.   → VehicleNavigation.SetDestination(VehicleMoveGoal)
4.     → m_OrderQueue.Clear()                    // очистить очередь
5.     → m_QueueAutoAdvance = true
6.     → SetDestinationFromGoal(goal)
7.       → NavigationRequest.FromPosition(...)    // создать запрос
8.       → m_FSM.SetDestination(request)          // запустить конечный автомат
9.         → DriverFSM: State = Driving
10.          → RebuildPlan()                      // построить путь и план
11.            → PathPlanner.BuildPath(from, to)   // NavMesh или прямой
12.            → DrivingPlanner.BuildPlan()        // выбрать режим (вперёд/назад/разворот)
13.              → генерирует 2-3 кандидата
14.              → проверяет каждый через FeasibilityChecker
15.              → оценивает через ScoringSystem
16.              → возвращает лучший план
17.            → ManeuverPlanner.BuildWaypoints()  // построить маршрутные точки
18.            → FeasibilityChecker.CheckPlan()    // финальная проверка
19.          → ExecuteManeuver()                   // исполнение
20.            → PursuitController.Tick()          // чистое преследование
21.            → SpeedPlanner.ComputeTargetSpeed()  // ограничение скорости
22.            → MotionController.Convert()        // в газ/тормоз/руль
23.              → VehicleCommand
24.                → VehicleBrain.SetCommand()
25.                  → WheeledMotor.TickDrive()
```

### 3.2 Shift+клик (добавление в очередь)

```
1. VehicleController.AppendMoveToQueue(Vector3 position, VehicleSpeedMode)
2. → VehicleController.EnqueueMoveOrder(VehicleMoveOrder)
3.   → VehicleNavigation.EnqueueOrder(order)
4.     → m_OrderQueue.Enqueue(order)              // добавить в конец очереди
5.     → TryPromoteNextOrder()                    // если нет текущего — начать
6.       → VehicleOrderQueue.PromoteNext()
7.         → order.MarkStarted()                  // отметить начало исполнения
8.         → возвращает order
9.       → SetDestinationFromOrder(order)         // запустить FSM
10.         → NavigationRequest.FromOrder(order)
11.           → m_FSM.SetDestination(request)
```

### 3.3 Завершение манёвра и авто-продвижение

```
1. DriverFSM.Tick()
2. → FSM достиг состояния Idle или Holding
3.   → TryPromoteNextOrder()                     // в VehicleNavigation
4.     → m_OrderQueue.MarkCurrentOrderCompleted()  // отметить завершение
5.     → m_OrderQueue.PromoteNext()                // взять следующий из очереди
6.       → order.MarkStarted()
7.     → SetDestinationFromOrder(next)             // начать следующий
```

---

## 4. Система приказов

### 4.1 VehicleOrderType
**Файл:** `Assets\_Scripts\Vehicle\Navigation\VehicleOrderType.cs`

Типы приказов, которые может отдать игрок или система:

| Значение | Смысл | Кто отдаёт |
|----------|-------|-----------|
| `Move` | Поехать в точку | Игрок (ПКМ) |
| `Stop` | Мягкая остановка, сохранить очередь | Игрок (клавиша S) |
| `Hold` | Держать позицию | Система / AI |
| `Park` | Приехать и встать по направлению | Игрок (ПКМ + тянуть) |
| `Reverse` | Ехать задним ходом | Система (recovery) |
| `Repath` | Перестроить маршрут | Система (recovery) |
| `EmergencyStop` | Экстренная остановка, очистить всё | Система / игрок (пробел) |

### 4.2 VehicleMoveOrder
**Файл:** `Assets\_Scripts\Vehicle\Navigation\VehicleMoveOrder.cs`

Приказ — это НЕ просто точка. Это полноценный объект с жизненным циклом.

**Поля:**

| Поле | Тип | Описание |
|------|-----|----------|
| `OrderId` | `long` | Уникальный номер приказа (автоинкремент) |
| `ParentOrderId` | `long?` | Родительский приказ (для цепочек) |
| `Type` | `VehicleOrderType` | Тип приказа |
| `State` | `OrderState` | Текущее состояние (см. ниже) |
| `Destination` | `Vector3` | Точка назначения |
| `HasDestination` | `bool` | Задана ли точка |
| `DesiredHeadingYaw` | `float` | Желаемый угол корпуса |
| `HasDesiredHeading` | `bool` | Задан ли угол |
| `FacingMode` | `ArrivalFacingMode` | Как встать в конечной точке |
| `SpeedMode` | `VehicleSpeedMode` | Режим скорости |
| `AllowReverse` | `bool` | Разрешён ли задний ход |
| `AllowTurnAround` | `bool` | Разрешён ли разворот |
| `AllowThreePointTurn` | `bool` | Разрешён ли трёхточечный разворот |
| `Priority` | `int` | Приоритет (больше = важнее) |
| `IsCancelable` | `bool` | Можно ли отменить |
| `TimeoutSeconds` | `float` | Таймаут (0 = бесконечно) |
| `CreatedTime` | `float` | Время создания |
| `SourceTag` | `string` | Кто создал (user, system, emergency, legacy-move-goal) |

**Жизненный цикл приказа:**

```
Pending ──► Executing ──► Completed
                │
                ├──► Aborted (отменён игроком или системой)
                └──► Expired  (истекло время)
```

Переходы:
- `Pending → Executing` — когда `PromoteNext()` делает приказ текущим
- `Executing → Completed` — когда FSM успешно доехал до цели
- `Executing → Aborted` — когда игрок нажал Stop, или система прервала
- `Pending → Aborted` — когда приказ удалён из очереди
- `Pending/Executing → Expired` — когда истекло время

**Статические фабрики:**

```csharp
VehicleMoveOrder.CreateMove(Vector3 destination, VehicleSpeedMode speedMode)
VehicleMoveOrder.CreateMove(Vector3 destination, float headingYaw, VehicleSpeedMode speedMode)
VehicleMoveOrder.CreateStop()
VehicleMoveOrder.CreateHold()
VehicleMoveOrder.CreateEmergencyStop()
VehicleMoveOrder.FromMoveGoal(VehicleMoveGoal goal)  // для обратной совместимости
```

### 4.3 OrderState
**Файл:** `Assets\_Scripts\Vehicle\Navigation\VehicleMoveOrder.cs`

```csharp
enum OrderState { Pending, Executing, Completed, Aborted, Expired, Interrupting }
```

---

## 5. Очередь приказов

### 5.1 VehicleOrderQueue
**Файл:** `Assets\_Scripts\Vehicle\Navigation\VehicleOrderQueue.cs`

Диспетчер очереди. Хранит цепочку приказов и управляет их жизненным циклом.

**Поля:**
- `m_Queue` — `Queue<VehicleMoveOrder>` — основная очередь
- `m_Current` — текущий исполняемый приказ
- `m_PendingInterrupt` — приоритетное прерывание (Stop / EmergencyStop)

**Свойства:**
- `Count` — сколько приказов в очереди
- `HasCurrent` — есть ли текущий исполняемый приказ
- `HasPendingInterrupt` — есть ли ожидающее прерывание
- `CurrentOrder` — текущий приказ
- `QueuedOrders` — `IReadOnlyList<VehicleMoveOrder>` — снимок очереди для отладки

**Методы:**

```csharp
// Добавление
void Enqueue(VehicleMoveOrder order)
  // EmergencyStop → очищает всё и ставит как прерывание
  // Stop → ставит как прерывание (сохраняет очередь)
  // Move → добавляет в конец, мержит близкие точки (замена последнего)

void EnqueueFront(VehicleMoveOrder order)
  // Вставить в начало очереди (важнее текущих)

// Управление
void Clear()                       // очистить всё
void CancelAll(string reason)      // отменить всё (Emergency)
void CancelCurrent(string reason)  // отменить только текущий (сохранить очередь)

// Навигация по очереди
bool TryPeek(out VehicleMoveOrder)   // посмотреть следующий
bool TryDequeue(out VehicleMoveOrder) // извлечь следующий
VehicleMoveOrder PromoteNext(float timeNow)  // продвинуть: завершить текущий, начать следующий
bool TryPromoteInterrupt(float timeNow)      // продвинуть прерывание в текущий

// Жизненный цикл
void MarkCurrentOrderStarted(float timeNow)    // отметить начало
void MarkCurrentOrderCompleted()               // отметить завершение
void MarkCurrentOrderAborted()                 // отметить отмену
void RemoveExpiredOrders(float timeNow)         // удалить просроченные

// Защита от дубликатов
// Если новый Move-приказ ближе чем m_OrderMergeDistance (2м) к последнему в очереди —
// последний заменяется, а не добавляется новый.
```

---

## 6. Навигационный запрос

### 6.1 NavigationRequest
**Файл:** `Assets\_Scripts\Vehicle\Navigation\NavigationRequest.cs`

Иммутабельная структура. Это то, что передаётся из слоя приказов в слой планирования. Планировщик не знает про `VehicleMoveOrder` — он работает только с `NavigationRequest`.

**Поля:**

| Поле | Тип | Описание |
|------|-----|----------|
| `Destination` | `Vector3` | Точка назначения |
| `HeadingYaw` | `float?` | Желаемый угол корпуса |
| `SpeedMode` | `VehicleSpeedMode` | Режим скорости |
| `FacingMode` | `ArrivalFacingMode` | Как встать в конечной точке |
| `AllowReverse` | `bool` | Разрешён ли задний ход |
| `AllowTurnAround` | `bool` | Разрешён ли разворот |
| `AllowRepath` | `bool` | Разрешено ли перестроение маршрута |
| `MinArrivalDistance` | `float` | Минимальная дистанция прибытия (по умолчанию 0.6м) |
| `MinArrivalHeading` | `float` | Допуск по углу прибытия (по умолчанию 8°) |

**Фабрики:**

```csharp
NavigationRequest.FromPosition(Vector3 dest, VehicleSpeedMode mode)
NavigationRequest.FromPositionAndHeading(Vector3 dest, float headingYaw, VehicleSpeedMode mode)
NavigationRequest.FromOrder(VehicleMoveOrder order)  // основной путь: приказ → запрос
```

---

## 7. Планировщик пути

### 7.1 PathPlanner
**Файл:** `Assets\_Scripts\Vehicle\Navigation\PathPlanner.cs`

Строит глобальный маршрут от текущей позиции до цели.

**Методы:**
```csharp
PathResult BuildPath(Vector3 from, Vector3 to)
  // Старая сигнатура — обёртка над новой с параметрами по умолчанию

PathResult BuildPath(Vector3 from, Vector3 to, PathBuildOptions options)
  // Новая сигнатура с опциями
  // 1. Пробует сэмплировать from и to на NavMesh
  // 2. Если оба на NavMesh — строит путь через NavMesh.CalculatePath
  // 3. Если путь частичный и AllowPartialPath=false — возвращает Invalid
  // 4. Если NavMesh недоступен и AllowDirectFallback=true — прямой путь [from, to]
  // 5. Если AllowDirectFallback=false — возвращает Invalid
```

### 7.2 PathBuildOptions
**Файл:** `Assets\_Scripts\Vehicle\Navigation\PathBuildOptions.cs`

| Поле | По умолчанию | Описание |
|------|-------------|----------|
| `AllowPartialPath` | `true` | Разрешить частичный NavMesh-путь |
| `AllowDirectFallback` | `true` | Разрешить прямой путь если NavMesh недоступен |
| `SampleRadiusFrom` | `3f` | Радиус поиска ближайшей точки NavMesh от начала |
| `SampleRadiusTo` | `4f` | Радиус поиска ближайшей точки NavMesh до цели |

Статические пресеты:
- `PathBuildOptions.Default` — всё разрешено
- `PathBuildOptions.SafeOnly` — только полный NavMesh-путь, без прямого
- `PathBuildOptions.ForReverse` — расширенные радиусы для заднего хода

### 7.3 PathResult
**Файл:** `Assets\_Scripts\Vehicle\Navigation\PathResult.cs`

| Поле | Тип | Описание |
|------|-----|----------|
| `Corners` | `Vector3[]` | Углы пути (точки поворота) |
| `Length` | `float` | Длина пути в метрах |
| `IsValid` | `bool` | Валиден ли путь |
| `IsPartial` | `bool` | Частичный ли путь (NavMesh не полный) |
| `UsedDirectFallback` | `bool` | Использован ли прямой путь вместо NavMesh |

---

## 8. Планировщик движения

### 8.1 DrivingPlanner
**Файл:** `Assets\_Scripts\Vehicle\Navigation\DrivingPlanner.cs`

**Это ключевое изменение версии 2.0.** Раньше планировщик выбирал один режим (вперёд/назад/разворот) по первому углу пути. Теперь он генерирует 2-3 кандидата, проверяет каждый и выбирает лучший.

**Поля:**
- `m_DecisionEvaluator` — проверяет безопасность режима по геометрии
- `m_Feasibility` — проверяет выполнимость манёвров

**Алгоритм `BuildPlan()`:**

```
1. Получить путь (PathResult) — он уже построен снаружи.
2. Сгенерировать кандидатов:
   a. ForwardCandidate     — всегда (едем вперёд по пути)
   b. ReverseCandidate     — если геометрия позволяет (есть место сзади)
   c. TurnAroundCandidate  — если геометрия позволяет (хватает места для разворота)

3. Для каждого кандидата:
   a. Построить последовательность манёвров
   b. Добавить финальный манёвр (парковка / подъезд с heading)
   c. Проверить выполнимость → FeasibilityResult
   d. Посчитать стоимость → ScoringSystem.ScoreCandidate()

4. Отсортировать по стоимости, выбрать самый дешёвый валидный.
5. Если все невалидны — вернуть Forward как fallback.
```

**Методы:**
```csharp
void SetFeasibility(ManeuverFeasibilityChecker)  // инъекция проверщика
DrivingPlan BuildPlan(NavigationRequest, PathResult, FeedbackState, ...)  // основной метод
```

### 8.2 DrivingPlan
**Файл:** `Assets\_Scripts\Vehicle\Navigation\DrivingPlan.cs`

| Поле | Тип | Описание |
|------|-----|----------|
| `Maneuvers` | `IReadOnlyList<Maneuver>` | Последовательность манёвров |
| `Reason` | `string` | Пояснение выбора |
| `IsValid` | `bool` | Есть ли манёвры |
| `DrivingMode` | `VehicleDrivingMode` | Выбранный режим (Forward/Reverse/TurnAround) |
| `TotalCost` | `float` | Стоимость плана (меньше = лучше) |

### 8.3 ScoringSystem
**Файл:** `Assets\_Scripts\Vehicle\Navigation\ScoringSystem.cs`

Статический класс для оценки вариантов.

**Методы:**
```csharp
DriverIntent Evaluate(DriverContext ctx)  // старый метод (сохранён для совместимости)

float ScoreCandidate(DriverIntent intent, float pathLength, int turnCount, FeasibilityResult feasibility)
  // Новый метод для оценки кандидата
  // Формула: pathLength * 1.0 + turnCount * 2.0 + штрафы за режим
  //   Reverse: +10
  //   TurnAround: +15
  //   ApplyRiskPenalty: невалидный → MaxValue, риск → +25 за единицу риска

float ApplyRiskPenalty(float baseScore, FeasibilityResult feasibility)
  // Добавляет штраф за риск: RiskScore * 25 + штрафы за типы коллизий
```

---

## 9. Проверка выполнимости

### 9.1 ManeuverFeasibilityChecker
**Файл:** `Assets\_Scripts\Vehicle\Navigation\ManeuverFeasibilityChecker.cs`

**Это новый слой безопасности.** Проверяет, можно ли физически выполнить манёвр ДО того, как машина начнёт движение.

**Константы:**
- Минимальный передний зазор: 1.8м
- Минимальный задний зазор: 1.8м
- Минимальный боковой зазор: 1.0м
- Минимальная ширина коридора для заднего хода: 2.5м

**Методы:**

```csharp
FeasibilityResult CheckPlan(DrivingPlan, NavigationContext, VehicleParameters)
  // Проверяет ВЕСЬ план: проходит по всем манёврам, возвращает первый Invalid
  // или наихудший по риску.

FeasibilityResult CheckPlan(DrivingPlan, VehicleLocalGeometry.Sample, float turnRadius)
  // Облегчённая версия для вызова из DrivingPlanner (без NavigationContext)

FeasibilityResult CheckForwardPath(VehicleLocalGeometry.Sample geometry)
  // Проверяет: нет ли обрыва спереди, хватает ли зазора, не слишком ли узко.

FeasibilityResult CheckReversePath(VehicleLocalGeometry.Sample geometry)
  // Проверяет: нет ли обрыва сзади, хватает ли зазора, не слишком ли узкий коридор.

FeasibilityResult CheckTurnAroundArc(float turnSign, float turnRadius, VehicleLocalGeometry.Sample geometry)
  // Проверяет: хватает ли места спереди и сзади, смотрит на диагональные зазоры,
  // проверяет CanFitTurnRadius.

FeasibilityResult CheckParkingSpot(Vector3 destination, float targetYaw, VehicleLocalGeometry.Sample geometry)
  // Проверяет: нет ли обрывов около места парковки, хватает ли места.
```

### 9.2 FeasibilityResult
**Файл:** `Assets\_Scripts\Vehicle\Navigation\FeasibilityResult.cs`

Единый вердикт проверки. Собирает информацию из геометрии и предсказания траектории.

| Поле | Тип | Описание |
|------|-----|----------|
| `IsValid` | `bool` | Можно ли выполнить манёвр вообще |
| `IsFullySafe` | `bool` | Полностью безопасно (без рисков) |
| `MinClearance` | `float` | Минимальный зазор в метрах |
| `RiskScore` | `float` | Оценка риска (0 = безопасно, 1+ = рискованно) |
| `HasFrontCollision` | `bool` | Есть риск столкновения спереди |
| `HasRearCollision` | `bool` | Есть риск столкновения сзади |
| `HasSideCollision` | `bool` | Есть риск бокового столкновения |
| `HasCliffRisk` | `bool` | Есть риск падения с обрыва |
| `HasSlopeRisk` | `bool` | Есть риск на слишком крутом склоне |
| `HasNarrowPassage` | `bool` | Слишком узкий проход |
| `FailureReason` | `string` | Причина отказа (понятный текст) |
| `FailurePoint` | `Vector3` | Точка, где обнаружена проблема |

**Статические фабрики:**
```csharp
FeasibilityResult.Valid           // всё хорошо
FeasibilityResult.Invalid(reason) // нельзя выполнить
FeasibilityResult.Invalid(reason, point) // нельзя с указанием точки
```

### 9.3 VehicleLocalGeometry (расширен)
**Файл:** `Assets\_Scripts\Vehicle\Navigation\VehicleLocalGeometry.cs`

**Добавленные поля в Sample:**

| Поле | Описание |
|------|----------|
| `FrontDiagonalLeftClearance` | Зазор спереди-слева (30° от носа) |
| `FrontDiagonalRightClearance` | Зазор спереди-справа (30° от носа) |
| `RearDiagonalLeftClearance` | Зазор сзади-слева (150° от носа) |
| `RearDiagonalRightClearance` | Зазор сзади-справа (150° от носа) |
| `HasDropAhead` | Есть ли обрыв спереди (луч вниз на 3м) |
| `HasDropBehind` | Есть ли обрыв сзади |
| `HasNarrowPassage` | Узкий проход (оба боковых зазора < 2м) |

**Новые статические методы:**
```csharp
bool CanFitTurnRadius(float radius, Sample geometry)    // помещается ли радиус разворота
bool HasSafeBackingSpace(Sample geometry, float minDist) // безопасно ли сдать назад
bool HasSafeForwardSpace(Sample geometry, float minDist) // безопасно ли ехать вперёд
```

### 9.4 TrajectoryPrediction (расширен)
**Файл:** `Assets\_Scripts\Vehicle\Navigation\TrajectoryPrediction.cs`

**Добавленные поля в PredictionResult:**

| Поле | Описание |
|------|----------|
| `RiskScore` | Оценка риска: 0 при TTC>0.5с, 1 при TTC=0 |
| `CollisionPoint` | Точка предполагаемого столкновения |
| `CollisionStepIndex` | На каком шаге предсказания обнаружено |

**Новые методы:**
```csharp
PredictionResult PredictForManeuver(Maneuver, DriverContext, VehicleParameters)
  // Диспетчер: выбирает PredictForward / PredictReverse / PredictTurnAround по типу

PredictionResult PredictForward(DriverContext, VehicleParameters)
  // Прямолинейное движение вперёд на крейсерской скорости

PredictionResult PredictReverse(DriverContext, VehicleParameters)
  // Прямолинейное движение назад (с разворотом контекста на 180°)

PredictionResult PredictTurnAround(DriverContext, VehicleParameters, float turnSign)
  // Движение по дуге разворота с заданным радиусом
```

---

## 10. Манёвры

### 10.1 Общая архитектура манёвров

Манёвр — это атомарная единица плана движения. Каждый манёвр описывает ЧТО делать (тип, скорость, разрешения), а КАК ехать определяет `PursuitController` через waypoint-ы.

**Базовый класс `Maneuver`:**
```csharp
abstract class Maneuver {
    VehicleManeuverType Type       // тип манёвра
    IReadOnlyList<Vector3> Waypoints  // маршрутные точки
    bool AllowReverse              // разрешён ли задний ход
    float SpeedScale               // множитель скорости (0.15 = 15% от максимальной)
    float? LookAheadOverride       // фиксированная дистанция преследования
    bool IsArrivalManeuver         // финальный ли это манёвр (для обработки прибытия)
}
```

### 10.2 Типы манёвров

**ForwardManeuver** — движение вперёд.
- `AllowReverse = false`
- `SpeedScale = 1.0`
- Waypoints: все углы NavMesh-пути
- **Когда используется:** основной режим движения, цель впереди.

**ReverseManeuver** — движение назад (устаревший, заменён на ReverseIntentManeuver).
- `AllowReverse = true`
- Waypoints: текущая позиция + первая точка пути

**ReverseIntentManeuver** — полноценный задний ход (новый).
- `AllowReverse = true`
- Строит свой путь через `ReversePathBuilder`
- Исполняется через отдельный `ReverseDriver` (собственный конечный автомат)
- **Когда используется:** цель сзади и расстояние небольшое.

**TurnAroundManeuver** — разворот на 180°.
- `AllowReverse = false`
- `SpeedScale = 0.5`
- Параметр: `TurnSign` (-1 = влево, +1 = вправо)
- Waypoints: дуга из 30+ точек, построенная `PathSmoother.GenerateTurnaroundTrajectory()`
- **Как происходит разворот:**
  1. Машина начинает движение по дуге с прогрессивным поворотом руля
  2. Фазы: вход (20°) → поворот (140°) → выход (20°)
  3. Руль плавно нарастает и спадает между фазами
  4. В конце добавляется прямолинейный отрезок для выравнивания

**ThreePointTurnManeuver** — трёхточечный разворот в узком месте.
- `AllowReverse = true`
- `SpeedScale = 0.3`
- Waypoints: 4 точки (вперёд → назад-вбок → вперёд)
- **Как происходит:** сдал назад под углом, проехал вперёд — развернулся в ограниченном пространстве.

**ParkingManeuver** — парковка с выравниванием по heading.
- `AllowReverse = true`
- `SpeedScale = 0.22`
- `LookAheadOverride = 1.6`
- `IsArrivalManeuver = true`
- Параметр: `TargetHeadingYaw`
- Waypoints: 3 точки (from → lerp(65%) → to)
- **Как происходит парковка:**
  1. Машина подъезжает к точке
  2. `DriverFSM.TickArrival()` обнаруживает, что дистанция < `HeadingBlendStartDistance` (6м)
  3. Начинает подмешивать curvature для выравнивания по heading
  4. Скорость ограничивается `HeadingBlendMaxSpeedKmh` (5 км/ч)
  5. Когда дистанция < `PositionTolerance` И угол < `HeadingToleranceDeg` → Holding
  6. `GoalLimiter` в `SpeedPlanner` автоматически снижает скорость при подъезде

**ApproachWithHeadingManeuver** — подъезд с гарантированным выходом на heading (новый).
- `AllowReverse = true`
- `SpeedScale = 0.28`
- `LookAheadOverride = 2.0`
- `IsArrivalManeuver = true`
- Параметры: `Destination`, `TargetHeadingYaw`
- Waypoints: 4-5 точек, где последний отрезок идёт строго по целевому направлению
- **Как происходит:**
  1. `PathSmoother.GenerateApproachWithHeadingArc()` строит маршрут:
     - Точка входа (entry point): на расстоянии min(3м, turnRadius) ПЕРЕД целью, строго на линии heading
     - Промежуточные точки от текущей позиции к точке входа
     - Финальная точка — сама цель
  2. Машина едет к точке входа, затем прямо к цели уже по правильному heading
  3. `GoalLimiter` форсирует creep-скорость для точности
  4. **Отличие от ParkingManeuver:** ApproachWithHeading гарантирует правильную геометрию подъезда, а не просто «приехал — довернул»

**UnstuckManeuver** — выход из застревания.
- `AllowReverse = false`
- Параметр: `SteerSign` (чередуется при повторных попытках)
- Waypoints: дуга выхода
- **Как происходит:** машина поворачивает руль и даёт газ, пытаясь выехать из заблокированного положения.

**StopManeuver** — остановка.
- Waypoints: текущая позиция (одна точка)
- Используется для немедленной остановки без продолжения движения.

### 10.3 Как манёвры создаются и исполняются

```
DrivingPlanner.BuildPlan()
  │
  ├─► BuildForwardCandidate()
  │     └─► maneuvers = [ForwardManeuver] + AppendArrivalManeuver()
  │
  ├─► BuildReverseCandidate()
  │     └─► maneuvers = [ReverseIntentManeuver] + AppendArrivalManeuver()
  │
  └─► BuildTurnAroundCandidate()
        └─► maneuvers = [TurnAroundManeuver, ForwardManeuver] + AppendArrivalManeuver()

AppendArrivalManeuver() выбирает финальный манёвр по FacingMode:
  FaceHeading   → ApproachWithHeadingManeuver
  UsePathFacing → ParkingManeuver (с направлением последнего сегмента пути)
  KeepCurrent   → без финального манёвра
  None          → старый fallback: ParkingManeuver если heading задан и угол > 18°

ManeuverPlanner.BuildWaypoints() генерирует waypoint-ы для каждого манёвра:
  Forward         → все углы пути
  Reverse         → текущая позиция + первая точка
  TurnAround      → PathSmoother.GenerateTurnaroundTrajectory()
  ThreePointTurn  → PathSmoother.GenerateThreePointWaypoints()
  Parking         → PathSmoother.GenerateParkingWaypoints()
  ApproachHeading → PathSmoother.GenerateApproachWithHeadingArc()
  Unstuck         → PathSmoother.GenerateUnstuckWaypoints()
  Stop            → текущая позиция

DriverFSM.ExecuteManeuver():
  1. Если ReverseIntentManeuver → делегирует ReverseDriver.Tick()
  2. Иначе:
     a. PursuitController.Tick() — находит точку преследования, считает curvature и скорость
     b. SpeedPlanner.ComputeTargetSpeed() — применяет лимиты
     c. MotionController.Convert() → VehicleCommand
```

---

## 11. Прибытие с направлением

### 11.1 ArrivalFacingMode
**Файл:** `Assets\_Scripts\Vehicle\Navigation\ArrivalFacingMode.cs`

| Значение | Поведение |
|----------|----------|
| `None` | Просто приехать в точку. Ориентация не важна. |
| `UsePathFacing` | Встать по направлению последнего сегмента NavMesh-пути. |
| `FaceHeading` | Встать капотом строго по заданному углу (от игрока через drag). |
| `KeepCurrent` | Не менять ориентацию. |

### 11.2 ArrivalCriteria
**Файл:** `Assets\_Scripts\Vehicle\Navigation\ArrivalCriteria.cs`

Собирает все настройки прибытия в один объект.

| Поле | По умолчанию | Описание |
|------|-------------|----------|
| `PositionTolerance` | `0.6f` | Допуск по расстоянию (метры) |
| `HeadingToleranceDeg` | `8f` | Допуск по углу (градусы) |
| `RequireFaceHeading` | `false` | Требуется ли выравнивание по heading |
| `HeadingBlendStartDistance` | `6f` | С какого расстояния начинать подмешивать curvature для выравнивания |
| `HeadingBlendMaxSpeedKmh` | `5f` | Максимальная скорость при выравнивании |
| `TargetForward` | `Vector3` | Вектор желаемого направления |
| `HasTargetForward` | `bool` | Задан ли вектор |

Статическая фабрика: `ArrivalCriteria.FromRequest(NavigationRequest)` — создаёт критерии из навигационного запроса.

### 11.3 Как происходит прибытие с heading

```
1. Игрок зажимает ПКМ, тянет — появляется стрелка направления.
2. Создаётся VehicleMoveOrder с FacingMode = FaceHeading и DesiredHeadingYaw.
3. NavigationRequest.FromOrder() пробрасывает FacingMode в запрос.
4. DrivingPlanner.AppendArrivalManeuver() видит FaceHeading → создаёт ApproachWithHeadingManeuver.
5. ManeuverPlanner строит waypoint-ы с финальным прямым участком по heading.
6. PursuitController ведёт машину по waypoint-ам.
7. SpeedPlanner.GoalLimiter форсирует creep-скорость (3 км/ч) для точности.
8. Когда дистанция до цели < HeadingBlendStartDistance (6м):
   a. Вычисляется ошибка heading = разница между текущим и желаемым углом
   b. Вычисляется curvature для доворота = ошибка * Deg2Rad / 5
   c. Curvature подмешивается с весом = 1 - distance/6
   d. Скорость ограничивается до 5 км/ч
9. Когда дистанция < PositionTolerance И угол < HeadingToleranceDeg → машина остановилась правильно.
10. FSM переходит в Holding, применяется HoldPosition.
```

---

## 12. Конечный автомат водителя

### 12.1 DriverFSM
**Файл:** `Assets\_Scripts\Vehicle\Navigation\DriverFSM.cs`

**Состояния:**

| Состояние | Описание |
|-----------|----------|
| `Idle` | Нет цели, машина стоит |
| `Driving` | Движение по манёврам |
| `Arrival` | Финальная фаза — подъезд к цели |
| `Holding` | Цель достигнута, удержание позиции |
| `Recovery` | Выход из застревания |
| `EmergencyStop` | Экстренное торможение |

**Основной цикл `Tick()`:**
```
1. Проверить EmergencyStop
2. Если Idle → Idle()
3. Если Holding → TickHolding()
4. Если PlanDirty или нет плана → RebuildPlan()
5. Проверить Recovery:
   a. RecoveryController.EvaluateAndGetManeuver() → (RecoveryAction, Maneuver?)
   b. RebuildPath → m_PlanDirty=true, BrakeToStop
   c. AbortAndStop → State=Idle, сброс счётчиков
   d. UnstuckRock → State=Recovery, исполнить UnstuckManeuver
6. Проверить завершение манёвра → AdvanceManeuverIfComplete()
7. Если все манёвры пройдены → State=Arrival
8. Если Arrival → TickArrival()
9. Исполнить текущий манёвр:
   a. Если ReverseIntentManeuver → ReverseDriver.Tick()
   b. Иначе → PursuitController + SpeedPlanner + MotionController
```

**Ключевые методы:**
```csharp
void SetDestination(NavigationRequest)  // начать движение к цели
void Stop()                             // остановка, сброс контекста
void EmergencyStop(StopReason)          // экстренная остановка
VehicleCommand Tick()                   // главный метод (каждый FixedUpdate)

private void RebuildPlan()              // построить путь + план + waypoint-ы
private VehicleCommand ExecuteManeuver(Maneuver)  // исполнить манёвр
private VehicleCommand TickArrival()     // финальная фаза прибытия
private VehicleCommand TickHolding()     // удержание позиции
```

### 12.2 NavigationContext
Единый контекст, передаваемый всем подсистемам каждый кадр.

**Поля:**
- `Params` — `VehicleParameters` (длина, ширина, скорость, руль)
- `State` — `FeedbackState` (позиция, скорость, застревание, геометрия)
- `Request` — `NavigationRequest` (текущая цель)
- `Path` — `PathResult` (построенный путь)
- `Plan` — `DrivingPlan` (план манёвров)
- `CurrentManeuverIndex` — индекс текущего манёвра
- `RemainingDistance` — оставшееся расстояние
- `Memory` — `VehicleDriverMemory` (история решений, анти-осцилляция)

---

## 13. Система восстановления

### 13.1 RecoveryController
**Файл:** `Assets\_Scripts\Vehicle\Navigation\RecoveryController.cs`

**Методы:**
```csharp
(RecoveryAction Action, Maneuver Maneuver) EvaluateAndGetManeuver(FeedbackState, VehicleDriverMemory)
  // Главный метод. Вызывает RecoveryDecision.Evaluate(), возвращает действие и манёвр.
  // UnstuckRock → создаёт UnstuckManeuver
  // ReverseOut/RebuildPath/AbortAndStop → возвращает только действие (манёвр = null)

bool CheckRecoveryComplete(FeedbackState)  // проверка: скорость > 2.5 км/ч И зазор > 2м
```

### 13.2 RecoveryDecision
**Файл:** `Assets\_Scripts\Vehicle\Navigation\RecoveryDecision.cs`

Статический метод `Evaluate()` принимает решение по приоритету (от наиболее специфичного к общему):

```
1. UnstuckAttempts >= 6 → AbortAndStop (сдаёмся)
2. UnstuckAttempts >= 4 → RebuildPath (перестроить маршрут)
3. Спереди блок И сзади свободно И >= 1 попытки → ReverseOut (сдать назад)
4. IsStuck → UnstuckRock (раскачка)
5. Иначе → None
```

Типы действий (`RecoveryAction`):

| Действие | Описание |
|----------|----------|
| `None` | Ничего не делать |
| `UnstuckRock` | Раскачка вперёд-назад (текущее поведение) |
| `ReverseOut` | Сдать назад и перестроить путь |
| `CreepAside` | Сместиться в сторону |
| `RebuildPath` | Перестроить NavMesh-путь |
| `AbortAndStop` | Остановиться и сдаться |

### 13.3 VehicleDriverMemory (расширен)
**Файл:** `Assets\_Scripts\Vehicle\Navigation\VehicleDriverMemory.cs`

Добавленные счётчики:
- `FeasibilityFailures` — сколько раз план не прошёл проверку выполнимости
- `RecoveryCycles` — сколько циклов восстановления было
- `UnstuckAttempts` — сколько попыток раскачки (уже был)

Методы:
```csharp
void RecordFeasibilityFailure()    // +1 к счётчику ошибок
void RecordRecoveryCycle()         // +1 к счётчику циклов
void ResetRecoveryCounters()       // сброс всех recovery-счётчиков
```

Все счётчики сбрасываются в `ResetForNewOrder()` при новом приказе.

---

## 14. Визуализация и отладка

### 14.1 VehicleNavigationDebugDrawer (расширен)
**Файл:** `Assets\_Scripts\Vehicle\Navigation\VehicleNavigationDebugDrawer.cs`

**Существующие режимы отрисовки:**
- `m_DrawNavMeshPath` — синие линии и сферы по углам NavMesh-пути
- `m_DrawManeuverWaypoints` — цветные сферы waypoint-ов (зелёные=вперёд, фиолетовые=назад, оранжевые=разворот)
- `m_DrawPursuitTarget` — жёлтая сфера точки преследования + пунктир + перекрёстная ошибка
- `m_DrawCurvatureArc` — дуга поворота (красная=крутой, зелёная=плавный)
- `m_DrawGeometryProbes` — 4 луча (перед/зад/лево/право) с цветом по зазору
- `m_DrawVehicleInfo` — состояние, скорость, газ, руль над машиной
- `m_DrawDestination` — оранжевый крест на цели + стрелка heading
- `m_DrawLookAheadRing` — кольца дистанции преследования

**Новые режимы:**

`m_DrawDiagonalProbes` — диагональные лучи (±30° вперёд, ±150° назад). Цвет как у основных: зелёный > 4м, жёлтый > 2м, красный < 2м.

`m_DrawFeasibilityInfo` — статус проверки выполнимости над машиной:
- Зелёный: `F:SAFE`
- Жёлтый: `F:RISK 0.3 clr=2.1m`
- Красный: `F:INVALID (drop ahead)`

`m_DrawQueuePreview` — список приказов в очереди:
```
Очередь (3):
  [0] Move → (12, 0, 34) st=Pending
  [1] Move → (45, 0, 12) st=Pending
  [2] Park → (67, 0, 89) st=Pending
```

**Логирование:**
- `m_LogPlanRebuild` — детальный лог при перестроении плана (режим, дистанция, манёвры, геометрия)
- `m_LogManeuverTransitions` — лог при смене манёвра
- `m_LogPursuitEveryFrame` / `m_LogPursuitPeriodSeconds` — периодический лог преследования
- `m_LogArrival` — лог при смене состояния FSM
- `m_LogGeometry` — лог геометрии раз в 60 кадров

---

## 15. Физический слой

### 15.1 VehicleCommand (расширен)
**Файл:** `Assets\CombatVehicleSystem\Scripts\Core\VehicleCommand.cs`

**Новые поля:**

| Поле | Тип | Описание |
|------|-----|----------|
| `HoldPosition` | `bool` | Активно удерживать позицию (тормоз + нулевой газ). Отличается от пассивного coast. |
| `Phase` | `DrivingPhase` | Фаза движения (см. ниже) |

**DrivingPhase — фазы движения:**
| Значение | Когда используется |
|----------|-------------------|
| `Cruise` | Обычное движение вперёд |
| `Precision` | Точное маневрирование: задний ход, разворот, трёхточечный разворот |
| `Parking` | Парковка, подъезд с heading |
| `Recovery` | Выход из застревания |

Фаза задаётся в `MotionController.ResolvePhase()` на основе типа текущего манёвра и автоматически пробрасывается в `VehicleCommand`.

### 15.2 MotionController (расширен)
**Файл:** `Assets\_Scripts\Vehicle\Navigation\MotionController.cs`

**Новые методы:**

```csharp
VehicleCommand HoldInPlace()
  // Активное удержание позиции:
  //   Steer = текущий (не меняется)
  //   Throttle = 0
  //   BrakeMode = Soft
  //   HoldPosition = true
  //   Phase = Parking

VehicleCommand Park()
  // Парковочная остановка:
  //   Вызывает HoldInPlace()
  //   Дополнительно: плавно выравнивает руль к нулю
  //   Phase = Parking
```

**Модификация `Convert()`:** теперь устанавливает `Phase` через `ResolvePhase(ctx)`.

**`BrakeToStop()`:** теперь также устанавливает `Phase` из контекста.

### 15.3 GoalLimiter (расширен)
**Файл:** `Assets\_Scripts\Vehicle\Navigation\Speed\GoalLimiter.cs`

Добавлена проверка типа манёвра: если текущий манёвр — `ApproachWithHeading` или `Parking`, скорость принудительно ограничивается до `m_CreepSpeedKmh` (3 км/ч) независимо от расстояния до цели.

### 15.4 DriverFSM.TickHolding()
Упрощён до одной строки: `return m_Motion.HoldInPlace()`. Раньше был ручной if/else с проверкой скорости и созданием команды вручную.

---

## 16. Полный список файлов

### Новые файлы (созданы в ходе рефакторинга)

| Файл | Назначение | Часть |
|------|-----------|------|
| `Navigation/VehicleOrderType.cs` | Типы приказов (Move, Stop, Hold, Park...) | 1 |
| `Navigation/ArrivalFacingMode.cs` | Режимы ориентации при прибытии | 1 |
| `Navigation/VehicleMoveOrder.cs` | Класс приказа с жизненным циклом | 1 |
| `Navigation/VehicleOrderQueue.cs` | Диспетчер очереди приказов | 1 |
| `Navigation/ArrivalCriteria.cs` | Конфигурация критериев прибытия | 2 |
| `Navigation/ApproachWithHeadingManeuver.cs` | Манёвр подъезда с heading | 2 |
| `Navigation/FeasibilityResult.cs` | Вердикт проверки выполнимости | 3 |
| `Navigation/ManeuverFeasibilityChecker.cs` | Проверщик выполнимости манёвров | 3 |
| `Navigation/PathBuildOptions.cs` | Опции построения пути | 4 |
| `Navigation/RecoveryDecision.cs` | Выбор стратегии выхода из застревания | 5 |

### Изменённые файлы

| Файл | Что изменилось | Части |
|------|---------------|-------|
| `Navigation/NavigationRequest.cs` | +FacingMode, +Allow*, +MinArrival*, +FromOrder() | 1, 2 |
| `Navigation/VehicleNavigation.cs` | +Очередь, +EnqueueOrder, +TryPromoteNextOrder, +LastFeasibility | 1, 3, 4 |
| `VehicleController.cs` | +EnqueueMoveOrder, +AppendMoveToQueue, +StopCurrentOrder, +ClearOrders | 1 |
| `Navigation/DriverFSM.cs` | +Recovery-логика, +Feasibility, +TickHolding через HoldInPlace, +TickArrival через ArrivalCriteria | 2, 3, 5, 6 |
| `Navigation/VehicleManeuverType.cs` | +ApproachWithHeading | 2 |
| `Navigation/PathPlanner.cs` | +BuildPath с PathBuildOptions | 4 |
| `Navigation/PathResult.cs` | +UsedDirectFallback | 4 |
| `Navigation/DrivingPlan.cs` | +TotalCost, +DrivingMode | 4 |
| `Navigation/DrivingPlanner.cs` | Полная переработка: многокандидатная логика | 4 |
| `Navigation/ScoringSystem.cs` | +ScoreCandidate, +ApplyRiskPenalty | 3, 4 |
| `Navigation/ArrivalController.cs` | +HasArrived с ArrivalCriteria, +HasCorrectHeading, +IsFacingDestination | 2 |
| `Navigation/ManeuverPlanner.cs` | +case ApproachWithHeading | 2 |
| `Navigation/PathSmoother.cs` | +GenerateApproachWithHeadingArc | 2 |
| `Navigation/VehicleLocalGeometry.cs` | +Диагонали, +Обрывы, +CanFitTurnRadius, +HasSafe*Space | 3 |
| `Navigation/TrajectoryPrediction.cs` | +PredictForManeuver, +PredictForward/Reverse/TurnAround, +RiskScore | 3 |
| `Navigation/RecoveryController.cs` | +EvaluateAndGetManeuver | 5 |
| `Navigation/VehicleDriverMemory.cs` | +FeasibilityFailures, +RecoveryCycles, +Record* | 5 |
| `Navigation/DriverRecovery.cs` | +UnstuckRock, +ReverseOut, +CreepAside, +RebuildPath, +AbortAndStop в enum | 5 |
| `Navigation/VehicleNavigationDebugDrawer.cs` | +Диагонали, +Feasibility, +Очередь | 5 |
| `Navigation/VehicleOrderQueue.cs` | +QueuedOrders свойство | 5 |
| `Core/VehicleCommand.cs` | +HoldPosition, +Phase (DrivingPhase) | 6 |
| `Navigation/MotionController.cs` | +HoldInPlace(), +Park(), +ResolvePhase() | 6 |
| `Navigation/Speed/GoalLimiter.cs` | +Проверка манёвра для creep-скорости | 6 |

---

## 17. Отчёт: что сделано и что нет

### Сделано (все 8 этапов)

1. **Очередь приказов** — можно ставить цепочки команд, Shift+клик добавляет в очередь, EmergencyStop прерывает всё.
2. **Прибытие с направлением** — машина паркуется капотом по стрелке, FacingMode управляет поведением.
3. **Проверка выполнимости** — перед движением проверяется геометрия, обрывы, зазоры. Невыполнимые манёвры отвергаются.
4. **Умный выбор маршрута** — просчитывается 2-3 варианта (вперёд/назад/разворот), выбирается лучший по стоимости и безопасности.
5. **Восстановление** — умный выход из застревания: раскачка → задний ход → перестроение → остановка.
6. **Визуализация** — диагональные лучи, статус проверки, очередь приказов.
7. **Финальный регулятор** — фазы движения (Cruise/Precision/Parking/Recovery), активное удержание позиции, creep при парковке.
8. **Обратная совместимость** — старые команды (один клик ПКМ) работают без изменений.

### Не сделано (отложено осознанно)

| Пункт | Причина |
|-------|---------|
| `CreepForwardManeuver` — медленный подкат для тесных мест | Отложен. Частично заменён creep-режимом в GoalLimiter. |
| `WaitInPlaceManeuver` — держать позицию без выхода из FSM | Отложен. Функционально покрыто Holding + HoldPosition. |
| `PullAsideManeuver` — съехать в сторону если дорога блокирована | Отложен. Требует сложной геометрии объезда. |
| `RepathManeuver` — перестроить путь если текущий не проходит | Отложен. Частично покрыто RecoveryAction.RebuildPath. |
| `AbortToStopManeuver` — быстро но безопасно остановиться | Отложен. Покрыто BrakeToStop + EmergencyStop. |
| `RecoverAndTurnManeuver` — раскачка + выбор нового направления | Отложен. Покрыто RecoveryDecision с эскалацией. |
| `RouteVisualizer` — отдельная система визуализации маршрута | Заменено расширением существующего DebugDrawer. |
| `ManeuverPlanSegment` — метаданные сегментов манёвра | Отложен. Без потребителей (визуализация, отладка) не нужен. |
| `PathResult.EstimatedRisk` — оценка риска на уровне пути | Отложен. Дублирует FeasibilityResult. |
| `VehicleLocalGeometry.CanFitVehicleBox` — проверка вписывания габаритов | Отложен. Слишком сложная геометрия для текущего этапа. |
| `ReversePlan` — расширение ReverseDriver | Отложен. ReverseDriver уже работает хорошо. |

---

## 18. Итоговая схема системы

```
┌─────────────────────────────────────────────────┐
│                  ИГРОК / AI                       │
│   ПКМ = замена цели                               │
│   Shift+ПКМ = добавить в очередь                   │
│   ПКМ+тянуть = цель с направлением                 │
└──────────────────┬──────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────┐
│              VehicleController                    │
│   IssueMoveOrder() — замена цели (старый API)      │
│   AppendMoveToQueue() — добавить в очередь         │
│   StopCurrentOrder() — прервать текущий            │
│   ClearOrders() — очистить всё                     │
└──────────────────┬──────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────┐
│             VehicleOrderQueue                     │
│   m_Queue: [Order₁, Order₂, Order₃]               │
│   m_Current: Order₀ (Executing)                   │
│   Продвижение: Complete → PromoteNext             │
│   Прерывание: Stop (сохранить), Emergency (очистить)│
└──────────────────┬──────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────┐
│           VehicleNavigation                       │
│   Владеет всеми подсистемами                       │
│   EnqueueOrder() → TryPromoteNextOrder()          │
└───┬──────────┬──────────┬──────────┬─────────────┘
    │          │          │          │
    ▼          ▼          ▼          ▼
┌────────┐ ┌────────┐ ┌────────┐ ┌──────────────┐
│PathPlanner│ │Driving │ │Maneuver│ │ManeuverFeasi-│
│ NavMesh  │ │Planner │ │Planner │ │bilityChecker │
│ + прямой │ │Кандида-│ │Waypoint│ │Геометрия +   │
│ fallback │ │ты +    │ │генера- │ │Предикшн =    │
│          │ │Scoring │ │ция     │ │Вердикт       │
└────┬─────┘ └───┬────┘ └───┬────┘ └──────┬───────┘
     │           │          │              │
     └───────────┴──────────┴──────────────┘
                      │
                      ▼
┌─────────────────────────────────────────────────┐
│                 DriverFSM                         │
│   Idle → Driving → Arrival → Holding              │
│          ↓                                        │
│       Recovery (Unstuck/Reverse/Rebuild/Abort)    │
│                                                    │
│   ExecuteManeuver():                               │
│     PursuitController → SpeedPlanner → MotionCtrl  │
└──────────────────┬──────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────┐
│              VehicleCommand                       │
│   Steer, Throttle, BrakeMode, HoldPosition,       │
│   Phase (Cruise/Precision/Parking/Recovery)        │
└──────────────────┬──────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────┐
│         VehicleBrain → WheeledMotor               │
│   Физика колёс, мотор, руль, тормоза               │
└─────────────────────────────────────────────────┘
```
