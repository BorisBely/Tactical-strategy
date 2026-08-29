# Readiness State

**Слой:** тактический **#14B**.  
**Дизайн:** `Пехота_система_дизайн.md` §6.7 (revision **1.2**).  
**Статус:** **OPEN** — 14B.0 ✅ **20/0**. 14B.1 ✅ **38/0** + **15/0**. 14B.2 ✅ **51/0** + **23/0**. 14B.3 ✅ **81/0** + **31/0**. 14B.4 ✅ **121/0** + **40/0**. 14B.5 ✅ **165/0** + **55/0**. 14B.6 ✅ **205/0** + **70/0**. 14B.7 ✅ **252/0** + **90/0**. #13 / #14 **не reopen**. #15 **не открывать**. #14B **не FROZEN**. Слой **#14C** — отдельный (`Threat_Direction_Knowledge.md`), 14C.0–14C.6 ✅ **49/0** + **20/0**, **14C.1 ✅** **40/0** + **22/0**, **14C.2 ✅** **38/0** + **19/0**, **14C.3 ✅** **43/0** + **18/0**, **14C.4 ✅** **36/0** + **18/0**, **14C.5 ✅** **39/0** + **18/0**, **не FROZEN**.

`ReadinessState` — независимая ось боевой готовности: от спокойного ношения до наведения. Не поза оружия. Не G6. Не задача AI.

Не заменяет:

```text
UnitAIState          Idle / Defense / Attack / Search / …
CombatIntent         Hold / Engage
G6                   Track / Aim / Fire
WeaponPoseState      NotReady / LowReady / HighReady / HipFire / PointAim / Aiming / …
Dynamic Cover #13
Tactical Movement #14
```

```text
Perception / Combat Events
          ↓
   ReadinessStimulus / ReadinessFrame
          ↓
   ReadinessController (RequestTransition)
          ↓
    ReadinessState
          ↓  (с 14B.2)
pose request → существующий CombatReadiness / ReadyHands
```

Зрение и звук **не пишут** `ReadinessState`. Только стимулы. Контроллер решает.

---

## 14B.0 Contract  ✅ закрыт внутри открытого #14B

### Состояния

```text
NotReady
Patrol
LowReady
HighReady
PreAim
Aim
```

Это **уровни**, не обязательный линейный workflow.

| Состояние | Смысл |
|-----------|--------|
| `NotReady` | Минимальная готовность. Предпочтительный спокойный уровень Recruit. |
| `Patrol` | Спокойная рабочая готовность. Предпочтительный спокойный уровень Soldier+. |
| `LowReady` | Настороженность без боевой цели. Оружие уже не «в отпуске». |
| `HighReady` | Высокая настороженность к контакту. Всё ещё **не** Aim и **не** Fire. |
| `PreAim` | Доступный переходный режим (корпус, точка интереса, CQB, lean — позже). **Не** обязательная ступень перед Aim. |
| `Aim` | Непосредственное наведение на боевую цель / aim point. |

**`Aim ≠ Fire`.** Физический выстрел по-прежнему только через боевой контур (G6 + RoE + discipline + pose `CanShootFromPose`). `ReadinessState.Aim` сам не создаёт `SHOT` и не вызывает Fire.

`ReadinessState.Aim` ≠ `EngagementDecision.Aim` ≠ `WeaponPoseState.Aiming`.

### Начальное состояние (initial policy)

Пока не вынесено в SO-профиль:

```text
Recruit     → NotReady
Soldier+    → Patrol
```

Ранг задаёт **предпочтительное спокойное состояние**, не отдельную AI-машину. Дальше — настраиваемый `ReadinessProfile`, не хардкод в `UnitAIController`.

### Стимулы

```text
None
HostileVisible
GunshotHeard
CombatContactLost   (14B.0; то же, что HostileLost)
CombatActivityExpired
HostileLost
CombatActivity
```

### Звук / боевое событие

