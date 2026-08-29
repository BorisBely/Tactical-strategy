# Пехота: зрение, бой, тактический AI

**Дата съёмки:** 24 августа 2026.  
**Назначение:** самодостаточное описание поведения пехоты — что видит, как решает стрелять или нет, как работает тактический AI, зачем тестовая площадка, что сделано и что планируется. Документ рассчитан на анализ **вне репозитория**: все ключевые контракты, числа приёмки и философия собраны здесь, без отсылок к другим файлам.  
**Дизайн конечной системы:** `Пехота_система_дизайн.md` (v1.3). **Рабочий backlog:** `Пехота_дорожная_карта.md` — этапы с привязкой к §6 дизайн-дока.  
**Источник:** код, префаб юнита, Play-отчёты регрессии.  
**Прежнее имя файла:** `Пехота_зрение_поведение_стрельба.md`.

### Статус слоёв (снимок 26.08.2026)

| Блок | Статус |
|------|--------|
| Зрение, память, идентификация (этапы 1–18 perception) | **CLOSED / FROZEN** — Final Perception **PASS 49/0** |
| Боевой контур G6 + оружие + hitscan | **FROZEN** |
| A10 — новая стрельба и отдача (`RecoilOffset`) | **CLOSED 24.08.2026** — G/H/RecoilContract **PASS** |
| CombatIntent (этап 2) | **FROZEN** — Play **PASS 31/0** |
| Search / Attack / Retreat / Flee (этапы 3–4) | **FROZEN** |
| Игровые приказы (этап 6.1–6.4) | **CLOSED** |
| Use of Force / RoE матрица | **FROZEN** — Play **PASS 107/0**; ImmediateThreat **#7 CLOSED 24.08.2026** |
| Ранги юнитов (5 пресетов навыков) | Готово на префабе (`UnitCombatStats`); поведенческий слой — #15B |
| Оружие по дистанции (классы, E, WorkingRange) | Готово, A3/A9/A10 CLOSED; тактические роли — #15A |
| Тактическое **#7** ImmediateThreat + живой RoE | **CLOSED 24.08.2026** — A 10/0, B 11/0, C 3/0, D 18/0, E 107/0 + 36/0 |
| Тактическое **#10** Search 2.0 | **CLOSED / FROZEN 26.08.2026** — Play 22/0; регрессия #7–#9 PASS |
| Тактическое **#11** Command priority | **CLOSED / FROZEN 26.08.2026** — EditMode 18/0; Play 18/0; регрессия #7–#10 PASS |
| Тактическое **#12** Target + fire calibration | **CLOSED / FROZEN 26.08.2026** — EditMode 18/0; Play 26/0; регрессия 62/0 + 114/0 |
| Тактическое **#13** Dynamic Cover | **CLOSED / FROZEN 27.08.2026** — EditMode 169/0; Play `CoverIntegration_LAST.txt` 18/0; `Closed/Dynamic_Cover.md` |
| Индивидуальная тактика **#14** Tactical Movement | **CLOSED / FROZEN 27.08.2026** — EditMode 178/0; Play 157/0; `Closed/Tactical_Movement.md` |
| **#14B** Readiness State | **OPEN** — 14B.0–14B.7 ✅ 252/0 + 90/0. `Readiness_State.md`. Aim ≠ Fire |
| **#14C** Threat Direction Knowledge | **OPEN** — 14C.0–14C.6 ✅ 49/0 + 20/0. 14C.1 ✅ 40/0 + 22/0. 14C.2 ✅ 38/0 + 19/0. 14C.3 ✅ 43/0 + 18/0. 14C.4 ✅ 36/0 + 18/0. **14C.5 ✅** 39/0 + 18/0. `Threat_Direction_Knowledge.md` |
| Индивидуальная тактика #15 (weapon+rank) | не открывать |
| Группа / CQB #16, адаптивный бой #17–#23, командир/planner #24–#26 | не начинать раньше своего номера |

---

## Как читать

Три системы уже существуют и **не смешиваются**:

1. **Зрение** — что видит и помнит солдат, кем считает цель. Не ходит и не стреляет.
2. **Боевой контур** — выбирает цель, называет Track / Aim / Fire, ведёт оружие и реально стреляет. Не читает приказы AI.
3. **Тактический AI** — задача (держать / атаковать / искать), CombatIntent Hold/Engage, правила применения силы (RoE). Search ищет по области последнего контакта. **Сам не жмёт спуск.**

На префабе юнита живёт **только боевой контур**. `UnitAIController` на префаб **нет**. Арена и отладочные оверлеи добавляют AI в рантайме.

Диагностика по слоям:

```
плохо увидел  ≠  плохо понял кто это  ≠  плохо выбрал  ≠  плохо решил стрелять  ≠  плохо пошёл искать
```

---

## Философия и конечное видение

**Цель проекта** — тактическая пехота уровня «один солдат = исполняющий контур», а не «умный бот в вакууме». Каждый слой отвечает на свой вопрос; слои соединяются узкими контрактами, чтобы при сбое было видно **где** сломалось.

**Конечное видение (не текущая реализация):**

- Игрок и AI используют **один и тот же** боевой контур: те же лучи, те же правила Track/Aim/Fire, та же отдача и дисциплина огня.
- Тактический AI **не стреляет напрямую** — он задаёт задачу, CombatIntent и вето силы; выстрел всегда проходит через оружие.
- RoE различает «видел врага» и «можно стрелять»: SelfDefense требует ImmediateThreat; MissionCombat — нет.
- Звук и доклады союзников **дополняют** зрение, но не подменяют его (звук ≠ Observed, звук ≠ автоматический Fire).
- Отряд, укрытия и командир строятся **поверх** стабильного одиночного исполнителя, а не вместо него.

**Принцип разработки:** сначала заморозить слой приёмкой (EditMode + Play), потом открывать следующий. Не «чинить AI ретюном зрения». Не добавлять BT/GOAP, пока не работает цепочка Decision → Command → State → Execution для одного юнита.

---

## Решение «стрелять или нет» — полная цепочка

Выстрел — результат **последовательных гейтов**. Любой гейт может остановить Aim/Fire; Track обычно остаётся.

```text
Мир (противники, укрытия, звук)
        ↓
Скан лучами: FOV, дистанция, LOD, hit-зоны
        ↓
Контакт: DetectionProgress, Identity, Relationship, Threat
        ↓
┌─────────────────────────────────────────────────────────────┐
│ БОЕВОЙ КОНТУР (всегда на префабе)                           │
│   TargetSelector → своя выбранная цель                        │
│   EngagementDecisionMath (G6) → Track | Aim | Fire | Ignore │
└─────────────────────────────────────────────────────────────┘
        ↓  (если есть UnitAIController)
┌─────────────────────────────────────────────────────────────┐
│ ТАКТИЧЕСКИЙ AI (только если компонент добавлен)              │
│   UnitAIState: Idle / Defense / Attack / Search / …         │
│   UnitAIAction: Hold | Engage (не стреляет сам)              │
│   → CombatIntent: Hold | Engage                               │
│   → UseOfForceEvaluator: Allow | Deny (по RoE + Relationship)│
└─────────────────────────────────────────────────────────────┘
        ↓
EngagementDecisionController — порядок гейтов:
  1. G6 raw (DefaultCombatEngagementPolicy)
  2. RoE: Denied → Fire/Aim → Ignore; Track не трогать
  3. CombatIntent Hold: Fire/Aim → Ignore
        ↓
CombatReadiness: с Readiness — pose request; без AI — Engage → Auto; Hold не ломает intent
        ↓
Fire discipline + FireController
        ↓
Hitscan (потолок = текущий ResolvedMaxRange обзора: 150 м глаз / до 300 м с оптикой)
        ↓
Выстрел (θ-конус + RecoilOffset kick)
```

### Ключевые равенства (закон архитектуры)

```text
увидел           ≠  выбран
выбран           ≠  можно целиться
можно целиться   ≠  решение Fire
решение Fire     ≠  выстрел ушёл
разрешена сила   ≠  стрелять
Engage (AI)      ≠  Fire (G6)
AI.EngageTarget  ≠  Combat.SelectedTarget   (наблюдаемый факт, не баг-фикс)
Hold (AI)        ≠  выключить Combat       (Track остаётся)
ImmediateThreat  ≠  ThreatLevel.High       (отдельный injected bool)
Readiness.Aim    ≠  G6.Aim  ≠  Fire  ≠  поза Aiming
```

### Тактическое действие Hold / Engage

| Состояние AI | Hostile + VisibleNow | Action | CombatIntent | Эффект на бой |
|--------------|----------------------|--------|--------------|---------------|
| Idle | да | None | — | AI не начинает тактику сам |
| Defense / Attack | да | **Engage** | Engage | Aim/Fire разрешены гейтами ниже |
| Defense / Attack | нет (или Unknown) | **Hold** | Hold | Aim/Fire → Ignore |
| Search / Retreat / Flee | — | None | Hold | Aim/Fire → Ignore |

`CurrentEngageTarget` задаётся только при Engage (макс. Threat среди Hostile+VisibleNow). **Engage не вызывает** TargetSelector и не стреляет.

### Пять уровней применения силы (RoE)

По умолчанию на новом `UnitAIController`: **SelfDefense**. Смена — любой уровень в любой, не лестница.

| Level | Смысл |
|-------|--------|
| SelfDefense | сила только против Hostile при **ImmediateThreat=true** |
| RestrictedDefense | сила против Hostile (зона позже; матрица как MissionCombat) |
| MissionCombat | сила против Hostile |
| FullEngagement | сила против Hostile |
| NoFriendlyFire | сила против всех, кто не Friendly |

**Матрица (Relationship, не UnitTeam мира):**

| Policy | Friendly | Neutral | Unknown | Hostile без threat | Hostile + ImmediateThreat |
|--------|----------|---------|---------|--------------------|---------------------------|
| SelfDefense | NO | NO | NO | NO | YES |
| RestrictedDefense | NO | NO | NO | YES | YES |
| MissionCombat | NO | NO | NO | YES | YES |
| FullEngagement | NO | NO | NO | YES | YES |
| NoFriendlyFire | NO | YES | YES | YES | YES |

**#7 CLOSED 24.08.2026:** SelfDefense без ImmediateThreat = «не стрелять»; источник (`ImmediateThreatSource`, hitscan IncomingFire/ConfirmedHit) живой. Live PASS — Immediate Threat Live 18/0, Combat Engage T3b, Use of Force Play 107/0.

Порядок в evaluator: нет контакта → Denied; Friendly → Denied; далее switch по Level.

### A10 — модель стрельбы (закрыта 24.08.2026)

До A10 стрельба считалась «некорректной» (старый RecoilPenalty раздувал конус). **A10 закрыт:**

- Накопленная отдача: **RecoilOffset** (градусы yaw/pitch), kick за выстрел.
- Конус **θ** (intrinsic spread) **не зависит** от отдачи.
- Регрессия: RecoilContract PASS, G-TEST 10/10, H-TEST 11/11, F2 Benelli replay.
- **Заморожено:** фазы A–F баланса, G/H runners, формулы Semi/Auto/LMG.
- **Вне A10 (отдельный tuning stage):** Benelli (θ/spread), M2 (тяжёлый recoil stack), Review-кандидаты AK-74/PKM/MK12/SVD.
- **Не входит в A10:** ShooterControl, locomotion лёжа, visual back-first.

Живой стрелок на префабе теперь использует **закрытую** модель; тактический AI по-прежнему не на префабе.

---

## Ранги юнитов — устройство и влияние

У каждого юнита есть **боевой ранг** — не тактическая должность и не звание в AI-приказах, а **пресет навыков** (`UnitCombatRankDefinition`), который задаёт, насколько солдат меток, быстр и устойчив в бою. Ранг хранится в `UnitCombatStats` на префабе или назначается при спавне; при `Awake` пресет применяется автоматически, если включён `ApplyRankPresetOnAwake`.

### Пять рангов (порядок прокачки)

Цикл на полигоне и в отладке: **Recruit → Soldier → Corporal → Veteran → Elite**.  
Ассеты: `Assets/GameData/Combat/Ranks/Rank_*.asset`. Создаются baker'ом `Polygone/Combat Balance/Create Unit Combat Rank Assets`.

| Ранг (asset) | Отображаемое имя | Marksmanship | Weapon Handling | Recoil Control | Реакция (сек) | Интервал скана (сек) |
|--------------|------------------|-------------|-----------------|----------------|---------------|----------------------|
| Rank_Recruit | Recruit | 35 | 40 | 35 | 0.38 – 0.65 | 0.65 – 0.90 |
| Rank_Soldier | Soldier | 50 | 50 | 50 | 0.32 – 0.50 | 0.45 – 0.60 |
| Rank_Veteran | Corporal | 58 | 56 | 58 | 0.27 – 0.40 | 0.28 – 0.42 |
| Rank_Specialist | Veteran | 61 | 68 | 60 | 0.23 – 0.32 | 0.22 – 0.35 |
| Rank_Elite | Elite | 65 | 63 | 66 | 0.20 – 0.26 | 0.16 – 0.28 |

**Шкала навыков:** 0–100, **50 = обученный базовый уровень** с нейтральными множителями. Выше — лучше, ниже — хуже.

### Что даёт ранг (механика)

Ранг через `UnitCombatRankDefinition.ApplyTo()` записывает в `UnitCombatStats`:

| Параметр | Влияние |
|----------|---------|
| **Marksmanship** | Множитель разброса θ: при 0 → ×1.25 к конусу, при 100 → ×0.75 |
| **Weapon Handling** | Множитель времени прицеливания: при 0 → ×1.25, при 100 → ×0.75 |
| **Recoil Control** | Множитель накопления отдачи (×0.8…×1.2) и восстановления (обратная шкала) |
| **ReactionTime** | Случайная задержка в диапазоне min–max при реакции на приказ / обнаружение |
| **VisionScanInterval** | Как часто пересканирует цели; у элиты интервал короче → быстрее переключение |
| **WeightPenaltyReduction** | Снижение штрафа перегруза (0 = нет бонуса; у старших рангов может быть > 0) |

**Дополнительно поверх ранга** — `UnitIndividualTraits`: при старте сессии случайные модификаторы **±10%** к Marksmanship, Handling, RecoilControl и к «личности» огневой дисциплины (агрессия очереди / темп пауз). Не сохраняются между запусками.

**Выносливость в готовности:** Recruit и Soldier считаются «младшими» — быстрее тратят stamina в позе ready; Corporal и выше — «старшие», расход ниже.

**Внешность головы** привязана к рангу через `HeadAppearanceRankTable` (при смене ранга обновляется визуал).

### Чего ранг не делает

