# Отчёт — Исправления навигации машины (v2.0 → v2.3)

## Хронология всех изменений

---

## 1. FeasibilitySeverity — градация вместо бинарного запрета

### Файл: `FeasibilityResult.cs`

**Добавлено:**
```csharp
public enum FeasibilitySeverity { Valid, Risky, Unsafe, Impossible }
public FeasibilitySeverity Severity { get; set; }
```

**Заменены фабрики:**
- Старая `Invalid(reason)` → теперь возвращает `Severity = Unsafe`
- Добавлена `Impossible(reason)` — для обрыва, зазора < 50% порога
- Добавлена `Unsafe(reason)` — для зазора < порога, но > половины
- Добавлена `Risky(risk, reason)` — для узких проходов
- `Valid` — без изменений

### Файл: `ScoringSystem.cs`

**Заменён метод `ApplyRiskPenalty()`:**
- Было: `if (!IsValid) return float.MaxValue` — бинарный отказ
- Стало: штраф по `Severity`:
  - `Impossible` → +999999 (фактически исключает)
  - `Unsafe` → +200
  - `Risky` → +50
  - `Valid` → +0
  - `HasCliffRisk` → +50
  - `HasNarrowPassage` → +10

**Изменён `ScoreCandidate()`:**
- Убран `if (!IsValid) return MaxValue` в начале — теперь все кандидаты получают реальный Score
- Штрафы за Reverse и TurnAround скорректированы: Reverse +15, TurnAround +10

---

## 2. Единый выбор по Score — fallback удалён

### Файл: `DrivingPlanner.cs`

**Удалён блок fallback (15 строк):**
```csharp
// Было:
if (best == null)
{
    // Pick best by severity
    DrivingCandidate bestUnsafe = null;
    int bestSev = 99;
    for (...) { ... }
    best = bestUnsafe ?? candidates[0];
}
```
Теперь выбор всегда по минимальному `c.Cost`. Поскольку Impossible = +999999, они естественно проигрывают.

**Изменён лог:**
- Было: `valid=True/False risk=0.00`
- Стало: `severity=Impossible/Unsafe/Risky/Valid`

**Изменено создание Reverse-кандидата:**
- Было: `canReverse = AllowReverse && HasSafeBackingSpace(geo, 1.8f)` — почти никогда не создавался
- Стало: `revOk = AllowReverse` — всегда создаётся, если не запрещён явно. Качество оценивает FeasibilitySeverity

---

## 3. Тирированные пороги в FeasibilityChecker

### Файл: `ManeuverFeasibilityChecker.cs`

**Метод `CheckForwardPath()`:**
- Было: `drop → Invalid`, `clearance < 1.8м → Invalid`
- Стало:
  - `drop → Impossible`
  - `clearance < 0.9м → Impossible` (50% порога)
  - `clearance < 1.8м → Unsafe`
  - `narrow passage → Risky`

**Метод `CheckTurnAroundArc()`:**
- Было: `clearance < 0.8R → Invalid`, `clearance < 0.5R зад → Invalid`
- Стало:
  - `clearance < 0.35R → Impossible`
  - `clearance < 0.7R → Unsafe`
  - Боковые проверки смягчены: раньше требовали `0.7R × 0.7` слева/справа, теперь просто `< 3м`

---

## 4. Двойной луч обрыва

### Файл: `VehicleLocalGeometry.cs`

**Метод `Probe()` — проверка обрыва:**
- Было: один луч на 3м вниз. Не попал → обрыв
- Стало: ДВА луча (0.5м и 1.5м от носа), оба на 5м вниз. ОБА должны не попасть → обрыв. Дистанция увеличена с 3м до 5м

---

## 5. Промежуточные waypoints для прямых путей

### Файл: `PathPlanner.cs`

**Добавлен метод `BuildDirectPath()`:**
```csharp
// Если прямой путь > 10м → вставляем промежуточные точки каждые 5м
// Это помогает Pure Pursuit сходиться без осцилляции
```
Вызывается при прямом fallback (NavMesh недоступен). Заменяет старый `[from, to]` из 2 точек.

---

## 6. Исправление прибытия — жёсткий стоп

### Файл: `DriverFSM.cs`

**Метод `TickArrival()`:**
- Было: `BrakeToStop(false)` — мягкий тормоз
- Стало: `BrakeToStop(true)` — жёсткий тормоз

**Добавлен guard:**
```csharp
if (dist < 1.5f && speed > 5f)
    return m_Motion.BrakeToStop(false); // принудительный crawl
```

### Файл: `ArrivalController.cs`

**Метод `HasArrived()`:**
- Добавлен отладочный лог при dist < 1.5м но ещё не прибыл:
```csharp
[Arrival] close but not there: dist=X.XXm > tol=0.60m
```

---

## 7. Штраф за боковое смещение

### Файл: `ArrivalCostEvaluator.cs`

**Добавлен параметр `Weight_Lateral = 3.0`:**
```csharp
cost += _analysis.LateralOffset * Weight_Lateral;
```
Боковые цели получают штраф, поэтому `ArcArrivalStrategy` и `RepositionArrivalStrategy` выигрывают у `DirectArrivalStrategy`.