Тот же факт мира, что уже кормит Perception / Search. Реакции **не смешивать**:

```text
Gunshot
 ├─ Perception / AI  → Search (канон Defense/Attack)
 └─ Readiness        → LowReady / HighReady
```

```text
NotReady / Patrol
        ↓ GunshotHeard
Recruit / Soldier → LowReady
Corporal+        → HighReady
```

Звук **не** ставит Aim. Звук **не** ставит Fire.

Если текущий уровень уже ≥ цели (например уже Aim) — не понижать.

### Визуальный контакт (ключ)

Подтверждённый визуальный противник:

```text
любое спокойное / промежуточное состояние
            ↓ HostileVisible
           Aim
```

Прямые переходы **разрешены**:

```text
NotReady → Aim
Patrol   → Aim
LowReady → Aim
HighReady→ Aim
PreAim   → Aim
```

**Не требуется** `Patrol → LowReady → HighReady → PreAim → Aim` на каждое обнаружение.

Разница уровней — **скорость подъёма** (`transitionDuration`), не обязательные промежуточные `CurrentState`.

Прототип длительностей (не freeze):

```text
NotReady → Aim   медленнее
Patrol   → Aim   медленнее
LowReady → Aim   быстрее
HighReady→ Aim   ещё быстрее
PreAim   → Aim   быстро
```

Переход — фаза, не голый assignment:

```text
StartState
TargetState
StartTime
Duration
Progress
```

Пока `Progress < 1`, `CurrentState` остаётся `StartState`. На завершении — сразу `TargetState`. Промежуточные уровни **не посещаются**.

### Decay / hysteresis

Нет видимого противника, нет актуального боевого события, нет других threat-событий → постепенное снижение. **Не мгновенно.**

Лестница вниз (по одному шагу):

```text
Aim
 ↓
PreAim
 ↓
HeardThreatState   (LowReady или HighReady по рангу)
 ↓
CalmState          (Patrol или NotReady по рангу)
```

```text
есть активность        → держим уровень, обновляем LastCombatActivityTime
активности нет         → ждём CalmDownDelay
timer истёк / Expired  → один шаг вниз
```

Запрещено одним шагом: `Aim → Patrol`. Короткое исчезновение цели не бросает `Aim → Patrol`.

### Кто решает

```text
HostileVisible / GunshotHeard / CombatContactLost / CombatActivityExpired
        ↓
ReadinessController
        ↓
ReadinessState + ReadinessContext
```

### Ранг

Не создаёт разные состояния. Подставляет параметры профиля:

```text
Recruit    calm=NotReady  heard=LowReady   reaction slower
Soldier    calm=Patrol    heard=LowReady   reaction normal
Corporal+  calm=Patrol    heard=HighReady  reaction faster
```

Вход реакции: существующий `UnitCombatStats.ReactionTime*` (подключение не в 14B.0). Не параллельная система реакции.

### Arm Fatigue — заготовка, не механика

В `ReadinessContext`:

```text
ArmFatigue = 0
ArmFatigueModifier = 1
```

Поля есть. **Сейчас не влияют** на target, duration, decay. Тест: 0 / 0.5 / 1 дают тот же результат. Усталость рук — отдельная механика позже (удержание Aim, стабильность, recoil) — не смена state machine.

### Существующий CombatReadiness

Сейчас: `Engage → Auto`, `Hold` не форсирует pose.

14B **не заменяет** этот слой в 14B.0. С 14B.2:

```text
ReadinessState → pose request → CombatReadiness / ReadyHands
```

CombatIntent / G6 остаются своими осями.

### Лог

Канал `READINESS` (`UnitActionLog`). Только события и переходы, не каждый Update:

```text
READINESS state=Patrol reason=Initial
READINESS transition=Patrol->LowReady reason=GunshotHeard
READINESS transition=LowReady->Aim reason=HostileVisible duration=...
READINESS transition=Aim->PreAim reason=CombatActivityExpired
READINESS transition=PreAim->LowReady reason=CalmDown
READINESS transition=LowReady->Patrol reason=CalmDown
```

