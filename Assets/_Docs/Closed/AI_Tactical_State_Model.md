# AI Tactical State Model

**Статус: FROZEN** (Play 2026-08-20 00:08:24)  
**Приёмка:** `AITacticalState_LAST.txt` **PASS 71/0**. `IK-GRIP-UNREACHABLE` — шум harness.  
Vision / AI-0 остаются FROZEN. Этот слой — задача юнита, не зрение и не стрельба.

```text
Order  →  UnitAIController  →  UnitAIState + UnitAIStateContext
                ↑                     ↓
      AIPerceptionFrame (AI-0)   UnitAIAction (Hold / Engage)
                ↑                     ↓ later
         LastKnown (read-only)   Navigation / Combat
```

Perception **не** пишет State. `UnitAIController` может сам выдать Search из LastKnown. Search **не** пишет Memory.

---

## 1. Шесть состояний

| State | Смысл | Context |
|-------|--------|---------|
| Idle | нет задачи; ждёт приказ; не начинает тактику сам | пусто |
| Defense | удерживать место / сектор | anchor, area, facing |
| Attack | добиться результата в точке / зоне / по объекту | destination, entity, direction |
| Search | искать по LastKnown из AI-0 | origin, search position, resume |
| Retreat | управляемо уйти на другую позицию | destination |
| Flee | бросить задачу, уйти от угрозы | escape direction / destination |

Не состояния: Observe, Track, Investigate, Engage, Chase, Suppress, Patrol.

---

## 1.1 Действие внутри состояния (AI-1.9)

| Action | Когда |
|--------|--------|
| None | Idle / Search / Retreat / Flee (этот проход) |
| Hold | Defense или Attack, нет Hostile+VisibleNow |
| Engage | Defense или Attack, есть Hostile+VisibleNow |

```text
Idle    + HostileVisible → Idle  + None     (не начинает тактику сам)
Defense + HostileVisible → Defense + Engage
Attack  + HostileVisible → Attack  + Engage
Defense + UnknownVisible → Defense + Hold   (Unknown ≠ Hostile)
Defense + Hostile lost   → Search (LastKnown; не пишет Memory)
```

`CurrentEngageTarget` задаётся только при Action=Engage (наибольший Threat среди Hostile+VisibleNow).  
`HasHostileVisible` — факт Perception, даже если Action=None.  
Engage **не** вызывает TargetSelector, Navigation, Fire.

---

## 1.2 Search из LastKnown (AI-1.10)

```text
Defense / Attack + Hostile not visible + HasUsefulMemory
  → Search(SearchPosition = LastKnownPosition)
Search + HostileVisible
  → Resume Defense / Attack (найден)
Search + memory gone / stale
  → Resume Defense / Attack / Idle
Idle + useful LastKnown
  → Idle (не начинает тактику сам)
```

Полезность памяти — порог AI-0 (`LastSeenConfidence > 0.25`). Stale не начинает Search.  
Context хранит точку поиска, не копию Confidence/Identity. Confidence читается со снимка.  
Search не вызывает decay и не меняет `LastSeenConfidence` / `LastKnownPosition` / `LastSeenTime`.

---

## 2. Ownership

| Слой | Решает |
|------|--------|
| Order (`UnitAICommand`) | какую задачу поручили |
| AI State (`UnitAIController`) | какая задача сейчас основная |
| Perception (`AIPerceptionFrame`) | что юнит знает (AI-0) |
| Action (`UnitAIAction`) | Hold / Engage внутри текущей задачи |
| Navigation | как дойти (не этот проход) |
| Combat | как вести engagement (не этот проход) |

Только `UnitAIController` меняет `UnitAIState`. Не Vision, не Combat, не Nav.

---

## 3. Явные переходы (приказы)

```text
Idle    → Defense, Attack, Search, Flee
Defense → Attack, Retreat, Idle, Search, Flee
Attack  → Defense, Retreat, Idle, Search, Flee
Search  → Attack, Defense, Idle, Retreat, Flee
Retreat → Defense, Idle, Flee
Flee    → Idle
```

Тот же state повторным приказом: без Exit/Enter. Контекст на месте: `TrySetContext`.

`* → Flee` — явная команда, не auto-threat.  
Этап 3: **Search → Retreat** разрешён (отмена поиска). Решение Search (AI-1.10) не ретюнилось.

---

## 4. Проверки

EditMode: `UnitAIStateMachineTests`, `UnitAIPerceptionActionTests`, `UnitAISearchTests`, `UnitAISearchExecutionTests`.  
Play: `Tools/Tests/Run AI Tactical State (Play)` (`m_RunOnStart = false`).

`UnitAIController` на `Unit.prefab` **не** ставится. Smoke добавляет компонент на observer в Play.

### AI-1.1–1.8 CLOSED / VERIFIED (Play 2026-08-19 23:35:32)

```text
Idle → Defense → Attack → Retreat → Defense → Search → Attack → Flee → Idle
```

Контекст места заменяется на переходах. Повторный Idle без Exit/Enter. Nav / TargetSelector не вызывались.

**AI-1 skeleton FROZEN.**

### AI-1.9 CLOSED / VERIFIED (Play 2026-08-19 23:53:13)

Defense + HostileVisible → Defense + Engage. Attack + HostileVisible → Attack + Engage.  
Idle + HostileVisible → Idle + None. Unknown/Friendly → Hold. Lost contact на тот момент: Hold (Search — AI-1.10).

**AI-1.9 FROZEN.**

### AI-1.10 CLOSED / VERIFIED (Play 2026-08-20 00:08:24)

Defense + lost useful memory (`conf≈0.998`) → Search, `SearchPosition = LastKnown`.  
8 тиков Search без Advance: Memory не сдвинулась. Vision Advance 20 s → stale (`conf≈0.191`), Search resume **Defense**, LastKnown на месте.

**AI-1.10 FROZEN.** Combat execution **FROZEN**: `Combat_Engage_Execution.md`. Search locomotion **FROZEN**: `Search_Navigation_Execution.md`.

---

## 5. Следующий слой

Use of Force (AI-1A) — **отдельный** слой, FROZEN: `AI_UseOfForce_Policy.md` (Play PASS 107/0).  
Combat execution — **FROZEN**: `Combat_Engage_Execution.md` (Play PASS 31/0).  
Search locomotion — **FROZEN**: `Search_Navigation_Execution.md` (Play PASS 45/0, EditMode 18/0).  
Tactical navigation (Attack / Retreat / Flee) — **FROZEN**: `Tactical_Navigation_Execution.md` (Play PASS 36/0, EditMode 31/0).  
Дорожная карта — **FROZEN**: `Tactical_AI_Roadmap.md`. Следующее — **#6 Real game commands**.  
Squad, commander, utility, BT, auto-retreat, morale, cover, formations — не открывать раньше своих номеров.
