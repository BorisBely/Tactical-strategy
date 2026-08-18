# Зрение: текущая архитектура, справочник API, история и философия будущего

**Самодостаточный документ** — читается без открытия репозитория.  
**Проверено по коду:** 2026-08-18  
**Расположение в проекте (справочно):** `Assets/_Scripts/Unit/Vision/`  
**Статус:** Stage F complete (shim `VisibleTarget` удалён), freeze; **план Stage G0–G9 зафиксирован в §9**  
**Транскрипт работ:** `f706500b-e830-41ef-8b64-105de633b8ea`

---

## Как читать этот документ

| Раздел | Для кого |
|--------|----------|
| §0–1 | Быстрый статус «что есть / чего нет» |
| §2 | **Справочник скриптов, данных, методов** (standalone) |
| §3 | Поведение скана и selection (алгоритмы словами) |
| §4 | Кто что читает (consumers) |
| §5–6 | История «до → этапы» |
| §7–8 | Философия будущего и gap-анализ |
| **§9** | **План работ Stage G0–G9 (roadmap)** |
| §10–12 | Smoke / хронология / cheat-sheet |

---

## 0. Статус одной строкой

| Слой | В коде сейчас |
|------|---------------|
| Detect → Perception → TargetSelector → Combat | ✅ Реализовано |
| Shim `UnitVision.VisibleTarget` | ❌ Удалён |
| Detection progress / quality score | ❌ Не реализовано (G+) |
| Identity / Threat / Relationship / LastKnown | ❌ Не реализовано (G+) |
| Sound / Shared → Perception | ❌ Не реализовано (есть задел в комментариях) |

**Правило состояний (уже в архитектуре):**

```text
Observed (Perception)
  ≠ Selected (TargetSelector)
  ≠ Engageable (TargetEngageability)
  ≠ AI intent
```

**Поток данных:**

```text
UnitObservationSource
    → UnitVision (candidates → range/FOV → LOS → VisionObservation[])
    → UnitPerception.ApplyVisionFrame
    → event PerceptionFrameApplied
    → TargetSelector.SelectFromPerception
    → SelectedTarget / SelectedTargetChanged
    → TargetEngageability (для огня)
    → Fire / Aim / Nav / RTS / Vehicles / EnemyPatrolAI
```

---

## 1. Компоненты на юните (wiring)

На солдате (типичный префаб / runtime Ensure) живут рядом:

| Компонент | Require / создание |
|-----------|-------------------|
| `UnitTeam` | Require на `UnitVision` |
| `UnitObservationSource` | Require на `UnitVision` и `TargetSelector` |
| `UnitPerception` | Require на `UnitVision`; ExecutionOrder **-200** |
| `TargetSelector` | Require `UnitPerception` + `UnitObservationSource`; в `UnitPerception.Awake` создаётся через `AddComponent`, если нет |
| `UnitVision` | оркестратор detect; **не** вызывает Select |

Сцена/мир:

| Компонент | Роль |
|-----------|------|
| `UnitVisionRegistry` | регистрация всех `UnitVision`; выдача opponents по team |
| `ShootingRangeTargetRegistry` | мишени полигона для Player |

Вспомогательные типы (не MonoBehaviour):

- `VisionCandidateProvider`, `VisionGeometry` (static), `VisibilityChecker`
- `VisionObservation` (struct DTO)
- `TargetEngageability` (static)
- `VisionSystemContract` (internal empty class — только XML-контракт)
- `IObservationSource` (interface)

Команды:

```csharp
public enum UnitTeamId { Player = 0, Enemy = 1, Neutral = 2 }
```

`UnitTeam` — MonoBehaviour с `Team` / `SetTeam(UnitTeamId)`.

---

## 2. Справочник скриптов: данные и методы

Ниже — публичные контракты, достаточные для понимания без IDE.

### 2.1. `VisionObservation` (struct)

**Смысл:** результат физического detect по одному кандидату в **текущем** кадре скана.  
**Не** knowledge / memory / «это враг».

| Поле | Тип | Смысл |
|------|-----|--------|
| `Target` | `Transform` | корень цели |
| `Position` | `Vector3` | позиция корня |
| `AimPoint` | `Vector3` | лучшая видимая точка прицеливания |
| `HasAimPoint` | `bool` | aim point валиден |
| `DistanceSq` | `float` | квадрат дистанции по XZ от origin зрения |
| `IsVisible` | `bool` | true для текущего кадра (прошёл LOS) |

---

### 2.2. `UnitPerception` (MonoBehaviour)

**Смысл:** хранит **только текущий кадр** наблюдений. Не знает, кто producer (сегодня Vision; позже sound/shared).  
**Не** зависит от `UnitVision`.

**Свойства**

