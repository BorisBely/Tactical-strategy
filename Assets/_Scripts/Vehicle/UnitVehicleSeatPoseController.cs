using UnityEngine;

/// <summary>
/// Позы в машине на слое Carried_Pose:
/// litter → Laying Sleeping; салон (не стрелок) → Driving;
/// у всех — скрыт декор оружия за спиной и рюкзак;
/// у водителя — скрыто основное оружие (без drop), base layer weight 0;
/// пассажиры — Driving + слой Vehicle_Passenger_Hands (не готов) поверх с приоритетом.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(80)]
public sealed class UnitVehicleSeatPoseController : MonoBehaviour
{
	#region Constants
	public const string ParamIsVehicleDriving = "IsVehicleDriving";
	public const string PassengerHandsLayerName = "Vehicle_Passenger_Hands";
	public const string PassengerHandsRelaxedState = "PassengerHands_RelaxedIdle";
	#endregion

	#region Private Fields
	private static readonly int s_IsVehicleDriving = Animator.StringToHash(ParamIsVehicleDriving);
	private static readonly int s_IsStabilizedSleeping =
		Animator.StringToHash(UnitStabilizedUnconsciousPoseController.ParamIsStabilizedSleeping);
	private static readonly int s_PassengerRelaxedState = Animator.StringToHash(PassengerHandsRelaxedState);

	private Animator m_Animator;
	private UnitWeaponReadyHandsLayer m_ReadyHands;
	private UnitWeaponAiming m_WeaponAiming;
	private UnitRagdollController m_Ragdoll;
	private UnitStabilizedUnconsciousPoseController m_SleepPose;
	private UnitEquipment m_Equipment;
	private UnitBackWeaponHolsterVisuals m_Holster;
	private UnitBackEquipment m_BackEquipment;
	private UnitWeaponVisualRecoilKick m_WeaponVisualRecoilKick;
	private AnimatorHandIk m_HandIk;
	private UnitEquippedWeaponPose m_EquippedWeaponPose;
	private int m_CarriedPoseLayerIndex = -1;
	private int m_PassengerHandsLayerIndex = -1;
	private float m_BaseLayerWeightBefore = 1f;
	private bool m_PoseActive;
	private bool m_IsDriverPose;
	private bool m_IsLitterPose;
	private bool m_IsPassengerHandsPose;
	private bool m_HadReadyWanted;
	private bool m_HidMainWeapon;
	private bool m_SuppressedDriverWeaponSystems;
	private bool m_WeaponAimingWasEnabled;
	private bool m_WeaponVisualRecoilKickWasEnabled;
	private bool m_HandIkWasEnabled;
	#endregion

	#region Public Properties
	public bool IsPoseActive => m_PoseActive;
	public bool IsPassengerHandsPoseActive => m_PoseActive && m_IsPassengerHandsPose;
	#endregion

	#region Public Methods
	public void ApplySeatPose(VehicleSeatId _seatId)
	{
		CacheRefs();
		ClearSeatPose();

		// Декор за спиной и рюкзак скрываем во всех слотах, включая стрелка.
		m_Holster?.SetForcedHidden(true);
		m_BackEquipment?.SetForcedHidden(true);

		if (_seatId == VehicleSeatId.Gunner)
		{
			m_PoseActive = true;
			return;
		}

		if (m_Animator == null || m_CarriedPoseLayerIndex < 0)
			return;

		m_PoseActive = true;
		m_IsLitterPose = VehicleSeatLayout.IsLitterSeat(_seatId);
		m_IsDriverPose = _seatId == VehicleSeatId.Driver;
		m_IsPassengerHandsPose = false;

		m_SleepPose?.NotifyExternalPoseOverride(true);

		if (!m_Animator.enabled)
			m_Animator.enabled = true;
		m_Animator.applyRootMotion = false;

		if (m_ReadyHands != null)
		{
			m_HadReadyWanted = m_ReadyHands.WantsReady;
			m_ReadyHands.SetReadyWanted(false);
		}

		if (m_IsLitterPose)
		{
			m_BaseLayerWeightBefore = m_Animator.GetLayerWeight(0);
			m_Animator.SetLayerWeight(0, 0f);
			m_Animator.SetBool(s_IsStabilizedSleeping, true);
			m_Animator.SetBool(s_IsVehicleDriving, false);
			m_Animator.SetLayerWeight(m_CarriedPoseLayerIndex, 1f);
			SetPassengerHandsLayerActive(false);
			m_Ragdoll?.SetWeaponControlFrozenForAnimatedPose(true);
			return;
		}

		// Driving (водитель / пассажиры салона)
		m_Animator.SetBool(s_IsStabilizedSleeping, false);
		m_Animator.SetBool(s_IsVehicleDriving, true);
		m_Animator.SetLayerWeight(m_CarriedPoseLayerIndex, 1f);

		if (m_IsDriverPose)
		{
			m_BaseLayerWeightBefore = m_Animator.GetLayerWeight(0);
			m_Animator.SetLayerWeight(0, 0f);
			SetPassengerHandsLayerActive(false);
			SuppressDriverWeaponSystems(true);
			return;
		}

		// Пассажир: руки «не готов» поверх Driving (слой выше Carried_Pose).
		SetPassengerHandsLayerActive(true);
		m_EquippedWeaponPose?.OnWeaponReadyStateChanged();
		m_HandIk?.OnWeaponReadyStateChanged();
	}

