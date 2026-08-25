# Combat Event World

**Слой:** тактический **#8**.  
**Дизайн:** `Пехота_система_дизайн.md` §6.2.  
**Статус:** **CLOSED / FROZEN 25.08.2026.** EditMode `CombatEventTests` **10/0**; Play `CombatEvent_LAST.txt` **PASS 36/0**; Arena `Infantry_20260825_222738`. #9 закрыт отдельным контрактом; этот слой не переоткрывать.

**#7 gate (без ретюна матрицы).** Use of Force **PASS 107/0**, Combat Engage **PASS 36/0**, Immediate Threat Live **PASS 18/0**. Матрица `UseOfForceEvaluator` в этом проходе не трогалась.

## Соответствие §6.2

| Требование дизайн-дока | Этот срез |
|------------------------|-----------|
| Мир публикует боевые факты | `CombatEventHub.Publish` |
| event ≠ automatic knowledge | шина **не** пишет в `DetectionProcessor`; не вызывает `WorldSoundHub` |
| SHOT / Hit / Impact / Death | `CombatEventType.Gunshot / Hit / Impact / Death` |
| Explosion | контракт отложен (гранаты позже); в enum среза нет |
| Footstep | остаётся в `WorldSoundHub`, не CombatEvent |
| Звук в AI snapshot / Search по слышанному | **#9**, не этот слой |

```text
Hitscan / UnitHealth
        ↓
CombatEventHub          ← факт мира
        ↓
ImmediateThreatCombatEventBridge    ← только Gunshot/Hit
        ↓
ImmediateThreatSignal → ImmediateThreatSource     ← внешний #7 без изменений
```

`WorldSoundHub` по-прежнему вещает в `DetectionProcessor` (знание солдата). Это **не** шина #8.

## Контракт

```text
CombatEvent = факт мира
event ≠ knowledge
event ≠ Observed
event ≠ AimPoint
event ≠ Fire
ImmediateThreat ≠ Fire
```

Поля: `Type`, `Source`, `Instigator`, `Target`, `Position`, `Time`.

| Type | Кто публикует | ImmediateThreat |
|------|---------------|-----------------|
| Gunshot | hitscan после выстрела; `Target` = `SelectedTarget` если есть | да, если `Target` — этот юнит и Instigator Hostile |
| Hit | hitscan попал в `DamageableTarget` | да, ConfirmedHit по тем же правилам #7 |
| Impact | hitscan попал не в `DamageableTarget` (геометрия) | нет |
| Death | `UnitHealth.EnterDead`, HP-смерть `DamageableTarget` | нет |

Внешний API #7 (`ImmediateThreatSignal.NotifyIncomingFire / NotifyConfirmedHit / NotifyHostileAttack`) **не меняется**. Signal сам CombatEvent **не** публикует (иначе цикл). Hitscan больше не вызывает Signal напрямую.

Потребители сами решают. В этом срезе потребитель — только ImmediateThreat. Perception / Search / Cover **не** подписаны.

## Не открывать сейчас

#9 Sound in AI, #10 Search 2.0, #11 приоритет приказов, #12 калибровка цели, #13–#16 cover/movement/weapon+rank/group, #17–#26. **GROUND ASSAULT** — северная цель фаз III–IV, не приёмка #8.

#9 закрыт отдельным контрактом `Sound_Report_AI.md`. Этот слой (#8) **не** переоткрывать.

## Тесты

| Набор | Где |
|-------|-----|
| event ≠ knowledge; Gunshot/Hit → #7; Impact/Death не threat; Signal API жив | EditMode `CombatEventTests` **PASS 10/0** (25.08.2026 22:11) |
| тот же контракт в Play | `Tools/Tests/Run Combat Event World (Play)` → `CombatEvent_LAST.txt` **PASS 36/0** (25.08.2026 22:25) |
| внешний #7 | EditMode `ImmediateThreatSourceTests`, `UseOfForcePolicyTests`, `ImmediateThreatRoeHandoffTests` (без ретюна) |
| Arena | `Infantry_20260825_222738` (22:27): 10 Player / 10 Enemy / 20 Civilian; SHOT 79 (Player 47, Enemy 32, Civilian 0); THREAT 40 (IncomingFire 1, ConfirmedHit 19, Expired 20) только у боевых; **0 THREAT / 0 SHOT у Civilian**. Death в арене не было — покрыто Play E7/E9 и EditMode |

`TestResults.xml` / `Unity.PerformanceTesting.Editor.TestRunBuilder` — это Test Runner, не Play-лог #8.

Слой **FROZEN**. Explosion не открывать в этом файле. #9 — `Sound_Report_AI.md`.