| Member | Тип | Описание |
|--------|-----|----------|
| `Observations` | `IReadOnlyList<VisionObservation>` | текущий кадр |
| `ObservationCount` | `int` | |
| `HasAnyObservation` | `bool` | |

**События**

| Event | Когда |
|-------|--------|
| `PerceptionChanged` | содержимое кадра изменилось (сравнение Target / HasAimPoint / IsVisible / AimPoint) |
| `PerceptionFrameApplied` | **каждый** `ApplyVisionFrame`, даже если content тот же — на него подписан TargetSelector |

**Методы**

```text
void ApplyVisionFrame(IReadOnlyList<VisionObservation> frame)
  — заменить кадр; fire PerceptionChanged если changed; всегда fire PerceptionFrameApplied

bool TryGetObservation(Transform target, out VisionObservation observation)
```

**Awake:** если нет `TargetSelector` → `AddComponent<TargetSelector>()`.

---

### 2.3. `IObservationSource` / `UnitObservationSource`

**Смысл:** «откуда смотрит юнит» — глаза или прицел оружия. Не сканирует мир.

**Интерфейс**

```text
Vector3 GetOriginWorld()
Vector3 GetEyeWorldPosition()
bool TryGetSightTransform(out Transform sight)
bool IsUsingWeaponSight { get; }
```

**UnitObservationSource — ключевое поведение**

- Origin = sight pivot, если high-ready и найден; иначе `position + up * EyeHeight` (default **1.6**).
- Sight ищется: override → EquippedWeapon Sight Pivot → child name под визуалом оружия.
- RPG aim/fire phase: не использует weapon sight cone.
- `ApplyConfig(eyeHeight, sightOverride, childName, equipment, readyHands)` — вызывается из Vision.
- `InvalidateSightCache()` — при смене ready.

---

### 2.4. `VisionCandidateProvider` (class, не Component)

**Смысл:** кого проверить. Без FOV/LOS.

**Candidate**

| Поле | Смысл |
|------|--------|
| `Root` | Transform |
| `HitZones` | `UnitBodyHitZone[]` (может быть пусто) |
| `LegacyCollider` | fallback collider |
| `HasHitZones` | |

**Методы**

```text
void Bind(UnitTeam team, UnitVisionRegistry registry, ShootingRangeTargetRegistry rangeRegistry)
void Collect(List<Candidate> out, Func<Transform,bool> shouldSkip)
  — opponents из Registry + (для Player) active range targets
void CollectOpponentsRaw(List<UnitVision> buffer)
```

Фильтры Collect: не self, enabled, consciousness targetable, alive DamageableTarget, optional shouldSkip.

---

### 2.5. `VisionGeometry` (static)

```text
bool IsWithinRangeAndFov(origin, forwardXZ, point, rangeSq, halfFovDegrees, out distanceSq)
  — проверка по XZ: дистанция + угол ≤ halfFov

float ResolveHalfFovDegrees(fovDegrees, widenForWeaponNotReady, minHalfWhenNotReady,
                            hasTrackingTarget, trackingHalfFovExtra)
  — half = fov/2; max с minHalf если low-ready widen; +extra если есть tracking Selected

Vector3 FlattenNormalized(direction, fallback)
```

---

### 2.6. `VisibilityChecker` (class)

**Смысл:** физический LOS / лучшие aim samples по hit-zones или legacy collider.  
Используется **UnitVision** (detect) и **своим экземпляром у TargetSelector** (retain/revalidate).

Ключевые методы (по смыслу):

```text
Configure(layerMask, queryTriggerInteraction, visionRange, drawGizmos)
ClearDebugRays()
TryFindBestVisibleAimPointFromHitZones(...)
TryFindBestVisibleAimPointFromCollider(...)
HasLineOfSightToPoint(...)
TryGetLosBlocker(...)
```

Опирается на `UnitBodyHitZone` / utility aim candidates (Chest и др.).

---

### 2.7. `UnitVision` (MonoBehaviour) — DETECT ONLY

**Смысл:** периодический скан → список `VisionObservation` → `Perception.ApplyVisionFrame`.  
**Не** выбирает боевую цель. **Нет** API `VisibleTarget` (удалён).

**RequireComponent:** `UnitTeam`, `UnitObservationSource`, `UnitPerception`.

#### Сериализованные параметры (defaults)