### Законы 14B.0

1. Overlay / controller **не** стреляет. `RequestsFire == false` всегда.
2. Controller **не** вызывает `SetPose` / `RequestCombatReadiness` (это 14B.2).
3. Controller **не** меняет `UnitAIState` / `CombatIntent` / Cover / Route.
4. Perception не владеет `ReadinessState`.
5. Любой raise к более высокому уровню допустим, включая shortcut в Aim.
6. Decay только по лестнице, один шаг за срабатывание таймера.
7. `ArmFatigue*` не участвует в формуле 14B.0.

## План внутри открытого #14B

```text
14B.0 Contract              ✅ EditMode 20/0 (21:47)
14B.1 Stimuli & transitions ✅ EditMode 38/0, Play 15/0 (22:08)
14B.2 Pose request          ✅ EditMode 51/0, Play 23/0 (22:21)
14B.3 Combat × Perception   ✅ EditMode 81/0, Play 31/0 (22:40)
14B.4 Rank / reaction speed ✅ EditMode 121/0, Play 40/0 (23:04)
14B.5 Persistence / calm-down ✅ EditMode 165/0, Play 55/0 (23:29)
14B.6 Arm Fatigue             ✅ EditMode 205/0, Play 70/0 (10:18)
14B.7 Combat Integration      ✅ EditMode 252/0, Play 90/0 (10:33)
```

---

## 14B.1 Stimuli & Transition Logic  ✅ закрыт внутри открытого #14B

Мир → флаги → контроллер. Без поз оружия, без Fire, без правок `CombatReadiness`.

### RequestTransition

Не `state = Aim`. Контроллер создаёт переход:

```text
FromState / ToState / Reason / StartTime / Duration / Progress
```

`HostileVisible` может сразу запросить `Aim`. Промежуточные `CurrentState` не посещаются.  
`HighReady → Aim` быстрее, чем `LowReady → Aim`, быстрее, чем `Patrol → Aim`. Длительности в профиле:

```text
NotReadyToAimDuration
PatrolToAimDuration
LowReadyToAimDuration
HighReadyToAimDuration
PreAimToAimDuration
GunshotReadyState   (= HeardThreatState)
```

### Приоритет

```text
HostileVisible  >  CombatActivity (hold)  >  GunshotHeard  >  Calm / decay
```

`GunshotHeard` не понижает `Aim`. `CombatActivity` держит `LastCombatActivityTime` / `HasActiveCombatActivity`, сам не ставит Aim.

### Decay / hysteresis

`HostileLost` только запускает `CalmDownTimer`, не `Aim → Patrol`.  
После шага вниз таймер активности сбрасывается — ступени не каскадят в соседних тиках.

### Законы 14B.1

1. Всё из 14B.0 сохраняется.
2. `Tick(now, stimulus)` остаётся. `Tick(now, ReadinessFrame)` — комбинированные флаги.
3. `RequestsFire == false`. Нет `SetPose` / `RequestCombatReadiness`.
4. `UnitAIState` / Cover / Movement / G6 не меняются от Readiness.
5. `ArmFatigue*` по-прежнему не влияет.

### Не в 14B.1

Позы оружия, анимации, G6.Aim, Fire, TargetSelector, Cover, Movement, ArmFatigue logic, подключение `ReactionTime*` ранга (источник согласован, формула позже).

---

## Приёмка 14B.0 (28.08.2026 21:47)

EditMode **20/0** (`ReadinessContractTests`, `[Readiness] finished passed=20 failed=0 skipped=0`).

Переходы, rank init, Gunshot, HostileVisible shortcut, нет обязательных ступеней, Aim ≠ Fire, decay, hysteresis, ArmFatigue placeholder.

Play не в 14B.0. #14B **не FROZEN**.

