using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Play Mode tool for the new hold system.
/// Sources of truth:
/// 1) WeaponPoseDefinition — weapon local pos/rot under Hand_R
/// 2) GripRig/RightHand/{Standing|Crouch|Vehicle}/{Ready|NotReady} — right hand
/// 3) LeftHandGrip (or ForeGrip/LeftHandGrip) — left hand, one point only
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(44)]
public sealed class UnitEquippedWeaponPoseRuntimeTuner : MonoBehaviour
{
	#region Nested Types
	public enum TuningTarget
	{
		/// <summary>IK off. Move the weapon root.</summary>
		HandsFrozen = 0,
		/// <summary>«Не готов» — оружие не готово.</summary>
		NotReady = 1,
		/// <summary>LowReady — оружие вниз.</summary>
		LowReady = 2,
		/// <summary>From the hip.</summary>
		HipFire = 3,
		/// <summary>LCU point — PointAim pose + IK.</summary>
		PointAim = 4,
		/// <summary>Full ADS — Aiming pose + IK.</summary>
		Aiming = 5,
		/// <summary>Muzzle up over a threat. Authored; fire forbidden. PreAim is derived and not a tuner target.</summary>
		HighReady = 6,
		/// <summary>Peaceful patrol carry. Same rules as NotReady; own authored coordinates.</summary>
		NotReadyPatrol = 7,
		/// <summary>HipFire standing walk. Body plays Walk_Aim_F_Loop.</summary>
		HipFireWalk = 8,
		/// <summary>HipFire crouch walk. Body plays RifleCrouch_Move.</summary>
		HipFireCrouchWalk = 9,
	}

	/// <summary>Tuner weapon-pose buffer slots.</summary>
	public enum TunerWeaponPoseBuffer
	{
		HoldNotReady = 0,
		LowReady = 1,
		HipFire = 2,
		PointAim = 3,
		Aiming = 4,
		HighReady = 5,
		NotReadyPatrol = 6,
		HipFireWalk = 7,
		HipFireCrouchWalk = 8,
	}

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
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitAnimatorStance m_AnimatorStance;
	[SerializeField] private UnitVehicleSeatPoseController m_SeatPose;
	[SerializeField] private Animator m_UnitAnimator;

	[Header("Runtime tuning")]
	[SerializeField] private bool m_EnableRuntimeTuning;
	[SerializeField] private TuningTarget m_ActiveTarget = TuningTarget.HandsFrozen;
	[SerializeField] private TuningPosture m_ActivePosture = TuningPosture.Standing;

	[Header("Weapon pose buffers — Standing")]
	[SerializeField] private Vector3 m_StandingHoldNotReadyPos;
	[SerializeField] private Vector3 m_StandingHoldNotReadyEuler;
	[SerializeField] private Vector3 m_StandingNotReadyPos;
	[SerializeField] private Vector3 m_StandingNotReadyEuler;
	[SerializeField] private Vector3 m_StandingHipFirePos;
	[SerializeField] private Vector3 m_StandingHipFireEuler;
	[SerializeField] private Vector3 m_StandingHipFireWalkPos;
	[SerializeField] private Vector3 m_StandingHipFireWalkEuler;
	[SerializeField] private Vector3 m_StandingHipFireCrouchWalkPos;
	[SerializeField] private Vector3 m_StandingHipFireCrouchWalkEuler;
	[SerializeField] private Vector3 m_StandingReadyPos;
	[SerializeField] private Vector3 m_StandingReadyEuler;
	[SerializeField] private Vector3 m_StandingAimingPos;
	[SerializeField] private Vector3 m_StandingAimingEuler;
	[SerializeField] private Vector3 m_StandingHighReadyPos;
	[SerializeField] private Vector3 m_StandingHighReadyEuler;
	[SerializeField] private Vector3 m_StandingHoldNotReadyPatrolPos;
	[SerializeField] private Vector3 m_StandingHoldNotReadyPatrolEuler;

	[Header("Weapon pose buffers — Crouch")]
	[SerializeField] private Vector3 m_CrouchHoldNotReadyPos;
	[SerializeField] private Vector3 m_CrouchHoldNotReadyEuler;
	[SerializeField] private Vector3 m_CrouchNotReadyPos;
	[SerializeField] private Vector3 m_CrouchNotReadyEuler;
	[SerializeField] private Vector3 m_CrouchHipFirePos;
	[SerializeField] private Vector3 m_CrouchHipFireEuler;
	[SerializeField] private Vector3 m_CrouchHipFireWalkPos;
	[SerializeField] private Vector3 m_CrouchHipFireWalkEuler;
	[SerializeField] private Vector3 m_CrouchHipFireCrouchWalkPos;
	[SerializeField] private Vector3 m_CrouchHipFireCrouchWalkEuler;
	[SerializeField] private Vector3 m_CrouchReadyPos;
	[SerializeField] private Vector3 m_CrouchReadyEuler;
	[SerializeField] private Vector3 m_CrouchAimingPos;
	[SerializeField] private Vector3 m_CrouchAimingEuler;
	[SerializeField] private Vector3 m_CrouchHighReadyPos;
	[SerializeField] private Vector3 m_CrouchHighReadyEuler;
	[SerializeField] private Vector3 m_CrouchHoldNotReadyPatrolPos;
	[SerializeField] private Vector3 m_CrouchHoldNotReadyPatrolEuler;

	[Header("Weapon pose buffers — Vehicle")]
	[SerializeField] private Vector3 m_VehicleHoldNotReadyPos;
	[SerializeField] private Vector3 m_VehicleHoldNotReadyEuler;
	[SerializeField] private Vector3 m_VehicleNotReadyPos;
	[SerializeField] private Vector3 m_VehicleNotReadyEuler;
	[SerializeField] private Vector3 m_VehicleHipFirePos;
	[SerializeField] private Vector3 m_VehicleHipFireEuler;
	[SerializeField] private Vector3 m_VehicleHipFireWalkPos;
	[SerializeField] private Vector3 m_VehicleHipFireWalkEuler;
	[SerializeField] private Vector3 m_VehicleHipFireCrouchWalkPos;
	[SerializeField] private Vector3 m_VehicleHipFireCrouchWalkEuler;
	[SerializeField] private Vector3 m_VehicleReadyPos;
	[SerializeField] private Vector3 m_VehicleReadyEuler;
	[SerializeField] private Vector3 m_VehicleAimingPos;
	[SerializeField] private Vector3 m_VehicleAimingEuler;
	[SerializeField] private Vector3 m_VehicleHighReadyPos;
	[SerializeField] private Vector3 m_VehicleHighReadyEuler;
	[SerializeField] private Vector3 m_VehicleHoldNotReadyPatrolPos;
	[SerializeField] private Vector3 m_VehicleHoldNotReadyPatrolEuler;

	[SerializeField] private bool m_EnumOrderMigrated;
	[SerializeField] private int m_EnumLayoutVersion;
	#endregion

	#region Private Fields
	private TuningTarget m_LastAppliedTarget = (TuningTarget)(-1);
	private TuningPosture m_LastAppliedPosture = (TuningPosture)(-1);
	private bool m_WasTuningActive;
	private bool m_HasBodyStateSnapshot;
	private bool m_SavedReadyWanted;
	private LocomotionStance m_SavedStance = LocomotionStance.Standing;
	private bool m_WalkAnimatorFrozen;
	private const float c_WalkFreezeNormalizedTime = 0.2f;

	private struct HandGripClipboard
	{
		public bool HasRight;
		public Vector3 RightPos;
		public Quaternion RightRot;
		public bool HasLeft;
		public Vector3 LeftPos;
		public Quaternion LeftRot;
	}

	private struct WeaponInHandClipboard
	{
		public bool Has;
		public Vector3 LocalPos;
		public Vector3 LocalEuler;
	}

	private static HandGripClipboard s_HandGripClipboard;
	private static WeaponInHandClipboard s_WeaponInHandClipboard;
	private CharacterInventory m_Inventory;
	private UnitWeaponRuntime m_WeaponRuntime;
	private ItemDefinition m_LoadedTuningDefinition;
	private RocketLauncherType m_PreferredLauncherType = RocketLauncherType.Rpg7;
	#endregion

	#region Public Properties
	public event System.Action TuningModeChanged;

	public bool IsTuningActive => m_EnableRuntimeTuning && Application.isPlaying;
	/// <summary>
	/// HipFire walk / crouch walk clips are locomotion: freeze the Animator so the clip
	/// and graph transitions do not advance while the pose is edited.
	/// </summary>
	public bool ShouldFreezeWalkAnimator =>
		IsTuningActive &&
		m_ActivePosture != TuningPosture.Vehicle &&
		(m_ActiveTarget == TuningTarget.HipFireWalk || m_ActiveTarget == TuningTarget.HipFireCrouchWalk);
	public bool ShouldDisableAllHandIk => IsTuningActive && m_ActiveTarget == TuningTarget.HandsFrozen;
	/// <summary>NotReady / NotReadyPatrol save like other poses; excluded from AI Auto weapon mode only.</summary>
	public bool IsNonAiTunerPose =>
		m_ActiveTarget == TuningTarget.NotReady || m_ActiveTarget == TuningTarget.NotReadyPatrol;
	public bool ForcesRightHandIk =>
		IsTuningActive && m_ActiveTarget != TuningTarget.HandsFrozen;
	/// <summary>
	/// Only Hands Frozen: user moves Equipped_* freely.
	/// NotReady/Ready: pose system keeps writing override buffers (same path as gameplay).
	/// </summary>
	public bool ShouldSkipWeaponPoseWrite => IsTuningActive && m_ActiveTarget == TuningTarget.HandsFrozen;

	public TuningTarget ActiveTarget => m_ActiveTarget;
	public TuningPosture ActivePosture => m_ActivePosture;
	public UnitEquipment UnitEquipment => m_UnitEquipment;

	/// <summary>Gameplay pose slot driven by the tuner dropdown.</summary>
	public WeaponPoseState ActiveWeaponPoseState => m_ActiveTarget switch
	{
		TuningTarget.NotReady => WeaponPoseState.NotReady,
		TuningTarget.NotReadyPatrol => WeaponPoseState.NotReadyPatrol,
		TuningTarget.HipFire => WeaponPoseState.HipFire,
		TuningTarget.HipFireWalk => WeaponPoseState.HipFireWalk,
		TuningTarget.HipFireCrouchWalk => WeaponPoseState.HipFireCrouchWalk,
		TuningTarget.PointAim => WeaponPoseState.PointAim,
		TuningTarget.Aiming => WeaponPoseState.Aiming,
		TuningTarget.HighReady => WeaponPoseState.HighReady,
		_ => WeaponPoseState.LowReady,
	};

	/// <summary>Raised blend for legacy IK weight readers (0 = LowReady … 1 = Aiming).</summary>
	public float ForcedReadyBlend01 => m_ActiveTarget switch
	{
		TuningTarget.NotReady => 0f,
		TuningTarget.NotReadyPatrol => 0f,
		TuningTarget.HipFire => 0.33f,
		TuningTarget.HipFireWalk => 0.33f,
		TuningTarget.HipFireCrouchWalk => 0.33f,
		TuningTarget.PointAim => 0.66f,
		TuningTarget.Aiming => 1f,
		TuningTarget.HighReady => 0.5f,
		_ => 0f,
	};

	public bool UsesRocketLauncherContext =>
		m_RocketLauncherOrder != null &&
		m_RocketLauncherOrder.HandLauncherRoot != null;

	public ItemDefinition ActiveTuningDefinition => UsesRocketLauncherContext
		? m_RocketLauncherOrder.ActiveLauncherDefinition
		: m_UnitEquipment != null ? m_UnitEquipment.EquippedDefinition : null;

	public bool HasForegripLeftHand
	{
		get
		{
			Transform foregripRoot = GetForegripVisualRoot();
			if (foregripRoot == null)
				return false;
			return foregripRoot.GetComponentInChildren<WeaponForeGrip>(true) != null
			       || FindChildRecursive(foregripRoot, WeaponForeGrip.LeftHandGripName) != null;
		}
	}

	public string ForegripTunerStatus
	{
		get
		{
			if (!Application.isPlaying)
				return "Нужен Play Mode.";
			if (UsesRocketLauncherContext)
				return "Гранатомёт — рукоятка не ставится.";
			if (!TryGetForegripInstallContext(
				    out string reason,
				    out _,
				    out _,
				    out _,
				    out List<ItemDefinition> grips,
				    out ItemDefinition current))
				return reason;

			if (current != null)
			{
				int idx = IndexOfForegripItem(grips, current);
				int shown = idx >= 0 ? idx + 1 : 1;
				ItemDefinition next = grips[(idx >= 0 ? idx + 1 : 0) % grips.Count];
				return $"Стоит: {current.name} ({shown}/{grips.Count}). Сменить → {next.name}";
			}

			return $"Слот свободен. Можно поставить: {grips[0].name} ({grips.Count} шт.).";
		}
	}

	public bool CanTuneForegrip =>
		Application.isPlaying &&
		!UsesRocketLauncherContext &&
		TryGetForegripInstallContext(out _, out _, out _, out _, out _, out _);

	public Vector3 HoldNotReadyLocalPosition => GetBufferPos(TunerWeaponPoseBuffer.HoldNotReady);
	public Vector3 HoldNotReadyLocalEulerAngles => GetBufferEuler(TunerWeaponPoseBuffer.HoldNotReady);
	public Vector3 LowReadyLocalPosition => GetBufferPos(TunerWeaponPoseBuffer.LowReady);
	public Vector3 LowReadyLocalEulerAngles => GetBufferEuler(TunerWeaponPoseBuffer.LowReady);
	public Vector3 HipFireLocalPosition => GetBufferPos(TunerWeaponPoseBuffer.HipFire);
	public Vector3 HipFireLocalEulerAngles => GetBufferEuler(TunerWeaponPoseBuffer.HipFire);
	public Vector3 HipFireWalkLocalPosition => GetBufferPos(TunerWeaponPoseBuffer.HipFireWalk);
	public Vector3 HipFireWalkLocalEulerAngles => GetBufferEuler(TunerWeaponPoseBuffer.HipFireWalk);
	public Vector3 HipFireCrouchWalkLocalPosition => GetBufferPos(TunerWeaponPoseBuffer.HipFireCrouchWalk);
	public Vector3 HipFireCrouchWalkLocalEulerAngles => GetBufferEuler(TunerWeaponPoseBuffer.HipFireCrouchWalk);
	public Vector3 PointAimLocalPosition => GetBufferPos(TunerWeaponPoseBuffer.PointAim);
	public Vector3 PointAimLocalEulerAngles => GetBufferEuler(TunerWeaponPoseBuffer.PointAim);
	public Vector3 AimingLocalPosition => GetBufferPos(TunerWeaponPoseBuffer.Aiming);
	public Vector3 AimingLocalEulerAngles => GetBufferEuler(TunerWeaponPoseBuffer.Aiming);
	public Vector3 HighReadyLocalPosition => GetBufferPos(TunerWeaponPoseBuffer.HighReady);
	public Vector3 HighReadyLocalEulerAngles => GetBufferEuler(TunerWeaponPoseBuffer.HighReady);
	public Vector3 HoldNotReadyPatrolLocalPosition => GetBufferPos(TunerWeaponPoseBuffer.NotReadyPatrol);
	public Vector3 HoldNotReadyPatrolLocalEulerAngles => GetBufferEuler(TunerWeaponPoseBuffer.NotReadyPatrol);

	// Legacy aliases
	public Vector3 NotReadyLocalPosition => LowReadyLocalPosition;
	public Vector3 NotReadyLocalEulerAngles => LowReadyLocalEulerAngles;
	public Vector3 ReadyLocalPosition => PointAimLocalPosition;
	public Vector3 ReadyLocalEulerAngles => PointAimLocalEulerAngles;

	public Vector3 StandingNotReadyLocalPosition => m_StandingNotReadyPos;
	public Vector3 StandingNotReadyLocalEulerAngles => m_StandingNotReadyEuler;
	public Vector3 StandingReadyLocalPosition => m_StandingReadyPos;
	public Vector3 StandingReadyLocalEulerAngles => m_StandingReadyEuler;

	public Vector3 CrouchNotReadyLocalPosition => m_CrouchNotReadyPos;
	public Vector3 CrouchNotReadyLocalEulerAngles => m_CrouchNotReadyEuler;
	public Vector3 CrouchReadyLocalPosition => m_CrouchReadyPos;
	public Vector3 CrouchReadyLocalEulerAngles => m_CrouchReadyEuler;

	public Vector3 VehicleNotReadyLocalPosition => m_VehicleNotReadyPos;
	public Vector3 VehicleNotReadyLocalEulerAngles => m_VehicleNotReadyEuler;
	public Vector3 VehicleReadyLocalPosition => m_VehicleReadyPos;
	public Vector3 VehicleReadyLocalEulerAngles => m_VehicleReadyEuler;
	#endregion

