using System;
using UnityEngine;

/// <summary>
/// EDITOR TOOL — weapon pose / hand IK tuning in Play Mode.
/// Not required on Unit for gameplay. Runtime pose/IK comes from ItemDefinition +
/// LeftHandIkTarget* empties (weapon body or foregrip prefab).
///
/// How to tune again:
/// 1. Menu: Polygone → Weapons → Add Weapon Pose Runtime Tuner To Unit
///    (or Add Component → UnitEquippedWeaponPoseRuntimeTuner on Unit)
/// 2. Play Mode → Enable Runtime Tuning
/// 3. Hands Frozen → place Equipped_*
/// 4. Not Ready / Ready → move RightHandIkTarget* and LeftHandIkTarget*
/// 5. Save Standing / Save Crouch / Save Vehicle (separate — won't overwrite other postures)
/// 6. If foregrip: Save Left IK To Foregrip Prefab (standing left IK)
///
/// Modes: Hands Frozen (no IK) → Not Ready → Ready. Postures: Standing / Crouch / Vehicle.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(44)]
public sealed class UnitEquippedWeaponPoseRuntimeTuner : MonoBehaviour
{
	#region Nested Types
	public enum TuningTarget
	{
		/// <summary>Hands follow animation only (IK off). Place weapon — first / base coordinates.</summary>
		HandsFrozen = 0,
		/// <summary>Not ready: weapon pose + right-hand IK target.</summary>
		NotReady = 1,
		/// <summary>Ready: weapon pose + right-hand IK target.</summary>
		Ready = 2
	}

	/// <summary>Which posture set is being edited / saved (standing vs crouch).</summary>
	public enum TuningPosture
	{
		Standing = 0,
		Crouch = 1,
		Vehicle = 2
	}
	#endregion

	#region Serialized Fields
	[SerializeField] private UnitEquipment m_UnitEquipment;
	[SerializeField] private UnitEquippedWeaponPose m_EquippedWeaponPose;
	[SerializeField] private UnitRocketLauncherOrderController m_RocketLauncherOrder;

	[Header("Runtime tuning")]
	[Tooltip("When on: weapon/IK transforms are free to move in Hierarchy.")]
	[SerializeField] private bool m_EnableRuntimeTuning;
	[Tooltip("Hands Frozen = place weapon with IK off (base coords). Then Not Ready / Ready for poses + hand IK.")]
	[SerializeField] private TuningTarget m_ActiveTarget = TuningTarget.HandsFrozen;
	[Tooltip("Standing / Crouch / Vehicle — separate captured values and separate Save buttons in the inspector.")]
	[SerializeField] private TuningPosture m_ActivePosture = TuningPosture.Standing;

	[Header("Captured — standing weapon pose (Hand_R local)")]
	[SerializeField] private Vector3 m_NotReadyLocalPosition;
	[SerializeField] private Vector3 m_NotReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_ReadyLocalPosition;
	[SerializeField] private Vector3 m_ReadyLocalEulerAngles;

	[Header("Captured — standing right hand IK (weapon local)")]
	[SerializeField] private Vector3 m_NotReadyIkLocalPosition;
	[SerializeField] private Vector3 m_NotReadyIkLocalEulerAngles;
	[SerializeField] private Vector3 m_ReadyIkLocalPosition;
	[SerializeField] private Vector3 m_ReadyIkLocalEulerAngles;

	[Header("Captured — standing left hand IK (weapon local)")]
	[SerializeField] private Vector3 m_LeftNotReadyIkLocalPosition;
	[SerializeField] private Vector3 m_LeftNotReadyIkLocalEulerAngles;
	[SerializeField] private Vector3 m_LeftReadyIkLocalPosition;
	[SerializeField] private Vector3 m_LeftReadyIkLocalEulerAngles;

	[Header("Captured — crouch weapon pose (Hand_R local)")]
	[SerializeField] private Vector3 m_CrouchNotReadyLocalPosition;
	[SerializeField] private Vector3 m_CrouchNotReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_CrouchReadyLocalPosition;
	[SerializeField] private Vector3 m_CrouchReadyLocalEulerAngles;

	[Header("Captured — crouch right hand IK (weapon local)")]
	[SerializeField] private Vector3 m_CrouchNotReadyIkLocalPosition;
	[SerializeField] private Vector3 m_CrouchNotReadyIkLocalEulerAngles;
	[SerializeField] private Vector3 m_CrouchReadyIkLocalPosition;
	[SerializeField] private Vector3 m_CrouchReadyIkLocalEulerAngles;

	[Header("Captured — crouch left hand IK (weapon local)")]
	[SerializeField] private Vector3 m_CrouchLeftNotReadyIkLocalPosition;
	[SerializeField] private Vector3 m_CrouchLeftNotReadyIkLocalEulerAngles;
	[SerializeField] private Vector3 m_CrouchLeftReadyIkLocalPosition;
	[SerializeField] private Vector3 m_CrouchLeftReadyIkLocalEulerAngles;

	[Header("Captured — vehicle weapon pose (Hand_R local)")]
	[SerializeField] private Vector3 m_VehicleNotReadyLocalPosition;
	[SerializeField] private Vector3 m_VehicleNotReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_VehicleReadyLocalPosition;
	[SerializeField] private Vector3 m_VehicleReadyLocalEulerAngles;

	[Header("Captured — vehicle right hand IK (weapon local)")]
	[SerializeField] private Vector3 m_VehicleNotReadyIkLocalPosition;
	[SerializeField] private Vector3 m_VehicleNotReadyIkLocalEulerAngles;
	[SerializeField] private Vector3 m_VehicleReadyIkLocalPosition;
	[SerializeField] private Vector3 m_VehicleReadyIkLocalEulerAngles;

	[Header("Captured — vehicle left hand IK (weapon local)")]
	[SerializeField] private Vector3 m_VehicleLeftNotReadyIkLocalPosition;
	[SerializeField] private Vector3 m_VehicleLeftNotReadyIkLocalEulerAngles;
	[SerializeField] private Vector3 m_VehicleLeftReadyIkLocalPosition;
	[SerializeField] private Vector3 m_VehicleLeftReadyIkLocalEulerAngles;
	#endregion

	#region Private Fields
	private TuningTarget m_LastAppliedTarget = (TuningTarget)(-1);
	private TuningPosture m_LastAppliedPosture = (TuningPosture)(-1);
	private bool m_WasTuningActive;

	private static readonly Vector3 s_TemplateWeaponLocalPosition = new Vector3(0.05f, 0.02f, 0.08f);
	private static readonly Vector3 s_TemplateWeaponLocalEuler = new Vector3(-10f, 90f, 90f);
	#endregion

	#region Public Properties
	public bool IsTuningActive => m_EnableRuntimeTuning && Application.isPlaying;

	/// <summary>Hands Frozen: IK off so the weapon root can be placed freely.</summary>
	public bool ShouldDisableAllHandIk => IsTuningActive && m_ActiveTarget == TuningTarget.HandsFrozen;

	/// <summary>Not Ready / Ready: force right-hand IK on so hands follow the empties while tuning.</summary>
	public bool ForcesRightHandIk => IsTuningActive && m_ActiveTarget != TuningTarget.HandsFrozen;

	public TuningTarget ActiveTarget => m_ActiveTarget;
	public TuningPosture ActivePosture => m_ActivePosture;

	/// <summary>
	/// Rocket launchers only author Ready IK — always drive ready targets while tuning (except Hands Frozen).
	/// Regular weapons: 0 in Not Ready, 1 in Ready.
	/// </summary>
	public float ForcedReadyBlend01
	{
		get
		{
			if (UsesRocketLauncherContext)
				return m_ActiveTarget == TuningTarget.HandsFrozen ? 0f : 1f;

			return m_ActiveTarget == TuningTarget.Ready ? 1f : 0f;
		}
	}