## Приёмка 14B.1 (28.08.2026 22:08)

EditMode **38/0** (`ReadinessContractTests` + `ReadinessStimulusTests`, `[Readiness] finished passed=38 failed=0 skipped=0`).

Play **15/0** (`Readiness_LAST.txt`, stamp 22:08:12): init ranks, gunshot ranks, direct Aim, один `Patrol→Aim`, duration HighReady < LowReady < Patrol, decay ladder, hysteresis, re-trigger, priority, Aim ≠ Fire, fatigue placeholder, цикл spawn→gunshot→Aim→lost→decay→calm, mapper, world hook без смены UnitAIState / Cover / Movement / CombatReadiness.

Логическая машина стабильна. Канал `READINESS`. #14B **не FROZEN**.

---

## 14B.2 Pose Integration  ✅ закрыт внутри открытого #14B

```text
ReadinessState
      ↓
ReadinessPoseRequest
      ↓
CombatReadiness (исполнитель)
      ↓
ReadyHands / WeaponPoseState
```

| Readiness | Physical pose |
|-----------|-----------------|
| NotReady | NotReady |
| Patrol | NotReadyPatrol |
| LowReady | LowReady |
| HighReady | HighReady |
| PreAim | PreAim |
| Aim | Aiming |

Логический `Patrol → Aim` — один Readiness transition. Поза сразу `Aiming`; существующий blend ReadyHands может интерполировать. Промежуточные Readiness states не посещаются.

`Readiness.Aim ≠ G6.Aim ≠ Fire`. PoseRequest не вызывает Fire / G6 / SelectTarget.

Приоритет: LifeGate (Dead/Unconscious → NotReady) > Readiness. KO не оставляет HighReady/Aiming.

CombatIntent.Hold не ломается: можно держать Aiming-позу при Hold. Engage не ставит второй Auto-драйвер, если есть Readiness; только `NotifyCombatAlert`. Без AI Readiness: Stage 2 `Engage → Auto`.

Канал `READINESS_POSE` (не путать с `READINESS` / `G6` / `SHOT`).

ArmFatigue не влияет. #13/#14/#15 не открывать.

## Приёмка 14B.2 (28.08.2026 22:21)

EditMode **51/0** (`ReadinessContractTests` + `ReadinessStimulusTests` + `ReadinessPoseTests`, `[Readiness] finished passed=51 failed=0 skipped=0`).

Play **23/0** (`Readiness_LAST.txt`, stamp 22:21:20): прежние 15 логических + mapping, gunshot poses, цикл поз, Aim ≠ Fire ≠ G6, LifeGate, logical skip / physical Aiming, Hold vs Engage без второго Auto.

`Readiness.Aim ≠ G6.Aim ≠ Fire`. Каналы `READINESS` и `READINESS_POSE` различимы. #14B **не FROZEN**. 14B.3–14B.4 закрыты. #15 не открывать.

---

## 14B.3 Readiness ↔ Perception / Combat  ✅ закрыт внутри открытого #14B

Не расширяет состояния. Подключает готовый `ReadinessState` к реальному perception / combat activity.

```text
Observed + Relationship Hostile  →  HostileVisible  →  Aim
Gunshot (sound channel)          →  GunshotHeard    →  LowReady / HighReady
HostileLost                      →  decay (не snap)
ImmediateThreat / Hit / Gunshot event  →  CombatActivity hold
```

Прямой переход сохраняется: Patrol / LowReady / HighReady / PreAim / NotReady → Aim.

`LastCombatActivityTime` / `HasCombatActivity` (`HasActiveCombatActivity`) — единый контекст. Источники: HostileVisible, GunshotHeard, combat event. Пока нет Suppression / UnderFire / Wound.

```text
CombatIntent.Engage  ≠  Readiness.Aim
Readiness.Aim        ≠  Engage / G6.Aim / Fire
```

