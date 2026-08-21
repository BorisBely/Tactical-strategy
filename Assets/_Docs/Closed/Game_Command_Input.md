# Game Command Input

**Статус: 6.3 CLOSED** (`GameCommandInput` + `GameCommandRecipientQuery` + `IssueMany`)  
**6.4 command layer — CLOSED.** См. `Closed/Game_Command_Layer.md`. Overlay / `TryIssue` не подменять. #7 не открыт.

Способ выбрать **получателей** различается. `TacticalCommand` и `GameCommandService` — те же. Нет Group AI: 3 выбранных → 3 одинаковых приказа.

```text
GameCommandInput  (InputMode + Audience + Point)
        ↓
recipients[]
        ↓
GameCommandService.Issue / IssueMany
        ↓
ITacticalCommandReceiver → IssueCommand → существующий AI
```

`InputMode` ≠ `UnitAIState`. Не делать `EnemyAttackAll()` / `PlayerAttackSelected()` как отдельные AI-функции.

## Аудитории

| Audience | Кто | Attach AI |
|----------|-----|-----------|
| `PlayerSelected` | валидные выбранные `RtsUnitMember` (`IsRtsControllable`) | нет; нет приёмника → `NoAI` |
| `EnemyDebug` | активные `UnitTeam == Enemy`, не мёртвые, пехота (есть `UnitFactionConfigurator` или `UnitHealth`, нет `VehicleController`) | да, до `Issue`, по образцу debug overlay |

Neutral не входит ни в один набор.

`GameCommandRecipientQuery` не кэширует армию. Публичная копия выбора: `RtsUnitSelectionManager.CopyValidSelectedUnits`.

## Ввод

`GameCommandInput` (bootstrap AfterSceneLoad). Режимы: `Normal` / `AttackPending` / `DefensePending` / `RetreatPending` / `SearchPending` / `FleePending`.

Поток: кнопка IMGUI (или `BeginPending`) → **не слать приказ** → ПКМ / `ConfirmPoint` → луч + `NavMesh.SamplePosition` (без выбора боевой цели) → `IssueMany(..., Source=Game)` → `Normal`.

`Cancel` точки не требует: сразу `IssueMany(Cancel)`. Esc / ПКМ без точки — сброс latch, 0 команд. Пустое выделение Player → 0, лог `INPUT skip=NoRecipients`.

Пока latch: `TacticalDebugOrderSession.IsCommandPointPending` — RTS **не** коммитит walk. LMB-выделение остаётся. Обычный RMB-ход в `Normal` не тронут.

HUD — отдельная полоса у `GameCommandInput`. Overlay `UseOfForceDebugOverlay` / `TacticalSideCommands` остаются на `TryIssue` (6.1 freeze).

## Лог

Тег `INPUT` (не `SELECT`). Timeline: `mode=` `audience=` `verb=pending|issue|cancel|skip` `n=` `pos=`.

```
INPUT → GAMECMD → CMD → AI → MOVE
```

## Приёмка

- EditMode: `GameCommandInputTests` (`AI63_…`), публичные `BeginPending` / `ConfirmPoint` / `CancelPending` (мышь не симулируется).
- Play: `Tools/Tests/Run Game Command Input (Play)` → `_Docs/Logs/Tests/GameCommandInput_LAST.txt`.

## Явно не в 6.3

Box-select rewrite, Action Panel production UX, формации как Group AI, сеть, командир, подмена overlay на `TryIssue`, #7 RoE, авто-AI на `Unit.prefab` для Player.
