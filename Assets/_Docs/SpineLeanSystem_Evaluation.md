# Spine Lean — самодостаточная оценка системы наклона

Дата снимка: **2026-08-18**.  
Юнит прогона: `Unit(Clone)`.  
Фильтр консоли: `SpineLeanDiag`.  
Этот файл закрывает вопрос «наклоняется ли юнит правильно» **без репозитория**. Ниже — как устроена система, как читать поля, сырые логи прогона и вывод по ним.

---

## 0. Вердикт этого прогона

**Наклон корпуса работает. 4/4 PASS.**

Проверено: поза Aiming, без цели, взгляд прямо по телу, стоя и сидя, влево и вправо.

| Клетка | leanDeg (факт / профиль) | Ствол в сторону lean | Ствол всё ещё вперёд | Корень | Итог |
|--------|-------------------------:|---------------------:|---------------------:|--------|------|
| Standing Left | −42.0 / 42.0 | 0.244 m ≥ 0.16 | yaw −0.4°, dot 1.00 | 0.000 m | PASS |
| Standing Right | +49.6 / 49.6 | 0.208 m ≥ 0.16 | yaw +0.5°, dot 1.00 | 0.000 m | PASS |
| Crouch Left | −38.0 / 38.0 | 0.143 m ≥ 0.10 | yaw +0.3°, dot 1.00 | 0.000 m | PASS |
| Crouch Right | +44.8 / 44.8 | 0.137 m ≥ 0.10 | yaw −0.4°, dot 1.00 | 0.000 m | PASS |

Что это **доказывает**

- Кости `Spine_01` / `Spine_02` крутятся поверх аниматора на величину профиля.
- Ствол уезжает в ту же сторону, что наклон, и остаётся направленным вдоль тела.
- Юнит не шагает корнем. Peek — не стрейф.

Что это **не доказывает**

- Доворот ствола на выбранную цель (lean-aim / FromTo). Прогон специально **снимает цель**.
- Наклон на ходьбе, беге, prone, в технике, с гранатой.
- Боевой AI: он `SetLeanTarget` не вызывает. Вход — кнопки панели **Накл.Л / Накл.П** и этот прогон по **K**.

---

## 1. Что это за система

Юнит **не сдвигается корнем** и **не шагает в сторону**. Peek — это **roll двух костей позвоночника** (`Spine_01`, `Spine_02`) вокруг горизонтального forward юнита. Плечи, руки, IK и ствол уезжают влево/вправо как следствие геометрии скелета.

Наклон корпуса и прицеливание ствола — **разные подсистемы**. Этот прогон проверяет только наклон (и что ствол не развернулся в бок/в небо).

Знак:

- `lean01 = −1` — влево (к −right юнита)
- `lean01 = +1` — вправо
- `lean01 = 0` — нейтраль (чистый аниматор)

Правый наклон сильнее левого на множитель **1.18** (компенсация оружия на правой стороне):

- Standing: лево 42°, право 42 × 1.18 = **49.6°**
- Crouch: лево 38°, право 38 × 1.18 = **44.8°**

Боковой world-сдвиг костей выключен (`MaxLateralMeters = 0`). Смещение ствола — побочный эффект roll, не отдельный translate.

Профили idle (этот прогон):

| Профиль | MaxLeanDegrees | Spine_01 доля | Spine_02 доля | SmoothTime |
|---------|---------------:|--------------:|--------------:|-----------:|
| StandingIdle | 42 | 0.25 | 0.75 | 0.13 с |
| CrouchIdle | 38 | 0.25 | 0.75 | 0.14 с |

Блоки (наклон принудительно к 0): нет костей, ragdoll, техника, перетаскивание/перенос раненого, граната, переход стойки, prone, бег/спринт. Walk **не** блок.

Порядок кадра (зачем это знать): аниматор пишет кости, затем `UnitSpineLean.LateUpdate` добавляет roll. Оружие висит на кисти: крутится торс → кисть едет в мире → ствол едет. Local-поворот оружия этому прогону не нужен (цели нет, FromTo не включается).