- Не меняет RoE, CombatIntent, тактическое состояние.
- Не подменяет класс оружия и дистанционные кривые ствола.
- Не даёт отдельных AI-приказов или приоритетов (#11 FROZEN).
- На тестовой арене 150×50 ранг **не назначается спавнером явно** — берётся с префаба / дефолт Soldier.

---

## Оружие — классы и эффективность по дистанции

В проекте **не все стволы одинаковы**: у каждого есть класс, рабочий диапазон дисциплины огня, кривые точности/прицеливания по метрам и своя **эффективная дальность урона**. Близкий бой выигрывает дробовик и короткий карабин; средняя дистанция — штурмовая винтовка; дальняя — marksman/DMR/снайперка и пулемёт.

### Три разных «дальности» (не смешивать)

| Понятие | Смысл | Примеры значений |
|---------|--------|------------------|
| **Обзор / hitscan** | Потолок, куда солдат **видит** и куда может направить луч | Глаз **150 м**; с боевой оптикой до **300 м** |
| **EffectiveRange (E)** | Дистанция патрона/ствола для **падения урона**; за **2×E** урон = 0. **Не запрещает** выстрел | M4 ≈ **140 м**; снайперская платформа ≈ **225 м**; MK19 ≈ **300 м** |
| **WorkingRange** | Нормализованный диапазон **огневой дисциплины** (длина очереди, паузы, порог Aim). **Не** потолок зрения | См. таблицу классов ниже |

```text
близко к цели  →  короче паузы, длиннее очередь, ниже порог Aim
далеко         →  полуавтомат, длиннее Aim, короче серии
снайпер/МГ     →  на VeryFar почти только одиночные / 1–2 выстрела
```

Нормализация: `distance / workingRange` → пояс 0…1 с гистерезисом **0.08** (Close / Near / Mid / Far / VeryFar).

### Классы оружия (WeaponClassType)

| Класс | Роль | Типичные примеры в проекте |
|-------|------|----------------------------|
| Pistol | запасное, CQB | — |
| SubmachineGun | CQB | — |
| Shotgun | **ближний** бой | Benelli M4 |
| Rifle | универсальный / штурм | M4, AK-74, AK-47, M16 |
| LightMachineGun | подавление, средняя–дальняя | M249, PKM, RPK |
| SniperRifle | **дальний** точный огонь | SVD, Mosin, MK12 |
| HeavyMachineGun | дальняя огневая поддержка | M2 |
| AutomaticGrenadeLauncher | дальняя косвенная | MK19 |

### Профили огневой дисциплины (WorkingRange)

Класс дисциплины (`WeaponFireDisciplineProfileKind`) выводится из `WeaponClassType` и/или balance-kind ствола (`WeaponDistanceCurveLibrary`):

| Профиль | WorkingRange | Сильная сторона |
|---------|--------------|-----------------|
| **Shotgun** | **50 м** | Максимум урона и плотности в комнатах и на 0–25 м |
| **CQB** (пистолет, ПП, короткий карабин) | **150 м** | Быстрый огонь, HipFire/PointAim на короткой |
| **Assault** (штурмовая винтовка) | **200 м** | Sweet spot средней дистанции; основа пехоты |
| **LMG** | **220 м** | Длинные очереди, удержание сектора |
| **Marksman** | **250 м** | Точечный огонь на средне-дальней |
| **Sniper** | **300 м** | Одиночные / короткие серии на дальней |
| **Heavy / Grenade** | **300 м** | Подавление и косвенный огонь |

### Balance-kind — индивидуальные кривые стволов

Поверх класса у каждой платформы свой **WeaponBalanceKind** с ключевыми кадрами кривых **разброса θ**, **времени прицеливания** и **разброса автоматической очереди** по метрам:

| Balance-kind | Роль по дистанции | Примеры оружия |
|--------------|-------------------|----------------|
| CqbShort | лучше вблизи, хуже на дальней | MK18, AK-74U |
| CqbControlled | контролируемый CQB | AK-47S (складной приклад) |
| ShotgunCqb | дробь, резкий спад | Benelli |
| Carbine / CarbineModA1/A2 | штурмовой карбин | M4 варианты |
| Intermediate545 | средний калибр 5.45 | AK-74 |
| BattleRifle762* | тяжелее, чуть лучше на средней | AK-47 семейство |
| MidRifle | полноразмерная винтовка | M16A |
| Marksman | оптимум на средне-дальней | M16A4 marksman |
| Dmr | дальняя точность | MK12 |
| Support762 / Support545 | пулемётное подавление | RPK-47/74, PKM |
| HeavySupport | крупнокалибер | M2 |
| GrenadeSupport | гранатомёт | MK19 |

Кривые задают **sweet spot** и деградацию вне роли: на своей дистанции ствол точнее и быстрее прицеливается; вне роли — растут θ и AimTime (но выстрел не запрещён, пока цель в обзоре).

### Практический пример на одной карте

На арене **150×50** (ось боя ~140 м между дворами):

- **Benelli** — доминирует в South/North CQB и комнатах; на дальнем конце арены эффективность падает (E и WorkingRange 50 м).
- **M4 / AK** — основной баланс на всей длине коридора; оптимум середина карты.
- **PKM / M249** — сильны с укрытий в center knot, длинные очереди.
- **SVD / Mosin / MK12** — выигрывают с дальних маркеров и открытых секторов, но требуют Aim и времени.
- **MK19 / M2** — крайние дистанции и открытые участки; на CQB избыточны.

Отдача (A10): kick **RecoilOffset** зависит от ствола, режима огня, позы и **RecoilControl** ранга стрелка; конус **θ** от дистанции и balance-kind, не от накопленной отдачи.

### Статус реализации

| Аспект | Статус |
|--------|--------|
| Классы, WorkingRange, дисциплина по поясам | **CLOSED** — Fire Discipline A3 PASS |
| Кривые WeaponDistanceCurveLibrary | **CLOSED** — этап 10 кривых PASS |
| EffectiveRange и падение урона | **CLOSED** — этап 9 PASS |
| Отдача RecoilOffset по стволам | **CLOSED** — A10 PASS |
| AI выбор ствола «под дистанцию» | **нет** — #12 калибровка цели и огня |
| Tuning отдельных кандидатов (Benelli θ, M2 recoil, Review AK-74/PKM/MK12/SVD) | **отложено** — отдельный balance stage вне A10 |

---

## Тестовая площадка 150×50 м

### Зачем создана

**CombatTestArena_150x50** в SampleScene — изолированный **CQB-полигон** для ручной и полуавтоматической проверки боевого контура **вне** harness G-тестов и калибровочных сцен. Задачи площадки:

1. **Сквозной бой** на коротких и средних дистанциях с укрытиями, комнатами и перекрёстным огнём.
2. **Разнообразие оружия** — уникальные классы на каждой стороне, fill-киты для оставшихся слотов.
3. **Нейтралы** — гражданские без оружия для будущих проверок RoE / NoFriendlyFire (сейчас 20 маркеров).
4. **Тактический AI в бою** — `UnitAIController` живёт на `Unit.prefab` (выключен по умолчанию). Арена только включает его у Player/Enemy и даёт `SetAttack`. Cover cache / occupancy — на сценовом `TacticalWorld`, не на солдате.
5. **Не ломать** основной `UnitSceneSpawner` (polygon / G-regression): арена со своим `CombatTestArenaSpawner`, `m_SpawnOnStart` у сценового спавнера отключается при wire-build.

Предыдущая версия **300×100** заменена на компактную **150×50** для плотного CQB и быстрого обзора камеры.

### Геометрия и зоны

| Параметр | Значение |
|----------|----------|
| Размер пола | **50 м (X) × 150 м (Z)** |
| Периметр | закрыт барьерами Tall_Group, **без обходных флангов** |
| Ось боя | Player yard (Z≈5–18) → Center knot (Z≈54–96) → Enemy yard (Z≈132–145) |
| Центр тактики | локальная точка **(0, 0, 75)** — цель Attack после спавна |
| NavMesh | отдельный asset на арене |
| Постройка | меню `Polygone/Combat Test/Build 150x50 Arena (SampleScene)` |

**Зоны (по Z):**

- **Player yard** — стартовые «комнаты» игрока, укрытия Jersey/Hesco/бочки, флаг у южного края.
- **South CQB** — кирпичные комнаты, узкие коридоры, медицинский знак.
- **Center knot** — перекрёсток, две контрольные комнаты, дорожный барьер, плотный огонь.
- **North CQB** — зеркальные комнаты севернее центра.
- **Enemy yard** — зеркало player yard у северного края.

Внутренние стены: Tall_01. Периметр: Tall_Group_01. Только **малые** укрытия (jersey, hesco, ящики) — без больших зданий.

### Спавн и состав сил

**Маркеры:** 10 Player + 10 Enemy + 20 Neutral (`CombatTestSpawnMarker`).

| Сторона | Расположение | Поведение после спавна |
|---------|--------------|------------------------|
| Player | Z≈11 и Z≈15, X сетка −18…+18 | `UnitAIController` + Attack к центру (75 м) + RoE стороны |
| Enemy | Z≈139 и Z≈135, yaw 180° | то же |
| Neutral | 20 точек внутри арены (CQB зоны) | без AI, без оружия (Civilian-01) |

**Киты (типичная конфигурация после bake):**

- Player: 5 unique + 5 fill (M-line), 4 типа шлемов, 5 типов гранат ×2, IFAK ×2.
- Enemy: 3 unique (Mosin/SVD/PKM) + 12 fill (AK-серия).
- Авто-волны: опционально каждые 30 с (смещение ±1.5 м), Player+Enemy без нейтралов.

Спавнер **не использует** стартовый спавн сцены; при G-regression Play спавн арены **пропускается** (`DetectionHarnessPlayMode`).

Префаб юнита — `Assets/Prefabs/Characters/Unit.prefab`. Тело, инвентарь, зрение, бой, `UnitNavLocomotionDriver`, `NavMeshAgent`, `RtsUnitMember`, `UnitClickToMove` (прямое WASD выключено), `UnitSpineLean`. После меню `Polygone/Tactical AI/Install Arena Editor Wiring`: на префабе есть **`UnitAIController` (выключен)**, профили мира/тактики, `CoverCandidateDebugDraw`, `TacticalMovementDebugDraw`. **Не** вешать на солдата `SharedCoverSpatialCache` / `CoverOccupancyBoard` — это объекты `TacticalWorld` на сцене.

### Editor-time wiring (чеклист Inspector, 28.08.2026)

Play **не** создаёт тактическую инфраструктуру. Сцена и префаб готовятся в Editor. Формулы CoverScore / RouteScore **не** трогаем. Hand-authored `CoverPoint_*` **не** ставим.

**Меню**

| Пункт | Что делает |
|-------|------------|
| `Polygone/Tactical AI/Install Arena Editor Wiring` | SO-профили, `UnitAIController` на префабе, child `TacticalWorld`, bake |
| `Polygone/Tactical AI/Bake Cover (TacticalWorld)` | Повторный bake геометрии открытой сцены |
| `Polygone/Tactical AI/Validate Unit Prefab` | PASS/FAIL компонентов и профилей |
| `Polygone/Tactical AI/Validate Arena Wiring` | PASS/FAIL мира, bake, совпадения profile |

Rebuild арены (`Polygone/Combat Test/Build 150x50 Arena`) заново вызывает Install, потому что уничтожает корень вместе с `TacticalWorld`.

#### `Unit.prefab` — Inspector

| Поле / компонент | Ожидание |
|------------------|----------|
| `UnitLifeGate` | есть (Alive/Unconscious/Dead; не AI-state) |
| `UnitAIController` | есть, **Enabled = off** (G-тесты и Neutral не тикают Idle/Search) |
| World Profile | `CombatArenaWorldProfile` |
| Tactical Profile | `InfantryDefaultTacticalProfile` |
| Tactical Profile.UseCover | true |
| Tactical Profile.AllowCoverReservation | true |
| Tactical Profile.Movement Mode | **Tactical** |
| `TacticalCoverOverlay` / `TacticalMovementOverlay` | **не** MonoBehaviour: их владеет контроллер |
| `NavMeshAgent`, `UnitNavLocomotionDriver`, `UnitSpineLean` | есть |
| `CoverCandidateDebugDraw`, `TacticalMovementDebugDraw` | есть (Play gizmos) |
| `SharedCoverSpatialCache`, `CoverOccupancyBoard` | **нет** на юните |

#### Сцена `CombatTestArena_150x50` — `TacticalWorld`

```text
CombatTestArena_150x50
└── TacticalWorld
    └── CoverOccupancyBoard   ← пустой host; board = C# объект в TacticalWorld
```

| Поле | Ожидание |
|------|----------|
| Profile | тот же `CombatArenaWorldProfile`, что на префабе |
| Bake Bounds | local center `(0, 1, 75)`, size `(50, 4, 150)` |
| Baked list | `baked > 0` после Bake (сферы + нормали в Scene View; C+id при выделении) |
| Cache / Occupancy | создаются в `Awake` / `EnsureRuntime` из bake; Play геометрию заново не сканирует |

#### `InfantryDefaultTacticalProfile.asset`

Включает существующий механизм, не новые веса: UseCover, reservation, mode = Tactical. Overlay решает: есть cover request → цель хода = слот; нет → обычный tactical route к Destination приказа. Attack context **не** переписывается (Cover ≠ Move).

#### Что делает Editor Bake

```text
геометрия сцены (стены, jersey, hesco, ящики)
      → CoverCandidateGenerator + Physics/NavMesh probes
      → BakedCoverCandidateRecord[] на TacticalWorld
```

Play: Available / Reserved / Occupied / Free по board. Поиск всей арены заново — нет.

Полный разбор поиска, классификации, bake, кэша, достоинств и дыр — раздел **«Поиск и запекание потенциальных укрытий»** сразу после этой главы про площадку.

### Как юнит получает приказы и двигается (SampleScene, после Install)

Обычный Play, не меню `Run Tactical Movement` / `Run Dynamic Cover`. Harness Play bind **пропускает**, чтобы #13/#14 smoke остались Normal/unbound.

```text
маркер спавна
      ↓
Unit.prefab + loadout (AI уже на префабе, выключен)
      ↓
арена включает UnitAIController у Player/Enemy
      ↓
Bind TacticalWorld (тот же World Profile) → cache + occupancy
      ↓
приказ Attack → центр арены (локально 0,0,75)  ← Destination приказа
      ↓
#13 overlay: Stay / RepositionRequest (не Move)
      ↓
#14 overlay, режим Tactical
      если RepositionRequest → hop goal = cover slot
      иначе → hop к Destination приказа
      ↓
TacticalNavigationExecutor.TryMoveTo
      ↓
UnitNavMoveCommand → UnitNavLocomotionDriver.Walk
```

**Канал 1 — спавн арены (авто).** `CombatTestArenaSpawner`, `m_SpawnOnStart = 1`. Спавнер **не** делает `AddComponent<UnitAIController>`. Neutral: AI остаётся выключенным. Боевые стороны: enable + RoE `MissionCombat` + `SetAttack(центр ± 1.35 м)`. Debug `TryIssue`, не `GameCommandService`.

**Канал 2 — HUD приказов.** Без изменений: `GameCommandInput` → `GameCommandService.Issue`. Нет AI → `NoAI`.

**Канал 3 — RTS клик.** По-прежнему второй путь на `UnitNavLocomotionDriver`, минуя #13/#14.

**До Install (диагноз 28.08.2026).** На префабе не было AI, cache/occupancy не bound, hop всегда `Normal` → Direct к центру, `RepositionRequest` не становился walk goal. Геометрия jersey/hesco была, слотов в Play не было.

**После Install — Play 28.08.2026.** Первый прогон (`Infantry_20260828_113530`, ~181 с): таблицы волны 0 и карточки всех слотов — **§8.8**. Walk+Occupied (`Infantry_20260828_163640`, ~114 с) — **§8.10**. Актуальный AI после Search/Attack hold (`Infantry_20260828_204049`, ~123 с) — **§8.11**.

### Контракт слота: Reserve → Occupied (не #13/#14)

#13/#14 **не** переоткрываем. Выбор укрытия и hop уже работают. Дыра была в последнем метре и в логе.

Board хранит только occupancy:

```text
Available / Reserved / Occupied
```

Наблюдаемый lifecycle слота (лог `COVER_STATE`):

```text
Reserved
    ↓
Approaching
    ↓
Acquired          ← геометрия, dist ≤ 0.60. Это ещё не Occupied
    ↓
Occupied          ← только CoverOccupancyBoard.ConfirmOccupied
```

```text
Acquired ≠ Occupied
CoverOverlay       → выбрал
ReservationBoard   → забронировал
Movement           → довёл
OccupancyBoard     ← подтвердил занятие
```

Пока юнит идёт к своему Reserved и путь жив — TTL **heartbeat**, слот не отпускаем. Release только: path invalid, cover invalid, timeout без подхода, **Unconscious**, **Death**, смена приказа.

Объект слота **не искать заново в момент acquire**. `TacticalMovementOverlay` хранит `ReservedCoverCandidate` с Reserve до конца lifecycle. `ConfirmOccupied` вызывается после `POSITION_ACQUIRE Acquired`, если объект слота есть.

Лог `POSITION_ACQUIRE`:

```text
cover=0      → dest-only, обычная точка назначения, не jersey/hesco
cover=C1     → реальный cover slot
```

`candidate=C0` при `cover=0` — не слот. Не путать с boolean `cover=0|1`.

Nav Reached для Attack/Defense (включая dest-only) садится в диск acquire **0.60**, не в 1.50. Search / Retreat / Flee dest-only остаются 1.50. **Tolerance 0.60 не поднимать.** Если `remaining≈0`, а transform дальше 0.60 — Walk выдаётся снова, Stop только внутри диска.

Пока слот Reserved и юнит подходит, #13 current = этот слот (`Occupied=false`). Stay Committed: другой candidate сменяет его **только если текущий invalid**. OccupancyVersion **не** ключ переоценки (иначе каждый Reserve заново выбирает C1↔C2).

Attack→Search:
  - visual memory после dwell 1.5 с (`LastHostileVisibleAt` + 1.5)
  - Search 2.0: Defense/Attack + gunshot / report — сразу, без dwell
Search→Attack Found: только `HostileVisible`.
`ImmediateThreat` не меняет Attack/Search: RoE + EmergencyCover (overlay разрешён и во время Search).
Occupied + valid + LOS: Stay Committed, не `BetterTacticalPosition`. CoverScore / PathScore / 0.60 не трогать.

`OutOfTolerance` **не** повод крутить tolerance. В `POSITION_ACQUIRE` пишутся `distance`, `tolerance`, `remaining`, `velocity`, `pathStatus`, `unitPos`, `dest`, `acquire`, `agentPos`. `reason=` больше не сваливается в одну корзину: `OutOfTolerance` / `CandidateMissing` / `ReservationLost` / `NotReservedByUnit` / `PathInvalid`. `Dead` / `Unconscious` / `CommandChanged` — на `COVER_STATE Released` и `COVER_HEARTBEAT action=Release`, не на acquire.

Play 20:40:49 (`Infantry_20260828_204049`, **§8.11**): Walk идёт, **13× Occupied** (10 юнитов), `Search→Attack reason=ImmediateThreat` = **0**. `OutOfTolerance` есть, но это dest центра vs слот ~10 м, **не** повод поднимать 0.60. Occupy **не FROZEN**. Vision / G6 / Weapon / CoverScore / PathScore / 0.60 / #13 / #14 не трогать.

### Unit Lifecycle: Alive / Unconscious / Dead

Это **не** UnitAIState. Источник — `UnitHealth` + `UnitConsciousness`. Координатор `UnitLifeGate` на солдате.

```text
Alive         → все контуры
Unconscious   → тело / визуал / Health остаются
                AI / nav / G6 / SELECT / SCAN / Fire / cover — стоп
                reservation и occupancy — Released
Dead          → то же; объект не Destroy
```

`UnitVision` **не** disable: иначе Unregister и соседи теряют его как кандидата реестра. Scan в Update просто не идёт. NavMeshAgent: `isStopped` + `ResetPath`, компонент не выключаем (revive).

Лог:

```text
LIFE  life=Unconscious  was=Alive  reason=Damage  health=0|1  consciousness=0
      ai=off vision=off combat=off move=off cover=Released  coverReleased=1 navStopped=1
SNAP  life=Unconscious  cover=none coverState=None  ai=off vision=off combat=off move=off
LIFE  life=Dead  was=Unconscious
```

После unconscious в файле юнита не должно быть новых `MOVE` / `COVER_HOP` / `COVER_DECISION` / `POSITION_DECISION` / `SCAN` / `VISION` / `SELECT` / `G6` / `SHOT`.

### Что площадка проверяет и что нет

| Проверяет | Не заменяет |
|-----------|-------------|
| Видимость в комнатах, углы, перекрёсток | G1–G8 harness (строгие числа) |
| Hitscan + урон + отдача в бою | RecoilContract (детерминированный replay) |
| AI Attack + Engage + tactical hop (после Install) | #13/#14 smoke (формулы, golden occupancy) |
| Разные стволы в одном бою | H-баланс отчёт (аналитика) |
| Будущий RoE с нейтралами | зоны RestrictedDefense vs MissionCombat — не #7 |

---

## Поиск и запекание потенциальных укрытий

Слой тактического **#13**. На арене **CLOSED / FROZEN 27.08.2026** (формулы CoverScore / RouteScore не крутить). Этот раздел — как мир **находит** потенциальные слоты и **запекает** их в сцену; не как солдат выбирает «лучшее для меня» (это individual score 13.3) и не как он туда идёт (это #14).

### Зачем так, а не CoverPoint_001

Укрытия **не расставляет дизайнер**. Нет ручных `CoverPoint_*`. Солдат читает геометрию мира: стены, jersey, hesco, ящики, барьеры.

`CoverCandidate` — **потенциальная тактическая позиция, привязанная к геометрии**. Это ещё не «это укрытие», не «это хорошо для снайпера», не destination хода и не AimPoint.

```text
существует ли здесь позиция у стены?     ← поиск / bake  (этот раздел)
        ↓
какого она геометрического типа?         ← классификация в том же Generate
        ↓
насколько она выгодна именно мне сейчас? ← individual score (13.3), не bake
        ↓
Stay / RepositionRequest                 ← overlay #13, не Move
        ↓
hop / Walk                               ← #14
        ↓
Reserved → Acquired → Occupied           ← board, не геометрия
```

Десять юнитов **не** делают десять раз один и тот же дорогой анализ стен. Общий список слотов один на регион. Score у каждого свой.

### Два режима одного конвейера

Один генератор, два входа:

| Режим | Когда | Источник `ICoverCandidateSource` | Physics / NavMesh |
|-------|--------|----------------------------------|-------------------|
| **Editor bake** | меню `Bake Cover` / Install wiring | `CoverCandidateGenerator` пишет `BakedCoverCandidateRecord[]` на `TacticalWorld` | да, один раз в Editor |
| **Play арены** | обычный Play после Install | `BakedCoverCandidateSource` копирует bake в кэш | **нет** — геометрию не сканирует |
| **Smoke / EditMode #13** | `Run Dynamic Cover`, golden tests | тот же `CoverCandidateGenerator` в рантайме | да, lazy на регион |

Продакшен-арена **намеренно** запекает в Editor: Play не создаёт `TacticalWorld` и не бегает OverlapBox по всей карте. Тесты #13 оставляют runtime generation, чтобы проверять сам алгоритм, а не сцену.

Контракт Play:

```text
Play не создаёт тактическую инфраструктуру
Play не ищет стены заново
Play читает bake → SharedCoverSpatialCache (lazy copy по региону)
Play пишет только occupancy: Available / Reserved / Occupied
```

### Регион — единица поиска, не вся карта

Мир режется сеткой **16 м** (`CoverSpatialMath.DefaultRegionSizeMeters`). Регион — клетка `(X, Z)` по полу, высота bounds генерации **8 м**.

```text
NeedCover
  → Region(world) = Floor(x/16), Floor(z/16)
  → Cache.GetCandidates(region)
  → miss → ICoverCandidateSource.Generate
  → hit  → тот же список
```

Запрос геометрии — **только этот регион + 1.5 м margin**, не весь мир.

На арене 50×150 м bake bounds local `(0, 1, 75)` size `(50, 4, 150)` → порядка **40 клеток** (X: −2…1, Z: 0…9). Потолок слотов: **16 на клетку** (`DefaultMaxCoverCandidates`). Это spatial cap, не «лучшие 16».

Occupancy ключ = `(RegionX, RegionZ, CandidateId)`. `CandidateId` уникален **внутри клетки** (1…16), не глобально. `C1` в R0_4 и `C1` в R1_4 — разные слоты.

### Конвейер поиска (`CoverCandidateGenerator`)

```text
PhysicsCoverGeometrySource.Collect
        ↓  OverlapBox региона, до 128 collider
стены / box-грани / AABB-грани прочих collider
        ↓  отсев: trigger, персонаж, техника, крыша (normal.y > 0.7)
        ↓  грань короче 0.8 м или ниже 0.8 м — выкинуть
CoverGeometrySurface[]   Origin, Normal, Tangent, Length
        ↓  sort по XZ (детерминизм)
сэмплы вдоль грани: шаг 2 м, минимум 1 на поверхность
        ↓  позиция = точка на грани + Normal × 0.45 м (standoff)
фильтры по каждому сэмплу:
        1. внутри региона (planar)
        2. NavMesh.SamplePosition радиус 1.2 м (bake) / иначе OffNavMesh
        3. якорь лучем в стену (ConfirmSurfaceWithPhysics = true на bake)
        4. capsule clearance 0.28 × 1.8 м (тело стоит; стену «сзади» игнор)
        ↓
dedup радиус 0.75 м + похожий Normal (dot > 0.5)
        ↓
spatial diversity → оставить ≤ 16 самых разнесённых по XZ
        ↓
CoverClassifier.Classify  (тип + профили защиты)
        ↓
CoverCandidate[]  Occupancy=Available, CoverType уже не None/Crouch/…
```

Не 1 collider → 1 candidate. Длинная стена даёт несколько точек. Два близких ящика с одной нормалью схлопываются.

**Геометрия, которую видит поиск.** Статичный мир: walls, buildings, obstacles, boxes / barriers. BoxCollider разбирается по локальным граням (уважает поворот). Всё остальное — по AABB. Персонажи (`UnitConsciousness` / `RtsUnitMember`) и техника (`VehicleController`) пропускаются.

**Геометрия, которую поиск не видит.** Vehicles, destruction, живые люди, squad, reservations, оружие, ранг, движущиеся пропы после bake.

Отсев пишется в `CoverRejectedSample`: `OutsideRegion` / `OffNavMesh` / `Unanchored` / `NoClearance`. Debug: сферы слотов и нормали на `TacticalWorld`; у юнита `CoverCandidateDebugDraw`.

### Классификация — часть Generate, не часть выбора солдата

После cap каждый оставшийся кандидат классифицируется **один раз**, относительно своей `Normal`, не относительно врага E01. Lean здесь нет.

Рамка `CoverBacked`: луч идёт **со стороны геометрии** (через стену к телу). Четыре сегмента на стойку:

| Стойка | Head | Torso | Pelvis | Legs |
|--------|------|-------|--------|------|
| Standing | 1.60 | 1.30 | 0.95 | 0.40 |
| Crouch | 0.95 | 0.70 | 0.50 | 0.25 |

Длина луча **3 м**. Сегмент «закрыт», если linecast упёрся в статику (не персонаж). Стойка валидна, если Head+Torso+Pelvis ≥ порог **0.5**. `PartialValid` — ни standing, ни crouch, но хоть один сегмент закрыт. `CornerValid` — есть защита **и** стена продолжается только влево **или** только вправо (span 1.2 м).

`CoverType` (приоритет): Corner → Standing → Crouch → Partial → None.

Профили `StandingProfile` / `CrouchProfile` (0 = открыт, 1 = закрыт) **запекаются** вместе с типом. Individual score потом читает их как Protection, не пересчитывает лучи по сегментам тела.

Пороги классификации — **прототип, не freeze**. Менять их = менять bake и все слоты арены.

### Запекание (`TacticalWorldBaker`)

Меню:

| Пункт | Что делает |
|-------|------------|
| `Polygone/Tactical AI/Install Arena Editor Wiring` | профили, AI на префабе, child `TacticalWorld`, **Bake** |
| `Polygone/Tactical AI/Bake Cover (TacticalWorld)` | повторный bake открытой сцены, Save Scene |
| `Polygone/Tactical AI/Validate Arena Wiring` | PASS/FAIL мира, bake>0, NavMesh reachable |

Алгоритм bake:

1. Взять `TacticalWorld.ResolveWorldBakeBounds()` (на арене local → world).
2. Пройти все клетки 16 м, попавшие в bounds (progress bar `Bake Cover`).
3. На клетку: `CoverCandidateGenerator.Generate` с Physics + NavMesh + clearance + occlusion + classification.
4. Каждый живой `CoverCandidate` → `BakedCoverCandidateRecord.FromCandidate`.
5. `TacticalWorld.ReplaceBake` — заменить serialized list, сбросить runtime cache.

В записи слота: Position, Normal, CoverType, флаги стоек, 8 чисел профилей, NavMeshValid, RegionX/Z, GeometryVersion. **Occupancy не хранится** — в Play всегда стартует Available.

Gizmo на `TacticalWorld`: сфера + нормаль 0.8 м; цвет по типу (Standing голубой, Crouch жёлтый, Corner оранжевый, Partial розовый, None зелёный). В Play Reserved = жёлтый, Occupied = красный. Выделение: подписи `C{id} {type}` (до 80).

Rebuild арены уничтожает корень вместе с `TacticalWorld` → Install (и bake) нужно снова. Сдвинул jersey без Bake — Play ходит к **старым** точкам.

### Как Play читает bake

```text
TacticalWorld.Awake / EnsureRuntime
      → BakedCoverCandidateSource(m_Baked)
      → SharedCoverSpatialCache(source)
      → CoverOccupancyBoard()   пустой
```

Первый `GetCandidates(region)` — miss: source копирует из списка все записи с этим `(RegionX, RegionZ)`, кладёт в слот кэша (cap 16 ещё раз). Следующий юнит в той же клетке — hit, без обхода bake. In-flight dedup: два одновременных запроса одной клетки не стартуют вторую generation.

`BumpGeometryVersion` / `InvalidateRegion` есть в кэше. На арене **автодетекта разрушения нет**: v1 — ручное invalidation. После bake GeometryVersion в записях = 1; runtime cache живёт своей версией (старт 1). Occupancy при смене версии отпускает все слоты.

`UnitAIController` **не** владеет кэшем. Bind: тот же `TacticalWorldProfile`, что на префабе и на мире сцены.

### Как солдат видит список (граница слоя)

Overlay тактики и emergency берут **одну клетку** — ту, где стоит юнит:

```text
cache.GetCandidates(unit.position)   → ≤ 16 кандидатов этой клетки
```

#14 urban walls, если своих якорей нет, дополнительно спрашивает origin, destination и середину пути (до трёх клеток). Это **не** расширяет выбор укрытия #13 — только коридор стен для маршрута.

Дальше (не bake):

- cheap filter: `NavMeshValid` и `CoverType != None`;
- individual `CoverPositionEvaluator` / `CoverScoreMath` (Protection + Visibility − TravelCost и тонкие факторы);
- Stay / Reposition, если `NewScore > Current + SwitchingCost`;
- ImmediateThreat → EmergencyCover (ближайшее приемлемое destination, не Move);
- occupancy board бронирует `(region, id)`.

Cover **не** жмёт Fire, не пишет AimPoint, не сливает `AI.EngageTarget` и `Combat.SelectedTarget`, не выдаёт NavMesh path.

### Достоинства

1. **Нет ручной расстановки.** Дизайнер ставит геометрию боя; слоты появляются из стен. Арена CQB не требует сотен `CoverPoint_*`, которые разъезжаются при сдвиге jersey.
2. **Shared ≠ Individual.** Кэш не хранит «лучшее укрытие». C3 плохо для снайпера и хорошо для LMG — это score солдата, не bake. Один список, разные решения.
3. **Дорогой поиск один раз, и не в Play.** OverlapBox, NavMesh sample, capsule, 8+ лучей классификации на слот — Editor. Play копирует struct-записи. 20 юнитов / 3 региона в приёмке 13.1 дали **3** generation, не 20.
4. **Регион, а не вся сцена.** Генерация и кэш локальны. Солдат на южном yard не сканирует северный.
5. **Якорь к NavMesh и телу.** Off-mesh и «внутри ящика» отсекаются до классификации. Standoff 0.45 м совпадает с urban wall inset #14 — слот и «идти вдоль стены» говорят об одной геометрии.
6. **Детерминизм.** Сортировка поверхностей и сэмплов по XZ, фиксированные knobs, spatial tie-break. Golden EditMode / Play воспроизводимы.
7. **Чистый контракт occupancy.** Геометрия и бронь разведены. Сломался Reserve/Occupied — чинить board, не bake. Сломался тип стены — переbake, не score.
8. **Подмена источника.** `ICoverCandidateSource` позволяет тестам кормить mock-список без физики, а арене — bake, не меняя overlay / solver.
9. **Видимость в Editor.** Сразу видно, где генератор нашёл слоты, какого они типа, заняты ли в Play. Диагноз «нет укрытий» vs «есть слоты, AI не выбрал» разделяется глазами.

### Недостатки и дыры

1. **Bake стареет.** Сдвинул проп, пересобрал арену, забыл Bake — AI идёт в пустоту или в старый угол. Нет dirty-флага «геометрия ≠ bake». Destroy в рантайме слот не убивает: GeometryVersion руками.
2. **Одна клетка 16 м на выбор #13.** Юнит у края региона не видит слот в 2 м, если тот в соседней клетке. Dense CQB на швах сетки слепой. #14 для стен смотрит до трёх клеток; выбор укрытия — нет. Это главная практическая дыра поиска «вокруг себя».
3. **Cap 16 — разнообразие, не качество.** В центре арены (перекрёсток, комнаты, hesco) генератор может выкинуть плотный хороший угол, оставив далёкие точки «чтобы покрыть клетку». Tactical quality в reduce **запрещён** контрактом 13.1 — и это плата.
4. **Прототип геометрии, не mesh.** Не-box collider → 4 стороны AABB. Бочка, цилиндр, наклонная плита, сложный MeshCollider дают фальшивые плоскости и фальшивые нормали. Поворот box уважается; «кривая» стена — нет.
5. **Мелочь отсекается порогом 0.8 м.** Низкий бордюр, тонкий щит, короткая секция забора не становятся поверхностью. Часть «укрытий глазами игрока» для AI не существует.
6. **OverlapBox 128 collider на клетку.** Лишние молча отбрасываются. Плотная CQB-клетка с кучей мелких пропов может недобрать грани.
7. **Классификация «спиной к стене», не «от этого врага».** Protection в bake — «стена закрывает сегменты с тыла геометрии». Враг с той же стороны, что солдат, всё равно видит высокий StandingProfile. Score 13.3 частично компенсирует Facing / FireLane / Danger, но **не пересэмплирует** лучи на текущего hostile. Снайпер за низкой стенкой со «своей» стороны формально «в укрытии».
8. **CoverType = None не выбирается.** `IsSelectable` режет None. Если классификатор ошибся (луч 3 м не нашёл стену, порог 0.5), слот мёртв для AI, хотя точка на NavMesh живая.
9. **Нет этажей / окон / внутренних полостей.** Region Y фиксирован около земли. Второй этаж, окоп, проём в стене, стрельба через окно — вне модели сэмпла. Corner ≠ peek: угол помечается, lean — 13.7 по уже выбранному слоту.
10. **Play не умеет догенерировать.** Новый баррикадный проп в рантайме, разрушенная стена, закрытая дверь — bake не знает. Контракт v1 это признаёт; для живого разрушения слой не готов.
11. **Стоимость сцены.** Список struct на `TacticalWorld` лежит в YAML сцены. 40 клеток × до 16 записей × профили — терпимо на 150×50; на километровую карту без нарезки миров будет тяжело и в bake-time (двойной цикл клеток, progress bar, SyncTransforms каждый Generate).
12. **Идентичность слота хрупкая.** `CandidateId` = порядок после reduce, не hash позиции. Переbake перенумеровывает C1…Cn. Логи «ходил в C2» между bake несравнимы. Occupancy с прошлого Play не переживает.
13. **Два мира отладки.** Smoke #13 генерирует в Play и рисует rejected сэмплы. Арена читает bake — rejected в Play не видно, только итоговые сферы. «Почему нет слота у этой бочки» на арене отвечает только повторный Editor bake / Scene View, не runtime reject log.
14. **Knobs генерации не на профиле.** Шаг 2 м, standoff 0.45, cap 16, margin 1.5 зашиты в `CoverGenerationSettings` (new() в baker). `InfantryTacticalProfile` включает UseCover / reservation / movement mode, но **не** плотность слотов. Смена шага = код + переbake, не крутилка ассета.

### Что не чинить этим слоем

```text
нет слотов в Play          → bake / NavMesh / wiring, не CoverScore
слоты есть, AI не идёт     → overlay / reservation / #14 hop, не генератор
идёт не туда               → individual score / SwitchingCost, не cap 16
двое в одном слоте         → occupancy board, не bake
не стреляет из укрытия     → G6 / RoE / Readiness, Cover ≠ Fire
снайпер слишком близко     → #15 weapon profile, не CoverPoint
```

Sampling алгоритм **прототип**: верхний API кэша от него не зависит. Менять шаг/грани/cap можно, не ломая overlay. Менять смысл `CoverCandidate` («это уже лучшее укрытие») — нельзя.

### Приёмка поиска (не арены)

| Подэтап | Что закрыто | Числа |
|---------|-------------|--------|
| 13.0 cache | lazy регион, hit/miss, in-flight dedup, GeometryVersion | EditMode в пакете 13.0–13.8 |
| 13.1 generation | sample → NavMesh → clearance → dedup → cap 16, без score | EditMode **43/0**; Play `CoverGeneration_LAST.txt` **18/0** (16 candidates; 20 units / 3 regions → 3 generation) |
| 13.2 classification | тип + профили в Generate, не vs враг | EditMode **60/0**; Play `CoverClassification_LAST.txt` **12/0** (standing/crouch/corner; cache hit тот же тип) |
| Арена bake | Editor list > 0, Play без rescan | wiring 28.08.2026; формулы не трогать |

#13 целиком: EditMode **169/0**, Play `CoverIntegration_LAST.txt` **18/0**. Это **не** freeze occupy на массовой арене (см. контракт слота выше).

---

## Атака, оборона, захват — сверка с дорожной картой

Короткий ответ: **одиночный солдат умеет идти в точку, прятаться в слот и стрелять по RoE. Штурм объекта vs оборона объекта — не сделаны.** «Захват» как отдельная задача на дорожной карте — **#24 Commander**, не открыт.

### Что дорожная карта обещает по ролям

Конечная картина (дизайн, не текущий код):

```text
командир: Attack / Defend / Flank / Withdraw / Search / Hold / Capture
        ↓
группа: assault / support / cover / reserve
        ↓
солдат: конкретный слот, маршрут, lean, готовность
```

По этапам:

| Слой | Что должно отличаться у атакующего и обороняющегося | Статус |
|------|------------------------------------------------------|--------|
| **#5** Attack / Retreat / Flee | Attack: Walk в точку, остаться Attack. Defense: держать якорь | **частично** — см. ниже |
| **#3** Hold / Engage | в Attack и Defense одинаково: виден Hostile → Engage, иначе Hold | **сделано** |
| **#10** Search | потерял контакт из Attack/Defense → искать, потом вернуться в то же состояние | **сделано** |
| **#13.5** позиция | Attack: ближе к цели может бить «безопаснее назад». Defense: контроль сектора зоны может бить «идеал за периметром» | **сделано тонко** (один фактор MissionScore) |
| **#13.6–occupy** | слот Reserved → Occupied, не двое в одной точке | **на арене работает, не FROZEN** |
| **#13.7** peek | из занятого слота lean, не Fire | **сделано** (если Occupied и стоит) |
| **#14** путь | cover-to-cover, вдоль стен | **слой закрыт**; на арене hop часто Direct к слоту/центру |
| **#15** оружие / ранг | снайпер overwatch, LMG сектор, Recruit хуже выбирает | **не открыт** |
| **#16 / #25** группа | один штурмует, один прикрывает, углы комнаты | **не открыт** |
| **#17–#22** адаптация | отпор → фланг / fire&maneuver / не прилипать к первой позиции | **не открыт** |
| **#24** командир | Capture ≠ Attack, Defend ≠ Defense солдата | **не открыт** |

### Как юнит действует сейчас

Один и тот же обработчик `UnitAIPointNavigationHandler` на Attack **и** Defense. Разница — в контексте приказа и в одном числе cover-score.

```text
приказ Attack(P)     → state=Attack   Destination=P        Walk к P (или к cover slot)
приказ Defense(P)    → state=Defense  Anchor=P, радиус 10 м Walk к P (или к cover slot)
оба + Hostile+VisibleNow → Action=Engage → CombatIntent=Engage → G6 / RoE
оба без видимого врага   → Action=Hold   → Aim/Fire Ignore, Track жив
ImmediateThreat          → RoE + EmergencyCover overlay; state не меняется
потерял контакт          → Search 2.0, ResumeState = Attack или Defense
```

**Walk goal (если UseCover):**

```text
ImmediateThreat + emergency dest  → тот слот
иначе Reserved / Current cover    → тот слот
иначе RepositionRequest           → выбранный слот
иначе Destination приказа         → центр атаки / якорь обороны
```

Приход Attack/Defense: диск **0.60 м** (не 1.50). Search / Retreat / Flee dest-only остаются 1.50. Occupied только после ConfirmOccupied.

**Peek** только когда юнит уже стоит в слоте (не Retreat / Flee / Search, не во время hop). Lean — существующий `UnitSpineLean`.

#### Attack сегодня

Смысл состояния: «добиться результата в точке». На арене обе стороны получают **только Attack** в центр `(0, 0, 75)` ± разброс. Отдельного «защитника точки» нет.

Что делает солдат:

1. Идёт к центру (или сворачивает в ближайший baked-слот своей клетки 16 м).
2. Бронирует слот, доходит, Occupied.
3. Если видит врага — Engage и стреляет (при RoE стороны MissionCombat).
4. Occupied + valid + LOS: Stay Committed, не прыгает на чуть лучший score.
5. Потерял визуал 1.5 с (или gunshot/report Search 2.0) → Search, потом снова Attack.

Cover mission = `CoverMissionIntent.Attack`: бонус, если слот **ближе к цели**, чем сам юнит (`MissionScore` clamp −0.6…+0.8). Цель для score — `EngageTarget` или LastAttacker, **не** пункт атаки P. Если врага нет, HasTarget=false → mission-бонус **0**, Attack и Defense выбирают слот одинаково.

#### Defense сегодня

Смысл состояния: «держать место / сектор». Контекст: якорь, facing, `AreaRadius = 10`.

Что делает солдат:

1. **Ходит к якорю** (HasDestination=true, Destination=якорь). Изначальный freeze #5 / 6.4 писал «Defense якорь не ходит». Occupy 28.08.2026 выровнял Defense с Attack: тот же Walk, диск 0.60. Контракт 6.4 в Closed-доке **устарел** относительно кода.
2. Overlay укрытий работает так же, как в Attack (Idle/Attack/Defense; не Search/Retreat/Flee).
3. Cover mission = `Defense`: бонус, если слот **дальше от цели** (держать дистанцию / не лезть вперёд). Якорь и радиус 10 м в `CoverSituation` **не передаются**. «Контроль сектора зоны vs идеал за периметром» из 13.5 в коде **не реализован** — есть только зеркало MissionScore.
4. Facing якоря не крутит выбор слота: `SectorForward = transform.forward` сейчас.

На арене `SetDefense` **никто не выдаёт**. Проверить оборону точки в SampleScene текущим спавнером нельзя.

#### Захват

Команды `Capture` нет. Нет флага «точка взята», нет удержания объекта, нет смены Attack→Defense после прихода. Приход в центр атаки = Stop и остаться Attack, не «захватили». Это **#24**, не дыра #13.

### Какие позиции занимают

| Вопрос | Сейчас | По карте когда |
|--------|--------|----------------|
| Откуда берутся точки | bake геометрии, ≤16 на клетку 16 м | #13 ✅ |
| Кто выбирает слот | individual score (protection, LOS, travel, тонкий mission/weapon/rank) | #13.3 ✅ прототип |
| Attack vs Defense слот | чуть ближе / чуть дальше от **врага**, не от пункта приказа | #13.5 тонко; сектор Defense — нет |
| Снайпер / LMG / дробовик | enum есть; арена не биндит ствол → все как Rifle/Soldier | **#15** |
| Recruit vs Elite | три класса cover-rank, default Soldier | **#15B** |
| Присед в низкой стенке | `CoverStance` всегда Standing в `BuildCoverSituation` | не открыто |
| Углы комнаты, stack, кто left/right | нет | **#16 CQB** |
| Один assault, один support | нет; occupancy только «слот занят» | **#16 / #25** |
| Не прилипать навсегда / фланг при отпоре | Occupied+LOS наоборот **держит** слот | **#19–#22** |

WeaponScore / Rank в cover — **нюдж**, не доктрина. `BindCoverProfile` есть, спавнер арены не вызывает.

### Сводка: сделано или нет

**Сделано (одиночка, M2 почти):** приказ Attack/Defense, Hold/Engage, Search из обоих, динамические слоты, Stay/Reposition, emergency под огнём, бронь слота, peek из Occupied, тактический hop к слоту.

**Сделано очень тонко:** «атакующий прёт вперёд, обороняющийся держит сектор». Это одно слагаемое ±0.8 к score относительно врага. Не зона, не facing, не «не выходить за 10 м».

**Не сделано и по карте не должно быть сейчас:** захват объекта, оборона как миссия группы, распределение ролей, CQB-углы, оружейные позиции, адаптивный штурм. **#14B / #14C OPEN** (подэтапы ✅, не FROZEN). CoverDirectionScore / **#15** не открывать, не Capture.

**Дрейф контракта, не баг слоя:** Defense в коде ходит к якорю. Старые формулировки «якорь не ходит» / `MOVE reason=Defense` нет — смотреть код `ForDefense` + `UnitAIPointNavigationHandler`.

Диагноз на арене: обе команды **штурмуют одну точку**. Это не тест «захват vs оборона». Чтобы увидеть Defense, нужен приказ Defense на якорь (HUD / `SetDefense`), не спавн волны.

---

## Матрица реализации

### Сделано и заморожено

| Компонент | Реализация | На префабе | Приёмка |
|-----------|------------|------------|---------|
| Скан, FOV 150°/120°, оптика до 300°/8° | полная | да | G1–G8, конверт 69/0 |
| Detection Q, progress, Strict калибровка | полная | да | 79/0 |
| Память RecentlyLost / LastKnown / decay | полная | да | 105/0 |
| Identity + look на цели | полная | да | 49/0 этап 1 |
| TargetSelector + G6 Track/Aim/Fire | полная | да | G6 26/0 |
| RoE матрица + адаптер в G6 | полная | нет (нужен AI) | 107/0 |
| CombatIntent Hold/Engage | полная | нет | 31/0 Play |
| Search → Walk → 15 м стоп | полная | нет | 45/0 |
| Attack/Retreat/Flee ходьба | полная | нет | 36/0 |
| GameCommand IssueCommand / Service / Input | полная | нет | 6.1–6.4 CLOSED |
| Sound C1, Ally Report C2, Final Perception | полная | приёмник | 47/0, 72/0, 49/0 |
| Стрельба A10 RecoilOffset + θ | **CLOSED** | да | G/H/Contract PASS |
| Combat Test Arena 150×50 + spawner | полная | в сцене | wire PASS |

### Частично / мёртвые входы

| Компонент | Состояние |
|-----------|-----------|
| ImmediateThreat | **#7 CLOSED** — `ImmediateThreatSource` + RoE veto |
| RestrictedDefense vs MissionCombat | **одинаковая матрица**, зона не реализована |
| AI.EngageTarget vs Combat.SelectedTarget | **#12:** расхождение допустимо и объяснимо, не auto-merge |
| Defense anchor walk | якорь Defense **не ходит** (отдельно от #5) |
| Звук → боевые события в мире | API C1 есть, **публикация из оружия слабая** (#8) |
| Звук в кадре тактического AI | **#9 CLOSED** |

### Не сделано (по дорожной карте)

| # | Слой | Зависимость |
|---|------|-------------|
| 7 | ImmediateThreat + живой RoE | **CLOSED 24.08.2026** |
| 8 | Combat events / sound в мир | **CLOSED 25.08.2026** |
| 9 | Звук в AI perception snapshot | **CLOSED 25.08.2026** |
| 10 | Search 2.0 (area, несколько точек) | **CLOSED 26.08.2026** |
| 11 | Приоритет / отмена приказов | **CLOSED 26.08.2026** |
| 12 | Калибровка выбора цели и огня | **CLOSED / FROZEN 26.08.2026** |
| 13 | Dynamic Cover (без CoverPoints) | **CLOSED / FROZEN 27.08.2026** — арена: Editor bake + bind, формулы не трогать |
| 14 | Tactical Movement | **CLOSED / FROZEN 27.08.2026** — арена: mode из InfantryTacticalProfile |
| 14B | Readiness State | **OPEN** — 14B.0–14B.7 ✅; `Readiness_State.md` |
| 14C | Threat Direction Knowledge | **OPEN** — 14C.0–14C.6 ✅; 14C.1 ✅; 14C.2 ✅; 14C.3 ✅; 14C.4 ✅; 14C.5 ✅; `Threat_Direction_Knowledge.md` |
| 15 | Weapon role + Rank behaviour | не открыт |
| 16 | Group + CQB | после #15 |
| 17–23 | Under Fire, Wound, Suppression, Reposition, Adaptive, Flank, Fire&Maneuver, Grenade | после #16 |
| 24–26 | Commander, Squad tactics, HTN/GOAP/Utility/BT | после адаптивного боя |

### Намеренно отложено (не баги)

- **UnitAIController на префабе** — компонент есть, **выключен**; арена включает Player/Enemy. SelfDefense по умолчанию режет огонь, пока спавнер не поставит RoE стороны.
- **Слияние AI-цели и боевой цели** — #12 оставляет два представления; не auto-merge.
- **Отряд, формации, командир** — группа с #16, командир с #24; до стабильного одиночки (#12) не открывать.
- **Укрытия** — динамические, #13; не hand-authored CoverPoints и не навигация.
- **ShooterControl, prone locomotion** — вне A10, отдельный трек.
- **Патруль как состояние AI** — старый patrol параллелен; сначала Idle/Defense/Attack + приказы #6.
- **Ретюн замороженных слоёв** «чтобы AI стал умнее» — запрещён процессом.

---

## Дорожная карта тактики (канон)

Нумерация **#1–#16 не ломается**. #13–#16 расширены подэтапами; после #16 идут #17–#26.  
Шесть фаз: I фундамент #1–#6 (**закрыта**) → II живой контур #7–#12 → III индивидуальная тактика #13–#15 → IV группа/CQB #16 → V адаптивный бой #17–#23 → VI командир/planner #24–#26.

**Ближайшая последовательность: #14B OPEN** (14B.0–14B.7 ✅), **#14C OPEN** (14C.0–14C.6 ✅; 14C.1–14C.5 ✅; слой **не FROZEN**). CoverDirectionScore / #15 не открывать. #13/#14 не reopen.

Цикл каждого этапа: DESIGN → CONTRACT → IMPLEMENT → EDITMODE → PLAY → ARENA → LOG → FREEZE. **DESIGN** сверяется с `Пехота_система_дизайн.md` §6.x (таблица §11 дизайн-дока / дорожной карты).

```text
#1 Vision          FROZEN
#2 Identity        FROZEN
#3 CombatIntent    FROZEN
#4 Search          FROZEN
#5 Attack/Retreat/Flee  FROZEN
#6 Commands        6.1–6.4 CLOSED
#7 ImmediateThreat + RoE   CLOSED 24.08.2026
#8 Combat events / sound   CLOSED 25.08.2026
#9 Sound in AI frame          CLOSED 25.08.2026
#10 Search 2.0              CLOSED / FROZEN 26.08.2026
#11 Command priority          CLOSED / FROZEN 26.08.2026
#12 Target + fire calibration CLOSED / FROZEN 26.08.2026
#13 Dynamic Cover CLOSED / FROZEN 27.08.2026
#14 Tactical Movement CLOSED / FROZEN 27.08.2026
#15 Weapon role + Rank behaviour   ← не открывать, пока не сказано явно
#16 Group + CQB
#17 Under Fire + Wound
#18 Suppression
#19 Reposition
#20 Adaptive Attack
#21 Flank / alternative route
#22 Fire & Maneuver
#23 Grenades
#24 Commander
#25 Squad tactics
#26 High-level planner (HTN/GOAP/Utility/BT)
```

Замороженные принципы конечной системы: AI не стреляет напрямую; Weapon Role = предпочтение, не запрет; укрытия динамические; lean подключается, не переписывается; ранг меняет поведение, не плодит пять AI; формация в бою может распасться на роли.

Не решаем сейчас точные формулы CoverScore / PathScore, пороги ready, CQB entry, suppression, мораль, гранаты, выбор planner.

**#7 — что должно появиться:**

```text
входящий огонь / подтверждённая атака
        ↓
ImmediateThreat = true
        ↓
RoE (SelfDefense vs MissionCombat реально различаются)
        ↓
Allow / Deny Aim/Fire
```

ImmediateThreat **не вызывает Fire** — только разрешает силу. Threat High ≠ ImmediateThreat.

**#9 — зафиксированное поведение звука (тестом, не импровизацией):**

```text
Defense + heard hostile  → Search
Attack  + heard hostile  → Search
Idle    + heard hostile  → ничего
```

Звук ≠ автоматическая стрельба.

---

## Снимок на сегодня

| Слой | Состояние | Живёт на префабе юнита? | Стреляет / ходит? |
|------|-----------|-------------------------|-------------------|
| Скан лучами, FOV, LOD | Готово, конверт 150/300 | Да | Нет |
| Обнаружение (Q, progress) | Готово, калибровка закрыта | Да | Нет |
| Память (RecentlyLost / LastKnown / decay) | Готово, калибровка закрыта | Да | Нет |
| Идентификация (кто это / угроза) | **FROZEN** этап 1: look на цели, commit 2 с | Да, `VisualIdentityEvidence`; спавн пишет look | Не стреляет |
| Выбор цели + решение Track/Aim/Fire | Готово | Да | Называет намерение, не жмёт спуск |
| Оружие, поза, дисциплина огня, hitscan, отдача | **A10 CLOSED** — RecoilOffset + θ; живой стрелок на префабе | Да | **Да** |
| Кадр восприятия для AI | Готово, заморожено | Нет | Нет |
| Тактические состояния Idle/Defense/Attack/Search/Retreat/Flee | Готово, заморожено | Нет | Search / Attack / Retreat / Flee ходят; Idle и Defense — нет |
| Действие Hold / Engage | **FROZEN** этап 2: CombatIntent | Нет (AI не на префабе) | Engage разрешает контур; Hold закрывает Aim/Fire |
| Search по LastKnown | **FROZEN** этап 3: Walk к snapshot | Нет | Идёт к LastKnown, стоп в 15 м, не пишет память |
| Attack / Defense / Retreat / Flee ходьба | **FROZEN** этап 4 + Defense Walk | Нет | Walk к snapshot точки; Attack/Defense/Retreat стоп и остаются в состоянии; Flee стоп → Idle |
| Игровой приказ (6.1) | **CLOSED** контракт | Нет | `IssueCommand` → существующая машина |
| Игровой источник (6.2) | **CLOSED** сервис | Нет | `GameCommandService`; нет AI → отказ; RTS нет |
| Правила применения силы | Матрица готова | Нет | Только вето на Aim/Fire |
| Звук | C1 **CLOSED / VERIFIED PASS 47/0**: Gunshot/Explosion/Footstep/Impact | Да приёмник | Sound 47/0 |
| Доклад союзника | C2 CLOSED / VERIFIED PASS 72/0 | Да приёмник | Stage 17 приёмка |
| Perception Contract D+E+F | Stage 18 **CLOSED / VERIFIED PASS 49/0** (12:36:53) | Да снимок | Final Perception 49/0 |
| Combat Test Arena 150×50 | Готово, CQB полигон в SampleScene | В сцене | wire spawner PASS |
| Навигация пехоты | Драйвер есть | Да | Тактика зовёт Walk; RTS ClickToMove — только игрок |
| Отряд, командир, динамические укрытия, мораль, формации | Нет (слои #13–#26) | — | — |
| Файловый лог действий | Готово, Editor Play | Биндер вешается в рантайме | Не меняет поведение. Каналы — часть 8 |

Приёмка (Play, все PASS). Консольный IK / CombatIntent mismatch выключен; диагноз — файловый лог (часть 8).

- Зрение G1–G8 одним Play: 9/0 (22.08.2026 14:57). По стадиям: G1 20, G2 20, G3 30, G4 32, G5 21, G6 26, G7 29, G8 19, G8 Stress 24.
- Калибровка обнаружения Strict: 79/0 (22.08 14:55). Конверт глаз/оптика: 69/0 (15:03). Freeze: 22/0 (15:04). Balance (этап 6): 61/0 (14:05). Lifecycle контакта (этап 7): 37/0 (15:52). Этап 8 боевых прицелов: **CLOSED / VERIFIED** 29/0 (16:42, `OpticRangeContract_LAST.txt`). Этап 9 дальности урона: **CLOSED / VERIFIED** 53/0 (18:54, `WeaponRangeContract_LAST.txt`).
- Память runtime: 105/0. Identity runtime: 48/0.
- Контракт AI-восприятия: 41/0 (19.08 23:10).
- Тактические состояния: 71/0 (20.08 00:08).
- Применение силы: 107/0 (20.08 00:38).
- CombatIntent Play: 31/0 (20.08 10:56). EditMode: 14/0 (11:25).
- Search locomotion Play: 45/0 (20.08 12:06). EditMode: 18/0.
- Attack / Retreat / Flee Play: 36/0 (20.08 14:46). EditMode: 31/0.
- Игровой вход команды 6.1: EditMode `TacticalCommandContractTests`; Play `TacticalCommandContract_LAST.txt`.
- Игровой сервис 6.2: EditMode `GameCommandServiceTests`; Play `GameCommandSource_LAST.txt`.

Это **закрытые слои**. Их не ретюнить, пока пишется следующий.

---

## Каркас одной фразы

```
мир
  → лучи / FOV / hit-зоны
  → физический кадр зрения
  → знание наблюдателя (контакт)
        ├─ боевой контур: выбрать → Track/Aim/Fire → дисциплина → hitscan
        └─ тактический AI (если компонент есть): задача + Hold/Engage + Search + вето силы
```

Два читателя одного реестра контактов. Разные правила. Разные поля.

---

## Нерушимые равенства

Эти фразы — закон текущей архитектуры. Нарушение любой из них ломает возможность понять, какой слой виноват.

```
увидел     ≠  выбран
выбран     ≠  можно целиться
можно целиться  ≠  решение Fire
решение Fire   ≠  выстрел ушёл
LastKnown  ≠  точка прицела
дальность зрения (150, до 300 с оптикой)  =  потолок живого hitscan
retain при reload/misfire                 =  текущий ResolvedMaxRange источника
дальность зрения  ≠  EffectiveRange (это падение урона, не запрет выстрела)
UnitTeam мира  ≠  кем солдат считает цель
Detected + Identity=Unknown     нормально
Hostile + Threat=Low            нормально
Forgotten (уверенность 0)       ≠  контакт удалён
пропуск скана LOD               ≠  цель исчезла
разрешена сила                  ≠  стрелять
Engage (действие AI)            ≠  Fire (решение боя)
```

Три независимые уверенности на одном контакте:

| Поле | Смысл | Что с ним происходит при потере взгляда |
|------|--------|------------------------------------------|
| DetectionProgress | насколько заметил объект | падает по своим порогам |
| IdentityConfidence | насколько уверен, кто это | **держится**, не гниёт |
| LastSeenConfidence | насколько верит месту LastKnown | гниёт 30 секунд до нуля |

---

# ЧАСТЬ 1. ЗРЕНИЕ

Зрение отвечает на вопрос: **что этот солдат знает о мире прямо сейчас**. Не куда идти. Не стрелять ли.

Знание **локально**. Два солдата про один объект мира могут знать разное.

В логе это теги `SCAN` (бюджет кадра) и `VISION` (знание-контакт). Пропуск LOD ≠ потеря цели.

## 1.1. Кого вообще сканируют

В мире у юнита есть сторона: Player / Enemy / Neutral.

Реестр зрения регистрирует всех. Кандидаты на скан:

- игрок смотрит только врагов;
- враг смотрит только игроков;
- нейтралы регистрируются и **никогда не попадают в кандидаты**.

Мишени тира видит только игрок.

Это фильтр существования, не «кто враг по мнению солдата».

## 1.2. Откуда смотрит

Точка глаз: высота глаз 1.6 м от корня, либо прицел оружия, если стойка high-ready. Сам источник не сканирует и не знает целей.

## 1.3. Скан (лучи)

Один проход:

1. Собрать оппонентов в грубом радиусе (текущий обзор + 4 м запаса).
2. Решить, сколько работы можно потратить в этом кадре (LOD, ниже).
3. Только на полном проходе: грубый FOV, кэш линии взгляда, лучи по hit-зонам тела.
4. Если точка прицела внутри конуса и дальности — записать **наблюдение**.
5. Список наблюдений заменить целиком. Пропущенный скан **не** пишет пустой список.

Наблюдение — только физика этого кадра. В нём нет прогресса, личности, команды, памяти.

Поля наблюдения:

- цель (Transform);
- позиция корня;
- точка прицела и флаг, что она есть;
- квадрат дистанции;
- видим (если запись создана — всегда да: невидимых в список не кладут);
- угол от оси взгляда;
- доля видимых hit-зон (Exposure, 0…1).

Веса зон для прицела: грудь важнее ног. Если hit-зон нет — запасной путь по коллайдеру, Exposure = 1 при любом попадании.

## 1.4. Бюджет скана (когда работать, не насколько хорошо видно)

Четыре уровня наблюдателя:

| Уровень | Что делает | Пишет кадр зрения? |
|---------|------------|---------------------|
| Idle | спит | нет |
| Cheap | считает кандидатов | нет |
| RangeFov | грубый конус (половина FOV + 8°) | нет |
| Detail | лучи, кэш, полный кадр | **да, только он** |

На полный проход сразу идут: принудительный скан, уже выбранная цель, свежая потеря контакта, уже поставленная в очередь детализация.

Иначе: если давно не было полного скана (≥ 0.5 с) — грубый конус; если давно не считали состав (≥ 1.5 с) — Cheap; иначе Idle.

Полных проходов на весь кадр игры не больше **8**. Если слотов нет — повтор через 0.02 с, пустой кадр не пишется.

LOD **не штрафует** качество обнаружения. «В этом кадре не сканировал» ≠ «цель пропала». Из-за этого состояние Observed может держаться на **последнем** удачном луче, пока не придёт новый полный проход.

Интервал скана на префабе: 0.25–0.45 с, умножается на коэффициент уровня (Idle ×3, Cheap ×1.75, RangeFov ×1, Detail ×0.75).

## 1.5. Знание: контакт

Контакт — объект знания **одного** наблюдателя про **одну** цель. Его пишет только процессор обнаружения.

Каналы на контакте:

- зрение (наблюдение, прогресс, личность, память места);
- звук (отдельная уверенность и точка, горизонт 3 с);
- доклад союзника (отдельная уверенность и точка, горизонт 8 с).

Звук и доклад **не** пишут последнее визуальное наблюдение, **не** ставят Observed, **не** создают точку прицела. Личность из звука/доклада не коммитится. LastKnown звук/доклад могут сдвинуть, только если цель сейчас **не** Observed.

В продакшене звук и доклад никто не шлёт. Есть только тестовые синтетические события. Боевой выбор цели умеет учесть звуковой контакт, кадр AI — **нет** (звуковые поля туда не копируются).

## 1.6. Обнаружение

Формула качества видимости:

```
Q = Distance × FOV × Exposure × Movement
```

Movement всегда ≥ 1. Стоячая цель = 1. Ходьба и бег цели только **помогают** её заметить. Движение самого наблюдателя в Q не входит.

| Параметр | Значение | Смысл |
|----------|----------|--------|
| Дальность зрения | глаз **150 м**; кратная оптика в Aiming **своё число ≤ 300** | дальше цели нет в кандидатах |
| Конус глаза | 120° (половина 60°) | HipFire и без кратности |
| Конус оптики | 8° в Aiming | только кратный прицел, не коллиматор |
| Кривая FOV для Q | половина 60°, край 0.15 | в центре 1, на краю 0.15 |
| Дистанция для Q | нормализованная кривая, край t=1 → **0.30** | на краю дальности слабый, но не ноль |
| Порог заметить | 0.25 | выше — копим прогресс |
| Порог потерять | 0.20 | ниже — теряем прогресс |
| Время набрать Detected при Q=1 | 0.35 с | |
| Форма накопления | exponent **3.8** | 1 = линейное Q; >1 — медленнее на низком Q |
| Время сбросить прогресс | 2.5 с | |
| Бонус движения цели | idle 1.00 / walk 1.15 / run 1.35 / потолок 1.50 | пороги скорости 0.6 и 3.2 м/с |

Гистерезис прогресса:

- Q > 0.25 → растёт;
- 0.20 < Q ≤ 0.25 → держится;
- Q ≤ 0.20 → падает.

Состояния прогресса: Undetected / Detecting / Detected. Пока прогресс = 0, контакт может ещё не существовать (живёт в pending). Как только прогресс > 0 — контакт в реестре.

Пока цель в кадре зрения: ObservationState = Observed, LastSeen = сейчас, LastKnown = позиция корня, LastSeenConfidence = 1.

Пропал из **реального** кадра: Q обнуляется, Observed → RecentlyLost. LastSeen/LastKnown **замораживаются**. Нет экстраполяции по скорости.

## 1.6.1. Закон конверта (закрыто этапами 1–9)

Это текущая игра, не план.

```
без кратности / HipFire / коллиматор / 1×     →  150 м, конус 120°
кратный прицел в Aiming                       →  своё поле 150…300 м, конус 8°
дальше текущего обзора                        →  нет Observation, нет AimPoint, нет Fire
длина hitscan                                 →  min(запас луча на префабе, текущий обзор)
retain reload/misfire                         →  текущий ResolvedMaxRange (InfantryEye / Passenger / Turret), не 18 м
```

Запас луча на префабе часто **650 м**. Это потолок железа, не игровой мир. Живой луч обрезается обзором: глаз 150, Vortex 6× — 250, Scope9 — 300.

Оптика читается **абсолютными метрами**. Не «×1.2 к 150». У боевых прицелов Range× урона = **1.0**. Глушитель **1.1** — физический модуль, не прицел.

Бонус кратности только в **Aiming**. HipFire оптику для обзора игнорирует.

Переменный прицел — один ассет, два режима. Низкий = 150. Высокий = своё число. Игрового переключателя нет: runtime держит **высокий**. Второго VisionSystem нет.

Тестовый прицел на 300 м в боевой каталог не входит.

Урон — отдельная шкала внутри того же конверта. Выстрел уже разрешён зрением:

```
E = min(дальность ствола × физические модули, дальность патрона)
d ≤ E        →  урон × 1
E < d < 2E   →  линейно к нулю
d ≥ 2E       →  урон × 0
```

Неверный инвариант «урон обязан стать нулём к 300 м» не используется. Если E = 225, ноль урона на 450, но луч туда не долетит: обзор режет раньше.

Пример: M4 (E = 140) + 6× видит 250, полный урон до 140, на 200 ещё ×0.57, на 251 не видит и не стреляет.

Приёмка: этап 8 **PASS 29/0** (16:42), этап 9 **PASS 53/0** (18:54). Q / пороги / BaseDamage / recoil / `ScopeVisionRange` после bake не крутить.

## 1.6.2. Живые прицелы

Кратность — **класс**, не `150 × zoom`. Шкала: 150 → 175 → 200 → 210 → 220 → 250 → 260 → 280 → 300. Снайперский класс начинается с **6×**. Штурмовой 1–6× на высоком режиме = **250**, это не длинная труба. Только Scope9 = 300.

| Класс | Обзор | Ассеты |
| --- | ---: | --- |
| 1× коллиматор / голограф | 150 | Reddot1, Reddot3, RDC, Reddot2, AK_Reddot4_Rail |
| 2× компактная труба | 175 | Aimpoint |
| гибрид / LPVO, низкий режим | 150 | G33, ACOG_RMR, ELCAN, Vortex |
| 3× | 200 | Scope1_3x; G33 высокий |
| 3.5× | 210 | Mosin_Scope8; ACOG_RMR высокий |
| 4× | 220 | ACOG, SUSAT, AK_Scope11; ELCAN высокий |
| штурмовой 6× | 250 | Vortex высокий |
| снайперская 6× / 8× / 10× | 260 / 280 / 300 | Scope4 / Scope5 / Scope9 |

Range× после этапа 9 у всех боевых прицелов = **1.0**. AimTime — старые плоские множители, **не** пересчитаны из обзора.

| Прицел | Режим | Кратность | Обзор | Range× | AimTime× |
| --- | --- | ---: | ---: | ---: | ---: |
| Reddot1 / Reddot3 / RDC | 1× | 1 | 150 | 1.00 | 0.98 |
| Reddot2 (голограф) | 1× | 1 | 150 | 1.00 | 0.98 |
| AK_Reddot4_Rail | 1× | 1 | 150 | 1.00 | 0.95 |
| Aimpoint | 2× | 2 | 175 | 1.00 | 1.00 |
| EOTech_G33 | 1× / 3× | 1 / 3 | 150 / 200 | 1.00 | 1.14 |
| Scope1_3x | 3× | 3 | 200 | 1.00 | 1.14 |
| Mosin_Scope8 | 3.5× | 3.5 | 210 | 1.00 | 1.22 |
| ACOG_RMR | 1× / 3.5× | 1 / 3.5 | 150 / 210 | 1.00 | 1.22 |
| ACOG | 4× | 4 | 220 | 1.00 | 1.20 |
| SUSAT | 4× | 4 | 220 | 1.00 | 1.24 |
| AK_Scope11 | 4× | 4 | 220 | 1.00 | 1.24 |
| ELCAN | 1× / 4× | 1 / 4 | 150 / 220 | 1.00 | 1.24 |
| Vortex | 1× / 6× | 1 / 6 | 150 / 250 | 1.00 | 1.34 |
| Scope4 | 6× | 6 | 260 | 1.00 | 1.46 |
| Scope5 | 8× | 8 | 280 | 1.00 | 1.56 |
| Scope9 | 10× | 10 | 300 | 1.00 | 1.55 |

ЛЦУ — не прицел. Красная точка **50 м**, бонус PointAim. Обзор 150–300 не даёт.

## 1.6.3. Живые стволы и патроны

`current_*` ниже — снимок **до** bake (растянутая шкала 275–1200). Живое = колонка **E**.

| Ствол | Роль | Было | E | Край | × на краю | Модель |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| BenelliM4 | дробовик | 40 | 40 | 40 | 0.35 pellet | кривая дроби |
| AK74U | CQB | 275 | 100 | 150 | 0.50 | линейный hitscan |
| MK18 | CQB | 300 | 105 | 150 | 0.57 | линейный |
| AK74UMOD1 | CQB | 325 | 110 | 150 | 0.64 | линейный |
| AK47S | CQB | 375 | 115 | 150 | 0.70 | линейный |
| M4_ModA_1 | штурмовой | 475 | 140 | 200 | 0.57 | линейный |
| AK47 | штурмовой | 475 | 140 | 200 | 0.57 | линейный |
| AK47_1 / AK74 | штурмовой | 525 | 145 | 200 | 0.62 | линейный |
| M4_ModA_2 / AK47MOD1 | штурмовой | 525 / 550 | 150 | 200 | 0.67 | линейный |
| AK74MOD1 | штурмовой | 575 | 155 | 200 | 0.71 | линейный |
| M16A_ModA_1 | штурмовой | 625 | 160 | 200 | 0.75 | линейный |
| RPK47 | LMG | 625 | 150 | 200 | 0.67 | линейный |
| RPK47MOD1 / RPK74 | LMG | 675 | 155 | 200 | 0.71 | линейный |
| RPK74MOD1 | LMG | 725 | 160 | 200 | 0.75 | линейный |
| M249 | LMG | 140 | 150 | 200 | 0.67 | линейный |
| PKM | LMG | 150 | 160 | 200 | 0.75 | линейный |
| M16A4_ModA_2 / SVD | marksman | 700 / 320 | 175 | 225 | 0.71 | линейный |
| MK12 / Mosin | marksman | 800 / 300 | 200 | 250 | 0.75 | линейный |
| Sniper762x51 | снайпер | 380 | 225 | 300 | 0.67 | линейный |
| M2Browning_127 | HMG | 800 | 225 | 300 | 0.67 | тяжёлый hitscan |
| MK19 | AGL | 1200 | 300 | 300 | — | потолок приказа; не live-falloff |

Патрон — второй потолок `min(ствол, патрон)`. Старые 100 / 500 / 1500 сняты.

| Патрон | Было | Живое | Зачем |
| --- | ---: | ---: | --- |
| 12 Gauge | 40 | 40 | как Benelli |
| 5.45×39 | 500 | 250 | не режет RPK74MOD1 (160) |
| 7.62×39 | 500 | 250 | не режет AK47MOD1 / RPK47 |
| 5.56 NATO | 500 | 300 | не режет MK12 (200) |
| 7.62×51 | 500 | 300 | не режет снайпер (225) |
| 7.62×54R | 500 | 300 | не режет Mosin / SVD |
| 12.7×99 | 1500 | 300 | потолок M2 |
| 40×53 | 1500 | 300 | потолок MK19 в каталоге |

Глушители AK/M4 **1.1** не трогались. С глушителем E чуть выше ствола, пока патрон не упрётся.

РПГ / одноразовый гранатомёт **нет** в этой таблице: у них нет `WeaponDefinition` hitscan. Отдельный контроллер, см. §1.6.5.

## 1.6.4. Что после этапа 9 закрыто

```
видеть дальше, чем позволяет глаз/прицел     — нет
стрелять hitscan дальше, чем видишь          — нет
оптика удлиняет урон Range×                  — нет
патрон 100 м тихо режет винтовку 475         — нет
MK19 / M2 живут в километрах урона           — нет
Q / DistanceCurve / Acquire 0.25 / 0.35      — заморожены
ScopeVisionRange боевых прицелов             — запечён, не ретюнить
игровой sweet точности за текущим обзором    — нет (этап 10)
```

Пехотный hitscan-конверт **готов**. Кривые точности/AimTime работают внутри него (этап 10 **PASS 11/0**). Дисциплина очереди — этап 11 **PASS 21/0**. Это не значит, что новая отдача уже правильная.

## 1.6.5. Старое, что ещё надо доработать

Сознательно не открывали на этапах 8–9. Q из‑за этого **не** крутить. Порядок закрытия: `Пехота_дорожная_карта.md` (A1–A10, затем B).

**1. Кривые точности и времени прицела, ось 0…500. → A1 = Vision Stage 10 CLOSED / VERIFIED PASS 11/0**  
Игровые ключи внутри 150/300. Каталог: `Vision_Stage10_AccuracyAimCurves_Catalog.md`. HipFire по-прежнему игнорирует оптику в конусе и AimTime — это правильно.

**2. AimTime× прицелов. → A2 = тот же Stage 10 CLOSED**  
Живые 0.95…1.56 — снимок этапа 8, **не** пересчитаны из VisionRange. Дистанционные AimTime-кривые внутри того же конверта. Тяжёлая труба медленнее.

**3. Дисциплина огня, пояса 25 / 70 / 140 / 220. → A3 / Stage 11 CLOSED / VERIFIED PASS 21/0**  
Не запрещает дальнюю стрельбу. Нормализованный рабочий диапазон класса, не фиксированные метры. Каталог: `Vision_Stage11_FireDiscipline_Catalog.md`. Play: `FireDisciplineContract_LAST.txt`. GATE независим. Attention сюда не входит.

**4. Авто-режим по диаметру группы.**  
Селектор смотрит ожидаемый диаметр (конус + прогноз отдачи) против ширины человека ~0.60 м (порог ≈ 0.775 м). Жёстких метров почти нет, но «дальние» ветки 150–220 теперь край без прицела и середина со снайперским. Порог не ретюнили.

**5. РПГ / одноразовый. → A4 / Stage 12 CLOSED / VERIFIED PASS 30/0**  
Не hitscan. Приказ H остаётся. Запуск проходит GATE permit: Observed AimPoint внутри `ResolvedMaxRange`. LastKnown не цель. Ракета 115–130 м/с × 12 с может лететь дальше обзора — lifetime не клипится. Каталог: `Vision_Stage12_ProjectileVision_Catalog.md`. Play: `ProjectileVisionContract_LAST.txt`. Кривые 0…500 не ретюнили.

**6. MK19 как снаряд. → A5 / тот же Stage 12 CLOSED / VERIFIED PASS 30/0**  
Каталожный потолок E = 300 — потолок **замысла**, не live-falloff гранаты (240 м/с × 25 с). Турель не целится в `SelectedTarget` без engageable. Тот же permit, что у RPG.

**7. Подствольный гранатомёт.**  
Слот в данных есть, боевого контента со своей дальностью нет. Когда появится — тот же закон 150 / до 300, не «реалистичные 400 м».

**8. Пассажир в технике. → A6 / Stage 13 CLOSED / VERIFIED PASS 35/0**  
Потолок огня из окна **100 м снят**. Пассажир видит и стреляет по тем же правилам, что пехота на земле (глаз 150, оптика до 300 при ready). Окно режет LOS корпус/стекло, не метры. Play: `VehicleVisionContract_LAST.txt`.

**9. Удержание цели при перезарядке. → A7 / Stage 14 CLOSED / VERIFIED PASS 31/0**  
Поле `m_MaxEngageRange` (18 м) снято. Retain = текущий `ResolvedMaxRange` источника + актуальный LOS/AimPoint. SELECT не режется. Память не продлевается. LastKnown ≠ AimPoint. Play: `CombatRetainContract_LAST.txt`.

**10. Переключатель кратности. → A8 (закрыто решением, без кода)**  
В данных 1×/высокий есть. В игре и у AI кнопки нет. Всегда высокий режим. На 175 м Vortex в 1× не видит — это только тесты клоном ассета.

**11. Турель без пехотной позы Aiming. → A9 / Stage 13 CLOSED / VERIFIED PASS 35/0**  
Пехотный бонус оптики завязан на Aiming. У M2/MK19 этой позы нет. `treatAsAlwaysAimed` когда gunner bound. `OpticVisionRange` если задан (>150), иначе 150. Live M2/MK19 = 0 → 150. Тот же Play PASS.

**12. Отдача, конус, HipFire-множители.**  
**A10 CLOSED (24.08.2026):** `RecoilOffset` в градусах, θ не зависит от отдачи, RecoilContract/G/H PASS. Tuning Benelli/M2/Review — отдельный stage вне A10. Не читать промахи как баг AI до #12.

**13. Допуск ствола 3° / 12°. → A10, не раньше**  
Углы не зависят от 150 vs 500. На 150 м 3° ≈ 7.9 м промах центра, на 300 м ≈ 15.7 м. Гейт ствола сравнивает дуло с aim+RecoilOffset (`Стрельба_и_отдача.md` §6). Молчание после длинной очереди — не баг AI.

**14. Excel / UI-графики точности.**  
Экспортёр и UI-ось переведены на **0…300**. Книгу пересобрать: `python Tools/export_combat_balance_excel.py`. Не закон зрения.

**15. Звук выстрела 625 / 220 м. → C1 CLOSED**  
Слышимость — другая шкала. Production: Gunshot 300 / Explosion 500 / Footstep 25 / Impact 40. Не путать с обзором.

**16. LOD зрения 20 / 100 / 500.**  
Корзины нагрузки, не геймплей. «Дальше 500» больше не игровой Far-мир.

**17. Угроза 25 / 80.**  
При глазе 150 м «дальше 80 = Low» — почти вся дальняя зона без оптики. Числа не меняли. Это не баг формулы Q.

**18. Ручная граната 35 м.**  
Уже внутри 150. Не трогать.

**19. Бинокль / зоркость / тепловизор.**  
Нет. Не выдумывать.

**20. Facing / спина ±35° / AimPitch.**  
Углы, не метры. Не переписывать «под 150». Attention / Facing как скорость Detection — этап **B**, не меняет эти углы и не меняет Q.

## 1.6.6. Принятые решения (не открывать заново)

- Глаз 150 / 120°. Оптика Aiming 150…300 / 8°.
- Коллиматор и любой 1× = 150. «Чуть-чуть 160» запрещено. Mag > 1 не может остаться 150.
- Aimpoint = компактная 2× / 175, не коллиматор.
- Mosin_Scope8 = индекс ассета, не 8×. Это 3.5× / 210.
- ACOG_RMR = два канала 1× / 3.5×, не зум. SUSAT сверху — запасные целики, не второй обзор.
- ELCAN 1×/4× высокий 220. Vortex 1–6× высокий 250 (штурмовой). G33 1×/3× высокий 200.
- Scope4/5/9 = снайперская лестница 260/280/300. Только Scope9 = 300.
- Оптика не удлиняет урон. Range× = 1.0. Глушитель 1.1 остаётся.
- M2 = 225, как снайпер. MK19 = потолок 300 как снаряд, не линейный falloff.
- Формула Q заморожена. Acquire 0.25 / 0.35, exponent 3.8. Хвост DistanceCurve `t=0.82/0.90/0.96/1.00 → 0.50/0.38/0.32/0.30`.
- 18 м ≠ обзор. Retain = `ResolvedMaxRange` источника, не отдельный 18/150 в combat-коде.
- HipFire игнорирует оптику в обзоре, конусе и AimTime.
- BaseDamage, броня, голова/конечности, recoil-числа на этапах 8–9 не трогали.
- Тактический **#7** ≠ Vision Stage 7. Vision Stage 10 = A1+A2 (кривые, не зрение Q). **#7** — после этапов A–E.

## 1.6.7. Открытые вопросы

Числа Attention **не** фиксировать здесь. Решения карты A (retain, турель, РПГ, пассажир, без переключателя кратности) — `Пехота_дорожная_карта.md`.

| # | Вопрос | Сейчас | Карта |
|---|--------|--------|--------|
| 1 | Retain 18 м или текущий обзор? | **A7 CLOSED PASS 31/0**: `ResolvedMaxRange` | **A7: текущий обзор источника** |
| 2 | Турель без позы Aiming | **A9 CLOSED PASS 35/0** | **A9: OpticVisionRange или 150** |
| 3 | Переключатель 1× / высокий | всегда высокий | **A8: не добавлять** |
| 4 | Пассажир vs пехота | **A6 CLOSED PASS 35/0**: те же 150/300, не 100 | **A6: VisionSource, один pipeline** |
| 5 | РПГ: приказ vs жизнь снаряда | **A4 CLOSED PASS 30/0** | **A4: видеть→вести; снаряд может лететь дальше** |
| 6 | MK19: каталог 300 или жизнь гранаты? | **A5 CLOSED PASS 30/0** | **A5: то же разделение, тем же контрактом** |
| 7 | Пояса 140/220 | **A3 CLOSED PASS 21/0**, не 25/70/140 | **A3: характер огня, не запрет** |
| 8 | Кривые 0…500 | внутри 150/300 | **A1/Stage 10 CLOSED PASS 11/0** |
| 9 | AimTime× из обзора? | нет, × заморожены | **A2/Stage 10 CLOSED, не формула** |
| 10 | Допуск ствола 3° на 300 м | да, пока | **A10, не раньше** |
| 11 | Угрозу 25/80 в мире 150? | нет | не A; не Q |
| 12 | Когда recoil/конус? | **A10 CLOSED** — RecoilOffset + θ | tuning Benelli/M2 — отдельно |

## 1.6.8. Готова ли система целиком?

**Нет, если «система» = весь бой на новой шкале.**  
**Да, если «система» = честный конверт видеть / hitscan / урон / кривые внутри обзора.**

Готово:

- солдат не видит дальше своего обзора;
- пехотная пуля не летит дальше этого обзора;
- урон внутри обзора падает по живым E ствола и патрона;
- прицел задаёт обзор абсолютными метрами, не множителем урона;
- точность и AimTime работают внутри текущего обзора — **Stage 10 CLOSED PASS 11/0**;
- назначение RPG/MK19 только по Observed внутри обзора; снаряд может лететь дальше — **Stage 12 CLOSED PASS 30/0**;
- пассажир из окна = пехота на земле; турель optic или 150 — **Stage 13 CLOSED PASS 35/0**;
- retain reload/misfire = текущий обзор источника — **Stage 14 CLOSED PASS 31/0**;
- обнаружение, память, личность, выбор, Fire-ворота **работают**; отдача-модель **поставлена частично** и сейчас стреляет неверно.

Не готово, и это не дыры зрения:

- очереди и экономия патронов: **A3 CLOSED / VERIFIED PASS 21/0** (`Vision_Stage11_FireDiscipline_Catalog.md`);
- РПГ / MK19: **A4+A5 CLOSED / VERIFIED PASS 30/0** (`Vision_Stage12_ProjectileVision_Catalog.md`);
- пассажир/турель как VisionSource: **A6+A9 CLOSED / VERIFIED PASS 35/0** (`Vision_Stage13_VehicleVision_Catalog.md`);
- retain reload/misfire = `ResolvedMaxRange` — **A7 CLOSED PASS 31/0**; A8 закрыто решением;
- Attention / Facing — **B / Stage 15 CLOSED PASS 44/0**; кривая BAKED, не фактор Q;
- отдача/конус: **A10 CLOSED** — RecoilOffset, G/H/Contract PASS; тактический **#7 CLOSED**; следующий **#8**;
- тактический огонь по RoE: **#7 CLOSED** — ImmediateThreat ставит `ImmediateThreatSource`.

Полный пехотный солдат «видеть → понять → выбрать → попасть» закрыт по perception A–F, **A10** и тактическому **#7**. Следующий открытый слой — **#8** (combat events / sound). «Плохо попал» после A10 — калибровка #12 или balance tuning, не задача зрения.

Не чинить пункты §1.6.5 кручением Q, VisionRange или E ствола.

## 1.7. Память места

После потери взгляда:

```
0–5 с     RecentlyLost     очень свежая потеря
5–30 с    Lost             место помнит, уверенность падает
≥ 30 с    Lost, conf = 0   забыл место; контакт не удаляется
```

Формула уверенности места:

```
conf(t) = (1 − t / 30)^1.5     при старте 1
```

Ориентиры по секундам (Q-независимая кривая):

| t, с | Состояние | conf | Ощущение |
|-----:|-----------|-----:|----------|
| 0 | RecentlyLost | ≈1.00 | только что тут |
| 2 | RecentlyLost | ≈0.90 | очень свежо |
| 5 | Lost | ≈0.76 | ещё свежо |
| 12 | Lost | ≈0.46 | неуверенно |
| ≈18 | Lost | 0.25 | граница stale |
| 20 | Lost | ≈0.19 | stale |
| 30 | Lost | 0 | забыл |
| 60 | контакт жив | 0 | всё ещё в реестре |

Правила:

- полезность памяти для AI: LastSeenConfidence **> 0.25**;
- stale: 0 < conf ≤ 0.25;
- forgotten: conf = 0 — это не stale;
- повторный захват взгляда: conf = 1, тот же контакт, личность сохраняется;
- LastKnown = LastSeen, пока Observed; без «он побежал туда».

Запрещено и не сделано: удлинять память из-за перезарядки, множители «опасности», экстраполяция скорости.

## 1.8. Кто это, отношение, угроза

Отдельный контур от обнаружения. Сначала видит объект, потом понимает кто.

Личность — **класс принадлежности**, не роль и не тип оружия:

```
Unknown / Friendly / Neutral / Hostile
```

Отношение (Relationship) выводится из **закоммиченной** личности. Это разные поля. Угроза считается только при Relationship = Hostile.

Откуда улика «кто это»:

1. Ручная подсказка на процессоре наблюдателя (тесты).
2. `VisualIdentityEvidence` на цели: look Player / Enemy / Civilian.
3. Иначе — Unknown.

На префабе юнита компонент есть, default Unknown. Спавн записывает look отдельно от `UnitTeam`. Наблюдатель маппит look **относительно своей стороны**. `UnitTeam` цели в знание не копируется.

| Кто смотрит \ look цели | Player | Enemy | Civilian |
|-------------------------|--------|-------|----------|
| Player | Friendly | Hostile | Neutral |
| Enemy | Hostile | Friendly | Neutral |
| Neutral | Neutral | Neutral | Neutral |

Калибровка (при Q=1 и ненулевой улике):

| Параметр | Значение |
|----------|----------|
| Время до полной уверенности | 4.0 с |
| Порог commit личности | 0.50 (≈ 2.0 с взгляда) |
| Threat High | Hostile и ≤ 25 м |
| Threat Medium | Hostile и ≤ 80 м |
| Threat Low | Hostile и дальше 80 м |
| Threat None | кто угодно, кто не Hostile |

Дистанция угрозы берётся из **последнего визуального** наблюдения, даже после потери LOS.

Конфликт улики с уже закоммиченной личностью: сброс уверенности и новое накопление. Мгновенного «телепорта команды» нет. Потеря LOS личность **не сбрасывает**.

Detected + Unknown — нормальная ситуация: человек виден, кто он — ещё нет.

## 1.9. Замороженные числа зрения (не крутить ради AI)

```
конус глаза 120°, кривая FOV 60° / 0.15; оптика Aiming 8°
acquire 0.25 за 0.35 с, lose 0.20 за 2.5 с, accumulation exponent 3.8
дистанция Q: t = d/resolvedRange, край t=1 → 0.30
память: 5 с / 30 с / форма 1.5 / stale 0.25
личность: 4.0 с / commit 0.50
угроза: 25 м / 80 м
глаз 150 м (до 300 с кратным прицелом)  ≠  отдельный combat-retain 18 м (поле снято)
EffectiveRange  ≠  разрешение выстрелить
```

Если «AI плохо ищет» или «плохо стреляет» — это не повод менять эти числа.

## 1.10. Кадр для тактического AI

Раз в тик из реестра контактов собирается снимок. Он **не** содержит:

- Q и факторы FOV/Exposure;
- DetectionProgress;
- UnitTeam цели;
- выбранную цель боевого контура;
- звук и доклад.

Флаги на контакте в снимке:

| Флаг | Правило |
|------|---------|
| VisibleNow | Detected **и** Observed |
| RecentlyLost / Lost | по ObservationState |
| HasUsefulMemory | LastSeenConfidence > 0.25 |
| MemoryStale | 0 < conf ≤ 0.25 |
| Hostile / Friendly / Neutral | по Relationship, не по UnitTeam |
| IdentityUnknown | Identity == Unknown |
| Threat High/Medium/Low/None | как в контакте |

Корзины кадра: все / видимые / помнит (не видим, но память полезна) / stale / Hostile / Unknown / сильнейшая угроза кадра.

VisibleNow у AI строже, чем «боевой контур может целиться»: бой может выбрать цель ещё в Detecting, если есть Observed и луч. AI считает видимым только Detected+Observed.

---

# ЧАСТЬ 2. БОЕВОЙ КОНТУР (ЖИВАЯ СТРЕЛЬБА)

Это то, чем юнит на префабе **уже стреляет**. Тактический AI здесь не участвует, пока на объект не повешен его контроллер.

В логе цепочка живого огня: `SELECT` → `G6` → `DISC` → `GATE` → `SHOT` / `PROJECTILE`. Разрыв в этой цепочке ≠ ошибка зрения.

Порядок в одном кадре (номера — execution order):

```
−200  списки восприятия
   0  скан зрения
  10  тик знания, событие «контакты изменились»
  20  выбор цели
  30  решение Track / Aim / Fire / Ignore
  50  поза оружия / визуальная отдача
  54  огневая дисциплина (жмёт виртуальный спуск)
  55  патрон / RPM
  56  ворота выстрела
  57  hitscan + прогресс прицела
  58  игровой штраф отдачи
  65  IK / ствол к точке прицела
```

## 2.1. Выбор цели

Кандидаты — контакты знания, не сырой список лучей.

Кого отсекают:

- нет цели;
- нет знания (забыт и нет звука/доклада);
- мёртв / без сознания / мишень тира недоступна;
- Friendly (личность или отношение);
- Neutral **личность**;
- Unknown — **не** отсекают (в продакшене личности нет);
- stale — по умолчанию **можно** выбирать.

Очки (больше — лучше):

```
+10 если сейчас Observed
+ уверенность (макс из LastSeen / звук / доклад) × 2
+ (Threat / High) × 1
+ 0.5 если Hostile
+ 1 / (1 + дистанция до LastKnown)
− 3 если память stale
```

LastKnown здесь только **подсказка дистанции**. Точка прицела берётся иначе.

Точка прицела для боя:

- только если Observed;
- только если в последнем наблюдении есть AimPoint и он видим;
- иначе цель может быть **выбрана без прицела**.

Три разных понятия:

| Понятие | Условие |
|---------|---------|
| Selected | победил в очках |
| Engageable | Selected + есть LOS-прицел + жив/в сознании |
| Fire | отдельное решение следующего шага |

При перезарядке/осечке текущую выбранную цель можно удерживать. Дальность retain = **текущий `ResolvedMaxRange` источника зрения** (глаз / пассажир / турель) плюс актуальный LOS/AimPoint. Основной выбор цели по контактам **этим порогом не режется**. Нет AimPoint → нет Fire. LastKnown сюда не подставляется.

Если линия ствол → прицел пересекает дружеское/нейтральное тело (сфера 0.35 м), цель глушится ≈ 0.15 с и заказывается немедленный скан зрения.

Скорость цели для упреждения: сглаживание точки прицела, проекция не дальше 0.5 с. Это не LastKnown.

## 2.2. Решение, что делать с выбранным

Не стреляет. Называет намерение.

```
нет выбранного                    → None
нет контакта                      → Ignore
нет знания                        → Ignore
Friendly                          → Ignore
Neutral личность                  → Ignore
мир говорит «нельзя трогать»      → Ignore
нет LOS-прицела                   → Track
оружие/поза/прицел ещё не готовы  → Aim
иначе                             → Fire
```

Никогда не возвращает Observe / Suppress / Report (значения в перечислении есть, политики нет).

Угроза **не** открывает и не закрывает Fire. Unknown **может** получить Fire. Память без луча → Track, не Fire.

Смысл для оружия:

- Aim или Fire = «держим огневой контакт», можно копить прицел и серии;
- выстрел проходит только при **Fire**.

## 2.3. Кто жмёт спуск

На префабе стоит огневая дисциплина. Старый «стреляй пока наведён» себя выключает, если дисциплина есть.

Дисциплина:

1. Можно ли держать контакт (оружие в принципе сможет выстрелить, и решение Aim или Fire).
2. Строит план серии: сколько патронов, пауза, какой режим, какой порог прицела — по дистанции, классу оружия, навыкам.
3. Копит AimProgress.
4. Вызывает старт огня.
5. После серии — пауза, затем новая серия.

Дистанции планирования (не запрет стрельбы): `distance / workingRange` класса (CQB 150 / Assault 200 / LMG 220 / Marksman 250 / Sniper 300), hysteresis 0.08. Ближе к 0 — короче паузы и больше очередь; к 1.0 — полуавтомат и выше Aim. Пулемёт держит длинные очереди. Старые 25/70/140/220 больше не шкала. Каталог: `Vision_Stage11_FireDiscipline_Catalog.md`.

Другие источники спуска: RTS-команда игрока, тесты. Тактический AI спуск **не** вызывает.

## 2.4. Ворота одного выстрела

Все должны пройти (типовые флаги префаба включены):

**Намерение.** Решение == Fire.

**Сознание.** Юнит в сознании.

**Оружие.** Есть определение оружия; патрон в патроннике или магазин с патронами; не осечка; RPM позволяет; не идёт анимация перезарядки.

**Поза.** Выстрел только из HipFire / HipFireWalk / HipFireCrouchWalk / PointAim / Aiming.  
PreAim, HighReady, LowReady, NotReady — нет. Во время бленда поз **обе** (текущая и целевая) должны быть стрелковыми. Не спринт. Не занят сменой стойки / броском / стабилизацией.

**Прицел.** AimProgress не ниже порога позы: HipFire ≈ 0.35, PointAim ≈ 0.65, Aiming = 1.0, PreAim недостижим. Дисциплина может поднять порог. Для очереди порог в основном на первый выстрел серии; полуавтомат — каждый. Ствол должен смотреть в точку прицела с допуском позы/стойки/движения (в Aiming стоя ≈ 3°).

**Линия огня.** Сфера от ствола к точке прицела не упирается в союзника/нейтрала.

После успеха: hitscan, затем событие выстрела (звук, визуальная отдача, игровой штраф отдачи). Штраф этой пули на **этот** выстрел не действует — hitscan берёт штраф **до** события.

## 2.5. Куда летит пуля

Если есть engageable цель — не «вперёд из ствола», а в точку LOS-прицела плюс упреждение по скорости и время полёта, плюс конус рассеивания и рисунок отдачи.

Запасного прицела в LastKnown / коллайдер / «центр врага без луча» **нет**. Нет точки — нет этого выстрела по цели.

Длина живого hitscan = **текущий обзор** (глаз 150, оптика до 300). На префабе запас луча часто 650 м — железо, не игровой потолок.  

Эффективная дальность ствола и патрона режет **урон**, не разрешение выстрелить. После 2× E урон ноль. Боевая оптика Range× = 1.0 и урон не удлиняет.

Практический конверт: нет Observed дальше обзора → нет AimPoint → нет Fire. С M4 + 6× цель на 200 м видна и бьётся слабее; на 251 м её нет. **18 м — не потолок живого огня.**

## 2.6. Поза и наведение тела

Ствол и позвоночник смотрят в ту же engageable точку, что и hitscan, не в LastKnown.

Пока нет стрелковой позы — ворота выстрела закрыты, даже если решение Fire. Поднять оружие должен кто-то снаружи: игрок, RTS, авто-поза. Тактический AI позу не ставит.

## 2.7. Что боевой контур не знает

Не читает Q, DetectionProgress, LOD, факторы FOV/Exposure. Угроза в контексте решения есть и **игнорируется**. LastKnown не цель.

---

# ЧАСТЬ 3. ТАКТИЧЕСКИЙ AI

Это слой **задачи**, не слой выстрела. На префабе юнита его нет. Появляется в Play у тестов и если нажать отладочные кнопки применения силы.

В логе: `SPAWN ai=none|UnitAIController`, тег `INPUT` (ввод), `GAMECMD` (сервис), `CMD` (IssueCommand), `AI` (state/action/intent/roe), `MOVE reason=Search|Attack|Retreat|Flee`. AI не пишет `SHOT`.

## 3.1. Шесть состояний

| Состояние | Смысл |
|-----------|--------|
| Idle | нет задачи, сам тактику не начинает |
| Defense | держать место / сектор |
| Attack | добиться результата в точке / зоне / по объекту |
| Search | искать по LastKnown |
| Retreat | уйти на другую позицию управляемо |
| Flee | бросить задачу, уйти от угрозы |

Не состояния: Observe, Track, Investigate, Engage, Chase, Suppress, Patrol.

Переходы **приказом** (тот же набор — без выхода/входа, контекст не затирается):

```
Idle     → Defense, Attack, Search, Flee
Defense  → Attack, Retreat, Idle, Search, Flee
Attack   → Defense, Retreat, Idle, Search, Flee
Search   → Attack, Defense, Idle, Retreat, Flee
Retreat  → Defense, Idle, Flee
Flee     → Idle
```

Запрещено: Idle→Retreat; Retreat→Attack/Search; Flee куда угодно кроме Idle.  
Search→Retreat открыт (отмена поиска). Attack / Retreat / Flee после этапа 4 **ходят**.

Писатель состояния — только контроллер AI. Зрение, бой, навигация состояние не пишут.

В продакшене приказы в контроллер **никто не шлёт**. Нет RTS/UI пути в эту машину. Автономно контроллер сам умеет только Search start/finish.

Обработчик Search выдаёт **один** Walk к snapshotted `SearchPosition`. Приход в 15 м — стоп, состояние Search. Found/stale — уже готовый ReturnState. Attack / Defense / Retreat / Flee ходят так же (этап 4): Walk к snapshot точки; Attack/Defense/Retreat после прихода стоп и остаются в состоянии; Flee после прихода стоп → Idle. Idle не ходит.

Контекст хранит место, не копию зрения: якорь обороны, пункт атаки, точка поиска, куда отступать. Уверенность и личность в контекст не копируются — читаются со снимка каждый тик.

## 3.2. Действие внутри состояния

Три действия: None / Hold / Engage.

```
Defense или Attack + есть Hostile и VisibleNow  → Engage
Defense или Attack + иначе                      → Hold
Idle / Search / Retreat / Flee                  → None
```

Engage-цель = среди Hostile+VisibleNow тот, у кого выше Threat.

Явно не Engage:

- Idle + видимый враг → остаётся Idle, действие None (сам войну не начинает);
- виден Unknown → это не Hostile → Hold;
- виден друг → Hold;
- враг только в памяти, не виден → Hold (Search — отдельно).

Факт «враг виден» живёт даже при действии None.

Engage **не** вызывает выбор цели боевого контура, навигацию, подъём оружия, выстрел. Никто в стрельбе это действие не читает.

## 3.3. Search по памяти

Старт, только если состояние Defense или Attack, враг **не** виден, и есть улика: Hostile с полезной памятью (conf > 0.25), либо hostile combat sound, либо hostile report. Источники не сливаются. Приоритет: Visual LastKnown > SoundPosition > Report. Куда вернуться — предыдущее Defense/Attack.

Idle сам Search не начинает. Stale (conf ≤ 0.25) Search не начинает.

На входе снимок **SearchArea** (центр / радиус 15 м / source / confidence). Кандидаты строятся один раз и кэшируются. Солдат ходит к кандидатам (arrival 1.5 м), на каждой точке STOP / Inspect ~1 с, затем следующая. Успех поиска — Hostile+VisibleNow, не радиус и не прибытие.

Конец:

- увидел Hostile → Found, вернуться в Defense или Attack;
- все кандидаты осмотрены → Exhausted, то же возвращение;
- память+звук+доклад кончились → Expired;
- ImmediateThreat → RoE / EmergencyCover, Search **не** завершается;
- явный приказ → Search отменяется;
- явный Search без «куда вернуться» + нашёл врага → Attack.

Search **не** пишет память, не двигает LastKnown, не вызывает decay. Destination — снимок на входе, не живой LastKnown каждый тик. Новый звук во время Search area не двигает. Контракт: `Closed/Search_2.md`. Baseline decision: `Closed/Search_Navigation_Execution.md`.

## 3.4. Правила применения силы

Отдельное поле, не состояние. Смена политики состояние не меняет. По умолчанию на новом контроллере: **SelfDefense**. ImmediateThreat — ручной bool, по умолчанию false, из восприятия **никто не выставляет**.

Пять уровней:

| Уровень | Задумка |
|---------|---------|
| SelfDefense | сила только против Hostile при ImmediateThreat |
| RestrictedDefense | сила против Hostile (зоны ещё нет) |
| MissionCombat | сила против Hostile |
| FullEngagement | сила против Hostile |
| NoFriendlyFire | сила против всех, кто не друг |

Матрица. Строки — политика. Столбцы — Relationship контакта. Да = разрешить силу. Друг всегда Нет на всех уровнях. Нет контакта = отказ.

| Политика | Друг | Нейтрал | Unknown | Враг без угрозы | Враг + ImmediateThreat |
|----------|------|---------|---------|-----------------|------------------------|
| SelfDefense | Нет | Нет | Нет | Нет | Да |
| RestrictedDefense | Нет | Нет | Нет | Да | Да |
| MissionCombat | Нет | Нет | Нет | Да | Да |
| FullEngagement | Нет | Нет | Нет | Да | Да |
| NoFriendlyFire | Нет | Да | Да | Да | Да |

RestrictedDefense, MissionCombat и FullEngagement в коде — **одна и та же матрица**. Разница имён без зонального оценщика.

Разрешено ≠ выстрел. Оценщик не вызывает выбор цели и не стреляет. Identity, UnitTeam, ThreatLevel он не читает — только Relationship и флаг ImmediateThreat.

Два юнита — две политики. Глобального static RoE нет. Отладочные кнопки «Игрок (сила)» / «Враг (сила)» в обычном Play справа сверху крутят уровень **всем** юнитам стороны и **добавляют** контроллер AI, если его не было.

Цикл кнопок:

```
SelfDefense → RestrictedDefense → MissionCombat → FullEngagement → NoFriendlyFire → SelfDefense
```

## 3.5. Как сила врезается в живую стрельбу

Сначала боевой контур считает Track/Aim/Fire как обычно.

Если на том же объекте есть контроллер AI:

- берётся Relationship **выбранного боевого** контакта (не цель Engage AI);
- если сила запрещена и решение было Fire или Aim → становится **Ignore**;
- Track не трогают;
- если разрешено — пропускают решение боя как есть (разрешено всё ещё не значит Fire).

Если контроллера нет — вето нет. Префаб без AI стреляет по правилам боя, включая Fire по Unknown.

Следствие: повесили AI и не сменили политику → SelfDefense + ImmediateThreat=false → по Hostile Aim/Fire режутся в Ignore → **юнит перестаёт стрелять**, даже если действие Engage.

---

# ЧАСТЬ 4. КАК ЭТО СХОДИТСЯ В СЦЕНЕ

## 4.1. Что стоит на префабе пехотинца

Есть: зрение глаз 150 м / 120° (оптика в Aiming до 300 / 8°), процессор знания, look принадлежности (`VisualIdentityEvidence`), выбор цели (retain = `ResolvedMaxRange`, Unknown можно выбирать), контроллер решения, оружие, дисциплина огня, hitscan = текущий обзор, навигационный драйвер, команда мира Player/Enemy/Neutral.

Нет: тактический AI, сенсор кадра AI.

Типичный враг на сцене патрулирует старым скриптом патруля **только если его повесили вручную**. На префабе его нет. Патруль читает Selected боевого контура, поднимает авто-позу, **не** жмёт спуск и **не** останавливает маршрут при контакте.

## 4.2. Два мозга, два мнения о «враге»

| Вопрос | Боевой контур | Тактический AI |
|--------|---------------|----------------|
| Кого считать врагом | Unknown можно выбрать и даже Fire | Только Relationship=Hostile |
| Когда «вижу» | Observed + луч прицела | Detected и Observed |
| Память | можно выбрать, решение Track | Hold, либо Search если Defense/Attack |
| Кто цель | своя формула очков | максимальный Threat среди видимых Hostile |
| Кто стреляет | дисциплина при решении Fire | никто |

Пока look Unknown (не задан спавном), боевой контур стреляет в неопознанных, а тактический AI не видит Hostile. После спавна look + ~2 с взгляда Identity коммитится. Этап 1 на этом останавливается: Engage и огонь из Identity **не** открывает.

## 4.3. Готовый конвейер vs дырка исполнения

Исполнено телом (этап 2 **FROZEN**):

```
Defense + вижу врага     → CombatIntent.Engage → Auto-поза → Combat сам выбирает → Aim/Fire при Allowed
Defense + нет Hostile    → Hold → Aim/Fire закрыты
SelfDefense без угрозы   → Engage может быть, выстрела нет
```

Ещё не исполнено:

```
Idle    → ждать приказ из игры
живой RoE / ImmediateThreat  →  #7 CLOSED 24.08.2026
```

Search **исполнен** (этап 3 **FROZEN**): один Walk к snapshotted LastKnown, стоп в 15 м, Found/stale → ReturnState. Attack / Defense / Retreat / Flee **исполнены** (этап 4 **FROZEN**): тот же Walk к snapshot точки. Навигацию зовёт только тактический слой. ClickToMove / RTS для этой ходьбы не используются.

---

# ЧАСТЬ 5. ДЫРЫ И ЛОВУШКИ

Ниже — факты, которые искажают решение «что чинить».

**1. Личность в мире — закрыто (этап 1 FROZEN).** `VisualIdentityEvidence` на префабе; спавн пишет look. Identity по-прежнему не жмёт спуск и не ставит Engage.

**2. Тактический AI не на префабе.** Арена вешает `UnitAIController` после спавна (боевые стороны). Без AI живая стрельба идёт без RoE. С AI огонь идёт через RoE стороны (`MissionCombat` на арене).

**3. ImmediateThreat был мёртвый вход — закрыт #7 (24.08.2026).** IncomingFire/ConfirmedHit ставят флаг. Threat High по-прежнему не равен ImmediateThreat.

**4. Три политики боя неотличимы.** RestrictedDefense / MissionCombat / FullEngagement — одно и то же. Зон нет.

**5. Звук и радио — розетка без вилки.** Выбор цели мог бы Track по выстрелу. В мире выстрел в эту розетку не пишется. AI выстрел не слышит.

**6. Старый retain 18 м снят (A7 CLOSED PASS 31/0).** На основном огневом пути 18 м и раньше почти не действовал. Живой огонь ограничен **текущим обзором** (150 без кратности, до 300 с ней). Retain при reload/misfire читает тот же `ResolvedMaxRange`. Не путать с EffectiveRange (урон) и с запасом луча 650 на префабе.

**7. Neutral невидим зрению.** В реестре есть, в кандидатах нет. Нейтрала нельзя ни обнаружить, ни опознать.

**8. Игровой канал команды есть (6.1–6.4).** `GameCommandInput` выбирает получателей; `GameCommandService` отдаёт `TacticalCommand`. Нет AI → `NoAI`. Спавн арены ставит Attack через debug `TryIssue`. HUD — production `IssueCommand`. RTS-клик — отдельный locomotion, не tactical cover. SHOT после приказа — #7.

**9. Патруль — параллельная вселенная.** Не состояние AI. Не связан с Search/Defense. На префабе отсутствует.

**10. Observe / Suppress / Report** зарезервированы и не используются. Ролей Scout/MG/командир нет.

**11. Кадр AI глух к звуку.** Даже когда звук появится в боевом контуре, Search/Engage его не увидят, пока канал не добавят в снимок — это уже смена контракта восприятия.

**12. Разные цели.** AI может Engage одного, Combat выбрать другого. Флаг `EngageTargetMismatch` это видит и **не** подменяет SelectedTarget.

**13. Конверт 150/300 закрыт, «ощущение боя» внутри него — частично.** Кривые точности **Stage 10 CLOSED**. Дисциплина **A3 CLOSED PASS 21/0**. Projectile Vision **A4+A5 CLOSED PASS 30/0**. Пассажир/турель **Stage 13 CLOSED PASS 35/0**. Retain **A7 CLOSED PASS 31/0**. Attention **B CLOSED PASS 44/0**. Это не баг Q и не повод открывать #7.

---

# ЧАСТЬ 6. ЧТО НЕ ДЕЛАТЬ СЛЕДУЮЩИМ ШАГОМ

Не открывать, пока не закрыт следующий исполнительный слой:

- ретюнить Q / память 5/30/1.5/0.25 / IdentifyTime / Threat 25/80 «чтобы AI стал умнее»;
- чинить кривые 0…500, РПГ или дисциплину 140/220 увеличением обзора или E ствола;
- встраивать поиск в зрение или в выбор цели;
- целиться или стрелять в LastKnown;
- считать Threat High приказом огня;
- считать Identity=Hostile уже выбранной и стреляющей целью;
- отряд, командир, utility AI, behaviour tree, мораль, укрытия, формации, авто-отход;
- одновременно чинить обнаружение, личность, выбор и стрельбу.

Калибровку выбора/огня как отдельный балансный блок тоже рано: сначала должно быть ясно, **кто** вообще имеет право поднять оружие.

---

# ЧАСТЬ 7. РЕШЕНИЕ: ЧТО ДЕЛАТЬ ДАЛЬШЕ

Ниже четыре развилки. A **FROZEN** 10:23. B **FROZEN** 10:56 / EditMode 11:25. C **FROZEN** 12:06 / EditMode 18/0. D — отдельно и позже.

## Развилка A. Дать миру улику «кто это» — FROZEN (2026-08-20 10:23)

`VisualIdentityEvidence` на префабе. Спавн пишет look. Наблюдатель маппит Player/Enemy/Civilian → Friendly/Hostile/Neutral. IdentifyTime / commit не менялись. Этап не открывает огонь.

Приёмка: Play C13 **PASS 49/0**; EditMode mapping **13/13**. Контракт: `Closed/Identity_World_Evidence.md`.

Не делать: читать UnitTeam как знание солдата. Улика — внешность, commit — через уже готовую математику. Слой не ретюнить.

## Развилка B. Исполнение боя из задачи — FROZEN (2026-08-20 10:56)

Сшит Engage с уже живым боевым контуром. Второй стрелок не создавался.

```
Defense / Attack + Hostile + VisibleNow
→ CombatIntent.Engage
→ RequestCombatReadiness (Auto)
→ Combat выбирает цель
→ G6 Aim/Fire
→ дисциплина стреляет, если ROE Allowed
```

Нет AI на объекте → intent-гейта нет, стрельба как раньше.  
Hold закрывает Aim/Fire, не выключает Combat.  
SelfDefense без ImmediateThreat: Engage есть, выстрела нет.  
Приёмка: Play **PASS 31/0**; EditMode **14/0**. Контракт: `Closed/Combat_Engage_Execution.md`.

Не делать: подменять SelectedTarget целью AI; звать Fire() из тактики; ходить в Search; класть AI на `Unit.prefab`; ретюнить слой.

## Развилка C. Исполнение ходьбы из задачи — FROZEN (2026-08-20 12:06)

Search ходит к LastKnown через существующий `UnitNavLocomotionDriver` (Walk). Не пишет память. Стоп в 15 м ≠ Found. Found/stale — уже готовые возвраты в Defense/Attack. Search→Retreat отменяет nav.

Приёмка: Play **PASS 45/0**; EditMode **18/0**. Меню: `Tools/Tests/Run Search Execution (Play)` / `(EditMode)`. Контракт: `Closed/Search_Navigation_Execution.md`.

## Развилка C2. Attack / Retreat / Flee — FROZEN (2026-08-20 14:46)

Attack / Defense / Retreat / Flee ходят через тот же `TacticalNavigationExecutor`, что и Search. Attack/Defense/Retreat: Stop, состояние на месте. Flee: Stop → Idle. Без точки — не ходить; Flee без точки остаётся Flee.

Приёмка: Play **PASS 36/0**; EditMode **31/0**. Контракт: `Closed/Tactical_Navigation_Execution.md`.

## Развилка C3. Контракт игровой команды — CLOSED (6.1)

Внешний приказ ≠ состояние. Игровой вход: `IssueCommand(TacticalCommand)`. Строгая таблица, без bounce. Same-state Attack(A)→Attack(B) перезаписывает context и перезаходит в handler. `TryIssue` остаётся отладкой.

Команда не стреляет, не пишет Vision/RoE, не выбирает цель. `Target` только прокидывается в Attack context.

Приёмка: EditMode `TacticalCommandContractTests`; Play `Tools/Tests/Run Tactical Command Contract (Play)`. Контракт: `Closed/Tactical_Game_Command_Contract.md`.

## Развилка C4. GameCommandService — CLOSED (6.2)

Игра говорит с юнитом через `GameCommandService` → `ITacticalCommandReceiver` → `IssueCommand`. Нет AI — отказ, контроллер не создаётся. `DebugGameCommandSource` — приёмочный источник с `Source=Game`, не RTS и не overlay.

Приёмка: EditMode `GameCommandServiceTests`; Play `Tools/Tests/Run Game Command Source (Play)`. Контракт: `Closed/Game_Command_Source.md`.

## Развилка C5. GameCommandInput — CLOSED (6.3)

Один input-слой, две аудитории (выбранные игроки / все живые Enemy). Оба шлют ту же `TacticalCommand` через `IssueMany`. Нет Group AI. `InputMode` ≠ `UnitAIState`. Обычный RTS RMB-ход в `Normal` не украден.

Приёмка: EditMode `GameCommandInputTests`; Play `Tools/Tests/Run Game Command Input (Play)`. Контракт: `Closed/Game_Command_Input.md`.

## Развилка C6. Game Command Layer — CLOSED (6.4)

Стабилизация: замена задачи, Cancel, живой collect, изоляция сторон. Вход — тот же `ConfirmPoint`. Combat isolation: команда не Fire и не назначает цель. SHOT — #7.

Приёмка: EditMode `GameCommandLayerTests`; Play `Tools/Tests/Run Game Command Layer (Play)`. Контракт: `Closed/Game_Command_Layer.md`.

## Дорожная карта

Perception A–F, A10 и **#7 CLOSED**. Следующий шаг — **#8**. Не чинить очередь через Q.

Тактический канон `#1…#26` зафиксирован в разделе «Дорожная карта» выше. **#7 CLOSED.** Attention — не приказ «держать сектор»: повернули юнита → взгляд впереди копит быстрее. Q не менять.

Не делать до своего этапа: прочёс, сектора как команда, экстраполяцию, aim в LastKnown, укрытия, групповой поиск; ретюнить 0.25 / 15 м / IdentifyTime / G6 / CombatIntent.

## Развилка D. Разобрана по номерам карты

То, что раньше сваливалось в Block D, теперь имеет слот:

| Было «потом» | Слот |
|--------------|------|
| Игровые приказы: контракт + сервис + ввод + слой | #6.1–6.4 CLOSED; живой RoE — #7 |
| Авто-ImmediateThreat / живой RoE | #7 |
| Боевой звук в мир | #8 |
| Звук в кадре AI / радио как reports | #9 |
| Расширение Search | #10 |
| Патруль как AI-режим | после #6, не новое состояние сразу |
| Калибровка «кого выбирать» | #12 |
| Динамические укрытия | #13 |
| Тактическое движение / lean | #14 |
| Роль оружия и поведение ранга | #15 |
| Отряд / CQB | #16 |
| Командир / planner | #24–#26 |
| Зоны RestrictedDefense | с #7, не отдельный ранний трек |
| Слияние 18 м и старого обзора 500 м | закрыто конвертом 150/300; 18 м по-прежнему не потолок боя |

---

# ЧАСТЬ 8. ДИАГНОСТИКА ПО ЛОГАМ

Для разбора Play вне репозитория достаточно **этого файла и папки сессии**. Код не нужен. Лог не меняет зрение, бой и AI: пишет смену состояния, не каждый кадр, не в консоль. В Editor Play пишется сам.

Пара для диагноза:

```
этот документ
+ папка Infantry_YYYYMMDD_HHMMSS
```

Без папки сессии файл всё ещё описывает, как устроен солдат. Без этого файла сырой лог нельзя однозначно разобрать.

## 8.1. Что лежит в папке сессии

```
Infantry_YYYYMMDD_HHMMSS/
  _index.txt          кто на сцене
  _timeline.log       кто кого задел в момент t
  Player/P01_Имя.log  полная цепочка одного солдата
  Enemy/E01_Имя.log
  Neutral/N01_Имя.log
```

Если папку копируют из проекта, она обычно живёт в `_Docs/Logs/Runtime/`. Для чтения это неважно: смотри содержимое папки, не дерево репозитория.

Слоты `P01` / `E01` / `N01` стабильны внутри одной сессии. В строках лога цели названы этими слотами.

Порядок чтения:

1. `_index.txt` — сторона мира, look, позывной, был ли тактический AI.
2. Файл юнита — цепочка **этого** солдата.
3. `_timeline.log` — стыки между юнитами (детекция, выстрел, смерть, приказ хода).

`SNAP` раз в 0.5 с только в файле юнита. Дискретные события — сразу. Один и тот же отказ ворот каждый кадр не дублируется; в `SNAP` поле `gate=` его повторяет.

Пример `_index.txt`:

```
P01  team=Player look=Player callsign=Alpha iid=… go=Player-01 file=Player/P01_Alpha.log ai=none
E01  team=Enemy look=Enemy callsign=Enemy-01 … ai=none
N01  team=Neutral look=Civilian callsign=Civilian-01 … ai=none
```

`ai=none` = тактического контроллера на объекте не было в момент записи индекса. Если его повесили кнопкой позже, в файле юнита появится `AI attached=1`.

## 8.2. Как устроена строка

```
12.340  VISION  tgt=E03 obs=Observed det=Detected Q=0.82 D=1.00 F=0.91 E=0.70 M=1.15 id=Hostile idC=0.51 …
```

| Кусок | Смысл |
|-------|--------|
| `12.340` | секунды от старта Play (`Time.time`) |
| `VISION` | какой контур написал строку (см. 8.3) |
| `ключ=значение` | факты этого события |

Тег = контур. Не ставить диагноз по чужому тегу: `MOVE` не значит «увидел», `SHOT` не значит «решил Fire».

## 8.3. Тег = слой (словарь для чтения лога)

Равенства из начала файла:

```
увидел              SCAN / VISION
понял кто это       VISION  id=  idWas=
выбран              SELECT
можно целиться      SELECT engageable=   /  G6 los=
решил стрелять      G6  raw=  final=
нажал спуск         GATE result=Success
пуля ушла           SHOT
снаряд выпущен      PROJECTILE result=Launch
пошёл / зачем       INPUT  /  GAMECMD  /  CMD issue  /  AI state=  /  MOVE reason=
погиб               DEATH
жизнь тела          LIFE / SNAP life=
слот укрытия        COVER_STATE
срез «сейчас»       SNAP
```

Когда появляется строка (без имён классов):

| Тег | Что случилось | Типичные ключи | В файле юнита | В timeline |
|-----|---------------|----------------|---------------|------------|
| `SPAWN` | юнит появился / сконфигурирован | team, look, body, weapon, ai, scanCandidates, pos | да | да |
| `SCAN` | сменился бюджет взгляда **или** полный проход не влез в лимит 8/кадр | tier, skip=DetailSlot, notALoss=1 | да | нет |
| `VISION` | знание о цели изменилось (увидел / потерял / опознал / threat / контакт исчез) | tgt, obs, det, Q D F E M, id, rel, threat, lastKnown, aim, p, memC | да | да, если контакт новый или стал Observed |
| `SELECT` | сменилась боевая цель, сильно сдвинулся score, или линию огня закрыл свой | selected, score, engageable, aim, runnerUp, rejected, lofSuppress | да | да, если сменилась цель |
| `G6` | сменилось намерение Track/Aim/Fire/Ignore или вето | raw, final, selected, los, weaponOk, aimReady, roe, intent, mismatch | да | да |
| `DISC` | дисциплина сменила фазу серии | phase, tgt, mode, needAim, series, pause | да | нет |
| `GATE` | попытка выстрела: успех или **новый** отказ | result, tgt, g6, pose, aimProg, fail | да | да, только Success |
| `SHOT` | луч/дробина уже ушли | tgt, hit, result, zone, part, dist, dmg, pose, weapon, pellet | да | да |
| `PROJECTILE` | попытка запуска RPG/MK19 (успех или deny), не кадр полёта | weapon, tgt, aim, distance, visionRange, physicalRange, result | да | нет |
| `MOVE` | приказ хода, стоп, приход, срыв NavMesh | verb, dest, snapped, tier, reason, ok, fail, source | да | да, кроме мелкого continuous |
| `AI` | сменилась задача, действие, цель Engage, RoE; либо контроллер повесили в Play | cause, state, action, intent, roe, engage, dest, search | да | да для state / attached |
| `CMD` | AI принял/отверг `IssueCommand` | verb=issue\|accepted\|rejected, type, pos, tgt, source, from, reason | да | да |
| `GAMECMD` | игровой сервис выдал / принял / отверг приказ юниту | verb=issue\|accepted\|rejected, unit, type, pos, tgt, source, reason | да | да |
| `INPUT` | слой ввода защёлкнул режим, выдал набор приказов, отменил latch или пропустил | mode, audience, verb=pending\|issue\|cancel\|skip, n, units, pos, skip= | нет | да |
| `SNAP` | сердцебиение 0.5 с | life, vision, obs, combat, move, cover=C2\|none, coverState, coverDistance, dest, remaining, g6, selected, ai, aiAction | да | нет |
| `DEATH` | юнит погиб | dead, pos | да | да |
| `POSITION_ACQUIRE` | попытка занять точку (не тик) | result, reason, distance, tolerance, remaining, pathStatus, velocity | да | да |
| `COVER_STATE` | lifecycle слота (не score) | unit, candidate, state=Reserved\|Approaching\|Acquired\|Occupied\|Released, reason, dist | да | да |
| `COVER_DECISION` | смена Stay/Reposition | unit, current, best, decision, reason, score | да | да |
| `COVER_REF` | тот же объект слота | unit, coverId, candidateRef=0x…\|MISSING, phase=Reserve\|Acquire\|ConfirmOccupied | да | да |
| `COVER_INVALID` | почему current invalid | unit, cover, reason=CandidateMissing\|ReservationLost\|TooFar\|ExposureChanged\|PathInvalid | да | да |
| `COVER_HEARTBEAT` | Keep (≤1 с) или Release | unit, cover, action=Keep\|Release, reason, remaining, pathValid | да | да |
| `MOVE_COVER` | последний метр к слоту | unit, cover, goal, acquire, unitPos, agentPos, remaining, velocity, stoppingDistance, radius, distance, pathStatus | да | да |
| `AI_TRANSITION` | Attack↔Search | unit, from, to, reason=LostCurrentTarget\|HostileVisible\|ImmediateThreat\|CommandChanged, target, immediateThreat | да | да |
| `READINESS` | смена / запрос перехода ReadinessState | state= / transition=From->To reason= duration= | да | да |
| `READINESS_POSE` | запрос физической позы | state= pose= [transition= duration=] [reason=LifeGate] | да | да |
| `LIFE` | смена Alive / Unconscious / Dead | life, was, reason=Damage, health, consciousness, ai, vision, combat, move, cover, coverReleased, navStopped | да | да |

`MOVE source=`: `Tactical` = зачем (Search/Attack/Retreat/Flee); `NavDriver` = как сняли точку на NavMesh; `ClickToMove` = приказ игрока RTS (`reason=Rts`). Один тактический шаг часто даёт обе строки Tactical и NavDriver — это не два приказа.

`SCAN skip=DetailSlot notALoss=1` = в этом кадре полный проход не сделали. Это **не** «цель пропала» (см. 1.4).

## 8.4. Словарь значений

Без этой таблицы лог нельзя читать вне репозитория. Значения совпадают с частями 1–3.

**VISION `obs=`** — взгляд: `Observed` (в кадре) / `RecentlyLost` (0–5 с) / `Lost` (дальше) / `NotObserved`.

**VISION `det=`** — прогресс: `Undetected` / `Detecting` / `Detected`. `Detected` + `id=Unknown` нормально.

**VISION `Q=` и `D F E M`** — качество кадра: Distance × FOV × Exposure × Movement. Порог заметить 0.25, потерять 0.20 (часть 1.6).

**VISION `id=` / `rel=` / `threat=`** — кем считает **этот** наблюдатель, не команда мира. Маппинг look → id: таблица в 1.8. Threat только при rel=Hostile: High ≤25 м, Medium ≤80 м, иначе Low.

**SELECT `rejected=`** — почему кандидат не взят: `Friendly`, `NeutralIdentity`, `Forgotten`, `NotWorldEngageable`, `UnknownDisallowed`, `StaleDisallowed`, `LoFSuppressed`, `NoTarget`. Unknown по умолчанию **не** отсекают.

**G6 `raw=` / `final=`** — намерение до и после вето. Возможные имена: `None` `Ignore` `Track` `Aim` `Fire`. `Observe` / `Suppress` / `Report` в живой политике нет.  
`raw=Fire final=Ignore` = вето (RoE или Hold), не промах.

**G6 `intent=`** — `Engage` разрешает Aim/Fire, `Hold` закрывает. `n/a` = на объекте нет тактического AI, бой стреляет как на префабе.

**G6 `roe=`** — `Allowed/…` или `Denied/…`. Разрешено ≠ выстрел. Нет AI → `n/a`.

**DISC `phase=`** — `Idle` / `Aiming` / `Firing` / `Pause`.

**GATE `result=`** (отказ пишется при смене причины; `FireRateLimited` не пишется):

| result | Смысл |
|--------|--------|
| Success | патрон израсходован, дальше должен быть SHOT |
| NotReady | поза / ready не стрелковые |
| NotAimed | ствол мимо допуска |
| NotAimedProgress | AimProgress ниже порога |
| NoVisibleTarget | нет engageable цели |
| LineOfFireBlocked | в стволе свой/нейтрал |
| EmptyMagazine / NoMagazine / NeedsBoltCycle | магазин / патронник |
| Busy | перезарядка, анимация, без сознания |
| NoWeapon / MalfunctionStoppage / WeaponBroken | нет оружия или отказ |

Стрелковые позы: HipFire / HipFireWalk / HipFireCrouchWalk / PointAim / Aiming. LowReady / HighReady / NotReady / PreAim — из них выстрела нет.

**SHOT `result=`** — `HitTarget` / `Miss` / `HitOther` (препятствие) / `BlockedBySelf`.

**MOVE** первая колонка после тега: `issue` приказ, `continuous` сопровождение точки, `stop` стоп, `reached` дошёл, `fail` срыв, `defer` RTS ждёт двойной клик.  
`reason=`: `Search` `Attack` `Retreat` `Flee` `Rts` `None`. Укрытий в причине нет.

**AI `state=`** — Idle Defense Attack Search Retreat Flee.  
**AI `action=`** — None Hold Engage. Engage ≠ Fire.

**SPAWN `scanCandidates=`** — у Neutral всегда `none`: их не сканируют и они никого не сканируют. Это фильтр мира, не баг логгера.

**SNAP** (срез; ключи те же, плюс):

```
life= vision= obs= combat= move= cover=C2|none coverState= coverDistance= coverTolerance= coverReserved=
pos vel stance pose g6 selected dest moveGoal remaining reason gate
ai=Attack aiAction=Engage intent= roe= engage=     ← unconscious/dead: ai=off
contacts= vis= mem= | E03:Observed/Detected/Hostile/Q0.82
```

`life=` — Alive / Unconscious / Dead. `cover=` на SNAP: `C2` слот или `none`. Состояние слота — `coverState=` (None / Approaching / Occupied). `vision=`/`combat=`/`move=` — активность контура. `obs=` — Observed / RecentlyLost / none / off. `COVER_DECISION` и `POSITION_DECISION` пишутся вместе, только при смене решения, не каждый кадр.

`vis=` сколько Observed сейчас, `mem=` сколько с полезной памятью (conf > 0.25) без взгляда.

## 8.5. Поля, которые нельзя путать

Как в частях 1–3. В логе они рядом — это не одно и то же.

| Поле | Чей смысл | Где в логе |
|------|-----------|------------|
| `team=` в `SPAWN` | `UnitTeam` мира | не то, кем солдат считает цель |
| `look=` в `SPAWN` | `VisualAffiliation` на цели | улика внешности |
| `id=` в `VISION` | `PerceivedIdentity` **этого** наблюдателя | commit личности |
| `rel=` | отношение из закоммиченной личности | не копия UnitTeam |
| `lastKnown=` | память места | не точка прицела |
| `aim=` / `aimPt=` | LOS-прицел из последнего наблюдения | только при Observed |
| `selected=` | победитель G5 | не значит Fire |
| `engageable=` | selected + жив + есть aim point | не значит выстрел |
| `raw=` / `final=` | G6 до и после вето RoE/Hold | `raw=Fire final=Ignore` — вето, не промах |
| `intent=` | CombatIntent Hold/Engage | нет AI на объекте → `n/a` |
| `roe=` | разрешение силы | Allowed ≠ Fire |
| `reason=` в `MOVE` | зачем выдан nav | укрытий/POI в коде нет |
| `life=` | тело Alive/Unconscious/Dead | не UnitAIState |
| `COVER_STATE Acquired` | дошёл до слота по геометрии | не Occupied |
| `COVER_STATE Occupied` | board подтвердил занятие | не POSITION_ACQUIRE |
| `cover=` в `POSITION_ACQUIRE` | `0` dest-only / `C1` реальный слот | не SNAP cover= и не boolean 0\|1 |
| `cover=` в `SNAP` | `C2` id слота или `none` | не Occupied/Reserved; это `coverState=` |
| `candidateRef=MISSING` | объект CoverCandidate потерян | дыра Acquire без ConfirmOccupied |
| `POSITION_ACQUIRE reason=CandidateMissing` | id слота есть, объекта нет | не путать с OutOfTolerance |
| `scanCandidates=` | фильтр реестра зрения | Neutral: **никогда не кандидат и сам никого не сканирует** |

`SNAP` одной строкой: `g6=`, `selected=`, `engageable=`, `dest=`, `reason=`, `gate=`, `ai=State/Action`, список `E03:Observed/Detected/Hostile/Q0.82`.

## 8.6. Как ставить диагноз (сценарий → какие теги)

Идти сверху вниз. Первый разрыв — виноватый слой. Не ретюнить замороженные числа, пока лог не показал этот слой.

**«Не видит»**

1. `SPAWN scanCandidates=none` у Neutral — так и должно быть. Игроки и враги гражданских контуром зрения **не видят**.
2. Нет `VISION` на цель — смотри `SCAN`. `skip=DetailSlot notALoss=1` = в этом кадре не сканировал, цель не пропала.
3. Есть `VISION`, но `obs=RecentlyLost` / `det=Detecting` / `Q=` низкий — зрение/Q/экспозиция, не стрельба.
4. `id=Unknown` при `det=Detected` — нормально (ещё не commit). Не ждать, что SELECT отсечёт Unknown: по умолчанию Unknown **можно** выбирать.

**«Видит, но не выбирает»**

`VISION` есть, `SELECT selected=none` или `rejected=N01:NeutralIdentity` / `Friendly` / `LoFSuppressed`. Слой G5. `SELECT lofSuppress` — линия огня через союзника/нейтрала, не «пропал из зрения».

**«Выбрал, но не стреляет»**

Цепочка должна быть полной:

```
SELECT engageable=1  →  G6 raw=Fire final=Fire  →  DISC phase=Firing  →  GATE result=Success  →  SHOT
RPG/MK19: тот же G6 Fire → GATE / projectile permit → PROJECTILE result=Launch
```

Разрыв:

| Что в логе | Смысл |
|------------|--------|
| `G6 los=0` / `SELECT engageable=0` | нет LOS-прицела. LastKnown ≠ прицел → будет Track, не Fire |
| `G6 raw=Fire final=Ignore roe=Denied` | на объекте AI, default SelfDefense режет Aim/Fire |
| `G6 intent=Hold` | CombatIntent закрыл Aim/Fire. Нет AI → `intent=n/a`, бой стреляет как раньше |
| `G6 mismatch=1` | цель AI и цель боя разные; стреляет **боевой** selected |
| `DISC phase=Idle` при `g6=Fire` | дисциплина не держит контакт (поза / оружие / не Aim|Fire) |
| `GATE result=NotReady` / `pose=LowReady` | нет стрелковой позы |
| `GATE result=NotAimed` / `NotAimedProgress` | ствол или AimProgress |
| `GATE result=NoVisibleTarget` | нет engageable |
| `GATE result=LineOfFireBlocked` | затем `SELECT lofSuppress` |
| `GATE Success`, нет `SHOT` | не должно быть: hitscan зовётся из Success. Если так — смотреть hitscan |
| `PROJECTILE fireDenied=NoAimPoint` | LastKnown / нет Observed AimPoint |
| `PROJECTILE fireDenied=OutsideVision` | AimPoint дальше `ResolvedMaxRange` |
| `SHOT result=Miss` при `G6 final=Fire` | решение было верное, пуля не попала (разброс, упреждение) |

**«Пошёл не туда» / «стоит»**

Укрытий и точек интереса в выборе маршрута **нет**. `MOVE reason=` — факт: `Search` / `Attack` / `Retreat` / `Flee` / `Rts` / `None`.

Цепочка игрового приказа:

```
INPUT mode=AttackPending audience=PlayerSelected verb=issue n=2 units=… pos=…
  → GAMECMD issue type=Attack  →  CMD issue  →  CMD accepted  →  AI state=Attack  →  MOVE reason=Attack
```

Разрыв: нет `INPUT` — приказ не через `GameCommandInput` (сервис/тест напрямую). Нет `GAMECMD` — приказ не через `GameCommandService`. `INPUT skip=NoRecipients` — пустое выделение или пустой debug-набор. `GAMECMD rejected reason=NoAI` — на юните нет тактического AI. Нет `CMD` при debug `TryIssue` (пишет `AI`). Есть `CMD rejected` — таблица или нет точки. Есть `AI` без `MOVE` — Defense / нет точки / нет драйвера.

- `AI state=Search` + `MOVE reason=Search` + `search=(…)` — идёт к snapshot LastKnown, стоп ~15 м, память не пишет.
- `SNAP dest=none reason=None` при движении — приказ не из тактики и не из ClickToMove (патруль или другой драйвер).
- `MOVE fail=SamplePosition` — точка не на NavMesh.
- Нет `AI` в файле и `SPAWN ai=none` — на префабе нет контроллера; ходьбу тактики не ждать, пока его не повесят.

**«Повесили AI — перестал стрелять»**

Искать `G6 raw=Fire final=Ignore roe=Denied:…`. Для теста: Defense + политика сильнее SelfDefense (в документе — MissionCombat), либо снять AI.

**«Выстрелил в гражданского»**

Сначала `_index`: `team=` и `look=` жертвы. Потом файл стрелка: был ли `VISION tgt=N…` (при текущем фильтре кандидатов — не должен). Если `SELECT` взял Neutral identity — это уже знание наблюдателя, не команда мира. `SPAWN` жертвы `scanCandidates=none` не мешает её убить hitscan’ом: зрение и попадание — разные контуры.

## 8.7. Чего в логе не будет

- Сырой список лучей кадра (есть только знание-контакт после процессора).
- Каждый тик Q и каждый Update G6 — только смена.
- Sound/shared в продакшене: в мире никто не шлёт; если появятся — `VISION sound=` / `shared=`.
- Приказ «займи укрытие» / POI — слоя нет, в `reason=` его не будет.
- Теги `Observe` / `Suppress` / `Report` в G6 — политика их не возвращает.
- Консоль Unity: `IK-TARGET-JUMP` / `IK-GRIP-*` и `[CombatIntent] AI EngageTarget != Combat SelectedTarget` **не пишутся**. Расхождение целей — только `G6 mismatch=1` в файле юнита.

Код для диагноза не нужен. Если лог показал виноватый контур — чинить этот контур, не соседний. Замороженные числа из части 1.9 не крутить, пока тег не указал именно зрение.

## 8.8. Play 28.08.2026: `Infantry_20260828_113530`

Сессия **11:35:30**, запись **~181 с**. Арена 150×50 **после Editor wiring / bake**. Сырые логи: `_Docs/Logs/Runtime/Infantry_20260828_113530/`.

Слоты логгера все в `Player/P01…P90`. Сторона — `SPAWN team=`. Enemy это **P11+**, не E01. `ai=UnitAIController` (на префабе выключен, арена включает Player/Enemy).

| Волна | t | Кто |
|-------|---|-----|
| 0 | 0.000 | P01–P10 Player, P11–P20 Enemy, P21–P40 Neutral |
| 1 | 121.5 | +10 Enemy (P41–P50) |
| 2 | 150.7 | +10 Player +10 Enemy |
| 3 | 181.0 | ещё волна, сессия оборвалась |

### Цепочка волны 0 (боевые, не Neutral)

```
0.000  SPAWN   team=Player|Enemy  ai=UnitAIController  pos=линия Z≈11/15 или Z≈135/139
0.020  AI      roe=SelfDefense->MissionCombat
0.020  CMD     Attack Accept  dest≈(0, 75)
0.020  ROUTE   mode=Tactical  hop к центру
0.020  MOVE    reason=Attack
0.353  POSITION_DECISION  Reposition C0→Cx  reason=CurrentInvalid
0.353  COVER_HOP  candidate=Cx reserved=1
       … подход …
       POSITION_ACQUIRE  Acquired  dist≤0.60   или  OutOfTolerance
```

Cover **видят и бронируют**. Occupied в timeline по-прежнему не пишется (`state=Occupied` нет). Search↔Attack (OverlaySearch) — шум, не отдельная миссия. Neutral: Idle, SelfDefense, без Attack/cover/SHOT, стоят на спавне.

### Волна 0 Player — что решил каждый

Attack dest у всех ≈ центр (z≈75). Hop/reserve — слоты C1–C5.

| Слот | Оружие | Спавн | Cover | Acquire | G6 / SHOT | Исход |
|------|--------|-------|-------|---------|-----------|-------|
| P01 | M4_ModA_1 | (−18, 11) | hop C1,C2 | нет | Track/Aim, **0** | unconscious |
| P02 | MK18 | (−12, 11) | C1–C3 | C0 **OutOfTolerance** dist=1.03 | Track/Aim, **0** | unconscious |
| P03 | MK12 | (−2, 11) | C1–C5 | C0 **Acquired** dist=0.54 | Fire, **1** | жив, engage P17/P15/P14 |
| P04 | M16A_ModA_1 | (4, 11) | C1–C4 | нет | Fire, **10** | unconscious, engage много Enemy |
| P05 | BenelliM4 | (16, 11) | C1–C4 | C0 **Acquired** dist=0.42 | Fire, **45** | жив, engage P18/P19/P11 |
| P06 | MK18 | (−16, 15) | C1,C2 | нет | Fire, **11** | жив |
| P07 | M4_ModA_1 | (−4, 15) | C1–C5 | **C1 Acquired** dist=0.59 | Fire, **12** | unconscious, engage P15 |
| P08 | Sniper762x51 | (2, 15) | C1–C4 | C0 **OutOfTolerance** dist=1.46 | Fire, **3** | жив |
| P09 | M249 | (12, 15) | C1–C4 | C0 Acquired ×2, потом OutOfTolerance 0.84 | Track, **0** | unconscious |
| P10 | M16A4_ModA_2 | (18, 15) | C1–C3 | **C1 Acquired** dist=0.00…0.27 | Fire, **5** | unconscious |

Итого Player: **пятеро acquire** (P03, P05, P07, P09, P10), двое стабильно мимо слота (P02, P08). Огонь в основном P04–P07. P01/P02/P09 без выстрела.

### Волна 0 Enemy — что решил каждый

Та же цепочка Attack к центру + COVER_HOP. **Acquire ни у кого.** Все десять **DEATH**. Выстрелов почти нет (G6 чаще Track/Aim, SHOT 0–1).

| Слот | Оружие | Спавн | Engage | DEATH t | Позиция смерти |
|------|--------|-------|--------|---------|----------------|
| P11 | Mosin | (−18, 139) | P03,P08,P10,P05,P04 | 126.7 | (−16.7, 91.7) |
| P12 | AK74UMOD1 | (−12, 139) | много Player | 180.3 | (−7.0, 91.0) |
| P13 | RPK47 | (−2, 139) | P08 | 170.9 | (0.4, 96.7) |
| P14 | PKM | (4, 139) | P08,P03,P10,P05 | 170.9 | (2.3, 102.6) |
| P15 | SVD | (16, 139) | много Player | 127.8 | (2.6, 98.6) |
| P16 | RPK74MOD1 | (−16, 135) | P10,P05,P08… | **92.9** первый | (−7.4, 88.8) |
| P17 | RPK74MOD1 | (−4, 135) | P08,P04,P10,P03,P05 | 157.2 | (2.4, 100.8) |
| P18 | RPK74MOD1 | (2, 135) | много Player | 159.2 | (2.7, 96.3) |
| P19 | AK47_1 | (12, 135) | P04,P07,P03,P10 | 108.9 | (1.8, 95.5) |
| P20 | AK47S | (18, 135) | P08,P10,P04,P03 | 143.6 | (3.0, 102.3) |

Картина боя: Player доходят до середины/дальше и стреляют; Enemy бронируют слоты, почти не подтверждают acquire и гибнут у z≈90–100.

### Neutral P21–P40

Idle, RoE SelfDefense, `weapon=none`, `scanCandidates=none`. SNAP: `pose=NotReady dest=none gate=NoWeapon`. Слышат шаги, VISION не коммитит. Нет COVER_HOP, SHOT, DEATH.

Ниже — карточки всех слотов этого прогона (волны 0–3), без timeline.

### Волна 0 - Player P01-P10

#### P01 - Player, Soldier

- Spawn t=0.000 pos=(-18.0, 0.0, 11.0), weapon=Item_Weapon_M4_ModA_1, log until t=181.315
- Outcome: unconscious (MOVE fail)
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (1.2, 0.1, 75.6)
- Engage: P12, P43
- Cover hop: C1, C2
- Reserved: C1, C2
- Acquire: none (Occupied not confirmed)
- PositionDecision first 3 of 3:
  - 0.353  Reposition C0->C2 (CurrentInvalid) score=3.9
  - 4.484  Stay C0->C0 (NoCandidate) score=1.7
  - 24.853  Reposition C0->C1 (CurrentInvalid) score=3.2
- Combat: G6=Track, Ignore, Aim, SHOT=0, ImmediateThreat=2, SearchAttackOsc=220
- Last SNAP t=181.315 pos=(-7.8, -0.7, 66.4) ai=Attack/Hold dest=none gate=NoWeapon engage=none

#### P02 - Player, Soldier

- Spawn t=0.000 pos=(-12.0, 0.0, 11.0), weapon=Item_Weapon_MK18, log until t=181.315
- Outcome: unconscious (MOVE fail)
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (1.3, 0.1, 74.9)
- Engage: P42, P43
- Cover hop: C1, C2, C3
- Reserved: C1, C2, C3
- Acquire:
  - 5.229  C0 Rejected OutOfTolerance dist=1.03 tol=0.60
- PositionDecision first 8 of 16:
  - 0.353  Reposition C0->C3 (CurrentInvalid) score=3.6
  - 3.126  Stay C4->C3 (Committed) score=4.1
  - 3.379  Reposition C4->C1 (BetterTacticalPosition) score=4.4
  - 3.502  Stay C4->C1 (Committed) score=4.4
  - 3.976  Reposition C0->C1 (CurrentInvalid) score=4.4
  - 4.154  Reposition C0->C3 (CurrentInvalid) score=4.7
  - 4.319  Reposition C0->C1 (CurrentInvalid) score=4.4
  - 4.681  Reposition C0->C3 (CurrentInvalid) score=4.8
- Combat: G6=Track, Ignore, Aim, SHOT=0, ImmediateThreat=2, SearchAttackOsc=8
- Last SNAP t=181.315 pos=(10.2, -0.4, 60.0) ai=Attack/Hold dest=none gate=NoWeapon engage=none

#### P03 - Player, Soldier

- Spawn t=0.000 pos=(-2.0, 0.0, 11.0), weapon=Item_Weapon_MK12, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-0.9, 0.1, 76.0)
- Engage: P17, P15, P14
- Cover hop: C1, C2, C3, C4, C5
- Reserved: C1, C2, C3, C4, C5
- Acquire:
  - 107.186  C0 Acquired dist=0.54 tol=0.60
- PositionDecision first 8 of 33:
  - 0.353  Reposition C0->C5 (CurrentInvalid) score=5.4
  - 3.379  Stay C0->C0 (NoCandidate) score=1.7
  - 6.323  Reposition C0->C1 (CurrentInvalid) score=5.1
  - 14.070  Reposition C0->C4 (CurrentInvalid) score=3.2
  - 14.223  Reposition C0->C3 (CurrentInvalid) score=4.2
  - 15.244  Reposition C0->C4 (CurrentInvalid) score=3.0
  - 18.223  Reposition C0->C3 (CurrentInvalid) score=3.0
  - 19.230  Stay C0->C0 (NoCandidate) score=1.7
- Combat: G6=Track, Aim, Ignore, Fire, SHOT=1, ImmediateThreat=0, SearchAttackOsc=11
- Last SNAP t=181.315 pos=(13.6, 0.1, 78.7) ai=Search/None dest=(1.9, 0.1, 84.2) gate=Success engage=none

#### P04 - Player, Soldier

- Spawn t=0.000 pos=(4.0, 0.0, 11.0), weapon=Item_Weapon_M16A_ModA_1, log until t=181.315
- Outcome: unconscious (MOVE fail)
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-1.3, 0.1, 74.9)
- Engage: P17, P13, P18, P19, P14
- Cover hop: C1, C2, C3, C4
- Reserved: C1, C2, C3, C4
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 72:
  - 0.353  Reposition C0->C1 (CurrentInvalid) score=5.3
  - 5.889  Reposition C0->C4 (CurrentInvalid) score=2.2
  - 15.084  Reposition C0->C3 (CurrentInvalid) score=3.8
  - 15.244  Reposition C0->C2 (CurrentInvalid) score=1.5
  - 15.762  Reposition C0->C1 (CurrentInvalid) score=1.4
  - 15.873  Reposition C0->C4 (CurrentInvalid) score=1.6
  - 16.007  Reposition C0->C1 (CurrentInvalid) score=1.4
  - 16.835  Stay C0->C0 (NoCandidate) score=1.7
- Combat: G6=Ignore, Fire, Aim, Track, SHOT=10, ImmediateThreat=10, SearchAttackOsc=44
- Last SNAP t=181.315 pos=(13.9, -0.3, 81.0) ai=Attack/Hold dest=none gate=Success engage=none

#### P05 - Player, Soldier

- Spawn t=0.000 pos=(16.0, 0.0, 11.0), weapon=Item_Weapon_BenelliM4, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (1.3, 0.1, 74.8)
- Engage: P18, P19, P11
- Cover hop: C1, C2, C3, C4
- Reserved: C1, C2, C3, C4
- Acquire:
  - 30.337  C0 Acquired dist=0.42 tol=0.60
- PositionDecision first 8 of 35:
  - 0.353  Stay C0->C0 (NoCandidate) score=1.7
  - 6.042  Reposition C0->C3 (CurrentInvalid) score=1.2
  - 6.479  Reposition C0->C4 (CurrentInvalid) score=1.7
  - 7.437  Reposition C0->C2 (CurrentInvalid) score=0.8
  - 7.547  Reposition C0->C3 (CurrentInvalid) score=1.5
  - 12.908  Reposition C0->C1 (CurrentInvalid) score=3.9
  - 15.084  Stay C1->C1 (ImprovementTooSmall) score=4.8
  - 15.485  Reposition C0->C2 (CurrentInvalid) score=3.1
- Combat: G6=Track, Ignore, Fire, Aim, SHOT=45, ImmediateThreat=0, SearchAttackOsc=10
- Last SNAP t=181.315 pos=(15.3, 0.1, 79.9) ai=Search/None dest=(1.9, 0.1, 84.2) gate=Success engage=none

#### P06 - Player, Soldier

- Spawn t=0.000 pos=(-16.0, 0.0, 15.0), weapon=Item_Weapon_MK18, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-0.7, 0.1, 76.2)
- Engage: P12, P16
- Cover hop: C1, C2
- Reserved: C1, C2, C3
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 12:
  - 0.353  Reposition C0->C3 (CurrentInvalid) score=4.5
  - 0.614  Reposition C0->C1 (CurrentInvalid) score=3.4
  - 2.970  Stay C0->C0 (NoCandidate) score=1.7
  - 15.084  Reposition C0->C2 (CurrentInvalid) score=4.6
  - 23.530  Stay C2->C2 (Committed) score=6.2
  - 24.965  Stay C2->C0 (NoCandidate) score=6.4
  - 28.328  Stay C2->C1 (ImprovementTooSmall) score=6.0
  - 43.416  Reposition C0->C1 (CurrentInvalid) score=1.8
- Combat: G6=Track, Ignore, Fire, SHOT=11, ImmediateThreat=0, SearchAttackOsc=5
- Last SNAP t=181.315 pos=(-9.6, 0.1, 60.8) ai=Search/None dest=(17.2, 0.1, 89.9) gate=Success engage=none

#### P07 - Player, Soldier

- Spawn t=0.000 pos=(-4.0, 0.0, 15.0), weapon=Item_Weapon_M4_ModA_1, log until t=181.315
- Outcome: unconscious (MOVE fail)
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-1.3, 0.1, 75.1)
- Engage: P15
- Cover hop: C1, C2, C3, C4, C5
- Reserved: C1, C2, C3, C4, C5
- Acquire:
  - 28.843  C1 Acquired dist=0.59 tol=0.60
