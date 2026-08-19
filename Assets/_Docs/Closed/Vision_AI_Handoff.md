# Vision Freeze / AI Handoff

**Статус: FROZEN** (2026-08-19)  
**Калибровка:** Block A / B / C **CLOSED / VERIFIED**.  
**Контракт AI:** **FROZEN** — `AIPerceptionFrame` (`AI_Perception_Contract.md`). Play **PASS 41/0** (23:10). Vision knowledge на `PerceivedContact`; AI не читает Q / DetectionProgress / UnitTeam.  
**Архитектура:** `Vision_Current_Architecture_And_Future_Philosophy.md` (G0–G8 CLOSED).  
**Калибровочные числа:** `Vision_Gameplay_Calibration.md`.

```text
Vision = perception
Vision ≠ orders
Vision ≠ search
Vision ≠ tactics
```

```text
World State ≠ Perception State ≠ Decision State
physical evidence → confidence → knowledge → decision
```

Во время разработки AI **не менять** числа, типы и смысл полей ниже. Если «AI плохо ищет / плохо стреляет» — это не баг формулы Q.

---

## 1. Замороженный baseline

### Detection

```text
FOV half        = 60°
FOV edge        = 0.15
Acquire         = 0.25
AcquireTime     = 0.35 s
```

Q = Distance × FOV × Exposure × Movement. Формула не открывается.

### Memory

```text
RecentlyLost    = 5 s
Horizon         = 30 s
Shape           = 1.5
Stale           = 0.25
```

Decay только `LastSeenConfidence`. Identity при потере LOS **держится**. LastKnown **заморожен** (нет velocity × time). Forgotten (`conf=0`) ≠ удалён из registry.

### Identity

```text
IdentifyTime    = 4 s      (conf=1 при Q=1)
Commit          = 0.50     (Hostile ≈ 2.0 с при Q=1)
```

`PerceivedIdentity` — affiliation-класс: Unknown / Friendly / Neutral / Hostile. Не Soldier / Military. Evidence = `ObservableAffiliation` / `IdentityAppearance`, **never** `UnitTeam`.

### Threat

```text
High            <= 25 m
Medium          <= 80 m
Low             > 80 m
```

Считается только при Relationship=Hostile. Friendly / Neutral / Unknown → Threat=None. Hostile + far = Low — **валидно**. Threat **не** приказ стрелять.

### Соседние константы (тоже не крутить ради AI)

| Параметр | Значение | Зачем AI это знать |
|----------|--------:|--------------------|
| VisionRange | **500 м** (`Unit.prefab`) | perception range |
| MaxEngageRange | **18 м** (`TargetSelector`) | **не** perception |
| LoseThreshold | **0.20** | hysteresis под Acquire 0.25 |
| LossTime | **2.5 с** | падение DetectionProgress |
| DistanceNear / Far / FarFactor | **20 / 500 / 0.08** | Q distance |
| Movement idle / walk / run / cap | **1.00 / 1.15 / 1.35 / 1.50** | только бонус цели |

Проверка: `Tools/Tests/Verify Vision Freeze` → `Assets/_Docs/Logs/Tests/VisionFreeze_LAST.txt`.

---

## 2. Что AI получает

Один наблюдатель → свой registry → свой `AIPerceptionFrame`. Два солдата про один объект мира могут знать разное.

```text
UnitVision (лучи, 500 м, LOD)
  → UnitPerception.ApplyVisionFrame          // physical evidence only
  → DetectionProcessor                       // knowledge
  → AIPerceptionFrameBuilder                 // AI-0
  → AIPerceptionFrame                        // ← вход AI
```

Читать: `AIPerceptionFrame` / `AIContactKnowledge`.  
Боевой пайплайн (не AI) может читать `DetectionProcessor.Contacts`.  
Не читать `UnitPerception.Observations` как список целей.  
Не читать `UnitTeam` цели как «кто свой».  
Не читать `DetectionProgress` / Q для тактики.

Семантика флагов: `Assets/_Docs/Closed/AI_Perception_Contract.md`.

### Поля `PerceivedContact` (контракт)

