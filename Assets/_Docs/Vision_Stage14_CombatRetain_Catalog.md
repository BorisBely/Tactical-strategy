# Vision Stage 14 — Combat Retain Range

**Статус: CLOSED / VERIFIED** (2026-08-22 23:18:15). `Tools/Tests/Run Combat Retain Contract (Play)` → `Assets/_Docs/Logs/Tests/CombatRetainContract_LAST.txt`. **PASS 31/0**.  
**Это A7** карты `Пехота_дорожная_карта.md`. Не Attention (B). Не отдача (A10).  
**Не трогали:** Q, `VisionRange`, `ScopeVisionRange`, E, AccuracyCurve, AimTime×, Fire Discipline, RPG 115/130×12, MK19 240×25, Stage 12 permit, Stage 13 VisionSource, память 5/30, LastKnown ≠ AimPoint.

Этапы 8–13 остаются **CLOSED / VERIFIED**.

Play: `Tools/Tests/Run Combat Retain Contract (Play)` → `Assets/_Docs/Logs/Tests/CombatRetainContract_LAST.txt`.  
EditMode: `CombatRetainContractTests`.  
NavMesh warning harness — шум, не FAIL.

Следующее по зрению — **C1 Sound CLOSED / VERIFIED PASS 47/0**. C2 **CLOSED / VERIFIED PASS 72/0**. Stage 15 Attention **CLOSED / VERIFIED PASS 44/0**. A10 остаётся затвором перед AI.

---

## Закон

```text
retain = текущий VisionSource.ResolvedMaxRange + актуальный LOS/AimPoint
не 18 м, не отдельный 100/150/300 в combat-коде
SELECT не режется retain-дальностью
LastKnown ≠ AimPoint
память RecentlyLost/Lost не продлевается retain
```

```text
Contact → Selected → temporary GATE (reload/misfire) → retain → recheck AimPoint → Engageable → G6
```

---

## Инвентаризация

| Контур | Было | Stage 14 |
| --- | --- | --- |
| `TargetSelector.m_MaxEngageRange` | **18 м**, только reload/misfire revalidate | поле снято; range = `UnitVision.ResolvedMaxRange` |
| Основной SELECT | не резался 18 м | без изменений |
| VisibilityChecker retain LOS | конфиг на 18 м | тот же checker, длина = resolved range |
| Память / LastKnown | не AimPoint | не трогали |

InfantryEye / Passenger / Turret — тот же `ResolvedMaxRange`, что Stage 13.

---

## Лог

Нового тега нет. `SELECT` при смене цели: `retainRange=<resolvedRange>`. Не каждый кадр.

---

## Acceptance

- M4 reload: 20 / 80 / 149 м — retain; 151 без оптики — нет.
- Scope9: 250 / 300 — retain; 301 — нет.
- Passenger 80 / Turret inside source — retain.
- LOS lost / LastKnown — нет Fire.
- Нет второго scan / raycast-контура.

Play stamp: `CombatRetainContract_LAST.txt` **RESULT=PASS PASS=31 FAIL=0** (2026-08-22 23:18:15). NavMesh warning при спавне harness — тот же шум, что у этапов 8–13, не FAIL. Stage 15 Attention **CLOSED / VERIFIED PASS 44/0**. Stage 16 Sound **CLOSED / VERIFIED PASS 47/0**. C2 **CLOSED / VERIFIED PASS 72/0**. A10 не зрение.

---

## Файлы

- `CombatRetainMath`
- `TargetSelector`
- `CombatRetainContractTests`
- `CombatRetainContractRuntimeSmoke`
- `CombatRetainContractTestRunner`
