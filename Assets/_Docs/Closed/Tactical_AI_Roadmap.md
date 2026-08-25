# Tactical AI Roadmap

**Статус канона #1–#6: FROZEN.** Perception A–F и **A10 CLOSED 24.08.2026**.  
**#7 ImmediateThreat + живой RoE: CLOSED 24.08.2026.**  
**#8 Combat events / sound в мир: CLOSED / FROZEN 25.08.2026.**  
**#9 Sound / Reports → AI snapshot: CLOSED / FROZEN 25.08.2026.**  
**Следующий слой: #10 Search 2.0 — не открыт.**

**Дизайн-документ системы:** `Пехота_система_дизайн.md` (v1.0) — канон **что должно получиться**. Каждый этап #N при открытии сверяется с соответствующим §6.x дизайн-дока (таблица §11). Полный backlog — `Пехота_дорожная_карта.md`.

Не открывать отряд, укрытия, CQB, командир, utility / HTN / GOAP / BT, пока не закрыт одиночный исполнительный контур #7–#12.

```text
Decision  →  Command  →  State  →  Execution
```

Высокоуровневый выбор задачи стоит **над** этой машиной, не вместо неё.

Канонический номер — колонка «#». Проектные этапы исполнения 1–4 (Identity / CombatIntent / Search / Attack-Retreat-Flee) соответствуют #2, #3, #4, #5.

Цикл открытого этапа:

```text
DESIGN → CONTRACT → IMPLEMENT → EDITMODE → PLAY → ARENA → LOG → FREEZE
```

Фаза **DESIGN** каждого этапа: сверка с `Пехота_система_дизайн.md` §6.x. Фаза **FREEZE**: не нарушать §9 дизайн-дока.

После FREEZE следующий этап не повод переписывать предыдущий, пока тест не доказал дефект закрытого слоя.

---

## Порядок

| # | Слой | Статус |
|---|------|--------|
| 1 | Vision | **FROZEN** |
| 2 | Identity | **FROZEN** |
| 3 | CombatIntent | **FROZEN** |
| 4 | Search locomotion | **FROZEN** |
| 5 | Attack / Retreat / Flee execution | **FROZEN** |
| 6 | Real game commands | **6.1–6.4 CLOSED** |
| 7 | ImmediateThreat + working RoE | **CLOSED 24.08.2026** |
| 8 | World combat events / sound | **CLOSED 25.08.2026** |
| 9 | Sound in AI perception | **CLOSED 25.08.2026** |
| 10 | Search 2.0 | не открыт |
| 11 | Command priority / cancellation | не открыт |
| 12 | Target selection + fire calibration | не открыт |
| 13 | Dynamic Cover | не открыт |
| 14 | Tactical Movement + Lean / Readiness | не открыт |
| 15 | Weapon role + Rank behaviour | не открыт |
| 16 | Group + CQB | не открыт |
| 17 | Under Fire + Wound | не открыт |
| 18 | Suppression | не открыт |
| 19 | Reposition | не открыт |
| 20 | Adaptive Attack | не открыт |
| 21 | Flank / alternative route | не открыт |
| 22 | Fire & Maneuver | не открыт |
| 23 | Grenades / special actions | не открыт |
| 24 | Commander | не открыт |
| 25 | Squad tactics | не открыт |
| 26 | High-level planner (HTN / GOAP / Utility / BT) | не открыт |

Ближайшая последовательность: **открыть #10 после явного старта**. Не прыгать к #13–#26. #10 в этом проходе не открывать.

Не сейчас (явно не открывать раньше своего номера): зоны RestrictedDefense как отдельный ранний трек, радио как отдельный трек, слияние 18 м и 500 м, патруль как новое состояние «на всякий случай».

---

## Фазы конечной системы

```text
ФАЗА I    Фундамент солдата           #1–#6          CLOSED
ФАЗА II   Живой боевой контур         #7–#12         #9 CLOSED; #10 не открыт
ФАЗА III  Индивидуальная тактика      #13–#15
ФАЗА IV   Группа и CQB                #16
ФАЗА V    Адаптивный бой              #17–#23
ФАЗА VI   Командир и мышление         #24–#26
```

Замороженные принципы: Perception ≠ Combat ≠ Tactical AI; AI не стреляет напрямую; Weapon Role = предпочтение, не запрет; укрытия динамические, без CoverPoints; lean подключается, не переписывается; ранг — competence одного AI, не пять AI; формация в бою может распасться на роли.

---

## Закрыто (#1–#5)

Vision, Identity, CombatIntent, Search locomotion, Attack/Retreat/Flee — слои не ретюнить, чтобы «AI стал умнее».

#5 закрыл одиночное движение:

