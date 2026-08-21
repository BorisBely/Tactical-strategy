# Зрение как игровой инструмент: спецификация калибровки

**Статус: Block A CLOSED; Block B CLOSED; Block C CLOSED; Vision FROZEN; Identity World Evidence FROZEN; Combat Engage Execution FROZEN; AI Perception Contract FROZEN; Search Navigation FROZEN; Tactical Navigation FROZEN**  
**Block A — Detection: CLOSED / VERIFIED** (2026-08-19). Формула Q **заморожена**.  
**Block B — Memory: CLOSED / VERIFIED** (2026-08-19 22:03). B13 **5 / 30 / 1.5 / 0.25**.  
**Block C — Identity: CLOSED / VERIFIED** (2026-08-19 22:37). C15 **4.0 / 0.50**. Threat High≤25 / Medium≤80.  
**Vision Freeze / AI Handoff:** `Assets/_Docs/Closed/Vision_AI_Handoff.md`.  
**AI Perception Contract FROZEN:** `Assets/_Docs/Closed/AI_Perception_Contract.md`. Play **PASS 41/0** (23:10).

**Слой кода:** `Assets/_Scripts/Unit/Vision/`  
**Архитектура (G0–G8 закрыта):** `Assets/_Docs/Closed/Vision_Current_Architecture_And_Future_Philosophy.md`  
**Приёмка A:** Strict V1.9.4 Play **PASS 83/0** (20:27). G1–G8 one Play V1.9.5 **PASS 9/0** (20:45) → `DetectionG_Regression_LAST.txt`.  
**Приёмка B:** G1–G8 one Play **PASS 9/0** (22:03) → тот же файл, B13 5/30/1.5/0.25 не ретюнит Q.  
**Калибровка A:** math — `Tools/Tests/Run Detection Calibration Math (no Play)`. Runtime A–H — `Tools/Tests/Run Detection Calibration Runtime (Play)`. Strict — `Tools/Tests/Run Detection Calibration Strict (Play)`. G-регрессия — `Tools/Tests/Run Detection G1–G8 (Play)`.  
**Калибровка B:** math **PASS 14/0** (21:08). Runtime M1–M10 **PASS 105/0** (21:38). B13 locked 5/30/1.5/0.25. B15/B16: G1–G8 **PASS 9/0** (22:03).  
**Калибровка C:** math **PASS 36/0** (22:38). Runtime C3–C14 **PASS 49/0** (10:23, C13). C15/C16 **4.0 / 0.50**.  
**Этап 1 Identity World Evidence FROZEN:** `Closed/Identity_World_Evidence.md`. Play **PASS 49/0** (10:23). Mapping **13/13**.  
**Этап 2 Combat Engage Execution FROZEN:** `Closed/Combat_Engage_Execution.md`. Play **PASS 31/0** (10:56). EditMode **14/0** (11:25).  
**Этап 3 Search Navigation Execution FROZEN:** `Closed/Search_Navigation_Execution.md`. Play **PASS 45/0** (12:06). EditMode **18/0**. Не ретюнит A/B/C.  
**Этап 4 Tactical Navigation Execution FROZEN:** `Closed/Tactical_Navigation_Execution.md`. Play **PASS 36/0** (14:46). EditMode **31/0**.  
**Дорожная карта FROZEN:** `Closed/Tactical_AI_Roadmap.md`. Следующее — **#6 Real game commands**.  
**Vision Freeze:** `Tools/Tests/Verify Vision Freeze` → `VisionFreeze_LAST.txt`.  
**AI-0:** `Tools/Tests/Run AI Perception Handoff (Play)` → `AIPerceptionHandoff_LAST.txt` **PASS 41/0** (23:10).  
**AI-1:** `Tools/Tests/Run AI Tactical State (Play)` → `AITacticalState_LAST.txt` **PASS 71/0**.  
**AI-1A FROZEN:** `Tools/Tests/Run AI Use of Force (Play)` → `UseOfForcePolicy_LAST.txt` **PASS 107/0** (00:38).  
**Этот документ отвечает на вопрос:** как солдат **должен** воспринимать ситуацию с точки зрения gameplay — не «насколько реалистична формула».

Инвариант:

```text
physical evidence → confidence → knowledge → decision
```

Настройка отвечает не на «похоже ли на человеческий глаз», а на:

> при конкретных условиях заранее понятно, насколько быстро солдат обнаружит цель, насколько уверен в ней, как долго будет её помнить и что сможет сделать с этой информацией.

---

## 0. Зачем этот документ сейчас

Архитектура зрения (G0–G8) **закрыта**. Калибровка A/B/C **закрыта**. Зрение **заморожено**: `Vision_AI_Handoff.md`.

Дальше — AI / Search / tactics как **другие системы**, которые читают `PerceivedContact`. Не крутить Q / Memory / Identity, «чтобы AI стал умнее».

Шкала A/B/C зафиксирована. Крутить коэффициенты во время AI нельзя: иначе снова нельзя отличить «плохо обнаружил» от «плохо понял / выбрал / решил стрелять / плохо ищет».

**Текущая фаза:** зрение **заморожено**. Identity World Evidence **FROZEN**. Combat Engage Execution **FROZEN**. AI Perception Contract **FROZEN**. AI-1 **FROZEN**. AI-1A **FROZEN**. Search locomotion **FROZEN**. Attack/Retreat/Flee **FROZEN**. Следующее — **#6 Real game commands** (`Tactical_AI_Roadmap.md`). Block D разобран по номерам карты, не открывать пачкой.

---

## 1. Цель настройки

Нужно предсказуемое игровое поведение, а не симулятор глаза.

| Вопрос, на который отвечаем | Вопрос, на который не отвечаем |
|-----------------------------|--------------------------------|
| Правильно ли солдат воспринимает ситуацию для этой игры? | Насколько формула «как у человека»? |
| За сколько игрок/дизайнер ожидает Detected в сцене A…H? | Какой SmoothStep красивее на графике? |
| Где практический предел 500 м? | Нужно ли слить VisionRange и MaxEngageRange? (**нет**: 500 м perception, 18 м engage) |

Запрещено в любой фазе калибровки:

- `LOD →` штраф к Q / DetectionProgress;
- knowledge-поля в `VisionObservation`;
- путать `VisionRange` (perception, prefab **500 м**) и `MaxEngageRange` (**18 м**);
- одновременно крутить Detection + Identity + Selector + Engagement и «смотреть, стало лучше».

---

## 2. Пять блоков калибровки (карта, не очередь «всё сразу»)

```text
A Detection     насколько хорошо видна → как быстро замечают
B Memory        Observed → RecentlyLost → Lost → decay
C Identity      кто это / насколько уверен / relationship / threat
D Select/Act    PerceivedContact → TargetSelector → EngagementDecision
E Perf          стоимость после того, как поведение уже правильное
```

G8 уже дал compute budget (4 tier, FOV до LOS, cache, 500 м). **E проверяют после A**, не вместо A.

