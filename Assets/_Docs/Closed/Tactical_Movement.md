# Tactical Movement

**Слой:** тактический **#14**.  
**Дизайн:** `Пехота_система_дизайн.md` §6.7.  
**Статус:** **CLOSED / FROZEN 27.08.2026.** 14.0–14.10 закрыты. EditMode **178/0**. Play `TacticalMovement_LAST.txt` **PASS 157/0** (23:52). Overlay ≠ Move ≠ Fire ≠ RoE. Production Attack/Defense/Retreat/Flee запрашивают **Normal**. #15 не открывать.

**#13 отвечает «где мне выгодно находиться?»** PositionDecision / Destination. Не Move.  
**#14 отвечает «как туда добраться?»** Route / waypoints. Не новое решение о позиции.

```text
Tactical AI
      ↓
TacticalRouteEvaluator
      ↓
RouteDecision
      ↓
TacticalNavigationExecutor     ← существующий
      ↓
UnitNavLocomotionDriver
      ↓
NavMesh
```

Не: `TacticalRouteEvaluator → Move()`.

Не писать второй locomotion stack. Не подменять `UnitNavLocomotionDriver`. Не вызывать NavMesh напрямую из тактического слоя.

Не менять замороженное:

```text
#1–#12
#13 Dynamic Cover (score, occupancy, peek, lean request)
Vision / Combat / RoE / A10 / G5 / Command Priority
Search 2.0 candidate walking
```

---

## 14.0 Tactical Movement Contract  ✅ закрыт внутри открытого #14

Зафиксировать:

```text
Destination = конечная цель
Route      = способ её достичь
```

Оба маршрута ведут в B:

```text
A → B                         Direct (baseline)
A → Cover1 → Cover2 → B       Waypoint (тип есть; генерация цепочки — 14.2)
```

14.0 overlay всегда строил Direct. Waypoint можно **задать** (Adopt). 14.1 выбирает среди кандидатов; Direct остаётся первым кандидатом.

### Законы (живые)

1. Overlay **не** двигает юнита. Только `TacticalNavigationExecutor` → `IUnitMoveCommand`.
2. Overlay **не** стреляет.
3. Overlay **не** меняет `UnitAIStateContext.Destination`.
4. Overlay **не** переписывает #13 PositionDecision.
5. NavMesh остаётся исполнителем walkable geometry / pathfinding / agent.
6. `TacticalRouteContext.Formation` — extension point, `Present = false`.
7. Moving lean — контракт #13.7; **когда** идти в lean — 14.8.

### Режимы

```text
Normal      Distance + TravelTime + MissionProgress
Tactical    Cover + Exposure + Danger + Mission + Distance
Emergency   режим есть; ImmediateThreat → replan gate (14.5). Under-fire reaction — 14.6.
```

Production Attack/Defense/Retreat/Flee пока запрашивают **Normal** (Direct обычно побеждает). Cover всё ещё не Move.

### Приёмка 14.0 (27.08.2026 10:55)

EditMode **6/0**. Play `TacticalMovement_LAST.txt` **17/0**. Direct dest intact. Overlay не Walk/Fire. Executor Walks hop.

---

## 14.1 Tactical Route Evaluation  ✅ закрыт внутри открытого #14

Лучший маршрут **больше не** «самый короткий NavMesh path».

```text
A
 ↓
generate route candidates
 ↓
evaluate (viability → score)
 ↓
select route
 ↓
TacticalNavigationExecutor
 ↓
B
```

### Кандидат

`TacticalRouteCandidate`: Route, Distance, TravelTime, Exposure, Cover, Danger, MissionProgress.

Пока нет: Formation, Suppression, WeaponDoctrine, CQB, Flank.

### Viability first

Пока не score: reachable / valid NavMesh / not blocked / destination valid. Иначе REJECT.

### Score (prototype, не freeze)

```text
RouteScore =
    MissionProgress
  + Cover
  − Distance
  − TravelTime
  − Exposure
  − Danger
```

