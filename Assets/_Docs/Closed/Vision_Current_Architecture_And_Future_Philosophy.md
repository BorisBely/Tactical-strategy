# Зрение: текущая архитектура, справочник API, история и философия будущего

**Самодостаточный документ** — читается без открытия репозитория.  
**Проверено по коду:** 2026-08-19  
**Папка закрытых систем:** `Assets/_Docs/Closed/`  
**Расположение кода (справочно):** `Assets/_Scripts/Unit/Vision/`  
**Статус:** система зрения **заморожена** — Stage F + **G0–G8 CLOSED / VERIFIED** + калибровка A/B/C CLOSED. **Identity World Evidence FROZEN** (`Identity_World_Evidence.md`, Play PASS 49/0). **Combat Engage Execution FROZEN** (`Combat_Engage_Execution.md`, Play PASS 31/0, EditMode 14/0). **Search Navigation Execution FROZEN** (`Search_Navigation_Execution.md`, Play PASS 45/0, EditMode 18/0). **AI Perception Contract FROZEN** (`AI_Perception_Contract.md`, Play PASS 41/0). **AI-1 FROZEN** (`AI_Tactical_State_Model.md`, Play PASS 71/0). **AI-1A FROZEN** (`AI_UseOfForce_Policy.md`, Play PASS 107/0). Search / high-level AI — **другая система**, читает `AIPerceptionFrame`, не roadmap этого документа.  
**Автотесты:** EditMode `VisionFreezeTests`, `DetectionQualityMathTests`, `PerceivedContactLifecycleTests`, `IdentityKnowledgeMathTests`, `PerceivedIdentityTests`, `MemoryDecayMathTests`, `PerceivedMemoryTests`, `ContactSelectionEligibilityTests`, `TargetSelectionMathTests`, `TargetSelectorContactTests`, `EngagementDecisionMathTests`, `DefaultCombatEngagementPolicyTests`, `SoundPerceptionTests`, `SharedPerceptionTests`, `PerceptionFusionTests`, `VisionLodPolicyTests`. Play **2026-08-19 20:45** one-Play V1.9.5: G1 **20/0**, G2 **20/0**, G3 **30/0**, G4 **32/0**, G5 **21/0**, G6 **26/0**, G7 **29/0**, G8 **19/0**, G8 Stress **24/0** (`DetectionG_Regression_LAST.txt` PASS 9/0). Calibration Strict V1.9.4 **83/0**. Math menus G3–G8 as before. `*_LAST.txt` **не в git**. Каждый автотест **обязан** закончиться логом `RESULT=...` в Console (§9.2 п.7).

**Источник правды по статусу:** §0 и шапка. §9 — закрытый roadmap **G0–G8**. **Контракт для AI:** `AI_Perception_Contract.md` (FROZEN). Этапы другой системы сюда не добавлять. Не ретюнить зрение во время разработки AI.

---

## Как читать этот документ

| Раздел | Для кого |
|--------|----------|
| **`Combat_Engage_Execution.md`** | **Этап 2 FROZEN: CombatIntent Hold/Engage → существующий боевой контур** |
| **`Identity_World_Evidence.md`** | **Этап 1 FROZEN: look на цели ≠ Identity ≠ UnitTeam** |
| **`AI_Perception_Contract.md`** | **AI: FROZEN snapshot (AIPerceptionFrame)** |
| **`AI_Tactical_State_Model.md`** | **AI-1: FROZEN (6 states, Hold/Engage, Search from LastKnown)** |
| **`Vision_AI_Handoff.md`** | **Vision numbers freeze** |
| §0–1 | Быстрый статус «что есть / чего нет» |
| §2 | **Справочник скриптов, данных, методов** (standalone) |
| §3 | Поведение: боевой кадр (F) vs knowledge G1–G5 |
| §4 | Кто что читает (consumers) |
| §5–6 | История «до → этапы» |
| §7–8 | Границы системы + философия (не backlog G-этапов) |
| **§9** | **План работ Stage G0–G8** (закрыт) |
| §10–12 | Smoke / хронология / cheat-sheet |

---

## 0. Статус одной строкой

| Слой | В коде сейчас | Почему так |
|------|---------------|------------|
| Detect → Perception → TargetSelector → Engagement → Combat | ✅ Stage F + **G5** + **G6** + **G7** | Selector = кого; Engagement = что делать; Fire только Decision=Fire + LOS; Sound/Shared → Track |
| Shim `UnitVision.VisibleTarget` | ❌ Удалён (F) | Consumers мигрированы на TargetSelector |
| Detection / PerceivedContact boundary | ✅ G0 **CLOSED** | Типы отделены от `VisionObservation` |
| Detection progress / quality score | ✅ G1 + G1.1 **CLOSED** | Теперь источник selection (G5) |
| Contact lifecycle Observed / RecentlyLost / Lost | ✅ G2 **CLOSED** | Soft lose + LastSeen; dual-observer |
| LastSeenTime / LastSeenPosition | ✅ G2 | Заморозка evidence при потере LOS |
| LastSeenConfidence decay / stale LastKnown | ✅ G4 **CLOSED** | `LastSeenConfidence` стареет после LOS loss; `LastKnownPosition` ≠ live transform; Forgotten ≠ `ObservationState.Lost` |
| Identity / IdentityConfidence / Relationship / Threat | ✅ G3 **CLOSED** | Модификаторы priority; Unknown **можно** выбрать. Cue ≠ UnitTeam |
| Selector ← PerceivedContacts | ✅ G5 **CLOSED** | Кандидаты = contacts; Observations не список выбора; LastKnown ≠ aim |
| DetectionProcessor на боевых юнитах | ✅ G5 | `Unit.prefab`, SampleScene `EnemyPatrolUnit`, `EnsurePipelineComponents` |
| Engagement Decision слой | ✅ G6 **CLOSED** | `EngagementDecisionController`; DefaultCombatPolicy; Fire только при Decision=Fire |
| Sound / Shared → Perception | ✅ G7 **CLOSED** | Отдельные `SoundObservation` / `SharedObservation`; один contact на Transform; Play **PASS 29/0** |
| Perf / 500 m / LOD | ✅ **G8 CLOSED** | Compute budget: 4 tiers, FOV before LOS, TTL cache. AutoSmoke **PASS 19/0**, Stress **PASS 24/0**. Q/selector/engagement не тронуты |
| **Vision Freeze / AI Handoff** | ✅ **FROZEN 2026-08-19** | Калибровка A/B/C закрыта. Не ретюнить зрение ради Search / tactics |
| **AI Perception Contract** | ✅ **FROZEN 2026-08-19 23:10** | Play **PASS 41/0**. AI читает `AIPerceptionFrame`, не Q / DetectionProgress / UnitTeam |
| **AI-1 Tactical States** | ✅ **FROZEN 2026-08-20 00:08** | Play **PASS 71/0**. Search from LastKnown; Search does not write Memory |
| **AI-1A Use of Force** | ✅ **FROZEN 2026-08-20 00:38** | Play **PASS 107/0**. ForcePermission из Relationship; G6 math не трогать |
| **Combat Engage Execution** | ✅ **FROZEN 2026-08-20 10:56** | Play **PASS 31/0**, EditMode **14/0**. CombatIntent; AI не на prefab |
| **Search Navigation Execution** | ✅ **FROZEN 2026-08-20 12:06** | Play **PASS 45/0**, EditMode **18/0**. Walk к snapshot LastKnown; не пишет Memory |

**Правило состояний (уже в архитектуре):**

```text
Observed (Perception кадр)
  ≠ DetectionState (Undetected / Detecting / Detected)
  ≠ ObservationState (Observed / RecentlyLost / Lost)
  ≠ Selected (TargetSelector)
  ≠ Engageable (TargetEngageability)
  ≠ EngagementDecision (None / Track / Aim / Fire / Ignore)
  ≠ Fire (execution)

Detected ≠ Selected
Detected + Identity=Unknown     — валидно
Hostile + Threat=Low            — валидно
ForcePermission ≠ Fire          — Allowed не значит выстрел
Detected + RecentlyLost         — валидно
Detected + Lost                 — валидно
Lost + LastSeenConfidence=0     — валидно (forgotten ≠ удалён из registry)
DetectionProgress ≠ LastSeenConfidence ≠ IdentityConfidence
```

