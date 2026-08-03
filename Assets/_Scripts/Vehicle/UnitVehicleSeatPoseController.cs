using UnityEngine;

/// <summary>
/// Позы в машине на слое Carried_Pose:
/// litter → Laying Sleeping; салон (не стрелок) → Driving;
/// у всех — скрыт декор оружия за спиной и рюкзак;
/// у водителя — скрыто основное оружие (без drop), base layer weight 0;
/// пассажиры — Driving + слой Vehicle_Passenger_Hands (Seat_relax или Seat_Aim_Blend) поверх.
/// Состояние готов/не готов читает из <see cref="VehiclePassengerState"/>; сам не хранит.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(80)]
public sealed class UnitVehicleSeatPoseController : MonoBehaviour
{
	#region Constants
	public const string ParamIsVehicleDriving = "IsVehicleDriving";
	public const string ParamIsVehicleGunner = "IsVehicleGunner";
	public const string ParamIsGunnerCover = "IsGunnerCover";
	public const string ParamVehicleReady = "VehicleReady";
	public const string ParamVehicleAimYaw = "VehicleAimYaw";
	public const string ParamVehicleAimSide = "VehicleAimSide";
	public const string PassengerHandsLayerName = "Vehicle_Passenger_Hands";
	public const string SeatRelaxState = "Seat_relax";
	#endregion

	#region Private Fields
	private static readonly int s_IsVehicleDriving = Animator.StringToHash(ParamIsVehicleDriving);
	private static readonly int s_IsVehicleGunner = Animator.StringToHash(ParamIsVehicleGunner);
	private static readonly int s_IsGunnerCover = Animator.StringToHash(ParamIsGunnerCover);
	private static readonly int s_IsStabilizedSleeping =
		Animator.StringToHash(UnitStabilizedUnconsciousPoseController.ParamIsStabilizedSleeping);
	private static readonly int s_VehicleReady = Animator.StringToHash(ParamVehicleReady);
	private static readonly int s_VehicleAimYaw = Animator.StringToHash(ParamVehicleAimYaw);
	private static readonly int s_VehicleAimSide = Animator.StringToHash(ParamVehicleAimSide);
	private static readonly int s_SeatRelaxState = Animator.StringToHash(SeatRelaxState);

	private Animator m_Animator;
	private VehiclePassengerState m_PassengerState;
	private UnitWeaponReadyHandsLayer m_ReadyHands;
	private UnitWeaponAiming m_WeaponAiming;
	private UnitRagdollController m_Ragdoll;
	private UnitStabilizedUnconsciousPoseController m_SleepPose;
	private UnitEquipment m_Equipment;
	private UnitBackWeaponHolsterVisuals m_Holster;
	private UnitBackEquipment m_BackEquipment;
	private UnitWeaponRecoil m_WeaponRecoil;
	private AnimatorHandIk m_HandIk;
	private UnitEquippedWeaponPose m_EquippedWeaponPose;
	private UnitProximityReadyController m_ProximityReady;
	private int m_CarriedPoseLayerIndex = -1;
	private int m_PassengerHandsLayerIndex = -1;
	private int m_AimLayerIndex = -1;
	private int m_MagazineLoadingLayerIndex = -1;
	private float m_BaseLayerWeightBefore = 1f;
	private bool m_PoseActive;
	private bool m_IsDriverPose;
	private bool m_IsLitterPose;
	private bool m_IsPassengerHandsPose;
	private bool m_HadReadyWanted;
	private bool m_HadKeyboardInputEnabled;
	private bool m_HidMainWeapon;
	private bool m_SuppressedDriverWeaponSystems;
	private bool m_WeaponAimingWasEnabled;
	private bool m_WeaponRecoilWasEnabled;
	private bool m_HandIkWasEnabled;
	private bool m_WasVehicleReady;
	private bool m_WasPreparing;
	private bool m_PassengerWeaponAimingWasEnabled;
	private bool m_GunnerWeaponAimingWasEnabled;
	private bool m_GunnerAimLayerWasZeroed;
	private float m_GunnerAimLayerWeightBefore = 1f;
	#endregion

	#region Public Properties
	public bool IsPoseActive => m_PoseActive;
	public bool IsPassengerHandsPoseActive => m_PoseActive && m_IsPassengerHandsPose;
	#endregion

