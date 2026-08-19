# AI Perception Contract

**Статус: FROZEN** (Play 2026-08-19 23:10:39)  
**Vision:** FROZEN (`Vision_AI_Handoff.md`). Числа A/B/C не менять.  
**Приёмка:** `AIPerceptionHandoff_LAST.txt` **PASS 41/0**. `IK-GRIP-UNREACHABLE` — шум harness.

```text
Vision = perception
AI     = decision

Vision ≠ orders
Vision ≠ search
Vision ≠ tactics
TargetSelector ≠ AI
Selected ≠ Engageable ≠ Fire
```

```text
UnitVision
  → PerceivedContact registry     (frozen Vision knowledge)
  → AIPerceptionFrameBuilder
  → AIPerceptionFrame             (immutable snapshot, one tick)
  → AI
```

Новое знание для AI: сначала вопрос «должен ли это дать Perception?», а не новое поле на `PerceivedContact`.

---

## 1. Semantics (интерпретация, не новые поля Vision)

Источник: `PerceivedContact`. Mapper: `AIPerceptionSemantics` / `AIContactKnowledge`.

| Flag | Правило |
|------|---------|
| VisibleNow | `Detected` **and** `Observed` |
| RecentlyLost | `ObservationState == RecentlyLost` |
| Lost | `ObservationState == Lost` |
| HasUsefulMemory | `LastSeenConfidence > 0.25` |
| MemoryStale | `0 < LastSeenConfidence ≤ 0.25` |
| IdentityUnknown / IdentityKnown | `Identity == Unknown` / не Unknown |
| Friendly / Neutral / Hostile | из **Relationship**, не `UnitTeam` |
| ThreatNone / Low / Medium / High | 1:1 из `Threat` |

Forgotten (`LastSeenConfidence == 0`) — ни useful, ни stale.  
Detected + Unknown — VisibleNow + IdentityUnknown, **не** Hostile.  
Hostile + far — ThreatLow валидно.

`selectedByCurrentKnowledge` **нет** в AI-0: это было бы похоже на TargetSelector.

---

## 2. Что AI может и не может читать

**Можно (на снимке):** Target, DetectionState, ObservationState, Identity, IdentityConfidence, Relationship, Threat, LastKnownPosition, LastSeenPosition, LastSeenTime, LastSeenConfidence, флаги выше.

**Нельзя (поля отсутствуют на снимке):**

- `UnitTeam` цели
- Q / FOV factors / Exposure internals
- `DetectionProgress` (не тактический вход)
- `VisionObservation` / LOS implementation
- Vision scheduler / LOD
- `TargetSelector.SelectedTarget`

Sound/Shared (G7) в AI-0 не входят.

---

## 3. Snapshot

`AIPerceptionFrame` (observer-local, immutable):

- `AllContacts`
- `VisibleContacts`
- `RememberedContacts` — useful memory, не VisibleNow
- `StaleContacts`
- `HostileContacts` — Relationship=Hostile
- `UnknownContacts` — Identity=Unknown
- `StrongestThreat` — max Threat на кадре, не приказ Fire

Сборка: `AIPerceptionFrameBuilder.Build(IPerceivedContactRegistry)`.  
Опционально `AIPerceptionSensor` (execution order 15) кэширует кадр. **Не** на `Unit.prefab` в AI-0.

Два наблюдателя → два кадра. Один объект мира может быть Hostile у A и Unknown у B.

---

## 4. TargetSelector не мозг AI

```text
PerceivedContacts → TargetSelector → EngagementDecision
```

это боевой пайплайн, не high-level AI. `EnemyPatrolAI` по-прежнему читает SelectedTarget. Новый AI читает `AIPerceptionFrame`.

---

## 5. Проверки

EditMode: `AIPerceptionContractTests`.  
Play: `Tools/Tests/Run AI Perception Handoff (Play)` (`m_RunOnStart = false`).

H1 visible hostile · H2 recently lost · H3 lost useful memory · H4 stale · H5 unknown · H6 friendly · H7 neutral · H8 threat bands · H9 reacquire · H10 two observers.

### AI-0 CLOSED / VERIFIED (Play 2026-08-19 23:10:39)

`AIPerceptionHandoff_LAST.txt` — **RESULT=PASS pass=41 fail=0**.

| H | Результат |
|---|-----------|
| H1 | VisibleNow, Hostile, Threat High |
| H2 | not visible, RecentlyLost, useful memory (conf≈0.998) |
| H3 | Lost, useful memory (conf≈0.713), Identity held |
| H4 | Lost, MemoryStale (conf≈0.191), Identity held |
| H5 | VisibleNow + IdentityUnknown, not Hostile |
| H6 | Friendly, Threat None |
| H7 | Neutral, Threat None |
| H8 | 10 m High / 50 m Medium / 100 m Low |
| H9 | reacquire → VisibleNow, Hostile preserved |
| H10 | observer A Hostile ≠ observer B Unknown; UnitTeam Neutral |

**AI Perception Contract FROZEN.** Смысл флагов не менять во время разработки AI. Новое знание — только если его должен дать Perception, не новое поле Vision.