	public void ClearSeatPose()
	{
		if (!m_PoseActive && m_Animator == null)
			return;

		CacheRefs();

		if (m_Animator != null)
		{
			m_Animator.SetBool(s_IsVehicleDriving, false);
			if (m_IsLitterPose)
				m_Animator.SetBool(s_IsStabilizedSleeping, false);

			if (m_CarriedPoseLayerIndex >= 0)
				m_Animator.SetLayerWeight(m_CarriedPoseLayerIndex, 0f);

			SetPassengerHandsLayerActive(false);

			if (m_IsDriverPose || m_IsLitterPose)
				m_Animator.SetLayerWeight(0, m_BaseLayerWeightBefore > 0f ? m_BaseLayerWeightBefore : 1f);
		}

		if (m_IsLitterPose)
			m_Ragdoll?.SetWeaponControlFrozenForAnimatedPose(false);

		SuppressDriverWeaponSystems(false);

		if (m_ReadyHands != null && m_HadReadyWanted)
			m_ReadyHands.SetReadyWanted(true);

		m_Holster?.SetForcedHidden(false);
		m_BackEquipment?.SetForcedHidden(false);

		m_SleepPose?.NotifyExternalPoseOverride(false);

		m_PoseActive = false;
		m_IsDriverPose = false;
		m_IsLitterPose = false;
		m_IsPassengerHandsPose = false;
		m_HadReadyWanted = false;
	}

	public static UnitVehicleSeatPoseController GetOrAdd(GameObject _unitObject)
	{
		if (_unitObject == null)
			return null;
		if (!_unitObject.TryGetComponent(out UnitVehicleSeatPoseController pose))
			pose = _unitObject.AddComponent<UnitVehicleSeatPoseController>();
		return pose;
	}
	#endregion

	#region Unity Lifecycle
	private void LateUpdate()
	{
		if (!m_PoseActive || !m_IsPassengerHandsPose || m_Animator == null)
			return;

		// UnitWeaponAiming и др. могут гасить верхние слои — держим руки поверх Driving.
		if (m_PassengerHandsLayerIndex < 0)
			ResolvePassengerHandsLayerIndex();
		if (m_PassengerHandsLayerIndex < 0)
			return;

		if (m_Animator.GetLayerWeight(m_PassengerHandsLayerIndex) < 0.99f)
			m_Animator.SetLayerWeight(m_PassengerHandsLayerIndex, 1f);

		if (m_ReadyHands != null && m_ReadyHands.WantsReady)
			m_ReadyHands.SetReadyWanted(false);
	}

	private void OnDisable()
	{
		if (m_PoseActive)
			ClearSeatPose();
	}
	#endregion