| Параметр | Default | Смысл |
|----------|---------|--------|
| `VisionRange` | 18 | макс. дальность detect |
| `FieldOfViewDegrees` | 120 | полный угол конуса |
| `TrackingHalfFovExtraDegrees` | 15 | бонус half-FOV при наличии Selected |
| `EyeHeight` | 1.6 | прокидывается в ObservationSource |
| `ScanIntervalMin` / `Max` | 0.25 / 0.45 | рандомный интервал скана (сек) |
| `ImmediateRescanAngleDegrees` | 2.5 | внеочередной скан при повороте прицела |
| LayerMask / QueryTrigger | ~0 / Ignore | физика LOS |
| View forward override / torso / smooth | — | ось FOV |
| `MinHalfFovDegreesWhenWeaponNotReady` | 70 | widen при low ready |
| Sight pivot override / child name | — | в ObservationSource |
| Ready bore transition | off | временный поворот корня при high ready |

Интервал может переопределяться через `UnitCombatStats` (GetVisionScanIntervalMin/Max).

#### Публичный API (scan / facing / body)

```text
Collider BodyCollider
IReadOnlyList<UnitBodyHitZone> BodyHitZones
UnitBodyHitZone[] GetBodyHitZonesArray()
void RefreshBodyHitZones()

void SetVisionRange(float range)
void RequestImmediateScan()     — мгновенный RunVisionScan + schedule next
void DeferNextScan()            — schedule next по интервалу (после clear selection)
void NotifyWeaponReadyChanged(bool ready)  — invalidate sight + ImmediateScan

bool TryFindTargetInDirection(float worldAngle, float halfAngleDegrees, out Transform best)
float ResolveHalfFovDegreesForScan()

bool TryGetEngageFacingOriginWorld(out Vector3 origin)
bool TryGetEngageFacingForwardXZ(out Vector3 forwardXZ)
Vector3 GetEngageFacingOriginWorld()   — fallback на root/sight
```

#### Алгоритм скана (словами)

```text
1. origin = ObservationSource.GetOriginWorld()
2. forwardXZ = сглаженная ось взгляда (override / torso / root)
3. halfFov = ResolveHalfFovDegrees(...)
4. CandidateProvider.Collect(...)
5. для каждого кандидата:
     rough center → range+FOV (VisionGeometry)
     → VisibilityChecker best aim / LOS
     → VisionObservation { IsVisible=true, DistanceSq, AimPoint... }
6. Perception.ApplyVisionFrame(list)
   // TargetSelector сам среагирует на PerceptionFrameApplied
```

---

### 2.8. `TargetSelector` (MonoBehaviour) — SELECT

**Смысл:** выбрать engage target из Perception.  
**Не** владеет FOV/detect scan.

**RequireComponent:** `UnitPerception`, `UnitObservationSource`.  
Подписка: `PerceptionFrameApplied` → `SelectFromPerception()`.

#### Сериализованные параметры (defaults)

| Параметр | Default | Смысл |
|----------|---------|--------|
| `MaxEngageRange` | 18 | retain/range checks |
| `RetainTargetDuringReloadOrMalfunction` | true | держать цель без FOV (range+LOS) |
| `LineOfFireSafetyRadius` | 0.35 | suppress radius semantics |
| `LineOfFireBlockedRetrySeconds` | 0.15 | |
| `AimPointVelocitySmoothTime` | 0.15 | |
| `AimPointMaxProjectionSeconds` | 0.5 | экстраполяция aim |
| LayerMask / QueryTrigger | физика собственного VisibilityChecker |

#### Публичные свойства / методы

```text
Transform SelectedTarget { get; }
bool HasSelectedAimPoint { get; }
Vector3 SelectedAimPointWorld { get; }
Transform ForcedPriorityTarget { get; set; }   // RTS priority

Vector3 SelectedTargetVelocity { get; }        // сглаженная оценка
float LastAimPointUpdateTime { get; }
Transform VelocityTrackedTarget { get; }

Transform GetEngageableSelectedTarget()
  — Selected, если TargetEngageability.IsEngageable; иначе null

Vector3 GetEngageableAimPointWorld()
  — aim SelectedEngageable + velocity projection; иначе Vector3.zero

bool IsTrackingTarget(Transform targetRoot)
  — Selected == root или child/parent связь

bool ShouldReacquireAimAfterSwitch(Transform prevEngageable, Transform nextEngageable)
  — сброс aim/серии при смене живой цели / появлении после null

void ClearSelection(bool invokeEvent)
void ClearSelectionAndNotifyIfHadTarget()
void SuppressCurrentTargetForLineOfFire(float seconds)
  — пометить в suppress dict, ClearSelection(true)
bool IsLineOfFireSuppressed(Transform candidate)

void SelectFromPerception()
void SelectFromPerception(Vector3 visionOrigin)

event Action<Transform> SelectedTargetChanged
```

#### Алгоритм selection (словами)