	#region Unity Lifecycle
	private void OnValidate()
	{
		if (m_EnumLayoutVersion >= 3)
			return;

		int raw = (int)m_ActiveTarget;

		if (m_EnumLayoutVersion < 2)
		{
			// Layout v0: до HipFire/NotReady (PointAim=2, …).
			if (m_EnumLayoutVersion == 0 && !m_EnumOrderMigrated)
			{
				switch (raw)
				{
					case 2:
						raw = 3;
						break;
					case 3:
						raw = 2;
						break;
					case 4:
						raw = 4;
						break;
				}
			}

			// Layout v1 → v2: HandsFrozen, LowReady, HipFire, PointAim, Aiming, NotReady, HighReady.
			raw = RemapLayoutV1IndexToV2(raw);
			m_EnumLayoutVersion = 2;
		}

		if (m_EnumLayoutVersion == 2)
		{
			// Layout v2 → v3: drop HighReady (index 5 → PointAim, Aiming 6 → 5).
			raw = RemapLayoutV2IndexToV3(raw);
			m_EnumLayoutVersion = 3;
		}

		m_ActiveTarget = (TuningTarget)raw;
		m_EnumOrderMigrated = true;
	}

	/// <summary>Maps serialized index from dropdown layout v1 to layout v2.</summary>
	private static int RemapLayoutV1IndexToV2(int _layoutV1Index) => _layoutV1Index switch
	{
		0 => 0,
		1 => 2,
		2 => 3,
		3 => 4,
		4 => 6,
		5 => 1,
		6 => 5,
		_ => 0,
	};

	/// <summary>Maps layout v2 index (with HighReady) to v3 (without).</summary>
	private static int RemapLayoutV2IndexToV3(int _layoutV2Index) => _layoutV2Index switch
	{
		5 => 4,
		6 => 5,
		_ => _layoutV2Index,
	};

	private void Awake() => ResolveReferences();

	private void OnEnable()
	{
		ResolveReferences();
		SubscribeEquipmentEvents();
	}

	private void OnDisable()
	{
		UnsubscribeEquipmentEvents();
		if (!m_WasTuningActive)
			return;

		EndTuningSession();
		m_WasTuningActive = false;
	}

	private void Update()
	{
		if (!Application.isPlaying || !IsTuningActive)
			return;

		TickWalkAnimatorFreeze();
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
		{
			ApplyActiveTargetSwitch();
			TuningModeChanged?.Invoke();
		}
		else
			SyncUnitBodyToActiveMode();

		TickWalkAnimatorFreeze();

		// Hands Frozen: do not capture. Stamping live Equipped_* every frame used to copy
		// standing TRS over Crouch/Vehicle when switching posture while frozen.
		if (m_ActiveTarget != TuningTarget.HandsFrozen)
			CaptureLiveWeaponPoseFromScene();
	}
	#endregion

	#region Public Methods
	public Transform GetActiveWeaponRoot()
	{
		return UsesRocketLauncherContext
			? m_RocketLauncherOrder.HandLauncherRoot
			: m_UnitEquipment != null ? m_UnitEquipment.MainWeaponRoot : null;
	}

	public Transform GetForegripVisualRoot()
	{
		EquippedWeapon equippedWeapon = m_UnitEquipment != null ? m_UnitEquipment.EquippedWeapon : null;
		return equippedWeapon != null ? equippedWeapon.UnderBarrelForegripVisualRoot : null;
	}

	private bool TryGetForegripInstallContext(
		out string _reason,
		out WeaponRuntimeState _state,
		out WeaponDefinition _weapon,
		out int _underBarrelIndex,
		out List<ItemDefinition> _grips,
		out ItemDefinition _current)
	{
		_reason = null;
		_state = null;
		_weapon = null;
		_underBarrelIndex = -1;
		_grips = null;
		_current = null;

		if (UsesRocketLauncherContext)
		{
			_reason = "Гранатомёт — рукоятка не ставится.";
			return false;
		}

		if (m_UnitEquipment == null || m_UnitEquipment.MainWeaponRoot == null)
		{
			_reason = "Сначала экипируй винтовку.";
			return false;
		}

		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>() ?? GetComponentInParent<UnitWeaponRuntime>();

		_state = m_WeaponRuntime != null ? m_WeaponRuntime.RuntimeState : null;
		_weapon = _state != null
			? _state.WeaponDefinition
			: m_UnitEquipment.EquippedDefinition != null
				? m_UnitEquipment.EquippedDefinition.WeaponDefinition
				: null;
		if (_weapon == null)
		{
			_reason = "Нет WeaponDefinition у экипированного оружия.";
			return false;
		}

		if (_state == null)
		{
			_reason = "Нет WeaponRuntimeState — экипируй оружие в руку.";
			return false;
		}

		if (!TryFindEnabledUnderBarrelSlot(_weapon, out _underBarrelIndex, out _reason))
			return false;

		EquippedWeapon equippedWeapon = m_UnitEquipment.EquippedWeapon;
		if (equippedWeapon == null || equippedWeapon.UnderBarrelSocketTransform == null)
		{
			_reason = "На Equipped_* нет UnderBarrelSocket.";
			return false;
		}

		_grips = FindCompatibleForegripItems(_weapon);
		if (_grips == null || _grips.Count == 0)
		{
			_reason = "Нет совместимых рукояток (Item + визуал).";
			return false;
		}

		_current = GetCurrentForegripItem(_state, _underBarrelIndex);
		return true;
	}

	private static bool TryFindEnabledUnderBarrelSlot(
		WeaponDefinition _weapon,
		out int _index,
		out string _reason)
	{
		_index = -1;
		_reason = null;
		WeaponAttachmentSlotDefinition[] slots = _weapon.AttachmentSlots;
		if (slots == null || slots.Length == 0)
		{
			_reason = "В WeaponDefinition нет слотов модулей.";
			return false;
		}

		int foundDisabled = -1;
		for (int i = 0; i < slots.Length; i++)
		{
			if (slots[i].SlotType != WeaponAttachmentSlotType.UnderBarrel)
				continue;
			if (!WeaponAttachmentSlotPolicy.IsWeaponSlotEnabled(_weapon, i))
			{
				foundDisabled = i;
				continue;
			}

			_index = i;
			return true;
		}

		_reason = foundDisabled >= 0
			? "У этого оружия слот рукоятки выключен (профиль слотов)."
			: "В WeaponDefinition нет слота UnderBarrel.";
		return false;
	}

	private void ApplyForegripItem(
		WeaponRuntimeState _state,
		WeaponDefinition _weapon,
		int _underBarrelIndex,
		ItemDefinition _item)
	{
		CopyAttachmentArrays(_state, _weapon, out WeaponAttachmentDefinition[] attachments, out ItemDefinition[] items);
		if (_underBarrelIndex < 0 || _underBarrelIndex >= attachments.Length)
			return;

		attachments[_underBarrelIndex] = _item != null ? _item.WeaponAttachmentDefinition : null;
		items[_underBarrelIndex] = _item;
		_state.SetEquippedAttachmentSlotItems(attachments, items);

		EquippedWeapon equippedWeapon = m_UnitEquipment != null ? m_UnitEquipment.EquippedWeapon : null;
		if (equippedWeapon == null)
			return;

		if (_item == null && !HasAnyNonNullAttachment(attachments))
			equippedWeapon.ClearAttachmentVisuals();
		else
			equippedWeapon.RefreshAttachmentVisualsFromState(_weapon, _state);

		if (m_UnitEquipment != null)
			m_UnitEquipment.RefreshHandIkTargets();
		if (_item != null)
			EnsureForeGripLeftHandGrip();
		RefreshGripResolverTargets();
	}

	private static void CopyAttachmentArrays(
		WeaponRuntimeState _state,
		WeaponDefinition _weapon,
		out WeaponAttachmentDefinition[] _attachments,
		out ItemDefinition[] _items)
	{
		int slotCount = _weapon != null && _weapon.AttachmentSlots != null ? _weapon.AttachmentSlots.Length : 0;
		if (_state.EquippedAttachments != null)
			slotCount = Mathf.Max(slotCount, _state.EquippedAttachments.Length);
		if (_state.EquippedAttachmentItems != null)
			slotCount = Mathf.Max(slotCount, _state.EquippedAttachmentItems.Length);
		if (slotCount < 1)
			slotCount = 1;

		_attachments = new WeaponAttachmentDefinition[slotCount];
		_items = new ItemDefinition[slotCount];
		if (_state.EquippedAttachments != null)
		{
			int copy = Mathf.Min(_state.EquippedAttachments.Length, slotCount);
			for (int i = 0; i < copy; i++)
				_attachments[i] = _state.EquippedAttachments[i];
		}

		if (_state.EquippedAttachmentItems != null)
		{
			int copy = Mathf.Min(_state.EquippedAttachmentItems.Length, slotCount);
			for (int i = 0; i < copy; i++)
				_items[i] = _state.EquippedAttachmentItems[i];
		}
	}

	private static bool HasAnyNonNullAttachment(WeaponAttachmentDefinition[] _attachments)
	{
		if (_attachments == null)
			return false;
		for (int i = 0; i < _attachments.Length; i++)
		{
			if (_attachments[i] != null)
				return true;
		}

		return false;
	}

	private static ItemDefinition GetCurrentForegripItem(WeaponRuntimeState _state, int _underBarrelIndex)
	{
		if (_state == null || _underBarrelIndex < 0)
			return null;

		ItemDefinition[] items = _state.EquippedAttachmentItems;
		if (items != null && _underBarrelIndex < items.Length && IsForegripItem(items[_underBarrelIndex]))
			return items[_underBarrelIndex];

		WeaponAttachmentDefinition[] attachments = _state.EquippedAttachments;
		WeaponAttachmentDefinition attached = attachments != null && _underBarrelIndex < attachments.Length
			? attachments[_underBarrelIndex]
			: null;
		if (attached == null || !IsForegripAttachment(attached))
			return null;

		List<ItemDefinition> all = FindCompatibleForegripItems(_state.WeaponDefinition);
		if (all == null)
			return null;
		for (int i = 0; i < all.Count; i++)
		{
			if (all[i] != null && all[i].WeaponAttachmentDefinition == attached)
				return all[i];
		}

		return null;
	}

	private ItemDefinition GetCurrentForegripItem()
	{
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>() ?? GetComponentInParent<UnitWeaponRuntime>();
		WeaponRuntimeState state = m_WeaponRuntime != null ? m_WeaponRuntime.RuntimeState : null;
		WeaponDefinition weapon = state != null ? state.WeaponDefinition : null;
		if (weapon == null || !TryFindEnabledUnderBarrelSlot(weapon, out int index, out _))
			return null;
		return GetCurrentForegripItem(state, index);
	}

	private static int IndexOfForegripItem(List<ItemDefinition> _grips, ItemDefinition _current)
	{
		if (_grips == null || _current == null)
			return -1;
		for (int i = 0; i < _grips.Count; i++)
		{
			if (_grips[i] == _current)
				return i;
			if (_grips[i] != null &&
			    _current.WeaponAttachmentDefinition != null &&
			    _grips[i].WeaponAttachmentDefinition == _current.WeaponAttachmentDefinition)
				return i;
		}

		return -1;
	}

	private static bool IsForegripItem(ItemDefinition _item)
	{
		return _item != null && IsForegripAttachment(_item.WeaponAttachmentDefinition);
	}

	private static bool IsForegripAttachment(WeaponAttachmentDefinition _attachment)
	{
		if (_attachment == null)
			return false;
		return _attachment.AttachmentType == WeaponAttachmentType.Foregrip ||
		       _attachment.AttachmentType == WeaponAttachmentType.Bipod;
	}

	private static List<ItemDefinition> s_ForegripCatalog;
	private static WeaponDefinition s_ForegripCatalogWeapon;

	private static List<ItemDefinition> FindCompatibleForegripItems(WeaponDefinition _weapon)
	{
		var empty = s_ForegripCatalog;
		if (_weapon == null)
			return empty ?? new List<ItemDefinition>();

#if UNITY_EDITOR
		if (s_ForegripCatalog != null && s_ForegripCatalogWeapon == _weapon)
			return s_ForegripCatalog;

		var list = new List<ItemDefinition>();
		string[] guids = UnityEditor.AssetDatabase.FindAssets("t:ItemDefinition", new[] { "Assets/GameData/Inventory" });
		for (int i = 0; i < guids.Length; i++)
		{
			string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
			ItemDefinition item = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
			if (!IsForegripItem(item))
				continue;

			WeaponAttachmentDefinition attachment = item.WeaponAttachmentDefinition;
			if (!attachment.SupportsSlot(WeaponAttachmentSlotType.UnderBarrel))
				continue;
			if (!attachment.SupportsWeapon(_weapon))
				continue;

			GameObject visual = attachment.EquippedVisualPrefab != null
				? attachment.EquippedVisualPrefab
				: item.EquippedVisualPrefab;
			if (visual == null)
				continue;

			list.Add(item);
		}

		list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
		s_ForegripCatalog = list;
		s_ForegripCatalogWeapon = _weapon;
		return list;
#else
		return new List<ItemDefinition>();
#endif
	}

	public void GetOverridePoses(
		out Vector3 _relaxedPosition,
		out Quaternion _relaxedRotation,
		out Vector3 _readyPosition,
		out Quaternion _readyRotation,
		out float _forcedBlend01)
	{
		Vector3 pos = GetBufferPos(GetActivePoseBuffer());
		Quaternion rot = Quaternion.Euler(GetBufferEuler(GetActivePoseBuffer()));
		_relaxedPosition = pos;
		_relaxedRotation = rot;
		_readyPosition = pos;
		_readyRotation = rot;
		_forcedBlend01 = ForcedReadyBlend01;
	}

	public void EnsureGripTargetsExist()
	{
		if (UsesRocketLauncherContext)
		{
			m_RocketLauncherOrder.EnsureGripRigTargets();
			EnsureRightHandPoseTree(GetActiveWeaponRoot());
			m_RocketLauncherOrder.RefreshHandIkTargets();
			return;
		}

		if (m_UnitEquipment == null)
			return;

		Transform weaponRoot = GetActiveWeaponRoot();
		if (weaponRoot == null)
			return;

		EnsureWeaponGripRig();
		EnsureRightHandPoseTree(weaponRoot);
		EnsureForeGripLeftHandGrip();
		m_UnitEquipment.ResolveGripTargets();
	}

	public void CaptureAllForSave()
	{
		EnsureGripTargetsExist();
		CaptureLiveWeaponPoseFromScene();
	}

	public string RocketLauncherTunerStatus
	{
		get
		{
			if (!Application.isPlaying)
				return "Нужен Play Mode.";
			if (UsesRocketLauncherContext && m_RocketLauncherOrder != null)
			{
				ItemDefinition held = m_RocketLauncherOrder.ActiveLauncherDefinition;
				return held != null
					? "В руках: " + held.name
					: "Гранатомёт в руках.";
			}

			if (TryFindBagLauncher(m_PreferredLauncherType, out ItemDefinition preferred) && preferred != null)
				return "В сумке: " + preferred.name + " (ещё не в руках).";
			if (TryFindBagLauncher(RocketLauncherType.Rpg7, out ItemDefinition rpg) && rpg != null)
				return "В сумке: " + rpg.name + " (ещё не в руках).";
			if (TryFindBagLauncher(RocketLauncherType.Disposable, out ItemDefinition disp) && disp != null)
				return "В сумке: " + disp.name + " (ещё не в руках).";
			return "В сумке нет гранатомёта — жми «Выдать / сменить».";
		}
	}

	public bool TryCycleSpawnRocketLauncherForTuning(out string _message)
	{
		_message = "Нужен Play Mode.";
		if (!Application.isPlaying)
			return false;

		ResolveReferences();
		ItemDefinition rpg = FindRocketLauncherDefinition(RocketLauncherType.Rpg7);
		ItemDefinition disposable = FindRocketLauncherDefinition(RocketLauncherType.Disposable);
		if (rpg == null && disposable == null)
		{
			_message = "Не найдены Item_Weapon_Rpg7 / DisposableRocketLauncher.";
			return false;
		}

		bool hasRpg = HasLauncherInBag(rpg);
		bool hasDisposable = HasLauncherInBag(disposable);
		if (!hasRpg && !hasDisposable)
		{
			ItemDefinition first = rpg != null ? rpg : disposable;
			if (!TryAddLauncherToBag(first, out _message))
				return false;
			m_PreferredLauncherType = first.RocketLauncherType;
			_message = "Выдан в сумку: " + first.name;
			return true;
		}

		RocketLauncherType nextType = m_PreferredLauncherType == RocketLauncherType.Rpg7
			? RocketLauncherType.Disposable
			: RocketLauncherType.Rpg7;
		ItemDefinition next = nextType == RocketLauncherType.Rpg7 ? rpg : disposable;
		if (next == null)
		{
			next = rpg != null ? rpg : disposable;
			nextType = next.RocketLauncherType;
		}

		if (!HasLauncherInBag(next) && !TryAddLauncherToBag(next, out _message))
			return false;

		m_PreferredLauncherType = nextType;
		_message = "Следующий гранатомёт: " + next.name + ". Жми «Взять в руки».";
		return true;
	}