```text
Attack    → destination → Walk → Stop   (остаёмся Attack)
Retreat   → destination → Walk → Stop   (остаёмся Retreat)
Flee      → destination → Walk → Idle
Search    → LastKnown snapshot → Walk → 15 m → Stop   (остаёмся Search)
```

Отмена: `Exit` → `Stop`, затем новый `Enter` → `Walk`.  
Defense якорь **не** входит в #5 и **не** является следующим шагом.

---

## #6 — Реальные игровые приказы

### 6.1 — Контракт **CLOSED**

Внешний приказ ≠ состояние. Игровой вход один: `UnitAIController.IssueCommand(TacticalCommand)`. Строгая таблица, без Idle/Defense bounce.

```text
Test / Scenario
        ↓
TacticalCommand
        ↓
IssueCommand
        ↓
UnitAIState + Context
        ↓
существующие handlers
```

`IUnitTacticalCommand` / `TryIssue` остаются отладкой. `TryApplyCommand` — внутренний приказ машины (same-state не трогает context).

Приёмка: EditMode `TacticalCommandContractTests`; Play `TacticalCommandContract_LAST.txt`.

Не в 6.1: RTS, UI, группы, RoE, новые клетки таблицы, правка executors.

### 6.2 — GameCommandService  **CLOSED**

Production-канал: `GameCommandService.Issue(unit, TacticalCommand)` → `ITacticalCommandReceiver` → `IssueCommand`. Нет AI → `NoAI` (контроллер не создаётся). Мёртвый юнит → `InvalidUnit`. Overlay/арена по-прежнему `TryIssue`.

Источник приёмки: `DebugGameCommandSource` (`Source=Game`). Не RTS.

Приёмка: EditMode `GameCommandServiceTests`; Play `GameCommandSource_LAST.txt`.

Не в 6.2: RTS, UI, мышь, группы, авто-AI на префабе.

### 6.3 — RTS / игровой ввод  **CLOSED**

Один input-слой, две аудитории: выбранные игроки и все живые Enemy (debug). Оба шлют ту же `TacticalCommand` через `GameCommandService.IssueMany`. Нет Group AI. Обычный RTS RMB-ход в `Normal` не заменяется.

Приёмка: EditMode `GameCommandInputTests`; Play `GameCommandInput_LAST.txt`.

Не в 6.3: box-select rewrite, Action Panel production UX, формации как Group AI, сеть, командир, подмена overlay, #7, авто-AI на префабе.

### 6.4 — Сквозная стабилизация  **CLOSED**

Приёмка всего command layer: замена задачи, Cancel, живой collect, изоляция сторон. Вход Play — `GameCommandInput.ConfirmPoint`, не `DebugGameCommandSource`. Combat isolation (не Fire / не цель); SHOT — #7. Defense не ходит.

Приёмка: EditMode `GameCommandLayerTests`; Play `GameCommandLayer_LAST.txt`.

#6 больше не открывать. A10 **CLOSED**. **#7 CLOSED.** **#8 CLOSED.** **#9 CLOSED.**

Не добавлять второй игровой путь рядом с `GameCommandService`. `TryIssue` игрой не вести.

---

## #7 — Рабочий RoE / ImmediateThreat — CLOSED 24.08.2026

`ImmediateThreat` ставит только `ImmediateThreatSource`. Hitscan: цель селектора (даже промах) или попадание по `DamageableTarget` от Hostile UnitTeam. TTL на компоненте. ThreatLevel / TargetSelector / RoE evaluator флаг **не** ставят.

Тесты: EditMode A `ImmediateThreatSourceTests` **10/0**, B `UseOfForcePolicyTests` **11/0**, C `ImmediateThreatRoeHandoffTests` **3/0**; Play D `ImmediateThreatLive_LAST.txt` **18/0**; E Use of Force Play **107/0**, Combat Engage **36/0**.

```text
aimed shot / hit
        ↓
ImmediateThreatSource (per-unit + TTL)
        ↓
ImmediateThreat
        ↓
RoE
        ↓
Allow / Deny Aim/Fire
```

**ImmediateThreat не вызывает Fire.** ImmediateThreat ≠ ThreatLevel.High. RoE Allow ≠ Fire.

Матрица UseOfForce в #7 **не ретюнится**. RestrictedDefense vs MissionCombat (зоны) — не этот этап.

Приёмка **PASS 24.08.2026:** EditMode A/B/C; Play D 18/0; E Use of Force 107/0 + Combat Engage 36/0. Hostile+SelfDefense без threat → NO FIRE; с ImmediateThreat → FIRE ALLOWED; isolation A/B; TTL.

---

