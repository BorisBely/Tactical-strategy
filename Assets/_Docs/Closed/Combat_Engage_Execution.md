# Combat Engage Execution

**Статус: FROZEN** (Play 2026-08-20 10:56:57; EditMode 11:25)  
Этап 2 закрыт. Vision / Identity / Q / память / G6 math / Fire Discipline / hitscan **не менялись**.  
`UnitAIController` **не** на `Unit.prefab`. Нет источника intent → боевой контур как раньше.

**Приёмка:** `CombatEngageExecution_LAST.txt` **PASS 31/0** (T1–T8, выстрел T1). EditMode `CombatIntentMathTests` + `CombatIntentExecutionTests` **14/0**.  
Меню: `Tools/Tests/Run Combat Engage Execution (Play)`, `Tools/Tests/Run Combat Engage Execution (EditMode)`.  
`IK-GRIP-UNREACHABLE` и NRE `TestListGUI` — шум редактора, не слой.

## Контракт

```text
Tactical AI
  Defense / Attack + Hostile + VisibleNow → Action=Engage
  иначе (в Defense/Attack) → Hold
        ↓
CombatIntent  Hold | Engage     (не UnitAIState)
        ↓
Combat читает intent
  Hold  → Aim/Fire становятся Ignore; Track остаётся
  Engage → G6 как есть
        ↓
CombatReadiness
  без AI Readiness: Engage → Auto (Stage 2)
  с Readiness: pose из ReadinessPoseRequest; Engage только NotifyCombatAlert
  Hold → intent Hold; поза всё равно из Readiness
  KO / Unconscious → NotReady (LifeGate)
        ↓
TargetSelector выбирает свою цель
        ↓
G6 Aim / Fire
        ↓
дисциплина / FireController
        ↓
выстрел
```

```text
Engage ≠ Fire
Hold ≠ выключить Combat
Combat не знает UnitAIState
ROE остаётся вето (после G6, до/вместе с Hold-veto)
AI.EngageTarget ≠ Combat.SelectedTarget   (наблюдается, не чинится)
```

Порядок гейтов в `EngagementDecisionController`:

1. `EngagementDecisionMath` / DefaultCombatPolicy  
2. ROE: denied Fire/Aim → Ignore  
3. CombatIntent Hold: Fire/Aim → Ignore  

Нет `ICombatIntentSource` → шаги 2–3 пропускаются.

## Что не делает слой

Не вызывает `StartFiring` / `TryFireSingleShot` из AI.  
Не пишет `SelectedTarget` из `CurrentEngageTarget`.  
Не ретюнит IdentifyTime / Q / память.  
Не кладёт AI на `Unit.prefab` (SelfDefense по умолчанию режет огонь).

SelfDefense + ImmediateThreat=false: Action может быть Engage, CombatIntent=Engage, Aim/Fire режет ROE. Для полного цикла в тесте — MissionCombat.

## Диагностика

`EngagementDecisionController.EngageTargetMismatch` — AI.EngageTarget ≠ Combat.SelectedTarget.  
Это наблюдаемый факт, не авто-фикс. Лог раз в 2 с.

## Дальше

Слой не открывать. Search locomotion — **FROZEN**: `Search_Navigation_Execution.md`. Tactical navigation — **FROZEN**: `Tactical_Navigation_Execution.md`. Дальше — `Tactical_AI_Roadmap.md`. Не писать память из Search.