**Скорректированы веса:**
- `Weight_Reverse`: 8 → 12
- `Weight_Maneuvers`: 5 → 6

---

## 8. Precision фаза по расстоянию

### Файл: `MotionController.cs`

**Метод `ResolvePhase()`:**
- Добавлена проверка: если `dist < 3м` и манёвр — arrival → `Phase = Parking`
- Это замедляет машину РАНЬШЕ, не дожидаясь смены манёвра

---

## 9. Подробные логи во всех точках

### Файл: `DrivingPlanner.cs`
- Лог создания кандидатов: `Reverse=True (available)` или `Reverse=True (disabled)`
- Лог каждого кандидата: `severity=Unsafe` вместо `valid=False`
- Лог выбора: `severities=[Forward=Impossible Reverse=Unsafe]`

### Файл: `ArrivalPlanner.cs`
- Лог анализа: `dist=X.Xm angle=XX° lateral=X.Xm front=True/False deadZone=True/False`
- Лог каждой стратегии: `cost=XX.X maneuvers=N`
- Лог выбора: `CHOSE Direct cost=XX.X`

### Файл: `VehicleNavigation.cs`
- Лог теперь показывает `phase=Cruise/Precision/Parking/Recovery`

### Файл: `ArrivalController.cs`
- Лог близкого, но не достигнутого прибытия

### Файл: `RecoveryController.cs`
- Лог действия: `action=UnstuckRock reason=stuck attempts=N`

---

## 10. Сводка: что добавлено, что заменено

### Новые файлы (созданы)
| Файл | Назначение |
|------|-----------|
| `FeasibilitySeverity` (enum в FeasibilityResult.cs) | Градация вместо бинарного запрета |

### Новые методы (добавлены)
| Файл | Метод | Назначение |
|------|-------|-----------|
| `PathPlanner.cs` | `BuildDirectPath(from, to)` | Промежуточные waypoints каждые 5м |
| `FeasibilityResult.cs` | `Impossible(reason)` | Фабрика с Severity=Impossible |
| `FeasibilityResult.cs` | `Unsafe(reason)` | Фабрика с Severity=Unsafe |
| `FeasibilityResult.cs` | `Risky(risk, reason)` | Фабрика с Severity=Risky |

### Заменённые методы (переписаны)
| Файл | Метод | Что изменилось |
|------|-------|---------------|
| `ScoringSystem.cs` | `ApplyRiskPenalty()` | MaxValue → SeverityPenalty (+0/+50/+200/+999999) |
| `ScoringSystem.cs` | `ScoreCandidate()` | Убран бинарный отказ, добавлены Severity-штрафы |
| `DrivingPlanner.cs` | `BuildPlan()` | Удалён fallback, Reverse всегда создаётся |
| `ManeuverFeasibilityChecker.cs` | `CheckForwardPath()` | Три уровня вместо двух |
| `ManeuverFeasibilityChecker.cs` | `CheckTurnAroundArc()` | Три уровня вместо двух |
| `VehicleLocalGeometry.cs` | `Probe()` | Двойной луч вместо одиночного |
| `DriverFSM.cs` | `TickArrival()` | Жёсткий тормоз + crawl-guard |
| `MotionController.cs` | `ResolvePhase()` | Distance-based precision |
| `ArrivalCostEvaluator.cs` | `Evaluate()` | +LateralOffset штраф |

### Изменённые параметры (константы)
| Файл | Параметр | Было | Стало |
|------|---------|------|-------|
| `ArrivalCostEvaluator.cs` | `Weight_Distance` | 1.2 | 1.0 |
| `ArrivalCostEvaluator.cs` | `Weight_Lateral` | — | 3.0 (новый) |
| `ArrivalCostEvaluator.cs` | `Weight_Reverse` | 8 | 12 |
| `ArrivalCostEvaluator.cs` | `Weight_Maneuvers` | 5 | 6 |
| `ManeuverFeasibilityChecker.cs` | `TurnAround Impossible` | — | <0.35R (новый) |
| `ManeuverFeasibilityChecker.cs` | `TurnAround Unsafe` | <0.8R | <0.7R |
| `ManeuverFeasibilityChecker.cs` | `Forward Impossible` | — | <0.9м (новый) |
| `ManeuverFeasibilityChecker.cs` | `Forward Unsafe` | <1.8м → Invalid | <1.8м → Unsafe |
| `VehicleLocalGeometry.cs` | `Drop Ray Distance` | 3м | 5м |
| `VehicleLocalGeometry.cs` | `Drop Rays Count` | 1 | 2 |

---

## 11. Коммиты (хронология)

| Хеш | Описание |
|------|----------|
| `94cfc75` | v2.0 — полный рефакторинг (8 частей: очередь, heading, feasibility, планировщик, recovery, фазы, safety, arrival) |
| `901d9cf` | Часть 8: Precision Arrival System |
| `05d5da8` | Подробные логи во все ключевые точки |
| `906e31a` | Исправлены баги: бесконечное кружение, игнорирование разворота |
| `3746f81` | Единый Score через SeverityPenalty |
| `6de13c5` | v2.3: Reverse всегда кандидат, жёсткий arrival, боковые цели |
