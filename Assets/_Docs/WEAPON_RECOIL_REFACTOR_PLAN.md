# План: перераспределение визуальной отдачи (back-first recoil)

> Модернизация существующей системы `UnitWeaponRecoil` — не новая система.
> База: `_Docs/WEAPON_FIRE_VISUAL_FEEDBACK_SYSTEM.md` (разделы 6, 15, 16 — с захваченными логами прогона).
> Идея одним предложением: перестать использовать pitch как источник линейного recoil —
> ввести единый визуальный `impulse`, который честно раскладывается на back (главный), up (малый) и pitch (малый).

---

## 1. Принципы (что трогаем, а что нет)

### Не трогаем вообще
- `UnitWeaponRecoilController`, `RecoilPenalty`, `RecoilPerShot`, `AutoRecoilMultiplier`,
  `RecoilRecoveryPerSecond`, spread и всю fire-логику — это gameplay-канал. Проблема визуальная,
  лечим только визуальный канал.
- Архитектуру конвейера: `ShotFired → UnitWeaponRecoil → WeaponVisualRecoilApplicator → Hand_R → Weapon → Left IK`.
- `WeaponVisualRecoilState` — поля остаются: `punchPitch, punchYaw, climbPitch, backOffset, upOffset, isActive`.
- `VisualRecoilKickScale` — остаётся общим визуальным множителем (Rifle 1.0 / Heavy 1.2 / Pistol 0.8 — при необходимости настраивается в ассетах, не кодом).
- Правило «climb → только rotation; punch → rotation + translation» — сохранить (иначе длинная очередь даст постоянное смещение оружия назад — эффект «вытягивания рук»).
- Затухание (`ShotSmoothTime = 0.08`, `DecayWhileFiringMultiplier = 1.75`, tau в очереди = 0.14 с) — пока не трогать.

### Не добавляем
- Отдельный recoil-контроллер / recoil-кость / Rigidbody на оружие / state machine / физику оружия.
- `AnimationCurve` для Back / Up / Pitch; профили «стойка/оружие» (`if (isCrouching)`, `if (isRifle)` и т.п.).
- Зависимость recoil от скорости юнита через новые условия.
- Случайный шум на back/up/pitch. Случайность остаётся только в yaw (Perlin + bias), как сейчас.
- Новые Update/LateUpdate циклы; отдельное движение `WeaponRoot` (всё по-прежнему через `Hand_R`).

---

## 2. Код-изменения (файл `Assets/_Scripts/Shooting/UnitWeaponRecoil.cs`)

### 2.1 Единый импульс вместо pitch-импульса
Сейчас одна величина `m_ShotImpulsePitch` используется сразу для трёх эффектов:
`backOffset = punchPitch * BackScale * HandBack` и `upOffset = punchPitch * UpScale * HandUp`,
а `punchPitch` одновременно является вращением. Переходим на один общий импульс:

```text
recoilImpulse
      │
      ├── pitch  (rotation, малый)
      ├── back   (translation, главный)
      └── up     (translation, малый)
```

- Поле `m_ShotImpulsePitch` → `m_ShotImpulse` (единый накопленный импульс).
- Поле `m_MaxShotImpulsePitch` → `m_MaxShotImpulse` (это уже не pitch — переименовать честно).
- **Разделить капы по единицам измерения**: `m_MaxShotImpulse = 6f` (кап визуальной силы)
  и отдельный `m_MaxShotYawDegrees = 6f` (кап yaw в градусах) — иначе смена капа импульса
  неявно поменяет боковой увод.
- `m_ShotImpulseYaw` остаётся как есть (боковой канал с Perlin-шумом).

### 2.2 `HandleShotFired` (стало)

```csharp
float recoilPerShot = m_RecoilController != null
    ? m_RecoilController.ComputeRecoilAddedPerShot(_ammoDefinition)
    : 1f;
float kickScale = ResolveVisualRecoilKickScale();
float impulse = recoilPerShot * kickScale;

m_ShotImpulse = Mathf.Min(m_ShotImpulse + impulse, m_MaxShotImpulse);

float shotPitch = impulse * m_ShotPitch;          // для yaw сохраняем пропорцию к pitch
float yawNoise = Mathf.PerlinNoise(m_YawSeed, m_ShotIndex * 0.73f) * 2f - 1f;
float yawDir = Mathf.Clamp(m_YawBias + yawNoise * (1f - Mathf.Abs(m_YawBias)), -1f, 1f);
m_ShotImpulseYaw += yawDir * shotPitch * m_ShotYawScale;
m_ShotImpulseYaw = Mathf.Clamp(m_ShotImpulseYaw, -m_MaxShotYawDegrees, m_MaxShotYawDegrees);
m_ShotIndex++;
RebuildCurrentState();
```