	public bool TryActivateRocketLauncherForTuning(out string _message)
	{
		_message = "Нужен Play Mode.";
		if (!Application.isPlaying)
			return false;

		ResolveReferences();
		if (m_RocketLauncherOrder == null)
		{
			_message = "На юните нет UnitRocketLauncherOrderController.";
			return false;
		}

		ItemDefinition preferred = null;
		TryFindBagLauncher(m_PreferredLauncherType, out preferred);
		if (preferred == null)
			TryFindBagLauncher(RocketLauncherType.Rpg7, out preferred);
		if (preferred == null)
			TryFindBagLauncher(RocketLauncherType.Disposable, out preferred);

		if (preferred == null)
		{
			_message = "В сумке нет гранатомёта. Сначала «Выдать / сменить».";
			return false;
		}

		if (m_RocketLauncherOrder.IsBusy &&
		    m_RocketLauncherOrder.ActiveLauncherDefinition != null &&
		    m_RocketLauncherOrder.ActiveLauncherDefinition != preferred)
			m_RocketLauncherOrder.CancelOrder(true);

		if (!m_RocketLauncherOrder.TryHoldForTuning(preferred))
		{
			_message = "Не удалось взять гранатомёт (юнит занят или нет префаба в руках).";
			return false;
		}

		EnsureGripTargetsExist();
		LoadFromEquippedDefinition();
		_message = "В руках: " + preferred.name;
		return true;
	}

	public bool TryCycleForegripForTuning(out string _message)
	{
		_message = "Нужен Play Mode.";
		if (!Application.isPlaying)
			return false;

		ResolveReferences();
		if (!TryGetForegripInstallContext(
			    out _message,
			    out WeaponRuntimeState state,
			    out WeaponDefinition weapon,
			    out int underBarrelIndex,
			    out List<ItemDefinition> grips,
			    out ItemDefinition current))
			return false;

		int currentIndex = IndexOfForegripItem(grips, current);
		ItemDefinition next = grips[(currentIndex + 1) % grips.Count];
		ApplyForegripItem(state, weapon, underBarrelIndex, next);
		_message = current == null
			? "Поставлена: " + next.name
			: "Сменена: " + current.name + " → " + next.name;
		return true;
	}

	public bool TryRemoveForegripForTuning(out string _message)
	{
		_message = "Нужен Play Mode.";
		if (!Application.isPlaying)
			return false;

		ResolveReferences();
		if (!TryGetForegripInstallContext(
			    out _message,
			    out WeaponRuntimeState state,
			    out WeaponDefinition weapon,
			    out int underBarrelIndex,
			    out _,
			    out ItemDefinition current))
			return false;

		if (current == null && GetForegripVisualRoot() == null)
		{
			_message = "Рукоятка не стоит.";
			return false;
		}

		ApplyForegripItem(state, weapon, underBarrelIndex, null);
		_message = current != null ? "Снята: " + current.name : "Рукоятка снята.";
		return true;
	}

	public bool TryHolsterRocketLauncherForTuning(out string _message)
	{
		_message = "Нужен Play Mode.";
		if (!Application.isPlaying)
			return false;

		ResolveReferences();
		if (m_RocketLauncherOrder == null || !m_RocketLauncherOrder.IsBusy)
		{
			_message = "Гранатомёт не в руках.";
			return false;
		}

		m_RocketLauncherOrder.CancelOrder(true);
		_message = "Гранатомёт убран, снова винтовка.";
		return true;
	}

	public void LoadFromEquippedDefinition()
	{
		ItemDefinition def = ActiveTuningDefinition;
		if (def == null)
			return;

		m_LoadedTuningDefinition = def;
		LoadPostureFromDefinition(def, TuningPosture.Standing);
		LoadPostureFromDefinition(def, TuningPosture.Crouch);
		LoadPostureFromDefinition(def, TuningPosture.Vehicle);
		SeedTunerOnlyPoseBuffers(TuningPosture.Standing);
		SeedTunerOnlyPoseBuffers(TuningPosture.Crouch);
		SeedTunerOnlyPoseBuffers(TuningPosture.Vehicle);

		EnsureGripTargetsExist();
		ApplyActiveTargetPoseToWeapon();
		SyncUnitBodyToActiveMode();
		m_LastAppliedTarget = m_ActiveTarget;
		m_LastAppliedPosture = m_ActivePosture;
	}

	public void CaptureLiveWeaponPoseFromScene()
	{
		Transform weaponRoot = GetActiveWeaponRoot();
		if (weaponRoot == null)
			return;

		if (m_ActiveTarget == TuningTarget.HandsFrozen)
		{
			WriteLiveWeaponToCurrentPostureBuffers();
			return;
		}

		TunerWeaponPoseBuffer buffer = GetActivePoseBuffer();
		SetBufferPos(buffer, weaponRoot.localPosition);
		SetBufferEuler(buffer, weaponRoot.localEulerAngles);
	}

	public void ApplyActiveTargetPoseToWeapon()
	{
		Transform weaponRoot = GetActiveWeaponRoot();
		if (weaponRoot == null)
			return;

		// Hands Frozen still snaps to this posture's LowReady on switch/load so Crouch/Vehicle
		// do not keep leftover standing Equipped_* TRS. Live move is not written back until Save/Paste.
		TunerWeaponPoseBuffer buffer = m_ActiveTarget == TuningTarget.HandsFrozen
			? TunerWeaponPoseBuffer.LowReady
			: GetActivePoseBuffer();
		weaponRoot.localPosition = GetBufferPos(buffer);
		weaponRoot.localRotation = Quaternion.Euler(GetBufferEuler(buffer));
	}

	public void ApplyActiveTargetSwitch()
	{
		if (m_ActiveTarget != TuningTarget.HandsFrozen)
			EnsureGripTargetsExist();

		ApplyActiveTargetPoseToWeapon();
		SyncUnitBodyToActiveMode();
		m_EquippedWeaponPose?.ApplyImmediateFromEquipment();
		RefreshGripResolverTargets();

		m_LastAppliedTarget = m_ActiveTarget;
		m_LastAppliedPosture = m_ActivePosture;
	}

	/// <summary>
	/// Matches unit body animation to tuner mode: Ready/NotReady + Standing/Crouch/Vehicle.
	/// Vehicle sits the unit as a fire-capable passenger without boarding.
	/// </summary>
	public void SyncUnitBodyToActiveMode()
	{
		ResolveReferences();

		if (m_ActiveTarget == TuningTarget.HipFireWalk && m_ActivePosture != TuningPosture.Vehicle)
			m_ActivePosture = TuningPosture.Standing;
		else if (m_ActiveTarget == TuningTarget.HipFireCrouchWalk)
			m_ActivePosture = TuningPosture.Crouch;

		bool vehicleBody = m_ActivePosture == TuningPosture.Vehicle;
		WeaponPoseState vehicleBodyPose = m_ActiveTarget == TuningTarget.HandsFrozen
			? WeaponPoseState.NotReady
			: ActiveWeaponPoseState;
		bool vehicleReady = vehicleBody && vehicleBodyPose.UsesVehicleSeatAimClip();

		if (vehicleBody && m_SeatPose == null)
		{
			GameObject host = m_UnitEquipment != null ? m_UnitEquipment.gameObject : gameObject;
			m_SeatPose = UnitVehicleSeatPoseController.GetOrAdd(host);
		}

		if (m_SeatPose != null)
			m_SeatPose.SetTunerPassengerPreview(vehicleBody, vehicleReady);

		if (m_AnimatorStance != null && !vehicleBody)
		{
			LocomotionStance desired = m_ActivePosture == TuningPosture.Crouch
				? LocomotionStance.Crouch
				: LocomotionStance.Standing;
			if (m_AnimatorStance.CurrentStance != desired)
				m_AnimatorStance.RequestStance(desired);
		}

		bool readyChanged = false;
		if (m_ReadyHands != null)
		{
			if (m_ActiveTarget == TuningTarget.NotReady || m_ActiveTarget == TuningTarget.NotReadyPatrol)
			{
				WeaponPoseState carry = m_ActiveTarget == TuningTarget.NotReadyPatrol
					? WeaponPoseState.NotReadyPatrol
					: WeaponPoseState.NotReady;
				if (!m_ReadyHands.IsPeacefulNotReady || m_ReadyHands.PeacefulCarryPose != carry)
				{
					m_ReadyHands.SetPeacefulCarryPose(carry);
					readyChanged = true;
				}
			}
			else
			{
				WeaponPoseMode want = m_ActiveTarget switch
				{
					TuningTarget.HighReady => WeaponPoseMode.HighReady,
					TuningTarget.HipFire => WeaponPoseMode.HipFire,
					TuningTarget.HipFireWalk => WeaponPoseMode.HipFire,
					TuningTarget.HipFireCrouchWalk => WeaponPoseMode.HipFire,
					TuningTarget.PointAim => WeaponPoseMode.PointAim,
					TuningTarget.Aiming => WeaponPoseMode.Aiming,
					// Hands Frozen is an IK/tuner overlay, not a pose slot. Body stays LowReady
					// while the slot is authored; mismatch vs Hands Frozen is expected.
					TuningTarget.HandsFrozen => WeaponPoseMode.LowReady,
					_ => WeaponPoseMode.LowReady,
				};
				if (m_ReadyHands.WantedMode != want || m_ReadyHands.IsPeacefulNotReady)
				{
					m_ReadyHands.SetPoseModeWanted(want, true);
					readyChanged = true;
				}
			}
		}

		// Snap ReadyPoseBlend01 so GripRig/IK don't lag 0.28s behind ForcedReadyBlend01.
		if (readyChanged)
			m_EquippedWeaponPose?.ApplyImmediateFromEquipment();

		m_ReadyHands?.RefreshAnimatorPoseParameters();
	}

	public bool HasWeaponInHandClipboard => s_WeaponInHandClipboard.Has;

	/// <summary>Hands Frozen only: copy Equipped_* local pos/rot under the right hand.</summary>
	public bool CopyWeaponInHandToClipboard()
	{
		if (m_ActiveTarget != TuningTarget.HandsFrozen)
			return false;

		Transform weaponRoot = GetActiveWeaponRoot();
		if (weaponRoot == null)
			return false;

		s_WeaponInHandClipboard.Has = true;
		s_WeaponInHandClipboard.LocalPos = weaponRoot.localPosition;
		s_WeaponInHandClipboard.LocalEuler = weaponRoot.localEulerAngles;
		return true;
	}

	/// <summary>Hands Frozen only: paste onto Equipped_* and stamp current posture buffers.</summary>
	public bool PasteWeaponInHandFromClipboard()
	{
		if (!HasWeaponInHandClipboard || m_ActiveTarget != TuningTarget.HandsFrozen)
			return false;

		Transform weaponRoot = GetActiveWeaponRoot();
		if (weaponRoot == null)
			return false;

		weaponRoot.localPosition = s_WeaponInHandClipboard.LocalPos;
		weaponRoot.localRotation = Quaternion.Euler(s_WeaponInHandClipboard.LocalEuler);
		WriteLiveWeaponToCurrentPostureBuffers();
		return true;
	}

	public Transform GetActiveRightHandTarget()
	{
		if (m_ActiveTarget == TuningTarget.HandsFrozen)
			return null;

		Transform weaponRoot = GetActiveWeaponRoot();
		WeaponGripRig grip = weaponRoot != null
			? weaponRoot.GetComponentInChildren<WeaponGripRig>(true)
			: null;
		if (grip == null)
		{
			EquippedWeapon equipped = m_UnitEquipment != null ? m_UnitEquipment.EquippedWeapon : null;
			grip = equipped != null ? equipped.GetComponentInChildren<WeaponGripRig>(true) : null;
		}

		if (grip == null)
			return null;
		if (!grip.CachedRightTargets.HasAny)
			grip.BuildCache();

		WeaponPoseState pose = ActiveWeaponPoseState;
		return grip.GetRightHandTarget(GetActiveWeaponStance(), pose);
	}

	/// <summary>
	/// True when Hierarchy/Scene selection is a GripRig hand empty we are editing.
	/// AnimatorHandIk suppresses full-weight Grip IK while this is true so gizmos work.
	/// </summary>
	public bool IsSelectedGripEditTarget(Transform _selection)
	{
		if (_selection == null || !ForcesRightHandIk)
			return false;

		Transform left = GetLiveLeftHandGripTransform();
		if (left != null && (_selection == left || _selection.IsChildOf(left)))
			return true;

		if (!TryGetGripRightHandRoot(out Transform rightRoot) || rightRoot == null)
		{
			Transform activeRight = GetActiveRightHandTarget();
			return activeRight != null &&
			       (_selection == activeRight || _selection.IsChildOf(activeRight));
		}

		return _selection == rightRoot || _selection.IsChildOf(rightRoot);
	}

	private bool TryGetGripRightHandRoot(out Transform _rightRoot)
	{
		_rightRoot = null;
		Transform weaponRoot = GetActiveWeaponRoot();
		if (weaponRoot == null)
			return false;

		WeaponGripRig grip = weaponRoot.GetComponentInChildren<WeaponGripRig>(true);
		Transform gripRoot = FindGripRigRoot(weaponRoot, grip);
		if (gripRoot == null)
			return false;

		_rightRoot = ResolveRightHandIkRoot(gripRoot, grip);
		return _rightRoot != null;
	}

	public Transform GetLiveLeftHandGripTransform()
	{
		if (UsesRocketLauncherContext && m_RocketLauncherOrder != null)
		{
			Transform launcherRoot = m_RocketLauncherOrder.HandLauncherRoot;
			WeaponGripRig launcherGrip = launcherRoot != null
				? launcherRoot.GetComponentInChildren<WeaponGripRig>(true)
				: null;
			Transform launcherGripRoot = FindGripRigRoot(launcherRoot, launcherGrip);
			Transform authoredIk = launcherGripRoot != null
				? launcherGripRoot.Find(WeaponGripRig.LeftHandIkName)
				: null;
			if (authoredIk != null)
				return authoredIk;
			if (launcherGrip != null && launcherGrip.LeftHandIk != null)
				return launcherGrip.LeftHandIk;
			if (m_RocketLauncherOrder.GripLeftHandTarget != null)
				return m_RocketLauncherOrder.GripLeftHandTarget;
		}

		if (m_UnitEquipment != null && m_UnitEquipment.GripLeftHandTarget != null)
			return m_UnitEquipment.GripLeftHandTarget;

		Transform weaponRoot = GetActiveWeaponRoot();
		WeaponGripRig grip = weaponRoot != null
			? weaponRoot.GetComponentInChildren<WeaponGripRig>(true)
			: null;
		return grip != null ? grip.LeftHandGrip : null;
	}

	public bool HasHandGripClipboard => s_HandGripClipboard.HasRight || s_HandGripClipboard.HasLeft;

	/// <summary>Store local TRS of the current right IK target and left grip.</summary>
	public bool CopyHandGripToClipboard()
	{
		Transform right = GetActiveRightHandTarget();
		Transform left = GetLiveLeftHandGripTransform();
		if (right == null && left == null)
			return false;

		s_HandGripClipboard = default;
		if (right != null)
		{
			s_HandGripClipboard.HasRight = true;
			s_HandGripClipboard.RightPos = right.localPosition;
			s_HandGripClipboard.RightRot = right.localRotation;
		}

		if (left != null)
		{
			s_HandGripClipboard.HasLeft = true;
			s_HandGripClipboard.LeftPos = left.localPosition;
			s_HandGripClipboard.LeftRot = left.localRotation;
		}

		return true;
	}

	/// <summary>Paste clipboard into the active mode's right/left targets.</summary>
	public bool PasteHandGripFromClipboard()
	{
		if (!HasHandGripClipboard)
			return false;

		Transform right = GetActiveRightHandTarget();
		Transform left = GetLiveLeftHandGripTransform();
		bool applied = false;

		if (s_HandGripClipboard.HasRight && right != null)
		{
			right.localPosition = s_HandGripClipboard.RightPos;
			right.localRotation = s_HandGripClipboard.RightRot;
			applied = true;
		}

		if (s_HandGripClipboard.HasLeft && left != null)
		{
			left.localPosition = s_HandGripClipboard.LeftPos;
			left.localRotation = s_HandGripClipboard.LeftRot;
			applied = true;
		}

		return applied;
	}

