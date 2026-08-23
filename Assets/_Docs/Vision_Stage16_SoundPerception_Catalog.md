# Vision Stage 16 — Sound Perception

**Статус: CLOSED / VERIFIED** (2026-08-23 10:34:44). `Tools/Tests/Run Sound Perception Contract (Play)` → `Assets/_Docs/Logs/Tests/SoundPerceptionContract_LAST.txt`. **PASS 47/0**.  
**Это этап C1** карты `Пехота_дорожная_карта.md`. Не второй Vision. Не Q. C2 **CLOSED / VERIFIED PASS 72/0** (`Vision_Stage17_AllyReport_Catalog.md`). Не A10. Не тактический #7.

**Не трогали:** `Q = D × F × E × M`, Acquire 0.25 / Lose 0.20 / exponent 3.8 / AcquireTime 0.35, `VisionRange`, `ScopeVisionRange`, E, AccuracyCurve, AimTime×, Fire Discipline, полы позы 0.35 / 0.68 / 1.0, RPG 115/130×12, MK19 240×25, Stage 12 permit, Stage 13 VisionSource, память 5/30, LastKnown ≠ AimPoint, Attention curve BAKED, горизонт звука **3 с**.

Этапы 8–15 остаются **CLOSED / VERIFIED**.

Play: `Tools/Tests/Run Sound Perception Contract (Play)` → `SoundPerceptionContract_LAST.txt`.  
EditMode: `SoundPerceptionContractTests`.  
NavMesh warning harness — шум, не FAIL.

C2 (доклад союзника) **CLOSED / VERIFIED PASS 72/0** — `Vision_Stage17_AllyReport_Catalog.md`.

---

## Закон

```text
VISUAL  → Observed / AimPoint
SOUND   → SoundContact / SoundPosition
SHARED  → не этот этап
```

Один `PerceivedContact`. Валидно: `SoundEvidence > 0`, `Observed = false`, `AimPoint = none`, `Identity = Unknown`.

Sound не пишет Q, Observed, AimPoint, Identity. Fire / Engageable по звуку нет.

При живой визуальной памяти (`LastSeenConfidence > 0`): LastKnown / LastSeen = окно A, SoundPosition = окно B.  
Sound-only (никогда не видели): LastKnown звуком не заполняется. Выбор цели без AimPoint идёт через `TargetSelectionMath.ResolveBelievedPosition` → SoundPosition.

Свой юнит своё событие не слышит. `Source == null` контакт не создаёт.

---

## Дальности (BAKED, не VisionRange)

| Тип | Range | Strength |
| --- | ---: | ---: |
| Gunshot | **300** | 1 |
| Explosion | **500** | 1 |
| Footstep | **25** | 0.35 |
| Impact | **40** | 0.5 |

`confidence = Clamp01(strength * (1 - d/range))`. confidence ≤ 0 — нет улики. Дальше 150 м gunshot может быть полезен; `VisibleNow` остаётся false.

Occlusion / acoustic raycast **нет**. `CombatAudioManager` — наушники, не шина знания: Publish с геймплейного факта, даже если клип не сыграл.

---

## Шины

`WorldSoundHub.Publish` — один проход, distance², `DetectionProcessor` регистрируется OnEnable / OnDisable. Нет FindObjects, нет per-frame scan.

Публикация: выстрел `UnitWeaponFireAudio`, взрыв гранаты/РПГ/МК19, шаг `UnitFootsteps`, удар `UnitWeaponImpactVfx`.

Лог канал `SOUND`: только received / updated / expired. VISION не трогать.

AI snapshot (`AIPerceptionFrame`) звук **не** копирует — этап E.

---

## Приёмка

- A: слышал, не видел; нет AimPoint; Fire false.
- B: близкий выстрел — высокий conf.
- C: дальше range — нет улики.
- D: горизонт 3 с, к 3–4 с useful sound гаснет.
- E: визуал A, потом звук B → LastKnown = A, SoundPosition = B.
- F: никогда не видели → Identity Unknown.
- 1 Publish, слушатели в радиусе получают улику; в hub нет Raycast.
- Freeze Q / Attention / Acquire как Stage 15.

CLOSED / VERIFIED Play **PASS 47/0** (2026-08-23 10:34:44). C2 **CLOSED / VERIFIED PASS 72/0**. A10 остаётся затвором перед AI.

Play stamp: `SoundPerceptionContract_LAST.txt` **RESULT=PASS PASS=47 FAIL=0** (2026-08-23 10:34:44). NavMesh warning при спавне harness — тот же шум, что у этапов 8–15, не FAIL. Hub_DeliveryCount=5: harness-слушатели в радиусе, среди десяти тестовых 3/3. C2 доклад **CLOSED / VERIFIED PASS 72/0**.
