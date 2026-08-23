# Vision Stage 10 — каталог Accuracy / AimTime (150/300)

**Статус: CLOSED / VERIFIED** (2026-08-22 21:35:32). `Tools/Tests/Run Accuracy Aim Curve Contract (Play)` → `Assets/_Docs/Logs/Tests/AccuracyAimCurveContract_LAST.txt`. **PASS 11/0**.  
**Это A1+A2** карты `Пехота_дорожная_карта.md`. Не Attention (этап B). Дисциплина огня — **A3 / Stage 11 CLOSED / VERIFIED PASS 21/0**. Не отдача (A10).  
**Не трогали:** Q, DistanceCurve, Acquire/Lose, FOV, Exposure, Movement, `VisionRange`, `ScopeVisionRange`, E стволов/патронов, Range× оптики, BaseDamage, recoil, burst-by-shot, Cone, Threat, Memory, Identity, hitscan envelope.

Источник: `Tools/accuracy_aim_curves_catalog.py` → `python Tools/bake_accuracy_aim_curves.py` (18 оптик + 26 стволов).  
C# fallback: `OpticDistanceCurveLibrary`, `WeaponDistanceCurveLibrary` (clamp 300).  
Excel после bake: `python Tools/export_combat_balance_excel.py` (ось **0…300**, шаг 25).  
EditMode: `AccuracyAimCurveContractTests`.  
Play: `Tools/Tests/Run Accuracy Aim Curve Contract (Play)` → `Assets/_Docs/Logs/Tests/AccuracyAimCurveContract_LAST.txt`.

Старые пекари **не запускать:** `bake_weapon_combat_balance.py`, `patch_weapon_dispersion_curves.py`, `bake_optic_distance_profiles.py`, `bake_optic_assets.py`, `stretch_distance_balance.py`.

Этапы 8 и 9 остаются **CLOSED / VERIFIED** (`OpticRangeContract_LAST.txt` **PASS 29/0**, `WeaponRangeContract_LAST.txt` **PASS 53/0**). Q / `ScopeVisionRange` / E в этом Play не сдвинулись. **A3 / Stage 11 CLOSED / VERIFIED PASS 21/0** — `Vision_Stage11_FireDiscipline_Catalog.md`. **A4+A5 / Stage 12 CLOSED / VERIFIED PASS 30/0** — `Vision_Stage12_ProjectileVision_Catalog.md`. **A6+A9 / Stage 13 CLOSED / VERIFIED PASS 35/0**. **A7 / Stage 14 CLOSED / VERIFIED PASS 31/0**.

---

## Конверт (контракт, без новых ограничений зрения)

```text
VisionRange
   ↓
какая дистанция вообще доступна
   ↓
AccuracyCurve / AimTimeCurve
   ↓
поведение оружия внутри этой зоны
```

```text
1× / HipFire         → 0–150 м
кратная оптика       → 0–свой ScopeVisionRange
Scope9               → 0–300 м
```

Запечённые `ScopeVisionRange` (этап 8, не менять):

| Класс | м | Ассеты |
| --- | ---: | --- |
| 1× | 150 | Reddot1/2/3, RDC, AK_Reddot4_Rail |
| 2× | 175 | Aimpoint |
| 3× | 200 | Scope1_3x; G33 высокий |
| 3.5× | 210 | Mosin_Scope8; ACOG_RMR высокий |
| 4× | 220 | ACOG, SUSAT, AK_Scope11; ELCAN высокий |
| штурмовой 6× | 250 | Vortex высокий |
| снайперская 6× | 260 | Scope4 |
| снайперская 8× | 280 | Scope5 |
| снайперская 10× | 300 | Scope9 |

`AimTimeModifier` плоский (этап 8, **не** `VisionRange × k`): 0.95…1.56. Stage 10 его не пересчитывает.

---

## 10.1. Оружие: E / глаз / оптика

Глаз без оптики = **150**. С оптикой = `ScopeVisionRange` прицела. E — этап 9, не ретюнить.