```text
1. cleanup expired LoF suppress
2. если текущий Selected не engageable → сбросить локально
3. пройти Perception.Observations (IsVisible):
     skip LoF-suppressed
     revalidate если нужно
     nearest по DistanceSq
4. ForcedPriority: если задан и допустим — перекрывает nearest
5. retain: при reload/malfunction можно удержать старую цель (range+LOS, без FOV)
6. обновить Selected + aim; если changed → SelectedTargetChanged
7. обновить velocity estimate по aim point
```

---

### 2.9. `TargetEngageability` (static)

```text
bool IsEngageable(Transform target)
  null → false
  !UnitConsciousness.IsTargetableTarget → false
  ShootingRangeTarget → IsAvailableForTargeting
  DamageableTarget → IsAlive
  иначе → true
```

Нейтральное правило «можно ли engage сейчас», не принадлежит Vision.

---

### 2.10. `UnitVisionRegistry`

```text
void Register(UnitVision)
void Unregister(UnitVision)
void GetOpponents(UnitTeamId viewerTeam, List<UnitVision> outBuffer)
int PlayerUnitCount / EnemyUnitCount / NeutralUnitCount
```

**Техдолг:** GetOpponents смешивает «кто существует» и «кто противник».

---

### 2.11. Удалённый legacy API (не использовать)

Раньше на `UnitVision` (shim поверх Selector). **Удалено в Stage F:**

```text
VisibleTarget
GetEngageableVisibleTarget()
VisibleTargetChanged
GetVisibleTargetAimPointWorld()
GetVisibleTargetVelocity()
ForcedPriorityTarget          // теперь только на TargetSelector
ClearVisibleTargetAndWaitForNextScan()
SuppressCurrentTargetForLineOfFire()  // теперь на TargetSelector (+ ImmediateScan снаружи)
IsTrackingTarget / ShouldReacquireAimAfterSwitch  // на TargetSelector
```

Замены — §2.8 + `UnitVision.RequestImmediateScan` / `DeferNextScan`.

---

## 3. Поведение detection «как сейчас» (бинарный кадр)

```text
кандидат прошёл range + FOV + LOS
  → VisionObservation.IsVisible = true
  → может стать Selected (nearest / forced)
```

Нет:

- visibility score / detection progress
- delayed discovery для дальних целей
- множителя «цель бежит»
- soft memory RecentlyLost / LastKnown

Частичная видимость hit-zones влияет на **какой aim point виден**, но не на отдельный «уровень обнаружения».

---

## 4. Consumers (кто читает что)

| Система (скрипт) | Читает | Зачем |
|------------------|--------|--------|
| `EnemyPatrolAI` | `SelectedTarget != null` | reaction delay → ready |
| `UnitWeaponFireController` | engageable, aim, `SelectedTargetChanged`, Suppress + ImmediateScan | огонь / LoF |
| `UnitWeaponAimProgressController` | engageable, aim, Selected edge-case → Clear + DeferNextScan | накопление AimProgress |
| `UnitWeaponFireDisciplineController` | engageable, aim, event | серии / дистанция |
| `UnitWeaponHitscanShooting` | engageable, aim, velocity | lead / hit classification |
| `UnitWeaponAiming` | Selected + aim | вертикальный combat aim |
| `UnitSpineHorizontalAim` | Selected + aim | yaw торса |
| `UnitRocketLauncherOrderController` | engageable + aim | RPG |
| `UnitNavLocomotionDriver` | Selected + aim; facing с Vision | поворот корня |
| `UnitClickToMove` | то же | engage locomotion |
| `RtsUnitSelectionManager` | `ForcedPriorityTarget` + Vision ImmediateScan | приоритетная цель |
| `RtsUnitMember` | Selected (стрелка); Vision FOV/scan | facing UI |
| `VehicleTurretGunnerBridge` | gunner TargetSelector | aim турели |
| `VehiclePassengerAimController` | passenger TargetSelector | yaw пассажира |
| `VehiclePassengerFireValidator` | passenger TargetSelector | fire gate |
| `VehicleTurretMk19FireDiagnostics` | TargetSelector | debug |
| `ShootingRangeTargetRegistry` | IsTracking + Clear + DeferNextScan | сброс трекинга мишени |
| `ShootingRangeManager` | `UnitVision.RequestImmediateScan` | форс-скан |
| `UnitWeaponReadyHandsLayer` | `NotifyWeaponReadyChanged` | rescan на ready |

**Правило:** fire почти всегда нужен **engageable**, не сырой Selected.

---

## 5. Система ДО рефакторинга

Монолит `UnitVision` (~1780 строк):

```text
Registry → range/FOV → LOS → nearest → VisibleTarget + event
```

Один компонент = detect + боевая цель + aim/velocity + ForcedPriority + LoF + facing.  
20+ систем читали `VisibleTarget`.  
Perception / TargetSelector / ObservationSource / Geometry helpers не существовали.