### Блок A — Обнаружение — CLOSED / VERIFIED

Настраиваем только:

```text
цель в мире
  → Distance, FOV, Exposure, Movement
  → Q = Distance × FOV × Exposure × Movement
  → DetectionProgress + hysteresis
  → Undetected / Detecting / Detected
```

Вопрос блока:

```text
Насколько хорошо цель видна?
        ↓
Как быстро её замечают?
```

**В коде уже есть** (не изобретать второй Q):

| Смысл | Где |
|--------|-----|
| Формула Q | `DetectionQualityMath.VisibilityQuality` |
| Distance / FOV / Movement factors | `DetectionQualityMath` + поля `DetectionProcessor` |
| Exposure | `VisionObservation.Exposure01` (доля видимых hit-zone samples) |
| Накопление / потеря progress | `DetectionQualityMath.IntegrateProgress` |
| Пороги acquire / lose | `m_AcquireThreshold` / `m_LoseThreshold` |
| Времена acquire / loss | `m_AcquireTimeSeconds` / `m_LossTimeSeconds` |
| Конус / дальность скана | `UnitVision` (`VisionRange` 500 м на `Unit.prefab`, FOV 120°) |

Замороженные **production defaults** (V1.9.5, не крутить без нового блока калибровки):

| Параметр | Значение |
|----------|--------:|
| VisionRange | **500 м** (`Unit.prefab`; ≠ MaxEngageRange **18 м**) |
| FOV cone | **120°** |
| FovHalfReference | **60°** |
| FovEdgeFactor | **0.15** |
| DistanceNear | **20 м** |
| DistanceFar | **500 м** |
| DistanceFarFactor | **0.08** |
| AcquireThreshold | **0.25** |
| LoseThreshold | **0.20** |
| AcquireTime | **0.35 с** |
| LossTime | **2.5 с** |
| Movement idle / walk / run / cap | **1.00 / 1.15 / 1.35 / 1.50** |

Фаза 1 **не меняет** формулу-произведение и не добавляет lighting / camouflage / fatigue. Меняются значения и **ожидания по шкале ситуаций**.

### Блок B — Потеря контакта и память — CLOSED / VERIFIED

```text
Observed → RecentlyLost → Lost → LastSeenConfidence decay
```

Архитектура G2/G4 уже даёт `RecentlyLost → Lost`, `LastSeen`, `LastKnownPosition` (заморожен) и decay. Калибруются **игровые секунды**, не формула и не Search/Hunt.

B1 baseline (стартовый, финал — после B11/B12 по логам):

| Параметр | Baseline | Назначение |
|----------|--------:|------------|
| `RecentlyLostDurationSeconds` | **5.0 с** | очень свежая потеря |
| `MemoryHorizonSeconds` | **30.0 с** | полный срок полезности памяти |
| `MemoryDecayShape` | **1.5** | форма затухания |
| `MemoryStaleConfidence` | **0.25** | граница старой информации |

Зоны ощущения: 0–5 VERY FRESH; 5–12 FRESH; 12–20 UNCERTAIN; 20–30 STALE APPROACH; 30+ FORGOTTEN (`conf=0`, contact остаётся).

Запрещено: `if (reloading) extend memory`, danger/player multipliers, velocity extrapolation. Memory ничего не знает о приказах, reload, Search, роли или выборе цели.

### Блок C — Идентификация — CLOSED / VERIFIED

«Я вижу объект — кто это?» ≠ Detection. `PerceivedIdentity` — affiliation-класс, не таксономия Soldier/Military.

G3 уже разделяет `DetectionProgress ≠ IdentityConfidence`, `PerceivedIdentity ≠ UnitTeam`. Заморожены **игровые** IdentifyTime / commit / Threat-дистанции, не Selector и не Combat.

C15 production defaults (не крутить без нового блока калибровки):

| Параметр | Baseline | Назначение |
|----------|--------:|------------|
| `IdentifyTimeSeconds` | **4.0 с** | время до IdentityConfidence=1 при Q=1 |
| `IdentityCommitThreshold` | **0.50** | commit PerceivedIdentity (≈ **2.0 с** при Q=1) |
| Threat High | **≤ 25 м** | Hostile близко |
| Threat Medium | **≤ 80 м** | Hostile средне; дальше Low |

`PerceivedIdentity` сейчас affiliation-класс (Friendly/Neutral/Hostile/Unknown), не роль Soldier/Military. Relationship — отдельное поле, выводится из committed Identity. Hostile+far = Threat Low.

Запрещено: читать `UnitTeam` как знание AI; мгновенно менять committed identity при смене cue; открывать Selector/Fire из этого блока.

World look: `VisualIdentityEvidence` (Player/Enemy/Civilian) на цели. Наблюдатель маппит в Friendly/Hostile/Neutral. IdentifyTime / commit **не** менялись.

### Блок D — Выбор и действие (закрыт до конца фазы 1)

```text
PerceivedContact → TargetSelector → EngagementDecision
```

`Detected ≠ Selected ≠ Fire`. Unknown selectable, LastKnown ≠ aim — уже закон. Не трогать в фазе 1.

### Блок E — Производительность (проверка после A)

После того как сценарии A–H ведут себя правильно: тот же Play / G8 Stress. Если Q «починили» ценой постоянного T3 на 100 idle observers — это регресс E, не «более реалистичный глаз».

---

## 3. Иерархия настройки (обязательный порядок)

Не крутить 30 параметров сразу.

```text
1. Физическая заметность     Q-факторы (Distance, FOV, Exposure, Movement)
       ↓
2. Скорость обнаружения      acquire time / threshold / progress
       ↓
3. Потеря контакта           (фаза 2, блок B)
       ↓
4. Память                    (фаза 2, блок B)
       ↓
5. Идентификация             (фаза 3, блок C)
       ↓
6. Угроза / отношение        (фаза 3, блок C)
       ↓
7. Выбор цели                (фаза 4, блок D)
       ↓
8. Engagement                (фаза 4, блок D)
```

Если одновременно менять Detection, Identity и Selection, нельзя сказать, **что** сломалось.

Документ архитектуры разделяет уровни специально:

> плохо обнаружил ≠ плохо понял ≠ плохо выбрал ≠ плохо решил стрелять.

---

## 4. Фаза 1 — только физическое обнаружение

В работе:

```text
ЦЕЛЬ
↓
Distance
FOV
Exposure
Movement
↓
Q
↓
DetectionProgress
↓
Detected
```

**Вне фазы 1:** Identity, Threat, Memory, TargetSelector, EngagementDecision, LOD-tiers как «баланс заметности».

Критерий фазы 1:

> **Как должен видеть солдат этой игры?** — ответ по эталонной шкале ситуаций, не по ощущению от одной сцены.

---

## 5. Эталонная шкала ситуаций

Сначала категории времени, не `1.37 с`.

### 5.1. Категории скорости обнаружения