- PositionDecision first 8 of 24:
  - 0.353  Reposition C0->C5 (CurrentInvalid) score=3.9
  - 0.614  Reposition C0->C4 (CurrentInvalid) score=1.2
  - 3.379  Reposition C0->C5 (CurrentInvalid) score=3.9
  - 5.470  Stay C0->C0 (NoCandidate) score=1.7
  - 5.889  Reposition C0->C1 (CurrentInvalid) score=5.1
  - 7.063  Reposition C0->C3 (CurrentInvalid) score=2.5
  - 14.070  Reposition C0->C1 (CurrentInvalid) score=4.0
  - 14.819  Stay C2->C1 (Committed) score=4.2
- Combat: G6=Track, Ignore, Aim, Fire, SHOT=12, ImmediateThreat=1, SearchAttackOsc=44
- Last SNAP t=181.315 pos=(5.5, -0.7, 65.1) ai=Attack/Hold dest=none gate=Success engage=none

#### P08 - Player, Soldier

- Spawn t=0.000 pos=(2.0, 0.0, 15.0), weapon=Item_Weapon_Sniper762x51, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (1.2, 0.1, 74.4)
- Engage: P17, P13, P19, P14
- Cover hop: C1, C2, C3, C4
- Reserved: C1, C2, C3, C4
- Acquire:
  - 164.318  C0 Rejected OutOfTolerance dist=1.46 tol=0.60
  - 164.985  C0 Rejected OutOfTolerance dist=1.46 tol=0.60