---

## 6. История этапов

Инвариант: поведение игры не менять; не добавлять stealth/suspicion/last-known.

### 6.1. P0–P6 (Vision Perception Refactor)

| Этап | Результат |
|------|-----------|
| P0 | Regression-контракт |
| P1 | `VisionObservation` + список в скане |
| P2 | `UnitPerception` |
| P3 | `TargetSelector` |
| P4 | shim VisibleTarget ← Selected; Patrol на Selector |
| P5 | CandidateProvider / Geometry / VisibilityChecker |
| P6 | ObservationSource |

### 6.2. A–D (Architecture Finish)

Engageability helper; карта Vision; Perception без Require Vision; thin-up; fire ещё на shim.

### 6.3. Stage E

Vision scan только до ApplyVisionFrame; Selector на PerceptionFrameApplied; свой VisibilityChecker; shim жив.

### 6.4. Stage F (готово)

Миграция всех consumers → удаление shim → freeze.

---

## 7. Что НЕ реализовано (Stage G+ / философия)

### Detection quality

- небинарный «насколько хорошо виден»
- Detection Progress
- instant obvious vs delayed hard cases
- движение цели как множитель заметности
- градация до ~500 м «только хорошо заметные»
- состояние наблюдателя (идёт/бежит/занят) как detection modifiers

### Identity / Threat / Relationship

- Actual Team vs Perceived Identity
- Confidence, Threat Assessment, Relationship
- ошибка восприятия без смены `UnitTeam` на объекте
- полная цепочка identify → threat → decide ≠ fire

### Memory

- CurrentlyObserved → RecentlyLost → Lost
- LastKnownPosition / LastSeenTime
- soft lose за укрытием
- Search AI (осознанно отложено)

### Другие входы Perception / AI modes

- Sound / Shared info → Perception (есть комментарий-задел)
- «обнаружил ≠ стрелять» (контроль / доклад) как отдельный слой

### Perf model 500 м

- scan interval и cheap→expensive частично есть
- dedicated LOD/culling tiers под десятки наблюдателей на 500 м — нет

---

## 8. Философия будущей системы зрения

> **Намерение**, не текущий код. Gap — §7.

Главная идея:

> **AI не должен обладать магическим знанием о мире. Он должен иметь собственное восприятие мира.**

Разница:

```text
что существует на самом деле
  ≠
что конкретный солдат знает / видит / считает правдой
```

### 8.1. World ≠ Perception

Гражданский объективно `Neutral`, но солдат A может воспринимать его как «подозрительный/враг», солдат B — как civilian. **Нельзя** писать `civilian.UnitTeam = Enemy` из-за ошибки A.

```text
WORLD → Actual Entity (UnitTeam)
          ├─ Soldier A → Perception A
          └─ Soldier B → Perception B
```

### 8.2. Vision ≠ Target Selection ≠ Fire

```text
увидел → воспринял → идентифицировал → оценил угрозу → выбрал цель → решил действовать
```

Vision: физика наблюдения. Не «плохой парень», не «стреляй».

### 8.3. Небинарный detection (средний вариант)

```text
Observation → насколько хорошо виден? → Detection
```

Очевидные (20 м, открыт, центр взгляда) — почти мгновенно.  
80–100 м, половина тела, взгляд в сторону — быстро, без «смотреть секунды».  
400 м, край FOV, кусок головы — может требовать время / не обнаружиться.

Detection Progress — прежде всего для неоднозначных случаев.

### 8.4. Движение, дистанция ~500 м, perf

Движение помогает заметности, но не абсолют.  
~500 м — рабочий предел; около предела только хорошо заметные.  
Оптимизация №1: staggered scans, cheap→expensive, кэш, лимит кандидатов.

### 8.5. Частичная видимость и состояние наблюдателя

Hit zones / вес зон без лавины raycast.  
Обзор зависит от стойки/движения/занятости/направления взгляда (решение D), через ObservationSource + параметры, без знания всей game logic внутри Vision.

### 8.6. Целевая цепочка слоёв (уточнённая)

Не сводить к короткому `Vision → Perception → Relationship → TargetSelector`.  
Фактическая конечная модель:

```text
WORLD
  ↓
OBSERVATION SOURCES   (Vision / Sound / Shared)
  ↓
OBSERVATION           (сырые физические факты кадра)
  ↓
DETECTION             (насколько уверенно обнаружен)
  ↓
PERCEIVED CONTACT / KNOWLEDGE
  ↓
IDENTITY / THREAT / RELATIONSHIP
  ↓
TARGET SELECTION
  ↓
ENGAGEMENT DECISION   (observe / report / track / aim / fire / ignore…)
  ↓
COMBAT / AI
```