| Поле | Смысл для AI | Не путать с |
|------|----------------|-------------|
| `Target` | объект мира | |
| `State` | Undetected / Detecting / Detected | Selected / Fire |
| `DetectionProgress` | накопление «заметил» | IdentityConfidence, LastSeenConfidence |
| `ObservationState` | Observed / RecentlyLost / Lost | DetectionState |
| `LastSeenPosition` / `LastSeenTime` | последний реальный визуальный факт | live transform |
| `LastKnownPosition` | где **считает**, что цель | aim point, nav destination «магией» |
| `LastSeenConfidence` | доверие к LastKnown (0 = forgotten) | DetectionProgress |
| `Identity` | committed affiliation | `UnitTeam`, роль, класс оружия |
| `IdentityConfidence` | уверенность «кто это» | DetectionProgress |
| `Relationship` | вывод из committed Identity | Identity (это разные поля) |
| `Threat` | High/Medium/Low/None по дистанции | приказ Fire |
| `LastObservation` | последний physical кадр | knowledge |
| Sound / Shared | отдельные каналы G7 | Vision, aim, Search |

Хелперы: `HasVisualEvidence`, `HasMemory`, `IsMemoryForgotten`, `IsMemoryStale()`, `HasKnowledge`.

### Типичная кривая (Q=1, Hostile cue)

| t | Detection | Identity | Что это значит |
|--:|-----------|----------|----------------|
| ~0.35 с | Detected | Unknown | видит человека, не знает кто |
| 0–5 с после LOS loss | RecentlyLost | Identity держится | «только что тут» |
| **~2.0 с** взгляда | Detected | **Hostile** (commit 0.50) | готов считать врагом |
| **~4.0 с** | Detected | Hostile, conf=1 | полная уверенность |
| 5–30 с без LOS | Lost | Identity держится | память места стареет |
| ≥30 с | Lost, conf=0 | Identity всё ещё на контакте | forgotten ≠ deleted |

Detected + Identity=Unknown — **норма**. Не лечить это IdentifyTime.

---

## 3. Инварианты (AI обязан уважать)

```text
Detected ≠ Selected ≠ Fire
Detected + Identity=Unknown          валидно
Unknown ≠ Friendly
Hostile + Threat=Low                 валидно
Detected + RecentlyLost / Lost       валидно
Lost + LastSeenConfidence=0          валидно (forgotten)
DetectionProgress ≠ LastSeenConfidence ≠ IdentityConfidence
LastKnown ≠ aim ≠ live transform
VisionRange (500) ≠ MaxEngageRange (18)
Cue conflict ≠ instant team teleport
```

`VisionObservation` — только физика. Knowledge не писать туда.

---

## 4. Чего AI не делает со зрением

Запрещено во время разработки AI:

- крутить FOV / Acquire / Memory / IdentifyTime / Threat, «чтобы поиск заработал»;
- добавлять на `PerceivedContact` поля приказов, Search, роли, «хочу стрелять»;
- встраивать Search / Hunt в `UnitVision`, `DetectionProcessor`, `TargetSelector`, `EngagementDecisionController`;
- читать `UnitTeam` как знание солдата;
- целиться или стрелять в `LastKnownPosition` / `SoundPosition` / `SharedPosition`;
- трактовать Threat High как Fire;
- трактовать Identity Hostile как «уже выбран и стреляет»;
- `if (reloading) extend memory` и velocity-экстраполяция LastKnown;
- LOD → штраф к Q.

Уже существующие consumers contacts (боевой пайплайн, не high-level AI):

```text
TargetSelector → EngagementDecisionController → Combat
```

Их тоже **не чинят ретюном зрения**. `Selected ≠ Fire`. Memory-only / sound-only → Track, не Fire. Block D Select/Act **не открыт**.

---

## 5. Что AI может строить снаружи

Новые системы (свои компоненты, свой state):

| Система | Законный вход от зрения | Своё решение |
|---------|-------------------------|--------------|
| Search / Hunt | RecentlyLost, LastKnown, LastSeenConfidence | идти смотреть / не идти |
| Orders | contacts как ситуация | выполнить приказ или нет |
| Tactics / роли | Identity, Threat, freshness | как действовать группой |
| Report / Share | уже есть канал Shared (G7) | что докладывать |

Search **использует** LastKnown как гипотезу. Search **не** становится слоем зрения.

---

## 6. Когда зрение можно снова менять

Только явный новый блок калибровки (как A/B/C), с тестами и снятием freeze.  
Не «заодно», пока пишется AI.

Проверка freeze не сломана: `Tools/Tests/Verify Vision Freeze`.  
AI-0 Play: `Tools/Tests/Run AI Perception Handoff (Play)`.