- PositionDecision first 8 of 49:
  - 0.353  Stay C0->C0 (NoCandidate) score=1.7
  - 4.484  Reposition C0->C3 (CurrentInvalid) score=2.4
  - 6.889  Reposition C0->C2 (CurrentInvalid) score=1.7
  - 14.070  Reposition C0->C3 (CurrentInvalid) score=4.2
  - 14.223  Reposition C0->C2 (CurrentInvalid) score=2.6
  - 15.084  Reposition C0->C3 (CurrentInvalid) score=4.0
  - 18.080  Stay C0->C0 (NoCandidate) score=1.7
  - 28.635  Reposition C0->C2 (CurrentInvalid) score=2.2
- Combat: G6=Track, Ignore, Aim, Fire, SHOT=3, ImmediateThreat=0, SearchAttackOsc=12
- Last SNAP t=181.315 pos=(15.3, 0.1, 80.9) ai=Search/None dest=(1.9, 0.1, 84.2) gate=NeedsBoltCycle engage=none

#### P09 - Player, Soldier

- Spawn t=0.000 pos=(12.0, 0.0, 15.0), weapon=Item_Weapon_M249, log until t=181.315
- Outcome: unconscious (MOVE fail)
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-1.3, 0.1, 75.4)
- Cover hop: C1, C2, C3, C4
- Reserved: C1, C2, C3, C4
- Acquire:
  - 16.007  C0 Acquired dist=0.54 tol=0.60
  - 85.727  C0 Acquired dist=0.45 tol=0.60
  - 172.559  C0 Rejected OutOfTolerance dist=0.84 tol=0.60