Разделять: Actual Team | Perceived Identity | Confidence | Threat | Relationship | Engagement Decision.

Обнаружение ≠ обязанность стрелять (контроль / доклад / наблюдение).

**Главный архитектурный gap сейчас:** между текущим `UnitPerception` (кадр observations) и `TargetSelector` отсутствует явный слой **Detection → PerceivedContact/Knowledge**.

### 8.7. Потеря цели

```text
CurrentlyObserved → RecentlyLost → Lost
```

+ LastKnownPosition / LastSeenTime. Search AI пока не добавлять.

### 8.8. Perception как шина

```text
Vision ──┐
Sound ───┼─► Perception
Shared ──┘
```

### 8.9. Жёсткая формулировка цели и инварианты

> **Vision не знает, кто враг. Perception не знает, что делать. TargetSelector не решает, стрелять ли. Combat не знает, откуда возникла информация. Каждый слой работает только с информацией своего уровня.**

Фундаментальный инвариант:

```text
World State ≠ Perception State ≠ Decision State
```

Это важнее самого Detection Progress.

**Не цель:** «реалистичное зрение» / симулятор человеческого глаза с десятками коэффициентов (light/camouflage/fatigue/…).

**Цель:**

> **предсказуемая модель субъективной информации с небольшим числом физических факторов и понятным накоплением неопределённости.**

```text
physical evidence → confidence → knowledge → decision
```

Это продолжение уже достигнутого Stage F: Vision = detect-only, Perception не зависит от UnitVision, TargetSelector отделён от физики зрения.

### 8.10. Gap: философия ↔ код сейчас

| Принцип | Сейчас |
|---------|--------|
| World ≠ Perception | Частично (свой Perception на юните; нет subjective identity) |
| Vision ≠ Select | ✅ |
| Detection → PerceivedContact | ❌ **главный gap** |
| Небинарный detection | ❌ |
| Instant / delayed | ❌ |
| Perceived ≠ UnitTeam | ❌ |
| Soft lose / LastKnown | ❌ |
| Engagement Decision слой | частично (`TargetEngageability` / fire gates) |
| Sound/Shared | только задел |
| Cheap→expensive pipeline | частично ✅ |

**Следующий шаг:** план §9 (G0→…), не возврат к VisibleTarget, не хаотичное добавление механик.

---

## 9. План работ Stage G0–G9

> Stage F завершил `Detect → Perception → TargetSelector → Combat` и удалил `VisibleTarget`.  
> Дальше — **не сыпать механику**, а наращивать слои поверх фундамента.  
> **G0–G5** = основной архитектурный рефакторинг. **G6–G9** = расширение возможностей.  
> **Не объединять** G1+G2+G3 в один этап: иначе невозможно отладить «плохо обнаружил / плохо накопил / плохо идентифицировал / плохо выбрал».

### 9.0. Roadmap одной схемой

```text
G0  Contracts / data ownership / invariants
 ↓
G1  Detection Quality
 ↓
G2  Perceived Contacts
 ↓
G3  Identity / Confidence / Threat / Relationship
 ↓
G4  Memory / RecentlyLost / LastKnown
 ↓
G5  TargetSelector ← Perceived Contacts
 ↓
G6  Engagement Decision
 ↓
G7  Sound / Shared perception
 ↓
G8  Performance / 500m / LOD
 ↓
G9  Search AI / higher-level behavior
```

### 9.1. Ownership слоёв (что куда класть)

| Сущность | Отвечает на вопрос | Не должна содержать |
|----------|--------------------|---------------------|
| `Observation` (`VisionObservation` сегодня) | Что физически в кадре? | identity / threat / «враг» |
| `Detection` | Насколько уверенно обнаружен? | кого атаковать |
| `PerceivedContact` | Что *этот* AI считает известным об объекте? | world UnitTeam mutate |
| `Memory` | Что помнит после потери? | Search behaviour |
| `Identity / Confidence` | Кем кажется и насколько уверен? | fire decision |
| `Threat / Relationship` | Насколько опасен / hostile для меня? | физику LOS |
| `TargetSelector` | Кого выбрать из доступных contacts? | «стрелять ли» |
| `Engagement` | Что делать с выбранным? | откуда пришла info |

**Инвариант G0:** в `VisionObservation` — **только physical facts**. Detection / PerceivedContact / Memory / Threat не расширяют этот DTO полями knowledge.

Критерий готовности G0 (без смены геймплея): для любого объекта можно ответить раздельно:

> Что AI физически увидел? Что вывел? Что запомнил? Что решил?

Если один параметр нужен сразу 2–3 слоям — контракт ещё не разделён.

---

### Stage G0 — зафиксировать контракты будущей системы