---

## 2. Как снят этот прогон

Клавиша **K** на выбранном юните с экипированным оружием.

Последовательность:

1. Цель снимается (`target=none`), взгляд прямо по forward тела.
2. Поза **Aiming**, idle (не walk).
3. Standing: снимок нейтрали → lean влево до settle → lean вправо до settle.
4. Crouch: то же.
5. Повторное K отменяет.

Живые логи `[SpineLean] REQUEST/SETTLED/TICK` на этом прогоне выключены. Смотреть только `[SpineLeanDiag]`.  
Строки `[CrouchXfade]` — лог аниматора перехода в присед, к lean не относятся.

Пороги PASS (зашиты в прогоне):

| Проверка | Порог |
|----------|-------|
| Поза | Aiming, цели нет |
| `blocked` | false |
| `settled` | true |
| Корень не шагнул | ≤ 0.05 m |
| Смещение ствола в сторону lean | Standing ≥ 0.16 m, Crouch ≥ 0.10 m |
| Ствол смотрит вперёд тела | dot(barrelXZ, bodyForward) ≥ 0.55 |
| \|Δyaw ствола\| от нейтрали Aiming | ≤ 25° |
| \|pitch ствола\| | ≤ 22° |
| Кости спины реально накренились | \|Δroll Spine_02\| или Quaternion.Angle ≥ 8° |
| `leanDeg` дошёл до профиля | ≥ 85 % от ожидаемого максимума |

`rollMatch angle` — `Quaternion.Angle` позы Spine_02 до/после. Это надёжная величина наклона кости. Сырой Δroll в эйлерах может быть **больше** `leanDeg` (особенность раскладки Mixamo). Смотреть `rollMatch`, не абсолютный euler `roll=72°` в BEFORE.

---

## 3. Как читать поля лога

Формат: запятая — десятичный разделитель локали (`42,0` = 42.0).

### Строка BEFORE / AFTER

| Поле | Смысл |
|------|--------|
| `pose` | Должно быть `Aiming` |
| `target` | Должно быть `none` |
| `profile` | `StandingIdle` или `CrouchIdle` |
| `lean01` | Запрос: −1 / 0 / +1 |
| `leanDeg` | Сглаженный угол, **+ вправо**, уже с множителем 1.18 справа |
| `blocked` / `settled` | Блок и «дошли до цели» |
| `rootYaw` | Yaw корня. В этом прогоне 0° — юнит смотрит в +Z мира |
| `s1` / `s2` | `Spine_01` / `Spine_02`: world pos, `localX` вдоль right юнита, pitch/yaw/roll в системе тела |
| `barrel pos` / `fwd` | Мировая точка ствола и его forward |
| `barrel localX` | Проекция ствола на right юнита. Оружие справа → в нейтрали **уже > 0** |
| `barrel yaw` / `pitch` | Ствол относительно тела: yaw + вправо, pitch + вверх |

### Дельты (только AFTER)

Знак дельты: **минус = влево / вниз**, плюс = вправо / вверх.  
`barrelAlong` в VERDICT уже умножен на знак lean: должен быть **положительным**.

| Поле | Норма в этом прогоне |
|------|----------------------|
| `Δs1Pos` | Почти ноль: нижняя спина почти не едет, только крутится |
| `Δs2Pos` X | Небольшой сдвиг в сторону lean (~2–4 см) |
| `Δs1LocalX` | ~0 |
| `Δs2LocalX` | Тот же знак, что lean |
| `ΔbarrelLocalX` | Главный peek: десятки сантиметров в сторону lean |
| `ΔbarrelYaw` / `ΔbarrelPitch` | Около нуля: ствол не развернулся |
| `Δs1Roll` / `Δs2Roll` | Знак совпадает с lean; модуль S2 больше S1 (вес 0.75) |
| `rollMatch angle` | Должен совпасть с `\|leanDeg\|` |

### Абсолютные euler костей — не угол наклона