| Ствол | Класс кривой | E | Глаз | Макс. оптика на стволе |
| --- | --- | ---: | ---: | --- |
| Benelli M4 | ShotgunCqb | 40 | 150 | 150 (1×) |
| AK-74U / MK18 | CQB | 100 / 105 | 150 | 150 |
| AK-74U MOD1 / AK-47S | CQB | 110 / 115 | 150 | 150 |
| M4 ModA1 / AK-47 | Assault | 140 | 150 | до 250 (Vortex) |
| AK-47 wood / AK-74 | Assault | 145 | 150 | до 250 |
| M4 ModA2 / AK-47 MOD1 | Assault | 150 | 150 | до 250 |
| AK-74 MOD1 | Assault | 155 | 150 | до 250 |
| M16A ModA1 | MidRifle | 160 | 150 | до 250 |
| RPK-47 / M249 | LMG | 150 | 150 | до 250 |
| RPK-47 MOD1 / RPK-74 | LMG | 155 | 150 | до 250 |
| RPK-74 MOD1 / PKM | LMG | 160 | 150 | до 250 |
| M16A4 ModA2 | Marksman | 175 | 150 | до 250 |
| SVD | Marksman | 175 | 150 | до 220 (PSO-класс) |
| Mosin | Marksman | 200 | 150 | **210** (Scope8 = 3.5×, не снайпер 8×) |
| MK12 | DMR | 200 | 150 | до 260 (Scope4) |
| Sniper 7.62×51 | DMR/Sniper | 225 | 150 | **300** (Scope9) |
| M2 Browning | HeavySupport | 225 | 150 | турель, A9 |
| MK19 | GrenadeSupport | 300* | 150 | потолок замысла, не live-falloff |

\*MK19 E — каталог этапа 9, не кривая гранаты.

Кривая точности **не требует** игрового sweet-spot дальше доступного обзора этой пары ствол+прицел.

---

## 10.2. Инвентаризация (до bake)

Боевые YAML жили на ключах **0 / 125 / 250 / 375 / 500** (растяжка старых 0–100). C# library была чуть другой. Runtime брал YAML, если профиль не flat.

| Класс | AccuracyCurve | AimTimeCurve | Старая макс. | Новая макс. | Старый disp sweet |
| --- | --- | --- | ---: | ---: | --- |
| CQB / shotgun | да | да | 500 | 150 (+ хвост 300) | 0 (уже внутри) |
| Assault carbine / 7.62 / 5.45 | да | да | 500 | 150 / Scope | 0 |
| MidRifle (M16A) | да | да | 500 | 150 / Scope | **250** (за глазом) |
| Marksman | да | да | 500 | 150–210 / Scope | **250** |
| DMR / sniper | да | да | 500 | до 300 | **375** |
| LMG | да | да | 500 | 150–250 / Scope | **250** |
| Collimator 1× | да | да | 500 | 150 | ≤75 |
| Scope4 / 5 / 9 | да | да | 500 | 260 / 280 / 300 | **350 / 500 / 500** |

Позы HipFire/PointAim/Aiming в `WeaponPoseDistanceCurves` — отдельные множители 0–300; HipFire деградирует уже к 150.

**Не игровой хвост 0…500 (не sweet):** рукоятки, ДТК, глушители, фонарь — плоские `1.0` до 500. Runtime clamp 300. ЛЦУ: `m_LaserPointAimEffectByDistance` ещё имеет ключ 500 при нуле с 200 м — не AccuracyCurve. **РПГ** (`RocketLauncherData` 0…500) — **Stage 12 CLOSED**, кривые не ретюнили.

---

## 10.3. Правило переноса

Не растягивать `0…500 → 0…150` пропорционально.

- **CQB / assault:** реальные метры внутри 150 сохранить; хвост 375–500 отрезать. Sweet остаётся у нуля.
- **MidRifle / marksman / DMR / LMG:** старый far-valley **перенести** внутрь видимого конверта, не сжимать ближнюю зону.
- **Коллиматоры:** ближний характер, ключи >150 срезать.
- **Длинная труба:** старый far sweet сдвинуть к ~0.92× `ScopeVisionRange`.

Burst-by-shot не трогать.

---

## 10.4. Sweet после bake (disp, меньше = лучше)

