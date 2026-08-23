# Game Command Layer

**Статус: 6.4 CLOSED** (стабилизация и сквозная приёмка 6.1–6.3)  
**#7 ImmediateThreat + живой RoE** — следующий слой *тактической* карты, не раньше этапов **A–E** `Пехота_дорожная_карта.md`. Overlay / `TryIssue` не подменять. SHOT не критерий этого слоя.

6.4 не добавляет новый способ управления. Доказывает, что уже существующая цепочка устойчива:

```text
GameCommandInput (Player latch | Enemy Debug)
        ↓
GameCommandRecipientQuery (живой collect)
        ↓
GameCommandService.IssueMany
        ↓
IssueCommand → state + context
        ↓
существующие handlers / TacticalNavigationExecutor / CombatIntent
```

Можно: дать приказ → сменить → отменить → дать снова → нескольким. Одна актуальная задача на юнит. Нет Group AI. Нет отдельной Command FSM.

`InputMode` ≠ `UnitAIState`. Оба audience сходятся в один `TacticalCommand` (`Source=Game`). Разница только в получателях.

## Семантика команд

Новых команд нет. Повтор same-state идёт через `IssueCommand` (Exit/Enter, без Idle bounce). `TryApplyCommand` same-state **не** меняет context (FROZEN, не игровой путь).

| Команда | Кому | Данные | State | Context | Ход | Повтор same-state |
|---------|------|--------|-------|---------|-----|-------------------|
| Attack(P) | любой через сервис | точка обязательна | Attack | Destination=P | Walk reason=Attack | новый dest, без Idle |
| Defense(P) | любой | точка | Defense | Anchor=P | **нет Walk** | новый якорь, без Idle |
| Search(P) | любой | точка | Search | SearchPosition=P | Walk reason=Search | новый snapshot |
| Retreat(P) | не из Idle/Flee; не из Retreat в Attack/Search | точка | Retreat | Destination=P | Walk reason=Retreat | новый dest |
| Flee(P) | не из Flee в Attack/Defense/Search/Retreat | точка | Flee | dest | Walk reason=Flee | новый dest |
| Cancel | любой, в т.ч. Idle | нет точки | Idle | Empty | Stop | stay Idle |

Reject (таблица, без bounce): Idle→Retreat; Retreat→Attack/Search; Flee→всё кроме Idle/Cancel.

Нет AI (Player) → `NoAI`, контроллер не создаётся. Enemy Debug вешает AI до Issue. Мёртвый / неактивный / уничтоженный → `InvalidUnit`.

## Lifecycle = лог

Нет состояний Issued/Validated/Executing. Timeline:

```text
INPUT  mode=AttackPending audience=PlayerSelected verb=issue n=2 units=… pos=…
GAMECMD issue unit=P01 type=Attack …
CMD issue / accepted
AI state=Attack
MOVE reason=Attack
```

Reject: `GAMECMD … rejected reason=InvalidStateTransition`. Тег боя `SELECT` не использовать для выделения.

## Combat / Navigation

Команда не вызывает Fire, не назначает боевую цель, не пишет G6/DISC/GATE/SHOT. CombatIntent остаётся на существующем pipeline. Defense **не** даёт `MOVE reason=Defense`.

Ход только через `IUnitMoveCommand` / `TacticalNavigationExecutor`. Command layer не зовёт `NavMeshAgent.SetDestination`.

SHOT / «увидел → Engage → огонь» — **#7**.

## Приёмка

- EditMode: `GameCommandLayerTests` (`AI64_…`)
- Play: `Tools/Tests/Run Game Command Layer (Play)` → `_Docs/Logs/Tests/GameCommandLayer_LAST.txt`

Источник Play: `GameCommandInput.ConfirmPoint`, не `DebugGameCommandSource`, не overlay.

## Критерий закрытия

Один pipeline; Player и Enemy Debug; замена/Cancel/Stop; живой collect (спавн/смерть/disable); стороны не текут; запрещённые переходы; Combat/Nav не обойдены; EditMode matrix + Play E2E PASS. SHOT/Engage не pass 6.4.

## Явно не в 6.4

Modifier+RMB, Action Panel, box-select rewrite, Group AI, сеть, командир, overlay→`IssueCommand`, авто-AI на префабе, новые клетки таблицы, живой ImmediateThreat, требование SHOT.