### 2.3 `RebuildCurrentState` (стало)

```csharp
float kickScale = ResolveVisualRecoilKickScale();
float penalty = m_RecoilController != null ? m_RecoilController.RecoilPenalty : 0f;
float climbPitch = m_PitchCurve.Evaluate(penalty) * m_VisualOffsetScale * kickScale;

float impulse = m_ShotImpulse;
float punchPitch = impulse * m_ShotPitch;
float punchYaw   = m_ShotImpulseYaw;
float backOffset = impulse * m_BackScale * m_HandBack;
float upOffset   = impulse * m_UpScale * m_HandUp;

// isActive и сборка WeaponVisualRecoilState — без изменений
```

### 2.4 `Update` (затухание)
`m_ShotImpulsePitch` заменяется на `m_ShotImpulse` в той же формуле:
`impulse *= exp(-deltaTime / tau)`; порог среза < 0.001. Логика tau не меняется.

### 2.5 Диагностика
Геттеры в `UnitWeaponRecoil` (`ShotPitchDegrees`, `BackScale`, ... и пр.) остаются —
`UnitWeaponPoseSweepTest` использует `ShotSmoothTime`/`DecayWhileFiringMultiplier` для tau,
остальное логируется через `WeaponVisualRecoilState` (поля не изменились). Логика логов не меняется.

---

## 3. Начальная калибровка (первый тест)

| Параметр | Сейчас | Первый тест | Комментарий |
|---|---:|---:|---|
| `m_ShotPitch` | 3.75° | **2.5°** (диапазон 2.3–2.6) | pitch — малый |
| `m_BackScale` | 0.008 | **0.035** (0.032–0.038) | назад — главный; НЕ ниже ~0.03, иначе назад станет слабее текущего |
| `m_UpScale` | 0.0035 | **0.008** (диапазон 0.006–0.009) | вверх — вторичный, но заметный; старт сразу в целевом диапазоне |
| `m_HandPitch` | 1.0 | **0.80** (не ниже 0.75 до проверки) | ослабить вращение |
| `m_HandBack` | 1.0 | 1.0 | |
| `m_HandUp` | 0.85 | **0.75** | |
| `m_ShotYawScale` | 0.30 | 0.30 | сначала не менять |
| `m_YawBias` | 0.45 | 0.45 | сначала не менять |
| `m_ShotSmoothTime` | 0.08 | 0.08 | пока НЕ трогать |
| `m_DecayWhileFiringMultiplier` | 1.75 | 1.75 | не трогать |
| `m_MaxShotImpulsePitch` → `m_MaxShotImpulse` | 8° | **6.0** | страховочный кап; отдельно не настраивать, пока тест не покажет проблему |
| `m_MaxShotYawDegrees` (новый) | — | **6.0°** | отдельный кап yaw в градусах, независим от капа импульса |
| `m_PitchCurve` | 0→0°, 60→7° | **0→0°, 15→~0.6°, 30→~1.7°, 60→4.0–4.5°** | первые выстрелы почти не поднимают ствол, очередь уводит выше постепенно |

Про кап: при `impulse per shot = 0.72` и затухании последовательность накопления примерно
`0.72 → 1.2–1.3 → 1.7–1.9 → 2.2–2.4 ...`, поэтому `MaxShotImpulse = 6` в обычной очереди
практически недостижим — это просто страховка от «улетающей» очереди.

### Ожидаемые числа после калибровки (стоя, added=0.600, kickScale=1.20 → impulse=0.72)
| Величина | Было (лог §16) | Станет (расчёт) | Целевой диапазон |
|---|---:|---:|---|
| `punchPitch` | 2.699° | 0.72 × 2.5 = **1.80°** | 1.7–2.2° |
| `backOffset` | 0.0216 м | 0.72 × 0.035 × 1.0 = **0.0252 м (25 мм)** | 20–30 мм |
| `upOffset` | 0.0080 м | 0.72 × 0.008 × 0.75 = **0.0043 м (4.3 мм)** | 3–5 мм |
| соотношение back/up | 2.7 : 1 | ≈ **5.8 : 1** | ≈ 4–6 : 1 (ориентир) |
| `Hand_R Δrot` | 2.706° | pitch-компонента ≈ **1.4°** (1.8° × HandPitch 0.8), суммарно с yaw ≈ **1.5–2.2°** | ориентир, НЕ жёсткий критерий |