Engage сам не ставит Aim. Aim сам не создаёт Engage, G6 и SHOT. Search на звук остаётся независимым.

Приоритет:

```text
HostileVisible  >  CombatActivity  >  GunshotHeard  >  Calm / decay
```

LifeGate Unconscious / Dead: `ReadinessController.SetAllowed(false)` — новых READINESS transitions нет.

Каналы (событийные, не каждый кадр):

```text
READINESS_EVENT type=GunshotHeard
READINESS_TRANSITION Patrol->LowReady reason=GunshotHeard
READINESS_EVENT type=HostileVisible target=P12
READINESS_TRANSITION LowReady->Aim reason=HostileVisible
READINESS_DECAY Aim->PreAim reason=CombatActivityExpired
```

`READINESS` (14B.1) и `READINESS_POSE` (14B.2) сохраняются.

Vision / Identity / TargetSelector / G6 / Cover / Movement не менялись. ArmFatigue не влияет.

## Приёмка 14B.3 (28.08.2026 22:40)

EditMode **81/0** (`ReadinessContractTests` + `ReadinessStimulusTests` + `ReadinessPoseTests` + `ReadinessIntegrationTests`, `[Readiness] finished passed=81 failed=0 skipped=0`).

Play **31/0** (`Readiness_LAST.txt`, stamp 22:40:08): прежние 23 + live Patrol→HostileVisible→Aim (P25), цикл gunshot→Aim→decay→Patrol (P26), Attack не ставит Aim, Aim ≠ G6.Fire, LifeGate freeze, CombatActivity hold, Search независим.

Визуальный контакт реально переводит юнита в Aim. `CombatIntent.Engage ≠ Readiness.Aim ≠ Fire`. Каналы `READINESS_EVENT` / `READINESS_TRANSITION` / `READINESS_DECAY`. #14B **не FROZEN**. 14B.4–14B.5 закрыты. #15 не открывать.

---

## 14B.4 Readiness Balance & Rank Response  ✅ закрыт внутри открытого #14B

Ранг **не** меняет state machine. Меняет скорости:

```text
ToReadySpeed          NotReady/Patrol → LowReady/HighReady
ToAimSpeed            любой уровень → Aim
DecaySpeed            CalmDownDelayModifier (выше ранг — дольше держит)
RankReactionModifier  общий коэффициент (позже с ReactionTime)
```

Gunshot mapping без новых способностей:

```text
Recruit / Soldier → LowReady
Corporal+         → HighReady
```

Длительности — **отношения**, не freeze миллисекунд:

```text
Elite HighReady→Aim  <  Elite LowReady→Aim  <  Elite Patrol→Aim
Elite < Veteran < Corporal < Soldier < Recruit   (Patrol→Aim)
```

ArmFatigue поля есть и **не входят** в формулу. `UnitCombatStats` Marksmanship / Handling / Recoil / ReactionTime не дублируются в профиле.

Лог (событийный):

```text
READINESS_TRANSITION from=Patrol to=Aim rank=Veteran duration=... reason=HostileVisible profileDuration=... rankModifier=...
```

## Приёмка 14B.4 (28.08.2026 23:04)

EditMode **121/0** (`ReadinessContractTests` + `ReadinessStimulusTests` + `ReadinessPoseTests` + `ReadinessIntegrationTests` + `ReadinessBalanceTests`, `[Readiness] finished passed=121 failed=0 skipped=0`).

Play **40/0** (`Readiness_LAST.txt`, stamp 23:04:16): прежние 31 + P32–P40 (порядок Patrol→Aim, Elite High < Low < Patrol, gunshot ready, calm hold, пять рангов на одном стимуле, fatigue no-effect, log rank fields, Elite быстрее Recruit, decay ladder без смены ступеней).

Ранг меняет только ToReady / ToAim / Decay / RankReactionModifier. State machine, gunshot mapping и Instant (14B.0–14B.3) без изменений. ArmFatigue не в формуле. #14B **не FROZEN**. 14B.5 закрыт. #15 не открывать.