Distance и TravelTime — разные факторы. Exposure = along route, не бинарное «виден / не виден». Нет глобальной threat grid: known contacts / directions / perception локально. Cover factor = «маршрут рядом с cover candidates», не cover-to-cover planner.

#13 destination (например C07) — цель маршрута. Цепочка C01→C04→C07 — **14.2**.

### Кандидаты

Direct `A → B` всегда baseline. Плюс 1–3 разнообразных альтернативы (lateral offsets). `MaxRouteCandidates` (Direct + ≤3). `RouteDiversityThreshold` — отсекает почти одинаковые. Tie-break: Score → Distance → candidate id.

Same input → same route (cache). Replan triggers — 14.5.

### Лог / SNAP

```text
ROUTE_QUERY      from=A to=B mode=Tactical
ROUTE_CANDIDATE  route=R2 …
ROUTE_SCORE      route=R2 distance=18 exposure=.21 cover=.73 mission=.84 score=8.7
ROUTE_SELECT     route=R2 reason=HighestScore
```

SNAP: `routeMode` `routeCandidate` `routeScore` `routeReason`.

### Тесты 14.1

A viability · B shorter wins · C Tactical covered beats short/open · D Normal shorter · E mission progress · F cover · G cap · H diversity · I determinism.

Play: Route Evaluation Arena (Normal → short/open, Tactical → longer/cover) + urban exposure (не Urban Wall Bias).

### Приёмка 14.1 (27.08.2026 11:29)

EditMode `[TacticalMovement] finished` **25/0** (14.0 contract 6 + 14.1 A–I).  
Play `TacticalMovement_LAST.txt` **PASS 39/0**: 14.0 (17) + viability/distance/Normal vs Tactical/mission/cover/cap/diversity/determinism/urban exposure/Direct baseline/#13 dest/Emergency/overlay не Walk/executor Walks selected hop.

Формулу score **не** фиксировать. Не смешивать с FrozenLayers #7–#11 и cover-меню.

#14 **не** CLOSED / FROZEN. 14.2 закрыт.

---

## 14.2 Cover-to-Cover Movement  ✅ закрыт внутри открытого #14

`#13` говорит, что C07 — хорошая позиция. `#14.2` может построить `C03 → C07` как промежуточную последовательность. Cover **не** вызывает Move.

```text
Direct route
  ↓
Is direct tactically acceptable?
  ├─ YES → Direct
  └─ NO → cheap filter (16→≤6) → 1–2 hop combinations → evaluate → select
```

Direct остаётся baseline. Cover-to-cover — средство уменьшить риск, не самоцель. Не все 16 кандидатов. `MaxIntermediateCandidates=6`, `MaxTacticalHops=3`, `MaxRouteEvaluations=6`.

Промежуточный cover: progress к destination, достижимость, снижение exposure. Occupied/reserved чужой слот отбрасывается (`TryReserve` текущего hop). Промежуточная reservation отпускается при уходе; final из #13 держится до arrival. Полноценный replan — 14.5 (контракт `NeedsReroute` есть).

Не Urban Wall Bias (это **14.3**). Production Attack/Defense всё ещё **Normal** → Direct.

Лог: `ROUTE_PLAN`, `ROUTE_HOP`, `COVER_HOP`.

### Приёмка 14.2 (27.08.2026 12:06)

EditMode `[TacticalMovement] finished` **40/0** (14.0 contract 6 + 14.1 A–I + 14.2 A–K).  
Play `TacticalMovement_LAST.txt` **PASS 52/0**: 14.1 (39) + Direct acceptable / cover hops / hop cap / skip reserved / release / final held / cache / overlay не Walk / executor Walks hop / determinism.

Формулу intermediate value **не** фиксировать. Occupancy board — один слот на юнит (не менять). Не смешивать с FrozenLayers #7–#11 и cover-меню.