- PositionDecision first 8 of 54:
  - 0.353  Stay C0->C0 (NoCandidate) score=1.7
  - 5.470  Reposition C0->C4 (CurrentInvalid) score=1.6
  - 5.610  Reposition C0->C3 (CurrentInvalid) score=1.2
  - 5.748  Reposition C0->C4 (CurrentInvalid) score=1.5
  - 6.479  Reposition C0->C3 (CurrentInvalid) score=1.3
  - 7.437  Reposition C0->C4 (CurrentInvalid) score=2.1
  - 8.445  Reposition C0->C2 (CurrentInvalid) score=0.7
  - 12.908  Reposition C0->C1 (CurrentInvalid) score=3.6
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=7, SearchAttackOsc=16
- Last SNAP t=181.315 pos=(13.1, -0.3, 81.3) ai=Attack/Hold dest=none gate=NoWeapon engage=none

#### P10 - Player, Soldier

- Spawn t=0.000 pos=(18.0, 0.0, 15.0), weapon=Item_Weapon_M16A4_ModA_2, log until t=181.315
- Outcome: unconscious (MOVE fail)
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-0.5, 0.1, 76.2)
- Engage: P13, P16, P18, P11
- Cover hop: C1, C2, C3
- Reserved: C1, C2, C3, C4
- Acquire:
  - 163.318  C1 Acquired dist=0.00 tol=0.60
  - 163.985  C1 Acquired dist=0.00 tol=0.60
  - 164.985  C1 Acquired dist=0.00 tol=0.60
  - 165.318  C1 Acquired dist=0.20 tol=0.60
  - 165.652  C1 Acquired dist=0.06 tol=0.60
  - 165.985  C1 Acquired dist=0.05 tol=0.60
  - 166.318  C1 Acquired dist=0.27 tol=0.60
- PositionDecision first 8 of 25:
  - 0.353  Stay C0->C0 (NoCandidate) score=1.7
  - 3.976  Reposition C0->C4 (CurrentInvalid) score=1.6
  - 5.610  Reposition C0->C1 (CurrentInvalid) score=1.7
  - 12.795  Stay C1->C1 (ImprovementTooSmall) score=4.4
  - 13.412  Reposition C0->C2 (CurrentInvalid) score=3.1
  - 13.747  Reposition C0->C3 (CurrentInvalid) score=4.5
  - 14.819  Reposition C0->C4 (CurrentInvalid) score=2.8
  - 15.084  Reposition C0->C3 (CurrentInvalid) score=4.4
- Combat: G6=Track, Ignore, Fire, Aim, SHOT=5, ImmediateThreat=9, SearchAttackOsc=52
- Last SNAP t=181.315 pos=(4.6, -0.6, 64.8) ai=Attack/Hold dest=none gate=Success engage=none

### Волна 0 - Enemy P11-P20

#### P11 - Enemy, Insurgent

- Spawn t=0.000 pos=(-18.0, 0.0, 139.0), weapon=Item_Weapon_Mosin, log until t=181.315
- Outcome: DEATH t=126.653 (-16.7, -0.6, 91.7); unconscious (MOVE fail)
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-1.2, 0.1, 74.3)
- Engage: P03, P08, P10, P05, P04
- Cover hop: C1, C2, C3, C4
- Reserved: C1, C2, C3, C4, C5
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 11:
  - 0.353  Reposition C0->C1 (CurrentInvalid) score=2.3
  - 3.264  Reposition C0->C3 (CurrentInvalid) score=3.5
  - 6.042  Reposition C0->C4 (CurrentInvalid) score=4.3
  - 8.820  Reposition C0->C1 (CurrentInvalid) score=4.0
  - 9.442  Reposition C0->C2 (CurrentInvalid) score=4.1
  - 10.577  Reposition C0->C5 (CurrentInvalid) score=4.0
  - 10.683  Reposition C0->C2 (CurrentInvalid) score=3.8
  - 11.059  Reposition C0->C5 (CurrentInvalid) score=4.0