В нейтрали Standing `s1 yaw=103° roll=72°` — это rest-поза скелета в системе тела, не «юнит уже наклонён на 72°». Наклон = **дельта** и `rollMatch angle`.

---

## 4. Разбор клеток

Общее для всех четырёх:

- `blocked=False`, `settled=True`, `target=none`, `pose=Aiming`
- `rootShift=0.000 m`
- `lookStraight dot=1.00` — ствол смотрит туда же, куда тело
- `rollMatch angle` **точно равен** `\|leanDeg\|`

### Standing / Aiming — нейтраль

```
profile=StandingIdle lean01=0 leanDeg=0
s1 pos=(26.998, 1.577, −0.082) localX=−0.001 m
s2 pos=(27.009, 1.750, −0.030) localX=+0.010 m
barrel pos=(27.119, 2.027, 0.922) fwd=(0.00, 0.01, 1.00)
barrel localX=+0.120 m  yaw=0.0°  pitch=0.7°
```

Ствол уже на **+12 см** справа от корня (оружие в правой руке). Дальнейшие смещения считаются от этой базы, не от нуля. Forward почти (0,0,1) при `rootYaw=0` — взгляд прямо.

### Standing Left

```
lean01=−1  leanDeg=−42.0 / 42.0
Δs1Roll=−7.8°   Δs2Roll=−53.1°   rollMatch=42.0°
Δs2LocalX=−0.032 m
ΔbarrelLocalX=−0.244 m   barrelAlong=0.244 ≥ 0.16
barrel yaw −0.4°  pitch 0.4°
barrel pos X: 27.119 → 26.875  (уехал влево)
```

Профиль 42° выбран полностью. Ствол ушёл влево на 24.4 см, направление почти не изменилось. `Spine_01` origin не сдвинулся — так и задумано.

### Standing Right

```
lean01=+1  leanDeg=+49.6 / 49.6     (= 42 × 1.18)
Δs1Roll=+9.2°   Δs2Roll=+62.4°   rollMatch=49.6°
Δs2LocalX=+0.037 m
ΔbarrelLocalX=+0.208 m   barrelAlong=0.208 ≥ 0.16
barrel yaw +0.5°  pitch 0.4°
barrel Y: 2.027 → 1.839  (правое плечо с оружием опустилось)
```

49.6° — не ошибка и не «перекрутили», это штатный `RightLeanScale`. Ствол ушёл вправо меньше, чем влево (20.8 против 24.4 см): база уже была +12 см справа, плюс геометрия roll. Оба значения выше порога 16 см.

Высота ствола падает только на **правом** lean: оружие на правом плече, наклон вправо опускает правую сторону. На левом lean высота почти не меняется (2.027 → 2.028). Это геометрия, не баг.

### Crouch / Aiming — нейтраль

```
profile=CrouchIdle lean01=0 leanDeg=0
s1 pos=(27.016, 1.094, −0.182)     высота спины ниже стоячего (1.577)
barrel pos=(27.056, 1.411, 0.967)
barrel localX=+0.056 m  yaw=0.1°  pitch=−0.6°
```

Присед ниже, оружие ближе к оси тела (+5.6 см вместо +12 см). Взгляд прямо. Строки `[CrouchXfade]` между Standing END и этим BEFORE — смена клипа `RifleCrouch_Idle_Ready`, не lean.

### Crouch Left

```
lean01=−1  leanDeg=−38.0 / 38.0
Δs1Roll=−10.2°  Δs2Roll=−46.0°  rollMatch=38.0°
ΔbarrelLocalX=−0.143 m   barrelAlong=0.143 ≥ 0.10
```

Профиль 38° выбран полностью. Смещение ствола меньше, чем стоя (14.3 vs 24.4 см) — слабее угол и ниже стойка. Порог приседа 10 см выполнен.

### Crouch Right

```
lean01=+1  leanDeg=+44.8 / 44.8     (= 38 × 1.18)
Δs1Roll=+11.9°  Δs2Roll=+51.4°  rollMatch=44.8°
ΔbarrelLocalX=+0.137 m   barrelAlong=0.137 ≥ 0.10
barrel Y: 1.411 → 1.327
```

