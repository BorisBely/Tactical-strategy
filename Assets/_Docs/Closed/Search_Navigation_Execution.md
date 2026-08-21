# Search Navigation Execution

**Статус: FROZEN** (Play 2026-08-20 12:06:17; EditMode 18/0)  
Этап 3 закрыт. Search уже **решает** точку (LastKnown snapshot, 15 м, ReturnState). Этот слой только **ходит** туда через существующий пехотный `UnitNavLocomotionDriver`.

Vision / Identity / Q / память / G6 math / CombatIntent / AI-1 decision **не менялись**.  
`UnitAIController` **не** на `Unit.prefab`. Search **не** пишет Memory. LastKnown **не** aim.

**Приёмка:** Play `SearchExecution_LAST.txt` **PASS 45/0** (T1–T8, stamp 12:06:17). EditMode `UnitAISearchTests` + `UnitAISearchExecutionTests` **18/0**.  
Меню: `Tools/Tests/Run Search Execution (Play)`, `Tools/Tests/Run Search Execution (EditMode)`.  
`IK-GRIP-UNREACHABLE` и NRE `TestListGUI` — шум редактора, не слой.

## Контракт

```text
Defense / Attack + Hostile not VisibleNow + useful LastKnown
        ↓
UnitAISearchDecision (FROZEN) → Search
SearchPosition = LastKnown на входе (snapshot)
        ↓
UnitAISearchHandler
  один IssueNavOrder(SearchPosition, Walk) через IUnitMoveCommand
        ↓
UnitNavLocomotionDriver Walk
        ↓
planar dist ≤ 15 m → HardStop, остаёмся в Search, смотрим
        ↓
Found: Hostile + VisibleNow → ReturnState (Defense/Attack) → ResolveAction → Engage
Stale / conf=0 / forgotten → ReturnState, HardStop
External Retreat → Search→Retreat, HardStop Search Walk, затем Retreat Walk (Stage 4)
```

```text
успех поиска ≠ приход в 15 м
SearchPosition не едет за живым LastKnown
нет драйвера / CanIssue=false → Search как раньше (decision-only)
драйвер есть и TryMoveTo=false → Resume
скорость только Walk
CombatIntent на Search = Hold (action None)
```

Один активный заказ на время Search. Не каждый тик `IssueNavOrder`.

Destination = `CurrentContext.SearchPosition`. Живой LastKnown во время Search читается только в HUD/тестах.

## Адаптер

`IUnitMoveCommand` — runtime, на том же GO. Не vehicle `NavigationRequest`. Не RTS `RtsUnitMember.IssueMoveOrder` / `UnitClickToMove`.

| Реализация | Где |
|------------|-----|
| `UnitNavMoveCommand` | продакшен → enabled `UnitNavLocomotionDriver` |
| `UnitMoveCommandRecorder` | EditMode, runtime MonoBehaviour (не Editor asmdef) |

`UnitNavigationReason.Search` — только лог/HUD. Механика Walk от reason не зависит.

Нет команды и `CanIssue=false` → no-op (существующие decision-тесты без NavMesh остаются в Search).

## Граф

Единственное точечное изменение AI-1 графа: **Search → Retreat разрешён**. T7 = отмена Search nav + State=Retreat, затем Retreat Walk (Stage 4).

## Диагностика

HUD на `UnitAIController` (Search): State, Action, Intent, SearchPosition, live LastKnown, LastSeenConfidence, AreaRadius, nav issued/reached/intent/reason, ResumeState.

Цель: SearchPosition **не** едет за обновлённым LastKnown.

## Что слой не делает

Прочёс, сектора, кручение на месте, экстраполяция, запись LastKnown, aim/Fire в LastKnown, укрытия, flanking, групповой Search, Defense якорь, AI на `Unit.prefab`, ретюн 0.25 / 15 м / IdentifyTime / G6 / CombatIntent math.

## Дальше

Слой Search decision не открывать. Attack / Retreat / Flee locomotion — **FROZEN**: `Tactical_Navigation_Execution.md`. Дальше — `Tactical_AI_Roadmap.md` (#6 commands). Не Block D.