## #8 — Combat Event / Sound — CLOSED / FROZEN 25.08.2026

Контракт: `Closed/Combat_Event_World.md`. Сверка с `Пехота_система_дизайн.md` §6.2.

Мир публикует факты. **event ≠ automatic knowledge.** Шина `CombatEventHub` **не** смешивается с `WorldSoundHub` (хаб звука вещает в `DetectionProcessor`).

```text
Hitscan / death
    ↓
CombatEvent (Gunshot / Hit / Impact / Death)
    ↓
ImmediateThreatSource читает Gunshot/Hit   ← внешний #7 без изменений
    ↓
#9: кто услышал → SoundContact / Search(SoundPosition)
```

Срез на диске: Gunshot, Hit, Impact, Death. Explosion — контракт позже (гранаты). Footstep остаётся в `WorldSoundHub`, не CombatEvent.

Инварианты:

```text
звук ≠ Vision
звук ≠ Observed
звук ≠ AimPoint
event ≠ automatic knowledge
```

Приёмка **PASS 25.08.2026:** EditMode `CombatEventTests` **10/0**; Play `CombatEvent_LAST.txt` **36/0**; Arena `Infantry_20260825_222738` (SHOT 79, THREAT 40 на боевых, **0 THREAT у Civilian**). Death — Play E7/E9. **Не открывать #9–#16** и не делать GROUND ASSAULT приёмкой этого слоя.

---

## #9 — Звук в кадре тактического AI — CLOSED / FROZEN 25.08.2026

Контракт: `Closed/Sound_Report_AI.md`. Сверка с `Пехота_система_дизайн.md` §6.2.

Отдельный этап от #8. CombatEventHub **не** становится sound-шиной.

```text
WorldSoundHub → DetectionProcessor → SoundContact → AIPerceptionFrame
CombatEventHub → ImmediateThreat          ← независимо
```

```text
AI perception snapshot
 ├─ Visual
 ├─ Sound
 └─ Reports
```

Канон (тестом, не импровизацией):

```text
Defense + hostile Gunshot/Explosion  → Search (SoundPosition)
Attack  + hostile Gunshot/Explosion  → Search (SoundPosition)
Idle    + heard hostile              → ничего
Search  + new sound                  → не дублировать Search
Attack  + VisibleNow                 → звук не сбрасывает Attack
```

Sound ≠ Observed / AimPoint / LastKnown / Fire. Visual Search = LastKnown. Sound Search = SoundPosition.  
Report-канал в снимке есть. Report → Search **не** в этом срезе.

Приёмка **PASS 25.08.2026:** EditMode `SoundInAiTests` **18/0**; Play `SoundInAi_LAST.txt` **20/0**; Arena `Infantry_20260825_231643` (SHOT 95; Search в SoundPosition `(-5.4, 1.5, 86.9)` при `hostileVis=0`; 10+10 боевых Search, **0 Civilian Search / 0 Civilian THREAT**). #10 не открывать. GROUND ASSAULT не приёмка #9.

---

## #10 — Search 2.0

Только после рабочего звука. Не новое состояние. Более содержательное исполнение **существующего** Search:

```text
LastKnown → uncertainty area
+ несколько SearchPosition
+ оценка freshness / confidence / visibility / danger
```

Базовый Search остаётся:

```text
LastKnown snapshot → Walk → 15 m → остановка → Found / stale
```

Сектора и «умный прочёс» не импровизировать сверх контракта этапа.

---

## #11 — Отмена / приоритет приказов

Единый контракт приоритетов. Порядок — **правила игры**, не заглушка в коде. Черновик для утверждения на старте этапа:

```text
Flee > Retreat > Attack > Search > Defense > Idle
```

Явно решить и покрыть тестами: новый приказ во время Search; Flee во время Attack; можно ли отменить Flee; повтор того же состояния; что с контекстом; локальный override (приказ задаёт намерение, не каждый физический шаг).

Это продолжение уже зафиксированной таблицы переходов, не вторая машина.

---

## #12 — Выбор цели и калибровка боя

Открывать только после #6–#11. TargetSelector до этого не ретюнить.

Измерять: кого выбирает, как часто меняет, как долго держит, как далеко стреляет, как часто по Unknown.

Затем: TargetScore, Aim, Fire discipline, Retain = ResolvedMaxRange.  
Окончательно решить связь `AI.EngageTarget` и `Combat.SelectedTarget` (сейчас расхождение сознательно допускается).

После freeze не менять perception или G6 только ради выбора целей.

---

## #13 — Dynamic Cover

Не Squad. Не hand-authored CoverPoints. Солдат читает геометрию: crouch cover / standing cover / partial / corner.

