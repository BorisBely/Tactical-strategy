using UnityEngine;

/// <summary>
/// Read-only IK target references on equipped weapon prefab.
/// Right: stance × pose under RightHandIK (or legacy Ready/NotReady names).
/// Left: single LeftHandIK (ForeGrip overrides at resolve time).
/// </summary>
[DisallowMultipleComponent]
public sealed class WeaponGripRig : MonoBehaviour
{
	public const string GripRigChildName = "GripRig";
	public const string RightHandIkRootName = "RightHandIK";
	public const string RightHandRootName = "RightHand";
	public const string LeftHandIkName = "LeftHandIK";
	public const string LeftHandGripName = "LeftHandGrip";
	public const string RightHandGripName = "RightHandGrip";
	public const string StandingName = "Standing";
	public const string CrouchName = "Crouch";
	public const string VehicleName = "Vehicle";
	public const string LowReadyName = "LowReady";
	public const string HoldNotReadyName = "HoldNotReady";
	public const string HoldNotReadyPatrolName = "HoldNotReadyPatrol";
	public const string HipFireName = "HipFire";
	public const string HipFireWalkName = "HipFireWalk";
	public const string HipFireCrouchWalkName = "HipFireCrouchWalk";
	public const string PointAimName = "PointAim";
	public const string AimingName = "Aiming";
	public const string HighReadyName = "HighReady";
	public const string ReadyName = "Ready";
	public const string NotReadyName = "NotReady";

	[SerializeField] private Transform m_LeftHandIk;
	[SerializeField] private Transform m_RightHandIkRoot;

	[SerializeField] private Transform m_StandingHoldNotReady;
	[SerializeField] private Transform m_StandingHoldNotReadyPatrol;
	[SerializeField] private Transform m_StandingLowReady;
	[SerializeField] private Transform m_StandingHipFire;
	[SerializeField] private Transform m_StandingHipFireWalk;
	[SerializeField] private Transform m_StandingHipFireCrouchWalk;
	[SerializeField] private Transform m_StandingPointAim;
	[SerializeField] private Transform m_StandingAiming;
	[SerializeField] private Transform m_StandingHighReady;
	[SerializeField] private Transform m_CrouchHoldNotReady;
	[SerializeField] private Transform m_CrouchHoldNotReadyPatrol;
	[SerializeField] private Transform m_CrouchLowReady;
	[SerializeField] private Transform m_CrouchHipFire;
	[SerializeField] private Transform m_CrouchHipFireWalk;
	[SerializeField] private Transform m_CrouchHipFireCrouchWalk;
	[SerializeField] private Transform m_CrouchPointAim;
	[SerializeField] private Transform m_CrouchAiming;
	[SerializeField] private Transform m_CrouchHighReady;
	[SerializeField] private Transform m_VehicleHoldNotReady;
	[SerializeField] private Transform m_VehicleHoldNotReadyPatrol;
	[SerializeField] private Transform m_VehicleLowReady;
	[SerializeField] private Transform m_VehicleHipFire;
	[SerializeField] private Transform m_VehicleHipFireWalk;
	[SerializeField] private Transform m_VehicleHipFireCrouchWalk;
	[SerializeField] private Transform m_VehiclePointAim;
	[SerializeField] private Transform m_VehicleAiming;
	[SerializeField] private Transform m_VehicleHighReady;

	// Legacy serialized refs (Ready/NotReady era) — remapped in OnValidate / BuildCache
	[SerializeField] private Transform m_StandingReady;
	[SerializeField] private Transform m_StandingNotReady;
	[SerializeField] private Transform m_CrouchReady;
	[SerializeField] private Transform m_CrouchNotReady;
	[SerializeField] private Transform m_VehicleReady;
	[SerializeField] private Transform m_VehicleNotReady;

	[SerializeField] private Transform m_RightHandGrip;
	[SerializeField] private Transform m_LeftHandGrip;

