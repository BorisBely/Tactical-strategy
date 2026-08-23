# Vision Stage 11 — Fire Discipline

**Статус: CLOSED / VERIFIED** (2026-08-22 22:06:49). `Tools/Tests/Run Fire Discipline Contract (Play)` → `Assets/_Docs/Logs/Tests/FireDisciplineContract_LAST.txt`. **PASS 21/0**.  
**Это A3** карты `Пехота_дорожная_карта.md`. Не Attention (B). Projectile Vision — **A4+A5 / Stage 12 CLOSED PASS 30/0**. Не отдача (A10).  
**Не трогали:** Q, `VisionRange`, `ScopeVisionRange`, E стволов/патронов, Range×, BaseDamage, recoil, Cone, Threat, Memory, Identity, AccuracyCurve, AimTime×, hitscan envelope, полы позы HipFire **0.35** / PointAim **0.68** / Aiming **1.0**.

Этапы 8 / 9 / 10 остаются **CLOSED / VERIFIED** (`OpticRangeContract_LAST.txt` **PASS 29/0**, `WeaponRangeContract_LAST.txt` **PASS 53/0**, `AccuracyAimCurveContract_LAST.txt` **PASS 11/0**). Q / `ScopeVisionRange` / E в этом Play не сдвинулись.

Play: `Tools/Tests/Run Fire Discipline Contract (Play)` → `Assets/_Docs/Logs/Tests/FireDisciplineContract_LAST.txt`.  
EditMode: `FireDisciplineContractTests`.  
NavMesh warning harness — тот же шум, что у этапов 8/9/10, не FAIL.

Следующее по зрению — **C1 Sound CLOSED / VERIFIED PASS 47/0**. C2 **CLOSED / VERIFIED PASS 72/0**. Stage 15 Attention **CLOSED / VERIFIED PASS 44/0**. A7 **CLOSED / VERIFIED PASS 31/0**. Stage 13 **CLOSED / VERIFIED PASS 35/0**.

---

## Цепочка

```text
VISION → CONTACT → SELECT → G6 = Fire
                              ↓
                         DISC = как стрелять
                              ↓
                         GATE = можно ли этот выстрел
                              ↓
                            SHOT
```

Дистанция **не** ставит `CanFire = false`. Она задаёт длину серии, паузу, режим, порог Aim, экономию патрона и характер пулемёта.

DISC не знает внутренностей Vision, не запускает SHOT, не пробивает GATE. Второй fire controller не добавлялся.

---

## 11.1. Заморозка

Неизменны на этом этапе:

```text
Q
VisionRange
ScopeVisionRange
E
Range×
BaseDamage
recoil
Cone
Threat
Memory
Identity
AccuracyCurve
AimTime×
hitscan
HipFire 0.35 / PointAim 0.68 / Aiming 1.0
```

Stage 11 определяет, **как солдат использует уже существующую точность**.

---

## 11.2. Инвентаризация (до правки)

Живой слой уже был отдельным:

| Кусок | Где |
| --- | --- |
| план серии | `WeaponFireDisciplinePlanner` |
| фазы Idle / Aiming / Firing / Pause | `UnitWeaponFireDisciplineController` (order 54) |
| виртуальный спуск | `UnitWeaponFireController.StartFiring` → GATE |
| контакт без порога прицела | `ShouldHoldVirtualTriggerIgnoringAim` (G6 Aim **или** Fire) |
| лог | `DISC` на смене фазы |

Старые пояса (сняты как основная шкала): авто **25 / 70 / 140** (LMG ещё **220**). Это была шкала мира 500 м, не запрет огня, но «140 м = далеко» для любого ствола.

---

## 11.3. Семантика поясов, не косметика 25→30

Не делали `25→30 / 70→60 / 140→120 / 220→180`.

Старый смысл:

```text
близко     агрессивный ближний бой
далее      контролируемый автомат
далее      короче очередь
дальше     экономный огонь
```

Новый смысл привязан к **рабочему диапазону класса**, не к фиксированным метрам.

---

## 11.4. Нормализованная дистанция

```text
normalizedDistance = distance / effectiveDisciplineRange
0.0 = вплотную
0.5 = середина рабочего диапазона
1.0 = дальний край доступного применения этого класса
```

`effectiveDisciplineRange` — **не** новая дальность стрельбы и **не** `VisionRange` / `ScopeVisionRange`. Это уже известный боевой конверт класса (engagement edge этапа 9 как характер, не как E урона).

| Профиль | WorkingRange, м | Источник |
| --- | ---: | --- |
| CQB | 150 | CqbShort / CqbControlled, пистолет, SMG |
| Shotgun | 50 | Shotgun |
| Assault | 200 | carbine / battle rifle / default |
| LMG | 220 | Support* / LightMachineGun |
| Marksman | 250 | Mosin, SVD, M16A4 |
| Sniper | 300 | Dmr: MK12, Sniper762x51 |
| Heavy / Grenade | 300 | M2 / MK19 — без открытия A4/A5 |

SniperRifle в `WeaponClass` **не** делает всех снайперами: Mosin/SVD остаются Marksman.

LMG **220** здесь — рабочий край класса, не старый пояс 140–220.

---

## 11.5. Не VisionRange

Снайпер может видеть 300 м с Scope9. Это не автоматически «дисциплина на 300», если бы working range был другим. И наоборот: `VisionRange` не становится `MaxFireRange`.