NeedCover → локальный query → фильтр → 3–10 кандидатов → оценка → cache.  
Emergency: ближайшее приемлемое. Tactical: смена только если `NewScore > CurrentScore + SwitchingCost` (принцип, не формула). Бой из укрытия: expose → fire → return → reassess.

Формулу CoverScore не фиксировать заранее.

---

## #14 — Tactical Movement + Lean / Readiness

NavMesh = физический путь. Tactical route = путь с меньшим риском. В городе вдоль стен, если опасно. Cover-to-cover, не через открытое поле.

Lean уже есть (Quick / Smooth / Deep) — подключить, не переписать. Movement lean вдоль стены.

Readiness: NotReady / LowReady / HighReady / HipFire / PointAim / Aiming. Точные пороги — на этапе.

---

## #15 — Weapon role + Rank behaviour

Weapon Role = preference, not restriction. Влияет на позицию, движение, укрытие, слот формации, агрессию, дистанцию — не только на Fire.

Ранги Recruit / Soldier / Corporal / Veteran / Elite: один AI + разный competence. Разница должна быть видна в поведении, не только в числах Marksmanship / Handling / Recoil.

---

## #16 — Group + CQB

Group / Leader / Members / Formation / Spacing / Role. Стартовые формации: Column, Line, Wedge, Compressed Column; позже CQB Stack. Лидер задаёт направление и задачу, не Transform-follow. В контакте формация может стать cover / fire / move / rear.

CQB рамка: один вход → stack → последовательный вход → сектора. Кто какой угол, кто первый, slice, dead space — **не решать до design cycle этапа**.

---

## #17–#23 — Adaptive combat

#17 Under Fire (return fire / cover / move-to-cover; rank и weapon влияют).  
#17B Wound: can fight → cover/continue, иначе emergency. Не медицина.  
#18 Suppression отдельно от ImmediateThreat и Wound.  
#19 Reposition только если новая позиция достаточно лучше.  
#20 Adaptive Attack: resistance → assess → continue или change plan.  
#21 Flank / alternative route.  
#22 Fire & Maneuver.  
#23 Grenade как ещё одно тактическое действие в том же ряду, не отдельный AI.

---

## #24–#26 — Commander / Squad / Planner

#24 Commander решает Attack / Defend / Flank / Withdraw / Search / Hold / Capture — не lean солдату X.  
#25 Squad: assault / support / cover / reserve.  
#26 Utility / HTN / GOAP / BT только здесь: **что попытаться сделать**, не как стрелять из-за угла.

```text
Decision → Command → State → Execution
```

Не заменяет #5–#11.

---

## Патруль

Старый patrol — параллельная система, не состояние AI. Не добавлять состояние `Patrol` автоматически.

Сначала проверить, хватает ли Idle / Defense / Attack плюс приказы #6. Отдельный режим — только если патруль действительно самостоятельная задача.

---

## Дыры, которые карта закрывает по очереди

| Дыра | Слой |
|------|------|
| ImmediateThreat мёртвый | #7 |
| Уровни RoE ведут себя одинаково | #7 |
| Звук/радио не в боевом мире | #8, потом радио не раньше #9 |
| AI-кадр без звука | #9 |
| Search слишком простой | #10 |
| Нет приоритета приказов | #11 |
| Патруль живёт отдельно | после #6, проверка на #11 |
| Выбор цели не калиброван | #12 |
| Нет динамических укрытий | #13 |
| Нет тактического движения / lean | #14 |
| Оружие и ранг не влияют на тактику | #15 |
| Нет группы / CQB | #16 |
| Нет адаптивного боя | #17–#23 |
| Нет командира / planner | #24–#26 |

---

## Правило открытия следующего слоя

Не начинать N+1, пока N не закрыт приёмкой (EditMode + Play, как #4/#5) и не помечен FROZEN.  
#8 **FROZEN**. #9 **FROZEN**. **#10 не начинать**, пока слой явно не открыт.  
Не открывать #13–#26, чтобы «сразу было видно группу». GROUND ASSAULT не является приёмкой #9.

---

## Milestones

| Веха | После | Результат |
|------|-------|-----------|
| M1 Autonomous Gunfighter | #7–#12 | видит, понимает угрозу, слышит, реагирует, стреляет по RoE |
| M2 Tactical Individual | #13–#14 | укрытие, тактический путь, угол, lean |
| M3 Competent Soldier | #15 | Recruit ≠ Veteran ≠ Elite по поведению |
| M4 Tactical Group | #16 | группа движется, входит, поддерживает |
| M5 Adaptive Combatant | #17–#22 | сопротивление → смена плана |
| M6 Tactical Force | #24–#26 | командир ведёт бой через группы |