	public Transform LeftHandIk => m_LeftHandIk != null ? m_LeftHandIk : m_LeftHandGrip;
	public Transform RightHandIkRoot => m_RightHandIkRoot;
	public Transform RightHandRoot => m_RightHandIkRoot;
	public Transform RightHandGrip => m_RightHandGrip;
	public Transform LeftHandGrip => LeftHandIk;
	public CachedRightHandTargets CachedRightTargets { get; private set; }

	public bool HasRightHandIkTargets => CachedRightTargets.HasAny;
	public bool HasLeftHandIk => LeftHandIk != null;
	public bool HasValidGrips => HasRightHandIkTargets || m_RightHandGrip != null;

	public void BuildCache()
	{
		MigrateLegacyRefsIfNeeded();
		CachedRightTargets = new CachedRightHandTargets
		{
			Standing = new HandIkPoseSet
			{
				HoldNotReady = m_StandingHoldNotReady,
				HoldNotReadyPatrol = m_StandingHoldNotReadyPatrol,
				LowReady = m_StandingLowReady,
				HipFire = m_StandingHipFire,
				HipFireWalk = m_StandingHipFireWalk,
				HipFireCrouchWalk = m_StandingHipFireCrouchWalk,
				PointAim = m_StandingPointAim,
				Aiming = m_StandingAiming,
				HighReady = m_StandingHighReady,
			},
			Crouch = new HandIkPoseSet
			{
				HoldNotReady = m_CrouchHoldNotReady,
				HoldNotReadyPatrol = m_CrouchHoldNotReadyPatrol,
				LowReady = m_CrouchLowReady,
				HipFire = m_CrouchHipFire,
				HipFireWalk = m_CrouchHipFireWalk,
				HipFireCrouchWalk = m_CrouchHipFireCrouchWalk,
				PointAim = m_CrouchPointAim,
				Aiming = m_CrouchAiming,
				HighReady = m_CrouchHighReady,
			},
			Vehicle = new HandIkPoseSet
			{
				HoldNotReady = m_VehicleHoldNotReady,
				HoldNotReadyPatrol = m_VehicleHoldNotReadyPatrol,
				LowReady = m_VehicleLowReady,
				HipFire = m_VehicleHipFire,
				HipFireWalk = m_VehicleHipFireWalk,
				HipFireCrouchWalk = m_VehicleHipFireCrouchWalk,
				PointAim = m_VehiclePointAim,
				Aiming = m_VehicleAiming,
				HighReady = m_VehicleHighReady,
			},
		};
	}

	public Transform GetRightHandTarget(WeaponStance _stance, WeaponPoseState _pose)
	{
		if (!CachedRightTargets.HasAny)
			BuildCache();
		return CachedRightTargets.Pick(_stance, _pose);
	}

	public Transform GetRightHandTarget(WeaponStance _stance, bool _ready)
	{
		return GetRightHandTarget(_stance, _ready ? WeaponPoseState.PointAim : WeaponPoseState.LowReady);
	}

	public bool TryGetRightHandTargets(WeaponStance _stance, out Transform _lowReady, out Transform _pointAim)
	{
		if (!CachedRightTargets.HasAny)
			BuildCache();
		_lowReady = CachedRightTargets.Pick(_stance, WeaponPoseState.LowReady);
		_pointAim = CachedRightTargets.Pick(_stance, WeaponPoseState.PointAim);
		return _lowReady != null || _pointAim != null;
	}

	public void SetLeftHandIk(Transform _left)
	{
		m_LeftHandIk = _left;
		m_LeftHandGrip = _left;
	}

	public void SetGrips(Transform _rightMarker, Transform _left)
	{
		m_RightHandGrip = _rightMarker;
		SetLeftHandIk(_left);
		BuildCache();
	}