Важно:
- Назад не должен стать слабее текущего (21.6 мм): цель — не огромный backward, а доминирующий
  относительно pitch/up. Не поднимать `BackScale` к 0.05–0.06: оружие — ребёнок `Hand_R`,
  слишком большой translation визуально «отрывает» оружие от рук.
- Соотношение 4–6 — ориентир, а не магическое число: у разных оружий/поз оно может слегка
  отличаться. Если после визуального теста вверх всё ещё мал — `UpScale` 0.009–0.010 или
  `HandUp` 0.8–1.0.
- `Δrot` в диагностике включает и pitch, и yaw — как приёмочный критерий используем
  pitch-компоненту (`punchPitch` в логе), а `Δrot` смотрим только как контекст.

---

## 4. Порядок внедрения (шаги)

1. Не трогать `RecoilPenalty`, spread и fire-логику.
2. В `UnitWeaponRecoil` ввести единый `m_ShotImpulse` (п. 2.1–2.4).
3. Переименовать `MaxShotImpulsePitch` → `MaxShotImpulse` (и публичный геттер).
4. Поставить значения из таблицы п. 3 (ShotPitch 2.5, BackScale 0.035, UpScale 0.008,
   HandPitch 0.80, HandUp 0.75, MaxShotImpulse 6.0, PitchCurve 0/15/30/60 = 0/0.6/1.7/4.5°).
5. Прогнать x1 (Standing/Idle/Aiming, Crouch/Idle/Aiming, Standing/Walk/Aiming).
6. Прогнать x3.
7. Прогнать x10.
8. Прогнать полную матрицу L-прогона (48 клеток).
9. Только после этого — при необходимости подправить `ShotSmoothTime`.
10. Только если нужно — подправить `ShotYawScale` (вторая стадия).

---

## 5. Тестирование (существующая диагностика, без изменений)

Запуск: выбрать юнит → клавиша **L** → матрица Standing/Crouch × Idle/Walk ×
LowReady/HighReady/PreAim × HipFire/PointAim/Aiming × 1/3/10 выстрелов.
Фильтры консоли: `WeaponVisDiag` (поза оружия на выстрел), `HeadSweep` (голова), `RecoilSweep` (выкл).

### Этап 1 — одиночный выстрел (x1)
Клетки:
- Standing/Idle/HipFire x1, Standing/Idle/PointAim x1, Standing/Idle/Aiming x1 — обязательны все
  три: recoil накладывается поверх РАЗНЫХ базовых поз, качество импульса должно быть одинаковым;
- дополнительно Crouch/Idle/Aiming x1 и Standing/Walk/Aiming x1.
Смотреть `visualState` и `Hand_R`:
- численные цели: back 20–30 мм, up 3–5 мм, punchPitch 1.7–2.2°, соотношение back/up ≈ 4–6;
- визуально (главный критерий): глаз должен видеть «оружие толкнуло назад», а не
  «ствол резко задрался вверх»;
- последовательность: толчок назад → лёгкий подъём → плавный возврат.

### Этап 2 — три выстрела (x3)
- Каждый выстрел — новый импульс назад; `climb` лишь постепенно добавляет угол;
- 2-й выстрел не должен превращать оружие в огромный pitch (в новой калибровке
  `punchPitch` ~1.8 → ~2.6 → ~3.1°, страховочный кап 6.0 практически недостижим).

### Этап 3 — десять выстрелов (x10)
Три проверки:
- нет накопления назад: `backOffset` возвращается к нулю после очереди (импульс, а не displacement);
- нет чрезмерного pitch: ствол приподнят, но юнит «держит оружие»;
- нет «пружины»: возврат smooth, без `kick → snap → kick → snap`.

### Этап 4 — полная матрица (48 клеток)
Позы не должны выглядеть как разные recoil-системы. Сравнивать с бейзлайном из §16
документа описания (старый прогон, 168 выстрелов).

