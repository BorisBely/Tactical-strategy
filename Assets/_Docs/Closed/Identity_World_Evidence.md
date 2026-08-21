# Identity World Evidence

**Статус: FROZEN** (Play 2026-08-20 10:23)  
Этап 1 закрыт. IdentifyTime **4.0 с** / commit **0.50** / Threat High≤25 / Medium≤80 **не менялись**.  
Q / память / Selector / G6 / Fire / RoE / UnitAI **не открывать** из этого слоя.

**Приёмка:** `IdentityCalibrationRuntime_LAST.txt` **PASS 49/0** (C13). EditMode `VisualAffiliationMappingTests` **13/13**.  
Меню: `Tools/Tests/Run Identity Calibration (Play)`. Маппинг без окна Test Runner: `Tools/Tests/Run Identity World Evidence (EditMode)`.  
`IK-GRIP-UNREACHABLE` и NRE `TestListGUI` — шум редактора, не слой.

## Контракт

```text
цель.VisualAffiliation     Player / Enemy / Civilian / Unknown
наблюдатель.UnitTeam       своя сторона мира
        ↓
VisualAffiliationMapping.ToCue
        ↓
ObservableAffiliation      Friendly / Neutral / Hostile / Unknown
        ↓
IdentityKnowledgeMath      без ретюна
        ↓
Contact.Identity
Contact.Relationship
```

Три разных факта:

```text
UnitTeam цели              кто объект в мире
VisualAffiliation          как объект выглядит
Contact.Identity           кем его считает этот наблюдатель
```

Процессор знания **не** читает `UnitTeam` цели. Скан зрения **не** читает evidence. `VisionObservation` остаётся физикой.

Detected + Unknown первые ~2 с — норма. Commit не телепортирует команду. Два наблюдателя могут разойтись.

## Маппинг

Наблюдатель Player: Player→Friendly, Enemy→Hostile, Civilian→Neutral.  
Наблюдатель Enemy: Player→Hostile, Enemy→Friendly, Civilian→Neutral.  
Наблюдатель Neutral: любой ненулевой look→Neutral.  
Unknown look → Unknown cue → Identity не растёт.

## Что не делает этот слой

Не открывает огонь. Не ставит Engage. Не ходит. Не меняет Q, память, commit, Threat.

G6 по-прежнему может Ignore после commit Friendly — это старое правило боя, не новый код этапа.

## Контент

`VisualIdentityEvidence` на `Unit.prefab`, default Unknown.  
Спавн пишет look из `UnitSpawnConfig.ResolvedVisualAffiliation` (отдельно от team). Маскировка: team Enemy + look Player.  
`IdentityAppearance` устарел и в cue не читается.

## Дальше

Развилка B **FROZEN**: `Combat_Engage_Execution.md`. Развилка C **FROZEN**: `Search_Navigation_Execution.md`. Attack/Retreat/Flee **FROZEN**: `Tactical_Navigation_Execution.md`. Дальше: `Tactical_AI_Roadmap.md`. Identity не ретюнить.