	public Vector3 StandingNotReadyLocalPosition => m_NotReadyLocalPosition;
	public Vector3 StandingNotReadyLocalEulerAngles => m_NotReadyLocalEulerAngles;
	public Vector3 StandingReadyLocalPosition => m_ReadyLocalPosition;
	public Vector3 StandingReadyLocalEulerAngles => m_ReadyLocalEulerAngles;
	public Vector3 StandingNotReadyIkLocalPosition => m_NotReadyIkLocalPosition;
	public Vector3 StandingNotReadyIkLocalEulerAngles => m_NotReadyIkLocalEulerAngles;
	public Vector3 StandingReadyIkLocalPosition => m_ReadyIkLocalPosition;
	public Vector3 StandingReadyIkLocalEulerAngles => m_ReadyIkLocalEulerAngles;
	public Vector3 StandingLeftNotReadyIkLocalPosition => m_LeftNotReadyIkLocalPosition;
	public Vector3 StandingLeftNotReadyIkLocalEulerAngles => m_LeftNotReadyIkLocalEulerAngles;
	public Vector3 StandingLeftReadyIkLocalPosition => m_LeftReadyIkLocalPosition;
	public Vector3 StandingLeftReadyIkLocalEulerAngles => m_LeftReadyIkLocalEulerAngles;

	public Vector3 CrouchNotReadyLocalPosition => m_CrouchNotReadyLocalPosition;
	public Vector3 CrouchNotReadyLocalEulerAngles => m_CrouchNotReadyLocalEulerAngles;
	public Vector3 CrouchReadyLocalPosition => m_CrouchReadyLocalPosition;
	public Vector3 CrouchReadyLocalEulerAngles => m_CrouchReadyLocalEulerAngles;
	public Vector3 CrouchNotReadyIkLocalPosition => m_CrouchNotReadyIkLocalPosition;
	public Vector3 CrouchNotReadyIkLocalEulerAngles => m_CrouchNotReadyIkLocalEulerAngles;
	public Vector3 CrouchReadyIkLocalPosition => m_CrouchReadyIkLocalPosition;
	public Vector3 CrouchReadyIkLocalEulerAngles => m_CrouchReadyIkLocalEulerAngles;
	public Vector3 CrouchLeftNotReadyIkLocalPosition => m_CrouchLeftNotReadyIkLocalPosition;
	public Vector3 CrouchLeftNotReadyIkLocalEulerAngles => m_CrouchLeftNotReadyIkLocalEulerAngles;
	public Vector3 CrouchLeftReadyIkLocalPosition => m_CrouchLeftReadyIkLocalPosition;
	public Vector3 CrouchLeftReadyIkLocalEulerAngles => m_CrouchLeftReadyIkLocalEulerAngles;

	public Vector3 VehicleNotReadyLocalPosition => m_VehicleNotReadyLocalPosition;
	public Vector3 VehicleNotReadyLocalEulerAngles => m_VehicleNotReadyLocalEulerAngles;
	public Vector3 VehicleReadyLocalPosition => m_VehicleReadyLocalPosition;
	public Vector3 VehicleReadyLocalEulerAngles => m_VehicleReadyLocalEulerAngles;
	public Vector3 VehicleNotReadyIkLocalPosition => m_VehicleNotReadyIkLocalPosition;
	public Vector3 VehicleNotReadyIkLocalEulerAngles => m_VehicleNotReadyIkLocalEulerAngles;
	public Vector3 VehicleReadyIkLocalPosition => m_VehicleReadyIkLocalPosition;
	public Vector3 VehicleReadyIkLocalEulerAngles => m_VehicleReadyIkLocalEulerAngles;
	public Vector3 VehicleLeftNotReadyIkLocalPosition => m_VehicleLeftNotReadyIkLocalPosition;
	public Vector3 VehicleLeftNotReadyIkLocalEulerAngles => m_VehicleLeftNotReadyIkLocalEulerAngles;
	public Vector3 VehicleLeftReadyIkLocalPosition => m_VehicleLeftReadyIkLocalPosition;
	public Vector3 VehicleLeftReadyIkLocalEulerAngles => m_VehicleLeftReadyIkLocalEulerAngles;

	public Vector3 NotReadyLocalPosition => ResolveByPosture(
		m_NotReadyLocalPosition, m_CrouchNotReadyLocalPosition, m_VehicleNotReadyLocalPosition);
	public Vector3 NotReadyLocalEulerAngles => ResolveByPosture(
		m_NotReadyLocalEulerAngles, m_CrouchNotReadyLocalEulerAngles, m_VehicleNotReadyLocalEulerAngles);
	public Vector3 ReadyLocalPosition => ResolveByPosture(
		m_ReadyLocalPosition, m_CrouchReadyLocalPosition, m_VehicleReadyLocalPosition);
	public Vector3 ReadyLocalEulerAngles => ResolveByPosture(
		m_ReadyLocalEulerAngles, m_CrouchReadyLocalEulerAngles, m_VehicleReadyLocalEulerAngles);
	public Vector3 NotReadyIkLocalPosition => ResolveByPosture(
		m_NotReadyIkLocalPosition, m_CrouchNotReadyIkLocalPosition, m_VehicleNotReadyIkLocalPosition);
	public Vector3 NotReadyIkLocalEulerAngles => ResolveByPosture(
		m_NotReadyIkLocalEulerAngles, m_CrouchNotReadyIkLocalEulerAngles, m_VehicleNotReadyIkLocalEulerAngles);
	public Vector3 ReadyIkLocalPosition => ResolveByPosture(
		m_ReadyIkLocalPosition, m_CrouchReadyIkLocalPosition, m_VehicleReadyIkLocalPosition);
	public Vector3 ReadyIkLocalEulerAngles => ResolveByPosture(
		m_ReadyIkLocalEulerAngles, m_CrouchReadyIkLocalEulerAngles, m_VehicleReadyIkLocalEulerAngles);
	public Vector3 LeftNotReadyIkLocalPosition => ResolveByPosture(
		m_LeftNotReadyIkLocalPosition, m_CrouchLeftNotReadyIkLocalPosition, m_VehicleLeftNotReadyIkLocalPosition);
	public Vector3 LeftNotReadyIkLocalEulerAngles => ResolveByPosture(
		m_LeftNotReadyIkLocalEulerAngles, m_CrouchLeftNotReadyIkLocalEulerAngles, m_VehicleLeftNotReadyIkLocalEulerAngles);
	public Vector3 LeftReadyIkLocalPosition => ResolveByPosture(
		m_LeftReadyIkLocalPosition, m_CrouchLeftReadyIkLocalPosition, m_VehicleLeftReadyIkLocalPosition);
	public Vector3 LeftReadyIkLocalEulerAngles => ResolveByPosture(
		m_LeftReadyIkLocalEulerAngles, m_CrouchLeftReadyIkLocalEulerAngles, m_VehicleLeftReadyIkLocalEulerAngles);
	public UnitEquipment UnitEquipment => m_UnitEquipment;
	public bool UsesRocketLauncherContext =>
		m_RocketLauncherOrder != null &&
		m_RocketLauncherOrder.IsBusy &&
		m_RocketLauncherOrder.HandLauncherRoot != null;
	public ItemDefinition ActiveTuningDefinition => UsesRocketLauncherContext
		? m_RocketLauncherOrder.ActiveLauncherDefinition
		: m_UnitEquipment != null ? m_UnitEquipment.EquippedDefinition : null;

	/// <summary>
	/// Left-hand IK empties live on the installed foregrip visual (not the weapon body).
	/// In that case ItemDefinition left IK coords must not override / be overwritten by Save.
	/// </summary>
	public bool IsLeftHandIkDrivenByForegrip
	{
		get
		{
			if (UsesRocketLauncherContext)
				return false;

			Transform foregripRoot = GetForegripVisualRoot();
			if (foregripRoot == null)
				return false;

			return FindChildRecursive(foregripRoot, "LeftHandIkTarget") != null
			       || FindChildRecursive(foregripRoot, "LeftHandIkTarget_NotReady") != null;
		}
	}

	/// <summary>Installed under-barrel foregrip visual root, if any.</summary>
	public Transform GetForegripVisualRoot()
	{
		EquippedWeapon equippedWeapon = m_UnitEquipment != null ? m_UnitEquipment.EquippedWeapon : null;
		return equippedWeapon != null ? equippedWeapon.UnderBarrelForegripVisualRoot : null;
	}

	/// <summary>When tuning: do not overwrite MainWeaponRoot — user moves it in Hierarchy.</summary>
	public bool ShouldSkipWeaponPoseWrite => IsTuningActive;