	public WeaponGripRig EnsureWeaponGripRig()
	{
		Transform weaponRoot = GetActiveWeaponRoot();
		if (weaponRoot == null)
			return null;

		WeaponGripRig gripRig = weaponRoot.GetComponentInChildren<WeaponGripRig>(true);
		if (gripRig == null)
			gripRig = weaponRoot.gameObject.AddComponent<WeaponGripRig>();

		Transform gripRoot = FindGripRigRoot(weaponRoot, gripRig);
		if (gripRoot == null)
		{
			var go = new GameObject(WeaponGripRig.GripRigChildName);
			gripRoot = go.transform;
			gripRoot.SetParent(weaponRoot, false);
		}

		Transform left = EnsureNamedChild(gripRoot, WeaponGripRig.LeftHandIkName);
		if (left == null)
			left = EnsureNamedChild(gripRoot, WeaponGripRig.LeftHandGripName);
		gripRig.SetLeftHandIk(left);
		EnsureRightHandPoseTree(weaponRoot);
		gripRig.BuildCache();

		if (m_UnitEquipment != null)
			m_UnitEquipment.ResolveGripTargets();

		return gripRig;
	}

	public Transform EnsureForeGripLeftHandGrip()
	{
		Transform foregripRoot = GetForegripVisualRoot();
		if (foregripRoot == null)
			return null;

		WeaponForeGrip component = foregripRoot.GetComponent<WeaponForeGrip>();
		if (component == null)
			component = foregripRoot.gameObject.AddComponent<WeaponForeGrip>();

		Transform left = EnsureNamedChild(foregripRoot, WeaponForeGrip.LeftHandGripName);
		component.SetLeftHandGrip(left);

		if (m_UnitEquipment != null)
			m_UnitEquipment.ResolveGripTargets();

		return left;
	}

#if UNITY_EDITOR
	/// <summary>Write WeaponPoseDefinition slots for the chosen posture (weapon only).</summary>
	public bool SaveWeaponPose(TuningPosture _posture)
	{
		ItemDefinition def = ActiveTuningDefinition;
		if (def == null)
			return false;

		WeaponPoseDefinition pose = def.WeaponPoseDefinition;
		if (pose == null)
		{
			Debug.LogWarning($"[WeaponPoseTuner] '{def.name}' has no WeaponPoseDefinition.", def);
			return false;
		}

		if (_posture == m_ActivePosture)
			CaptureAllForSave();

		WeaponStance stance = ToStance(_posture);
		GetPostureAllPoseBuffers(_posture,
			out Vector3 holdPos, out Vector3 holdEu,
			out Vector3 lowPos, out Vector3 lowEu,
			out Vector3 hipPos, out Vector3 hipEu,
			out Vector3 pointPos, out Vector3 pointEu,
			out Vector3 aimPos, out Vector3 aimEu);

		UnityEditor.Undo.RecordObject(pose, "Save WeaponPoseDefinition");
		pose.SetOrAddPose(stance, WeaponPoseState.NotReady, holdPos, holdEu);
		pose.SetOrAddPose(stance, WeaponPoseState.LowReady, lowPos, lowEu);
		pose.SetOrAddPose(stance, WeaponPoseState.HipFire, hipPos, hipEu);
		pose.SetOrAddPose(
			stance,
			WeaponPoseState.HipFireWalk,
			GetHipFireWalkBufferPos(_posture),
			GetHipFireWalkBufferEuler(_posture));
		pose.SetOrAddPose(
			stance,
			WeaponPoseState.HipFireCrouchWalk,
			GetHipFireCrouchWalkBufferPos(_posture),
			GetHipFireCrouchWalkBufferEuler(_posture));
		pose.SetOrAddPose(stance, WeaponPoseState.PointAim, pointPos, pointEu);
		pose.SetOrAddPose(stance, WeaponPoseState.Aiming, aimPos, aimEu);
		pose.SetOrAddPose(
			stance,
			WeaponPoseState.HighReady,
			GetHighReadyBufferPos(_posture),
			GetHighReadyBufferEuler(_posture));
		pose.SetOrAddPose(
			stance,
			WeaponPoseState.NotReadyPatrol,
			GetNotReadyPatrolBufferPos(_posture),
			GetNotReadyPatrolBufferEuler(_posture));

		pose.EnsureSeededPoseSlots();
		UnityEditor.EditorUtility.SetDirty(pose);
		UnityEditor.AssetDatabase.SaveAssets();

		Debug.Log($"[WeaponPoseTuner] Saved {_posture} → {pose.name}", pose);
		return true;
	}

