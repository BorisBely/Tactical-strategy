# Search 2.0

**Слой:** тактический **#10**.  
**Дизайн:** `Пехота_система_дизайн.md` §6.3.  
**Статус:** **CLOSED / FROZEN 26.08.2026.** Play `Search20_LAST.txt` **PASS 22/0** (09:52; регресс 10:58). Регрессия Play: ImmediateThreat **18/0**; Combat Event **36/0**; Sound In AI **20/0**. **#11 FROZEN** (`Command_Priority.md`).

**Live amendment 28.08.2026 (occupy/execution, слой не reopen):** overlay Search больше не выходит по ImmediateThreat. Found / Exhausted / Expired / Cancelled без изменений. Attack+gunshot/report Search 2.0 сохранён. Visual Attack overlay — dwell 1.5 с.

Baseline decision **FROZEN**: `Search_Navigation_Execution.md`. Этот слой расширил **исполнение** существующего `Search`, не добавил состояние Investigate.

## Контракт

```text
Defense / Attack + no Hostile VisibleNow
        + (useful LastKnown | hostile combat sound | hostile report)
        ↓
SearchArea snapshot (один источник, без слияния)
        ↓
кандидаты generate → filter → score → cache (не каждый тик)
        ↓
Walk candidate → STOP / LOOK / EVALUATE (~1 с)
        ↓
Found: Hostile+VisibleNow → ReturnState + Engage
Exhausted: все кандидаты осмотрены → ReturnState
Expired: память+звук+доклад кончились → ReturnState
Threat: ImmediateThreat не завершает Search (amendment 28.08.2026).
        ImmediateThreat → RoE / EmergencyCover, state остаётся Search.
New order: приказ отменяет Search
```

```text
Visual useful memory  >  hostile combat sound  >  hostile report
SoundPosition ≠ LastKnown ≠ ReportPosition
Search не пишет Memory / LastKnown
SearchPosition = текущий кандидат
AreaRadius 15 м = граница области, не arrival
arrival кандидата = 1.5 м
во время Search новый звук не пересобирает area/cache (#9 E4)
```

Idle сам Search не начинает. Stale / expired visual memory не стартует Search.

## Источники области

| Source | Center | Confidence |
|--------|--------|------------|
| VisualMemory | LastKnown | LastSeenConfidence |
| Sound | SoundPosition | sound.Confidence |
| AllyReport | Report.Position | report.Confidence (Identity=Hostile) |

Не смешивать в один контакт. Итоговая область — от выбранного источника.

## Кандидаты (v1)

Локально, детерминированно, 3–8 семян, cap `MaxSearchCandidates = 6`:

- центр области
- точка на оси origin→center
- точка за центром
- 4 точки кольца на 0.6×radius

Фильтр: внутри area, planar dedup, reachable (injectable; NavMesh в Play).  
Score v1: evidence alignment, confidence, freshness, proximity (ближе к юниту безопаснее). Формулу cover/weapon не вводим.

Cache на `Enter`. Инвалидация только при выходе из Search.

## Завершение

`Found | Exhausted | Expired | Cancelled | NewOrder | Threat`

Threat остаётся в enum; ImmediateThreat его больше не выставляет (amendment 28.08.2026).

LOOK = HardStop, остаёмся в Search. HeadLookAround / cover / экстраполяция скорости — не этот слой.

## Тесты

| Набор | Где |
|-------|-----|
| A–E Area / Candidates / Ordering / Execution / Integration | EditMode `Search20Tests` + `SearchAttackHoldTests` + регрессия `UnitAISearchTests` + `UnitAISearchExecutionTests` — `Tools/Tests/Run Search 2.0 (EditMode)` |
| Play SearchTestArena | `Tools/Tests/Run Search 2.0 (Play)` → `Search20_LAST.txt` **PASS 22/0** (26.08.2026 09:52) |
| #7 регрессия | Play Immediate Threat Live **PASS 18/0** |
| #8 регрессия | Play Combat Event World **PASS 36/0** |
| #9 регрессия | Play Sound In AI **PASS 20/0** |

Слой **FROZEN**. #11 **FROZEN**: `Command_Priority.md`.

## Логи

Канал `SEARCH`. SNAP: `searchState` `searchSource` `searchCandidate` `searchRemaining` `searchArea`.

## Не в этом слое

#13 cover/проёмы, поворот/огонь на звук, Investigate, ретюн 0.25 / IdentifyTime / G6 / CombatIntent. #11 приоритет — отдельный FROZEN слой: `Command_Priority.md`.