| Код | Категория | Смысл для дизайна |
|-----|-----------|-------------------|
| I | мгновенно | Detected в первом же осмысленном кадре evidence |
| VF | очень быстро | меньше чем «успел среагировать ногами» |
| F | быстро | короткая задержка, без «вглядывания» |
| M | умеренно | заметная пауза, но не поиск |
| S | медленно | надо удерживать в конусе |
| VS | очень медленно | почти предел системы |
| N | практически нет | полный скан имел бы право не Detected |

Секунды заполнены по runtime Play (допуск Strict, не math до сотых). AcquireTime=0.35 с не даёт категорию **I**.

### 5.2. Контрольные сценарии

Exposure в таблице — **дизайн-намерение** (какая доля тела должна быть видима). В рантайме `Exposure01` приходит из LOS / hit-zones, не из этой колонки.

| ID | Дистанция | Видимость (намерение) | FOV | Движение | Категория | Runtime tDetect |
|----|----------:|-----------------------|-----|----------|-----------|----------------:|
| A | 10 м | 100% тела | центр (0°) | стоит | **VF** | **~0.34 с** |
| B | 30 м | 100% тела | центр | идёт | **VF** | **~0.35 с** |
| C | 80 м | 50% тела | центр | стоит | **F** | **~0.72 с** |
| D | 80 м | 50% тела | 30° | идёт | **M** | **~1.28 с** |
| E | 150 м | 30% тела | центр (0°) | стоит | **M** | **~1.34 с** |
| F | 250 м | 30% тела | 50° | стоит | **N** | timeout (observation есть) |
| G | 400 м | 10% тела | 50° | бежит | **N** | timeout (observation есть) |
| H | 500 м | минимальная цель (0.05) | край FOV (60°) | стоит | **N** | timeout (observation на краю) |

Категории **утверждены** V1.9.5: A/B = VF, C = F, D/E = M, F/G/H = N (Observed ≠ Detected). Не баг формулы, что F/G/H timeout.

Связь с уже существующим harness `DetectionTestController` (пресеты A–G **другие**, не путать буквы):

| Harness сейчас | Дистанция / FOV / движение | Ближайший новый ID |
|----------------|----------------------------|--------------------|
| A | 10 м, 0°, idle | новый A |
| B | 30 м, 0°, idle | новый B без движения |
| C | 80 м, 15°, walk, exp 0.5 | между новым C и D |
| D | 100 м, 50°, walk | ближе к новому F по углу |
| E | 200 м, 0°, idle, exp 0.2 | между новым E и F |
| F/G | 400 м, 50°, idle/run, exp 0.1 | новый G |

Фаза 1 может расширить harness до шкалы A–H, не ломая G1 AutoSmoke, **после** утверждения категорий.

### 5.3. Таблица зрения (дизайн поведения)

Итог фазы 1 — не набор float в инспекторе, а такая шкала:

```text
ОЧЕНЬ БЛИЗКО + открытая цель
  → практически мгновенное обнаружение

СРЕДНЯЯ ДИСТАНЦИЯ + открытая цель
  → быстро

СРЕДНЯЯ ДИСТАНЦИЯ + частично закрытая
  → заметная задержка

ДАЛЬНЯЯ ДИСТАНЦИЯ + небольшая неподвижная
  → очень медленно

ДАЛЬНЯЯ ДИСТАНЦИЯ + край FOV
  → может вообще не обнаружиться
```

---

## 6. Что калибруем в формуле (фаза 1) — карта ручек

Только эти ручки, и только чтобы попасть в шкалу §5.

**Заметность (шаг иерархии 1)**

| Ручка | Поле | Влияние |
|-------|------|---------|
| Ближняя полная заметность | `m_DistanceNearMeters` (20) | Q по дистанции = 1 до этой дальности |
| Потолок perception | `m_DistanceFarMeters` (500) | совпадает с `UnitVision.VisionRange` на префабе |
| Насколько «слепнет» на far | `m_DistanceFarFactor` (0.08) | Q на 500 м при прочих = 1 |
| Насколько край FOV хуже центра | `m_FovHalfReferenceDegrees` / `m_FovEdgeFactor` | offset 0 → 1; на half (60°) → edge 0.15 |
| Движение цели | walk/run multipliers + cap | только бонус ≥ 1 |
| Частичная видимость | не отдельный float | `Exposure01` из hit-zones |

**Скорость (шаг иерархии 2)** — крутить **после** того, как относительный порядок A…H по Q выглядит верно:

| Ручка | Поле | Влияние |
|-------|------|---------|
| Когда progress растёт | `m_AcquireThreshold` | Q выше порога → accumulate |
| Когда progress падает | `m_LoseThreshold` | ниже → decay; между — hold |
| Как быстро до Detected | `m_AcquireTimeSeconds` | скорость роста |
| Как быстро progress тает без Q | `m_LossTimeSeconds` | **не** RecentlyLost (это блок B) |

`m_LossTimeSeconds` в фазе 1 — только про **progress**, не про «сколько помнит солдат».

Не калибровать в фазе 1: `m_RecentlyLostDurationSeconds`, `m_MemoryHorizonSeconds`, `m_IdentifyTimeSeconds`, Selector, Engagement, G8 LOD intervals.

---

## 7. Спецификация поведения (фаза 1) — восемь пунктов

Пункт 8 (потеря уже полученного контакта) **записываем**, но **не калибруем** в фазе 1.

### Черновик ответов (нужно утвердить)

Опора: философия закрытой архитектуры §8.3–8.4. Это **намерение дизайна**, не измеренные секунды.

**1. Как быстро солдат замечает очевидные цели?**  
Близко (~10–20 м), почти всё тело, центр конуса, без укрытия → категория **I / VF**. Не «смотреть секунду».

**2. Какие цели считаются трудными?**  
Дальняя дистанция (≳250 м) **и/или** малый Exposure **и/или** край FOV **и** неподвижность. Трудная ≠ «враг в кустах с lighting shader». Lighting / camouflage / fatigue **не входят**.

**3. Как дистанция влияет на заметность?**  
До ~20 м фактор дистанции полный. Дальше плавно падает к far. На **500 м** остаётся слабый, но ненулевой вклад — только хорошо заметные ещё имеют шанс. 500 м — предел **perception**, не дистанция огня.

**4. Как FOV влияет на заметность?**  
Центр конуса — полный вклад. К краю — слабее: `FovHalfReference` = **60°** (half конуса 120°), `FovEdgeFactor` = 0.15. V1.8a растянул кривую: 30° ещё сильный вклад, 50° уже заметно хуже, 60° = край. Цель вне конуса `UnitVision` не даёт `VisionObservation` → нет Q. Не чинить «не видит за спиной» увеличением Q.

**5. Как частичная видимость влияет?**  
`Exposure01` множитель в Q. 50% тела ≈ вдвое хуже при тех же Distance/FOV (до потолка Movement). Не подменять Identity («вижу голову → сразу Hostile»).