- Combat: G6=Track, Ignore, Fire, Aim, SHOT=0, ImmediateThreat=1, SearchAttackOsc=158
- Last SNAP t=181.315 pos=(-16.7, -0.6, 91.7) ai=Attack/Hold dest=none gate=NoWeapon engage=none

#### P12 - Enemy, Insurgent

- Spawn t=0.000 pos=(-12.0, 0.0, 139.0), weapon=Item_Weapon_AK74UMOD1, log until t=181.315
- Outcome: DEATH t=180.315 (-7.0, -0.7, 91.0); unconscious (MOVE fail)
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-1.1, 0.1, 74.2)
- Engage: P01, P06, P10, P04, P08, P03, P05, P02
- Cover hop: C1, C2, C3, C4
- Reserved: C1, C2, C3, C4, C5
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 17:
  - 0.353  Reposition C0->C2 (CurrentInvalid) score=3.6
  - 5.470  Stay C4->C2 (Committed) score=4.2
  - 5.748  Reposition C0->C1 (CurrentInvalid) score=4.4
  - 8.616  Reposition C0->C5 (CurrentInvalid) score=2.7
  - 9.632  Reposition C0->C2 (CurrentInvalid) score=3.9
  - 9.768  Reposition C0->C5 (CurrentInvalid) score=2.7
  - 10.922  Reposition C0->C2 (CurrentInvalid) score=0.1
  - 18.080  Reposition C0->C1 (CurrentInvalid) score=0.8
- Combat: G6=Track, Ignore, Fire, Aim, SHOT=1, ImmediateThreat=4, SearchAttackOsc=184
- Last SNAP t=181.315 pos=(-7.0, -0.7, 91.0) ai=Attack/Hold dest=none gate=Success engage=none

#### P13 - Enemy, Insurgent

- Spawn t=0.000 pos=(-2.0, 0.0, 139.0), weapon=Item_Weapon_RPK47, log until t=181.315
- Outcome: DEATH t=170.936 (0.4, -0.6, 96.7); unconscious (MOVE fail)
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-1.4, 0.1, 75.0)
- Engage: P08
- Cover hop: C1, C2, C3, C4
- Reserved: C1, C2, C3, C4, C5
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 40:
  - 0.353  Reposition C0->C5 (CurrentInvalid) score=5.4
  - 5.470  Stay C0->C0 (NoCandidate) score=1.7
  - 8.616  Reposition C0->C1 (CurrentInvalid) score=4.9
  - 8.820  Stay C0->C0 (NoCandidate) score=1.7
  - 9.154  Reposition C0->C3 (CurrentInvalid) score=2.1
  - 11.059  Reposition C0->C1 (CurrentInvalid) score=3.1
  - 11.181  Reposition C0->C2 (CurrentInvalid) score=1.4
  - 11.607  Reposition C0->C3 (CurrentInvalid) score=2.6
- Combat: G6=Track, Ignore, Aim, SHOT=0, ImmediateThreat=6, SearchAttackOsc=619
- Last SNAP t=181.315 pos=(0.4, -0.6, 96.7) ai=Attack/Hold dest=none gate=NoWeapon engage=none

#### P14 - Enemy, Insurgent

- Spawn t=0.000 pos=(4.0, 0.0, 139.0), weapon=Item_Weapon_PKM, log until t=181.315
- Outcome: DEATH t=170.936 (2.3, -0.7, 102.6); unconscious (MOVE fail)
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-0.4, 0.1, 76.3)
- Engage: P08, P03, P10, P05
- Cover hop: C1, C2, C3
- Reserved: C1, C2, C3
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 19:
  - 0.353  Reposition C0->C1 (CurrentInvalid) score=5.3
  - 10.922  Stay C3->C1 (Committed) score=4.0
  - 11.181  Stay C3->C0 (NoCandidate) score=3.2
  - 11.607  Stay C3->C2 (ImprovementTooSmall) score=0.9
  - 11.948  Stay C3->C0 (NoCandidate) score=3.7
  - 12.480  Stay C3->C2 (ImprovementTooSmall) score=0.9
  - 39.611  Reposition C0->C3 (CurrentInvalid) score=4.6
  - 57.010  Reposition C0->C2 (CurrentInvalid) score=2.9
- Combat: G6=Track, Ignore, Aim, SHOT=0, ImmediateThreat=12, SearchAttackOsc=532
- Last SNAP t=181.315 pos=(2.3, -0.7, 102.6) ai=Attack/Hold dest=none gate=NoWeapon engage=none

#### P15 - Enemy, Insurgent

- Spawn t=0.000 pos=(16.0, 0.0, 139.0), weapon=Item_Weapon_SVD, log until t=181.315
- Outcome: DEATH t=127.805 (2.6, -0.6, 98.6); unconscious (MOVE fail)
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-1.2, 0.1, 74.4)
- Engage: P03, P04, P07, P05, P10, P08, P09, P02
- Cover hop: C1, C2, C3, C4
- Reserved: C1, C2, C3, C4
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 61:
  - 0.353  Stay C0->C0 (NoCandidate) score=1.7
  - 10.922  Reposition C0->C1 (CurrentInvalid) score=2.7
  - 11.468  Reposition C0->C4 (CurrentInvalid) score=1.7
  - 18.362  Reposition C0->C3 (CurrentInvalid) score=2.1
  - 18.483  Reposition C0->C4 (CurrentInvalid) score=0.7
  - 19.230  Reposition C0->C2 (CurrentInvalid) score=1.6
  - 19.338  Reposition C0->C4 (CurrentInvalid) score=0.5
  - 20.797  Reposition C0->C2 (CurrentInvalid) score=1.3
- Combat: G6=Track, Aim, Ignore, SHOT=0, ImmediateThreat=2, SearchAttackOsc=206
- Last SNAP t=181.315 pos=(2.6, -0.6, 98.6) ai=Attack/Hold dest=none gate=NoWeapon engage=none

#### P16 - Enemy, Insurgent

- Spawn t=0.000 pos=(-16.0, 0.0, 135.0), weapon=Item_Weapon_RPK74MOD1, log until t=181.315
- Outcome: DEATH t=92.951 (-7.4, -0.7, 88.8); unconscious (MOVE fail)
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-1.3, 0.1, 74.8)
- Engage: P10, P05, P08, P04, P03, P07
- Cover hop: C1, C2
- Reserved: C1, C2
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 10:
  - 0.353  Reposition C0->C2 (CurrentInvalid) score=4.5
  - 0.614  Reposition C0->C1 (CurrentInvalid) score=3.4
  - 5.470  Reposition C0->C2 (CurrentInvalid) score=4.5
  - 9.154  Stay C1->C1 (ImprovementTooSmall) score=3.6
  - 9.442  Reposition C0->C1 (CurrentInvalid) score=3.5
  - 18.080  Reposition C0->C2 (CurrentInvalid) score=0.5
  - 30.848  Stay C2->C2 (ImprovementTooSmall) score=4.4
  - 39.611  Reposition C0->C2 (CurrentInvalid) score=4.0
- Combat: G6=Track, Ignore, Aim, SHOT=0, ImmediateThreat=1, SearchAttackOsc=188
- Last SNAP t=181.315 pos=(-7.4, -0.7, 88.8) ai=Attack/Hold dest=none gate=NoWeapon engage=none

#### P17 - Enemy, Insurgent

- Spawn t=0.000 pos=(-4.0, 0.0, 135.0), weapon=Item_Weapon_RPK74MOD1, log until t=181.315
- Outcome: DEATH t=157.210 (2.4, -0.7, 100.8); unconscious (MOVE fail)
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (0.8, 0.1, 76.1)
- Engage: P08, P04, P10, P03, P05
- Cover hop: C1, C2, C3, C5
- Reserved: C1, C2, C3, C4, C5
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 63:
  - 0.353  Reposition C0->C5 (CurrentInvalid) score=3.8
  - 0.614  Reposition C0->C4 (CurrentInvalid) score=1.2
  - 5.470  Reposition C0->C5 (CurrentInvalid) score=3.8
  - 7.063  Stay C0->C0 (NoCandidate) score=1.7
  - 9.442  Reposition C0->C2 (CurrentInvalid) score=1.9
  - 10.922  Reposition C0->C1 (CurrentInvalid) score=3.2
  - 11.059  Reposition C0->C3 (CurrentInvalid) score=2.3
  - 11.468  Reposition C0->C1 (CurrentInvalid) score=3.0
- Combat: G6=Ignore, Aim, Track, SHOT=0, ImmediateThreat=1, SearchAttackOsc=430
- Last SNAP t=181.315 pos=(2.4, -0.7, 100.8) ai=Attack/Hold dest=none gate=NoWeapon engage=none

#### P18 - Enemy, Insurgent

- Spawn t=0.000 pos=(2.0, 0.0, 135.0), weapon=Item_Weapon_RPK74MOD1, log until t=181.315
- Outcome: DEATH t=159.210 (2.7, -0.7, 96.3); unconscious (MOVE fail)
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (1.0, 0.1, 74.1)
- Engage: P04, P08, P05, P03, P09, P07, P02, P10
- Cover hop: C1, C2, C3, C4
- Reserved: C1, C2, C3, C4
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 52:
  - 0.353  Reposition C0->C1 (CurrentInvalid) score=4.0
  - 0.614  Reposition C0->C2 (CurrentInvalid) score=2.3
  - 9.442  Reposition C0->C4 (CurrentInvalid) score=3.9
  - 9.928  Reposition C0->C3 (CurrentInvalid) score=4.2
  - 20.703  Reposition C0->C1 (CurrentInvalid) score=7.0
  - 23.066  Stay C1->C1 (ImprovementTooSmall) score=6.2
  - 23.434  Stay C1->C0 (NoCandidate) score=6.3
  - 24.095  Stay C1->C2 (ImprovementTooSmall) score=6.0
- Combat: G6=Track, Ignore, Aim, Fire, SHOT=1, ImmediateThreat=1, SearchAttackOsc=267
- Last SNAP t=181.315 pos=(2.7, -0.7, 96.3) ai=Attack/Hold dest=none gate=Success engage=none

#### P19 - Enemy, Insurgent

- Spawn t=0.000 pos=(12.0, 0.0, 135.0), weapon=Item_Weapon_AK47_1, log until t=181.315
- Outcome: DEATH t=108.867 (1.8, -0.6, 95.5)
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-1.3, 0.1, 74.9)
- Engage: P04, P07, P03, P10
- Cover hop: C1, C2, C3, C4
- Reserved: C1, C2, C3, C4
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 39:
  - 0.353  Reposition C0->C1 (CurrentInvalid) score=2.9
  - 0.614  Reposition C0->C3 (CurrentInvalid) score=1.2
  - 8.820  Stay C0->C0 (NoCandidate) score=1.7
  - 11.607  Reposition C0->C2 (CurrentInvalid) score=0.5
  - 11.822  Reposition C0->C3 (CurrentInvalid) score=1.4
  - 12.480  Reposition C0->C1 (CurrentInvalid) score=-1.1
  - 13.412  Stay C0->C0 (NoCandidate) score=1.7
  - 14.070  Reposition C0->C1 (CurrentInvalid) score=-1.1
- Combat: G6=Track, Ignore, Aim, SHOT=0, ImmediateThreat=3, SearchAttackOsc=3
- Last SNAP t=181.315 pos=(1.8, -0.6, 95.5) ai=Attack/Engage dest=none gate=NoWeapon engage=P10

#### P20 - Enemy, Insurgent

- Spawn t=0.000 pos=(18.0, 0.0, 135.0), weapon=Item_Weapon_AK47S, log until t=181.315
- Outcome: DEATH t=143.623 (3.0, -0.7, 102.3); unconscious (MOVE fail)
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-1.3, 0.1, 75.1)
- Engage: P08, P10, P04, P03
- Cover hop: C1, C2
- Reserved: C1, C2
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 52:
  - 0.353  Stay C0->C0 (NoCandidate) score=1.7
  - 9.442  Reposition C0->C2 (CurrentInvalid) score=3.8
  - 10.198  Stay C1->C2 (Committed) score=4.0
  - 10.345  Reposition C0->C2 (CurrentInvalid) score=2.4
  - 20.703  Reposition C0->C1 (CurrentInvalid) score=6.9
  - 20.797  Reposition C0->C2 (CurrentInvalid) score=5.9
  - 23.066  Reposition C0->C1 (CurrentInvalid) score=7.4
  - 23.191  Reposition C0->C2 (CurrentInvalid) score=5.9
- Combat: G6=Track, Ignore, Aim, SHOT=0, ImmediateThreat=6, SearchAttackOsc=375
- Last SNAP t=181.315 pos=(3.0, -0.7, 102.3) ai=Attack/Hold dest=none gate=NoWeapon engage=none

### Волна 0 - Neutral P21-P40

All 20 civilians: AI=Idle, RoE=SelfDefense, no weapon, no Attack/cover. SNAP: pose=NotReady, dest=none, gate=NoWeapon. Hear footsteps, VISION does not commit. No SHOT, no DEATH.

| Slot | Spawn | Last SNAP |
|------|-------|-----------|
| P21 | (-16.0, 0.0, 42.0) | t=181.315 (-16.0, 0.2, 42.0) Idle/None |
| P22 | (0.0, 0.0, 66.0) | t=181.315 (0.0, 0.1, 66.0) Idle/None |
| P23 | (16.0, 0.0, 42.0) | t=181.315 (16.0, 0.1, 42.0) Idle/None |
| P24 | (-18.0, 0.0, 58.0) | t=181.315 (-18.0, 0.1, 58.0) Idle/None |
| P25 | (18.0, 0.0, 58.0) | t=181.315 (18.0, 0.1, 58.0) Idle/None |
| P26 | (-16.0, 0.0, 72.0) | t=181.315 (-16.0, 0.1, 72.0) Idle/None |
| P27 | (16.0, 0.0, 72.0) | t=181.315 (16.0, 0.1, 72.0) Idle/None |
| P28 | (0.0, 0.0, 80.0) | t=181.315 (0.0, 0.1, 80.0) Idle/None |
| P29 | (-18.0, 0.0, 90.0) | t=181.315 (-18.0, 0.1, 90.0) Idle/None |
| P30 | (18.0, 0.0, 90.0) | t=181.315 (18.0, 0.1, 90.0) Idle/None |
| P31 | (4.0, 0.0, 92.0) | t=181.315 (4.1, 0.1, 92.0) Idle/None |
| P32 | (-16.0, 0.0, 46.0) | t=181.315 (-16.0, 0.1, 46.0) Idle/None |
| P33 | (-16.0, 0.0, 104.0) | t=181.315 (-15.9, 0.1, 103.9) Idle/None |
| P34 | (8.0, 0.0, 104.0) | t=181.315 (8.0, 0.1, 104.0) Idle/None |
| P35 | (-16.0, 0.0, 122.0) | t=181.315 (-16.2, 0.1, 121.9) Idle/None |
| P36 | (16.0, 0.0, 122.0) | t=181.315 (16.1, 0.1, 121.6) Idle/None |
| P37 | (-20.0, 0.0, 50.0) | t=181.315 (-20.0, 0.1, 50.0) Idle/None |
| P38 | (20.0, 0.0, 50.0) | t=181.315 (20.0, 0.1, 50.0) Idle/None |
| P39 | (-20.0, 0.0, 100.0) | t=181.315 (-20.0, 0.1, 100.0) Idle/None |
| P40 | (20.0, 0.0, 108.0) | t=181.315 (20.0, 0.1, 108.0) Idle/None |

### Волна 1 - Enemy P41-P50 (t=121.5)

#### P41 - Enemy, Insurgent

- Spawn t=121.518 pos=(-17.3, 0.0, 139.2), weapon=Item_Weapon_AK74, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (0.7, 0.1, 73.8)
- Cover hop: C1, C3, C4
- Reserved: C1, C3, C4
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 8:
  - 122.176  Reposition C0->C3 (CurrentInvalid) score=2.4
  - 124.994  Reposition C0->C1 (CurrentInvalid) score=3.5
  - 125.412  Reposition C0->C4 (CurrentInvalid) score=4.3
  - 135.975  Reposition C0->C1 (CurrentInvalid) score=1.5
  - 141.944  Reposition C0->C3 (CurrentInvalid) score=2.1
  - 145.353  Reposition C0->C4 (CurrentInvalid) score=4.0
  - 156.876  Reposition C0->C1 (CurrentInvalid) score=1.8
  - 176.774  Reposition C0->C4 (CurrentInvalid) score=2.1
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=4
- Last SNAP t=181.315 pos=(-11.5, 0.1, 103.4) ai=Search/None dest=none gate=NoWeapon engage=none

#### P42 - Enemy, Insurgent

- Spawn t=121.518 pos=(-10.9, 0.0, 140.0), weapon=Item_Weapon_Mosin, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (0.4, 0.1, 73.7)
- Engage: P02
- Cover hop: C1, C2, C3, C4, C5
- Reserved: C1, C2, C3, C4, C5
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 13:
  - 122.176  Reposition C0->C5 (CurrentInvalid) score=3.5
  - 125.281  Stay C4->C5 (Committed) score=4.2
  - 125.647  Reposition C0->C1 (CurrentInvalid) score=4.4
  - 136.204  Reposition C0->C2 (CurrentInvalid) score=0.6
  - 136.956  Reposition C0->C3 (CurrentInvalid) score=0.6
  - 137.791  Reposition C0->C2 (CurrentInvalid) score=-0.7
  - 140.785  Stay C1->C2 (Committed) score=-0.7
  - 141.028  Reposition C0->C5 (CurrentInvalid) score=2.7
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=4
- Last SNAP t=181.315 pos=(-10.0, 0.1, 100.9) ai=Search/None dest=none gate=NoWeapon engage=none

#### P43 - Enemy, Insurgent

- Spawn t=121.518 pos=(-2.0, 0.0, 137.7), weapon=Item_Weapon_RPK74, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (0.5, 0.1, 73.7)
- Engage: P10, P07, P02
- Cover hop: C2, C3, C4
- Reserved: C1, C2, C3, C4, C5
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 12:
  - 122.176  Reposition C0->C5 (CurrentInvalid) score=5.2
  - 122.350  Reposition C0->C2 (CurrentInvalid) score=1.8
  - 124.035  Reposition C0->C4 (CurrentInvalid) score=1.8
  - 124.223  Reposition C0->C2 (CurrentInvalid) score=1.5
  - 124.443  Stay C0->C0 (NoCandidate) score=1.7
  - 135.975  Reposition C0->C3 (CurrentInvalid) score=3.9
  - 144.522  Stay C0->C0 (NoCandidate) score=1.7
  - 162.543  Reposition C0->C1 (CurrentInvalid) score=1.7
- Combat: G6=Track, Ignore, Aim, Fire, SHOT=14, ImmediateThreat=0, SearchAttackOsc=4
- Last SNAP t=181.315 pos=(3.5, 0.1, 75.3) ai=Search/None dest=none gate=Success engage=none

#### P44 - Enemy, Insurgent

- Spawn t=121.518 pos=(4.6, 0.0, 138.0), weapon=Item_Weapon_PKM, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-1.2, 0.1, 75.6)
- Cover hop: C1, C3
- Reserved: C1, C2, C3, C4
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 11:
  - 122.176  Reposition C0->C1 (CurrentInvalid) score=4.4
  - 135.975  Reposition C0->C3 (CurrentInvalid) score=3.8
  - 136.204  Reposition C0->C1 (CurrentInvalid) score=3.1
  - 136.956  Reposition C0->C2 (CurrentInvalid) score=2.1
  - 137.791  Reposition C0->C4 (CurrentInvalid) score=2.2
  - 138.700  Stay C0->C0 (NoCandidate) score=1.7
  - 141.028  Reposition C0->C1 (CurrentInvalid) score=2.7
  - 156.876  Reposition C0->C3 (CurrentInvalid) score=4.2
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=4
- Last SNAP t=181.315 pos=(4.6, 0.1, 108.3) ai=Search/None dest=none gate=NoWeapon engage=none

#### P45 - Enemy, Insurgent

- Spawn t=121.518 pos=(16.4, 0.0, 138.4), weapon=Item_Weapon_RPK47, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (1.2, 0.1, 74.5)
- Engage: P07
- Cover hop: C2
- Reserved: C1, C2, C3
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 11:
  - 122.176  Stay C0->C0 (NoCandidate) score=1.7
  - 135.975  Reposition C0->C3 (CurrentInvalid) score=2.7
  - 136.204  Reposition C0->C2 (CurrentInvalid) score=2.3
  - 136.956  Stay C0->C0 (NoCandidate) score=1.7
  - 137.578  Reposition C0->C2 (CurrentInvalid) score=2.2
  - 143.786  Stay C0->C0 (NoCandidate) score=1.7
  - 162.543  Reposition C0->C1 (CurrentInvalid) score=1.7
  - 162.543  Reposition C0->C2 (CurrentInvalid) score=0.4
- Combat: G6=Track, Ignore, Aim, Fire, SHOT=1, ImmediateThreat=0, SearchAttackOsc=3
- Last SNAP t=181.315 pos=(2.3, 0.1, 93.1) ai=Search/None dest=none gate=Success engage=none

#### P46 - Enemy, Insurgent

- Spawn t=121.518 pos=(-16.8, 0.0, 133.7), weapon=Item_Weapon_AK47MOD1, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-0.2, 0.1, 73.7)
- Cover hop: C1, C2, C3, C4
- Reserved: C1, C2, C3, C4, C5
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 17:
  - 122.176  Reposition C0->C4 (CurrentInvalid) score=2.3
  - 124.035  Reposition C0->C2 (CurrentInvalid) score=4.1
  - 124.223  Reposition C0->C1 (CurrentInvalid) score=3.1
  - 124.826  Reposition C0->C2 (CurrentInvalid) score=4.5
  - 135.975  Reposition C0->C1 (CurrentInvalid) score=1.1
  - 136.204  Reposition C0->C3 (CurrentInvalid) score=-0.7
  - 136.956  Reposition C0->C2 (CurrentInvalid) score=-0.7
  - 137.578  Reposition C0->C3 (CurrentInvalid) score=0.5
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=4
- Last SNAP t=181.315 pos=(-13.1, 0.1, 105.2) ai=Search/None dest=none gate=NoWeapon engage=none

#### P47 - Enemy, Insurgent

- Spawn t=121.518 pos=(-4.9, 0.0, 133.5), weapon=Item_Weapon_AK47_1, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-1.3, 0.1, 74.8)
- Cover hop: C1, C3, C4, C5
- Reserved: C1, C2, C3, C4, C5
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 12:
  - 122.176  Reposition C0->C5 (CurrentInvalid) score=3.5
  - 122.350  Reposition C0->C4 (CurrentInvalid) score=1.3
  - 123.081  Reposition C0->C2 (CurrentInvalid) score=2.4
  - 123.403  Reposition C0->C4 (CurrentInvalid) score=1.3
  - 125.281  Reposition C0->C5 (CurrentInvalid) score=3.6
  - 135.975  Reposition C0->C3 (CurrentInvalid) score=4.3
  - 136.204  Stay C0->C0 (NoCandidate) score=1.7
  - 136.743  Reposition C0->C2 (CurrentInvalid) score=3.1
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=4
- Last SNAP t=181.315 pos=(6.0, 0.1, 110.7) ai=Search/None dest=none gate=NoWeapon engage=none

#### P48 - Enemy, Insurgent

- Spawn t=121.518 pos=(2.0, 0.0, 133.9), weapon=Item_Weapon_AK47_1, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-1.3, 0.1, 75.1)
- Cover hop: C2, C3, C4
- Reserved: C1, C2, C3, C4
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 13:
  - 122.176  Reposition C0->C1 (CurrentInvalid) score=3.8
  - 122.350  Reposition C0->C2 (CurrentInvalid) score=2.0
  - 135.975  Reposition C0->C1 (CurrentInvalid) score=3.3
  - 136.204  Stay C0->C0 (NoCandidate) score=1.7
  - 136.743  Reposition C0->C1 (CurrentInvalid) score=3.3
  - 142.760  Reposition C0->C2 (CurrentInvalid) score=-0.8
  - 156.876  Reposition C0->C3 (CurrentInvalid) score=4.0
  - 157.210  Reposition C0->C1 (CurrentInvalid) score=2.8
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=4
- Last SNAP t=181.315 pos=(3.6, 0.1, 107.5) ai=Search/None dest=none gate=NoWeapon engage=none

#### P49 - Enemy, Insurgent

- Spawn t=121.518 pos=(11.7, 0.0, 136.0), weapon=Item_Weapon_SVD, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-1.3, 0.1, 75.5)
- Cover hop: C1, C2, C3
- Reserved: C1, C2, C3, C4
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 15:
  - 122.176  Reposition C0->C1 (CurrentInvalid) score=3.1
  - 122.350  Reposition C0->C3 (CurrentInvalid) score=1.1
  - 123.081  Reposition C0->C2 (CurrentInvalid) score=1.3
  - 123.403  Reposition C0->C3 (CurrentInvalid) score=1.1
  - 136.204  Stay C0->C0 (NoCandidate) score=1.7
  - 136.743  Reposition C0->C2 (CurrentInvalid) score=1.1
  - 136.956  Stay C0->C0 (NoCandidate) score=1.7
  - 138.367  Reposition C0->C4 (CurrentInvalid) score=1.6
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=4
- Last SNAP t=181.315 pos=(2.5, 0.1, 98.4) ai=Search/None dest=none gate=NoWeapon engage=none

#### P50 - Enemy, Insurgent

- Spawn t=121.518 pos=(17.6, 0.0, 133.7), weapon=Item_Weapon_AK74, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-1.1, 0.1, 75.8)
- Engage: P04, P09
- Cover hop: C4
- Reserved: C1, C2, C3, C4
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 14:
  - 122.176  Stay C0->C0 (NoCandidate) score=1.7
  - 135.975  Reposition C0->C3 (CurrentInvalid) score=2.5
  - 136.204  Reposition C0->C4 (CurrentInvalid) score=1.3
  - 136.743  Reposition C0->C2 (CurrentInvalid) score=2.1
  - 136.956  Reposition C0->C4 (CurrentInvalid) score=1.3
  - 137.791  Stay C0->C0 (NoCandidate) score=1.7
  - 138.367  Reposition C0->C4 (CurrentInvalid) score=1.4
  - 143.786  Reposition C0->C2 (CurrentInvalid) score=1.3
- Combat: G6=Track, Ignore, Aim, Fire, SHOT=9, ImmediateThreat=0, SearchAttackOsc=5
- Last SNAP t=181.315 pos=(17.0, 0.1, 88.1) ai=Search/None dest=none gate=Success engage=none

### Волна 2 - Player P51-P60 (t=150.7)

#### P51 - Player, Soldier

- Spawn t=150.679 pos=(-16.7, 0.0, 12.4), weapon=Item_Weapon_M4_ModA_2, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (1.3, 0.1, 75.2)
- Cover hop: C1, C2
- Reserved: C1, C2
- Acquire: none (Occupied not confirmed)
- PositionDecision first 2 of 2:
  - 151.345  Reposition C0->C2 (CurrentInvalid) score=3.6
  - 155.945  Stay C0->C0 (NoCandidate) score=1.7
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=2
- Last SNAP t=181.315 pos=(-9.4, 0.1, 31.0) ai=Search/None dest=(17.2, 0.1, 89.9) gate=NoWeapon engage=none

#### P52 - Player, Soldier

- Spawn t=150.679 pos=(-12.0, 0.0, 10.0), weapon=Item_Weapon_M249, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-0.4, 0.1, 73.7)
- Cover hop: C2, C3
- Reserved: C1, C2, C3
- Acquire:
  - 157.210  C0 Rejected OutOfTolerance dist=1.07 tol=0.60
- PositionDecision first 6 of 6:
  - 151.345  Reposition C0->C3 (CurrentInvalid) score=3.4
  - 152.973  Reposition C0->C1 (CurrentInvalid) score=3.6
  - 155.611  Stay C4->C1 (Committed) score=4.4
  - 155.945  Reposition C0->C1 (CurrentInvalid) score=4.4
  - 156.210  Reposition C0->C3 (CurrentInvalid) score=4.7
  - 157.210  Stay C0->C0 (NoCandidate) score=1.7
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=2
- Last SNAP t=181.315 pos=(-15.0, 0.1, 25.1) ai=Search/None dest=(17.2, 0.1, 89.9) gate=NoWeapon engage=none

#### P53 - Player, Soldier

- Spawn t=150.679 pos=(-2.0, 0.0, 10.5), weapon=Item_Weapon_BenelliM4, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (1.3, 0.1, 75.3)
- Cover hop: C2, C3, C4, C5
- Reserved: C2, C3, C4, C5
- Acquire: none (Occupied not confirmed)
- PositionDecision first 6 of 6:
  - 151.345  Reposition C0->C5 (CurrentInvalid) score=5.5
  - 154.973  Stay C0->C0 (NoCandidate) score=1.7
  - 159.210  Reposition C0->C3 (CurrentInvalid) score=2.4
  - 159.543  Reposition C0->C2 (CurrentInvalid) score=1.7
  - 160.543  Reposition C0->C4 (CurrentInvalid) score=2.8
  - 161.543  Reposition C0->C3 (CurrentInvalid) score=2.6
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=2
- Last SNAP t=181.315 pos=(7.1, 0.1, 22.7) ai=Search/None dest=(17.2, 0.1, 89.9) gate=NoWeapon engage=none

#### P54 - Player, Soldier

- Spawn t=150.679 pos=(4.0, 0.0, 10.7), weapon=Item_Weapon_M16A_ModA_1, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (1.2, 0.1, 74.4)
- Cover hop: C1, C2, C3
- Reserved: C1, C2, C3, C4
- Acquire: none (Occupied not confirmed)
- PositionDecision first 5 of 5:
  - 151.345  Reposition C0->C1 (CurrentInvalid) score=5.3
  - 157.876  Reposition C0->C4 (CurrentInvalid) score=2.3
  - 160.210  Reposition C0->C2 (CurrentInvalid) score=2.3
  - 160.543  Reposition C0->C3 (CurrentInvalid) score=2.6
  - 161.543  Reposition C0->C2 (CurrentInvalid) score=2.9
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=2
- Last SNAP t=181.315 pos=(8.2, 0.1, 28.9) ai=Search/None dest=(17.2, 0.1, 89.9) gate=NoWeapon engage=none

#### P55 - Player, Soldier

- Spawn t=150.679 pos=(15.0, 0.0, 11.1), weapon=Item_Weapon_MK12, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-0.6, 0.1, 76.2)
- Cover hop: C1, C2, C4
- Reserved: C1, C2, C3, C4
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 8:
  - 151.345  Stay C0->C0 (NoCandidate) score=1.7
  - 158.543  Reposition C0->C3 (CurrentInvalid) score=1.2
  - 159.210  Reposition C0->C4 (CurrentInvalid) score=1.8
  - 159.876  Reposition C0->C3 (CurrentInvalid) score=1.5
  - 160.876  Reposition C0->C2 (CurrentInvalid) score=1.0
  - 161.210  Reposition C0->C3 (CurrentInvalid) score=1.9
  - 168.153  Reposition C0->C1 (CurrentInvalid) score=2.2
  - 168.487  Reposition C0->C3 (CurrentInvalid) score=2.1
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=2
- Last SNAP t=181.315 pos=(18.2, 0.1, 22.1) ai=Search/None dest=(17.2, 0.1, 89.9) gate=NoWeapon engage=none

#### P56 - Player, Soldier

- Spawn t=150.679 pos=(-16.3, 0.0, 15.4), weapon=Item_Weapon_M4_ModA_1, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (0.5, 0.1, 73.8)
- Cover hop: C1, C2, C3
- Reserved: C1, C2, C3
- Acquire: none (Occupied not confirmed)
- PositionDecision first 3 of 3:
  - 151.345  Reposition C0->C2 (CurrentInvalid) score=3.0
  - 151.679  Reposition C0->C3 (CurrentInvalid) score=0.8
  - 154.973  Stay C0->C0 (NoCandidate) score=1.7
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=2
- Last SNAP t=181.315 pos=(-11.0, 0.1, 29.0) ai=Search/None dest=(17.2, 0.1, 89.9) gate=NoWeapon engage=none

#### P57 - Player, Soldier

- Spawn t=150.679 pos=(-5.4, 0.0, 14.0), weapon=Item_Weapon_M4_ModA_2, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (0.9, 0.1, 74.0)
- Cover hop: C1, C4, C5
- Reserved: C1, C3, C4, C5
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 8:
  - 151.345  Reposition C0->C5 (CurrentInvalid) score=3.9
  - 151.679  Reposition C0->C4 (CurrentInvalid) score=1.5
  - 152.973  Reposition C0->C3 (CurrentInvalid) score=2.5
  - 154.973  Reposition C0->C5 (CurrentInvalid) score=3.9
  - 159.210  Stay C0->C0 (NoCandidate) score=1.7
  - 159.543  Reposition C0->C1 (CurrentInvalid) score=5.1
  - 161.210  Reposition C0->C3 (CurrentInvalid) score=2.4
  - 161.543  Reposition C0->C4 (CurrentInvalid) score=2.3
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=2
- Last SNAP t=181.315 pos=(7.2, 0.1, 17.0) ai=Search/None dest=(17.2, 0.1, 89.9) gate=NoWeapon engage=none

#### P58 - Player, Soldier

- Spawn t=150.679 pos=(1.3, 0.0, 14.2), weapon=Item_Weapon_Sniper762x51, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (1.1, 0.1, 74.2)
- Cover hop: C1, C4
- Reserved: C1, C2, C3, C4
- Acquire: none (Occupied not confirmed)
- PositionDecision first 7 of 7:
  - 151.345  Stay C0->C0 (NoCandidate) score=1.7
  - 157.876  Reposition C0->C1 (CurrentInvalid) score=5.0
  - 158.210  Reposition C0->C3 (CurrentInvalid) score=2.0
  - 160.210  Reposition C0->C4 (CurrentInvalid) score=2.8
  - 160.543  Reposition C0->C2 (CurrentInvalid) score=1.7
  - 161.210  Reposition C0->C3 (CurrentInvalid) score=2.3
  - 161.543  Reposition C0->C1 (CurrentInvalid) score=1.1
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=2
- Last SNAP t=181.315 pos=(8.1, 0.1, 21.4) ai=Search/None dest=(17.2, 0.1, 89.9) gate=NoWeapon engage=none