Та же картина, что стоя справа: сильнее угол, чуть меньше боковой уход ствола, чем слева, ствол чуть опускается.

---

## 5. Сводка «что считать нормой / что чинить»

Норма (этот прогон так и выглядит):

- `leanDeg` слева = MaxLeanDegrees профиля, справа = Max × 1.18
- `rollMatch angle` = `|leanDeg|`
- `rootShift = 0`
- `target=none`, `pose=Aiming`, `blocked=False`
- `|ΔbarrelYaw|` и `|pitch|` доли градуса
- `Δs1Pos ≈ 0`, `Δs2` несколько сантиметров в сторону lean
- Левый уход ствола ≥ правого при той же стойке

Чинить, если в будущем прогоне:

- `FAIL` / `BLOCKED` / `поза ≠ Aiming` / `цель появилась`
- `leanDeg` заметно меньше профиля (не дошёл)
- `ствол не ушёл в сторону` или ушёл **против** lean
- `ствол не смотрит вперёд` (dot низкий, yaw десятки градусов) — сломан hold/aim, не обязательно MaxLeanDegrees
- `корень шагнул` — в lean затесался стрейф/nav
- `спина почти не накренилась` — кости не резолвятся или LateUpdate не применяется
- `rollMatch` сильно расходится с `leanDeg` (> 12°) — вес костей или ось roll

Не чинить:

- Абсолютные `s1 yaw≈100° roll≈70°` в нейтрали
- `|Δs2Roll|` > `leanDeg` при совпадающем `rollMatch`
- Правый `leanDeg` 49.6 / 44.8
- Правый ствол ниже левого
- Левый `barrelAlong` больше правого
- `[CrouchXfade]`

---

## 6. Сырые логи прогона (очищено от стека Unity)