	#region Private Methods
	private void SetPassengerHandsLayerActive(bool _active)
	{
		ResolvePassengerHandsLayerIndex();
		if (m_Animator == null || m_PassengerHandsLayerIndex < 0)
		{
			if (_active)
			{
				Debug.LogWarning(
					$"[UnitVehicleSeatPose] Layer '{PassengerHandsLayerName}' not found. " +
					"Run Polygone/Animation/Setup Vehicle Driving Pose.",
					this);
			}

			return;
		}

		if (_active)
		{
			m_Animator.SetLayerWeight(m_PassengerHandsLayerIndex, 1f);
			m_Animator.Play(s_PassengerRelaxedState, m_PassengerHandsLayerIndex, 0f);
			m_IsPassengerHandsPose = true;
		}
		else
		{
			m_Animator.SetLayerWeight(m_PassengerHandsLayerIndex, 0f);
		}
	}

	private void ResolvePassengerHandsLayerIndex()
	{
		if (m_Animator == null)
		{
			m_PassengerHandsLayerIndex = -1;
			return;
		}

		m_PassengerHandsLayerIndex = m_Animator.GetLayerIndex(PassengerHandsLayerName);
	}

	private void SuppressDriverWeaponSystems(bool _suppress)
	{
		if (_suppress)
		{
			if (m_SuppressedDriverWeaponSystems)
				return;

			if (m_Equipment != null)
			{
				m_Equipment.SetMainWeaponVisualActive(false);
				m_HidMainWeapon = true;
			}

			if (m_WeaponAiming != null)
			{
				m_WeaponAimingWasEnabled = m_WeaponAiming.enabled;
				m_WeaponAiming.enabled = false;
			}

			if (m_WeaponVisualRecoilKick != null)
			{
				m_WeaponVisualRecoilKickWasEnabled = m_WeaponVisualRecoilKick.enabled;
				m_WeaponVisualRecoilKick.enabled = false;
			}

			if (m_HandIk != null)
			{
				m_HandIkWasEnabled = m_HandIk.enabled;
				m_HandIk.enabled = false;
			}

			m_SuppressedDriverWeaponSystems = true;
			return;
		}

		if (!m_SuppressedDriverWeaponSystems)
			return;

		if (m_HidMainWeapon && m_Equipment != null)
			m_Equipment.SetMainWeaponVisualActive(true);
		m_HidMainWeapon = false;

		if (m_WeaponAiming != null)
			m_WeaponAiming.enabled = m_WeaponAimingWasEnabled;
		if (m_WeaponVisualRecoilKick != null)
			m_WeaponVisualRecoilKick.enabled = m_WeaponVisualRecoilKickWasEnabled;
		if (m_HandIk != null)
			m_HandIk.enabled = m_HandIkWasEnabled;

		m_SuppressedDriverWeaponSystems = false;
	}

	private void CacheRefs()
	{
		if (m_Animator == null)
			TryGetComponent(out m_Animator);
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>(true);
		if (m_ReadyHands == null)
			TryGetComponent(out m_ReadyHands);
		if (m_Ragdoll == null)
			TryGetComponent(out m_Ragdoll);
		if (m_SleepPose == null)
			TryGetComponent(out m_SleepPose);
		if (m_Equipment == null)
			TryGetComponent(out m_Equipment);
		if (m_Holster == null)
			TryGetComponent(out m_Holster);
		if (m_Holster == null)
			m_Holster = GetComponentInChildren<UnitBackWeaponHolsterVisuals>(true);
		if (m_BackEquipment == null)
			TryGetComponent(out m_BackEquipment);
		if (m_BackEquipment == null)
			m_BackEquipment = GetComponentInChildren<UnitBackEquipment>(true);
		if (m_WeaponAiming == null)
			TryGetComponent(out m_WeaponAiming);
		if (m_WeaponVisualRecoilKick == null)
			TryGetComponent(out m_WeaponVisualRecoilKick);
		if (m_HandIk == null)
			m_HandIk = GetComponentInChildren<AnimatorHandIk>(true);
		if (m_EquippedWeaponPose == null)
			TryGetComponent(out m_EquippedWeaponPose);

		if (m_Animator != null && m_CarriedPoseLayerIndex < 0)
			m_CarriedPoseLayerIndex = m_Animator.GetLayerIndex(UnitFiremanCarryController.CarriedPoseLayerName);

		ResolvePassengerHandsLayerIndex();
	}
	#endregion
}