**6. Как движение цели помогает?**  
Только бонус. Стоит = 1. Идёт / бежит ускоряет обнаружение, не делает невидимое видимым. Движение не компенсирует полное укрытие и не открывает Fire.

**7. Где практический предел зрения?**  
Условные **500 м**. Сценарий H имеет право быть **N**. Не решать «плохо видит на 500» сжатием `VisionRange` — это ломает смысл G8.

**8. Как быстро теряется уже полученный контакт?**  
**Вне фазы 1.** Зафиксировать позже в блоке B: RecentlyLost ≠ memory horizon ≠ progress loss time.

---

## 8. Порядок работ фазы 1

1. Утвердить категории и черновик §7 (этот файл).  
2. Не менять код, пока не заполнена колонка «категория» для A–H.  
3. Прогнать шкалу на текущих defaults → записать фактическую категорию / время Detected.  
4. Сравнить факт с ожиданием. Крутить **сначала факторы Q**, потом acquire time/threshold.  
5. Один параметр за проход, когда можно. Не трогать Identity/Selector.  
6. Когда A–H совпадают с категориями — перевести категории в секунды.  
7. Регрессия: G1–G8 Play должны остаться зелёными; G8 Stress — sanity стоимости (блок E light).

Приёмка фазы 1: таблица §5.2 заполнена (ожидание + факт), расхождения объяснены, формула-произведение не разъехалась.

---

## 9. Вне скоупа этого документа

- Search / hunt AI (другая система).  
- Роли Scout / MG / Commander, Observe / Report / Suppress как поведение.  
- Калибровка Identity / Threat / Selector / Engagement.  
- «Реалистичный глаз»: свет, камуфляж, усталость, внимание.  
- Слияние 500 м detect и 18 м engage.

---

## 10. Диагностический прогон V1.1–V1.3 (2026-08-19)

Не G-этап. Формула и defaults тогда **не** менялись. Относительный порядок Q: **PASS**. G1–G8 Play остались зелёными.

Калибровку **не** гонять на каждом Play: `DetectionCalibrationAutoSmoke.m_RunOnStart=false`. Повтор: `Tools/Tests/Run Detection Calibration`.

### Факт V1.3 (FOV half=45°)

| ID | Q | tDetect | expected | actual | Вывод |
|----|--:|---------|----------|--------|--------|
| A | 1.00 | 0.36 с | I | VF | Q полный, но AcquireTime=0.35 с не даёт «мгновенно» |
| B | 1.00 | 0.36 с | VF | VF | Distance 30 м почти 1.0; walk clamp в Q=1 — A и B неотличимы |
| C | 0.48 | 0.74 с | F | F | совпадает |
| D | 0.20 | timeout | F–M | N | **FOV 30° → fovFactor=0.37**; Q < AcquireThreshold 0.35 |
| E | 0.25 | timeout | M | N | центр, 150 м, 30% тела — Q всё ещё **ниже 0.35** |
| F | 0.03 | timeout | S | N | 50° ≥ 45° ref → fovFactor=0.15 |
| G | 0.00 | timeout | VS | N | то же + far + exp 0.1 |
| H | 0.00 | timeout | N | N | как задумано |

Порядок Q V1.3: A=B > C > E > D > F > G > H. Distance не ломал шкалу. Ломало обнаружение: (1) `AcquireThreshold=0.35`; (2) FOV half 45° резал уже на 30°.

---

## 10.1. V1.8a FOV retune (2026-08-19)

Только `DefaultFovHalfDegrees` **45 → 60**. Формула, Distance, Exposure, Movement, AcquireThreshold **0.35**, AcquireTime **0.35 с**, edge **0.15** без изменений.

Menu `Tools/Tests/Run Detection Calibration` **15:40:49**: **RESULT=PASS pass=6 fail=0** (`DetectionCalibration_LAST.txt`).

FOV-кривая:

| offset | V1.3 | V1.8a |
|-------:|-----:|------:|
| 0° | 1.000 | 1.000 |
| 30° | 0.370 | **0.575** |
| 50° | 0.150 | **0.213** |
| 60° | 0.150 | 0.150 |

A–H (math, те же сценарии):

| ID | Q before | Q after | tDetect | expected | actual | Вывод |
|----|--------:|--------:|---------|----------|--------|--------|
| A | 1.0000 | 1.0000 | 0.36 с | I | VF | без изменений (FOV 0°) |
| B | 1.0000 | 1.0000 | 0.36 с | VF | VF | без изменений |
| C | 0.4802 | 0.4802 | 0.74 с | F | F | без изменений |
| D | 0.2045 | **0.3176** | timeout | F–M | N | fov 0.370→0.575; Q всё ещё **ниже 0.35** |
| E | 0.2502 | 0.2502 | timeout | M | N | FOV 0° — retune не трогает E |
| F | 0.0256 | **0.0363** | timeout | S | N | 50° больше не равен краю; всё ещё трудный |
| G | 0.0037 | **0.0053** | timeout | VS | N | остаётся очень трудным |
| H | 0.0006 | 0.0006 | timeout | N | N | край 60° = edge 0.15 |

Порядок Q после V1.8a: A=B > C > **D > E** > F > G > H. D обогнал E — ближе к шкале (80 м / 50% / walk vs 150 м / 30% / idle). 50° и 60° больше не совпадают.

Успех V1.8a = форма кривой 30/50/60, не попадание D/E в Detected. **Эксперимент закрыт.**

Play регрессия после V1.8a (2026-08-19): G1 **20/0**, G2 **20/0**, G3 **30/0**, G4 **32/0**, G5 **21/0**, G6 **26/0**, G7 **29/0**, G8 AutoSmoke **19/0**, G8 Stress **24/0**. Калибровочный AutoSmoke на Play **не** запускался (`m_RunOnStart=false`) — так и задумано. `IK-GRIP-UNREACHABLE` — шум harness, не зрение.

---

## 10.2. V1.8b AcquireThreshold sweep (2026-08-19 15:59:55)

Menu **RESULT=PASS pass=6 fail=0**. Тогда production default ещё был 0.35. Apply порога — V1.8c (**0.25**).

Обрывы:

- `high`: 0.35 — D/E/F timeout (default на момент sweep)
- `D_only`: 0.30 … 0.26 — D Detected **1.12 с** (provCat F), E timeout
- `DE`: 0.25 … 0.15 — D 1.12 с, E **1.40 с** (provCat M), F timeout
- Внутри зоны tDetect **не** меняется (вентиль бинарный)

Сводка:

| THR | D | E | F | zone |
|----:|---|---|---|------|
| 0.35 | timeout | timeout | timeout | high |
| 0.30 … 0.26 | Detected 1.12 с | timeout | timeout | D_only |
| **0.25** | Detected 1.12 с | Detected 1.40 с | timeout | **DE** |
| 0.24 … 0.15 | Detected 1.12 с | Detected 1.40 с | timeout | DE |

A/B/C на 0.15 те же 0.36 / 0.36 / 0.74 с. G/H на 0.15 timeout (`maxProgress=0`).