```
[SpineLeanDiag] START unit=Unit(Clone) pose=Aiming target=none look=straight filter=SpineLeanDiag

[SpineLeanDiag] CELL Standing/Aiming BEGIN

[SpineLeanDiag] BEFORE Standing/Aiming Off pose=Aiming target=none profile=StandingIdle lean01=0,00 leanDeg=0,0 blocked=False settled=True rootYaw=0,0° s1 pos=(26,998,1,577,-0,082) localX=-0,001m pitch=0,4° yaw=103,1° roll=72,4° s2 pos=(27,009,1,750,-0,030) localX=0,010m pitch=-1,7° yaw=-71,4° roll=97,2° barrel pos=(27,119,2,027,0,922) fwd=(0,00,0,01,1,00) localX=0,120m yaw=0,0° pitch=0,7°

[SpineLeanDiag] AFTER Standing/Aiming Left pose=Aiming target=none profile=StandingIdle lean01=-1,00 leanDeg=-42,0 blocked=False settled=True rootYaw=0,0° s1 pos=(26,998,1,577,-0,082) localX=-0,002m pitch=10,6° yaw=103,3° roll=64,6° s2 pos=(26,977,1,750,-0,030) localX=-0,022m pitch=-41,0° yaw=-65,1° roll=44,2° barrel pos=(26,875,2,028,0,922) fwd=(-0,01,0,01,1,00) localX=-0,124m yaw=-0,4° pitch=0,4° Δs1Roll=-7,8° Δs2Roll=-53,1° Δs1Pos=(0,000,0,000,0,000) Δs2Pos=(-0,032,-0,001,0,000) Δs1LocalX=0,000m Δs2LocalX=-0,032m ΔbarrelLocalX=-0,244m ΔbarrelYaw=-0,4° ΔbarrelPitch=-0,3°

[SpineLeanDiag] VERDICT Standing/Aiming Left PASS OK rootShift=0,000m; spineRoll S1=-7,8° S2=-53,1° alongS2=53,1°; rollMatch angle=42,0° leanDeg=42,0°; barrelAlong=0,244m ≥ 0,16m; barrelΔyaw=-0,4°; barrelPitch=0,4°; lookStraight dot=1,00; leanDeg=-42,0/42,0; spine02Along=0,032m s1Along=7,8°

[SpineLeanDiag] AFTER Standing/Aiming Right pose=Aiming target=none profile=StandingIdle lean01=1,00 leanDeg=49,6 blocked=False settled=True rootYaw=0,0° s1 pos=(26,998,1,577,-0,082) localX=-0,001m pitch=-11,6° yaw=103,4° roll=81,6° s2 pos=(27,046,1,744,-0,030) localX=0,046m pitch=44,5° yaw=-63,5° roll=159,6° barrel pos=(27,327,1,839,0,922) fwd=(0,01,0,01,1,00) localX=0,328m yaw=0,5° pitch=0,4° Δs1Roll=9,2° Δs2Roll=62,4° Δs1Pos=(0,000,0,000,0,000) Δs2Pos=(0,037,-0,006,0,000) Δs1LocalX=0,000m Δs2LocalX=0,037m ΔbarrelLocalX=0,208m ΔbarrelYaw=0,5° ΔbarrelPitch=-0,3°

[SpineLeanDiag] VERDICT Standing/Aiming Right PASS OK rootShift=0,000m; spineRoll S1=9,2° S2=62,4° alongS2=62,4°; rollMatch angle=49,6° leanDeg=49,6°; barrelAlong=0,208m ≥ 0,16m; barrelΔyaw=0,5°; barrelPitch=0,4°; lookStraight dot=1,00; leanDeg=49,6/49,6; spine02Along=0,037m s1Along=9,2°

[SpineLeanDiag] CELL Standing/Aiming END

[SpineLeanDiag] CELL Crouch/Aiming BEGIN

[SpineLeanDiag] BEFORE Crouch/Aiming Off pose=Aiming target=none profile=CrouchIdle lean01=0,00 leanDeg=0,0 blocked=False settled=True rootYaw=0,0° s1 pos=(27,016,1,094,-0,182) localX=0,017m pitch=1,7° yaw=86,9° roll=51,7° s2 pos=(27,006,1,239,-0,074) localX=0,007m pitch=-4,6° yaw=-92,3° roll=117,4° barrel pos=(27,056,1,411,0,967) fwd=(0,00,-0,01,1,00) localX=0,056m yaw=0,1° pitch=-0,6°

[SpineLeanDiag] AFTER Crouch/Aiming Left pose=Aiming target=none profile=CrouchIdle lean01=-1,00 leanDeg=-38,0 blocked=False settled=True rootYaw=0,0° s1 pos=(27,016,1,093,-0,182) localX=0,017m pitch=11,2° yaw=86,9° roll=41,5° s2 pos=(26,982,1,235,-0,074) localX=-0,017m pitch=-42,6° yaw=-93,1° roll=71,4° barrel pos=(26,913,1,404,0,967) fwd=(0,01,-0,01,1,00) localX=-0,086m yaw=0,3° pitch=-0,3° Δs1Roll=-10,2° Δs2Roll=-46,0° Δs1Pos=(0,000,0,000,0,000) Δs2Pos=(-0,024,-0,004,0,000) Δs1LocalX=0,000m Δs2LocalX=-0,024m ΔbarrelLocalX=-0,143m ΔbarrelYaw=0,3° ΔbarrelPitch=0,3°

[SpineLeanDiag] VERDICT Crouch/Aiming Left PASS OK rootShift=0,000m; spineRoll S1=-10,2° S2=-46,0° alongS2=46,0°; rollMatch angle=38,0° leanDeg=38,0°; barrelAlong=0,143m ≥ 0,10m; barrelΔyaw=0,3°; barrelPitch=-0,3°; lookStraight dot=1,00; leanDeg=-38,0/38,0; spine02Along=0,024m s1Along=10,2°

[SpineLeanDiag] AFTER Crouch/Aiming Right pose=Aiming target=none profile=CrouchIdle lean01=1,00 leanDeg=44,8 blocked=False settled=True rootYaw=0,0° s1 pos=(27,016,1,094,-0,182) localX=0,017m pitch=-9,5° yaw=86,9° roll=63,5° s2 pos=(27,035,1,238,-0,074) localX=0,035m pitch=40,1° yaw=-93,0° roll=168,7° barrel pos=(27,193,1,327,0,967) fwd=(-0,01,-0,01,1,00) localX=0,193m yaw=-0,3° pitch=-0,4° Δs1Roll=11,9° Δs2Roll=51,4° Δs1Pos=(0,000,0,000,0,000) Δs2Pos=(0,029,-0,001,0,000) Δs1LocalX=0,000m Δs2LocalX=0,029m ΔbarrelLocalX=0,137m ΔbarrelYaw=-0,4° ΔbarrelPitch=0,3°

[SpineLeanDiag] VERDICT Crouch/Aiming Right PASS OK rootShift=0,000m; spineRoll S1=11,9° S2=51,4° alongS2=51,4°; rollMatch angle=44,8° leanDeg=44,8°; barrelAlong=0,137m ≥ 0,10m; barrelΔyaw=-0,4°; barrelPitch=-0,4°; lookStraight dot=1,00; leanDeg=44,8/44,8; spine02Along=0,029m s1Along=11,9°

[SpineLeanDiag] CELL Crouch/Aiming END

[SpineLeanDiag] SUMMARY unit=Unit(Clone) cells=4
  PASS Standing/Aiming Left  OK rootShift=0,000m; spineRoll S1=-7,8° S2=-53,1° alongS2=53,1°; rollMatch angle=42,0° leanDeg=42,0°; barrelAlong=0,244m ≥ 0,16m; barrelΔyaw=-0,4°; barrelPitch=0,4°; lookStraight dot=1,00; leanDeg=-42,0/42,0; spine02Along=0,032m s1Along=7,8°
  PASS Standing/Aiming Right OK rootShift=0,000m; spineRoll S1=9,2° S2=62,4° alongS2=62,4°; rollMatch angle=49,6° leanDeg=49,6°; barrelAlong=0,208m ≥ 0,16m; barrelΔyaw=0,5°; barrelPitch=0,4°; lookStraight dot=1,00; leanDeg=49,6/49,6; spine02Along=0,037m s1Along=9,2°
  PASS Crouch/Aiming Left    OK rootShift=0,000m; spineRoll S1=-10,2° S2=-46,0° alongS2=46,0°; rollMatch angle=38,0° leanDeg=38,0°; barrelAlong=0,143m ≥ 0,10m; barrelΔyaw=0,3°; barrelPitch=-0,3°; lookStraight dot=1,00; leanDeg=-38,0/38,0; spine02Along=0,024m s1Along=10,2°
  PASS Crouch/Aiming Right   OK rootShift=0,000m; spineRoll S1=11,9° S2=51,4° alongS2=51,4°; rollMatch angle=44,8° leanDeg=44,8°; barrelAlong=0,137m ≥ 0,10m; barrelΔyaw=-0,4°; barrelPitch=-0,4°; lookStraight dot=1,00; leanDeg=44,8/44,8; spine02Along=0,029m s1Along=11,9°
[SpineLeanDiag] SUMMARY totals PASS=4 FAIL=0

[SpineLeanDiag] DONE unit=Unit(Clone)
```

Игнорируемые соседние строки того же Play Mode (не часть lean-диагностики):

```
[CrouchXfade] unit=Unit(Clone) src=graph kind=graph-AnyState … → RifleCrouch_Idle_Ready …
[CrouchXfade] unit=Unit(Clone) src=enter RifleCrouch_Idle_Ready …
[CrouchXfade] unit=Unit(Clone) … RifleCrouch_Idle_Ready → … pending=replay→Stand_Aim_Idle …
```

---

## 7. Одна фраза на вывод

В Aiming без цели юнит стоя и сидя полностью добирает профильный roll спины, ствол уходит в сторону peek и не теряет направление вперёд, корень на месте — система наклона на этом прогоне исправна.
