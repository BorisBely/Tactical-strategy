using UnityEngine;

/// <summary>
/// Near-camera процедурный цикл затвора/слайда и dust cover.
/// Работает только если на <see cref="EquippedWeapon"/> явно задан BoltCarrier / DustCoverHinge.
/// Автоматы: цикл при выстреле. Болтовые (<see cref="WeaponDefinition.RequiresManualBoltCycle"/>):
/// при выстреле гильза откладывается, цикл — на <see cref="UnitWeaponReloadController.BoltMotionPresented"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(56)]
public sealed class UnitWeaponBoltCycleVisual : MonoBehaviour
{
	#region Constants
	private const float c_IdleEpsilon = 0.0005f;
	#endregion

	#region Nested Types
	private enum BoltMotionMode
	{
		None = 0,
		/// <summary>Выстрел / передёргивание: rest → open → rest.</summary>
		FullCycle = 1,
		/// <summary>Отпускание bolt catch: open → rest, затем закрытие dust cover.</summary>
		CloseFromOpen = 2,
		/// <summary>Болтовая рукоятка: rotate → slide open → eject → slide close → rotate close.</summary>
		BoltActionHandleCycle = 3,
	}
	#endregion

	#region Serialized Fields
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitWeaponReloadController m_ReloadController;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private UnitWeaponShellEjection m_ShellEjection;
	[SerializeField] private UnitRagdollController m_RagdollController;
	#endregion

	#region Private Fields
	private EquippedWeapon m_BoundWeapon;
	private Transform m_BoltCarrier;
	private Vector3 m_BoltRestLocalPosition;
	private Quaternion m_BoltRestLocalRotation = Quaternion.identity;
	private Vector3 m_BoltOpenLocalOffset;
	private Vector3 m_BoltHandleOpenLocalEulerAngles;
	private float m_BoltHandleRotatePhaseNormalized = 0.25f;
	private float m_BoltCycleSecondsAuto = 0.085f;
	private float m_BoltCycleSecondsSingleShot = 0.16f;
	private float m_BoltActionCycleSeconds = 0.55f;
	private float m_BoltShellEjectNormalizedTime = 0.5f;
	private float m_ActiveBoltCycleSeconds = 0.16f;

	private Transform m_DustCoverHinge;
	private float m_DustCoverClosedDegrees = 105f;
	private Vector3 m_DustCoverHingeAxis = Vector3.forward;
	private float m_DustCoverTweenSeconds = 0.12f;

	private BoltMotionMode m_BoltMotionMode;
	private bool m_BoltHoldOpen;
	private bool m_CloseDustCoverAfterBoltClose;
	private bool m_ShellEjectedThisCycle;
	private float m_BoltCycleElapsed;
	private AmmoDefinition m_PendingShellAmmo;
	private AmmoDefinition m_DeferredShellAmmoFromShot;

	private bool m_DustCoverDesiredOpen;
	private float m_DustCoverAngleDegrees;
	private bool m_DustCoverTweenActive;
	#endregion