	#region Public Methods
	public void ApplySeatPose(VehicleSeatId _seatId, VehicleController _vehicle = null)
	{
		CacheRefs();

		if (m_ReadyHands != null)
			m_HadKeyboardInputEnabled = m_ReadyHands.IsKeyboardInputEnabled;

		ClearSeatPose();

		m_Holster?.SetForcedHidden(true);
		m_BackEquipment?.SetForcedHidden(true);

		if (_seatId == VehicleSeatId.Gunner)
		{
			ApplyGunnerPose(_vehicle);
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
			m_HadReadyWanted = m_ReadyHands.WantsReady;

		if (m_IsLitterPose)
		{
			m_ReadyHands?.SetReadyWanted(false);
			ApplyLitterPose();
			return;
		}

		m_Animator.SetBool(s_IsStabilizedSleeping, false);
		m_Animator.SetBool(s_IsVehicleDriving, true);
		m_Animator.SetLayerWeight(m_CarriedPoseLayerIndex, 1f);

		if (m_IsDriverPose)
		{
			m_ReadyHands?.SetReadyWanted(false);
			ApplyDriverPose();
			return;
		}

		ApplyPassengerPose(_seatId, _vehicle);
	}

	public void ClearSeatPose()
	{
		if (!m_PoseActive && m_Animator == null)
			return;

		CacheRefs();

		if (m_Animator != null)
		{
			m_Animator.SetBool(s_IsVehicleDriving, false);
			m_Animator.SetBool(s_IsVehicleGunner, false);
			m_Animator.SetBool(s_IsGunnerCover, false);
			m_Animator.SetBool(s_VehicleReady, false);
			m_Animator.SetFloat(s_VehicleAimYaw, 0f);
			m_Animator.SetInteger(s_VehicleAimSide, 0);
			if (m_IsLitterPose)
				m_Animator.SetBool(s_IsStabilizedSleeping, false);

			if (m_CarriedPoseLayerIndex >= 0)
				m_Animator.SetLayerWeight(m_CarriedPoseLayerIndex, 0f);

			SetPassengerHandsLayerActive(false);

			if (m_IsDriverPose || m_IsLitterPose || m_IsPassengerHandsPose)
				m_Animator.SetLayerWeight(0, m_BaseLayerWeightBefore > 0f ? m_BaseLayerWeightBefore : 1f);
		}

		if (m_IsLitterPose)
			m_Ragdoll?.SetWeaponControlFrozenForAnimatedPose(false);

		SuppressDriverWeaponSystems(false);

		if (m_IsPassengerHandsPose && m_WeaponAiming != null && m_PassengerWeaponAimingWasEnabled)
			m_WeaponAiming.enabled = true;

		if (m_GunnerAimLayerWasZeroed && m_Animator != null && m_AimLayerIndex >= 0)
			m_Animator.SetLayerWeight(m_AimLayerIndex, m_GunnerAimLayerWeightBefore);
		if (m_WeaponAiming != null && m_GunnerWeaponAimingWasEnabled)
			m_WeaponAiming.enabled = true;
		m_GunnerAimLayerWasZeroed = false;
		m_GunnerWeaponAimingWasEnabled = false;

		if (m_ProximityReady != null)
			m_ProximityReady.enabled = true;

		if (m_AimLayerIndex >= 0 && m_Animator != null)
			m_Animator.SetLayerWeight(m_AimLayerIndex, 1f);

		if (m_HidMainWeapon && m_Equipment != null)
		{
			m_Equipment.SetMainWeaponVisualActive(true);
			m_HidMainWeapon = false;
		}

		if (m_ReadyHands != null && m_HadReadyWanted)
			m_ReadyHands.SetReadyWanted(true);
		if (m_ReadyHands != null)
			m_ReadyHands.SetKeyboardInputEnabled(m_HadKeyboardInputEnabled);

		m_Holster?.SetForcedHidden(false);
		m_BackEquipment?.SetForcedHidden(false);

		m_SleepPose?.NotifyExternalPoseOverride(false);

		if (m_PassengerState != null)
		{
			m_PassengerState.Detach();
			m_PassengerState = null;
		}

		m_PoseActive = false;
		m_IsDriverPose = false;
		m_IsLitterPose = false;
		m_IsPassengerHandsPose = false;
		m_HadReadyWanted = false;
		m_HadKeyboardInputEnabled = false;
		m_WasVehicleReady = false;
		m_WasPreparing = false;
		m_PassengerWeaponAimingWasEnabled = false;
		m_GunnerWeaponAimingWasEnabled = false;
		m_GunnerAimLayerWasZeroed = false;
		m_GunnerAimLayerWeightBefore = 1f;
	}

	public static UnitVehicleSeatPoseController GetOrAdd(GameObject _unitObject)
	{
		if (_unitObject == null)
			return null;
		if (!_unitObject.TryGetComponent(out UnitVehicleSeatPoseController pose))
			pose = _unitObject.AddComponent<UnitVehicleSeatPoseController>();
		return pose;
	}

	public void SetGunnerCover(bool _cover)
	{
		CacheRefs();
		if (m_Animator != null)
			m_Animator.SetBool(s_IsGunnerCover, _cover);
	}
	#endregion

	#region Unity Lifecycle
	private void LateUpdate()
	{
		if (!m_PoseActive || m_Animator == null)
			return;

		if (m_Animator.GetBool(s_IsVehicleGunner))
		{
			MaintainGunnerIkLayers();
			return;
		}

		if (!m_IsPassengerHandsPose)
			return;

		if (m_PassengerHandsLayerIndex < 0)
			ResolvePassengerHandsLayerIndex();
		if (m_PassengerHandsLayerIndex < 0)
			return;

		if (m_Animator.GetLayerWeight(m_PassengerHandsLayerIndex) < 0.99f)
			m_Animator.SetLayerWeight(m_PassengerHandsLayerIndex, 1f);

		if (m_PassengerState == null)
			return;

		SyncPassengerReadyState();
	}

	private void MaintainGunnerIkLayers()
	{
		if (m_CarriedPoseLayerIndex >= 0 && m_Animator.GetLayerWeight(m_CarriedPoseLayerIndex) < 0.99f)
			m_Animator.SetLayerWeight(m_CarriedPoseLayerIndex, 1f);

		if (m_AimLayerIndex < 0)
			m_AimLayerIndex = m_Animator.GetLayerIndex("Aim_Point_U90-D90");
		if (m_AimLayerIndex >= 0 && m_Animator.GetLayerWeight(m_AimLayerIndex) > 0f)
			m_Animator.SetLayerWeight(m_AimLayerIndex, 0f);

		if (m_MagazineLoadingLayerIndex < 0)
			m_MagazineLoadingLayerIndex = m_Animator.GetLayerIndex(UnitMagazineLoadingController.MagazineLoadingHandsLayerName);
		if (m_MagazineLoadingLayerIndex >= 0 && m_Animator.GetLayerWeight(m_MagazineLoadingLayerIndex) > 0f)
			m_Animator.SetLayerWeight(m_MagazineLoadingLayerIndex, 0f);

		if (m_HandIk != null && !m_HandIk.enabled)
			m_HandIk.enabled = true;

		if (m_WeaponAiming != null && m_WeaponAiming.enabled)
			m_WeaponAiming.enabled = false;
	}

	private void OnDisable()
	{
		if (m_PoseActive)
			ClearSeatPose();
	}
	#endregion

	#region Private Methods — Seat Apply
	private void ApplyGunnerPose(VehicleController _vehicle)
	{
		CacheRefs();

		if (m_Animator == null || m_CarriedPoseLayerIndex < 0)
		{
			m_PoseActive = true;
			return;
		}

		m_PoseActive = true;
		m_IsDriverPose = false;
		m_IsLitterPose = false;
		m_IsPassengerHandsPose = false;

		if (!m_Animator.enabled)
			m_Animator.enabled = true;
		m_Animator.applyRootMotion = false;

		m_BaseLayerWeightBefore = m_Animator.GetLayerWeight(0);
		m_Animator.SetLayerWeight(0, 0f);
		m_Animator.SetBool(s_IsStabilizedSleeping, false);
		m_Animator.SetBool(s_IsVehicleDriving, false);
		m_Animator.SetBool(s_IsVehicleGunner, true);
		m_Animator.SetLayerWeight(m_CarriedPoseLayerIndex, 1f);

		// Пехотный AimPitch/Aim_Point конфликтует с IK на турели; турель крутится на машине.
		if (m_WeaponAiming != null)
		{
			m_GunnerWeaponAimingWasEnabled = m_WeaponAiming.enabled;
			m_WeaponAiming.enabled = false;
		}

		m_AimLayerIndex = m_Animator.GetLayerIndex("Aim_Point_U90-D90");
		if (m_AimLayerIndex >= 0)
		{
			m_GunnerAimLayerWeightBefore = m_Animator.GetLayerWeight(m_AimLayerIndex);
			m_Animator.SetLayerWeight(m_AimLayerIndex, 0f);
			m_GunnerAimLayerWasZeroed = true;
		}

		if (m_Equipment != null)
		{
			m_Equipment.SetMainWeaponVisualActive(false);
			m_HidMainWeapon = true;
		}

		if (_vehicle != null)
		{
			m_ReadyHands?.SetReadyWanted(true);
			m_ReadyHands?.SetKeyboardInputEnabled(false);
		}
	}

	private void ApplyLitterPose()
	{
		m_BaseLayerWeightBefore = m_Animator.GetLayerWeight(0);
		m_Animator.SetLayerWeight(0, 0f);
		m_Animator.SetBool(s_IsStabilizedSleeping, true);
		m_Animator.SetBool(s_IsVehicleDriving, false);
		m_Animator.SetLayerWeight(m_CarriedPoseLayerIndex, 1f);
		SetPassengerHandsLayerActive(false);
		m_Ragdoll?.SetWeaponControlFrozenForAnimatedPose(true);
	}

	private void ApplyDriverPose()
	{
		m_BaseLayerWeightBefore = m_Animator.GetLayerWeight(0);
		m_Animator.SetLayerWeight(0, 0f);
		SetPassengerHandsLayerActive(false);
		SuppressDriverWeaponSystems(true);
	}

	private void ApplyPassengerPose(VehicleSeatId _seatId, VehicleController _vehicle)
	{
		SetPassengerHandsLayerActive(true);
		m_Animator.SetBool(s_VehicleReady, false);
		m_Animator.SetFloat(s_VehicleAimYaw, 0f);

		int aimSide = IsLeftSideSeat(_seatId) ? 0 : 1;
		m_Animator.SetInteger(s_VehicleAimSide, aimSide);

		m_BaseLayerWeightBefore = m_Animator.GetLayerWeight(0);
		m_Animator.SetLayerWeight(0, 0f);

		m_PassengerState = VehiclePassengerState.GetOrAdd(gameObject);
		m_PassengerState.Attach(_vehicle, _seatId);

		EnsurePassengerComponents(_vehicle);

		m_WasVehicleReady = false;
		m_WasPreparing = false;

		// Пассажир начинает в не готов; E на клавиатуре заблокирована.
		m_ReadyHands?.SetReadyWanted(false);
		m_ReadyHands?.SetKeyboardInputEnabled(false);

		// Корпус машины не должен блокировать ProximityReady.
		m_ProximityReady = GetComponent<UnitProximityReadyController>();
		if (m_ProximityReady != null)
			m_ProximityReady.enabled = false;
	}

	private static bool IsLeftSideSeat(VehicleSeatId _seatId)
	{
		return _seatId == VehicleSeatId.RearLeft;
	}

	private void EnsurePassengerComponents(VehicleController _vehicle)
	{
		if (!TryGetComponent(out VehiclePassengerAimController aim))
			aim = gameObject.AddComponent<VehiclePassengerAimController>();
		if (!TryGetComponent(out VehiclePassengerFireValidator fire))
			fire = gameObject.AddComponent<VehiclePassengerFireValidator>();
	}

	private void SyncPassengerReadyState()
	{
		if (m_PassengerState == null)
			return;

		bool wantsReady = m_PassengerState.IsVehicleReady || m_PassengerState.IsPreparing;
		bool wasReady = m_WasVehicleReady || m_WasPreparing;

		if (wantsReady != wasReady)
		{
			m_Animator.SetBool(s_VehicleReady, wantsReady);

			if (!wantsReady)
			{
				m_Animator.Play(s_SeatRelaxState, m_PassengerHandsLayerIndex, 0f);
			}

			m_ReadyHands?.SetReadyWanted(wantsReady);

			// В режиме готов в машине — отключаем пехотный доворот оружия,
			// иначе он конфликтует с боковой анимацией Seat_aim.
			if (wantsReady)
			{
				if (m_WeaponAiming != null)
				{
					m_PassengerWeaponAimingWasEnabled = m_WeaponAiming.enabled;
					m_WeaponAiming.enabled = false;
				}

				// Гасим Aim_Point_U90-D90 слой — его вес остался от пехоты.
				m_AimLayerIndex = m_Animator.GetLayerIndex("Aim_Point_U90-D90");
				if (m_AimLayerIndex >= 0)
					m_Animator.SetLayerWeight(m_AimLayerIndex, 0f);
			}
			else
			{
				if (m_WeaponAiming != null && m_PassengerWeaponAimingWasEnabled)
					m_WeaponAiming.enabled = true;
			}
		}

		m_WasVehicleReady = m_PassengerState.IsVehicleReady;
		m_WasPreparing = m_PassengerState.IsPreparing;
	}
	#endregion

	#region Private Methods — Helpers
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
			m_Animator.Play(s_SeatRelaxState, m_PassengerHandsLayerIndex, 0f);
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

			if (m_WeaponRecoil != null)
			{
				m_WeaponRecoilWasEnabled = m_WeaponRecoil.enabled;
				m_WeaponRecoil.enabled = false;
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
		if (m_WeaponRecoil != null)
			m_WeaponRecoil.enabled = m_WeaponRecoilWasEnabled;
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
		if (m_WeaponRecoil == null)
			TryGetComponent(out m_WeaponRecoil);
		if (m_HandIk == null)
			m_HandIk = GetComponentInChildren<AnimatorHandIk>(true);
		if (m_EquippedWeaponPose == null)
			TryGetComponent(out m_EquippedWeaponPose);
		if (m_PassengerState == null)
			TryGetComponent(out m_PassengerState);

		if (m_Animator != null && m_CarriedPoseLayerIndex < 0)
			m_CarriedPoseLayerIndex = m_Animator.GetLayerIndex(UnitFiremanCarryController.CarriedPoseLayerName);

		ResolvePassengerHandsLayerIndex();
	}
	#endregion
}