| Оптика | V | Sweet | AimTime× (заморожен) |
| --- | ---: | ---: | ---: |
| Reddot1/3, RDC | 150 | 0–75 | 0.98 |
| Reddot2 | 150 | 0 | 0.98 |
| AK_Reddot4_Rail | 150 | 0 | 0.95 |
| Aimpoint | 175 | 175 | 1.00 |
| G33 высокий | 200 | 200 | 1.14 |
| Scope1_3x | 200 | 200 | 1.14 |
| Mosin_Scope8 | 210 | 210 | 1.22 |
| ACOG_RMR | 210 | 200 | 1.22 |
| ACOG / SUSAT / AK_Scope11 / ELCAN | 220 | 210 | 1.20–1.24 |
| Vortex | 250 | 240 | 1.34 |
| **Scope4** | **260** | **255** | **1.46** |
| **Scope5** | **280** | **280** | **1.56** |
| **Scope9** | **300** | **300** | **1.55** |

| Роль ствола | Disp sweet | Характер |
| --- | ---: | --- |
| CQB / shotgun | 0 | быстро портится с дистанцией |
| Assault carbine / 7.62 / 5.45 | 0 | стабильная середина, лучшее всё ещё близко |
| MidRifle | 150 | рабочая даль глаза |
| LMG | 150 | поддержка на краю глаза |
| Marksman (M16A4, SVD, **Mosin**) | 200 | преимущество 150–210 |
| DMR / Sniper (MK12, Sniper762) | 260 | пик у края снайперского обзора |
| M2 / MK19 | 200 | турель; MK19 не hitscan-falloff |

Mosin больше **не** DMR: компактный 3.5× / 210 не должен тащить sweet 260.

---

## 10.5. AimTime отдельно

Сначала Accuracy, потом AimTime (те же X, другие Y).

Плоский `AimTimeModifier` не трогать. Тяжёлая труба медленнее коллиматора (`1.55 > 0.98`). Дистанционная кривая AimTime работает **внутри** того же vision envelope; нет формулы `AimTime = VisionRange × k`.

---

## 10.6. Заморозка

```text
Q
DistanceCurve
Acquire / Lose
FOV / Exposure / Movement bonus
VisionRange / ScopeVisionRange
E оружия / патронов
BaseDamage
Recoil / Cone
Threat / Memory / Identity
burst-by-shot
```

---

## 10.7–10.8. Матрица Play

Дистанции: `0 25 50 75 100 125 150 175 200 225 250 275 300`.  
`distance > VisionRange` → тег **OUTSIDE_VISION**, не FAIL оружия.

Ощущение: CQB близко естественно; assault на 100–150 ещё работает; marksman 150–210/250; sniper 200–300 **до края зрения**.

---

## 10.9. Контракт

```text
ForEveryCombatWeapon
    ForEveryApplicableScope
        ForEveryDistance
            AssertCurveIsMeaningfulInsideVision
            AssertNoRequiredSweetSpotBeyondVision

distance > VisionRange
    → кривая может существовать математически
    → не игровое рабочее состояние
```

---

## Acceptance — выполнено Play **PASS 11/0** (21:35:32)

### Accuracy

- нет игрового sweet за доступным обзором;
- Scope4 / 5 / 9 пик внутри 260 / 280 / 300;
- CQB не получает дальнего sweet;
- marksman/sniper используют свой дальний видимый пояс.

### AimTime

- плоские `AimTime×` сохранены;
- тяжёлая оптика медленнее;
- нет `AimTime = VisionRange × k`.

### Regression

- Stage 8 **PASS 29/0**, Stage 9 **PASS 53/0**;
- Q / `VisionRange` / `ScopeVisionRange` / E / hitscan envelope без изменений.

Play stamp: `AccuracyAimCurveContract_LAST.txt` **RESULT=PASS PASS=11 FAIL=0**. NavMesh warning при спавне harness — тот же шум, что у этапов 8/9, не FAIL. **A3 / Stage 11 CLOSED / VERIFIED PASS 21/0**. **A4+A5 / Stage 12 CLOSED / VERIFIED PASS 30/0**. Stage 13 **CLOSED / VERIFIED PASS 35/0**. Stage 14 **CLOSED / VERIFIED PASS 31/0**.