	public Transform GetActiveWeaponRoot()
	{
		return UsesRocketLauncherContext
			? m_RocketLauncherOrder.HandLauncherRoot
			: m_UnitEquipment != null ? m_UnitEquipment.MainWeaponRoot : null;
	}

	public Transform GetRightHandIkTargetTransform() => UsesRocketLauncherContext
		? m_RocketLauncherOrder.RightHandIkTargetTransform
		: m_UnitEquipment != null ? m_UnitEquipment.RightHandIkTargetTransform : null;

	public Transform GetRightHandIkTargetNotReadyTransform() => UsesRocketLauncherContext
		? m_RocketLauncherOrder.RightHandIkTargetNotReadyTransform
		: m_UnitEquipment != null ? m_UnitEquipment.RightHandIkTargetNotReadyTransform : null;

	public Transform GetLeftHandIkTargetTransform() => UsesRocketLauncherContext
		? m_RocketLauncherOrder.LeftHandIkTargetTransform
		: m_UnitEquipment != null ? m_UnitEquipment.LeftHandIkTargetTransform : null;

	public Transform GetLeftHandIkTargetNotReadyTransform() => UsesRocketLauncherContext
		? m_RocketLauncherOrder.LeftHandIkTargetNotReadyTransform
		: m_UnitEquipment != null ? m_UnitEquipment.LeftHandIkTargetNotReadyTransform : null;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveReferences();
	}

	private void OnEnable()
	{
		ResolveReferences();
		SubscribeEquipmentEvents();
	}

	private void OnDisable()
	{
		UnsubscribeEquipmentEvents();
	}

	private void LateUpdate()
	{
		if (!Application.isPlaying)
			return;

		bool tuning = IsTuningActive;
		if (tuning && !m_WasTuningActive)
			BeginTuningSession();
		else if (!tuning && m_WasTuningActive)
			EndTuningSession();

		m_WasTuningActive = tuning;
		if (!tuning)
			return;

		if (m_ActiveTarget != m_LastAppliedTarget || m_ActivePosture != m_LastAppliedPosture)
			ApplyActiveTargetSwitch();

		CaptureLiveWeaponPoseFromScene();
		// Always capture all four IK targets (L/R × ready/not-ready), even in Hands Frozen.
		CaptureLiveIkFromScene();
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Ensure Left/Right HandIkTarget and *_NotReady empties exist, then refresh caches.
	/// Right IK → weapon body. Left IK → foregrip visual when present, else weapon body.
	/// </summary>
	public void EnsureAllHandIkTargetsExist()
	{
		if (UsesRocketLauncherContext)
		{
			m_RocketLauncherOrder.EnsureHandIkTargetsExist();
			m_RocketLauncherOrder.RefreshHandIkTargets();
			return;
		}

		if (m_UnitEquipment == null)
			return;

		Transform weaponRoot = GetActiveWeaponRoot();
		ItemDefinition def = ActiveTuningDefinition;
		if (weaponRoot == null || def == null)
			return;

		EnsureChildEmpty(weaponRoot, def.RightHandIkTargetChildName, ReadyIkLocalPosition, ReadyIkLocalEulerAngles);
		EnsureChildEmpty(weaponRoot, def.RightHandIkTargetNotReadyChildName, NotReadyIkLocalPosition, NotReadyIkLocalEulerAngles);

		EquippedWeapon equippedWeapon = m_UnitEquipment.EquippedWeapon;
		Transform leftParent = equippedWeapon != null && equippedWeapon.UnderBarrelForegripVisualRoot != null
			? equippedWeapon.UnderBarrelForegripVisualRoot
			: weaponRoot;

		EnsureChildEmpty(leftParent, def.LeftHandIkTargetChildName, LeftReadyIkLocalPosition, LeftReadyIkLocalEulerAngles);

		string leftNotReadyName = def.LeftHandIkTargetNotReadyChildName;
		if (string.IsNullOrWhiteSpace(leftNotReadyName))
			leftNotReadyName = "LeftHandIkTarget_NotReady";
		EnsureChildEmpty(leftParent, leftNotReadyName, LeftNotReadyIkLocalPosition, LeftNotReadyIkLocalEulerAngles);

		m_UnitEquipment.RefreshHandIkTargets();
	}

	/// <summary>Capture weapon pose + all four hand IK targets from Hierarchy.</summary>
	public void CaptureAllForSave()
	{
		EnsureAllHandIkTargetsExist();
		CaptureLiveWeaponPoseFromScene();
		CaptureLiveIkFromScene();
	}

	public void GetOverridePoses(
		out Vector3 _relaxedPosition,
		out Quaternion _relaxedRotation,
		out Vector3 _readyPosition,
		out Quaternion _readyRotation,
		out float _forcedBlend01)
	{
		_relaxedPosition = NotReadyLocalPosition;
		_relaxedRotation = Quaternion.Euler(NotReadyLocalEulerAngles);
		_readyPosition = ReadyLocalPosition;
		_readyRotation = Quaternion.Euler(ReadyLocalEulerAngles);
		_forcedBlend01 = ForcedReadyBlend01;
	}

	public void LoadFromEquippedDefinition()
	{
		ItemDefinition def = ActiveTuningDefinition;
		if (def == null)
			return;

		LoadStandingFromDefinition(def);
		LoadCrouchFromDefinition(def);
		LoadVehicleFromDefinition(def);

		CaptureIkFromTargetsIfUnset();
		ApplyActiveTargetPoseToWeapon();
		ApplyStoredIkToTargets();
		m_LastAppliedTarget = m_ActiveTarget;
		m_LastAppliedPosture = m_ActivePosture;
	}

	public void LoadStandingFromDefinition(ItemDefinition _def)
	{
		if (_def == null)
			return;

		m_NotReadyLocalPosition = _def.RightHandLocalPosition;
		m_NotReadyLocalEulerAngles = _def.RightHandLocalEulerAngles;

		m_ReadyLocalPosition = _def.RightHandReadyLocalPosition;
		m_ReadyLocalEulerAngles = _def.RightHandReadyLocalEulerAngles;
		if (ShouldSeedReadyWeaponPoseFromNotReady(
			    m_NotReadyLocalPosition,
			    m_NotReadyLocalEulerAngles,
			    m_ReadyLocalPosition,
			    m_ReadyLocalEulerAngles))
		{
			m_ReadyLocalPosition = m_NotReadyLocalPosition;
			m_ReadyLocalEulerAngles = m_NotReadyLocalEulerAngles;
		}

		m_NotReadyIkLocalPosition = _def.RightHandIkNotReadyLocalPosition;
		m_NotReadyIkLocalEulerAngles = _def.RightHandIkNotReadyLocalEulerAngles;
		m_ReadyIkLocalPosition = _def.RightHandIkReadyLocalPosition;
		m_ReadyIkLocalEulerAngles = _def.RightHandIkReadyLocalEulerAngles;

		if (!IsLeftHandIkDrivenByForegrip)
		{
			m_LeftNotReadyIkLocalPosition = _def.LeftHandIkNotReadyLocalPosition;
			m_LeftNotReadyIkLocalEulerAngles = _def.LeftHandIkNotReadyLocalEulerAngles;
			m_LeftReadyIkLocalPosition = _def.LeftHandIkReadyLocalPosition;
			m_LeftReadyIkLocalEulerAngles = _def.LeftHandIkReadyLocalEulerAngles;
		}
	}

	public void LoadCrouchFromDefinition(ItemDefinition _def)
	{
		if (_def == null)
			return;

		m_CrouchNotReadyLocalPosition = _def.CrouchRightHandLocalPosition;
		m_CrouchNotReadyLocalEulerAngles = _def.CrouchRightHandLocalEulerAngles;

		m_CrouchReadyLocalPosition = _def.CrouchRightHandReadyLocalPosition;
		m_CrouchReadyLocalEulerAngles = _def.CrouchRightHandReadyLocalEulerAngles;
		if (ShouldSeedReadyWeaponPoseFromNotReady(
			    m_CrouchNotReadyLocalPosition,
			    m_CrouchNotReadyLocalEulerAngles,
			    m_CrouchReadyLocalPosition,
			    m_CrouchReadyLocalEulerAngles))
		{
			m_CrouchReadyLocalPosition = m_CrouchNotReadyLocalPosition;
			m_CrouchReadyLocalEulerAngles = m_CrouchNotReadyLocalEulerAngles;
		}

		m_CrouchNotReadyIkLocalPosition = _def.CrouchRightHandIkNotReadyLocalPosition;
		m_CrouchNotReadyIkLocalEulerAngles = _def.CrouchRightHandIkNotReadyLocalEulerAngles;
		m_CrouchReadyIkLocalPosition = _def.CrouchRightHandIkReadyLocalPosition;
		m_CrouchReadyIkLocalEulerAngles = _def.CrouchRightHandIkReadyLocalEulerAngles;

		if (!IsLeftHandIkDrivenByForegrip)
		{
			m_CrouchLeftNotReadyIkLocalPosition = _def.CrouchLeftHandIkNotReadyLocalPosition;
			m_CrouchLeftNotReadyIkLocalEulerAngles = _def.CrouchLeftHandIkNotReadyLocalEulerAngles;
			m_CrouchLeftReadyIkLocalPosition = _def.CrouchLeftHandIkReadyLocalPosition;
			m_CrouchLeftReadyIkLocalEulerAngles = _def.CrouchLeftHandIkReadyLocalEulerAngles;
		}
	}

	public void LoadVehicleFromDefinition(ItemDefinition _def)
	{
		if (_def == null)
			return;

		m_VehicleNotReadyLocalPosition = _def.VehicleRightHandLocalPosition;
		m_VehicleNotReadyLocalEulerAngles = _def.VehicleRightHandLocalEulerAngles;

		m_VehicleReadyLocalPosition = _def.VehicleRightHandReadyLocalPosition;
		m_VehicleReadyLocalEulerAngles = _def.VehicleRightHandReadyLocalEulerAngles;
		if (ShouldSeedReadyWeaponPoseFromNotReady(
			    m_VehicleNotReadyLocalPosition,
			    m_VehicleNotReadyLocalEulerAngles,
			    m_VehicleReadyLocalPosition,
			    m_VehicleReadyLocalEulerAngles))
		{
			m_VehicleReadyLocalPosition = m_VehicleNotReadyLocalPosition;
			m_VehicleReadyLocalEulerAngles = m_VehicleNotReadyLocalEulerAngles;
		}

		m_VehicleNotReadyIkLocalPosition = _def.VehicleRightHandIkNotReadyLocalPosition;
		m_VehicleNotReadyIkLocalEulerAngles = _def.VehicleRightHandIkNotReadyLocalEulerAngles;
		m_VehicleReadyIkLocalPosition = _def.VehicleRightHandIkReadyLocalPosition;
		m_VehicleReadyIkLocalEulerAngles = _def.VehicleRightHandIkReadyLocalEulerAngles;

		if (!IsLeftHandIkDrivenByForegrip)
		{
			m_VehicleLeftNotReadyIkLocalPosition = _def.VehicleLeftHandIkNotReadyLocalPosition;
			m_VehicleLeftNotReadyIkLocalEulerAngles = _def.VehicleLeftHandIkNotReadyLocalEulerAngles;
			m_VehicleLeftReadyIkLocalPosition = _def.VehicleLeftHandIkReadyLocalPosition;
			m_VehicleLeftReadyIkLocalEulerAngles = _def.VehicleLeftHandIkReadyLocalEulerAngles;
		}
	}

	/// <summary>Copy standing captured buffers into vehicle buffers (tuner only).</summary>
	public void CopyStandingCaptureToVehicleCapture()
	{
		m_VehicleNotReadyLocalPosition = m_NotReadyLocalPosition;
		m_VehicleNotReadyLocalEulerAngles = m_NotReadyLocalEulerAngles;
		m_VehicleReadyLocalPosition = m_ReadyLocalPosition;
		m_VehicleReadyLocalEulerAngles = m_ReadyLocalEulerAngles;
		m_VehicleNotReadyIkLocalPosition = m_NotReadyIkLocalPosition;
		m_VehicleNotReadyIkLocalEulerAngles = m_NotReadyIkLocalEulerAngles;
		m_VehicleReadyIkLocalPosition = m_ReadyIkLocalPosition;
		m_VehicleReadyIkLocalEulerAngles = m_ReadyIkLocalEulerAngles;
		m_VehicleLeftNotReadyIkLocalPosition = m_LeftNotReadyIkLocalPosition;
		m_VehicleLeftNotReadyIkLocalEulerAngles = m_LeftNotReadyIkLocalEulerAngles;
		m_VehicleLeftReadyIkLocalPosition = m_LeftReadyIkLocalPosition;
		m_VehicleLeftReadyIkLocalEulerAngles = m_LeftReadyIkLocalEulerAngles;

		if (m_ActivePosture == TuningPosture.Vehicle)
		{
			ApplyActiveTargetPoseToWeapon();
			ApplyStoredIkToTargets();
		}
	}
	public void CopyStandingCaptureToCrouchCapture()
	{
		m_CrouchNotReadyLocalPosition = m_NotReadyLocalPosition;
		m_CrouchNotReadyLocalEulerAngles = m_NotReadyLocalEulerAngles;
		m_CrouchReadyLocalPosition = m_ReadyLocalPosition;
		m_CrouchReadyLocalEulerAngles = m_ReadyLocalEulerAngles;
		m_CrouchNotReadyIkLocalPosition = m_NotReadyIkLocalPosition;
		m_CrouchNotReadyIkLocalEulerAngles = m_NotReadyIkLocalEulerAngles;
		m_CrouchReadyIkLocalPosition = m_ReadyIkLocalPosition;
		m_CrouchReadyIkLocalEulerAngles = m_ReadyIkLocalEulerAngles;
		m_CrouchLeftNotReadyIkLocalPosition = m_LeftNotReadyIkLocalPosition;
		m_CrouchLeftNotReadyIkLocalEulerAngles = m_LeftNotReadyIkLocalEulerAngles;
		m_CrouchLeftReadyIkLocalPosition = m_LeftReadyIkLocalPosition;
		m_CrouchLeftReadyIkLocalEulerAngles = m_LeftReadyIkLocalEulerAngles;

		if (m_ActivePosture == TuningPosture.Crouch)
		{
			ApplyActiveTargetPoseToWeapon();
			ApplyStoredIkToTargets();
		}
	}

	public void CaptureLiveWeaponPoseFromScene()
	{
		Transform weaponRoot = GetActiveWeaponRoot();
		if (weaponRoot == null)
			return;

		if (m_ActiveTarget == TuningTarget.Ready)
		{
			SetActiveReadyLocalPosition(weaponRoot.localPosition);
			SetActiveReadyLocalEulerAngles(weaponRoot.localEulerAngles);
			return;
		}

		// HandsFrozen + NotReady both write the base / not-ready weapon pose (first coordinates).
		SetActiveNotReadyLocalPosition(weaponRoot.localPosition);
		SetActiveNotReadyLocalEulerAngles(weaponRoot.localEulerAngles);
	}

	public void CaptureLiveIkFromScene()
	{
		// Rocket launchers: only Ready IK is authored — never capture Not Ready empties.
		bool rocket = UsesRocketLauncherContext;
		bool captureNotReady = !rocket && (!IsTuningActive || m_ActiveTarget == TuningTarget.NotReady);
		bool captureReady = !IsTuningActive || m_ActiveTarget == TuningTarget.Ready || rocket;

		if (captureNotReady)
		{
			Transform notReady = GetRightHandIkTargetNotReadyTransform();
			if (notReady != null)
			{
				SetActiveNotReadyIkLocalPosition(notReady.localPosition);
				SetActiveNotReadyIkLocalEulerAngles(notReady.localEulerAngles);
			}
		}

		if (captureReady)
		{
			Transform ready = GetRightHandIkTargetTransform();
			if (ready != null)
			{
				SetActiveReadyIkLocalPosition(ready.localPosition);
				SetActiveReadyIkLocalEulerAngles(ready.localEulerAngles);
			}
		}

		if (IsLeftHandIkDrivenByForegrip && m_ActivePosture == TuningPosture.Standing)
			return;

		if (captureNotReady)
		{
			Transform leftNotReady = GetLeftHandIkTargetNotReadyTransform();
			if (leftNotReady != null)
			{
				SetActiveLeftNotReadyIkLocalPosition(leftNotReady.localPosition);
				SetActiveLeftNotReadyIkLocalEulerAngles(leftNotReady.localEulerAngles);
			}
		}

		if (captureReady)
		{
			Transform leftReady = GetLeftHandIkTargetTransform();
			if (leftReady != null)
			{
				SetActiveLeftReadyIkLocalPosition(leftReady.localPosition);
				SetActiveLeftReadyIkLocalEulerAngles(leftReady.localEulerAngles);
			}
		}
	}

	public void ApplyActiveTargetPoseToWeapon()
	{
		Transform weaponRoot = GetActiveWeaponRoot();
		if (weaponRoot == null)
			return;

		if (m_ActiveTarget == TuningTarget.Ready)
		{
			weaponRoot.localPosition = ReadyLocalPosition;
			weaponRoot.localRotation = Quaternion.Euler(ReadyLocalEulerAngles);
			return;
		}

		weaponRoot.localPosition = NotReadyLocalPosition;
		weaponRoot.localRotation = Quaternion.Euler(NotReadyLocalEulerAngles);
	}

	public void ApplyStoredIkToTargets()
	{
		if (IsTuningActive && m_ActiveTarget == TuningTarget.HandsFrozen)
			return;

		bool rocket = UsesRocketLauncherContext;
		// Rocket launchers: only Ready IK empties are authored / written.
		bool writeNotReadyRight = !rocket && (!IsTuningActive || m_ActiveTarget == TuningTarget.NotReady);
		bool writeReadyRight = !IsTuningActive || m_ActiveTarget == TuningTarget.Ready || rocket;
		bool writeNotReadyLeft = writeNotReadyRight;
		bool writeReadyLeft = writeReadyRight;

		if (writeNotReadyRight)
		{
			Transform notReady = GetRightHandIkTargetNotReadyTransform();
			if (notReady != null)
			{
				notReady.localPosition = NotReadyIkLocalPosition;
				notReady.localRotation = Quaternion.Euler(NotReadyIkLocalEulerAngles);
			}
		}

		if (writeReadyRight)
		{
			Transform ready = GetRightHandIkTargetTransform();
			if (ready != null)
			{
				ready.localPosition = ReadyIkLocalPosition;
				ready.localRotation = Quaternion.Euler(ReadyIkLocalEulerAngles);
			}
		}

		// Never overwrite foregrip LeftHandIkTarget* from weapon-asset coords while tuning standing
		// and foregrip owns left IK — unless editing crouch left IK from ItemDefinition buffers.
		if (IsLeftHandIkDrivenByForegrip && m_ActivePosture == TuningPosture.Standing)
			return;

		Transform leftNotReady = GetLeftHandIkTargetNotReadyTransform();
		if (writeNotReadyLeft && leftNotReady != null)
		{
			leftNotReady.localPosition = LeftNotReadyIkLocalPosition;
			leftNotReady.localRotation = Quaternion.Euler(LeftNotReadyIkLocalEulerAngles);
		}

		Transform leftReady = GetLeftHandIkTargetTransform();
		if (writeReadyLeft && leftReady != null)
		{
			leftReady.localPosition = LeftReadyIkLocalPosition;
			leftReady.localRotation = Quaternion.Euler(LeftReadyIkLocalEulerAngles);
		}
	}

	public Transform GetActiveIkTargetTransform()
	{
		if (m_ActiveTarget == TuningTarget.HandsFrozen)
			return null;

		return m_ActiveTarget == TuningTarget.NotReady
			? GetRightHandIkTargetNotReadyTransform()
			: GetRightHandIkTargetTransform();
	}

	/// <summary>Copy current base (not-ready) weapon pose into ready pose as a starting point.</summary>
	public void CopyBaseWeaponPoseToReady()
	{
		SetActiveReadyLocalPosition(NotReadyLocalPosition);
		SetActiveReadyLocalEulerAngles(NotReadyLocalEulerAngles);
		if (m_ActiveTarget == TuningTarget.Ready)
			ApplyActiveTargetPoseToWeapon();
	}

	/// <summary>Copy current not-ready hand IK into ready hand IK as a starting point.</summary>
	public void CopyBaseIkPoseToReady()
	{
		SetActiveReadyIkLocalPosition(NotReadyIkLocalPosition);
		SetActiveReadyIkLocalEulerAngles(NotReadyIkLocalEulerAngles);
		SetActiveLeftReadyIkLocalPosition(LeftNotReadyIkLocalPosition);
		SetActiveLeftReadyIkLocalEulerAngles(LeftNotReadyIkLocalEulerAngles);
		if (m_ActiveTarget == TuningTarget.Ready)
			ApplyStoredIkToTargets();
	}

	/// <summary>Copy not-ready weapon + IK buffers into ready buffers.</summary>
	public void CopyBasePoseToReady(bool _includeIk = true)
	{
		CopyBaseWeaponPoseToReady();
		if (_includeIk)
			CopyBaseIkPoseToReady();
	}

	/// <summary>Called when Active Target / Posture changes while tuning is active.</summary>
	public void ApplyActiveTargetSwitch()
	{
		if (m_LastAppliedTarget == TuningTarget.HandsFrozen && m_ActiveTarget == TuningTarget.Ready)
		{
			// Weapon pose: copy Frozen → Ready. Rocket IK: keep Ready empties (do not seed from Not Ready).
			CopyBaseWeaponPoseToReady();
			if (!UsesRocketLauncherContext)
				CopyBaseIkPoseToReady();
		}

		ApplyActiveTargetPoseToWeapon();
		ApplyStoredIkToTargets();
		m_LastAppliedTarget = m_ActiveTarget;
		m_LastAppliedPosture = m_ActivePosture;
	}

	public string BuildYamlSnippet(TuningPosture _posture)
	{
		if (_posture == TuningPosture.Crouch)
		{
			return
				$"  m_CrouchRightHandLocalPosition: {{x: {Format(m_CrouchNotReadyLocalPosition.x)}, y: {Format(m_CrouchNotReadyLocalPosition.y)}, z: {Format(m_CrouchNotReadyLocalPosition.z)}}}\n" +
				$"  m_CrouchRightHandLocalEulerAngles: {{x: {Format(m_CrouchNotReadyLocalEulerAngles.x)}, y: {Format(m_CrouchNotReadyLocalEulerAngles.y)}, z: {Format(m_CrouchNotReadyLocalEulerAngles.z)}}}\n" +
				$"  m_CrouchRightHandReadyLocalPosition: {{x: {Format(m_CrouchReadyLocalPosition.x)}, y: {Format(m_CrouchReadyLocalPosition.y)}, z: {Format(m_CrouchReadyLocalPosition.z)}}}\n" +
				$"  m_CrouchRightHandReadyLocalEulerAngles: {{x: {Format(m_CrouchReadyLocalEulerAngles.x)}, y: {Format(m_CrouchReadyLocalEulerAngles.y)}, z: {Format(m_CrouchReadyLocalEulerAngles.z)}}}\n" +
				$"  m_CrouchRightHandIkNotReadyLocalPosition: {{x: {Format(m_CrouchNotReadyIkLocalPosition.x)}, y: {Format(m_CrouchNotReadyIkLocalPosition.y)}, z: {Format(m_CrouchNotReadyIkLocalPosition.z)}}}\n" +
				$"  m_CrouchRightHandIkNotReadyLocalEulerAngles: {{x: {Format(m_CrouchNotReadyIkLocalEulerAngles.x)}, y: {Format(m_CrouchNotReadyIkLocalEulerAngles.y)}, z: {Format(m_CrouchNotReadyIkLocalEulerAngles.z)}}}\n" +
				$"  m_CrouchRightHandIkReadyLocalPosition: {{x: {Format(m_CrouchReadyIkLocalPosition.x)}, y: {Format(m_CrouchReadyIkLocalPosition.y)}, z: {Format(m_CrouchReadyIkLocalPosition.z)}}}\n" +
				$"  m_CrouchRightHandIkReadyLocalEulerAngles: {{x: {Format(m_CrouchReadyIkLocalEulerAngles.x)}, y: {Format(m_CrouchReadyIkLocalEulerAngles.y)}, z: {Format(m_CrouchReadyIkLocalEulerAngles.z)}}}\n" +
				$"  m_CrouchLeftHandIkNotReadyLocalPosition: {{x: {Format(m_CrouchLeftNotReadyIkLocalPosition.x)}, y: {Format(m_CrouchLeftNotReadyIkLocalPosition.y)}, z: {Format(m_CrouchLeftNotReadyIkLocalPosition.z)}}}\n" +
				$"  m_CrouchLeftHandIkNotReadyLocalEulerAngles: {{x: {Format(m_CrouchLeftNotReadyIkLocalEulerAngles.x)}, y: {Format(m_CrouchLeftNotReadyIkLocalEulerAngles.y)}, z: {Format(m_CrouchLeftNotReadyIkLocalEulerAngles.z)}}}\n" +
				$"  m_CrouchLeftHandIkReadyLocalPosition: {{x: {Format(m_CrouchLeftReadyIkLocalPosition.x)}, y: {Format(m_CrouchLeftReadyIkLocalPosition.y)}, z: {Format(m_CrouchLeftReadyIkLocalPosition.z)}}}\n" +
				$"  m_CrouchLeftHandIkReadyLocalEulerAngles: {{x: {Format(m_CrouchLeftReadyIkLocalEulerAngles.x)}, y: {Format(m_CrouchLeftReadyIkLocalEulerAngles.y)}, z: {Format(m_CrouchLeftReadyIkLocalEulerAngles.z)}}}";
		}

		if (_posture == TuningPosture.Vehicle)
		{
			return
				$"  m_VehicleRightHandLocalPosition: {{x: {Format(m_VehicleNotReadyLocalPosition.x)}, y: {Format(m_VehicleNotReadyLocalPosition.y)}, z: {Format(m_VehicleNotReadyLocalPosition.z)}}}\n" +
				$"  m_VehicleRightHandLocalEulerAngles: {{x: {Format(m_VehicleNotReadyLocalEulerAngles.x)}, y: {Format(m_VehicleNotReadyLocalEulerAngles.y)}, z: {Format(m_VehicleNotReadyLocalEulerAngles.z)}}}\n" +
				$"  m_VehicleRightHandReadyLocalPosition: {{x: {Format(m_VehicleReadyLocalPosition.x)}, y: {Format(m_VehicleReadyLocalPosition.y)}, z: {Format(m_VehicleReadyLocalPosition.z)}}}\n" +
				$"  m_VehicleRightHandReadyLocalEulerAngles: {{x: {Format(m_VehicleReadyLocalEulerAngles.x)}, y: {Format(m_VehicleReadyLocalEulerAngles.y)}, z: {Format(m_VehicleReadyLocalEulerAngles.z)}}}\n" +
				$"  m_VehicleRightHandIkNotReadyLocalPosition: {{x: {Format(m_VehicleNotReadyIkLocalPosition.x)}, y: {Format(m_VehicleNotReadyIkLocalPosition.y)}, z: {Format(m_VehicleNotReadyIkLocalPosition.z)}}}\n" +
				$"  m_VehicleRightHandIkNotReadyLocalEulerAngles: {{x: {Format(m_VehicleNotReadyIkLocalEulerAngles.x)}, y: {Format(m_VehicleNotReadyIkLocalEulerAngles.y)}, z: {Format(m_VehicleNotReadyIkLocalEulerAngles.z)}}}\n" +
				$"  m_VehicleRightHandIkReadyLocalPosition: {{x: {Format(m_VehicleReadyIkLocalPosition.x)}, y: {Format(m_VehicleReadyIkLocalPosition.y)}, z: {Format(m_VehicleReadyIkLocalPosition.z)}}}\n" +
				$"  m_VehicleRightHandIkReadyLocalEulerAngles: {{x: {Format(m_VehicleReadyIkLocalEulerAngles.x)}, y: {Format(m_VehicleReadyIkLocalEulerAngles.y)}, z: {Format(m_VehicleReadyIkLocalEulerAngles.z)}}}\n" +
				$"  m_VehicleLeftHandIkNotReadyLocalPosition: {{x: {Format(m_VehicleLeftNotReadyIkLocalPosition.x)}, y: {Format(m_VehicleLeftNotReadyIkLocalPosition.y)}, z: {Format(m_VehicleLeftNotReadyIkLocalPosition.z)}}}\n" +
				$"  m_VehicleLeftHandIkNotReadyLocalEulerAngles: {{x: {Format(m_VehicleLeftNotReadyIkLocalEulerAngles.x)}, y: {Format(m_VehicleLeftNotReadyIkLocalEulerAngles.y)}, z: {Format(m_VehicleLeftNotReadyIkLocalEulerAngles.z)}}}\n" +
				$"  m_VehicleLeftHandIkReadyLocalPosition: {{x: {Format(m_VehicleLeftReadyIkLocalPosition.x)}, y: {Format(m_VehicleLeftReadyIkLocalPosition.y)}, z: {Format(m_VehicleLeftReadyIkLocalPosition.z)}}}\n" +
				$"  m_VehicleLeftHandIkReadyLocalEulerAngles: {{x: {Format(m_VehicleLeftReadyIkLocalEulerAngles.x)}, y: {Format(m_VehicleLeftReadyIkLocalEulerAngles.y)}, z: {Format(m_VehicleLeftReadyIkLocalEulerAngles.z)}}}";
		}

		return
			$"  m_RightHandLocalPosition: {{x: {Format(m_NotReadyLocalPosition.x)}, y: {Format(m_NotReadyLocalPosition.y)}, z: {Format(m_NotReadyLocalPosition.z)}}}\n" +
			$"  m_RightHandLocalEulerAngles: {{x: {Format(m_NotReadyLocalEulerAngles.x)}, y: {Format(m_NotReadyLocalEulerAngles.y)}, z: {Format(m_NotReadyLocalEulerAngles.z)}}}\n" +
			$"  m_RightHandReadyLocalPosition: {{x: {Format(m_ReadyLocalPosition.x)}, y: {Format(m_ReadyLocalPosition.y)}, z: {Format(m_ReadyLocalPosition.z)}}}\n" +
			$"  m_RightHandReadyLocalEulerAngles: {{x: {Format(m_ReadyLocalEulerAngles.x)}, y: {Format(m_ReadyLocalEulerAngles.y)}, z: {Format(m_ReadyLocalEulerAngles.z)}}}\n" +
			$"  m_RightHandIkNotReadyLocalPosition: {{x: {Format(m_NotReadyIkLocalPosition.x)}, y: {Format(m_NotReadyIkLocalPosition.y)}, z: {Format(m_NotReadyIkLocalPosition.z)}}}\n" +
			$"  m_RightHandIkNotReadyLocalEulerAngles: {{x: {Format(m_NotReadyIkLocalEulerAngles.x)}, y: {Format(m_NotReadyIkLocalEulerAngles.y)}, z: {Format(m_NotReadyIkLocalEulerAngles.z)}}}\n" +
			$"  m_RightHandIkReadyLocalPosition: {{x: {Format(m_ReadyIkLocalPosition.x)}, y: {Format(m_ReadyIkLocalPosition.y)}, z: {Format(m_ReadyIkLocalPosition.z)}}}\n" +
			$"  m_RightHandIkReadyLocalEulerAngles: {{x: {Format(m_ReadyIkLocalEulerAngles.x)}, y: {Format(m_ReadyIkLocalEulerAngles.y)}, z: {Format(m_ReadyIkLocalEulerAngles.z)}}}\n" +
			$"  m_LeftHandIkNotReadyLocalPosition: {{x: {Format(m_LeftNotReadyIkLocalPosition.x)}, y: {Format(m_LeftNotReadyIkLocalPosition.y)}, z: {Format(m_LeftNotReadyIkLocalPosition.z)}}}\n" +
			$"  m_LeftHandIkNotReadyLocalEulerAngles: {{x: {Format(m_LeftNotReadyIkLocalEulerAngles.x)}, y: {Format(m_LeftNotReadyIkLocalEulerAngles.y)}, z: {Format(m_LeftNotReadyIkLocalEulerAngles.z)}}}\n" +
			$"  m_LeftHandIkReadyLocalPosition: {{x: {Format(m_LeftReadyIkLocalPosition.x)}, y: {Format(m_LeftReadyIkLocalPosition.y)}, z: {Format(m_LeftReadyIkLocalPosition.z)}}}\n" +
			$"  m_LeftHandIkReadyLocalEulerAngles: {{x: {Format(m_LeftReadyIkLocalEulerAngles.x)}, y: {Format(m_LeftReadyIkLocalEulerAngles.y)}, z: {Format(m_LeftReadyIkLocalEulerAngles.z)}}}";
	}

	public string BuildYamlSnippet() => BuildYamlSnippet(m_ActivePosture);
	#endregion

	#region Private Methods
	private void BeginTuningSession()
	{
		m_ActiveTarget = TuningTarget.HandsFrozen;
		m_LastAppliedTarget = TuningTarget.HandsFrozen;
		LoadFromEquippedDefinition();
		Debug.Log(
			"[WeaponPoseTuner] ON — ActiveTarget reset to Hands Frozen. " +
			"Set Active Posture (Standing/Crouch/Vehicle), then Hands Frozen → Not Ready → Ready.",
			this);
	}

	private void EndTuningSession()
	{
		m_LastAppliedTarget = (TuningTarget)(-1);
		m_LastAppliedPosture = (TuningPosture)(-1);
		m_EquippedWeaponPose?.ApplyImmediateFromEquipment();
		Debug.Log("[WeaponPoseTuner] OFF — pose driven from ItemDefinition again.", this);
	}

	private Vector3 ResolveByPosture(Vector3 _standing, Vector3 _crouch, Vector3 _vehicle)
	{
		return m_ActivePosture == TuningPosture.Crouch ? _crouch
			: m_ActivePosture == TuningPosture.Vehicle ? _vehicle
			: _standing;
	}

	private T GetByPosture<T>(T _standing, T _crouch, T _vehicle)
	{
		return m_ActivePosture == TuningPosture.Crouch ? _crouch
			: m_ActivePosture == TuningPosture.Vehicle ? _vehicle
			: _standing;
	}

	private void SetByPosture(ref Vector3 _standing, ref Vector3 _crouch, ref Vector3 _vehicle, Vector3 _value)
	{
		switch (m_ActivePosture)
		{
			case TuningPosture.Crouch: _crouch = _value; break;
			case TuningPosture.Vehicle: _vehicle = _value; break;
			default: _standing = _value; break;
		}
	}

	private void SetActiveNotReadyLocalPosition(Vector3 _value)
		=> SetByPosture(ref m_NotReadyLocalPosition, ref m_CrouchNotReadyLocalPosition, ref m_VehicleNotReadyLocalPosition, _value);

	private void SetActiveNotReadyLocalEulerAngles(Vector3 _value)
		=> SetByPosture(ref m_NotReadyLocalEulerAngles, ref m_CrouchNotReadyLocalEulerAngles, ref m_VehicleNotReadyLocalEulerAngles, _value);

	private void SetActiveReadyLocalPosition(Vector3 _value)
		=> SetByPosture(ref m_ReadyLocalPosition, ref m_CrouchReadyLocalPosition, ref m_VehicleReadyLocalPosition, _value);

	private void SetActiveReadyLocalEulerAngles(Vector3 _value)
		=> SetByPosture(ref m_ReadyLocalEulerAngles, ref m_CrouchReadyLocalEulerAngles, ref m_VehicleReadyLocalEulerAngles, _value);

	private void SetActiveNotReadyIkLocalPosition(Vector3 _value)
		=> SetByPosture(ref m_NotReadyIkLocalPosition, ref m_CrouchNotReadyIkLocalPosition, ref m_VehicleNotReadyIkLocalPosition, _value);

	private void SetActiveNotReadyIkLocalEulerAngles(Vector3 _value)
		=> SetByPosture(ref m_NotReadyIkLocalEulerAngles, ref m_CrouchNotReadyIkLocalEulerAngles, ref m_VehicleNotReadyIkLocalEulerAngles, _value);

	private void SetActiveReadyIkLocalPosition(Vector3 _value)
		=> SetByPosture(ref m_ReadyIkLocalPosition, ref m_CrouchReadyIkLocalPosition, ref m_VehicleReadyIkLocalPosition, _value);

	private void SetActiveReadyIkLocalEulerAngles(Vector3 _value)
		=> SetByPosture(ref m_ReadyIkLocalEulerAngles, ref m_CrouchReadyIkLocalEulerAngles, ref m_VehicleReadyIkLocalEulerAngles, _value);

	private void SetActiveLeftNotReadyIkLocalPosition(Vector3 _value)
		=> SetByPosture(ref m_LeftNotReadyIkLocalPosition, ref m_CrouchLeftNotReadyIkLocalPosition, ref m_VehicleLeftNotReadyIkLocalPosition, _value);

	private void SetActiveLeftNotReadyIkLocalEulerAngles(Vector3 _value)
		=> SetByPosture(ref m_LeftNotReadyIkLocalEulerAngles, ref m_CrouchLeftNotReadyIkLocalEulerAngles, ref m_VehicleLeftNotReadyIkLocalEulerAngles, _value);

	private void SetActiveLeftReadyIkLocalPosition(Vector3 _value)
		=> SetByPosture(ref m_LeftReadyIkLocalPosition, ref m_CrouchLeftReadyIkLocalPosition, ref m_VehicleLeftReadyIkLocalPosition, _value);

	private void SetActiveLeftReadyIkLocalEulerAngles(Vector3 _value)
		=> SetByPosture(ref m_LeftReadyIkLocalEulerAngles, ref m_CrouchLeftReadyIkLocalEulerAngles, ref m_VehicleLeftReadyIkLocalEulerAngles, _value);

	private void CaptureIkFromTargetsIfUnset()
	{
		CaptureStandingIkFromTargetsIfUnset();
		SeedUnsetPostureBuffersFromStanding();
	}

	private void CaptureStandingIkFromTargetsIfUnset()
	{
		if (m_NotReadyIkLocalPosition == Vector3.zero && m_NotReadyIkLocalEulerAngles == Vector3.zero)
		{
			Transform t = GetRightHandIkTargetNotReadyTransform();
			if (t != null)
			{
				m_NotReadyIkLocalPosition = t.localPosition;
				m_NotReadyIkLocalEulerAngles = t.localEulerAngles;
			}
		}

		if (m_ReadyIkLocalPosition == Vector3.zero && m_ReadyIkLocalEulerAngles == Vector3.zero)
		{
			Transform t = GetRightHandIkTargetTransform();
			if (t != null)
			{
				m_ReadyIkLocalPosition = t.localPosition;
				m_ReadyIkLocalEulerAngles = t.localEulerAngles;
			}
		}

		if (m_LeftNotReadyIkLocalPosition == Vector3.zero && m_LeftNotReadyIkLocalEulerAngles == Vector3.zero)
		{
			Transform t = GetLeftHandIkTargetNotReadyTransform();
			if (t == null)
				t = GetLeftHandIkTargetTransform();
			if (t != null)
			{
				m_LeftNotReadyIkLocalPosition = t.localPosition;
				m_LeftNotReadyIkLocalEulerAngles = t.localEulerAngles;
			}
		}

		if (m_LeftReadyIkLocalPosition == Vector3.zero && m_LeftReadyIkLocalEulerAngles == Vector3.zero)
		{
			Transform t = GetLeftHandIkTargetTransform();
			if (t != null)
			{
				m_LeftReadyIkLocalPosition = t.localPosition;
				m_LeftReadyIkLocalEulerAngles = t.localEulerAngles;
			}
		}
	}

	/// <summary>
	/// When crouch/vehicle asset fields are empty, seed capture buffers from standing so
	/// Save Crouch / Save Vehicle do not write zeros over a blank posture set.
	/// </summary>
	private void SeedUnsetPostureBuffersFromStanding()
	{
		if (IsPoseBufferEmpty(
			    m_CrouchNotReadyLocalPosition,
			    m_CrouchNotReadyLocalEulerAngles,
			    m_CrouchReadyLocalPosition,
			    m_CrouchReadyLocalEulerAngles)
		    && IsPoseBufferEmpty(
			    m_CrouchNotReadyIkLocalPosition,
			    m_CrouchNotReadyIkLocalEulerAngles,
			    m_CrouchReadyIkLocalPosition,
			    m_CrouchReadyIkLocalEulerAngles)
		    && IsPoseBufferEmpty(
			    m_CrouchLeftNotReadyIkLocalPosition,
			    m_CrouchLeftNotReadyIkLocalEulerAngles,
			    m_CrouchLeftReadyIkLocalPosition,
			    m_CrouchLeftReadyIkLocalEulerAngles))
		{
			m_CrouchNotReadyLocalPosition = m_NotReadyLocalPosition;
			m_CrouchNotReadyLocalEulerAngles = m_NotReadyLocalEulerAngles;
			m_CrouchReadyLocalPosition = m_ReadyLocalPosition;
			m_CrouchReadyLocalEulerAngles = m_ReadyLocalEulerAngles;
			m_CrouchNotReadyIkLocalPosition = m_NotReadyIkLocalPosition;
			m_CrouchNotReadyIkLocalEulerAngles = m_NotReadyIkLocalEulerAngles;
			m_CrouchReadyIkLocalPosition = m_ReadyIkLocalPosition;
			m_CrouchReadyIkLocalEulerAngles = m_ReadyIkLocalEulerAngles;
			m_CrouchLeftNotReadyIkLocalPosition = m_LeftNotReadyIkLocalPosition;
			m_CrouchLeftNotReadyIkLocalEulerAngles = m_LeftNotReadyIkLocalEulerAngles;
			m_CrouchLeftReadyIkLocalPosition = m_LeftReadyIkLocalPosition;
			m_CrouchLeftReadyIkLocalEulerAngles = m_LeftReadyIkLocalEulerAngles;
		}

		if (IsPoseBufferEmpty(
			    m_VehicleNotReadyLocalPosition,
			    m_VehicleNotReadyLocalEulerAngles,
			    m_VehicleReadyLocalPosition,
			    m_VehicleReadyLocalEulerAngles)
		    && IsPoseBufferEmpty(
			    m_VehicleNotReadyIkLocalPosition,
			    m_VehicleNotReadyIkLocalEulerAngles,
			    m_VehicleReadyIkLocalPosition,
			    m_VehicleReadyIkLocalEulerAngles)
		    && IsPoseBufferEmpty(
			    m_VehicleLeftNotReadyIkLocalPosition,
			    m_VehicleLeftNotReadyIkLocalEulerAngles,
			    m_VehicleLeftReadyIkLocalPosition,
			    m_VehicleLeftReadyIkLocalEulerAngles))
		{
			m_VehicleNotReadyLocalPosition = m_NotReadyLocalPosition;
			m_VehicleNotReadyLocalEulerAngles = m_NotReadyLocalEulerAngles;
			m_VehicleReadyLocalPosition = m_ReadyLocalPosition;
			m_VehicleReadyLocalEulerAngles = m_ReadyLocalEulerAngles;
			m_VehicleNotReadyIkLocalPosition = m_NotReadyIkLocalPosition;
			m_VehicleNotReadyIkLocalEulerAngles = m_NotReadyIkLocalEulerAngles;
			m_VehicleReadyIkLocalPosition = m_ReadyIkLocalPosition;
			m_VehicleReadyIkLocalEulerAngles = m_ReadyIkLocalEulerAngles;
			m_VehicleLeftNotReadyIkLocalPosition = m_LeftNotReadyIkLocalPosition;
			m_VehicleLeftNotReadyIkLocalEulerAngles = m_LeftNotReadyIkLocalEulerAngles;
			m_VehicleLeftReadyIkLocalPosition = m_LeftReadyIkLocalPosition;
			m_VehicleLeftReadyIkLocalEulerAngles = m_LeftReadyIkLocalEulerAngles;
		}
	}

	private static bool IsPoseBufferEmpty(
		Vector3 _aPos, Vector3 _aEuler, Vector3 _bPos, Vector3 _bEuler)
	{
		return _aPos == Vector3.zero && _aEuler == Vector3.zero
		       && _bPos == Vector3.zero && _bEuler == Vector3.zero;
	}

	private static void EnsureChildEmpty(
		Transform _parent,
		string _name,
		Vector3 _fallbackLocalPosition,
		Vector3 _fallbackLocalEuler)
	{
		if (_parent == null || string.IsNullOrWhiteSpace(_name))
			return;

		// Direct child only — never reparent a foregrip LeftHandIkTarget onto the weapon body.
		Transform existing = _parent.Find(_name);
		if (existing != null)
			return;

		GameObject go = new GameObject(_name);
		Transform t = go.transform;
		t.SetParent(_parent, false);
		t.localPosition = _fallbackLocalPosition;
		t.localRotation = Quaternion.Euler(_fallbackLocalEuler);
		t.localScale = Vector3.one;
	}

	private static Transform FindChildRecursive(Transform _root, string _name)
	{
		if (_root == null || string.IsNullOrWhiteSpace(_name))
			return null;

		Transform direct = _root.Find(_name);
		if (direct != null)
			return direct;

		for (int i = 0; i < _root.childCount; i++)
		{
			Transform found = FindChildRecursive(_root.GetChild(i), _name);
			if (found != null)
				return found;
		}

		return null;
	}

	private void ResolveReferences()
	{
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponent<UnitEquipment>();
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponentInParent<UnitEquipment>();

		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = GetComponent<UnitEquippedWeaponPose>();
		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = GetComponentInParent<UnitEquippedWeaponPose>();
		if (m_RocketLauncherOrder == null)
			m_RocketLauncherOrder = GetComponent<UnitRocketLauncherOrderController>();
		if (m_RocketLauncherOrder == null)
			m_RocketLauncherOrder = GetComponentInParent<UnitRocketLauncherOrderController>();
	}

	private void SubscribeEquipmentEvents()
	{
		if (m_UnitEquipment != null)
			m_UnitEquipment.EquipmentChanged += HandleEquipmentChanged;
		if (m_RocketLauncherOrder != null)
			m_RocketLauncherOrder.OrderStateChanged += HandleEquipmentChanged;
	}

	private void UnsubscribeEquipmentEvents()
	{
		if (m_UnitEquipment != null)
			m_UnitEquipment.EquipmentChanged -= HandleEquipmentChanged;
		if (m_RocketLauncherOrder != null)
			m_RocketLauncherOrder.OrderStateChanged -= HandleEquipmentChanged;
	}

	private void HandleEquipmentChanged()
	{
		if (IsTuningActive)
		{
			EnsureAllHandIkTargetsExist();
			return;
		}
	}

	private static string Format(float _value)
	{
		return Math.Abs(_value) < 0.0001f ? "0" : _value.ToString("0.####");
	}

	private static bool ShouldSeedReadyWeaponPoseFromNotReady(
		Vector3 _notReadyPosition,
		Vector3 _notReadyEulerAngles,
		Vector3 _readyPosition,
		Vector3 _readyEulerAngles)
	{
		if (_readyPosition == Vector3.zero && _readyEulerAngles == Vector3.zero)
			return true;

		bool readyStillTemplate = Approximately(_readyPosition, s_TemplateWeaponLocalPosition)
		                          && ApproximatelyEuler(_readyEulerAngles, s_TemplateWeaponLocalEuler);
		if (!readyStillTemplate)
			return false;

		return !Approximately(_notReadyPosition, s_TemplateWeaponLocalPosition)
		       || !ApproximatelyEuler(_notReadyEulerAngles, s_TemplateWeaponLocalEuler);
	}

	private static bool Approximately(Vector3 _a, Vector3 _b, float _tolerance = 0.02f)
	{
		return (_a - _b).sqrMagnitude <= _tolerance * _tolerance;
	}

	private static bool ApproximatelyEuler(Vector3 _a, Vector3 _b, float _tolerance = 0.75f)
	{
		return Mathf.Abs(Mathf.DeltaAngle(_a.x, _b.x)) <= _tolerance
		       && Mathf.Abs(Mathf.DeltaAngle(_a.y, _b.y)) <= _tolerance
		       && Mathf.Abs(Mathf.DeltaAngle(_a.z, _b.z)) <= _tolerance;
	}
	#endregion
}