Самый высокий THR в `DE` = **0.25**. Пользователь зафиксировал **0.25** (не 0.24). Apply — V1.8c.

F в этом диапазоне не копится (`Q=0.0363`). Категория S для F — не задача этого порога.

---

## 10.3. V1.8c Q→time + gate (2026-08-19 16:11:06)

Menu **RESULT=PASS pass=10 fail=0**. Production `AcquireThreshold=0.25`, AcquireTime **0.35**. Формула Q и FOV без изменений. **Эксперимент закрыт.**

A–H на default 0.25:

| ID | Q | tDetect | expected | actual |
|----|--:|---------|----------|--------|
| A | 1.0000 | 0.36 с | I | VF |
| B | 1.0000 | 0.36 с | VF | VF |
| C | 0.4802 | 0.74 с | F | F |
| D | 0.3176 | **1.12 с** | F–M | M |
| E | 0.2502 | **1.40 с** | M | M |
| F | 0.0363 | timeout | S | N |
| G | 0.0053 | timeout | VS | N |
| H | 0.0006 | timeout | N | N |

D/E впервые Detected. F/G/H по-прежнему N (Q ниже вентиля). Diagnostic bin для D даёт M (порог F &lt; 1 с), не F–M — секунды пока не acceptance.

Q TIME SWEEP (монотонно 1.00→0.30; на 0.25 обрыв hold):

| Q | tDetect | state | branch |
|--:|---------|-------|--------|
| 1.00 | 0.36 с | Detected | grow |
| 0.90 | 0.40 с | Detected | grow |
| 0.80 | 0.44 с | Detected | grow |
| 0.70 | 0.50 с | Detected | grow |
| 0.60 | 0.60 с | Detected | grow |
| 0.50 | 0.70 с | Detected | grow |
| 0.40 | 0.88 с | Detected | grow |
| 0.30 | 1.18 с | Detected | grow |
| 0.25 | timeout | Undetected | hold |

GATE: 0.251 Detected 1.40 с grow; 0.250 и 0.249 timeout hold `maxProgress=0`. Совпадает со `Q > 0.25`.

Play регрессия **после** `AcquireThreshold=0.25` (обычный Play, не V1.9): G1 **20/0**, G2 **20/0**, G3 **30/0**, G4 **32/0**, G5 **21/0**, G6 **26/0**, G7 **29/0**, G8 AutoSmoke **19/0**, G8 Stress **24/0**. Инварианты пайплайна живы. `IK-GRIP-UNREACHABLE` — шум harness.

---

## 10.4. V1.9 Runtime A–H (протокол)

Параметры не менять. Math уже принят. Цель: production `UnitVision` + LOS/hit-zones + scheduler дают те же входы/времена.

Запуск: `Tools/Tests/Run Detection Calibration Runtime (Play)`. Отчёт: `Assets/_Docs/Logs/Tests/DetectionCalibrationRuntime_LAST.txt`. G1–G8 на Play не гоняются.

Путь: `UnitVision → VisionObservation → UnitPerception → DetectionProcessor → PerceivedContact`. Без `ApplySyntheticObservation`. ImmediateScan только чтобы observer остался на Detail; skip ≠ empty frame, Q не штрафуется LOD.

Для каждого A–H лог: math expected vs runtime (distance, FOV, Exposure01, movement, факторы, Q, progress, DetectionState, ObservationState, tDetect).

PASS/FAIL:

- A–E Detected; F–H нет Detected
- Distance ±2.5 м, FOV ±8°
- Exposure vs дизайн ±0.20 — если fail, **не** крутить AcquireThreshold (physical observation / hit-zones)
- tDetect vs math только если Exposure совпал (допуск max(0.45 с, 40% math))

### Факт (2026-08-19 17:16:17)

Play **SampleScene** (не synthetic). Отчёт: `Assets/_Docs/Logs/Tests/DetectionCalibrationRuntime_LAST.txt`.  
**RESULT=FAIL pass=32 fail=7**. Пайплайн: `UnitVision → VisionObservation → UnitPerception → DetectionProcessor`. Defaults без изменений (FOV 60/0.15, `AcquireThreshold=0.25`, `AcquireTime=0.35`). **Порог не крутить.**

Размещение: observer pad `z=16` look `+Z`. A–E/G/H: `cand=1`, `hitZones=35`, `perception=1`. F: `losBlocker=S5_Top`.

| ID | math Q | math t | runtime dist | FOV | E hit-zones (дизайн) | runtime Q | tDetect | Det / Obs |
|----|-------:|-------:|-------------:|----:|----------------------|----------:|---------|-----------|
| A | 1.0000 | 0.36 с | 9.8 м | 0.3° | 1.00 (1.00) | 0.9999 | **0.29 с** | Detected / Observed |
| B | 1.0000 | 0.36 с | 29.8 м | 0.7° | 1.00 (1.00) | 0.9985 | **0.34 с** | Detected / Observed |
| C | 0.4802 | 0.74 с | 79.8 м | 0.0° | **1.00 (0.50)** | 0.9607 | 0.35 с | Detected / Observed |
| D | 0.3176 | 1.12 с | 79.8 м | 30.5° | **1.00 (0.50)** | 0.5419 | 0.63 с | Detected / Observed |
| E | 0.2502 | 1.40 с | 149.8 м | 0.0° | **1.00 (0.30)** | 0.8345 | 0.40 с | Detected / Observed |
| F | 0.0363 | timeout | 250 м | нет obs | blocker **S5_Top** (0.30) | — | timeout | Undetected / Lost |
| G | 0.0053 | timeout | 400.9 м | 57.6° | **1.00 (0.10)** | 0 | timeout | Undetected / Lost |
| H | 0.0006 | timeout | 499.8 м | 60.0° | **1.00 (0.05)** | 0 | timeout | Undetected / Lost |

Факторы runtime vs math:

| ID | D | F | E | M |
|----|--:|--:|--:|--:|
| A | 1.000 / 1.000 | 1.000 / 1.000 | 1.000 / 1.000 | 1.000 / 1.000 |
| B | 0.999 / 0.999 | 1.000 / 1.000 | 1.000 / 1.000 | 1.000 / 1.150 |
| C | 0.961 / 0.960 | 1.000 / 1.000 | 1.000 / 0.500 | 1.000 / 1.000 |
| D | 0.961 / 0.960 | 0.564 / 0.575 | 1.000 / 0.500 | 1.000 / 1.150 |
| E | 0.834 / 0.834 | 1.000 / 1.000 | 1.000 / 0.300 | 1.000 / 1.000 |

Контракт Detected: **A–E да, F–H нет** — выполнен. Distance ±2.5 м: все PASS. FOV ±8°: A–E/G/H PASS; F нет наблюдения. A/B Time PASS (допуск 0.45 с). C/D/E Time SKIP (exposure mismatch). F/G/H NoDetect PASS.

