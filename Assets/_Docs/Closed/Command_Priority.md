# Command Priority / Interruption

**Слой:** тактический **#11**.  
**Дизайн:** `Пехота_система_дизайн.md` §6.4.  
**Статус:** **CLOSED / FROZEN 26.08.2026.** EditMode **18/0**. Play `CommandPriority_LAST.txt` **PASS 18/0** (10:47). Регрессия: Immediate Threat Live **18/0**; Combat Event World **36/0**; Sound In AI **20/0** (11:03); Search 2.0 Play **22/0**; Search 2.0 EditMode **44/0**. #13 CLOSED / FROZEN: `Dynamic_Cover.md`.

**Live amendment 28.08.2026 (occupy/execution, слой не reopen):** ImmediateThreat = HoldState во всех состояниях, включая Search. Не Flee. EmergencyCover — overlay.

Состояние не выбирает приоритет. Приоритет считается отдельным слоем, затем применяется уже существующая `UnitAITransitionTable`.

```text
Event / Command
      ↓
Priority Resolver
      ↓
Accept / Reject / Interrupt / ReplaceContext / Resume / HoldState
      ↓
UnitAIState
```

## Command ≠ Interrupt

**Command** задаёт, что солдат должен делать: Attack, Defense, Search, Retreat, Flee, Cancel.  
**Interrupt** говорит, что продолжать текущую задачу прямо сейчас опасно или бессмысленно: ImmediateThreat, completion, более высокий приказ.

ImmediateThreat **не** переводит `UnitAIState` в Flee / Retreat. Attack + ImmediateThreat = Attack остаётся, локальная реакция (RoE) разрешена.

## Полосы (зафиксировано в #11)

```text
Emergency > High > Mission > Tactical > Routine
```

| Состояние / событие | Полоса | Эффект |
|---------------------|--------|--------|
| Flee | Emergency | немедленная смена задачи, если таблица позволяет |
| ImmediateThreat | Emergency | HoldState, включая Search (RoE / EmergencyCover) |
| Retreat | High | отменяет Mission / Tactical |
| Attack / Defense | Mission | заменяют текущую mission; друг друга — ReplaceMission |
| Search | Tactical | overlay на Attack/Defense; с Retreat/Flee не конкурирует как равный приказ |
| Idle | Routine | фон |
| Arrival / Search exhausted / Found | Result (StateCompletion) | не «перебивает» новый Attack |

Таблица переходов **не обходится**. Illegal cell → `InvalidStateTransition` (как 6.1).  
Table-legal, но ниже полосой (Retreat → Defense) → `LowerPriority`.

Search с Attack/Defense — overlay: Tactical на Mission, `ReturnState` сохраняется.  
Новый Attack/Defense/Retreat/Flee во время Search отменяет Search (`NewOrder`), ReturnState и destination поиска сбрасываются.

## Cancel

Не отдельное состояние.

| Было | Cancel |
|------|--------|
| Search | ReturnState (Attack / Defense / Idle) |
| Attack / Defense / Retreat / Flee | Idle, если таблица позволяет |
| Idle | Accepted, остаёмся Idle |

## Очередь

v1: **текущая команда + пустой pending**. Нет RTS-очереди. Retreat заменяет Attack, не ставится в хвост. Поле pending зарезервировано и очищается на accept.

`TryApplyCommand` — внутренний путь (автономный Search, completion). Same-state по-прежнему **не** меняет context (FROZEN 6.1). LowerPriority на внутреннем пути не применяется, чтобы не ломать Flee→Idle и исторический Retreat→Defense через `TryApplyCommand`.

## ImmediateThreat (E3)

| Состояние | Решение |
|-----------|---------|
| Attack / Defense / Idle / Retreat / Flee / Search | HoldState, state не меняется |

Автономный старт Search из Attack/Defense по звуку/памяти **не блокируется** ImmediateThreat (#9). Search + ImmediateThreat **не** ReturnState (amendment 28.08.2026): HoldState + EmergencyCover.

## Лог

Канал `CMD_PRIORITY`:

```text
incoming=Retreat current=Attack result=Interrupt reason=HigherPriority
incoming=Search current=Retreat result=Reject reason=IllegalTransition
incoming=Attack current=Attack result=ReplaceContext reason=SameStateReplace
incoming=ImmediateThreat current=Attack result=HoldState reason=EmergencyLocal
```

## Тесты

Меню `Tools/Tests` сверху вниз:

```text
── Current ──
Tools/Tests/Run Dynamic Cover (EditMode)

── Regression ──
Tools/Tests/Run Regression (Play)
Tools/Tests/Run Regression (EditMode)

Archive/Regression/   одиночные #7–#11
Archive/   закрытое зрение, G, tactics, weapon, calibration
```

| Набор | Меню | Отчёт |
|-------|------|-------|
| #11 A–I | `Tools/Tests/Run Regression (EditMode)` (вместе с #10) или `Archive/Regression/Run Command Priority (EditMode)` | Console `[CommandPriority] finished` **18/0** (26.08.2026) |
| #11 Play S1–S4 | `Tools/Tests/Run Regression (Play)` или `Archive/Regression/Run Command Priority (Play)` | `CommandPriority_LAST.txt` **18/0** (26.08.2026 10:47) |

### J — регрессия #7–#10

`Tools/Tests/Run Regression (Play)` и `Run Regression (EditMode)` гоняют все слои. Сводка Play: `FrozenLayersPlay_LAST.txt`.

| Слой | Отчёт | PASS при FREEZE |
|------|-------|-----------------|
| #7 ImmediateThreat | `ImmediateThreatLive_LAST.txt` | **18/0** (26.08.2026 10:56) |
| #8 Combat Events | `CombatEvent_LAST.txt` | **36/0** (26.08.2026 10:57) |
| #9 Sound in AI | `SoundInAi_LAST.txt` | **20/0** (26.08.2026 11:03) |
| #10 Search 2.0 Play | `Search20_LAST.txt` | **22/0** (26.08.2026 10:58) |
| #10 Search 2.0 EditMode | Console `[Search20] finished` / `[FrozenLayers] finished` | **44/0** (26.08.2026) |

Слой **FROZEN**. #13 CLOSED / FROZEN: `Dynamic_Cover.md`.

## Не в #11

Cover, Reposition, Flank, Group, CQB, Suppression, Morale, Utility AI, полноценная очередь приказов. Калибровка цели — #12 FROZEN. Cover — #13 CLOSED / FROZEN.
