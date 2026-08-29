# Game Design

**Версия:** 1.0  
**Дата:** 29 августа 2026.  
**Назначение:** единственный канонический дизайн-документ проекта. Описывает **что это за игра**, **как устроены слои**, **что уже работает**, **что заморожено**, **что сознательно не решено**. Документ самодостаточен для анализа вне репозитория.

**Источник правды:** код, префаб `Assets/Prefabs/Characters/Unit.prefab`, сцена `Assets/Scenes/SampleScene.unity` (корень `CombatTestArena_150x50`), Play-отчёты регрессии. Если реализация расходится с этим файлом — либо меняется код, либо сначала revision этого документа (версия + дата + что изменилось), не «тихо в коде».

**Файл:** `Assets/_Docs/Game_Design.md`.  
**Прежние имена (сняты):** `Дизайн_игры.md`, `Пехота_зрение_бой_AI.md`, `Пехота_система_дизайн.md`, `Пехота_дорожная_карта.md` и отдельные контракты слоёв. Их содержание сверено с кодом 29.08.2026; устаревшее не переносилось.

**Логи не входят в этот файл.** Сырые прогоны: `Assets/_Docs/Logs/Runtime/` (папки `Infantry_*`) и `Assets/_Docs/Logs/Tests/` (`*_LAST.txt`). Диагноз Play = этот документ + папка сессии.

**Пометки пробелов:** блоки `заполним позже` — тема есть в коде или в замысле, но игрового/продуктового описания ещё нет. Не выдумывать.

---

## Как читать

Три контура **не смешиваются**. Диагностика всегда ищет **первый разрыв** цепочки.

```text
плохо увидел  ≠  плохо понял кто это  ≠  плохо выбрал  ≠  плохо решил стрелять  ≠  плохо пошёл искать
```

```text
Perception  ≠  Combat  ≠  Tactical AI
AI никогда не стреляет напрямую
Engage ≠ Fire
ImmediateThreat ≠ Fire
RoE Allow ≠ Fire
звук ≠ Observed ≠ AimPoint ≠ Fire
приказ задаёт намерение, не каждое физическое действие
```

Цикл этапа: DESIGN → CONTRACT → IMPLEMENT → EDITMODE → PLAY → ARENA → LOG → FREEZE.  
После FREEZE слой не переписывают ради «умнее AI», пока тест не доказал дефект **этого** слоя.

---

## Что это за игра

**заполним позже:** жанр, фантазия игрока, кампания / режимы, победа и поражение, тон, сеттинг, мультиплеер.

**Что уже ясно из работающей системы (не маркетинг):**

- Игрок и AI используют **одного и того же солдата**: те же лучи, те же Track / Aim / Fire, та же отдача, та же дисциплина огня, те же позы и lean. Разница — в **решениях** (задача, RoE, тактика, позиция), не в читерском боевом контуре.
- Сейчас живой полигон — тактический CQB-бой пехоты на арене 150×50 м плюс отдельный контур техники и RTS-маршрутов игрока. Это ещё не «готовый продукт», это исполняющий солдат и связанный с ним мир.
- Противник после отпора **должен** переоценивать ситуацию (фазы V–VI). Сейчас одиночка идёт, занимает слот и стреляет по RoE; штурм объекта vs оборона объекта vs захват — нет.

---

## Статус слоёв (снимок 29.08.2026)