7 FAIL (не формула, не порог):

1. `Runtime_C_Exposure` design 0.50 → hit-zones 1.00  
2. `Runtime_D_Exposure` design 0.50 → hit-zones 1.00  
3. `Runtime_E_Exposure` design 0.30 → hit-zones 1.00  
4. `Runtime_F_Fov` нет `VisionObservation` (`S5_Top`)  
5. `Runtime_F_Exposure` то же  
6. `Runtime_G_Exposure` design 0.10 → hit-zones 1.00  
7. `Runtime_H_Exposure` design 0.05 → hit-zones 1.00  

C/D/E детектятся быстрее math, потому что runtime Q выше (E=1 на открытом поле). B/D Movement runtime=1.00 при math walk/run 1.15/1.35 — цель не успела дать скорость до Detected / G не накопил contact. `IK-GRIP-UNREACHABLE` — шум harness.

---

## 10.5. V1.9.1 / V1.9.2 — физическое staging

Пайплайн V1.9 принят. **Не менять** `Q`, `AcquireThreshold`, `AcquireTime`, FOV, DistanceFactor. **Не писать** `VisionObservation.Exposure01`.

Staging только в harness: `DetectionCalibrationExposureStaging` + `DetectionTestController.ApplyCalibrationScenario`. G1–G8 presets **без** cover.

| ID | Design Exposure | Staging |
|----|----------------:|---------|
| A, B | 1.00 | без стенки |
| C, D | 0.50 | стенка по весам hit-zone samples |
| E | 0.30 | стенка |
| F | 0.30 | yaw-mirror если `S5_Top` полностью закрывает LOS, затем частичная стенка |
| G | 0.10 | то же; Detected **не** ожидается |
| H | 0.05 | стенка; Observation на краю FOV **не** форсировать |

---

## 10.6. V1.9.3 Runtime A–H (Play 2026-08-19 19:54:47)

Play **SampleScene**. Отчёт: `Assets/_Docs/Logs/Tests/DetectionCalibrationRuntime_LAST.txt`.  
**RESULT=PASS pass=42 fail=0**. Defaults без изменений. Cover: C/D h=1.20 E=0.50; E h=1.30 E=0.31; F h=1.30 E=0.31 (yaw mirrored); G h=1.61 E=0.09; H h=1.65 E=0.05.

| ID | Runtime E | Runtime Q | tDetect | Ожидание | Факт |
|----|----------:|----------:|--------:|----------|------|
| A | 1.00 | 0.9999 | 0.34 с | Detected | PASS Time |
| B | 1.00 | 0.9986 | 0.35 с | Detected | PASS Time (M runtime=1.00, Q всё ещё 1) |
| C | **0.50** | **0.4842** | **0.72 с** | Detected ~0.74 | PASS Time |
| D | **0.50** | 0.2657 | **1.28 с** | Detected ~1.12 | PASS Time (M=1.00, не 1.15) |
| E | **0.31** | **0.2617** | **1.33 с** | Detected ~1.40 | PASS Time |
| F | **0.31** | (ниже вентиля) | timeout | No Detect | PASS; blocker=`CalibrationExposureCover`, не `S5_Top` |
| G | **0.09** | (ниже вентиля) | timeout | No Detect | PASS; FOV 42.4° (допуск ±8°) |
| H | **0.05** | (ниже вентиля) | timeout | No Detect | PASS; Observation на краю есть |

Контракт V1.9: A–E Detected, F–H нет — выполнен. Distance/FOV/Exposure все PASS. Time C/D/E больше не SKIP.

Известный шум harness (не Detection): `IK-GRIP-UNREACHABLE`; NavMesh Warp off-mesh при yaw-mirror F/G (vision не требует NavMesh). MovementFactor walk/run по-прежнему 1.00 до Detected — не крутить порог.

**Эксперимент V1.9.3 закрыт.** F/G/H runtime Q=0 не чинить: NoDetect — acceptance, snapshot Q не требуется. Дальше V1.9.4 strict.

---

## 10.7. V1.9.4 Strict validation — CLOSED

Не менять defaults. Acceptance только: distance / FOV / exposure / Detected / time. Staging (cover, yaw-mirror) не входит в PASS.

Запуск: `Tools/Tests/Run Detection Calibration Strict (Play)` → `DetectionCalibrationRuntimeStrict_LAST.txt`.

Состав:

```text
A–H contract (Detected A–E, NoDetect F–H; Observed ≠ Detected)
N1 вне FOV (90°)
N2 дальше 500 м
N3 полный LOS blocker
N4 за спиной (180°)
N5 Exposure≈0 + walk
FOV 59/60/61, Range 499/500/501, Exposure 0/0.05/0.10
Skip-scan ≠ empty frame
G1–G8 LAST (не re-run в этом Play)
```

Play **2026-08-19 20:12** — `FAIL pass=80 fail=2`: только `N2_NoObservation` и `Boundary_Range501_NoObservation`. Detected на 501/510 был правильный отказ. Observation жила потому что `ShootingRangeManager` ставил Player `VisionRange=550` (контракт / prefab = 500). Не Q.

Play **2026-08-19 20:27:55** — `RESULT=PASS pass=83 fail=0` после пина range=500. `Defaults_VisionRange expected=500 runtime=500`. N2: `dist=510 visible=False range=500`. Range 499/500 observe, 501 нет.

A–H в этом Play совпали с V1.9.3 (E tDetect 1.34 vs 1.33). F/G/H: Observed ≠ Detected. Live half-FOV 70° (weapon not ready) — FOV 61° имеет observation, это не FAIL.

Шум harness, не Detection: `IK-GRIP-UNREACHABLE`; NavMesh `set_enabled` на G (off-mesh, vision не требует NavMesh).

**Эксперимент V1.9.4 закрыт.** Q / пороги / FOV curve не трогать.

---

## 10.8. Detection Calibration Baseline (зафиксирован)

Источник времён A–H: V1.9.3 Play **2026-08-19 19:54:47**; игровые секунды шкалы — V1.9.4 Strict (E **1.34 с**). Не подгонять к math до сотых.

| ID | Dist | FOV | Design E | tDetect baseline | Detected |
|----|-----:|----:|---------:|-----------------:|----------|
| A | 10 м | 0° | 1.00 | **0.34 с** | да |
| B | 30 м | 0° | 1.00 | **0.35 с** | да (M runtime=1.00) |
| C | 80 м | 0° | 0.50 | **0.72 с** | да |
| D | 80 м | 30° | 0.50 | **1.28 с** | да |
| E | 150 м | 0° | 0.30 | **1.34 с** | да |
| F | 250 м | 50° | 0.30 | timeout | нет (observation есть) |
| G | 400 м | 50° | 0.10 | timeout | нет (observation есть) |
| H | 500 м | 60° | 0.05 | timeout | нет (observation на краю FOV) |

Perception cap: **500 м** (`Unit.prefab` / `DetectionQualityMath.DefaultFarMeters`). 501 м и 510 м — нет `VisionObservation`. Engage range остаётся **18 м**.

