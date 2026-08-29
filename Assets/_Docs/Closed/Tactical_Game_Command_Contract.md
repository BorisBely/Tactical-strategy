# Tactical Game Command Contract

**Статус: 6.1 CLOSED** (контракт данных + `IssueCommand`)  
**6.2–6.4 CLOSED.** **#11** слой приоритета поверх IssueCommand: `Command_Priority.md` (**CLOSED / FROZEN 26.08.2026**). Таблица переходов не заменяется.

Внешний приказ говорит **что и где**. Не говорит, как стрелять или как ходить. Состояние машины — не тип команды.

```text
Game / Test / Scenario
        ↓
TacticalCommand
        ↓
IssueCommand → Priority Resolver → table
        ↓
UnitAIState + UnitAIStateContext
        ↓
существующие handlers / TacticalNavigationExecutor
```

Vision, CombatIntent, G6, RoE, Search/Attack/Retreat/Flee executors и клетки `UnitAITransitionTable` **не менялись**.

## Два входа

| Вход | Роль |
|------|------|
| `IssueCommand(TacticalCommand)` | Вход в машину состояний (6.1). Игра после 6.2 зовёт его только через `GameCommandService`. |
| `IUnitTacticalCommand` / `TryIssue` / overlay / арена | Отладка. Bounce остаётся. Игру сюда не вести. |

`UnitAICommand` / `TryApplyCommand` — внутренний приказ машины. Тот же state **не** обновляет context (FROZEN).

Не делать `state = command.Type`. Cancel ≠ отдельное состояние. **#11:** Cancel из Search возвращает `ReturnState`; иначе Idle, если таблица позволяет.

## Данные команды

`TacticalCommand`: `Type`, `Position`, `HasPosition`, `Target` (Transform, опционально), `Source`.

Типы: Defense, Attack, Search, Retreat, Flee, Cancel.  
Источник: Test, Debug, Scenario. `Game` зарезервирован именем — RTS не подключать.

`Vector3.zero` — допустимая точка. Нет точки = `HasPosition == false`, не «ноль».

В команду **не** класть: Area, formation, cover, RoE, aim, LastKnown.

## Маппинг после accept

| Команда | Точка | После accept | Ходьба |
|---------|-------|--------------|--------|
| Defense | да | Defense + `ForDefense` | нет |
| Attack | да | Attack + `ForAttack(pos, dir, target)` | Walk → Stop, остаёмся Attack |
| Search | да | Search + существующий `ForSearch` / snapshot точка | тот же Search executor |
| Retreat | да | Retreat + `ForRetreat` | Walk → Stop |
| Flee | да | Flee + `ForFlee` | Walk → Stop → Idle |
| Cancel | нет | Search → ReturnState; иначе Idle + empty | Exit отменяет nav |

`Target` только копируется в `UnitAIStateContext.TargetEntity` у Attack. Выбор цели команда не делает.

Команда **не** вызывает Fire / Aim / Navigate, **не** пишет Vision / RoE / SelectedTarget / LastKnown.

## Отказы (без bounce)

`InvalidStateTransition` — как таблица, без обхода через Idle:

- Idle → Retreat
- Retreat → Attack / Search
- Flee → Defense / Attack / любое не-Idle

`LowerPriority` — клетка таблицы разрешена, но входящая полоса ниже текущей (#11): Retreat → Defense.  
`MissingDestination` — тип требует точку, `HasPosition == false`.  
`InvalidCommandData` — неизвестный тип, NaN/Inf.

Cancel из Idle = Accepted, остаёмся Idle.

## Same-state replace

`Attack(A)` затем `Attack(B)` через `IssueCommand`: остаёмся Attack, пишем новый context, **перезаходим** в handler (`Exit` → cancel nav → `Enter` → новый Walk). Это не переход Attack→Attack в смысле bounce и не `TryApplyCommand`.

## Лог

Тег `CMD`: `issue` / `accepted` / `rejected reason=`. **#11:** `CMD_PRIORITY` (`incoming/current/result/reason`). После 6.2 игра пишет ещё `GAMECMD` (сервис). Диагноз хода: `GAMECMD → CMD → CMD_PRIORITY → AI → MOVE`.

## Приёмка

- EditMode: `TacticalCommandContractTests`
- Play: `Tools/Tests/Run Tactical Command Contract (Play)` → `_Docs/Logs/Tests/TacticalCommandContract_LAST.txt`

Источник команд в Play — сам smoke (`TacticalCommandSource.Scenario`), не RTS.

## Явно не в 6.1 / не в 6.3 до отдельного этапа

RTS, selection, UI, группы, смена RoE, ImmediateThreat, AI на префабе, правка Search executor, новые клетки таблицы, подмена overlay/арены на `IssueCommand`.