	/// <summary>Write live RightHand/{Stance}/… + LeftHandGrip onto equipped prefabs.</summary>
	public bool SaveGripTransformsToPrefabs()
	{
		Transform weaponRoot = GetActiveWeaponRoot();
		if (weaponRoot == null)
			return false;

		// Capture FIRST — do not rebuild hierarchy before snapshot (avoids wiping live edits).
		WeaponGripRig liveGrip = weaponRoot.GetComponentInChildren<WeaponGripRig>(true);
		if (liveGrip == null)
			liveGrip = EnsureWeaponGripRig();
		if (liveGrip == null)
			return false;

		EnsureRightHandPoseTree(weaponRoot);
		liveGrip = weaponRoot.GetComponentInChildren<WeaponGripRig>(true);
		if (liveGrip == null || !liveGrip.HasValidGrips)
		{
			Debug.LogWarning("[WeaponPoseTuner] Нет валидного GripRig (RightHandGrip + LeftHandGrip).", weaponRoot);
			return false;
		}

		Transform liveGripRoot = FindGripRigRoot(weaponRoot, liveGrip);
		Transform liveRightRoot = ResolveRightHandIkRoot(liveGripRoot, liveGrip);

		Transform liveForeRoot = GetForegripVisualRoot();
		WeaponForeGrip liveFore = liveForeRoot != null
			? liveForeRoot.GetComponentInChildren<WeaponForeGrip>(true)
			: null;
		Transform liveForeLeft = liveFore != null ? liveFore.LeftHandGrip : null;

		Transform weaponBodyLeft = liveGripRoot != null
			? liveGripRoot.Find(WeaponGripRig.LeftHandIkName)
			: null;
		if (weaponBodyLeft == null && liveGrip != null)
			weaponBodyLeft = liveGrip.LeftHandIk;

		Transform captureLeft = weaponBodyLeft;
		if (UsesRocketLauncherContext &&
		    m_RocketLauncherOrder != null &&
		    m_RocketLauncherOrder.GripLeftHandTarget != null)
			captureLeft = m_RocketLauncherOrder.GripLeftHandTarget;

		var snap = new GripSnapshot();
		snap.Capture(liveGrip, liveRightRoot, captureLeft);

		Vector3 fgPos = Vector3.zero;
		Quaternion fgRot = Quaternion.identity;
		bool hasForegripLeft = liveForeLeft != null;
		if (hasForegripLeft)
		{
			fgPos = liveForeLeft.localPosition;
			fgRot = liveForeLeft.localRotation;
		}

		string savedForegripPath = null;
		if (hasForegripLeft)
		{
			string fgPath = ResolveForegripPrefabAssetPath(liveForeRoot != null ? liveForeRoot.gameObject : null);
			if (string.IsNullOrEmpty(fgPath))
			{
				Debug.LogWarning(
					"[WeaponPoseTuner] Не найден префаб рукоятки — LeftHandGrip не записан. " +
					"Поставь рукоятку кнопкой тюнера и сохрани снова.",
					liveForeRoot);
			}
			else
			{
				GameObject fgContents = UnityEditor.PrefabUtility.LoadPrefabContents(fgPath);
				try
				{
					WeaponForeGrip prefabFg = fgContents.GetComponent<WeaponForeGrip>();
					if (prefabFg == null)
						prefabFg = fgContents.AddComponent<WeaponForeGrip>();
					Transform leftFg = EnsureNamedChild(fgContents.transform, WeaponForeGrip.LeftHandGripName);
					leftFg.localPosition = fgPos;
					leftFg.localRotation = fgRot;
					prefabFg.SetLeftHandGrip(leftFg);
					UnityEditor.PrefabUtility.SaveAsPrefabAsset(fgContents, fgPath);
					savedForegripPath = fgPath;
				}
				finally
				{
					UnityEditor.PrefabUtility.UnloadPrefabContents(fgContents);
				}
			}
		}

		string weaponPrefabPath = ResolveEquippedPrefabAssetPath(weaponRoot.gameObject);
		if (string.IsNullOrEmpty(weaponPrefabPath))
		{
			Debug.LogWarning(
				"[WeaponPoseTuner] Не найден префаб оружия. " +
				"Для винтовки нужен EquippedVisualPrefab, для гранатомёта — RocketLauncherHandPrefab на Item.",
				weaponRoot);
			return false;
		}

		GameObject weaponContents = UnityEditor.PrefabUtility.LoadPrefabContents(weaponPrefabPath);
		try
		{
			WeaponGripRig prefabGrip = weaponContents.GetComponentInChildren<WeaponGripRig>(true);
			if (prefabGrip == null)
				prefabGrip = weaponContents.AddComponent<WeaponGripRig>();

			Transform gripRoot = FindGripRigRoot(weaponContents.transform, prefabGrip);
			if (gripRoot == null)
			{
				var go = new GameObject(WeaponGripRig.GripRigChildName);
				gripRoot = go.transform;
				gripRoot.SetParent(weaponContents.transform, false);
			}

			Transform rightMarker = EnsureNamedChild(gripRoot, WeaponGripRig.RightHandGripName);
			Transform left = ResolveWeaponLeftHandIk(gripRoot, prefabGrip);
			if (left == null)
				left = EnsureNamedChild(gripRoot, WeaponGripRig.LeftHandIkName);
			// Always persist onto LeftHandIK — never a parallel LeftHandGrip sibling.
			Transform authoredLeft = gripRoot.Find(WeaponGripRig.LeftHandIkName);
			if (authoredLeft != null)
				left = authoredLeft;
			snap.ApplyMarkers(rightMarker, left);
			prefabGrip.SetGrips(rightMarker, left);
			prefabGrip.SetLeftHandIk(left);

			Transform prefabRightRoot = ResolveRightHandIkRoot(gripRoot, prefabGrip);
			if (prefabRightRoot == null)
				prefabRightRoot = EnsureNamedChild(gripRoot, WeaponGripRig.RightHandIkRootName);
			snap.ApplyRightHandTree(prefabRightRoot);

			prefabGrip.SetRightHandAllPoseTargets(
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.StandingName, WeaponPoseState.NotReady),
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.StandingName, WeaponPoseState.LowReady),
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.StandingName, WeaponPoseState.HipFire),
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.StandingName, WeaponPoseState.PointAim),
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.StandingName, WeaponPoseState.Aiming),
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.CrouchName, WeaponPoseState.NotReady),
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.CrouchName, WeaponPoseState.LowReady),
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.CrouchName, WeaponPoseState.HipFire),
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.CrouchName, WeaponPoseState.PointAim),
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.CrouchName, WeaponPoseState.Aiming),
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.VehicleName, WeaponPoseState.NotReady),
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.VehicleName, WeaponPoseState.LowReady),
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.VehicleName, WeaponPoseState.HipFire),
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.VehicleName, WeaponPoseState.PointAim),
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.VehicleName, WeaponPoseState.Aiming));
			prefabGrip.SetHighReadyPoseTargets(
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.StandingName, WeaponPoseState.HighReady),
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.CrouchName, WeaponPoseState.HighReady),
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.VehicleName, WeaponPoseState.HighReady));
			prefabGrip.SetNotReadyPatrolPoseTargets(
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.StandingName, WeaponPoseState.NotReadyPatrol),
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.CrouchName, WeaponPoseState.NotReadyPatrol),
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.VehicleName, WeaponPoseState.NotReadyPatrol));
			prefabGrip.SetHipFireWalkPoseTargets(
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.StandingName, WeaponPoseState.HipFireWalk),
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.CrouchName, WeaponPoseState.HipFireWalk),
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.VehicleName, WeaponPoseState.HipFireWalk));
			prefabGrip.SetHipFireCrouchWalkPoseTargets(
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.StandingName, WeaponPoseState.HipFireCrouchWalk),
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.CrouchName, WeaponPoseState.HipFireCrouchWalk),
				ResolvePoseSlotUnderRoot(prefabRightRoot, WeaponGripRig.VehicleName, WeaponPoseState.HipFireCrouchWalk));

			UnityEditor.PrefabUtility.SaveAsPrefabAsset(weaponContents, weaponPrefabPath);
		}
		finally
		{
			UnityEditor.PrefabUtility.UnloadPrefabContents(weaponContents);
		}

		// Re-apply onto live instance — Unity may refresh prefab instance after asset save.
		if (weaponRoot != null)
		{
			liveGrip = weaponRoot.GetComponentInChildren<WeaponGripRig>(true);
			liveGripRoot = FindGripRigRoot(weaponRoot, liveGrip);
			liveRightRoot = ResolveRightHandIkRoot(liveGripRoot, liveGrip);
			Transform liveLeft = liveGripRoot != null
				? liveGripRoot.Find(WeaponGripRig.LeftHandIkName)
				: null;
			if (liveLeft == null)
				liveLeft = weaponBodyLeft;
			if (liveGrip != null)
				liveGrip.SetLeftHandIk(liveLeft != null ? liveLeft : liveGrip.LeftHandIk);
			if (liveGrip != null && liveGrip.HasValidGrips)
			{
				snap.ApplyMarkers(liveGrip.RightHandGrip, liveLeft != null ? liveLeft : liveGrip.LeftHandIk);
				if (liveRightRoot != null)
					snap.ApplyRightHandTree(liveRightRoot);
			}

			if (liveLeft != null)
				snap.ApplyLeft(liveLeft);
			if (UsesRocketLauncherContext && m_RocketLauncherOrder != null)
			{
				m_RocketLauncherOrder.EnsureGripRigTargets();
				m_RocketLauncherOrder.RefreshHandIkTargets();
			}
		}

		if (hasForegripLeft)
		{
			ItemDefinition gripItem = GetCurrentForegripItem();
			if (gripItem != null &&
			    m_WeaponRuntime != null &&
			    m_WeaponRuntime.RuntimeState != null &&
			    m_WeaponRuntime.RuntimeState.WeaponDefinition != null &&
			    TryFindEnabledUnderBarrelSlot(
				    m_WeaponRuntime.RuntimeState.WeaponDefinition,
				    out int ubIndex,
				    out _))
			{
				ApplyForegripItem(
					m_WeaponRuntime.RuntimeState,
					m_WeaponRuntime.RuntimeState.WeaponDefinition,
					ubIndex,
					gripItem);
			}

			Transform restoredLeft = GetForegripVisualRoot() != null
				? GetForegripVisualRoot().GetComponentInChildren<WeaponForeGrip>(true)?.LeftHandGrip
				: null;
			if (restoredLeft != null)
			{
				restoredLeft.localPosition = fgPos;
				restoredLeft.localRotation = fgRot;
			}
		}

		UnityEditor.AssetDatabase.SaveAssets();

		// Keep WeaponPose SO in sync with what is under Hand_R right now —
		// otherwise tuner-off reloads a different weapon pose and hands look wrong.
		SaveWeaponPose(m_ActivePosture);

		FinishGripSavePreview();

		string leftLog = savedForegripPath != null
			? $"LeftHandGrip → '{savedForegripPath}'"
			: $"Left (оружие)={snap.LeftPos}";
		Debug.Log(
			$"[WeaponPoseTuner] Руки сохранены → '{weaponPrefabPath}'.\n" +
			$"RightHandGrip={snap.RightMarkerPos} {leftLog}\n" +
			$"Standing PointAim={snap.Get(WeaponGripRig.StandingName, WeaponPoseState.PointAim)} " +
			$"LowReady={snap.Get(WeaponGripRig.StandingName, WeaponPoseState.LowReady)}\n" +
			"Превью = полный IK. Сверь все 5 pose-слотов в том же режиме.",
			weaponRoot);
		return true;
	}

	/// <summary>
	/// After prefab write: drop grip selection (so IK is not suppressed), refresh resolver,
	/// re-apply weapon + body so Play preview matches tuner-off gameplay weights.
	/// </summary>
	private void FinishGripSavePreview()
	{
		if (m_UnitEquipment != null)
		{
			m_UnitEquipment.ResolveGripTargets();
			WeaponGripResolver resolver = m_UnitEquipment.GetComponent<WeaponGripResolver>();
			if (resolver == null)
				resolver = GetComponent<WeaponGripResolver>();
			resolver?.RebuildCache();
			resolver?.RefreshTargets(force: true);
		}

		ApplyActiveTargetPoseToWeapon();
		SyncUnitBodyToActiveMode();
		m_EquippedWeaponPose?.ApplyImmediateFromEquipment();
	}

	private struct LocalTrs
	{
		public Vector3 Pos;
		public Quaternion Rot;
		public Vector3 Scale;
		public bool Valid;

		public static LocalTrs From(Transform _t)
		{
			if (_t == null)
				return default;
			return new LocalTrs
			{
				Pos = _t.localPosition,
				Rot = _t.localRotation,
				Scale = _t.localScale,
				Valid = true,
			};
		}

		public void Apply(Transform _t)
		{
			if (!Valid || _t == null)
				return;
			_t.localPosition = Pos;
			_t.localRotation = Rot;
			_t.localScale = Scale;
		}

		public override string ToString() => Valid ? Pos.ToString("F3") : "—";
	}

	private struct GripSnapshot
	{
		public LocalTrs RightMarker;
		public LocalTrs Left;
		public LocalTrs StandingHoldNotReady;
		public LocalTrs StandingLowReady;
		public LocalTrs StandingHipFire;
		public LocalTrs StandingHipFireWalk;
		public LocalTrs StandingHipFireCrouchWalk;
		public LocalTrs StandingPointAim;
		public LocalTrs StandingAiming;
		public LocalTrs StandingHighReady;
		public LocalTrs StandingHoldNotReadyPatrol;
		public LocalTrs CrouchHoldNotReady;
		public LocalTrs CrouchLowReady;
		public LocalTrs CrouchHipFire;
		public LocalTrs CrouchHipFireWalk;
		public LocalTrs CrouchHipFireCrouchWalk;
		public LocalTrs CrouchPointAim;
		public LocalTrs CrouchAiming;
		public LocalTrs CrouchHighReady;
		public LocalTrs CrouchHoldNotReadyPatrol;
		public LocalTrs VehicleHoldNotReady;
		public LocalTrs VehicleLowReady;
		public LocalTrs VehicleHipFire;
		public LocalTrs VehicleHipFireWalk;
		public LocalTrs VehicleHipFireCrouchWalk;
		public LocalTrs VehiclePointAim;
		public LocalTrs VehicleAiming;
		public LocalTrs VehicleHighReady;
		public LocalTrs VehicleHoldNotReadyPatrol;

		public Vector3 RightMarkerPos => RightMarker.Pos;
		public Vector3 LeftPos => Left.Pos;

		public void Capture(WeaponGripRig _grip, Transform _rightRoot, Transform _liveLeft)
		{
			RightMarker = LocalTrs.From(_grip != null ? _grip.RightHandGrip : null);
			Left = LocalTrs.From(_liveLeft != null ? _liveLeft : _grip != null ? _grip.LeftHandGrip : null);
			CaptureStance(_rightRoot, WeaponGripRig.StandingName,
				ref StandingHoldNotReady, ref StandingLowReady, ref StandingHipFire, ref StandingPointAim,
				ref StandingAiming, ref StandingHighReady, ref StandingHoldNotReadyPatrol);
			StandingHipFireWalk = LocalTrs.From(
				ResolvePoseSlotUnderRoot(_rightRoot, WeaponGripRig.StandingName, WeaponPoseState.HipFireWalk));
			StandingHipFireCrouchWalk = LocalTrs.From(
				ResolvePoseSlotUnderRoot(_rightRoot, WeaponGripRig.StandingName, WeaponPoseState.HipFireCrouchWalk));
			CaptureStance(_rightRoot, WeaponGripRig.CrouchName,
				ref CrouchHoldNotReady, ref CrouchLowReady, ref CrouchHipFire, ref CrouchPointAim,
				ref CrouchAiming, ref CrouchHighReady, ref CrouchHoldNotReadyPatrol);
			CrouchHipFireWalk = LocalTrs.From(
				ResolvePoseSlotUnderRoot(_rightRoot, WeaponGripRig.CrouchName, WeaponPoseState.HipFireWalk));
			CrouchHipFireCrouchWalk = LocalTrs.From(
				ResolvePoseSlotUnderRoot(_rightRoot, WeaponGripRig.CrouchName, WeaponPoseState.HipFireCrouchWalk));
			CaptureStance(_rightRoot, WeaponGripRig.VehicleName,
				ref VehicleHoldNotReady, ref VehicleLowReady, ref VehicleHipFire, ref VehiclePointAim,
				ref VehicleAiming, ref VehicleHighReady, ref VehicleHoldNotReadyPatrol);
			VehicleHipFireWalk = LocalTrs.From(
				ResolvePoseSlotUnderRoot(_rightRoot, WeaponGripRig.VehicleName, WeaponPoseState.HipFireWalk));
			VehicleHipFireCrouchWalk = LocalTrs.From(
				ResolvePoseSlotUnderRoot(_rightRoot, WeaponGripRig.VehicleName, WeaponPoseState.HipFireCrouchWalk));
		}

		public void ApplyMarkers(Transform _right, Transform _left)
		{
			RightMarker.Apply(_right);
			Left.Apply(_left);
		}

		public void ApplyLeft(Transform _left) => Left.Apply(_left);

		public void ApplyRightHandTree(Transform _rightRoot)
		{
			if (_rightRoot == null)
				return;
			ApplyStance(_rightRoot, WeaponGripRig.StandingName,
				StandingHoldNotReady, StandingLowReady, StandingHipFire, StandingPointAim,
				StandingAiming, StandingHighReady, StandingHoldNotReadyPatrol);
			ApplySlot(_rightRoot, WeaponGripRig.StandingName, WeaponPoseState.HipFireWalk, StandingHipFireWalk);
			ApplySlot(_rightRoot, WeaponGripRig.StandingName, WeaponPoseState.HipFireCrouchWalk, StandingHipFireCrouchWalk);
			ApplyStance(_rightRoot, WeaponGripRig.CrouchName,
				CrouchHoldNotReady, CrouchLowReady, CrouchHipFire, CrouchPointAim,
				CrouchAiming, CrouchHighReady, CrouchHoldNotReadyPatrol);
			ApplySlot(_rightRoot, WeaponGripRig.CrouchName, WeaponPoseState.HipFireWalk, CrouchHipFireWalk);
			ApplySlot(_rightRoot, WeaponGripRig.CrouchName, WeaponPoseState.HipFireCrouchWalk, CrouchHipFireCrouchWalk);
			ApplyStance(_rightRoot, WeaponGripRig.VehicleName,
				VehicleHoldNotReady, VehicleLowReady, VehicleHipFire, VehiclePointAim,
				VehicleAiming, VehicleHighReady, VehicleHoldNotReadyPatrol);
			ApplySlot(_rightRoot, WeaponGripRig.VehicleName, WeaponPoseState.HipFireWalk, VehicleHipFireWalk);
			ApplySlot(_rightRoot, WeaponGripRig.VehicleName, WeaponPoseState.HipFireCrouchWalk, VehicleHipFireCrouchWalk);
		}

		public LocalTrs Get(string _stance, WeaponPoseState _pose)
		{
			if (_pose == WeaponPoseState.HipFireWalk)
			{
				if (_stance == WeaponGripRig.StandingName)
					return StandingHipFireWalk;
				if (_stance == WeaponGripRig.CrouchName)
					return CrouchHipFireWalk;
				return VehicleHipFireWalk;
			}

			if (_pose == WeaponPoseState.HipFireCrouchWalk)
			{
				if (_stance == WeaponGripRig.StandingName)
					return StandingHipFireCrouchWalk;
				if (_stance == WeaponGripRig.CrouchName)
					return CrouchHipFireCrouchWalk;
				return VehicleHipFireCrouchWalk;
			}

			if (_stance == WeaponGripRig.StandingName)
				return Pick(StandingHoldNotReady, StandingLowReady, StandingHipFire, StandingPointAim,
					StandingAiming, StandingHighReady, StandingHoldNotReadyPatrol, _pose);
			if (_stance == WeaponGripRig.CrouchName)
				return Pick(CrouchHoldNotReady, CrouchLowReady, CrouchHipFire, CrouchPointAim,
					CrouchAiming, CrouchHighReady, CrouchHoldNotReadyPatrol, _pose);
			return Pick(VehicleHoldNotReady, VehicleLowReady, VehicleHipFire, VehiclePointAim,
				VehicleAiming, VehicleHighReady, VehicleHoldNotReadyPatrol, _pose);
		}

		private static void CaptureStance(
			Transform _rightRoot,
			string _stance,
			ref LocalTrs _hold,
			ref LocalTrs _low,
			ref LocalTrs _hip,
			ref LocalTrs _point,
			ref LocalTrs _aim,
			ref LocalTrs _highReady,
			ref LocalTrs _holdPatrol)
		{
			_hold = LocalTrs.From(ResolvePoseSlotUnderRoot(_rightRoot, _stance, WeaponPoseState.NotReady));
			_low = LocalTrs.From(ResolvePoseSlotUnderRoot(_rightRoot, _stance, WeaponPoseState.LowReady));
			_hip = LocalTrs.From(ResolvePoseSlotUnderRoot(_rightRoot, _stance, WeaponPoseState.HipFire));
			_point = LocalTrs.From(ResolvePoseSlotUnderRoot(_rightRoot, _stance, WeaponPoseState.PointAim));
			_aim = LocalTrs.From(ResolvePoseSlotUnderRoot(_rightRoot, _stance, WeaponPoseState.Aiming));
			_highReady = LocalTrs.From(ResolvePoseSlotUnderRoot(_rightRoot, _stance, WeaponPoseState.HighReady));
			_holdPatrol = LocalTrs.From(ResolvePoseSlotUnderRoot(_rightRoot, _stance, WeaponPoseState.NotReadyPatrol));
		}

		private static void ApplyStance(
			Transform _rightRoot,
			string _stance,
			LocalTrs _hold,
			LocalTrs _low,
			LocalTrs _hip,
			LocalTrs _point,
			LocalTrs _aim,
			LocalTrs _highReady,
			LocalTrs _holdPatrol)
		{
			ApplySlot(_rightRoot, _stance, WeaponPoseState.NotReady, _hold);
			ApplySlot(_rightRoot, _stance, WeaponPoseState.LowReady, _low);
			ApplySlot(_rightRoot, _stance, WeaponPoseState.HipFire, _hip);
			ApplySlot(_rightRoot, _stance, WeaponPoseState.PointAim, _point);
			ApplySlot(_rightRoot, _stance, WeaponPoseState.Aiming, _aim);
			ApplySlot(_rightRoot, _stance, WeaponPoseState.HighReady, _highReady);
			ApplySlot(_rightRoot, _stance, WeaponPoseState.NotReadyPatrol, _holdPatrol);
		}

		private static LocalTrs Pick(
			LocalTrs _hold,
			LocalTrs _low,
			LocalTrs _hip,
			LocalTrs _point,
			LocalTrs _aim,
			LocalTrs _highReady,
			LocalTrs _holdPatrol,
			WeaponPoseState _pose)
		{
			return _pose switch
			{
				WeaponPoseState.NotReady => _hold,
				WeaponPoseState.NotReadyPatrol => _holdPatrol,
				WeaponPoseState.HipFire => _hip,
				WeaponPoseState.PointAim => _point,
				WeaponPoseState.Aiming => _aim,
				WeaponPoseState.HighReady => _highReady,
				_ => _low,
			};
		}

		private static void ApplySlot(
			Transform _rightRoot,
			string _stance,
			WeaponPoseState _pose,
			LocalTrs _trs)
		{
			Transform slot = ResolvePoseSlotUnderRoot(_rightRoot, _stance, _pose);
			_trs.Apply(slot);
		}
	}

	/// <summary>
	/// Runtime Instantiate (RPG H) often has no PrefabInstance link — fall back to ItemDefinition hand/equipped prefab.
	/// </summary>
	private string ResolveEquippedPrefabAssetPath(GameObject _liveRoot)
	{
		string fromInstance = ResolveSceneObjectPrefabPath(_liveRoot);
		if (!string.IsNullOrEmpty(fromInstance))
			return fromInstance;

		ItemDefinition def = ActiveTuningDefinition;
		if (def == null)
			return null;

		GameObject assetPrefab = null;
		if (UsesRocketLauncherContext)
			assetPrefab = def.RocketLauncherHandPrefab != null ? def.RocketLauncherHandPrefab : def.EquippedVisualPrefab;
		else
			assetPrefab = def.EquippedVisualPrefab;

		if (assetPrefab == null)
			return null;

		return UnityEditor.AssetDatabase.GetAssetPath(assetPrefab);
	}

	private string ResolveForegripPrefabAssetPath(GameObject _liveRoot)
	{
		ItemDefinition item = GetCurrentForegripItem();
		GameObject assetPrefab = item != null ? item.EquippedVisualPrefab : null;
		if (assetPrefab == null && item != null && item.WeaponAttachmentDefinition != null)
			assetPrefab = item.WeaponAttachmentDefinition.EquippedVisualPrefab;
		if (assetPrefab != null)
		{
			string fromAsset = UnityEditor.AssetDatabase.GetAssetPath(assetPrefab);
			if (!string.IsNullOrEmpty(fromAsset))
				return fromAsset;
		}

		string fromInstance = ResolveSceneObjectPrefabPath(_liveRoot);
		if (!string.IsNullOrEmpty(fromInstance) &&
		    fromInstance.IndexOf("ForeGrip", System.StringComparison.OrdinalIgnoreCase) >= 0)
			return fromInstance;

		return null;
	}

	private static string ResolveSceneObjectPrefabPath(GameObject _live)
	{
		if (_live == null)
			return null;

		GameObject source = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(_live);
		if (source != null)
		{
			string path = UnityEditor.AssetDatabase.GetAssetPath(source);
			if (!string.IsNullOrEmpty(path))
				return path;
		}

		string nearest = UnityEditor.PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(_live);
		return string.IsNullOrEmpty(nearest) ? null : nearest;
	}