---

## 10.9. V1.9.5 Block A final Play — CLOSED / VERIFIED

Один Play: `Tools/Tests/Run Detection G1–G8 (Play)`.  
Отчёт: `Assets/_Docs/Logs/Tests/DetectionG_Regression_LAST.txt`.  
Stamp **2026-08-19 20:45:45**. **RESULT=PASS pass=9 fail=0**.

| Stage | LAST | Результат |
|-------|------|-----------|
| G1 | DetectionG1_LAST.txt | PASS 20/0 |
| G2 | DetectionG2_LAST.txt | PASS 20/0 |
| G3 | DetectionG3_LAST.txt | PASS 30/0 |
| G4 | DetectionG4_LAST.txt | PASS 32/0 |
| G5 | DetectionG5_LAST.txt | PASS 21/0 |
| G6 | DetectionG6_LAST.txt | PASS 26/0 |
| G7 | DetectionG7_LAST.txt | PASS 29/0 |
| G8 | DetectionG8_LAST.txt | PASS 19/0 |
| G8 Stress | DetectionG8_Stress_LAST.txt | PASS 24/0 |

Q / пороги / FOV / VisionRange не менялись. `IK-GRIP-UNREACHABLE` — шум harness.

**Block A CLOSED / VERIFIED.** Block B Memory закрыт отдельно (не ретюнит Q). См. §12.

---

## 11. Что дальше

```text
Block A Detection CLOSED / VERIFIED
Block B Memory CLOSED / VERIFIED   (5 / 30 / 1.5 / 0.25)
Block C Identity CLOSED / VERIFIED (4.0 / 0.50, Threat 25/80)
Vision FROZEN / AI Handoff
↓
AI / Search / tactics — другие системы (читают PerceivedContact)
Block D Select/Act — не открыт (не чинить ретюном зрения)
```

Контракт: `Assets/_Docs/Closed/Vision_AI_Handoff.md`. Проверка: `Tools/Tests/Verify Vision Freeze`.

---

## 12. Block B — Memory Gameplay Calibration

Цель: убедительная иллюзия субъективного знания, не симуляция человеческой памяти.

```text
World State ≠ Perception State ≠ Decision State
```

### B1 CONTRACT

```text
RecentlyLost = 5 s     LOCKED
MemoryHorizon = 30 s   LOCKED
Shape = 1.5            LOCKED
Stale = 0.25           LOCKED
```