	public void SetRightHandPoseTargets(
		Transform _standingReady,
		Transform _standingNotReady,
		Transform _crouchReady,
		Transform _crouchNotReady,
		Transform _vehicleReady,
		Transform _vehicleNotReady)
	{
		SetRightHandFullPoseTargets(
			_standingNotReady, _standingNotReady, _standingReady, _standingReady,
			_crouchNotReady, _crouchNotReady, _crouchReady, _crouchReady,
			_vehicleNotReady, _vehicleNotReady, _vehicleReady, _vehicleReady);
	}

	public void SetRightHandFullPoseTargets(
		Transform _standingLowReady,
		Transform _standingHipFire,
		Transform _standingPointAim,
		Transform _standingAiming,
		Transform _crouchLowReady,
		Transform _crouchHipFire,
		Transform _crouchPointAim,
		Transform _crouchAiming,
		Transform _vehicleLowReady,
		Transform _vehicleHipFire,
		Transform _vehiclePointAim,
		Transform _vehicleAiming)
	{
		SetRightHandAllPoseTargets(
			_standingLowReady, _standingLowReady, _standingHipFire, _standingPointAim, _standingAiming,
			_crouchLowReady, _crouchLowReady, _crouchHipFire, _crouchPointAim, _crouchAiming,
			_vehicleLowReady, _vehicleLowReady, _vehicleHipFire, _vehiclePointAim, _vehicleAiming);
	}

	public void SetRightHandAllPoseTargets(
		Transform _standingHoldNotReady,
		Transform _standingLowReady,
		Transform _standingHipFire,
		Transform _standingPointAim,
		Transform _standingAiming,
		Transform _crouchHoldNotReady,
		Transform _crouchLowReady,
		Transform _crouchHipFire,
		Transform _crouchPointAim,
		Transform _crouchAiming,
		Transform _vehicleHoldNotReady,
		Transform _vehicleLowReady,
		Transform _vehicleHipFire,
		Transform _vehiclePointAim,
		Transform _vehicleAiming)
	{
		m_StandingHoldNotReady = _standingHoldNotReady;
		m_StandingLowReady = _standingLowReady;
		m_StandingHipFire = _standingHipFire;
		m_StandingPointAim = _standingPointAim;
		m_StandingAiming = _standingAiming;
		m_CrouchHoldNotReady = _crouchHoldNotReady;
		m_CrouchLowReady = _crouchLowReady;
		m_CrouchHipFire = _crouchHipFire;
		m_CrouchPointAim = _crouchPointAim;
		m_CrouchAiming = _crouchAiming;
		m_VehicleHoldNotReady = _vehicleHoldNotReady;
		m_VehicleLowReady = _vehicleLowReady;
		m_VehicleHipFire = _vehicleHipFire;
		m_VehiclePointAim = _vehiclePointAim;
		m_VehicleAiming = _vehicleAiming;

		m_StandingReady = _standingPointAim;
		m_StandingNotReady = _standingLowReady;
		m_CrouchReady = _crouchPointAim;
		m_CrouchNotReady = _crouchLowReady;
		m_VehicleReady = _vehiclePointAim;
		m_VehicleNotReady = _vehicleLowReady;
		BuildCache();
	}

	public void SetHighReadyPoseTargets(
		Transform _standingHighReady,
		Transform _crouchHighReady,
		Transform _vehicleHighReady)
	{
		m_StandingHighReady = _standingHighReady;
		m_CrouchHighReady = _crouchHighReady;
		m_VehicleHighReady = _vehicleHighReady;
		BuildCache();
	}

	public void SetNotReadyPatrolPoseTargets(
		Transform _standingHoldNotReadyPatrol,
		Transform _crouchHoldNotReadyPatrol,
		Transform _vehicleHoldNotReadyPatrol)
	{
		m_StandingHoldNotReadyPatrol = _standingHoldNotReadyPatrol;
		m_CrouchHoldNotReadyPatrol = _crouchHoldNotReadyPatrol;
		m_VehicleHoldNotReadyPatrol = _vehicleHoldNotReadyPatrol;
		BuildCache();
	}

