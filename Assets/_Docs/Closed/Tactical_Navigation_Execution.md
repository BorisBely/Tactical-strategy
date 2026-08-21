# Tactical Navigation Execution

**Статус: FROZEN** (Play 2026-08-20 14:46:26; EditMode 31/0)  
Этап 4 закрыт. Attack / Retreat / Flee исполняют уже существующие состояния через тот же пехотный `UnitNavLocomotionDriver`, что и Search.

Vision / Identity / Q / память / G6 math / CombatIntent / Search decision **не менялись**.  
`UnitAIController` **не** на `Unit.prefab`. AI **не** ходит в `Transform` / `NavMesh` / Rigidbody напрямую.

**Приёмка:** Play `TacticalNavigation_LAST.txt` **PASS 36/0** (T1–T7, stamp 14:46:26). EditMode `UnitAITacticalNavigationTests` + `UnitAISearchExecutionTests` **31/0**.  
Меню: `Tools/Tests/Run Tactical Navigation (Play)`, `Tools/Tests/Run Tactical Navigation (EditMode)`.  
`IK-GRIP-UNREACHABLE` — шум harness, не слой.

## Контракт

```text
State + destination
        ↓
TacticalNavigationExecutor
        ↓
IUnitMoveCommand.TryMoveTo(dest, Walk)
        ↓
UnitNavLocomotionDriver
```

Один исполнитель на Search / Attack / Retreat / Flee. Разные правила только на завершении.

| State | Destination | Reached | Нет точки |
|-------|-------------|---------|-----------|
| Search | `SearchPosition` (15 м area) | HardStop, **остаёмся Search** | не этот слой |
| Attack | `Destination` (`HasDestination`) | HardStop, **остаёмся Attack** | не Walk, Attack |
| Retreat | `Destination` | HardStop, **остаёмся Retreat** | не Walk, Retreat |
| Flee | `Destination` | HardStop, **Flee → Idle** | не Walk, **Flee остаётся Flee** |

Приход в точку Attack / Retreat **не** переводит в Defense / Idle. Только Flee завершает себя.

Радиус точки Attack / Retreat / Flee: **1.5 м** planar (`TacticalNavigationMath.DefaultPointArrivalRadius`). Search по-прежнему 15 м.

```text
Attack + Hostile VisibleNow → CombatIntent=Engage, Walk продолжается
Attack ≠ подойти к врагу и стрелять
AI не выбирает Combat-цель и не вызывает Fire / Aim
```

## Отмена

Тот же механизм, что у Search: `Exit` → `Stop`, затем новый `Enter` → `Walk`.

```text
Attack Walk(A) → Retreat → A cancelled → Walk(B)
Search Walk → Retreat → Search cancelled → Retreat Walk
Defense → Retreat → Retreat Walk
Attack → Flee → Flee Walk
```

## Что слой не делает

Искать врага, читать Q / DetectionProgress / Identity, выбирать цель, Fire, Aim, писать LastKnown, менять состояние кроме **Flee → Idle при Reached**.

Скорость только Walk. Reason — диагностика (`Attack` / `Retreat` / `Flee` / `Search`).

Нет команды / `CanIssue=false` → decision-only, состояние на месте.  
`TryMoveTo=false` при Attack / Retreat / Flee → остаёмся в состоянии (Search по-прежнему Resume).

## Дальше

Слой не открывать. Дорожная карта: `Tactical_AI_Roadmap.md`. Следующее — **#6 Real game commands**. Не Block D, не BT, не auto-retreat, не Defense якорь.