LOS loss → RecentlyLost
RecentlyLost timeout → Lost
Lost → LastSeen/LastKnown frozen → Confidence decay
Confidence <= 0.25 → Stale (и ещё HasMemory)
t >= 30 s → Confidence = 0  (Forgotten ≠ Deleted)
Reacquire → confidence = 1, LastSeen/LastKnown/Time update, same contact, Identity preserved
LastKnownPosition = LastSeenPosition  (нет velocity × time)
```

Production `IsStale(0) = false`: forgotten отделён от stale. Калибровочная таблица пишет оба флага.

### Порядок

```text
B1  baseline 5 / 30 / 1.5 / 0.25
B2  MemoryCalibration runner
B3  Math decay tests
B4  Lifecycle M1/M2
B5  Frozen LastKnown
B6  0–60 s timeline
B7  Reacquire
B8  Long-loss / Forgotten
B9  Dual observers
B10 Reacquire after forgotten
B11 RecentlyLost sweep 2/3/5/7/10     diagnostic
B12 Horizon sweep 20/30/45/60         diagnostic
B13 LOCKED 5 / 30 / 1.5 / 0.25
B14 Runtime VERIFIED PASS 105/0
B15 G2/G4 PASS 20/0 и 32/0
B16 Memory CLOSED / VERIFIED       ← G1–G8 PASS 9/0 (22:03)
```

B11/B12 в math-отчёте — diagnostic dump, не FAIL. Финал (B13) определяется по логам, не по этому baseline как догме.

Меню: `Tools/Tests/Run Memory Calibration` (math). Play: `Tools/Tests/Run Memory Calibration (Play)`, `m_RunOnStart = false`.

### B1–B10 VERIFIED (Play 2026-08-19 21:38:47)

`MemoryCalibrationRuntime_LAST.txt` — **RESULT=PASS pass=105 fail=0**.

M6 лист (H=30, shape=1.5, RecentlyLost=5):

| t | Obs | conf | zone |
|--:|-----|-----:|------|
| 0 | RecentlyLost | 0.998 | VERY_FRESH |
| 2 | RecentlyLost | 0.899 | VERY_FRESH |
| 5 | Lost | 0.758 | FRESH_MEMORY |
| 12 | Lost | 0.463 | UNCERTAIN_MEMORY |
| 20 | Lost | 0.191 stale | STALE_APPROACH |
| 30 | Lost | 0.000 forgotten | FORGOTTEN |
| 60 | contact kept, conf=0 | 0.000 | FORGOTTEN |

Stale crossing ≈ 18.09 с. LastKnown заморожен. Reacquire (15 с и 60 с) → conf=1, same contact, Identity preserved. Observer A/B независимы. `IK-GRIP-UNREACHABLE` — шум harness.

### B15–B16 CLOSED / VERIFIED (Play 2026-08-19 22:03:54)

`DetectionG_Regression_LAST.txt` — **RESULT=PASS pass=9 fail=0**. Q / FOV / пороги не менялись.

| Stage | LAST | Результат |
|-------|------|-----------|
| G1 | DetectionG1_LAST.txt | PASS 20/0 |
| G2 | DetectionG2_LAST.txt | PASS 20/0 |
| G3 | DetectionG3_LAST.txt | PASS 30/0 |
| G4 | DetectionG4_LAST.txt | PASS 32/0 |
| G5 | DetectionG5_LAST.txt | PASS 21/0 |
| G6 | DetectionG6_LAST.txt | PASS 26/0 |
| G7 | DetectionG7_LAST.txt | PASS 29/0 |
| G8 | DetectionG8_LAST.txt | PASS 19/0 |
| G8 Stress | DetectionG8_Stress_LAST.txt | PASS 24/0 |

Промежуточный FAIL G5–G7 (21:50) был **selector LoF** на синтетическом aim 6 м в стороне от живого тела, не G4 decay. Harness: reacquire на `target.position`, pad idle 10 м между стадиями. G2/G4 по-прежнему проверяют LastSeen offset без selector aim.

**Block B — Memory CLOSED / VERIFIED.** Не добавлять `if (reloading) extend memory` и не экстраполировать LastKnown.

---

## 13. Block C — Identity / Relationship / Threat Gameplay Calibration

Цель: юнит сначала видит объект, потом понимает кто это, потом формирует отношение и угрозу. Не симуляция паспорта.

```text
Detection → Observed → Identity evidence → IdentityConfidence → PerceivedIdentity → Relationship → Threat
```

`VisionObservation` — только physical evidence. Знание живёт на `PerceivedContact`. `UnitTeam` не читается как AI-знание.

### C0 CONTRACT

```text
Detected + Identity=Unknown          валидно
Unknown ≠ Friendly
Relationship=Unknown, Threat=None    пока нет commit
Evidence = VisualAffiliation (world look) mapped by observer side, never target UnitTeam
LOS loss holds IdentityConfidence    (Memory не трогает Identity)
Cue conflict ≠ instant remap         reset + новое накопление
Hostile + far → Threat Low           валидно
```

`PerceivedIdentity` в G3 — affiliation-класс (Friendly / Neutral / Hostile / Unknown), не таксономия Soldier/Military. Relationship — отдельное поле, выводится из committed Identity.

### C1 BASELINE — C15 LOCKED

```text
IdentifyTimeSeconds     = 4.0 s     LOCKED
IdentityCommitThreshold = 0.50      LOCKED
Threat High             ≤ 25 m
Threat Medium           ≤ 80 m      иначе Low
```

IdentifyTime — время до **conf=1** при Q=1. Commit Hostile при Q=1 ≈ **2.0 с** (4.0 × 0.50). Полная уверенность ≈ 4.0 с.

### Порядок

```text
C0  правила
C1  baseline 4.0 / 0.50  LOCKED
C2  math tests
C3  Hostile timeline
C4  cues по отдельности
C5  Unknown (видеть, не знать)
C6  Identity ≠ Relationship ≠ Threat
C7  Threat vs relationship
C8  Threat distance sweep
C9  LOS loss holds Identity
C10 reacquire
C11 cue change (не телепорт команды)
C12 dual observers
C13 VisualIdentityEvidence
C14 отчёт IdentityCalibration_LAST.txt
C15 IdentifyTime LOCKED 4.0 / 0.50
C16 runtime VERIFIED PASS 49/0 (10:23, C13)
C17 не трогать Selector / Engagement / Combat / Search
C18 Identity CLOSED / VERIFIED       ← math 36/0 (22:38), Play 49/0 (10:23)
C19 Identity World Evidence FROZEN   ← look на цели; не огонь, не Engage
C20 Combat Engage Execution FROZEN   ← CombatIntent Hold/Engage; Play 31/0, EditMode 14/0
C21 Search Navigation Execution FROZEN ← Walk к snapshot LastKnown; Play 45/0, EditMode 18/0
```

C15 IdentifyTime sweep в math-отчёте — diagnostic dump, не FAIL.

Меню: `Tools/Tests/Run Identity Calibration` (math). Play: `Tools/Tests/Run Identity Calibration (Play)`, `m_RunOnStart = false`.

G-регрессия G3 остаётся архитектурным smoke. Block C — игровой масштаб тех же полей.

### C1–C14 VERIFIED (2026-08-19 22:21)

Math: `IdentityCalibration_LAST.txt` **PASS 35/0**.  
Runtime: `IdentityCalibrationRuntime_LAST.txt` **PASS 47/0**.  
`IK-GRIP-UNREACHABLE` — шум harness.

C3 Hostile, Q=1, IdentifyTime=2.0:

| t | conf | Identity | Rel | Threat |
|--:|-----:|----------|-----|--------|
| 0.00 | 0.000 | Unknown | Unknown | None |
| 0.25 | 0.125 | Unknown | Unknown | None |
| 0.50 | 0.250 | Unknown | Unknown | None |
| **1.00** | **0.500** | **Hostile** | Hostile | High |
| 1.50 | 0.750 | Hostile | Hostile | High |
| 2.00 | 1.000 | Hostile | Hostile | High |

Detected при t=0.50 (P=1) при Identity ещё Unknown. Dual observers независимы. Cue flip: Hostile→Friendly через Unknown, не мгновенный remap. VisualIdentityEvidence даёт world-look без `SetAffiliationCue`. UnitTeam цели остался Neutral.

Threat (Hostile): 10/25 High, 50/80 Medium, 100+ Low.

### C15 LOCKED (2026-08-19)

```text
IdentifyTimeSeconds     = 4.0 s
IdentityCommitThreshold = 0.50
```

Кривая Q=1 (C16 Play 22:37):

| t | conf | Identity |
|--:|-----:|----------|
| 0.50 | 0.125 | Unknown (уже Detected) |
| 1.00 | 0.250 | Unknown |
| **2.00** | **0.500** | **Hostile** |
| 4.00 | 1.000 | Hostile |

### C16 CLOSED / VERIFIED (Play 2026-08-19 22:37:56)

Math: `IdentityCalibration_LAST.txt` **PASS 36/0** (22:38:01).  
Runtime: `IdentityCalibrationRuntime_LAST.txt` **PASS 48/0**.  
`IK-GRIP-UNREACHABLE` — шум harness.

C3 Hostile, Q=1, IdentifyTime=4.0:

| t | conf | Identity | Rel | Threat |
|--:|-----:|----------|-----|--------|
| 0.50 | 0.125 | Unknown | Unknown | None (уже Detected, P=1) |
| 1.00 | 0.250 | Unknown | Unknown | None |
| **2.00** | **0.500** | **Hostile** | Hostile | High |
| 2.50 | 0.625 | Hostile | Hostile | High |
| 4.00 | 1.000 | Hostile | Hostile | High |

Detected при t=0.50 при Identity ещё Unknown. Dual observers независимы. Cue flip: Hostile→Friendly через Unknown, не мгновенный remap. VisualIdentityEvidence даёт world-look без `SetAffiliationCue`. UnitTeam цели остался Neutral.

Threat (Hostile): 10/25 High, 50 Medium, 100/400 Low. Friendly/Neutral → None.

Commit на границе 0.50: `CommitFloatSlack=0.001` (40×0.05 с). IdentifyTime не ретюнить из-за float. 0.49 остаётся Unknown.

**Block C — Identity CLOSED / VERIFIED.** Не читать `UnitTeam` как знание AI. Не открывать Selector / Fire из identity.

**Этап 1 — Identity World Evidence FROZEN** (Play 2026-08-20 10:23).  
Runtime: `IdentityCalibrationRuntime_LAST.txt` **PASS 49/0** (C13 Hostile / Unknown / UnitTeam цели Neutral).  
EditMode mapping: `VisualAffiliationMappingTests` **13/13**.  
Контракт: `Assets/_Docs/Closed/Identity_World_Evidence.md`. Слой не ретюнить.

**Этап 2 — Combat Engage Execution FROZEN** (Play 2026-08-20 10:56:57).  
Play: `CombatEngageExecution_LAST.txt` **PASS 31/0**. EditMode **14/0**.  
Контракт: `Assets/_Docs/Closed/Combat_Engage_Execution.md`. Слой не ретюнить. AI не на `Unit.prefab`.

**Vision FROZEN / AI Handoff.** Контракт: `Assets/_Docs/Closed/Vision_AI_Handoff.md`. Проверка: `Tools/Tests/Verify Vision Freeze`.