	public void SetHipFireWalkPoseTargets(
		Transform _standingHipFireWalk,
		Transform _crouchHipFireWalk,
		Transform _vehicleHipFireWalk)
	{
		m_StandingHipFireWalk = _standingHipFireWalk;
		m_CrouchHipFireWalk = _crouchHipFireWalk;
		m_VehicleHipFireWalk = _vehicleHipFireWalk;
		BuildCache();
	}

	public void SetHipFireCrouchWalkPoseTargets(
		Transform _standingHipFireCrouchWalk,
		Transform _crouchHipFireCrouchWalk,
		Transform _vehicleHipFireCrouchWalk)
	{
		m_StandingHipFireCrouchWalk = _standingHipFireCrouchWalk;
		m_CrouchHipFireCrouchWalk = _crouchHipFireCrouchWalk;
		m_VehicleHipFireCrouchWalk = _vehicleHipFireCrouchWalk;
		BuildCache();
	}

	private void MigrateLegacyRefsIfNeeded()
	{
		if (m_StandingLowReady == null)
			m_StandingLowReady = m_StandingNotReady;
		if (m_StandingHoldNotReady == null)
			m_StandingHoldNotReady = m_StandingLowReady;
		if (m_StandingHoldNotReadyPatrol == null)
			m_StandingHoldNotReadyPatrol = m_StandingHoldNotReady;
		if (m_StandingPointAim == null)
			m_StandingPointAim = m_StandingReady;
		if (m_StandingAiming == null)
			m_StandingAiming = m_StandingPointAim;
		if (m_StandingHipFire == null)
			m_StandingHipFire = m_StandingLowReady;
		if (m_StandingHighReady == null)
			m_StandingHighReady = m_StandingAiming != null ? m_StandingAiming : m_StandingPointAim;

		if (m_CrouchLowReady == null)
			m_CrouchLowReady = m_CrouchNotReady;
		if (m_CrouchHoldNotReady == null)
			m_CrouchHoldNotReady = m_CrouchLowReady;
		if (m_CrouchHoldNotReadyPatrol == null)
			m_CrouchHoldNotReadyPatrol = m_CrouchHoldNotReady;
		if (m_CrouchPointAim == null)
			m_CrouchPointAim = m_CrouchReady;
		if (m_CrouchAiming == null)
			m_CrouchAiming = m_CrouchPointAim;
		if (m_CrouchHipFire == null)
			m_CrouchHipFire = m_CrouchLowReady;
		if (m_CrouchHighReady == null)
			m_CrouchHighReady = m_CrouchAiming != null ? m_CrouchAiming : m_CrouchPointAim;

		if (m_VehicleLowReady == null)
			m_VehicleLowReady = m_VehicleNotReady;
		if (m_VehicleHoldNotReady == null)
			m_VehicleHoldNotReady = m_VehicleLowReady;
		if (m_VehicleHoldNotReadyPatrol == null)
			m_VehicleHoldNotReadyPatrol = m_VehicleHoldNotReady;
		if (m_VehiclePointAim == null)
			m_VehiclePointAim = m_VehicleReady;
		if (m_VehicleAiming == null)
			m_VehicleAiming = m_VehiclePointAim;
		if (m_VehicleHipFire == null)
			m_VehicleHipFire = m_VehicleLowReady;
		if (m_VehicleHighReady == null)
			m_VehicleHighReady = m_VehicleAiming != null ? m_VehicleAiming : m_VehiclePointAim;
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		WireHierarchyRefs();
		BuildCache();
	}