#14 **не** CLOSED / FROZEN. 14.3 закрыт. 14.4 не открыт.

---

## 14.3 Urban Wall Bias  ✅ закрыт внутри открытого #14

Контекст маршрута в городе, не hug-wall и не CQB.

```text
Urban Environment
      ↓
Wall / Building proximity
      ↓
Exposure reduction
      ↓
Route preference
```

Direct остаётся baseline. 14.1/14.2 не переписываются. Стена — предпочтение, не самоцель.

`UrbanGeometryContext` выводится из #13 cover (position + normal) или явных `TacticalWallAnchor`. Нет hand-authored `ThisIsStreet`.

`WallProximity` — коридор (примерно 0.4–2.5 м от стены), не «чем ближе, тем лучше». 0 м у стены хуже, чем ~1.5 м.

Режимы: Normal — bias нет; Tactical — слабый secondary; Emergency — survival важнее коридора. Mission может перебить wall. Viability никогда не отменяется bias. Left/right выбираются теми же факторами (exposure / cover / distance).

Score (prototype, не freeze):

```text
UrbanRouteScore =
    BaseRouteScore
  + WallProximityBonus
  − OpenAreaExposurePenalty
```

WallBias не дублирует Cover01 (слот) и не подменяет Exposure (угроза вдоль пути). Open penalty только если urban context Present.

Лог: `wallProximity` `openExposure` `wallBias` `urban` в `ROUTE_SCORE`.

Не 14.4 (опасные участки выбранного пути). Не slice/peek/entry.

### Приёмка 14.3 (27.08.2026 16:31)

EditMode `[TacticalMovement] finished` **54/0** (14.0–14.2 + 14.3 A–M).  
Play `TacticalMovement_LAST.txt` **PASS 62/0**: 14.2 (52) + Direct when safe / wall when exposed / detour too long stays Direct / side choice / blocked not selected / corridor / overlay не Walk / determinism / cache / explainable wall vs exposure.

Формулу wall weights **не** фиксировать. Hug-wall не является законом. Не смешивать с FrozenLayers #7–#11 и cover-меню.

#14 **не** CLOSED / FROZEN. 14.4 закрыт. 14.5 не открыт.

---

## 14.4 Exposure-aware Traversal  ✅ закрыт внутри открытого #14

Средний `Exposure` недостаточен. Маршрут делится на bounded samples (`MaxExposureSamples=8`) и получает профиль:

```text
Average / Peak / ExposureCost(time×exposure) / TimeAboveThreshold / TimeExposed
```

Короткий опасный участок распознаётся и не отвергается автоматически. Длинный открытый коридор даёт больший duration. Unknown ≠ safe. Cover transition снижает risk перед ближайшим укрытием (dash).

14.1 average **не** переписывается. Peak / duration — слабый secondary, только Tactical/Emergency. Скорость NavMesh, stance, Fire — не здесь. Кэш: route + geometry + knowledge fingerprint; cache hit не пересчитывает profile.

Лог: `EXPOSURE_PROFILE` + `peak` / `timeAbove` / `exposureCost` в `ROUTE_SCORE`. Overlay красит сегменты по risk.

Не 14.5 replan. Не global threat map. Не per-sample physics raycast.

### Приёмка 14.4 (27.08.2026 19:40)

EditMode `[TacticalMovement] finished` **70/0** (14.0–14.3 + 14.4 A–K).  
Play `TacticalMovement_LAST.txt` **PASS 72/0**: 14.3 (62) + same-average profile / short dash / long open / short crossing / unknown ≠ safe / sample bound / overlay не Walk / cache / explainable extras.

Формулу peak/duration **не** фиксировать. Скорость и stance не менялись. Не смешивать с FrozenLayers #7–#11 и cover-меню.

#14 **не** CLOSED / FROZEN. 14.5 закрыт. 14.6 не открыт.

---

## 14.5 Event-driven Replanning  ✅ закрыт внутри открытого #14