### Как читать логи
- Главное: `visualState` (`punchPitch/punchYaw/backOffset/upOffset`) и `Hand_R` (`Δrot`, `Δpos`) —
  разница `base → final` и есть чистый recoil.
- Не делать выводы по одному `WeaponRoot Δpos`: туда попадает движение юнита и смена базовой
  позы (зафиксировано в §16.1: в PointAim x10 большие дельты base — доворот в движении, не recoil).

---

## 6. Критерии приёмки

- **Числа — фильтр, визуальное ощущение — критерий приёмки.** Одинаковые `backOffset/upOffset`
  в разных позах (HipFire/PointAim/Aiming), при разной длине оружия и угле камеры ощущаются
  по-разному — решение принимаем глазами.
- Одиночный (числовой фильтр): back 20–30 мм, up 3–5 мм, punchPitch 1.7–2.2°,
  back/up ≈ 4–6 (ориентир). `Hand_R Δrot` жёстким критерием НЕ является (в нём pitch + yaw).
- Одиночный (визуально, решающий): «оружие толкнуло назад», а не «ствол задрался вверх».
- Очередь: назад повторяется импульсно, без накопления смещения.
- Накопление: `climb` ощущается, но не доминирует.
- Возврат: без резкого snap.
- Позы Standing/Crouch/Walk/Aiming/HipFire/PointAim не выглядят как разные системы.
- Gameplay не изменился: spread тот же, `RecoilPenalty` та же, RPM тот же, урон тот же.

---

## 7. Чего не делать (стоп-лист)

- `RecoilStateMachine`, отдельные кривые Back/Up/Pitch, профили на стойку/оружие.
- `if (isCrouching / isWalking / isRifle / isPistol)` в основном recoil.
- Физика оружия, Rigidbody, отдельная recoil-кость, второй контроллер.
- Новые Update/LateUpdate циклы; движение `WeaponRoot` напрямую.
- Менять `RecoilPerShot / AutoRecoilMultiplier / RecoilRecoveryPerSecond / spread`.
- Менять `UnitWeaponRecoilController` и `VisualRecoilKickScale`-канал.

---

## 8. Итоговая структура после рефактора

```text
                 ShotFired
                    │
          ┌─────────┴─────────┐
          ↓                   ↓
 RecoilController       UnitWeaponRecoil
          │                   │
 RecoilPenalty           ShotImpulse
          │                   │
          ↓            ┌──────┼──────┐
 Gameplay Spread       ↓      ↓      ↓
                    Pitch   Back     Up
                       \      |      /
                        WeaponVisualState
                                │
                                ↓
                         Hand_R Overlay
                                │
                                ↓
                             Weapon
                                │
                                ↓
                            Left IK
```

---

## Этап 2. Исправление координатного пространства translation (по прогону §18)

> Статус: **реализовано в коде** (18.08.2026). Описание live-системы: `_Docs/WEAPON_FIRE_VISUAL_FEEDBACK_SYSTEM.md`.
> Осталось вне кода: L-прогон после правки пространства и сверка `backProj`/`upProj` (критерии ниже).

### Диагноз
Численно recoil уже back-first (прогон Б: back=21,3 мм, up=3,6 мм, back/up ≈ 6:1),
но визуально отдача всё ещё читается как «ствол вверх». Причина — НЕ величина коэффициентов,
а пространство, в котором применяется translation:

```csharp
// WeaponVisualRecoilApplicator (было до этапа 2; в коде этого пути больше нет)
Vector3 punchLocal = m_Recoil.BuildHandLocalPunch();   // (0, up, -back) — оси Hand_R
Vector3 finalPos = basePos + baseRot * punchLocal;     // поворот осями кисти
```

`Hand_R` — кость руки; её локальные оси НЕ совпадают с продольной осью оружия.
Поэтому `-Z кисти`, повёрнутый `localRotation` кисти, не является «назад вдоль ствола» —
часть вектора уходит вверх/вбок. Подтверждение из лога прогона Б (Aiming x1):

```text
visualState: back=0,0213м up=0,0036м
Hand_R Δpos = (0,0197, -0,0014, -0,0087)  — не (0, 0,0036, -0,0213)
```

### Решение
Строить translation в **world-пространстве от реальной геометрии оружия**, затем
переводить в parent-space кисти:

