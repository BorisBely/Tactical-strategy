# Vision Stage 17 — Ally Report / Shared Perception

**Статус: CLOSED / VERIFIED** (2026-08-23 11:22:31). `Tools/Tests/Run Ally Report Contract (Play)` → `Assets/_Docs/Logs/Tests/AllyReportContract_LAST.txt`. **PASS 72/0**.  
**Это этап C2** карты `Пехота_дорожная_карта.md`. Не второй Vision. Не Q. Не Stage 18. Не Search. Не A10. Не тактический #7.

**Не трогали:** `Q = D × F × E × M`, Acquire 0.25 / Lose 0.20 / exponent 3.8 / AcquireTime 0.35, `VisionRange`, `ScopeVisionRange`, E, AccuracyCurve, AimTime×, Fire Discipline, полы позы 0.35 / 0.68 / 1.0, RPG 115/12, MK19 240×25, Attention BAKED, Memory 5/30, Identity commit 0.50 / IdentifyTime 4 s, горизонт звука **3 с**, дальность доклада **80 м**, горизонт доклада **8 с**.

Этапы 8–16 остаются **CLOSED / VERIFIED**.

Play: `Tools/Tests/Run Ally Report Contract (Play)` → `AllyReportContract_LAST.txt`.  
EditMode: `AllyReportContractTests`.  
NavMesh warning harness — шум, не FAIL.  
Hub_DeliveryCount в Play считать **≥** ожидаемого: у harness могут быть лишние слушатели.

---

## Закон

```text
VISUAL  → Observed / AimPoint
SOUND   → SoundContact / SoundPosition
SHARED  → SharedEvidence / SharedPosition / SharedIdentity
```

Не:

```text
копировать Contact A → Contact B
```

Да:

```text
A → ReportEvent → B → SharedEvidence
```

Доклад **не** создаёт `Observed`, `AimPoint`, не коммитит визуальную Identity, не даёт Fire.

Валидно: `SharedEvidence > 0`, `Observed = false`, `AimPoint = none`, `Identity = Unknown` (даже если союзник сообщил Hostile).

LastKnown докладом не затирается. Каналы разделены:

```text
VisualLastKnown / LastSeen
SoundPosition
SharedPosition
```

Believed position: живая визуальная память → LastKnown/LastSeen; иначе Sound; иначе Shared.

---

## Событие

```text
WorldAllyReportEvent
  reporter
  subject
  position
  reportedIdentity
  confidence
  range 80 m
  time
```

Не передаём: Observed, AimPoint, DetectionProgress, live Transform drive, Fire decision.

---

## Публикация

Только **событие**, не покадровая синхронизация.

```text
новая Observed цель
значительно сдвинулась позиция (≥ 8 м)
изменилась identity
интервал ≥ 1 с
```

`every frame → NO`.

Получатель: тот же `UnitTeam`, не Neutral, жив, в **80 м** от репортёра, не self. Командиры / радио / отряды **не** введены.

Без UnitTeam авто-publish молчит — существующие тесты без команды не шумят.

---

## Дальность / горизонт (BAKED, первый проход)

| | |
| --- | ---: |
| Range | **80 m** hard cutoff |
| Horizon | **8 s** |
| Occlusion / raycast | нет |
| FindObjects / N×N scan | нет |

`WorldAllyReportHub.Publish` — один проход, distance², регистрация `DetectionProcessor` OnEnable / OnDisable.

Несколько союзников на один `Transform` → **один** локальный контакт (Visual + Sound + Shared). Последний актуальный report побеждает SharedPosition / SharedIdentity. Multi-source fusion нет.

Конфликт X затем Y: SharedPosition = Y, визуальный LastKnown не телепортируется.

---

## Identity

```text
VisualIdentity   отдельно
SharedIdentity   отдельно
```

Hostile в докладе ≠ `IdentityConfidence = 1`. Unknown остаётся Unknown у приёмника, пока он сам не увидит.

---

## Бой / Search

```text
Report → SELECT может существовать
Report → G6 = Track
Report → AimPoint = none → Fire = no
```

**Search на Stage 17 не трогали.**

`AIPerceptionFrame` доклад **не** копирует — этап E.

---

## Лог

Канал `SHARED`: received / updated / expired. Не каждый тик.

```text
SHARED received reporter=E02 pos=(...) conf=0.72 identity=Unknown tgt=E05
```

---

## Приёмка

- A: A Observed, B SharedEvidence, B not Observed, no AimPoint, Fire false.
- B: в 80 м — высокий SharedConfidence.
- C: дальше 80 м — нет улики.
- D: горизонт 8 с; к 8–9 с useful shared гаснет. Снимок сразу после Advance.
- E: визуал A, затем доклад B → LastKnown = A, SharedPosition = B.
- F: Hostile в докладе не коммитит визуальную Identity.
- Два союзника → один контакт.
- Конфликт → последний SharedPosition, LastKnown не прыгает.
- Self / чужая команда — skip.
- Throttle: внутри 1 с повтор не уходит.
- SELECT может быть; Track; Fire нет.
- Архитектура: один UnitVision; shared не в Q; event-driven.

CLOSED / VERIFIED Play **PASS 72/0** (2026-08-23 11:22:31). Stage 18 **CLOSED / VERIFIED PASS 49/0**. A10 остаётся затвором перед AI.

Play stamp: `AllyReportContract_LAST.txt` **RESULT=PASS PASS=72 FAIL=0** (2026-08-23 11:22:31). NavMesh warning при спавне harness — тот же шум, что у этапов 8–16, не FAIL. Hub_DeliveryCount=2 / Live_Delivery=12: harness-слушатели в радиусе, среди тестовых 1/1. Search не трогали. `AIPerceptionFrame` доклад не копирует.