**Цель:** определить сущности и ownership, **не меняя поведение игры**.

Формально зафиксировать (комментарии / types / empty stubs / VisionSystemContract):

```text
Observation
Detection
PerceivedContact
Memory
Identity
Threat
Relationship
Selection
Engagement
```

Сейчас `UnitPerception` = контейнер текущего кадра observations и не знает producer — хороший фундамент для Vision/Sound/Shared.

**Не делать в G0:** Detection Progress, memory runtime, смену TargetSelector semantics.

**Проверка:** документ/контракт + compile; геймплей Stage F без регрессий.

---

### Stage G1 — Detection Quality (первый функциональный этап)

**Проблема сейчас:**

```text
range + FOV + LOS → IsVisible = true   (бинарно)
```

**Добавить (минимально):**

```text
physical observation
  → VisibilityQuality 0..1
  → DetectionScore / DetectionProgress
  → Detected / NotDetected
```

**Факторы G1 (ограниченный набор — не всё сразу):**

```text
distance
FOV position
visible hit-zones / body fraction
movement (цели)
```

Пока **не** добавлять: camouflage, lighting, stealth, fatigue, attention.

**Правило:** Detection Progress ≠ «любую цель смотреть 2 секунды».

```text
очевидное → почти мгновенно
неоднозначное → немного времени
крайне плохое → может не обнаружиться
```

Движение — **множитель**, не бинарное правило.

**Не класть Detection Progress прямо в TargetSelector** — только prepare данные для G2.

**Проверка — диагностическая сцена, одна цель:**

| Ситуация | Ожидание |
|----------|----------|
| 10 м, центр FOV | instant |
| 20 м, полный силуэт | почти instant |
| 80 м, половина тела | быстро |
| 100 м, край FOV | медленнее |
| 400 м, маленькая часть головы | долго / нестабильно |
| за стеной | не обнаруживается |

Плюс: цель стоит / идёт / бежит — ускорение от движения как множитель.

---

### Stage G2 — Detection State / Perceived Contact (критичный слой)

**Не** переносить Detection Progress в TargetSelector.

Нужен слой/объект уровня:

```text
PerceivedContact
{
  Entity
  DetectionState
  DetectionConfidence
  LastObservation
  LastKnownPosition      // может появиться полностью в G4
  IdentityConfidence     // заполняется в G3
  Threat                 // G3
  Relationship           // G3
}
```

Структура может отличаться — важен принцип:

```text
VisionObservation  = сырой физический факт текущего скана
PerceivedContact   = накопленная субъективная информация этого AI
```

Соответствует `World ≠ Perception`.

**Проверка — два наблюдателя:**

```text
Soldier A видит цель, Soldier B нет
→ A.PerceivedContacts ≠ B.PerceivedContacts
→ ActualWorld и UnitTeam объекта одинаковы
```

Это главный архитектурный smoke новой системы.

---

### Stage G3 — Identity / Confidence / Relationship / Threat

Только после G1–G2.

Разделять (не один `PerceptionScore`):

```text
DetectionConfidence
IdentityConfidence
ThreatAssessment
Relationship
```

Возможны состояния вроде:

```text
100% detected + 30% identity
100% detected + 100% identity + low threat
```

Ошибка восприятия **никогда** не делает `entity.UnitTeam = Enemy`.

**Проверка:**

```text
вижу → не знаю кто
вижу → считаю friendly
вижу → считаю hostile
вижу → ошибочно считаю hostile   (UnitTeam мира не меняется)
```

---

### Stage G4 — Memory

После PerceivedContact:

```text
CurrentlyObserved → RecentlyLost → Lost
```

Хранить не «цель всё ещё точно известна», а:

```text
LastKnownPosition
LastSeenTime
LastSeenConfidence   // ухудшается со временем
```

Пример баланса (потом тюнить):

```text
0 s → точно
1 s → хорошо
3 s → приблизительно
7 s → stale
10 s → lost
```

**На этом этапе НЕ делать Search AI** — только «я потерял цель», не «я побежал искать».

---

### Stage G5 — TargetSelector ← Perceived Contacts

Серьёзная смена смысла Selector.

**Сейчас:** nearest engageable из `Perception.Observations` (+ forced / retain).

**Цель:**

```text
Perceived contacts → eligibility → priority → TargetSelector
```

Selector больше не спрашивает «кого физически вижу в этом кадре?», а:

> Из того, что мне **известно**, кого выбираю как текущую боевую цель?

Позже сможет учитывать RecentlyLost / suspected hostile как knowledge, не как текущий LOS.

**Проверка:**

```text
Enemy observed → Selected
→ Enemy hides → VisionObservation исчез
→ PerceivedContact остаётся
→ Selector может сохранить / сменить target
```