#endif
	#endregion

	#region Private Methods
	private void BeginTuningSession()
	{
		ResolveReferences();
		CaptureBodyStateSnapshot();

		m_ActiveTarget = TuningTarget.HandsFrozen;
		m_LastAppliedTarget = TuningTarget.HandsFrozen;
		LoadFromEquippedDefinition();
		SyncUnitBodyToActiveMode();
		TuningModeChanged?.Invoke();
		Debug.Log(
			"[WeaponPoseTuner] ON. Hands Frozen (оружие) → LowReady / PointAim (руки + IK) → Save.",
			this);
	}

	private void EndTuningSession()
	{
		m_LastAppliedTarget = (TuningTarget)(-1);
		m_LastAppliedPosture = (TuningPosture)(-1);
		RestoreWalkAnimatorIfFrozen();
		m_SeatPose?.SetTunerPassengerPreview(false, false);
		RestoreBodyStateSnapshot();
		m_EquippedWeaponPose?.ApplyImmediateFromEquipment();

		if (m_UnitEquipment != null)
		{
			m_UnitEquipment.ResolveGripTargets();
			WeaponGripResolver resolver = m_UnitEquipment.GetComponent<WeaponGripResolver>();
			resolver?.RebuildCache();
			resolver?.RefreshTargets(force: true);
		}

		TuningModeChanged?.Invoke();
		Debug.Log("[WeaponPoseTuner] OFF.", this);
	}

	private void CaptureBodyStateSnapshot()
	{
		ResolveReferences();
		m_SavedReadyWanted = m_ReadyHands != null && m_ReadyHands.WantsReady;
		m_SavedStance = m_AnimatorStance != null
			? m_AnimatorStance.CurrentStance
			: LocomotionStance.Standing;
		m_HasBodyStateSnapshot = true;
	}

	private void RestoreBodyStateSnapshot()
	{
		if (!m_HasBodyStateSnapshot)
			return;

		ResolveReferences();
		if (m_AnimatorStance != null)
			m_AnimatorStance.RequestStance(m_SavedStance);
		if (m_ReadyHands != null)
			m_ReadyHands.SetReadyWanted(m_SavedReadyWanted, true);

		m_HasBodyStateSnapshot = false;
	}

	private void TickWalkAnimatorFreeze()
	{
		if (!ShouldFreezeWalkAnimator)
		{
			RestoreWalkAnimatorIfFrozen();
			return;
		}

		Animator anim = m_UnitAnimator;
		if (anim == null || !anim.isActiveAndEnabled)
			return;

		string leaf = m_ActiveTarget == TuningTarget.HipFireCrouchWalk
			? "RifleCrouch_Move"
			: "Walk_Aim_F_Loop";
		string subMachine = m_ActiveTarget == TuningTarget.HipFireCrouchWalk
			? UnitAnimatorWeaponMode.SubStateMachineRifleCrouch
			: UnitAnimatorWeaponMode.SubStateMachineRifleStanding;
		string qualified =
			$"{UnitAnimatorWeaponMode.BaseLayerAnimatorName}.{subMachine}.{leaf}";

		int qualifiedHash = Animator.StringToHash(qualified);
		int leafHash = Animator.StringToHash(leaf);
		bool hasState = anim.HasState(0, qualifiedHash);
		bool inTransition = anim.IsInTransition(0);
		AnimatorStateInfo current = anim.GetCurrentAnimatorStateInfo(0);
		bool onTarget = !inTransition
		                && (current.fullPathHash == qualifiedHash || current.shortNameHash == leafHash);

		if (hasState && (!onTarget || !m_WalkAnimatorFrozen))
			anim.Play(qualified, 0, c_WalkFreezeNormalizedTime);

		// Keep the graph on the walk leaf so AnyState idle transitions do not start.
		anim.SetFloat(UnitClickToMove.ParamNavSpeed, 0.2f);
		anim.SetInteger(UnitClickToMove.ParamLocomotionTier, (int)UnitClickToMove.MoveTier.Walk);
		anim.speed = 0f;
		m_WalkAnimatorFrozen = true;
	}

	private void RestoreWalkAnimatorIfFrozen()
	{
		if (!m_WalkAnimatorFrozen)
			return;

		if (m_UnitAnimator != null)
			m_UnitAnimator.speed = 1f;
		m_WalkAnimatorFrozen = false;
	}

	private void LoadPostureFromDefinition(ItemDefinition _def, TuningPosture _posture)
	{
		WeaponStance stance = ToStance(_posture);
		WeaponPoseDefinition pose = _def.WeaponPoseDefinition;
		if (pose != null)
		{
			pose.EnsureSeededPoseSlots();
			pose.GetPoseOrFallback(stance, WeaponPoseState.NotReady, out WeaponPoseEntry hold);
			pose.GetPoseOrFallback(stance, WeaponPoseState.LowReady, out WeaponPoseEntry low);
			pose.GetPoseOrFallback(stance, WeaponPoseState.HipFire, out WeaponPoseEntry hip);
			pose.GetPoseOrFallback(stance, WeaponPoseState.HipFireWalk, out WeaponPoseEntry hipWalk);
			pose.GetPoseOrFallback(stance, WeaponPoseState.HipFireCrouchWalk, out WeaponPoseEntry hipCrouchWalk);
			pose.GetPoseOrFallback(stance, WeaponPoseState.PointAim, out WeaponPoseEntry point);
			pose.GetPoseOrFallback(stance, WeaponPoseState.Aiming, out WeaponPoseEntry aim);
			pose.GetPoseOrFallback(stance, WeaponPoseState.HighReady, out WeaponPoseEntry high);
			pose.GetPoseOrFallback(stance, WeaponPoseState.NotReadyPatrol, out WeaponPoseEntry patrol);

			bool hasAuthored = pose.TryGetPose(stance, WeaponPoseState.LowReady, out _)
			                   || pose.TryGetPose(stance, WeaponPoseState.PointAim, out _)
			                   || pose.TryGetPose(WeaponStance.Standing, WeaponPoseState.LowReady, out _)
			                   || pose.TryGetPose(WeaponStance.Standing, WeaponPoseState.PointAim, out _);
			if (hasAuthored)
			{
				SetPostureAllPoseBuffers(
					_posture,
					hold.Position, hold.EulerAngles,
					low.Position, low.EulerAngles,
					hip.Position, hip.EulerAngles,
					point.Position, point.EulerAngles,
					aim.Position, aim.EulerAngles);
				SetHighReadyBuffer(_posture, high.Position, high.EulerAngles);
				SetNotReadyPatrolBuffer(_posture, patrol.Position, patrol.EulerAngles);
				SetHipFireWalkBuffer(_posture, hipWalk.Position, hipWalk.EulerAngles);
				SetHipFireCrouchWalkBuffer(_posture, hipCrouchWalk.Position, hipCrouchWalk.EulerAngles);
				return;
			}
		}

		// Flat ItemDefinition fallback for old assets without Pose SO.
		switch (_posture)
		{
			case TuningPosture.Crouch:
				SetPostureAllPoseBuffers(
					_posture,
					_def.CrouchRightHandLocalPosition,
					_def.CrouchRightHandLocalEulerAngles,
					_def.CrouchRightHandLocalPosition,
					_def.CrouchRightHandLocalEulerAngles,
					_def.CrouchRightHandLocalPosition,
					_def.CrouchRightHandLocalEulerAngles,
					_def.CrouchRightHandReadyLocalPosition,
					_def.CrouchRightHandReadyLocalEulerAngles,
					_def.CrouchRightHandReadyLocalPosition,
					_def.CrouchRightHandReadyLocalEulerAngles);
				break;
			case TuningPosture.Vehicle:
				SetPostureAllPoseBuffers(
					_posture,
					_def.VehicleRightHandLocalPosition,
					_def.VehicleRightHandLocalEulerAngles,
					_def.VehicleRightHandLocalPosition,
					_def.VehicleRightHandLocalEulerAngles,
					_def.VehicleRightHandLocalPosition,
					_def.VehicleRightHandLocalEulerAngles,
					_def.VehicleRightHandReadyLocalPosition,
					_def.VehicleRightHandReadyLocalEulerAngles,
					_def.VehicleRightHandReadyLocalPosition,
					_def.VehicleRightHandReadyLocalEulerAngles);
				break;
			default:
				SetPostureAllPoseBuffers(
					_posture,
					_def.RightHandLocalPosition,
					_def.RightHandLocalEulerAngles,
					_def.RightHandLocalPosition,
					_def.RightHandLocalEulerAngles,
					_def.RightHandLocalPosition,
					_def.RightHandLocalEulerAngles,
					_def.RightHandReadyLocalPosition,
					_def.RightHandReadyLocalEulerAngles,
					_def.RightHandReadyLocalPosition,
					_def.RightHandReadyLocalEulerAngles);
				break;
		}
	}

	private WeaponStance GetActiveWeaponStance() => ToStance(m_ActivePosture);

	private static WeaponStance ToStance(TuningPosture _posture) => _posture switch
	{
		TuningPosture.Crouch => WeaponStance.Crouching,
		TuningPosture.Vehicle => WeaponStance.Vehicle,
		_ => WeaponStance.Standing,
	};

	private bool TryGetGripRightHandTargets(out Transform _notReady, out Transform _ready)
	{
		_notReady = null;
		_ready = null;
		Transform weaponRoot = GetActiveWeaponRoot();
		if (weaponRoot == null)
			return false;

		WeaponGripRig grip = weaponRoot.GetComponentInChildren<WeaponGripRig>(true);
		return grip != null && grip.TryGetRightHandTargets(GetActiveWeaponStance(), out _notReady, out _ready);
	}

	private void EnsureRightHandPoseTree(Transform _weaponRoot)
	{
		if (_weaponRoot == null)
			return;

		WeaponGripRig grip = _weaponRoot.GetComponentInChildren<WeaponGripRig>(true);
		if (grip == null)
			return;

		Transform gripRoot = FindGripRigRoot(_weaponRoot, grip);
		if (gripRoot == null)
			gripRoot = grip.transform;

		// Prefer existing RightHandIK (or legacy RightHand) under GripRig.
		Transform rightRoot = ResolveRightHandIkRoot(gripRoot, grip);
		if (rightRoot == null)
			rightRoot = EnsureNamedChild(gripRoot, WeaponGripRig.RightHandIkRootName);

		Transform standing = EnsureNamedChild(rightRoot, WeaponGripRig.StandingName);
		Transform crouch = EnsureNamedChild(rightRoot, WeaponGripRig.CrouchName);
		Transform vehicle = EnsureNamedChild(rightRoot, WeaponGripRig.VehicleName);

		grip.SetRightHandAllPoseTargets(
			EnsurePoseSlot(standing, WeaponPoseState.NotReady),
			EnsurePoseSlot(standing, WeaponPoseState.LowReady),
			EnsurePoseSlot(standing, WeaponPoseState.HipFire),
			EnsurePoseSlot(standing, WeaponPoseState.PointAim),
			EnsurePoseSlot(standing, WeaponPoseState.Aiming),
			EnsurePoseSlot(crouch, WeaponPoseState.NotReady),
			EnsurePoseSlot(crouch, WeaponPoseState.LowReady),
			EnsurePoseSlot(crouch, WeaponPoseState.HipFire),
			EnsurePoseSlot(crouch, WeaponPoseState.PointAim),
			EnsurePoseSlot(crouch, WeaponPoseState.Aiming),
			EnsurePoseSlot(vehicle, WeaponPoseState.NotReady),
			EnsurePoseSlot(vehicle, WeaponPoseState.LowReady),
			EnsurePoseSlot(vehicle, WeaponPoseState.HipFire),
			EnsurePoseSlot(vehicle, WeaponPoseState.PointAim),
			EnsurePoseSlot(vehicle, WeaponPoseState.Aiming));
		grip.SetHighReadyPoseTargets(
			EnsurePoseSlot(standing, WeaponPoseState.HighReady),
			EnsurePoseSlot(crouch, WeaponPoseState.HighReady),
			EnsurePoseSlot(vehicle, WeaponPoseState.HighReady));
		grip.SetNotReadyPatrolPoseTargets(
			EnsurePoseSlot(standing, WeaponPoseState.NotReadyPatrol),
			EnsurePoseSlot(crouch, WeaponPoseState.NotReadyPatrol),
			EnsurePoseSlot(vehicle, WeaponPoseState.NotReadyPatrol));
		grip.SetHipFireWalkPoseTargets(
			EnsurePoseSlot(standing, WeaponPoseState.HipFireWalk),
			EnsurePoseSlot(crouch, WeaponPoseState.HipFireWalk),
			EnsurePoseSlot(vehicle, WeaponPoseState.HipFireWalk));
		grip.SetHipFireCrouchWalkPoseTargets(
			EnsurePoseSlot(standing, WeaponPoseState.HipFireCrouchWalk),
			EnsurePoseSlot(crouch, WeaponPoseState.HipFireCrouchWalk),
			EnsurePoseSlot(vehicle, WeaponPoseState.HipFireCrouchWalk));
		SeedHighReadySlotFromAiming(standing);
		SeedHighReadySlotFromAiming(crouch);
		SeedHighReadySlotFromAiming(vehicle);
		SeedNotReadyPatrolSlotFromHoldNotReady(standing);
		SeedNotReadyPatrolSlotFromHoldNotReady(crouch);
		SeedNotReadyPatrolSlotFromHoldNotReady(vehicle);
		SeedHipFireWalkSlotFromHipFire(standing);
		SeedHipFireWalkSlotFromHipFire(crouch);
		SeedHipFireWalkSlotFromHipFire(vehicle);
		SeedHipFireCrouchWalkSlotFromHipFire(standing);
		SeedHipFireCrouchWalkSlotFromHipFire(crouch);
		SeedHipFireCrouchWalkSlotFromHipFire(vehicle);
	}

	private static void SeedHighReadySlotFromAiming(Transform _stanceRoot)
	{
		if (_stanceRoot == null)
			return;
		Transform high = _stanceRoot.Find(WeaponGripRig.HighReadyName);
		Transform aim = _stanceRoot.Find(WeaponGripRig.AimingName) ?? _stanceRoot.Find(WeaponGripRig.PointAimName);
		if (high == null || aim == null)
			return;
		if (high.localPosition != Vector3.zero || high.localRotation != Quaternion.identity)
			return;
		high.localPosition = aim.localPosition;
		high.localRotation = aim.localRotation;
	}

	private static void SeedNotReadyPatrolSlotFromHoldNotReady(Transform _stanceRoot)
	{
		if (_stanceRoot == null)
			return;
		Transform patrol = _stanceRoot.Find(WeaponGripRig.HoldNotReadyPatrolName);
		Transform hold = _stanceRoot.Find(WeaponGripRig.HoldNotReadyName)
		                 ?? _stanceRoot.Find(WeaponGripRig.LowReadyName)
		                 ?? _stanceRoot.Find(WeaponGripRig.NotReadyName);
		if (patrol == null || hold == null)
			return;
		if (patrol.localPosition != Vector3.zero || patrol.localRotation != Quaternion.identity)
			return;
		patrol.localPosition = hold.localPosition;
		patrol.localRotation = hold.localRotation;
	}

	private static void SeedHipFireWalkSlotFromHipFire(Transform _stanceRoot)
	{
		if (_stanceRoot == null)
			return;
		Transform walk = _stanceRoot.Find(WeaponGripRig.HipFireWalkName);
		Transform hip = _stanceRoot.Find(WeaponGripRig.HipFireName);
		if (walk == null || hip == null)
			return;
		if (walk.localPosition != Vector3.zero || walk.localRotation != Quaternion.identity)
			return;
		walk.localPosition = hip.localPosition;
		walk.localRotation = hip.localRotation;
	}

	private static void SeedHipFireCrouchWalkSlotFromHipFire(Transform _stanceRoot)
	{
		if (_stanceRoot == null)
			return;
		Transform walk = _stanceRoot.Find(WeaponGripRig.HipFireCrouchWalkName);
		Transform hip = _stanceRoot.Find(WeaponGripRig.HipFireName);
		if (walk == null || hip == null)
			return;
		if (walk.localPosition != Vector3.zero || walk.localRotation != Quaternion.identity)
			return;
		walk.localPosition = hip.localPosition;
		walk.localRotation = hip.localRotation;
	}

	private static Transform EnsurePoseSlot(Transform _stanceRoot, WeaponPoseState _pose)
	{
		if (_stanceRoot == null)
			return null;

		string primary = GetPoseSlotName(_pose);
		Transform slot = _stanceRoot.Find(primary);
		string legacy = GetLegacyPoseSlotName(_pose);
		if (slot == null && legacy != null)
			slot = _stanceRoot.Find(legacy);
		if (slot == null)
			slot = EnsureNamedChild(_stanceRoot, primary);
		return slot;
	}

	private static string GetPoseSlotName(WeaponPoseState _pose) => _pose switch
	{
		WeaponPoseState.NotReady => WeaponGripRig.HoldNotReadyName,
		WeaponPoseState.NotReadyPatrol => WeaponGripRig.HoldNotReadyPatrolName,
		WeaponPoseState.HipFire => WeaponGripRig.HipFireName,
		WeaponPoseState.HipFireWalk => WeaponGripRig.HipFireWalkName,
		WeaponPoseState.HipFireCrouchWalk => WeaponGripRig.HipFireCrouchWalkName,
		WeaponPoseState.PointAim => WeaponGripRig.PointAimName,
		WeaponPoseState.Aiming => WeaponGripRig.AimingName,
		WeaponPoseState.HighReady => WeaponGripRig.HighReadyName,
		_ => WeaponGripRig.LowReadyName,
	};

	private static string GetLegacyPoseSlotName(WeaponPoseState _pose) => _pose switch
	{
		WeaponPoseState.LowReady => WeaponGripRig.NotReadyName,
		WeaponPoseState.PointAim => WeaponGripRig.ReadyName,
		_ => null,
	};

	private static Transform ResolvePoseSlotUnderRoot(
		Transform _rightRoot,
		string _stanceName,
		WeaponPoseState _pose)
	{
		if (_rightRoot == null)
			return null;
		Transform stance = EnsureNamedChild(_rightRoot, _stanceName);
		return EnsurePoseSlot(stance, _pose);
	}

	private static Transform FindPoseSlot(Transform _stanceRoot, WeaponPoseState _pose)
	{
		if (_stanceRoot == null)
			return null;
		string primary = GetPoseSlotName(_pose);
		Transform slot = _stanceRoot.Find(primary);
		string legacy = GetLegacyPoseSlotName(_pose);
		if (slot == null && legacy != null)
			slot = _stanceRoot.Find(legacy);
		return slot;
	}

	private TunerWeaponPoseBuffer GetActivePoseBuffer() => m_ActiveTarget switch
	{
		TuningTarget.NotReady => TunerWeaponPoseBuffer.HoldNotReady,
		TuningTarget.NotReadyPatrol => TunerWeaponPoseBuffer.NotReadyPatrol,
		TuningTarget.LowReady => TunerWeaponPoseBuffer.LowReady,
		TuningTarget.HipFire => TunerWeaponPoseBuffer.HipFire,
		TuningTarget.HipFireWalk => TunerWeaponPoseBuffer.HipFireWalk,
		TuningTarget.HipFireCrouchWalk => TunerWeaponPoseBuffer.HipFireCrouchWalk,
		TuningTarget.PointAim => TunerWeaponPoseBuffer.PointAim,
		TuningTarget.Aiming => TunerWeaponPoseBuffer.Aiming,
		TuningTarget.HighReady => TunerWeaponPoseBuffer.HighReady,
		_ => TunerWeaponPoseBuffer.LowReady,
	};

	private Vector3 GetBufferPos(TunerWeaponPoseBuffer _buffer)
	{
		GetBufferRefs(_buffer, out Vector3 pos, out _);
		return pos;
	}

	private Vector3 GetBufferEuler(TunerWeaponPoseBuffer _buffer)
	{
		GetBufferRefs(_buffer, out _, out Vector3 euler);
		return euler;
	}

	private void SetBufferPos(TunerWeaponPoseBuffer _buffer, Vector3 _value)
	{
		SetBufferRefs(_buffer, _value, GetBufferEuler(_buffer));
	}

	private void SetBufferEuler(TunerWeaponPoseBuffer _buffer, Vector3 _value)
	{
		SetBufferRefs(_buffer, GetBufferPos(_buffer), _value);
	}

	private void GetBufferRefs(TunerWeaponPoseBuffer _buffer, out Vector3 _pos, out Vector3 _euler)
	{
		if (_buffer == TunerWeaponPoseBuffer.HighReady)
		{
			switch (m_ActivePosture)
			{
				case TuningPosture.Crouch:
					_pos = m_CrouchHighReadyPos;
					_euler = m_CrouchHighReadyEuler;
					return;
				case TuningPosture.Vehicle:
					_pos = m_VehicleHighReadyPos;
					_euler = m_VehicleHighReadyEuler;
					return;
				default:
					_pos = m_StandingHighReadyPos;
					_euler = m_StandingHighReadyEuler;
					return;
			}
		}

		if (_buffer == TunerWeaponPoseBuffer.NotReadyPatrol)
		{
			switch (m_ActivePosture)
			{
				case TuningPosture.Crouch:
					_pos = m_CrouchHoldNotReadyPatrolPos;
					_euler = m_CrouchHoldNotReadyPatrolEuler;
					return;
				case TuningPosture.Vehicle:
					_pos = m_VehicleHoldNotReadyPatrolPos;
					_euler = m_VehicleHoldNotReadyPatrolEuler;
					return;
				default:
					_pos = m_StandingHoldNotReadyPatrolPos;
					_euler = m_StandingHoldNotReadyPatrolEuler;
					return;
			}
		}

		if (_buffer == TunerWeaponPoseBuffer.HipFireWalk)
		{
			switch (m_ActivePosture)
			{
				case TuningPosture.Crouch:
					_pos = m_CrouchHipFireWalkPos;
					_euler = m_CrouchHipFireWalkEuler;
					return;
				case TuningPosture.Vehicle:
					_pos = m_VehicleHipFireWalkPos;
					_euler = m_VehicleHipFireWalkEuler;
					return;
				default:
					_pos = m_StandingHipFireWalkPos;
					_euler = m_StandingHipFireWalkEuler;
					return;
			}
		}

		if (_buffer == TunerWeaponPoseBuffer.HipFireCrouchWalk)
		{
			switch (m_ActivePosture)
			{
				case TuningPosture.Crouch:
					_pos = m_CrouchHipFireCrouchWalkPos;
					_euler = m_CrouchHipFireCrouchWalkEuler;
					return;
				case TuningPosture.Vehicle:
					_pos = m_VehicleHipFireCrouchWalkPos;
					_euler = m_VehicleHipFireCrouchWalkEuler;
					return;
				default:
					_pos = m_StandingHipFireCrouchWalkPos;
					_euler = m_StandingHipFireCrouchWalkEuler;
					return;
			}
		}
		switch (m_ActivePosture)
		{
			case TuningPosture.Crouch:
				GetPostureBufferRefs(
					_buffer,
					ref m_CrouchHoldNotReadyPos, ref m_CrouchHoldNotReadyEuler,
					ref m_CrouchNotReadyPos, ref m_CrouchNotReadyEuler,
					ref m_CrouchHipFirePos, ref m_CrouchHipFireEuler,
					ref m_CrouchReadyPos, ref m_CrouchReadyEuler,
					ref m_CrouchAimingPos, ref m_CrouchAimingEuler,
					out _pos, out _euler);
				break;
			case TuningPosture.Vehicle:
				GetPostureBufferRefs(
					_buffer,
					ref m_VehicleHoldNotReadyPos, ref m_VehicleHoldNotReadyEuler,
					ref m_VehicleNotReadyPos, ref m_VehicleNotReadyEuler,
					ref m_VehicleHipFirePos, ref m_VehicleHipFireEuler,
					ref m_VehicleReadyPos, ref m_VehicleReadyEuler,
					ref m_VehicleAimingPos, ref m_VehicleAimingEuler,
					out _pos, out _euler);
				break;
			default:
				GetPostureBufferRefs(
					_buffer,
					ref m_StandingHoldNotReadyPos, ref m_StandingHoldNotReadyEuler,
					ref m_StandingNotReadyPos, ref m_StandingNotReadyEuler,
					ref m_StandingHipFirePos, ref m_StandingHipFireEuler,
					ref m_StandingReadyPos, ref m_StandingReadyEuler,
					ref m_StandingAimingPos, ref m_StandingAimingEuler,
					out _pos, out _euler);
				break;
		}
	}

	private void SetBufferRefs(TunerWeaponPoseBuffer _buffer, Vector3 _pos, Vector3 _euler)
	{
		if (_buffer == TunerWeaponPoseBuffer.HighReady)
		{
			switch (m_ActivePosture)
			{
				case TuningPosture.Crouch:
					m_CrouchHighReadyPos = _pos;
					m_CrouchHighReadyEuler = _euler;
					return;
				case TuningPosture.Vehicle:
					m_VehicleHighReadyPos = _pos;
					m_VehicleHighReadyEuler = _euler;
					return;
				default:
					m_StandingHighReadyPos = _pos;
					m_StandingHighReadyEuler = _euler;
					return;
			}
		}

		if (_buffer == TunerWeaponPoseBuffer.NotReadyPatrol)
		{
			switch (m_ActivePosture)
			{
				case TuningPosture.Crouch:
					m_CrouchHoldNotReadyPatrolPos = _pos;
					m_CrouchHoldNotReadyPatrolEuler = _euler;
					return;
				case TuningPosture.Vehicle:
					m_VehicleHoldNotReadyPatrolPos = _pos;
					m_VehicleHoldNotReadyPatrolEuler = _euler;
					return;
				default:
					m_StandingHoldNotReadyPatrolPos = _pos;
					m_StandingHoldNotReadyPatrolEuler = _euler;
					return;
			}
		}

		if (_buffer == TunerWeaponPoseBuffer.HipFireWalk)
		{
			switch (m_ActivePosture)
			{
				case TuningPosture.Crouch:
					m_CrouchHipFireWalkPos = _pos;
					m_CrouchHipFireWalkEuler = _euler;
					return;
				case TuningPosture.Vehicle:
					m_VehicleHipFireWalkPos = _pos;
					m_VehicleHipFireWalkEuler = _euler;
					return;
				default:
					m_StandingHipFireWalkPos = _pos;
					m_StandingHipFireWalkEuler = _euler;
					return;
			}
		}

		if (_buffer == TunerWeaponPoseBuffer.HipFireCrouchWalk)
		{
			switch (m_ActivePosture)
			{
				case TuningPosture.Crouch:
					m_CrouchHipFireCrouchWalkPos = _pos;
					m_CrouchHipFireCrouchWalkEuler = _euler;
					return;
				case TuningPosture.Vehicle:
					m_VehicleHipFireCrouchWalkPos = _pos;
					m_VehicleHipFireCrouchWalkEuler = _euler;
					return;
				default:
					m_StandingHipFireCrouchWalkPos = _pos;
					m_StandingHipFireCrouchWalkEuler = _euler;
					return;
			}
		}
		switch (m_ActivePosture)
		{
			case TuningPosture.Crouch:
				SetPostureBufferRefs(
					_buffer,
					ref m_CrouchHoldNotReadyPos, ref m_CrouchHoldNotReadyEuler,
					ref m_CrouchNotReadyPos, ref m_CrouchNotReadyEuler,
					ref m_CrouchHipFirePos, ref m_CrouchHipFireEuler,
					ref m_CrouchReadyPos, ref m_CrouchReadyEuler,
					ref m_CrouchAimingPos, ref m_CrouchAimingEuler,
					_pos, _euler);
				break;
			case TuningPosture.Vehicle:
				SetPostureBufferRefs(
					_buffer,
					ref m_VehicleHoldNotReadyPos, ref m_VehicleHoldNotReadyEuler,
					ref m_VehicleNotReadyPos, ref m_VehicleNotReadyEuler,
					ref m_VehicleHipFirePos, ref m_VehicleHipFireEuler,
					ref m_VehicleReadyPos, ref m_VehicleReadyEuler,
					ref m_VehicleAimingPos, ref m_VehicleAimingEuler,
					_pos, _euler);
				break;
			default:
				SetPostureBufferRefs(
					_buffer,
					ref m_StandingHoldNotReadyPos, ref m_StandingHoldNotReadyEuler,
					ref m_StandingNotReadyPos, ref m_StandingNotReadyEuler,
					ref m_StandingHipFirePos, ref m_StandingHipFireEuler,
					ref m_StandingReadyPos, ref m_StandingReadyEuler,
					ref m_StandingAimingPos, ref m_StandingAimingEuler,
					_pos, _euler);
				break;
		}
	}

	private static void GetPostureBufferRefs(
		TunerWeaponPoseBuffer _buffer,
		ref Vector3 _holdPos, ref Vector3 _holdEu,
		ref Vector3 _lowPos, ref Vector3 _lowEu,
		ref Vector3 _hipPos, ref Vector3 _hipEu,
		ref Vector3 _pointPos, ref Vector3 _pointEu,
		ref Vector3 _aimPos, ref Vector3 _aimEu,
		out Vector3 _pos,
		out Vector3 _euler)
	{
		switch (_buffer)
		{
			case TunerWeaponPoseBuffer.HoldNotReady:
				_pos = _holdPos; _euler = _holdEu;
				break;
			case TunerWeaponPoseBuffer.HipFire:
				_pos = _hipPos; _euler = _hipEu;
				break;
			case TunerWeaponPoseBuffer.PointAim:
				_pos = _pointPos; _euler = _pointEu;
				break;
			case TunerWeaponPoseBuffer.Aiming:
				_pos = _aimPos; _euler = _aimEu;
				break;
			default:
				_pos = _lowPos; _euler = _lowEu;
				break;
		}
	}

	private static void SetPostureBufferRefs(
		TunerWeaponPoseBuffer _buffer,
		ref Vector3 _holdPos, ref Vector3 _holdEu,
		ref Vector3 _lowPos, ref Vector3 _lowEu,
		ref Vector3 _hipPos, ref Vector3 _hipEu,
		ref Vector3 _pointPos, ref Vector3 _pointEu,
		ref Vector3 _aimPos, ref Vector3 _aimEu,
		Vector3 _pos,
		Vector3 _euler)
	{
		switch (_buffer)
		{
			case TunerWeaponPoseBuffer.HoldNotReady:
				_holdPos = _pos; _holdEu = _euler;
				break;
			case TunerWeaponPoseBuffer.HipFire:
				_hipPos = _pos; _hipEu = _euler;
				break;
			case TunerWeaponPoseBuffer.PointAim:
				_pointPos = _pos; _pointEu = _euler;
				break;
			case TunerWeaponPoseBuffer.Aiming:
				_aimPos = _pos; _aimEu = _euler;
				break;
			default:
				_lowPos = _pos; _lowEu = _euler;
				break;
		}
	}

	private void SeedTunerOnlyPoseBuffers(TuningPosture _posture)
	{
		TuningPosture saved = m_ActivePosture;
		m_ActivePosture = _posture;

		if (GetBufferPos(TunerWeaponPoseBuffer.HoldNotReady) == Vector3.zero
		    && GetBufferEuler(TunerWeaponPoseBuffer.HoldNotReady) == Vector3.zero)
		{
			SetBufferPos(TunerWeaponPoseBuffer.HoldNotReady, GetBufferPos(TunerWeaponPoseBuffer.LowReady));
			SetBufferEuler(TunerWeaponPoseBuffer.HoldNotReady, GetBufferEuler(TunerWeaponPoseBuffer.LowReady));
		}

		if (GetBufferPos(TunerWeaponPoseBuffer.HighReady) == Vector3.zero
		    && GetBufferEuler(TunerWeaponPoseBuffer.HighReady) == Vector3.zero)
		{
			SetBufferPos(TunerWeaponPoseBuffer.HighReady, GetBufferPos(TunerWeaponPoseBuffer.Aiming));
			SetBufferEuler(TunerWeaponPoseBuffer.HighReady, GetBufferEuler(TunerWeaponPoseBuffer.Aiming));
		}

		if (GetBufferPos(TunerWeaponPoseBuffer.NotReadyPatrol) == Vector3.zero
		    && GetBufferEuler(TunerWeaponPoseBuffer.NotReadyPatrol) == Vector3.zero)
		{
			SetBufferPos(TunerWeaponPoseBuffer.NotReadyPatrol, GetBufferPos(TunerWeaponPoseBuffer.HoldNotReady));
			SetBufferEuler(TunerWeaponPoseBuffer.NotReadyPatrol, GetBufferEuler(TunerWeaponPoseBuffer.HoldNotReady));
		}

		if (GetBufferPos(TunerWeaponPoseBuffer.HipFireWalk) == Vector3.zero
		    && GetBufferEuler(TunerWeaponPoseBuffer.HipFireWalk) == Vector3.zero)
		{
			SetBufferPos(TunerWeaponPoseBuffer.HipFireWalk, GetBufferPos(TunerWeaponPoseBuffer.HipFire));
			SetBufferEuler(TunerWeaponPoseBuffer.HipFireWalk, GetBufferEuler(TunerWeaponPoseBuffer.HipFire));
		}

		if (GetBufferPos(TunerWeaponPoseBuffer.HipFireCrouchWalk) == Vector3.zero
		    && GetBufferEuler(TunerWeaponPoseBuffer.HipFireCrouchWalk) == Vector3.zero)
		{
			SetBufferPos(TunerWeaponPoseBuffer.HipFireCrouchWalk, GetBufferPos(TunerWeaponPoseBuffer.HipFire));
			SetBufferEuler(TunerWeaponPoseBuffer.HipFireCrouchWalk, GetBufferEuler(TunerWeaponPoseBuffer.HipFire));
		}

		m_ActivePosture = saved;
	}

	private void WriteLiveWeaponToCurrentPostureBuffers()
	{
		Transform weaponRoot = GetActiveWeaponRoot();
		if (weaponRoot == null)
			return;

		Vector3 pos = weaponRoot.localPosition;
		Vector3 eu = weaponRoot.localEulerAngles;
		SetBufferPos(TunerWeaponPoseBuffer.HoldNotReady, pos);
		SetBufferEuler(TunerWeaponPoseBuffer.HoldNotReady, eu);
		SetBufferPos(TunerWeaponPoseBuffer.LowReady, pos);
		SetBufferEuler(TunerWeaponPoseBuffer.LowReady, eu);
		SetBufferPos(TunerWeaponPoseBuffer.HipFire, pos);
		SetBufferEuler(TunerWeaponPoseBuffer.HipFire, eu);
		SetBufferPos(TunerWeaponPoseBuffer.HipFireWalk, pos);
		SetBufferEuler(TunerWeaponPoseBuffer.HipFireWalk, eu);
		SetBufferPos(TunerWeaponPoseBuffer.HipFireCrouchWalk, pos);
		SetBufferEuler(TunerWeaponPoseBuffer.HipFireCrouchWalk, eu);
		SetBufferPos(TunerWeaponPoseBuffer.PointAim, pos);
		SetBufferEuler(TunerWeaponPoseBuffer.PointAim, eu);
		SetBufferPos(TunerWeaponPoseBuffer.Aiming, pos);
		SetBufferEuler(TunerWeaponPoseBuffer.Aiming, eu);
		SetBufferPos(TunerWeaponPoseBuffer.HighReady, pos);
		SetBufferEuler(TunerWeaponPoseBuffer.HighReady, eu);
		SetBufferPos(TunerWeaponPoseBuffer.NotReadyPatrol, pos);
		SetBufferEuler(TunerWeaponPoseBuffer.NotReadyPatrol, eu);
	}

	private void GetAllBufferPoses(
		out Vector3 _holdPos, out Vector3 _holdEu,
		out Vector3 _lowPos, out Vector3 _lowEu,
		out Vector3 _hipPos, out Vector3 _hipEu,
		out Vector3 _pointPos, out Vector3 _pointEu,
		out Vector3 _aimPos, out Vector3 _aimEu)
	{
		_holdPos = GetBufferPos(TunerWeaponPoseBuffer.HoldNotReady);
		_holdEu = GetBufferEuler(TunerWeaponPoseBuffer.HoldNotReady);
		_lowPos = GetBufferPos(TunerWeaponPoseBuffer.LowReady);
		_lowEu = GetBufferEuler(TunerWeaponPoseBuffer.LowReady);
		_hipPos = GetBufferPos(TunerWeaponPoseBuffer.HipFire);
		_hipEu = GetBufferEuler(TunerWeaponPoseBuffer.HipFire);
		_pointPos = GetBufferPos(TunerWeaponPoseBuffer.PointAim);
		_pointEu = GetBufferEuler(TunerWeaponPoseBuffer.PointAim);
		_aimPos = GetBufferPos(TunerWeaponPoseBuffer.Aiming);
		_aimEu = GetBufferEuler(TunerWeaponPoseBuffer.Aiming);
	}

	private void SetAllBufferPoses(
		Vector3 _holdPos, Vector3 _holdEu,
		Vector3 _lowPos, Vector3 _lowEu,
		Vector3 _hipPos, Vector3 _hipEu,
		Vector3 _pointPos, Vector3 _pointEu,
		Vector3 _aimPos, Vector3 _aimEu)
	{
		SetBufferPos(TunerWeaponPoseBuffer.HoldNotReady, _holdPos);
		SetBufferEuler(TunerWeaponPoseBuffer.HoldNotReady, _holdEu);
		SetBufferPos(TunerWeaponPoseBuffer.LowReady, _lowPos);
		SetBufferEuler(TunerWeaponPoseBuffer.LowReady, _lowEu);
		SetBufferPos(TunerWeaponPoseBuffer.HipFire, _hipPos);
		SetBufferEuler(TunerWeaponPoseBuffer.HipFire, _hipEu);
		SetBufferPos(TunerWeaponPoseBuffer.PointAim, _pointPos);
		SetBufferEuler(TunerWeaponPoseBuffer.PointAim, _pointEu);
		SetBufferPos(TunerWeaponPoseBuffer.Aiming, _aimPos);
		SetBufferEuler(TunerWeaponPoseBuffer.Aiming, _aimEu);
	}

	private void GetPostureAllPoseBuffers(
		TuningPosture _posture,
		out Vector3 _holdPos,
		out Vector3 _holdEu,
		out Vector3 _lowPos,
		out Vector3 _lowEu,
		out Vector3 _hipPos,
		out Vector3 _hipEu,
		out Vector3 _pointPos,
		out Vector3 _pointEu,
		out Vector3 _aimPos,
		out Vector3 _aimEu)
	{
		TuningPosture saved = m_ActivePosture;
		m_ActivePosture = _posture;
		GetAllBufferPoses(
			out _holdPos, out _holdEu,
			out _lowPos, out _lowEu,
			out _hipPos, out _hipEu,
			out _pointPos, out _pointEu,
			out _aimPos, out _aimEu);
		m_ActivePosture = saved;
	}

	private void SetPostureAllPoseBuffers(
		TuningPosture _posture,
		Vector3 _holdPos,
		Vector3 _holdEu,
		Vector3 _lowPos,
		Vector3 _lowEu,
		Vector3 _hipPos,
		Vector3 _hipEu,
		Vector3 _pointPos,
		Vector3 _pointEu,
		Vector3 _aimPos,
		Vector3 _aimEu)
	{
		TuningPosture saved = m_ActivePosture;
		m_ActivePosture = _posture;
		SetAllBufferPoses(_holdPos, _holdEu, _lowPos, _lowEu, _hipPos, _hipEu, _pointPos, _pointEu, _aimPos, _aimEu);
		m_ActivePosture = saved;
	}

	private Vector3 GetHighReadyBufferPos(TuningPosture _posture)
	{
		return _posture switch
		{
			TuningPosture.Crouch => m_CrouchHighReadyPos,
			TuningPosture.Vehicle => m_VehicleHighReadyPos,
			_ => m_StandingHighReadyPos,
		};
	}

	private Vector3 GetHighReadyBufferEuler(TuningPosture _posture)
	{
		return _posture switch
		{
			TuningPosture.Crouch => m_CrouchHighReadyEuler,
			TuningPosture.Vehicle => m_VehicleHighReadyEuler,
			_ => m_StandingHighReadyEuler,
		};
	}

	private void SetHighReadyBuffer(TuningPosture _posture, Vector3 _pos, Vector3 _euler)
	{
		switch (_posture)
		{
			case TuningPosture.Crouch:
				m_CrouchHighReadyPos = _pos;
				m_CrouchHighReadyEuler = _euler;
				break;
			case TuningPosture.Vehicle:
				m_VehicleHighReadyPos = _pos;
				m_VehicleHighReadyEuler = _euler;
				break;
			default:
				m_StandingHighReadyPos = _pos;
				m_StandingHighReadyEuler = _euler;
				break;
		}
	}

	private Vector3 GetNotReadyPatrolBufferPos(TuningPosture _posture)
	{
		return _posture switch
		{
			TuningPosture.Crouch => m_CrouchHoldNotReadyPatrolPos,
			TuningPosture.Vehicle => m_VehicleHoldNotReadyPatrolPos,
			_ => m_StandingHoldNotReadyPatrolPos,
		};
	}

	private Vector3 GetNotReadyPatrolBufferEuler(TuningPosture _posture)
	{
		return _posture switch
		{
			TuningPosture.Crouch => m_CrouchHoldNotReadyPatrolEuler,
			TuningPosture.Vehicle => m_VehicleHoldNotReadyPatrolEuler,
			_ => m_StandingHoldNotReadyPatrolEuler,
		};
	}

	private void SetNotReadyPatrolBuffer(TuningPosture _posture, Vector3 _pos, Vector3 _euler)
	{
		switch (_posture)
		{
			case TuningPosture.Crouch:
				m_CrouchHoldNotReadyPatrolPos = _pos;
				m_CrouchHoldNotReadyPatrolEuler = _euler;
				break;
			case TuningPosture.Vehicle:
				m_VehicleHoldNotReadyPatrolPos = _pos;
				m_VehicleHoldNotReadyPatrolEuler = _euler;
				break;
			default:
				m_StandingHoldNotReadyPatrolPos = _pos;
				m_StandingHoldNotReadyPatrolEuler = _euler;
				break;
		}
	}

	private Vector3 GetHipFireWalkBufferPos(TuningPosture _posture)
	{
		return _posture switch
		{
			TuningPosture.Crouch => m_CrouchHipFireWalkPos,
			TuningPosture.Vehicle => m_VehicleHipFireWalkPos,
			_ => m_StandingHipFireWalkPos,
		};
	}

	private Vector3 GetHipFireWalkBufferEuler(TuningPosture _posture)
	{
		return _posture switch
		{
			TuningPosture.Crouch => m_CrouchHipFireWalkEuler,
			TuningPosture.Vehicle => m_VehicleHipFireWalkEuler,
			_ => m_StandingHipFireWalkEuler,
		};
	}

	private void SetHipFireWalkBuffer(TuningPosture _posture, Vector3 _pos, Vector3 _euler)
	{
		switch (_posture)
		{
			case TuningPosture.Crouch:
				m_CrouchHipFireWalkPos = _pos;
				m_CrouchHipFireWalkEuler = _euler;
				break;
			case TuningPosture.Vehicle:
				m_VehicleHipFireWalkPos = _pos;
				m_VehicleHipFireWalkEuler = _euler;
				break;
			default:
				m_StandingHipFireWalkPos = _pos;
				m_StandingHipFireWalkEuler = _euler;
				break;
		}
	}

	private Vector3 GetHipFireCrouchWalkBufferPos(TuningPosture _posture)
	{
		return _posture switch
		{
			TuningPosture.Crouch => m_CrouchHipFireCrouchWalkPos,
			TuningPosture.Vehicle => m_VehicleHipFireCrouchWalkPos,
			_ => m_StandingHipFireCrouchWalkPos,
		};
	}

	private Vector3 GetHipFireCrouchWalkBufferEuler(TuningPosture _posture)
	{
		return _posture switch
		{
			TuningPosture.Crouch => m_CrouchHipFireCrouchWalkEuler,
			TuningPosture.Vehicle => m_VehicleHipFireCrouchWalkEuler,
			_ => m_StandingHipFireCrouchWalkEuler,
		};
	}

	private void SetHipFireCrouchWalkBuffer(TuningPosture _posture, Vector3 _pos, Vector3 _euler)
	{
		switch (_posture)
		{
			case TuningPosture.Crouch:
				m_CrouchHipFireCrouchWalkPos = _pos;
				m_CrouchHipFireCrouchWalkEuler = _euler;
				break;
			case TuningPosture.Vehicle:
				m_VehicleHipFireCrouchWalkPos = _pos;
				m_VehicleHipFireCrouchWalkEuler = _euler;
				break;
			default:
				m_StandingHipFireCrouchWalkPos = _pos;
				m_StandingHipFireCrouchWalkEuler = _euler;
				break;
		}
	}

	private bool TryFindBagLauncher(RocketLauncherType _type, out ItemDefinition _definition)
	{
		_definition = null;
		ResolveReferences();
		if (m_Inventory == null)
			return false;

		for (int i = 0; i < m_Inventory.BagCount; i++)
		{
			InventorySlotRuntimeData slot = m_Inventory.BagItems[i];
			if (slot.IsEmpty || slot.Definition == null || !slot.Definition.IsRocketLauncher)
				continue;
			if (slot.Definition.RocketLauncherType != _type)
				continue;
			_definition = slot.Definition;
			return true;
		}

		return false;
	}

	private bool HasLauncherInBag(ItemDefinition _definition)
	{
		if (_definition == null || m_Inventory == null)
			return false;

		for (int i = 0; i < m_Inventory.BagCount; i++)
		{
			InventorySlotRuntimeData slot = m_Inventory.BagItems[i];
			if (!slot.IsEmpty && slot.Definition == _definition)
				return true;
		}

		return false;
	}

	private bool TryAddLauncherToBag(ItemDefinition _definition, out string _message)
	{
		_message = null;
		ResolveReferences();
		if (_definition == null)
		{
			_message = "Нет ItemDefinition гранатомёта.";
			return false;
		}

		if (m_Inventory == null)
		{
			_message = "На юните нет CharacterInventory.";
			return false;
		}

		if (HasLauncherInBag(_definition))
			return true;

		InventorySlotRuntimeData slot = InventorySlotRuntimeData.FromDefinition(_definition);
		if (_definition.RocketLauncherType == RocketLauncherType.Rpg7)
		{
			UnitRpg7LauncherHandler rpg = GetComponent<UnitRpg7LauncherHandler>()
				?? GetComponentInParent<UnitRpg7LauncherHandler>();
			if (rpg != null)
			{
				InventorySlotRuntimeData rocket = default;
				if (_definition.RpgRocketItemDefinition != null)
					rocket = InventorySlotRuntimeData.FromDefinition(_definition.RpgRocketItemDefinition);
				rpg.MarkLoaded(ref slot, rocket);
			}
		}

		if (!m_Inventory.TryAdd(slot, true))
		{
			_message = "Не удалось положить гранатомёт в сумку.";
			return false;
		}

		return true;
	}

	private static ItemDefinition FindRocketLauncherDefinition(RocketLauncherType _type)
	{
		ItemDefinition[] loaded = Resources.FindObjectsOfTypeAll<ItemDefinition>();
		ItemDefinition fallback = null;
		for (int i = 0; i < loaded.Length; i++)
		{
			ItemDefinition def = loaded[i];
			if (def == null || def.RocketLauncherType != _type)
				continue;
			if (def.name.StartsWith("Item_Weapon_"))
				return def;
			if (fallback == null)
				fallback = def;
		}

#if UNITY_EDITOR
		string[] guids = UnityEditor.AssetDatabase.FindAssets("t:ItemDefinition");
		for (int i = 0; i < guids.Length; i++)
		{
			string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
			ItemDefinition def = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
			if (def != null && def.RocketLauncherType == _type)
				return def;
		}
#endif
		return fallback;
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
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponentInParent<UnitWeaponReadyHandsLayer>();
		if (m_AnimatorStance == null)
			m_AnimatorStance = GetComponent<UnitAnimatorStance>();
		if (m_AnimatorStance == null)
			m_AnimatorStance = GetComponentInParent<UnitAnimatorStance>();
		if (m_SeatPose == null)
			m_SeatPose = GetComponent<UnitVehicleSeatPoseController>();
		if (m_SeatPose == null)
			m_SeatPose = GetComponentInParent<UnitVehicleSeatPoseController>();
		if (m_UnitAnimator == null)
			m_UnitAnimator = GetComponentInChildren<Animator>();
		if (m_Inventory == null)
			m_Inventory = GetComponent<CharacterInventory>();
		if (m_Inventory == null)
			m_Inventory = GetComponentInParent<CharacterInventory>();
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponentInParent<UnitWeaponRuntime>();
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
		if (!IsTuningActive)
			return;

		EnsureGripTargetsExist();
		ItemDefinition def = ActiveTuningDefinition;
		if (def == m_LoadedTuningDefinition)
			return;
		LoadFromEquippedDefinition();
	}

	private void RefreshGripResolverTargets()
	{
		if (m_UnitEquipment == null)
			return;

		Transform weaponRoot = GetActiveWeaponRoot();
		if (weaponRoot != null)
		{
			WeaponGripRig grip = weaponRoot.GetComponentInChildren<WeaponGripRig>(true);
			grip?.BuildCache();
		}

		WeaponGripResolver resolver = m_UnitEquipment.GetComponent<WeaponGripResolver>();
		if (resolver == null)
			resolver = GetComponent<WeaponGripResolver>();
		resolver?.RefreshTargets(force: true);
	}

