# Система визуальной отдачи при стрельбе из пехотного оружия (юниты)

> Самодостаточное описание для диагностики. Не требует доступа к репозиторию: все ключевые скрипты, методы и параметры приведены здесь целиком или в виде точных выдержек.
> Проект: Unity (C#), версия скриптового рантайма с `linearVelocity` (Unity 6).
> Дата обновления: 18.08.2026.
>
> **Текущее состояние кода (live):** единый визуальный `impulse` (back-first) + translation вдоль реального ствола в parent-space `Hand_R`. После оверлея кисти `UnitWeaponArmRecoil` (220) перестраивает плечо → верхнюю руку → предплечье вокруг той же `Hand_R`, не двигая оружие. Левая рука следует IK (250).
>
> **Геймплейная отдача заменена (2026-08-21):** вместо `RecoilPenalty` → раздутый конус hitscan теперь `RecoilOffset` в градусах (yaw/pitch). Visual punch тот же канал. Climb читает `RecoilOffset.y`, не `PitchCurve(P)`. Актуальные формулы пули: `Assets/_Docs/Стрельба_и_отдача.md`. Исторические выдержки `RecoilPenalty` ниже описывают **старую** модель и не являются live-кодом.
> Исторические логи не переснимались после правки пространства: §16 — до back-first; §18 — после калибровки коэффициентов, но ещё в осях кисти (без `backProj`).

---

## 1. Обзор архитектуры

При выстреле из пехотного оружия работают **два независимых канала**:

1. **Геймплейная отдача** (`UnitWeaponRecoilController` + `EquippedWeaponTransientState.RecoilPenalty`)
   - Накопление штрафа `RecoilPenalty` и его восстановление.
   - Влияет на разброс hitscan (`UnitWeaponHitscanShooting`) и на процедурный подъём ствола в очереди.
   - **Не двигает модель оружия.**

2. **Визуальная отдача** (`UnitWeaponRecoil` + `WeaponVisualRecoilApplicator`)
   - Считает единый `impulse` за выстрел (нормализованная визуальная сила, не физический импульс) и раскладывает его на **back (главный, вдоль ствола)**, **up (вторичный, мировая вертикаль)** и **pitch (вторичное вращение)**, плюс **climb** от накопленного `RecoilPenalty` (только вращение, без сдвига).
   - В `LateUpdate` (порядок 200) накладывает оверлей на кость `Hand_R`:
     - вращение: `finalRot = animBaseRot × Euler(−(climbPitch+punchPitch)×HandPitch, punchYaw×HandYaw, 0)`;
     - сдвиг: `worldDelta = Vector3.up × upOffset − FireOrigin.forward × backOffset`, затем `finalPos = animBasePos + Hand_R.parent.InverseTransformVector(worldDelta)`.
   - Оси кости `Hand_R` для translation **не** используются (они не совпадают с продольной осью оружия).
   - Оружие двигается, потому что является **ребёнком Hand_R**. Левая кисть в том же кадре следует за уже откинутым оружием (`AnimatorHandIk`, порядок 250). Правую кисть снапать нельзя — получился бы feedback-loop.

3. **Визуальная реакция руки** (`UnitWeaponArmRecoil`, порядок 220)
   - Не двигает оружие и не задаёт цель кисти. Снимает post-recoil TRS `Hand_R`, чуть смещает плечо, решает two-bone IK к **той же** кисти, затем возвращает `Hand_R` на сохранённый TRS.
   - Impulse тот же источник, что у kick: `LastAddedVisualImpulse × WeaponDefinition.ArmRecoilMultiplier`, кап `2.5`, отдельные tau руки/плеча.

Плюс параллельные визуальные эффекты выстрела, запускаемые тем же событием `ShotFired`:
затвор (`UnitWeaponBoltCycleVisual`), вспышка (`UnitWeaponMuzzleVfx`), гильза (`UnitWeaponShellEjection`),
звук (`UnitWeaponFireAudio`), палец на спуске (`UnitWeaponTriggerFingerDriver`), трассер (`UnitWeaponBulletFlightVfx`), эффекты попадания (`UnitWeaponImpactVfx`).

Центральный сигнал всей системы — событие:

```csharp
// UnitWeaponFireController
public event Action<AmmoDefinition> ShotFired;
```

Оно вызывается ровно один раз на каждый успешный выстрел (после расхода патрона и hitscan, до перезарядки по пустому магазину).

---

## 2. Иерархия объектов и якоря

- Юнит (корень) содержит `UnitEquipment`; оружие — **ребёнок `Hand_R`** (кость правой кисти, поле `UnitEquipment.m_RightHand`).
- `UnitEquipment.RightHandAnchor` → трансформ правой кисти (родитель визуала оружия).
- `UnitEquipment.MainWeaponRoot` → корень инстанса оружия в руке (null, если оружия нет).
- `UnitEquipment.EquippedWeapon` → компонент `EquippedWeapon` на инстансе оружия (якоря ствола, затвора, гильзы).
- `EquippedWeapon.FireOriginTransform` → точка вылета пули/VFX/звука:
  - сначала ищется дочерний объект `MuzzleExit` на инстансе дульного модуля (глушитель и т.п.);
  - иначе прямой дочерний `MuzzleExit` оружия;
  - иначе `BarrelTransform` (= `m_Barrel` ?? `m_MuzzleModuleVisualSocket` ?? `transform`).
- `EquippedWeapon.BoltCarrierTransform` → затворная рама/слайд (null = процедурного цикла затвора нет).
- `EquippedWeapon.ShellEjectTransform` → точка выброса гильзы.
- Кости руки читаются через `Animator.GetBoneTransform(HumanBodyBones.RightHand / LeftHand / RightUpperArm / ...)`.

Ключевые свойства `UnitEquipment` (файл `Assets/_Scripts/Inventory/UnitEquipment.cs`):

```csharp
public ItemDefinition EquippedDefinition;       // тип экипированного оружия
public EquippedWeapon EquippedWeapon;           // компонент на инстансе оружия
public bool IsOperatingVehicleTurret;           // true если юнит управляет турелью машины
public Transform MainWeaponRoot;                // корень инстанса оружия в руке
public Transform RightHandAnchor;               // кость Hand_R
public bool IsWeaponHeldForBoltCycle;           // оружие временно на отдельном якоре (болтовое передёргивание)
```

Во время болтового передёргивания `TryBeginBoltCycleLeftHandGrip()` перепарентинивает оружие на стабильный якорь `BoltCycleWeaponHoldAnchor` (сохраняя world-pose), а `EndBoltCycleLeftHandGrip()` возвращает его в Hand_R. Пока оружие на якоре, визуальная отдача **отключена** (см. ниже `ShouldApplyOverlayThisFrame`).

---

## 3. Порядок исполнения (Script Execution Order)

| Порядок | Компонент | Роль |
|---:|---|---|
| 50 | `UnitWeaponRecoil` | Update: затухание punch и пересчёт состояния; LateUpdate: пересчёт состояния |
| 55 | `UnitWeaponRuntime` | привязка состояния оружия, расход патрона |
| 56 | `UnitWeaponFireController` | Update: выстрел + вызов `ShotFired` |
| 56 | `UnitWeaponBoltCycleVisual` | LateUpdate: процедурный цикл затвора |
| 56 | `UnitWeaponFireAudio` | звук выстрела |
| 57 | `UnitWeaponHitscanShooting` | hitscan до `ShotFired` (разброс, урон, `ShotTrace`) |
| 57 | `UnitWeaponShellEjection` | спавн физической гильзы |
| 57 | `UnitWeaponBulletFlightVfx` | трассер (по `ShotTrace`) |
| 58 | `UnitWeaponRecoilController` | Update: восстановление `RecoilPenalty`; +за выстрел |
| 58 | `UnitWeaponMuzzleVfx` | дульная вспышка |
| 58 | `UnitWeaponParticleShellEjection` | частицы гильз (режим Particle / Hybrid вне near-радиуса) |
| 58 | `UnitWeaponTriggerFingerDriver` | Update: параметр `TriggerPress` в Animator |
| 58 | `UnitWeaponImpactVfx` | эффекты попадания (по `ShotTrace`) |
| 60 | `EquippedWeapon` | якоря/визуал модулей |
| 64 | `UnitEquippedWeaponPose` | авторинг базовой позы оружия в Hand_R (relaxed↔ready) |
| 200 | `WeaponVisualRecoilApplicator` | LateUpdate: оверлей recoil на Hand_R |
| 201 | `WeaponAimVisualBarrelSpinFlush` | сброс визуала вращения ствола |
| 220 | `UnitWeaponArmRecoil` | LateUpdate: плечо/локоть вокруг post-recoil Hand_R; кисть возвращается на место |
| 250 | `AnimatorHandIk` | IK рук; LateUpdate-снап левой кисти к оружию (уже с учётом recoil) |

Важное следствие порядка **50 → 56**:
- В кадре выстрела `UnitWeaponRecoil.Update` (50) сначала применяет экспоненциальное затухание punch за прошедший кадр, затем `UnitWeaponFireController.Update` (56) производит выстрел и событие `ShotFired` добавляет новый punch. Т.е. свежедобавленный punch в этом кадре **не затухает** — затухание начнётся со следующего кадра.

---

## 4. Жизненный цикл выстрела (по шагам)

### Шаг 1. Команда огня
`UnitWeaponFireController.StartFiring()` ставит `m_IsFiringCommandActive = true`.
- SemiAuto: сразу `TryFireSingleShot()`; повторный выстрел при удержании — только после повторного набора прицела (`TryReleaseSemiTriggerForReAim`).
- FullAuto: `Update()` каждый кадр вызывает `TryFireSingleShot()`.
- Burst: `UpdateBurstFire()` ведёт очереди по `BurstRounds` с паузой `BurstPauseSeconds`.

### Шаг 2. Проверки (все в `TryFireSingleShotInternal`, порядок важен)
1. Есть ли `UnitWeaponRuntime`.
2. Юнит в сознании (`UnitConsciousness`).
3. `IsFireAllowedByWeaponPose()`: турель машины — всегда можно; иначе `UnitWeaponReadyHandsLayer.CanFireFromSettledCombatPose()` (поза Aiming / HipFire / PointAim; PreAim запрещён).
4. `m_RequireReady` && `ReadyHands.IsWeaponReadyToFire()`.
5. `IsFireBlockedByBusyState()`: смена стойки, reload, бросок, гранатомёт, самостабилизация, стабилизация другого, ProximityRelax.
6. `IsWeaponReloadBusy()` (включая reload-анимацию турели).
7. `m_RequireVisibleTarget` && есть engageable-цель (`TargetSelector`).
8. `HasRequiredAimProgressForFire()`: `AimProgress01 >=` порога позы (`PreAimPoseUtility.GetPoseFireThreshold01`); для авто-режимов порог проверяется только для 1-го выстрела серии.
9. `IsBarrelAlignedEnoughToFire()`: угол между `FireOriginTransform.forward` и направлением на точку прицеливания <= таблицы допусков (см. §7).
10. `IsLineOfFireBlocked()`: SphereCast по линии огня, дружественный/нейтральный юнит блокирует (кэш 0.15 с).
11. `UnitWeaponRuntime.TryConsumeShot()` — расход патрона (см. ниже).

### Шаг 3. Расход патрона
`UnitWeaponRuntime.TryConsumeShot(time, fireMode, out ammo)`:
- проверка неисправности (`MalfunctionController.EvaluateBeforeChamberedShot`);
- `time < TransientState.NextAllowedShotTime` → `FireRateLimited` (лимит RPM);
- нет патрона в патроннике → попытка дослать из магазина (для shell-by-shell) либо `NeedsBoltCycle` / `NoMagazine` / `EmptyMagazine`;
- `TryConsumeRound` → вычленение `AmmoDefinition` выпущенного патрона;
- `ApplyConditionAfterSuccessfulShot` (износ/загрязнение);
- `NextAllowedShotTime = time + 60/RPM` (`SemiAutoFireRateRpm` для Semi, если задан);
- возврат `Success`.

### Шаг 4. Вызов хитов и события (в `TryFireSingleShot`, строго в этом порядке)
```csharp
m_DebugSuccessfulShotCount++;
m_HitscanShooting?.ProcessSuccessfulShot(firedAmmoDefinition); // hitscan + ShotTrace
RegisterBurstSpreadShotIfNeeded();                            // ++ConsecutiveBurstShotsFired (авто)
ShotFired?.Invoke(firedAmmoDefinition);                       // ← все визуальные подписчики
// далее: авто-перезарядка при пустом магазине
```

Hitscan выполняется **до** `ShotFired`, чтобы разброс этого выстрела не включал отдачу от самого выстрела.

### Шаг 5. Подписчики `ShotFired` (в порядке подписки; обычно порядок регистрации компонентов)

| Компонент | Что делает в обработчике |
|---|---|
| `UnitWeaponRecoilController` | `RecoilPenalty += ComputeRecoilAddedPerShot(...)` (кламп 0..30) |
| `UnitWeaponRecoil` | `impulse = RecoilAddedPerShot × kickScale`; `m_ShotImpulse = min(m_ShotImpulse + impulse, кап 6)`; yaw-шум; `RebuildCurrentState()` |
| `UnitWeaponBoltCycleVisual` | запускает цикл затвора (или откладывает гильзу для болтовых) |
| `UnitWeaponMuzzleVfx` | спавнит дульную вспышку (пул, с учётом глушителя и дистанции) |
| `UnitWeaponShellEjection` | спавнит физическую гильзу (если затвор её не берёт на себя) |
| `UnitWeaponFireAudio` | проигрывает звук выстрела (+ хвост через корутину) |
| `UnitWeaponTriggerFingerDriver` | `m_Trigger01 = max(m_Trigger01, 1)` → параметр `TriggerPress` |
| `UnitWeaponPoseSweepTest` (только во время прогона по L) | `m_BurstShotsFired++` и захват «до-выстрельных» значений позы оружия для лога `[WeaponVisDiag]` |

### Шаг 6. LateUpdate-конвейер
1. `UnitWeaponRecoil.LateUpdate` (50): если наложение разрешено — пересборка состояния.
2. `UnitWeaponBoltCycleVisual.LateUpdate` (56): анимация затвора/крышки.
3. `WeaponVisualRecoilApplicator.LateUpdate` (200): **наложение recoil на Hand_R**:
   ```csharp
   Quaternion recoilRot = m_Recoil.BuildHandRotationOffset();
   Vector3 punchParent = m_Recoil.BuildHandParentSpaceTranslation(hand);
   Quaternion baseRot = hand.localRotation;
   Vector3 basePos = hand.localPosition;
   hand.localRotation = baseRot * recoilRot;
   hand.localPosition = basePos + punchParent; // без baseRot * punchLocal — оси кисти ≠ ось ствола
   ```
   (применяется только если `CurrentState.isActive` и `ShouldApplyOverlayThisFrame()`).
4. `UnitWeaponArmRecoil.LateUpdate` (220): захват TRS `Hand_R` → overlay плеча → two-bone к той же кисти → `RestoreHandPose()`. Оружие не переприцеливается.
5. `AnimatorHandIk.LateUpdate` (250): снап **левой** кисти к оружию (two-bone IK) — левая рука следует за «откинутым» оружием. Правую кисть снапать запрещено (оружие — ребёнок Hand_R, получился бы feedback-loop).
6. Конец кадра (`WaitForEndOfFrame`): корутина прогона `UnitWeaponPoseSweepTest` возобновляется, читает финальные трансформы и печатает лог `[WeaponVisDiag]` (поза оружия до → после выстрела).

---

## 5. Геймплейная отдача (RecoilPenalty)

### 5.1 `UnitWeaponRecoilController` — полный листинг
Файл: `Assets/_Scripts/Shooting/UnitWeaponRecoilController.cs`. Не пишет кости и не двигает оружие.

```csharp
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(58)]
public sealed class UnitWeaponRecoilController : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitCombatStats m_CombatStats;
	[SerializeField] private UnitIndividualTraits m_IndividualTraits;
	[SerializeField] private UnitCombatCondition m_CombatCondition;
	[SerializeField] private UnitStanceCombatModifiers m_StanceCombatModifiers;

	[Header("Recovery")]
	[SerializeField, Min(0.1f)] private float m_MaxRecoilPenalty = 30f;
	[SerializeField, Min(0f)] private float m_RecoilSpreadScale = 0.15f;
	[SerializeField, Min(0f)] private float m_RecoveryWhileFiringMultiplier = 0.7f;
	[SerializeField, Min(0f)] private float m_RecoveryWhenNotReadyMultiplier = 1.2f;

	[Header("Debug")]
	[SerializeField, Min(0f)] private float m_DebugLastRecoilAdded;
	[SerializeField, Min(0f)] private float m_DebugLastRecoveryPerSecond;
	[SerializeField, Min(0.01f)] private float m_DebugSkillRecoilAddedMultiplier = 1f;
	[SerializeField, Min(0.01f)] private float m_DebugConditionRecoilAddedMultiplier = 1f;
	[SerializeField, Min(0.01f)] private float m_DebugSkillRecoveryMultiplier = 1f;
	[SerializeField, Min(0.01f)] private float m_DebugConditionRecoveryMultiplier = 1f;
	#endregion

	#region Public Properties
	public float MaxRecoilPenalty => m_MaxRecoilPenalty;
	public float RecoilPenalty =>
		m_WeaponRuntime != null && m_WeaponRuntime.TransientState != null
			? m_WeaponRuntime.TransientState.RecoilPenalty
			: 0f;
	public float SpreadHalfAngle => RecoilPenalty * m_RecoilSpreadScale;
	public float Stability01 => 1f - Mathf.Clamp01(RecoilPenalty / m_MaxRecoilPenalty);
	public float RecoveryWhileFiringMultiplier => m_RecoveryWhileFiringMultiplier;
	public bool IsRecoveringWhileFiring =>
		m_FireController != null && m_FireController.IsFiringCommandActive;
	#endregion

	private void Awake() { /* GetComponent-резолв всех ссылок */ }
	private void OnEnable()  { if (m_FireController != null) m_FireController.ShotFired += HandleShotFired; }
	private void OnDisable() { if (m_FireController != null) m_FireController.ShotFired -= HandleShotFired; }

	private void Update()
	{
		if (m_WeaponRuntime == null || m_WeaponRuntime.CurrentWeaponDefinition == null)
			return;

		float currentPenalty = m_WeaponRuntime.TransientState.RecoilPenalty;
		float recoveryPerSecond = CalculateCurrentRecoveryPerSecond();
		float nextPenalty;
		if (currentPenalty <= 0f)
		{
			nextPenalty = 0f;
			m_DebugLastRecoveryPerSecond = 0f;
		}
		else
		{
			nextPenalty = ClampRecoilPenalty(Mathf.MoveTowards(currentPenalty, 0f, recoveryPerSecond * Time.deltaTime));
			m_DebugLastRecoveryPerSecond = recoveryPerSecond;
		}

		if (!Mathf.Approximately(nextPenalty, currentPenalty))
			m_WeaponRuntime.SetRecoilPenalty(nextPenalty);
	}

	private void HandleShotFired(AmmoDefinition _ammoDefinition)
	{
		if (m_WeaponRuntime == null || m_WeaponRuntime.CurrentWeaponDefinition == null)
			return;

		float recoilAdded = CalculateRecoilAddedPerShot(_ammoDefinition);
		float oldPenalty = m_WeaponRuntime.TransientState.RecoilPenalty;
		float newPenalty = ClampRecoilPenalty(oldPenalty + recoilAdded);
		m_WeaponRuntime.SetRecoilPenalty(newPenalty);
		m_DebugLastRecoilAdded = recoilAdded;
	}

	public float GetCurrentRecoveryPerSecond() => CalculateCurrentRecoveryPerSecond();
	public float ComputeRecoilAddedPerShot(AmmoDefinition _ammoDefinition) => CalculateRecoilAddedPerShot(_ammoDefinition);

	public void ResetRecoilPenalty()
	{
		if (m_WeaponRuntime == null) return;
		m_WeaponRuntime.SetRecoilPenalty(0f);
		m_DebugLastRecoilAdded = 0f;
		m_DebugLastRecoveryPerSecond = 0f;
	}

	private float CalculateRecoilAddedPerShot(AmmoDefinition _ammoDefinition)
	{
		WeaponDefinition weaponDefinition = m_WeaponRuntime.CurrentWeaponDefinition;
		WeaponFireMode fireMode = m_FireController != null
			? m_FireController.ResolveEffectiveFireMode()
			: WeaponFireMode.SemiAuto;

		float attachmentModifier = m_WeaponRuntime.RuntimeState != null
			? m_WeaponRuntime.RuntimeState.GetAttachmentRecoilProduct(fireMode)
			: 1f;
		float skillMultiplier = m_CombatStats != null ? m_CombatStats.GetRecoilAddedMultiplier() : 1f;
		float individualMultiplier = m_IndividualTraits != null ? m_IndividualTraits.GetRecoilAddedMultiplier() : 1f;
		float conditionMultiplier = m_CombatCondition != null ? m_CombatCondition.GetRecoilAddedMultiplier() : 1f;
		float postureMultiplier = m_StanceCombatModifiers != null
			? m_StanceCombatModifiers.GetRecoilAddedMultiplier()
			: 1f;
		m_DebugSkillRecoilAddedMultiplier = skillMultiplier * individualMultiplier;
		m_DebugConditionRecoilAddedMultiplier = conditionMultiplier;
		return WeaponDefinition.ComputeAddedRecoilPenalty(weaponDefinition, fireMode, _ammoDefinition, attachmentModifier) *
		       skillMultiplier *
		       individualMultiplier *
		       conditionMultiplier *
		       postureMultiplier;
	}

	private float CalculateCurrentRecoveryPerSecond()
	{
		WeaponDefinition weaponDefinition = m_WeaponRuntime.CurrentWeaponDefinition;
		if (weaponDefinition == null) return 0f;

		float recoveryPerSecond = weaponDefinition.RecoilRecoveryPerSecond;

		if (m_FireController != null && m_FireController.IsFiringCommandActive)
			recoveryPerSecond *= m_RecoveryWhileFiringMultiplier;

		if (m_ReadyHands != null && !m_ReadyHands.IsWeaponEquippedAndReady())
			recoveryPerSecond *= m_RecoveryWhenNotReadyMultiplier;

		float skillMultiplier = m_CombatStats != null ? m_CombatStats.GetRecoilRecoveryMultiplier() : 1f;
		float individualMultiplier = m_IndividualTraits != null ? m_IndividualTraits.GetRecoilRecoveryMultiplier() : 1f;
		float conditionMultiplier = m_CombatCondition != null ? m_CombatCondition.GetRecoilRecoveryMultiplier() : 1f;
		recoveryPerSecond *= skillMultiplier;
		recoveryPerSecond *= individualMultiplier;
		recoveryPerSecond *= conditionMultiplier;
		m_DebugSkillRecoveryMultiplier = skillMultiplier * individualMultiplier;
		m_DebugConditionRecoveryMultiplier = conditionMultiplier;

		return Mathf.Max(0f, recoveryPerSecond);
	}

	private float ClampRecoilPenalty(float _value) => Mathf.Clamp(_value, 0f, m_MaxRecoilPenalty);
}
```

### 5.2 Базовая формула прибавки отдачи (статическая, `WeaponDefinition`)
```csharp
public static float ComputeAddedRecoilPenalty(
	WeaponDefinition weaponDefinition,
	WeaponFireMode fireMode,
	AmmoDefinition ammoDefinition,
	float attachmentRecoilModifier = 1f)
{
	if (weaponDefinition == null) return 0f;

	float fireModeMultiplier = fireMode switch
	{
		WeaponFireMode.FullAuto => weaponDefinition.AutoRecoilMultiplier,
		WeaponFireMode.Burst    => weaponDefinition.AutoRecoilMultiplier,
		WeaponFireMode.Auto     => weaponDefinition.AutoRecoilMultiplier,
		_                       => weaponDefinition.SemiAutoRecoilMultiplier
	};

	float ammoModifier = ammoDefinition != null ? ammoDefinition.RecoilModifier : 1f;
	return weaponDefinition.RecoilPerShot * fireModeMultiplier * ammoModifier * attachmentRecoilModifier;
}
```

### 5.3 `EquippedWeaponTransientState` — полный листинг (временное состояние оружия в руках)
Файл: `Assets/_Scripts/Shooting/WeaponRuntimeState.cs` (класс в конце файла).

```csharp
[Serializable]
public sealed class EquippedWeaponTransientState
{
	[SerializeField, Range(0f, 1f)] private float m_AimProgress01;
	[SerializeField, Min(0f)] private float m_RecoilPenalty;
	[SerializeField, Min(0)] private int m_ConsecutiveBurstShotsFired;
	[SerializeField] private float m_NextAllowedShotTime;
	[SerializeField] private WeaponMalfunctionKind m_MalfunctionKind;
	[SerializeField] private WeaponMalfunctionChannel m_MalfunctionChannel;
	[SerializeField] private WeaponMalfunctionPhase m_MalfunctionPhase;
	[SerializeField, Range(0, 2)] private int m_MalfunctionRackAttemptIndex;
	[SerializeField] private bool m_MalfunctionBoltAnimInProgress;

	public const float FullAimProgress01 = 1f;
	public float AimProgress01 => m_AimProgress01;
	public bool IsFullyAimed => m_AimProgress01 >= FullAimProgress01;
	public float RecoilPenalty => m_RecoilPenalty;
	public int ConsecutiveBurstShotsFired => m_ConsecutiveBurstShotsFired;
	public float NextAllowedShotTime => m_NextAllowedShotTime;
	public bool HasActiveMalfunction => m_MalfunctionKind != WeaponMalfunctionKind.None;

	public void Clear() { m_AimProgress01 = 0f; m_RecoilPenalty = 0f; m_ConsecutiveBurstShotsFired = 0; m_NextAllowedShotTime = 0f; ClearMalfunction(); }
	public void SetAimProgress(float _value) { m_AimProgress01 = Mathf.Clamp01(_value); }
	public void SetRecoilPenalty(float _value) { m_RecoilPenalty = Mathf.Max(0f, _value); }
	public int GetNextBurstShotIndex() => m_ConsecutiveBurstShotsFired + 1;
	public void RegisterBurstShotFired() { m_ConsecutiveBurstShotsFired = Mathf.Max(0, m_ConsecutiveBurstShotsFired + 1); }
	public void ResetBurstShotCounter() { m_ConsecutiveBurstShotsFired = 0; }
	public void SetNextAllowedShotTime(float _time) { m_NextAllowedShotTime = _time; }
	/* + методы неисправностей (SetMalfunction / SetMalfunctionPhase / SetMalfunctionRackAttemptIndex / SetMalfunctionBoltAnimInProgress) */
}
```

### 5.4 Поля отдачи в `WeaponDefinition` (ScriptableObject, файл `Assets/_Scripts/Shooting/WeaponDefinition.cs`)
| Поле | Дефолт | Описание |
|---|---|---|
| `m_FireRateRpm` | 600 | RPM для FullAuto/Burst (и Semi, если Semi RPM = 0) |
| `m_SemiAutoFireRateRpm` | 0 | отдельный лимит RPM для SemiAuto (0 = использовать FireRateRpm) |
| `m_AimTimeSeconds` | 0.28 | время выхода на полное качество прицеливания |
| `m_BaseShotDispersion` | 1 | базовый разброс платформы |
| `m_RecoilPerShot` | 1 | базовое накопление штрафа отдачи за выстрел |
| `m_SemiAutoRecoilMultiplier` | 0.85 | множитель отдачи в одиночном |
| `m_AutoRecoilMultiplier` | 1.25 | множитель отдачи в авто/очереди |
| `m_RecoilRecoveryPerSecond` | 3.5 | восстановление штрафа в секунду |
| `m_VisualRecoilKickScale` | 1 | множитель ТОЛЬКО визуального kick ствола (не влияет на penalty/разброс) |
| `m_ArmRecoilMultiplier` | 1 | множитель ТОЛЬКО визуальной реакции руки (`UnitWeaponArmRecoil`) |
| `m_BurstRounds` | 3 | длина очереди Burst |
| `m_BurstPauseSeconds` | 0.12 | пауза между очередями Burst |
| `m_HasBoltHoldOpenDelay` | false | bolt catch держит затвор после последнего выстрела |
| `m_RequiresManualBoltCycle` | false | болтовая винтовка: досыл только передёргиванием |
| `m_EffectiveRangeMeters` | 100 | дальность без штрафа за дистанцию |
| `m_ReloadTimeSeconds` | 2.2 | базовое время перезарядки |

---

## 6. Визуальная отдача оружия (главный канал движения оружия)

### 6.1 `UnitWeaponRecoil` — полный листинг (back-first рефактор, с геттерами для диагностики)
Файл: `Assets/_Scripts/Shooting/UnitWeaponRecoil.cs`.

```csharp
using UnityEngine;

/// <summary>
/// Computes visual recoil state only. Does not author weapon BASE pose and does not write bones.
///
/// Roles of the visual channels:
///   Back  = primary translation
///   Up    = secondary translation
///   Pitch = secondary rotation
///   Climb = PitchCurve(RecoilPenalty) — sustained secondary rotation (rotation only, no translation)
///   Yaw   = small variation
///
/// Punch — per-shot value: one shared visual impulse (normalized visual recoil strength,
/// not a physical impulse) decomposed into pitch / back / up.
///
/// <see cref="WeaponVisualRecoilApplicator"/> applies the state to Hand_R after animation, before left IK.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(50)]
public sealed class UnitWeaponRecoil : MonoBehaviour
{
	#region Serialized Fields
	[Tooltip("Снаряжение: корень оружия в руке.")]
	[SerializeField] private UnitEquipment m_Equipment;
	[Tooltip("Геймплейный контроллер отдачи — источник RecoilPenalty.")]
	[SerializeField] private UnitWeaponRecoilController m_RecoilController;
	[Tooltip("Контроллер стрельбы — источник ShotFired.")]
	[SerializeField] private UnitWeaponFireController m_FireController;
	[Tooltip("Runtime оружия — чтение VisualRecoilKickScale.")]
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitRagdollController m_RagdollController;
	[SerializeField] private UnitEquippedWeaponPoseRuntimeTuner m_RuntimeTuner;
	[Tooltip("Редко: явная точка выборки дистанции до камеры.")]
	[SerializeField] private Transform m_KickTransformOverride;
	[Tooltip("Базовая поза оружия (relaxed↔ready).")]
	[SerializeField] private UnitEquippedWeaponPose m_EquippedWeaponPose;

	[Header("Climb — устойчивый подъём от накопленной отдачи")]
	[Tooltip("Кривая: ось X = RecoilPenalty (абсолютный), ось Y = визуальный pitch (градусы). Первые выстрелы почти не поднимают ствол — очередь уводит выше постепенно.")]
	[SerializeField] private AnimationCurve m_PitchCurve = new AnimationCurve(
		new Keyframe(0f, 0f),
		new Keyframe(15f, 0.6f),
		new Keyframe(30f, 1.7f),
		new Keyframe(60f, 4.5f));
	[Tooltip("Множитель climb-подъёма (не влияет на punch).")]
	[SerializeField, Min(0f)] private float m_VisualOffsetScale = 1f;

	[Header("Punch — удар выстрела (единый impulse → pitch/back/up)")]
	[Tooltip("Градусы pitch на единицу визуальной силы отдачи. Impulse = RecoilAddedPerShot x VisualRecoilKickScale — нормализованная визуальная сила, не физический импульс.")]
	[SerializeField, Min(0f)] private float m_ShotPitch = 2.5f;
	[Tooltip("Доля pitch для амплитуды yaw-импульса.")]
	[SerializeField, Range(0f, 1f)] private float m_ShotYawScale = 0.3f;
	[Tooltip("Смещение yaw вправо (>0) / влево (<0). Шум не переворачивает сторону каждый выстрел.")]
	[SerializeField, Range(-1f, 1f)] private float m_YawBias = 0.45f;
	[Tooltip("Постоянная времени затухания punch (сек). Без перелёта за ноль.")]
	[SerializeField, Min(0.01f)] private float m_ShotSmoothTime = 0.08f;
	[Tooltip("Во время очереди punch гасится медленнее — ствол не ныряет между выстрелами.")]
	[SerializeField, Min(1f)] private float m_DecayWhileFiringMultiplier = 1.75f;
	[Tooltip("Страховочный потолок накопленной визуальной силы отдачи (impulse cap), чтобы автоочередь не улетала.")]
	[SerializeField, Min(1f)] private float m_MaxShotImpulse = 6f;
	[Tooltip("Отдельный потолок бокового yaw (градусы). Независим от impulse cap.")]
	[SerializeField, Min(1f)] private float m_MaxShotYawDegrees = 6f;
	[Tooltip("Сдвиг назад (метры) на единицу визуальной силы отдачи. Главное направление recoil. Climb сдвигом не едет.")]
	[SerializeField, Min(0f)] private float m_BackScale = 0.035f;
	[Tooltip("Сдвиг вверх (метры) на единицу визуальной силы отдачи. Вторичное направление.")]
	[SerializeField, Min(0f)] private float m_UpScale = 0.008f;

	[Header("Hand Kick")]
	[Tooltip("Множитель pitch-вращения кисти. 1 = полный визуальный kick через руку.")]
	[SerializeField, Range(0f, 2f)] private float m_HandPitch = 0.8f;
	[Tooltip("Множитель yaw-вращения кисти.")]
	[SerializeField, Range(0f, 2f)] private float m_HandYaw = 0.85f;
	[Tooltip("Множитель сдвига кисти назад.")]
	[SerializeField, Range(0f, 2f)] private float m_HandBack = 1f;
	[Tooltip("Множитель сдвига кисти вверх.")]
	[SerializeField, Range(0f, 2f)] private float m_HandUp = 0.75f;
	#endregion

	#region Private Fields
	private WeaponVisualRecoilState m_CurrentState;
	private float m_ShotImpulse;
	private float m_ShotImpulseYaw;
	private int m_ShotIndex;
	private float m_YawSeed;
	#endregion

	#region Public API
	public WeaponVisualRecoilState CurrentState => m_CurrentState;
	public bool HasVisualKick => m_CurrentState.isActive;

	/// <summary>
	/// RecoilSweep: keep punch/climb even if the camera is outside the VFX near-detail radius.
	/// </summary>
	public bool IgnoreCameraDistanceCull { get; set; }

	public bool IsCameraNearForVisualKick() => IsNearCameraForVisualDetail();

	public Quaternion BuildHandRotationOffset()
	{
		return Quaternion.Euler(
			-(m_CurrentState.climbPitch + m_CurrentState.punchPitch) * m_HandPitch,
			m_CurrentState.punchYaw * m_HandYaw,
			0f);
	}

	/// <summary>
	/// Translation recoil в пространстве родителя кисти (parent-space delta для Hand_R.localPosition).
	/// Back идёт строго назад вдоль реального ствола (FireOriginTransform.forward),
	/// up — по мировой вертикали (roll оружия не уводит recoil вбок).
	/// Оси кости Hand_R НЕ используются: они не совпадают с продольной осью оружия.
	/// </summary>
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

	public float RecoilRotationDeltaDegrees =>
		Quaternion.Angle(Quaternion.identity, BuildHandRotationOffset());

	// Геттеры, добавленные для диагностики (лог [WeaponVisDiag] в UnitWeaponPoseSweepTest):
	public float ShotPitchDegrees => m_ShotPitch;
	public float ShotYawScale => m_ShotYawScale;
	public float YawBias => m_YawBias;
	public float ShotSmoothTime => m_ShotSmoothTime;
	public float DecayWhileFiringMultiplier => m_DecayWhileFiringMultiplier;
	public float MaxShotImpulse => m_MaxShotImpulse;
	public float MaxShotYawDegrees => m_MaxShotYawDegrees;
	public float ShotImpulse => m_ShotImpulse;
	public float BackScale => m_BackScale;
	public float UpScale => m_UpScale;
	public float HandPitch => m_HandPitch;
	public float HandYaw => m_HandYaw;
	public float HandBack => m_HandBack;
	public float HandUp => m_HandUp;

	public bool ShouldApplyOverlayThisFrame()
	{
		if (m_RagdollController != null && m_RagdollController.ShouldBlockWeaponPoseScripts)
			return false;
		if (IsRuntimePoseTuningActive())
			return false;
		if (m_Equipment != null && m_Equipment.IsWeaponHeldForBoltCycle)
			return false;
		if (m_Equipment != null && m_Equipment.IsOperatingVehicleTurret)
			return false;
		return HasEquippedWeaponForVisualKick() && ResolveRightHandTransform() != null;
	}

	public void ResetVisualKick()
	{
		ResetImpulseState();
		m_CurrentState = default;
	}
	#endregion

	#region Unity Lifecycle
	private void Awake() { /* GetComponent-резолв всех ссылок; m_YawSeed = Random.value * 100f; */ }

	private void OnEnable()
	{
		if (m_Equipment != null) m_Equipment.EquipmentChanged += HandleEquipmentChanged;
		if (m_FireController != null) m_FireController.ShotFired += HandleShotFired;
	}

	private void OnDisable()
	{
		if (m_Equipment != null) m_Equipment.EquipmentChanged -= HandleEquipmentChanged;
		if (m_FireController != null) m_FireController.ShotFired -= HandleShotFired;
		ResetVisualKick();
	}

	private void Update()
	{
		if (m_RagdollController != null && m_RagdollController.ShouldBlockWeaponPoseScripts)
			return;
		if (IsRuntimePoseTuningActive())
			return;
		if (m_Equipment != null && m_Equipment.IsWeaponHeldForBoltCycle) { ResetVisualKick(); return; }
		if (m_Equipment != null && m_Equipment.IsOperatingVehicleTurret) { ResetVisualKick(); return; }
		if (!HasEquippedWeaponForVisualKick()) { ResetVisualKick(); return; }
		if (!IgnoreCameraDistanceCull && !IsNearCameraForVisualDetail()) { ResetVisualKick(); return; }

		float tau = Mathf.Max(0.01f, m_ShotSmoothTime);
		if (m_FireController != null && m_FireController.IsFiringCommandActive)
			tau *= Mathf.Max(1f, m_DecayWhileFiringMultiplier);
		float decay = Mathf.Exp(-Time.deltaTime / tau);
		m_ShotImpulse *= decay;
		m_ShotImpulseYaw *= decay;
		if (Mathf.Abs(m_ShotImpulse) < 0.001f) m_ShotImpulse = 0f;
		if (Mathf.Abs(m_ShotImpulseYaw) < 0.001f) m_ShotImpulseYaw = 0f;

		RebuildCurrentState();
	}

	private void LateUpdate()
	{
		if (!ShouldApplyOverlayThisFrame())
			return;
		RebuildCurrentState();
	}
	#endregion

	#region Private Methods
	private void RebuildCurrentState()
	{
		float kickScale = ResolveVisualRecoilKickScale();
		float penalty = m_RecoilController != null ? m_RecoilController.RecoilPenalty : 0f;
		float climbPitch = m_PitchCurve.Evaluate(penalty) * m_VisualOffsetScale * kickScale;

		float impulse = m_ShotImpulse;
		float punchPitch = impulse * m_ShotPitch;
		float punchYaw = m_ShotImpulseYaw;
		float backOffset = impulse * m_BackScale * m_HandBack;
		float upOffset = impulse * m_UpScale * m_HandUp;

		float totalPitch = climbPitch + punchPitch;
		bool isActive = Mathf.Abs(totalPitch) >= 0.001f
		                || Mathf.Abs(punchYaw) >= 0.001f
		                || Mathf.Abs(backOffset) >= 0.000001f
		                || Mathf.Abs(upOffset) >= 0.000001f;

		if (!isActive) { m_CurrentState = default; return; }

		m_CurrentState = new WeaponVisualRecoilState
		{
			punchPitch = punchPitch,
			punchYaw = punchYaw,
			climbPitch = climbPitch,
			backOffset = backOffset,
			upOffset = upOffset,
			isActive = true
		};
	}

	private Transform ResolveRightHandTransform()
	{
		return m_Equipment != null ? m_Equipment.RightHandAnchor : null;
	}

	private Transform ResolveFireOriginTransform()
	{
		EquippedWeapon weapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		if (weapon != null && weapon.FireOriginTransform != null)
			return weapon.FireOriginTransform;
		return m_Equipment != null ? m_Equipment.MainWeaponRoot : null;
	}

	private bool HasEquippedWeaponForVisualKick()
	{
		return m_Equipment != null && m_Equipment.MainWeaponRoot != null;
	}

	private bool IsRuntimePoseTuningActive()
	{
		if (m_RuntimeTuner == null)
			m_RuntimeTuner = GetComponent<UnitEquippedWeaponPoseRuntimeTuner>();
		return m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive;
	}

	private float ResolveVisualRecoilKickScale()
	{
		WeaponDefinition definition = m_WeaponRuntime != null
			? m_WeaponRuntime.CurrentWeaponDefinition
			: null;
		return definition != null ? definition.VisualRecoilKickScale : 1f;
	}

	private void HandleShotFired(AmmoDefinition _ammoDefinition)
	{
		if (!HasEquippedWeaponForVisualKick())
			return;

		float recoilPerShot = m_RecoilController != null
			? m_RecoilController.ComputeRecoilAddedPerShot(_ammoDefinition)
			: 1f;
		float kickScale = ResolveVisualRecoilKickScale();
		float impulse = recoilPerShot * kickScale;
		float shotPitch = impulse * m_ShotPitch;

		m_ShotImpulse = Mathf.Min(m_ShotImpulse + impulse, m_MaxShotImpulse);

		float yawNoise = Mathf.PerlinNoise(m_YawSeed, m_ShotIndex * 0.73f) * 2f - 1f;
		float yawDir = Mathf.Clamp(m_YawBias + yawNoise * (1f - Mathf.Abs(m_YawBias)), -1f, 1f);
		m_ShotImpulseYaw += yawDir * shotPitch * m_ShotYawScale;
		m_ShotImpulseYaw = Mathf.Clamp(m_ShotImpulseYaw, -m_MaxShotYawDegrees, m_MaxShotYawDegrees);
		m_ShotIndex++;
		RebuildCurrentState();
	}

	private void HandleEquipmentChanged() { ResetVisualKick(); }

	private bool IsNearCameraForVisualDetail()
	{
		WeaponVfxProfile profile = WeaponVfxUtility.GetCurrentProfile(null);
		EquippedWeapon weapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		Vector3 samplePosition;
		if (weapon != null && WeaponVfxUtility.TryGetShellEjectionPose(weapon, out Vector3 shellPos, out _))
			samplePosition = shellPos;
		else if (m_KickTransformOverride != null)
			samplePosition = m_KickTransformOverride.position;
		else if (m_Equipment != null && m_Equipment.MainWeaponRoot != null)
			samplePosition = m_Equipment.MainWeaponRoot.position;
		else
			samplePosition = transform.position;
		return WeaponVfxUtility.IsWithinNearCameraDetailDistance(profile, samplePosition);
	}

	private void ResetImpulseState()
	{
		m_ShotImpulse = 0f;
		m_ShotImpulseYaw = 0f;
		m_ShotIndex = 0;
	}
	#endregion
}
```

### 6.2 `WeaponVisualRecoilState` — полный листинг
Файл: `Assets/_Scripts/Shooting/WeaponVisualRecoilState.cs`.

```csharp
public struct WeaponVisualRecoilState
{
	public float punchPitch;   // градусы: мгновенный удар за выстрел (затухает)
	public float punchYaw;     // градусы: боковой удар
	public float climbPitch;   // градусы: подъём от накопленного RecoilPenalty (только вращение)
	public float backOffset;   // метры: сдвиг кисти назад
	public float upOffset;     // метры: сдвиг кисти вверх
	public bool isActive;      // накладывать ли оверлей
}
```

### 6.3 `WeaponVisualRecoilApplicator` — полный листинг
Файл: `Assets/_Scripts/Shooting/WeaponVisualRecoilApplicator.cs`.

```csharp
using UnityEngine;

/// <summary>
/// Applies WeaponVisualRecoilState to Hand_R as an absolute overlay on this frame's animation pose.
/// Does not write weapon local, pose, or aim. Left IK (order 250) follows the kicked weapon child.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UnitWeaponRecoil))]
[DefaultExecutionOrder(200)]
public sealed class WeaponVisualRecoilApplicator : MonoBehaviour
{
	[SerializeField] private UnitWeaponRecoil m_Recoil;
	[SerializeField] private UnitEquipment m_Equipment;

	public Quaternion LastHandBaseLocalRotation { get; private set; } = Quaternion.identity;
	public Quaternion LastHandFinalLocalRotation { get; private set; } = Quaternion.identity;
	public Vector3 LastHandBaseLocalPosition { get; private set; }
	public Vector3 LastHandFinalLocalPosition { get; private set; }
	public bool AppliedThisFrame { get; private set; }

	private void Awake()
	{
		if (m_Recoil == null) m_Recoil = GetComponent<UnitWeaponRecoil>();
		if (m_Equipment == null) m_Equipment = GetComponent<UnitEquipment>();
	}

	private void LateUpdate()
	{
		AppliedThisFrame = false;
		if (m_Recoil == null || !m_Recoil.isActiveAndEnabled ||
		    !m_Recoil.ShouldApplyOverlayThisFrame() ||
		    !m_Recoil.CurrentState.isActive)
		{
			LastHandFinalLocalRotation = LastHandBaseLocalRotation;
			LastHandFinalLocalPosition = LastHandBaseLocalPosition;
			return;
		}

		Transform hand = m_Equipment != null ? m_Equipment.RightHandAnchor : null;
		if (hand == null)
			return;

		Quaternion recoilRot = m_Recoil.BuildHandRotationOffset();
		Vector3 punchParent = m_Recoil.BuildHandParentSpaceTranslation(hand);

		Quaternion baseRot = hand.localRotation;
		Vector3 basePos = hand.localPosition;
		Quaternion finalRot = baseRot * recoilRot;
		Vector3 finalPos = basePos + punchParent;

		hand.localRotation = finalRot;
		hand.localPosition = finalPos;

		LastHandBaseLocalRotation = baseRot;
		LastHandFinalLocalRotation = finalRot;
		LastHandBaseLocalPosition = basePos;
		LastHandFinalLocalPosition = finalPos;
		AppliedThisFrame = true;
	}
}
```

### 6.4 Формулы визуальной отдачи (сводка, текущая live-схема)

Калибровка по умолчанию (и миграция префабов `Polygone/Shooting/Migrate Weapon Recoil Back-First Calibration`):

| Параметр | Значение | Роль |
|---|---:|---|
| `m_ShotPitch` | 2.5° | pitch на единицу impulse (вторичный) |
| `m_BackScale` | 0.035 м | назад на единицу impulse (главный) |
| `m_UpScale` | 0.008 м | вверх на единицу impulse (вторичный) |
| `m_HandPitch` | 0.80 | доля pitch, доходящая до кисти |
| `m_HandYaw` | 0.85 | доля yaw, доходящая до кисти |
| `m_HandBack` | 1.00 | доля back, доходящая до кисти |
| `m_HandUp` | 0.75 | доля up, доходящая до кисти |
| `m_ShotYawScale` | 0.30 | амплитуда yaw относительно shotPitch |
| `m_YawBias` | 0.45 | смещение yaw вправо |
| `m_ShotSmoothTime` | 0.08 с | tau затухания punch |
| `m_DecayWhileFiringMultiplier` | 1.75 | в очереди tau = 0.14 с |
| `m_MaxShotImpulse` | 6 | кап визуальной силы (не градусы) |
| `m_MaxShotYawDegrees` | 6° | отдельный кап yaw |
| `m_PitchCurve` | 0→0°, 15→0.6°, 30→1.7°, 60→4.5° | climb от RecoilPenalty |

Ожидаемый одиночный импульс (стоя, `added=0.600`, `kickScale=1.20` → `impulse=0.72`): `punchPitch≈1.80°`, `back≈25 мм`, `up≈4.3 мм`, back/up ≈ 6:1. Реальные `added`/`kickScale` зависят от оружия, патрона, стойки и скилла — сверять `visualState` в `[WeaponVisDiag]`.

Формулы:
- **Единый импульс за выстрел** (нормализованная визуальная сила отдачи, не физический импульс):
  `impulse = RecoilAddedPerShot × VisualRecoilKickScale`
  (`RecoilAddedPerShot` = та же формула, что у геймплейной отдачи, со всеми множителями.)
- **Накопление импульса**: `m_ShotImpulse = min(m_ShotImpulse + impulse, m_MaxShotImpulse = 6)`.
- **Punch pitch** (вращение, вторичное): `punchPitch = m_ShotImpulse × m_ShotPitch(2.5°)`.
- **Punch yaw** (вращение, шум):
  `yawNoise = PerlinNoise(seed, shotIndex×0.73)×2−1`;
  `yawDir = clamp(m_YawBias(0.45) + yawNoise×(1−|m_YawBias|), −1, 1)`;
  `m_ShotImpulseYaw += yawDir × (impulse × m_ShotPitch) × m_ShotYawScale(0.3)`, кламп `±m_MaxShotYawDegrees(6°)` —
  кап yaw **независим** от капа импульса (единицы измерения разные).
- **Затухание** (в Update, до выстрела текущего кадра):
  `tau = max(0.01, m_ShotSmoothTime(0.08))`, а если команда огня активна — `tau *= m_DecayWhileFiringMultiplier(1.75)`;
  `impulse *= exp(−deltaTime/tau)`. Срез: значение < 0.001 обнуляется.
  Полураспад punch при tau=0.08 c ≈ 0.055 c; при стрельбе очередь tau=0.14 c → полураспад ≈ 0.097 c.
- **Climb pitch** (только вращение):
  `climbPitch = PitchCurve.Evaluate(RecoilPenalty) × m_VisualOffsetScale(1) × kickScale`,
  где `PitchCurve` по умолчанию: `0→0°, 15→0.6°, 30→1.7°, 60→4.5°`.
  Climb **не затухает** — это функция текущего penalty. Первые выстрелы почти не поднимают ствол,
  очередь уводит его выше постепенно.
- **Сдвиги кисти** (метры), независимы от pitch:
  `backOffset = m_ShotImpulse × m_BackScale(0.035) × m_HandBack(1)` — главное направление;
  `upOffset = m_ShotImpulse × m_UpScale(0.008) × m_HandUp(0.75)` — вторичное.
  Climb на сдвиг не влияет.
- **Наложение на Hand_R**:
  - Вращение: `finalRot = baseRot × Euler(−(climbPitch+punchPitch)×m_HandPitch, punchYaw×m_HandYaw, 0)` — **поворот вверх = отрицательный pitch** (канал не связан с translation).
  - Позиция (translation в world-пространстве оружия, НЕ в осях кисти):
    `worldDelta = Vector3.up × upOffset − fireOrigin.forward × backOffset`;
    `finalPos = basePos + hand.parent.InverseTransformVector(worldDelta)`.
    Back — строго назад вдоль реального ствола (`FireOriginTransform.forward`, fallback `MainWeaponRoot`),
    up — мировая вертикаль (roll оружия не уводит recoil вбок). Оси кости `Hand_R` не используются,
    т.к. не совпадают с продольной осью оружия. Старый путь `baseRot * (0, up, −back)` в осях кисти
    давал «отдача вверх» при уже правильных `backOffset`/`upOffset` — см. исторические логи §18.3.
- **Порог активности**: `isActive` если `|climbPitch+punchPitch| ≥ 0.001°` ИЛИ `|punchYaw| ≥ 0.001°` ИЛИ `|backOffset| ≥ 1e−6 м` ИЛИ `|upOffset| ≥ 1e−6 м`.

### 6.5 Условия сброса/блокировки визуальной отдачи
`ResetVisualKick()` (обнуление punch и состояния) вызывается при:
- `OnDisable`, смене оружия (`EquipmentChanged`);
- `UnitWeaponFireController.StopFiring()` → `m_WeaponRecoil.ResetVisualKick()` (и `m_RecoilController.ResetRecoilPenalty()`); это поведение можно отключить свойством `ResetRecoilOnStopFiring=false` (используется RecoilSweep-режимом для замеров затухания);
- удержании оружия для болтового цикла (`IsWeaponHeldForBoltCycle`), управлении турелью машины, отсутствии оружия в руках;
- активном рантайм-тюнере позы;
- блокировке скриптов позы ragdoll-контроллером;
- если юнит дальше радиуса near-camera detail (см. §6.6) и `IgnoreCameraDistanceCull == false`.

Оверлей не накладывается (`ShouldApplyOverlayThisFrame() == false`) в тех же случаях, что и сброс, плюс при отсутствии `RightHandAnchor`.

### 6.6 Кадр-цензура по дистанции до камеры
`IsNearCameraForVisualDetail()` вычисляет дистанцию от точки выброса гильзы (или `m_KickTransformOverride`, или корня оружия, или юнита) до активной камеры и сравнивает с порогом `WeaponVfxProfile.HybridPhysicalShellDistanceMeters` (дефолт 12 м; если профиля нет — 12 м). Дальше порога визуальный kick/затвор/физические гильзы отключаются.

Ключевые методы `WeaponVfxUtility` (файл `Assets/_Scripts/Shooting/WeaponVfxUtility.cs`):

```csharp
public static WeaponVfxProfile GetCurrentProfile(UnitWeaponRuntime _runtime)
{
	WeaponDefinition weaponDefinition = _runtime != null ? _runtime.CurrentWeaponDefinition : null;
	return weaponDefinition != null ? weaponDefinition.VfxProfile : null;
}

public static bool TryGetShellEjectionPose(EquippedWeapon _weapon, out Vector3 _position, out Vector3 _direction)
{
	_position = Vector3.zero;
	_direction = Vector3.right;
	if (_weapon == null) return false;
	Transform barrel = _weapon.BarrelTransform;
	if (barrel == null) return false;
	Transform eject = _weapon.ShellEjectTransform;
	_position = eject != null ? eject.position : barrel.position;
	Vector3 dir = eject != null ? eject.forward : (-barrel.right);
	// нормализация; fallback Vector3.right
	return true;
}

public static bool TryGetEffectViewerPosition(out Vector3 _position)
{
	// 1) Camera.main (кэш) → 2) первая активная Camera → 3) AudioListener → Vector3.zero/false
}

public static bool IsWithinDistance(Vector3 _worldPosition, float _maxDistanceMeters)
{
	if (_maxDistanceMeters <= 0f) return false;
	if (!TryGetEffectViewerPosition(out Vector3 viewerPosition)) return false;
	return (_worldPosition - viewerPosition).sqrMagnitude <= _maxDistanceMeters * _maxDistanceMeters;
}

public static bool IsWithinNearCameraDetailDistance(WeaponVfxProfile _profile, Vector3 _worldPosition)
{
	float distance = _profile != null ? Mathf.Max(0f, _profile.HybridPhysicalShellDistanceMeters) : 12f;
	return IsWithinDistance(_worldPosition, distance);
}

public static WeaponVfxQualityTier ResolveEffectQualityTier(
	WeaponVfxProfile _profile, Vector3 _worldPosition, float _maxDistanceMeters,
	float _nearDistanceMeters = -1f, float _midDistanceMeters = -1f)
{
	if (_maxDistanceMeters <= 0f || !IsWithinEffectDistance(_worldPosition, _maxDistanceMeters))
		return WeaponVfxQualityTier.Skip;
	float nearDistance = _nearDistanceMeters >= 0f ? _nearDistanceMeters
		: _profile != null ? _profile.EffectNearQualityDistanceMeters : 15f;
	float midDistance = _midDistanceMeters >= 0f ? _midDistanceMeters
		: _profile != null ? _profile.EffectMidQualityDistanceMeters : 35f;
	// sqrDistance <= near² → Full; иначе Reduced (mid сейчас не даёт Skip — обе ветки Reduced)
}
```

---

### 6.7 Реакция руки — `UnitWeaponArmRecoil` (порядок 220)

Файл: `Assets/_Scripts/Shooting/UnitWeaponArmRecoil.cs`. Состояние кадра: `WeaponArmRecoilState`.

Инвариант: слой **не задаёт цель оружия**. После applicator кисть уже там, где должна быть. ArmRecoil перестраивает `RightShoulder → RightUpperArm → RightLowerArm` вокруг этой кисти и возвращает `Hand_R` на сохранённый world TRS. Существующий `AnimatorHandIk.ApplyTwoBoneIk` не вызывается — он пишет кисть как IK к грипу.

```text
WeaponVisualRecoilApplicator (200)
        ↓
capture Hand_R TRS
        ↓
falloff: shoulder += kick×0.22, elbow += kick×0.50, hand stays at full kick
        ↓
aim upper → elbow, lower → hand
        ↓
restore Hand_R TRS
        ↓
AnimatorHandIk left snap (250)
```

Impulse: на `ShotFired` читает `UnitWeaponRecoil.LastAddedVisualImpulse` (тот же `RecoilAddedPerShot × VisualRecoilKickScale`) × `WeaponDefinition.ArmRecoilMultiplier`, кап `m_MaxArmImpulse = 2.5`. Часть импульса сразу идёт в текущие значения: рука `m_ArmShotCatchup = 0.9`, плечо `0.5` (плечо отстаёт). Tau `0.13` / `0.18` только догоняют остаток и держат очередь выстрелов — без catch-up первый кадр брал ~10% и слой был невидим. В LateUpdate 220 сначала damp/apply, потом decay target.

Качество (кэш раз в `0.25` с, `sqrMagnitude` до камеры):
- `0–12 м` Full: falloff-сдвиг цепи (`ShoulderCarry` 0.22 / `ElbowCarry` 0.5, кисть = 1) + aim костей на локоть/кисть;
- `12–25 м` Light: тот же falloff, затем тот же restore;
- `25+ м` Off: кости не пишутся.

Направление recoil: `UnitWeaponRecoil.GetCurrentKickTranslationWorld()` — тот же back+up, что у кисти. Плечо и локоть едут вдоль него с затуханием; кисть не трогаем (restore). Гейты те же, что у kick: `ShouldApplyOverlayThisFrame()` (ragdoll / тюнер / bolt-hold / турель / нет оружия). Gameplay spread / `RecoilPenalty` не трогает.

Лог на выстрел (флаг `m_LogOnShot` / L-sweep): `[ArmRecoil] handKick elbowMove shoulderMove elbowBack elbowSide restoreErr`. Критерий: `restoreErr < 0.005 м`, `handKick > elbowMove > shoulderMove`.

---

## 7. `UnitWeaponFireController` — ключевые методы (гейт выстрела)

Файл: `Assets/_Scripts/Shooting/UnitWeaponFireController.cs`. Поля-пороги (дефолты):

| Поле | Дефолт | Смысл |
|---|---|---|
| `m_RequireReady` | true | выстрел только при ready-оружии |
| `m_RequireVisibleTarget` | true | нужна видимая цель |
| `m_EnableAutomaticFireLoop` | true | авто-цикл FullAuto/Burst при удержании |
| `m_TryReloadWhenOutOfAmmo` | true | авто-перезарядка при пустом магазине |
| `m_OutOfAmmoReloadRetrySeconds` | 0.35 | интервал повторных попыток перезарядки |
| `m_LineOfFireSafetyRadius` | 0.35 | радиус SphereCast линии огня |
| `m_LineOfFireBlockedRetrySeconds` | 0.15 | кэш результата проверки линии огня |
| `m_RequireFullAimToFire` | true | порог прицела перед выстрелом (авто — только 1-й выстрел) |
| `m_RequireBarrelAlignedToFire` | true | проверка доворота ствола к цели |
| `m_MaxBarrelAimErrorDegrees` | 3 / 9 / 9 / 8 | стоя / присед / присед-ход / ход (Aiming) |
| PointAim допуски | 5 / 10 / 7 / 11 | стоя / ход / присед / присед-ход |
| HipFire допуски | 12 / 16 / 14 / 18 | стоя / ход / присед / присед-ход |

Ключевые методы (выдержки):

```csharp
private void Update()
{
	TrySyncEngagementTarget();
	TryReleaseSemiTriggerForReAim();

	if (!m_IsFiringCommandActive || !m_EnableAutomaticFireLoop) return;
	if (IsFireBlockedByBusyState()) { StopFiring(); return; }
	if (m_WeaponRuntime == null || m_WeaponRuntime.RuntimeState == null) return;

	WeaponFireMode mode = ResolveEffectiveFireMode();
	if (mode == WeaponFireMode.FullAuto) { TryFireSingleShot(); return; }
	if (mode == WeaponFireMode.Burst) UpdateBurstFire(Time.time);
}

public void StartFiring()
{
	if (!IsConscious()) return;
	if (IsFireBlockedByBusyState()) { StopFiring(); return; }
	m_IsFiringCommandActive = true;
	WeaponFireMode fireMode = ResolveEffectiveFireMode();
	if (fireMode == WeaponFireMode.FullAuto || fireMode == WeaponFireMode.Burst) return;
	if (m_SemiShotConsumedForCurrentTrigger) return;
	WeaponShotAttemptResult result = TryFireSingleShot();
	if (result == WeaponShotAttemptResult.Success)
		m_SemiShotConsumedForCurrentTrigger = true;
}

public WeaponShotAttemptResult TryFireSingleShot()
{
	AmmoDefinition firedAmmoDefinition;
	WeaponShotAttemptResult result = TryFireSingleShotInternal(Time.time, out firedAmmoDefinition);
	m_LastShotAttemptResult = result;
	m_LastFiredAmmoDefinition = firedAmmoDefinition;

	if (result == WeaponShotAttemptResult.Success)
	{
		m_DebugSuccessfulShotCount++;
		m_HitscanShooting?.ProcessSuccessfulShot(firedAmmoDefinition);
		RegisterBurstSpreadShotIfNeeded();
		ShotFired?.Invoke(firedAmmoDefinition);
		// авто-перезарядка при пустом магазине
	}
	else if (m_TryReloadWhenOutOfAmmo && (result == EmptyMagazine || NoMagazine || NeedsBoltCycle))
		TryAutoReloadOrBoltCycle(result);

	return result;
}

private WeaponShotAttemptResult TryFireSingleShotInternal(float _currentTime, out AmmoDefinition _firedAmmoDefinition)
{
	_firedAmmoDefinition = null;
	if (m_WeaponRuntime == null) return WeaponShotAttemptResult.NoWeapon;
	if (!IsConscious()) return WeaponShotAttemptResult.Busy;
	if (!IsFireAllowedByWeaponPose()) return WeaponShotAttemptResult.NotReady;
	if (m_RequireReady && (m_ReadyHands == null || !m_ReadyHands.IsWeaponReadyToFire())) return WeaponShotAttemptResult.NotReady;
	if (IsFireBlockedByBusyState()) return WeaponShotAttemptResult.Busy;
	if (IsWeaponReloadBusy()) return WeaponShotAttemptResult.Busy;
	if (m_RequireVisibleTarget && !HasEngageableVisibleTarget()) return WeaponShotAttemptResult.NoVisibleTarget;
	if (!HasRequiredAimProgressForFire()) { m_DebugLastAimGateFail = "progress"; return WeaponShotAttemptResult.NotAimedProgress; }
	if (!IsBarrelAlignedEnoughToFire()) { m_DebugLastAimGateFail = "barrel"; return WeaponShotAttemptResult.NotAimed; }
	m_DebugLastAimGateFail = "ok";
	if (IsLineOfFireBlocked()) { /* suppress + rescan */ return WeaponShotAttemptResult.LineOfFireBlocked; }
	WeaponFireMode fireMode = ResolveEffectiveFireMode();
	return m_WeaponRuntime.TryConsumeShot(_currentTime, fireMode, out _firedAmmoDefinition);
}

public void StopFiring()
{
	m_IsFiringCommandActive = false;
	m_BurstShotsRemainingInWave = 0;
	m_NextBurstWaveTime = 0f;
	m_SemiShotConsumedForCurrentTrigger = false;
	ResetBurstSpreadCounter();
	if (ResetRecoilOnStopFiring)
		ResetRecoilAfterStopFiring();  // m_RecoilController.ResetRecoilPenalty(); m_WeaponRecoil.ResetVisualKick();
}

private bool IsFireAllowedByWeaponPose()
{
	if (m_Equipment != null && m_Equipment.IsOperatingVehicleTurret) return true;
	if (m_ReadyHands != null) return m_ReadyHands.CanFireFromSettledCombatPose();
	return false;
}

private bool IsFireBlockedByBusyState()
{
	if (m_BusyState == null) return false;
	return m_BusyState.HasReason(UnitBusyState.BusyReason.StanceTransition) ||
	       m_BusyState.HasReason(UnitBusyState.BusyReason.Reload) ||
	       m_BusyState.HasReason(UnitBusyState.BusyReason.Throw) ||
	       m_BusyState.HasReason(UnitBusyState.BusyReason.RocketLauncher) ||
	       m_BusyState.HasReason(UnitBusyState.BusyReason.SelfStabilization) ||
	       m_BusyState.HasReason(UnitBusyState.BusyReason.StabilizeOther) ||
	       m_BusyState.HasReason(UnitBusyState.BusyReason.ProximityRelax);
}

private bool IsBarrelAlignedEnoughToFire()
{
	if (!m_RequireBarrelAlignedToFire || !HasEngageableVisibleTarget()) return true;
	if (ShouldSkipBarrelAlignmentForBoltCycle()) return true;
	EquippedWeapon weapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
	Transform fireOrigin = weapon != null ? weapon.FireOriginTransform : null;
	if (fireOrigin == null) return false;
	Vector3 targetPoint = m_TargetSelector.GetEngageableAimPointWorld();
	if (targetPoint == Vector3.zero) targetPoint = m_TargetSelector.GetEngageableSelectedTarget().position;
	Vector3 toTarget = targetPoint - fireOrigin.position;
	if (toTarget.sqrMagnitude < 1e-6f) { m_DebugLastBarrelAimErrorDegrees = 0f; return true; }
	m_DebugLastBarrelAimErrorDegrees = Vector3.Angle(fireOrigin.forward, toTarget.normalized);
	float maxError = ResolveMaxBarrelAimErrorDegrees();
	return m_DebugLastBarrelAimErrorDegrees <= maxError;
}
```

`ResolveMaxBarrelAimErrorDegrees()` выбирает допуск по таблице: поза (Aiming / PointAim / HipFire) × стойка (стоя/присед-лёжа) × движение (стоит/идёт). Движение определяется через `UnitNavLocomotionDriver.HasMoveIntent` или `UnitClickToMove.HasMoveIntent`.

`HasRequiredAimProgressForFire()`: сравнивает `TransientState.AimProgress01` с порогом позы (`PreAimPoseUtility.GetPoseFireThreshold01(pose)`), порог может быть поднят огневой дисциплиной. Для авто-режимов порог проверяется только когда `GetNextBurstShotIndex() <= 1`.

`IsLineOfFireBlocked()`: SphereCast `Physics.SphereCastNonAlloc` (радиус `m_LineOfFireSafetyRadius`) из `FireOriginTransform.position` в точку прицеливания, буфер 16 хитов, игнор собственных коллайдеров; блокирует только дружественный/нейтральный юнит (через `UnitBodyHitZone` + `UnitTeam`); результат кэшируется на `m_LineOfFireBlockedRetrySeconds`.

`UpdateBurstFire()`: если `BurstShotsRemainingInWave <= 0` и время >= `NextBurstWaveTime` — инициализирует волну размером `BurstRounds` (или override от дисциплины, минимум 2) и сбрасывает счётчик разброса; `TryFireSingleShot()`; успех декрементирует волну, при обнулении ставит паузу `BurstPauseSeconds` и `SetAimProgress(0)`; неблокирующие ошибки (FireRateLimited/Busy/NeedsBoltCycle/NotAimed/NotAimedProgress/LineOfFireBlocked) пропускаются, прочие — сброс волны с паузой.

`ResolveEffectiveFireMode()`: приоритет — огневая дисциплина (`FireDisciplineController.TryGetEffectiveFireModeOverride`) → авто-выбор по дистанции (`HitscanShooting.TrySelectAutoModes`) → `WeaponRuntime.ResolveEffectiveFireMode(dist)`.

---

## 8. Процедурный цикл затвора — `UnitWeaponBoltCycleVisual`

Файл: `Assets/_Scripts/Shooting/UnitWeaponBoltCycleVisual.cs`. Работает только если у `EquippedWeapon` задан `BoltCarrier` и/или `DustCoverHinge`. Near-camera процедурный цикл.

### Параметры (на `EquippedWeapon`, дефолты)
| Поле | Дефолт | Смысл |
|---|---|---|
| `m_BoltOpenLocalOffset` | (0, 0, −0.08) | локальное смещение затвора в полностью открытом положении (обычно только −Z) |
| `m_BoltHandleOpenLocalEulerAngles` | (0,0,0) | euler открытого положения болтовой рукоятки; (0,0,0) = только линейный ход |
| `m_BoltHandleRotatePhaseNormalized` | 0.25 | доля фазы поворота рукоятки в цикле |
| `m_BoltCycleSeconds` | 0.085 | длительность цикла rest→open→rest в авто |
| `m_BoltCycleSecondsSingleShot` | 0.16 | длительность цикла при одиночном/передёргивании |
| `m_BoltActionCycleSeconds` | 0.55 | длительность болтового передёргивания (0 = использовать single-shot) |
| `m_BoltShellEjectNormalizedTime` | 0.5 | доля цикла, на которой спавнится гильза (кламп 0.15..0.85) |
| `m_DustCoverClosedDegrees` | −160 | угол закрытия крышки от rest-меша (rest = открыто) |
| `m_DustCoverHingeAxis` | (0,0,1) | ось шарнира крышки |
| `m_DustCoverTweenSeconds` | 0.12 | длительность открытия/закрытия крышки (дальше камеры — мгновенный snap) |

### Ключевые методы (выдержки)

```csharp
private void HandleShotFired(AmmoDefinition _ammo)
{
	BindWeaponVisuals(false);
	if (!HasConfiguredBoltOrDustCover()) return;
	if (UsesManualBoltCycleWeapon()) { m_DeferredShellAmmoFromShot = _ammo; return; } // болтовые: гильза при передёргивании
	if (m_DustCoverHinge != null) SetDustCoverDesiredOpen(true);
	if (m_BoltCarrier == null) return;
	if (!IsNearCameraForBoundWeapon()) return;
	bool holdOpen = ShouldHoldBoltOpenAfterShot();   // HasBoltHoldOpenDelay && !HasRoundInChamber
	StartFullBoltCycle(_ammo, holdOpen);
}

private void StartFullBoltCycle(AmmoDefinition _ammo, bool _holdOpen)
{
	if (m_BoltCarrier == null) return;
	if (m_BoltMotionMode == BoltMotionMode.FullCycle && !m_ShellEjectedThisCycle) TryEjectPendingShell();
	m_PendingShellAmmo = _ammo;
	m_ShellEjectedThisCycle = _ammo == null;
	m_BoltHoldOpen = _holdOpen;
	m_CloseDustCoverAfterBoltClose = false;
	m_BoltMotionMode = BoltMotionMode.FullCycle;
	m_BoltCycleElapsed = 0f;
	m_ActiveBoltCycleSeconds = ResolveBoltCycleSecondsForShot(_ammo == null);
	if (_holdOpen)
	{
		ApplyBoltOpenAmount(1f);
		TryEjectPendingShell();
		m_BoltMotionMode = BoltMotionMode.None; // затвор остаётся открытым (bolt catch)
	}
}

private float ResolveBoltCycleSecondsForShot(bool _presentationCycle)
{
	if (_presentationCycle) return Mathf.Max(0.02f, m_BoltCycleSecondsSingleShot);
	WeaponFireMode fireMode = m_FireController != null ? m_FireController.ResolveEffectiveFireMode() : WeaponFireMode.SemiAuto;
	bool automaticBurst = WeaponFireModeUtility.IsAutomaticEffectiveMode(fireMode) &&
		m_FireController != null && m_FireController.IsFiringCommandActive;
	return Mathf.Max(0.02f, automaticBurst ? m_BoltCycleSecondsAuto : m_BoltCycleSecondsSingleShot);
}

private void UpdateBoltCycle(float _deltaTime)
{
	if (m_BoltMotionMode == BoltMotionMode.None || m_BoltCarrier == null) return;
	float cycleSeconds = Mathf.Max(0.02f, m_ActiveBoltCycleSeconds);
	m_BoltCycleElapsed += _deltaTime;

	if (m_BoltMotionMode == BoltMotionMode.CloseFromOpen)
	{
		float closeSeconds = cycleSeconds * 0.5f;
		float normalized = Mathf.Clamp01(m_BoltCycleElapsed / closeSeconds);
		ApplyBoltOpenAmount(Mathf.SmoothStep(1f, 0f, normalized));
		if (normalized < 1f) return;
		FinishCloseFromOpen();
		return;
	}

	if (m_BoltMotionMode == BoltMotionMode.BoltActionHandleCycle)
	{
		UpdateBoltActionHandleCycle(cycleSeconds);
		return;
	}

	float fullNormalized = Mathf.Clamp01(m_BoltCycleElapsed / cycleSeconds);
	ApplyBoltOpenAmount(EvaluateBoltOpenAmount(fullNormalized));
	if (!m_ShellEjectedThisCycle && fullNormalized >= m_BoltShellEjectNormalizedTime) TryEjectPendingShell();
	if (fullNormalized < 1f) return;
	m_BoltMotionMode = BoltMotionMode.None;
	if (m_BoltHoldOpen) ApplyBoltOpenAmount(1f);
	else ResetBoltToRest(false);
}

private static float EvaluateBoltOpenAmount(float _normalized)
{
	// треугольник: 0→1 на первой половине, 1→0 на второй (обе SmoothStep)
	if (_normalized <= 0.5f) return Mathf.SmoothStep(0f, 1f, _normalized * 2f);
	return Mathf.SmoothStep(1f, 0f, (_normalized - 0.5f) * 2f);
}

private void ApplyBoltOpenAmount(float _open01)
{
	if (m_BoltCarrier == null) return;
	float open01 = Mathf.Clamp01(_open01);
	m_BoltCarrier.localPosition = m_BoltRestLocalPosition + m_BoltOpenLocalOffset * open01;
	if (UsesBoltHandleRotation())
		m_BoltCarrier.localRotation = m_BoltRestLocalRotation * Quaternion.Euler(m_BoltHandleOpenLocalEulerAngles * open01);
	else
		m_BoltCarrier.localRotation = m_BoltRestLocalRotation;
}
```

Болтовой цикл (`UpdateBoltActionHandleCycle`): фазы `rotate open → slide back → eject → slide forward → rotate close`.
Фазы: `rotateOpenEnd = rotatePhase` (кламп 0.05..0.45), `slideOpenEnd = rotateOpenEnd + slidePhase` (slidePhase = max(0.05, 0.5−rotatePhase)), `slideCloseEnd = slideOpenEnd + slidePhase`, остаток — поворот закрытия. Выброс гильзы на `slideOpenEnd`.

Крышка (dust cover):
```csharp
private void SetDustCoverDesiredOpen(bool _open)
{
	if (m_DustCoverHinge == null) return;
	float target = ResolveDustCoverTargetAngle(_open); // open→0°, closed→m_DustCoverClosedDegrees
	// если далеко от камеры — мгновенный snap, иначе твин
}
private void UpdateDustCover(float _deltaTime)
{
	if (m_DustCoverHinge == null || !m_DustCoverTweenActive) return;
	float target = ResolveDustCoverTargetAngle(m_DustCoverDesiredOpen);
	float duration = Mathf.Max(0.01f, m_DustCoverTweenSeconds);
	float travel = Mathf.Max(0.01f, Mathf.Abs(m_DustCoverClosedDegrees));
	float step = travel / duration * _deltaTime;
	m_DustCoverAngleDegrees = Mathf.MoveTowards(m_DustCoverAngleDegrees, target, step);
	ApplyDustCoverAngle(m_DustCoverAngleDegrees, false);
	// достигли target → snap и выключить твин
}
private void ApplyDustCoverAngle(float _degrees, bool _force)
{
	m_DustCoverAngleDegrees = _degrees;
	m_DustCoverHinge.localRotation = Quaternion.AngleAxis(_degrees, axis);
}
```

`WillHandlePhysicalShellEjection`: true для болтовых (гильза всегда через передёргивание); для автоматов — true если есть `BoltCarrierTransform` И профиль/дистанция требуют физическую гильзу (тогда `UnitWeaponShellEjection` НЕ спавнит гильзу на событие выстрела, гильзу спавнит цикл затвора в момент открытия).

`BindWeaponVisuals(resetState)`: привязка к `EquippedWeapon` — копирует `BoltCarrierTransform`, `BoltOpenLocalOffset`, euler рукоятки, фазы, тайминги, фиксирует `BoltRestLocalPosition/Rotation` как текущий локальный трансформ затвора, читает параметры крышки.

---

## 9. Остальные визуальные компоненты выстрела

### 9.1 `UnitWeaponMuzzleVfx` — полный листинг
Файл: `Assets/_Scripts/Shooting/UnitWeaponMuzzleVfx.cs` (дефолты пула: 6/24).

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

[DisallowMultipleComponent]
[DefaultExecutionOrder(58)]
public sealed class UnitWeaponMuzzleVfx : MonoBehaviour
{
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField, Min(1)] private int m_DefaultPoolCapacity = 6;
	[SerializeField, Min(1)] private int m_MaxPoolSize = 24;

	private readonly Dictionary<GameObject, ObjectPool<GameObject>> m_Pools = new Dictionary<GameObject, ObjectPool<GameObject>>(2);

	// Awake: GetComponent-резолв. OnEnable/OnDisable: подписка на ShotFired.

	private void HandleShotFired(AmmoDefinition _ammo)
	{
		WeaponVfxProfile profile = WeaponVfxUtility.GetCurrentProfile(m_WeaponRuntime);
		if (profile == null || !profile.EnableMuzzleFlash) return;

		EquippedWeapon weapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		Transform fireOrigin = weapon != null ? weapon.FireOriginTransform : null;
		if (fireOrigin == null) return;

		if (!WeaponVfxUtility.IsWithinEffectDistance(fireOrigin.position, profile.MuzzleFlashMaxDistanceMeters)) return;
		if (!CombatVfxBudgetService.TryAcquire(CombatVfxBudgetService.Category.MuzzleFlash)) return;

		bool suppressed = WeaponVfxUtility.HasSuppressor(m_WeaponRuntime);
		GameObject prefab = suppressed && profile.SuppressedMuzzleFlashPrefab != null
			? profile.SuppressedMuzzleFlashPrefab
			: profile.UnsuppressedMuzzleFlashPrefab;
		if (prefab == null) { CombatVfxBudgetService.Release(...); return; }

		float scale = suppressed ? profile.SuppressedMuzzleScale : profile.UnsuppressedMuzzleScale;
		float lifetime = suppressed ? profile.SuppressedMuzzleLifetimeSeconds : profile.UnsuppressedMuzzleLifetimeSeconds;
		SpawnEffect(prefab, fireOrigin.position, fireOrigin.rotation, Vector3.one * scale, lifetime);
	}

	private void SpawnEffect(GameObject _prefab, Vector3 _position, Quaternion _rotation, Vector3 _scale, float _lifetime)
	{
		ObjectPool<GameObject> pool = GetOrCreatePool(_prefab);
		GameObject instance = pool.Get();
		instance.transform.SetPositionAndRotation(_position, _rotation);
		instance.transform.localScale = _scale;
		WeaponVfxUtility.PlayParticleSystems(instance);
		WeaponVfxRuntimeRelease.StartRelease(pool, instance, CombatVfxBudgetService.Category.MuzzleFlash, _lifetime, _waitForParticles: true);
	}
}
```

### 9.2 `UnitWeaponShellEjection` — полный листинг
Файл: `Assets/_Scripts/Shooting/UnitWeaponShellEjection.cs` (дефолты пула 12/48; AudioSource — дочерний `ShellImpactAudio_Auto`, spatial 35 м).

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

[DisallowMultipleComponent]
[DefaultExecutionOrder(57)]
public sealed class UnitWeaponShellEjection : MonoBehaviour
{
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitWeaponBoltCycleVisual m_BoltCycleVisual;
	[SerializeField] private Transform m_PoolRoot;
	[SerializeField] private AudioSource m_ImpactAudio;
	[SerializeField, Min(1)] private int m_DefaultPoolCapacity = 12;
	[SerializeField, Min(1)] private int m_MaxPoolSize = 48;

	private readonly Dictionary<EntityId, ObjectPool<GameObject>> m_Pools = new Dictionary<EntityId, ObjectPool<GameObject>>(8);

	// Awake: резолв + EnsurePoolRoot (дочерний "ShellCasingPool") + EnsureImpactAudio.
	// OnEnable/OnDisable: подписка на ShotFired.

	public void SpawnShellForAmmo(AmmoDefinition _ammo) { SpawnShellInternal(_ammo); } // для затвора/снятия отказа

	private void HandleShotFired(AmmoDefinition _ammo)
	{
		if (m_BoltCycleVisual != null && m_BoltCycleVisual.WillHandlePhysicalShellEjection)
			return; // гильзу выбросит цикл затвора
		SpawnShellInternal(_ammo);
	}

	private void SpawnShellInternal(AmmoDefinition _ammo)
	{
		if (_ammo == null || !_ammo.HasShellPrefab) return;
		WeaponVfxProfile profile = WeaponVfxUtility.GetCurrentProfile(m_WeaponRuntime);
		GameObject prefab = _ammo.ShellPrefab;
		EquippedWeapon weapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		if (weapon == null) return;
		if (!WeaponVfxUtility.TryGetShellEjectionPose(weapon, out Vector3 pos, out Vector3 dir)) return;
		if (!WeaponVfxUtility.ShouldUsePhysicalShellEjection(profile, pos)) return;

		float speed = _ammo.ShellEjectSpeed + Random.Range(-_ammo.ShellEjectSpeedVariance, _ammo.ShellEjectSpeedVariance);
		speed = Mathf.Max(0.1f, speed);
		Vector3 vel = dir * speed + Vector3.up * _ammo.ShellEjectUpSpeed;
		Vector3 angVel = Random.insideUnitSphere * _ammo.ShellAngularVelocity;

		ObjectPool<GameObject> pool = GetOrCreatePool(prefab);
		GameObject shell = pool.Get();
		ShellCasingBehaviour behaviour = shell.GetComponentInChildren<ShellCasingBehaviour>(true);
		if (behaviour == null) { pool.Release(shell); Debug.LogWarning(...); return; }

		Quaternion rot = Random.rotationUniform;
		behaviour.ActivateFromPool(pool, shell, m_ImpactAudio, _ammo, pos, rot, vel, angVel, transform);
	}
	// GetOrCreatePool: actionOnRelease возвращает в PoolRoot и обнуляет linearVelocity/angularVelocity Rigidbody.
}
```

### 9.3 `UnitWeaponTriggerFingerDriver` — полный листинг
Файл: `Assets/_Scripts/Shooting/UnitWeaponTriggerFingerDriver.cs`. Пишет float-параметр `TriggerPress` в Animator (дефолт: `m_DriveAnimatorParameter = true`, `m_FallSmoothTime = 0.3`).

```csharp
[DisallowMultipleComponent]
[DefaultExecutionOrder(58)]
public sealed class UnitWeaponTriggerFingerDriver : MonoBehaviour
{
	public const string ParamTriggerPress = "TriggerPress";
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private Animator m_Animator;
	[SerializeField] private bool m_DriveAnimatorParameter = true;
	[SerializeField] private string m_AnimatorParameterName = ParamTriggerPress;
	[SerializeField, Min(0.02f)] private float m_FallSmoothTime = 0.3f;

	private int m_ParameterHash;
	private float m_Trigger01;
	private float m_FallVelocity;

	// Awake: резолв + ResolveParameterHash (StringToHash). Подписка на ShotFired.

	private void Update()
	{
		if (!m_DriveAnimatorParameter || m_Animator == null) return;
		float smooth = Mathf.Max(0.02f, m_FallSmoothTime);
		m_Trigger01 = Mathf.SmoothDamp(m_Trigger01, 0f, ref m_FallVelocity, smooth, Mathf.Infinity, Time.deltaTime);
		if (m_Trigger01 < 0.0005f) { m_Trigger01 = 0f; m_FallVelocity = 0f; }
		SetAnimatorTrigger01(m_Trigger01);
	}

	private void HandleShotFired(AmmoDefinition _ammo)
	{
		if (!isActiveAndEnabled) return;
		m_Trigger01 = Mathf.Max(m_Trigger01, 1f);   // очередь не «дёргает» палец вниз между патронами
		m_FallVelocity = 0f;
	}

	private void SetAnimatorTrigger01(float _value)
	{
		if (!m_DriveAnimatorParameter || m_Animator == null || !m_Animator.isInitialized) return;
		m_Animator.SetFloat(m_ParameterHash, _value);
	}
}
```

### 9.4 `UnitWeaponFireAudio` — ключевые методы
Файл: `Assets/_Scripts/Shooting/UnitWeaponFireAudio.cs`. Дефолты: `m_SpatialMinDistance = 1`, `m_SpatialMaxDistance = 125`; константа `c_SubsonicSuppressedVolumeMultiplier = 0.5`; хвост звука — `TailThresholdSeconds` через корутину, громкость хвоста ×0.6, pitch = 1±`FirePitchVariance` (дефолт 0.04).

```csharp
private void HandleShotFired(AmmoDefinition _ammo)
{
	if (m_WeaponRuntime == null) return;
	WeaponDefinition weapon = m_WeaponRuntime.CurrentWeaponDefinition;
	WeaponRuntimeState runtimeState = m_WeaponRuntime.RuntimeState;
	float volumeMultiplier = 1f;
	WeaponAttachmentDefinition suppressor = TryGetEquippedSuppressor(runtimeState);
	WeaponFireSoundProfile profile = ResolveFireSoundProfile(_ammo, weapon, suppressor, ref volumeMultiplier);

	Vector3 pos = ResolveBarrelPosition(); // FireOriginTransform.position или transform.position
	float baseVolume = (weapon != null ? weapon.FireSoundVolume : 1f) * volumeMultiplier;
	float pitch = ResolvePitch(weapon);

	if (profile != null && profile.TryPickClip(out AudioClip clip))
	{
		float maxDistance = profile.ResolveMaxAudibleDistance(m_SpatialMaxDistance);
		CombatAudioManager.TryPlayGunshot(clip, pos, baseVolume, pitch, maxDistance, transform, m_SpatialMinDistance, weaponSignatureId);
	}
	if (profile != null && profile.HasAnyTailClips) { /* корутина PlayTailAfterDelay */ }
}
```

Приоритет профиля звука: `Ammo.FireSoundOverrideProfile` (если есть клипы) → выделенный suppressed-профиль оружия/глушителя (дозвуковые патроны ×0.5) → иначе `SuppressedFireVolumeMultiplier` глушителя → основной `Weapon.FireSoundProfile`.

### 9.5 `UnitWeaponBulletFlightVfx` — трассер
Файл: `Assets/_Scripts/Shooting/UnitWeaponBulletFlightVfx.cs`. Подписан на `UnitWeaponHitscanShooting.ShotTrace` (не на ShotFired). Геймплей остаётся hitscan; визуальный mesh летит от дула к точке попадания.

```csharp
private void HandleShotTrace(WeaponShotTraceInfo _trace)
{
	if (_trace.HitSelf) return;
	WeaponVfxProfile profile = WeaponVfxUtility.GetCurrentProfile(m_WeaponRuntime);
	if (profile == null || !profile.EnableBulletFlight) return;

	int shotIndex = m_WeaponRuntime?.TransientState?.GetNextBurstShotIndex() ?? 0;
	if (profile.TracerEveryNShot > 1 && shotIndex % profile.TracerEveryNShot != 0) return;
	bool isEnhanced = profile.EnhancedTracerEveryNShot > 0 && shotIndex % profile.EnhancedTracerEveryNShot == 0;
	if (!_trace.HasHit && !profile.ShowBulletFlightOnMiss) return;

	WeaponVfxQualityTier tier = WeaponVfxUtility.ResolveEffectQualityTier(profile, samplePosition, profile.BulletFlightMaxDistanceMeters);
	if (tier == WeaponVfxQualityTier.Skip) return;
	GameObject prefab = profile.BulletFlightPrefab;
	if (prefab == null) return;
	float distance = Vector3.Distance(_trace.Origin, _trace.EndPoint);
	if (distance <= 0.001f) return;
	if (!CombatVfxBudgetService.TryAcquire(CombatVfxBudgetService.Category.BulletTrail)) return;

	float ammoVelocity = _trace.Ammo != null ? _trace.Ammo.Velocity : 400f;
	float flightSeconds = profile.ComputeBulletFlightSeconds(distance, ammoVelocity);
	if (tier == WeaponVfxQualityTier.Reduced) flightSeconds *= 0.65f;
	// спавн из пула, корутина AnimateFlight: t.position = Lerp(origin, endPoint, elapsed/flightSeconds)
}
```

### 9.6 `UnitWeaponImpactVfx` — эффекты попадания
Файл: `Assets/_Scripts/Shooting/UnitWeaponImpactVfx.cs`. Подписан на `ShotTrace`. По `WeaponShotImpactVfxKind`: `Flesh`/`ArmorDeflect` — body impact (спавн частиц со смещением по нормали `BodyImpactSurfaceOffset`), иначе — по Physics Material подбирается `WeaponImpactSurfaceSet` → декаль + звук поверхности (`ImpactAudioMaxDistance = 45`). Пороги: `ImpactFxMaxDistanceMeters` (дефолт 30), `DecalMaxDistanceMeters` (дефолт 20).

---

## 10. Hitscan и процедурный подъём траектории (не двигает модель оружия)

`UnitWeaponHitscanShooting` (файл `Assets/_Scripts/Shooting/UnitWeaponHitscanShooting.cs`) вызывается из `FireController` до `ShotFired`. Модель оружия не двигает, но важно для понимания, куда летят пули в очереди.

Параметры (дефолты): `m_HitLayers = ~0`, `m_MaxDistance = 500`, `m_BarrelRayStartOffset = 0.08`, `m_TriggerInteraction = Ignore`, `m_TargetLeadFactor = 1`, `m_BaseSpreadToDegrees = 0.35`, `m_RecoilSpreadScale = 0.22`, `m_MinHalfAngleDegrees = 0.04`, `m_MaxHalfAngleDegrees = 12`, `m_AutoSpreadMultiplier = 0.869565`, `m_RecoilPatternPitchDegreesPerPenaltyUnit = 0.09`, `m_RecoilPatternYawFraction = 0.55`, `m_RecoilPatternChaosFraction = 0.22`, `m_FullAutoRecoilControlStartShot = 5`, `m_FullAutoRecoilControlEndShot = 10`, `m_FullAutoControlledPitchScale = 0.38`, `m_FullAutoControlledYawReferenceScale = 1`, `m_FullAutoControlledYawBoost = 1.2`, `m_FullAutoControlledYawFraction = 1.05`, `m_FullAutoRecoilControlSkillInfluence = 0.65`, стойки: стоя ×1, присед ×0.9, лёжа ×0.75, движение ×1.35, спринт ×2, `m_UseDistanceFalloff = true`, `m_FalloffZeroRangeMultiplier = 2`.

```csharp
private void HandleShotFired(AmmoDefinition _ammo)
{
	// 1) origin = FireOriginTransform.position + forward * BarrelRayStartOffset
	// 2) baseDirection = GetGameplayShotDirection(...)  — на точку прицеливания + упреждение по скорости цели
	// 3) patternResult = ApplyProceduralRecoilPattern(baseDirection, accuracyContext) — авто-очередь со 2-го выстрела
	// 4) разброс: ApplyConeSpread(patternedDirection, halfAngle) или дробовой паттерн
	// 5) TryHit → RaycastAll, сортировка по дистанции, урон, ShotTrace (событие для трассеров/импакта)
}

private ProceduralRecoilPatternResult ApplyProceduralRecoilPattern(Vector3 _baseDirection, WeaponShotAccuracyContext _accuracyContext)
{
	Vector3 normalizedBase = _baseDirection.normalized;
	if (!WeaponFireModeUtility.IsAutomaticEffectiveMode(_accuracyContext.EffectiveFireMode)) return CreateUnchanged(normalizedBase);
	if (WeaponFireModeUtility.IsFirstShotInAutomaticSeries(_accuracyContext.EffectiveFireMode, _accuracyContext.BurstShotIndex)) return CreateUnchanged(normalizedBase);
	if (m_WeaponRuntime == null || m_WeaponRuntime.TransientState == null) return CreateUnchanged(normalizedBase);
	float recoilPenalty = m_WeaponRuntime.TransientState.RecoilPenalty;
	if (recoilPenalty <= 0.0001f) return CreateUnchanged(normalizedBase);

	int shotIndex = Mathf.Max(1, _accuracyContext.BurstShotIndex);
	float basePitchDegrees = recoilPenalty * m_RecoilPatternPitchDegreesPerPenaltyUnit * m_AutoSpreadMultiplier;
	float controlBlend = CalculateFullAutoRecoilControlBlend(_accuracyContext.EffectiveFireMode, shotIndex);
	float pitchScale = Mathf.Lerp(1f, m_FullAutoControlledPitchScale, controlBlend);
	float pitchDegrees = basePitchDegrees * pitchScale;

	float yawReferencePitch = basePitchDegrees * Mathf.Lerp(1f, m_FullAutoControlledYawReferenceScale, controlBlend);
	float yawFraction = Mathf.Lerp(m_RecoilPatternYawFraction, m_FullAutoControlledYawFraction, controlBlend);
	float yawBoost = Mathf.Lerp(1f, m_FullAutoControlledYawBoost, controlBlend);
	float yawDegrees = CalculateProceduralPatternYaw(shotIndex, yawReferencePitch, yawFraction) * yawBoost;

	// up = world up (или forward при вертикальном стволе), right = cross(up, forward)
	Quaternion patternRotation = Quaternion.AngleAxis(yawDegrees, up) * Quaternion.AngleAxis(-pitchDegrees, right);
	return new ProceduralRecoilPatternResult((patternRotation * forward).normalized, recoilPenalty, pitchDegrees, yawDegrees, true);
}

private float CalculateProceduralPatternYaw(int _shotIndex, float _pitchDegrees, float _yawFraction)
{
	float seed = weaponDefinition != null ? Mathf.Abs(weaponDefinition.GetEntityId().GetHashCode() % 997) * 0.01f : 0f;
	float mainWave = Mathf.Sin(_shotIndex * 1.73f + seed);
	float chaosWave = Mathf.Sin(_shotIndex * 0.47f + seed * 2.31f) * m_RecoilPatternChaosFraction;
	return (mainWave + chaosWave) * _pitchDegrees * _yawFraction;
}

private float CalculateFullAutoRecoilControlBlend(WeaponFireMode _effectiveFireMode, int _shotIndex)
{
	// только FullAuto; shotBlend = InverseLerp(5, 10, shotIndex);
	// skill01 = InverseLerp(0, 100, CombatStats.RecoilControl)
	// return clamp01(lerp(shotBlend, shotBlend * skill01, m_FullAutoRecoilControlSkillInfluence(0.65)))
}

private static Vector3 ApplyConeSpread(Vector3 _forward, float _halfAngleDegrees)
{
	Vector3 f = _forward.normalized;
	if (_halfAngleDegrees <= 0.0001f) return f;
	float tan = Mathf.Tan(_halfAngleDegrees * Mathf.Deg2Rad);
	Vector2 rnd = Random.insideUnitCircle * tan;
	// up/right базис относительно forward; результат нормализуется
}

private Vector3 GetGameplayShotDirection(Vector3 _origin, Transform _fireOrigin, AmmoDefinition _ammo)
{
	// направление на GetEngageableAimPointWorld() с упреждением:
	// leadOffset = targetVelocity * (distance / ammo.Velocity * m_TargetLeadFactor)
	// fallback: _fireOrigin.forward
}
```

Полуугол конуса разброса собирается в `WeaponShotAccuracyEvaluator.Evaluate(...)` из множителей: база (`BaseShotDispersion × BaseSpreadToDegrees`), дистанция, патрон, модули, `RecoilPenalty × RecoilSpreadScale`, стойка, движение, скилл, состояние, поза (HipFire/PointAim/PreAim через `WeaponPoseDistanceCurves`/`WeaponPoseAutoCapabilityBaker`), `AimProgress01`, кламп `[MinHalfAngleDegrees, MaxHalfAngleDegrees]`, авто-множитель `AutoSpreadMultiplier`.

---

## 11. IK рук и следование левой кисти за recoil — `AnimatorHandIk`

Файл: `Assets/_Scripts/Inventory/AnimatorHandIk.cs`, `[RequireComponent(typeof(Animator))]`, порядок 250.

Конвейер:
- `OnAnimatorIK`: `TickHandIkPipeline()` (выбор режима и весов через `UnitHandIkModeResolver`), затем применение позиций/вращений кистей к целям IK (грип-риг оружия / турели / гранатомёта).
- `LateUpdate`: твин весов и **снап левой кисти** через two-bone IK (`ApplyTwoBoneIk`) — после анимации, после visual recoil (200) и после `UnitWeaponArmRecoil` (220), поэтому левая кисть следует за «откинутым» оружием. Правую кисть снапать запрещено: оружие — ребёнок Hand_R, снап правой кисти создал бы feedback-loop. `UnitWeaponArmRecoil` не вызывает этот `ApplyTwoBoneIk`.
- Веса по умолчанию: левая позиция/вращение 1/1, правая 1/1, `RightHandNotReadyIkWeight = 1`; на беге левая 1, правая 0; подъём/спад левой 0.07/0.12 с, правой 0.08/0.1 с; порог снапа левой 0.85; `MaxGripErrorMeters = 0.12` (лог `[IK-GRIP-ERROR]`), `RightTargetJumpLogMeters = 0.08` (лог `[IK-TARGET-JUMP]`).

```csharp
private void SnapHandsToGripRigAfterAnimation()
{
	EnsureGripResolver();
	if (m_GripResolver == null || !m_GripResolver.HasGripRig) return;
	HandIkState state = m_GripResolver.CurrentState;
	if (!state.HasLeft) return;
	float leftBlend = GetEquipBlendMultiplier();
	if (m_LeftHandPositionWeight * m_CurrentLeftWeight * leftBlend <= 0.01f) return;
	SnapHandBoneToWorldTarget(HumanBodyBones.LeftHand, _leftHand: true, state.LeftTarget.position, state.LeftTarget.rotation);
}

private static void ApplyTwoBoneIk(Transform _upper, Transform _lower, Transform _hand, Vector3 _targetPos, Quaternion _targetRot)
{
	// классическая двухкостная IK: длину удерживает, локоть — по полюсу из текущего смещения локтя,
	// верхняя кость доворачивается FromToRotation, нижняя — FromToRotation, кисть — SetPositionAndRotation.
}
```

Правая кисть: вес правого IK (`CurrentRightWeight`, дефолт в Hold — из GripRig `RightWeight`, fallback 0.35) применяется в `OnAnimatorIK` (не LateUpdate-снапом), и в этом же кадре поверх результата анимации+IK накладывается recoil-оверлей.

---

## 12. Данные оружия — `EquippedWeapon` (якоря и тайминги визуала)

Файл: `Assets/_Scripts/Inventory/EquippedWeapon.cs`. Ключевые свойства:

```csharp
public Transform BarrelTransform;      // m_Barrel ?? m_MuzzleModuleVisualSocket ?? transform
public Transform FireOriginTransform;  // "MuzzleExit" на визуале дульного модуля → дочерний "MuzzleExit" → BarrelTransform
public Transform ShellEjectTransform;  // точка выброса гильзы (null — эвристика от ствола)
public Transform SightPivotTransform;  // прицел для конуса зрения
public Transform BoltCarrierTransform; // затворная рама (null — нет процедурного цикла)
public Vector3 BoltOpenLocalOffset;            // дефолт (0, 0, -0.08)
public Vector3 BoltHandleOpenLocalEulerAngles; // дефолт zero
public float BoltHandleRotatePhaseNormalized;  // дефолт 0.25
public float BoltCycleSeconds;                 // дефолт 0.085 (авто)
public float BoltCycleSecondsSingleShot;       // дефолт 0.16
public float BoltActionCycleSeconds;           // дефолт 0.55
public float BoltShellEjectNormalizedTime;     // дефолт 0.5
public Transform DustCoverHingeTransform;      // дефолт null
public float DustCoverClosedDegrees;           // дефолт -160
public Vector3 DustCoverHingeAxis;             // дефолт forward
public float DustCoverTweenSeconds;            // дефолт 0.12
public const string MuzzleExitTransformName = "MuzzleExit";
// m_VisualRecoilKickPivot — УСТАРЕЛО: kick в корень оружия больше не пишется, recoil идёт в Hand_R.
```

`ResolveFireOriginTransform()` (выдержка):

```csharp
private Transform ResolveFireOriginTransform()
{
	Transform muzzleExit = ResolveMuzzleExitTransform(); // FindChildRecursive(m_MuzzleAttachmentVisualInstance, "MuzzleExit")
	if (muzzleExit != null) return muzzleExit;
	Transform directMuzzle = transform.Find(MuzzleExitTransformName);
	if (directMuzzle != null) return directMuzzle;
	return BarrelTransform;
}
```

---

## 13. Профиль VFX — `WeaponVfxProfile` (все поля с дефолтами)

Файл: `Assets/_Scripts/Shooting/WeaponVfxProfile.cs`. Привязывается к `WeaponDefinition.VfxProfile`; без профиля юнит не спавнит специфичные FX.

| Группа | Поле | Дефолт |
|---|---|---|
| Muzzle Flash | `EnableMuzzleFlash` | true |
| | `UnsuppressedMuzzleFlashPrefab` / `SuppressedMuzzleFlashPrefab` | null |
| | `UnsuppressedMuzzleLifetimeSeconds` / `SuppressedMuzzleLifetimeSeconds` | 0.18 / 0.12 |
| | `UnsuppressedMuzzleScale` / `SuppressedMuzzleScale` | 1 / 0.35 |
| Shell Ejection | `ShellEjectionMode` (Physical/Particle/Hybrid) | Physical |
| | `ShellParticlePrefab`, `ShellParticleLifetimeSeconds` | null / 2.5 |
| | `ShellParticleScale` | 2 |
| | `ShellPrefabEjectionAxis` / `ShellLocalEulerOffset` | right / zero |
| | `HybridPhysicalShellDistanceMeters` | 12 |
| Bullet Flight | `EnableBulletFlight` | true |
| | `BulletFlightScale` / `BulletFlightLengthScale` | 1 / 0.2 |
| | `BulletVisualSpeedMultiplier` | 0.35 |
| | `BulletMinFlightSeconds` / `BulletMaxFlightSeconds` | 0.045 / 0.85 |
| | `ShowBulletFlightOnMiss` | true |
| Body Impact | `EnableBodyImpactFx` | true |
| | `ArmorDeflectImpactLifetimeSeconds` / `FleshImpactLifetimeSeconds` | 0.35 / 0.8 |
| | `ArmorDeflectImpactScale` / `FleshImpactScale` | 1 / 0.2 |
| | `BodyImpactSurfaceOffset` | 0.01 |
| Impact Surfaces | `EnableImpactDecals` / `EnableImpactAudio` | true / true |
| | `DefaultSurfaceName` | "Concrete" |
| | `DecalSurfaceOffset` / `DecalScale` / `DecalLifetimeSeconds` | 0.012 / 0.45 / 20 |
| | `ImpactAudioMaxDistance` | 45 |
| Distance LOD | `MuzzleFlashMaxDistanceMeters` | 50 |
| | `ImpactFxMaxDistanceMeters` | 30 |
| | `BulletFlightMaxDistanceMeters` | 40 |
| | `DecalMaxDistanceMeters` | 20 |
| Quality Tiers | `EffectNearQualityDistanceMeters` / `EffectMidQualityDistanceMeters` | 15 / 35 |
| | `ReducedParticleScaleMultiplier` / `ReducedMaxParticlesMultiplier` | 0.6 / 0.35 |
| | `ReducedMuzzleScaleMultiplier` / `ReducedBulletFlightScaleMultiplier` / `ReducedDecalLifetimeMultiplier` | 0.55 / 0.7 / 0.5 |
| Turret/Heavy | `TracerEveryNShot` / `EnhancedTracerEveryNShot` / `EnhancedTracerScaleMultiplier` | 1 / 0 / 1.5 |

```csharp
public float ComputeBulletFlightSeconds(float _distanceMeters, float _ammoVelocityMetersPerSecond)
{
	float distance = Mathf.Max(0f, _distanceMeters);
	if (distance <= 0.0001f) return 0f;
	float velocity = Mathf.Max(0.1f, _ammoVelocityMetersPerSecond) * Mathf.Max(0.05f, m_BulletVisualSpeedMultiplier);
	float seconds = distance / velocity;
	if (m_BulletMinFlightSeconds > 0f) seconds = Mathf.Max(seconds, m_BulletMinFlightSeconds);
	if (m_BulletMaxFlightSeconds > 0f) seconds = Mathf.Min(seconds, m_BulletMaxFlightSeconds);
	return seconds;
}
```

Режимы гильз (`WeaponShellEjectionVisualMode`): `Physical` — всегда физическая гильза; `Particle` — только частицы (`UnitWeaponParticleShellEjection`, порядок 58, тот же `ShotFired`); `Hybrid` — физическая в радиусе `HybridPhysicalShellDistanceMeters` от камеры, иначе частицы.

---

## 14. Условия, блокирующие всю визуальную отдачу (сводка для диагностики)

Визуальный kick не происходит, если выполняется **любое** из:
1. `UnitRagdollController.ShouldBlockWeaponPoseScripts == true` (ragdoll).
2. Активен рантайм-тюнер позы (`UnitEquippedWeaponPoseRuntimeTuner.IsTuningActive`).
3. `UnitEquipment.IsWeaponHeldForBoltCycle == true` (болтовое передёргивание).
4. `UnitEquipment.IsOperatingVehicleTurret == true` (у турели свой recoil — `VehicleTurretWeaponRecoil`, порядок 200).
5. `UnitEquipment.MainWeaponRoot == null` (нет оружия в руках).
6. `UnitEquipment.RightHandAnchor == null`.
7. Юнит дальше `HybridPhysicalShellDistanceMeters` (12 м) от камеры (если не `IgnoreCameraDistanceCull`).

`UnitWeaponArmRecoil` использует те же пункты 1–6 через `ShouldApplyOverlayThisFrame()`. Дистанция у руки своя: Full 0–12 м, Light 12–25 м, Off 25+ м — kick ствола при этом всё равно гасится пунктом 7.

Геймплейный `RecoilPenalty` НЕ зависит от пунктов 1–7 и продолжает накапливаться/восстанавливаться, пока есть оружие.

---

---

## 15. Диагностическое логирование позы оружия на выстрел (ДОБАВЛЕНО)

### 15.1 Что добавлено
1. Логирование встроено в существующий компонент диагностики **`UnitWeaponPoseSweepTest`**
   (файл `Assets/_Scripts/Unit/UnitWeaponPoseSweepTest.cs`) — тот, что уже висит на юните
   и запускается клавишей **L** (new Input System, `Key.L`) для выбранного юнита.
   Новый флаг **`m_LogWeaponShotPose`** (по умолчанию true) печатает на каждый выстрел
   прогона подробный лог изменения позы оружия с тегом **`[WeaponVisDiag]`**.
2. В `UnitWeaponRecoil` добавлены публичные геттеры параметров (см. §6.1): `ShotPitchDegrees`,
   `ShotYawScale`, `YawBias`, `ShotSmoothTime`, `DecayWhileFiringMultiplier`, `MaxShotImpulse`,
   `MaxShotYawDegrees`, `ShotImpulse`,
   `BackScale`, `UpScale`, `HandPitch`, `HandYaw`, `HandBack`, `HandUp`.

### 15.2 Как запустить
1. В Play Mode выделить юнит (в прогоне требуется `m_RequireSelected = true` и выбранная цель).
2. Нажать **L** — `UnitWeaponPoseSweepTest.StartSweep()` запускает матрицу прогона:
   Standing/Crouch × Idle/Walk × LowReady/HighReady/PreAim (SKIP-FIRE) × HipFire/PointAim/Aiming × 1/3/10 выстрелов.
   Повторное L — отмена.
3. Логи в Console; фильтры: **WeaponVisDiag** (поза оружия на выстрел), **RecoilSweep**
   (старый замер отдачи, по умолчанию выключен флагом `m_LogRecoilSweep`), **HeadSweep**
   (положение головы, `m_LogHeadSweep=true`). Полный текст также попадает в
   `Editor.log` (`%LOCALAPPDATA%\Unity\Editor\Editor.log`) и `Player.log`.

Особенности прогона, влияющие на чтение логов:
- На время прогона `m_FireController.ResetRecoilOnStopFiring = false` — `StopFiring()` НЕ сбрасывает
  punch/climb/penalty, затухание видно в кадрах RECOVER.
- `m_WeaponRecoil.IgnoreCameraDistanceCull = true` — визуальный kick работает на любой дистанции
  до камеры (в логе `nearDetail` всё равно покажет фактическую дистанционную проверку).
- `m_PreferFullAuto = true` — если у оружия есть FullAuto, прогон переключит режим огня на него.
- Огонь боевого ИИ подавляется (`m_SuppressCombatFire`), стрельбу ведёт сам прогон через
  `StartFiring()`/`ResetSemiTriggerState()`.

### 15.3 Что логируется на каждый выстрел (строка `ВЫСТРЕЛ` с тегом `[WeaponVisDiag]`)
- Номер выстрела в клетке, юнит, имя клетки прогона (поза/стойка/ход/число выстрелов), время, патрон, эффективный режим огня.
- Геймплейная отдача: прибавка за выстрел, penalty до→после/кап, `VisualRecoilKickScale`.
- Состояние визуального recoil: `impulse` (исходная накопленная визуальная сила), `climbPitch`, `punchPitch`, `punchYaw`, `backOffset`, `upOffset`, `isActive`, текущая tau затухания.
- **Hand_R (local)**: base rot/pos → final rot/pos, Δугол, Δpos по осям и модуль, флаг `applied` (наложился ли оверлей в этом кадре) и `canApply`.
- **Корень оружия (world)**: pre → post, Δpos/Δrot.
- **Дуло FireOrigin (world)**: pre → post, Δpos/Δrot, проекции `backProj` (на −forward ствола до выстрела) и `upProj` (на мировую вертикаль) — главный критерий направления recoil.
- **Затвор (local)**: pre → post, Δpos, флаг `boltOwnsShell` (выброс гильзы передан циклу затвора).
- Дистанция до камеры, флаг near-detail, активна ли команда огня.

Затухание между выстрелами видно в штатных строках `[RecoilSweep] RECOVER` (включаются флагом
`m_LogRecoilSweep = true`; интервал `m_RecoverLogIntervalSeconds = 0.25` с) — там же punch/climb/penalty/aimResidual.

### 15.4 Код логирования (выдержки из `UnitWeaponPoseSweepTest`)

Новые сериализованные поля:

```csharp
[Header("Weapon shot pose logging")]
[Tooltip("Подробный лог изменения позы оружия на каждый выстрел прогона (Hand_R, корень оружия, дуло, затвор). Фильтр консоли: WeaponVisDiag.")]
[SerializeField] private bool m_LogWeaponShotPose = true;
[SerializeField] private UnitEquipment m_Equipment;
[SerializeField] private WeaponVisualRecoilApplicator m_RecoilApplicator;
[SerializeField] private UnitWeaponBoltCycleVisual m_BoltCycleVisual;
```

Поля захвата и константа:

```csharp
private const string c_WeaponVisDiagTag = "[WeaponVisDiag]";
private string m_CurrentCellName;
private float m_LastFramePenalty;
private bool m_HasPendingShotCapture;
private float m_ShotLogTime;
private string m_ShotLogAmmoName;
private WeaponFireMode m_ShotLogFireMode;
private int m_ShotLogBurstIndex;
private float m_ShotLogRecoilAdded;
private float m_ShotLogPenaltyBefore;
private float m_ShotLogKickScale;
private Vector3 m_ShotLogHandBaseLocalPos;
private Quaternion m_ShotLogHandBaseLocalRot;
private Vector3 m_ShotLogWeaponWorldPos;
private Quaternion m_ShotLogWeaponWorldRot;
private Vector3 m_ShotLogMuzzleWorldPos;
private Quaternion m_ShotLogMuzzleWorldRot;
private Vector3 m_ShotLogBoltLocalPos;
```

Точка захвата «до выстрела» — обработчик `ShotFired` прогона (вызывается в Update, порядок 56;
в этот момент Hand_R ещё в чистой анимационной позе, оверлей будет наложен в LateUpdate):

```csharp
private void HandleSweepShotFired(AmmoDefinition _ammo)
{
	m_BurstShotsFired++;

	if (!m_LogWeaponShotPose)
		return;

	m_HasPendingShotCapture = true;
	m_ShotLogTime = Time.time;
	m_ShotLogAmmoName = _ammo != null ? _ammo.name : "?";
	m_ShotLogFireMode = m_FireController != null
		? m_FireController.ResolveEffectiveFireMode()
		: WeaponFireMode.SemiAuto;
	m_ShotLogBurstIndex = m_BurstShotsFired;
	m_ShotLogRecoilAdded = m_RecoilController != null
		? m_RecoilController.ComputeRecoilAddedPerShot(_ammo)
		: 0f;
	m_ShotLogPenaltyBefore = m_LastFramePenalty;
	m_ShotLogKickScale = ResolveVisualKickScale();

	Transform hand = m_Equipment != null ? m_Equipment.RightHandAnchor : null;
	if (hand != null)
	{
		m_ShotLogHandBaseLocalPos = hand.localPosition;
		m_ShotLogHandBaseLocalRot = hand.localRotation;
	}
	else
	{
		m_ShotLogHandBaseLocalPos = Vector3.zero;
		m_ShotLogHandBaseLocalRot = Quaternion.identity;
	}

	Transform weaponRoot = m_Equipment != null ? m_Equipment.MainWeaponRoot : null;
	m_ShotLogWeaponWorldPos = weaponRoot != null ? weaponRoot.position : Vector3.zero;
	m_ShotLogWeaponWorldRot = weaponRoot != null ? weaponRoot.rotation : Quaternion.identity;

	EquippedWeapon equippedWeapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
	Transform muzzle = equippedWeapon != null ? equippedWeapon.FireOriginTransform : null;
	m_ShotLogMuzzleWorldPos = muzzle != null ? muzzle.position : Vector3.zero;
	m_ShotLogMuzzleWorldRot = muzzle != null ? muzzle.rotation : Quaternion.identity;

	Transform bolt = equippedWeapon != null ? equippedWeapon.BoltCarrierTransform : null;
	m_ShotLogBoltLocalPos = bolt != null ? bolt.localPosition : Vector3.zero;
}
```

Точка печати — в цикле огня `CoFireWantedShots`, сразу после `WaitForEndOfFrame`
(т.е. ПОСЛЕ LateUpdate-оверлея recoil и снапа левой руки):

```csharp
// начало CoFireWantedShots:
m_BurstShotsFired = 0;
m_HasPendingShotCapture = false;
m_LastFramePenalty = m_RecoilController != null ? m_RecoilController.RecoilPenalty : 0f;
m_FireController.ShotFired += HandleSweepShotFired;

// в цикле огня, после каждого end-of-frame:
yield return m_EndOfFrame;
BarrelSample sample = CaptureSample();
_onSample?.Invoke(sample);

if (m_HasPendingShotCapture)
	LogWeaponShotFrame();
m_LastFramePenalty = sample.Penalty;
```

Формирование лога (читает финальные трансформы после оверлея):

```csharp
private void LogWeaponShotFrame()
{
	m_HasPendingShotCapture = false;

	Transform hand = m_Equipment != null ? m_Equipment.RightHandAnchor : null;
	if (hand == null)
	{
		LogWeaponShot(
			$"{c_WeaponVisDiagTag} ВЫСТРЕЛ #{m_ShotLogBurstIndex} | unit={name} | cell={m_CurrentCellName} | Hand_R не найден");
		return;
	}

	Vector3 finalLocalPos = hand.localPosition;
	Quaternion finalLocalRot = hand.localRotation;
	Vector3 posDelta = finalLocalPos - m_ShotLogHandBaseLocalPos;
	float rotDelta = Quaternion.Angle(m_ShotLogHandBaseLocalRot, finalLocalRot);

	WeaponVisualRecoilState kick = m_WeaponRecoil != null ? m_WeaponRecoil.CurrentState : default;
	bool overlayApplied = m_RecoilApplicator != null && m_RecoilApplicator.AppliedThisFrame;
	bool canApply = m_WeaponRecoil != null && m_WeaponRecoil.ShouldApplyOverlayThisFrame();
	float penalty = m_RecoilController != null ? m_RecoilController.RecoilPenalty : 0f;
	float maxPenalty = m_RecoilController != null ? m_RecoilController.MaxRecoilPenalty : 0f;
	float tau = ResolveVisualDecayTau();
	float impulse = m_WeaponRecoil != null ? m_WeaponRecoil.ShotImpulse : 0f;

	Transform weaponRoot = m_Equipment != null ? m_Equipment.MainWeaponRoot : null;
	Vector3 weaponPosDelta = weaponRoot != null
		? weaponRoot.position - m_ShotLogWeaponWorldPos
		: Vector3.zero;
	float weaponRotDelta = weaponRoot != null
		? Quaternion.Angle(m_ShotLogWeaponWorldRot, weaponRoot.rotation)
		: 0f;

	EquippedWeapon equippedWeapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
	Transform muzzle = equippedWeapon != null ? equippedWeapon.FireOriginTransform : null;
	Vector3 muzzlePosDelta = muzzle != null ? muzzle.position - m_ShotLogMuzzleWorldPos : Vector3.zero;
	float muzzleRotDelta = muzzle != null
		? Quaternion.Angle(m_ShotLogMuzzleWorldRot, muzzle.rotation)
		: 0f;
	Vector3 muzzlePreForward = m_ShotLogMuzzleWorldRot * Vector3.forward;
	float muzzleBackProjection = Vector3.Dot(muzzlePosDelta, -muzzlePreForward);
	float muzzleUpProjection = Vector3.Dot(muzzlePosDelta, Vector3.up);

	Transform bolt = equippedWeapon != null ? equippedWeapon.BoltCarrierTransform : null;
	Vector3 boltPosDelta = bolt != null ? bolt.localPosition - m_ShotLogBoltLocalPos : Vector3.zero;
	bool boltOwnsShell = m_BoltCycleVisual != null && m_BoltCycleVisual.WillHandlePhysicalShellEjection;

	Vector3 samplePos = weaponRoot != null ? weaponRoot.position : transform.position;
	float cameraDistance = WeaponVfxUtility.TryGetEffectViewerDistance(samplePos, out float dist)
		? dist
		: -1f;
	bool nearCamera = m_WeaponRecoil != null && m_WeaponRecoil.IsCameraNearForVisualKick();

	var sb = new StringBuilder(640);
	sb.AppendLine(
		$"{c_WeaponVisDiagTag} ВЫСТРЕЛ #{m_ShotLogBurstIndex} | unit={name} | cell={m_CurrentCellName} | " +
		$"t={m_ShotLogTime:F3} | ammo={m_ShotLogAmmoName} | mode={m_ShotLogFireMode}");
	sb.AppendLine(
		$"  recoil: added={m_ShotLogRecoilAdded:F3} | penalty {m_ShotLogPenaltyBefore:F2}→{penalty:F2}/{maxPenalty:F1} | kickScale={m_ShotLogKickScale:F2}");
	sb.AppendLine(
		$"  visualState: impulse={impulse:F3} | climbPitch={kick.climbPitch:F3}° punchPitch={kick.punchPitch:F3}° punchYaw={kick.punchYaw:F3}° | " +
		$"back={kick.backOffset:F4}м up={kick.upOffset:F4}м active={kick.isActive} | tau={tau:F3}с");
	sb.AppendLine(
		$"  Hand_R local: base rot={FormatEuler(m_ShotLogHandBaseLocalRot)} pos={FormatVector(m_ShotLogHandBaseLocalPos)}");
	sb.AppendLine(
		$"    → final rot={FormatEuler(finalLocalRot)} pos={FormatVector(finalLocalPos)} | Δrot={rotDelta:F3}° " +
		$"Δpos={FormatVector(posDelta)} |Δpos|={posDelta.magnitude:F4}м | applied={overlayApplied} canApply={canApply}");
	sb.AppendLine(
		$"  WeaponRoot world: pre pos={FormatVector(m_ShotLogWeaponWorldPos)} rot={FormatEuler(m_ShotLogWeaponWorldRot)}");
	sb.AppendLine(
		$"    → post pos={(weaponRoot != null ? FormatVector(weaponRoot.position) : "null")} " +
		$"rot={(weaponRoot != null ? FormatEuler(weaponRoot.rotation) : "null")} | " +
		$"Δpos={FormatVector(weaponPosDelta)} |Δpos|={weaponPosDelta.magnitude:F4}м Δrot={weaponRotDelta:F3}°");
	sb.AppendLine(
		$"  Muzzle world: pre pos={FormatVector(m_ShotLogMuzzleWorldPos)} rot={FormatEuler(m_ShotLogMuzzleWorldRot)}");
	sb.AppendLine(
		$"    → post pos={(muzzle != null ? FormatVector(muzzle.position) : "null")} " +
		$"rot={(muzzle != null ? FormatEuler(muzzle.rotation) : "null")} | " +
		$"Δpos={FormatVector(muzzlePosDelta)} |Δpos|={muzzlePosDelta.magnitude:F4}м Δrot={muzzleRotDelta:F3}° | " +
		$"backProj={muzzleBackProjection:F4}м upProj={muzzleUpProjection:F4}м");
	sb.AppendLine(
		$"  Bolt local: pre pos={FormatVector(m_ShotLogBoltLocalPos)} → " +
		$"post pos={(bolt != null ? FormatVector(bolt.localPosition) : "null")} | Δpos={FormatVector(boltPosDelta)}");
	sb.Append(
		$"  cameraDist={cameraDistance:F1}м nearDetail={nearCamera} | firing={m_FireController != null && m_FireController.IsFiringCommandActive} | boltOwnsShell={boltOwnsShell}");
	LogWeaponShot(sb.ToString());
}

private float ResolveVisualKickScale()
{
	WeaponDefinition definition = m_WeaponRuntime != null ? m_WeaponRuntime.CurrentWeaponDefinition : null;
	return definition != null ? definition.VisualRecoilKickScale : 1f;
}

private float ResolveVisualDecayTau()
{
	if (m_WeaponRecoil == null)
		return 0f;

	float tau = Mathf.Max(0.01f, m_WeaponRecoil.ShotSmoothTime);
	if (m_FireController != null && m_FireController.IsFiringCommandActive)
		tau *= Mathf.Max(1f, m_WeaponRecoil.DecayWhileFiringMultiplier);
	return tau;
}

private void LogWeaponShot(string _message)
{
	if (m_LogWeaponShotPose)
		Debug.Log(_message, this);
}

private static string FormatVector(Vector3 _value)
{
	return $"({_value.x:F4}, {_value.y:F4}, {_value.z:F4})";
}

private static string FormatEuler(Quaternion _rotation)
{
	Vector3 euler = _rotation.eulerAngles;
	return $"({euler.x:F2}, {euler.y:F2}, {euler.z:F2})";
}
```

### 15.5 Нюансы интерпретации логов
- `Hand_R base` — чистая поза анимации (и правая IK в `OnAnimatorIK`) на момент события выстрела;
  `final` — та же кость после оверлея recoil (читается в конце кадра). Их разница и есть чистое движение от визуальной отдачи.
- `WeaponRoot Δpos` ≈ перенос кисти плюс плечо рычага от вращения кисти, т.к. оружие — ребёнок Hand_R. Сам по себе не критерий направления recoil.
- `Muzzle backProj` / `upProj` — главный критерий направления после parent-space translation:
  `backProj = dot(muzzlePost − muzzlePre, −fireOriginPre.forward)`, `upProj = dot(..., Vector3.up)`.
  Ожидается `backProj ≈ backOffset`, `upProj ≈ upOffset`. Сырой `|Δpos|` дула критерием не является
  (при наклонённом стволе мировая вертикаль частично проецируется на −forward).
- `Hand_R Δpos` в local-осях кисти **не** обязан выглядеть как `(0, up, −back)` — оси кисти ≠ ось ствола.
- `applied=false` при `active=true` означает, что оверлей заблокирован одним из условий §14
  (в прогоне дистанционный сброс отключён через `IgnoreCameraDistanceCull`, поэтому `applied` в прогоне практически всегда true).
- В очереди `penalty` растёт к капу 30, `climbPitch` — по кривой `0→0°, 15→0.6°, 30→1.7°, 60→4.5°`
  (при penalty 30 ≈ 1.7° × `VisualOffsetScale` × `kickScale`). Первые выстрелы почти не поднимают ствол.
- Логи `ВЫСТРЕЛ` печатаются только для выстрелов, выполненных самим прогоном (пока подписан
  `HandleSweepShotFired`); боевые выстрелы ИИ во время прогона подавлены.

---

## 16. Baseline ДО back-first (захваченные логи старого прогона)

> ВНИМАНИЕ: это БЕЙЗЛАЙН старой системы (до back-first рефактора). Значения `punchPitch 2.7–6.4°`,
> `back 20–51 мм`, `up 8–19 мм` получены на старой калибровке (ShotPitch 3.75, BackScale 0.008,
> UpScale 0.0035, HandPitch 1.0, HandUp 0.85, cap 8°) и НЕ соответствуют текущей конфигурации.
> Текущие формулы и пространство — §6.4. Логи после новой калибровки, но ещё в осях кисти — §18.
> Прогон: unit=Unit(Clone), target=Sphere50, патрон Ammo_556x45mmNATO, режим FullAuto,
> kickScale=1,20 (VisualRecoilKickScale оружия), tau в очереди 0,140 с (= 0,08 x 1,75).
> Матрица: 48 клеток (2 стойки x 2 движения x 3 skip-позы + 3 fire-позы x 1/3/10 выстрелов), выстрелов: 168.
> Ниже все строки [WeaponVisDiag] и сводка [HeadSweep]; покадровые HEAD-строки опущены (есть в Editor.log).

```text
[HeadSweep] START unit=Unit(Clone) target=Sphere50 lookProfile=StandingCasual lookBlocked=0 filter=HeadSweep
(строки [HeadSweep] HEAD ... покадрово опущены — см. Editor.log)
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x1 | t=15,048 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 0,00→0,57/30,0 | kickScale=1,20
  visualState: climbPitch=0,002° punchPitch=2,699° punchYaw=0,169° | back=0,0216м up=0,0080м active=True | tau=0,140с
  Hand_R local: base rot=(350,95, 14,16, 317,59) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(349,05, 16,13, 317,25) pos=(0,2709, 0,0025, -0,0225) | Δrot=2,706° Δpos=(-0,0002, 0,0025, -0,0229) |Δpos|=0,0230м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,2202, 1,6522, 0,2060) rot=(0,14, 339,35, 2,61)
    → post pos=(19,2122, 1,6744, 0,2048) rot=(0,31, 339,65, 359,93) | Δpos=(-0,0081, 0,0222, -0,0012) |Δpos|=0,0237м Δrot=2,702°
  Muzzle world: pre pos=(18,9817, 1,7509, 0,8260) rot=(0,14, 339,35, 2,61)
    → post pos=(18,9812, 1,7713, 0,8279) rot=(0,31, 339,65, 359,93) | Δpos=(-0,0005, 0,0204, 0,0019) |Δpos|=0,0205м Δrot=2,702°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1308) | Δpos=(0,0000, 0,0000, -0,0152)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x3 | t=16,306 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 0,00→0,57/30,0 | kickScale=1,20
  visualState: climbPitch=0,002° punchPitch=2,699° punchYaw=0,363° | back=0,0216м up=0,0080м active=True | tau=0,140с
  Hand_R local: base rot=(350,90, 14,26, 317,49) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(349,13, 16,35, 317,13) pos=(0,2709, 0,0024, -0,0225) | Δrot=2,719° Δpos=(-0,0002, 0,0024, -0,0229) |Δpos|=0,0230м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,2198, 1,6523, 0,2069) rot=(0,24, 339,51, 2,72)
    → post pos=(19,2117, 1,6748, 0,2057) rot=(0,24, 339,81, 0,02) | Δpos=(-0,0081, 0,0225, -0,0012) |Δpos|=0,0239м Δrot=2,719°
  Muzzle world: pre pos=(18,9828, 1,7500, 0,8276) rot=(0,24, 339,51, 2,72)
    → post pos=(18,9823, 1,7725, 0,8292) rot=(0,24, 339,81, 0,02) | Δpos=(-0,0005, 0,0225, 0,0016) |Δpos|=0,0226м Δrot=2,719°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1351) | Δpos=(0,0000, 0,0000, -0,0109)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x3 | t=16,415 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 0,32→0,90/30,0 | kickScale=1,20
  visualState: climbPitch=0,006° punchPitch=3,942° punchYaw=0,352° | back=0,0315м up=0,0117м active=True | tau=0,140с
  Hand_R local: base rot=(350,03, 15,29, 317,31) pos=(0,2710, 0,0012, -0,0109)
    → final rot=(348,21, 17,22, 316,96) pos=(0,2708, 0,0035, -0,0331) | Δrot=2,623° Δpos=(-0,0002, 0,0024, -0,0222) |Δpos|=0,0223м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,2158, 1,6634, 0,2063) rot=(0,24, 339,67, 1,41)
    → post pos=(19,2079, 1,6849, 0,2052) rot=(0,41, 339,95, 358,80) | Δpos=(-0,0079, 0,0215, -0,0012) |Δpos|=0,0229м Δrot=2,619°
  Muzzle world: pre pos=(18,9827, 1,7610, 0,8285) rot=(0,24, 339,67, 1,41)
    → post pos=(18,9821, 1,7806, 0,8303) rot=(0,41, 339,95, 358,80) | Δpos=(-0,0006, 0,0196, 0,0018) |Δpos|=0,0197м Δrot=2,619°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1363) | Δpos=(0,0000, 0,0000, -0,0097)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x3 | t=16,521 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 0,66→1,23/30,0 | kickScale=1,20
  visualState: climbPitch=0,010° punchPitch=4,540° punchYaw=0,826° | back=0,0363м up=0,0135м active=True | tau=0,140с
  Hand_R local: base rot=(349,54, 15,75, 317,22) pos=(0,2710, 0,0018, -0,0164)
    → final rot=(348,04, 17,95, 316,83) pos=(0,2708, 0,0041, -0,0381) | Δrot=2,624° Δpos=(-0,0002, 0,0023, -0,0217) |Δpos|=0,0218м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,2138, 1,6687, 0,2061) rot=(0,33, 339,76, 0,77)
    → post pos=(19,2061, 1,6904, 0,2050) rot=(0,09, 340,03, 358,17) | Δpos=(-0,0077, 0,0217, -0,0011) |Δpos|=0,0230м Δrot=2,620°
  Muzzle world: pre pos=(18,9826, 1,7654, 0,8292) rot=(0,33, 339,76, 0,77)
    → post pos=(18,9823, 1,7898, 0,8303) rot=(0,09, 340,03, 358,17) | Δpos=(-0,0003, 0,0244, 0,0011) |Δpos|=0,0244м Δrot=2,620°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1341) | Δpos=(0,0000, 0,0000, -0,0119)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x10 | t=17,822 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 0,00→0,57/30,0 | kickScale=1,20
  visualState: climbPitch=0,002° punchPitch=2,699° punchYaw=0,273° | back=0,0216м up=0,0080м active=True | tau=0,140с
  Hand_R local: base rot=(350,86, 14,38, 317,37) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(349,03, 16,42, 317,02) pos=(0,2709, 0,0024, -0,0225) | Δrot=2,712° Δpos=(-0,0003, 0,0024, -0,0229) |Δpos|=0,0230м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,2193, 1,6524, 0,2079) rot=(0,35, 339,71, 2,86)
    → post pos=(19,2111, 1,6748, 0,2067) rot=(0,43, 340,01, 0,17) | Δpos=(-0,0082, 0,0223, -0,0012) |Δpos|=0,0238м Δrot=2,710°
  Muzzle world: pre pos=(18,9841, 1,7488, 0,8295) rot=(0,35, 339,71, 2,86)
    → post pos=(18,9836, 1,7703, 0,8313) rot=(0,43, 340,01, 0,17) | Δpos=(-0,0006, 0,0215, 0,0018) |Δpos|=0,0216м Δrot=2,710°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1351) | Δpos=(0,0000, 0,0000, -0,0109)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x10 | t=17,929 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 0,33→0,90/30,0 | kickScale=1,20
  visualState: climbPitch=0,006° punchPitch=3,959° punchYaw=0,421° | back=0,0317м up=0,0118м active=True | tau=0,140с
  Hand_R local: base rot=(349,93, 15,41, 317,19) pos=(0,2710, 0,0012, -0,0112)
    → final rot=(348,19, 17,40, 316,82) pos=(0,2708, 0,0035, -0,0332) | Δrot=2,613° Δpos=(-0,0003, 0,0023, -0,0220) |Δpos|=0,0222м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,2151, 1,6637, 0,2074) rot=(0,39, 339,87, 1,51)
    → post pos=(19,2073, 1,6852, 0,2063) rot=(0,46, 340,15, 358,92) | Δpos=(-0,0078, 0,0215, -0,0011) |Δpos|=0,0229м Δrot=2,608°
  Muzzle world: pre pos=(18,9839, 1,7596, 0,8305) rot=(0,39, 339,87, 1,51)
    → post pos=(18,9834, 1,7803, 0,8322) rot=(0,46, 340,15, 358,92) | Δpos=(-0,0006, 0,0207, 0,0017) |Δpos|=0,0208м Δrot=2,608°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1328) | Δpos=(0,0000, 0,0000, -0,0132)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x10 | t=18,036 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 0,66→1,23/30,0 | kickScale=1,20
  visualState: climbPitch=0,011° punchPitch=4,540° punchYaw=0,600° | back=0,0363м up=0,0135м active=True | tau=0,140с
  Hand_R local: base rot=(349,52, 15,89, 317,10) pos=(0,2710, 0,0018, -0,0164)
    → final rot=(347,87, 17,93, 316,72) pos=(0,2707, 0,0040, -0,0382) | Δrot=2,588° Δpos=(-0,0003, 0,0023, -0,0217) |Δpos|=0,0218м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,2132, 1,6688, 0,2072) rot=(0,41, 339,95, 0,90)
    → post pos=(19,2055, 1,6902, 0,2061) rot=(0,39, 340,23, 358,33) | Δpos=(-0,0077, 0,0213, -0,0011) |Δpos|=0,0227м Δrot=2,585°
  Muzzle world: pre pos=(18,9839, 1,7646, 0,8311) rot=(0,41, 339,95, 0,90)
    → post pos=(18,9834, 1,7861, 0,8326) rot=(0,39, 340,23, 358,33) | Δpos=(-0,0005, 0,0216, 0,0015) |Δpos|=0,0216м Δrot=2,585°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1343) | Δpos=(0,0000, 0,0000, -0,0117)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #4 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x10 | t=18,141 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 0,99→1,57/30,0 | kickScale=1,20
  visualState: climbPitch=0,017° punchPitch=4,846° punchYaw=0,808° | back=0,0388м up=0,0144м active=True | tau=0,140с
  Hand_R local: base rot=(349,33, 16,19, 317,04) pos=(0,2709, 0,0020, -0,0192)
    → final rot=(347,76, 18,29, 316,65) pos=(0,2707, 0,0043, -0,0408) | Δrot=2,589° Δpos=(-0,0003, 0,0023, -0,0216) |Δpos|=0,0217м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,2122, 1,6716, 0,2071) rot=(0,38, 340,00, 0,58)
    → post pos=(19,2045, 1,6929, 0,2060) rot=(0,26, 340,27, 358,01) | Δpos=(-0,0077, 0,0213, -0,0011) |Δpos|=0,0227м Δrot=2,585°
  Muzzle world: pre pos=(18,9840, 1,7677, 0,8313) rot=(0,38, 340,00, 0,58)
    → post pos=(18,9835, 1,7904, 0,8327) rot=(0,26, 340,27, 358,01) | Δpos=(-0,0005, 0,0227, 0,0013) |Δpos|=0,0227м Δrot=2,585°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1352) | Δpos=(0,0000, 0,0000, -0,0108)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #5 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x10 | t=18,245 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 1,33→1,90/30,0 | kickScale=1,20
  visualState: climbPitch=0,025° punchPitch=4,998° punchYaw=0,932° | back=0,0400м up=0,0149м active=True | tau=0,140с
  Hand_R local: base rot=(349,26, 16,38, 317,00) pos=(0,2709, 0,0022, -0,0206)
    → final rot=(347,71, 18,49, 316,61) pos=(0,2706, 0,0044, -0,0420) | Δrot=2,581° Δpos=(-0,0003, 0,0022, -0,0215) |Δpos|=0,0216м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,2117, 1,6731, 0,2071) rot=(0,31, 340,03, 0,41)
    → post pos=(19,2040, 1,6943, 0,2060) rot=(0,18, 340,30, 357,85) | Δpos=(-0,0077, 0,0213, -0,0011) |Δpos|=0,0226м Δrot=2,578°
  Muzzle world: pre pos=(18,9841, 1,7699, 0,8315) rot=(0,31, 340,03, 0,41)
    → post pos=(18,9836, 1,7927, 0,8327) rot=(0,18, 340,30, 357,85) | Δpos=(-0,0005, 0,0227, 0,0013) |Δpos|=0,0228м Δrot=2,578°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1350) | Δpos=(0,0000, 0,0000, -0,0110)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #6 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x10 | t=18,348 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 1,67→2,24/30,0 | kickScale=1,20
  visualState: climbPitch=0,034° punchPitch=5,089° punchYaw=0,858° | back=0,0407м up=0,0151м active=True | tau=0,140с
  Hand_R local: base rot=(349,22, 16,51, 316,97) pos=(0,2709, 0,0023, -0,0214)
    → final rot=(347,59, 18,52, 316,59) pos=(0,2706, 0,0045, -0,0428) | Δrot=2,556° Δpos=(-0,0003, 0,0022, -0,0214) |Δpos|=0,0215м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,2113, 1,6740, 0,2071) rot=(0,28, 340,06, 0,30)
    → post pos=(19,2037, 1,6950, 0,2061) rot=(0,26, 340,32, 357,76) | Δpos=(-0,0076, 0,0210, -0,0011) |Δpos|=0,0224м Δrot=2,552°
  Muzzle world: pre pos=(18,9842, 1,7713, 0,8316) rot=(0,28, 340,06, 0,30)
    → post pos=(18,9837, 1,7924, 0,8331) rot=(0,26, 340,32, 357,76) | Δpos=(-0,0005, 0,0211, 0,0015) |Δpos|=0,0211м Δrot=2,552°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1347) | Δpos=(0,0000, 0,0000, -0,0113)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #7 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x10 | t=18,454 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 2,01→2,58/30,0 | kickScale=1,20
  visualState: climbPitch=0,045° punchPitch=5,095° punchYaw=0,655° | back=0,0408м up=0,0152м active=True | tau=0,140с
  Hand_R local: base rot=(349,12, 16,57, 316,95) pos=(0,2709, 0,0024, -0,0223)
    → final rot=(347,46, 18,41, 316,59) pos=(0,2706, 0,0045, -0,0429) | Δrot=2,452° Δpos=(-0,0002, 0,0021, -0,0206) |Δpos|=0,0207м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,2110, 1,6748, 0,2072) rot=(0,32, 340,08, 0,21)
    → post pos=(19,2036, 1,6948, 0,2062) rot=(0,44, 340,34, 357,78) | Δpos=(-0,0074, 0,0200, -0,0010) |Δpos|=0,0214м Δrot=2,448°
  Muzzle world: pre pos=(18,9843, 1,7715, 0,8319) rot=(0,32, 340,08, 0,21)
    → post pos=(18,9837, 1,7901, 0,8335) rot=(0,44, 340,34, 357,78) | Δpos=(-0,0006, 0,0185, 0,0016) |Δpos|=0,0186м Δrot=2,448°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1233) | Δpos=(0,0000, 0,0000, -0,0226)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #8 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x10 | t=18,555 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 2,35→2,92/30,0 | kickScale=1,20
  visualState: climbPitch=0,058° punchPitch=5,177° punchYaw=0,634° | back=0,0414м up=0,0154м active=True | tau=0,140с
  Hand_R local: base rot=(349,06, 16,50, 316,97) pos=(0,2709, 0,0024, -0,0222)
    → final rot=(347,38, 18,46, 316,59) pos=(0,2706, 0,0046, -0,0436) | Δrot=2,551° Δpos=(-0,0003, 0,0022, -0,0214) |Δpos|=0,0215м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,2110, 1,6745, 0,2071) rot=(0,41, 340,08, 0,21)
    → post pos=(19,2034, 1,6954, 0,2061) rot=(0,47, 340,34, 357,68) | Δpos=(-0,0076, 0,0209, -0,0011) |Δpos|=0,0222м Δrot=2,546°
  Muzzle world: pre pos=(18,9842, 1,7703, 0,8319) rot=(0,41, 340,08, 0,21)
    → post pos=(18,9836, 1,7904, 0,8335) rot=(0,47, 340,34, 357,68) | Δpos=(-0,0006, 0,0201, 0,0016) |Δpos|=0,0202м Δrot=2,546°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1352) | Δpos=(0,0000, 0,0000, -0,0108)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #9 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x10 | t=18,655 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 2,69→3,27/30,0 | kickScale=1,20
  visualState: climbPitch=0,072° punchPitch=5,223° punchYaw=0,647° | back=0,0418м up=0,0155м active=True | tau=0,140с
  Hand_R local: base rot=(349,01, 16,53, 316,97) pos=(0,2709, 0,0024, -0,0227)
    → final rot=(347,35, 18,50, 316,58) pos=(0,2706, 0,0046, -0,0440) | Δrot=2,542° Δpos=(-0,0002, 0,0022, -0,0213) |Δpos|=0,0214м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,2109, 1,6750, 0,2070) rot=(0,42, 340,07, 0,14)
    → post pos=(19,2033, 1,6958, 0,2060) rot=(0,46, 340,33, 357,61) | Δpos=(-0,0076, 0,0208, -0,0011) |Δpos|=0,0222м Δrot=2,540°
  Muzzle world: pre pos=(18,9841, 1,7707, 0,8319) rot=(0,42, 340,07, 0,14)
    → post pos=(18,9835, 1,7909, 0,8334) rot=(0,46, 340,33, 357,61) | Δpos=(-0,0006, 0,0203, 0,0015) |Δpos|=0,0203м Δrot=2,540°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1342) | Δpos=(0,0000, 0,0000, -0,0117)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #10 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x10 | t=18,761 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 3,02→3,60/30,0 | kickScale=1,20
  visualState: climbPitch=0,087° punchPitch=5,152° punchYaw=0,451° | back=0,0412м up=0,0153м active=True | tau=0,140с
  Hand_R local: base rot=(349,06, 16,47, 316,99) pos=(0,2709, 0,0023, -0,0219)
    → final rot=(347,28, 18,33, 316,61) pos=(0,2706, 0,0046, -0,0434) | Δrot=2,553° Δpos=(-0,0002, 0,0022, -0,0214) |Δpos|=0,0216м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,2112, 1,6743, 0,2070) rot=(0,41, 340,05, 0,21)
    → post pos=(19,2036, 1,6950, 0,2059) rot=(0,61, 340,31, 357,68) | Δpos=(-0,0076, 0,0207, -0,0011) |Δpos|=0,0221м Δrot=2,552°
  Muzzle world: pre pos=(18,9841, 1,7700, 0,8317) rot=(0,41, 340,05, 0,21)
    → post pos=(18,9834, 1,7884, 0,8334) rot=(0,61, 340,31, 357,68) | Δpos=(-0,0007, 0,0183, 0,0018) |Δpos|=0,0184м Δrot=2,552°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1358) | Δpos=(0,0000, 0,0000, -0,0102)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x1 | t=21,373 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 0,00→0,57/30,0 | kickScale=1,20
  visualState: climbPitch=0,002° punchPitch=2,699° punchYaw=0,193° | back=0,0216м up=0,0080м active=True | tau=0,140с
  Hand_R local: base rot=(16,65, 287,89, 6,92) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(13,95, 287,72, 6,88) pos=(0,2884, 0,0138, -0,0062) | Δrot=2,706° Δpos=(0,0172, 0,0138, -0,0066) |Δpos|=0,0230м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0511, 1,9061, 0,4274) rot=(0,56, 339,32, 37,34)
    → post pos=(19,0324, 1,9198, 0,4223) rot=(0,52, 339,65, 34,65) | Δpos=(-0,0186, 0,0138, -0,0052) |Δpos|=0,0237м Δrot=2,705°
  Muzzle world: pre pos=(18,7593, 1,9794, 1,0278) rot=(0,56, 339,32, 37,34)
    → post pos=(18,7477, 1,9965, 1,0256) rot=(0,52, 339,65, 34,65) | Δpos=(-0,0116, 0,0171, -0,0022) |Δpos|=0,0208м Δrot=2,705°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1346) | Δpos=(0,0000, 0,0000, -0,0114)
  cameraDist=1,6м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x3 | t=22,836 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 0,00→0,57/30,0 | kickScale=1,20
  visualState: climbPitch=0,002° punchPitch=2,699° punchYaw=0,198° | back=0,0216м up=0,0080м active=True | tau=0,140с
  Hand_R local: base rot=(16,65, 287,89, 6,92) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(13,95, 287,72, 6,88) pos=(0,2884, 0,0138, -0,0062) | Δrot=2,707° Δpos=(0,0172, 0,0138, -0,0066) |Δpos|=0,0230м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0511, 1,9054, 0,4277) rot=(0,65, 339,32, 37,31)
    → post pos=(19,0324, 1,9191, 0,4225) rot=(0,61, 339,65, 34,62) | Δpos=(-0,0186, 0,0138, -0,0051) |Δpos|=0,0237м Δrot=2,706°
  Muzzle world: pre pos=(18,7593, 1,9777, 1,0282) rot=(0,65, 339,32, 37,31)
    → post pos=(18,7477, 1,9948, 1,0260) rot=(0,61, 339,65, 34,62) | Δpos=(-0,0116, 0,0171, -0,0022) |Δpos|=0,0208м Δrot=2,706°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1338) | Δpos=(0,0000, 0,0000, -0,0122)
  cameraDist=1,6м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x3 | t=22,937 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 0,34→0,92/30,0 | kickScale=1,20
  visualState: climbPitch=0,006° punchPitch=4,016° punchYaw=0,644° | back=0,0321м up=0,0119м active=True | tau=0,140с
  Hand_R local: base rot=(15,25, 287,80, 6,90) pos=(0,2801, 0,0072, -0,0030)
    → final rot=(12,59, 287,95, 6,95) pos=(0,2968, 0,0206, -0,0094) | Δrot=2,655° Δpos=(0,0166, 0,0134, -0,0064) |Δpos|=0,0223м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0414, 1,9124, 0,4250) rot=(0,64, 339,49, 35,91)
    → post pos=(19,0231, 1,9261, 0,4200) rot=(0,36, 339,64, 33,27) | Δpos=(-0,0182, 0,0137, -0,0050) |Δpos|=0,0233м Δrot=2,653°
  Muzzle world: pre pos=(18,7533, 1,9864, 1,0271) rot=(0,64, 339,49, 35,91)
    → post pos=(18,7403, 2,0059, 1,0238) rot=(0,36, 339,64, 33,27) | Δpos=(-0,0130, 0,0195, -0,0033) |Δpos|=0,0237м Δrot=2,653°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1365) | Δpos=(0,0000, 0,0000, -0,0095)
  cameraDist=1,6м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x3 | t=23,041 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 0,68→1,26/30,0 | kickScale=1,20
  visualState: climbPitch=0,011° punchPitch=4,614° punchYaw=0,680° | back=0,0369м up=0,0137м active=True | tau=0,140с
  Hand_R local: base rot=(14,57, 287,92, 6,93) pos=(0,2843, 0,0105, -0,0046)
    → final rot=(11,99, 287,90, 6,95) pos=(0,3006, 0,0236, -0,0109) | Δrot=2,581° Δpos=(0,0163, 0,0131, -0,0062) |Δpos|=0,0218м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0368, 1,9158, 0,4238) rot=(0,51, 339,49, 35,23)
    → post pos=(19,0190, 1,9290, 0,4189) rot=(0,37, 339,72, 32,67) | Δpos=(-0,0177, 0,0132, -0,0049) |Δpos|=0,0227м Δrot=2,580°
  Muzzle world: pre pos=(18,7495, 1,9919, 1,0260) rot=(0,51, 339,49, 35,23)
    → post pos=(18,7378, 2,0093, 1,0234) rot=(0,37, 339,72, 32,67) | Δpos=(-0,0118, 0,0174, -0,0026) |Δpos|=0,0212м Δrot=2,580°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1349) | Δpos=(0,0000, 0,0000, -0,0111)
  cameraDist=1,6м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x10 | t=24,539 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 0,00→0,58/30,0 | kickScale=1,20
  visualState: climbPitch=0,002° punchPitch=2,699° punchYaw=0,139° | back=0,0216м up=0,0080м active=True | tau=0,140с
  Hand_R local: base rot=(16,65, 287,89, 6,92) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(13,96, 287,67, 6,87) pos=(0,2884, 0,0138, -0,0062) | Δrot=2,704° Δpos=(0,0172, 0,0138, -0,0066) |Δpos|=0,0230м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0511, 1,9052, 0,4278) rot=(0,65, 339,33, 37,31)
    → post pos=(19,0325, 1,9190, 0,4227) rot=(0,64, 339,68, 34,63) | Δpos=(-0,0186, 0,0137, -0,0051) |Δpos|=0,0237м Δrot=2,702°
  Muzzle world: pre pos=(18,7594, 1,9776, 1,0284) rot=(0,65, 339,33, 37,31)
    → post pos=(18,7481, 1,9942, 1,0264) rot=(0,64, 339,68, 34,63) | Δpos=(-0,0113, 0,0166, -0,0020) |Δpos|=0,0202м Δrot=2,702°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1358) | Δpos=(0,0000, 0,0000, -0,0102)
  cameraDist=1,6м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x10 | t=24,640 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 0,35→0,92/30,0 | kickScale=1,20
  visualState: climbPitch=0,006° punchPitch=4,013° punchYaw=0,207° | back=0,0321м up=0,0119м active=True | tau=0,140с
  Hand_R local: base rot=(15,24, 287,77, 6,89) pos=(0,2802, 0,0072, -0,0031)
    → final rot=(12,64, 287,57, 6,84) pos=(0,2967, 0,0206, -0,0094) | Δrot=2,608° Δpos=(0,0166, 0,0133, -0,0063) |Δpos|=0,0222м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0414, 1,9125, 0,4251) rot=(0,64, 339,51, 35,91)
    → post pos=(19,0235, 1,9257, 0,4201) rot=(0,64, 339,85, 33,33) | Δpos=(-0,0179, 0,0132, -0,0049) |Δpos|=0,0228м Δrot=2,605°
  Muzzle world: pre pos=(18,7535, 1,9865, 1,0273) rot=(0,64, 339,51, 35,91)
    → post pos=(18,7426, 2,0023, 1,0253) rot=(0,64, 339,85, 33,33) | Δpos=(-0,0108, 0,0158, -0,0019) |Δpos|=0,0192м Δrot=2,605°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1346) | Δpos=(0,0000, 0,0000, -0,0114)
  cameraDist=1,6м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x10 | t=24,748 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 0,67→1,25/30,0 | kickScale=1,20
  visualState: climbPitch=0,011° punchPitch=4,548° punchYaw=0,581° | back=0,0364м up=0,0135м active=True | tau=0,140с
  Hand_R local: base rot=(14,67, 287,73, 6,88) pos=(0,2838, 0,0101, -0,0044)
    → final rot=(12,07, 287,83, 6,92) pos=(0,3002, 0,0233, -0,0107) | Δrot=2,607° Δpos=(0,0164, 0,0132, -0,0063) |Δpos|=0,0219м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0375, 1,9155, 0,4239) rot=(0,62, 339,59, 35,35)
    → post pos=(19,0196, 1,9289, 0,4190) rot=(0,39, 339,75, 32,76) | Δpos=(-0,0179, 0,0134, -0,0050) |Δpos|=0,0229м Δrot=2,605°
  Muzzle world: pre pos=(18,7511, 1,9903, 1,0268) rot=(0,62, 339,59, 35,35)
    → post pos=(18,7386, 2,0089, 1,0236) rot=(0,39, 339,75, 32,76) | Δpos=(-0,0125, 0,0187, -0,0031) |Δpos|=0,0227м Δrot=2,605°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1357) | Δpos=(0,0000, 0,0000, -0,0103)
  cameraDist=1,6м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #4 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x10 | t=24,854 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 1,00→1,58/30,0 | kickScale=1,20
  visualState: climbPitch=0,017° punchPitch=4,836° punchYaw=0,420° | back=0,0387м up=0,0144м active=True | tau=0,140с
  Hand_R local: base rot=(14,35, 287,86, 6,92) pos=(0,2857, 0,0117, -0,0052)
    → final rot=(11,79, 287,65, 6,88) pos=(0,3020, 0,0248, -0,0114) | Δrot=2,562° Δpos=(0,0163, 0,0131, -0,0062) |Δpos|=0,0218м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0352, 1,9174, 0,4233) rot=(0,49, 339,54, 35,03)
    → post pos=(19,0177, 1,9303, 0,4184) rot=(0,50, 339,88, 32,49) | Δpos=(-0,0175, 0,0130, -0,0049) |Δpos|=0,0223м Δrot=2,561°
  Muzzle world: pre pos=(18,7489, 1,9940, 1,0259) rot=(0,49, 339,54, 35,03)
    → post pos=(18,7383, 2,0093, 1,0240) rot=(0,50, 339,88, 32,49) | Δpos=(-0,0105, 0,0153, -0,0019) |Δpos|=0,0187м Δrot=2,561°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1360) | Δpos=(0,0000, 0,0000, -0,0100)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #5 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x10 | t=24,959 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 1,34→1,91/30,0 | kickScale=1,20
  visualState: climbPitch=0,025° punchPitch=4,986° punchYaw=0,546° | back=0,0399м up=0,0148м active=True | tau=0,140с
  Hand_R local: base rot=(14,16, 287,76, 6,89) pos=(0,2869, 0,0127, -0,0056)
    → final rot=(11,62, 287,74, 6,90) pos=(0,3030, 0,0255, -0,0118) | Δrot=2,542° Δpos=(0,0160, 0,0129, -0,0061) |Δpos|=0,0215м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0340, 1,9183, 0,4229) rot=(0,54, 339,61, 34,85)
    → post pos=(19,0166, 1,9313, 0,4180) rot=(0,41, 339,84, 32,33) | Δpos=(-0,0174, 0,0130, -0,0048) |Δpos|=0,0223м Δrot=2,539°
  Muzzle world: pre pos=(18,7486, 1,9946, 1,0260) rot=(0,54, 339,61, 34,85)
    → post pos=(18,7371, 2,0115, 1,0234) rot=(0,41, 339,84, 32,33) | Δpos=(-0,0115, 0,0170, -0,0026) |Δpos|=0,0206м Δrot=2,539°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1330) | Δpos=(0,0000, 0,0000, -0,0130)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #6 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x10 | t=25,060 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 1,68→2,26/30,0 | kickScale=1,20
  visualState: climbPitch=0,035° punchPitch=5,121° punchYaw=0,574° | back=0,0410м up=0,0152м active=True | tau=0,140с
  Hand_R local: base rot=(14,02, 287,81, 6,90) pos=(0,2878, 0,0133, -0,0060)
    → final rot=(11,47, 287,75, 6,91) pos=(0,3038, 0,0262, -0,0121) | Δrot=2,544° Δpos=(0,0161, 0,0129, -0,0061) |Δpos|=0,0215м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0331, 1,9192, 0,4226) rot=(0,48, 339,60, 34,71)
    → post pos=(19,0156, 1,9321, 0,4177) rot=(0,38, 339,85, 32,18) | Δpos=(-0,0174, 0,0130, -0,0048) |Δpos|=0,0222м Δrot=2,541°
  Muzzle world: pre pos=(18,7477, 1,9962, 1,0256) rot=(0,48, 339,60, 34,71)
    → post pos=(18,7364, 2,0128, 1,0232) rot=(0,38, 339,85, 32,18) | Δpos=(-0,0112, 0,0166, -0,0024) |Δpos|=0,0202м Δrot=2,541°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1345) | Δpos=(0,0000, 0,0000, -0,0115)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #7 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x10 | t=25,168 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 2,01→2,59/30,0 | kickScale=1,20
  visualState: climbPitch=0,045° punchPitch=5,069° punchYaw=0,472° | back=0,0406м up=0,0151м active=True | tau=0,140с
  Hand_R local: base rot=(14,07, 287,82, 6,91) pos=(0,2874, 0,0131, -0,0058)
    → final rot=(11,53, 287,66, 6,88) pos=(0,3035, 0,0260, -0,0120) | Δrot=2,544° Δpos=(0,0161, 0,0129, -0,0061) |Δpos|=0,0215м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0334, 1,9190, 0,4226) rot=(0,47, 339,59, 34,76)
    → post pos=(19,0161, 1,9319, 0,4178) rot=(0,44, 339,89, 32,24) | Δpos=(-0,0174, 0,0129, -0,0048) |Δpos|=0,0221м Δrot=2,540°
  Muzzle world: pre pos=(18,7479, 1,9961, 1,0255) rot=(0,47, 339,59, 34,76)
    → post pos=(18,7372, 2,0118, 1,0235) rot=(0,44, 339,89, 32,24) | Δpos=(-0,0107, 0,0157, -0,0021) |Δpos|=0,0191м Δrot=2,540°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1346) | Δpos=(0,0000, 0,0000, -0,0114)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #8 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x10 | t=25,270 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 2,35→2,93/30,0 | kickScale=1,20
  visualState: climbPitch=0,058° punchPitch=5,147° punchYaw=0,367° | back=0,0412м up=0,0153м active=True | tau=0,140с
  Hand_R local: base rot=(13,99, 287,77, 6,89) pos=(0,2878, 0,0134, -0,0060)
    → final rot=(11,45, 287,56, 6,85) pos=(0,3040, 0,0264, -0,0122) | Δrot=2,551° Δpos=(0,0161, 0,0130, -0,0062) |Δpos|=0,0216м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0330, 1,9194, 0,4224) rot=(0,49, 339,62, 34,69)
    → post pos=(19,0156, 1,9323, 0,4176) rot=(0,50, 339,95, 32,16) | Δpos=(-0,0174, 0,0128, -0,0048) |Δpos|=0,0221м Δrot=2,549°
  Muzzle world: pre pos=(18,7479, 1,9964, 1,0256) rot=(0,49, 339,62, 34,69)
    → post pos=(18,7375, 2,0115, 1,0237) rot=(0,50, 339,95, 32,16) | Δpos=(-0,0104, 0,0151, -0,0019) |Δpos|=0,0184м Δrot=2,549°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1360) | Δpos=(0,0000, 0,0000, -0,0100)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #9 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x10 | t=25,378 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 2,68→3,25/30,0 | kickScale=1,20
  visualState: climbPitch=0,071° punchPitch=5,076° punchYaw=0,626° | back=0,0406м up=0,0151м active=True | tau=0,140с
  Hand_R local: base rot=(14,04, 287,72, 6,88) pos=(0,2875, 0,0131, -0,0059)
    → final rot=(11,48, 287,79, 6,92) pos=(0,3035, 0,0260, -0,0120) | Δrot=2,560° Δpos=(0,0160, 0,0129, -0,0061) |Δpos|=0,0214м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0334, 1,9192, 0,4225) rot=(0,52, 339,64, 34,74)
    → post pos=(19,0159, 1,9323, 0,4176) rot=(0,31, 339,82, 32,20) | Δpos=(-0,0175, 0,0131, -0,0049) |Δpos|=0,0224м Δrot=2,557°
  Muzzle world: pre pos=(18,7484, 1,9958, 1,0258) rot=(0,52, 339,64, 34,74)
    → post pos=(18,7364, 2,0138, 1,0228) rot=(0,31, 339,82, 32,20) | Δpos=(-0,0120, 0,0180, -0,0030) |Δpos|=0,0218м Δrot=2,557°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1334) | Δpos=(0,0000, 0,0000, -0,0126)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #10 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x10 | t=25,485 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 3,01→3,58/30,0 | kickScale=1,20
  visualState: climbPitch=0,086° punchPitch=5,063° punchYaw=0,353° | back=0,0405м up=0,0151м active=True | tau=0,140с
  Hand_R local: base rot=(14,04, 287,84, 6,91) pos=(0,2874, 0,0130, -0,0058)
    → final rot=(11,50, 287,56, 6,85) pos=(0,3034, 0,0259, -0,0119) | Δrot=2,548° Δpos=(0,0161, 0,0129, -0,0061) |Δpos|=0,0215м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0334, 1,9193, 0,4225) rot=(0,41, 339,58, 34,74)
    → post pos=(19,0161, 1,9321, 0,4177) rot=(0,48, 339,95, 32,23) | Δpos=(-0,0173, 0,0127, -0,0048) |Δpos|=0,0220м Δrot=2,545°
  Muzzle world: pre pos=(18,7478, 1,9971, 1,0253) rot=(0,41, 339,58, 34,74)
    → post pos=(18,7379, 2,0114, 1,0237) rot=(0,48, 339,95, 32,23) | Δpos=(-0,0099, 0,0143, -0,0016) |Δpos|=0,0175м Δrot=2,545°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1347) | Δpos=(0,0000, 0,0000, -0,0113)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Standing/Idle/Aiming(Aiming) x1 | t=28,079 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 0,00→0,56/30,0 | kickScale=1,20
  visualState: climbPitch=0,002° punchPitch=2,699° punchYaw=0,468° | back=0,0216м up=0,0080м active=True | tau=0,140с
  Hand_R local: base rot=(346,78, 295,78, 348,24) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(344,22, 296,76, 348,00) pos=(0,2924, 0,0027, -0,0081) | Δrot=2,730° Δpos=(0,0213, 0,0027, -0,0085) |Δpos|=0,0230м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0104, 1,9052, 0,4091) rot=(0,20, 339,02, 359,54)
    → post pos=(19,0035, 1,9283, 0,4083) rot=(0,13, 339,32, 356,83) | Δpos=(-0,0069, 0,0230, -0,0008) |Δpos|=0,0241м Δrot=2,728°
  Muzzle world: pre pos=(18,7733, 2,0034, 1,0297) rot=(0,20, 339,02, 359,54)
    → post pos=(18,7741, 2,0271, 1,0317) rot=(0,13, 339,32, 356,83) | Δpos=(0,0008, 0,0237, 0,0020) |Δpos|=0,0238м Δrot=2,728°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1256) | Δpos=(0,0000, 0,0000, -0,0203)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Standing/Idle/Aiming(Aiming) x3 | t=29,557 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 0,00→0,53/30,0 | kickScale=1,20
  visualState: climbPitch=0,002° punchPitch=2,699° punchYaw=0,152° | back=0,0216м up=0,0080м active=True | tau=0,140с
  Hand_R local: base rot=(346,78, 295,78, 348,24) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(344,16, 296,48, 348,07) pos=(0,2924, 0,0027, -0,0081) | Δrot=2,704° Δpos=(0,0213, 0,0027, -0,0085) |Δpos|=0,0230м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0103, 1,9067, 0,4085) rot=(0,03, 339,01, 359,60)
    → post pos=(19,0034, 1,9294, 0,4075) rot=(0,23, 339,30, 356,92) | Δpos=(-0,0069, 0,0226, -0,0009) |Δpos|=0,0237м Δrot=2,699°
  Muzzle world: pre pos=(18,7731, 2,0068, 1,0287) rot=(0,03, 339,01, 359,60)
    → post pos=(18,7735, 2,0271, 1,0309) rot=(0,23, 339,30, 356,92) | Δpos=(0,0004, 0,0202, 0,0022) |Δpos|=0,0203м Δrot=2,699°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,0941) | Δpos=(0,0000, 0,0000, -0,0519)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Standing/Idle/Aiming(Aiming) x3 | t=29,662 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 0,30→0,87/30,0 | kickScale=1,20
  visualState: climbPitch=0,005° punchPitch=3,979° punchYaw=0,430° | back=0,0318м up=0,0118м active=True | tau=0,140с
  Hand_R local: base rot=(345,43, 296,14, 348,15) pos=(0,2821, 0,0014, -0,0040)
    → final rot=(342,96, 297,00, 347,93) pos=(0,3025, 0,0040, -0,0121) | Δrot=2,615° Δpos=(0,0204, 0,0026, -0,0081) |Δpos|=0,0221м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0067, 1,9184, 0,4079) rot=(0,13, 339,16, 358,22)
    → post pos=(19,0001, 1,9403, 0,4071) rot=(0,15, 339,44, 355,62) | Δpos=(-0,0066, 0,0220, -0,0009) |Δpos|=0,0230м Δrot=2,613°
  Muzzle world: pre pos=(18,7733, 2,0173, 1,0298) rot=(0,13, 339,16, 358,22)
    → post pos=(18,7739, 2,0387, 1,0317) rot=(0,15, 339,44, 355,62) | Δpos=(0,0006, 0,0214, 0,0019) |Δpos|=0,0215м Δrot=2,613°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1325) | Δpos=(0,0000, 0,0000, -0,0135)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Standing/Idle/Aiming(Aiming) x3 | t=29,770 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 0,62→1,20/30,0 | kickScale=1,20
  visualState: climbPitch=0,010° punchPitch=4,536° punchYaw=0,773° | back=0,0363м up=0,0135м active=True | tau=0,140с
  Hand_R local: base rot=(344,88, 296,38, 348,10) pos=(0,2867, 0,0020, -0,0058)
    → final rot=(342,47, 297,42, 347,83) pos=(0,3069, 0,0046, -0,0138) | Δrot=2,611° Δpos=(0,0202, 0,0026, -0,0080) |Δpos|=0,0219м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0052, 1,9233, 0,4078) rot=(0,10, 339,23, 357,62)
    → post pos=(18,9987, 1,9453, 0,4069) rot=(359,95, 339,51, 355,03) | Δpos=(-0,0065, 0,0220, -0,0009) |Δpos|=0,0229м Δrot=2,609°
  Muzzle world: pre pos=(18,7735, 2,0226, 1,0302) rot=(0,10, 339,23, 357,62)
    → post pos=(18,7744, 2,0460, 1,0319) rot=(359,95, 339,51, 355,03) | Δpos=(0,0009, 0,0234, 0,0016) |Δpos|=0,0235м Δrot=2,609°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1345) | Δpos=(0,0000, 0,0000, -0,0115)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Standing/Idle/Aiming(Aiming) x10 | t=31,262 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 0,00→0,58/30,0 | kickScale=1,20
  visualState: climbPitch=0,002° punchPitch=2,699° punchYaw=0,406° | back=0,0216м up=0,0080м active=True | tau=0,140с
  Hand_R local: base rot=(346,78, 295,78, 348,24) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(344,21, 296,70, 348,02) pos=(0,2924, 0,0027, -0,0081) | Δrot=2,723° Δpos=(0,0213, 0,0027, -0,0085) |Δpos|=0,0230м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0104, 1,9052, 0,4091) rot=(0,21, 339,02, 359,54)
    → post pos=(19,0034, 1,9282, 0,4082) rot=(0,20, 339,31, 356,83) | Δpos=(-0,0069, 0,0229, -0,0008) |Δpos|=0,0240м Δrot=2,721°
  Muzzle world: pre pos=(18,7732, 2,0033, 1,0297) rot=(0,21, 339,02, 359,54)
    → post pos=(18,7739, 2,0262, 1,0317) rot=(0,20, 339,31, 356,83) | Δpos=(0,0007, 0,0229, 0,0020) |Δpos|=0,0230м Δrot=2,721°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1360) | Δpos=(0,0000, 0,0000, -0,0100)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Standing/Idle/Aiming(Aiming) x10 | t=31,363 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 0,35→0,92/30,0 | kickScale=1,20
  visualState: climbPitch=0,006° punchPitch=4,016° punchYaw=0,479° | back=0,0321м up=0,0119м active=True | tau=0,140с
  Hand_R local: base rot=(345,42, 296,27, 348,13) pos=(0,2823, 0,0014, -0,0041)
    → final rot=(342,93, 297,05, 347,92) pos=(0,3028, 0,0040, -0,0122) | Δrot=2,608° Δpos=(0,0204, 0,0026, -0,0081) |Δpos|=0,0221м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0067, 1,9172, 0,4087) rot=(0,21, 339,18, 358,11)
    → post pos=(19,0001, 1,9391, 0,4079) rot=(0,31, 339,45, 355,52) | Δpos=(-0,0066, 0,0219, -0,0008) |Δpos|=0,0229м Δrot=2,607°
  Muzzle world: pre pos=(18,7736, 2,0152, 1,0308) rot=(0,21, 339,18, 358,11)
    → post pos=(18,7741, 2,0356, 1,0329) rot=(0,31, 339,45, 355,52) | Δpos=(0,0005, 0,0205, 0,0021) |Δpos|=0,0206м Δrot=2,607°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1333) | Δpos=(0,0000, 0,0000, -0,0127)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Standing/Idle/Aiming(Aiming) x10 | t=31,466 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 0,69→1,26/30,0 | kickScale=1,20
  visualState: climbPitch=0,011° punchPitch=4,614° punchYaw=0,577° | back=0,0369м up=0,0137м active=True | tau=0,140с
  Hand_R local: base rot=(344,80, 296,43, 348,08) pos=(0,2874, 0,0021, -0,0061)
    → final rot=(342,36, 297,27, 347,86) pos=(0,3075, 0,0046, -0,0141) | Δrot=2,573° Δpos=(0,0201, 0,0026, -0,0080) |Δpos|=0,0218м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0051, 1,9225, 0,4085) rot=(0,28, 339,24, 357,47)
    → post pos=(18,9986, 1,9441, 0,4077) rot=(0,32, 339,51, 354,91) | Δpos=(-0,0065, 0,0216, -0,0008) |Δpos|=0,0226м Δrot=2,571°
  Muzzle world: pre pos=(18,7737, 2,0197, 1,0314) rot=(0,28, 339,24, 357,47)
    → post pos=(18,7743, 2,0405, 1,0334) rot=(0,32, 339,51, 354,91) | Δpos=(0,0006, 0,0208, 0,0019) |Δpos|=0,0209м Δrot=2,571°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1339) | Δpos=(0,0000, 0,0000, -0,0121)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #4 | unit=Unit(Clone) | cell=Standing/Idle/Aiming(Aiming) x10 | t=31,575 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 1,01→1,59/30,0 | kickScale=1,20
  visualState: climbPitch=0,017° punchPitch=4,827° punchYaw=0,677° | back=0,0386м up=0,0144м active=True | tau=0,140с
  Hand_R local: base rot=(344,59, 296,51, 348,06) pos=(0,2891, 0,0023, -0,0067)
    → final rot=(342,16, 297,41, 347,83) pos=(0,3092, 0,0049, -0,0147) | Δrot=2,584° Δpos=(0,0201, 0,0026, -0,0080) |Δpos|=0,0218м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0045, 1,9242, 0,4085) rot=(0,28, 339,27, 357,25)
    → post pos=(18,9980, 1,9459, 0,4077) rot=(0,27, 339,54, 354,68) | Δpos=(-0,0065, 0,0217, -0,0008) |Δpos|=0,0226м Δrot=2,581°
  Muzzle world: pre pos=(18,7738, 2,0213, 1,0317) rot=(0,28, 339,27, 357,25)
    → post pos=(18,7745, 2,0427, 1,0336) rot=(0,27, 339,54, 354,68) | Δpos=(0,0007, 0,0215, 0,0019) |Δpos|=0,0216м Δrot=2,581°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1362) | Δpos=(0,0000, 0,0000, -0,0098)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #5 | unit=Unit(Clone) | cell=Standing/Idle/Aiming(Aiming) x10 | t=31,682 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 1,35→1,91/30,0 | kickScale=1,20
  visualState: climbPitch=0,025° punchPitch=4,938° punchYaw=0,717° | back=0,0395м up=0,0147м active=True | tau=0,140с
  Hand_R local: base rot=(344,41, 296,60, 348,04) pos=(0,2906, 0,0025, -0,0073)
    → final rot=(342,05, 297,47, 347,81) pos=(0,3100, 0,0050, -0,0151) | Δrot=2,502° Δpos=(0,0195, 0,0025, -0,0077) |Δpos|=0,0211м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0041, 1,9259, 0,4084) rot=(0,25, 339,29, 357,05)
    → post pos=(18,9977, 1,9469, 0,4077) rot=(0,25, 339,55, 354,56) | Δpos=(-0,0063, 0,0210, -0,0008) |Δpos|=0,0219м Δrot=2,500°
  Muzzle world: pre pos=(18,7739, 2,0234, 1,0318) rot=(0,25, 339,29, 357,05)
    → post pos=(18,7745, 2,0441, 1,0336) rot=(0,25, 339,55, 354,56) | Δpos=(0,0006, 0,0207, 0,0018) |Δpos|=0,0208м Δrot=2,500°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1268) | Δpos=(0,0000, 0,0000, -0,0192)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #6 | unit=Unit(Clone) | cell=Standing/Idle/Aiming(Aiming) x10 | t=31,782 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 1,69→2,26/30,0 | kickScale=1,20
  visualState: climbPitch=0,035° punchPitch=5,115° punchYaw=0,682° | back=0,0409м up=0,0152м active=True | tau=0,140с
  Hand_R local: base rot=(344,29, 296,66, 348,03) pos=(0,2915, 0,0026, -0,0077)
    → final rot=(341,86, 297,48, 347,81) pos=(0,3114, 0,0051, -0,0156) | Δrot=2,555° Δpos=(0,0199, 0,0025, -0,0079) |Δpos|=0,0216м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0037, 1,9270, 0,4084) rot=(0,23, 339,30, 356,92)
    → post pos=(18,9973, 1,9484, 0,4076) rot=(0,29, 339,57, 354,38) | Δpos=(-0,0064, 0,0214, -0,0008) |Δpos|=0,0223м Δrot=2,552°
  Muzzle world: pre pos=(18,7740, 2,0248, 1,0318) rot=(0,23, 339,30, 356,92)
    → post pos=(18,7745, 2,0451, 1,0337) rot=(0,29, 339,57, 354,38) | Δpos=(0,0005, 0,0203, 0,0019) |Δpos|=0,0204м Δrot=2,552°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1356) | Δpos=(0,0000, 0,0000, -0,0104)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #7 | unit=Unit(Clone) | cell=Standing/Idle/Aiming(Aiming) x10 | t=31,884 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 2,04→2,60/30,0 | kickScale=1,20
  visualState: climbPitch=0,046° punchPitch=5,170° punchYaw=0,592° | back=0,0414м up=0,0154м active=True | tau=0,140с
  Hand_R local: base rot=(344,17, 296,67, 348,02) pos=(0,2924, 0,0027, -0,0081)
    → final rot=(341,78, 297,42, 347,82) pos=(0,3119, 0,0052, -0,0158) | Δrot=2,491° Δpos=(0,0194, 0,0025, -0,0077) |Δpos|=0,0210м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0035, 1,9281, 0,4083) rot=(0,24, 339,32, 356,80)
    → post pos=(18,9972, 1,9488, 0,4076) rot=(0,36, 339,57, 354,33) | Δpos=(-0,0063, 0,0208, -0,0008) |Δpos|=0,0217м Δrot=2,486°
  Muzzle world: pre pos=(18,7740, 2,0256, 1,0319) rot=(0,24, 339,32, 356,80)
    → post pos=(18,7745, 2,0447, 1,0338) rot=(0,36, 339,57, 354,33) | Δpos=(0,0004, 0,0191, 0,0019) |Δpos|=0,0192м Δrot=2,486°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1291) | Δpos=(0,0000, 0,0000, -0,0169)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #8 | unit=Unit(Clone) | cell=Standing/Idle/Aiming(Aiming) x10 | t=31,994 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 2,35→2,93/30,0 | kickScale=1,20
  visualState: climbPitch=0,058° punchPitch=5,058° punchYaw=0,601° | back=0,0405м up=0,0150м active=True | tau=0,140с
  Hand_R local: base rot=(344,30, 296,58, 348,05) pos=(0,2912, 0,0026, -0,0076)
    → final rot=(341,88, 297,40, 347,82) pos=(0,3110, 0,0051, -0,0155) | Δrot=2,545° Δpos=(0,0198, 0,0025, -0,0079) |Δpos|=0,0214м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0039, 1,9268, 0,4083) rot=(0,27, 339,30, 356,96)
    → post pos=(18,9975, 1,9480, 0,4076) rot=(0,33, 339,56, 354,43) | Δpos=(-0,0064, 0,0212, -0,0008) |Δpos|=0,0222м Δrot=2,541°
  Muzzle world: pre pos=(18,7740, 2,0240, 1,0318) rot=(0,27, 339,30, 356,96)
    → post pos=(18,7745, 2,0442, 1,0337) rot=(0,33, 339,56, 354,43) | Δpos=(0,0005, 0,0203, 0,0019) |Δpos|=0,0203м Δrot=2,541°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1333) | Δpos=(0,0000, 0,0000, -0,0127)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #9 | unit=Unit(Clone) | cell=Standing/Idle/Aiming(Aiming) x10 | t=32,102 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 2,69→3,25/30,0 | kickScale=1,20
  visualState: climbPitch=0,071° punchPitch=5,031° punchYaw=0,561° | back=0,0402м up=0,0150м active=True | tau=0,140с
  Hand_R local: base rot=(344,27, 296,60, 348,04) pos=(0,2914, 0,0026, -0,0077)
    → final rot=(341,89, 297,36, 347,83) pos=(0,3108, 0,0051, -0,0154) | Δrot=2,496° Δpos=(0,0194, 0,0025, -0,0077) |Δpos|=0,0210м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0038, 1,9270, 0,4083) rot=(0,25, 339,30, 356,93)
    → post pos=(18,9975, 1,9478, 0,4075) rot=(0,35, 339,56, 354,45) | Δpos=(-0,0063, 0,0208, -0,0008) |Δpos|=0,0217м Δrot=2,494°
  Muzzle world: pre pos=(18,7740, 2,0245, 1,0318) rot=(0,25, 339,30, 356,93)
    → post pos=(18,7745, 2,0438, 1,0337) rot=(0,35, 339,56, 354,45) | Δpos=(0,0005, 0,0194, 0,0019) |Δpos|=0,0195м Δrot=2,494°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1274) | Δpos=(0,0000, 0,0000, -0,0186)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #10 | unit=Unit(Clone) | cell=Standing/Idle/Aiming(Aiming) x10 | t=32,208 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,600 | penalty 3,01→3,58/30,0 | kickScale=1,20
  visualState: climbPitch=0,086° punchPitch=5,069° punchYaw=0,420° | back=0,0406м up=0,0151м active=True | tau=0,140с
  Hand_R local: base rot=(344,28, 296,58, 348,05) pos=(0,2911, 0,0026, -0,0076)
    → final rot=(341,81, 297,25, 347,86) pos=(0,3111, 0,0051, -0,0155) | Δrot=2,559° Δpos=(0,0199, 0,0025, -0,0079) |Δpos|=0,0216м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,0039, 1,9269, 0,4083) rot=(0,25, 339,30, 356,96)
    → post pos=(18,9974, 1,9481, 0,4075) rot=(0,46, 339,56, 354,42) | Δpos=(-0,0065, 0,0212, -0,0008) |Δpos|=0,0222м Δrot=2,556°
  Muzzle world: pre pos=(18,7740, 2,0243, 1,0317) rot=(0,25, 339,30, 356,96)
    → post pos=(18,7744, 2,0427, 1,0338) rot=(0,46, 339,56, 354,42) | Δpos=(0,0004, 0,0185, 0,0021) |Δpos|=0,0186м Δrot=2,556°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1357) | Δpos=(0,0000, 0,0000, -0,0103)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Standing/Walk/HipFire(HipFireWalk) x1 | t=36,128 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 0,00→0,72/30,0 | kickScale=1,20
  visualState: climbPitch=0,004° punchPitch=3,374° punchYaw=0,544° | back=0,0270м up=0,0100м active=True | tau=0,140с
  Hand_R local: base rot=(350,26, 358,05, 333,57) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(347,41, 359,89, 333,22) pos=(0,2766, 0,0043, -0,0276) | Δrot=3,373° Δpos=(0,0055, 0,0043, -0,0279) |Δpos|=0,0288м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,1606, 1,2833, 3,7507) rot=(358,10, 339,18, 2,64)
    → post pos=(19,1509, 1,3119, 3,7491) rot=(358,02, 339,58, 359,26) | Δpos=(-0,0097, 0,0286, -0,0016) |Δpos|=0,0302м Δrot=3,396°
  Muzzle world: pre pos=(18,9216, 1,4057, 4,3663) rot=(358,10, 339,18, 2,64)
    → post pos=(18,9219, 1,4352, 4,3682) rot=(358,02, 339,58, 359,26) | Δpos=(0,0003, 0,0296, 0,0020) |Δpos|=0,0297м Δrot=3,396°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1327) | Δpos=(0,0000, 0,0000, -0,0133)
  cameraDist=2,8м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Standing/Walk/HipFire(HipFireWalk) x3 | t=37,418 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 0,00→0,72/30,0 | kickScale=1,20
  visualState: climbPitch=0,004° punchPitch=3,374° punchYaw=0,774° | back=0,0270м up=0,0100м active=True | tau=0,140с
  Hand_R local: base rot=(350,08, 357,80, 333,62) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(347,40, 0,08, 333,11) pos=(0,2766, 0,0042, -0,0276) | Δrot=3,494° Δpos=(0,0055, 0,0042, -0,0280) |Δpos|=0,0288м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,1561, 1,2768, 5,7027) rot=(357,79, 338,19, 2,59)
    → post pos=(19,1476, 1,3052, 5,7008) rot=(357,52, 338,69, 359,20) | Δpos=(-0,0085, 0,0284, -0,0019) |Δpos|=0,0297м Δrot=3,413°
  Muzzle world: pre pos=(18,9069, 1,4027, 6,3134) rot=(357,79, 338,19, 2,59)
    → post pos=(18,9094, 1,4343, 6,3153) rot=(357,52, 338,69, 359,20) | Δpos=(0,0026, 0,0316, 0,0018) |Δpos|=0,0317м Δrot=3,413°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1293) | Δpos=(0,0000, 0,0000, -0,0167)
  cameraDist=2,3м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Standing/Walk/HipFire(HipFireWalk) x3 | t=37,519 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 0,49→1,21/30,0 | kickScale=1,20
  visualState: climbPitch=0,010° punchPitch=5,017° punchYaw=0,882° | back=0,0401м up=0,0149м active=True | tau=0,140с
  Hand_R local: base rot=(349,08, 359,50, 332,33) pos=(0,2740, 0,0023, -0,0142)
    → final rot=(346,42, 1,33, 331,89) pos=(0,2793, 0,0064, -0,0412) | Δrot=3,205° Δpos=(0,0053, 0,0042, -0,0269) |Δpos|=0,0278м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,1530, 1,2859, 5,8561) rot=(357,46, 338,02, 1,13)
    → post pos=(19,1434, 1,3123, 5,8528) rot=(357,43, 338,28, 357,81) | Δpos=(-0,0096, 0,0263, -0,0033) |Δpos|=0,0282м Δrot=3,314°
  Muzzle world: pre pos=(18,9046, 1,4157, 6,4664) rot=(357,46, 338,02, 1,13)
    → post pos=(18,9032, 1,4424, 6,4663) rot=(357,43, 338,28, 357,81) | Δpos=(-0,0014, 0,0266, -0,0001) |Δpos|=0,0267м Δrot=3,314°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1348) | Δpos=(0,0000, 0,0000, -0,0112)
  cameraDist=2,3м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Standing/Walk/HipFire(HipFireWalk) x3 | t=37,622 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 0,98→1,70/30,0 | kickScale=1,20
  visualState: climbPitch=0,020° punchPitch=5,775° punchYaw=1,006° | back=0,0462м up=0,0172м active=True | tau=0,140с
  Hand_R local: base rot=(348,67, 359,03, 331,81) pos=(0,2757, 0,0034, -0,0210)
    → final rot=(346,12, 0,95, 331,37) pos=(0,2814, 0,0076, -0,0472) | Δrot=3,158° Δpos=(0,0057, 0,0042, -0,0262) |Δpos|=0,0271м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,1429, 1,2871, 6,0079) rot=(357,38, 336,29, 0,14)
    → post pos=(19,1335, 1,3139, 6,0043) rot=(357,29, 336,51, 356,93) | Δpos=(-0,0094, 0,0268, -0,0036) |Δpos|=0,0286м Δrot=3,201°
  Muzzle world: pre pos=(18,8778, 1,4179, 6,6109) rot=(357,38, 336,29, 0,14)
    → post pos=(18,8760, 1,4455, 6,6104) rot=(357,29, 336,51, 356,93) | Δpos=(-0,0018, 0,0276, -0,0005) |Δpos|=0,0277м Δrot=3,201°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1328) | Δpos=(0,0000, 0,0000, -0,0132)
  cameraDist=2,3м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Standing/Walk/HipFire(HipFireWalk) x10 | t=38,948 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 0,00→0,73/30,0 | kickScale=1,20
  visualState: climbPitch=0,004° punchPitch=3,374° punchYaw=0,346° | back=0,0270м up=0,0100м active=True | tau=0,140с
  Hand_R local: base rot=(351,21, 356,20, 332,26) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(348,37, 358,04, 331,94) pos=(0,2777, 0,0047, -0,0273) | Δrot=3,373° Δpos=(0,0065, 0,0047, -0,0277) |Δpos|=0,0288м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,1413, 1,2603, 7,9948) rot=(357,16, 334,92, 2,96)
    → post pos=(19,1313, 1,2886, 7,9910) rot=(357,21, 335,27, 359,57) | Δpos=(-0,0099, 0,0282, -0,0038) |Δpos|=0,0302м Δrot=3,386°
  Muzzle world: pre pos=(18,8576, 1,3935, 8,5888) rot=(357,16, 334,92, 2,96)
    → post pos=(18,8566, 1,4213, 8,5893) rot=(357,21, 335,27, 359,57) | Δpos=(-0,0010, 0,0278, 0,0005) |Δpos|=0,0278м Δrot=3,386°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1374) | Δpos=(0,0000, 0,0000, -0,0086)
  cameraDist=2,8м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Standing/Walk/HipFire(HipFireWalk) x10 | t=39,059 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 0,48→1,20/30,0 | kickScale=1,20
  visualState: climbPitch=0,010° punchPitch=4,902° punchYaw=0,473° | back=0,0392м up=0,0146м active=True | tau=0,140с
  Hand_R local: base rot=(349,80, 356,74, 332,18) pos=(0,2744, 0,0023, -0,0132)
    → final rot=(347,06, 358,54, 331,89) pos=(0,2808, 0,0068, -0,0397) | Δrot=3,260° Δpos=(0,0064, 0,0045, -0,0265) |Δpos|=0,0276м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,1307, 1,2759, 8,1603) rot=(356,62, 334,91, 1,20)
    → post pos=(19,1207, 1,3031, 8,1568) rot=(356,68, 335,11, 357,95) | Δpos=(-0,0100, 0,0272, -0,0035) |Δpos|=0,0292м Δrot=3,241°
  Muzzle world: pre pos=(18,8503, 1,4154, 8,7543) rot=(356,62, 334,91, 1,20)
    → post pos=(18,8475, 1,4417, 8,7544) rot=(356,68, 335,11, 357,95) | Δpos=(-0,0028, 0,0264, 0,0001) |Δpos|=0,0265м Δrot=3,241°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1305) | Δpos=(0,0000, 0,0000, -0,0154)
  cameraDist=2,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Standing/Walk/HipFire(HipFireWalk) x10 | t=39,160 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 0,96→1,69/30,0 | kickScale=1,20
  visualState: climbPitch=0,020° punchPitch=5,752° punchYaw=0,491° | back=0,0460м up=0,0171м active=True | tau=0,140с
  Hand_R local: base rot=(349,09, 357,62, 332,57) pos=(0,2760, 0,0035, -0,0204)
    → final rot=(346,31, 359,39, 332,25) pos=(0,2820, 0,0080, -0,0468) | Δrot=3,272° Δpos=(0,0061, 0,0045, -0,0264) |Δpos|=0,0275м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,1189, 1,2844, 8,3151) rot=(356,09, 334,29, 0,19)
    → post pos=(19,1096, 1,3112, 8,3113) rot=(356,16, 334,56, 356,90) | Δpos=(-0,0093, 0,0268, -0,0038) |Δpos|=0,0286м Δrot=3,274°
  Muzzle world: pre pos=(18,8342, 1,4300, 8,9056) rot=(356,09, 334,29, 0,19)
    → post pos=(18,8328, 1,4557, 8,9058) rot=(356,16, 334,56, 356,90) | Δpos=(-0,0014, 0,0258, 0,0002) |Δpos|=0,0258м Δrot=3,274°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1372) | Δpos=(0,0000, 0,0000, -0,0087)
  cameraDist=2,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #4 | unit=Unit(Clone) | cell=Standing/Walk/HipFire(HipFireWalk) x10 | t=39,263 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 1,46→2,18/30,0 | kickScale=1,20
  visualState: climbPitch=0,032° punchPitch=6,129° punchYaw=0,597° | back=0,0490м up=0,0182м active=True | tau=0,140с
  Hand_R local: base rot=(348,64, 358,88, 332,76) pos=(0,2763, 0,0041, -0,0240)
    → final rot=(345,96, 0,78, 332,41) pos=(0,2817, 0,0086, -0,0501) | Δrot=3,267° Δpos=(0,0054, 0,0044, -0,0261) |Δpos|=0,0270м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,1220, 1,2860, 8,4638) rot=(355,69, 334,39, 359,74)
    → post pos=(19,1145, 1,3122, 8,4584) rot=(355,83, 334,70, 356,56) | Δpos=(-0,0074, 0,0261, -0,0054) |Δpos|=0,0277м Δrot=3,178°
  Muzzle world: pre pos=(18,8394, 1,4362, 9,0543) rot=(355,69, 334,39, 359,74)
    → post pos=(18,8401, 1,4605, 9,0531) rot=(355,83, 334,70, 356,56) | Δpos=(0,0007, 0,0244, -0,0012) |Δpos|=0,0244м Δrot=3,178°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1346) | Δpos=(0,0000, 0,0000, -0,0114)
  cameraDist=2,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #5 | unit=Unit(Clone) | cell=Standing/Walk/HipFire(HipFireWalk) x10 | t=39,367 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 1,94→2,67/30,0 | kickScale=1,20
  visualState: climbPitch=0,048° punchPitch=6,282° punchYaw=0,597° | back=0,0503м up=0,0187м active=True | tau=0,140с
  Hand_R local: base rot=(348,37, 359,19, 333,43) pos=(0,2763, 0,0043, -0,0249)
    → final rot=(345,56, 0,92, 333,12) pos=(0,2816, 0,0087, -0,0515) | Δrot=3,277° Δpos=(0,0053, 0,0044, -0,0265) |Δpos|=0,0274м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,1338, 1,2835, 8,6103) rot=(356,79, 335,59, 359,53)
    → post pos=(19,1258, 1,3098, 8,6060) rot=(356,99, 336,01, 356,26) | Δpos=(-0,0080, 0,0263, -0,0043) |Δpos|=0,0279м Δrot=3,270°
  Muzzle world: pre pos=(18,8629, 1,4210, 9,2092) rot=(356,79, 335,59, 359,53)
    → post pos=(18,8643, 1,4448, 9,2096) rot=(356,99, 336,01, 356,26) | Δpos=(0,0013, 0,0238, 0,0004) |Δpos|=0,0238м Δrot=3,270°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1395) | Δpos=(0,0000, 0,0000, -0,0065)
  cameraDist=2,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #6 | unit=Unit(Clone) | cell=Standing/Walk/HipFire(HipFireWalk) x10 | t=39,475 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 2,42→3,14/30,0 | kickScale=1,20
  visualState: climbPitch=0,067° punchPitch=6,284° punchYaw=0,503° | back=0,0503м up=0,0187м active=True | tau=0,140с
  Hand_R local: base rot=(347,83, 359,62, 333,66) pos=(0,2761, 0,0041, -0,0253)
    → final rot=(345,01, 1,33, 333,27) pos=(0,2811, 0,0083, -0,0517) | Δrot=3,268° Δpos=(0,0050, 0,0042, -0,0263) |Δpos|=0,0271м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,1427, 1,2810, 8,7660) rot=(357,80, 336,98, 359,43)
    → post pos=(19,1344, 1,3074, 8,7632) rot=(358,10, 337,41, 356,24) | Δpos=(-0,0083, 0,0265, -0,0029) |Δpos|=0,0279м Δrot=3,219°
  Muzzle world: pre pos=(18,8857, 1,4068, 9,3736) rot=(357,80, 336,98, 359,43)
    → post pos=(18,8869, 1,4297, 9,3754) rot=(358,10, 337,41, 356,24) | Δpos=(0,0012, 0,0228, 0,0018) |Δpos|=0,0229м Δrot=3,219°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1366) | Δpos=(0,0000, 0,0000, -0,0094)
  cameraDist=2,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #7 | unit=Unit(Clone) | cell=Standing/Walk/HipFire(HipFireWalk) x10 | t=39,582 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 2,90→3,62/30,0 | kickScale=1,20
  visualState: climbPitch=0,088° punchPitch=6,310° punchYaw=0,497° | back=0,0505м up=0,0188м active=True | tau=0,140с
  Hand_R local: base rot=(347,42, 359,32, 333,61) pos=(0,2763, 0,0040, -0,0257)
    → final rot=(344,62, 0,82, 333,23) pos=(0,2817, 0,0080, -0,0518) | Δrot=3,148° Δpos=(0,0053, 0,0040, -0,0261) |Δpos|=0,0269м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,1390, 1,2842, 8,9331) rot=(358,18, 336,46, 359,32)
    → post pos=(19,1300, 1,3107, 8,9314) rot=(358,35, 336,84, 356,13) | Δpos=(-0,0090, 0,0265, -0,0017) |Δpos|=0,0281м Δrot=3,204°
  Muzzle world: pre pos=(18,8763, 1,4057, 9,5391) rot=(358,18, 336,46, 359,32)
    → post pos=(18,8763, 1,4300, 9,5417) rot=(358,35, 336,84, 356,13) | Δpos=(0,0000, 0,0243, 0,0026) |Δpos|=0,0244м Δrot=3,204°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1348) | Δpos=(0,0000, 0,0000, -0,0112)
  cameraDist=2,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #8 | unit=Unit(Clone) | cell=Standing/Walk/HipFire(HipFireWalk) x10 | t=39,695 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 3,39→4,09/30,0 | kickScale=1,20
  visualState: climbPitch=0,112° punchPitch=6,188° punchYaw=0,865° | back=0,0495м up=0,0184м active=True | tau=0,140с
  Hand_R local: base rot=(347,15, 359,07, 333,60) pos=(0,2767, 0,0041, -0,0266)
    → final rot=(344,77, 1,18, 333,13) pos=(0,2814, 0,0078, -0,0508) | Δrot=3,136° Δpos=(0,0047, 0,0037, -0,0243) |Δpos|=0,0250м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,1366, 1,2870, 9,1149) rot=(357,96, 336,26, 359,12)
    → post pos=(19,1300, 1,3117, 9,1138) rot=(357,79, 336,76, 356,16) | Δpos=(-0,0066, 0,0246, -0,0011) |Δpos|=0,0255м Δrot=2,992°
  Muzzle world: pre pos=(18,8722, 1,4111, 9,7197) rot=(357,96, 336,26, 359,12)
    → post pos=(18,8759, 1,4375, 9,7225) rot=(357,79, 336,76, 356,16) | Δpos=(0,0037, 0,0264, 0,0029) |Δpos|=0,0268м Δrot=2,992°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1081) | Δpos=(0,0000, 0,0000, -0,0378)
  cameraDist=2,8м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #9 | unit=Unit(Clone) | cell=Standing/Walk/HipFire(HipFireWalk) x10 | t=39,810 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 3,84→4,55/30,0 | kickScale=1,20
  visualState: climbPitch=0,137° punchPitch=6,085° punchYaw=1,267° | back=0,0487м up=0,0181м active=True | tau=0,140с
  Hand_R local: base rot=(347,86, 0,05, 332,47) pos=(0,2760, 0,0039, -0,0247)
    → final rot=(345,51, 2,06, 331,94) pos=(0,2811, 0,0079, -0,0500) | Δrot=3,057° Δpos=(0,0051, 0,0040, -0,0253) |Δpos|=0,0261м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,1406, 1,2797, 9,2941) rot=(357,60, 336,10, 359,68)
    → post pos=(19,1318, 1,3045, 9,2906) rot=(357,23, 336,31, 356,49) | Δpos=(-0,0088, 0,0248, -0,0036) |Δpos|=0,0265м Δrot=3,206°
  Muzzle world: pre pos=(18,8741, 1,4079, 9,8970) rot=(357,60, 336,10, 359,68)
    → post pos=(18,8729, 1,4367, 9,8959) rot=(357,23, 336,31, 356,49) | Δpos=(-0,0012, 0,0288, -0,0011) |Δpos|=0,0288м Δrot=3,206°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1216) | Δpos=(0,0000, 0,0000, -0,0244)
  cameraDist=2,8м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #10 | unit=Unit(Clone) | cell=Standing/Walk/HipFire(HipFireWalk) x10 | t=39,924 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 4,30→5,01/30,0 | kickScale=1,20
  visualState: climbPitch=0,166° punchPitch=6,076° punchYaw=1,263° | back=0,0486м up=0,0181м active=True | tau=0,140с
  Hand_R local: base rot=(348,30, 359,23, 331,92) pos=(0,2765, 0,0040, -0,0243)
    → final rot=(345,86, 1,17, 331,45) pos=(0,2820, 0,0081, -0,0497) | Δrot=3,085° Δpos=(0,0056, 0,0041, -0,0254) |Δpos|=0,0263м | applied=True canApply=True
  WeaponRoot world: pre pos=(19,1333, 1,2738, 9,4618) rot=(357,26, 334,33, 359,54)
    → post pos=(19,1242, 1,2999, 9,4579) rot=(357,09, 334,51, 356,40) | Δpos=(-0,0091, 0,0261, -0,0040) |Δpos|=0,0279м Δrot=3,140°
  Muzzle world: pre pos=(18,8488, 1,4059, 10,0556) rot=(357,26, 334,33, 359,54)
    → post pos=(18,8467, 1,4338, 10,0546) rot=(357,09, 334,51, 356,40) | Δpos=(-0,0020, 0,0279, -0,0010) |Δpos|=0,0280м Δrot=3,140°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1247) | Δpos=(0,0000, 0,0000, -0,0212)
  cameraDist=2,9м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Standing/Walk/PointAim(PointAim) x1 | t=42,950 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 0,00→0,72/30,0 | kickScale=1,20
  visualState: climbPitch=0,004° punchPitch=3,374° punchYaw=0,188° | back=0,0270м up=0,0100м active=True | tau=0,140с
  Hand_R local: base rot=(16,73, 287,86, 6,81) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(13,35, 287,61, 6,75) pos=(0,2927, 0,0173, -0,0078) | Δrot=3,381° Δpos=(0,0215, 0,0173, -0,0082) |Δpos|=0,0288м | applied=True canApply=True
  WeaponRoot world: pre pos=(18,9792, 1,4371, 14,1408) rot=(359,83, 330,83, 39,21)
    → post pos=(18,9572, 1,4535, 14,1310) rot=(359,83, 331,24, 35,79) | Δpos=(-0,0220, 0,0164, -0,0098) |Δpos|=0,0291м Δrot=3,444°
  Muzzle world: pre pos=(18,6002, 1,5169, 14,6895) rot=(359,83, 330,83, 39,21)
    → post pos=(18,5863, 1,5370, 14,6846) rot=(359,83, 331,24, 35,79) | Δpos=(-0,0138, 0,0200, -0,0048) |Δpos|=0,0248м Δrot=3,444°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1350) | Δpos=(0,0000, 0,0000, -0,0109)
  cameraDist=2,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Standing/Walk/PointAim(PointAim) x3 | t=44,418 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 0,00→0,72/30,0 | kickScale=1,20
  visualState: climbPitch=0,004° punchPitch=3,374° punchYaw=0,302° | back=0,0270м up=0,0100м active=True | tau=0,140с
  Hand_R local: base rot=(16,73, 287,86, 6,81) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(13,34, 287,71, 6,78) pos=(0,2927, 0,0173, -0,0078) | Δrot=3,387° Δpos=(0,0215, 0,0173, -0,0082) |Δpos|=0,0288м | applied=True canApply=True
  WeaponRoot world: pre pos=(18,9799, 1,4220, 16,3632) rot=(0,25, 330,23, 38,57)
    → post pos=(18,9577, 1,4390, 16,3517) rot=(0,13, 330,68, 35,36) | Δpos=(-0,0222, 0,0170, -0,0115) |Δpos|=0,0303м Δrot=3,246°
  Muzzle world: pre pos=(18,5956, 1,4977, 16,9087) rot=(0,25, 330,23, 38,57)
    → post pos=(18,5817, 1,5195, 16,9023) rot=(0,13, 330,68, 35,36) | Δpos=(-0,0140, 0,0218, -0,0065) |Δpos|=0,0267м Δrot=3,246°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1349) | Δpos=(0,0000, 0,0000, -0,0111)
  cameraDist=2,8м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Standing/Walk/PointAim(PointAim) x3 | t=44,519 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 0,49→1,22/30,0 | kickScale=1,20
  visualState: climbPitch=0,010° punchPitch=5,017° punchYaw=0,436° | back=0,0401м up=0,0149м active=True | tau=0,140с
  Hand_R local: base rot=(14,97, 287,78, 6,79) pos=(0,2823, 0,0090, -0,0039)
    → final rot=(11,69, 287,62, 6,77) pos=(0,3031, 0,0257, -0,0118) | Δrot=3,283° Δpos=(0,0208, 0,0168, -0,0079) |Δpos|=0,0279м | applied=True canApply=True
  WeaponRoot world: pre pos=(18,9716, 1,4329, 16,4994) rot=(0,16, 331,63, 38,22)
    → post pos=(18,9499, 1,4488, 16,4895) rot=(0,09, 332,13, 35,04) | Δpos=(-0,0217, 0,0159, -0,0099) |Δpos|=0,0287м Δrot=3,217°
  Muzzle world: pre pos=(18,6013, 1,5100, 17,0543) rot=(0,16, 331,63, 38,22)
    → post pos=(18,5884, 1,5300, 17,0496) rot=(0,09, 332,13, 35,04) | Δpos=(-0,0129, 0,0200, -0,0047) |Δpos|=0,0242м Δrot=3,217°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1370) | Δpos=(0,0000, 0,0000, -0,0090)
  cameraDist=2,8м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Standing/Walk/PointAim(PointAim) x3 | t=44,630 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 0,97→1,69/30,0 | kickScale=1,20
  visualState: climbPitch=0,020° punchPitch=5,646° punchYaw=1,016° | back=0,0452м up=0,0168м active=True | tau=0,140с
  Hand_R local: base rot=(14,24, 287,74, 6,78) pos=(0,2869, 0,0127, -0,0056)
    → final rot=(11,00, 288,04, 6,89) pos=(0,3071, 0,0290, -0,0133) | Δrot=3,252° Δpos=(0,0202, 0,0163, -0,0077) |Δpos|=0,0270м | applied=True canApply=True
  WeaponRoot world: pre pos=(18,9630, 1,4367, 16,6601) rot=(0,59, 331,99, 38,28)
    → post pos=(18,9410, 1,4517, 16,6502) rot=(0,24, 332,14, 35,11) | Δpos=(-0,0219, 0,0150, -0,0098) |Δpos|=0,0283м Δrot=3,197°
  Muzzle world: pre pos=(18,5958, 1,5087, 17,2178) rot=(0,59, 331,99, 38,28)
    → post pos=(18,5794, 1,5311, 17,2105) rot=(0,24, 332,14, 35,11) | Δpos=(-0,0164, 0,0224, -0,0072) |Δpos|=0,0287м Δrot=3,197°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1302) | Δpos=(0,0000, 0,0000, -0,0158)
  cameraDist=2,8м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Standing/Walk/PointAim(PointAim) x10 | t=46,155 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 0,00→0,72/30,0 | kickScale=1,20
  visualState: climbPitch=0,004° punchPitch=3,374° punchYaw=0,658° | back=0,0270м up=0,0100м active=True | tau=0,140с
  Hand_R local: base rot=(16,73, 287,86, 6,81) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(13,31, 288,02, 6,86) pos=(0,2927, 0,0173, -0,0078) | Δrot=3,424° Δpos=(0,0215, 0,0173, -0,0082) |Δpos|=0,0288м | applied=True canApply=True
  WeaponRoot world: pre pos=(18,9605, 1,4103, 18,9608) rot=(0,55, 329,83, 40,05)
    → post pos=(18,9365, 1,4282, 18,9469) rot=(359,99, 329,65, 36,52) | Δpos=(-0,0240, 0,0178, -0,0139) |Δpos|=0,0330м Δrot=3,571°
  Muzzle world: pre pos=(18,5706, 1,4809, 19,5030) rot=(0,55, 329,83, 40,05)
    → post pos=(18,5495, 1,5091, 19,4897) rot=(359,99, 329,65, 36,52) | Δpos=(-0,0211, 0,0282, -0,0133) |Δpos|=0,0376м Δrot=3,571°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1292) | Δpos=(0,0000, 0,0000, -0,0167)
  cameraDist=2,0м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Standing/Walk/PointAim(PointAim) x10 | t=46,261 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 0,49→1,20/30,0 | kickScale=1,20
  visualState: climbPitch=0,010° punchPitch=4,957° punchYaw=0,674° | back=0,0397м up=0,0147м active=True | tau=0,140с
  Hand_R local: base rot=(355,24, 288,80, 6,98) pos=(0,4201, 0,0829, -0,0569)
    → final rot=(350,24, 288,76, 7,01) pos=(0,4581, 0,0942, -0,0717) | Δrot=4,999° Δpos=(0,0380, 0,0113, -0,0148) |Δpos|=0,0423м | applied=True canApply=True
  WeaponRoot world: pre pos=(18,7696, 1,5409, 19,0375) rot=(358,09, 330,01, 18,43)
    → post pos=(18,7468, 1,5768, 19,0264) rot=(357,97, 330,53, 13,45) | Δpos=(-0,0228, 0,0358, -0,0111) |Δpos|=0,0439м Δrot=4,997°
  Muzzle world: pre pos=(18,4120, 1,6583, 19,5937) rot=(358,09, 330,01, 18,43)
    → post pos=(18,4017, 1,6980, 19,5896) rot=(357,97, 330,53, 13,45) | Δpos=(-0,0103, 0,0397, -0,0041) |Δpos|=0,0412м Δrot=4,997°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1258) | Δpos=(0,0000, 0,0000, -0,0202)
  cameraDist=2,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Standing/Walk/PointAim(PointAim) x10 | t=46,367 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 0,96→1,68/30,0 | kickScale=1,20
  visualState: climbPitch=0,019° punchPitch=5,710° punchYaw=0,980° | back=0,0457м up=0,0170м active=True | tau=0,140с
  Hand_R local: base rot=(315,79, 288,36, 7,33) pos=(0,7246, 0,0695, -0,1747)
    → final rot=(310,00, 288,50, 7,26) pos=(0,7661, 0,0497, -0,1908) | Δrot=5,789° Δpos=(0,0415, -0,0198, -0,0161) |Δpos|=0,0487м | applied=True canApply=True
  WeaponRoot world: pre pos=(18,6071, 1,8637, 19,1374) rot=(358,33, 332,94, 339,07)
    → post pos=(18,6095, 1,9143, 19,1408) rot=(358,44, 333,56, 333,30) | Δpos=(0,0024, 0,0507, 0,0034) |Δpos|=0,0508м Δrot=5,788°
  Muzzle world: pre pos=(18,3384, 1,9768, 19,7424) rot=(358,33, 332,94, 339,07)
    → post pos=(18,3555, 2,0221, 19,7530) rot=(358,44, 333,56, 333,30) | Δpos=(0,0171, 0,0453, 0,0107) |Δpos|=0,0496м Δrot=5,788°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1365) | Δpos=(0,0000, 0,0000, -0,0095)
  cameraDist=2,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #4 | unit=Unit(Clone) | cell=Standing/Walk/PointAim(PointAim) x10 | t=46,474 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 1,43→2,16/30,0 | kickScale=1,20
  visualState: climbPitch=0,032° punchPitch=6,017° punchYaw=1,073° | back=0,0481м up=0,0179м active=True | tau=0,140с
  Hand_R local: base rot=(274,68, 294,88, 1,26) pos=(0,9561, -0,1581, -0,2697)
    → final rot=(271,59, 85,75, 210,47) pos=(0,9756, -0,2046, -0,2792) | Δrot=6,118° Δpos=(0,0196, -0,0465, -0,0095) |Δpos|=0,0514м | applied=True canApply=True
  WeaponRoot world: pre pos=(18,6414, 2,1887, 19,3483) rot=(0,41, 334,11, 298,11)
    → post pos=(18,6738, 2,2264, 19,3683) rot=(0,91, 334,55, 292,03) | Δpos=(0,0324, 0,0377, 0,0200) |Δpos|=0,0536м Δrot=6,117°
  Muzzle world: pre pos=(18,4310, 2,2313, 19,9847) rot=(0,41, 334,11, 298,11)
    → post pos=(18,4724, 2,2535, 20,0084) rot=(0,91, 334,55, 292,03) | Δpos=(0,0414, 0,0223, 0,0237) |Δpos|=0,0526м Δrot=6,117°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1369) | Δpos=(0,0000, 0,0000, -0,0091)
  cameraDist=2,3м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #5 | unit=Unit(Clone) | cell=Standing/Walk/PointAim(PointAim) x10 | t=46,623 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 1,82→2,54/30,0 | kickScale=1,20
  visualState: climbPitch=0,044° punchPitch=5,462° punchYaw=0,610° | back=0,0437м up=0,0163м active=True | tau=0,140с
  Hand_R local: base rot=(14,40, 287,93, 6,83) pos=(0,2857, 0,0117, -0,0052)
    → final rot=(11,20, 287,72, 6,80) pos=(0,3060, 0,0280, -0,0129) | Δrot=3,205° Δpos=(0,0203, 0,0163, -0,0077) |Δpos|=0,0271м | applied=True canApply=True
  WeaponRoot world: pre pos=(18,6188, 1,4227, 19,5785) rot=(359,79, 319,16, 35,67)
    → post pos=(18,5991, 1,4393, 19,5638) rot=(359,72, 319,48, 32,65) | Δpos=(-0,0197, 0,0166, -0,0146) |Δpos|=0,0296м Δrot=3,041°
  Muzzle world: pre pos=(18,1404, 1,5068, 20,0422) rot=(359,79, 319,16, 35,67)
    → post pos=(18,1267, 1,5272, 20,0331) rot=(359,72, 319,48, 32,65) | Δpos=(-0,0136, 0,0204, -0,0091) |Δpos|=0,0262м Δrot=3,041°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1296) | Δpos=(0,0000, 0,0000, -0,0164)
  cameraDist=2,3м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #6 | unit=Unit(Clone) | cell=Standing/Walk/PointAim(PointAim) x10 | t=46,731 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 2,29→3,01/30,0 | kickScale=1,20
  visualState: climbPitch=0,061° punchPitch=5,893° punchYaw=0,886° | back=0,0471м up=0,0175м active=True | tau=0,140с
  Hand_R local: base rot=(13,92, 287,78, 6,79) pos=(0,2888, 0,0142, -0,0063)
    → final rot=(10,73, 287,90, 6,85) pos=(0,3087, 0,0302, -0,0139) | Δrot=3,194° Δpos=(0,0200, 0,0161, -0,0076) |Δpos|=0,0267м | applied=True canApply=True
  WeaponRoot world: pre pos=(18,5784, 1,4287, 19,7223) rot=(359,53, 323,50, 36,78)
    → post pos=(18,5583, 1,4446, 19,7070) rot=(359,20, 323,66, 33,82) | Δpos=(-0,0202, 0,0159, -0,0153) |Δpos|=0,0299м Δrot=2,976°
  Muzzle world: pre pos=(18,1356, 1,5146, 20,2198) rot=(359,53, 323,50, 36,78)
    → post pos=(18,1205, 1,5373, 20,2077) rot=(359,20, 323,66, 33,82) | Δpos=(-0,0151, 0,0227, -0,0120) |Δpos|=0,0298м Δrot=2,976°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1286) | Δpos=(0,0000, 0,0000, -0,0174)
  cameraDist=2,2м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #7 | unit=Unit(Clone) | cell=Standing/Walk/PointAim(PointAim) x10 | t=46,835 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 2,78→3,50/30,0 | kickScale=1,20
  visualState: climbPitch=0,082° punchPitch=6,187° punchYaw=1,101° | back=0,0495м up=0,0184м active=True | tau=0,140с
  Hand_R local: base rot=(13,58, 287,88, 6,82) pos=(0,2907, 0,0158, -0,0071)
    → final rot=(10,39, 288,04, 6,90) pos=(0,3106, 0,0318, -0,0146) | Δrot=3,190° Δpos=(0,0198, 0,0160, -0,0075) |Δpos|=0,0266м | applied=True canApply=True
  WeaponRoot world: pre pos=(18,5333, 1,4306, 19,8620) rot=(359,47, 326,46, 37,37)
    → post pos=(18,5133, 1,4456, 19,8501) rot=(359,18, 326,71, 34,25) | Δpos=(-0,0200, 0,0149, -0,0119) |Δpos|=0,0277м Δrot=3,142°
  Muzzle world: pre pos=(18,1160, 1,5167, 20,3811) rot=(359,47, 326,46, 37,37)
    → post pos=(18,1023, 1,5382, 20,3730) rot=(359,18, 326,71, 34,25) | Δpos=(-0,0138, 0,0215, -0,0080) |Δpos|=0,0267м Δrot=3,142°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1297) | Δpos=(0,0000, 0,0000, -0,0163)
  cameraDist=2,3м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #8 | unit=Unit(Clone) | cell=Standing/Walk/PointAim(PointAim) x10 | t=46,945 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 3,25→3,97/30,0 | kickScale=1,20
  visualState: climbPitch=0,106° punchPitch=6,191° punchYaw=1,148° | back=0,0495м up=0,0184м active=True | tau=0,140с
  Hand_R local: base rot=(13,59, 287,95, 6,84) pos=(0,2905, 0,0156, -0,0070)
    → final rot=(10,36, 288,08, 6,91) pos=(0,3106, 0,0318, -0,0146) | Δrot=3,231° Δpos=(0,0201, 0,0162, -0,0076) |Δpos|=0,0269м | applied=True canApply=True
  WeaponRoot world: pre pos=(18,4751, 1,4296, 20,0151) rot=(359,93, 327,73, 37,83)
    → post pos=(18,4534, 1,4445, 20,0043) rot=(359,72, 327,93, 34,68) | Δpos=(-0,0217, 0,0149, -0,0108) |Δpos|=0,0285м Δrot=3,157°
  Muzzle world: pre pos=(18,0685, 1,5097, 20,5435) rot=(359,93, 327,73, 37,83)
    → post pos=(18,0526, 1,5304, 20,5362) rot=(359,72, 327,93, 34,68) | Δpos=(-0,0160, 0,0207, -0,0073) |Δpos|=0,0271м Δrot=3,157°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1340) | Δpos=(0,0000, 0,0000, -0,0120)
  cameraDist=2,3м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #9 | unit=Unit(Clone) | cell=Standing/Walk/PointAim(PointAim) x10 | t=47,052 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 3,74→4,45/30,0 | kickScale=1,20
  visualState: climbPitch=0,132° punchPitch=6,249° punchYaw=0,899° | back=0,0500м up=0,0186м active=True | tau=0,140с
  Hand_R local: base rot=(13,42, 287,97, 6,85) pos=(0,2914, 0,0163, -0,0073)
    → final rot=(10,30, 287,86, 6,85) pos=(0,3110, 0,0321, -0,0148) | Δrot=3,121° Δpos=(0,0196, 0,0158, -0,0074) |Δpos|=0,0262м | applied=True canApply=True
  WeaponRoot world: pre pos=(18,4081, 1,4272, 20,1690) rot=(0,74, 328,50, 38,36)
    → post pos=(18,3870, 1,4408, 20,1597) rot=(0,70, 328,91, 35,32) | Δpos=(-0,0211, 0,0136, -0,0093) |Δpos|=0,0268м Δrot=3,070°
  Muzzle world: pre pos=(18,0075, 1,4974, 20,7034) rot=(0,74, 328,50, 38,36)
    → post pos=(17,9939, 1,5147, 20,6991) rot=(0,70, 328,91, 35,32) | Δpos=(-0,0136, 0,0173, -0,0043) |Δpos|=0,0224м Δrot=3,070°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1259) | Δpos=(0,0000, 0,0000, -0,0201)
  cameraDist=2,3м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #10 | unit=Unit(Clone) | cell=Standing/Walk/PointAim(PointAim) x10 | t=47,163 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 4,20→4,92/30,0 | kickScale=1,20
  visualState: climbPitch=0,160° punchPitch=6,208° punchYaw=0,357° | back=0,0497м up=0,0185м active=True | tau=0,140с
  Hand_R local: base rot=(13,53, 287,85, 6,81) pos=(0,2907, 0,0157, -0,0070)
    → final rot=(10,37, 287,40, 6,71) pos=(0,3107, 0,0319, -0,0147) | Δrot=3,187° Δpos=(0,0200, 0,0161, -0,0076) |Δpos|=0,0268м | applied=True canApply=True
  WeaponRoot world: pre pos=(18,3418, 1,4251, 20,3322) rot=(0,95, 329,31, 38,56)
    → post pos=(18,3214, 1,4392, 20,3227) rot=(1,04, 329,95, 35,35) | Δpos=(-0,0204, 0,0141, -0,0095) |Δpos|=0,0266м Δrot=3,281°
  Muzzle world: pre pos=(17,9484, 1,4926, 20,8722) rot=(0,95, 329,31, 38,56)
    → post pos=(17,9378, 1,5091, 20,8695) rot=(1,04, 329,95, 35,35) | Δpos=(-0,0106, 0,0165, -0,0028) |Δpos|=0,0198м Δrot=3,281°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1330) | Δpos=(0,0000, 0,0000, -0,0130)
  cameraDist=2,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Standing/Walk/Aiming(Aiming) x1 | t=50,169 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 0,00→0,72/30,0 | kickScale=1,20
  visualState: climbPitch=0,004° punchPitch=3,374° punchYaw=0,019° | back=0,0270м up=0,0100м active=True | tau=0,140с
  Hand_R local: base rot=(346,87, 295,80, 348,13) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(343,57, 296,54, 347,95) pos=(0,2977, 0,0034, -0,0102) | Δrot=3,377° Δpos=(0,0266, 0,0034, -0,0106) |Δpos|=0,0288м | applied=True canApply=True
  WeaponRoot world: pre pos=(16,5961, 1,4086, 24,4767) rot=(359,27, 326,79, 1,53)
    → post pos=(16,5875, 1,4366, 24,4713) rot=(359,63, 327,22, 358,35) | Δpos=(-0,0086, 0,0279, -0,0054) |Δpos|=0,0297м Δrot=3,224°
  Muzzle world: pre pos=(16,2309, 1,5175, 25,0296) rot=(359,27, 326,79, 1,53)
    → post pos=(16,2309, 1,5413, 25,0306) rot=(359,63, 327,22, 358,35) | Δpos=(0,0000, 0,0238, 0,0009) |Δpos|=0,0238м Δrot=3,224°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1317) | Δpos=(0,0000, 0,0000, -0,0143)
  cameraDist=3,3м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Standing/Walk/Aiming(Aiming) x3 | t=51,655 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 0,00→0,72/30,0 | kickScale=1,20
  visualState: climbPitch=0,004° punchPitch=3,374° punchYaw=0,402° | back=0,0270м up=0,0100м active=True | tau=0,140с
  Hand_R local: base rot=(346,87, 295,80, 348,13) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(343,64, 296,87, 347,87) pos=(0,2977, 0,0034, -0,0102) | Δrot=3,395° Δpos=(0,0266, 0,0034, -0,0106) |Δpos|=0,0288м | applied=True canApply=True
  WeaponRoot world: pre pos=(15,7278, 1,4039, 26,5248) rot=(0,97, 327,77, 3,98)
    → post pos=(15,7172, 1,4315, 26,5223) rot=(1,06, 328,25, 0,66) | Δpos=(-0,0105, 0,0276, -0,0025) |Δpos|=0,0296м Δrot=3,360°
  Muzzle world: pre pos=(15,3669, 1,4929, 27,0842) rot=(0,97, 327,77, 3,98)
    → post pos=(15,3659, 1,5196, 27,0878) rot=(1,06, 328,25, 0,66) | Δpos=(-0,0009, 0,0268, 0,0037) |Δpos|=0,0271м Δrot=3,360°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1347) | Δpos=(0,0000, 0,0000, -0,0113)
  cameraDist=3,2м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Standing/Walk/Aiming(Aiming) x3 | t=51,758 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 0,49→1,21/30,0 | kickScale=1,20
  visualState: climbPitch=0,010° punchPitch=4,990° punchYaw=0,643° | back=0,0399м up=0,0148м active=True | tau=0,140с
  Hand_R local: base rot=(345,22, 296,35, 348,00) pos=(0,2847, 0,0018, -0,0050)
    → final rot=(342,10, 297,44, 347,72) pos=(0,3104, 0,0051, -0,0152) | Δrot=3,293° Δpos=(0,0257, 0,0033, -0,0102) |Δpos|=0,0279м | applied=True canApply=True
  WeaponRoot world: pre pos=(15,6577, 1,4180, 26,6724) rot=(0,91, 328,15, 2,19)
    → post pos=(15,6490, 1,4453, 26,6692) rot=(0,83, 328,56, 358,80) | Δpos=(-0,0088, 0,0273, -0,0032) |Δpos|=0,0288м Δrot=3,416°
  Muzzle world: pre pos=(15,3033, 1,5079, 27,2357) rot=(0,91, 328,15, 2,19)
    → post pos=(15,3037, 1,5361, 27,2380) rot=(0,83, 328,56, 358,80) | Δpos=(0,0004, 0,0282, 0,0023) |Δpos|=0,0283м Δrot=3,416°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1363) | Δpos=(0,0000, 0,0000, -0,0097)
  cameraDist=3,3м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Standing/Walk/Aiming(Aiming) x3 | t=51,865 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 0,96→1,69/30,0 | kickScale=1,20
  visualState: climbPitch=0,020° punchPitch=5,693° punchYaw=0,831° | back=0,0455м up=0,0169м active=True | tau=0,140с
  Hand_R local: base rot=(344,50, 296,60, 347,94) pos=(0,2906, 0,0025, -0,0073)
    → final rot=(341,43, 297,77, 347,63) pos=(0,3160, 0,0058, -0,0174) | Δrot=3,264° Δpos=(0,0254, 0,0033, -0,0101) |Δpos|=0,0275м | applied=True canApply=True
  WeaponRoot world: pre pos=(15,5973, 1,4278, 26,8130) rot=(359,77, 327,38, 0,10)
    → post pos=(15,5895, 1,4551, 26,8084) rot=(359,63, 327,71, 356,75) | Δpos=(-0,0078, 0,0273, -0,0047) |Δpos|=0,0288м Δrot=3,363°
  Muzzle world: pre pos=(15,2394, 1,5310, 27,3718) rot=(359,77, 327,38, 0,10)
    → post pos=(15,2400, 1,5597, 27,3722) rot=(359,63, 327,71, 356,75) | Δpos=(0,0005, 0,0287, 0,0003) |Δpos|=0,0287м Δrot=3,363°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1370) | Δpos=(0,0000, 0,0000, -0,0090)
  cameraDist=3,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Standing/Walk/Aiming(Aiming) x10 | t=53,391 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 0,00→0,73/30,0 | kickScale=1,20
  visualState: climbPitch=0,004° punchPitch=3,374° punchYaw=0,083° | back=0,0270м up=0,0100м active=True | tau=0,140с
  Hand_R local: base rot=(346,87, 295,80, 348,13) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(343,58, 296,60, 347,93) pos=(0,2977, 0,0034, -0,0102) | Δrot=3,379° Δpos=(0,0266, 0,0034, -0,0106) |Δpos|=0,0288м | applied=True canApply=True
  WeaponRoot world: pre pos=(14,7212, 1,4070, 28,9275) rot=(359,70, 324,02, 0,51)
    → post pos=(14,7121, 1,4346, 28,9248) rot=(0,05, 324,26, 357,09) | Δpos=(-0,0091, 0,0276, -0,0027) |Δpos|=0,0292м Δrot=3,440°
  Muzzle world: pre pos=(14,3306, 1,5110, 29,4639) rot=(359,70, 324,02, 0,51)
    → post pos=(14,3283, 1,5344, 29,4668) rot=(0,05, 324,26, 357,09) | Δpos=(-0,0024, 0,0234, 0,0029) |Δpos|=0,0237м Δrot=3,440°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1360) | Δpos=(0,0000, 0,0000, -0,0100)
  cameraDist=3,0м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Standing/Walk/Aiming(Aiming) x10 | t=53,493 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 0,50→1,22/30,0 | kickScale=1,20
  visualState: climbPitch=0,010° punchPitch=5,000° punchYaw=-0,003° | back=0,0400м up=0,0149м active=True | tau=0,140с
  Hand_R local: base rot=(345,14, 296,22, 348,03) pos=(0,2851, 0,0018, -0,0052)
    → final rot=(341,97, 296,88, 347,84) pos=(0,3105, 0,0051, -0,0153) | Δrot=3,234° Δpos=(0,0254, 0,0033, -0,0101) |Δpos|=0,0275м | applied=True canApply=True
  WeaponRoot world: pre pos=(14,6543, 1,4203, 29,0768) rot=(359,82, 324,03, 358,58)
    → post pos=(14,6455, 1,4473, 29,0727) rot=(0,21, 324,36, 355,52) | Δpos=(-0,0088, 0,0270, -0,0040) |Δpos|=0,0287м Δrot=3,104°
  Muzzle world: pre pos=(14,2665, 1,5228, 29,6154) rot=(359,82, 324,03, 358,58)
    → post pos=(14,2647, 1,5450, 29,6172) rot=(0,21, 324,36, 355,52) | Δpos=(-0,0018, 0,0222, 0,0018) |Δpos|=0,0224м Δrot=3,104°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1301) | Δpos=(0,0000, 0,0000, -0,0159)
  cameraDist=3,0м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Standing/Walk/Aiming(Aiming) x10 | t=53,596 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 0,99→1,71/30,0 | kickScale=1,20
  visualState: climbPitch=0,020° punchPitch=5,773° punchYaw=0,335° | back=0,0462м up=0,0172м active=True | tau=0,140с
  Hand_R local: base rot=(344,31, 296,36, 347,99) pos=(0,2918, 0,0027, -0,0078)
    → final rot=(341,27, 297,35, 347,72) pos=(0,3166, 0,0059, -0,0177) | Δrot=3,181° Δpos=(0,0248, 0,0032, -0,0099) |Δpos|=0,0269м | applied=True canApply=True
  WeaponRoot world: pre pos=(14,5924, 1,4280, 29,2103) rot=(359,74, 324,82, 358,94)
    → post pos=(14,5845, 1,4546, 29,2047) rot=(359,81, 325,23, 355,95) | Δpos=(-0,0079, 0,0266, -0,0056) |Δpos|=0,0283м Δrot=3,010°
  Muzzle world: pre pos=(14,2116, 1,5315, 29,7536) rot=(359,74, 324,82, 358,94)
    → post pos=(14,2119, 1,5570, 29,7539) rot=(359,81, 325,23, 355,95) | Δpos=(0,0003, 0,0255, 0,0002) |Δpos|=0,0255м Δrot=3,010°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1300) | Δpos=(0,0000, 0,0000, -0,0160)
  cameraDist=3,0м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #4 | unit=Unit(Clone) | cell=Standing/Walk/Aiming(Aiming) x10 | t=53,705 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 1,47→2,18/30,0 | kickScale=1,20
  visualState: climbPitch=0,032° punchPitch=6,020° punchYaw=0,600° | back=0,0482м up=0,0179м active=True | tau=0,140с
  Hand_R local: base rot=(344,01, 296,58, 347,94) pos=(0,2943, 0,0030, -0,0088)
    → final rot=(341,06, 297,64, 347,65) pos=(0,3186, 0,0061, -0,0184) | Δrot=3,119° Δpos=(0,0243, 0,0031, -0,0096) |Δpos|=0,0263м | applied=True canApply=True
  WeaponRoot world: pre pos=(14,5334, 1,4317, 29,3519) rot=(359,62, 326,02, 359,74)
    → post pos=(14,5262, 1,4574, 29,3477) rot=(359,68, 326,57, 356,71) | Δpos=(-0,0072, 0,0257, -0,0042) |Δpos|=0,0270м Δrot=3,067°
  Muzzle world: pre pos=(14,1630, 1,5366, 29,9021) rot=(359,62, 326,02, 359,74)
    → post pos=(14,1655, 1,5615, 29,9045) rot=(359,68, 326,57, 356,71) | Δpos=(0,0025, 0,0249, 0,0023) |Δpos|=0,0251м Δrot=3,067°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1241) | Δpos=(0,0000, 0,0000, -0,0219)
  cameraDist=3,1м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #5 | unit=Unit(Clone) | cell=Standing/Walk/Aiming(Aiming) x10 | t=53,807 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 1,95→2,67/30,0 | kickScale=1,20
  visualState: climbPitch=0,049° punchPitch=6,285° punchYaw=0,752° | back=0,0503м up=0,0187м active=True | tau=0,140с
  Hand_R local: base rot=(343,83, 296,75, 347,90) pos=(0,2959, 0,0032, -0,0095)
    → final rot=(340,82, 297,84, 347,60) pos=(0,3206, 0,0064, -0,0193) | Δrot=3,182° Δpos=(0,0247, 0,0032, -0,0098) |Δpos|=0,0268м | applied=True canApply=True
  WeaponRoot world: pre pos=(14,4724, 1,4336, 29,4884) rot=(359,97, 326,27, 0,07)
    → post pos=(14,4633, 1,4595, 29,4847) rot=(0,06, 326,65, 356,96) | Δpos=(-0,0091, 0,0259, -0,0037) |Δpos|=0,0276м Δrot=3,129°
  Muzzle world: pre pos=(14,1036, 1,5344, 30,0405) rot=(359,97, 326,27, 0,07)
    → post pos=(14,1026, 1,5591, 30,0423) rot=(0,06, 326,65, 356,96) | Δpos=(-0,0009, 0,0247, 0,0018) |Δpos|=0,0248м Δrot=3,129°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1330) | Δpos=(0,0000, 0,0000, -0,0130)
  cameraDist=3,1м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #6 | unit=Unit(Clone) | cell=Standing/Walk/Aiming(Aiming) x10 | t=53,915 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 2,43→3,15/30,0 | kickScale=1,20
  visualState: climbPitch=0,067° punchPitch=6,282° punchYaw=0,314° | back=0,0503м up=0,0187м active=True | tau=0,140с
  Hand_R local: base rot=(343,84, 296,81, 347,88) pos=(0,2958, 0,0032, -0,0094)
    → final rot=(340,72, 297,46, 347,68) pos=(0,3206, 0,0064, -0,0193) | Δrot=3,177° Δpos=(0,0248, 0,0032, -0,0099) |Δpos|=0,0269м | applied=True canApply=True
  WeaponRoot world: pre pos=(14,4005, 1,4308, 29,6400) rot=(0,80, 326,25, 0,88)
    → post pos=(14,3910, 1,4558, 29,6371) rot=(1,29, 326,69, 357,81) | Δpos=(-0,0096, 0,0250, -0,0030) |Δpos|=0,0269м Δrot=3,142°
  Muzzle world: pre pos=(14,0296, 1,5220, 30,1924) rot=(0,80, 326,25, 0,88)
    → post pos=(14,0284, 1,5413, 30,1959) rot=(1,29, 326,69, 357,81) | Δpos=(-0,0012, 0,0193, 0,0035) |Δpos|=0,0197м Δrot=3,142°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1341) | Δpos=(0,0000, 0,0000, -0,0119)
  cameraDist=3,2м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #7 | unit=Unit(Clone) | cell=Standing/Walk/Aiming(Aiming) x10 | t=54,021 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 2,91→3,63/30,0 | kickScale=1,20
  visualState: climbPitch=0,089° punchPitch=6,320° punchYaw=0,542° | back=0,0506м up=0,0188м active=True | tau=0,140с
  Hand_R local: base rot=(343,70, 296,64, 347,92) pos=(0,2965, 0,0033, -0,0097)
    → final rot=(340,71, 297,67, 347,63) pos=(0,3209, 0,0064, -0,0194) | Δrot=3,154° Δpos=(0,0245, 0,0032, -0,0097) |Δpos|=0,0265м | applied=True canApply=True
  WeaponRoot world: pre pos=(14,3315, 1,4296, 29,7939) rot=(1,25, 326,51, 1,14)
    → post pos=(14,3229, 1,4554, 29,7912) rot=(1,23, 326,97, 357,93) | Δpos=(-0,0086, 0,0258, -0,0027) |Δpos|=0,0273м Δrot=3,253°
  Muzzle world: pre pos=(13,9624, 1,5155, 30,3483) rot=(1,25, 326,51, 1,14)
    → post pos=(13,9629, 1,5415, 30,3515) rot=(1,23, 326,97, 357,93) | Δpos=(0,0005, 0,0260, 0,0032) |Δpos|=0,0262м Δrot=3,253°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1302) | Δpos=(0,0000, 0,0000, -0,0158)
  cameraDist=3,2м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #8 | unit=Unit(Clone) | cell=Standing/Walk/Aiming(Aiming) x10 | t=54,127 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 3,39→4,11/30,0 | kickScale=1,20
  visualState: climbPitch=0,113° punchPitch=6,328° punchYaw=0,560° | back=0,0506м up=0,0188м active=True | tau=0,140с
  Hand_R local: base rot=(343,73, 296,74, 347,90) pos=(0,2962, 0,0032, -0,0096)
    → final rot=(340,68, 297,69, 347,63) pos=(0,3210, 0,0064, -0,0194) | Δrot=3,188° Δpos=(0,0248, 0,0032, -0,0098) |Δpos|=0,0268м | applied=True canApply=True
  WeaponRoot world: pre pos=(14,2740, 1,4329, 29,9393) rot=(0,16, 326,01, 359,90)
    → post pos=(14,2661, 1,4591, 29,9345) rot=(0,19, 326,32, 356,61) | Δpos=(-0,0078, 0,0262, -0,0048) |Δpos|=0,0278м Δrot=3,305°
  Muzzle world: pre pos=(13,9027, 1,5316, 30,4901) rot=(0,16, 326,01, 359,90)
    → post pos=(13,9027, 1,5573, 30,4907) rot=(0,19, 326,32, 356,61) | Δpos=(0,0000, 0,0257, 0,0005) |Δpos|=0,0257м Δrot=3,305°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1338) | Δpos=(0,0000, 0,0000, -0,0122)
  cameraDist=3,3м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #9 | unit=Unit(Clone) | cell=Standing/Walk/Aiming(Aiming) x10 | t=54,231 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 3,87→4,60/30,0 | kickScale=1,20
  visualState: climbPitch=0,141° punchPitch=6,398° punchYaw=0,426° | back=0,0512м up=0,0190м active=True | tau=0,140с
  Hand_R local: base rot=(343,68, 296,76, 347,89) pos=(0,2965, 0,0033, -0,0097)
    → final rot=(340,56, 297,60, 347,64) pos=(0,3215, 0,0065, -0,0196) | Δrot=3,224° Δpos=(0,0251, 0,0032, -0,0100) |Δpos|=0,0272м | applied=True canApply=True
  WeaponRoot world: pre pos=(14,2219, 1,4377, 30,0712) rot=(359,09, 325,22, 358,59)
    → post pos=(14,2146, 1,4645, 30,0664) rot=(359,29, 325,53, 355,32) | Δpos=(-0,0073, 0,0268, -0,0048) |Δpos|=0,0282м Δrot=3,280°
  Muzzle world: pre pos=(13,8462, 1,5487, 30,6166) rot=(359,09, 325,22, 358,59)
    → post pos=(13,8463, 1,5729, 30,6174) rot=(359,29, 325,53, 355,32) | Δpos=(0,0001, 0,0242, 0,0007) |Δpos|=0,0242м Δrot=3,280°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1375) | Δpos=(0,0000, 0,0000, -0,0085)
  cameraDist=3,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #10 | unit=Unit(Clone) | cell=Standing/Walk/Aiming(Aiming) x10 | t=54,337 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,750 | penalty 4,36→5,08/30,0 | kickScale=1,20
  visualState: climbPitch=0,171° punchPitch=6,367° punchYaw=1,057° | back=0,0509м up=0,0189м active=True | tau=0,140с
  Hand_R local: base rot=(343,63, 296,71, 347,90) pos=(0,2966, 0,0033, -0,0097)
    → final rot=(340,67, 298,15, 347,52) pos=(0,3213, 0,0065, -0,0195) | Δrot=3,257° Δpos=(0,0247, 0,0032, -0,0098) |Δpos|=0,0268м | applied=True canApply=True
  WeaponRoot world: pre pos=(14,1530, 1,4382, 30,2100) rot=(359,11, 323,42, 358,56)
    → post pos=(14,1434, 1,4646, 30,2054) rot=(358,88, 323,54, 355,37) | Δpos=(-0,0096, 0,0263, -0,0046) |Δpos|=0,0284м Δrot=3,200°
  Muzzle world: pre pos=(13,7603, 1,5491, 30,7434) rot=(359,11, 323,42, 358,56)
    → post pos=(13,7566, 1,5778, 30,7426) rot=(358,88, 323,54, 355,37) | Δpos=(-0,0037, 0,0287, -0,0008) |Δpos|=0,0289м Δrot=3,200°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1333) | Δpos=(0,0000, 0,0000, -0,0127)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Crouch/Idle/HipFire(HipFire) x1 | t=58,263 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,00→0,54/30,0 | kickScale=1,20
  visualState: climbPitch=0,002° punchPitch=2,564° punchYaw=0,301° | back=0,0205м up=0,0076м active=True | tau=0,140с
  Hand_R local: base rot=(348,07, 6,88, 331,11) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(345,95, 8,39, 330,78) pos=(0,2722, 0,0023, -0,0214) | Δrot=2,579° Δpos=(0,0011, 0,0023, -0,0217) |Δpos|=0,0219м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,3308, 0,6609, 32,5398) rot=(359,93, 322,54, 0,89)
    → post pos=(13,3244, 0,6824, 32,5368) rot=(359,98, 322,82, 358,33) | Δpos=(-0,0064, 0,0215, -0,0030) |Δpos|=0,0227м Δrot=2,575°
  Muzzle world: pre pos=(12,9258, 0,7622, 33,0658) rot=(359,93, 322,54, 0,89)
    → post pos=(12,9255, 0,7831, 33,0676) rot=(359,98, 322,82, 358,33) | Δpos=(-0,0003, 0,0209, 0,0017) |Δpos|=0,0210м Δrot=2,575°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1329) | Δpos=(0,0000, 0,0000, -0,0131)
  cameraDist=3,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Crouch/Idle/HipFire(HipFire) x3 | t=59,536 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,00→0,54/30,0 | kickScale=1,20
  visualState: climbPitch=0,002° punchPitch=2,564° punchYaw=0,482° | back=0,0205м up=0,0076м active=True | tau=0,140с
  Hand_R local: base rot=(348,07, 6,88, 331,11) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(346,03, 8,53, 330,75) pos=(0,2722, 0,0023, -0,0214) | Δrot=2,598° Δpos=(0,0011, 0,0023, -0,0217) |Δpos|=0,0219м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,3306, 0,6607, 32,5396) rot=(0,08, 322,55, 0,98)
    → post pos=(13,3242, 0,6825, 32,5366) rot=(359,99, 322,83, 358,40) | Δpos=(-0,0065, 0,0217, -0,0030) |Δpos|=0,0229м Δrot=2,593°
  Muzzle world: pre pos=(12,9254, 0,7602, 33,0658) rot=(0,08, 322,55, 0,98)
    → post pos=(12,9253, 0,7831, 33,0674) rot=(359,99, 322,83, 358,40) | Δpos=(-0,0001, 0,0228, 0,0016) |Δpos|=0,0229м Δrot=2,593°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1290) | Δpos=(0,0000, 0,0000, -0,0170)
  cameraDist=3,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Crouch/Idle/HipFire(HipFire) x3 | t=59,645 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,29→0,83/30,0 | kickScale=1,20
  visualState: climbPitch=0,005° punchPitch=3,743° punchYaw=0,821° | back=0,0299м up=0,0111м active=True | tau=0,140с
  Hand_R local: base rot=(347,06, 7,69, 330,94) pos=(0,2717, 0,0011, -0,0104)
    → final rot=(345,14, 9,38, 330,55) pos=(0,2727, 0,0034, -0,0314) | Δrot=2,523° Δpos=(0,0010, 0,0022, -0,0209) |Δpos|=0,0211м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,3275, 0,6714, 32,5380) rot=(0,09, 322,69, 359,73)
    → post pos=(13,3212, 0,6925, 32,5351) rot=(359,90, 322,96, 357,23) | Δpos=(-0,0062, 0,0211, -0,0029) |Δpos|=0,0222м Δrot=2,517°
  Muzzle world: pre pos=(12,9253, 0,7709, 33,0665) rot=(0,09, 322,69, 359,73)
    → post pos=(12,9252, 0,7940, 33,0679) rot=(359,90, 322,96, 357,23) | Δpos=(-0,0001, 0,0231, 0,0014) |Δpos|=0,0232м Δrot=2,517°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1334) | Δpos=(0,0000, 0,0000, -0,0126)
  cameraDist=3,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Crouch/Idle/HipFire(HipFire) x3 | t=59,751 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,60→1,13/30,0 | kickScale=1,20
  visualState: climbPitch=0,009° punchPitch=4,314° punchYaw=0,451° | back=0,0345м up=0,0128м active=True | tau=0,140с
  Hand_R local: base rot=(346,57, 8,15, 330,84) pos=(0,2720, 0,0017, -0,0158)
    → final rot=(344,48, 9,39, 330,53) pos=(0,2730, 0,0039, -0,0362) | Δrot=2,407° Δpos=(0,0010, 0,0021, -0,0203) |Δpos|=0,0205м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,3259, 0,6769, 32,5372) rot=(0,03, 322,76, 359,08)
    → post pos=(13,3198, 0,6967, 32,5344) rot=(0,30, 323,01, 356,70) | Δpos=(-0,0061, 0,0198, -0,0028) |Δpos|=0,0209м Δrot=2,404°
  Muzzle world: pre pos=(12,9253, 0,7770, 33,0668) rot=(0,03, 322,76, 359,08)
    → post pos=(12,9246, 0,7935, 33,0687) rot=(0,30, 323,01, 356,70) | Δpos=(-0,0007, 0,0166, 0,0018) |Δpos|=0,0167м Δrot=2,404°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1293) | Δpos=(0,0000, 0,0000, -0,0167)
  cameraDist=3,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Crouch/Idle/HipFire(HipFire) x10 | t=61,053 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,00→0,55/30,0 | kickScale=1,20
  visualState: climbPitch=0,002° punchPitch=2,564° punchYaw=0,080° | back=0,0205м up=0,0076м active=True | tau=0,140с
  Hand_R local: base rot=(348,07, 6,88, 331,11) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(345,86, 8,22, 330,81) pos=(0,2722, 0,0023, -0,0214) | Δrot=2,567° Δpos=(0,0011, 0,0023, -0,0217) |Δpos|=0,0219м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,3307, 0,6606, 32,5394) rot=(0,16, 322,55, 1,01)
    → post pos=(13,3242, 0,6818, 32,5364) rot=(0,40, 322,82, 358,47) | Δpos=(-0,0065, 0,0212, -0,0030) |Δpos|=0,0224м Δrot=2,563°
  Muzzle world: pre pos=(12,9254, 0,7592, 33,0657) rot=(0,16, 322,55, 1,01)
    → post pos=(12,9247, 0,7777, 33,0676) rot=(0,40, 322,82, 358,47) | Δpos=(-0,0007, 0,0185, 0,0020) |Δpos|=0,0186м Δrot=2,563°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1365) | Δpos=(0,0000, 0,0000, -0,0095)
  cameraDist=3,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Crouch/Idle/HipFire(HipFire) x10 | t=61,158 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,31→0,85/30,0 | kickScale=1,20
  visualState: climbPitch=0,005° punchPitch=3,778° punchYaw=0,384° | back=0,0302м up=0,0112м active=True | tau=0,140с
  Hand_R local: base rot=(346,95, 7,56, 330,97) pos=(0,2717, 0,0012, -0,0107)
    → final rot=(344,93, 9,07, 330,61) pos=(0,2727, 0,0034, -0,0316) | Δrot=2,497° Δpos=(0,0011, 0,0022, -0,0210) |Δpos|=0,0211м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,3274, 0,6714, 32,5379) rot=(0,28, 322,69, 359,72)
    → post pos=(13,3212, 0,6922, 32,5350) rot=(0,29, 322,95, 357,24) | Δpos=(-0,0063, 0,0208, -0,0029) |Δpos|=0,0219м Δrot=2,494°
  Muzzle world: pre pos=(12,9251, 0,7686, 33,0667) rot=(0,28, 322,69, 359,72)
    → post pos=(12,9247, 0,7892, 33,0683) rot=(0,29, 322,95, 357,24) | Δpos=(-0,0004, 0,0206, 0,0016) |Δpos|=0,0207м Δrot=2,494°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1353) | Δpos=(0,0000, 0,0000, -0,0107)
  cameraDist=3,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Crouch/Idle/HipFire(HipFire) x10 | t=61,267 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,64→1,14/30,0 | kickScale=1,20
  visualState: climbPitch=0,009° punchPitch=4,290° punchYaw=0,644° | back=0,0343м up=0,0128м active=True | tau=0,140с
  Hand_R local: base rot=(346,34, 8,07, 330,85) pos=(0,2720, 0,0019, -0,0172)
    → final rot=(344,58, 9,53, 330,51) pos=(0,2730, 0,0038, -0,0360) | Δrot=2,253° Δpos=(0,0009, 0,0020, -0,0188) |Δpos|=0,0189м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,3255, 0,6779, 32,5370) rot=(0,23, 322,77, 358,95)
    → post pos=(13,3199, 0,6967, 32,5344) rot=(0,13, 323,01, 356,71) | Δpos=(-0,0056, 0,0188, -0,0026) |Δpos|=0,0198м Δrot=2,250°
  Muzzle world: pre pos=(12,9250, 0,7758, 33,0672) rot=(0,23, 322,77, 358,95)
    → post pos=(12,9248, 0,7955, 33,0685) rot=(0,13, 323,01, 356,71) | Δpos=(-0,0002, 0,0198, 0,0013) |Δpos|=0,0198м Δrot=2,250°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,0944) | Δpos=(0,0000, 0,0000, -0,0516)
  cameraDist=3,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #4 | unit=Unit(Clone) | cell=Crouch/Idle/HipFire(HipFire) x10 | t=61,368 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,92→1,46/30,0 | kickScale=1,20
  visualState: climbPitch=0,015° punchPitch=4,660° punchYaw=0,852° | back=0,0373м up=0,0139м active=True | tau=0,140с
  Hand_R local: base rot=(346,22, 8,28, 330,81) pos=(0,2721, 0,0020, -0,0189)
    → final rot=(344,35, 9,88, 330,43) pos=(0,2731, 0,0042, -0,0391) | Δrot=2,429° Δpos=(0,0010, 0,0021, -0,0202) |Δpos|=0,0203м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,3250, 0,6798, 32,5368) rot=(0,14, 322,80, 358,73)
    → post pos=(13,3189, 0,7001, 32,5340) rot=(0,01, 323,05, 356,31) | Δpos=(-0,0060, 0,0203, -0,0028) |Δpos|=0,0213м Δrot=2,426°
  Muzzle world: pre pos=(12,9251, 0,7786, 33,0672) rot=(0,14, 322,80, 358,73)
    → post pos=(12,9250, 0,8003, 33,0686) rot=(0,01, 323,05, 356,31) | Δpos=(-0,0001, 0,0217, 0,0014) |Δpos|=0,0217м Δrot=2,426°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1314) | Δpos=(0,0000, 0,0000, -0,0146)
  cameraDist=3,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #5 | unit=Unit(Clone) | cell=Crouch/Idle/HipFire(HipFire) x10 | t=61,477 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 1,22→1,76/30,0 | kickScale=1,20
  visualState: climbPitch=0,021° punchPitch=4,703° punchYaw=0,595° | back=0,0376м up=0,0140м active=True | tau=0,140с
  Hand_R local: base rot=(346,20, 8,37, 330,79) pos=(0,2721, 0,0021, -0,0194)
    → final rot=(344,20, 9,71, 330,45) pos=(0,2731, 0,0042, -0,0395) | Δrot=2,384° Δpos=(0,0010, 0,0021, -0,0201) |Δpos|=0,0202м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,3248, 0,6803, 32,5367) rot=(0,08, 322,81, 358,66)
    → post pos=(13,3188, 0,7001, 32,5340) rot=(0,23, 323,05, 356,29) | Δpos=(-0,0060, 0,0197, -0,0028) |Δpos|=0,0208м Δrot=2,381°
  Muzzle world: pre pos=(12,9252, 0,7799, 33,0672) rot=(0,08, 322,81, 358,66)
    → post pos=(12,9247, 0,7977, 33,0688) rot=(0,23, 323,05, 356,29) | Δpos=(-0,0005, 0,0178, 0,0017) |Δpos|=0,0179м Δrot=2,381°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1301) | Δpos=(0,0000, 0,0000, -0,0159)
  cameraDist=3,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #6 | unit=Unit(Clone) | cell=Crouch/Idle/HipFire(HipFire) x10 | t=61,580 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 1,52→2,06/30,0 | kickScale=1,20
  visualState: climbPitch=0,029° punchPitch=4,816° punchYaw=0,536° | back=0,0385м up=0,0143м active=True | tau=0,140с
  Hand_R local: base rot=(346,06, 8,33, 330,79) pos=(0,2722, 0,0022, -0,0202)
    → final rot=(344,07, 9,73, 330,44) pos=(0,2732, 0,0043, -0,0404) | Δrot=2,404° Δpos=(0,0010, 0,0021, -0,0202) |Δpos|=0,0203м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,3246, 0,6810, 32,5366) rot=(0,19, 322,81, 358,57)
    → post pos=(13,3185, 0,7009, 32,5338) rot=(0,29, 323,06, 356,18) | Δpos=(-0,0060, 0,0199, -0,0028) |Δpos|=0,0210м Δrot=2,401°
  Muzzle world: pre pos=(12,9251, 0,7793, 33,0674) rot=(0,19, 322,81, 358,57)
    → post pos=(12,9246, 0,7978, 33,0690) rot=(0,29, 323,06, 356,18) | Δpos=(-0,0005, 0,0185, 0,0016) |Δpos|=0,0186м Δrot=2,401°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1333) | Δpos=(0,0000, 0,0000, -0,0127)
  cameraDist=3,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #7 | unit=Unit(Clone) | cell=Crouch/Idle/HipFire(HipFire) x10 | t=61,680 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 1,84→2,38/30,0 | kickScale=1,20
  visualState: climbPitch=0,039° punchPitch=4,912° punchYaw=0,746° | back=0,0393м up=0,0146м active=True | tau=0,140с
  Hand_R local: base rot=(345,93, 8,38, 330,78) pos=(0,2722, 0,0023, -0,0213)
    → final rot=(344,06, 9,94, 330,40) pos=(0,2732, 0,0044, -0,0413) | Δrot=2,399° Δpos=(0,0010, 0,0021, -0,0199) |Δpos|=0,0201м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,3242, 0,6820, 32,5364) rot=(0,23, 322,83, 358,44)
    → post pos=(13,3183, 0,7020, 32,5337) rot=(0,13, 323,08, 356,05) | Δpos=(-0,0059, 0,0199, -0,0027) |Δpos|=0,0210м Δrot=2,395°
  Muzzle world: pre pos=(12,9250, 0,7799, 33,0675) rot=(0,23, 322,83, 358,44)
    → post pos=(12,9248, 0,8008, 33,0689) rot=(0,13, 323,08, 356,05) | Δpos=(-0,0002, 0,0209, 0,0014) |Δpos|=0,0209м Δrot=2,395°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1302) | Δpos=(0,0000, 0,0000, -0,0158)
  cameraDist=3,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #8 | unit=Unit(Clone) | cell=Crouch/Idle/HipFire(HipFire) x10 | t=61,788 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 2,14→2,68/30,0 | kickScale=1,20
  visualState: climbPitch=0,049° punchPitch=4,846° punchYaw=0,635° | back=0,0388м up=0,0144м active=True | tau=0,140с
  Hand_R local: base rot=(346,05, 8,41, 330,78) pos=(0,2722, 0,0022, -0,0205)
    → final rot=(344,06, 9,83, 330,42) pos=(0,2732, 0,0043, -0,0407) | Δrot=2,411° Δpos=(0,0010, 0,0021, -0,0202) |Δpos|=0,0203м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,3245, 0,6813, 32,5366) rot=(0,14, 322,82, 358,52)
    → post pos=(13,3185, 0,7013, 32,5338) rot=(0,22, 323,07, 356,12) | Δpos=(-0,0060, 0,0200, -0,0028) |Δpos|=0,0210м Δrot=2,408°
  Muzzle world: pre pos=(12,9252, 0,7802, 33,0674) rot=(0,14, 322,82, 358,52)
    → post pos=(12,9247, 0,7991, 33,0690) rot=(0,22, 323,07, 356,12) | Δpos=(-0,0004, 0,0189, 0,0016) |Δpos|=0,0190м Δrot=2,408°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1335) | Δpos=(0,0000, 0,0000, -0,0125)
  cameraDist=3,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #9 | unit=Unit(Clone) | cell=Crouch/Idle/HipFire(HipFire) x10 | t=61,893 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 2,44→2,98/30,0 | kickScale=1,20
  visualState: climbPitch=0,060° punchPitch=4,843° punchYaw=0,490° | back=0,0387м up=0,0144м active=True | tau=0,140с
  Hand_R local: base rot=(346,02, 8,37, 330,78) pos=(0,2722, 0,0022, -0,0204)
    → final rot=(343,99, 9,72, 330,44) pos=(0,2732, 0,0043, -0,0407) | Δrot=2,411° Δpos=(0,0010, 0,0021, -0,0202) |Δpos|=0,0204м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,3245, 0,6812, 32,5366) rot=(0,19, 322,82, 358,52)
    → post pos=(13,3185, 0,7011, 32,5338) rot=(0,34, 323,07, 356,13) | Δpos=(-0,0060, 0,0199, -0,0028) |Δpos|=0,0210м Δrot=2,407°
  Muzzle world: pre pos=(12,9251, 0,7795, 33,0674) rot=(0,19, 322,82, 358,52)
    → post pos=(12,9246, 0,7974, 33,0691) rot=(0,34, 323,07, 356,13) | Δpos=(-0,0005, 0,0179, 0,0017) |Δpos|=0,0180м Δrot=2,407°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1340) | Δpos=(0,0000, 0,0000, -0,0120)
  cameraDist=3,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #10 | unit=Unit(Clone) | cell=Crouch/Idle/HipFire(HipFire) x10 | t=62,001 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 2,73→3,28/30,0 | kickScale=1,20
  visualState: climbPitch=0,073° punchPitch=4,810° punchYaw=0,712° | back=0,0385м up=0,0143м active=True | tau=0,140с
  Hand_R local: base rot=(346,03, 8,29, 330,80) pos=(0,2722, 0,0021, -0,0200)
    → final rot=(344,11, 9,88, 330,42) pos=(0,2732, 0,0043, -0,0404) | Δrot=2,464° Δpos=(0,0010, 0,0022, -0,0204) |Δpos|=0,0206м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,3247, 0,6807, 32,5366) rot=(0,25, 322,81, 358,57)
    → post pos=(13,3186, 0,7011, 32,5338) rot=(0,15, 323,07, 356,12) | Δpos=(-0,0061, 0,0204, -0,0028) |Δpos|=0,0215м Δrot=2,461°
  Muzzle world: pre pos=(12,9251, 0,7783, 33,0675) rot=(0,25, 322,81, 358,57)
    → post pos=(12,9249, 0,7997, 33,0689) rot=(0,15, 323,07, 356,12) | Δpos=(-0,0002, 0,0214, 0,0014) |Δpos|=0,0214м Δrot=2,461°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1362) | Δpos=(0,0000, 0,0000, -0,0098)
  cameraDist=3,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Crouch/Idle/PointAim(PointAim) x1 | t=64,532 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,00→0,54/30,0 | kickScale=1,20
  visualState: climbPitch=0,002° punchPitch=2,564° punchYaw=0,732° | back=0,0205м up=0,0076м active=True | tau=0,140с
  Hand_R local: base rot=(6,99, 287,22, 354,70) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(4,49, 288,08, 354,80) pos=(0,2899, 0,0100, -0,0047) | Δrot=2,641° Δpos=(0,0188, 0,0100, -0,0051) |Δpos|=0,0219м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0980, 0,7974, 32,6781) rot=(359,28, 322,19, 43,28)
    → post pos=(13,0814, 0,8091, 32,6669) rot=(358,85, 322,19, 40,67) | Δpos=(-0,0167, 0,0117, -0,0112) |Δpos|=0,0232м Δrot=2,638°
  Muzzle world: pre pos=(12,6371, 0,8789, 33,1596) rot=(359,28, 322,19, 43,28)
    → post pos=(12,6235, 0,8986, 33,1500) rot=(358,85, 322,19, 40,67) | Δpos=(-0,0135, 0,0197, -0,0096) |Δpos|=0,0257м Δrot=2,638°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1340) | Δpos=(0,0000, 0,0000, -0,0119)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Crouch/Idle/PointAim(PointAim) x3 | t=65,982 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,00→0,54/30,0 | kickScale=1,20
  visualState: climbPitch=0,002° punchPitch=2,564° punchYaw=0,259° | back=0,0205м up=0,0076м active=True | tau=0,140с
  Hand_R local: base rot=(6,99, 287,22, 354,70) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(4,46, 287,67, 354,75) pos=(0,2899, 0,0100, -0,0047) | Δrot=2,575° Δpos=(0,0188, 0,0100, -0,0051) |Δpos|=0,0219м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0979, 0,7987, 32,6782) rot=(359,10, 322,19, 43,28)
    → post pos=(13,0815, 0,8099, 32,6673) rot=(358,98, 322,45, 40,72) | Δpos=(-0,0163, 0,0112, -0,0110) |Δpos|=0,0226м Δrot=2,572°
  Muzzle world: pre pos=(12,6371, 0,8823, 33,1596) rot=(359,10, 322,19, 43,28)
    → post pos=(12,6257, 0,8979, 33,1526) rot=(358,98, 322,45, 40,72) | Δpos=(-0,0113, 0,0156, -0,0070) |Δpos|=0,0205м Δrot=2,572°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1316) | Δpos=(0,0000, 0,0000, -0,0144)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Crouch/Idle/PointAim(PointAim) x3 | t=66,095 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,29→0,83/30,0 | kickScale=1,20
  visualState: climbPitch=0,005° punchPitch=3,710° punchYaw=0,433° | back=0,0297м up=0,0110м active=True | tau=0,140с
  Hand_R local: base rot=(5,74, 287,44, 354,72) pos=(0,2804, 0,0050, -0,0021)
    → final rot=(3,33, 287,93, 354,77) pos=(0,2983, 0,0145, -0,0070) | Δrot=2,461° Δpos=(0,0179, 0,0096, -0,0048) |Δpos|=0,0209м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0898, 0,8041, 32,6728) rot=(359,06, 322,32, 42,02)
    → post pos=(13,0742, 0,8149, 32,6624) rot=(358,91, 322,54, 39,57) | Δpos=(-0,0156, 0,0107, -0,0105) |Δpos|=0,0216м Δrot=2,458°
  Muzzle world: pre pos=(12,6315, 0,8897, 33,1561) rot=(359,06, 322,32, 42,02)
    → post pos=(12,6205, 0,9049, 33,1492) rot=(358,91, 322,54, 39,57) | Δpos=(-0,0110, 0,0152, -0,0069) |Δpos|=0,0200м Δrot=2,458°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1258) | Δpos=(0,0000, 0,0000, -0,0202)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Crouch/Idle/PointAim(PointAim) x3 | t=66,195 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,60→1,14/30,0 | kickScale=1,20
  visualState: climbPitch=0,009° punchPitch=4,378° punchYaw=0,778° | back=0,0350м up=0,0130м active=True | tau=0,140с
  Hand_R local: base rot=(5,03, 287,60, 354,74) pos=(0,2857, 0,0078, -0,0036)
    → final rot=(2,68, 288,28, 354,81) pos=(0,3032, 0,0171, -0,0083) | Δrot=2,441° Δpos=(0,0175, 0,0094, -0,0047) |Δpos|=0,0204м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0852, 0,8072, 32,6697) rot=(359,03, 322,38, 41,29)
    → post pos=(13,0698, 0,8179, 32,6594) rot=(358,73, 322,45, 38,87) | Δpos=(-0,0154, 0,0107, -0,0104) |Δpos|=0,0214м Δrot=2,438°
  Muzzle world: pre pos=(12,6282, 0,8940, 33,1540) rot=(359,03, 322,38, 41,29)
    → post pos=(12,6163, 0,9109, 33,1459) rot=(358,73, 322,45, 38,87) | Δpos=(-0,0118, 0,0169, -0,0082) |Δpos|=0,0222м Δrot=2,438°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1287) | Δpos=(0,0000, 0,0000, -0,0173)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Crouch/Idle/PointAim(PointAim) x10 | t=67,675 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,00→0,54/30,0 | kickScale=1,20
  visualState: climbPitch=0,002° punchPitch=2,564° punchYaw=0,160° | back=0,0205м up=0,0076м active=True | tau=0,140с
  Hand_R local: base rot=(6,99, 287,22, 354,70) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(4,45, 287,59, 354,74) pos=(0,2899, 0,0100, -0,0047) | Δrot=2,569° Δpos=(0,0188, 0,0100, -0,0051) |Δpos|=0,0219м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0982, 0,7968, 32,6780) rot=(359,38, 322,20, 43,28)
    → post pos=(13,0819, 0,8079, 32,6671) rot=(359,32, 322,52, 40,73) | Δpos=(-0,0163, 0,0111, -0,0109) |Δpos|=0,0225м Δrot=2,566°
  Muzzle world: pre pos=(12,6372, 0,8771, 33,1597) rot=(359,38, 322,20, 43,28)
    → post pos=(12,6263, 0,8919, 33,1534) rot=(359,32, 322,52, 40,73) | Δpos=(-0,0109, 0,0148, -0,0064) |Δpos|=0,0195м Δrot=2,566°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1314) | Δpos=(0,0000, 0,0000, -0,0146)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Crouch/Idle/PointAim(PointAim) x10 | t=67,783 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,29→0,84/30,0 | kickScale=1,20
  visualState: climbPitch=0,005° punchPitch=3,748° punchYaw=0,502° | back=0,0300м up=0,0111м active=True | tau=0,140с
  Hand_R local: base rot=(5,73, 287,40, 354,72) pos=(0,2804, 0,0050, -0,0021)
    → final rot=(3,29, 287,99, 354,78) pos=(0,2986, 0,0147, -0,0070) | Δrot=2,509° Δpos=(0,0181, 0,0097, -0,0049) |Δpos|=0,0212м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0901, 0,8023, 32,6726) rot=(359,34, 322,35, 42,02)
    → post pos=(13,0742, 0,8133, 32,6620) rot=(359,11, 322,51, 39,52) | Δpos=(-0,0159, 0,0110, -0,0106) |Δpos|=0,0221м Δrot=2,505°
  Muzzle world: pre pos=(12,6318, 0,8847, 33,1565) rot=(359,34, 322,35, 42,02)
    → post pos=(12,6201, 0,9011, 33,1489) rot=(359,11, 322,51, 39,52) | Δpos=(-0,0117, 0,0164, -0,0076) |Δpos|=0,0216м Δrot=2,505°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1354) | Δpos=(0,0000, 0,0000, -0,0106)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Crouch/Idle/PointAim(PointAim) x10 | t=67,891 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,59→1,13/30,0 | kickScale=1,20
  visualState: climbPitch=0,009° punchPitch=4,301° punchYaw=0,785° | back=0,0344м up=0,0128м active=True | tau=0,140с
  Hand_R local: base rot=(5,16, 287,60, 354,74) pos=(0,2848, 0,0073, -0,0033)
    → final rot=(2,76, 288,28, 354,81) pos=(0,3026, 0,0168, -0,0081) | Δrot=2,490° Δpos=(0,0179, 0,0096, -0,0048) |Δpos|=0,0208м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0862, 0,8051, 32,6701) rot=(359,22, 322,35, 41,41)
    → post pos=(13,0705, 0,8161, 32,6595) rot=(358,93, 322,44, 38,94) | Δpos=(-0,0157, 0,0110, -0,0105) |Δpos|=0,0219м Δrot=2,488°
  Muzzle world: pre pos=(12,6287, 0,8895, 33,1543) rot=(359,22, 322,35, 41,41)
    → post pos=(12,6166, 0,9066, 33,1461) rot=(358,93, 322,44, 38,94) | Δpos=(-0,0121, 0,0171, -0,0082) |Δpos|=0,0225м Δrot=2,488°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1358) | Δpos=(0,0000, 0,0000, -0,0102)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #4 | unit=Unit(Clone) | cell=Crouch/Idle/PointAim(PointAim) x10 | t=67,994 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,90→1,44/30,0 | kickScale=1,20
  visualState: climbPitch=0,014° punchPitch=4,617° punchYaw=0,639° | back=0,0369м up=0,0137м active=True | tau=0,140с
  Hand_R local: base rot=(4,82, 287,76, 354,76) pos=(0,2873, 0,0086, -0,0040)
    → final rot=(2,43, 288,19, 354,80) pos=(0,3049, 0,0181, -0,0088) | Δrot=2,426° Δpos=(0,0176, 0,0094, -0,0048) |Δpos|=0,0206м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0839, 0,8069, 32,6685) rot=(359,11, 322,32, 41,05)
    → post pos=(13,0686, 0,8174, 32,6583) rot=(359,01, 322,57, 38,64) | Δpos=(-0,0153, 0,0105, -0,0102) |Δpos|=0,0212м Δrot=2,422°
  Muzzle world: pre pos=(12,6265, 0,8929, 33,1527) rot=(359,11, 322,32, 41,05)
    → post pos=(12,6161, 0,9073, 33,1463) rot=(359,01, 322,57, 38,64) | Δpos=(-0,0105, 0,0144, -0,0064) |Δpos|=0,0189м Δrot=2,422°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1345) | Δpos=(0,0000, 0,0000, -0,0115)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #5 | unit=Unit(Clone) | cell=Crouch/Idle/PointAim(PointAim) x10 | t=68,101 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 1,20→1,74/30,0 | kickScale=1,20
  visualState: climbPitch=0,021° punchPitch=4,719° punchYaw=0,659° | back=0,0378м up=0,0140м active=True | tau=0,140с
  Hand_R local: base rot=(4,68, 287,71, 354,75) pos=(0,2883, 0,0092, -0,0043)
    → final rot=(2,32, 288,21, 354,80) pos=(0,3057, 0,0185, -0,0090) | Δrot=2,406° Δpos=(0,0174, 0,0093, -0,0047) |Δpos|=0,0203м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0831, 0,8075, 32,6680) rot=(359,15, 322,38, 40,92)
    → post pos=(13,0679, 0,8180, 32,6578) rot=(358,98, 322,58, 38,53) | Δpos=(-0,0152, 0,0105, -0,0102) |Δpos|=0,0211м Δrot=2,403°
  Muzzle world: pre pos=(12,6264, 0,8933, 33,1528) rot=(359,15, 322,38, 40,92)
    → post pos=(12,6156, 0,9084, 33,1459) rot=(358,98, 322,58, 38,53) | Δpos=(-0,0108, 0,0151, -0,0069) |Δpos|=0,0198м Δrot=2,403°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1316) | Δpos=(0,0000, 0,0000, -0,0144)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #6 | unit=Unit(Clone) | cell=Crouch/Idle/PointAim(PointAim) x10 | t=68,208 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 1,50→2,04/30,0 | kickScale=1,20
  visualState: climbPitch=0,029° punchPitch=4,757° punchYaw=0,562° | back=0,0381м up=0,0142м active=True | tau=0,140с
  Hand_R local: base rot=(4,63, 287,72, 354,75) pos=(0,2886, 0,0093, -0,0043)
    → final rot=(2,27, 288,13, 354,79) pos=(0,3060, 0,0186, -0,0090) | Δrot=2,392° Δpos=(0,0174, 0,0093, -0,0047) |Δpos|=0,0202м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0828, 0,8079, 32,6678) rot=(359,12, 322,38, 40,87)
    → post pos=(13,0677, 0,8182, 32,6578) rot=(359,02, 322,64, 38,49) | Δpos=(-0,0151, 0,0104, -0,0101) |Δpos|=0,0209м Δrot=2,391°
  Muzzle world: pre pos=(12,6262, 0,8941, 33,1527) rot=(359,12, 322,38, 40,87)
    → post pos=(12,6159, 0,9082, 33,1464) rot=(359,02, 322,64, 38,49) | Δpos=(-0,0103, 0,0141, -0,0063) |Δpos|=0,0186м Δrot=2,391°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1311) | Δpos=(0,0000, 0,0000, -0,0149)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #7 | unit=Unit(Clone) | cell=Crouch/Idle/PointAim(PointAim) x10 | t=68,311 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 1,81→2,35/30,0 | kickScale=1,20
  visualState: climbPitch=0,038° punchPitch=4,844° punchYaw=0,800° | back=0,0387м up=0,0144м active=True | tau=0,140с
  Hand_R local: base rot=(4,55, 287,69, 354,75) pos=(0,2891, 0,0096, -0,0045)
    → final rot=(2,19, 288,34, 354,81) pos=(0,3066, 0,0190, -0,0092) | Δrot=2,444° Δpos=(0,0175, 0,0094, -0,0047) |Δpos|=0,0204м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0823, 0,8083, 32,6676) rot=(359,12, 322,42, 40,81)
    → post pos=(13,0670, 0,8190, 32,6573) rot=(358,85, 322,52, 38,38) | Δpos=(-0,0154, 0,0107, -0,0103) |Δpos|=0,0214м Δrot=2,442°
  Muzzle world: pre pos=(12,6261, 0,8945, 33,1528) rot=(359,12, 322,42, 40,81)
    → post pos=(12,6144, 0,9111, 33,1449) rot=(358,85, 322,52, 38,38) | Δpos=(-0,0117, 0,0166, -0,0079) |Δpos|=0,0218м Δrot=2,442°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1344) | Δpos=(0,0000, 0,0000, -0,0116)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #8 | unit=Unit(Clone) | cell=Crouch/Idle/PointAim(PointAim) x10 | t=68,414 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 2,13→2,66/30,0 | kickScale=1,20
  visualState: climbPitch=0,048° punchPitch=4,890° punchYaw=0,923° | back=0,0391м up=0,0145м active=True | tau=0,140с
  Hand_R local: base rot=(4,42, 287,82, 354,76) pos=(0,2901, 0,0101, -0,0047)
    → final rot=(2,15, 288,45, 354,83) pos=(0,3069, 0,0191, -0,0093) | Δrot=2,360° Δpos=(0,0169, 0,0090, -0,0046) |Δpos|=0,0197м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0814, 0,8091, 32,6670) rot=(359,02, 322,36, 40,65)
    → post pos=(13,0666, 0,8195, 32,6570) rot=(358,75, 322,46, 38,31) | Δpos=(-0,0148, 0,0104, -0,0099) |Δpos|=0,0206м Δrot=2,356°
  Muzzle world: pre pos=(12,6249, 0,8967, 33,1517) rot=(359,02, 322,36, 40,65)
    → post pos=(12,6137, 0,9128, 33,1441) rot=(358,75, 322,46, 38,31) | Δpos=(-0,0112, 0,0161, -0,0076) |Δpos|=0,0210м Δrot=2,356°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1239) | Δpos=(0,0000, 0,0000, -0,0220)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #9 | unit=Unit(Clone) | cell=Crouch/Idle/PointAim(PointAim) x10 | t=68,519 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 2,42→2,97/30,0 | kickScale=1,20
  visualState: climbPitch=0,060° punchPitch=4,869° punchYaw=0,477° | back=0,0390м up=0,0145м active=True | tau=0,140с
  Hand_R local: base rot=(4,51, 287,85, 354,77) pos=(0,2893, 0,0097, -0,0045)
    → final rot=(2,12, 288,08, 354,78) pos=(0,3068, 0,0191, -0,0093) | Δrot=2,403° Δpos=(0,0175, 0,0093, -0,0047) |Δpos|=0,0203м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0820, 0,8089, 32,6674) rot=(358,96, 322,33, 40,74)
    → post pos=(13,0669, 0,8191, 32,6574) rot=(359,01, 322,70, 38,36) | Δpos=(-0,0150, 0,0102, -0,0100) |Δpos|=0,0207м Δrot=2,400°
  Muzzle world: pre pos=(12,6251, 0,8971, 33,1517) rot=(358,96, 322,33, 40,74)
    → post pos=(12,6159, 0,9094, 33,1466) rot=(359,01, 322,70, 38,36) | Δpos=(-0,0093, 0,0123, -0,0051) |Δpos|=0,0163м Δrot=2,400°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1338) | Δpos=(0,0000, 0,0000, -0,0122)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #10 | unit=Unit(Clone) | cell=Crouch/Idle/PointAim(PointAim) x10 | t=68,628 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 2,72→3,26/30,0 | kickScale=1,20
  visualState: climbPitch=0,072° punchPitch=4,810° punchYaw=0,710° | back=0,0385м up=0,0143м active=True | tau=0,140с
  Hand_R local: base rot=(4,55, 287,65, 354,74) pos=(0,2889, 0,0095, -0,0044)
    → final rot=(2,19, 288,27, 354,81) pos=(0,3064, 0,0188, -0,0091) | Δrot=2,441° Δpos=(0,0175, 0,0093, -0,0047) |Δpos|=0,0204м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0825, 0,8086, 32,6678) rot=(359,08, 322,44, 40,81)
    → post pos=(13,0671, 0,8193, 32,6575) rot=(358,84, 322,56, 38,38) | Δpos=(-0,0153, 0,0107, -0,0103) |Δpos|=0,0213м Δrot=2,436°
  Muzzle world: pre pos=(12,6265, 0,8953, 33,1531) rot=(359,08, 322,44, 40,81)
    → post pos=(12,6150, 0,9115, 33,1454) rot=(358,84, 322,56, 38,38) | Δpos=(-0,0114, 0,0163, -0,0077) |Δpos|=0,0213м Δrot=2,436°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1335) | Δpos=(0,0000, 0,0000, -0,0125)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Crouch/Idle/Aiming(Aiming) x1 | t=71,127 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,00→0,54/30,0 | kickScale=1,20
  visualState: climbPitch=0,002° punchPitch=2,564° punchYaw=0,091° | back=0,0205м up=0,0076м active=True | tau=0,140с
  Hand_R local: base rot=(338,79, 302,49, 334,55) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(336,51, 303,77, 334,06) pos=(0,2911, -0,0010, -0,0085) | Δrot=2,567° Δpos=(0,0200, -0,0010, -0,0088) |Δpos|=0,0219м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0725, 0,8033, 32,6484) rot=(0,70, 323,16, 2,94)
    → post pos=(13,0653, 0,8243, 32,6452) rot=(0,92, 323,44, 0,40) | Δpos=(-0,0072, 0,0210, -0,0032) |Δpos|=0,0224м Δrot=2,565°
  Muzzle world: pre pos=(12,6695, 0,8955, 33,1777) rot=(0,70, 323,16, 2,94)
    → post pos=(12,6683, 0,9141, 33,1794) rot=(0,92, 323,44, 0,40) | Δpos=(-0,0012, 0,0186, 0,0017) |Δpos|=0,0187м Δrot=2,565°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1351) | Δpos=(0,0000, 0,0000, -0,0109)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Crouch/Idle/Aiming(Aiming) x3 | t=72,565 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,00→0,55/30,0 | kickScale=1,20
  visualState: climbPitch=0,002° punchPitch=2,564° punchYaw=0,148° | back=0,0205м up=0,0076м active=True | tau=0,140с
  Hand_R local: base rot=(338,79, 302,49, 334,55) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(336,53, 303,82, 334,05) pos=(0,2911, -0,0010, -0,0085) | Δrot=2,569° Δpos=(0,0200, -0,0010, -0,0088) |Δpos|=0,0219м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0722, 0,8048, 32,6486) rot=(0,46, 323,14, 2,94)
    → post pos=(13,0650, 0,8259, 32,6453) rot=(0,63, 323,42, 0,39) | Δpos=(-0,0072, 0,0211, -0,0033) |Δpos|=0,0225м Δrot=2,567°
  Muzzle world: pre pos=(12,6693, 0,8999, 33,1774) rot=(0,46, 323,14, 2,94)
    → post pos=(12,6682, 0,9191, 33,1790) rot=(0,63, 323,42, 0,39) | Δpos=(-0,0011, 0,0193, 0,0016) |Δpos|=0,0194м Δrot=2,567°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1371) | Δpos=(0,0000, 0,0000, -0,0089)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Crouch/Idle/Aiming(Aiming) x3 | t=72,666 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,31→0,86/30,0 | kickScale=1,20
  visualState: climbPitch=0,005° punchPitch=3,810° punchYaw=0,105° | back=0,0305м up=0,0113м active=True | tau=0,140с
  Hand_R local: base rot=(337,63, 303,17, 334,30) pos=(0,2815, -0,0005, -0,0042)
    → final rot=(335,40, 304,38, 333,82) pos=(0,3009, -0,0015, -0,0128) | Δrot=2,493° Δpos=(0,0194, -0,0010, -0,0086) |Δpos|=0,0212м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0685, 0,8158, 32,6469) rot=(0,53, 323,29, 1,63)
    → post pos=(13,0615, 0,8361, 32,6438) rot=(0,79, 323,56, 359,17) | Δpos=(-0,0070, 0,0203, -0,0032) |Δpos|=0,0217м Δrot=2,489°
  Muzzle world: pre pos=(12,6687, 0,9102, 33,1782) rot=(0,53, 323,29, 1,63)
    → post pos=(12,6675, 0,9274, 33,1798) rot=(0,79, 323,56, 359,17) | Δpos=(-0,0012, 0,0172, 0,0016) |Δpos|=0,0173м Δrot=2,489°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1381) | Δpos=(0,0000, 0,0000, -0,0079)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Crouch/Idle/Aiming(Aiming) x3 | t=72,767 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,63→1,17/30,0 | kickScale=1,20
  visualState: climbPitch=0,010° punchPitch=4,408° punchYaw=0,268° | back=0,0353м up=0,0131м active=True | tau=0,140с
  Hand_R local: base rot=(337,05, 303,45, 334,19) pos=(0,2864, -0,0008, -0,0064)
    → final rot=(334,92, 304,81, 333,64) pos=(0,3055, -0,0017, -0,0148) | Δrot=2,465° Δpos=(0,0191, -0,0010, -0,0085) |Δpos|=0,0209м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0667, 0,8211, 32,6461) rot=(0,60, 323,36, 1,00)
    → post pos=(13,0598, 0,8413, 32,6430) rot=(0,71, 323,62, 358,56) | Δpos=(-0,0068, 0,0202, -0,0031) |Δpos|=0,0216м Δrot=2,463°
  Muzzle world: pre pos=(12,6684, 0,9146, 33,1787) rot=(0,60, 323,36, 1,00)
    → post pos=(12,6673, 0,9335, 33,1800) rot=(0,71, 323,62, 358,56) | Δpos=(-0,0011, 0,0189, 0,0014) |Δpos|=0,0190м Δrot=2,463°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1381) | Δpos=(0,0000, 0,0000, -0,0079)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Crouch/Idle/Aiming(Aiming) x10 | t=74,261 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,00→0,54/30,0 | kickScale=1,20
  visualState: climbPitch=0,002° punchPitch=2,564° punchYaw=0,291° | back=0,0205м up=0,0076м active=True | tau=0,140с
  Hand_R local: base rot=(338,79, 302,49, 334,55) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(336,59, 303,94, 334,01) pos=(0,2911, -0,0010, -0,0085) | Δrot=2,578° Δpos=(0,0200, -0,0010, -0,0088) |Δpos|=0,0219м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0724, 0,8040, 32,6485) rot=(0,60, 323,16, 2,94)
    → post pos=(13,0652, 0,8252, 32,6453) rot=(0,66, 323,44, 0,38) | Δpos=(-0,0072, 0,0212, -0,0032) |Δpos|=0,0226м Δrot=2,574°
  Muzzle world: pre pos=(12,6695, 0,8974, 33,1776) rot=(0,60, 323,16, 2,94)
    → post pos=(12,6685, 0,9181, 33,1791) rot=(0,66, 323,44, 0,38) | Δpos=(-0,0010, 0,0207, 0,0015) |Δpos|=0,0208м Δrot=2,574°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1331) | Δpos=(0,0000, 0,0000, -0,0129)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Crouch/Idle/Aiming(Aiming) x10 | t=74,367 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,30→0,85/30,0 | kickScale=1,20
  visualState: climbPitch=0,005° punchPitch=3,769° punchYaw=0,210° | back=0,0302м up=0,0112м active=True | tau=0,140с
  Hand_R local: base rot=(337,66, 303,22, 334,28) pos=(0,2814, -0,0005, -0,0041)
    → final rot=(335,47, 304,45, 333,79) pos=(0,3005, -0,0015, -0,0126) | Δrot=2,465° Δpos=(0,0192, -0,0010, -0,0085) |Δpos|=0,0210м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0688, 0,8147, 32,6469) rot=(0,65, 323,30, 1,63)
    → post pos=(13,0618, 0,8347, 32,6438) rot=(0,89, 323,57, 359,20) | Δpos=(-0,0069, 0,0201, -0,0031) |Δpos|=0,0215м Δrot=2,461°
  Muzzle world: pre pos=(12,6690, 0,9076, 33,1785) rot=(0,65, 323,30, 1,63)
    → post pos=(12,6678, 0,9249, 33,1801) rot=(0,89, 323,57, 359,20) | Δpos=(-0,0012, 0,0173, 0,0016) |Δpos|=0,0174м Δrot=2,461°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1316) | Δpos=(0,0000, 0,0000, -0,0144)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Crouch/Idle/Aiming(Aiming) x10 | t=74,476 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,59→1,14/30,0 | kickScale=1,20
  visualState: climbPitch=0,009° punchPitch=4,292° punchYaw=0,449° | back=0,0343м up=0,0128м active=True | tau=0,140с
  Hand_R local: base rot=(337,16, 303,44, 334,19) pos=(0,2856, -0,0007, -0,0060)
    → final rot=(335,09, 304,91, 333,62) pos=(0,3046, -0,0017, -0,0144) | Δrot=2,465° Δpos=(0,0190, -0,0010, -0,0084) |Δpos|=0,0208м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0673, 0,8188, 32,6462) rot=(0,78, 323,37, 1,10)
    → post pos=(13,0604, 0,8391, 32,6431) rot=(0,78, 323,62, 358,65) | Δpos=(-0,0069, 0,0203, -0,0030) |Δpos|=0,0216м Δrot=2,462°
  Muzzle world: pre pos=(12,6687, 0,9103, 33,1789) rot=(0,78, 323,37, 1,10)
    → post pos=(12,6677, 0,9306, 33,1802) rot=(0,78, 323,62, 358,65) | Δpos=(-0,0010, 0,0203, 0,0013) |Δpos|=0,0203м Δrot=2,462°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1356) | Δpos=(0,0000, 0,0000, -0,0104)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #4 | unit=Unit(Clone) | cell=Crouch/Idle/Aiming(Aiming) x10 | t=74,587 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 0,89→1,43/30,0 | kickScale=1,20
  visualState: climbPitch=0,014° punchPitch=4,514° punchYaw=0,237° | back=0,0361м up=0,0134м active=True | tau=0,140с
  Hand_R local: base rot=(336,97, 303,66, 334,11) pos=(0,2875, -0,0008, -0,0069)
    → final rot=(334,81, 304,84, 333,63) pos=(0,3063, -0,0018, -0,0152) | Δrot=2,420° Δpos=(0,0188, -0,0009, -0,0083) |Δpos|=0,0206м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0666, 0,8209, 32,6458) rot=(0,73, 323,39, 0,84)
    → post pos=(13,0598, 0,8405, 32,6429) rot=(1,00, 323,65, 358,45) | Δpos=(-0,0068, 0,0196, -0,0030) |Δpos|=0,0210м Δrot=2,416°
  Muzzle world: pre pos=(12,6687, 0,9129, 33,1790) rot=(0,73, 323,39, 0,84)
    → post pos=(12,6674, 0,9294, 33,1806) rot=(1,00, 323,65, 358,45) | Δpos=(-0,0012, 0,0164, 0,0016) |Δpos|=0,0165м Δrot=2,416°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1337) | Δpos=(0,0000, 0,0000, -0,0123)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #5 | unit=Unit(Clone) | cell=Crouch/Idle/Aiming(Aiming) x10 | t=74,693 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 1,19→1,73/30,0 | kickScale=1,20
  visualState: climbPitch=0,021° punchPitch=4,678° punchYaw=0,534° | back=0,0374м up=0,0139м active=True | tau=0,140с
  Hand_R local: base rot=(336,76, 303,67, 334,11) pos=(0,2890, -0,0009, -0,0075)
    → final rot=(334,77, 305,17, 333,51) pos=(0,3076, -0,0018, -0,0158) | Δrot=2,426° Δpos=(0,0186, -0,0009, -0,0082) |Δpos|=0,0204м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0660, 0,8222, 32,6456) rot=(0,85, 323,41, 0,66)
    → post pos=(13,0593, 0,8421, 32,6426) rot=(0,79, 323,67, 358,25) | Δpos=(-0,0067, 0,0200, -0,0030) |Δpos|=0,0213м Δrot=2,422°
  Muzzle world: pre pos=(12,6685, 0,9128, 33,1792) rot=(0,85, 323,41, 0,66)
    → post pos=(12,6676, 0,9334, 33,1804) rot=(0,79, 323,67, 358,25) | Δpos=(-0,0009, 0,0206, 0,0012) |Δpos|=0,0207м Δrot=2,422°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1326) | Δpos=(0,0000, 0,0000, -0,0134)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #6 | unit=Unit(Clone) | cell=Crouch/Idle/Aiming(Aiming) x10 | t=74,793 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 1,51→2,05/30,0 | kickScale=1,20
  visualState: climbPitch=0,029° punchPitch=4,853° punchYaw=0,504° | back=0,0388м up=0,0144м active=True | tau=0,140с
  Hand_R local: base rot=(336,66, 303,89, 334,02) pos=(0,2904, -0,0010, -0,0081)
    → final rot=(334,59, 305,24, 333,48) pos=(0,3090, -0,0019, -0,0164) | Δrot=2,402° Δpos=(0,0186, -0,0009, -0,0082) |Δpos|=0,0203м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0655, 0,8238, 32,6454) rot=(0,75, 323,43, 0,46)
    → post pos=(13,0588, 0,8434, 32,6424) rot=(0,84, 323,68, 358,08) | Δpos=(-0,0067, 0,0197, -0,0029) |Δpos|=0,0210м Δrot=2,399°
  Muzzle world: pre pos=(12,6685, 0,9156, 33,1792) rot=(0,75, 323,43, 0,46)
    → post pos=(12,6675, 0,9341, 33,1806) rot=(0,84, 323,68, 358,08) | Δpos=(-0,0011, 0,0185, 0,0014) |Δpos|=0,0185м Δrot=2,399°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1335) | Δpos=(0,0000, 0,0000, -0,0125)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #7 | unit=Unit(Clone) | cell=Crouch/Idle/Aiming(Aiming) x10 | t=74,900 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 1,80→2,35/30,0 | kickScale=1,20
  visualState: climbPitch=0,038° punchPitch=4,821° punchYaw=0,637° | back=0,0386м up=0,0143м active=True | tau=0,140с
  Hand_R local: base rot=(336,69, 303,84, 334,04) pos=(0,2900, -0,0009, -0,0080)
    → final rot=(334,66, 305,34, 333,45) pos=(0,3087, -0,0019, -0,0163) | Δrot=2,442° Δpos=(0,0187, -0,0009, -0,0083) |Δpos|=0,0205м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0657, 0,8233, 32,6454) rot=(0,77, 323,43, 0,51)
    → post pos=(13,0589, 0,8433, 32,6424) rot=(0,73, 323,68, 358,09) | Δpos=(-0,0068, 0,0200, -0,0030) |Δpos|=0,0214м Δrot=2,437°
  Muzzle world: pre pos=(12,6685, 0,9148, 33,1792) rot=(0,77, 323,43, 0,51)
    → post pos=(12,6676, 0,9352, 33,1804) rot=(0,73, 323,68, 358,09) | Δpos=(-0,0009, 0,0204, 0,0013) |Δpos|=0,0205м Δrot=2,437°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1357) | Δpos=(0,0000, 0,0000, -0,0103)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #8 | unit=Unit(Clone) | cell=Crouch/Idle/Aiming(Aiming) x10 | t=75,005 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 2,10→2,65/30,0 | kickScale=1,20
  visualState: climbPitch=0,048° punchPitch=4,837° punchYaw=0,419° | back=0,0387м up=0,0144м active=True | tau=0,140с
  Hand_R local: base rot=(336,70, 303,91, 334,02) pos=(0,2900, -0,0009, -0,0080)
    → final rot=(334,56, 305,17, 333,50) pos=(0,3089, -0,0019, -0,0163) | Δrot=2,431° Δpos=(0,0188, -0,0009, -0,0083) |Δpos|=0,0206м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0656, 0,8234, 32,6454) rot=(0,71, 323,43, 0,49)
    → post pos=(13,0589, 0,8432, 32,6424) rot=(0,92, 323,68, 358,08) | Δpos=(-0,0068, 0,0198, -0,0030) |Δpos|=0,0211м Δrot=2,429°
  Muzzle world: pre pos=(12,6686, 0,9157, 33,1791) rot=(0,71, 323,43, 0,49)
    → post pos=(12,6674, 0,9330, 33,1807) rot=(0,92, 323,68, 358,08) | Δpos=(-0,0012, 0,0173, 0,0016) |Δpos|=0,0174м Δrot=2,429°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1368) | Δpos=(0,0000, 0,0000, -0,0092)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #9 | unit=Unit(Clone) | cell=Crouch/Idle/Aiming(Aiming) x10 | t=75,114 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 2,40→2,95/30,0 | kickScale=1,20
  visualState: climbPitch=0,059° punchPitch=4,789° punchYaw=0,717° | back=0,0383м up=0,0142м active=True | tau=0,140с
  Hand_R local: base rot=(336,69, 303,79, 334,06) pos=(0,2897, -0,0009, -0,0078)
    → final rot=(334,70, 305,40, 333,43) pos=(0,3085, -0,0019, -0,0161) | Δrot=2,471° Δpos=(0,0188, -0,0009, -0,0083) |Δpos|=0,0206м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0658, 0,8229, 32,6455) rot=(0,80, 323,42, 0,54)
    → post pos=(13,0590, 0,8432, 32,6425) rot=(0,65, 323,68, 358,09) | Δpos=(-0,0068, 0,0203, -0,0030) |Δpos|=0,0216м Δrot=2,469°
  Muzzle world: pre pos=(12,6686, 0,9142, 33,1792) rot=(0,80, 323,42, 0,54)
    → post pos=(12,6677, 0,9361, 33,1803) rot=(0,65, 323,68, 358,09) | Δpos=(-0,0008, 0,0219, 0,0011) |Δpos|=0,0219м Δrot=2,469°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1365) | Δpos=(0,0000, 0,0000, -0,0095)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #10 | unit=Unit(Clone) | cell=Crouch/Idle/Aiming(Aiming) x10 | t=75,223 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,570 | penalty 2,69→3,24/30,0 | kickScale=1,20
  visualState: climbPitch=0,071° punchPitch=4,770° punchYaw=0,626° | back=0,0382м up=0,0142м active=True | tau=0,140с
  Hand_R local: base rot=(336,76, 303,91, 334,02) pos=(0,2895, -0,0009, -0,0077)
    → final rot=(334,67, 305,32, 333,45) pos=(0,3083, -0,0019, -0,0161) | Δrot=2,454° Δpos=(0,0189, -0,0009, -0,0084) |Δpos|=0,0207м | applied=True canApply=True
  WeaponRoot world: pre pos=(13,0658, 0,8230, 32,6455) rot=(0,66, 323,42, 0,54)
    → post pos=(13,0590, 0,8430, 32,6425) rot=(0,72, 323,67, 358,10) | Δpos=(-0,0068, 0,0200, -0,0030) |Δpos|=0,0214м Δrot=2,451°
  Muzzle world: pre pos=(12,6687, 0,9158, 33,1790) rot=(0,66, 323,42, 0,54)
    → post pos=(12,6677, 0,9351, 33,1804) rot=(0,72, 323,67, 358,10) | Δpos=(-0,0010, 0,0193, 0,0014) |Δpos|=0,0194м Δrot=2,451°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1372) | Δpos=(0,0000, 0,0000, -0,0088)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Crouch/Walk/HipFire(HipFireCrouchWalk) x1 | t=79,056 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 0,00→0,64/30,0 | kickScale=1,20
  visualState: climbPitch=0,003° punchPitch=2,969° punchYaw=0,481° | back=0,0238м up=0,0088м active=True | tau=0,140с
  Hand_R local: base rot=(358,26, 356,55, 345,82) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(355,46, 357,65, 345,79) pos=(0,2748, 0,0078, -0,0234) | Δrot=3,012° Δpos=(0,0036, 0,0078, -0,0238) |Δpos|=0,0253м | applied=True canApply=True
  WeaponRoot world: pre pos=(12,3874, 1,0036, 35,0037) rot=(0,72, 321,49, 0,47)
    → post pos=(12,3801, 1,0288, 35,0004) rot=(0,71, 321,84, 357,48) | Δpos=(-0,0073, 0,0252, -0,0032) |Δpos|=0,0265м Δrot=3,008°
  Muzzle world: pre pos=(11,9725, 1,0957, 35,5237) rot=(0,72, 321,49, 0,47)
    → post pos=(11,9725, 1,1210, 35,5262) rot=(0,71, 321,84, 357,48) | Δpos=(0,0001, 0,0253, 0,0025) |Δpos|=0,0254м Δrot=3,008°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1357) | Δpos=(0,0000, 0,0000, -0,0103)
  cameraDist=3,3м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Crouch/Walk/HipFire(HipFireCrouchWalk) x3 | t=80,332 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 0,00→0,63/30,0 | kickScale=1,20
  visualState: climbPitch=0,003° punchPitch=2,969° punchYaw=0,607° | back=0,0238м up=0,0088м active=True | tau=0,140с
  Hand_R local: base rot=(358,06, 356,17, 346,20) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(355,27, 357,28, 346,16) pos=(0,2749, 0,0078, -0,0235) | Δrot=3,002° Δpos=(0,0037, 0,0078, -0,0238) |Δpos|=0,0253м | applied=True canApply=True
  WeaponRoot world: pre pos=(11,8737, 1,0058, 36,3809) rot=(0,89, 319,96, 0,27)
    → post pos=(11,8661, 1,0318, 36,3783) rot=(0,74, 320,35, 357,28) | Δpos=(-0,0076, 0,0260, -0,0025) |Δpos|=0,0272м Δrot=3,021°
  Muzzle world: pre pos=(11,4452, 1,0959, 36,8900) rot=(0,89, 319,96, 0,27)
    → post pos=(11,4453, 1,1236, 36,8936) rot=(0,74, 320,35, 357,28) | Δpos=(0,0001, 0,0277, 0,0036) |Δpos|=0,0279м Δrot=3,021°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1286) | Δpos=(0,0000, 0,0000, -0,0173)
  cameraDist=1,6м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Crouch/Walk/HipFire(HipFireCrouchWalk) x3 | t=80,436 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 0,40→1,02/30,0 | kickScale=1,20
  visualState: climbPitch=0,007° punchPitch=4,381° punchYaw=0,940° | back=0,0350м up=0,0130м active=True | tau=0,140с
  Hand_R local: base rot=(356,38, 356,33, 346,24) pos=(0,2732, 0,0040, -0,0120)
    → final rot=(353,73, 357,62, 346,15) pos=(0,2768, 0,0113, -0,0348) | Δrot=2,947° Δpos=(0,0037, 0,0073, -0,0228) |Δpos|=0,0242м | applied=True canApply=True
  WeaponRoot world: pre pos=(11,8243, 1,0244, 36,5006) rot=(0,69, 320,27, 358,77)
    → post pos=(11,8174, 1,0492, 36,4987) rot=(0,50, 320,63, 355,93) | Δpos=(-0,0069, 0,0247, -0,0019) |Δpos|=0,0258м Δrot=2,866°
  Muzzle world: pre pos=(11,4008, 1,1168, 37,0135) rot=(0,69, 320,27, 358,77)
    → post pos=(11,4012, 1,1437, 37,0172) rot=(0,50, 320,63, 355,93) | Δpos=(0,0004, 0,0268, 0,0037) |Δpos|=0,0271м Δrot=2,866°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1299) | Δpos=(0,0000, 0,0000, -0,0161)
  cameraDist=1,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Crouch/Walk/HipFire(HipFireCrouchWalk) x3 | t=80,545 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 0,78→1,41/30,0 | kickScale=1,20
  visualState: climbPitch=0,014° punchPitch=4,987° punchYaw=1,192° | back=0,0399м up=0,0148м active=True | tau=0,140с
  Hand_R local: base rot=(355,66, 357,33, 345,70) pos=(0,2739, 0,0057, -0,0174)
    → final rot=(353,10, 358,59, 345,48) pos=(0,2774, 0,0128, -0,0397) | Δrot=2,850° Δpos=(0,0035, 0,0071, -0,0224) |Δpos|=0,0237м | applied=True canApply=True
  WeaponRoot world: pre pos=(11,7793, 1,0308, 36,6244) rot=(0,60, 320,19, 358,28)
    → post pos=(11,7730, 1,0542, 36,6216) rot=(0,36, 320,60, 355,46) | Δpos=(-0,0063, 0,0234, -0,0029) |Δpos|=0,0244м Δrot=2,867°
  Muzzle world: pre pos=(11,3559, 1,1243, 37,1373) rot=(0,60, 320,19, 358,28)
    → post pos=(11,3573, 1,1502, 37,1402) rot=(0,36, 320,60, 355,46) | Δpos=(0,0014, 0,0260, 0,0029) |Δpos|=0,0262м Δrot=2,867°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1292) | Δpos=(0,0000, 0,0000, -0,0168)
  cameraDist=1,2м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Crouch/Walk/HipFire(HipFireCrouchWalk) x10 | t=81,870 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 0,00→0,63/30,0 | kickScale=1,20
  visualState: climbPitch=0,003° punchPitch=2,969° punchYaw=0,383° | back=0,0238м up=0,0088м active=True | tau=0,140с
  Hand_R local: base rot=(357,51, 356,23, 344,86) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(354,72, 357,37, 344,74) pos=(0,2750, 0,0075, -0,0235) | Δrot=3,012° Δpos=(0,0039, 0,0075, -0,0239) |Δpos|=0,0253м | applied=True canApply=True
  WeaponRoot world: pre pos=(11,2582, 1,0083, 38,0517) rot=(0,71, 317,99, 0,85)
    → post pos=(11,2512, 1,0332, 38,0474) rot=(0,75, 318,34, 357,89) | Δpos=(-0,0070, 0,0250, -0,0043) |Δpos|=0,0263м Δrot=2,982°
  Muzzle world: pre pos=(10,8119, 1,1005, 38,5450) rot=(0,71, 317,99, 0,85)
    → post pos=(10,8118, 1,1249, 38,5469) rot=(0,75, 318,34, 357,89) | Δpos=(-0,0002, 0,0244, 0,0019) |Δpos|=0,0244м Δrot=2,982°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1340) | Δpos=(0,0000, 0,0000, -0,0120)
  cameraDist=1,3м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Crouch/Walk/HipFire(HipFireCrouchWalk) x10 | t=81,973 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 0,40→1,03/30,0 | kickScale=1,20
  visualState: climbPitch=0,007° punchPitch=4,391° punchYaw=0,611° | back=0,0351м up=0,0131м active=True | tau=0,140с
  Hand_R local: base rot=(356,15, 357,07, 344,73) pos=(0,2731, 0,0039, -0,0121)
    → final rot=(353,53, 358,15, 344,70) pos=(0,2767, 0,0111, -0,0350) | Δrot=2,836° Δpos=(0,0036, 0,0072, -0,0229) |Δpos|=0,0242м | applied=True canApply=True
  WeaponRoot world: pre pos=(11,2134, 1,0213, 38,1568) rot=(0,94, 317,68, 359,40)
    → post pos=(11,2060, 1,0455, 38,1529) rot=(0,92, 317,95, 356,54) | Δpos=(-0,0073, 0,0242, -0,0039) |Δpos|=0,0256м Δrot=2,872°
  Muzzle world: pre pos=(10,7660, 1,1110, 38,6496) rot=(0,94, 317,68, 359,40)
    → post pos=(10,7647, 1,1352, 38,6511) rot=(0,92, 317,95, 356,54) | Δpos=(-0,0013, 0,0242, 0,0016) |Δpos|=0,0243м Δrot=2,872°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1303) | Δpos=(0,0000, 0,0000, -0,0156)
  cameraDist=1,2м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Crouch/Walk/HipFire(HipFireCrouchWalk) x10 | t=82,078 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 0,80→1,43/30,0 | kickScale=1,20
  visualState: climbPitch=0,014° punchPitch=5,042° punchYaw=0,510° | back=0,0403м up=0,0150м active=True | tau=0,140с
  Hand_R local: base rot=(355,67, 357,00, 345,05) pos=(0,2742, 0,0059, -0,0181)
    → final rot=(353,07, 357,76, 345,00) pos=(0,2778, 0,0130, -0,0401) | Δrot=2,700° Δpos=(0,0037, 0,0071, -0,0220) |Δpos|=0,0234м | applied=True canApply=True
  WeaponRoot world: pre pos=(11,1649, 1,0301, 38,2680) rot=(0,77, 317,05, 358,53)
    → post pos=(11,1574, 1,0535, 38,2645) rot=(0,92, 317,22, 355,78) | Δpos=(-0,0075, 0,0233, -0,0035) |Δpos|=0,0248м Δrot=2,757°
  Muzzle world: pre pos=(10,7135, 1,1217, 38,7567) rot=(0,77, 317,05, 358,53)
    → post pos=(10,7108, 1,1431, 38,7580) rot=(0,92, 317,22, 355,78) | Δpos=(-0,0027, 0,0214, 0,0013) |Δpos|=0,0216м Δrot=2,757°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1239) | Δpos=(0,0000, 0,0000, -0,0221)
  cameraDist=0,6м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #4 | unit=Unit(Clone) | cell=Crouch/Walk/HipFire(HipFireCrouchWalk) x10 | t=82,185 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 1,18→1,82/30,0 | kickScale=1,20
  visualState: climbPitch=0,023° punchPitch=5,318° punchYaw=0,657° | back=0,0425м up=0,0158м active=True | tau=0,140с
  Hand_R local: base rot=(355,52, 356,29, 345,29) pos=(0,2747, 0,0066, -0,0200)
    → final rot=(352,91, 357,29, 345,23) pos=(0,2786, 0,0138, -0,0422) | Δrot=2,790° Δpos=(0,0039, 0,0072, -0,0223) |Δpos|=0,0237м | applied=True canApply=True
  WeaponRoot world: pre pos=(11,1127, 1,0356, 38,3862) rot=(0,40, 316,07, 358,12)
    → post pos=(11,1052, 1,0595, 38,3830) rot=(0,33, 316,25, 355,33) | Δpos=(-0,0075, 0,0239, -0,0033) |Δpos|=0,0253м Δrot=2,791°
  Muzzle world: pre pos=(10,6539, 1,1315, 38,8672) rot=(0,40, 316,07, 358,12)
    → post pos=(10,6516, 1,1558, 38,8687) rot=(0,33, 316,25, 355,33) | Δpos=(-0,0024, 0,0243, 0,0015) |Δpos|=0,0245м Δrot=2,791°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1329) | Δpos=(0,0000, 0,0000, -0,0131)
  cameraDist=0,6м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #5 | unit=Unit(Clone) | cell=Crouch/Walk/HipFire(HipFireCrouchWalk) x10 | t=82,287 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 1,58→2,22/30,0 | kickScale=1,20
  visualState: climbPitch=0,034° punchPitch=5,525° punchYaw=0,817° | back=0,0442м up=0,0164м active=True | tau=0,140с
  Hand_R local: base rot=(355,46, 356,68, 345,50) pos=(0,2748, 0,0071, -0,0216)
    → final rot=(352,88, 357,85, 345,43) pos=(0,2785, 0,0144, -0,0439) | Δrot=2,842° Δpos=(0,0037, 0,0073, -0,0223) |Δpos|=0,0238м | applied=True canApply=True
  WeaponRoot world: pre pos=(11,0649, 1,0402, 38,5023) rot=(0,11, 315,26, 357,99)
    → post pos=(11,0579, 1,0638, 38,4983) rot=(0,04, 315,48, 355,17) | Δpos=(-0,0070, 0,0236, -0,0039) |Δpos|=0,0249м Δrot=2,831°
  Muzzle world: pre pos=(10,5999, 1,1394, 38,9765) rot=(0,11, 315,26, 357,99)
    → post pos=(10,5983, 1,1635, 38,9777) rot=(0,04, 315,48, 355,17) | Δpos=(-0,0016, 0,0241, 0,0012) |Δpos|=0,0242м Δrot=2,831°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1357) | Δpos=(0,0000, 0,0000, -0,0103)
  cameraDist=0,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #6 | unit=Unit(Clone) | cell=Crouch/Walk/HipFire(HipFireCrouchWalk) x10 | t=82,392 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 1,99→2,61/30,0 | kickScale=1,20
  visualState: climbPitch=0,046° punchPitch=5,592° punchYaw=1,076° | back=0,0447м up=0,0166м active=True | tau=0,140с
  Hand_R local: base rot=(355,57, 357,65, 345,56) pos=(0,2747, 0,0077, -0,0230)
    → final rot=(353,15, 358,85, 345,60) pos=(0,2779, 0,0149, -0,0445) | Δrot=2,704° Δpos=(0,0032, 0,0072, -0,0215) |Δpos|=0,0229м | applied=True canApply=True
  WeaponRoot world: pre pos=(11,0233, 1,0383, 38,6095) rot=(359,86, 315,48, 357,60)
    → post pos=(11,0176, 1,0602, 38,6035) rot=(359,67, 315,72, 354,86) | Δpos=(-0,0057, 0,0219, -0,0060) |Δpos|=0,0234м Δrot=2,757°
  Muzzle world: pre pos=(10,5610, 1,1403, 39,0857) rot=(359,86, 315,48, 357,60)
    → post pos=(10,5609, 1,1641, 39,0847) rot=(359,67, 315,72, 354,86) | Δpos=(-0,0001, 0,0239, -0,0010) |Δpos|=0,0239м Δrot=2,757°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1244) | Δpos=(0,0000, 0,0000, -0,0216)
  cameraDist=0,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #7 | unit=Unit(Clone) | cell=Crouch/Walk/HipFire(HipFireCrouchWalk) x10 | t=82,494 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 2,38→3,01/30,0 | kickScale=1,20
  visualState: climbPitch=0,061° punchPitch=5,658° punchYaw=0,822° | back=0,0453м up=0,0168м active=True | tau=0,140с
  Hand_R local: base rot=(355,69, 357,72, 346,04) pos=(0,2746, 0,0077, -0,0227)
    → final rot=(353,00, 358,61, 346,00) pos=(0,2779, 0,0151, -0,0450) | Δrot=2,833° Δpos=(0,0033, 0,0074, -0,0223) |Δpos|=0,0237м | applied=True canApply=True
  WeaponRoot world: pre pos=(10,9904, 1,0295, 38,7092) rot=(0,05, 316,15, 357,47)
    → post pos=(10,9847, 1,0523, 38,7048) rot=(0,23, 316,50, 354,66) | Δpos=(-0,0057, 0,0228, -0,0043) |Δpos|=0,0239м Δrot=2,839°
  Muzzle world: pre pos=(10,5335, 1,1294, 39,1911) rot=(0,05, 316,15, 357,47)
    → post pos=(10,5342, 1,1497, 39,1932) rot=(0,23, 316,50, 354,66) | Δpos=(0,0007, 0,0204, 0,0021) |Δpos|=0,0205м Δrot=2,839°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1358) | Δpos=(0,0000, 0,0000, -0,0102)
  cameraDist=0,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #8 | unit=Unit(Clone) | cell=Crouch/Walk/HipFire(HipFireCrouchWalk) x10 | t=82,601 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 2,77→3,40/30,0 | kickScale=1,20
  visualState: climbPitch=0,078° punchPitch=5,597° punchYaw=0,443° | back=0,0448м up=0,0167м active=True | tau=0,140с
  Hand_R local: base rot=(355,44, 357,56, 346,36) pos=(0,2745, 0,0074, -0,0223)
    → final rot=(352,70, 358,23, 346,31) pos=(0,2778, 0,0147, -0,0446) | Δrot=2,820° Δpos=(0,0033, 0,0073, -0,0223) |Δpos|=0,0237м | applied=True canApply=True
  WeaponRoot world: pre pos=(10,9522, 1,0266, 38,8221) rot=(0,67, 316,54, 357,31)
    → post pos=(10,9458, 1,0497, 38,8185) rot=(1,02, 316,84, 354,53) | Δpos=(-0,0064, 0,0231, -0,0036) |Δpos|=0,0242м Δrot=2,822°
  Muzzle world: pre pos=(10,4981, 1,1193, 39,3081) rot=(0,67, 316,54, 357,31)
    → post pos=(10,4974, 1,1379, 39,3106) rot=(1,02, 316,84, 354,53) | Δpos=(-0,0007, 0,0186, 0,0025) |Δpos|=0,0188м Δrot=2,822°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1353) | Δpos=(0,0000, 0,0000, -0,0106)
  cameraDist=0,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #9 | unit=Unit(Clone) | cell=Crouch/Walk/HipFire(HipFireCrouchWalk) x10 | t=82,711 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 3,15→3,79/30,0 | kickScale=1,20
  visualState: climbPitch=0,096° punchPitch=5,529° punchYaw=0,592° | back=0,0442м up=0,0164м active=True | tau=0,140с
  Hand_R local: base rot=(355,22, 356,84, 346,62) pos=(0,2746, 0,0072, -0,0219)
    → final rot=(352,56, 357,71, 346,56) pos=(0,2781, 0,0144, -0,0440) | Δrot=2,798° Δpos=(0,0035, 0,0072, -0,0221) |Δpos|=0,0235м | applied=True canApply=True
  WeaponRoot world: pre pos=(10,9062, 1,0298, 38,9433) rot=(0,94, 316,45, 357,23)
    → post pos=(10,8994, 1,0536, 38,9406) rot=(0,97, 316,79, 354,45) | Δpos=(-0,0068, 0,0239, -0,0027) |Δpos|=0,0250м Δrot=2,804°
  Muzzle world: pre pos=(10,4511, 1,1192, 39,4290) rot=(0,94, 316,45, 357,23)
    → post pos=(10,4508, 1,1424, 39,4324) rot=(0,97, 316,79, 354,45) | Δpos=(-0,0004, 0,0232, 0,0034) |Δpos|=0,0234м Δrot=2,804°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1323) | Δpos=(0,0000, 0,0000, -0,0136)
  cameraDist=0,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #10 | unit=Unit(Clone) | cell=Crouch/Walk/HipFire(HipFireCrouchWalk) x10 | t=82,815 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 3,55→4,18/30,0 | kickScale=1,20
  visualState: climbPitch=0,117° punchPitch=5,598° punchYaw=0,560° | back=0,0448м up=0,0167м active=True | tau=0,140с
  Hand_R local: base rot=(354,96, 356,81, 346,63) pos=(0,2747, 0,0072, -0,0222)
    → final rot=(352,26, 357,79, 346,53) pos=(0,2782, 0,0144, -0,0446) | Δrot=2,872° Δpos=(0,0035, 0,0072, -0,0225) |Δpos|=0,0238м | applied=True canApply=True
  WeaponRoot world: pre pos=(10,8610, 1,0353, 39,0659) rot=(0,74, 316,63, 357,29)
    → post pos=(10,8546, 1,0589, 39,0631) rot=(0,87, 316,96, 354,51) | Δpos=(-0,0064, 0,0236, -0,0028) |Δpos|=0,0246м Δrot=2,806°
  Muzzle world: pre pos=(10,4075, 1,1270, 39,5527) rot=(0,74, 316,63, 357,29)
    → post pos=(10,4074, 1,1488, 39,5560) rot=(0,87, 316,96, 354,51) | Δpos=(-0,0001, 0,0218, 0,0033) |Δpos|=0,0220м Δrot=2,806°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1368) | Δpos=(0,0000, 0,0000, -0,0092)
  cameraDist=0,6м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Crouch/Walk/PointAim(PointAim) x1 | t=85,628 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 0,00→0,63/30,0 | kickScale=1,20
  visualState: climbPitch=0,003° punchPitch=2,969° punchYaw=0,218° | back=0,0238м up=0,0088м active=True | tau=0,140с
  Hand_R local: base rot=(7,02, 287,79, 355,23) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(4,08, 288,23, 355,27) pos=(0,2928, 0,0116, -0,0058) | Δrot=2,978° Δpos=(0,0216, 0,0116, -0,0062) |Δpos|=0,0253м | applied=True canApply=True
  WeaponRoot world: pre pos=(9,4587, 1,1871, 42,2343) rot=(1,08, 310,33, 51,04)
    → post pos=(9,4416, 1,1968, 42,2175) rot=(0,97, 310,76, 48,11) | Δpos=(-0,0171, 0,0097, -0,0168) |Δpos|=0,0259м Δrot=2,960°
  Muzzle world: pre pos=(8,9011, 1,2377, 42,6052) rot=(1,08, 310,33, 51,04)
    → post pos=(8,8890, 1,2526, 42,5950) rot=(0,97, 310,76, 48,11) | Δpos=(-0,0122, 0,0149, -0,0102) |Δpos|=0,0218м Δrot=2,960°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1341) | Δpos=(0,0000, 0,0000, -0,0119)
  cameraDist=3,6м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Crouch/Walk/PointAim(PointAim) x3 | t=87,097 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 0,00→0,62/30,0 | kickScale=1,20
  visualState: climbPitch=0,003° punchPitch=2,969° punchYaw=0,280° | back=0,0238м up=0,0088м active=True | tau=0,140с
  Hand_R local: base rot=(7,02, 287,81, 355,24) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(4,08, 288,29, 355,29) pos=(0,2928, 0,0116, -0,0058) | Δrot=2,981° Δpos=(0,0216, 0,0116, -0,0062) |Δpos|=0,0253м | applied=True canApply=True
  WeaponRoot world: pre pos=(8,8497, 1,1951, 43,8277) rot=(0,20, 308,08, 49,18)
    → post pos=(8,8339, 1,2063, 43,8097) rot=(359,93, 308,39, 46,15) | Δpos=(-0,0158, 0,0112, -0,0180) |Δpos|=0,0265м Δrot=3,051°
  Muzzle world: pre pos=(8,2800, 1,2584, 44,1775) rot=(0,20, 308,08, 49,18)
    → post pos=(8,2684, 1,2768, 44,1651) rot=(359,93, 308,39, 46,15) | Δpos=(-0,0115, 0,0183, -0,0124) |Δpos|=0,0249м Δrot=3,051°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1229) | Δpos=(0,0000, 0,0000, -0,0231)
  cameraDist=2,9м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Crouch/Walk/PointAim(PointAim) x3 | t=87,200 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 0,40→1,02/30,0 | kickScale=1,20
  visualState: climbPitch=0,007° punchPitch=4,389° punchYaw=0,470° | back=0,0351м up=0,0131м active=True | tau=0,140с
  Hand_R local: base rot=(5,45, 288,07, 355,27) pos=(0,2827, 0,0062, -0,0029)
    → final rot=(2,67, 288,57, 355,32) pos=(0,3031, 0,0172, -0,0088) | Δrot=2,816° Δpos=(0,0204, 0,0110, -0,0058) |Δpos|=0,0239м | applied=True canApply=True
  WeaponRoot world: pre pos=(8,7980, 1,2034, 43,9092) rot=(359,33, 306,45, 47,57)
    → post pos=(8,7842, 1,2132, 43,8900) rot=(359,09, 306,59, 44,76) | Δpos=(-0,0139, 0,0097, -0,0193) |Δpos|=0,0256м Δrot=2,817°
  Muzzle world: pre pos=(8,2205, 1,2790, 44,2435) rot=(359,33, 306,45, 47,57)
    → post pos=(8,2098, 1,2951, 44,2282) rot=(359,09, 306,59, 44,76) | Δpos=(-0,0107, 0,0161, -0,0153) |Δpos|=0,0246м Δrot=2,817°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1208) | Δpos=(0,0000, 0,0000, -0,0252)
  cameraDist=2,9м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Crouch/Walk/PointAim(PointAim) x3 | t=87,310 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 0,77→1,40/30,0 | kickScale=1,20
  visualState: climbPitch=0,014° punchPitch=4,970° punchYaw=0,222° | back=0,0398м up=0,0148м active=True | tau=0,140с
  Hand_R local: base rot=(4,86, 288,19, 355,28) pos=(0,2871, 0,0086, -0,0042)
    → final rot=(2,07, 288,41, 355,30) pos=(0,3074, 0,0195, -0,0100) | Δrot=2,795° Δpos=(0,0203, 0,0109, -0,0058) |Δpos|=0,0238м | applied=True canApply=True
  WeaponRoot world: pre pos=(8,7465, 1,2045, 44,0113) rot=(359,29, 305,17, 47,22)
    → post pos=(8,7322, 1,2138, 43,9938) rot=(359,38, 305,60, 44,47) | Δpos=(-0,0143, 0,0093, -0,0175) |Δpos|=0,0244м Δrot=2,768°
  Muzzle world: pre pos=(8,1619, 1,2809, 44,3330) rot=(359,29, 305,17, 47,22)
    → post pos=(8,1520, 1,2926, 44,3227) rot=(359,38, 305,60, 44,47) | Δpos=(-0,0099, 0,0117, -0,0103) |Δpos|=0,0185м Δrot=2,768°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1300) | Δpos=(0,0000, 0,0000, -0,0160)
  cameraDist=3,0м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Crouch/Walk/PointAim(PointAim) x10 | t=88,819 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 0,00→0,63/30,0 | kickScale=1,20
  visualState: climbPitch=0,003° punchPitch=2,969° punchYaw=0,267° | back=0,0238м up=0,0088м active=True | tau=0,140с
  Hand_R local: base rot=(7,02, 287,83, 355,26) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(4,08, 288,30, 355,31) pos=(0,2928, 0,0116, -0,0058) | Δrot=2,981° Δpos=(0,0216, 0,0116, -0,0062) |Δpos|=0,0253м | applied=True canApply=True
  WeaponRoot world: pre pos=(8,1098, 1,1934, 45,6205) rot=(0,85, 298,65, 50,64)
    → post pos=(8,0975, 1,2030, 45,6001) rot=(0,67, 298,97, 47,76) | Δpos=(-0,0123, 0,0096, -0,0204) |Δpos|=0,0257м Δrot=2,904°
  Muzzle world: pre pos=(7,4891, 1,2474, 45,8710) rot=(0,85, 298,65, 50,64)
    → post pos=(7,4799, 1,2629, 45,8569) rot=(0,67, 298,97, 47,76) | Δpos=(-0,0092, 0,0155, -0,0141) |Δpos|=0,0229м Δrot=2,904°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1334) | Δpos=(0,0000, 0,0000, -0,0126)
  cameraDist=3,8м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Crouch/Walk/PointAim(PointAim) x10 | t=88,921 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 0,41→1,03/30,0 | kickScale=1,20
  visualState: climbPitch=0,007° punchPitch=4,401° punchYaw=0,650° | back=0,0352м up=0,0131м active=True | tau=0,140с
  Hand_R local: base rot=(5,45, 288,08, 355,29) pos=(0,2827, 0,0062, -0,0029)
    → final rot=(2,68, 288,74, 355,36) pos=(0,3032, 0,0173, -0,0088) | Δrot=2,855° Δpos=(0,0205, 0,0110, -0,0059) |Δpos|=0,0240м | applied=True canApply=True
  WeaponRoot world: pre pos=(8,0697, 1,1957, 45,7147) rot=(0,55, 298,64, 49,16)
    → post pos=(8,0575, 1,2048, 45,6941) rot=(0,26, 298,72, 46,32) | Δpos=(-0,0122, 0,0091, -0,0206) |Δpos|=0,0257м Δrot=2,862°
  Muzzle world: pre pos=(7,4500, 1,2550, 45,9665) rot=(0,55, 298,64, 49,16)
    → post pos=(7,4400, 1,2712, 45,9495) rot=(0,26, 298,72, 46,32) | Δpos=(-0,0100, 0,0162, -0,0170) |Δpos|=0,0255м Δrot=2,862°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1253) | Δpos=(0,0000, 0,0000, -0,0207)
  cameraDist=3,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Crouch/Walk/PointAim(PointAim) x10 | t=89,024 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 0,80→1,44/30,0 | kickScale=1,20
  visualState: climbPitch=0,014° punchPitch=5,088° punchYaw=0,700° | back=0,0407м up=0,0151м active=True | tau=0,140с
  Hand_R local: base rot=(4,78, 288,30, 355,31) pos=(0,2877, 0,0089, -0,0044)
    → final rot=(1,99, 288,84, 355,36) pos=(0,3082, 0,0199, -0,0102) | Δrot=2,840° Δpos=(0,0205, 0,0110, -0,0059) |Δpos|=0,0240м | applied=True canApply=True
  WeaponRoot world: pre pos=(8,0279, 1,1969, 45,8175) rot=(0,51, 298,53, 48,17)
    → post pos=(8,0157, 1,2068, 45,7984) rot=(0,32, 298,78, 45,29) | Δpos=(-0,0122, 0,0098, -0,0191) |Δpos|=0,0247м Δrot=2,887°
  Muzzle world: pre pos=(7,4082, 1,2580, 46,0691) rot=(0,51, 298,53, 48,17)
    → post pos=(7,3990, 1,2738, 46,0556) rot=(0,32, 298,78, 45,29) | Δpos=(-0,0093, 0,0157, -0,0135) |Δpos|=0,0227м Δrot=2,887°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1350) | Δpos=(0,0000, 0,0000, -0,0110)
  cameraDist=3,6м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #4 | unit=Unit(Clone) | cell=Crouch/Walk/PointAim(PointAim) x10 | t=89,130 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 1,20→1,83/30,0 | kickScale=1,20
  visualState: climbPitch=0,023° punchPitch=5,354° punchYaw=0,911° | back=0,0428м up=0,0159м active=True | tau=0,140с
  Hand_R local: base rot=(4,45, 288,35, 355,32) pos=(0,2901, 0,0102, -0,0050)
    → final rot=(1,73, 289,04, 355,39) pos=(0,3102, 0,0210, -0,0108) | Δrot=2,806° Δpos=(0,0201, 0,0108, -0,0057) |Δpos|=0,0235м | applied=True canApply=True
  WeaponRoot world: pre pos=(7,9821, 1,1981, 45,9247) rot=(0,83, 298,24, 47,70)
    → post pos=(7,9697, 1,2075, 45,9055) rot=(0,57, 298,39, 44,95) | Δpos=(-0,0124, 0,0095, -0,0193) |Δpos|=0,0248м Δrot=2,761°
  Muzzle world: pre pos=(7,3611, 1,2561, 46,1739) rot=(0,83, 298,24, 47,70)
    → post pos=(7,3512, 1,2720, 46,1590) rot=(0,57, 298,39, 44,95) | Δpos=(-0,0099, 0,0159, -0,0149) |Δpos|=0,0239м Δrot=2,761°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1304) | Δpos=(0,0000, 0,0000, -0,0156)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #5 | unit=Unit(Clone) | cell=Crouch/Walk/PointAim(PointAim) x10 | t=89,243 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 1,58→2,20/30,0 | kickScale=1,20
  visualState: climbPitch=0,033° punchPitch=5,361° punchYaw=0,946° | back=0,0429м up=0,0159м active=True | tau=0,140с
  Hand_R local: base rot=(4,42, 288,43, 355,33) pos=(0,2903, 0,0103, -0,0051)
    → final rot=(1,71, 289,07, 355,39) pos=(0,3102, 0,0210, -0,0108) | Δrot=2,783° Δpos=(0,0199, 0,0107, -0,0057) |Δpos|=0,0233м | applied=True canApply=True
  WeaponRoot world: pre pos=(7,9342, 1,2006, 46,0474) rot=(0,92, 298,63, 47,88)
    → post pos=(7,9221, 1,2104, 46,0308) rot=(0,64, 298,98, 45,08) | Δpos=(-0,0121, 0,0098, -0,0166) |Δpos|=0,0227м Δrot=2,839°
  Muzzle world: pre pos=(7,3148, 1,2573, 46,3006) rot=(0,92, 298,63, 47,88)
    → post pos=(7,3061, 1,2739, 46,2907) rot=(0,64, 298,98, 45,08) | Δpos=(-0,0087, 0,0165, -0,0099) |Δpos|=0,0212м Δrot=2,839°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1280) | Δpos=(0,0000, 0,0000, -0,0179)
  cameraDist=3,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #6 | unit=Unit(Clone) | cell=Crouch/Walk/PointAim(PointAim) x10 | t=89,344 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 1,97→2,61/30,0 | kickScale=1,20
  visualState: climbPitch=0,046° punchPitch=5,574° punchYaw=1,039° | back=0,0446м up=0,0166м active=True | tau=0,140с
  Hand_R local: base rot=(4,22, 288,49, 355,33) pos=(0,2917, 0,0111, -0,0055)
    → final rot=(1,49, 289,17, 355,40) pos=(0,3118, 0,0219, -0,0112) | Δrot=2,812° Δpos=(0,0201, 0,0108, -0,0057) |Δpos|=0,0235м | applied=True canApply=True
  WeaponRoot world: pre pos=(7,8944, 1,2057, 46,1713) rot=(0,50, 299,58, 46,88)
    → post pos=(7,8827, 1,2159, 46,1557) rot=(0,17, 299,92, 43,97) | Δpos=(-0,0117, 0,0102, -0,0156) |Δpos|=0,0220м Δrot=2,945°
  Muzzle world: pre pos=(7,2803, 1,2686, 46,4355) rot=(0,50, 299,58, 46,88)
    → post pos=(7,2722, 1,2863, 46,4265) rot=(0,17, 299,92, 43,97) | Δpos=(-0,0081, 0,0177, -0,0090) |Δpos|=0,0215м Δrot=2,945°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1327) | Δpos=(0,0000, 0,0000, -0,0132)
  cameraDist=3,3м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #7 | unit=Unit(Clone) | cell=Crouch/Walk/PointAim(PointAim) x10 | t=89,453 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 2,36→2,99/30,0 | kickScale=1,20
  visualState: climbPitch=0,061° punchPitch=5,530° punchYaw=0,758° | back=0,0442м up=0,0165м active=True | tau=0,140с
  Hand_R local: base rot=(4,25, 288,50, 355,34) pos=(0,2914, 0,0109, -0,0054)
    → final rot=(1,50, 288,93, 355,37) pos=(0,3115, 0,0217, -0,0112) | Δrot=2,783° Δpos=(0,0201, 0,0108, -0,0057) |Δpos|=0,0235м | applied=True canApply=True
  WeaponRoot world: pre pos=(7,8478, 1,2110, 46,2899) rot=(359,78, 298,85, 46,00)
    → post pos=(7,8363, 1,2218, 46,2709) rot=(359,55, 299,13, 43,17) | Δpos=(-0,0115, 0,0108, -0,0190) |Δpos|=0,0247м Δrot=2,852°
  Muzzle world: pre pos=(7,2315, 1,2833, 46,5468) rot=(359,78, 298,85, 46,00)
    → post pos=(7,2233, 1,3002, 46,5338) rot=(359,55, 299,13, 43,17) | Δpos=(-0,0082, 0,0169, -0,0130) |Δpos|=0,0228м Δrot=2,852°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1321) | Δpos=(0,0000, 0,0000, -0,0139)
  cameraDist=3,2м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #8 | unit=Unit(Clone) | cell=Crouch/Walk/PointAim(PointAim) x10 | t=89,562 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 2,77→3,38/30,0 | kickScale=1,20
  visualState: climbPitch=0,077° punchPitch=5,504° punchYaw=0,504° | back=0,0440м up=0,0164м active=True | tau=0,140с
  Hand_R local: base rot=(4,06, 288,43, 355,33) pos=(0,2927, 0,0116, -0,0058)
    → final rot=(1,50, 288,72, 355,35) pos=(0,3113, 0,0216, -0,0111) | Δrot=2,577° Δpos=(0,0186, 0,0100, -0,0053) |Δpos|=0,0218м | applied=True canApply=True
  WeaponRoot world: pre pos=(7,8044, 1,2170, 46,3824) rot=(358,89, 296,76, 45,76)
    → post pos=(7,7956, 1,2265, 46,3621) rot=(358,73, 296,95, 43,19) | Δpos=(-0,0088, 0,0094, -0,0203) |Δpos|=0,0241м Δrot=2,568°
  Muzzle world: pre pos=(7,1804, 1,3001, 46,6165) rot=(358,89, 296,76, 45,76)
    → post pos=(7,1741, 1,3144, 46,6009) rot=(358,73, 296,95, 43,19) | Δpos=(-0,0063, 0,0143, -0,0155) |Δpos|=0,0220м Δrot=2,568°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1061) | Δpos=(0,0000, 0,0000, -0,0398)
  cameraDist=3,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #9 | unit=Unit(Clone) | cell=Crouch/Walk/PointAim(PointAim) x10 | t=89,671 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 3,13→3,76/30,0 | kickScale=1,20
  visualState: climbPitch=0,095° punchPitch=5,495° punchYaw=0,769° | back=0,0440м up=0,0163м active=True | tau=0,140с
  Hand_R local: base rot=(4,21, 288,29, 355,31) pos=(0,2914, 0,0109, -0,0054)
    → final rot=(1,51, 288,95, 355,38) pos=(0,3112, 0,0215, -0,0111) | Δrot=2,778° Δpos=(0,0198, 0,0106, -0,0057) |Δpos|=0,0232м | applied=True canApply=True
  WeaponRoot world: pre pos=(7,7596, 1,2163, 46,4856) rot=(358,93, 295,44, 46,25)
    → post pos=(7,7487, 1,2258, 46,4658) rot=(358,72, 295,55, 43,52) | Δpos=(-0,0109, 0,0095, -0,0198) |Δpos|=0,0245м Δrot=2,729°
  Muzzle world: pre pos=(7,1301, 1,2982, 46,7047) rot=(358,93, 295,44, 46,25)
    → post pos=(7,1214, 1,3135, 46,6889) rot=(358,72, 295,55, 43,52) | Δpos=(-0,0087, 0,0153, -0,0158) |Δpos|=0,0236м Δrot=2,729°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1273) | Δpos=(0,0000, 0,0000, -0,0187)
  cameraDist=4,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #10 | unit=Unit(Clone) | cell=Crouch/Walk/PointAim(PointAim) x10 | t=89,778 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 3,52→4,15/30,0 | kickScale=1,20
  visualState: climbPitch=0,115° punchPitch=5,533° punchYaw=0,995° | back=0,0443м up=0,0165м active=True | tau=0,140с
  Hand_R local: base rot=(4,20, 288,40, 355,33) pos=(0,2914, 0,0109, -0,0054)
    → final rot=(1,46, 289,14, 355,40) pos=(0,3115, 0,0217, -0,0112) | Δrot=2,837° Δpos=(0,0201, 0,0108, -0,0058) |Δpos|=0,0235м | applied=True canApply=True
  WeaponRoot world: pre pos=(7,7111, 1,2121, 46,5958) rot=(359,61, 294,56, 46,04)
    → post pos=(7,6995, 1,2216, 46,5762) rot=(359,39, 294,56, 43,15) | Δpos=(-0,0117, 0,0094, -0,0197) |Δpos|=0,0247м Δrot=2,895°
  Muzzle world: pre pos=(7,0776, 1,2864, 46,8058) rot=(359,61, 294,56, 46,04)
    → post pos=(7,0677, 1,3020, 46,7893) rot=(359,39, 294,56, 43,15) | Δpos=(-0,0099, 0,0156, -0,0165) |Δpos|=0,0247м Δrot=2,895°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1327) | Δpos=(0,0000, 0,0000, -0,0133)
  cameraDist=4,6м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Crouch/Walk/Aiming(Aiming) x1 | t=92,575 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 0,00→0,63/30,0 | kickScale=1,20
  visualState: climbPitch=0,003° punchPitch=2,969° punchYaw=0,512° | back=0,0238м up=0,0088м active=True | tau=0,140с
  Hand_R local: base rot=(338,82, 302,73, 335,27) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(336,31, 304,52, 334,59) pos=(0,2942, -0,0011, -0,0101) | Δrot=3,004° Δpos=(0,0231, -0,0011, -0,0104) |Δpos|=0,0253м | applied=True canApply=True
  WeaponRoot world: pre pos=(6,5538, 1,2107, 49,4558) rot=(1,22, 276,87, 9,08)
    → post pos=(6,5473, 1,2311, 49,4367) rot=(1,29, 276,38, 6,21) | Δpos=(-0,0065, 0,0203, -0,0191) |Δpos|=0,0286м Δrot=2,896°
  Muzzle world: pre pos=(5,8907, 1,2958, 49,5197) rot=(1,22, 276,87, 9,08)
    → post pos=(5,8841, 1,3160, 49,4999) rot=(1,29, 276,38, 6,21) | Δpos=(-0,0066, 0,0201, -0,0198) |Δpos|=0,0290м Δrot=2,896°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1320) | Δpos=(0,0000, 0,0000, -0,0139)
  cameraDist=4,8м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Crouch/Walk/Aiming(Aiming) x3 | t=94,061 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 0,00→0,62/30,0 | kickScale=1,20
  visualState: climbPitch=0,003° punchPitch=2,969° punchYaw=0,456° | back=0,0238м up=0,0088м active=True | tau=0,140с
  Hand_R local: base rot=(338,82, 302,74, 335,30) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(336,29, 304,48, 334,65) pos=(0,2942, -0,0011, -0,0101) | Δrot=2,997° Δpos=(0,0231, -0,0011, -0,0104) |Δpos|=0,0253м | applied=True canApply=True
  WeaponRoot world: pre pos=(4,8793, 1,1823, 49,1841) rot=(3,25, 280,42, 9,14)
    → post pos=(4,8738, 1,2061, 49,1763) rot=(3,15, 280,96, 5,94) | Δpos=(-0,0055, 0,0238, -0,0078) |Δpos|=0,0256м Δrot=3,277°
  Muzzle world: pre pos=(4,2189, 1,2438, 49,2893) rot=(3,25, 280,42, 9,14)
    → post pos=(4,2155, 1,2694, 49,2931) rot=(3,15, 280,96, 5,94) | Δpos=(-0,0033, 0,0257, 0,0038) |Δpos|=0,0262м Δrot=3,277°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1219) | Δpos=(0,0000, 0,0000, -0,0241)
  cameraDist=5,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Crouch/Walk/Aiming(Aiming) x3 | t=94,168 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 0,38→1,01/30,0 | kickScale=1,20
  visualState: climbPitch=0,007° punchPitch=4,353° punchYaw=0,425° | back=0,0348м up=0,0129м active=True | tau=0,140с
  Hand_R local: base rot=(337,53, 303,62, 334,98) pos=(0,2829, -0,0006, -0,0049)
    → final rot=(335,03, 305,11, 334,39) pos=(0,3050, -0,0016, -0,0149) | Δrot=2,852° Δpos=(0,0221, -0,0011, -0,0100) |Δpos|=0,0243м | applied=True canApply=True
  WeaponRoot world: pre pos=(4,7554, 1,1968, 49,1617) rot=(2,83, 280,97, 6,93)
    → post pos=(4,7510, 1,2194, 49,1514) rot=(2,93, 281,28, 4,14) | Δpos=(-0,0044, 0,0226, -0,0103) |Δpos|=0,0252м Δrot=2,826°
  Muzzle world: pre pos=(4,0972, 1,2636, 49,2769) rot=(2,83, 280,97, 6,93)
    → post pos=(4,0942, 1,2856, 49,2750) rot=(2,93, 281,28, 4,14) | Δpos=(-0,0030, 0,0220, -0,0019) |Δpos|=0,0223м Δrot=2,826°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1298) | Δpos=(0,0000, 0,0000, -0,0162)
  cameraDist=5,8м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Crouch/Walk/Aiming(Aiming) x3 | t=94,278 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 0,76→1,39/30,0 | kickScale=1,20
  visualState: climbPitch=0,013° punchPitch=4,957° punchYaw=0,621° | back=0,0397м up=0,0147м active=True | tau=0,140с
  Hand_R local: base rot=(336,94, 303,90, 334,88) pos=(0,2879, -0,0008, -0,0072)
    → final rot=(334,55, 305,57, 334,22) pos=(0,3097, -0,0018, -0,0171) | Δrot=2,836° Δpos=(0,0218, -0,0010, -0,0099) |Δpos|=0,0239м | applied=True canApply=True
  WeaponRoot world: pre pos=(4,6354, 1,2013, 49,1304) rot=(2,63, 280,32, 6,74)
    → post pos=(4,6315, 1,2236, 49,1197) rot=(2,58, 280,52, 3,95) | Δpos=(-0,0039, 0,0222, -0,0107) |Δpos|=0,0250м Δrot=2,807°
  Muzzle world: pre pos=(3,9762, 1,2705, 49,2384) rot=(2,63, 280,32, 6,74)
    → post pos=(3,9736, 1,2938, 49,2349) rot=(2,58, 280,52, 3,95) | Δpos=(-0,0026, 0,0233, -0,0036) |Δpos|=0,0237м Δrot=2,807°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1325) | Δpos=(0,0000, 0,0000, -0,0135)
  cameraDist=5,9м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Crouch/Walk/Aiming(Aiming) x10 | t=95,779 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 0,00→0,63/30,0 | kickScale=1,20
  visualState: climbPitch=0,003° punchPitch=2,969° punchYaw=0,369° | back=0,0238м up=0,0088м active=True | tau=0,140с
  Hand_R local: base rot=(338,82, 302,78, 335,43) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(336,26, 304,45, 334,80) pos=(0,2942, -0,0011, -0,0101) | Δrot=2,988° Δpos=(0,0231, -0,0011, -0,0105) |Δpos|=0,0253м | applied=True canApply=True
  WeaponRoot world: pre pos=(2,9348, 1,1705, 48,8910) rot=(4,97, 286,82, 9,60)
    → post pos=(2,9261, 1,1935, 48,8787) rot=(4,99, 287,05, 6,81) | Δpos=(-0,0087, 0,0230, -0,0123) |Δpos|=0,0275м Δrot=2,811°
  Muzzle world: pre pos=(2,2885, 1,2117, 49,0689) rot=(4,97, 286,82, 9,60)
    → post pos=(2,2819, 1,2352, 49,0638) rot=(4,99, 287,05, 6,81) | Δpos=(-0,0066, 0,0234, -0,0051) |Δpos|=0,0249м Δrot=2,811°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1344) | Δpos=(0,0000, 0,0000, -0,0116)
  cameraDist=4,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Crouch/Walk/Aiming(Aiming) x10 | t=95,889 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 0,38→1,02/30,0 | kickScale=1,20
  visualState: climbPitch=0,007° punchPitch=4,324° punchYaw=0,549° | back=0,0346м up=0,0129м active=True | tau=0,140с
  Hand_R local: base rot=(337,56, 303,60, 335,14) pos=(0,2825, -0,0005, -0,0048)
    → final rot=(335,10, 305,24, 334,49) pos=(0,3047, -0,0016, -0,0149) | Δrot=2,883° Δpos=(0,0222, -0,0010, -0,0101) |Δpos|=0,0244м | applied=True canApply=True
  WeaponRoot world: pre pos=(2,8068, 1,1779, 48,8660) rot=(5,10, 288,32, 9,38)
    → post pos=(2,8000, 1,1993, 48,8554) rot=(5,10, 288,59, 6,59) | Δpos=(-0,0068, 0,0214, -0,0106) |Δpos|=0,0249м Δrot=2,819°
  Muzzle world: pre pos=(2,1655, 1,2176, 49,0610) rot=(5,10, 288,32, 9,38)
    → post pos=(2,1610, 1,2398, 49,0581) rot=(5,10, 288,59, 6,59) | Δpos=(-0,0044, 0,0222, -0,0030) |Δpos|=0,0228м Δrot=2,819°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1332) | Δpos=(0,0000, 0,0000, -0,0127)
  cameraDist=4,8м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Crouch/Walk/Aiming(Aiming) x10 | t=95,992 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 0,78→1,42/30,0 | kickScale=1,20
  visualState: climbPitch=0,014° punchPitch=5,038° punchYaw=0,396° | back=0,0403м up=0,0150м active=True | tau=0,140с
  Hand_R local: base rot=(336,91, 304,03, 334,98) pos=(0,2884, -0,0008, -0,0074)
    → final rot=(334,39, 305,45, 334,39) pos=(0,3103, -0,0018, -0,0174) | Δrot=2,832° Δpos=(0,0219, -0,0010, -0,0100) |Δpos|=0,0241м | applied=True canApply=True
  WeaponRoot world: pre pos=(2,6997, 1,1810, 48,8488) rot=(4,77, 289,50, 9,12)
    → post pos=(2,6928, 1,2023, 48,8378) rot=(4,95, 289,74, 6,38) | Δpos=(-0,0069, 0,0213, -0,0110) |Δpos|=0,0249м Δrot=2,772°
  Muzzle world: pre pos=(2,0629, 1,2246, 49,0574) rot=(4,77, 289,50, 9,12)
    → post pos=(2,0583, 1,2445, 49,0536) rot=(4,95, 289,74, 6,38) | Δpos=(-0,0046, 0,0198, -0,0038) |Δpos|=0,0207м Δrot=2,772°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1355) | Δpos=(0,0000, 0,0000, -0,0105)
  cameraDist=4,9м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #4 | unit=Unit(Clone) | cell=Crouch/Walk/Aiming(Aiming) x10 | t=96,100 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 1,17→1,80/30,0 | kickScale=1,20
  visualState: climbPitch=0,022° punchPitch=5,303° punchYaw=0,735° | back=0,0424м up=0,0158м active=True | tau=0,140с
  Hand_R local: base rot=(336,62, 304,09, 334,95) pos=(0,2905, -0,0009, -0,0084)
    → final rot=(334,27, 305,87, 334,25) pos=(0,3123, -0,0019, -0,0183) | Δrot=2,854° Δpos=(0,0218, -0,0010, -0,0099) |Δpos|=0,0239м | applied=True canApply=True
  WeaponRoot world: pre pos=(2,5851, 1,1817, 48,8392) rot=(5,13, 290,88, 8,89)
    → post pos=(2,5779, 1,2031, 48,8298) rot=(5,04, 291,16, 6,10) | Δpos=(-0,0072, 0,0214, -0,0094) |Δpos|=0,0244м Δrot=2,827°
  Muzzle world: pre pos=(1,9533, 1,2212, 49,0635) rot=(5,13, 290,88, 8,89)
    → post pos=(1,9490, 1,2443, 49,0618) rot=(5,04, 291,16, 6,10) | Δpos=(-0,0043, 0,0231, -0,0017) |Δpos|=0,0235м Δrot=2,827°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1359) | Δpos=(0,0000, 0,0000, -0,0101)
  cameraDist=5,0м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #5 | unit=Unit(Clone) | cell=Crouch/Walk/Aiming(Aiming) x10 | t=96,207 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 1,55→2,19/30,0 | kickScale=1,20
  visualState: climbPitch=0,033° punchPitch=5,425° punchYaw=0,725° | back=0,0434м up=0,0161м active=True | tau=0,140с
  Hand_R local: base rot=(336,55, 304,30, 334,89) pos=(0,2916, -0,0010, -0,0089)
    → final rot=(334,14, 305,93, 334,23) pos=(0,3133, -0,0020, -0,0188) | Δrot=2,829° Δpos=(0,0217, -0,0010, -0,0099) |Δpos|=0,0239м | applied=True canApply=True
  WeaponRoot world: pre pos=(2,4677, 1,1755, 48,8329) rot=(6,04, 293,00, 9,32)
    → post pos=(2,4586, 1,1963, 48,8236) rot=(6,11, 293,37, 6,60) | Δpos=(-0,0091, 0,0207, -0,0092) |Δpos|=0,0245м Δrot=2,777°
  Muzzle world: pre pos=(1,8439, 1,2043, 49,0799) rot=(6,04, 293,00, 9,32)
    → post pos=(1,8381, 1,2249, 49,0791) rot=(6,11, 293,37, 6,60) | Δpos=(-0,0057, 0,0206, -0,0009) |Δpos|=0,0214м Δrot=2,777°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1358) | Δpos=(0,0000, 0,0000, -0,0102)
  cameraDist=5,1м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #6 | unit=Unit(Clone) | cell=Crouch/Walk/Aiming(Aiming) x10 | t=96,310 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 1,96→2,59/30,0 | kickScale=1,20
  visualState: climbPitch=0,046° punchPitch=5,581° punchYaw=0,718° | back=0,0446м up=0,0166м active=True | tau=0,140с
  Hand_R local: base rot=(336,39, 304,39, 334,86) pos=(0,2929, -0,0010, -0,0095)
    → final rot=(333,99, 306,01, 334,20) pos=(0,3145, -0,0020, -0,0193) | Δrot=2,814° Δpos=(0,0216, -0,0010, -0,0098) |Δpos|=0,0237м | applied=True canApply=True
  WeaponRoot world: pre pos=(2,3491, 1,1785, 48,8234) rot=(6,20, 294,56, 8,79)
    → post pos=(2,3404, 1,2008, 48,8145) rot=(6,13, 294,84, 5,85) | Δpos=(-0,0087, 0,0223, -0,0088) |Δpos|=0,0256м Δrot=2,977°
  Muzzle world: pre pos=(1,7326, 1,2056, 49,0882) rot=(6,20, 294,56, 8,79)
    → post pos=(1,7272, 1,2293, 49,0871) rot=(6,13, 294,84, 5,85) | Δpos=(-0,0053, 0,0237, -0,0012) |Δpos|=0,0243м Δrot=2,977°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1353) | Δpos=(0,0000, 0,0000, -0,0107)
  cameraDist=5,2м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #7 | unit=Unit(Clone) | cell=Crouch/Walk/Aiming(Aiming) x10 | t=96,413 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 2,35→2,99/30,0 | kickScale=1,20
  visualState: climbPitch=0,060° punchPitch=5,632° punchYaw=0,421° | back=0,0451м up=0,0168м active=True | tau=0,140с
  Hand_R local: base rot=(336,34, 304,41, 334,86) pos=(0,2932, -0,0010, -0,0096)
    → final rot=(333,82, 305,79, 334,28) pos=(0,3149, -0,0021, -0,0195) | Δrot=2,815° Δpos=(0,0217, -0,0010, -0,0099) |Δpos|=0,0238м | applied=True canApply=True
  WeaponRoot world: pre pos=(2,2370, 1,1881, 48,8211) rot=(5,52, 295,90, 6,78)
    → post pos=(2,2288, 1,2097, 48,8139) rot=(5,77, 296,29, 3,91) | Δpos=(-0,0082, 0,0216, -0,0072) |Δpos|=0,0242м Δrot=2,946°
  Muzzle world: pre pos=(1,6286, 1,2236, 49,1034) rot=(5,52, 295,90, 6,78)
    → post pos=(1,6244, 1,2427, 49,1048) rot=(5,77, 296,29, 3,91) | Δpos=(-0,0042, 0,0192, 0,0014) |Δpos|=0,0197м Δrot=2,946°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1368) | Δpos=(0,0000, 0,0000, -0,0092)
  cameraDist=5,3м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #8 | unit=Unit(Clone) | cell=Crouch/Walk/Aiming(Aiming) x10 | t=96,524 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 2,74→3,37/30,0 | kickScale=1,20
  visualState: climbPitch=0,077° punchPitch=5,532° punchYaw=0,429° | back=0,0443м up=0,0165м active=True | tau=0,140с
  Hand_R local: base rot=(336,32, 304,26, 334,93) pos=(0,2928, -0,0010, -0,0095)
    → final rot=(333,90, 305,76, 334,31) pos=(0,3141, -0,0020, -0,0192) | Δrot=2,777° Δpos=(0,0213, -0,0010, -0,0097) |Δpos|=0,0234м | applied=True canApply=True
  WeaponRoot world: pre pos=(2,1163, 1,1925, 48,8091) rot=(5,22, 297,13, 6,37)
    → post pos=(2,1079, 1,2144, 48,8002) rot=(5,30, 297,35, 3,70) | Δpos=(-0,0084, 0,0219, -0,0089) |Δpos|=0,0251м Δrot=2,693°
  Muzzle world: pre pos=(1,5147, 1,2315, 49,1049) rot=(5,22, 297,13, 6,37)
    → post pos=(1,5095, 1,2529, 49,1024) rot=(5,30, 297,35, 3,70) | Δpos=(-0,0052, 0,0214, -0,0025) |Δpos|=0,0222м Δrot=2,693°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1311) | Δpos=(0,0000, 0,0000, -0,0148)
  cameraDist=5,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #9 | unit=Unit(Clone) | cell=Crouch/Walk/Aiming(Aiming) x10 | t=96,630 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 3,13→3,76/30,0 | kickScale=1,20
  visualState: climbPitch=0,095° punchPitch=5,554° punchYaw=0,550° | back=0,0444м up=0,0165м active=True | tau=0,140с
  Hand_R local: base rot=(336,32, 304,28, 334,94) pos=(0,2927, -0,0010, -0,0094)
    → final rot=(333,91, 305,89, 334,29) pos=(0,3142, -0,0020, -0,0193) | Δrot=2,818° Δpos=(0,0215, -0,0010, -0,0098) |Δpos|=0,0237м | applied=True canApply=True
  WeaponRoot world: pre pos=(2,0011, 1,1924, 48,7847) rot=(5,13, 297,08, 7,15)
    → post pos=(1,9933, 1,2139, 48,7753) rot=(5,16, 297,24, 4,42) | Δpos=(-0,0078, 0,0215, -0,0094) |Δpos|=0,0248м Δrot=2,750°
  Muzzle world: pre pos=(1,3986, 1,2323, 49,0787) rot=(5,13, 297,08, 7,15)
    → post pos=(1,3938, 1,2540, 49,0752) rot=(5,16, 297,24, 4,42) | Δpos=(-0,0049, 0,0217, -0,0034) |Δpos|=0,0225м Δrot=2,750°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1346) | Δpos=(0,0000, 0,0000, -0,0114)
  cameraDist=5,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #10 | unit=Unit(Clone) | cell=Crouch/Walk/Aiming(Aiming) x10 | t=96,739 | ammo=Ammo_556x45mmNATO | mode=FullAuto
  recoil: added=0,660 | penalty 3,51→4,15/30,0 | kickScale=1,20
  visualState: climbPitch=0,115° punchPitch=5,525° punchYaw=0,678° | back=0,0442м up=0,0164м active=True | tau=0,140с
  Hand_R local: base rot=(336,35, 304,33, 334,94) pos=(0,2925, -0,0010, -0,0094)
    → final rot=(333,96, 305,99, 334,27) pos=(0,3140, -0,0020, -0,0192) | Δrot=2,824° Δpos=(0,0215, -0,0010, -0,0098) |Δpos|=0,0237м | applied=True canApply=True
  WeaponRoot world: pre pos=(1,8860, 1,1955, 48,7695) rot=(5,03, 297,98, 7,60)
    → post pos=(1,8773, 1,2177, 48,7605) rot=(4,99, 298,15, 4,83) | Δpos=(-0,0088, 0,0221, -0,0090) |Δpos|=0,0255м Δrot=2,786°
  Muzzle world: pre pos=(1,2879, 1,2365, 49,0722) rot=(5,03, 297,98, 7,60)
    → post pos=(1,2823, 1,2597, 49,0692) rot=(4,99, 298,15, 4,83) | Δpos=(-0,0056, 0,0231, -0,0030) |Δpos|=0,0240м Δrot=2,786°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1340) | Δpos=(0,0000, 0,0000, -0,0119)
  cameraDist=5,6м nearDetail=True | firing=True | boltOwnsShell=True
[HeadSweep] SUMMARY unit=Unit(Clone) cancelled=False
  Standing/Idle/LowReady(LowReady): headPitch=-17,7° headYaw=0,6° headRoll=4,2°
  Standing/Idle/HighReady(HighReady): headPitch=-22,8° headYaw=-21,3° headRoll=-2,9°
  Standing/Idle/PreAim(PreAim): headPitch=-22,9° headYaw=-22,3° headRoll=-3,3°
  Standing/Idle/HipFire(HipFire) x1: headPitch=-6,1° headYaw=-16,2° headRoll=1,8°
  Standing/Idle/HipFire(HipFire) x3: headPitch=-6,1° headYaw=-15,9° headRoll=1,6°
  Standing/Idle/HipFire(HipFire) x10: headPitch=-6,0° headYaw=-15,4° headRoll=1,3°
  Standing/Idle/PointAim(PointAim) x1: headPitch=-23,7° headYaw=-22,3° headRoll=-3,5°
  Standing/Idle/PointAim(PointAim) x3: headPitch=-23,8° headYaw=-22,3° headRoll=-3,5°
  Standing/Idle/PointAim(PointAim) x10: headPitch=-23,8° headYaw=-22,3° headRoll=-3,5°
  Standing/Idle/Aiming(Aiming) x1: headPitch=-23,9° headYaw=-22,3° headRoll=-3,5°
  Standing/Idle/Aiming(Aiming) x3: headPitch=-23,7° headYaw=-22,3° headRoll=-3,5°
  Standing/Idle/Aiming(Aiming) x10: headPitch=-23,9° headYaw=-22,3° headRoll=-3,5°
  Standing/Walk/LowReady(LowReady): headPitch=-20,1° headYaw=-15,6° headRoll=14,4°
  Standing/Walk/HighReady(HighReady): headPitch=-2,2° headYaw=-19,2° headRoll=-0,4°
  Standing/Walk/PreAim(PreAim): headPitch=-19,7° headYaw=-36,6° headRoll=7,8°
  Standing/Walk/HipFire(HipFireWalk) x1: headPitch=-21,1° headYaw=-22,3° headRoll=12,5°
  Standing/Walk/HipFire(HipFireWalk) x3: headPitch=-21,5° headYaw=-21,3° headRoll=12,8°
  Standing/Walk/HipFire(HipFireWalk) x10: headPitch=-22,1° headYaw=-23,2° headRoll=12,6°
  Standing/Walk/PointAim(PointAim) x1: headPitch=-23,2° headYaw=-9,7° headRoll=-0,1°
  Standing/Walk/PointAim(PointAim) x3: headPitch=-24,0° headYaw=-13,4° headRoll=0,4°
  Standing/Walk/PointAim(PointAim) x10: headPitch=-25,2° headYaw=-7,4° headRoll=-1,6°
  Standing/Walk/Aiming(Aiming) x1: headPitch=-23,5° headYaw=-19,7° headRoll=-2,7°
  Standing/Walk/Aiming(Aiming) x3: headPitch=-24,2° headYaw=-16,2° headRoll=-4,5°
  Standing/Walk/Aiming(Aiming) x10: headPitch=-23,4° headYaw=-16,9° headRoll=-2,7°
  Crouch/Idle/LowReady(LowReady): headPitch=-22,0° headYaw=-4,9° headRoll=6,7°
  Crouch/Idle/HighReady(HighReady): headPitch=-19,3° headYaw=-15,4° headRoll=1,8°
  Crouch/Idle/PreAim(PreAim): headPitch=-19,2° headYaw=-16,2° headRoll=1,6°
  Crouch/Idle/HipFire(HipFire) x1: headPitch=-18,9° headYaw=-20,2° headRoll=-5,0°
  Crouch/Idle/HipFire(HipFire) x3: headPitch=-19,0° headYaw=-20,1° headRoll=-5,3°
  Crouch/Idle/HipFire(HipFire) x10: headPitch=-19,0° headYaw=-20,0° headRoll=-5,2°
  Crouch/Idle/PointAim(PointAim) x1: headPitch=-19,2° headYaw=-16,3° headRoll=1,6°
  Crouch/Idle/PointAim(PointAim) x3: headPitch=-19,0° headYaw=-16,3° headRoll=1,6°
  Crouch/Idle/PointAim(PointAim) x10: headPitch=-19,3° headYaw=-16,3° headRoll=1,6°
  Crouch/Idle/Aiming(Aiming) x1: headPitch=-19,3° headYaw=-16,3° headRoll=1,6°
  Crouch/Idle/Aiming(Aiming) x3: headPitch=-19,1° headYaw=-16,3° headRoll=1,6°
  Crouch/Idle/Aiming(Aiming) x10: headPitch=-19,2° headYaw=-16,3° headRoll=1,6°
  Crouch/Walk/LowReady(LowReady): headPitch=-34,6° headYaw=9,6° headRoll=6,9°
  Crouch/Walk/HighReady(HighReady): headPitch=-26,7° headYaw=-30,2° headRoll=-9,8°
  Crouch/Walk/PreAim(PreAim): headPitch=-34,6° headYaw=-8,4° headRoll=-3,2°
  Crouch/Walk/HipFire(HipFireCrouchWalk) x1: headPitch=-37,3° headYaw=0,8° headRoll=6,3°
  Crouch/Walk/HipFire(HipFireCrouchWalk) x3: headPitch=-37,7° headYaw=1,0° headRoll=6,7°
  Crouch/Walk/HipFire(HipFireCrouchWalk) x10: headPitch=-37,4° headYaw=2,7° headRoll=6,2°
  Crouch/Walk/PointAim(PointAim) x1: headPitch=-19,9° headYaw=11,0° headRoll=1,4°
  Crouch/Walk/PointAim(PointAim) x3: headPitch=-20,1° headYaw=14,2° headRoll=3,3°
  Crouch/Walk/PointAim(PointAim) x10: headPitch=-20,1° headYaw=14,5° headRoll=4,2°
  Crouch/Walk/Aiming(Aiming) x1: headPitch=-19,3° headYaw=19,4° headRoll=4,7°
  Crouch/Walk/Aiming(Aiming) x3: headPitch=-21,8° headYaw=-2,5° headRoll=-3,9°
  Crouch/Walk/Aiming(Aiming) x10: headPitch=-22,6° headYaw=-6,6° headRoll=-4,0°
  totals: cells=48 headSamples=48
[HeadSweep] DONE unit=Unit(Clone)
```

### 16.1 Наблюдения по прогону (сверка со СТАРЫМИ формулами, не с §6.4)

- Первый выстрел клетки: added x kickScale x ShotPitch = punchPitch: 0,600x1,20x3,75 = 2,700 (лог 2,699) стоя; 0,750 -> 3,374 в ходьбе; 0,570 -> 2,564 в приседе; 0,660 -> 2,969 присед+ход. Множители стойки/движения (UnitStanceCombatModifiers): стоя x1, ход x1,25, присед x0,95, присед-ход x1,10.
- penalty после 1-го выстрела чуть меньше added (0,57 при added 0,60): в том же кадре UnitWeaponRecoilController.Update (порядок 58, ПОСЛЕ выстрела) успевает восстановить ~3,5 x 0,7 x dt (~0,04 за кадр).
- Hand_R dRot ~= climbPitch + punchPitch (yaw даёт малый вклад): 2,699+0,002 ~= 2,70, лог 2,706.
- tau=0,140 с: ShotSmoothTime 0,08 x DecayWhileFiringMultiplier 1,75 (firing=True всю очередь).
- Climb растёт медленно: за 10 выстрелов penalty ~3,6 -> climbPitch ~0,09 (тогда кривая была EaseInOut 0..7° на диапазоне 0..60, почти нулевая в начале; сейчас 0/0.6/1.7/4.5).
- Bolt dZ меняется от выстрела к выстрелу (0,008..0,052 м): лог снимает затвор в конце кадра и попадает в разные фазы авто-цикла 0,085 с при интервале выстрелов ~0,105 с (600 RPM). Полный ход 0,08 м в лог не попадает.
- В клетке Standing/Walk/PointAim x10 выстрелы #2-#4 имеют большие дельты base-позы Hand_R (pos 0,2711 -> 0,4201 -> 0,7246 -> 0,9561): это НЕ recoil - юнит в движении доворачивался/переприцеливался (rootYaw менялся), базовая анимационная поза сместилась между выстрелами.
- applied=True, canApply=True, nearDetail=True во всех клетках: на время прогона IgnoreCameraDistanceCull=true.
- Строки [RecoilSweep] в прогоне отсутствуют: флаг m_LogRecoilSweep=false (калибровка отдачи завершена ранее).

---

## 17. Чек-лист диагностики типовых проблем

| Симптом | Где искать |
|---|---|
| Оружие не двигается при выстреле | `[WeaponVisDiag]` строка `applied=False`: проверить условия §14 (дистанция до камеры > 12 м — самая частая причина; `IsWeaponHeldForBoltCycle`; турель; тюнер; ragdoll) |
| Оружие «ныряет» между выстрелами очереди | `tau` в логе: если не умножается на 1.75 — `IsFiringCommandActive == false` (команда огня снята); проверить `m_DecayWhileFiringMultiplier` |
| Punch упирается в потолок | `punchPitch` держится ~`m_MaxShotImpulse × ShotPitch` (6 × 2.5 = 15° — в практике недостижимо, кап страховочный); проверить `RecoilAddedPerShot` (умножители скилла/состояния/стойки/обвеса) |
| Climb отсутствует при большом penalty | `climbPitch=0` при `penalty>0`: `m_PitchCurve`/`m_VisualOffsetScale`/`kickScale`; помнить, что climb считается от penalty, а penalty сбрасывается при `StopFiring` |
| Назад слабее/сильнее ожидаемого | `backOffset = impulse × m_BackScale(0.035) × m_HandBack`; `upOffset = impulse × m_UpScale(0.008) × m_HandUp` — импульс и масштабы в логе `visualState` |
| Числа back-first, глазами «ствол вверх» | Не крутить BackScale. Смотреть `backProj`/`upProj` на дуле (§15.5). `Hand_R Δpos` в осях кисти не критерий. Код: translation вдоль −FireOrigin.forward + Vector3.up |
| `backProj` сильно меньше `backOffset` | `FireOriginTransform` null (fallback на корень оружия), либо `hand.parent == null` (translation = 0), либо в логе старый кадр до parent-space |
| Гильза не вылетает | `Bolt local: Δpos=0` — нет `BoltCarrierTransform` на префабе; либо `WillHandlePhysicalShellEjection` и профиль Hybrid/Particle; `AmmoDefinition.HasShellPrefab` |
| Затвор не анимируется | `Bolt local Δpos=0` после выстрела: юнит дальше 12 м от камеры (`IsNearCameraForBoundWeapon`); `ResolveBoltCycleSecondsForShot` |
| Логи вообще не пишутся | флаг `m_LogWeaponShotPose` выключен на `UnitWeaponPoseSweepTest`, либо юнит не выделен/нет цели перед L; выстрелы блокируются гейтами (проверить `m_LastShotAttemptResult` в строках `[RecoilSweep] STALL`) |
| Калибровка не применилась на существующих юнитах | префаб хранит старые сериализованные значения: меню `Polygone/Shooting/Migrate Weapon Recoil Back-First Calibration` (`WeaponRecoilBackFirstMigration`) |

---

## 18. Прогоны после back-first калибровки (исторические, до правки пространства)

> Два частичных прогона после применения калибровки коэффициентов
> (ShotPitch 2.5, BackScale 0.035, UpScale 0.008, HandPitch 0.8, HandUp 0.75,
> MaxShotImpulse 6, MaxShotYawDegrees 6, PitchCurve 0/0.6/1.7/4.5).
> Translation в этих логах ещё шёл в локальных осях `Hand_R` (`baseRot * punchLocal`) —
> строк `backProj`/`upProj` нет. Полные 48-клеточные прогоны после правки пространства
> в документ не вложены; текущий код — §6.4 / §18.3.

### 18.1 Прогон А (АК, Ammo_545x39mm, FullAuto, kickScale=1,45) — сводка первых выстрелов

| Клетка | impulse | punchPitch | back | up | Δrot | back/up |
|---|---:|---:|---:|---:|---:|---:|
| Standing/Idle/HipFire x1 | 1,206 | 3,016° | 42 мм | 7,2 мм | 2,43° | 5,9:1 |
| Standing/Idle/PointAim x1 | 1,206 | 3,016° | 42 мм | 7,2 мм | 2,43° | 5,9:1 |
| Standing/Idle/Aiming x1 | 1,206 | 3,016° | 42 мм | 7,2 мм | 2,42° | 5,9:1 |

Сверка формул: impulse = 0,832 × 1,45 = 1,206 ✓; punchPitch = 1,206 × 2,5 = 3,016 ✓;
back = 1,206 × 0,035 = 0,0422 м ✓; up = 1,206 × 0,008 × 0,75 = 0,0072 м ✓.
Полные строки этого прогона — в Editor.log; здесь только сводка.

### 18.2 Прогон Б (Ammo_556x45mmNATO, SemiAuto/Burst, kickScale=1,55) — полные строки

```text
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x3 | t=10,738 | ammo=Ammo_556x45mmNATO | mode=SemiAuto
  recoil: added=0,313 | penalty 0,00→0,31/30,0 | kickScale=1,55
  visualState: impulse=0,486 | climbPitch=0,001° punchPitch=1,214° punchYaw=0,188° | back=0,0170м up=0,0029м active=True | tau=0,140с
  Hand_R local: base rot=(350,91, 14,25, 317,50) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(350,30, 15,04, 317,37) pos=(0,2688, -0,0006, -0,0167) | Δrot=0,986° Δpos=(-0,0023, -0,0006, -0,0171) |Δpos|=0,0172м | applied=True canApply=True
  WeaponRoot world: pre pos=(25,5466, 1,6585, 0,2218) rot=(0,23, 333,42, 2,71)
    → post pos=(25,5431, 1,6758, 0,2219) rot=(0,18, 333,53, 1,73) | Δpos=(-0,0035, 0,0173, 0,0001) |Δpos|=0,0177м Δrot=0,978°
  Muzzle world: pre pos=(25,1265, 1,7552, 1,0508) rot=(0,23, 333,42, 2,71)
    → post pos=(25,1261, 1,7734, 1,0524) rot=(0,18, 333,53, 1,73) | Δpos=(-0,0004, 0,0182, 0,0016) |Δpos|=0,0182м Δrot=0,978°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1460) | Δpos=(0,0000, 0,0000, 0,0000)
  cameraDist=12,5м nearDetail=False | firing=True | boltOwnsShell=False
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x3 | t=10,905 | ammo=Ammo_556x45mmNATO | mode=SemiAuto
  recoil: added=0,313 | penalty 0,00→0,27/30,0 | kickScale=1,55
  visualState: impulse=0,572 | climbPitch=0,001° punchPitch=1,429° punchYaw=0,095° | back=0,0200м up=0,0034м active=True | tau=0,140с
  Hand_R local: base rot=(350,72, 14,50, 317,45) pos=(0,2704, -0,0002, -0,0048)
    → final rot=(350,12, 15,11, 317,35) pos=(0,2684, -0,0007, -0,0197) | Δrot=0,850° Δpos=(-0,0020, -0,0005, -0,0149) |Δpos|=0,0151м | applied=True canApply=True
  WeaponRoot world: pre pos=(25,5455, 1,6637, 0,2220) rot=(0,22, 333,47, 2,43)
    → post pos=(25,5424, 1,6787, 0,2221) rot=(0,29, 333,57, 1,59) | Δpos=(-0,0031, 0,0150, 0,0001) |Δpos|=0,0153м Δrot=0,839°
  Muzzle world: pre pos=(25,1266, 1,7605, 1,0516) rot=(0,22, 333,47, 2,43)
    → post pos=(25,1262, 1,7745, 1,0532) rot=(0,29, 333,57, 1,59) | Δpos=(-0,0004, 0,0140, 0,0016) |Δpos|=0,0141м Δrot=0,839°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1460) | Δpos=(0,0000, 0,0000, 0,0000)
  cameraDist=12,5м nearDetail=False | firing=True | boltOwnsShell=False
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x10 | t=12,251 | ammo=Ammo_556x45mmNATO | mode=SemiAuto
  recoil: added=0,313 | penalty 0,00→0,31/30,0 | kickScale=1,55
  visualState: impulse=0,486 | climbPitch=0,001° punchPitch=1,214° punchYaw=0,180° | back=0,0170м up=0,0029м active=True | tau=0,140с
  Hand_R local: base rot=(350,86, 14,37, 317,38) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(350,25, 15,15, 317,25) pos=(0,2688, -0,0006, -0,0167) | Δrot=0,984° Δpos=(-0,0023, -0,0006, -0,0171) |Δpos|=0,0172м | applied=True canApply=True
  WeaponRoot world: pre pos=(25,5461, 1,6586, 0,2229) rot=(0,34, 333,61, 2,85)
    → post pos=(25,5425, 1,6759, 0,2230) rot=(0,30, 333,72, 1,87) | Δpos=(-0,0036, 0,0173, 0,0001) |Δpos|=0,0177м Δrot=0,976°
  Muzzle world: pre pos=(25,1285, 1,7535, 1,0534) rot=(0,34, 333,61, 2,85)
    → post pos=(25,1281, 1,7716, 1,0550) rot=(0,30, 333,72, 1,87) | Δpos=(-0,0004, 0,0180, 0,0016) |Δpos|=0,0181м Δrot=0,976°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1400) | Δpos=(0,0000, 0,0000, -0,0059)
  cameraDist=6,9м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x10 | t=12,417 | ammo=Ammo_556x45mmNATO | mode=SemiAuto
  recoil: added=0,313 | penalty 0,00→0,25/30,0 | kickScale=1,55
  visualState: impulse=0,551 | climbPitch=0,001° punchPitch=1,378° punchYaw=0,276° | back=0,0193м up=0,0033м active=True | tau=0,140с
  Hand_R local: base rot=(350,67, 14,62, 317,33) pos=(0,2704, -0,0002, -0,0048)
    → final rot=(350,20, 15,32, 317,21) pos=(0,2685, -0,0007, -0,0190) | Δrot=0,830° Δpos=(-0,0019, -0,0005, -0,0142) |Δpos|=0,0143м | applied=True canApply=True
  WeaponRoot world: pre pos=(25,5449, 1,6639, 0,2230) rot=(0,34, 333,67, 2,56)
    → post pos=(25,5420, 1,6784, 0,2232) rot=(0,24, 333,76, 1,75) | Δpos=(-0,0030, 0,0144, 0,0001) |Δpos|=0,0147м Δrot=0,816°
  Muzzle world: pre pos=(25,1286, 1,7589, 1,0542) rot=(0,34, 333,67, 2,56)
    → post pos=(25,1283, 1,7749, 1,0555) rot=(0,24, 333,76, 1,75) | Δpos=(-0,0003, 0,0160, 0,0013) |Δpos|=0,0161м Δrot=0,816°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1340) | Δpos=(0,0000, 0,0000, -0,0119)
  cameraDist=5,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x10 | t=12,593 | ammo=Ammo_556x45mmNATO | mode=SemiAuto
  recoil: added=0,313 | penalty 0,00→0,26/30,0 | kickScale=1,55
  visualState: impulse=0,595 | climbPitch=0,001° punchPitch=1,487° punchYaw=0,134° | back=0,0208м up=0,0036м active=True | tau=0,140с
  Hand_R local: base rot=(350,64, 14,70, 317,30) pos=(0,2703, -0,0002, -0,0060)
    → final rot=(350,05, 15,30, 317,20) pos=(0,2683, -0,0007, -0,0205) | Δrot=0,831° Δpos=(-0,0020, -0,0005, -0,0146) |Δpos|=0,0147м | applied=True canApply=True
  WeaponRoot world: pre pos=(25,5446, 1,6651, 0,2232) rot=(0,33, 333,70, 2,51)
    → post pos=(25,5416, 1,6797, 0,2233) rot=(0,38, 333,79, 1,69) | Δpos=(-0,0031, 0,0146, 0,0001) |Δpos|=0,0149м Δrot=0,821°
  Muzzle world: pre pos=(25,1288, 1,7602, 1,0546) rot=(0,33, 333,70, 2,51)
    → post pos=(25,1284, 1,7739, 1,0561) rot=(0,38, 333,79, 1,69) | Δpos=(-0,0004, 0,0137, 0,0015) |Δpos|=0,0138м Δrot=0,821°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1379) | Δpos=(0,0000, 0,0000, -0,0081)
  cameraDist=3,8м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #4 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x10 | t=12,766 | ammo=Ammo_556x45mmNATO | mode=SemiAuto
  recoil: added=0,313 | penalty 0,00→0,24/30,0 | kickScale=1,55
  visualState: impulse=0,575 | climbPitch=0,001° punchPitch=1,437° punchYaw=0,120° | back=0,0201м up=0,0034м active=True | tau=0,140с
  Hand_R local: base rot=(350,59, 14,70, 317,29) pos=(0,2702, -0,0002, -0,0064)
    → final rot=(350,07, 15,28, 317,19) pos=(0,2684, -0,0007, -0,0198) | Δrot=0,767° Δpos=(-0,0018, -0,0005, -0,0134) |Δpos|=0,0135м | applied=True canApply=True
  WeaponRoot world: pre pos=(25,5445, 1,6655, 0,2233) rot=(0,38, 333,72, 2,50)
    → post pos=(25,5416, 1,6790, 0,2234) rot=(0,41, 333,81, 1,75) | Δpos=(-0,0028, 0,0135, 0,0001) |Δpos|=0,0138м Δrot=0,753°
  Muzzle world: pre pos=(25,1290, 1,7597, 1,0550) rot=(0,38, 333,72, 2,50)
    → post pos=(25,1286, 1,7729, 1,0564) rot=(0,41, 333,81, 1,75) | Δpos=(-0,0003, 0,0131, 0,0014) |Δpos|=0,0132м Δrot=0,753°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1295) | Δpos=(0,0000, 0,0000, -0,0164)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #5 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x10 | t=12,948 | ammo=Ammo_556x45mmNATO | mode=SemiAuto
  recoil: added=0,313 | penalty 0,00→0,26/30,0 | kickScale=1,55
  visualState: impulse=0,602 | climbPitch=0,001° punchPitch=1,505° punchYaw=0,126° | back=0,0211м up=0,0036м active=True | tau=0,140с
  Hand_R local: base rot=(350,59, 14,70, 317,28) pos=(0,2702, -0,0002, -0,0061)
    → final rot=(350,03, 15,33, 317,17) pos=(0,2682, -0,0007, -0,0208) | Δrot=0,838° Δpos=(-0,0020, -0,0005, -0,0147) |Δpos|=0,0148м | applied=True canApply=True
  WeaponRoot world: pre pos=(25,5445, 1,6652, 0,2234) rot=(0,40, 333,75, 2,54)
    → post pos=(25,5414, 1,6800, 0,2236) rot=(0,42, 333,84, 1,71) | Δpos=(-0,0031, 0,0148, 0,0001) |Δpos|=0,0151м Δrot=0,826°
  Muzzle world: pre pos=(25,1293, 1,7592, 1,0553) rot=(0,40, 333,75, 2,54)
    → post pos=(25,1288, 1,7736, 1,0568) rot=(0,42, 333,84, 1,71) | Δpos=(-0,0004, 0,0144, 0,0015) |Δpos|=0,0145м Δrot=0,826°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1385) | Δpos=(0,0000, 0,0000, -0,0075)
  cameraDist=3,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #6 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x10 | t=13,114 | ammo=Ammo_556x45mmNATO | mode=SemiAuto
  recoil: added=0,313 | penalty 0,00→0,25/30,0 | kickScale=1,55
  visualState: impulse=0,600 | climbPitch=0,001° punchPitch=1,501° punchYaw=0,180° | back=0,0210м up=0,0036м active=True | tau=0,140с
  Hand_R local: base rot=(350,56, 14,73, 317,28) pos=(0,2702, -0,0002, -0,0068)
    → final rot=(350,06, 15,36, 317,17) pos=(0,2682, -0,0007, -0,0207) | Δrot=0,800° Δpos=(-0,0019, -0,0005, -0,0139) |Δpos|=0,0141м | applied=True canApply=True
  WeaponRoot world: pre pos=(25,5443, 1,6659, 0,2234) rot=(0,40, 333,75, 2,50)
    → post pos=(25,5414, 1,6800, 0,2235) rot=(0,37, 333,83, 1,70) | Δpos=(-0,0029, 0,0141, 0,0001) |Δpos|=0,0144м Δrot=0,798°
  Muzzle world: pre pos=(25,1292, 1,7599, 1,0553) rot=(0,40, 333,75, 2,50)
    → post pos=(25,1288, 1,7745, 1,0566) rot=(0,37, 333,83, 1,70) | Δpos=(-0,0004, 0,0146, 0,0013) |Δpos|=0,0146м Δrot=0,798°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1342) | Δpos=(0,0000, 0,0000, -0,0118)
  cameraDist=3,4м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #7 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x10 | t=13,296 | ammo=Ammo_556x45mmNATO | mode=SemiAuto
  recoil: added=0,313 | penalty 0,00→0,26/30,0 | kickScale=1,55
  visualState: impulse=0,597 | climbPitch=0,001° punchPitch=1,492° punchYaw=0,226° | back=0,0209м up=0,0036м active=True | tau=0,140с
  Hand_R local: base rot=(350,60, 14,70, 317,29) pos=(0,2702, -0,0002, -0,0062)
    → final rot=(350,10, 15,37, 317,18) pos=(0,2683, -0,0007, -0,0206) | Δrot=0,831° Δpos=(-0,0020, -0,0005, -0,0144) |Δpos|=0,0145м | applied=True canApply=True
  WeaponRoot world: pre pos=(25,5445, 1,6653, 0,2233) rot=(0,37, 333,72, 2,52)
    → post pos=(25,5415, 1,6799, 0,2234) rot=(0,32, 333,81, 1,69) | Δpos=(-0,0030, 0,0146, 0,0001) |Δpos|=0,0149м Δrot=0,823°
  Muzzle world: pre pos=(25,1290, 1,7597, 1,0549) rot=(0,37, 333,72, 2,52)
    → post pos=(25,1286, 1,7752, 1,0562) rot=(0,32, 333,81, 1,69) | Δpos=(-0,0004, 0,0155, 0,0013) |Δpos|=0,0155м Δrot=0,823°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1369) | Δpos=(0,0000, 0,0000, -0,0091)
  cameraDist=2,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #8 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x10 | t=13,476 | ammo=Ammo_556x45mmNATO | mode=SemiAuto
  recoil: added=0,313 | penalty 0,00→0,25/30,0 | kickScale=1,55
  visualState: impulse=0,589 | climbPitch=0,001° punchPitch=1,472° punchYaw=0,237° | back=0,0206м up=0,0035м active=True | tau=0,140с
  Hand_R local: base rot=(350,62, 14,69, 317,31) pos=(0,2702, -0,0002, -0,0062)
    → final rot=(350,12, 15,35, 317,20) pos=(0,2683, -0,0007, -0,0203) | Δrot=0,814° Δpos=(-0,0019, -0,0005, -0,0141) |Δpos|=0,0143м | applied=True canApply=True
  WeaponRoot world: pre pos=(25,5446, 1,6653, 0,2232) rot=(0,34, 333,70, 2,50)
    → post pos=(25,5416, 1,6796, 0,2233) rot=(0,29, 333,78, 1,69) | Δpos=(-0,0030, 0,0143, 0,0001) |Δpos|=0,0146м Δrot=0,807°
  Muzzle world: pre pos=(25,1287, 1,7601, 1,0546) rot=(0,34, 333,70, 2,50)
    → post pos=(25,1283, 1,7753, 1,0558) rot=(0,29, 333,78, 1,69) | Δpos=(-0,0004, 0,0152, 0,0013) |Δpos|=0,0152м Δrot=0,807°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1350) | Δpos=(0,0000, 0,0000, -0,0110)
  cameraDist=2,5м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #9 | unit=Unit(Clone) | cell=Standing/Idle/HipFire(HipFire) x10 | t=13,649 | ammo=Ammo_556x45mmNATO | mode=SemiAuto
  recoil: added=0,313 | penalty 0,00→0,26/30,0 | kickScale=1,55
  visualState: impulse=0,599 | climbPitch=0,001° punchPitch=1,499° punchYaw=0,163° | back=0,0210м up=0,0036м active=True | tau=0,140с
  Hand_R local: base rot=(350,61, 14,70, 317,32) pos=(0,2702, -0,0002, -0,0065)
    → final rot=(350,07, 15,30, 317,22) pos=(0,2683, -0,0007, -0,0207) | Δrot=0,809° Δpos=(-0,0019, -0,0005, -0,0142) |Δpos|=0,0144м | applied=True canApply=True
  WeaponRoot world: pre pos=(25,5446, 1,6656, 0,2230) rot=(0,33, 333,68, 2,47)
    → post pos=(25,5416, 1,6799, 0,2231) rot=(0,35, 333,76, 1,66) | Δpos=(-0,0030, 0,0143, 0,0001) |Δpos|=0,0146м Δrot=0,798°
  Muzzle world: pre pos=(25,1285, 1,7607, 1,0543) rot=(0,33, 333,68, 2,47)
    → post pos=(25,1281, 1,7748, 1,0557) rot=(0,35, 333,76, 1,66) | Δpos=(-0,0004, 0,0140, 0,0014) |Δpos|=0,0141м Δrot=0,798°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1359) | Δpos=(0,0000, 0,0000, -0,0100)
  cameraDist=1,8м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x3 | t=17,264 | ammo=Ammo_556x45mmNATO | mode=SemiAuto
  recoil: added=0,313 | penalty 0,00→0,31/30,0 | kickScale=1,55
  visualState: impulse=0,486 | climbPitch=0,001° punchPitch=1,214° punchYaw=0,176° | back=0,0170м up=0,0029м active=True | tau=0,140с
  Hand_R local: base rot=(16,65, 287,89, 6,92) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(15,67, 287,92, 6,93) pos=(0,2857, 0,0076, -0,0047) | Δrot=0,984° Δpos=(0,0146, 0,0076, -0,0051) |Δpos|=0,0172м | applied=True canApply=True
  WeaponRoot world: pre pos=(25,3538, 1,9119, 0,4241) rot=(0,61, 333,25, 37,31)
    → post pos=(25,3420, 1,9244, 0,4201) rot=(0,52, 333,32, 36,33) | Δpos=(-0,0118, 0,0125, -0,0040) |Δpos|=0,0177м Δrot=0,980°
  Muzzle world: pre pos=(24,8809, 1,9820, 1,2269) rot=(0,61, 333,25, 37,31)
    → post pos=(24,8713, 1,9970, 1,2240) rot=(0,52, 333,32, 36,33) | Δpos=(-0,0096, 0,0150, -0,0029) |Δpos|=0,0180м Δrot=0,980°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1414) | Δpos=(0,0000, 0,0000, -0,0046)
  cameraDist=0,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x3 | t=17,739 | ammo=Ammo_556x45mmNATO | mode=SemiAuto
  recoil: added=0,313 | penalty 0,00→0,27/30,0 | kickScale=1,55
  visualState: impulse=0,459 | climbPitch=0,001° punchPitch=1,147° punchYaw=0,166° | back=0,0161м up=0,0028м active=True | tau=0,140с
  Hand_R local: base rot=(16,62, 287,89, 6,92) pos=(0,2716, 0,0003, 0,0002)
    → final rot=(15,72, 287,92, 6,93) pos=(0,2849, 0,0072, -0,0044) | Δrot=0,895° Δpos=(0,0133, 0,0070, -0,0046) |Δpos|=0,0157м | applied=True canApply=True
  WeaponRoot world: pre pos=(25,3535, 1,9118, 0,4242) rot=(0,66, 333,26, 37,25)
    → post pos=(25,3427, 1,9232, 0,4205) rot=(0,58, 333,32, 36,36) | Δpos=(-0,0108, 0,0114, -0,0036) |Δpos|=0,0161м Δrot=0,890°
  Muzzle world: pre pos=(24,8807, 1,9811, 1,2272) rot=(0,66, 333,26, 37,25)
    → post pos=(24,8719, 1,9948, 1,2245) rot=(0,58, 333,32, 36,36) | Δpos=(-0,0088, 0,0136, -0,0027) |Δpos|=0,0164м Δrot=0,890°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1408) | Δpos=(0,0000, 0,0000, -0,0052)
  cameraDist=0,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x10 | t=19,513 | ammo=Ammo_556x45mmNATO | mode=SemiAuto
  recoil: added=0,313 | penalty 0,00→0,31/30,0 | kickScale=1,55
  visualState: impulse=0,486 | climbPitch=0,001° punchPitch=1,214° punchYaw=0,155° | back=0,0170м up=0,0029м active=True | tau=0,140с
  Hand_R local: base rot=(16,65, 287,89, 6,92) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(15,67, 287,90, 6,93) pos=(0,2857, 0,0076, -0,0047) | Δrot=0,980° Δpos=(0,0146, 0,0076, -0,0051) |Δpos|=0,0172м | applied=True canApply=True
  WeaponRoot world: pre pos=(25,3539, 1,9121, 0,4241) rot=(0,58, 333,25, 37,32)
    → post pos=(25,3421, 1,9246, 0,4201) rot=(0,50, 333,33, 36,34) | Δpos=(-0,0118, 0,0125, -0,0040) |Δpos|=0,0177м Δrot=0,975°
  Muzzle world: pre pos=(24,8810, 1,9827, 1,2269) rot=(0,58, 333,25, 37,32)
    → post pos=(24,8715, 1,9975, 1,2241) rot=(0,50, 333,33, 36,34) | Δpos=(-0,0095, 0,0148, -0,0029) |Δpos|=0,0178м Δrot=0,975°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1409) | Δpos=(0,0000, 0,0000, -0,0050)
  cameraDist=0,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #2 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x10 | t=19,978 | ammo=Ammo_556x45mmNATO | mode=SemiAuto
  recoil: added=0,313 | penalty 0,00→0,26/30,0 | kickScale=1,55
  visualState: impulse=0,446 | climbPitch=0,001° punchPitch=1,116° punchYaw=0,172° | back=0,0156м up=0,0027м active=True | tau=0,140с
  Hand_R local: base rot=(16,62, 287,89, 6,92) pos=(0,2717, 0,0003, 0,0002)
    → final rot=(15,75, 287,93, 6,93) pos=(0,2846, 0,0070, -0,0043) | Δrot=0,870° Δpos=(0,0129, 0,0068, -0,0045) |Δpos|=0,0152м | applied=True canApply=True
  WeaponRoot world: pre pos=(25,3534, 1,9131, 0,4237) rot=(0,52, 333,25, 37,30)
    → post pos=(25,3430, 1,9241, 0,4202) rot=(0,43, 333,31, 36,44) | Δpos=(-0,0105, 0,0110, -0,0036) |Δpos|=0,0156м Δrot=0,865°
  Muzzle world: pre pos=(24,8806, 1,9846, 1,2265) rot=(0,52, 333,25, 37,30)
    → post pos=(24,8721, 1,9980, 1,2238) rot=(0,43, 333,31, 36,44) | Δpos=(-0,0086, 0,0134, -0,0027) |Δpos|=0,0162м Δrot=0,865°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1370) | Δpos=(0,0000, 0,0000, -0,0090)
  cameraDist=0,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #3 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x10 | t=20,450 | ammo=Ammo_556x45mmNATO | mode=SemiAuto
  recoil: added=0,313 | penalty 0,00→0,27/30,0 | kickScale=1,55
  visualState: impulse=0,454 | climbPitch=0,001° punchPitch=1,135° punchYaw=0,060° | back=0,0159м up=0,0027м active=True | tau=0,140с
  Hand_R local: base rot=(16,62, 287,89, 6,92) pos=(0,2717, 0,0003, 0,0002)
    → final rot=(15,74, 287,83, 6,90) pos=(0,2848, 0,0071, -0,0044) | Δrot=0,874° Δpos=(0,0131, 0,0069, -0,0046) |Δpos|=0,0155м | applied=True canApply=True
  WeaponRoot world: pre pos=(25,3534, 1,9131, 0,4237) rot=(0,52, 333,25, 37,30)
    → post pos=(25,3428, 1,9242, 0,4201) rot=(0,51, 333,36, 36,43) | Δpos=(-0,0106, 0,0111, -0,0036) |Δpos|=0,0157м Δrot=0,866°
  Muzzle world: pre pos=(24,8806, 1,9847, 1,2264) rot=(0,52, 333,25, 37,30)
    → post pos=(24,8727, 1,9968, 1,2243) rot=(0,51, 333,36, 36,43) | Δpos=(-0,0079, 0,0122, -0,0021) |Δpos|=0,0147м Δrot=0,866°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1392) | Δpos=(0,0000, 0,0000, -0,0068)
  cameraDist=0,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #4 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x10 | t=20,920 | ammo=Ammo_556x45mmNATO | mode=SemiAuto
  recoil: added=0,313 | penalty 0,00→0,27/30,0 | kickScale=1,55
  visualState: impulse=0,458 | climbPitch=0,001° punchPitch=1,145° punchYaw=0,235° | back=0,0160м up=0,0027м active=True | tau=0,140с
  Hand_R local: base rot=(16,62, 287,88, 6,92) pos=(0,2717, 0,0003, 0,0002)
    → final rot=(15,72, 287,98, 6,95) pos=(0,2849, 0,0072, -0,0044) | Δrot=0,904° Δpos=(0,0132, 0,0069, -0,0046) |Δpos|=0,0156м | applied=True canApply=True
  WeaponRoot world: pre pos=(25,3534, 1,9127, 0,4238) rot=(0,57, 333,25, 37,28)
    → post pos=(25,3426, 1,9241, 0,4201) rot=(0,44, 333,28, 36,39) | Δpos=(-0,0108, 0,0114, -0,0037) |Δpos|=0,0161м Δrot=0,899°
  Muzzle world: pre pos=(24,8806, 1,9833, 1,2267) rot=(0,57, 333,25, 37,28)
    → post pos=(24,8714, 1,9978, 1,2236) rot=(0,44, 333,28, 36,39) | Δpos=(-0,0092, 0,0145, -0,0030) |Δpos|=0,0174м Δrot=0,899°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1403) | Δpos=(0,0000, 0,0000, -0,0057)
  cameraDist=0,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #5 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x10 | t=21,396 | ammo=Ammo_556x45mmNATO | mode=SemiAuto
  recoil: added=0,313 | penalty 0,00→0,27/30,0 | kickScale=1,55
  visualState: impulse=0,455 | climbPitch=0,001° punchPitch=1,138° punchYaw=0,108° | back=0,0159м up=0,0027м active=True | tau=0,140с
  Hand_R local: base rot=(16,62, 287,89, 6,92) pos=(0,2716, 0,0003, 0,0002)
    → final rot=(15,74, 287,87, 6,92) pos=(0,2848, 0,0072, -0,0044) | Δrot=0,881° Δpos=(0,0132, 0,0069, -0,0046) |Δpos|=0,0156м | applied=True canApply=True
  WeaponRoot world: pre pos=(25,3534, 1,9122, 0,4240) rot=(0,62, 333,25, 37,26)
    → post pos=(25,3428, 1,9234, 0,4204) rot=(0,58, 333,34, 36,39) | Δpos=(-0,0107, 0,0112, -0,0036) |Δpos|=0,0159м Δrot=0,875°
  Muzzle world: pre pos=(24,8806, 1,9820, 1,2270) rot=(0,62, 333,25, 37,26)
    → post pos=(24,8723, 1,9948, 1,2246) rot=(0,58, 333,34, 36,39) | Δpos=(-0,0083, 0,0128, -0,0023) |Δpos|=0,0154м Δrot=0,875°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1398) | Δpos=(0,0000, 0,0000, -0,0062)
  cameraDist=0,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #6 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x10 | t=21,861 | ammo=Ammo_556x45mmNATO | mode=SemiAuto
  recoil: added=0,313 | penalty 0,00→0,26/30,0 | kickScale=1,55
  visualState: impulse=0,449 | climbPitch=0,001° punchPitch=1,122° punchYaw=0,093° | back=0,0157м up=0,0027м active=True | tau=0,140с
  Hand_R local: base rot=(16,62, 287,89, 6,92) pos=(0,2717, 0,0003, 0,0002)
    → final rot=(15,75, 287,86, 6,91) pos=(0,2846, 0,0071, -0,0043) | Δrot=0,865° Δpos=(0,0130, 0,0068, -0,0045) |Δpos|=0,0153м | applied=True canApply=True
  WeaponRoot world: pre pos=(25,3534, 1,9117, 0,4242) rot=(0,68, 333,26, 37,24)
    → post pos=(25,3430, 1,9227, 0,4207) rot=(0,65, 333,35, 36,39) | Δpos=(-0,0105, 0,0110, -0,0035) |Δpos|=0,0156м Δrot=0,856°
  Muzzle world: pre pos=(24,8807, 1,9807, 1,2273) rot=(0,68, 333,26, 37,24)
    → post pos=(24,8726, 1,9931, 1,2251) rot=(0,65, 333,35, 36,39) | Δpos=(-0,0081, 0,0124, -0,0022) |Δpos|=0,0150м Δrot=0,856°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1376) | Δpos=(0,0000, 0,0000, -0,0084)
  cameraDist=0,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #7 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x10 | t=22,339 | ammo=Ammo_556x45mmNATO | mode=SemiAuto
  recoil: added=0,313 | penalty 0,00→0,25/30,0 | kickScale=1,55
  visualState: impulse=0,442 | climbPitch=0,001° punchPitch=1,104° punchYaw=0,213° | back=0,0155м up=0,0027м active=True | tau=0,140с
  Hand_R local: base rot=(16,62, 287,89, 6,92) pos=(0,2716, 0,0003, 0,0002)
    → final rot=(15,75, 287,96, 6,94) pos=(0,2844, 0,0070, -0,0042) | Δrot=0,869° Δpos=(0,0128, 0,0067, -0,0044) |Δpos|=0,0151м | applied=True canApply=True
  WeaponRoot world: pre pos=(25,3535, 1,9114, 0,4244) rot=(0,70, 333,26, 37,24)
    → post pos=(25,3431, 1,9224, 0,4209) rot=(0,58, 333,29, 36,38) | Δpos=(-0,0104, 0,0110, -0,0035) |Δpos|=0,0155м Δrot=0,862°
  Muzzle world: pre pos=(24,8807, 1,9801, 1,2275) rot=(0,70, 333,26, 37,24)
    → post pos=(24,8719, 1,9939, 1,2246) rot=(0,58, 333,29, 36,38) | Δpos=(-0,0088, 0,0139, -0,0028) |Δpos|=0,0167м Δrot=0,862°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1358) | Δpos=(0,0000, 0,0000, -0,0102)
  cameraDist=0,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #8 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x10 | t=22,808 | ammo=Ammo_556x45mmNATO | mode=SemiAuto
  recoil: added=0,313 | penalty 0,00→0,26/30,0 | kickScale=1,55
  visualState: impulse=0,448 | climbPitch=0,001° punchPitch=1,120° punchYaw=0,195° | back=0,0157м up=0,0027м active=True | tau=0,140с
  Hand_R local: base rot=(16,62, 287,89, 6,92) pos=(0,2717, 0,0003, 0,0002)
    → final rot=(15,74, 287,95, 6,94) pos=(0,2846, 0,0071, -0,0043) | Δrot=0,876° Δpos=(0,0129, 0,0068, -0,0045) |Δpos|=0,0153м | applied=True canApply=True
  WeaponRoot world: pre pos=(25,3535, 1,9119, 0,4243) rot=(0,64, 333,26, 37,26)
    → post pos=(25,3430, 1,9230, 0,4207) rot=(0,54, 333,30, 36,39) | Δpos=(-0,0105, 0,0111, -0,0036) |Δpos|=0,0157м Δrot=0,863°
  Muzzle world: pre pos=(24,8807, 1,9814, 1,2273) rot=(0,64, 333,26, 37,26)
    → post pos=(24,8720, 1,9951, 1,2245) rot=(0,54, 333,30, 36,39) | Δpos=(-0,0087, 0,0137, -0,0028) |Δpos|=0,0165м Δrot=0,863°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1375) | Δpos=(0,0000, 0,0000, -0,0084)
  cameraDist=0,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #9 | unit=Unit(Clone) | cell=Standing/Idle/PointAim(PointAim) x10 | t=23,278 | ammo=Ammo_556x45mmNATO | mode=SemiAuto
  recoil: added=0,313 | penalty 0,00→0,27/30,0 | kickScale=1,55
  visualState: impulse=0,456 | climbPitch=0,001° punchPitch=1,140° punchYaw=0,147° | back=0,0160м up=0,0027м active=True | tau=0,140с
  Hand_R local: base rot=(16,62, 287,89, 6,92) pos=(0,2717, 0,0003, 0,0002)
    → final rot=(15,73, 287,90, 6,93) pos=(0,2848, 0,0072, -0,0044) | Δrot=0,885° Δpos=(0,0132, 0,0069, -0,0046) |Δpos|=0,0156м | applied=True canApply=True
  WeaponRoot world: pre pos=(25,3535, 1,9124, 0,4241) rot=(0,59, 333,25, 37,27)
    → post pos=(25,3428, 1,9236, 0,4204) rot=(0,52, 333,32, 36,40) | Δpos=(-0,0107, 0,0113, -0,0036) |Δpos|=0,0159м Δrot=0,877°
  Muzzle world: pre pos=(24,8807, 1,9828, 1,2270) rot=(0,59, 333,25, 37,27)
    → post pos=(24,8721, 1,9961, 1,2244) rot=(0,52, 333,32, 36,40) | Δpos=(-0,0085, 0,0133, -0,0026) |Δpos|=0,0161м Δrot=0,877°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1397) | Δpos=(0,0000, 0,0000, -0,0063)
  cameraDist=0,7м nearDetail=True | firing=True | boltOwnsShell=True
[WeaponVisDiag] ВЫСТРЕЛ #1 | unit=Unit(Clone) | cell=Standing/Idle/Aiming(Aiming) x1 | t=25,860 | ammo=Ammo_556x45mmNATO | mode=Burst
  recoil: added=0,392 | penalty 0,00→0,35/30,0 | kickScale=1,55
  visualState: impulse=0,607 | climbPitch=0,001° punchPitch=1,518° punchYaw=0,160° | back=0,0213м up=0,0036м active=True | tau=0,140с
  Hand_R local: base rot=(346,78, 295,78, 348,24) pos=(0,2711, 0,0000, 0,0004)
    → final rot=(345,62, 296,17, 348,15) pos=(0,2908, -0,0014, -0,0083) | Δrot=1,222° Δpos=(0,0197, -0,0014, -0,0087) |Δpos|=0,0216м | applied=True canApply=True
  WeaponRoot world: pre pos=(25,3153, 1,9116, 0,4014) rot=(0,18, 332,95, 359,53)
    → post pos=(25,3120, 1,9334, 0,4020) rot=(0,20, 333,08, 358,32) | Δpos=(-0,0033, 0,0217, 0,0006) |Δpos|=0,0220м Δrot=1,216°
  Muzzle world: pre pos=(24,8934, 2,0092, 1,2293) rot=(0,18, 332,95, 359,53)
    → post pos=(24,8939, 2,0307, 1,2320) rot=(0,20, 333,08, 358,32) | Δpos=(0,0005, 0,0215, 0,0026) |Δpos|=0,0217м Δrot=1,216°
  Bolt local: pre pos=(0,0065, 0,1070, 0,1460) → post pos=(0,0065, 0,1070, 0,1287) | Δpos=(0,0000, 0,0000, -0,0173)
  cameraDist=0,8м nearDetail=True | firing=True | boltOwnsShell=True
```

### 18.3 Что показали эти логи и что сейчас в коде

Прогоны А и Б подтвердили **калибровку коэффициентов**: `visualState` уже back-first
(back/up ≈ 5,9:1, punchPitch 1,1–3,0°). Но визуально отдача в тех прогонах читалась как
«ствол вверх», потому что translation ещё применялся в осях кости `Hand_R`:

```csharp
// ИСТОРИЧЕСКИ, прогоны А/Б — этого пути в коде больше нет
Vector3 punchLocal = (0, upOffset, -backOffset); // оси Hand_R
finalPos = basePos + baseRot * punchLocal;
```

Оси кисти не совпадают с продольной осью оружия, поэтому вектор «назад» частично уходил
вверх/вбок. Подтверждение из прогона Б (Aiming x1): `back=0,0213м up=0,0036м`, но
`Hand_R Δpos = (0,0197, -0,0014, -0,0087)` — это local кисти, не «назад вдоль ствола».
`Muzzle Δpos` в той же строке почти вертикальный `(0,0005, 0,0215, 0,0026)`.

**Сейчас в коде (live, 18.08.2026):**

```text
ShotFired
  ├─ UnitWeaponRecoilController → RecoilPenalty (геймплей, spread)
  └─ UnitWeaponRecoil
        impulse = RecoilAddedPerShot × VisualRecoilKickScale
           ├─ punchPitch = impulse × ShotPitch          (вращение, вторичное)
           ├─ punchYaw   = Perlin + YawBias             (вращение, малая вариация)
           ├─ backOffset = impulse × BackScale × HandBack   (главный сдвиг)
           ├─ upOffset   = impulse × UpScale × HandUp       (вторичный сдвиг)
           └─ climbPitch = PitchCurve(RecoilPenalty) × scale  (только вращение)
                │
                ▼
        WeaponVisualRecoilApplicator (200) → Hand_R
           finalRot = animBaseRot × Euler(-(climb+punchPitch)×HandPitch, punchYaw×HandYaw, 0)
           worldDelta = Vector3.up × upOffset − FireOrigin.forward × backOffset
           finalPos = animBasePos + Hand_R.parent.InverseTransformVector(worldDelta)
                │
                ▼
           Equipped_* (ребёнок Hand_R, local = BASE слота)
                │
                ▼
           AnimatorHandIk (250) — снап только левой кисти к грипу
```

`BuildHandLocalPunch()` удалён. `FireOriginTransform == null` или `hand.parent == null` →
translation в этом кадре ноль, вращение работает как раньше.

Как читать следующий L-прогон (фильтр `WeaponVisDiag`):
- `visualState.back/up` не должны измениться от правки пространства (расчёт state тот же);
- на строке дула смотреть `backProj` / `upProj`, не сырой `|Δpos|`: ожидается
  `backProj ≈ backOffset`, `upProj ≈ upOffset`;
- `Δrot` не должен измениться (rotation-канал не трогали);
- `Hand_R Δpos` в local-осях кисти по-прежнему не обязан быть `(0, up, −back)`.

Критерий приёмки глазами: оружие толкается **назад вдоль ствола**, чуть вверх;
ствол не «задирается» одним только pitch. Очередь: back импульсный, без накопления смещения
(climb — только угол).

Резерв (только если после правки пространства вращение всё ещё мешает глазу):
`m_ShotPitch` 2.5 → 1.2–1.5, `m_HandPitch` 0.8 → 0.5–0.6. `BackScale` не поднимать —
соотношение ~6:1 уже корректно, усиление back лечило бы не ту проблему.

Примечание к прогону Б: клетки x1 (HipFire x1, PointAim x1) и Aiming x3 в захвате
не содержат строк «ВЫСТРЕЛ» — выстрелы в них не состоялись (режим SemiAuto/Burst,
Sweep переключал режим огня; полные причины — в строках [RecoilSweep] STALL в Editor.log).