Различать:

```text
не вижу ≠ забыл ≠ не могу выбрать ≠ могу стрелять
```

---

### Stage G6 — Engagement Decision

Отдельно от selection и рядом/вместо разрастания `TargetEngageability`.

```text
Detected ≠ Identified ≠ Threat ≠ Selected ≠ Engageable ≠ Fire
```

Концепт:

```text
EngagementDecision: Observe | Report | Track | Aim | Suppress | Fire | Ignore
```

Не все состояния сразу. Архитектурно:

- TargetSelector → **кого** выбрать  
- Engagement → **что** с ним делать  

Нужно разным ролям (наблюдатель / разведчик / гранатомётчик / пулемётчик / командир / мирный NPC) при одном perception.

---

### Stage G7 — Sound / Shared Information

Когда Vision стабильно наполняет PerceivedContact:

```text
Vision ──┐
Sound ───┼─► Perception processing → PerceivedContact
Shared ──┘
```

**Правило:** Sound не притворяется Vision.

Sound даёт например:

```text
Position / Direction / Loudness / SourceConfidence / Type / Time
```

**не** `EnemyTransform + AimPoint + IsVisible`.

Отдельные observation types → единый perception processing.

---

### Stage G8 — Performance / 500 m

После семантики (не раньше как главная цель).

Направление:

```text
staggered scans + cheap→expensive + cache + candidate limits + LOD/culling
```

Уровни наблюдения (пример):

```text
Tier 0 — idle
Tier 1 — cheap candidates
Tier 2 — FOV/range
Tier 3 — LOS
Tier 4 — detailed hit-zone evaluation
```

Не каждый юнит — дорогой LOS каждый кадр.

**Проверка — stress:**

```text
10 / 25 / 50 / 100 observers
```

Мерить: scan cost, LOS raycasts, candidate count, perception processing, selection, GC — не только FPS.

---

### Stage G9 — AI Search / Behaviour

Только здесь:

```text
Observed → hidden → RecentlyLost → LastKnown → Search → no confirm → Lost
```

Поиск — AI behaviour, **не** встраивать в Vision.

---

### 9.2. Правила исполнения плана

1. После каждого Gx — отдельный demo/smoke (см. проверки выше).  
2. Не смешивать G1+G2+G3.  
3. Не начинать G7–G9 до стабильных G0–G5.  
4. Не раздувать tuning-коэффициенты «реалистичного глаза».  
5. Не мутировать world `UnitTeam` из perception.  
6. Не класть knowledge-поля в `VisionObservation`.

### 9.3. Одна главная правка к архитектуре (приоритет)

Между текущим Perception (кадр observations) и Relationship/TargetSelector добавить явный слой:

```text
Detection → PerceivedContact / Knowledge
```

Именно его сейчас не хватает больше всего.

---

## 10. Smoke checklist (Stage F — регрессия)

1. Detect FOV+LOS → observation / Selected nearest  
2. ForcedPriority RTS  
3. Мёртвая цель не engageable → нет fire  
4. Fire / AimProgress / Hitscan / spine  
5. LoF suppress + ImmediateScan  
6. Retain при reload  
7. Nav / ClickToMove engage facing  
8. EnemyPatrolAI ready по Selected  
9. Vehicle gunner / passenger  
10. Range clear tracking  
11. ReadyHands → NotifyWeaponReadyChanged → ImmediateScan  

---

## 11. Краткая хронология

```text
Монолит VisibleTarget
  → P1–P6 Observation / Perception / Selector / shim / split / ObservationSource
  → A–D Engageability + decoupling
  → E Vision detect-only
  → F consumers → delete shim → freeze
  → G0–G5 архитектурный слой Detection/Contacts/Memory/Selector
  → G6–G9 Engagement / Sound-Shared / Perf / Search AI
```

## 12. Быстрый cheat-sheet для программиста (состояние после F)

```text
Нужна боевая цель?          → TargetSelector.SelectedTarget
Нужно стрелять?             → GetEngageableSelectedTarget()
Нужна точка прицеливания?   → GetEngageableAimPointWorld()
Смена цели?                 → SelectedTargetChanged
Приоритет RTS?              → ForcedPriorityTarget = x; vision.RequestImmediateScan()
Сбросить цель и подождать?  → ClearSelectionAndNotifyIfHadTarget(); vision.DeferNextScan()
LoF blocked?                → SuppressCurrentTargetForLineOfFire(t); vision.RequestImmediateScan()
Форс перескан мира?         → UnitVision.RequestImmediateScan()
Кто сейчас в кадре зрения?  → UnitPerception.Observations
```

После G5 cheat-sheet обновится: selection из PerceivedContacts, не из сырого кадра observations.

---

Конец документа.