	#region Public Properties
	public bool WillHandlePhysicalShellEjection
	{
		get
		{
			// Болтовые: гильза всегда через передёргивание, не при выстреле.
			if (UsesManualBoltCycleWeapon())
				return true;

			EquippedWeapon weapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
			if (weapon == null || weapon.BoltCarrierTransform == null)
				return false;

			Transform bolt = m_BoltCarrier != null ? m_BoltCarrier : weapon.BoltCarrierTransform;
			WeaponVfxProfile profile = WeaponVfxUtility.GetCurrentProfile(m_WeaponRuntime);
			if (!WeaponVfxUtility.TryGetShellEjectionPose(weapon, out Vector3 pos, out _))
				pos = bolt.position;

			return WeaponVfxUtility.ShouldUsePhysicalShellEjection(profile, pos)
				&& WeaponVfxUtility.IsWithinNearCameraDetailDistance(profile, pos);
		}
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_ReloadController == null)
			m_ReloadController = GetComponent<UnitWeaponReloadController>();
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_Equipment == null)
			m_Equipment = GetComponentInChildren<UnitEquipment>(true);
		if (m_ShellEjection == null)
			m_ShellEjection = GetComponent<UnitWeaponShellEjection>();
		if (m_RagdollController == null)
			m_RagdollController = GetComponent<UnitRagdollController>();
	}

	private void OnEnable()
	{
		if (m_FireController != null)
			m_FireController.ShotFired += HandleShotFired;
		if (m_ReloadController != null)
		{
			m_ReloadController.ReloadSequenceCompleted += HandleReloadSequenceCompleted;
			m_ReloadController.BoltMotionPresented += HandleBoltMotionPresented;
		}

		if (m_Equipment != null)
			m_Equipment.EquipmentChanged += HandleEquipmentChanged;

		BindWeaponVisuals(true);
	}

	private void OnDisable()
	{
		if (m_FireController != null)
			m_FireController.ShotFired -= HandleShotFired;
		if (m_ReloadController != null)
		{
			m_ReloadController.ReloadSequenceCompleted -= HandleReloadSequenceCompleted;
			m_ReloadController.BoltMotionPresented -= HandleBoltMotionPresented;
		}

		if (m_Equipment != null)
			m_Equipment.EquipmentChanged -= HandleEquipmentChanged;

		ResetBoltToRest(true);
		ApplyDustCoverAngle(ResolveDustCoverTargetAngle(false), true);
		ClearCycleState();
	}

	private void LateUpdate()
	{
		if (m_RagdollController != null && m_RagdollController.ShouldBlockWeaponPoseScripts)
			return;

		UpdateBoltCycle(Time.deltaTime);
		UpdateDustCover(Time.deltaTime);
	}
	#endregion

	#region Private Methods
	private void HandleEquipmentChanged()
	{
		BindWeaponVisuals(true);
	}

	private bool UsesManualBoltCycleWeapon()
	{
		WeaponDefinition weaponDefinition = m_WeaponRuntime != null ? m_WeaponRuntime.CurrentWeaponDefinition : null;
		return weaponDefinition != null && weaponDefinition.RequiresManualBoltCycle;
	}

	private void HandleShotFired(AmmoDefinition _ammo)
	{
		BindWeaponVisuals(false);

		if (!HasConfiguredBoltOrDustCover())
			return;

		// Болтовые: гильза при передёргивании, не при выстреле.
		if (UsesManualBoltCycleWeapon())
		{
			m_DeferredShellAmmoFromShot = _ammo;
			return;
		}

		if (m_DustCoverHinge != null)
			SetDustCoverDesiredOpen(true);

		if (m_BoltCarrier == null)
			return;

		if (!IsNearCameraForBoundWeapon())
			return;

		bool holdOpen = ShouldHoldBoltOpenAfterShot();
		StartFullBoltCycle(_ammo, holdOpen);
	}

	private void HandleBoltMotionPresented()
	{
		BindWeaponVisuals(false);

		if (m_BoltCarrier == null)
		{
			if (m_DustCoverHinge != null && m_DustCoverDesiredOpen)
				SetDustCoverDesiredOpen(false);
			return;
		}

		if (m_BoltHoldOpen)
		{
			StartCloseFromOpen(closeDustCoverAfter: true);
			return;
		}

		if (!IsNearCameraForBoundWeapon())
		{
			ResetBoltToRest(true);
			if (UsesManualBoltCycleWeapon() && m_DeferredShellAmmoFromShot != null)
			{
				AmmoDefinition farShell = m_DeferredShellAmmoFromShot;
				m_DeferredShellAmmoFromShot = null;
				if (m_ShellEjection != null)
					m_ShellEjection.SpawnShellForAmmo(farShell);
			}
			else
				m_DeferredShellAmmoFromShot = null;
			return;
		}

		if (UsesManualBoltCycleWeapon())
		{
			AmmoDefinition shell = m_DeferredShellAmmoFromShot;
			m_DeferredShellAmmoFromShot = null;
			StartBoltActionHandleCycle(shell);
			return;
		}

		StartFullBoltCycle(null, _holdOpen: false);
	}

	private void HandleReloadSequenceCompleted()
	{
		BindWeaponVisuals(false);

		if (m_BoltHoldOpen || m_CloseDustCoverAfterBoltClose)
			return;

		if (m_DustCoverHinge != null)
			SetDustCoverDesiredOpen(false);
	}

	private bool HasConfiguredBoltOrDustCover()
	{
		return m_BoltCarrier != null || m_DustCoverHinge != null;
	}

	private bool ShouldHoldBoltOpenAfterShot()
	{
		WeaponDefinition weaponDefinition = m_WeaponRuntime != null ? m_WeaponRuntime.CurrentWeaponDefinition : null;
		if (weaponDefinition == null || !weaponDefinition.HasBoltHoldOpenDelay)
			return false;

		return m_WeaponRuntime != null && !m_WeaponRuntime.HasRoundInChamber;
	}

	private bool UsesBoltHandleRotation()
	{
		return m_BoltHandleOpenLocalEulerAngles.sqrMagnitude > 0.0001f;
	}

	private void StartBoltActionHandleCycle(AmmoDefinition _ammo)
	{
		if (m_BoltCarrier == null)
			return;

		if (m_BoltMotionMode != BoltMotionMode.None && !m_ShellEjectedThisCycle)
			TryEjectPendingShell();

		m_PendingShellAmmo = _ammo;
		m_ShellEjectedThisCycle = _ammo == null;
		m_BoltHoldOpen = false;
		m_CloseDustCoverAfterBoltClose = false;
		m_BoltMotionMode = UsesBoltHandleRotation()
			? BoltMotionMode.BoltActionHandleCycle
			: BoltMotionMode.FullCycle;
		m_BoltCycleElapsed = 0f;
		m_ActiveBoltCycleSeconds = Mathf.Max(0.05f,
			m_BoltActionCycleSeconds > 0f ? m_BoltActionCycleSeconds : m_BoltCycleSecondsSingleShot);
	}

	private void StartFullBoltCycle(AmmoDefinition _ammo, bool _holdOpen)
	{
		if (m_BoltCarrier == null)
			return;

		if (m_BoltMotionMode == BoltMotionMode.FullCycle && !m_ShellEjectedThisCycle)
			TryEjectPendingShell();

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
			m_BoltMotionMode = BoltMotionMode.None;
		}
	}

	private void StartCloseFromOpen(bool closeDustCoverAfter)
	{
		if (m_BoltCarrier == null)
		{
			m_BoltHoldOpen = false;
			if (closeDustCoverAfter && m_DustCoverHinge != null)
				SetDustCoverDesiredOpen(false);
			return;
		}

		m_BoltHoldOpen = false;
		m_CloseDustCoverAfterBoltClose = closeDustCoverAfter;
		m_PendingShellAmmo = null;
		m_ShellEjectedThisCycle = true;
		m_BoltMotionMode = BoltMotionMode.CloseFromOpen;
		m_BoltCycleElapsed = 0f;
		m_ActiveBoltCycleSeconds = Mathf.Max(0.02f, m_BoltCycleSecondsSingleShot);

		if (!IsNearCameraForBoundWeapon())
		{
			ResetBoltToRest(true);
			FinishCloseFromOpen();
		}
		else
		{
			ApplyBoltOpenAmount(1f);
		}
	}

	private void UpdateBoltCycle(float _deltaTime)
	{
		if (m_BoltMotionMode == BoltMotionMode.None || m_BoltCarrier == null)
			return;

		float cycleSeconds = Mathf.Max(0.02f, m_ActiveBoltCycleSeconds);
		m_BoltCycleElapsed += _deltaTime;

		if (m_BoltMotionMode == BoltMotionMode.CloseFromOpen)
		{
			float closeSeconds = cycleSeconds * 0.5f;
			float normalized = Mathf.Clamp01(m_BoltCycleElapsed / closeSeconds);
			float openAmount = Mathf.SmoothStep(1f, 0f, normalized);
			ApplyBoltOpenAmount(openAmount);
			if (normalized < 1f)
				return;

			FinishCloseFromOpen();
			return;
		}

		if (m_BoltMotionMode == BoltMotionMode.BoltActionHandleCycle)
		{
			UpdateBoltActionHandleCycle(cycleSeconds);
			return;
		}

		float fullNormalized = Mathf.Clamp01(m_BoltCycleElapsed / cycleSeconds);
		float fullOpenAmount = EvaluateBoltOpenAmount(fullNormalized);
		ApplyBoltOpenAmount(fullOpenAmount);

		if (!m_ShellEjectedThisCycle && fullNormalized >= m_BoltShellEjectNormalizedTime)
			TryEjectPendingShell();

		if (fullNormalized < 1f)
			return;

		m_BoltMotionMode = BoltMotionMode.None;
		if (m_BoltHoldOpen)
			ApplyBoltOpenAmount(1f);
		else
			ResetBoltToRest(false);
	}

	/// <summary>
	/// Mosin: rotate → slide back → eject → slide forward → rotate close.
	/// Sniper (без euler): тот же FullCycle через StartBoltActionHandleCycle fallback.
	/// </summary>
	private void UpdateBoltActionHandleCycle(float _cycleSeconds)
	{
		float normalized = Mathf.Clamp01(m_BoltCycleElapsed / _cycleSeconds);
		float rotatePhase = Mathf.Clamp(m_BoltHandleRotatePhaseNormalized, 0.05f, 0.45f);
		float slidePhase = Mathf.Max(0.05f, 0.5f - rotatePhase);

		float rotateOpenEnd = rotatePhase;
		float slideOpenEnd = rotateOpenEnd + slidePhase;
		float slideCloseEnd = slideOpenEnd + slidePhase;
		// remainder = rotate close

		float rotateAmount = 0f;
		float slideAmount = 0f;

		if (normalized <= rotateOpenEnd)
		{
			float t = rotateOpenEnd > 0.0001f ? normalized / rotateOpenEnd : 1f;
			rotateAmount = Mathf.SmoothStep(0f, 1f, t);
			slideAmount = 0f;
		}
		else if (normalized <= slideOpenEnd)
		{
			rotateAmount = 1f;
			float t = slidePhase > 0.0001f ? (normalized - rotateOpenEnd) / slidePhase : 1f;
			slideAmount = Mathf.SmoothStep(0f, 1f, t);
		}
		else if (normalized <= slideCloseEnd)
		{
			rotateAmount = 1f;
			float t = slidePhase > 0.0001f ? (normalized - slideOpenEnd) / slidePhase : 1f;
			slideAmount = Mathf.SmoothStep(1f, 0f, t);
		}
		else
		{
			slideAmount = 0f;
			float closeSpan = Mathf.Max(0.0001f, 1f - slideCloseEnd);
			float t = (normalized - slideCloseEnd) / closeSpan;
			rotateAmount = Mathf.SmoothStep(1f, 0f, t);
		}

		ApplyBoltHandlePose(rotateAmount, slideAmount);

		float ejectAt = slideOpenEnd;
		if (!m_ShellEjectedThisCycle && normalized >= ejectAt - 0.001f)
			TryEjectPendingShell();

		if (normalized < 1f)
			return;

		m_BoltMotionMode = BoltMotionMode.None;
		ResetBoltToRest(false);
	}

	private float ResolveBoltCycleSecondsForShot(bool _presentationCycle)
	{
		if (_presentationCycle)
			return Mathf.Max(0.02f, m_BoltCycleSecondsSingleShot);

		WeaponFireMode fireMode = m_FireController != null
			? m_FireController.ResolveEffectiveFireMode()
			: WeaponFireMode.SemiAuto;
		bool automaticBurst = WeaponFireModeUtility.IsAutomaticEffectiveMode(fireMode) &&
			m_FireController != null &&
			m_FireController.IsFiringCommandActive;

		return Mathf.Max(0.02f, automaticBurst ? m_BoltCycleSecondsAuto : m_BoltCycleSecondsSingleShot);
	}

	private void FinishCloseFromOpen()
	{
		m_BoltMotionMode = BoltMotionMode.None;
		ResetBoltToRest(true);
		bool closeDust = m_CloseDustCoverAfterBoltClose;
		m_CloseDustCoverAfterBoltClose = false;
		if (closeDust && m_DustCoverHinge != null)
			SetDustCoverDesiredOpen(false);
	}

	private static float EvaluateBoltOpenAmount(float _normalized)
	{
		if (_normalized <= 0.5f)
			return Mathf.SmoothStep(0f, 1f, _normalized * 2f);

		return Mathf.SmoothStep(1f, 0f, (_normalized - 0.5f) * 2f);
	}

	private void TryEjectPendingShell()
	{
		if (m_ShellEjectedThisCycle)
			return;

		m_ShellEjectedThisCycle = true;
		AmmoDefinition ammo = m_PendingShellAmmo;
		m_PendingShellAmmo = null;
		if (ammo != null && m_ShellEjection != null)
			m_ShellEjection.SpawnShellForAmmo(ammo);
	}

	private void ApplyBoltOpenAmount(float _open01)
	{
		if (m_BoltCarrier == null)
			return;

		float open01 = Mathf.Clamp01(_open01);
		m_BoltCarrier.localPosition = m_BoltRestLocalPosition + m_BoltOpenLocalOffset * open01;
		if (UsesBoltHandleRotation())
			m_BoltCarrier.localRotation = m_BoltRestLocalRotation *
				Quaternion.Euler(m_BoltHandleOpenLocalEulerAngles * open01);
		else
			m_BoltCarrier.localRotation = m_BoltRestLocalRotation;
	}

	private void ApplyBoltHandlePose(float _rotate01, float _slide01)
	{
		if (m_BoltCarrier == null)
			return;

		float rotate01 = Mathf.Clamp01(_rotate01);
		float slide01 = Mathf.Clamp01(_slide01);
		m_BoltCarrier.localPosition = m_BoltRestLocalPosition + m_BoltOpenLocalOffset * slide01;
		m_BoltCarrier.localRotation = m_BoltRestLocalRotation *
			Quaternion.Euler(m_BoltHandleOpenLocalEulerAngles * rotate01);
	}

	private void ResetBoltToRest(bool _snap)
	{
		if (m_BoltCarrier == null)
			return;

		m_BoltCarrier.localPosition = m_BoltRestLocalPosition;
		m_BoltCarrier.localRotation = m_BoltRestLocalRotation;
	}

	private void SetDustCoverDesiredOpen(bool _open)
	{
		if (m_DustCoverHinge == null)
			return;

		float target = ResolveDustCoverTargetAngle(_open);
		if (m_DustCoverDesiredOpen == _open && !m_DustCoverTweenActive)
		{
			if (Mathf.Abs(m_DustCoverAngleDegrees - target) <= c_IdleEpsilon)
				return;
		}

		m_DustCoverDesiredOpen = _open;
		if (!IsNearCameraForBoundWeapon())
		{
			ApplyDustCoverAngle(target, true);
			m_DustCoverTweenActive = false;
			return;
		}

		m_DustCoverTweenActive = true;
	}

	private void UpdateDustCover(float _deltaTime)
	{
		if (m_DustCoverHinge == null || !m_DustCoverTweenActive)
			return;

		float target = ResolveDustCoverTargetAngle(m_DustCoverDesiredOpen);
		float duration = Mathf.Max(0.01f, m_DustCoverTweenSeconds);
		float travel = Mathf.Max(0.01f, Mathf.Abs(m_DustCoverClosedDegrees));
		float step = travel / duration * _deltaTime;
		m_DustCoverAngleDegrees = Mathf.MoveTowards(m_DustCoverAngleDegrees, target, step);
		ApplyDustCoverAngle(m_DustCoverAngleDegrees, false);

		if (Mathf.Abs(m_DustCoverAngleDegrees - target) <= c_IdleEpsilon)
		{
			m_DustCoverAngleDegrees = target;
			ApplyDustCoverAngle(target, false);
			m_DustCoverTweenActive = false;
		}
	}

	private float ResolveDustCoverTargetAngle(bool _open) =>
		_open ? 0f : m_DustCoverClosedDegrees;

	private void ApplyDustCoverAngle(float _degrees, bool _force)
	{
		if (m_DustCoverHinge == null)
			return;

		m_DustCoverAngleDegrees = _degrees;
		Vector3 axis = m_DustCoverHingeAxis.sqrMagnitude > 1e-6f
			? m_DustCoverHingeAxis.normalized
			: Vector3.forward;
		m_DustCoverHinge.localRotation = Quaternion.AngleAxis(_degrees, axis);
		if (_force)
			m_DustCoverTweenActive = false;
	}

	private void BindWeaponVisuals(bool _resetState)
	{
		EquippedWeapon weapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		if (ReferenceEquals(weapon, m_BoundWeapon) && !_resetState)
			return;

		if (m_BoltCarrier != null && m_BoundWeapon != null)
		{
			m_BoltCarrier.localPosition = m_BoltRestLocalPosition;
			m_BoltCarrier.localRotation = m_BoltRestLocalRotation;
		}

		m_BoundWeapon = weapon;
		ClearCycleState();
		m_DeferredShellAmmoFromShot = null;

		if (weapon == null)
		{
			m_BoltCarrier = null;
			m_DustCoverHinge = null;
			return;
		}

		m_BoltCarrier = weapon.BoltCarrierTransform;
		m_BoltOpenLocalOffset = weapon.BoltOpenLocalOffset;
		m_BoltHandleOpenLocalEulerAngles = weapon.BoltHandleOpenLocalEulerAngles;
		m_BoltHandleRotatePhaseNormalized = weapon.BoltHandleRotatePhaseNormalized;
		m_BoltCycleSecondsAuto = Mathf.Max(0.02f, weapon.BoltCycleSeconds);
		m_BoltCycleSecondsSingleShot = Mathf.Max(0.02f, weapon.BoltCycleSecondsSingleShot);
		m_BoltActionCycleSeconds = Mathf.Max(0f, weapon.BoltActionCycleSeconds);
		m_ActiveBoltCycleSeconds = m_BoltCycleSecondsSingleShot;
		m_BoltShellEjectNormalizedTime = Mathf.Clamp(weapon.BoltShellEjectNormalizedTime, 0.15f, 0.85f);
		if (m_BoltCarrier != null)
		{
			m_BoltRestLocalPosition = m_BoltCarrier.localPosition;
			m_BoltRestLocalRotation = m_BoltCarrier.localRotation;
		}

		m_DustCoverHinge = weapon.DustCoverHingeTransform;
		m_DustCoverClosedDegrees = weapon.DustCoverClosedDegrees;
		m_DustCoverHingeAxis = weapon.DustCoverHingeAxis.sqrMagnitude > 1e-6f
			? weapon.DustCoverHingeAxis
			: Vector3.forward;
		m_DustCoverTweenSeconds = Mathf.Max(0.01f, weapon.DustCoverTweenSeconds);
		m_DustCoverDesiredOpen = false;
		ApplyDustCoverAngle(ResolveDustCoverTargetAngle(false), true);
	}

	private void ClearCycleState()
	{
		m_BoltMotionMode = BoltMotionMode.None;
		m_BoltHoldOpen = false;
		m_CloseDustCoverAfterBoltClose = false;
		m_ShellEjectedThisCycle = false;
		m_BoltCycleElapsed = 0f;
		m_PendingShellAmmo = null;
	}

	private bool IsNearCameraForBoundWeapon()
	{
		WeaponVfxProfile profile = WeaponVfxUtility.GetCurrentProfile(m_WeaponRuntime);
		Vector3 samplePos = transform.position;
		if (m_BoundWeapon != null && WeaponVfxUtility.TryGetShellEjectionPose(m_BoundWeapon, out Vector3 shellPos, out _))
			samplePos = shellPos;
		else if (m_BoltCarrier != null)
			samplePos = m_BoltCarrier.position;

		return WeaponVfxUtility.IsWithinNearCameraDetailDistance(profile, samplePos);
	}
	#endregion
}
