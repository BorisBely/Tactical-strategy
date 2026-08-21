# Game Command Source

**Статус: 6.2 CLOSED** (`GameCommandService` + `DebugGameCommandSource`)  
**6.3 RTS / ввод — CLOSED.** См. `Closed/Game_Command_Input.md`. **6.4 command layer — CLOSED.** См. `Closed/Game_Command_Layer.md`.

Игра отдаёт приказ юниту одним каналом. Она не знает, как ходить и как стрелять.

```text
DebugGameCommandSource / later RTS
        ↓
GameCommandService.Issue(unit, TacticalCommand)
        ↓
ITacticalCommandReceiver
        ↓
UnitAIController.IssueCommand
        ↓
существующая state machine / TacticalNavigationExecutor
```

`GameCommandService` **не** ссылается на `UnitAIController`. Нет прямого Game → Navigation и Game → Combat.

## Адресация

Публичный API принимает `Component` / `GameObject` / `Transform`. Корень: `UnitTeam` в родителях, иначе сам объект. Слоты `E01` и `EntityId` — не ключи API.

Нет получателя (`ITacticalCommandReceiver`) → `NoAI`. AI **не** создаётся как побочный эффект приказа.  
`UnitHealth.IsDead` → `InvalidUnit`. Нет `UnitHealth` = живой.

Переходы Attack→Retreat и т.д. проверяет только AI. Сервис прокидывает `InvalidStateTransition` / `MissingDestination` / `InvalidCommandData`.

Повтор `Attack(A)` затем `Attack(B)`: сервис тупой, шлёт две команды. Same-state replace — в `IssueCommand` (6.1).

## Источник приёмки

`DebugGameCommandSource` — не кнопка debug AI. Строит `TacticalCommand` с `Source=Game` и зовёт сервис.

`IUnitTacticalCommand` / overlay / арена остаются на `TryIssue`.

## Лог

Тег `GAMECMD`: `issue` / `accepted` / `rejected reason=`. Timeline для всех трёх.

```
GAMECMD → CMD → AI → MOVE
```

`GAMECMD rejected reason=NoAI` — на юните нет тактического AI.  
Нет `GAMECMD`, есть `CMD` — приказ обошёл сервис (`IssueCommand` напрямую).  
Нет `GAMECMD` и нет `CMD`, есть `AI` — debug `TryIssue`.

## Приёмка

- EditMode: `GameCommandServiceTests`
- Play: `Tools/Tests/Run Game Command Source (Play)` → `_Docs/Logs/Tests/GameCommandSource_LAST.txt`

## Явно не в 6.2 / не в 6.3 до отдельного этапа

RTS selection, мышь, UI, отряд, командир, сеть, mission planner, авто-создание AI на префабе, правка executors, новые клетки таблицы, подмена overlay/арены, #7 ImmediateThreat / RoE.