#### P59 - Player, Soldier

- Spawn t=150.679 pos=(12.1, 0.0, 14.1), weapon=Item_Weapon_M4_ModA_1, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (1.2, 0.1, 75.6)
- Cover hop: C1
- Reserved: C1, C3, C4
- Acquire: none (Occupied not confirmed)
- PositionDecision first 7 of 7:
  - 151.345  Stay C0->C0 (NoCandidate) score=1.7
  - 158.210  Reposition C0->C1 (CurrentInvalid) score=4.0
  - 159.210  Stay C0->C0 (NoCandidate) score=1.7
  - 159.876  Reposition C0->C1 (CurrentInvalid) score=0.5
  - 160.210  Reposition C0->C4 (CurrentInvalid) score=1.6
  - 160.543  Reposition C0->C3 (CurrentInvalid) score=1.3
  - 160.876  Reposition C0->C4 (CurrentInvalid) score=1.7
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=2
- Last SNAP t=181.315 pos=(19.8, 0.1, 18.2) ai=Search/None dest=(17.2, 0.1, 89.9) gate=NoWeapon engage=none

#### P60 - Player, Soldier

- Spawn t=150.679 pos=(17.6, 0.0, 16.3), weapon=Item_Weapon_M4_ModA_1, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-0.6, 0.1, 76.2)
- Cover hop: C1, C2, C4
- Reserved: C1, C2, C3, C4
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 8:
  - 151.345  Reposition C0->C1 (CurrentInvalid) score=1.4
  - 156.543  Reposition C0->C4 (CurrentInvalid) score=1.6
  - 157.210  Reposition C0->C3 (CurrentInvalid) score=1.8
  - 157.543  Reposition C0->C4 (CurrentInvalid) score=2.2
  - 158.543  Reposition C0->C3 (CurrentInvalid) score=1.6
  - 158.876  Reposition C0->C1 (CurrentInvalid) score=1.8
  - 159.876  Reposition C0->C2 (CurrentInvalid) score=1.2
  - 160.210  Reposition C0->C1 (CurrentInvalid) score=2.2
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=2
- Last SNAP t=181.315 pos=(18.2, 0.1, 23.1) ai=Search/None dest=(17.2, 0.1, 89.9) gate=NoWeapon engage=none

### Волна 2 - Enemy P61-P70 (t=150.7)

#### P61 - Enemy, Insurgent

- Spawn t=150.679 pos=(-18.8, 0.0, 140.1), weapon=Item_Weapon_Mosin, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (0.5, 0.1, 76.2)
- Cover hop: C1, C2, C4
- Reserved: C1, C2, C4
- Acquire: none (Occupied not confirmed)
- PositionDecision first 7 of 7:
  - 151.345  Reposition C0->C1 (CurrentInvalid) score=2.5
  - 158.210  Reposition C0->C2 (CurrentInvalid) score=3.1
  - 158.876  Stay C1->C2 (ImprovementTooSmall) score=3.3
  - 176.774  Reposition C0->C1 (CurrentInvalid) score=3.1
  - 177.108  Reposition C0->C2 (CurrentInvalid) score=0.1
  - 178.086  Reposition C0->C1 (CurrentInvalid) score=3.1
  - 178.982  Reposition C0->C2 (CurrentInvalid) score=0.1
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=3
- Last SNAP t=181.315 pos=(-14.8, 0.1, 125.3) ai=Search/None dest=none gate=NoWeapon engage=none

#### P62 - Enemy, Insurgent

- Spawn t=150.679 pos=(-12.1, 0.0, 139.4), weapon=Item_Weapon_AK74UMOD1, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (0.9, 0.1, 76.0)
- Cover hop: C2
- Reserved: C1, C2, C3
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 8:
  - 151.345  Reposition C0->C2 (CurrentInvalid) score=3.5
  - 158.543  Reposition C0->C1 (CurrentInvalid) score=3.7
  - 159.543  Reposition C0->C2 (CurrentInvalid) score=3.9
  - 176.774  Reposition C0->C1 (CurrentInvalid) score=1.5
  - 177.108  Reposition C0->C3 (CurrentInvalid) score=-0.6
  - 177.753  Reposition C0->C2 (CurrentInvalid) score=0.6
  - 178.689  Reposition C0->C1 (CurrentInvalid) score=1.5
  - 178.982  Reposition C0->C3 (CurrentInvalid) score=-0.6
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=3
- Last SNAP t=181.315 pos=(-14.6, 0.1, 120.2) ai=Search/None dest=none gate=NoWeapon engage=none

#### P63 - Enemy, Insurgent

- Spawn t=150.679 pos=(-1.6, 0.0, 139.5), weapon=Item_Weapon_AK74UMOD1, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (1.3, 0.1, 75.4)
- Cover hop: C3, C4
- Reserved: C1, C2, C3, C4, C5
- Acquire: none (Occupied not confirmed)
- PositionDecision first 5 of 5:
  - 151.345  Reposition C0->C5 (CurrentInvalid) score=5.5
  - 176.774  Reposition C0->C1 (CurrentInvalid) score=4.1
  - 177.108  Stay C0->C0 (NoCandidate) score=1.7
  - 178.086  Reposition C0->C3 (CurrentInvalid) score=2.9
  - 178.982  Reposition C0->C2 (CurrentInvalid) score=1.2
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=3
- Last SNAP t=181.315 pos=(4.2, 0.1, 129.6) ai=Search/None dest=none gate=NoWeapon engage=none

#### P64 - Enemy, Insurgent

- Spawn t=150.679 pos=(3.2, 0.0, 139.6), weapon=Item_Weapon_AK74, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-1.3, 0.1, 74.9)
- Cover hop: C3
- Reserved: C1, C3
- Acquire: none (Occupied not confirmed)
- PositionDecision first 1 of 1:
  - 151.345  Reposition C0->C1 (CurrentInvalid) score=5.5
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=3
- Last SNAP t=181.315 pos=(5.4, 0.1, 128.0) ai=Search/None dest=none gate=NoWeapon engage=none

#### P65 - Enemy, Insurgent

- Spawn t=150.679 pos=(17.2, 0.0, 139.7), weapon=Item_Weapon_SVD, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-0.9, 0.1, 76.0)
- Cover hop: C3
- Reserved: C3
- Acquire: none (Occupied not confirmed)
- PositionDecision first 2 of 2:
  - 151.345  Stay C0->C0 (NoCandidate) score=1.7
  - 176.774  Reposition C0->C3 (CurrentInvalid) score=2.7
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=3
- Last SNAP t=181.315 pos=(15.6, 0.1, 125.6) ai=Search/None dest=none gate=NoWeapon engage=none

#### P66 - Enemy, Insurgent

- Spawn t=150.679 pos=(-17.0, 0.0, 134.4), weapon=Item_Weapon_AK74, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-0.5, 0.1, 76.3)
- Cover hop: C1, C2, C4
- Reserved: C1, C2, C3, C4
- Acquire: none (Occupied not confirmed)
- PositionDecision first 5 of 5:
  - 151.345  Reposition C0->C4 (CurrentInvalid) score=2.2
  - 159.543  Reposition C0->C2 (CurrentInvalid) score=4.5
  - 159.876  Reposition C0->C1 (CurrentInvalid) score=3.1
  - 178.086  Reposition C0->C3 (CurrentInvalid) score=-0.7
  - 178.689  Reposition C0->C1 (CurrentInvalid) score=1.7
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=3
- Last SNAP t=181.315 pos=(-15.1, 0.1, 121.3) ai=Search/None dest=none gate=NoWeapon engage=none

#### P67 - Enemy, Insurgent

- Spawn t=150.679 pos=(-4.0, 0.0, 136.2), weapon=Item_Weapon_RPK74, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (1.3, 0.1, 75.4)
- Cover hop: C3, C4
- Reserved: C2, C3, C4, C5
- Acquire: none (Occupied not confirmed)
- PositionDecision first 8 of 8:
  - 151.345  Reposition C0->C5 (CurrentInvalid) score=4.3
  - 151.679  Reposition C0->C4 (CurrentInvalid) score=1.1
  - 158.543  Reposition C0->C2 (CurrentInvalid) score=2.1
  - 159.210  Reposition C0->C3 (CurrentInvalid) score=1.9
  - 159.876  Reposition C0->C4 (CurrentInvalid) score=1.0
  - 176.774  Reposition C0->C3 (CurrentInvalid) score=2.4
  - 178.086  Reposition C0->C2 (CurrentInvalid) score=1.4
  - 178.689  Reposition C0->C3 (CurrentInvalid) score=2.4
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=3
- Last SNAP t=181.315 pos=(4.0, 0.1, 131.1) ai=Search/None dest=none gate=NoWeapon engage=none

#### P68 - Enemy, Insurgent

- Spawn t=150.679 pos=(0.6, 0.0, 134.4), weapon=Item_Weapon_PKM, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-0.2, 0.1, 73.7)
- Cover hop: C2
- Reserved: C1, C2, C3
- Acquire: none (Occupied not confirmed)
- PositionDecision first 6 of 6:
  - 151.345  Reposition C0->C1 (CurrentInvalid) score=3.9
  - 151.679  Reposition C0->C2 (CurrentInvalid) score=2.1
  - 177.753  Reposition C0->C3 (CurrentInvalid) score=2.9
  - 178.086  Stay C0->C0 (NoCandidate) score=1.7
  - 178.689  Reposition C0->C2 (CurrentInvalid) score=0.9
  - 178.982  Stay C0->C0 (NoCandidate) score=1.7
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=3
- Last SNAP t=181.315 pos=(4.2, 0.1, 128.4) ai=Search/None dest=none gate=NoWeapon engage=none

#### P69 - Enemy, Insurgent

- Spawn t=150.679 pos=(12.9, 0.0, 134.0), weapon=Item_Weapon_AK47, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (-0.3, 0.1, 76.3)
- Cover hop: 
- Reserved: C1, C3
- Acquire: none (Occupied not confirmed)
- PositionDecision first 3 of 3:
  - 151.345  Reposition C0->C1 (CurrentInvalid) score=2.7
  - 151.679  Reposition C0->C3 (CurrentInvalid) score=1.3
  - 176.774  Stay C0->C0 (NoCandidate) score=1.7
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=3
- Last SNAP t=181.315 pos=(16.3, 0.1, 128.3) ai=Search/None dest=none gate=NoWeapon engage=none

#### P70 - Enemy, Insurgent

- Spawn t=150.679 pos=(16.7, 0.0, 134.9), weapon=Item_Weapon_AK47S, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack, then Search<->Attack oscillation (count in Combat line)
- Attack dest: (0.9, 0.1, 76.0)
- Cover hop: C4
- Reserved: C3, C4
- Acquire: none (Occupied not confirmed)
- PositionDecision first 3 of 3:
  - 151.345  Stay C0->C0 (NoCandidate) score=1.7
  - 176.774  Reposition C0->C3 (CurrentInvalid) score=2.7
  - 177.108  Reposition C0->C4 (CurrentInvalid) score=1.6
- Combat: G6=Track, Ignore, SHOT=0, ImmediateThreat=0, SearchAttackOsc=3
- Last SNAP t=181.315 pos=(15.4, 0.1, 124.1) ai=Search/None dest=none gate=NoWeapon engage=none

### Волна 3 - Player P71-P80 (t=181.0, только спавн)

Session ended right after spawn (~0.3 s live). Idle->Attack and first route toward center. No cover acquire / SHOT yet.

#### P71 - Player, Soldier

- Spawn t=180.982 pos=(-17.9, 0.0, 11.8), weapon=Item_Weapon_M249, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack
- Attack dest: (-0.3, 0.1, 73.7)
- Cover hop: C2
- Reserved: C2
- Acquire: none (Occupied not confirmed)
- Combat: G6=None, SHOT=0, ImmediateThreat=0, SearchAttackOsc=0

#### P72 - Player, Soldier

- Spawn t=180.982 pos=(-11.3, 0.0, 9.7), weapon=Item_Weapon_Sniper762x51, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack
- Attack dest: (1.1, 0.1, 75.8)
- Cover hop: 
- Reserved: 
- Acquire: none (Occupied not confirmed)
- Combat: G6=None, SHOT=0, ImmediateThreat=0, SearchAttackOsc=0

#### P73 - Player, Soldier

- Spawn t=180.982 pos=(-1.1, 0.0, 9.6), weapon=Item_Weapon_BenelliM4, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack
- Attack dest: (1.1, 0.1, 75.8)
- Cover hop: 
- Reserved: 
- Acquire: none (Occupied not confirmed)
- Combat: G6=None, SHOT=0, ImmediateThreat=0, SearchAttackOsc=0

#### P74 - Player, Soldier

- Spawn t=180.982 pos=(3.1, 0.0, 9.7), weapon=Item_Weapon_M4_ModA_1, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack
- Attack dest: (-0.1, 0.1, 76.3)
- Cover hop: C1
- Reserved: C1
- Acquire: none (Occupied not confirmed)
- Combat: G6=None, SHOT=0, ImmediateThreat=0, SearchAttackOsc=0

#### P75 - Player, Soldier

- Spawn t=180.982 pos=(16.4, 0.0, 12.5), weapon=Item_Weapon_M4_ModA_1, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack
- Attack dest: (1.0, 0.1, 75.9)
- Cover hop: 
- Reserved: 
- Acquire: none (Occupied not confirmed)
- Combat: G6=None, SHOT=0, ImmediateThreat=0, SearchAttackOsc=0

#### P76 - Player, Soldier

- Spawn t=180.982 pos=(-14.9, 0.0, 14.3), weapon=Item_Weapon_M4_ModA_1, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack
- Attack dest: (-1.0, 0.1, 76.0)
- Cover hop: C2
- Reserved: C2
- Acquire: none (Occupied not confirmed)
- Combat: G6=None, SHOT=0, ImmediateThreat=0, SearchAttackOsc=0

#### P77 - Player, Soldier

- Spawn t=180.982 pos=(-5.1, 0.0, 13.7), weapon=Item_Weapon_M4_ModA_1, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack
- Attack dest: (-1.3, 0.1, 74.7)
- Cover hop: 
- Reserved: 
- Acquire: none (Occupied not confirmed)
- Combat: G6=None, SHOT=0, ImmediateThreat=0, SearchAttackOsc=0

#### P78 - Player, Soldier

- Spawn t=180.982 pos=(1.2, 0.0, 14.1), weapon=Item_Weapon_MK12, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack
- Attack dest: (1.1, 0.1, 75.8)
- Cover hop: 
- Reserved: 
- Acquire: none (Occupied not confirmed)
- Combat: G6=None, SHOT=0, ImmediateThreat=0, SearchAttackOsc=0

#### P79 - Player, Soldier

- Spawn t=180.982 pos=(11.4, 0.0, 15.3), weapon=Item_Weapon_M4_ModA_2, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack
- Attack dest: (0.9, 0.1, 76.0)
- Cover hop: 
- Reserved: 
- Acquire: none (Occupied not confirmed)
- Combat: G6=None, SHOT=0, ImmediateThreat=0, SearchAttackOsc=0

#### P80 - Player, Soldier

- Spawn t=180.982 pos=(17.5, 0.0, 16.4), weapon=Item_Weapon_M4_ModA_1, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack
- Attack dest: (-1.3, 0.1, 74.9)
- Cover hop: C1
- Reserved: C1
- Acquire: none (Occupied not confirmed)
- Combat: G6=None, SHOT=0, ImmediateThreat=0, SearchAttackOsc=0

### Волна 3 - Enemy P81-P90 (t=181.0, только спавн)

Session ended right after spawn (~0.3 s live). Idle->Attack and first route toward center. No cover acquire / SHOT yet.

#### P81 - Enemy, Insurgent

- Spawn t=180.982 pos=(-19.3, 0.0, 140.1), weapon=Item_Weapon_AK74, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack
- Attack dest: (-0.6, 0.1, 73.8)
- Cover hop: C4
- Reserved: C4
- Acquire: none (Occupied not confirmed)
- Combat: G6=None, SHOT=0, ImmediateThreat=0, SearchAttackOsc=0

#### P82 - Enemy, Insurgent

- Spawn t=180.982 pos=(-13.5, 0.0, 138.4), weapon=Item_Weapon_AK47MOD1, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack
- Attack dest: (-0.9, 0.1, 74.0)
- Cover hop: C1
- Reserved: C1
- Acquire: none (Occupied not confirmed)
- Combat: G6=None, SHOT=0, ImmediateThreat=0, SearchAttackOsc=0

#### P83 - Enemy, Insurgent

- Spawn t=180.982 pos=(-3.2, 0.0, 140.3), weapon=Item_Weapon_Mosin, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack
- Attack dest: (-1.3, 0.1, 74.6)
- Cover hop: C4
- Reserved: C4
- Acquire: none (Occupied not confirmed)
- Combat: G6=None, SHOT=0, ImmediateThreat=0, SearchAttackOsc=0

#### P84 - Enemy, Insurgent

- Spawn t=180.982 pos=(3.9, 0.0, 138.8), weapon=Item_Weapon_RPK47, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack
- Attack dest: (-1.3, 0.1, 74.6)
- Cover hop: C3
- Reserved: C3
- Acquire: none (Occupied not confirmed)
- Combat: G6=None, SHOT=0, ImmediateThreat=0, SearchAttackOsc=0

#### P85 - Enemy, Insurgent

- Spawn t=180.982 pos=(14.9, 0.0, 139.5), weapon=Item_Weapon_AK74U, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack
- Attack dest: (0.1, 0.1, 76.3)
- Cover hop: 
- Reserved: 
- Acquire: none (Occupied not confirmed)
- Combat: G6=None, SHOT=0, ImmediateThreat=0, SearchAttackOsc=0

#### P86 - Enemy, Insurgent

- Spawn t=180.982 pos=(-15.0, 0.0, 134.6), weapon=Item_Weapon_RPK74MOD1, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack
- Attack dest: (1.1, 0.1, 75.8)
- Cover hop: 
- Reserved: 
- Acquire: none (Occupied not confirmed)
- Combat: G6=None, SHOT=0, ImmediateThreat=0, SearchAttackOsc=0

#### P87 - Enemy, Insurgent

- Spawn t=180.982 pos=(-2.5, 0.0, 136.1), weapon=Item_Weapon_SVD, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack
- Attack dest: (-0.3, 0.1, 73.7)
- Cover hop: 
- Reserved: 
- Acquire: none (Occupied not confirmed)
- Combat: G6=None, SHOT=0, ImmediateThreat=0, SearchAttackOsc=0

#### P88 - Enemy, Insurgent

- Spawn t=180.982 pos=(3.5, 0.0, 135.6), weapon=Item_Weapon_RPK74MOD1, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack
- Attack dest: (0.3, 0.1, 73.7)
- Cover hop: 
- Reserved: 
- Acquire: none (Occupied not confirmed)
- Combat: G6=None, SHOT=0, ImmediateThreat=0, SearchAttackOsc=0

#### P89 - Enemy, Insurgent

- Spawn t=180.982 pos=(10.7, 0.0, 134.0), weapon=Item_Weapon_PKM, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack
- Attack dest: (-1.1, 0.1, 75.8)
- Cover hop: 
- Reserved: 
- Acquire: none (Occupied not confirmed)
- Combat: G6=None, SHOT=0, ImmediateThreat=0, SearchAttackOsc=0

#### P90 - Enemy, Insurgent

- Spawn t=180.982 pos=(18.2, 0.0, 134.5), weapon=Item_Weapon_AK74U, log until t=181.315
- Outcome: alive at end of recording
- AI: Idle -> Attack
- Attack dest: (1.3, 0.1, 74.7)
- Cover hop: 
- Reserved: 
- Acquire: none (Occupied not confirmed)
- Combat: G6=None, SHOT=0, ImmediateThreat=0, SearchAttackOsc=0

### Что это значит для системы

```
Bake / bind / Tactical hop     работает
Reservation + COVER_HOP        работает
Acquire last meter             частично: 5 Player Acquired, 2 OutOfTolerance, Enemy — ни одного
Occupied в occupancy-логе      не видно (канал не пишет state=Occupied) — чинится COVER_STATE + ConfirmOccupied
Unconscious                    в логе не отдельный тег; слот мог оставаться Reserved — LIFE + release
Neutral без AI                 ок
Search↔Attack overlay          шум, не читать как смену миссии
```

Словарь тегов — 8.3–8.5. Старый разбор старта без cover (август 21): 8.9. Occupied+Walk+LIFE 16:36:40 — **§8.10**. Актуальный AI после Search/Attack hold (20:40:49) — **§8.11**.

## 8.9. Play 21.08.2026: `Infantry_20260821_195740`


Сессия 21 августа 2026, старт **19:57:40**, запись ~**51 с**. Папка: `_Docs/Logs/Runtime/Infantry_20260821_195740`. Сцена CQB-арены: 10 Player + 10 Enemy + 20 Neutral.

Ниже — **то, что логгер пишет при запуске сцены на каждого юнита** (`SPAWN` t=0.000, приказ арены t=0.020, первый `SNAP` t=0.500). Словарь тегов — 8.3–8.5. Предыдущий полный прогон арены: `Infantry_20260821_141448` (~88 с).

Индекс в момент SPAWN: `ai=none` у всех. Callsign у P/E — `Unit(Clone)` (арена не ставит позывной). Neutral — `Civilian-01_01` … `_20`.

### Общая цепочка старта (все P и E, одинаковая)

```
0.000  SCAN    tier=Detail immediate=1          ← только у P/E; Neutral без немедленного Detail
0.000  SPAWN   slot=… team=… look=… body=… weapon=… ai=none scanCandidates=… pos=… go=Unit(Clone)
0.000  DISC    phase=Idle tgt=none
0.020  AI      cause=action state=Idle action=None intent=Hold roe=SelfDefense engage=none dest=none
0.020  MOVE    issue dest=(≈0, 75) snapped=… tier=Walk reason=None ok=1 source=NavDriver
0.020  MOVE    issue dest=(≈0, 75) reason=Attack ok=1 source=Tactical
0.020  AI      cause=state state=Attack action=Hold intent=Hold roe=SelfDefense engage=none dest=(≈0, 75)
0.122  G6      raw=None final=None selected=none los=0 weaponOk=0 aimReady=0 roe=Denied/NoContact intent=Hold
0.122  AI      attached=1 state=Attack action=Hold intent=Hold roe=SelfDefense
0.500  SNAP    pose=Aiming g6=None selected=none engageable=0 reason=Attack gate=NoWeapon
               ai=Attack/Hold intent=Hold roe=SelfDefense engage=none contacts=0 vis=0 mem=0
```

Neutral: только `SPAWN` + `DISC Idle`. Нет `MOVE`, нет `AI attached`. Первый SNAP: `pose=NotReady dest=none reason=None ai=none contacts=0`.

`gate=NoWeapon` на старте — дефолт «выстрела ещё не пытались», не пустые руки. Оружие — в `SPAWN weapon=`.

### Player (t=0.000 SPAWN + t=0.020 Attack)

Все: `team=Player look=Player body=Soldier scanCandidates=opponents ai=none` → в 0.122 `attached=1`.

| Слот | Оружие | Спавн | Attack dest |
|------|--------|-------|-------------|
| P01 | M4_ModA_1 | (−18, 0, 11) | (−1.5, 0.1, 74.4) |
| P02 | MK18 | (−12, 0, 11) | (1.1, 0.1, 75.2) |
| P03 | M4_ModA_1 | (−2, 0, 11) | (−0.1, 0.1, 76.1) |
| P04 | M249 | (4, 0, 11) | (0.7, 0.1, 73.7) |
| P05 | M16A_ModA_1 | (16, 0, 11) | (−1.4, 0.1, 75.1) |
| P06 | MK12 | (−16, 0, 15) | (0.9, 0.1, 74.0) |
| P07 | M16A_ModA_1 | (−4, 0, 15) | (−0.1, 0.1, 73.4) |
| P08 | M4_ModA_1 | (2, 0, 15) | (0.6, 0.1, 73.6) |
| P09 | Sniper762x51 | (12, 0, 15) | (0.4, 0.1, 73.5) |
| P10 | BenelliM4 | (18, 0, 15) | (1.2, 0.1, 74.5) |

Сырой `SPAWN` (timeline):

```
0.000  SPAWN  P01  team=Player look=Player body=Soldier weapon=Item_Weapon_M4_ModA_1  ai=none scanCandidates=opponents pos=(-18.0, 0.0, 11.0)
0.000  SPAWN  P02  team=Player look=Player body=Soldier weapon=Item_Weapon_MK18        ai=none scanCandidates=opponents pos=(-12.0, 0.0, 11.0)
0.000  SPAWN  P03  team=Player look=Player body=Soldier weapon=Item_Weapon_M4_ModA_1  ai=none scanCandidates=opponents pos=(-2.0, 0.0, 11.0)
0.000  SPAWN  P04  team=Player look=Player body=Soldier weapon=Item_Weapon_M249       ai=none scanCandidates=opponents pos=(4.0, 0.0, 11.0)
0.000  SPAWN  P05  team=Player look=Player body=Soldier weapon=Item_Weapon_M16A_ModA_1 ai=none scanCandidates=opponents pos=(16.0, 0.0, 11.0)
0.000  SPAWN  P06  team=Player look=Player body=Soldier weapon=Item_Weapon_MK12       ai=none scanCandidates=opponents pos=(-16.0, 0.0, 15.0)
0.000  SPAWN  P07  team=Player look=Player body=Soldier weapon=Item_Weapon_M16A_ModA_1 ai=none scanCandidates=opponents pos=(-4.0, 0.0, 15.0)
0.000  SPAWN  P08  team=Player look=Player body=Soldier weapon=Item_Weapon_M4_ModA_1  ai=none scanCandidates=opponents pos=(2.0, 0.0, 15.0)
0.000  SPAWN  P09  team=Player look=Player body=Soldier weapon=Item_Weapon_Sniper762x51 ai=none scanCandidates=opponents pos=(12.0, 0.0, 15.0)
0.000  SPAWN  P10  team=Player look=Player body=Soldier weapon=Item_Weapon_BenelliM4  ai=none scanCandidates=opponents pos=(18.0, 0.0, 15.0)
```

Пример файла юнита (P01, первые строки):

```
0.000  SCAN    tier=Detail immediate=1
0.000  SPAWN   slot=P01 team=Player look=Player body=Soldier weapon=Item_Weapon_M4_ModA_1 ai=none scanCandidates=opponents pos=(-18.0, 0.0, 11.0)
0.000  DISC    phase=Idle tgt=none none
0.020  AI      cause=action state=Idle action=None intent=Hold roe=SelfDefense engage=none dest=none search=n/a hostileVis=0
0.020  MOVE    issue dest=(-1.5, 0.1, 74.4) snapped=(-1.5, 0.1, 74.4) tier=Walk reason=None ok=1 source=NavDriver
0.020  MOVE    issue dest=(-1.5, 0.1, 74.4) reason=Attack ok=1 source=Tactical
0.020  AI      cause=state state=Attack action=Hold intent=Hold roe=SelfDefense engage=none dest=(-1.5, 0.1, 74.4)
0.122  G6      raw=None final=None selected=none los=0 weaponOk=0 aimReady=0 roe=Denied/NoContact intent=Hold mismatch=0
0.122  AI      attached=1 state=Attack action=Hold intent=Hold roe=SelfDefense
0.500  SNAP    pos=(-18.0, 0.1, 11.1) vel=0.2 stance=Standing pose=Aiming g6=None selected=none engageable=0 dest=(-1.5, 0.1, 74.4) remaining=inf reason=Attack gate=NoWeapon ai=Attack/Hold intent=Hold roe=SelfDefense engage=none contacts=0 vis=0 mem=0
```

### Enemy (t=0.000 SPAWN + t=0.020 Attack)

Все: `team=Enemy look=Enemy body=Insurgent scanCandidates=opponents ai=none` → в 0.122 `attached=1`.

| Слот | Оружие | Спавн | Attack dest |
|------|--------|-------|-------------|
| E01 | AK74MOD1 | (−18, 0, 139) | (−1.5, 0.1, 74.8) |
| E02 | AK74U | (−12, 0, 139) | (−1.0, 0.1, 73.7) |
| E03 | AK74UMOD1 | (−2, 0, 139) | (1.1, 0.1, 74.3) |
| E04 | PKM | (4, 0, 139) | (0.2, 0.1, 76.1) |
| E05 | Mosin | (16, 0, 139) | (0.9, 0.1, 75.6) |
| E06 | SVD | (−16, 0, 135) | (−1.4, 0.1, 74.1) |
| E07 | RPK47MOD1 | (−4, 0, 135) | (0.8, 0.1, 73.8) |
| E08 | RPK74MOD1 | (2, 0, 135) | (1.2, 0.1, 74.4) |
| E09 | RPK74 | (12, 0, 135) | (1.1, 0.1, 75.1) |
| E10 | AK47 | (18, 0, 135) | (0.4, 0.1, 76.0) |

Сырой `SPAWN`:

```
0.000  SPAWN  E01  team=Enemy look=Enemy body=Insurgent weapon=Item_Weapon_AK74MOD1  ai=none scanCandidates=opponents pos=(-18.0, 0.0, 139.0)
0.000  SPAWN  E02  team=Enemy look=Enemy body=Insurgent weapon=Item_Weapon_AK74U     ai=none scanCandidates=opponents pos=(-12.0, 0.0, 139.0)
0.000  SPAWN  E03  team=Enemy look=Enemy body=Insurgent weapon=Item_Weapon_AK74UMOD1 ai=none scanCandidates=opponents pos=(-2.0, 0.0, 139.0)
0.000  SPAWN  E04  team=Enemy look=Enemy body=Insurgent weapon=Item_Weapon_PKM       ai=none scanCandidates=opponents pos=(4.0, 0.0, 139.0)
0.000  SPAWN  E05  team=Enemy look=Enemy body=Insurgent weapon=Item_Weapon_Mosin     ai=none scanCandidates=opponents pos=(16.0, 0.0, 139.0)
0.000  SPAWN  E06  team=Enemy look=Enemy body=Insurgent weapon=Item_Weapon_SVD       ai=none scanCandidates=opponents pos=(-16.0, 0.0, 135.0)
0.000  SPAWN  E07  team=Enemy look=Enemy body=Insurgent weapon=Item_Weapon_RPK47MOD1 ai=none scanCandidates=opponents pos=(-4.0, 0.0, 135.0)
0.000  SPAWN  E08  team=Enemy look=Enemy body=Insurgent weapon=Item_Weapon_RPK74MOD1 ai=none scanCandidates=opponents pos=(2.0, 0.0, 135.0)
0.000  SPAWN  E09  team=Enemy look=Enemy body=Insurgent weapon=Item_Weapon_RPK74     ai=none scanCandidates=opponents pos=(12.0, 0.0, 135.0)
0.000  SPAWN  E10  team=Enemy look=Enemy body=Insurgent weapon=Item_Weapon_AK47      ai=none scanCandidates=opponents pos=(18.0, 0.0, 135.0)
```

Пример E01. Первый SNAP иногда ещё `dest=none remaining=-` при уже выданном Attack — dest в SNAP появляется со второго кадра (~1.0 с):

```
0.000  SPAWN   slot=E01 team=Enemy look=Enemy body=Insurgent weapon=Item_Weapon_AK74MOD1 ai=none scanCandidates=opponents pos=(-18.0, 0.0, 139.0)
0.020  MOVE    issue dest=(-1.5, 0.1, 74.8) reason=Attack ok=1 source=Tactical
0.122  AI      attached=1 state=Attack
0.500  SNAP    pos=(-18.0, 0.1, 139.0) pose=Aiming dest=none remaining=- reason=Attack gate=NoWeapon ai=Attack/Hold roe=SelfDefense contacts=0
1.017  SNAP    pos=(-18.0, 0.1, 139.0) pose=Aiming dest=(-1.5, 0.1, 74.8) remaining=inf reason=Attack gate=NoWeapon ai=Attack/Hold contacts=0
```

### Neutral (t=0.000 SPAWN, дальше стоят)

Все: `team=Neutral look=Civilian body=Civilian weapon=none ai=none scanCandidates=none` (в логе: `Neutral never scans / never a vision candidate`). Нет `MOVE` / `AI attached` / `G6` на старте.

| Слот | Спавн | Слот | Спавн |
|------|-------|------|-------|
| N01 | (−16, 0, 42) | N11 | (4, 0, 92) |
| N02 | (0, 0, 66) | N12 | (−16, 0, 46) |
| N03 | (16, 0, 42) | N13 | (−16, 0, 104) |
| N04 | (−18, 0, 58) | N14 | (8, 0, 104) |
| N05 | (18, 0, 58) | N15 | (−16, 0, 122) |
| N06 | (−16, 0, 72) | N16 | (16, 0, 122) |
| N07 | (16, 0, 72) | N17 | (−20, 0, 50) |
| N08 | (0, 0, 80) | N18 | (20, 0, 50) |
| N09 | (−18, 0, 90) | N19 | (−20, 0, 100) |
| N10 | (18, 0, 90) | N20 | (20, 0, 108) |

Пример N01:

```
0.000  SPAWN   slot=N01 team=Neutral look=Civilian body=Civilian weapon=none ai=none scanCandidates=none pos=(-16.0, 0.0, 42.0)
0.000  DISC    phase=Idle tgt=none none
0.500  SNAP    pos=(-16.0, 0.2, 42.0) vel=0.0 stance=Standing pose=NotReady g6=None selected=none engageable=0 dest=none remaining=- reason=None gate=NoWeapon ai=none contacts=0 vis=0 mem=0
```

### Что было после старта (этот прогон, ~51 с)

Ход выдан всем P/E (`reason=Attack ok=1`). P01 к 50.6 с ещё у `(−16, 20)` (`remaining=inf` — путь есть, идёт медленно). P06 к ~47 с у центра `(2.4, 74)` и стоит. E01 к 50.6 с у `(−14.7, 130)`. Neutral не двигались.

Первый контакт ~**43.3 с**: E02/E08 видят P06 (`id=Unknown`). Потом commit `id=Hostile idWas=Unknown` (~2 с взгляда). В VISION гражданских нет.

Бой снова рвётся на G6. В сессии **0** `GATE Success`, **0** `SHOT`, **0** `DEATH`.

```
SELECT engageable=1  →  G6 raw=Fire final=Ignore  →  выстрела нет
```

| `roe=` / `intent=` | Смысл в этом прогоне |
|--------------------|----------------------|
| `Denied/NoContact` | старт и пока нет выбранной |
| `Denied/UnknownNotAllowed` | цель ещё Unknown |
| `Denied/SelfDefenseNoImmediateThreat` | уже Hostile + иногда `intent=Engage`, ImmediateThreat нет |
| `intent=Hold` | Attack без видимого Hostile |
| `G6 mismatch=1` | у P06 Engage=E05/E06, Combat.Selected=E04/E08 |

