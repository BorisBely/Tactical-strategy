# Vision Stage 18 — Final Perception Contract

**Статус: CLOSED / VERIFIED** (2026-08-23 12:36:53). `Tools/Tests/Run Final Perception Contract (Play)` → `Assets/_Docs/Logs/Tests/FinalPerceptionContract_LAST.txt`. **PASS 49/0**.  
**Это интеграция D+E+F** карты `Пехота_дорожная_карта.md`. Не Search. Не A10. Не тактический #7. Не новый канал восприятия.

**Не трогали:** `Q = D × F × E × M`, Acquire 0.25 / Lose 0.20 / exponent 3.8 / AcquireTime 0.35, `VisionRange`, `ScopeVisionRange`, E, AccuracyCurve, AimTime×, Fire Discipline, полы позы 0.35 / 0.68 / 1.0, RPG 115/12, MK19 240×25, Attention BAKED, Memory 5/30, Identity commit 0.50 / IdentifyTime 4 s, горизонт звука **3 с**, доклад **80 м / 8 с**.

Этапы 8–17 остаются **CLOSED / VERIFIED**.

Play: `Tools/Tests/Run Final Perception Contract (Play)` → `FinalPerceptionContract_LAST.txt`.  
EditMode: `FinalPerceptionContractTests`.  
NavMesh warning harness — шум, не FAIL.  
Hub_DeliveryCount в Play считать **≥** ожидаемого.

CLOSED / VERIFIED Play **PASS 49/0** (2026-08-23 12:36:53). A10 остаётся затвором перед AI / #7.

---

## Закон

```text
WORLD
 ├─ Vision  → Observed / AimPoint / LastKnown
 ├─ Sound   → SoundEvidence / SoundPosition
 └─ Shared  → SharedEvidence / SharedPosition / SharedIdentity
        ↓
   one local Contact
        ↓
 Perception Snapshot
        ↓
 Combat / Tactical AI
```

Evidence раздельно. Derived не подменяет канал.

```text
VisibleNow = Detected AND Observed
AimPoint   = только визуальное наблюдение
Identity   = только визуальный commit
```

Sound / Shared / Memory никогда не дают VisibleNow и AimPoint.  
SharedIdentity не превращает Unknown в визуально подтверждённого Hostile.

После потери зрения:

```text
VisualLastKnown = A
SoundPosition   = B
SharedPosition  = C
```

Три разные сведения. LastKnown не телепортируется.

---

## Снимок AI

Компактный `AIContactKnowledge`: VisibleNow / RecentlyLost / memory / Hostile|Friendly|Neutral|Unknown / Threat / SoundPresent+pos / SharedPresent+pos+identity.

Без Q, DetectionProgress, FOV/Exposure, UnitTeam, combat Selected/AimPoint.

---

## Не добавлять

Search, Investigate, отряд, командир, строя, укрытия, suppression, morale, #7, occlusion sound, binocular/thermal, новый target scoring.

---

## Приёмка

- Один Transform → один Contact, три канала различимы.
- Conflict: LastKnown=A, Sound=B, Shared=C, AimPoint нет.
- Shared Hostile ≠ visual Identity commit.
- VisibleNow только Detected+Observed.
- Sound/Shared/Memory → нет AimPoint, нет Fire, G6 Track допустим.
- RPG/MK19 без своего Observed+AimPoint → нет пуска.
- Attention+Shared не создают Detected.
- Горизонты 5/30, 3 с, 8 с независимы.
- 149/150/151 глаз, оптика 300, passenger/turret envelope; Sound/Shared могут быть дальше Vision и не дают AimPoint.
- Detail ≤ 8/кадр, starve не бесконечен, event-driven, нет второго VisionSystem, нет N×N polling.
- E2E: доклад → знание → взгляд → свой Observed → AimPoint → Fire. Без команды «держать окно».

Play stamp: `FinalPerceptionContract_LAST.txt` **RESULT=PASS PASS=49 FAIL=0** (2026-08-23 12:36:53). NavMesh warning при спавне harness — тот же шум, что у этапов 8–17, не FAIL. Import Error Code 4 на runtime-логе — гонка SourceAssetDB, не FAIL. Search не трогали. A10 остаётся затвором перед AI. #7 — после закрытого A10.