```text
back → строго вдоль -FireOriginTransform.forward (реальный ствол / MuzzleExit)
up   → мировая вертикаль Vector3.up (НЕ weapon.up — roll оружия не должен уводить recoil вбок)
pitch/yaw/climb → без изменений (rotation-оверлей Hand_R, как сейчас)
```

### Изменения кода

1. `UnitWeaponRecoil` — заменить `BuildHandLocalPunch()` на:

```csharp
public Vector3 BuildHandParentSpaceTranslation(Transform hand)
{
    Transform fireOrigin = ResolveFireOriginTransform();

    if (fireOrigin == null || hand == null || hand.parent == null)
        return Vector3.zero;

    Vector3 worldDelta =
        Vector3.up * m_CurrentState.upOffset -
        fireOrigin.forward * m_CurrentState.backOffset;

    return hand.parent.InverseTransformVector(worldDelta);
}

private Transform ResolveFireOriginTransform()
{
    EquippedWeapon weapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
    if (weapon != null && weapon.FireOriginTransform != null)
        return weapon.FireOriginTransform;
    return m_Equipment != null ? m_Equipment.MainWeaponRoot : null;
}
```

2. `WeaponVisualRecoilApplicator.LateUpdate` — translation применять без умножения на `baseRot`:

```csharp
Quaternion recoilRot = m_Recoil.BuildHandRotationOffset();
Vector3 punchParent = m_Recoil.BuildHandParentSpaceTranslation(hand);

Quaternion baseRot = hand.localRotation;
Vector3 basePos = hand.localPosition;
Quaternion finalRot = baseRot * recoilRot;
Vector3 finalPos = basePos + punchParent;   // без baseRot * punchLocal
```

Rotation-канал не трогаем: `finalRot = baseRot * recoilRot` остаётся.

### Что НЕ трогать
- Калибровку: ShotPitch 2.5, BackScale 0.035, UpScale 0.008, HandPitch 0.8, HandBack 1.0,
  HandUp 0.75, PitchCurve 0/0.6/1.7/4.5, ShotSmoothTime 0.08, DecayWhileFiringMultiplier 1.75,
  YawBias 0.45, ShotYawScale 0.3, MaxShotImpulse 6, MaxShotYawDegrees 6.
- `RecoilPenalty`, миграцию, `VisualRecoilKickScale`, диагностику.
- Предложение прошлой итерации «ShotPitch 1.2–1.5 / HandPitch 0.5–0.6» отложить в резерв:
  применить только если после этой правки вращение всё ещё будет мешать.

### Почему не «ещё больше BackScale»
Коэффициенты уже дают правильное соотношение 6:1 — усиление back лечило бы не ту проблему
и увеличивало бы риск «отрыва» оружия от рук на АК (impulse ≈ 1,2).

### Проверка после правки (L-прогон, фильтр WeaponVisDiag)
- `visualState.back/up` не меняются (расчёт не тронут).
- В лог добавлены проекции дула (`backProj`/`upProj`) — измеряем **проекциями**, а не общим Δpos:

  ```csharp
  Vector3 delta = muzzlePost - muzzlePre;
  float backMeasured = Vector3.Dot(delta, -fireOriginPreForward);
  float upMeasured   = Vector3.Dot(delta, Vector3.up);
  ```

  `|Δpos|` сам по себе не говорит о направлении — только проекции дают проверяемый критерий.
- `Hand_R Δpos` теперь направлен преимущественно вдоль `-FireOrigin.forward`.
- `Δrot` не изменился (rotation-канал нетронут).

### Критерии приёмки
- Визуально: оружие толкается НАЗАД вдоль ствола, чуть вверх; ствол не «задирается».
- Численно (без жёсткого фиксированного диапазона — при наклоне ствола мировая вертикаль
  меняет проекцию на −forward, поэтому сравниваем с рассчитанным, а не с константой):
  `backProj ≈ backOffset` (в `visualState`), `upProj ≈ upOffset`.
  Для справки на 556 в прогоне Б: back ≈ 21 мм, up ≈ 3–4 мм, pitch ≈ 1,5–2°.
- Edge cases: `hand.parent == null` или `FireOriginTransform == null` → translation не применяется
  (rotation работает как раньше); турель/болт-холд/ragdoll уже отсечены `ShouldApplyOverlayThisFrame`.
- Очередь: back остаётся импульсным, без накопления смещения (затухание не тронуто).