#if UNITY_EDITOR
	private static void CopyRightHandPoseTree(Transform _liveRightRoot, Transform _prefabRightRoot)
	{
		if (_liveRightRoot == null || _prefabRightRoot == null)
			return;

		string[] stances = { WeaponGripRig.StandingName, WeaponGripRig.CrouchName, WeaponGripRig.VehicleName };
		WeaponPoseState[] poses =
		{
			WeaponPoseState.NotReady,
			WeaponPoseState.NotReadyPatrol,
			WeaponPoseState.LowReady,
			WeaponPoseState.HighReady,
			WeaponPoseState.HipFire,
			WeaponPoseState.HipFireWalk,
			WeaponPoseState.HipFireCrouchWalk,
			WeaponPoseState.PointAim,
			WeaponPoseState.Aiming,
		};
		foreach (string stance in stances)
		{
			foreach (WeaponPoseState pose in poses)
			{
				Transform liveSlot = ResolvePoseSlotUnderRoot(_liveRightRoot, stance, pose);
				Transform prefabSlot = ResolvePoseSlotUnderRoot(_prefabRightRoot, stance, pose);
				if (liveSlot == null || prefabSlot == null)
					continue;
				prefabSlot.localPosition = liveSlot.localPosition;
				prefabSlot.localRotation = liveSlot.localRotation;
				prefabSlot.localScale = liveSlot.localScale;
			}
		}
	}

	private static Transform FindChildPath(Transform _root, string _a, string _b)
	{
		if (_root == null)
			return null;
		Transform a = _root.Find(_a);
		return a != null ? a.Find(_b) : null;
	}