---

## 14B.5 Readiness Persistence & Calm-Down Balance  ✅ закрыт внутри открытого #14B

Rising и falling **не симметричны**. Подъём быстрый. Снижение — удержание, потом один шаг лестницы.

```text
Rising  = реакция на угрозу
Falling = Hold (минимум уровня) → Step Down (один rung)
```

Таймер Hold считается от `LastCombatActivityTime`. Новое боевое событие **продлевает** готовность, не делает `HighReady → Patrol → HighReady`.

```text
HostileVisible / GunshotHeard / CombatEvent
    ↓
refresh LastCombatActivityTime
    ↓
Cancel pending decay
    ↓
текущий уровень удерживается
```

`HostileLost` сам не понижает. Фаза A = Hold. Фаза B = один step-down после timeout.

```text
Aim → PreAim → HeardReady → Calm
```

Запрещено одним шагом `Aim → Patrol`. Повторный `HostileVisible` во время decay: сразу к Aim, без возврата через Ready.

Профиль (прототип Play-калибровки, не freeze мс):

```text
AimHoldTime           ~6 с
PreAimHoldTime        ~4 с
LowReadyHoldTime      ~10 с
HighReadyHoldTime     ~10 с
AimToPreAimDuration   ~0.4 с
PreAimToReadyDuration ~0.5 с
ReadyToCalmDuration   ~0.7 с
```

Полный `Aim → Calm` порядка **15–25 с**. `RankCalmDownModifier` масштабирует hold; структура лестницы одинакова для всех рангов. Instant (14B.0–14B.3): hold 1 с, step duration 0.

ArmFatigue поля есть и **не входят** в формулу.

Лог (событийный, hold только на смене фазы):

```text
READINESS_DECAY hold state=Aim remaining=4.2
READINESS_DECAY Aim->PreAim reason=CombatActivityExpired
READINESS_DECAY PreAim->LowReady reason=CalmDown
READINESS_DECAY HighReady->Patrol reason=CalmDown
```

## Приёмка 14B.5 (28.08.2026 23:29)

EditMode **165/0** (`ReadinessContractTests` + `ReadinessStimulusTests` + `ReadinessPoseTests` + `ReadinessIntegrationTests` + `ReadinessBalanceTests` + `ReadinessPersistenceTests`, `[Readiness] finished passed=165 failed=0 skipped=0`).

Play **55/0** (`Readiness_LAST.txt`, stamp 23:29:16): прежние 40 + P41–P55 (hold 1 с, ForRank step-down, gunshot refresh, reacquire PreAim→Aim, no-oscillation, общая структура decay, scenario A/B, LifeGate freeze). Instant 14B.0–14B.3 без изменений.

Подъём быстрый, снижение медленное: Hold от последней боевой активности, затем один rung. Refresh без дребезга. Логический AimTime / LastRequest.Duration без ArmFatigue. #14B **не FROZEN**. 14B.6 закрыт. #15 не открывать.

---

## 14B.6 Arm Fatigue  ✅ закрыт внутри открытого #14B

Рабочий `ArmFatigue` 0..1. Копится под нагрузкой, восстанавливается без нагрузки. **Не** `ReadinessState`. **Не** форсирует Aim→HighReady / HighReady→Patrol.

```text
Readiness / Aim / Fire → ArmFatigue → AimTime ↑, RecoilControl ↓, TurnToTargetTime ↑
```

Три независимых множителя (не один `FatiguePenalty`):

```text
FinalAimTime              = BaseAimTime × FatigueAimMultiplier
EffectiveRecoilControl    = RankRecoilControl × FatigueRecoilModifier
FinalTurnToTargetTime     = BaseTurnToTargetTime × FatigueTurnMultiplier
```