	private void WireHierarchyRefs()
	{
		if (m_RightHandIkRoot == null)
			m_RightHandIkRoot = FindNamed(transform, RightHandIkRootName)
			                    ?? FindNamed(transform, RightHandRootName);
		if (m_LeftHandIk == null)
			m_LeftHandIk = FindNamed(transform, LeftHandIkName)
			               ?? FindNamed(transform, LeftHandGripName);
		if (m_RightHandIkRoot == null)
			return;

		WireStance(m_RightHandIkRoot, StandingName,
			ref m_StandingHoldNotReady, ref m_StandingLowReady, ref m_StandingHipFire, ref m_StandingPointAim,
			ref m_StandingAiming, ref m_StandingHighReady, ref m_StandingNotReady, ref m_StandingReady);
		WireStance(m_RightHandIkRoot, CrouchName,
			ref m_CrouchHoldNotReady, ref m_CrouchLowReady, ref m_CrouchHipFire, ref m_CrouchPointAim,
			ref m_CrouchAiming, ref m_CrouchHighReady, ref m_CrouchNotReady, ref m_CrouchReady);
		WireStance(m_RightHandIkRoot, VehicleName,
			ref m_VehicleHoldNotReady, ref m_VehicleLowReady, ref m_VehicleHipFire, ref m_VehiclePointAim,
			ref m_VehicleAiming, ref m_VehicleHighReady, ref m_VehicleNotReady, ref m_VehicleReady);
		WireNamedSlot(m_RightHandIkRoot, StandingName, HoldNotReadyPatrolName, ref m_StandingHoldNotReadyPatrol);
		WireNamedSlot(m_RightHandIkRoot, CrouchName, HoldNotReadyPatrolName, ref m_CrouchHoldNotReadyPatrol);
		WireNamedSlot(m_RightHandIkRoot, VehicleName, HoldNotReadyPatrolName, ref m_VehicleHoldNotReadyPatrol);
		WireNamedSlot(m_RightHandIkRoot, StandingName, HipFireWalkName, ref m_StandingHipFireWalk);
		WireNamedSlot(m_RightHandIkRoot, CrouchName, HipFireWalkName, ref m_CrouchHipFireWalk);
		WireNamedSlot(m_RightHandIkRoot, VehicleName, HipFireWalkName, ref m_VehicleHipFireWalk);
		WireNamedSlot(m_RightHandIkRoot, StandingName, HipFireCrouchWalkName, ref m_StandingHipFireCrouchWalk);
		WireNamedSlot(m_RightHandIkRoot, CrouchName, HipFireCrouchWalkName, ref m_CrouchHipFireCrouchWalk);
		WireNamedSlot(m_RightHandIkRoot, VehicleName, HipFireCrouchWalkName, ref m_VehicleHipFireCrouchWalk);
	}

	private static void WireStance(
		Transform _root,
		string _stanceName,
		ref Transform _hold,
		ref Transform _low,
		ref Transform _hip,
		ref Transform _point,
		ref Transform _aim,
		ref Transform _highReady,
		ref Transform _legacyNotReady,
		ref Transform _legacyReady)
	{
		Transform stance = _root.Find(_stanceName);
		if (stance == null)
			return;

		_hold = stance.Find(HoldNotReadyName) ?? _hold;
		_low = stance.Find(LowReadyName) ?? stance.Find(NotReadyName) ?? _legacyNotReady;
		_point = stance.Find(PointAimName) ?? stance.Find(ReadyName) ?? _legacyReady;
		_hip = stance.Find(HipFireName) ?? _hip;
		_aim = stance.Find(AimingName) ?? _aim;
		_highReady = stance.Find(HighReadyName) ?? _highReady;
		_legacyNotReady = _low;
		_legacyReady = _point;
	}

	private static void WireNamedSlot(
		Transform _root,
		string _stanceName,
		string _slotName,
		ref Transform _slot)
	{
		Transform stance = _root.Find(_stanceName);
		if (stance == null)
			return;
		_slot = stance.Find(_slotName) ?? _slot;
	}

	private static Transform FindNamed(Transform _root, string _name)
	{
		if (_root == null || string.IsNullOrEmpty(_name))
			return null;
		foreach (Transform t in _root.GetComponentsInChildren<Transform>(true))
		{
			if (t != _root && t.name == _name)
				return t;
		}

		return null;
	}
#endif
}