| Блок | Статус |
|------|--------|
| Зрение, память, идентификация (perception этапы 1–18) | **CLOSED / FROZEN** — Final Perception **PASS 49/0** |
| Боевой контур G6 + оружие + hitscan | **FROZEN** |
| A10 — стрельба и отдача (`RecoilOffset`) | **CLOSED 24.08.2026** — G/H/RecoilContract **PASS** |
| CombatIntent (этап 2) | **FROZEN** — Play **PASS 31/0** |
| Search / Attack / Retreat / Flee (этапы 3–4) | **FROZEN** |
| Игровые приказы (этап 6.1–6.4) | **CLOSED** |
| Use of Force / RoE | **FROZEN** — Play **PASS 107/0**; ImmediateThreat **#7 CLOSED 24.08.2026** |
| Combat events / sound в мир (#8) | **CLOSED 25.08.2026** — Play **36/0** |
| Звук и доклад в AI snapshot (#9) | **CLOSED 25.08.2026** — Play **20/0** |
| Search 2.0 (#10) | **CLOSED / FROZEN 26.08.2026** — Play **22/0** |
| Command priority (#11) | **CLOSED / FROZEN 26.08.2026** — EditMode **18/0**; Play **18/0** |
| Target + fire calibration (#12) | **CLOSED / FROZEN 26.08.2026** — EditMode **18/0**; Play **26/0**; регрессия **62/0** + **114/0** |
| Dynamic Cover (#13) | **CLOSED / FROZEN 27.08.2026** — выбор / occupancy / CoverScore не reopen. EditMode **169/0**; Play `CoverIntegration_LAST.txt` **18/0** |
| **#13.2B** Extended Cover Position Bake | **CLOSED** как точечный bake — детекторы остаются библиотекой. Сцена печёт **#13.2C** |
| **#13.2C** Protection Geometry Bake | **OPEN** — геометрия укрытия, не точки и не CoverType. Runtime позиция = **#13.3, не открывать** |
| Tactical Movement (#14) | **CLOSED / FROZEN 27.08.2026** — EditMode **178/0**; Play **157/0** |
| Occupy слота на массовой арене | **работает, не FROZEN** |
| Readiness State (#14B) | **OPEN** — 14B.0–14B.7 ✅ EditMode **252/0**, Play **90/0**. Слой **не FROZEN** |
| Threat Direction Knowledge (#14C) | **OPEN** — 14C.0–14C.6 ✅ **49/0** + **20/0**; 14C.1 ✅ **40/0** + **22/0**; 14C.2 ✅ **38/0** + **19/0**; 14C.3 ✅ **43/0** + **18/0**; 14C.4 ✅ **36/0** + **18/0**; **14C.5 ✅** **39/0** + **18/0**. Слой **не FROZEN** |
| Ранги (5 пресетов навыков) | Готово на префабе (`UnitCombatStats`); поведенческий слой — **#15B, не открывать** |
| Оружие по дистанции (классы, E, WorkingRange) | Готово, A3/A9/A10 CLOSED; тактические роли — **#15A, не открывать** |
| Группа / CQB #16, адаптивный бой #17–#23, командир/planner #24–#26 | не начинать раньше своего номера |

```text
████████████████████  ФАЗА I   #1–#6 + Perception + A10     CLOSED
████████████████████  ФАЗА II  #7–#12                       CLOSED / FROZEN
████████████████████  #13 Dynamic Cover                     CLOSED / FROZEN 27.08
▓░░░░░░░░░░░░░░░░░░░  #13.2C Protection Geometry Bake       OPEN (геометрия, не позиции)
████████████████████  #14 Tactical Movement                 CLOSED / FROZEN 27.08
▓░░░░░░░░░░░░░░░░░░░  #14B Readiness                        OPEN (подэтапы ✅, не FROZEN)
▓░░░░░░░░░░░░░░░░░░░  #14C Threat Direction                 OPEN (подэтапы ✅, не FROZEN)
░░░░░░░░░░░░░░░░░░░░  #15 Weapon role + Rank behaviour      не открыт
░░░░░░░░░░░░░░░░░░░░  ФАЗА IV  #16 Group + CQB
░░░░░░░░░░░░░░░░░░░░  ФАЗА V   #17–#23 Adaptive
░░░░░░░░░░░░░░░░░░░░  ФАЗА VI  #24–#26 Commander / Planner
```

**Сейчас открыто:** #13.2C, #14B и #14C. **#13.3 не открывать.** **#13 выбор/occupancy и #14 не reopen.** CoverDirectionScore / **#15 не открывать.**

---

## Философия и конечное видение

**Цель** — тактическая сила уровня «один солдат = исполняющий контур», не «умный бот в вакууме». Каждый слой отвечает на свой вопрос; слои соединяются узкими контрактами.

Конечный результат (не вся текущая реализация):

- солдат **видит и понимает** ситуацию (восприятие, память, звук, доклады);
- **получает задачу** и умеет её менять при сопротивлении;
- **выбирает позицию** (укрытие, маршрут, готовность, lean);
- **ведёт бой** через общий combat executor;
- **адаптируется**, когда первый план не сработал;
- на высоком уровне **командир** распределяет задачи между группами, а planner выбирает **что попытаться сделать**, не как нажать спуск.

Игрок не должен выучить «болванчиков на точках».

Принцип разработки: сначала заморозить слой приёмкой (EditMode + Play), потом открывать следующий. Не чинить AI ретюном зрения. Не добавлять BT / GOAP / HTN, пока не работает цепочка `Decision → Command → State → Execution` для одного юнита. Planner — только **#26**.

---

## Центральный цикл

```text
                 МИССИЯ / ПРИКАЗ
                        ↓
              СИТУАЦИЯ / ЗНАНИЕ
         (Perception + Sound + Reports + Memory)
                        ↓
                    ТАКТИКА
         (State, RoE, Search, Cover, Movement, Group)
                        ↓
                   ПОЗИЦИЯ
         (укрытие, маршрут, exposure, fire lane)
                        ↓
                  ГОТОВНОСТЬ
         (ReadinessState: NotReady … Aim;
          не поза оружия, не G6 Aim/Fire)
                        ↓
                    COMBAT
         (Target, G6, Discipline, Weapon, Hitscan)
                        ↓
                    ACTION
              (выстрел, движение, граната…)
                        ↓
              МИР ИЗМЕНИЛСЯ
                        ↓
                  PERCEPTION
```

Приказ задаёт **намерение**, не каждый физический шаг. Локальные решения (укрыться, lean, сменить позу) допустимы в рамках задачи и RoE.

---

## Три контура

```text
┌─────────────────────────────────────────────────────────┐
│ PERCEPTION — что солдат знает о мире                    │
│   Vision, Detection, Memory, Identity, Relationship     │
│   Sound, Ally Report → Perception Snapshot              │
│   Не ходит. Не стреляет.                                │
└─────────────────────────────────────────────────────────┘
                        ↓ read-only snapshot
┌─────────────────────────────────────────────────────────┐
│ COMBAT — как солдат стреляет, если разрешено            │
│   TargetSelector → G6 Track/Aim/Fire → Discipline       │
│   → Weapon → Hitscan / Projectile                       │
│   На префабе всегда. Не читает приказы AI.              │
└─────────────────────────────────────────────────────────┘
                        ↑ gates (RoE, CombatIntent, Readiness pose)
┌─────────────────────────────────────────────────────────┐
│ TACTICAL AI — задача, намерение, тактика                │
│   UnitAIState, Commands, Hold/Engage, RoE, Search       │
│   Cover, Movement, Readiness, ThreatDirection, Group    │
│   Не жмёт спуск.                                        │
└─────────────────────────────────────────────────────────┘
```

**Префаб.** Боевой контур на `Unit.prefab` всегда. `UnitAIController` **стоит на префабе и выключен** (`m_Enabled: 0`). Арена включает его у Player/Enemy; Neutral остаётся без AI. Спавнер **не** делает `AddComponent`. `CombatReadinessController` добавляется, когда AI стартует.

### Нерушимые равенства

```text
увидел           ≠  выбран
выбран           ≠  можно целиться
можно целиться   ≠  решение Fire
решение Fire     ≠  выстрел ушёл
разрешена сила   ≠  стрелять
Engage (AI)      ≠  Fire (G6)
Hold (AI)        ≠  выключить Combat (Track остаётся)
ImmediateThreat  ≠  ThreatLevel.High
ImmediateThreat  ≠  Fire
RoE Allow        ≠  Fire
звук             ≠  Observed
звук             ≠  AimPoint
звук             ≠  Fire
доклад           ≠  увидел
AI.EngageTarget  ≠  Combat.SelectedTarget   (#12: наблюдаемый факт, не auto-merge)
Readiness.Aim    ≠  G6.Aim  ≠  Fire  ≠  поза Aiming
ThreatDirection  ≠  точная позиция врага
Cover overlay    ≠  Move  ≠  Fire
Destination      ≠  Route
Acquired         ≠  Occupied
Nav Reached      ≠  Cover acquired
Weapon Role      =  preference, not restriction
```

---

## Правило работы

Нумерация **#1–#16 не ломается**. Она отражает зависимости. #13–#16 расширяются подэтапами. После #16 идут **#17–#26** — продолжение той же машины.

Старые ярлыки «#13 отряд / #14 командир / #15 укрытие / #16 planner» были заглушками. После закрытия одиночного контура (#12) содержание уточнено: сначала индивидуальная тактика, затем группа. Командир и planner — #24–#26.

| # | Слой | Статус |
|---|------|--------|
| 1 | Vision | **FROZEN** |
| 2 | Identity | **FROZEN** |
| 3 | CombatIntent (Hold / Engage) | **FROZEN** |
| 4 | Search locomotion | **FROZEN** |
| 5 | Attack / Retreat / Flee | **FROZEN** |
| 6 | Game commands 6.1–6.4 | **CLOSED** |
| 7 | ImmediateThreat + живой RoE | **CLOSED 24.08.2026** |
| 8 | Combat events / sound в мир | **CLOSED 25.08.2026** |
| 9 | Звук и доклад в AI snapshot | **CLOSED 25.08.2026** |
| 10 | Search 2.0 | **CLOSED / FROZEN 26.08.2026** |
| 11 | Приоритет / отмена приказов | **CLOSED / FROZEN 26.08.2026** |
| 12 | Калибровка цели и огня | **CLOSED / FROZEN 26.08.2026** |
| 13 | Dynamic Cover (13.0–13.8) | **CLOSED / FROZEN 27.08.2026** |
| 13.2B | Extended Cover Position Bake | **OPEN** (генератор; не поведение юнита) |
| 14 | Tactical Movement + Lean (14.0–14.10) | **CLOSED / FROZEN 27.08.2026** |
| 14B | Readiness State | **OPEN** (14B.0–14B.7 ✅) |
| 14C | Threat Direction Knowledge | **OPEN** (14C.0–14C.6 ✅; 14C.1–14C.5 ✅) |
| 15 | Weapon role + Rank behaviour (15A, 15B) | не открыт |
| 16 | Group + CQB (16A, 16B) | не открыт |
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

### Шесть фаз

| Фаза | Этапы | Смысл |
|------|-------|--------|
| **I** | #1–#6 + Perception + A10 | Честный одиночный солдат: видит, понимает, двигается, стреляет, принимает приказ |
| **II** | #7–#12 | Живой боец: угроза, RoE, звук в мире и в AI, поиск, приоритет приказов, калибровка цели |
| **III** | #13–#15 | Индивидуальная тактика: укрытие, движение, lean, готовность, направление угрозы, роль оружия, competence ранга |
| **IV** | #16 | Группа и CQB |
| **V** | #17–#23 | Адаптивный бой |
| **VI** | #24–#26 | Командир, тактика отряда, planner |

### Контрольные milestones

| Веха | После | Наблюдаемый результат |
|------|-------|------------------------|
| M1 Autonomous Gunfighter | #7–#12 | видит, понимает угрозу, слышит, реагирует, выбирает, стреляет по RoE |
| M2 Tactical Individual | #13–#14 | укрытие, тактический путь, угол, lean, ready state |
| M3 Competent Soldier | #15 | Recruit ≠ Veteran ≠ Elite по поведению |
| M4 Tactical Group | #16 | группа движется, входит, распределяется, поддерживает |
| M5 Adaptive Combatant | #17–#22 | сопротивление → смена плана |
| M6 Tactical Force | #24–#26 | командир ведёт бой через группы |

### Соответствие этапа дизайн-документу

Перед открытием #N — сверка с разделом ниже. Перед FREEZE — «не нарушает замороженные принципы».

| Этап | Раздел | Критерий |
|------|--------|----------|
| #7 | ImmediateThreat и RoE | флаг живой; RoE различаются; ≠ Fire |
| #8 | События и звук | события в мире; event ≠ knowledge |
| #9 | События и звук | Sound/Report в snapshot; Defense/Attack + hostile → Search |
| #10 | Search 2.0 | без нового AI-состояния Investigate |
| #11 | Приоритет приказов | приказ ≠ каждый шаг |
| #12 | Цель и огонь | калибровка выбора; не крутить G6/perception |
| #13 | Dynamic Cover | shared cache; local query; Cover ≠ Move |
| #13.2B | Extended Cover Bake | детекторы Edge/Opening/Window/Corner; точечный bake сцены снят |
| #13.2C | Protection Geometry Bake | Surface/Boundary/Obstacle; Play читает bake; юнит не выбирает |
| #14 | Tactical Movement | Destination ≠ Route; production Normal |
| #14B | Readiness | независим; HostileVisible → Aim без обязательных ступеней; Aim ≠ Fire |
| #14C | Threat Direction | knowledge ≠ Cover/Move/Aim; overlay ≠ CoverScore |
| #15 | Weapon / Rank | preference; один AI + competence |
| #16 | Group / CQB | рамка без преждевременных деталей CQB |
| #17–#23 | Adaptive | Threat / Suppression / Wound раздельно |
| #24–#26 | Commander | командир ≠ микро; planner ≠ combat |

---

## Замороженные принципы

Считать не обсуждаемым до явного revision этого документа:

```text
три контура Perception / Combat / Tactical AI
AI не стреляет напрямую
Engage ≠ Fire, Hold не выключает Track
Weapon Role = preference, not restriction
Dynamic cover, no pre-authored CoverPoints
crouch cover + standing cover + partial/corner
lean подключается, не переписывается
NavMesh + Tactical Movement (два разных вопроса)
формация может распасться в бою
один AI + rank competence (не 5 AI)
Readiness ≠ WeaponPose ≠ G6; Aim готовности ≠ Fire; HostileVisible может сразу в Aim
ThreatDirection ≠ точная позиция врага; knowledge ≠ Cover/Move/Aim
гранаты после adaptive combat, как tactical action
planner только на #26
конверт зрения 150/300 и замороженная Q
```

## Что сознательно не решаем сейчас

Точные формулы появляются на своём этапе, не из раннего митинга:

```text
точная формула CoverScore
точная формула CoverDirectionScore относительно ThreatDirection
точная формула TacticalPathScore
пороги поз оружия (HipFire / PointAim / Aiming) как баланс
секунды Aim-transition / decay (#14B прототип, не freeze)
правила CQB entry, slice-the-pie, dead space
распределение углов в комнате
состав и переключение формаций в деталях
поведение Squad Leader в частностях
модель suppression (числа)
модель тяжести ранения
flanking utility formula
мораль
детальная система гранат
выбор HTN vs GOAP vs BT
вертикальный CQB / этажи
```

---

## Пробелы продукта (заполним позже)

Эти системы в коде есть кусками или задуманы дорожной картой, но **игрового описания ещё нет**. Не заполнять выдумкой.

| Тема | Что есть сейчас | Что заполнить |
|------|-----------------|---------------|
| Фантазия / жанр / кампания | нет | кто игрок, зачем бой, режимы |
| UI / MissionPrep | скрипты UI есть | экраны, поток миссии |
| Инвентарь и обвес как продукт | данные оружия и модулей живые | экономика, прогрессия, UX |
| Управление игроком | RTS-маршрут + арена-спавн + HUD приказов | канон камеры, TPS vs RTS, кто «главный» |
| Гранаты как тактика | `UnitGrenadeThrowController` на префабе | #23 |
| Ранения сверх Alive/Unconscious/Dead | health / consciousness / ragdoll | #17B |
| Аудио как продукт | шины знания + клипы | микс, голос, радио |
| Техника как геймплей | навигация + CombatVehicleSystem + VisionSource пассажир/турель | посадка, бой из машины, AI водителя |
| Карты кроме арены 150×50 | полигон / G-regression | большие карты, этажи |
| Мультиплеер | нет | — |

---

# ЧАСТЬ A. ВОСПРИЯТИЕ

Зрение отвечает: **что этот солдат знает о мире прямо сейчас**. Не куда идти. Не стрелять ли. Знание **локально**.

В логе: `SCAN` (бюджет кадра), `VISION` (контакт). Пропуск LOD ≠ потеря цели.

## A.1 Кого сканируют

Сторона мира: Player / Enemy / Neutral. Реестр регистрирует всех. Кандидаты:

- игрок смотрит только врагов;
- враг смотрит только игроков;
- нейтралы регистрируются и **никогда не попадают в кандидаты** (нейтрала нельзя обнаружить этим сканом).

Мишени тира видит только игрок. Это фильтр существования, не «кто враг по мнению солдата».

## A.2 Откуда смотрит

Точка глаз: 1.6 м от корня, либо прицел оружия в high-ready. Источник сам не знает целей.

Источники зрения (Stage 13 **CLOSED PASS 35/0**): `InfantryEye` / `Passenger` / `Turret`. Пассажир из окна = пехота на земле (глаз 150, оптика до 300). Окно режет LOS, не метры. Турель без позы Aiming: `treatAsAlwaysAimed` когда gunner bound; `OpticVisionRange` если задан (>150), иначе 150. Live M2/MK19 без оптики = 150.

## A.3 Скан

Один проход: собрать оппонентов в грубом радиусе (текущий обзор + 4 м) → бюджет LOD → на полном проходе грубый FOV, кэш LOS, лучи по hit-зонам → если точка прицела в конусе и дальности, записать наблюдение. Список наблюдений заменить целиком. Пропущенный скан **не** пишет пустой список.

Наблюдение — только физика кадра. Нет прогресса, личности, команды, памяти.

Поля: цель, позиция корня, AimPoint и флаг, квадрат дистанции, угол, Exposure (доля видимых hit-зон). Веса зон: грудь важнее ног.

## A.4 Бюджет скана

| Уровень | Что делает | Пишет кадр зрения? |
|---------|------------|---------------------|
| Idle | спит | нет |
| Cheap | считает кандидатов | нет |
| RangeFov | грубый конус (половина FOV + 8°) | нет |
| Detail | лучи, кэш, полный кадр | **да, только он** |

На полный проход сразу: принудительный скан, уже выбранная цель, свежая потеря, очередь детализации. Иначе: ≥ 0.5 с без полного → грубый конус; ≥ 1.5 с без состава → Cheap; иначе Idle.

Полных проходов на весь кадр игры не больше **8**. LOD **не штрафует** качество обнаружения. Observed может держаться на последнем удачном луче.

Интервал скана на префабе: 0.25–0.45 с × коэффициент уровня (Idle ×3, Cheap ×1.75, RangeFov ×1, Detail ×0.75). Ранг задаёт свой диапазон `VisionScanInterval`.

## A.5 Контакт

Контакт — знание **одного** наблюдателя про **одну** цель. Каналы:

- зрение (наблюдение, прогресс, личность, память места);
- звук (уверенность и точка, горизонт **3 с**);
- доклад союзника (уверенность и точка, горизонт **8 с**, дальность **80 м**).

Звук и доклад **не** пишут визуальное наблюдение, **не** ставят Observed, **не** создают AimPoint. Личность из звука/доклада не коммитится. LastKnown звук/доклад могут сдвинуть, только если цель сейчас **не** Observed.

**Актуально 29.08.2026:** звук и доклад в продакшене публикуются. `CombatEventHub` (Gunshot / Hit / Impact / Death) и `WorldSoundHub` (Gunshot / Explosion / Footstep / Impact) — **разные шины**. Hitscan публикует Gunshot/Hit/Impact. #9 копирует Sound/Report в `AIPerceptionFrame`. Старая формула «в продакшене никто не шлёт» **снята**.

## A.6 Обнаружение

```text
Q = Distance × FOV × Exposure × Movement
```

Movement всегда ≥ 1. Стоячая цель = 1. Ходьба/бег цели помогают её заметить. Движение наблюдателя в Q не входит.

| Параметр | Значение |
|----------|----------|
| Дальность глаза | **150 м** |
| Конус глаза | **120°** (половина 60°) |
| Оптика в Aiming | своё поле **150…300 м**, конус **8°** |
| Кривая FOV для Q | половина 60°, край 0.15 |
| Дистанция для Q | t = d/resolvedRange; хвост t=0.82/0.90/0.96/1.00 → **0.50 / 0.38 / 0.32 / 0.30** |
| Порог заметить | **0.25** |
| Порог потерять | **0.20** |
| Время Detected при Q=1 | **0.35 с** |
| Exponent накопления | **3.8** |
| Сброс прогресса | **2.5 с** |
| Бонус движения цели | idle 1.00 / walk 1.15 / run 1.35 / потолок 1.50 (пороги 0.6 и 3.2 м/с) |

Гистерезис: Q > 0.25 растёт; 0.20 < Q ≤ 0.25 держится; Q ≤ 0.20 падает. Состояния: Undetected / Detecting / Detected.

Пока цель в кадре: ObservationState = Observed, LastSeen = сейчас, LastKnown = корень, LastSeenConfidence = 1. Пропал из **реального** кадра: Q обнуляется, Observed → RecentlyLost. LastSeen/LastKnown **замораживаются**. Нет экстраполяции по скорости.

Attention / Facing (Stage 15 **PASS 44/0**): множитель скорости Detection, **не** фактор Q и не второй FOV. Cap 2.5 @0°; ≥45° = 1.0.

## A.7 Закон конверта (закрыто этапами 1–9)

```text
без кратности / HipFire / коллиматор / 1×     →  150 м, конус 120°
кратный прицел в Aiming                       →  своё поле 150…300 м, конус 8°
дальше текущего обзора                        →  нет Observation, нет AimPoint, нет Fire
длина hitscan                                 →  min(запас луча на префабе, текущий обзор)
retain reload/misfire                         →  текущий ResolvedMaxRange источника
```

Запас луча на префабе часто **650 м** — потолок железа, не игровой мир. Оптика читается **абсолютными метрами**, не «×1.2 к 150». У боевых прицелов Range× урона = **1.0**. Глушитель **1.1** — физический модуль. Бонус кратности только в **Aiming**. Переменный прицел в runtime держит **высокий** режим (переключателя игроку нет, A8 закрыто решением).

Урон — отдельная шкала внутри того же конверта:

```text
E = min(дальность ствола × физические модули, дальность патрона)
d ≤ E        →  урон × 1
E < d < 2E   →  линейно к нулю
d ≥ 2E       →  урон × 0
```

Выстрел не запрещён EffectiveRange. Если E = 225, ноль урона на 450, но луч туда не долетит: обзор режет раньше.

## A.8 Живые прицелы

Кратность — **класс**, не `150 × zoom`. Шкала: 150 → 175 → 200 → 210 → 220 → 250 → 260 → 280 → 300. Снайперский класс с **6×**. Штурмовой 1–6× высокий = **250**. Только Scope9 = 300. Приёмка этапа 8 **PASS 29/0**.

| Класс | Обзор | Примеры |
|------:|------:|---------|
| 1× коллиматор / голограф | 150 | Reddot1/2/3, RDC, AK_Reddot4_Rail |
| 2× | 175 | Aimpoint |
| гибрид низкий | 150 | G33, ACOG_RMR, ELCAN, Vortex |
| 3× | 200 | Scope1_3x; G33 высокий |
| 3.5× | 210 | Mosin_Scope8; ACOG_RMR высокий |
| 4× | 220 | ACOG, SUSAT, AK_Scope11; ELCAN высокий |
| штурмовой 6× | 250 | Vortex высокий |
| снайпер 6× / 8× / 10× | 260 / 280 / 300 | Scope4 / Scope5 / Scope9 |

AimTime× прицелов — старые плоские множители 0.95…1.56, **не** пересчитаны из обзора. ЛЦУ — не прицел (красная точка 50 м, бонус PointAim).

## A.9 Живые стволы и патроны (E)

Приёмка этапа 9 **PASS 53/0**. Колонка «было» — историческая шкала 275–1200, не закон.

| Ствол | Роль | E | Край пояса |
|-------|------|--:|-----------:|
| BenelliM4 | дробовик | 40 | 40 |
| AK74U | CQB | 100 | 150 |
| MK18 | CQB | 105 | 150 |
| AK74UMOD1 | CQB | 110 | 150 |
| AK47S | CQB | 115 | 150 |
| M4_ModA_1 / AK47 | штурм | 140 | 200 |
| AK47_1 / AK74 | штурм | 145 | 200 |
| M4_ModA_2 / AK47MOD1 | штурм | 150 | 200 |
| AK74MOD1 | штурм | 155 | 200 |
| M16A_ModA_1 | штурм | 160 | 200 |
| RPK47 / M249 | LMG | 150 | 200 |
| RPK47MOD1 / RPK74 | LMG | 155 | 200 |
| RPK74MOD1 | LMG | 160 | 200 |
| PKM | LMG | 160 | 200 |
| M16A4_ModA_2 / SVD | marksman | 175 | 225 |
| MK12 / Mosin | marksman | 200 | 250 |
| Sniper762x51 | снайпер | 225 | 300 |
| M2Browning_127 | HMG | 225 | 300 |
| MK19 | AGL | 300 | потолок замысла, не live-falloff |

Патрон — второй потолок `min(ствол, патрон)`: 12 Gauge 40; 5.45/7.62×39 **250**; 5.56 / 7.62×51 / 7.62×54R / 12.7 / 40×53 **300**.

РПГ — не hitscan. Permit: Observed AimPoint внутри `ResolvedMaxRange`. Ракета **115–130 м/с × 12 с** может лететь дальше обзора. MK19: **240 м/с × 25 с**. Stage 12 **PASS 30/0**. Подствольный гранатомёт: слот в данных есть, боевого контента со своей дальностью нет; когда появится — тот же закон 150 / до 300.

## A.10 Память места

```text
0–5 с     RecentlyLost
5–30 с    Lost             уверенность падает
≥ 30 с    Lost, conf = 0   забыл место; контакт не удаляется
```

```text
conf(t) = (1 − t / 30)^1.5     при старте 1
```

Полезность для AI: LastSeenConfidence **> 0.25**. Stale: 0 < conf ≤ 0.25. Forgotten: conf = 0. Повторный взгляд: conf = 1, тот же контакт, личность сохраняется. LastKnown = LastSeen, пока Observed. Нет экстраполяции скорости. Память не удлижается из-за перезарядки.

Retain при reload/misfire = `ResolvedMaxRange` (A7 **PASS 31/0**). Поле 18 м снято.

## A.11 Кто это, отношение, угроза

Личность: `Unknown / Friendly / Neutral / Hostile`. Relationship из **закоммиченной** личности. Угроза только при Hostile.

Улика: `VisualIdentityEvidence` на цели (look Player / Enemy / Civilian). Спавн пишет look отдельно от `UnitTeam`. Наблюдатель маппит look **относительно своей стороны**. `UnitTeam` в знание не копируется.

| Кто смотрит \ look | Player | Enemy | Civilian |
|--------------------|--------|-------|----------|
| Player | Friendly | Hostile | Neutral |
| Enemy | Hostile | Friendly | Neutral |
| Neutral | Neutral | Neutral | Neutral |

Калибровка при Q=1: IdentifyTime **4.0 с**, commit **0.50** (≈ 2 с взгляда). Threat High ≤ **25 м**, Medium ≤ **80 м**, Low дальше 80 м. Дистанция угрозы — из последнего **визуального** наблюдения. Потеря LOS личность не сбрасывает. Detected + Unknown — норма. Этап 1 **FROZEN**, Play mapping **PASS 49/0**.

## A.12 Звук и доклад (C1 / C2 / #8 / #9)

Дальности звука (BAKED, не VisionRange):

| Тип | Range | Strength |
|-----|------:|---------:|
| Gunshot | 300 | 1 |
| Explosion | 500 | 1 |
| Footstep | 25 | 0.35 |
| Impact | 40 | 0.5 |

`confidence = Clamp01(strength * (1 - d/range))`. Occlusion raycast нет. Свой юнит своё событие не слышит.

```text
VISUAL  → Observed / AimPoint
SOUND   → SoundContact / SoundPosition
SHARED  → SharedEvidence / SharedPosition / SharedIdentity
```

Доклад **не** копирует Contact A → Contact B. Stage 16 **PASS 47/0**. Stage 17 **PASS 72/0**.

Канон AI (#9):

```text
Defense + heard hostile  → Search
Attack  + heard hostile  → Search
Idle    + heard hostile  → ничего
```

`event ≠ automatic knowledge`. ImmediateThreat читает боевые события, не звуковую шину как Fire.

## A.13 Кадр для тактического AI

Снимок **не** содержит Q, DetectionProgress, UnitTeam, Combat.SelectedTarget.

| Флаг | Правило |
|------|---------|
| VisibleNow | Detected **и** Observed |
| HasUsefulMemory | LastSeenConfidence > 0.25 |
| MemoryStale | 0 < conf ≤ 0.25 |
| Hostile / Friendly / Neutral | по Relationship |
| IdentityUnknown | Identity == Unknown |

VisibleNow у AI строже, чем у боя: бой может выбрать цель ещё в Detecting при Observed + луче. AI считает видимым только Detected+Observed.

Stage 18 Final Perception **PASS 49/0**.

## A.14 Замороженные числа зрения

```text
конус глаза 120°, кривая FOV 60° / 0.15; оптика Aiming 8°
acquire 0.25 за 0.35 с, lose 0.20 за 2.5 с, exponent 3.8
хвост DistanceCurve 0.82/0.90/0.96/1.00 → 0.50/0.38/0.32/0.30
память 5 с / 30 с / форма 1.5 / stale 0.25
личность 4.0 с / commit 0.50
угроза 25 м / 80 м
глаз 150 м (до 300 с кратным прицелом)
EffectiveRange ≠ разрешение выстрелить
HipFire игнорирует оптику в обзоре, конусе и AimTime
Range× оптики = 1.0
```

Если «AI плохо ищет или плохо стреляет» — это не повод менять эти числа.

---

# ЧАСТЬ B. БОЕВОЙ КОНТУР

То, чем юнит на префабе **уже стреляет**. Тактический AI здесь не участвует, пока контроллер не включён.

Лог: `SELECT` → `G6` → `DISC` → `GATE` → `SHOT` / `PROJECTILE`.

Порядок кадра (execution order):

```text
−200  списки восприятия
   0  скан зрения
  10  тик знания
  20  выбор цели
  30  Track / Aim / Fire / Ignore
  50  поза / визуальная отдача
  54  огневая дисциплина
  55  патрон / RPM
  56  ворота выстрела
  57  hitscan + прогресс прицела
  58  игровой RecoilOffset
  65  IK / ствол к точке прицела
```

## B.1 Выбор цели

Кандидаты — контакты знания. Отсекают: нет цели / нет знания / мёртв / без сознания / Friendly / Neutral личность. Unknown **можно** выбирать. Stale по умолчанию можно.

Очки (G5, **не переписывать**): +10 Observed; уверенность ×2; (Threat/High)×1; +0.5 Hostile; +1/(1+дистанция до LastKnown); −3 если stale. LastKnown — только подсказка дистанции.

AimPoint только если Observed и в последнем наблюдении есть видимый AimPoint. Иначе цель может быть **выбрана без прицела**.

| Понятие | Условие |
|---------|---------|
| Selected | победил в очках |
| Engageable | Selected + LOS-прицел + жив |
| Fire | отдельное решение G6 |

Retain reload/misfire = текущий `ResolvedMaxRange`. Нет AimPoint → нет Fire. Дружеский/нейтральный корпус на линии ствол→прицел (сфера 0.35 м) глушит цель ≈ 0.15 с.

Упреждение: сглаживание AimPoint, проекция не дальше 0.5 с. Это не LastKnown.

## B.2 Калибровка выбора (#12 CLOSED / FROZEN)

Selection ≠ Fire. Hysteresis: `NewScore > CurrentScore + SwitchThreshold`, **SwitchThreshold = 0.45**.

Weapon suitability — нюдж, не роли: Shotgun/Pistol/SMG ближняя ↑; Sniper дальняя ↑; Rifle/LMG мягкий пик ~0.45×E. `WeaponSuitabilityWeight = 0.35`. Mission `TargetEntity` бонус **0.6**. `AI.EngageTarget` и `Combat.SelectedTarget` не сливаются. Play **26/0**.

## B.3 G6 — Track / Aim / Fire / Ignore

```text
нет выбранного / нет знания / Friendly / Neutral / мир запретил  → Ignore
нет LOS-прицела                                                 → Track
оружие/поза/прицел не готовы                                    → Aim
иначе                                                           → Fire
```

Угроза **не** открывает Fire. Unknown **может** получить Fire. Память без луча → Track. Observe / Suppress / Report в enum есть, политики нет.

Aim или Fire = держим огневой контакт. Выстрел только при **Fire**.

## B.4 Кто жмёт спуск

`UnitWeaponFireDisciplineController` (order 54). План серии: патроны, пауза, режим, порог Aim — по `distance / workingRange` класса, hysteresis **0.08**. Пояса Close / Near / Mid / Far / VeryFar. Старые метры 25/70/140/220 сняты как шкала мира.

Полы AimProgress (GATE / A3): HipFire **0.35**, PointAim **0.68** (в части pose-кода встречается 0.65 — не ретюнить как «баг 0.03»), Aiming **1.0**. PreAim не стреляет. Stage 11 **PASS 21/0**.

Другие источники спуска: RTS игрока, тесты. AI спуск **не** вызывает.

## B.5 Ворота одного выстрела

Намерение Fire. Сознание. Оружие (патрон, не осечка, RPM, не reload). Поза: только HipFire / HipFireWalk / HipFireCrouchWalk / PointAim / Aiming. Во время бленда **обе** позы стрелковые. Не спринт. Ствол в AimPoint с допуском позы (Aiming стоя ≈ 3°). Линия огня не в союзника/нейтрала.

После успеха: hitscan, затем kick RecoilOffset. Kick **этого** выстрела на **этот** луч не действует.

## B.6 Куда летит пуля

В AimPoint + упреждение + конус θ вокруг направления **после** текущего RecoilOffset. Запасного прицела в LastKnown / коллайдер **нет**. Длина hitscan = текущий обзор. E режет **урон**, не разрешение.

## B.7 Стрельба и отдача (A10 CLOSED)

Три канала:

| Канал | Что |
|-------|-----|
| `θ` | полуугол intrinsic-конуса, градусы |
| `Offset` | yaw/pitch °, сдвиг цели **до** конуса, cap **12°** |
| Visual punch | картинка; в hitscan при выбранной цели **не** пишется |

```text
aim = ApplyOffsetToDirection(toTarget, Offset)
затем конус θ вокруг aim
kick после успешного луча
каждый кадр Offset → 0 со скоростью recovery
```

θ **не** зависит от накопленной отдачи. Старый `RecoilPerShot` на ассетах лежит, в пулю не входит.

```text
raw = BaseShotDispersion × ammo.Spread × DistanceDispersion × модули
    × стойка × навык × черты × IncompleteAim × поза × BaseSpreadToDegrees(0.35)
θ = clamp(raw, 0.04°, 12°)
```

Поза θ×: Aiming **1**, PointAim **1.5**, PreAim **1.75**, HipFire **2.5**.

Kick: fireMode × ammo × attachments × RecoilControl × stance × pose. Паттерн: два синуса 0.73 / 1.31. На паузе recovery ×1; курок зажат ×0.7. `StopFiring` снимает только visual punch.

Tuning вне A10 (не открывать как баг AI): Benelli θ, M2 recoil stack, Review AK-74/PKM/MK12/SVD.

Код: `WeaponRecoilMath`, `UnitWeaponRecoilController`.

## B.8 Поза, hold, lean, голова

Позы оружия: NotReady / LowReady / HighReady / HipFire / PointAim / Aiming (и walk-варианты). Это **не** ReadinessState.

**заполним позже:** полная матрица hold/IK/AimPitch как продуктовый UX. Заморожено по коду: нет weapon-local aim correction; визуальная отдача через `WeaponVisualRecoilApplicator` на `Hand_R` (order 200); AimPitch ≠ residual cancel.

**Lean / peek.** Юнит не шагает корнем. Peek = roll `Spine_01` / `Spine_02`. Уровни Quick / Smooth / Deep. StandingIdle порядка **42°**, CrouchIdle **38°**, правая сторона ×**1.18**. Блоки: ragdoll / vehicle / prone / sprint. Тактика подключает существующий `UnitSpineLean` (`SetLeanLevel`) — #13.7 peek из Occupied, #14.8 moving lean. Не второй LeanController. Lean ≠ Fire.

**заполним позже:** перепись HeadLookAround (есть диагностика, не канон поведения).

## B.9 Что боевой контур не знает

Не читает Q, DetectionProgress, LOD. Угроза в контексте решения есть и **игнорируется**. LastKnown не цель. Приказы AI не читает.

---

# ЧАСТЬ C. ТАКТИЧЕСКИЙ AI

Слой **задачи**, не выстрела. Лог: `INPUT` → `GAMECMD` → `CMD` → `AI` → `MOVE`. AI не пишет `SHOT`.

## C.1 Шесть состояний

| Состояние | Смысл |
|-----------|--------|
| Idle | нет задачи, сам тактику не начинает |
| Defense | держать место / сектор |
| Attack | добиться результата в точке / зоне / по объекту |
| Search | искать по области улики |
| Retreat | уйти на другую позицию управляемо |
| Flee | бросить задачу, уйти от угрозы |

Не состояния: Observe, Track, Investigate, Engage, Chase, Suppress, Patrol.

Переходы приказом:

```text
Idle     → Defense, Attack, Search, Flee
Defense  → Attack, Retreat, Idle, Search, Flee
Attack   → Defense, Retreat, Idle, Search, Flee
Search   → Attack, Defense, Idle, Retreat, Flee
Retreat  → Defense, Idle, Flee
Flee     → Idle
```

Запрещено: Idle→Retreat; Retreat→Attack/Search; Flee куда угодно кроме Idle. Писатель состояния — только `UnitAIController`.

## C.2 Hold / Engage

```text
Defense или Attack + Hostile + VisibleNow  → Engage
Defense или Attack + иначе                 → Hold
Idle / Search / Retreat / Flee             → None → CombatIntent Hold
```

Engage-цель = max Threat среди Hostile+VisibleNow. Engage **не** вызывает TargetSelector, навигацию, подъём оружия, выстрел. Idle + видимый враг → остаётся Idle. Unknown не Hostile → Hold.

`CombatIntent` Hold: Aim/Fire → Ignore; Track жив.

## C.3 Search 2.0 (#10 FROZEN)

Старт только из Defense/Attack, нет Hostile VisibleNow, есть улика: полезная visual memory **или** hostile combat sound **или** hostile report. Источники не сливаются. Приоритет: Visual LastKnown > SoundPosition > Report. Idle сам не начинает. Stale visual не стартует Search.

```text
SearchArea snapshot (радиус 15 м = граница области, не arrival)
        ↓
кандидаты generate → filter → score → cache (cap 6, не каждый тик)
        ↓
Walk candidate (arrival 1.5 м) → STOP / LOOK / EVALUATE ~1 с
        ↓
Found: Hostile+VisibleNow → ReturnState + Engage
Exhausted / Expired → ReturnState
ImmediateThreat не завершает Search (amendment 28.08.2026)
        → RoE + EmergencyCover, state остаётся Search
New order отменяет Search
```

Search **не** пишет Memory / LastKnown. Destination — снимок на входе. Новый звук во время Search area не двигает. Play **22/0**.

Attack→Search: visual dwell **1.5 с** (`LastHostileVisibleAt`) **или** Search 2.0 gunshot/report сразу. Search→Attack Found: только `HostileVisible`.

## C.4 Attack / Defense / Retreat / Flee

Один обработчик `UnitAIPointNavigationHandler` на Attack **и** Defense.

```text
приказ Attack(P)     → Attack   Destination=P        Walk к P или к cover slot
приказ Defense(P)    → Defense  Anchor=P, радиус 10 м Walk к якорю или к слоту
ImmediateThreat      → RoE + EmergencyCover; state не меняется
потерял контакт      → Search 2.0, ResumeState = Attack или Defense
```

Приход Attack/Defense: диск **0.60 м**. Search / Retreat / Flee dest-only: **1.50 м**. **Tolerance 0.60 не поднимать.**

**Актуально:** Defense **ходит** к якорю. Старые формулировки «Defense якорь не ходит» / контракт 6.4 «Defense no Walk» **устарели относительно кода**.

На арене обе стороны получают только **Attack** в центр `(0, 0, 75)`. `SetDefense` спавнер не выдаёт. Захват (`Capture`) нет — это **#24**.

Cover mission: Attack чуть ближе к **врагу**, Defense чуть дальше; якорь и радиус 10 м в `CoverSituation` не передаются. Сектор Defense из 13.5 **не реализован**. WeaponScore / Rank в cover — нюдж; спавнер `BindCoverProfile` не вызывает.

Retreat / Flee: Walk к snapshot точки. Attack/Defense/Retreat после прихода Stop и остаются в состоянии. Flee после прихода Stop → Idle. Play этапа 4 **36/0**.

## C.5 RoE и ImmediateThreat (#7 CLOSED)

По умолчанию на новом контроллере: **SelfDefense**. Смена — любой уровень в любой, не лестница.

| Level | Смысл |
|-------|--------|
| SelfDefense | сила только против Hostile при **ImmediateThreat=true** |
| RestrictedDefense | сила против Hostile (зона позже; матрица как MissionCombat) |
| MissionCombat | сила против Hostile |
| FullEngagement | сила против Hostile |
| NoFriendlyFire | сила против всех, кто не Friendly |

| Policy | Friendly | Neutral | Unknown | Hostile без threat | Hostile + ImmediateThreat |
|--------|----------|---------|---------|--------------------|---------------------------|
| SelfDefense | NO | NO | NO | NO | YES |
| RestrictedDefense | NO | NO | NO | YES | YES |
| MissionCombat | NO | NO | NO | YES | YES |
| FullEngagement | NO | NO | NO | YES | YES |
| NoFriendlyFire | NO | YES | YES | YES | YES |

RestrictedDefense / MissionCombat / FullEngagement в коде — **одна матрица**. Зон нет.

Разрешено ≠ выстрел. Evaluator не стреляет. Читает Relationship выбранного **боевого** контакта и bool ImmediateThreat. Denied + Aim/Fire → Ignore; Track не трогать.

**Источник ImmediateThreat.** Production: `ImmediateThreatSource` по событию боя (TTL SerializeField, на префабе **4 с**). Источники #7: (A) враг выстрелил, боевая цель = этот юнит, даже промах; (B) hitscan/снаряд попал в `DamageableTarget`, атакующий Hostile по **UnitTeam**; (C) `NotifyHostileAttack` — stub API. Геометрический «мимо, но рядом» и дружеский огонь — нет. Perception / TargetSelector / evaluator флаг **не** ставят.

Нюанс кода: публичный setter `UnitAIController.ImmediateThreat` пишет флаг напрямую (тесты/smokes). Это не второй production-источник мира.

На арене спавнер ставит RoE **MissionCombat**. Повесили AI и оставили SelfDefense без threat → Aim/Fire режутся, юнит «перестал стрелять». Это RoE, не баг зрения.

Play: Immediate Threat Live **18/0**, Use of Force **107/0**.

## C.6 Команды (6.1–6.4 CLOSED, #11 FROZEN)

Внешний приказ ≠ состояние. Вход: `IssueCommand(TacticalCommand)`. Типы: Defense / Attack / Search / Retreat / Flee / Cancel. Команда не стреляет, не пишет Vision/RoE, не выбирает цель.

Цепочка: `GameCommandInput` → `GameCommandService` → `IssueCommand`. Нет AI → `NoAI`. Neutral исключён из игрового ввода.

Полосы приоритета: **Emergency > High > Mission > Tactical > Routine**. ImmediateThreat не меняет state на Flee (HoldState, включая Search). Search — тактический overlay. Play #11 **18/0**.

Три канала на арене:

1. Спавн: enable AI + `SetAttack` (debug `TryIssue`), не `GameCommandService`.
2. HUD: production `IssueCommand`.
3. RTS-клик: второй путь на `UnitNavLocomotionDriver`, **минуя** #13/#14.

## C.7 Атака vs оборона vs захват (сверка)

**Сделано (одиночка):** приказ Attack/Defense, Hold/Engage, Search из обоих, динамические слоты, Stay/Reposition, emergency под огнём, бронь слота, peek из Occupied, hop к слоту.

**Сделано тонко:** Attack/Defense различаются одним слагаемым MissionScore относительно врага, не зоны.

**Не сделано и по карте не сейчас:** захват объекта, оборона как миссия группы, роли assault/support, CQB-углы, оружейные позиции, адаптивный штурм.

Диагноз арены: обе команды **штурмуют одну точку**. Это не тест «захват vs оборона».

---

# ЧАСТЬ D. УКРЫТИЯ И ДВИЖЕНИЕ

## D.1 Принцип

Укрытия **не расставляет дизайнер**. Нет `CoverPoint_001`. Солдат читает геометрию мира.

```text
существует ли позиция у стены?     ← поиск / bake
какого она геометрического типа?   ← классификация в Generate
насколько выгодна мне сейчас?      ← individual score 13.3
Stay / RepositionRequest           ← overlay #13, не Move
hop / Walk                         ← #14
Reserved → Acquired → Occupied     ← board, не геометрия
```

Десять юнитов не делают десять раз один анализ стен.

## D.2 Поиск и bake

Регион — клетка **16 м**. Cap **16** слотов на клетку (spatial diversity, не «лучшие 16»). Occupancy ключ = `(RegionX, RegionZ, CandidateId)`. `C1` в разных клетках — разные слоты.

Конвейер: OverlapBox → грани (отсев trigger/персонаж/техника/крыша) → сэмплы шаг **2 м**, standoff **0.45 м** → NavMesh 1.2 м → якорь в стену → capsule 0.28×1.8 → dedup 0.75 м → cap 16 → `CoverClassifier`.

Классификация относительно `Normal` геометрии, не врага E01. Сегменты Head/Torso/Pelvis/Legs. **#13 FROZEN** приоритет типа: Corner → Standing → Crouch → Partial → None. **#13.2B** расширяет типы bake (Edge / Opening / Window / …) без смены CoverScore / occupancy. Пороги классификации — **прототип, не freeze**.

Editor bake: `TacticalWorldBaker` пишет `BakedCoverCandidateRecord[]` на сценовый `TacticalWorld`. Play **не** сканирует стены: `BakedCoverCandidateSource` → `SharedCoverSpatialCache` → occupancy. Smoke #13 может генерировать в рантайме.

Выбор #13 смотрит **одну клетку**, где стоит юнит. Главная дыра: слот в 2 м за швом сетки 16 м не виден. #14 для стен может спросить до трёх клеток — это не расширяет выбор укрытия.

Сдвинул проп без Bake — Play ходит к старым точкам. Destroy в рантайме слот не убивает (GeometryVersion руками). Нет этажей / окон / внутренних полостей.

#13 целиком (выбор / occupancy): EditMode **169/0**, Play **18/0**. Формулы CoverScore **не крутить**. Расширение генератора — **#13.2B**, раздел D.6.

## D.3 Occupancy (не reopen #13/#14)

Board: Available / Reserved / Occupied.

```text
Reserved → Approaching → Acquired (dist ≤ 0.60) → Occupied (только ConfirmOccupied)
```

`Acquired ≠ Occupied`. `cover=0` в логе — dest-only, не jersey. `cover=C1` — реальный слот.

Пока Reserved и путь жив — heartbeat TTL. Release: path invalid, cover invalid, timeout, Unconscious, Death, смена приказа. Occupied + valid + LOS: Stay Committed, не свап по score. OccupancyVersion **не** ключ переоценки.

`OutOfTolerance` часто = dest центра vs слот 10–13 м, **не** повод поднимать 0.60.

Occupy на массовой арене **не FROZEN**. Play 28.08: `Infantry_20260828_163640` — 15 Occupied; `Infantry_20260828_204049` — 13 Occupied / 10 юнитов, `Search→Attack ImmediateThreat` = 0. Карточки юнитов — в папках Runtime, не здесь.

## D.4 Tactical Movement (#14 FROZEN)

NavMesh = физический путь. Tactical route = путь с меньшим риском. Overlay **не** Move и не Fire. Evaluator не Walk. Executor Walks выбранный hop.

Режимы: Normal / Tactical / Emergency. Direct dest — baseline. Cover-to-cover hops из кэша #13. Urban: предпочтение коридора вдоль стен, не hug-wall. Exposure profile сегмента (peak / duration), не скорость. Replan по событию, не каждый кадр. Under fire: Continue / Replan / EmergencyCover / Hold — не новый UnitAIState, не Flee.

**Production Attack/Defense на арене:** hop часто **Normal / Direct** к слоту или центру. Mode в профиле может быть Tactical; формулы PathScore не крутить.

Arrival: Nav Reached ≠ Cover acquired. EditMode **178/0**, Play **157/0**.

## D.5 Дыры поиска (не чинить CoverScore)

```text
нет слотов в Play          → bake / NavMesh / wiring
слоты есть, AI не идёт     → overlay / reservation / hop
идёт не туда               → individual score / SwitchingCost
двое в одном слоте         → occupancy board
не стреляет из укрытия     → G6 / RoE / Readiness, Cover ≠ Fire
снайпер слишком близко     → #15, не CoverPoint
```

## D.6 #13.2B Extended Cover Position Bake (**CLOSED** как scene point-bake)

Детекторы и точечный `CoverCandidateGenerator` остаются для #13 inject и EditMode 13.2B. **Scene baker пишет #13.2C ProtectionGeometry.**

Два этапа. Сейчас **только A**.

```text
Этап A — найти и запечь правильные позиции на карте.     ← #13.2B
Этап B — юнит выбирает, занимает, открывается, стреляет. ← позже, не открывать
```

**#13 остаётся FROZEN** как действующая система выбора / occupancy / CoverScore. #13.2B расширяет **генератор и классификацию**, не механику солдата.

### Позиция

`CoverCandidate` — геометрически валидная тактическая позиция из мира. Ещё не значит: лучшее укрытие, сюда идти, стрелять, присесть, выглянуть.

Одна позиция = один candidate. Не печь `EdgePeekLeft` / `OpeningStand` отдельными точками. Runtime этапа B решит, как открываться.

### Геометрические типы (PrimaryType)

```text
Edge
Crouch
Opening
Window
Partial
Corner
```

`Standing` **больше не тип позиции**. `StandingProfile` остаётся профилем защиты стоящего человека, не `CoverType`. Высокая стена без Edge/Opening/Window/Corner запекается как `CoverType.None` (не selectable). Enum `Standing=2` сохранён для старого bake / #13 inject.

Приоритет PrimaryType (не score): `Window > Opening > геометрический Corner > Edge > legacy Corner > Crouch (без StandingValid) > Partial`. Доп. флаги и профили не выкидываются.

| Тип | Смысл bake | Сейчас делает |
|-----|------------|---------------|
| **Edge** | полностью скрытая база у края стены/проёма | точка + EdgeDirection + `CanPeek` (геометрия); peek/step/lean runtime нет |
| **Crouch** | полезная защита в приседе, standing не набран | `CrouchProfile`; primary только если `CrouchValid && !StandingValid` |
| **Opening** | позиция у открытого проёма | одна база, OpeningWidth / OpeningAxis / OpeningCenter; флаги `CanStepLeft/Right`, `CanOpen/Close` — геометрия, не действие юнита |
| **Window** | проём + оконная конструкция / стекло | Opening + pane; `CanObserveThrough` / `CanFireThrough`; OpeningValid и opening metadata сохраняются; Vision/Fire runtime нет |
| **Partial** | часть тела закрыта, standing/crouch не набраны | fallback PrimaryType, не AI-score |
| **Corner** | реальный угол двух поверхностей; защита сзади/сбоку, открытый сектор спереди | пара поверхностей + `CornerFacing`; не конец одной стены; legacy A5 classify-only остаётся Corner |

Целочисленные значения enum: `None=0 Crouch=1 Standing=2 Partial=3 Corner=4` **не сдвигать** (старый bake). `Standing=2` — legacy слот; новые: `Edge=5 Opening=6 Window=7`.

### Protection ≠ Type

```text
ProtectionProfile
├── Standing   (StandingProfile)
├── Crouch     (CrouchProfile)
└── Partial    (PartialValid)
```

### Bake record

Как сейчас: Position, Normal, CoverType, StandingProfile, CrouchProfile, NavMeshValid, Region, GeometryVersion.

Для новых типов, только если уже есть из геометрии:

```text
SecondaryDirection / EdgeDirection
LeftOffset
RightOffset
OpeningWidth
OpeningAxis
OpeningCenter
WindowCenter
WindowAxis
WindowWidth
HasFrame
HasTransparentPane
CornerFacing
CornerNormalA
CornerNormalB
CornerOrientation
Capabilities   (CanPeek, CanFireThrough, …) — флаги позиции, не действие юнита
```

Не хранить: PeekState, CurrentExposure, CurrentFacing, ChosenTarget, Occupancy (occupancy по-прежнему на board).

Несколько классификаций на одном месте **не выкидывать**: `PrimaryType` + `Capabilities` / Valid-флаги (`EdgeValid`, `OpeningValid`, …).

Иерархия Edge → Crouch → Opening/Window → Partial → Corner — **рекомендация для будущего score**, не формула CoverType и не CoverScore.

### Pipeline

Тот же конвейер. Классификация **один раз** в Editor / Generate. Play читает готовый тип, геометрию заново не ищет.

```text
Physics geometry → raw collider faces → MERGE logical surfaces (#13.2B.5A) → samples → NavMesh → clearance → classification → Edge/Opening tag → Window tag → Corner tag → CoverCandidate → BakedCoverCandidateRecord
```

**#13.2B.5A** склеивает коллинеарные грани до Edge/Opening/Corner. Стык префабов / AABB seam — продолжение стены, не дыра. Дверь — внутренний разрыв уже слитой стены. `CoverClassifier` и cap 16 не трогаем: сначала убрать ложную геометрию.

Стекло **не** создаёт Opening. Window = уже найденный Opening + прозрачная панель. Маркер: компонент `TacticalTransparent` (не имя материала). В extract/occlusion/clearance стекло пропускается: Frame блокирует, Glass — tactical passthrough только как семантика bake. Vision/Fire не подключены.

Corner **не** создаётся из конца одной стены. Нужны две сходящиеся поверхности, inset от вершины, открытый фронт (`CornerFacing`). `Inner` / `Outer` — ориентация, не новый `CoverType`. Классификатор A5 (стена продолжается с одной стороны) не ломаем.

### Подэтапы

| Подэтап | Содержание | Статус |
|---------|------------|--------|
| **13.2B.0** | Модель данных: новые типы, Standing только как protection, bake roundtrip | **PASS** |
| **13.2B.1** | Edge: концы поверхностей, скрытая база, без peek-точек | **PASS** |
| **13.2B.2** | Opening: разрыв между поверхностями, одна база на проём | **PASS** EditMode; Play читает bake, геометрию не ищет |
| **13.2B.3** | Window: Opening + рама/прозрачная плоскость; `TacticalTransparent`; один candidate | **PASS** |
| **13.2B.4** | Corner: стык двух поверхностей, не конец одной стены; Facing / Inner/Outer | **PASS** |
| **13.2B.5** | Final CoverType vs ProtectionProfile; mid-wall = `None` + StandingProfile, не selectable; capabilities как геометрия | **PASS** |
| **13.2B.5A** | Logical wall reconstruction: merge collider faces, seam ≠ Opening, дверь после merge, Edge на концах logical surface | **PASS** |
| **13.2B.6** | Dedup / PrimaryType + Capabilities, детерминизм | вошло в детекторы и 13.2B.5 |
| **13.3** | Runtime: юнит выбирает/занимает Edge/Crouch/Opening/Window/Corner/Partial | **не открывать** |

Матрица PrimaryType (одна геометрия → один тип; флаги и профили не выкидываются):

| Геометрия | PrimaryType |
|-----------|-------------|
| Длинная стена, середина | `None` (StandingProfile, non-selectable) |
| Край длинной стены | Edge |
| Низкое присадное укрытие | Crouch |
| Дверной проём | Opening |
| Окно со стеклом | Window (`OpeningValid` остаётся) |
| L-образный угол (две поверхности) | Corner (бьёт Edge) |
| Частичная защита | Partial |

Приоритет PrimaryType: `Window > Opening > геометрический Corner > Edge > legacy Corner > Crouch > Partial > None`. `Standing` классификатор не пишет.

Существующие тесты 13.1 / 13.2 / cover smoke должны остаться PASS. Новые типы — отдельные проверки. Одна геометрия → Bake #1 и Bake #2 одинаковый порядок типов.

### Debug

На bake: `C123 Edge` / `C124 Crouch` / `C125 Opening` / `C126 Window` / `C127 Corner` / `C128 Partial`. `Standing` как подпись типа не пишем. При выделении: Protection S/C и Capabilities. Mid-wall рисуется тусклее.

### Что #13.2B НЕ делает

```text
❌ юнит не выбирает эти позиции
❌ не резервирует и не двигается к ним
❌ не поворачивается, не peek, не lean
❌ не открывается/закрывается
❌ не стреляет через Opening/Window
❌ нет нового tactical scoring и нет ArmFatigue
❌ нет CoverPoint_* руками
```

Этап B = **#13.3**, отдельно: runtime `ProtectionZone + ThreatDirection → DesiredPosition`, затем Hidden↔Exposed. Не открывать из 13.2C.

---

## D.7 #13.2C Protection Geometry Bake (**OPEN**)

Смена модели генерации. Не новая система выбора. Не набор боевых позиций.

> Bake сообщает форму и возможности укрытия. Он не ставит солдата и не выбирает «лучшую сторону». Конкретную точку внутри геометрии считает **#13.3** от `ThreatDirection` + юнит + NavMesh.

#13 selection / occupancy / CoverScore **FROZEN**. `CoverType` runtime (#13) не расширяем отсюда.

```text
Geometry → ProtectionGeometry → Bake                    ← #13.2C сейчас
ProtectionGeometry + ThreatDirection + unit → DesiredPosition  ← #13.3, не открывать
DesiredPosition → Move → Occupy → Hidden / Peek / Exposed
```

### Что bake знает / не знает

```text
ЗНАЕТ                         НЕ ЗНАЕТ
Surface (непрерывная стена)   «эта сторона сейчас выгодна»
Boundary (торец / край)       DesiredPosition
Corner / Opening / Window     ThreatDirection
Obstacle (силуэт + OBB)       стойка юнита
Height / Depth / Normal       CoverType для AI
Capabilities (геометрия)      peek / fire как действие
```

Середина глухой стены — защитная поверхность, не слот «стояние №123». Runtime позже может взять **любое** место вдоль Surface.

### ProtectionGeometry

Запись bake. Не точка. Не CoverType.

Код сериализует прежние int (`Wall=0`, `Edge=1`, …). Смысл:

| GeometryType (код) | Имя в модели | Смысл |
|-----|--------|--------|
| **Wall** | **Surface** | непрерывный защитный сегмент. Не позиция AI |
| **Edge** | **Boundary** | геометрическая граница / торец. Не фиксированная точка стояния |
| **Opening** | Opening | один физический проход через толщину стены; не две стороны |
| **Window** | Window | Opening + `TacticalTransparent` |
| **Corner** | CornerPocket | только защищённый внутренний карман двух поверхностей; не внешний угол и не точка |
| **Obstacle** | Obstacle | замкнутый проп. OBB = 4 границы + 4 угла. Не 4 Crouch + 4 Corner |

`EdgeKind` (не отдельный CoverType): `WallEnd | ObjectEnd | BarrierEnd | OpeningJamb | RuinEdge`.

Boundary — **допустимый диапазон у торца**. Runtime выбирает сторону и конкретное место от `ThreatDirection`; bake хранит физический торец и направление наружу.

`CornerPocket` существует, только если обе поверхности:

- сходятся точным segment intersection или endpoint-to-segment T-стыком;
- имеют достаточно длинные полезные плечи от вершины;
- образуют один вогнутый открытый сектор;
- независимо подтверждают защиту по двум направлениям (rear + side).

Внешний угол и короткий выступ остаются `Surface/Boundary`. Bake хранит `CornerVertex`, два плеча, `CornerFacing` и допустимый сектор `CornerMinRadius..CornerMaxRadius + CornerHalfAngleDegrees`. Фиксированную точку `O` не хранить.

Один физический торец стены = одна `Boundary (WallEnd)` вокруг всего end-cap, а не по записи от каждой стороны стены. `Center` лежит на торце; `Axis/Width` описывают толщину стены; `EdgeDirection/Depth` — наружное направление и полезный диапазон. Левую или правую сторону выбирает #13.3.

`Opening` — пустой разрыв в `Surface`. Через проход нет Surface/Boundary-линии; существуют только два отдельных `OpeningJamb` на краях.

`Partial` / `Crouch` не геометрические типы. Высота + профили. **ThreatDirection не запекается.**

`NavMeshValid` — метаданные доступности, а не фильтр существования геометрии. Surface/Boundary/Corner остаются в bake даже без готовой точки стояния; конкретное место проверяет #13.3.

### Surface vs Boundary vs Obstacle

```text
Logical Surface
   ├── Boundary (WallEnd)     концы сегмента, в т.ч. короче 3 м
   ├── Boundary (OpeningJamb) торец у выреза проёма
   ├── Opening / Window
   └── Corner                 (топология, не точка)

Obstacle
   └── OBB: 4 границы + 4 угла внутри записи
       отдельные Edge/Corner зоны не плодятся
```

Любая геометрическая граница **может** быть Boundary, если это конец защитной поверхности. Порог «стена ≥ 3 м» для bake-зон снят. Полезность стороны относительно угрозы — не bake, а #13.3.

Квадратный ящик: одна Obstacle. Не 8 точек. Сторону выбирает runtime от `ThreatDirection`.

### Pipeline

```text
Small closed colliders → silhouette Obstacle
→ Physics faces; readable non-convex MeshCollider → vertical triangle contour
→ удалить внутренние торцы составных collider-модулей → MERGE
→ face-cluster Obstacle только для non-physics/test fallback
→ side openings → один physical Opening + два OpeningJamb
→ точный segment/T topology → только защищённые inner CornerPocket
→ Surface spans (минус проёмы) → side endpoints
→ противоположные стороны одного торца → один physical WallEnd BoundaryBand
→ внутренний seam / Corner-junction не создаёт WallEnd
→ Window tag → NavMesh/clearance как метаданные, не удаление геометрии
→ height/profile → GLOBAL dedup → BakedProtectionZoneRecord
```

Узкий jersey (~0.73×2.05×0.94): **#13.2C.10**, одна Obstacle до фильтра граней. Высокий `Barrier_Tall` (~3.3 м) идёт в Surface merge.

Обычный открытый gap не получает `CanOpen/CanClose`: для этого нужен отдельный semantic door marker.

Gizmo: Surface — тонкий wire-volume; WallEnd — отдельный прямоугольный band наружу от торца; CornerPocket — вершина, два плеча и кольцевой сектор допустимого положения; Opening — пустой разрыв с двумя jamb-маркерами без сплошной линии; Obstacle — OBB. Не оливковые шары каждые 2 м.

Play: `TacticalWorld` → `BakedProtectionZoneSource`. Геометрию заново не ищет.

### Подэтапы

| Подэтап | Содержание | Статус |
|---------|------------|--------|
| **13.2C.0–.9** | Модель зон, Surface/Opening/Window/Corner, Obstacle-кластер, gizmos, Play source | в коде |
| **13.2C.10** | Silhouette Obstacle для узких пропов (`Barrier_01`) | в коде |
| **13.2C.11** | Surface ≠ позиция; Boundary без порога 3 м; `EdgeKind`; Center на геометрии | в коде |
| **13.2C.12** | Топология арены: внутренние торцы/швы, physical Opening, только защищённый inner CornerPocket, один physical WallEnd band | **PASS** |
| **13.2C.13** | Контур readable non-convex MeshCollider; `Ruins_02` как составные Surface | в коде; voxel fallback отдельно при необходимости |

### Что #13.2C НЕ делает

```text
❌ юнит не выбирает геометрию и не идёт
❌ не считает DesiredPosition / DesiredFacing / ExposureMode
❌ не peek / lean / open / fire
❌ не трогает CoverScore / occupancy / CoverType #13
❌ нет CoverPoint_* руками
❌ Wall/Edge не становятся CoverType для AI
```

#13.3 — Zone Position Solver: `ProtectionGeometry[] + ThreatDirection + unit → DesiredPosition`. Не открывать.

---

# ЧАСТЬ E. ГОТОВНОСТЬ И НАПРАВЛЕНИЕ УГРОЗЫ

## E.1 Readiness (#14B OPEN, не FROZEN)

Независимая ось. Не поза. Не G6. Не задача AI.

```text
NotReady / Patrol / LowReady / HighReady / PreAim / Aim
```

Это **уровни**, не обязательный линейный workflow. `ReadinessState.Aim ≠ G6.Aim ≠ WeaponPose.Aiming`. **Aim ≠ Fire.** `RequestsFire == false` всегда.

Начальное спокойное: Recruit → NotReady; Soldier+ → Patrol.

Стимулы: HostileVisible, GunshotHeard, CombatActivity, HostileLost / CombatActivityExpired.

```text
Gunshot
 ├─ Perception / AI  → Search (Defense/Attack)
 └─ Readiness        → LowReady (Recruit/Soldier) или HighReady (Corporal+)
```

Звук **не** ставит Aim и не Fire.

```text
любое спокойное / промежуточное
            ↓ HostileVisible
           Aim
```

Прямые переходы разрешены (Patrol→Aim). Промежуточные `CurrentState` не посещаются. Разница рангов — **скорость**, не отдельный AI.

Приоритет: HostileVisible > CombatActivity (hold) > GunshotHeard > Calm.

Decay вниз по одному rung: Aim → PreAim → HeardThreatState → CalmState. Запрещено Aim→Patrol одним шагом. Прототип hold (не freeze): Aim ~6 с, PreAim ~4 с, Low/HighReady ~10 с; полный Aim→Calm порядка **15–25 с**. Instant-тесты 14B.0–14B.3: hold 1 с.

14B.2: Readiness → pose request → существующий CombatReadiness / ReadyHands. Hold не ломает intent. Engage не создаёт второй Auto поверх Readiness.

**ArmFatigue** (14B.6 ✅): 0..1, не ReadinessState. Load под нагрузкой / огнём, recovery без нагрузки. Влияет: AimTime ↑, RecoilControl ↓, TurnToTargetTime ↑ (и RecoilRecovery ↓ в 14B.7). Не двигает Readiness / G6 / RoE / Cover / Movement. Instant 14B.0–14B.5: load=0. Пороги лога 0.25 / 0.50 / 0.75. Rank load multipliers сейчас **1**.

14B.7 ✅: Perception → Readiness → ArmFatigue → боевые AimTime/Turn/Recoil. Новой механики нет.

Приёмка слоя: EditMode **252/0**, Play `Readiness_LAST.txt` **90/0**. #14B **не FROZEN**. #15 не открывать.

## E.2 Threat Direction Knowledge (#14C OPEN, не FROZEN)

Вопрос: **в каком направлении относительно меня, скорее всего, угроза?** Не точная позиция врага.

Состояния: None / Expected / Known / Stale.  
Источники: Visual LastKnown > Sound > AllyReport > InitialEstimate.

Снимок: Direction (XZ), Compass N…NW (**+Z = North, +X = East**), Confidence, Uncertainty°, Age, Source, State.

Прототип (не freeze): Expected **0.5 / 45°**; Visual **0.9 / 15°**; Sound **0.7 / 30°**; Report **0.6 / 35°**. Visual stale→Expected **8 с**. Sound Known→Stale **4 с**, Stale→Expected **4 с**. Expected **не** истекает до конца сессии.

Expected один раз при старте боя: `Normalize(EnemySpawnCenter − OwnSpawnCenter)` из существующих `CombatTestSpawnMarker` / точек спавнера. Новые scene objects не создавать. Neutral без Expected. Player и Enemy получают противоположные направления.

Обновление **по событиям** (Spawn, HostileVisible, HostileLost, Gunshot, AllyReport, Expiry), не polling.

Потребители (не CoverScore, не Move):

| Подэтап | Что |
|---------|-----|
| 14C.1 | overlay ориентации: стена между юнитом и угрозой = bonus; facing в центр сектора; deadband **12°** |
| 14C.2 | confidence/uncertainty масштабируют вес cover/facing |
| 14C.3 | `TacticalPositionPreference` поверх CoverScore; Occupied Stay Committed |
| 14C.4 | SignificantChange (≥ **50°** и conf ≥ **0.4**) → facing + CoverThreatFit; Occupied не сбрасывается; fatigue замедляет turn |
| 14C.5 | FaceOnly / Stay / RepositionAllowed. Δangle < **80°** или conf < **0.75** → FaceOnly. Fit Poor + лучший Good → RepositionAllowed. Occupied не снимается без флага. #13 исполняет только по флагу |

#14C по смыслу завершён (knowledge + потребители). **Не FROZEN**, пока не сказано. #13/#14 не reopen. #15 не открывать.

---

# ЧАСТЬ F. РАНГИ И ОРУЖИЕ

## F.1 Пять рангов

Не тактическая должность. Пресет навыков `UnitCombatRankDefinition` → `UnitCombatStats`. Ассеты: `Assets/GameData/Combat/Ranks/Rank_*.asset`.

Цикл: **Recruit → Soldier → Corporal → Veteran → Elite**.

| Asset | DisplayName | Marksmanship | Handling | Recoil Control | Реакция, с | Скан, с |
|-------|-------------|-------------:|---------:|---------------:|-----------:|--------:|
| Rank_Recruit | Recruit | 35 | 40 | 35 | 0.38–0.65 | 0.65–0.90 |
| Rank_Soldier | Soldier | 50 | 50 | 50 | 0.32–0.50 | 0.45–0.60 |
| Rank_Veteran | **Corporal** | 58 | 56 | 58 | 0.27–0.40 | 0.28–0.42 |
| Rank_Specialist | **Veteran** | 61 | 68 | 60 | 0.23–0.32 | 0.22–0.35 |
| Rank_Elite | Elite | 65 | 63 | 66 | 0.20–0.26 | 0.16–0.28 |

Шкала 0–100, **50 = нейтральные множители**. Marksmanship → θ ×1.25…×0.75. Handling → AimTime ×1.25…×0.75. Recoil Control → накопление ×0.8…×1.2 и обратное восстановление.

`UnitIndividualTraits`: ±10% к навыкам на сессию, не сохраняется. Внешность головы — `HeadAppearanceRankTable`.

Ранг **не** меняет RoE, CombatIntent, состояние AI, класс оружия. На арене 150×50 ранг спавнером **не** назначается явно — префаб / дефолт Soldier.

Поведенческий слой (позиция, CQB, экспозиция) — **#15B, не открыт**. Один AI, разный competence. Не пять AI.

## F.2 Три дальности (не смешивать)

| Понятие | Смысл |
|---------|--------|
| Обзор / hitscan | куда видит и куда луч: глаз 150, оптика до 300 |
| EffectiveRange (E) | падение урона; за 2E урон 0; **не запрет** |
| WorkingRange | дисциплина очереди, не потолок зрения |

## F.3 Классы и WorkingRange

| Класс | Примеры | Профиль дисциплины | WorkingRange |
|-------|---------|--------------------|-------------:|
| Pistol / SMG | — | CQB | 150 |
| Shotgun | Benelli M4 | Shotgun | 50 |
| Rifle | M4, AK-74, AK-47, M16 | Assault | 200 |
| LMG | M249, PKM, RPK | LMG | 220 |
| SniperRifle | SVD, Mosin, MK12 | Marksman/Sniper | 250–300 |
| HMG | M2 | Heavy | 300 |
| AGL | MK19 | Grenade | 300 |

Balance-kind задаёт кривые θ / AimTime / auto-spread по метрам (CqbShort, ShotgunCqb, Carbine, Intermediate545, Dmr, Support762, …). Ствол вне роли не запрещён.

На арене 150×50 (~140 м между дворами): Benelli — CQB комнаты; M4/AK — коридор; PKM/M249 — center knot; SVD/Mosin/MK12 — дальние маркеры; MK19/M2 — открытые участки.

AI выбор ствола «под дистанцию» как доктрина — **#15A, не открыт**. #12 даёт только маленький suitability-нюдж.

## F.4 Дизайн #15 (не открывать)

Weapon Role = **предпочтение, не запрет**. Снайпер может штурмовать. Приказ и миссия сильнее предпочтения.

| Роль | Предпочитает |
|------|----------------|
| Sniper | дистанция, overwatch, стабильная LOS |
| LMG | широкий сектор, support, стабильное укрытие |
| Rifle | универсальная тактика |
| Shotgun / CQB | близко, углы, комнаты |

Ранг как поведение (наблюдаемое, не отдельный FSM): Recruit — плохая позиция, медленные решения; Elite — быстрая оценка, минимум экспозиции.

---

# ЧАСТЬ G. ЖИЗНЬ ЮНИТА

Не UnitAIState. Источник: `UnitHealth` + `UnitConsciousness`. Координатор `UnitLifeGate`.

```text
Alive         → все контуры
Unconscious   → тело / визуал / Health остаются
                AI / nav / G6 / SELECT / SCAN / Fire / cover — стоп
                reservation — Released
Dead          → то же; объект не Destroy
```

`UnitVision` **не** disable (соседи держат цель в реестре). Scan в Update не идёт. NavMeshAgent: `isStopped` + `ResetPath`, компонент не выключаем.

**Сверка 29.08.2026:** чеклист wiring ожидает `UnitLifeGate` на префабе. В сохранённом `Unit.prefab` компонента **нет**; Install / `UnitActionLogBinder` могут добавить в рантайме. Это дыра wiring, не дыра модели состояний.

Лог: `LIFE life=Unconscious was=Alive …`. После unconscious не должно быть новых MOVE / COVER_* / SCAN / VISION / SELECT / G6 / SHOT.

**заполним позже:** медицина, drag, тяжёлые ранения (#17B).

---

# ЧАСТЬ H. ТЕСТОВАЯ ПЛОЩАДКА 150×50

`CombatTestArena_150x50` в SampleScene. Изолированный CQB-полигон. Не замена harness G-тестов.

| Параметр | Значение |
|----------|----------|
| Пол | **50 м (X) × 150 м (Z)** |
| Периметр | закрыт, без обходных флангов |
| Ось | Player yard Z≈5–18 → Center knot Z≈54–96 → Enemy yard Z≈132–145 |
| Центр атаки | локально **(0, 0, 75)** |
| Постройка | `Polygone/Combat Test/Build 150x50 Arena (SampleScene)` |

Маркеры: 10 Player + 10 Enemy + 20 Neutral. Neutral: без AI, без оружия. Боевые: AI enable + Attack к центру + RoE MissionCombat. Авто-волны опционально каждые 30 с.

Префаб: `Assets/Prefabs/Characters/Unit.prefab`. Прямое WASD выключено. `UnitClickToMove` есть. После `Polygone/Tactical AI/Install Arena Editor Wiring`: `UnitAIController` выключен, профили мира/тактики, debug draw. **Не** вешать на солдата `SharedCoverSpatialCache` / `CoverOccupancyBoard`.

### Чеклист Inspector (wiring 28.08.2026)

Play **не** создаёт тактическую инфраструктуру.

| Пункт меню | Что делает |
|------------|------------|
| `Polygone/Tactical AI/Install Arena Editor Wiring` | SO-профили, AI на префабе, child `TacticalWorld`, bake |
| `Polygone/Tactical AI/Bake Cover (TacticalWorld)` | повторный bake |
| `Polygone/Tactical AI/Validate Unit Prefab` | PASS/FAIL |
| `Polygone/Tactical AI/Validate Arena Wiring` | PASS/FAIL |

Rebuild арены уничтожает `TacticalWorld` → Install снова.

`TacticalWorld`: Profile = тот же `CombatArenaWorldProfile`; Bake Bounds local center `(0, 1, 75)`, size `(50, 4, 150)`; baked > 0.

`InfantryDefaultTacticalProfile`: UseCover, reservation, movement mode. Overlay: есть cover request → hop = слот; иначе hop к Destination приказа. Attack context overlay не переписывает (Cover ≠ Move).

G-regression Play спавн арены пропускает (`DetectionHarnessPlayMode`).

Что площадка **не** заменяет: G1–G8 числа, RecoilContract replay, #13/#14 smoke формул, H-баланс, зоны RestrictedDefense.

---

# ЧАСТЬ I. ИГРОК И RTS-МАРШРУТ

**заполним позже:** канон «как игрок ведёт бой» (камера, выделение, приказы vs рисование пути, техника от первого лица).

Сейчас в коде два параллельных контура пехоты:

1. **Тактический AI** (#13/#14): Attack/Defense → overlay → `TacticalNavigationExecutor` → `UnitNavLocomotionDriver.Walk`.
2. **RTS-маршрут игрока:** `RtsUnitSelectionManager` → `RtsUnitMember` (очередь waypoint + сегментные приказы Reload/Grenade/RPG, facing arrows, wait groups) → `UnitClickToMove` (Walk/Run/Sprint). Этот путь **минует** cover overlay.

Не смешивать: клик по земле игрока ≠ Search AI и ≠ Cover hop.

---

# ЧАСТЬ J. ТЕХНИКА

**заполним позже:** посадка/высадка, бой десанта, AI водителя vs стрелок, разрушение, мины как геймплей миссии.

Что уже есть и сверено как архитектура, не как игровой дизайн:

**Навигация техники (проектный контур).** `VehicleNavigation` → Feedback → `VehicleOrderQueue` → `DriverFSM` → Path/Driving/Maneuver planners → Pursuit → `MotionController` → `VehicleCommand` → `VehicleController`. Это движение по NavMesh/маневрам, не пехотный #14.

**Пакет CombatVehicleSystem** (`Assets/CombatVehicleSystem/`): drop-in URP, колёса/гусеницы, турель, оружие. Внешнее управление через `VehicleBrain.SetCommand`. Документация пакета живёт **рядом с пакетом**, не в этом файле (тюнинг 8 машин, FX).

**Зрение из техники:** пассажир и турель — Stage 13, тот же perception pipeline. Потолок «100 м из окна» снят.

Пехотный cover generator технику как геометрию укрытия **пропускает**.

---

# ЧАСТЬ K. БУДУЩИЕ СЛОИ (дизайн, не реализация)

Не открывать раньше номера. Детали формул — на этапе.

### #16 Group и CQB

Group: Leader, Members, Formation, Spacing, Role, Sector. Формации: Column, Line, Wedge, Compressed Column; позже CQB Stack. Лидер задаёт direction / tempo / objective / formation — не Transform-follow. В контакте формация **может распасться**.

CQB рамка: один вход → stack → последовательный вход → сектор на бойца. Не решать заранее slice-the-pie, dead space, вертикальный CQB.

### #17–#23 Adaptive combat

Понятия **не смешивать:** Threat, ImmediateThreat, Suppression, Wound.

Under Fire: return fire | cover | move-to-cover — зависит от rank, weapon, distance, cover, mission. Wound: hit → can fight? → cover/continue или emergency. Suppression снижает risk tolerance. Reposition только если новая позиция **достаточно лучше**. Adaptive attack: ATTACK → RESISTANCE → ASSESS → continue | change plan. Grenade — tactical action в ряду Shoot/Move/Suppress/Flank/Withdraw, не отдельная вселенная.

На префабе уже есть throw controller — это не открытие #23.

### #24–#26 Commander и Planner

Commander: Attack / Defend / Flank / Withdraw / Search / Hold / Capture — **не** «поверни на 13°». Squad: assault / support / cover / reserve. Planner решает **что попытаться сделать**. Не управляет спуском.

```text
Decision → Command → State → Execution
```

---

# ЧАСТЬ L. ОПТИМИЗАЦИЯ (сквозное)

Не отдельный этап.

| Уровень | Частота | Примеры |
|---------|---------|---------|
| Continuous | каждый кадр | movement, weapon, animation, critical combat |
| Fast | часто | perception, target, aim |
| Tactical | по событию | cover eval, reposition |
| Strategic | редко | squad, commander, planner |

O1 event-driven. O2 локальные query. O3 сужение кандидатов. O4 cache. O5 тактика не на Update 60 Гц. O6 группа шарит геометрию. O7 LOD по дистанции до игрока.

---

# ЧАСТЬ M. ДИАГНОСТИКА

Логировать **смену решений**, не каждый кадр. Editor Play сам пишет папку `Infantry_*`.

```text
Infantry_YYYYMMDD_HHMMSS/
  _index.txt
  _timeline.log
  Player/P01_….log
  Enemy/…
  Neutral/…
```

Порядок чтения: `_index` → `_timeline` (кто кого задел) → файл юнита. Первый разрыв цепочки = виновный слой.

Существующие теги:

```text
VISION  SELECT  G6  DISC  GATE  SHOT  PROJECTILE
SCAN    SOUND
MOVE    AI      CMD  GAMECMD  INPUT  SNAP  DEATH  LIFE
COVER_* POSITION_*  COVER_STATE  COVER_HEARTBEAT
READINESS*  ARM_FATIGUE*
THREAT_DIRECTION*  FACING_*  TACTICAL_POSITION  THREAT_REPOSITION
```

Цепочки:

```text
VISION → SELECT → G6 → DISC → GATE → SHOT / PROJECTILE
INPUT → GAMECMD → CMD → AI → MOVE
NeedCover → COVER_DECISION → MOVE_COVER → POSITION_ACQUIRE → Occupied
Perception → READINESS_TRANSITION → ARM_FATIGUE → боевой AimTime/Recoil
```

Поля, которые нельзя путать: `AI.EngageTarget` vs `Combat.SelectedTarget`; `cover=0` vs `cover=C1`; `life=` vs `ai=`; ImmediateThreat vs Threat High; Readiness.Aim vs G6.Aim vs поза Aiming.

Harness-меню: `Tools/Tests/Run Regression (Play)` / `(EditMode)`. Одиночные слои — соответствующие `Run … (Play|EditMode)`. Актуальные `*_LAST.txt` лежат в `Assets/_Docs/Logs/Tests/`.

---

# ЧАСТЬ N. ЧТО НЕ ДЕЛАТЬ СЛЕДУЮЩИМ ШАГОМ

- ретюнить Q / память 5/30 / IdentifyTime / Threat 25/80 «чтобы AI стал умнее»;
- чинить кривые, РПГ или дисциплину увеличением обзора или E;
- целиться или стрелять в LastKnown;
- считать Threat High приказом огня;
- сливать `AI.EngageTarget` и `Combat.SelectedTarget`;
- открывать #15 / группу / командир / planner, пока не сказано;
- reopen #13 выбор / occupancy / CoverScore; #13.2C трогает только геометрию bake;
- reopen #14 ради Readiness, ThreatDirection или occupy;
- открывать **#13.3** (DesiredPosition внутри зоны, Hidden↔Exposed, Step/Peek/Lean/Open/Fire) без отдельной команды;
- поднимать диск occupy 0.60 из-за `OutOfTolerance` dest≠slot;
- одновременно чинить обнаружение, личность, выбор и стрельбу;
- добавлять CoverPoint_* руками.

Повесили AI и нет огня: сначала RoE (SelfDefense без ImmediateThreat), не Q.

---

# ЧАСТЬ O. КАРКАС ОДНОЙ ФРАЗЫ

Солдат видит честно в 150/300, помнит место, понимает кто это, выбирает цель сам, стреляет через RecoilOffset и дисциплину, получает задачу Attack/Defense/Search, ищет по области улики, занимает baked-слот, поднимает готовность до Aim отдельно от спуска, знает направление угрозы отдельно от позиции врага, и **никогда не жмёт спуск из тактики**.

---

## Журнал этого документа

| Версия | Дата | Изменение |
|--------|------|-----------|
| 1.0 | 29.08.2026 | Слияние бывших пехотных доков, Closed-контрактов и каталогов в один канон по всей игре. Сверка с кодом: AI на префабе выключен; звук/доклад живые; Defense ходит к якорю; UnitLifeGate на ассете отсутствует; #14B/#14C OPEN. Пробелы продукта помечены. Play-карточки юнитов не переносились (живут в Logs). |
| 1.0 | 29.08.2026 | Файл переименован в `Game_Design.md`. |
| 1.1 | 29.08.2026 | **#13.2B OPEN** (этап A bake). Новые геометрические типы Edge/Opening/Window; Standing — protection, не CoverType. Выбор/occupancy #13 FROZEN. Поведение юнита — этап B, не сейчас. |
| 1.2 | 29.08.2026 | #13.2B.0–.1 в коде: Edge bake без peek-точек; Standing остаётся legacy CoverType до 13.2B.5. EditMode Extended Cover Bake 12/12; 13.1+13.2 52/52. |
| 1.3 | 29.08.2026 | **#13.2B.2 Opening PASS.** Один проём = одна база. Ложные gap между короткими пропами отсекаются. Следующий — Window. |
| 1.4 | 29.08.2026 | **#13.2B.3 Window PASS.** Opening без стекла остаётся Opening; Opening + `TacticalTransparent` = Window. Стекло не стена. Play читает bake. Следующий — Corner (две поверхности). |
| 1.5 | 29.08.2026 | **#13.2B.4 Corner PASS.** Пара поверхностей → один Corner с Facing в открытый сектор. Конец стены остаётся Edge. Legacy A5 classify-only не сломан. Следующий — 13.2B.5 типы. |
| 1.6 | 29.08.2026 | **#13.2B.5 PASS.** Standing больше не PrimaryType; mid-wall = None + StandingProfile. Этап A bake набора позиций закрыт. Runtime usage — отдельно. |
| 1.7 | 29.08.2026 | **#13.2B.5A PASS.** Logical wall merge до Edge/Opening/Corner. Стык префабов не Opening; дверь после merge. Cap 16 не поднимали. Нужен re-bake TacticalWorld. #13.3 не открыт. |
| 1.8 | 29.08.2026 | **#13.2C OPEN.** Bake = Protection Zones, не точки солдата. Scene baker пишет зоны; mid-wall кандидаты не пекутся. #13 selection/occupancy FROZEN. #13.3 не открыт. |
| 1.9 | 29.08.2026 | **#13.2C.10.** Узкий проп (`Barrier_01` 0.73×2.05) → одна Obstacle из силуэта коллайдера, без порога грани 0.8 м. Высокий Barrier_Tall остаётся в merge. Edge/Corner/mesh contour не открыты. |
| 1.10 | 29.08.2026 | **#13.2C = Protection Geometry.** Wall = Surface (не CoverType). Edge = Boundary + EdgeKind, не точка и не порог 3 м. Obstacle OBB несёт 4 границы. #13.3 не открыт. |
| 1.11 | 29.08.2026 | **#13.2C.12–.13 arena topology.** `Ruins_02` читает вертикальный mesh-контур вместо AABB; внутренние торцы модульных стен удаляются до merge; T-стык не создаёт Boundary внутри host-wall; проход = один Opening + два jamb; Corner хранится в вершине с двумя плечами; отсутствие NavMesh не удаляет ProtectionGeometry. Bake 534: Surface 159, Boundary 260, Opening 39, Corner 22, Obstacle 54. EditMode ProtectionGeometry 32/32. |
| 1.12 | 29.08.2026 | **#13.2C Protected Corners + Endcaps PASS.** Corner теперь только подтверждённый внутренний карман с двумя полезными плечами и rear+side protection; точка `O` заменена диапазоном сектора. Две стороны торца объединяются в один `WallEnd BoundaryBand`; Opening рисуется пустым gap с двумя jamb. Re-bake 473: Surface 159, Boundary 155 (`WallEnd` 77 + `OpeningJamb` 78), Opening 39, CornerPocket 66 (`non-inner=0`), Obstacle 54; Surface bridges через проёмы = 0, каждый проём имеет ровно два jamb. Cover EditMode 124/124. #13.3 не открыт. |