`FinalAimTime` — физический AimProgress (`UnitWeaponAimProgressController`), **не** логический raise `ReadinessMath.AimTransitionDuration`. Instant 14B.0–14B.5: load/recovery = 0 (`ArmFatigueProfile.Disabled`).

Load: `ArmFatigue += LoadRate(state) * dt`. Если огонь — `max(stateLoad, LoadRateFiring)`. Recovery, когда load ≈ 0. Смена Readiness **не** обнуляет fatigue. LifeGate Unconscious/Dead: freeze (не копить, не отдыхать). Rank modifiers и `ArmLoadMultiplier` сейчас **1**.

Не в 14B.6: sway, Detection, Vision, Threat, TargetSelector, G6, RoE, CoverScore, PathScore, скорость движения, урон оружия.

Лог (событийный, не каждый Update):

```text
ARM_FATIGUE threshold=0.25 / 0.50 / 0.75 / max / recovery-start
ARM_FATIGUE_EFFECT fatigue= aimMultiplier= recoilMultiplier= turnMultiplier=
```

## Приёмка 14B.6 (29.08.2026 10:18)

EditMode **205/0** (`ReadinessContractTests` + `ReadinessStimulusTests` + `ReadinessPoseTests` + `ReadinessIntegrationTests` + `ReadinessBalanceTests` + `ReadinessPersistenceTests` + `ReadinessFatigueTests`, `[Readiness] finished passed=205 failed=0 skipped=0`).

Play **70/0** (`Readiness_LAST.txt`, stamp 10:18:31): прежние 55 + P56–P70 (load order, firing max, recovery, clamp, AimTime↑, RecoilControl↓, TurnTime↑, Instant без накопления, ForRank копит, LifeGate freeze, не state, independence flags, ArmLoadMultiplier=1, лог не каждый тик, recovery-start).

Физический AimTime / RecoilControl / TurnTime. Логический raise и `ReadinessState` без изменений. Instant 14B.0–14B.5 load=0. #14B **не FROZEN**. 14B.7 закрыт. #15 не открывать.

---

## 14B.7 Readiness & Fatigue Combat Integration  ✅ закрыт внутри открытого #14B

Новой механики нет. Связка закрытых слоёв:

```text
Perception → ReadinessState → ArmFatigue → AimTime / TurnTime / RecoilControl → Combat
```

Fatigue **не** управляет Readiness. Логический `HostileVisible → Aim` без изменений. Измерения идут через боевые компоненты (`UnitWeaponAimProgressController`, `UnitWeaponAiming` yaw, `UnitWeaponRecoilController` / `WeaponRecoilMath.Recover`).

Цепочка логов на переход (не каждый тик):

```text
READINESS_TRANSITION ...
ARM_FATIGUE value=0.72
READINESS_EFFECT aimMultiplier= recoilMultiplier= turnMultiplier=
```

G6 / SHOT остаются своими каналами. Fatigue не меняет G6 decision, Target, RoE, Cover, Movement.

## Приёмка 14B.7 (29.08.2026 10:45)

EditMode **252/0** (`ReadinessContractTests` + `ReadinessStimulusTests` + `ReadinessPoseTests` + `ReadinessIntegrationTests` + `ReadinessBalanceTests` + `ReadinessPersistenceTests` + `ReadinessFatigueTests` + `ReadinessCombatIntegrationTests`, `[Readiness] finished passed=252 failed=0 skipped=0`).

Play **90/0** (`Readiness_LAST.txt`, stamp 10:33:24): прежние 70 + P71–P90 (Test A fresh vs tired AimTime/Turn/RecoilRecovery, 0 < 0.5 < 1, load Patrol < Ready < Aim < Firing, long firefight, ceasefire recovery, interrupt, logical Aim unchanged, AI/G6/RoE/Cover/Movement isolation, LifeGate, five ranks, chain log).

Связка Readiness + ArmFatigue + Combat доходит до живого контура. Новой механики нет. #14B **не FROZEN**. #15 не открывать.