#endif

	private static Transform EnsureNamedChild(Transform _parent, string _name)
	{
		Transform existing = _parent.Find(_name);
		if (existing != null)
			return existing;

		var go = new GameObject(_name);
		Transform t = go.transform;
		t.SetParent(_parent, false);
		t.localPosition = Vector3.zero;
		t.localRotation = Quaternion.identity;
		t.localScale = Vector3.one;
		return t;
	}

	private static Transform ResolveRightHandIkRoot(Transform _gripRoot, WeaponGripRig _grip)
	{
		if (_grip != null && _grip.RightHandIkRoot != null)
			return _grip.RightHandIkRoot;

		if (_gripRoot == null)
			return null;

		Transform found = FindNamedChild(_gripRoot, WeaponGripRig.RightHandIkRootName);
		if (found != null)
			return found;

		return FindNamedChild(_gripRoot, WeaponGripRig.RightHandRootName);
	}

	private static Transform ResolveWeaponLeftHandIk(Transform _gripRoot, WeaponGripRig _grip)
	{
		if (_grip != null && _grip.LeftHandIk != null)
			return _grip.LeftHandIk;

		if (_gripRoot == null)
			return null;

		Transform found = FindNamedChild(_gripRoot, WeaponGripRig.LeftHandIkName);
		if (found != null)
			return found;

		return FindNamedChild(_gripRoot, WeaponGripRig.LeftHandGripName);
	}

	private static Transform FindGripRigRoot(Transform _weaponRoot, WeaponGripRig _grip)
	{
		if (_weaponRoot != null)
		{
			Transform direct = _weaponRoot.Find(WeaponGripRig.GripRigChildName);
			if (direct != null)
				return direct;
			Transform nested = FindChildRecursive(_weaponRoot, WeaponGripRig.GripRigChildName);
			if (nested != null)
				return nested;
		}

		if (_grip != null && _grip.transform.name == WeaponGripRig.GripRigChildName)
			return _grip.transform;
		if (_grip != null)
		{
			Transform under = _grip.transform.Find(WeaponGripRig.GripRigChildName);
			if (under != null)
				return under;
		}

		return null;
	}

	private static Transform FindNamedChild(Transform _parent, string _name)
	{
		if (_parent == null)
			return null;
		Transform direct = _parent.Find(_name);
		if (direct != null)
			return direct;
		return FindChildRecursive(_parent, _name);
	}

	private static Transform FindChildRecursive(Transform _root, string _name)
	{
		if (_root == null)
			return null;
		if (_root.name == _name)
			return _root;
		for (int i = 0; i < _root.childCount; i++)
		{
			Transform found = FindChildRecursive(_root.GetChild(i), _name);
			if (found != null)
				return found;
		}
		return null;
	}
	#endregion
}