Дисциплина сидит **между** выбором Fire и использованием оружия.

---

## 11.6. Профили

| Класс | Характер |
| --- | --- |
| CQB | короткий Aim, короткая пауза, длиннее очередь, aggressive auto |
| Assault | короткая/средняя очередь; на дальнем краю серия падает, Aim выше |
| LMG | поддерживает огонь: длиннее очередь, меньше пауза; на дальнем краю очередь ещё допустима |
| Marksman | преимущественно одиночный, короткая серия, выше Aim, пауза между выстрелами |
| Sniper | одиночный, высокий Aim, длинная пауза, нет spray |

Формула: **Profile + DistanceCurve + Skill**. Лишних множителей нет.

---

## 11.7–11.9. План один раз на серию + hysteresis

```text
New target / смена существенного состояния
   ↓
Build FirePlan
   ↓
series, pause, mode, needAim
   ↓
execute
```

Пересчёт только если сменились цель, пояс дистанции, оружие, пояс позы, G6-контакт, конец серии.

Hysteresis поясов **0.08** по нормализованной оси:

```text
Close < 0.20
Near  < 0.45
Mid   < 0.70
Far   < 0.90
VeryFar
```

Выход из пояса — через `enter − 0.08`. 69↔71 м у штурмового (оба Near при range 200) больше не качают очередь.

Потеря контакта (G6 не Aim/Fire): план сбрасывается, hysteresis пояса не держится через «цель пропала на 0.3 с».

---

## 11.10. AimProgress

```text
Close    можно быстро
Mid      короткая стабилизация
Far      сначала навестись
VeryFar  почти полный Aim
```

Полы позы не менялись. Дисциплина может требовать **больше** пола режима (`max(band, pose floor)`). Precision до Mid → QuickAim (пол 0.68); Far+ → FullAim.

---

## 11.11. GATE

```text
DISC → виртуальный trigger → GATE → SHOT
```

GATE по-прежнему сам проверяет Fire intent, conscious, weapon, pose, AimProgress, line of fire. `DISC says fire / GATE says no` остаётся отдельной диагностикой.

---

## 11.12–11.15. Матрица приёмки

Классы: CQB (AK-74U), Assault (M4), LMG (M249), Marksman (SVD), Sniper (7.62×51).  
Дистанции: 10 / 25 / 50 / 100 / 150 / 200 / 225 / 250 / 300.

Ожидаемый характер (детерминированный mid min/max, skill 50):

| | 10 м | 100 м | 150 м | 300 м |
| --- | --- | --- | --- | --- |
| CQB | FullAuto, длинная серия, низкий Aim | короче | Semi | Semi, серия ≥ 1 |
| Assault | Burst/Auto | Burst 2–4 («ещё штурмовой») | Semi 1–2 | Semi, серия ≥ 1 |
| LMG | длинный FullAuto | FullAuto | FullAuto ≥ 5, длиннее Assault | очередь ещё есть |
| Marksman | Semi ≤ 2 | Semi | Semi ≤ 2 | Semi |
| Sniper | Semi × 1 | Semi × 1 | Semi × 1 | Semi × 1 |

Дальняя клетка **стреляет**. Просто менее выгодна.

Ammo: оценка выстрелов за 3 с контакта. Штурмовой у 10 м < 30 (магазин), LMG < 80 (не высыпает ленту за контакт).

---

## 11.16. Лог

Существующий `DISC` на смене фазы. На **смене FirePlan** добавляется:

```text
profile=... distanceBand=... n=... range=...
```

Не каждый кадр. Новых raycast / VisionSystem нет.

---

## 11.17. Acceptance

- Discipline не знает Vision, не стреляет сама, GATE независим, нет второго контроллера.
- 25/70/140/220 не основная шкала; нет логики «мира 500 м».
- CQB агрессивен вблизи; Assault универсален; LMG поддерживает; Marksman/Sniper не автомат.
- Hysteresis; сброс плана при потере цели; план не каждый Update.
- Q / VisionRange / ScopeVisionRange / E численно те же (Frozen_E_M4=140, Frozen_E_Sniper=225, Reddot V=150, Scope9 V=300, AimTime× Scope9=1.55).
- Stage 8/9/10 регрессия: те же LAST-файлы, этапом 11 не переписываются.

**Не добавляли:** suppression-систему, morale, отрядный огонь, cover, Attention/Facing, sound, доклад.

Play stamp: `FireDisciplineContract_LAST.txt` **RESULT=PASS PASS=21 FAIL=0** (2026-08-22 22:06:49). NavMesh warning при спавне harness — тот же шум, что у этапов 8/9/10, не FAIL. Stage 12 **CLOSED / VERIFIED PASS 30/0**. Stage 13 **CLOSED / VERIFIED PASS 35/0**. A7 **CLOSED / VERIFIED PASS 31/0**. Stage 15 Attention **CLOSED / VERIFIED PASS 44/0**. Stage 16 Sound **CLOSED / VERIFIED PASS 47/0**. C2 **CLOSED / VERIFIED PASS 72/0**.

---

## Файлы

- `WeaponFireDisciplineProfile.cs`
- `WeaponFireDisciplinePlanner.cs`
- `UnitWeaponFireDisciplineController.cs`
- `FireDisciplineContractTests.cs`
- `FireDisciplineContractRuntimeSmoke.cs`
- `FireDisciplineContractTestRunner.cs`
