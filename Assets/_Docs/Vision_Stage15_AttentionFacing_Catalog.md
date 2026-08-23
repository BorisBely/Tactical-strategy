# Vision Stage 15 — Attention / Facing

**Статус: CLOSED / VERIFIED** (2026-08-23 09:42:24). `Tools/Tests/Run Attention Facing Contract (Play)` → `Assets/_Docs/Logs/Tests/AttentionFacingContract_LAST.txt`. **PASS 44/0**.  
**Это этап B** карты `Пехота_дорожная_карта.md`. Не Q. Не второй FOV. Не A10. Не тактический #7.  
**Не трогали:** формулу `Q = D × F × E × M`, Acquire 0.25 / Lose 0.20 / exponent 3.8 / AcquireTime 0.35, `VisionRange`, `ScopeVisionRange`, E, AccuracyCurve, AimTime×, Fire Discipline, полы позы 0.35 / 0.68 / 1.0, RPG 115/130×12, MK19 240×25, Stage 12 permit, Stage 13 VisionSource, память 5/30, LastKnown ≠ AimPoint.

Этапы 8–14 остаются **CLOSED / VERIFIED**.

Play: `Tools/Tests/Run Attention Facing Contract (Play)` → `AttentionFacingContract_LAST.txt`.  
EditMode: `AttentionFacingContractTests`.  
NavMesh warning harness — шум, не FAIL.

Следующее по зрению — C2 **CLOSED / VERIFIED PASS 72/0** (`Vision_Stage17_AllyReport_Catalog.md`). Stage 18 **CLOSED / VERIFIED PASS 49/0**. A10 остаётся затвором перед AI. Кривая Attention **BAKED**, не freeze Q.

---

## Закон

```text
Q              = D × F × E × M     (frozen; Attention is NOT a Q factor)
Attention      = curve(horizontal angle to observerForward)
detectionRate  = AcquisitionFactor(Q) × AttentionMul × (1/AcquireTime)
```

Acquire **0.25** — порог Q. Attention применяется **только** на grow (`Q > 0.25`). `Q ≤ 0.25` + любой mul → hold/decay. Attention не «видит» то, что физически слишком тускло.

FOV уже штрафует периферию внутри Q. Attention **не** второй штраф FOV: на 45–60° множитель = **1.0** (текущая скорость).

---

## Кривая (BAKED, не freeze)

| Угол | AttentionMul | Смысл |
| ---: | ---: | --- |
| 0° | **2.5** (cap) | смотрит прямо |
| 10° | 2.15 | почти центр |
| 20° | 1.55 | высокий |
| 30° | 1.12 | чуть выше 1 |
| ≥ 45° | **1.0** | обычный FOV, без бонуса |

Пол **1.0**. Потолок `AttentionMath.MultiplierMax = 2.5`. Код: `AttentionMath`. Одна кривая для InfantryEye / Aiming optic / Passenger / Turret.

Угол: `VisionObservation.FovOffsetDegrees` (горизонтальный). На skip-scan: текущий `UnitVision.GetVisionForwardXZForGameplay()` против последней Observation Position/AimPoint, **не** LastKnown, **не** Selected, **не** ствол как отдельный закон.

Нет HoldSector / WatchSector / Focused / `WatchingWindow`. Поворот юнита игроком достаточен.

---

## Скан

Потолок **8 Detail / кадр** остаётся. ImmediateScan по-прежнему обходит кап.

Не-immediate Detail: `Update` только `Request`, `LateUpdate` `Flush` по score + starve. Пропуск ≠ пустой кадр зрения (`skip=DetailSlot notALoss=1`).

```text
score = AttentionPriority + CurrentTargetBonus + RecentlyLostBonus + ForcedScanBonus + StarveAge
```

RecentlyLost на оси взгляда поднимает **скан**, не память 5/30.

---

## Лог

На смене контакта, тег `VISION`:

```text
angle=4.2 att=High attMul=2.3
```

Не каждый кадр.

---

## Приёмка

- Q / Acquire / Lose / exponent не сдвинулись.
- `Q=0.24`, mul=3 → нет роста. `Q=0.26`, mul=2.5 быстрее, чем mul=1.
- 0° быстрее 30–45°. 60° ≈ baseline. 0° **не** Detected за 0.10 с при AcquireTime 0.35.
- 50 заявок / 8 слотов: никто не голодает 8 кадров подряд.
- Нет второго `UnitVision`, нет типа `WatchingWindow`.

CLOSED / VERIFIED Play **PASS 44/0** (2026-08-23 09:42:24). Не ретюнить Q, чтобы «peek красивее». Кривая BAKED, не freeze.

Play stamp: `AttentionFacingContract_LAST.txt` **RESULT=PASS PASS=44 FAIL=0** (2026-08-23 09:42:24). NavMesh warning при спавне harness — тот же шум, что у этапов 8–14, не FAIL. **C1 Sound CLOSED / VERIFIED PASS 47/0**. C2 **CLOSED / VERIFIED PASS 72/0**. A10 остаётся затвором перед AI.