**Поток данных:**

```text
UnitObservationSource
    → UnitVision (G8 scheduler: cheap range/FOV → LOS/hit-zones → VisionObservation[])
    → UnitPerception.ApplyVisionFrame
    → event PerceptionFrameApplied
         ├→ DetectionProcessor (G1–G7) → PerceivedContact
         └→ TargetSelector ← contacts → EngagementDecision → Combat
              (eligibility + score; aim only if Observed LOS; Fire only if Decision=Fire)
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
| `DetectionProcessor` | **G1–G6**; Require `UnitPerception`; quality/progress + lifecycle + identity + memory + `IPerceivedContactRegistry`. На `Unit.prefab` и в `EnsurePipelineComponents`. |
| `EngagementDecisionController` | **G6**; order 30; Require Perception + Processor + Selector. `DefaultCombatEngagementPolicy`. Не стреляет. На `Unit.prefab` / `EnemyPatrolUnit` / `EnsurePipeline`. |

Сцена/мир:

| Компонент | Роль |
|-----------|------|
| `UnitVisionRegistry` | регистрация всех `UnitVision`; выдача opponents по team |
| `ShootingRangeTargetRegistry` | мишени полигона для Player |

Вспомогательные типы (не MonoBehaviour):

- `VisionCandidateProvider`, `VisionGeometry` (static), `VisibilityChecker`
- `VisionScanTier`, `VisionLodMath`, `VisionScanStats`, `VisionScanScheduler`, `VisionLosCache` (G8 compute budget)
- `VisionObservation` (struct DTO; + FovOffsetDegrees / Exposure01)
- `DetectionState`, `ObservationState`, `PerceivedContact`, `DetectionEvaluation`
- `PerceivedIdentity`, `PerceivedRelationship`, `ThreatLevel`, `ObservableAffiliation`, `VisualAffiliation`
- `DetectionQualityMath`, `IdentityKnowledgeMath`, `VisualAffiliationMapping`, `MemoryDecayMath`, `ContactSelectionEligibility`, `TargetSelectionMath`, `EngagementDecisionMath` (pure math)
- `TargetEngageability` (static; world viability only)
- `EngagementDecision`, `EngagementDecisionContext`, `IEngagementPolicy`, `DefaultCombatEngagementPolicy`
- `VisionSystemContract` (internal empty class — только XML-контракт)
- `IObservationSource` (interface)

`DetectionProcessor` — MonoBehaviour, см. таблицу wiring выше (G5: на юнитах).  
`VisualIdentityEvidence` — world look (Player/Enemy/Civilian) на цели; на `Unit.prefab` default Unknown. Наблюдатель маппит относительно своей стороны. Не `UnitTeam` цели.

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
| `FovOffsetDegrees` | `float` | угол forwardXZ↔цель (0 = центр); physical only |
| `Exposure01` | `float` | доля веса видимых hit-zone samples; legacy LOS → 1 |

---

### 2.1b. Detection / PerceivedContact (G1–G4)

`DetectionState`: Undetected / Detecting / Detected  
`ObservationState`: NotObserved / Observed / RecentlyLost / Lost  
`PerceivedIdentity` / `PerceivedRelationship`: Unknown / Friendly / Neutral / Hostile  
`ThreatLevel`: None / Low / Medium / High  
`ObservableAffiliation`: world-look cue (не UnitTeam)

`PerceivedContact`: Target, State, DetectionProgress, LastObservation, CurrentEvaluation, ObservationState, LastSeenTime / LastSeenPosition, **LastKnownPosition**, **LastSeenConfidence**, Identity, IdentityConfidence, Relationship, Threat, **SoundConfidence / SoundTime / SoundPosition**, **SharedConfidence / SharedTime / SharedPosition**, HasKnowledge / HasVisualEvidence / HasSoundEvidence / HasSharedEvidence  

`DetectionEvaluation`: VisibilityQuality + 4 factors (frame snapshot)  

`DetectionProcessor`: per-observer Contacts; create when progress&gt;0 **или** sound/shared `EnsureContact` (G7); Lost kept with LastSeen; RecentlyLost grace; G3 identity; G4 memory; G7 sound/shared TTL; G5 `IPerceivedContactRegistry` + `ContactsChanged`. На `Unit.prefab`.

`DetectionG1AutoSmoke` … `DetectionG8AutoSmoke` / `DetectionG8StressSmoke` → `Assets/_Docs/Logs/Tests/*_LAST.txt` в Play.  
Menu: G3 Identity; G4 Memory; G5 Selection; G6 Engagement; G7 Sound/Shared; G8 LOD math.

**Обязательно:** suite не считается законченным, пока в Console нет финального лога `RESULT=...` (см. §9.2 п.7). Зависание без этого лога ≠ PASS.

---

### 2.2. `UnitPerception` (MonoBehaviour)

**Смысл:** хранит **текущие кадры** по каналам. Не знает, кто producer. Vision / Sound / Shared **не смешиваются** в одном списке.  
**Не** зависит от `UnitVision`.  
Memory / LastObserved **не** живут здесь: contacts — `DetectionProcessor` (G2 LastSeen + **G4** LastSeenConfidence / LastKnownPosition + **G7** Sound/Shared channels).

**Свойства**

| Member | Тип | Описание |
|--------|-----|----------|
| `Observations` | `IReadOnlyList<VisionObservation>` | текущий кадр зрения |
| `SoundEvents` | `IReadOnlyList<SoundObservation>` | текущий кадр звука (G7) |
| `SharedEvents` | `IReadOnlyList<SharedObservation>` | текущий кадр shared (G7) |
| `ObservationCount` | `int` | |
| `HasAnyObservation` | `bool` | |

**События**

| Event | Когда |
|-------|--------|
| `PerceptionChanged` | содержимое кадра зрения изменилось (сравнение Target / HasAimPoint / IsVisible / AimPoint) |
| `PerceptionFrameApplied` | **каждый** `ApplyVisionFrame`, даже если content тот же |
| `SoundEventsApplied` | каждый `ApplySoundEvents` |
| `SharedEventsApplied` | каждый `ApplySharedEvents` |

**Методы**

```text
void ApplyVisionFrame(IReadOnlyList<VisionObservation> frame)
  — заменить кадр зрения; fire PerceptionChanged если changed; всегда fire PerceptionFrameApplied

void ApplySoundEvents(IReadOnlyList<SoundObservation> events)
void ApplySharedEvents(IReadOnlyList<SharedObservation> events)

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
void Collect(..., origin, maxDistanceSq)
  — после GetOpponents: squared-distance cull; maxDistanceSq < 0 отключает фильтр
void CollectOpponentsRaw(List<UnitVision> buffer)
```

Фильтры Collect: не self, enabled, consciousness targetable, alive DamageableTarget, optional shouldSkip.

---

### 2.5. `VisionGeometry` (static)

```text
bool IsWithinRangeAndFov(origin, forwardXZ, point, rangeSq, halfFovDegrees, out distanceSq)
  — проверка по XZ: дистанция + угол ≤ halfFov

bool IsWithinCoarseRangeAndFov(..., bounds, ..., out rangePass, out fovPass)
  — conservative: root + bounds center + ClosestPoint; false positives OK

float HorizontalDistanceSq(origin, point)

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
BindStats(VisionScanStats)          — G8 counters
ClearDebugRays()
TryFindBestVisibleAimPointFromHitZones(...)
TryFindBestVisibleAimPointFromCollider(...)
TryCoarseLineOfSightToBounds(...)   — closest point on combined bounds, not chest-only
HasLineOfSightToPoint(...)          — каждый вызов +1 LosCheckCount
TryGetLosBlocker(...)
```

Опирается на `UnitBodyHitZone` / utility aim candidates (Chest и др.).

---

### 2.7. `UnitVision` (MonoBehaviour) — DETECT ONLY

**Смысл:** периодический скан → список `VisionObservation` → `Perception.ApplyVisionFrame`.  
**Не** выбирает боевую цель. **Нет** API `VisibleTarget` (удалён).  
G8: **когда** делать дорогую работу (LOD / budget), не **что** значит contact.

**RequireComponent:** `UnitTeam`, `UnitObservationSource`, `UnitPerception`.

#### Сериализованные параметры (defaults)

| Параметр | Default | Смысл |
|----------|---------|--------|
| `VisionRange` | код 18; **`Unit.prefab` = 500** | дальность **detect/perception**. **Не** `TargetSelector.MaxEngageRange` (prefab **18 м**) |
| `FieldOfViewDegrees` | 120 | полный угол конуса |
| `TrackingHalfFovExtraDegrees` | 15 | бонус half-FOV при наличии Selected |
| `EyeHeight` | 1.6 | прокидывается в ObservationSource |
| `ScanIntervalMin` / `Max` | 0.25 / 0.45 | базовый интервал; G8 **масштабирует** по tier |
| `ImmediateRescanAngleDegrees` | 2.5 | внеочередной скан при повороте прицела |
| `CoarseFovPadDegrees` | 8 | conservative FOV pad до LOS |
| `CoarseRangePadMeters` | 4 | conservative range pad / spatial cull |
| `DetailQueueDelaySeconds` | 0.35 | T2 → очередь T3 |
| `DiscoverIntervalSeconds` | 0.5 | как часто idle observer делает T2 |
| `MembershipIntervalSeconds` | 1.5 | T1 cheap membership |
| `LosCacheTtlSeconds` | 0.3 | TTL LOS cache (~scan interval) |
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
float VisionRange
void RequestImmediateScan()     — этот observer → T3, тот же cheap→expensive pipeline (не «скан всего мира»)
void DeferNextScan()            — schedule next по интервалу (после clear selection)
VisionScanStats ScanStats
VisionScanTier CurrentScanTier
void NotifyWeaponReadyChanged(bool ready)  — invalidate sight + ImmediateScan

bool TryFindTargetInDirection(float worldAngle, float halfAngleDegrees, out Transform best)
float ResolveHalfFovDegreesForScan()

bool TryGetEngageFacingOriginWorld(out Vector3 origin)
bool TryGetEngageFacingForwardXZ(out Vector3 forwardXZ)
Vector3 GetEngageFacingOriginWorld()   — fallback на root/sight
```

#### Алгоритм скана (словами)

```text
1. ResolveObserverTier (Idle / Cheap / RangeFov / Detail). Skip ≠ empty frame.
2. Cheap: Collect after GetOpponents with distance cull. No rays, no ApplyVisionFrame.
3. RangeFov: coarse range/FOV (pad); queue T3 (~0.35s). No rays, no ApplyVisionFrame.
4. Detail (or ImmediateScan this observer):
     origin / forwardXZ / halfFov
     Collect + distance cull
     per candidate: coarse range/FOV → TTL cache → coarse bounds LOS
       → detailed hit-zones only if coarse LOS (or Selected / RecentlyLost)
       → exact FOV on aim → VisionObservation.IsVisible
     Perception.ApplyVisionFrame(list)
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
| `MaxEngageRange` | 18 | retain/range checks. **Не** VisionRange (perception 500 м на `Unit.prefab`) |
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

## 3. Поведение detection «как сейчас»

Два независимых контура. Их нельзя смешивать в одной фразе «зрение бинарное / уже progressive».

### 3.1. Боевой контур (Stage F) — бинарный кадр

Это то, чем живут fire / aim / patrol **сейчас**:

```text
кандидат прошёл range + FOV + LOS
  → VisionObservation.IsVisible = true
  → TargetSelector: nearest / forced → Selected
```

Combat **не** читает VisibilityQuality, DetectionProgress, ObservationState, LastSeen.

**Почему Combat ещё бинарный:** G1/G2 намеренно параллельны. Подключать knowledge к выбору цели — **G5**, и только после Identity (**G3**). Иначе «плохо обнаружил» нельзя отличить от «плохо выбрал».

### 3.2. Knowledge + selection (G1–G7)

`DetectionProcessor` на боевом юните. Selector читает contacts:

```text
PerceptionFrameApplied
  → DetectionProcessor Tick → ContactsChanged
  → eligibility + TargetSelectionMath
  → SelectedTarget
  → GetEngageableSelectedTarget только при LOS aim
```

Unknown identity **можно** выбрать. Friendly / Neutral identity — нет (policy). Forgotten — нет. LastKnown **не** aim.

Вне этой системы (не Vision roadmap):

- Search / hunt AI (поведение, отдельная система)
- стрельба по LastKnown / SoundPosition

G8 (compute budget, не семантика): 4 scan tiers, FOV before LOS, TTL LOS cache. Q по-прежнему Distance × FOV × Exposure × Movement.

Частичная видимость hit-zones в **Combat** влияет на **какой aim point виден**. В **G1** та же доля идёт в `Exposure01` → Q.

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

Дальше G0–G7 **не меняли** этот инвариант для Combat. G8 меняет только стоимость скана, не смысл contact.

### 6.5. Stage G0–G2 (готово, параллельный слой)

См. §9. Combat path = Selector (G5 contacts) + Engageable/LOS aim + G6 Decision + G7 sound/shared Track. **G8 CLOSED** (budget). Документ зрения предварительно завершён.

---

## 7. Границы этого документа

Ниже — что **уже есть** в зрении и что **намеренно не входит** (другая система или не цель «глаза»). Это не открытый G-roadmap. Числа и `PerceivedContact` **заморожены**: `Vision_AI_Handoff.md`.

### 7.0. Уже есть (не считать gap)

| Тема | Где | Ограничение |
|------|-----|-------------|
| Небинарный Q + Detection Progress + hysteresis | G1 / G1.1 | Только на `DetectionProcessor` (opt-in) |
| Instant obvious vs delayed hard cases | G1 acquire/lose rates | Не влияет на Combat |
| Движение **цели** как множитель заметности | G1 MovementFactor ≥ 1 | Idle=1, только бонус |
| Observed → RecentlyLost → Lost + LastSeen | G2 | LastSeen freeze; decay confidence — G4 |
| Dual-observer независимые contacts | G2 | Не подключено к Selector |
| Identity / IdentityConfidence / Relationship / Threat | G3 | Opt-in процессор; не кормит Combat. Cue ≠ UnitTeam |
| LastSeenConfidence / LastKnownPosition | G4 | Opt-in процессор; Stale derived; Forgotten ≠ ObservationState.Lost; Identity не decay |

### Detection quality — остаток

| Пункт | Статус | Почему не сделано |
|-------|--------|-------------------|
| Градация до ~500 м «только хорошо заметные» | Частично G1 (`DistanceFarMeters` = 500) + G8 budget | Формула Q есть. **`Unit.prefab` VisionRange = 500 м** (perception). **MaxEngageRange остаётся 18 м**. Не сливать. Дальний detect дешёвый за счёт LOD, не за счёт урезания range |
| Состояние **наблюдателя** (идёт / бежит / занят) как detection modifiers | ❌ Нет | Не входило в G1 (там только движение **цели**). Намеренно позже: иначе Vision узнаёт всю game logic. Путь — ObservationSource + параметры, без знания анимаций внутри Vision. Не G3 |

Lighting / camouflage / fatigue / attention — **не цель системы** (см. §8.9). Не backlog.

### Identity / Threat / Relationship — **G3 CLOSED** (остаток — использование в бою)

Слой знания есть на `PerceivedContact`. **Не сделано и не входит в G3:**

| Пункт | Статус | Почему |
|-------|--------|--------|
| Selector читает Identity/Threat | ✅ G5 priority | Не fire gate; Unknown всё ещё selectable |
| Ошибка восприятия влияет на fire | ❌ G6 | Иначе G3/G5 смешается с решением стрелять |
| VisualIdentityEvidence на юнитах | ✅ этап 1 **FROZEN** | look ≠ Identity; без look Identity остаётся Unknown |

Полная цепочка identify → threat → **decide ≠ fire** — decide это G6.

### Memory — G2 lifecycle + **G4 CLOSED**

| Пункт | Статус | Почему |
|-------|--------|--------|
| CurrentlyObserved → RecentlyLost → Lost | ✅ G2 | |
| LastSeenPosition / LastSeenTime | ✅ G2 | Заморозка evidence, не live transform |
| LastSeenConfidence / stale LastKnown | ✅ G4 | Parametric decay; contact остаётся при confidence=0 |

### Другие входы Perception / AI modes

| Пункт | Статус | Почему |
|-------|--------|--------|
| Sound / Shared info → Perception | ✅ G7 CLOSED | Отдельные observation types; один contact; Sound ≠ Vision. Play PASS 29/0 |
| «обнаружил ≠ стрелять» | ✅ G6 | `EngagementDecision`; Fire только Decision=Fire + LOS aim. Observe/Report/Suppress ещё не роли |

### Perf model 500 м — **G8 CLOSED / VERIFIED**

- 4 compute-budget tiers: Idle skip / Cheap membership / RangeFOV без лучей / Detail LOS+hit-zones
- Conservative coarse range/FOV **до** любого LOS (hit-zone path больше не стреляет лучи до конуса)
- Spatial cull: squared-distance после `GetOpponents`, без дерева
- TTL LOS cache (~scan interval) + invalidation по движению origin/target/forward
- ImmediateScan: этот observer → T3, тот же pipeline
- Stress stubs 10/25/50/100 (не клоны `Unit.prefab`)
- **Нет** `LOD → confidence`. Skip-scan ≠ empty frame

Play AutoSmoke **PASS 19/0**, Stress **PASS 24/0** (2026-08-19).

### Wiring, которое выглядит как «не доделали G2»

`DetectionProcessor` стоит на `Unit.prefab` и создаётся в `EnsurePipeline` (**G5**). До G5 это было opt-in, чтобы contacts не жили в проде без selection.

---

## 8. Философия будущей системы зрения

> **Намерение целевой модели**, не обещание что всё уже в коде.  
> Что сделано / что нет — §0 и §7. Этот раздел не удалять: он задаёт, **зачем** следующие этапы.

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

**Главный архитектурный gap после G6 (закрыт в рамках зрения):** named decision есть (`EngagementDecision`). Роли Scout/MG/Commander и полноценный Observe/Report/Suppress — не слой зрения; живут в боевой/ролевой AI-системе, не в этом документе.

### 8.7. Потеря цели

Целевая модель (не менялась):

```text
CurrentlyObserved → RecentlyLost → Lost
```

+ LastKnownPosition / LastSeenTime / LastSeenConfidence. Поиск цели по LastKnown — **не** слой зрения.

**Что уже сделано (G2+G4):** `ObservationState` Observed / RecentlyLost / Lost, `LastSeenTime` / `LastSeenPosition`, grace, contact остаётся после Lost; `LastKnownPosition` + `LastSeenConfidence` decay; Stale derived from confidence.

**Handoff наружу (не backlog зрения):** LastKnown / RecentlyLost — вход для отдельной AI-системы. Контракт: `Vision_AI_Handoff.md`. В Vision / DetectionProcessor / TargetSelector / EngagementDecisionController поиск не встраивать.

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

| Принцип | Сейчас | Почему не «готово целиком» |
|---------|--------|----------------------------|
| World ≠ Perception | Частично | Per-observer contacts + G5 selection + G6 named decision. Fire всё ещё LOS + Decision=Fire |
| Vision ≠ Select | ✅ | Stage F |
| Detection → PerceivedContact | ✅ G0 types + G1–G7 runtime | Selector читает contacts; Engagement читает selected+contact; Sound/Shared — каналы, не Vision |
| Небинарный detection | ✅ G1 VisibilityQuality + DetectionProgress | Процессор на юнитах; fire = Decision=Fire |
| Perceived ≠ UnitTeam | ✅ G1–G7 | Contacts/identity/memory/engagement/sound/shared не трогают UnitTeam |
| Subjective Identity / Relationship / Threat | ✅ G3 | Priority modifiers in G5; G6 Ignore Friendly/Neutral; не fire-from-Hostile |
| LastKnown confidence decay | ✅ G4 | LastSeenConfidence; IdentityConfidence hold |
| Engagement Decision слой | ✅ G6 | DefaultCombatPolicy; роли не раздуты |
| Sound/Shared | ✅ G7 CLOSED PASS 29/0 | SoundObservation / SharedObservation → Processor channels; Track, не Fire |
| Cheap→expensive pipeline | ✅ G8 CLOSED PASS 19/0 + Stress 24/0 | 4 tiers; FOV before LOS; TTL cache |
| Observer stance/busy modifiers | вне документа | Не G-этап зрения; Q не обязан знать анимации наблюдателя |

Документ зрения **заморожен** после G8 + калибровки A/B/C. DetectionProcessor, TargetSelector и EngagementDecision стоят на `Unit.prefab`. AI читает `PerceivedContact` (`Vision_AI_Handoff.md`), не открывает новый G-этап.

---

## 9. План работ Stage G0–G8 (закрыт)

> Stage F завершил `Detect → Perception → TargetSelector → Combat` и удалил `VisibleTarget`.  
> Дальше — **не сыпать механику**, а наращивать слои поверх фундамента.  
> **G0–G5** = основной архитектурный рефакторинг. **G6–G8** = расширение (decision / sound / perf).  
> **Не объединять** G1+G2+G3 в один этап: иначе невозможно отладить «плохо обнаружил / плохо накопил / плохо идентифицировал / плохо выбрал».  
> Search / high-level AI **не** входит в этот план — другая система.

### 9.0. Roadmap одной схемой

```text
G0  Contracts / data ownership / invariants          ✅ CLOSED
 ↓
G1  Detection Quality                                ✅ CLOSED
 ↓
G1.1 hysteresis / DetectionEvaluation                ✅ CLOSED
 ↓
G2  Perceived Contacts (lifecycle / LastSeen)        ✅ CLOSED
 ↓
G3  Identity / Confidence / Threat / Relationship    ✅ CLOSED
 ↓
G4  Memory decay / LastKnown confidence              ✅ CLOSED
 ↓
G5  TargetSelector ← Perceived Contacts              ✅ CLOSED PASS 21/0
 ↓
G6  Engagement Decision                              ✅ CLOSED PASS 26/0
 ↓
G7  Sound / Shared perception                        ✅ CLOSED PASS 29/0
 ↓
G8  Performance / 500m / LOD                         ✅ CLOSED PASS 19/0 + Stress 24/0
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

### Stage G0 — Detection / Perceived Knowledge boundary — **CLOSED** (types only)

**Цель:** зафиксировать границу `VisionObservation` ≠ `PerceivedContact` / `DetectionState`, **не меняя геймплей Stage F**.

Runtime-stub `DetectionProcessor` на G0 **не нужен** был и был удалён. Реальный `DetectionProcessor` вернулся в **G1** как quality/progress слой (см. ниже), всё ещё без кормления Combat.

#### Фактическая реализация (код)

| Файл | Роль |
|------|------|
| `Assets/_Scripts/Unit/Vision/DetectionState.cs` | enum `Undetected` / `Detecting` / `Detected` |
| `Assets/_Scripts/Unit/Vision/PerceivedContact.cs` | `Target`, `State`, `DetectionProgress`, `LastObservation` |
| `VisionSystemContract.cs` | G0 = type boundary; Combat path = Stage F only |
| `VisionObservation.cs` | XML: не путать с PerceivedContact |

**Combat pipeline (без изменений):**

```text
UnitVision → UnitPerception.ApplyVisionFrame → TargetSelector → Combat
```

**Knowledge types (на момент закрытия G0 не были в runtime; сейчас подключены в G1/G2, но не к Combat):**

```text
DetectionState
PerceivedContact   // отдельный тип от VisionObservation
```

G0 закрыт как **types-only**. Runtime contacts появились в G1 (quality) и G2 (lifecycle). TargetSelector по-прежнему не читает их — это не регресс G0, а правило до G5.

#### Ownership (зафиксировано)

```text
VisionObservation  = физический факт текущего кадра
PerceivedContact   = внутренняя информация AI (G1: quality+progress runtime; Selector wiring → G5)
TargetSelector     = по-прежнему только Perception.Observations
```

#### Запрещено на G0 (не делалось)

```text
DetectionProcessor / verify-only parallel MonoBehaviour
distance / movement / body / observer multipliers
Detection Progress tuning / decay
RecentlyLost / LastKnown / Identity / Threat / Relationship
Search AI
изменение TargetSelector / Fire / Aim / Nav
knowledge-поля в VisionObservation
```

#### Критерий завершения G0 (выполнен)

1. VisionObservation остаётся physical-only  
2. Отдельный контракт DetectionState / PerceivedContact  
3. Stage F combat без изменений  
4. Нет мёртвого verify-only runtime layer  

**Commit message:** `Vision: introduce detection/knowledge boundary`

**Следующий этап:** G1 — Detection Quality (implemented ниже).

---

### Stage G1 — Detection Quality / Detection Progress — **IMPLEMENTED**

**Цель:** небинарное качество видимости + накопление DetectionProgress на **параллельном** слое, **без** изменения Combat path Stage F.

```text
Combat (без изменений):
  Vision → Perception → TargetSelector → Combat

Параллельно G1:
  PerceptionFrameApplied → DetectionProcessor → PerceivedContact
```

**Инвариант:** `Detected ≠ Selected`. Detection не трогает `UnitTeam` / Fire / TargetSelector.

#### Физические метрики Observation (не knowledge)

`VisionObservation` расширен только physical fields:

| Поле | Смысл |
|------|--------|
| `FovOffsetDegrees` | угол цель↔forward XZ (0 = центр) |
| `Exposure01` | доля веса видимых hit-zone samples; legacy collider LOS → 1 |

`DetectionProgress` **не** пишется в Observation.

#### VisibilityQuality

```text
Q = clamp01(DistanceFactor * FovFactor * ExposureFactor * MovementFactor)
```

| Factor | Источник |
|--------|----------|
| Distance | `sqrt(DistanceSq)` → smooth 1→low к `DistanceFarMeters` (~500) |
| FOV | `FovOffsetDegrees` → 1 в центре, low у края (smooth) |
| Exposure | `Exposure01` |
| Movement | скорость цели (NavAgent / delta pos); idle=1, walk/run >1, capped |

Нет lighting / camouflage / fatigue / attention.

#### DetectionProgress (G1.1 hysteresis)

```text
Q > AcquireThreshold (~0.35)  → progress += Q * acquireRate * dt
LoseThreshold < Q ≤ Acquire   → hold
Q ≤ LoseThreshold (~0.20)     → progress -= (1-Q) * lossRate * dt
```

- `AcquireTime ≪ LossTime`; `MovementFactor >= 1` (idle=1, только бонус)
- Между сканами Q держится от последнего Perception frame
- Empty frame → Q=0, **LastObservation не перезаписывается**
- Factors живут в `DetectionEvaluation CurrentEvaluation` (не god-fields на contact)

**Verified (исторически):** ранний прогон G1 AutoSmoke был PASS 18/0; финальный G1.1 runner на 2026-08-18 — **PASS 20/0** (`DetectionG1_LAST.txt`). Оба прогона закрывают этап; актуальная цифра — 20/0.

#### State

```text
progress == 0     → Undetected
0 < progress < 1  → Detecting
progress >= 1     → Detected
```

#### Файлы

| Файл | Роль |
|------|------|
| `DetectionProcessor.cs` | opt-in; Q + progress + G2 lifecycle |
| `DetectionQualityMath.cs` | pure math + hysteresis |
| `DetectionEvaluation.cs` | frame quality snapshot |
| `PerceivedContact.cs` | Target/State/Progress/LastObservation/CurrentEvaluation (+ G2 fields) |
| `DetectionG1AutoSmoke.cs` | Play report → `DetectionG1_LAST.txt` |

**Commit message (G1):** `Vision: add detection quality and progress`  
**Commit message (G1.1):** `Vision: harden detection progress hysteresis`

**Автопроверка G1/G1.1 — CLOSED / VERIFIED 2026-08-18:**
- EditMode: `DetectionQualityMathTests.cs`
- Play: `DetectionG1AutoSmoke` → `Assets/_Docs/Logs/Tests/DetectionG1_LAST.txt` → **RESULT=PASS pass=20 fail=0**
- Menu: `Tools/Tests/Run DetectionG1 Math Smoke (no Play)`

**Этап G1/G1.1 закрыт.** Следующий был G2 (ниже — тоже закрыт).

---

### Stage G1.1 — polish (hysteresis / contact slim) — **CLOSED**

См. DetectionProgress hysteresis + `DetectionEvaluation` выше. Существенных переделок Combat не было.

---

### Stage G2 — Perceived Contact Lifecycle — **CLOSED**

**Цель:** `PerceivedContact` = subjective knowledge **конкретного** observer; знание переживает потерю LOS.

```text
DetectionState (Undetected/Detecting/Detected)
  ⊥
ObservationState (NotObserved/Observed/RecentlyLost/Lost)
```

`Detected + RecentlyLost` / `Detected + Lost` валидны. TargetSelector / Combat / UnitTeam **не менялись**.

#### Поля contact (G2)

```text
ObservationState
LastSeenTime / LastSeenPosition   // memory; AimPoint только внутри LastObservation
LastObservation   // только real evidence
CurrentEvaluation // текущий Q snapshot
```

#### Поведение

- pending track until `DetectionProgress > 0`, then promote to contact
- observation → Observed + refresh LastSeen/LastObservation
- missing → RecentlyLost (grace `RecentlyLostDurationSeconds`), Q=0, LastSeen сохранён
- grace timeout → Lost; contact остаётся в registry (LastSeen для G4); optional cleanup только если `m_RemoveContactWhenUndetectedAndLost`
- reacquire → тот же contact instance, LastSeen обновляется

Registry уже per-`DetectionProcessor` (observer-local).

**Wiring:** процессор **opt-in**. На `Unit.prefab` нет. Это не дыра этапа: G2 не должен был менять боевых юнитов.

#### Автопроверка G2 — CLOSED / VERIFIED 2026-08-18

- EditMode: `PerceivedContactLifecycleTests.cs` (soft lose, grace−ε, reacquire, dual observer, LastSeen≠live transform)
- Play: `DetectionG2AutoSmoke` → `Assets/_Docs/Logs/Tests/DetectionG2_LAST.txt` → **RESULT=PASS pass=20 fail=0** (файл отчёта gitignored, см. шапку)
  - A/B independence, RecentlyLost→Lost (contact kept), reacquire same instance + LastSeen update

**Этап G2 закрыт.**  
**Commit message:** `Vision: add perceived contact lifecycle`

**Следующий этап:** G3 Identity / Threat (implemented ниже; Selector всё ещё не читает contacts).

---

### Stage G2 — (historical roadmap notes)

Черновик до реализации. **Не удалять:** показывает, какие поля contact были задуманы заранее.

**Не** переносить Detection Progress в TargetSelector. (Соблюдено: Selector его не читает. Перенос — запрещён до G5.)

Нужен слой/объект уровня:

```text
PerceivedContact
{
  Entity                    // ✅ G2: Target
  DetectionState            // ✅ G1/G2
  DetectionConfidence       // ✅ G1: DetectionProgress (+ Q в CurrentEvaluation)
  LastObservation           // ✅ G1/G2
  LastKnownPosition         // ✅ G4 (belief; freeze while lost; no motion prediction)
  LastSeenConfidence        // ✅ G4
  IdentityConfidence        // ✅ G3
  Threat                    // ✅ G3
  Relationship              // ✅ G3
}
```

Структура может отличаться — важен принцип:

```text
VisionObservation  = сырой физический факт текущего скана
PerceivedContact   = накопленная субъективная информация этого AI
```

Соответствует `World ≠ Perception`.

**Проверка — два наблюдателя (G2 CLOSED, verified):**

```text
Soldier A видит цель, Soldier B нет
→ A.PerceivedContacts ≠ B.PerceivedContacts
→ ActualWorld и UnitTeam объекта одинаковы
```

Это главный архитектурный smoke новой системы. Покрыто EditMode `DualObservers_IndependentContacts_SameTarget` и Play `DetectionG2AutoSmoke`.

---

### Stage G3 — Identity / Confidence / Relationship / Threat — **CLOSED**

**Цель:** субъективное Identity / Confidence / Threat / Relationship на `PerceivedContact`, **без** изменения TargetSelector и Combat.

```text
DetectionProgress ≠ IdentityConfidence
Detected + Identity=Unknown     — валидно
Relationship=Hostile + Threat=Low — валидно
PerceivedIdentity ≠ world UnitTeam
```

Evidence **не** копирует `UnitTeam`. Входы:

- per-observer `DetectionProcessor.SetAffiliationCue` (тесты / override)
- `VisualIdentityEvidence` на цели (Player/Enemy/Civilian look; маппит наблюдатель)

`IdentityKnowledgeMath`: identity растёт только при Observed + cue≠Unknown; IdentifyTime **4 с** ≫ AcquireTime 0.35 с; commit 0.50 (Hostile ≈ **2 с** при Q=1, conf=1 ≈ **4 с**); Lost/RecentlyLost **hold IdentityConfidence**. Смена **валидного** cue, конфликтующего с committed Identity: confidence сбрасывается, Identity→Unknown, накопление заново (не мгновенный remap). Missing/Unknown cue по-прежнему hold. G4 decays **LastSeenConfidence** only (не Identity). Threat = f(Relationship, distance). Block C **CLOSED / VERIFIED** (2026-08-19 22:37): `Vision_Gameplay_Calibration.md` §13.

#### Файлы

| Файл | Роль |
|------|------|
| `PerceivedIdentity.cs` / `PerceivedRelationship.cs` / `ThreatLevel.cs` / `ObservableAffiliation.cs` | enums |
| `IdentityKnowledgeMath.cs` | pure math (нет UnitTeam) |
| `VisualIdentityEvidence.cs` | world look cue |
| `PerceivedContact.cs` | Identity / IdentityConfidence / Relationship / Threat |
| `DetectionProcessor.cs` | TickIdentity + per-observer cues |
| `DetectionG3AutoSmoke.cs` | Play → `DetectionG3_LAST.txt` |
| `IdentityKnowledgeMathTests.cs` / `PerceivedIdentityTests.cs` | EditMode |
| Menu | `Tools/Tests/Run DetectionG3 Identity Smoke (no Play)` |

**Автопроверка G3 — CLOSED / VERIFIED 2026-08-19:**
- EditMode: `IdentityKnowledgeMathTests.cs`, `PerceivedIdentityTests.cs`
- Play: `DetectionG3AutoSmoke` → `Assets/_Docs/Logs/Tests/DetectionG3_LAST.txt` → **RESULT=PASS pass=30 fail=0**
- Menu: `Tools/Tests/Run DetectionG3 Identity Smoke (no Play)` → **PASS 12/0**
  - dual-observer A≠B, Neutral world team, Hostile+far=Low, isolation Selector/Observation

**Этап G3 закрыт.** Selector / Combat / `UnitTeam` **не менялись**.

**Следующий этап:** G5 Selector ← contacts (G4 memory closed).

---

### Stage G4 — Memory — **CLOSED**

**Цель:** `LastSeenConfidence` / `LastKnownPosition` на `PerceivedContact`. Не Search AI, не Selector, не decay Identity.

```text
DetectionProgress ≠ LastSeenConfidence ≠ IdentityConfidence
ObservationState.Lost ≠ memory forgotten (confidence → 0)
Stale = derived from LastSeenConfidence (не новое ObservationState)
LastKnownPosition ≠ live transform.position
```

`LastSeenPosition` / `LastSeenTime` остаются G2 freeze. `LastKnownPosition` на старте G4 = копия LastSeen и **заморожен** while lost (нет экстраполяции движения). Observed → confidence 1, LastKnown = LastSeen. Reacquire → LastSeen/LastKnown/Time обновляются, confidence → 1; **Identity не сбрасывается**.

`MemoryDecayMath.Evaluate(elapsed, initial, horizon=30, shape=1.5)`: parametric `(1 - t/H)^shape * initial`. Block B **CLOSED / VERIFIED** (2026-08-19 22:03): RecentlyLost **5 с**, horizon **30 с**, stale **0.25**. Калибровка: `Vision_Gameplay_Calibration.md` §12.

#### Файлы

| Файл | Роль |
|------|------|
| `MemoryDecayMath.cs` | pure math (нет Unit / Transform / Vision / Combat) |
| `PerceivedContact.cs` | LastKnownPosition / LastSeenConfidence / HasMemory / IsMemoryStale |
| `DetectionProcessor.cs` | TickMemory после TickIdentity; HUD Memory |
| `PerceivedContactLifecycleSimulator.cs` | тот же memory tick для EditMode |
| `DetectionG4AutoSmoke.cs` | Play → `DetectionG4_LAST.txt` |
| `MemoryDecayMathTests.cs` / `PerceivedMemoryTests.cs` | EditMode |
| Menu | `Tools/Tests/Run DetectionG4 Memory Smoke (no Play)` |

**Автопроверка G4 — CLOSED / VERIFIED 2026-08-19:**
- EditMode: `MemoryDecayMathTests.cs`, `PerceivedMemoryTests.cs`
- Play: `DetectionG4AutoSmoke` на `DetectionG1Harness` (SampleScene), `DefaultExecutionOrder(400)`, warmup 22s → `Assets/_Docs/Logs/Tests/DetectionG4_LAST.txt` → **RESULT=PASS pass=32 fail=0**
- Menu: `Tools/Tests/Run DetectionG4 Memory Smoke (no Play)` → **PASS 11/0**
  - Observed conf=1, LastKnown=LastSeen; RecentlyLost freeze + high conf; Lost LastKnown ≠ live transform; memory ≠ DetectionProgress; Identity hold while memory decays; horizon conf=0 contact kept; reacquire same instance conf=1 Identity preserved; Selector isolation

**Этап G4 закрыт.** Selector / Combat / `UnitTeam` / `Unit.prefab` / Identity commit rules **не менялись**.

**На этом этапе НЕ делалось:** Search (другая система), Selector←contacts (G5), decay IdentityConfidence, постановка процессора на `Unit.prefab`.

---

### Stage G5 — TargetSelector ← Perceived Contacts — **CLOSED / VERIFIED**

**Цель:** Selector выбирает из observer-local `PerceivedContact`, не из текущего кадра `VisionObservation`.

```text
PerceivedContacts → eligibility → score → SelectedTarget
Selected ≠ Engageable ≠ Fire
Unknown identity selectable
Forgotten not selectable
LastKnown ≠ combat AimPoint
```

`IPerceivedContactRegistry` / `ContactsChanged` на `DetectionProcessor`. `ContactSelectionEligibility` + `TargetSelectionMath` — pure helpers. ForcedPriority требует eligible contact. Reload retain — Stage F LOS compatibility, но contact должен оставаться eligible. `GetEngageableSelectedTarget` только при LOS-confirmed aim.

#### Файлы

| Файл | Роль |
|------|------|
| `IPerceivedContactRegistry.cs` | Contacts / TryGet / ContactsChanged |
| `ContactSelectionEligibility.cs` | policy + reject reasons |
| `TargetSelectionMath.cs` | score; observed aim helper |
| `TargetSelector.cs` | SelectFromContacts |
| `DetectionProcessor.cs` | registry + event after Tick |
| `Unit.prefab` / SampleScene `EnemyPatrolUnit` / `EnsurePipelineComponents` | production processor |
| `DetectionG5AutoSmoke.cs` | Play → `DetectionG5_LAST.txt` |
| EditMode | `ContactSelectionEligibilityTests`, `TargetSelectionMathTests`, `TargetSelectorContactTests` |
| Menu | `Tools/Tests/Run DetectionG5 Selection Smoke (no Play)` |

**Автопроверка G5 — CLOSED / VERIFIED 2026-08-19:**
- EditMode: `ContactSelectionEligibilityTests`, `TargetSelectionMathTests`, `TargetSelectorContactTests`
- Play: `DetectionG5AutoSmoke` order 500, warmup 40s → `Assets/_Docs/Logs/Tests/DetectionG5_LAST.txt` → **RESULT=PASS pass=21 fail=0**
- Menu: `Tools/Tests/Run DetectionG5 Selection Smoke (no Play)` → **PASS 5/0**
  - Unknown selectable; Friendly/forgotten out; observe → Selected + engageable aim; hide → Selected remains, HasSelectedAimPoint false; horizon → deselect; reacquire restores aim; ForcedPriority needs contact; ClearContacts deselects; LastKnown ≠ aim

**Этап G5 закрыт.** G6 EngagementDecision добавлен отдельным слоем. Search / Sound **не** добавлялись. Identity на префабе остаётся Unknown, пока спавн не запишет `VisualIdentityEvidence`.

---

### Stage G6 — Engagement Decision — **CLOSED / VERIFIED**

**Цель:** отделить «что делать» от выбора цели и от выстрела.

```text
Detected ≠ Identified ≠ Threat ≠ Selected ≠ Engageable ≠ Fire
TargetSelector = КОГО
EngagementDecision = ЧТО ДЕЛАТЬ
FireController = ВЫПОЛНИТЬ Fire
```

`EngagementDecision`: None | Ignore | Observe | Track | Aim | Fire | Suppress | Report.  
DefaultCombatPolicy использует None / Ignore / Track / Aim / Fire. Observe / Suppress / Report зарезервированы.

Иерархия DefaultCombatPolicy: нет цели → None; forgotten / Friendly / Neutral / не engageable → Ignore; нет LOS aim → Track; LOS без aim-progress или оружия → Aim; иначе Fire. Unknown может Fire. Threat не открывает Fire. LastKnown ≠ aim.

FireController: контакт (дисциплина) = Aim|Fire; выстрел = только Fire. `TargetEngageability` не уничтожен.

#### Файлы

| Файл | Роль |
|------|------|
| `EngagementDecision.cs` | enum |
| `EngagementDecisionContext.cs` | snapshot |
| `IEngagementPolicy.cs` / `DefaultCombatEngagementPolicy.cs` | policy |
| `EngagementDecisionMath.cs` | pure evaluate |
| `EngagementDecisionController.cs` | order 30; CurrentDecision |
| `UnitWeaponFireController.cs` | Aim\|Fire contact; Fire shot gate |
| `Unit.prefab` / `EnemyPatrolUnit` / `EnsurePipeline` | processor + TargetSelector + engagement |
| `DetectionG6AutoSmoke.cs` | Play → `DetectionG6_LAST.txt` |
| EditMode | `EngagementDecisionMathTests`, `DefaultCombatEngagementPolicyTests` |
| Menu | `Tools/Tests/Run DetectionG6 Engagement Smoke (no Play)` |

**Автопроверка G6 — CLOSED / VERIFIED 2026-08-19:**
- EditMode: `EngagementDecisionMathTests`, `DefaultCombatEngagementPolicyTests`
- Play: `DetectionG6AutoSmoke` order 600, warmup 60s → `Assets/_Docs/Logs/Tests/DetectionG6_LAST.txt` → **RESULT=PASS pass=26 fail=0**
- Menu: `Tools/Tests/Run DetectionG6 Engagement Smoke (no Play)` → **PASS 10/0**
  - None when no target; Unknown may Fire; Friendly/forgotten Ignore; memory Track not Fire; LOS without aim progress → Aim; gates pass → Fire; hide → Track; LastKnown ≠ aim; forget → None; reacquire Aim/Fire; ClearContacts → None

**Этап G6 закрыт.** Scout/MG/Commander policies, AimProgress/FireDiscipline migration, RPG/vehicles, Search **не** добавлялись.

---

### Stage G7 — Sound / Shared Information — **CLOSED**

**Цель:** Vision / Sound / Shared → один `PerceivedContact`. Sound **не** притворяется Vision.

```text
Vision ──┐
Sound ───┼─► UnitPerception (раздельные списки) → DetectionProcessor → PerceivedContact
Shared ──┘
```

`SoundObservation`: Source, Position, Direction, Loudness, Type, Time, SourceConfidence.  
`SharedObservation`: Subject, SourceUnit, Position, Time, SourceConfidence, InformationType=ContactReport.  
Ключ контакта — Transform (`Source` / `Subject`). `Source == null` не создаёт ghost. Identity из sound/shared **не** коммитится (Unknown, пока нет G3 vision cue).

Каналы на контакте: `SoundConfidence` / `SharedConfidence` (+ Time/Position).  
`HasKnowledge` = LastSeen **или** sound **или** shared.  
`LastSeen*` / `LastObservation` — только зрение. Sound/Shared могут обновить `LastKnownPosition`, если контакт **не** Observed. G4 decay LastSeen **не** останавливается.

TTL: `SoundKnowledgeMath` ~3 с, `SharedKnowledgeMath` ~8 с (reuse `MemoryDecayMath.Evaluate`).

Selector source-blind: eligibility = `HasKnowledge`; score confidence = max(LastSeen, Sound, Shared); aim только Observed + LastObservation.  
Engagement: нет sound-типов; `HasKnowledge` в контексте; без LOS → **Track**, никогда Fire.

**Вне G7:** hearing cone, radio, gunshot→AI hearing (`UnitWeaponFireAudio` playback only), direction-only ghosts.

#### Файлы

| Файл | Роль |
|------|------|
| `SoundObservation.cs` / `SoundEventType.cs` / `SoundKnowledgeMath.cs` | sound facts + TTL |
| `SharedObservation.cs` / `SharedInformationType.cs` / `SharedKnowledgeMath.cs` | report facts + TTL |
| `PerceivedContact.cs` | каналы + HasKnowledge |
| `UnitPerception.cs` | `ApplySoundEvents` / `ApplySharedEvents` |
| `DetectionProcessor.cs` | EnsureContact, TickSound/TickShared, synthetic APIs |
| `ContactSelectionEligibility.cs` | Forgotten = !HasKnowledge |
| `TargetSelectionMath.cs` | max трёх confidence |
| `EngagementDecisionContext.cs` / `EngagementDecisionMath.cs` | HasKnowledge; Track без LOS |
| `DetectionG7AutoSmoke.cs` | Play order 700, warmup 80s → `DetectionG7_LAST.txt` |
| EditMode | `SoundPerceptionTests`, `SharedPerceptionTests`, `PerceptionFusionTests` |
| Menu | `Tools/Tests/Run DetectionG7 Sound Shared Smoke (no Play)` |

**Автопроверка G7 — CLOSED / VERIFIED 2026-08-19:**
- EditMode: `SoundPerceptionTests`, `SharedPerceptionTests`, `PerceptionFusionTests` + eligibility/score/engagement knowledge tests
- Play: `DetectionG7AutoSmoke` order 700, warmup 80s → `Assets/_Docs/Logs/Tests/DetectionG7_LAST.txt` → **RESULT=PASS pass=29 fail=0**
  - Sound/shared TTL math; sound-only eligible, not aim; forgotten without channels; knowledge → Track not Fire
  - Isolation: Selector/Engagement без Sound/Shared types
  - Observe → Aim; hide → Track; inject sound → no aim, Decision=Track, G4 LastSeen не сбрасывается
  - Sound TTL → G4 memory ещё жива, всё ещё Track; reacquire → Aim
  - Sound-only: Track, Identity=Unknown, NotObserved; fusion: один contact, sound+shared
- Menu: `Tools/Tests/Run DetectionG7 Sound Shared Smoke (no Play)` → те же math-checks, что Play `[MATH]`

**Этап G7 закрыт.** Hearing cone, radio, gunshot→AI hearing **не** добавлялись в G7. G8 LOD — следующий (и последний) этап этого документа.

---

### Stage G8 — Performance / 500 m / LOD — **CLOSED / VERIFIED**

**Цель:** масштабировать существующий vision pipeline на много наблюдателей / ~500 м **без смены смысла** G0–G7.

```text
LOD = compute budget, не DetectionProgress
Q = Distance × FOV × Exposure × Movement   (без LOD penalty)
500 m = perception (Unit.prefab VisionRange / DistanceFarMeters)
18 m  = MaxEngageRange                     (не сливать)
skip-scan ≠ ApplyVisionFrame(empty)
T1/T2 никогда не фабрикуют VisionObservation.IsVisible
только T3 применяет vision frame
```

Tiers (4; LOS + hit-zone сжаты в T3 с внутренним coarse-LOS gate):

| Tier | Работа | Интервал |
|------|--------|----------|
| 0 Idle | skip, последний реальный кадр | длинный |
| 1 Cheap | registry + coarse distance | средний |
| 2 RangeFOV | + conservative range/FOV, **без лучей** | обычный |
| 3 Detail | + LOS, затем hit-zone samples | бой / ImmediateScan / очередь с T2 |

Порядок в `TryBuildObservation`: coarse range/FOV (pad) → TTL cache → coarse bounds LOS (closest point combined bounds) → detailed hit-zones/collider → точный FOV на aim. Spatial cull после `GetOpponents` внутри `VisionCandidateProvider`. `GetOpponents` team-mix **не** переписывался.

ImmediateScan промоутит **этого** observer в T3 через тот же pipeline.

#### Файлы

| Файл | Роль |
|------|------|
| `VisionScanTier.cs` / `VisionLodMath.cs` | pure policy |
| `VisionScanStats.cs` | counters (scan/LOS/hit-zone/contacts/buckets) |
| `VisionScanScheduler.cs` | per-frame T3 budget; Immediate bypass |
| `VisionLosCache.cs` | TTL + movement invalidation |
| `UnitVision.cs` | scheduler + cheap→expensive |
| `VisionCandidateProvider.cs` | distance cull after GetOpponents |
| `VisibilityChecker.cs` | BindStats; coarse bounds LOS |
| `DetectionProcessor.cs` | `HasRecentlyLostContact`; contact created/updated counters |
| `DetectionG8AutoSmoke.cs` | Play order **800**, warmup **100s**, harness `9109100011` |
| `DetectionG8StressSmoke.cs` | Play order **850**, warmup **120s**, stubs 10/25/50/100 |
| `VisionLodPolicyTests.cs` | EditMode |
| Menu | `Tools/Tests/Run DetectionG8 Lod Smoke (no Play)` → `DetectionG8_Math_LAST.txt` |

**Автопроверка G8 — CLOSED / VERIFIED 2026-08-19:**
- EditMode: `VisionLodPolicyTests` (tier / cache / FOV-before-LOS / Immediate → T3)
- Play: `DetectionG8AutoSmoke` order 800, warmup 100s → `Assets/_Docs/Logs/Tests/DetectionG8_LAST.txt` → **RESULT=PASS pass=19 fail=0**
  - Isolation: Selector/Engagement без LOD types
  - Observe → Aim; hide → Track; sound-only → Track, не Fire
  - ImmediateScan → T3 и находит visible stub; out-of-FOV decoy: candidate есть, **LOS=0**, нет fake `IsVisible`
  - Skip-scan ≠ empty frame
- Menu: `Tools/Tests/Run DetectionG8 Lod Smoke (no Play)` → **PASS 8/0**
- G1–G7 Play остались зелёными. `IK-GRIP-UNREACHABLE` = harness noise
- Stress: `DetectionG8StressSmoke` order 850, warmup 120s → `DetectionG8_Stress_LAST.txt` → **RESULT=PASS pass=24 fail=0** (stubs 10/25/50/100 × idle/mixed/combat)

**Этап G8 закрыт.** Последний этап этого документа. LOD не штрафует Q. `GetOpponents` mix не переписывался. Selector / Engagement / DetectionQualityMath formula не менялись.

---

### 9.2. Правила исполнения плана

1. После каждого Gx — отдельный demo/smoke (см. проверки выше).  
2. Не смешивать G1+G2+G3.  
3. Не начинать G6–G8 до стабильных G0–G5. Search не является этапом зрения.  
4. Не раздувать tuning-коэффициенты «реалистичного глаза».  
5. Не мутировать world `UnitTeam` из perception.  
6. Не класть knowledge-поля в `VisionObservation`.  
7. **Каждый автотест обязан закончиться логом о завершении** (и PASS, и FAIL). Без финального лога прогон считается незавершённым (можно спутать с зависанием).

**Формат финального лога (Play AutoSmoke и menu math runner):**

```text
[DetectionGxAutoSmoke] wrote <path> RESULT=PASS|FAIL pass=N fail=M
```

или эквивалент menu-runner:

```text
[DetectionGxTestRunner] wrote <path>
RESULT=PASS pass=N fail=0
```

Писать **после** `*_LAST.txt`, в `Debug.Log` (успех) / плюс `Debug.LogError` на каждый FAIL check. Нельзя завершать suite молча.

### 9.3. Документ закрыт

Roadmap зрения = **G0–G8**, все CLOSED / VERIFIED. Search, роли Scout/MG/Commander, Observe/Report/Suppress как поведение — **другие системы**, не следующие G-этапы этого файла.

```text
Vision doc  G0 … G8  ✅
Other AI    Search / roles / hunt   — вне документа
```

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
  → G0 detection/knowledge boundary (DetectionState + PerceivedContact types)  ✅ CLOSED
  → G1 Detection Quality / Progress (parallel DetectionProcessor)              ✅ CLOSED PASS 20/0
  → G1.1 hysteresis + DetectionEvaluation slim contact                        ✅ CLOSED
  → G2 Perceived Contact Lifecycle (ObservationState / LastSeen)              ✅ CLOSED PASS 20/0
  → G3 Identity / Threat / Relationship          ✅ CLOSED PASS 30/0
  → G4 Memory decay / LastKnown confidence       ✅ CLOSED PASS 32/0
  → G5 Selector ← PerceivedContacts              ✅ CLOSED PASS 21/0
  → G6 Engagement Decision                       ✅ CLOSED PASS 26/0
  → G7 Sound / Shared → Perception               ✅ CLOSED PASS 29/0
  → G8 Perf / 500 m tiers                        ✅ CLOSED PASS 19/0 + Stress 24/0
```

## 12. Быстрый cheat-sheet для программиста (зрение заморожено, G0–G8 + A/B/C)

AI / Search / tactics: читать `AIPerceptionFrame` (`AI_Perception_Contract.md`), не Q / DetectionProgress / UnitTeam. Vision freeze: `Vision_AI_Handoff.md`. Проверка: `Tools/Tests/Verify Vision Freeze`. Play: `Tools/Tests/Run AI Perception Handoff (Play)`.

Combat-пути: Selected из contacts; intent = `EngagementDecision`; fire только Decision=Fire + LOS aim.  
G8 не меняет этот смысл — только когда Vision тратит лучи.

```text
Нужна картина мира для AI?        → AIPerceptionFrameBuilder.Build(registry) / AIPerceptionSensor.CurrentFrame
Нужна detection quality / contact? → DetectionProcessor.TryGetContact / Contacts (на Unit.prefab)
Нужна identity / threat?           → contact.Identity / IdentityConfidence / Relationship / Threat
Нужна память LastKnown?            → contact.LastKnownPosition / LastSeenConfidence (не aim)
Нужен звук / доклад?               → contact.SoundConfidence / SharedConfidence (не Vision, не aim)
Нужна боевая цель (knowledge)?     → TargetSelector.SelectedTarget
Нужно ЧТО ДЕЛАТЬ?                  → EngagementDecisionController.CurrentDecision
Нужно стрелять?                    → Decision==Fire && GetEngageableSelectedTarget()
Нужна точка прицеливания?          → GetEngageableAimPointWorld()   // не LastKnown, не SoundPosition
Смена цели?                        → SelectedTargetChanged
Приоритет RTS?                     → ForcedPriorityTarget = x (нужен eligible contact)
Сбросить цель и подождать?         → ClearSelectionAndNotifyIfHadTarget(); vision.DeferNextScan()
LoF blocked?                       → SuppressCurrentTargetForLineOfFire(t); vision.RequestImmediateScan()
Форс перескан этого observer?      → UnitVision.RequestImmediateScan()  // T3, не «весь мир»
Кто сейчас в кадре зрения?         → UnitPerception.Observations  // не список выбора
Текущий звук / доклад?             → UnitPerception.SoundEvents / SharedEvents  // не список выбора
LOD / сколько стоит скан?          → UnitVision.ScanStats / CurrentScanTier  // не Q, не confidence
Perception range?                  → UnitVision.VisionRange (prefab 500 м) ≠ MaxEngageRange (18 м)
Soft-lost LastSeen?                → contact.LastSeenPosition / LastSeenTime
LastSeenConfidence decay?          → contact.LastSeenConfidence / IsMemoryStale
Memory-only / sound-only selected? → Decision=Track; не Fire
```

---

Конец документа.