**Не менять закрытые 14.0–14.4.** Overlay **не** Move. **Не** новый `UnitAIState`. Partial replanning не в первом проходе: если маршрут устарел — строить новый **от текущей позиции**.

```text
PLAN → MOVE → WORLD CHANGES → EVENT → CHECK CURRENT PLAN → REPLAN?
```

Не каждый кадр. Событие проходит **Replanning Gate**: изменение достаточно большое? Не: было событие?

### События (ограниченный список)

```text
NewHostile / EnemyMoved / ImmediateThreat
GeometryChanged (только region маршрута)
RouteBlocked / DestinationInvalid / CoverInvalid
MissionChanged   ← dest/приказ; семантика команды остаётся #11
```

`Enemy appears` ≠ Attack. Только: нужно ли сменить способ передвижения.

### Invalid ≠ Degraded

**Invalid** (обязательный replan): blocked, dest invalid, current hop unusable.  
**Degraded** (gate): exposure 0.31→0.33 Stay; 0.31→0.78 Replan.

### RouteCommitment + ReplanningCost

После выбора — committed. Мелочи не срывают маршрут.  
`NewRouteAdvantage > ReplanningCost` (prototype, как SwitchingCost). ImmediateThreat снижает порог и может обойти cooldown.

### Coalesce + cooldown

Несколько событий в одном окне → **одна** reevaluation → **один** replan.  
`ReplanCooldown` после reevaluation; emergency bypass. Время **не freeze**.

### Reservations + progress

Replan: cancel old route → `ReleaseUnit` obsolete hops → generate from **current position** → reserve new. Не возвращать на origin без причины.

### Reevaluate ≠ ReplaceRoute

Можно пересчитать и оставить тот же маршрут. Лог различает Keep / Replace.

### API / лог / overlay

`NotifyEvent` / `ShouldReplan?` / `RouteStatus = Committed | Replanning` (не AI state).  
`ROUTE_EVENT` `REPLAN_CHECK` `REPLAN`. Debug: CURRENT ROUTE, STATUS, LAST EVENT, REPLAN Yes/No, Reason.

### EditMode