Диагноз по 8.6 тот же, что в `141448`: «выбрал, но не стреляет» = **вето RoE / CombatIntent**, не зрение. Для выстрела — политика сильнее SelfDefense (MissionCombat) или снять RoE. Q/Identify не крутить: commit Hostile есть.

---

## 8.10. Play 28.08.2026: `Infantry_20260828_163640`

**Когда:** 28.08.2026 **16:36:40**, запись **~114 с** (последний SNAP t=113.726, timeline до 113.938).
**Сырые логи:** `_Docs/Logs/Runtime/Infantry_20260828_163640/` (`_index.txt`, `_timeline.log`, `Player/Pxx_*.log`).
**Слоты:** все файлы в `Player/P01…P40`. Сторона — `SPAWN team=`. Enemy это **P11–P20**, Neutral **P21–P40**. `_index team=` здесь врёт (все Player); смотреть SPAWN.

Полная арена 10+10+20, не «1 P01 + 1 cover». Прогон **после** фикса Walk (`pathPending` больше не сбрасывает `SetDestination`). Предыдущий отчёт 14:49:50 снят.

### Счётчики сессии (timeline)

| Тег / факт | Число |
|------------|-------|
| `MOVE` (Walk выдан) | 205 |
| `COVER_STATE Occupied` | **15** юнитов (раньше 0) |
| `COVER_STATE Acquired` | 15 |
| `POSITION_ACQUIRE Acquired` | 15 (+ 4 `Traversed` на промежуточном hop) |
| `POSITION_ACQUIRE Rejected` / `OutOfTolerance` | **0** |
| `COVER_REF candidateRef=MISSING` | 9, все `phase=Reserve` @ 0.020 — **не** дыра Acquire |
| `COVER_DECISION` | 98 |
| `COVER_INVALID` | 24 |
| `COVER_HEARTBEAT` | 818 (Keep ≤1 с на подходе) |
| `MOVE_COVER` | 19 |
| `AI_TRANSITION` | 38 (P20 = 20 из них) |
| `SHOT` | 119 (P04 Benelli 9 pellets ≈ 99 строк; P17 RPK 18; P10 снайпер 2) |
| `LIFE` | 7: Unconscious×6, Dead×1 |

Walk **идёт**. Occupied **случился**. Диск 0.60 не поднимали.

### Occupied — кто и когда

| t | Слот | Сторона | Слот cover | dist |
|---|------|---------|------------|------|
| 3.909 | P02 | Player, M249 | C3 | 0.58 |
| 10.062 | P11 | Enemy, AK74 | C4 | 0.51 |
| 11.141 | P14 | Enemy, RPK74MOD1 | C3 | 0.37 |
| 14.490 | P09 | Player, MK18 | C4 | 0.57 |
| 15.523 | P07 | Player, M4_ModA_2 | C1 | 0.55 |
| 23.276 | P18 | Enemy, RPK74 | C1 | 0.27 |
| 24.733 | **P01** | Player, MK18 | **C2** | **0.38** |
| 25.094 | P19 | Enemy, RPK74 | C2 | 0.34 |
| 30.736 | P16 | Enemy, Mosin | C2 | 0.44 |
| 35.300 | P13 | Enemy, AK47 | C4 | 0.01 |
| 41.092 | P06 | Player, M4 | C1 | 0.24 |
| 58.310 | P05 | Player, M16A4 | C1 | 0.54 |
| 58.310 | P12 | Enemy, SVD | C1 | 0.43 |
| 79.273 | P10 | Player, снайпер | C1 | 0.54 |
| 89.576 | P08 | Player, MK12 | C4 | 0.42 |

Не заняли слот к концу записи: **P03** (ещё Approaching C5), **P04** (Approaching C2, 2.3 м), **P15 / P17 / P20** (KO по дороге).

Конец сессии Occupied + `vel=0`: P01, P02, P05, P06, P07, P08, P09, P10, P11, P12, P14, P18, P19.

### LIFE

| t | Слот | life | was | coverReleased |
|---|------|------|-----|---------------|
| 40.731 | P17 Enemy RPK74 | Unconscious | Alive | 1 (`reason=Unconscious`) |
| 44.360 | P31 Neutral | Unconscious | Alive | 1 (collateral, AI и так Idle) |
| 46.595 | P15 Enemy AK74U | Unconscious | Alive | 1 |
| 47.540 | P20 Enemy PKM | Unconscious | Alive | 1 |
| 60.375 | P13 Enemy | Unconscious | Alive | 1 (был Occupied C4) |
| 73.069 | P16 Enemy | Unconscious | Alive | 1 (был Occupied C2) |
| 113.396 | P16 | **Dead** | Unconscious | 1 `health=0` |

После `LIFE` у боевых нет новых `MOVE` / `COVER_HOP` / `COVER_DECISION` / `SHOT`. SNAP: `cover=none coverState=None ai=off vision=off`. Поля `g6=` / `selected=` на SNAP после KO могут остаться от последнего кадра боя — это не новые решения.

---

### P01 — эталонная цепочка C2 (Player, MK18)

Спавн `(-18.0, 0.0, 11.0)`. Attack dest ≈ `(1.3, 0.1, 75.4)`. Hop C2 `(-4.0, 0.1, 42.8)`.

```text
0.020  COVER_STATE  C2 Reserved
0.020  COVER_REF    C2 candidateRef=MISSING phase=Reserve     ← объекта ещё нет
0.020  COVER_STATE  C2 Approaching
0.020  MOVE         dest=(-4.0, 0.1, 42.8) reason=Attack ok=1 path=pending
0.353  COVER_DECISION Stay Committed current=C2 best=C2
0.606  SNAP         cover=C2 coverState=Approaching vel=1.5     ← идёт
24.617 POSITION_ACQUIRE Traversed dist=0.53 (промежуточный hop)
24.733 COVER_REF    C2 candidateRef=0x3D8C8908 phase=Reserve
24.733 COVER_REF    C2 candidateRef=0x3D8C8908 phase=Acquire    ← тот же объект
24.733 COVER_STATE  C2 Acquired dist=0.38
24.733 COVER_STATE  C2 Occupied
24.733 COVER_REF    C2 candidateRef=0x3D8C8908 phase=ConfirmOccupied
24.733 POSITION_ACQUIRE Acquired cover=C2 dist=0.38 remaining=0
113.726 SNAP        cover=C2 coverState=Occupied coverDistance=0.38
                    vel=0 dest=none ai=Attack Hold selected=none gate=NoWeapon
```

`MISSING` только на первом Reserve кадра 0.020. К acquire объект тот же (`0x3D8C8908`). `ConfirmOccupied` вызван. Юнит стоит в слоте до конца записи. Выстрелов нет: `obs=none vis=0 selected=none` — не видит противника из этой точки, не дыра Occupied.

### P02 — быстрый Occupied без MISSING (Player, M249)

Раньше (14:49:50) это был dest-only `OutOfTolerance` remaining=0 dist=0.85. Сейчас:

```text
0.353  COVER_REF C3 0xC21C0532 Reserve
0.353  COVER_INVALID C3 CandidateMissing   ← current ещё C0, не слот
0.606  COVER_DECISION Stay Committed C3
3.909  COVER_REF C3 0xC21C0532 Acquire = ConfirmOccupied
3.909  COVER_STATE Occupied
3.909  POSITION_ACQUIRE Acquired dist=0.58 remaining=0 cover=C3
113.726 SNAP cover=C3 Occupied vel=0
```

Тот же `candidateRef` на Reserve / Acquire / ConfirmOccupied. Диск 0.60 взят (0.58).

### P03 — идёт, слот не занял (Player, M4)

C5 `(-0.8, 0.1, 6.5)` — укрытие **сзади** спавна. Search↔Attack @ 42.7 / 44.4 / 50.1 / 63.3 (`LostCurrentTarget` / `HostileVisible` / `Expired`), каждый раз `COVER_STATE Released reason=CommandChanged` и снова Approaching тот же C5.

Конец: `cover=C5 Approaching coverDistance=15.12 vel=1.5` к `(-0.8, 0.1, 6.5)`, `remaining=inf path=PathComplete`. Walk жив, acquire не наступил — сессия кончилась по дороге назад.

### P04 — бой на подходе, Occupied не успел (Player, Benelli)

`AI_TRANSITION` 10 раз (LostCurrentTarget ↔ HostileVisible/Expired). SHOT с 33.466 по P13 (дробина 9 pellets → 99 строк). Конец: Approaching C2, dist=2.28, remaining=2.3, `gate=Success`, `vel=1.4`. До диска 0.60 не дошёл.

### P17 — огонь без Occupied, затем LIFE (Enemy, RPK74)

Approaching C5 с 0.353. С 36.499 **18× SHOT** в P05, все `HitOther` (стенка арены), dist 60→56 м. @ 39.948 ImmediateThreat: слот C5→C1→C5 за один кадр. @ 40.731 `COVER_STATE Released reason=Unconscious` + `LIFE Unconscious`. Дальше нет SHOT/MOVE. Occupied не было.

### P20 — ImmediateThreat качает Search↔Attack (Enemy, PKM)

20 из 38 `AI_TRANSITION` сессии. Паттерн ~0.15 с @ 40.1–42.8:

```text
Attack → Search  reason=LostCurrentTarget  immediateThreat=1
Search → Attack  reason=ImmediateThreat    immediateThreat=1
COVER_STATE Released reason=CommandChanged
```

Слот C2/C1/C3 не удерживается. @ 47.540 Unconscious. Occupied нет. Это **не** LostVisible dwell 1.5 с: `ImmediateThreat` поднимает Attack из Search сам.

### P16 — Occupied, потом Unconscious → Dead (Enemy, Mosin)

Как P01: Reserve MISSING @ 0.020, затем @ 30.736 тот же `0x115FE984` Acquire → Occupied C2 dist=0.44. @ 73.069 Unconscious (`coverReleased=1`). @ 113.396 `LIFE Dead was=Unconscious health=0`. После KO тактики нет.

### P21 — Neutral контроль

`team=Neutral look=Civilian weapon=none scanCandidates=none`. Idle / Hold / SelfDefense. Нет Attack, cover, SHOT, MOVE. Стоит `(-16.0, 0.2, 42.0)` 114 с. P31 — единственный Neutral с LIFE (попал под огонь).

---

### Что следует (формулы не крутить)

1. **Walk починился.** `MOVE ok=1 path=pending` больше не затирается каждым кадром. Юниты идут, SNAP `vel=1.5` на подходе.
2. **Occupied есть.** 15× `COVER_STATE Occupied` + `COVER_REF ConfirmOccupied` с живым `candidateRef`. `cover=C2` на acquire, не dest-only `cover=0`. P01: Acquired dist=0.38 → Occupied → SNAP Occupied до конца.
3. **`MISSING` на Reserve t=0.020** — объект слота ещё не проставлен в hop. К acquire hash совпадает. Это не дыра ConfirmOccupied из 14:49:50.
4. **OutOfTolerance в этой сессии нет.** Все Acquired dist ≤ 0.60. Диск не поднимать.
5. **Search↔Attack** у большинства редкий (`LostCurrentTarget` / `HostileVisible` / `Expired`). Ломает слот **P20**: `ImmediateThreat=1` сам возвращает Attack из Search каждые ~0.15 с → `Release reason=CommandChanged`.
6. **LIFE PASS.** Unconscious/Dead: `ai/vision/combat/move=off`, `coverReleased=1`, `navStopped=1`. Dead `was=Unconscious health=0`.
7. **P01 Occupied ≠ стрельба.** Слот занят, цели в vis нет (`gate=NoWeapon` / `selected=none`). Не ретюнить CoverScore из-за этого.

Occupy **не FROZEN** автоматически: массовая арена всё ещё мешает (Search, ImmediateThreat, Neutral KO). Чистый прогон 1 P01 + cover больше не обязателен, чтобы *увидеть* Occupied — он уже в этом логе. #13/#14/#15 не открывать. Vision / G6 / Weapon / CoverScore / PathScore / 0.60 не трогать.

Словарь тегов — 8.3–8.5. Карточки волны 0 / все слоты 11:35:30 — §8.8. Старт без cover (21.08) — §8.9. Следующий прогон (Search hold) — **§8.11**.

---

## 8.11. Play 28.08.2026: `Infantry_20260828_204049`

**Когда:** 28.08.2026 **20:40:49**, запись **~123 с** (последний SNAP / timeline до t=123.5).
**Сырые логи:** `_Docs/Logs/Runtime/Infantry_20260828_204049/` (`_index.txt`, `_timeline.log`, `Player/Pxx_*.log`).
**Слоты:** файлы все в `Player/P01…P40`. `_index team=` врёт (все Player). Сторона — `SPAWN team=`: Player **P01–P10**, Enemy **P11–P20**, Neutral **P21–P40**.

Полная арена 10+10+20. Прогон **после** Search/Attack hold: `ImmediateThreat` больше не делает Search→Attack. Предыдущий Occupied-отчёт — §8.10 (16:36:40).

### Счётчики сессии (timeline)

| Тег / факт | Число |
|------------|-------|
| `MOVE` source=Tactical | 191 |
| `COVER_STATE Occupied` | **13** событий / **10** юнитов |
| `COVER_STATE Acquired` | 13 |
| `POSITION_ACQUIRE Acquired` | 13 |
| `POSITION_ACQUIRE Traversed` | 7 |
| `POSITION_ACQUIRE Rejected` / `OutOfTolerance` | **9** (P06×7 C2 + P16×2 C4) |
| `COVER_REF candidateRef=MISSING` | 15, фаза Reserve t=0.020 |
| `COVER_DECISION` | 121 (Stay **88**, Reposition **33** — только `CurrentInvalid` / `current=C0`) |
| `COVER_INVALID` | 33 |
| `COVER_HEARTBEAT` | 800 |
| `MOVE_COVER` | 29 |
| `AI_TRANSITION` | 150: LostCurrentTarget 79, HostileVisible 55, Expired 16 |
| `Search→Attack reason=ImmediateThreat` | **0** |
| `PEEK` | 16, все `decision=NoLean` `available=0` |
| `EMERGENCY_COVER Selected` | 1 (P02 C1 @ 41.889) |
| `SHOT` | 174 |
| `LIFE Unconscious` | 18 боевых (все Player + 8 Enemy) |
| `LIFE Dead` | 2 (P17 @ 71.934, P20 @ 116.113) |
| Neutral LIFE / SHOT / COVER | **0** |

Walk **идёт**. Occupied **есть**. Качалка P20 из §8.10 **снята**. Диск 0.60 не поднимать.

### Occupied — кто и когда

| t | Слот | Сторона | Слот cover | dist |
|---|------|---------|------------|------|
| 7.715 | P11 | Enemy, PKM | C4 | 0.57 |
| 8.187 | P14 | Enemy | C3 | 0.60 |
| 12.597 | P05 | Player | C4 | 0.58 |
| 19.533 | P19 | Enemy | C2 | 0.55 |
| 19.838 | P06 | Player, MK12 | C1 | 0.54 |
| 19.911 | P18 | Enemy | C1 | 0.59 |
| 24.223 | **P01** | Player, MK18 | **C2** | **0.54** |
| 27.667 | P16 | Enemy | C2 | 0.53 |
| 29.049 | P12 | Enemy | C1 | 0.55 |
| 29.541 | P13 | Enemy | C4 | 0.53 |
| 35.082 | P13 | Enemy | C4 | 0.59, повтор после Search |
| 89.393 | P11 | Enemy | C2 | 0.59, повтор, стоит до конца |
| 91.788 | P16 | Enemy | C4 | 0.60, повтор, стоит до конца |

Не заняли слот: P02, P03, P04, P07, P08, P09, P10, P15, P17, P20 (KO / Search по дороге).

Конец сессии **Alive + Occupied + vel=0**: **P11 C2** `pos≈(-3.1, 0.1, 66.6)` Attack Hold `gate=NoVisibleTarget`; **P16 C4** `pos≈(13.4, 0.1, 78.6)` Attack Hold `gate=Success` при vis=0. Остальные боевые Unconscious/Dead. Neutral стоят Idle.

### LIFE

18× `Unconscious was=Alive reason=Damage health=1 coverReleased=1 navStopped=1 ai/vision/combat/move=off`, затем Dead у P17 и P20 (`was=Unconscious health=0`). Neutral KO в этой сессии нет (в 16:36:40 был P31).

После LIFE у боевых нет новых MOVE / COVER_DECISION / SHOT.

### P01 — эталон (Player, MK18)

Спавн `(-18.0, 0.0, 11.0)`. Attack dest ≈ `(0.4, 0.1, 76.3)`. Hop C2 `(-4.0, 0.1, 42.8)`. `routeMode=Tactical` `ROUTE_SELECT R102`.

```text
0.020  COVER_STATE  C2 Reserved / Approaching
0.020  COVER_REF    C2 candidateRef=MISSING phase=Reserve
0.020  MOVE         dest=(-4.0, 0.1, 42.8) reason=Attack ok=1 path=pending
24.223 COVER_REF    C2 0xDFC976CA Reserve = Acquire = ConfirmOccupied
24.223 COVER_STATE  C2 Occupied dist=0.54
24.223 POSITION_ACQUIRE Acquired cover=C2
28.545 Attack→Search LostCurrentTarget immediateThreat=0
       COVER_STATE Released reason=CommandChanged     ← слот сброшен Search
37.433 Search→Attack HostileVisible target=P12
       снова C2 Approaching; **20× SHOT** по P12, все `HitOther` (стенка арены)
38.215 слот C2→C1
41.422 LIFE Unconscious coverReleased=1
```

Цепочка Reserve→Occupied с тем же `candidateRef` жива. Occupied **не держится через Search**: `CommandChanged` отпускает слот. Это не качалка ImmediateThreat.

### P20 — hold сработал (Enemy)

5 `AI_TRANSITION`, не 20 как в 16:36:40. Search→Attack только `Expired` / `HostileVisible`. **Нет** `reason=ImmediateThreat`. @ 37.142 Attack→Search `LostCurrentTarget` при `immediateThreat=1` — остаётся в Search. @ 41.067 Unconscious. Occupied не было.

### P06 — OutOfTolerance не про диск 0.60

Occupied C1 @ 19.838 dist=0.54. @ 28.545 Search отпустил слот. Дальше Attack/Search вокруг центра арены. @ 57.241 Nav Reached dest `(0.06, 76.35)` (точка приказа), acquire всё ещё C2 `(3.93, 66.83)` → `distance=9.69` `remaining=0`. Семь Rejected подряд. То же у **P16**: dest центра vs C4 `(13.95, 78.67)` → `distance=13.51` (×2). Это **приехал в центр, слот в 10–13 м**, не «agent чуть мимо 0.60». Tolerance не крутить. P06 @ 61.684 Unconscious.

### P21 — Neutral контроль

`team=Neutral look=Civilian weapon=none scanCandidates=none`. Idle / Hold / SelfDefense. Нет Attack, cover, SHOT, MOVE. Стоит `(-16.0, 0.2, 42.0)` 123 с.

### Peek / Emergency

Peek после Occupied: `direction=None available=0 NoLean` — угла/выгоды нет, lean не обязателен. Emergency: куча `Fallback NoAcceptableCandidate`; один `Selected` P02 C1 под ImmediateThreat (P02 к тому моменту уже шёл в Unconscious).

### Что следует (формулы не крутить)

1. **Юниты пользуются укрытиями на арене.** Attack → Tactical route → hop слота → Walk → Occupied. Не только тесты.
2. **Search/Attack hold PASS.** `ImmediateThreat` не выдёргивает из Search. P20 больше не дёргает слот каждые 0.15 с.
3. **Occupied всё ещё рвёт Search** (`LostCurrentTarget` / `Expired` / `HostileVisible` → `CommandChanged`). Массовые волны Attack→Search: Player @ 28.545, Enemy @ 29.762 (vis=0 из слота).
4. **OutOfTolerance ≠ сломанный диск.** Юнит на dest приказа, acquire — другой слот. 0.60 не поднимать.
5. **Peek не стартует** без доступного направления. Не ретюнить lean.
6. **SHOT часто HitOther** (геометрия арены), не повод крутить G6/оружие.
7. **LIFE PASS.** Neutral не воюют.

Occupy **не FROZEN**. #13/#14/#15 не открывать. Vision / G6 / Weapon / CoverScore / PathScore / 0.60 не трогать.

Словарь тегов — 8.3–8.5. Карточки 11:35:30 — §8.8. Occupied без hold — §8.10.

---

## Практический вывод одной страницей

Зрение, память и конверт 150/300 **готовы**. Личность в мире **FROZEN**. CombatIntent **FROZEN**. Второй стрелок не создавался. Search locomotion **FROZEN**. Attack/Retreat/Flee **FROZEN**. Контракт команды **6.1 CLOSED**. Игровой сервис **6.2 CLOSED**. Игровой ввод **6.3 CLOSED**. Слой команд **6.4 CLOSED**. Боевые прицелы (этап 8), дальность урона (этап 9), кривые точности (этап 10), дисциплина огня (этап 11) и projectile vision (этап 12) **CLOSED**.

Дисциплина огня **A3 CLOSED PASS 21/0**. Projectile Vision **A4+A5 PASS 30/0**. Пассажир/турель **Stage 13 PASS 35/0**. Retain **A7 PASS 31/0**. Attention **B / Stage 15 PASS 44/0**. Sound **C1 PASS 47/0**. Доклад **C2 PASS 72/0**. **Stage 18 Final Perception PASS 49/0**. **A10 стрельба/отдача CLOSED 24.08.2026** — RecoilContract/G/H PASS. **#7 ImmediateThreat CLOSED 24.08.2026.** Следующий шаг — **#8**.

1. ~~в мире нет внешности принадлежности~~ — **FROZEN**;
2. ~~Engage ничего не делает~~ — **FROZEN** (`CombatIntent` → готовность + вето Aim/Fire);
3. ~~Search не ходит~~ — **FROZEN** (Walk к snapshot LastKnown);
4. ~~Attack / Retreat / Flee не ходят~~ — **FROZEN**;
5. ~~нет входа игровой команды~~ — **6.1 CLOSED** (`IssueCommand`);
6. ~~игра не умеет отдать приказ AI~~ — **6.2 CLOSED** (`GameCommandService`);
7. ~~нет RTS/ввода в сервис~~ — **6.3 CLOSED** (`GameCommandInput`);
8. ~~command layer не принят~~ — **6.4 CLOSED** (замена/Cancel/масса; SHOT — #7).

Perception complete — **Stage 18 CLOSED PASS 49/0**. **A10 CLOSED.** **#7 CLOSED.** Следующий шаг — **#8**. Cover/движение/ранг — #13–#15. Группа — #16. Planner — #26.

Play в Editor пишет папку `Infantry_*`. Диагноз конкретного прогона: **этот файл + эта папка**. Цепочка `VISION → SELECT → G6 → DISC → GATE → SHOT` / `PROJECTILE` (ход: `INPUT → GAMECMD → CMD → AI → MOVE`). Первый разрыв = виноватый слой. Словарь тегов — часть 8.

Пока на префабе нет AI, живая стрельба идёт без RoE. Повесили AI и не сменили политику — SelfDefense режет Aim/Fire (`G6 raw=Fire final=Ignore`). Для полного цикла в тесте: Defense + MissionCombat.

---

## Журнал обновлений

| Дата | Изменение |
|------|-----------|
| 29.08.2026 | Сверка **Attack / Defense / захват** с дорожной картой: одиночка ходит и занимает слот; Capture нет (#24); Defense на арене не выдаётся; MissionScore тонкий; Defense в коде ходит к якорю (6.4 «не ходит» устарел). |
| 29.08.2026 | Документирован слой **поиска и запекания потенциальных укрытий**: конвейер Generate → classify → Editor bake → Play cache; достоинства и дыры (одна клетка 16 м, cap 16, stale bake, AABB). Формулы CoverScore / #13/#14 не трогать. |
| 24.08.2026 | A10 CLOSED. Добавлены разделы: философия, цепочка shoot/no-shoot, RoE, площадка 150×50, матрица реализации, дорожная карта #7–#16. Снимок синхронизирован с закрытием стрельбы/отдачи. Следующий открытый слой — **#7 ImmediateThreat**. |
| 24.08.2026 | Добавлены разделы **ранги юнитов** (5 пресетов, навыки, влияние на бой) и **оружие по дистанции** (три типа дальности, классы, WorkingRange, balance-kind). |
| 24.08.2026 | Дорожная карта расширена до конечной системы: фазы I–VI, канон #1–#16 сохранён, #13–#16 уточнены (cover → movement → weapon/rank → group/CQB), добавлены #17–#26. Backlog этапа: цель / появление / приёмка / freeze. Следующий блок по-прежнему **#7**. |
| 24.08.2026 | Создан дизайн-документ `Пехота_система_дизайн.md` v1.0. Дорожная карта и этапы #7–#26 привязаны к §6 дизайн-дока; цикл DESIGN/FREEZE требует соответствия. |
| 24.08.2026 | **#7 CLOSED.** ImmediateThreat live + RoE. E: Use of Force 107/0, Combat Engage 36/0. Следующий слой — **#8**. |
| 28.08.2026 | Play 11:35:30: карточки всех слотов в **§8.8**. |
| 28.08.2026 | Play 16:36:40 **§8.10** (`Infantry_20260828_163640`): Walk PASS, **15 Occupied**, P01 C2 ConfirmOccupied тот же `candidateRef`. OutOfTolerance=0. P20 ImmediateThreat качает Search↔Attack. Отчёт 14:49:50 снят. |
| 28.08.2026 | Play 20:40:49 **§8.11** (`Infantry_20260828_204049`): Walk PASS, **13 Occupied** / 10 юнитов. `Search→Attack ImmediateThreat` = **0**. Occupied рвёт Search (`CommandChanged`). OutOfTolerance = dest центра vs слот 10–13 м, диск 0.60 не поднимать. Occupy не FROZEN. #15 не открывать. |
| 28.08.2026 | **#14B OPEN (14B.0).** Контракт `Readiness_State.md`. HostileVisible → Aim без обязательных ступеней. Aim ≠ Fire. #13/#14 не reopen. #15 не открывать. |
| 28.08.2026 | **#14B.0 закрыт внутри открытого #14B.** EditMode **20/0** (21:47). Play не в 14B.0. |
| 28.08.2026 | **#14B.1 закрыт внутри открытого #14B.** EditMode **38/0**. Play `Readiness_LAST.txt` **15/0** (22:08:12). Stimuli / RequestTransition / decay / hysteresis / прямой Patrol→Aim / канал READINESS. Без поз / Fire. |
| 28.08.2026 | **#14B.2 закрыт внутри открытого #14B.** EditMode **51/0**. Play `Readiness_LAST.txt` **23/0** (22:21:20). Pose request → CombatReadiness. Mapping / LifeGate / Hold≠сломан / Engage без второго Auto / READINESS_POSE. Aim ≠ G6 ≠ Fire. |
| 28.08.2026 | **#14B.3 закрыт внутри открытого #14B.** EditMode **81/0**. Play `Readiness_LAST.txt` **31/0** (22:40:08). HostileVisible из Observed+Hostile → Aim. Gunshot из sound. CombatActivity. Engage ≠ Aim ≠ Fire. LifeGate freeze. READINESS_EVENT / TRANSITION / DECAY. |
| 28.08.2026 | **#14B.4 закрыт внутри открытого #14B.** EditMode **121/0**. Play `Readiness_LAST.txt` **40/0** (23:04:16). Ранг = ToReady/ToAim/Decay, не отдельный AI. Отношения, не freeze мс. ArmFatigue не в формуле. |
| 28.08.2026 | **#14B.5 закрыт внутри открытого #14B.** EditMode **165/0**. Play `Readiness_LAST.txt` **55/0** (23:29:16). Hold от последней боевой активности, затем один step-down. Rising ≠ falling. Refresh без дребезга. Instant 1 с сохранён. ArmFatigue не в формуле. 14B.6 / #15 не открывать. |
| 29.08.2026 | **#14B.6 OPEN (Arm Fatigue).** `ArmFatigue` 0..1. Load/recovery. AimTime↑ RecoilControl↓ TurnTime↑. Не ReadinessState. Instant load=0. LifeGate freeze. `ARM_FATIGUE` / `ARM_FATIGUE_EFFECT`. Не закрывать без PASS. #15 не открывать. |
| 29.08.2026 | **#14B.6 закрыт внутри открытого #14B.** EditMode **205/0**. Play `Readiness_LAST.txt` **70/0** (10:18:31). AimTime↑ RecoilControl↓ TurnTime↑. Не ReadinessState. Instant load=0. LifeGate freeze. 14B.7 / #15 не открывать. #14B **не FROZEN**. |
| 29.08.2026 | **#14B.7 OPEN (Combat Integration).** Perception→Readiness→ArmFatigue→AimTime/Turn/Recoil→Combat. Без новой механики. `ARM_FATIGUE value=` / `READINESS_EFFECT`. Не закрывать без PASS. #15 не открывать. |
| 29.08.2026 | **#14B.7 закрыт внутри открытого #14B.** EditMode **252/0**. Play `Readiness_LAST.txt` **90/0** (10:33:24). AimTime↑ TurnTime↑ RecoilRecovery↓ в боевом контуре. Fatigue не двигает Readiness/G6/RoE/Cover/Movement. #15 не открывать. #14B **не FROZEN**. |
| 29.08.2026 | **#14C OPEN (Threat Direction Knowledge).** `Threat_Direction_Knowledge.md`. Expected из существующих spawn points. Visual > Sound > Report > InitialEstimate. События, не polling. Не Cover / Movement / Readiness. Не закрывать без PASS 35+/0 + 15+/0. #13/#14 не reopen. #15 не открывать. |
| 29.08.2026 | **#14C закрыт внутри открытого #14C.** EditMode **49/0**. Play `ThreatDirection_LAST.txt` **20/0** (11:56:21). Expected из spawn points. Visual > Sound > Report > InitialEstimate. События, не polling. Не Cover / Movement / Readiness. Cover не подключать. #14C **не FROZEN**. #15 не открывать. |
| 29.08.2026 | **#14C.1 OPEN (Cover Orientation & Facing).** Overlay на CoverScore + event facing. Stay Committed. Не закрывать без PASS 35+/0 + 15+/0. #13/#14 не reopen. #15 не открывать. |
| 29.08.2026 | **#14C.1 закрыт внутри открытого #14C.** EditMode **40/0**. Play `ThreatDirectionCover_LAST.txt` **22/0** (12:58:02). Expected влияет на cover до контакта. Visual заменяет Expected. Facing без дрожи. Stay Committed. #13/#14 не reopen. #14C **не FROZEN**. #15 не открывать. |
| 29.08.2026 | **#14C.2 OPEN (Confidence & Uncertainty).** Качество знания → вес cover/facing. Не scan / CoverScore / Move. Не закрывать без PASS 30+/0 + 15+/0. #13/#14 не reopen. #15 не открывать. |
| 29.08.2026 | **#14C.2 закрыт внутри открытого #14C.** EditMode **38/0**. Play `ThreatDirectionQuality_LAST.txt` **19/0** (13:15:02). Visual > Sound > Report > Expected. Stale слабеет. Cover/facing учитывают confidence. Stay Committed. #13/#14 не reopen. #14C **не FROZEN**. #15 не открывать. |
| 29.08.2026 | **#14C.3 OPEN (Tactical Positioning).** `TacticalPositionPreference` поверх CoverScore. Direction + Facing × confidence × sector overlap. Stay Committed. Не Move / scan / CoverScore. Не закрывать без PASS 35+/0 + 15+/0. #13/#14 не reopen. #15 не открывать. |
| 29.08.2026 | **#14C.3 закрыт внутри открытого #14C.** EditMode **43/0**. Play `ThreatDirectionPosition_LAST.txt` **18/0** (13:41:42). Direction/confidence/uncertainty влияют на preference. Stay Committed. Нет Move / скана. CoverScore не трогался. #13/#14 не reopen. #14C **не FROZEN**. #15 не открывать. |
| 29.08.2026 | **#14C.4 OPEN (Dynamic Threat Reorientation).** SignificantChange (50° + conf 0.4). Facing + ThreatFit. Occupied не сбрасывается. Fatigue замедляет turn. Не Move / scan / CoverScore. Не закрывать без PASS 35+/0 + 15+/0. #13/#14 не reopen. #15 не открывать. |
| 29.08.2026 | **#14C.4 закрыт внутри открытого #14C.** EditMode **36/0**. Play `ThreatDirectionReorientation_LAST.txt` **18/0** (14:47:14). Deadband / SignificantChange. Facing + ThreatFit. Occupied не сбрасывается. Fatigue замедляет turn. Нет Move / скана. CoverScore не трогался. #13/#14 не reopen. #14C **не FROZEN**. #15 не открывать. |
| 29.08.2026 | **#14C.5 OPEN (Threat Direction → Reposition Decision).** FaceOnly / Stay / RepositionAllowed. Occupied не снимается. #13 исполняет только по флагу. Не Move / scan / CoverScore. Не закрывать без PASS 35+/0 + 15+/0. #13/#14 не reopen. #15 не открывать. |
| 29.08.2026 | **#14C.5 закрыт внутри открытого #14C.** EditMode **39/0**. Play `ThreatDirectionReposition_LAST.txt` **18/0** (15:51:13). FaceOnly / Stay / RepositionAllowed. Occupied Stay Committed без флага. Fit Poor→Good даёт разрешение. Нет прямого Move / скана. CoverScore не трогался. #13/#14 не reopen. #14C **не FROZEN**. #15 не открывать. |
| 28.08.2026 | **Search/Attack hold + Occupied hold.** `ImmediateThreat` больше не делает Search→Attack. Attack→Search: visual dwell 1.5 с **или** Search 2.0 gunshot/report. Occupied+valid+LOS не свапает по score. EmergencyCover overlay разрешён в Search. CoverScore/0.60/#13/#14/#15 не трогать. |
| 28.08.2026 | **Occupy integration поверх frozen #13/#14.** Хранить CoverCandidate до acquire; ConfirmOccupied; `cover=C1` vs `cover=0`; Attack/Defense Walk диск 0.60; reservation на подходе; Attack Search dwell 1.5 с (не gunshot); commit пока Reserved. #13/#14 не открывать. #15 не открывать. Occupy не FROZEN (массовая арена). |
| 28.08.2026 | **Диагностика occupy (события, не тик).** Теги `COVER_DECISION` `COVER_REF` `COVER_INVALID` `COVER_HEARTBEAT` `MOVE_COVER` `AI_TRANSITION`; `LIFE`/`SNAP`/`POSITION_ACQUIRE` расширены. |
| 28.08.2026 | **Fix Walk: не reissue на pathPending.** `!hasPath` во время расчёта пути каждый кадр сбрасывал `SetDestination`. Диск 0.60 не трогать. |