| Id | Контракт |
|----|----------|
| A | нет события → нет replan (в т.ч. 100 ticks) |
| B | minor exposure → Stay |
| C | major exposure → replan |
| D | ImmediateThreat → immediate reassessment |
| E | route blocked → mandatory |
| F | geometry off-route → нет; on-route → replan |
| G | dest/command change → новый маршрут (#11 семантика не здесь) |
| H | 5 events / одно окно → ровно 1 replan |
| I | cooldown блокирует minor |
| J | ImmediateThreat обходит cooldown |
| K | старые reservation released |
| L | новые reserved |
| M | 70% пути → новый маршрут от текущей позиции |
| N | reevaluate, тот же маршрут → Keep, не Replace |

### Play

Enemy on route; minor ignore; geometry blocked; simultaneous → one; progress from here; same route Keep; reservation swap. 1000 events ≠ 1000 replans. Overlay не Walk.

### Не в 14.5

Suppression, fire & maneuver, flank, squad, weapon/rank, CQB, moving lean, per-frame planner, #14.6 under fire (stop / dash / emergency hop).

### Приёмка 14.5 (27.08.2026 22:06)

EditMode `[TacticalMovement] finished` **86/0** (14.0–14.4 + replan A–N).  
Play `TacticalMovement_LAST.txt` **PASS 84/0**: no-event / minor stay / enemy on route replace / geometry off-route ignore / blocked / 5 events → 1 replan / progress from current / same route Keep / reservation swap / 1000 events ≠ 1000 replans / overlay не Walk.

Пороги gate / cooldown / ReplanningCost **не** фиксировать. Partial replanning не делали. Не смешивать с FrozenLayers #7–#11 и cover-меню.

#14 **не** CLOSED / FROZEN. 14.6 закрыт. 14.7 не открыт.

---

## 14.6 Movement Under Fire  ✅ закрыт внутри открытого #14

**Не менять закрытые 14.0–14.5.** Overlay **не** Move. **Не** новый `UnitAIState`. **Не** Flee. Suppression / wounds / fire-on-the-move / sprint / moving lean — не этот слой.

Юнит **уже движется** и получает ImmediateThreat. Не останавливаться на месте и не слепо держать старый маршрут.

```text
Attack|Defense + Movement + UnderFire reaction
```

`ImmediateThreat ≠ always EmergencyCover.` Близкое укрытие впереди → Continue. Длинный открытый участок → Replan / EmergencyCover.

### Действие (первый проход)

```text
UnderFireDecision.Action = Continue | Replan | EmergencyCover | Hold
```

Скорость исполнителя не меняется автоматически. Overlay не пишет Attack/Defense Destination. #13 = WHERE, #14 = HOW.

| Ситуация | Предпочтение |
|----------|----------------|
| hop короткий + dest защищён | Continue |
| nearby emergency cover, маршрут плох | EmergencyCover |
| текущий route резко опаснее альтернативы | Replan |
| route заблокирован | Replan (14.5 mandatory) |
| открытый участок длинный | Cover / Replan |
| текущая позиция уже защищает | Hold |
| нет разумной альтернативы | Continue fallback — не Flee |

Направление угрозы сравнивается с направлением движения (уже известный вектор, без global threat map).

### 14.5

ImmediateThreat без явного under-fire snapshot по-прежнему идёт в gate (regression D). Continue / Hold / EmergencyCover при явном snapshot **не** запускают 14.5-поиск старого dest. Replan форсирует 14.5. Commitment / coalesce / cooldown — те же окна. 100 выстрелов ≠ 100 route searches.

Command (#11) важнее: Retreat dest → MissionChanged, 14.6 Reason=CommandOverride.

### Не в 14.6

Suppression, wounds, panic/morale, fire-and-maneuver, flank, group, CQB, weapon/rank doctrine, auto speed, moving lean.

### EditMode

| Id | Контракт |
|----|----------|
| A | cover 2m ahead → Continue, без 14.5 search |
| B | опасный route + alt → Replan |
| C | нет forward route + nearby cover → EmergencyCover |
| D | already protected → Hold, без лишнего search |
| E | короткий exposed hop + cover beyond → Continue |
| F | длинный exposed + alt → Replan |
| G | нет cover / нет alt → Continue fallback, не Flee |
| H | 10 threat events → 1 decision |
| I | cooldown, нет thrashing |
| J | Retreat command wins |
| K | reservation: old released, new reserved |
| L | 70% пути → ответ от текущей позиции |
| Golden | 1.5m to cover → Continue (don't panic) |
| Golden | open + cover 3m aside → EmergencyCover (don't suicide) |

### Play

Cover ahead Continue; dangerous → C1; alternative B→C; no cover fallback; fire+Retreat command wins; don't panic / don't suicide; 100 shots → 1 under-fire eval; cooldown; reservation swap; progress from here. Overlay не Walk.

Лог: `UNDER_FIRE` `decision=CONTINUE|REPLAN|EMERGENCY_COVER|HOLD` `reason=`. Debug: UNDER FIRE / hop / cover ahead / Decision.

### Приёмка 14.6 (27.08.2026 22:32)

EditMode `[TacticalMovement] finished` **101/0** (14.0–14.5 + under fire A–L / goldens).  
Play `TacticalMovement_LAST.txt` **PASS 97/0**: cover ahead Continue / dangerous EmergencyCover / alt Replan / no-cover fallback / Retreat command wins / don't panic / don't suicide / 100 shots → 1 eval / cooldown / reservation swap / progress from current / overlay не Walk.

Пороги NearbyCover / ShortHop / LongHop / HighExposure **не** фиксировать. Suppression / wounds / sprint не подключали. Не смешивать с FrozenLayers #7–#11 и cover-меню.

#14 **не** CLOSED / FROZEN. 14.7 закрыт. 14.8 не открыт.

---

## 14.7 Arrival / Tactical Position Acquisition  ✅ закрыт внутри открытого #14

**Не менять закрытые 14.0–14.6.** Overlay **не** Move. **Не** Fire. **Не** переписывает #13 PositionDecision. **Не** полный replan (это 14.5). Moving lean — 14.8.

Navigation Reached ≠ тактический acquire.

```text
Route → Movement → Navigation Arrival → Validate → Acquire | Reject
```

`Occupied` на доске #13.6 подтверждается только после фактического arrival, не после «я решил идти сюда».

### Результаты

```text
Acquired | Traversed | Invalid | Occupied | OutOfTolerance | Reevaluate | Rejected
```

`ArrivalFailureReason` минимум: InvalidPosition, Occupied, ReservationLost, GeometryChanged, OutOfTolerance, NavigationStopped. RouteStale — диагностика stale dest.

Промежуточный hop → Traversed (не `CurrentTacticalPosition`). Финал → ConfirmOccupied + `CurrentTacticalPosition`. Старая Occupied отпускается; не держать C03 и C07 Occupied одновременно (закон #13.6: один слот на юнит).

`PositionAcquireTolerance` — параметр, прототип = `CoverScoreMath.ArrivalSnapMeters` (0.6). **Не freeze.** Orientation — extension point (`pending` / `valid`), полноценный facing не в 14.7.

Attack после acquire остаётся Attack. SearchPoint ≠ Cover, пока #13 не назвал позицию. Arrival не генерирует global cover.

Лог: `ARRIVAL` `POSITION_ACQUIRE` `POSITION_RELEASE`. Debug: TARGET / DISTANCE / RESERVATION / GEOMETRY / RESULT.

`NotifyHopCompleted` остаётся hop FSM 14.2 (G1 без envelope). Production arrival — `NotifyTacticalArrival`.

### Не в 14.7

Suppression, wounds, moving lean, #15, полноценный replan, global cover generate, второй locomotion stack.

### EditMode

A envelope; B type; C GeometryVersion; D reservation; E occupancy + previous released; F intermediate hop; G final CurrentTacticalPosition; H Attack остаётся Attack; I stale route revalidate; J determinism. Overlay не Walk.

### Play

Normal acquire; too far; occupied by other; geometry change; intermediate hop; cancel → available; C01→C07 transition. Overlay не Walk.

### Приёмка 14.7 (27.08.2026 22:54)

EditMode `[TacticalMovement] finished` **120/0** (14.0–14.6 + arrival A–J).  
Play `TacticalMovement_LAST.txt` **PASS 109/0**: acquire+occupied / too far / occupied by other / geometry revalidate / intermediate hop / cancel release / C01→C07 transition / Attack remains / determinism / overlay не Walk.

`PositionAcquireTolerance` **не** фиксировать. Orientation / moving lean не подключали. Не смешивать с FrozenLayers #7–#13 и cover-меню.

#14 **не** CLOSED / FROZEN. 14.8 закрыт. 14.9 закрыт.

---

## 14.8 Moving Lean / Lean During Traversal  ✅ закрыт внутри открытого #14

**Не менять закрытые 14.0–14.7.** Overlay **не** Move. **Не** Fire. **Не** новый LeanController. **Не** CQB / pie slice. Существующий `ICoverLeanExecutor` / `CoverMovementLeanContract` / `UnitSpineLean`.

#13.7 = позиция → peek → lean.  
#14.8 = traversal → lean, **когда** это выгодно.

```text
Tactical Movement → MovingLeanDecision → CoverMovementLeanContract → existing executor
```

Угол создаёт opportunity, не automatic lean. Минимальная достаточная глубина. Lean временный: Normal → MovingLean → Normal. ImmediateThreat / Replan / Arrival отменяют Moving Lean. Wall bias не инициирует lean сам.

Лог: `MOVING_LEAN` `MOVING_LEAN_EXIT` (+ `LEAN mode=Moving`). Debug: opportunity / direction / depth / visibility / exposure / decision.

Пороги approach / corridor / MinLeanValue **не** freeze. Скорость executor не меняли.

### EditMode

A opportunity; B direction; C min depth; D transition; E corner crossing; F threat cancel; G replan cancel; H arrival exit. Overlay не Walk. Existing executor only. Не каждый кадр 2×3 raycasts.

### Play

Opportunity; no benefit; left/right; small depth; corner pass; threat; replan; arrival; coalesce; overlay не Walk.

### Приёмка 14.8 (27.08.2026 23:08)

EditMode `[TacticalMovement] finished` **140/0** (14.0–14.7 + moving lean A–H).  
Play `TacticalMovement_LAST.txt` **PASS 121/0**: opportunity / no benefit / left-right / small / corner pass / threat cancel / replan cancel / arrival exit / coalesce / overlay не Walk / existing executor.

Пороги approach / corridor / MinLeanValue **не** фиксировать. Новый LeanController не вводили. Скорость executor не меняли. Не смешивать с FrozenLayers #7–#13 и cover-меню.

#14 **не** CLOSED / FROZEN. 14.8 закрыт.

---

## 14.9 Performance / Tactical LOD  ✅ закрыт внутри открытого #14

**Не менять закрытые 14.0–14.8.** Overlay **не** Move. **Не** Fire. **Не** новый scoring. **Не** Cover / Exposure formula / Urban Bias. **Не** Rank / Group / CQB. LOD меняет **когда**, не **что**.

```text
Tactical Movement → TacticalLodDecision → TacticalUpdateScheduler → existing 14.0–14.8
```

Три тира: Full / Reduced / Background. Event (ImmediateThreat / IncomingFire / NewHostile / corner) будит Background → Full. Quiet: Full → Reduced → Background. Distance to player — один фактор, не единственный (far + under fire = Full; near + idle = Reduced).

Scheduler задаёт budget / stagger / priority. Не выбирает маршрут. First commit и event replan **не** режутся budget (иначе Direct на кадр, Wall позже). Background не считает moving lean, пока угол не подойдёт. Arrival и locomotion всегда.

Route/exposure reuse: `GeometryVersion` + `KnowledgeVersion` + evaluator cache. Shared Spatial Cache остаётся #13.

Пороги far/near/interval/budget **не** freeze. FPS / GC — baseline машины, не CLOSED-цифра.

### EditMode

A tier; B wake; C quiet decay; D Full vs Reduced = тот же route; E budget; F stagger; G emergency first; H route cache; I exposure cache; J lean pause/wake; K nav continues. Overlay не Walk. WITHOUT vs WITH LOD.

### Play

Tier A1–A3; wake; quiet; invariance; budget 100→20; stagger; priority; caches; lean pause/wake; nav continues; mix 10/20/40 + emergency; WITHOUT vs WITH; overlay не Walk.

### Приёмка 14.9 (27.08.2026 23:36)

EditMode `[TacticalMovement] finished` **160/0** (14.0–14.8 + LOD A–K / budget / stagger / priority / cache / benchmark).  
Play `TacticalMovement_LAST.txt` **PASS 139/0**: tier / wake / quiet / invariance / budget / stagger / emergency first / route+exposure cache / lean pause-wake / nav continues / mix / WITHOUT vs WITH / overlay не Walk.

Пороги far/near/interval/budget **не** фиксировать. Scoring / Cover / Exposure formula не меняли. First commit и event replan не режутся budget. Не смешивать с FrozenLayers #7–#13 и cover-меню.

#14 **не** CLOSED / FROZEN. 14.9 закрыт. 14.10 тогда не был открыт.

---

## 14.10 Final Acceptance / Freeze  ✅ CLOSED / FROZEN

**Не добавлять новую тактику.** Склеить 14.0–14.9. Overlay **не** Move. **не** Fire. **не** RoE. **не** #13 PositionDecision. LOD не меняет route result.

Golden: Destination → Route → cover hop → moving lean → minor event Keep → arrival Acquire / Occupied.

Границы: Cover overlay не вызывается из movement. Executor Walks hop. Quiet ticks без rebuild. Near-equal scores без осцилляции. 10 Full / 20 Reduced / 70 Background + budget.

Регрессия FrozenLayers #7–#13 — **отдельные** cover/regression меню, не этот runner.

### EditMode

A golden; B dest; C executor; D cover dest; E occupancy; F replan; G under fire; H arrival; I lean; J LOD same; K urban; L C2C lifecycle; M no thrash; N no per-frame; O boundaries; P budget mix; Q overlay не Walk.

### Play

Golden dest+reserve+lean+hop+stable+acquire; nav; under fire Continue + EmergencyCover; urban; no per-frame; no thrash; LOD mix; RoE; no cover pick; overlay не Walk.

### Приёмка 14.10 (27.08.2026 23:52)

EditMode `[TacticalMovement] finished` **178/0** (14.0–14.9 + Final A–Q).  
Play `TacticalMovement_LAST.txt` **PASS 157/0**: golden dest/reserve/lean/hop/stable/acquire; nav; Continue + EmergencyCover; urban; no per-frame; no thrash; LOD mix; RoE; no cover pick; overlay не Walk.

#14 **CLOSED / FROZEN.** Scoring / Cover / Urban / Exposure / Replan не трогать из #15. Если снайпер идёт слишком близко — Weapon Tactical Profile, не этот слой.

**#14B Readiness** — отдельный слой (`Readiness_State.md`), не reopen #14.

#15 не открывать.

### Обычный Play / арена (не меню тестов)

Сцена: `Assets/Scenes/SampleScene.unity`, корень `CombatTestArena_150x50`. Inspector-чеклист префаба, `TacticalWorld` и bake — `Пехота_зрение_бой_AI.md` (раздел «Тестовая площадка 150×50 м», 28.08.2026).

После `Polygone/Tactical AI/Install Arena Editor Wiring`: production `ResolvePointMovementHop` берёт mode из `InfantryTacticalProfile` (Tactical), cache/occupancy с `TacticalWorld`. Walk goal = cover slot только если есть #13 RepositionRequest / emergency dest; иначе Destination приказа. Overlay по-прежнему не Walk. Attack context не переписывается.

До Install: hop был **Normal** / Direct к центру, bind не вызывался.

Канонический trace в Editor Play (UnitActionLog): `CMD` → `AI` → `ROUTE_SELECT` → при угрозе `UNDER_FIRE` / `REPLAN_CHECK` → `ARRIVAL` / `POSITION_ACQUIRE` → SNAP `routeMode` `TACTICAL_LOD`. Quiet movement не должен писать `ROUTE_SELECT` каждый кадр.

Актуальный разбор живой арены: `Пехота_зрение_бой_AI.md` **§8.11** (`Infantry_20260828_204049`). Формулы #14 не менялись.

#15 не открывать.

---

## Не в #14

```text
Group / Formation behaviour / Squad / CQB
Flanking / Fire & maneuver / Suppression
Weapon doctrine / Rank behaviour
«всегда вдоль стен»          ← 14.3 = preference, не hard rule
второй NavMesh / второй driver
переписывание #13 WHERE
```

---

## Тесты

```text
── Current ──
Tools/Tests/Run Tactical Movement (EditMode)   #14 FROZEN 14.0–14.10
Tools/Tests/Run Tactical Movement (Play)       # TacticalMovement_LAST.txt

── Frozen ──
Tools/Tests/Run Dynamic Cover (EditMode)     #13
Tools/Tests/Run Dynamic Cover (Play)         #13
Tools/Tests/Run Regression (Play)            #7–#11
Tools/Tests/Run Regression (EditMode)        #10+#11
```
