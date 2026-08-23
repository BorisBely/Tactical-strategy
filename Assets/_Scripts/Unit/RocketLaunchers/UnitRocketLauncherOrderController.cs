using System.Collections;
using UnityEngine;

/// <summary>
/// Приказ гранатомёта (H): force ready, скрыть основное оружие, aim → fire/reload, восстановить состояние.
/// Делегирует тип-специфичную логику в <see cref="UnitRpg7LauncherHandler"/> / <see cref="UnitDisposableLauncherHandler"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(64)]
public sealed class UnitRocketLauncherOrderController : MonoBehaviour
{
	#region Constants
	public const string ParamRocketLauncherAim = "RocketLauncherAim";
	public const string ParamRocketLauncherFire = "RocketLauncherFire";
	public const string ParamRocketLauncherReload = "RocketLauncherReload";
	public const string ParamRocketLauncherKind = "RocketLauncherKind";

	private static readonly int s_Aim = Animator.StringToHash(ParamRocketLauncherAim);
	private static readonly int s_Fire = Animator.StringToHash(ParamRocketLauncherFire);
	private static readonly int s_Reload = Animator.StringToHash(ParamRocketLauncherReload);
	private static readonly int s_Kind = Animator.StringToHash(ParamRocketLauncherKind);

	private const string AimLayerName = "Aim_Point_U90-D90";
	private const int c_KindRpg = 0;
	private const int c_KindDisposable = 1;
	#endregion

	#region Serialized Fields
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private CharacterInventory m_Inventory;
	[SerializeField] private UnitBusyState m_BusyState;
	[SerializeField] private Animator m_Animator;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyLayer;
	[SerializeField] private TargetSelector m_TargetSelector;
	[SerializeField] private RocketLauncherData m_Data;
	[SerializeField] private UnitRpg7LauncherHandler m_RpgHandler;
	[SerializeField] private UnitDisposableLauncherHandler m_DisposableHandler;
	[SerializeField] private UnitCombatStats m_CombatStats;
	[SerializeField] private UnitIndividualTraits m_IndividualTraits;
	[SerializeField] private UnitCombatCondition m_CombatCondition;
	[SerializeField] private UnitStanceCombatModifiers m_StanceCombatModifiers;
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;
	[SerializeField] private UnitFallenDragController m_FallenDragController;
	[SerializeField] private Transform m_RightHandAnchor;
	[SerializeField] private Transform m_LeftHandAnchor;
	[SerializeField] private UnitEquippedWeaponPoseRuntimeTuner m_RuntimeTuner;
	[SerializeField] private UnitEquippedWeaponPose m_EquippedWeaponPose;
	[SerializeField] private UnitAnimatorStance m_Stance;
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitWeaponFireDisciplineController m_FireDisciplineController;
	[SerializeField] private UnitWeaponAiming m_WeaponAiming;
	[SerializeField] private UnitAnimatorWeaponMode m_AnimatorWeaponMode;

	[Header("Aim Trajectory Gizmo")]
	[SerializeField] private bool m_DrawAimTrajectoryGizmo = true;
	[SerializeField] private Color m_AimTrajectoryColor = new Color(1f, 0.55f, 0.1f, 0.95f);
	[SerializeField] private Color m_AimImpactColor = new Color(1f, 0.2f, 0.15f, 0.9f);
	[SerializeField] private Color m_AimTargetColor = new Color(0.25f, 0.95f, 0.35f, 0.9f);
	#endregion

	#region Private Fields
	private RocketLauncherOrderPhase m_Phase;
	private InventorySlotRuntimeData m_ActiveSlot;
	private int m_ActiveBagIndex = -1;
	private ItemDefinition m_ActiveDefinition;
	private ItemInstanceState m_ActiveInstanceState;
	private GameObject m_HandLauncherInstance;
	private GameObject m_HandRocketInstance;
	private Transform m_MuzzleTransform;
	private Transform m_BackblastTransform;
	private Transform m_RightHandIkTargetTransform;
	private Transform m_RightHandIkTargetNotReadyTransform;
	private Transform m_LeftHandIkTargetTransform;
	private Transform m_LeftHandIkTargetNotReadyTransform;
	private WeaponGripRig m_LauncherGripRig;
	private Transform m_GripLeftHand;
	private Transform m_GripRightReady;
	private Transform m_GripRightNotReady;
	private bool m_FiredProjectile;
	private bool m_DiscardedLauncher;
	private bool m_InsertedRocket;
	private bool m_UiRocketInstallActive;
	private bool m_UiRocketMirrorAnimationOnly;
	private InventorySlotRuntimeData m_PendingUiRocket;
	private Coroutine m_OrderCoroutine;
	private int m_AimLayerIndex = -1;
	private bool m_EnteredRocketLauncherFireReady;
	private bool m_HoldForTuning;
	private readonly Vector3[] m_AimGizmoBuffer = new Vector3[64];
	#endregion

	#region Events
	public event System.Action OrderStateChanged;
	public event System.Action UiRocketModificationCompleted;
	#endregion

	#region Public Properties
	public RocketLauncherOrderPhase CurrentPhase => m_Phase;
	public bool IsBusy => m_Phase != RocketLauncherOrderPhase.None;
	public RocketLauncherData Data => m_Data;
	public ItemDefinition ActiveLauncherDefinition => m_ActiveDefinition;
	public int ActiveBagIndex => m_ActiveBagIndex;
	public Transform HandLauncherRoot => m_HandLauncherInstance != null ? m_HandLauncherInstance.transform : null;
	public Transform RightHandIkTargetTransform =>
		m_RightHandIkTargetTransform != null ? m_RightHandIkTargetTransform : m_GripRightReady;
	public Transform RightHandIkTargetNotReadyTransform =>
		m_RightHandIkTargetNotReadyTransform != null ? m_RightHandIkTargetNotReadyTransform : m_GripRightNotReady;
	public Transform LeftHandIkTargetTransform =>
		m_LeftHandIkTargetTransform != null ? m_LeftHandIkTargetTransform : m_GripLeftHand;
	public Transform LeftHandIkTargetNotReadyTransform =>
		m_LeftHandIkTargetNotReadyTransform != null ? m_LeftHandIkTargetNotReadyTransform : m_GripLeftHand;

	/// <summary>GripRig left hand on the held launcher tube.</summary>
	public Transform GripLeftHandTarget => m_GripLeftHand;
	public WeaponGripRig LauncherGripRig => m_LauncherGripRig;

	/// <summary>
	/// Держать локальную позу трубы в правой руке: aim, fire до выброса, reload, тюнер.
	/// </summary>
	public bool ShouldDriveWeaponPose =>
		IsBusy && HandLauncherRoot != null &&
		((m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive) ||
		 m_Phase == RocketLauncherOrderPhase.Aiming ||
		 (m_Phase == RocketLauncherOrderPhase.Firing && !m_FiredProjectile) ||
		 m_Phase == RocketLauncherOrderPhase.Reloading);

	/// <summary>Правая рука: поза+IK на aim, fire до выброса и весь reload (труба в правой).</summary>
	public bool ShouldUseRightHandIk => ShouldDriveWeaponPose;

	/// <summary>Левая рука: поза+IK на aim и fire до выброса; на reload — анимация вставки ракеты.</summary>
	public bool ShouldUseLeftHandIk =>
		IsBusy && HandLauncherRoot != null &&
		((m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive) ||
		 m_Phase == RocketLauncherOrderPhase.Aiming ||
		 (m_Phase == RocketLauncherOrderPhase.Firing && !m_FiredProjectile));

	/// <summary>Совместимость: общий контекст позы/корня оружия для IK resolve.</summary>
	public bool ShouldUsePoseAndIk => ShouldDriveWeaponPose;

	/// <summary>
	/// Клипы aim/fire/reload гранатомёта на Aim_Point_U90-D90 — слой должен быть видимым на всём приказе.
	/// </summary>
	public bool ShouldHoldAimLayerVisible => IsBusy && HandLauncherRoot != null;

	/// <summary>Тюнер IK: держать RocketLauncherAim и позу прицеливания для всех режимов настройки.</summary>
	public bool ShouldMaintainTuningAimAnimation =>
		IsBusy &&
		HandLauncherRoot != null &&
		m_RuntimeTuner != null &&
		m_RuntimeTuner.IsTuningActive;

	/// <summary>
	/// True, если предмет сумки сейчас в руках как активный гранатомёт приказа (не показывать за спиной).
	/// </summary>
	public bool IsBagSlotHeldAsActiveLauncher(int _bagIndex, InventorySlotRuntimeData _slot)
	{
		if (!IsBusy || m_ActiveDefinition == null)
			return false;

		if (m_ActiveBagIndex >= 0)
			return _bagIndex == m_ActiveBagIndex;

		// После удаления из сумки (одноразовый) индекса нет — матчим по InstanceState.
		return m_ActiveInstanceState != null &&
		       _slot.InstanceState != null &&
		       ReferenceEquals(_slot.InstanceState, m_ActiveInstanceState);
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveReferences();
	}

	private void OnEnable()
	{
		ResolveAimLayerIndex();
	}

	private void OnDisable()
	{
		if (m_Phase != RocketLauncherOrderPhase.None)
			CancelOrder(true);
	}

	private void LateUpdate()
	{
		if (!ShouldMaintainTuningAimAnimation)
			return;

		MaintainTuningAimAnimation();
	}

	private void OnDrawGizmos()
	{
		if (!Application.isPlaying || !ShouldDrawAimTrajectoryGizmo())
			return;

		if (!TryBuildAimTrajectory(
			    out Vector3 origin,
			    out Vector3 aimDirection,
			    out Vector3 targetPoint,
			    out bool hasTarget,
			    out int pointCount,
			    out bool hitGeometry,
			    out Vector3 impactPoint))
			return;

		Color trajectoryColor = m_AimTrajectoryColor;
		Gizmos.color = trajectoryColor;
		for (int i = 1; i < pointCount; i++)
			Gizmos.DrawLine(m_AimGizmoBuffer[i - 1], m_AimGizmoBuffer[i]);

		for (int i = 0; i < pointCount; i += 4)
			Gizmos.DrawSphere(m_AimGizmoBuffer[i], 0.06f);

		Gizmos.color = m_AimImpactColor;
		Gizmos.DrawSphere(impactPoint, hitGeometry ? 0.18f : 0.12f);

		if (hasTarget)
		{
			Gizmos.color = m_AimTargetColor;
			Gizmos.DrawWireSphere(targetPoint, 0.22f);
			Gizmos.DrawLine(origin, origin + aimDirection * 1.5f);
		}
	}
	#endregion

	#region Public Methods
	public bool HasAnyRocketLauncher()
	{
		return TryFindBestLauncher(out _, out _);
	}

	public bool TryGetBestLauncherBagIndex(out int _bagIndex)
	{
		return TryFindBestLauncher(out _, out _bagIndex);
	}

	public bool ShouldShowReloadButtonLabel()
	{
		if (m_Inventory == null || m_RpgHandler == null)
			return false;

		for (int i = 0; i < m_Inventory.BagCount; i++)
		{
			InventorySlotRuntimeData slot = m_Inventory.BagItems[i];
			if (slot.IsEmpty || slot.Definition == null)
				continue;

			if (m_RpgHandler.ShouldReload(slot, m_Inventory))
				return true;
		}

		return false;
	}

	public bool TryStartOrder()
	{
		if (m_Phase != RocketLauncherOrderPhase.None)
			return false;

		if (m_BusyState != null && m_BusyState.IsBusy)
			return false;

		if (!TryFindBestLauncher(out InventorySlotRuntimeData slot, out int bagIndex))
			return false;

		return TryStartOrderInternal(slot, bagIndex);
	}

	public bool TryStartOrder(int _bagIndex)
	{
		if (m_Phase != RocketLauncherOrderPhase.None)
			return false;

		if (m_BusyState != null && m_BusyState.IsBusy)
			return false;

		if (m_Inventory == null || _bagIndex < 0 || _bagIndex >= m_Inventory.BagCount)
			return false;

		InventorySlotRuntimeData slot = m_Inventory.BagItems[_bagIndex];
		if (!IsUsableLauncher(slot))
			return false;

		return TryStartOrderInternal(slot, _bagIndex);
	}

	/// <summary>
	/// Тюнер: взять гранатомёт в руки и держать aim, без выстрела и без reload-ролика.
	/// RPG помечается заряженным, чтобы не уходить в ReloadOnly.
	/// </summary>
	public bool TryHoldForTuning(ItemDefinition _preferredLauncher = null)
	{
		ResolveReferences();

		if (IsBusy && HandLauncherRoot != null &&
		    (_preferredLauncher == null || m_ActiveDefinition == _preferredLauncher))
		{
			EnsureGripRigTargets();
			m_HoldForTuning = true;
			EnterRocketLauncherAimReady();
			SetAimParameter(true);
			OrderStateChanged?.Invoke();
			return true;
		}

		if (IsBusy)
			CancelOrder(true);

		if (m_BusyState != null &&
		    m_BusyState.IsBusy &&
		    !m_BusyState.HasReason(UnitBusyState.BusyReason.RocketLauncher))
			return false;

		if (!TryFindLauncherForTuning(_preferredLauncher, out InventorySlotRuntimeData slot, out int bagIndex))
			return false;

		EnsureLauncherLoadedForTuning(ref slot, bagIndex);
		m_HoldForTuning = true;
		return TryStartOrderInternal(slot, bagIndex, _holdForTuning: true);
	}

	/// <summary>
	/// UI: вставка снаряда в РПГ из инвентаря — анимация reload, предмет уже изъят из источника.
	/// </summary>
	public bool TryStartUiRocketInstall(int _launcherBagIndex, InventorySlotRuntimeData _rocket, bool _mirrorAnimationOnly = false)
	{
		if (m_Phase != RocketLauncherOrderPhase.None)
			return false;

		if (m_BusyState != null && m_BusyState.IsBusy)
			return false;

		if (_rocket.IsEmpty || _rocket.Definition == null || _launcherBagIndex < 0)
			return false;

		ResolveReferences();
		if (m_Inventory == null || !m_Inventory.TryGetInventorySlot(false, _launcherBagIndex, out InventorySlotRuntimeData launcherSlot))
			return false;

		if (launcherSlot.IsEmpty || launcherSlot.Definition == null ||
		    launcherSlot.Definition.RocketLauncherType != RocketLauncherType.Rpg7)
			return false;

		if (m_RpgHandler == null || !m_RpgHandler.CanAcceptRocketItem(launcherSlot.Definition, _rocket))
			return false;

		if (launcherSlot.InstanceState == null)
		{
			launcherSlot.InstanceState = ItemInstanceState.CreateForDefinition(launcherSlot.Definition);
			m_Inventory.TrySetBagItemAt(_launcherBagIndex, launcherSlot);
			m_Inventory.TryGetInventorySlot(false, _launcherBagIndex, out launcherSlot);
		}

		launcherSlot.InstanceState.EnsureRocketLauncherState(launcherSlot.Definition);
		if (!_mirrorAnimationOnly && launcherSlot.InstanceState.RocketLauncherState.IsLoaded)
		{
			if (launcherSlot.InstanceState.RocketLauncherState.TryEjectLoadedRocket(out InventorySlotRuntimeData previousRocket) &&
			    !previousRocket.IsEmpty)
			{
				m_Inventory.TryAdd(previousRocket);
			}
			else
			{
				launcherSlot.InstanceState.RocketLauncherState.ClearLoadedRocket();
			}

			m_Inventory.TrySetBagItemAt(_launcherBagIndex, launcherSlot);
			m_Inventory.TryGetInventorySlot(false, _launcherBagIndex, out launcherSlot);
		}

		m_ActiveSlot = launcherSlot;
		m_ActiveBagIndex = _launcherBagIndex;
		m_ActiveDefinition = launcherSlot.Definition;
		m_ActiveInstanceState = launcherSlot.InstanceState;
		m_FiredProjectile = false;
		m_DiscardedLauncher = false;
		m_InsertedRocket = false;
		m_UiRocketInstallActive = true;
		m_UiRocketMirrorAnimationOnly = _mirrorAnimationOnly;
		m_PendingUiRocket = _rocket;

		SuppressEquippedWeaponFire();
		HideMainWeapon();
		m_Phase = RocketLauncherOrderPhase.Reloading;
		if (m_BusyState != null)
			m_BusyState.SetReasonActive(UnitBusyState.BusyReason.RocketLauncher, true);

		SpawnHandLauncherVisual();
		SetAnimatorKind(RocketLauncherType.Rpg7);
		SetAimParameter(false);
		TriggerReload();

		m_OrderCoroutine = StartCoroutine(UiRocketInstallRoutine());
		OrderStateChanged?.Invoke();
		return true;
	}

	public void CancelOrder(bool _restoreImmediately)
	{
		if (m_OrderCoroutine != null)
		{
			StopCoroutine(m_OrderCoroutine);
			m_OrderCoroutine = null;
		}

		FinishOrder(_restoreImmediately);
	}

	public void EnsureHandIkTargetsExist()
	{
		EnsureGripRigTargets();
	}

	/// <summary>
	/// Wire IK to the authored GripRig on the held tube.
	/// Prefabs store LeftHandIK + RightHandIK. Creating a parallel LeftHandGrip / RightHand
	/// steals the rig: tuner saves LeftHandIK, next spawn seeds LeftHandGrip from legacy
	/// LeftHandIkTarget — switching RPG ↔ disposable then looks like the left hand jumped.
	/// </summary>
	public void EnsureGripRigTargets()
	{
		Transform root = HandLauncherRoot;
		if (root == null)
			return;

		WeaponGripRig grip = root.GetComponent<WeaponGripRig>()
		                     ?? root.GetComponentInChildren<WeaponGripRig>(true);
		if (grip == null)
			grip = root.gameObject.AddComponent<WeaponGripRig>();
		m_LauncherGripRig = grip;

		Transform gripRoot = root.Find(WeaponGripRig.GripRigChildName)
		                     ?? FindChildRecursive(root, WeaponGripRig.GripRigChildName);
		if (gripRoot == null)
		{
			var go = new GameObject(WeaponGripRig.GripRigChildName);
			gripRoot = go.transform;
			gripRoot.SetParent(root, false);
		}

		Transform rightMarker = grip.RightHandGrip != null
			? grip.RightHandGrip
			: EnsureNamedChild(gripRoot, WeaponGripRig.RightHandGripName);
		Transform leftIk = ResolveAuthoredLeftHandIk(grip, gripRoot);
		grip.SetGrips(rightMarker, leftIk);
		grip.SetLeftHandIk(leftIk);

		Transform rightRoot = grip.RightHandIkRoot != null
			? grip.RightHandIkRoot
			: (gripRoot.Find(WeaponGripRig.RightHandIkRootName)
			   ?? gripRoot.Find(WeaponGripRig.RightHandRootName));
		if (rightRoot == null)
			rightRoot = EnsureNamedChild(gripRoot, WeaponGripRig.RightHandIkRootName);

		grip.BuildCache();
		if (!grip.HasRightHandIkTargets)
		{
			Transform standing = EnsureNamedChild(rightRoot, WeaponGripRig.StandingName);
			Transform crouch = EnsureNamedChild(rightRoot, WeaponGripRig.CrouchName);
			Transform vehicle = EnsureNamedChild(rightRoot, WeaponGripRig.VehicleName);

			Transform sReady = EnsureNamedChild(standing, WeaponGripRig.ReadyName);
			Transform sNotReady = EnsureNamedChild(standing, WeaponGripRig.NotReadyName);
			Transform cReady = EnsureNamedChild(crouch, WeaponGripRig.ReadyName);
			Transform cNotReady = EnsureNamedChild(crouch, WeaponGripRig.NotReadyName);
			Transform vReady = EnsureNamedChild(vehicle, WeaponGripRig.ReadyName);
			Transform vNotReady = EnsureNamedChild(vehicle, WeaponGripRig.NotReadyName);
			grip.SetRightHandPoseTargets(sReady, sNotReady, cReady, cNotReady, vReady, vNotReady);
			SeedFromLegacyIfNeeded(root, sReady, sNotReady, cReady, cNotReady, vReady, vNotReady, leftIk, rightMarker);
		}
		else if (IsIdentityLocal(leftIk))
		{
			Transform authoredLeft = gripRoot.Find(WeaponGripRig.LeftHandIkName);
			Transform legacyLeft = FindChildRecursive(root, "LeftHandIkTarget");
			if (authoredLeft != null && authoredLeft != leftIk && !IsIdentityLocal(authoredLeft))
				CopyLocal(authoredLeft, leftIk);
			else if (legacyLeft != null)
				CopyLocal(legacyLeft, leftIk);
		}

		RefreshHandIkTargets();
	}

	public void RefreshHandIkTargets()
	{
		Transform root = HandLauncherRoot;
		if (root == null)
			return;

		if (m_LauncherGripRig == null)
			m_LauncherGripRig = root.GetComponent<WeaponGripRig>();

		if (m_LauncherGripRig != null &&
		    m_LauncherGripRig.TryGetRightHandTargets(WeaponStance.Standing, out Transform notReady, out Transform ready))
		{
			m_GripRightReady = ready;
			m_GripRightNotReady = notReady;
			m_RightHandIkTargetTransform = ready;
			m_RightHandIkTargetNotReadyTransform = notReady;
		}
		else
		{
			m_RightHandIkTargetTransform = FindChildRecursive(root, "RightHandIkTarget");
			m_RightHandIkTargetNotReadyTransform = FindChildRecursive(root, "RightHandIkTarget_NotReady");
			m_GripRightReady = m_RightHandIkTargetTransform;
			m_GripRightNotReady = m_RightHandIkTargetNotReadyTransform;
		}

		m_GripLeftHand = m_LauncherGripRig != null ? m_LauncherGripRig.LeftHandIk : null;
		if (m_GripLeftHand == null)
		{
			Transform gripRoot = root.Find(WeaponGripRig.GripRigChildName);
			if (gripRoot != null)
			{
				m_GripLeftHand = gripRoot.Find(WeaponGripRig.LeftHandIkName)
				                 ?? gripRoot.Find(WeaponGripRig.LeftHandGripName);
			}
		}

		if (m_GripLeftHand == null)
			m_GripLeftHand = FindChildRecursive(root, "LeftHandIkTarget");

		m_LeftHandIkTargetTransform = m_GripLeftHand;
		m_LeftHandIkTargetNotReadyTransform = m_GripLeftHand;
	}

	/// <summary>Blended right-hand world pose from GripRig stance targets.</summary>
	public bool TryGetGripRightHandWorldPose(WeaponStance _stance, float _readyBlend01, out Vector3 _pos, out Quaternion _rot)
	{
		_pos = Vector3.zero;
		_rot = Quaternion.identity;
		if (m_LauncherGripRig == null ||
		    !m_LauncherGripRig.TryGetRightHandTargets(_stance, out Transform notReady, out Transform ready))
			return false;

		float t = Mathf.Clamp01(_readyBlend01);
		_pos = Vector3.Lerp(notReady.position, ready.position, t);
		_rot = Quaternion.Slerp(notReady.rotation, ready.rotation, t);
		return true;
	}

	/// <summary>Exact GripRig pose slot (tuner). Falls back to LowReady↔PointAim blend if the slot is missing.</summary>
	public bool TryGetGripRightHandWorldPose(
		WeaponStance _stance,
		WeaponPoseState _pose,
		out Vector3 _pos,
		out Quaternion _rot)
	{
		_pos = Vector3.zero;
		_rot = Quaternion.identity;
		if (m_LauncherGripRig == null)
			RefreshHandIkTargets();
		if (m_LauncherGripRig == null)
			return false;

		Transform target = m_LauncherGripRig.GetRightHandTarget(_stance, _pose);
		if (target != null)
		{
			_pos = target.position;
			_rot = target.rotation;
			return true;
		}

		float blend = _pose == WeaponPoseState.NotReady ||
		              _pose == WeaponPoseState.NotReadyPatrol ||
		              _pose == WeaponPoseState.LowReady
			? 0f
			: 1f;
		return TryGetGripRightHandWorldPose(_stance, blend, out _pos, out _rot);
	}
	#endregion

	#region Animation Events
	public void AnimationEvent_RocketLauncherFire()
	{
		if (m_Phase != RocketLauncherOrderPhase.Firing)
			return;

		PlayFireAudio();
		PlayFireVfx();
		TrySpawnProjectile();

		if (m_ActiveDefinition != null &&
		    m_ActiveDefinition.RocketLauncherType == RocketLauncherType.Rpg7 &&
		    m_RpgHandler != null)
		{
			m_RpgHandler.MarkUnloaded(ref m_ActiveSlot);
			if (m_ActiveBagIndex >= 0 && m_Inventory != null)
				m_Inventory.TrySetBagItemAt(m_ActiveBagIndex, m_ActiveSlot);
			if (m_ActiveSlot.InstanceState != null)
				m_ActiveInstanceState = m_ActiveSlot.InstanceState;
			SyncHandLauncherRocketVisual();
		}
	}

	public void AnimationEvent_DisposableLauncherDiscard()
	{
		if (m_Phase != RocketLauncherOrderPhase.Firing)
			return;

		if (m_ActiveDefinition == null || m_ActiveDefinition.RocketLauncherType != RocketLauncherType.Disposable)
			return;

		if (m_DiscardedLauncher)
			return;

		m_DiscardedLauncher = true;
		if (m_DisposableHandler != null)
		{
			// Сбросить индекс ДО удаления из сумки, чтобы holster не исключил чужой слот.
			int bagIndex = m_ActiveBagIndex;
			m_ActiveBagIndex = -1;
			m_DisposableHandler.DiscardLauncherVisual(
				m_HandLauncherInstance,
				m_Inventory,
				bagIndex,
				m_Data,
				transform);
		}

		m_HandLauncherInstance = null;
		m_ActiveBagIndex = -1;
	}

	public void AnimationEvent_RpgRocketShowInHand()
	{
		if (m_Phase != RocketLauncherOrderPhase.Reloading)
			return;

		SpawnRpgRocketHandVisual();
	}

	public void AnimationEvent_RpgRocketInsert()
	{
		if (m_Phase != RocketLauncherOrderPhase.Reloading || m_InsertedRocket)
			return;

		if (m_RpgHandler == null || m_Inventory == null || m_ActiveDefinition == null)
			return;

		InventorySlotRuntimeData rocketToInsert = default;
		if (!m_PendingUiRocket.IsEmpty)
		{
			rocketToInsert = m_PendingUiRocket;
			m_PendingUiRocket = default;
		}
		else
		{
			// Скорректировать ActiveBagIndex до мутации сумки (иначе holster на InventoryChanged схватит чужой индекс).
			int rocketBagIndex = FindRpgRocketBagIndex();
			if (rocketBagIndex >= 0 && m_ActiveBagIndex > rocketBagIndex)
				m_ActiveBagIndex--;

			if (!m_RpgHandler.TryConsumeRocketFromBag(m_Inventory, m_ActiveDefinition, out int removedIndex, out rocketToInsert))
				return;

			if (removedIndex >= 0 && m_ActiveBagIndex > removedIndex)
				m_ActiveBagIndex--;
		}

		m_RpgHandler.MarkLoaded(ref m_ActiveSlot, rocketToInsert);
		if (m_ActiveBagIndex >= 0)
			m_Inventory.TrySetBagItemAt(m_ActiveBagIndex, m_ActiveSlot);
		if (m_ActiveSlot.InstanceState != null)
			m_ActiveInstanceState = m_ActiveSlot.InstanceState;

		ClearHandRocketVisual();
		SyncHandLauncherRocketVisual();
		m_InsertedRocket = true;
		PlayRpgReloadInsertAudio();
	}

	public void AnimationEvent_RocketLauncherOrderFinished()
	{
		if (m_Phase == RocketLauncherOrderPhase.None)
			return;

		FinishOrder(true);
	}
	#endregion

	#region Private Methods - Order
	private bool TryStartOrderInternal(
		InventorySlotRuntimeData _slot,
		int _bagIndex,
		bool _holdForTuning = false)
	{
		ResolveReferences();
		m_ActiveSlot = _slot;
		m_ActiveBagIndex = _bagIndex;
		m_ActiveDefinition = _slot.Definition;
		m_ActiveInstanceState = _slot.InstanceState;
		m_FiredProjectile = false;
		m_DiscardedLauncher = false;
		m_InsertedRocket = false;
		m_UiRocketInstallActive = false;
		m_UiRocketMirrorAnimationOnly = false;
		m_PendingUiRocket = default;
		m_HoldForTuning = _holdForTuning;

		// Сразу гасим автоогонь экипированного оружия — до ready/aim, иначе возможен выстрел в том же кадре.
		SuppressEquippedWeaponFire();
		HideMainWeapon();

		bool needsReload =
			!_holdForTuning &&
			m_ActiveDefinition != null &&
			m_ActiveDefinition.RocketLauncherType == RocketLauncherType.Rpg7 &&
			m_RpgHandler != null &&
			m_RpgHandler.ShouldReload(m_ActiveSlot, m_Inventory);

		if (m_BusyState != null)
			m_BusyState.SetReasonActive(UnitBusyState.BusyReason.RocketLauncher, true);

		// Aiming до Instantiate — тюнер сразу видит HandLauncherRoot как активный контекст.
		m_Phase = needsReload ? RocketLauncherOrderPhase.Reloading : RocketLauncherOrderPhase.Aiming;

		SpawnHandLauncherVisual();
		SetAnimatorKind(m_ActiveDefinition.RocketLauncherType);

		// Пустой RPG с ракетой в сумке: сразу reload, без aim-таймера (иначе зацикленный aim крутится 1–2 раза).
		if (needsReload)
		{
			SetAimParameter(false);
			TriggerReload();
			m_OrderCoroutine = StartCoroutine(ReloadOnlyRoutine());
			OrderStateChanged?.Invoke();
			return true;
		}

		SetAimParameter(true);
		// Поворот к цели + подъём трубы — с начала aim, не в момент fire.
		EnterRocketLauncherAimReady();

		m_OrderCoroutine = StartCoroutine(_holdForTuning ? TuningHoldRoutine() : OrderRoutine());
		OrderStateChanged?.Invoke();
		return true;
	}

	private IEnumerator UiRocketInstallRoutine()
	{
		yield return WaitForAnimatorStateEnd(ParamRocketLauncherReload, 3.5f);
		FinishOrder(true);
	}

	private IEnumerator ReloadOnlyRoutine()
	{
		yield return WaitForAnimatorStateEnd(ParamRocketLauncherReload, 3.5f);
		while (m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive)
			yield return null;
		FinishOrder(true);
	}

	private IEnumerator TuningHoldRoutine()
	{
		bool sawTuner = m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive;
		while (true)
		{
			yield return null;
			bool tuning = m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive;
			if (tuning)
				sawTuner = true;
			else if (sawTuner)
				break;
		}

		FinishOrder(true);
	}

	private IEnumerator OrderRoutine()
	{
		float aimSeconds = CalculateAimReadySeconds();
		yield return new WaitForSeconds(aimSeconds);

		bool heldForTuning = false;
		while (m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive)
		{
			heldForTuning = true;
			yield return null;
		}

		if (heldForTuning || m_HoldForTuning)
		{
			FinishOrder(true);
			yield break;
		}

		bool isRpg = m_ActiveDefinition != null && m_ActiveDefinition.RocketLauncherType == RocketLauncherType.Rpg7;
		bool canFire = true;
		if (isRpg && m_RpgHandler != null && !m_RpgHandler.IsLoaded(m_ActiveSlot))
			canFire = false;

		if (canFire)
		{
			Vector3 origin = ResolveMuzzleOrigin();
			if (!TryAuthorizeRocketLaunch(origin, out ProjectileLaunchDeny deny))
			{
				LogProjectileAttempt(deny, origin);
				FinishOrder(true);
				yield break;
			}

			m_Phase = RocketLauncherOrderPhase.Firing;
			SetAimParameter(false);
			TriggerFire();
			yield return WaitForAnimatorStateEnd(ParamRocketLauncherFire, 2.5f);
			ExitRocketLauncherAimReady();
		}

		FinishOrder(true);
	}

	private IEnumerator WaitForAnimatorStateEnd(string _stateHint, float _fallbackSeconds)
	{
		float timeout = Mathf.Max(0.5f, _fallbackSeconds);
		float elapsed = 0f;

		// Give animator one frame to enter the state.
		yield return null;

		while (elapsed < timeout)
		{
			elapsed += Time.deltaTime;
			if (m_Animator == null)
				yield break;

			AnimatorStateInfo info = m_AimLayerIndex >= 0
				? m_Animator.GetCurrentAnimatorStateInfo(m_AimLayerIndex)
				: m_Animator.GetCurrentAnimatorStateInfo(0);

			if (info.normalizedTime >= 0.95f && !m_Animator.IsInTransition(m_AimLayerIndex >= 0 ? m_AimLayerIndex : 0))
				yield break;

			yield return null;
		}
	}

	private void FinishOrder(bool _restore)
	{
		if (m_Phase == RocketLauncherOrderPhase.None && m_OrderCoroutine == null && m_HandLauncherInstance == null)
			return;

		if (m_OrderCoroutine != null)
		{
			StopCoroutine(m_OrderCoroutine);
			m_OrderCoroutine = null;
		}

		bool wasUiRocketInstall = m_UiRocketInstallActive;
		bool wasUiMirrorOnly = m_UiRocketMirrorAnimationOnly;

		if (wasUiRocketInstall && !wasUiMirrorOnly && !m_PendingUiRocket.IsEmpty && m_Inventory != null)
			m_Inventory.TryAdd(m_PendingUiRocket);

		SetAimParameter(false);

		if (m_Animator != null)
		{
			m_Animator.ResetTrigger(s_Fire);
			m_Animator.ResetTrigger(s_Reload);
		}

		// Сначала гасим Aim-слой: иначе конец reload/fire ещё override'ит руки в «пустую» позу.
		ReleaseAimLayerOverride();

		if (!m_DiscardedLauncher)
			ClearHandLauncherVisual();

		ClearHandRocketVisual();

		if (_restore)
			ShowMainWeapon();

		ExitRocketLauncherAimReady();
		RestoreEquippedWeaponPresentation();

		if (m_BusyState != null)
			m_BusyState.SetReasonActive(UnitBusyState.BusyReason.RocketLauncher, false);

		m_Phase = RocketLauncherOrderPhase.None;
		m_ActiveDefinition = null;
		m_ActiveInstanceState = null;
		m_ActiveBagIndex = -1;
		m_MuzzleTransform = null;
		m_BackblastTransform = null;
		m_FiredProjectile = false;
		m_DiscardedLauncher = false;
		m_InsertedRocket = false;
		m_UiRocketInstallActive = false;
		m_UiRocketMirrorAnimationOnly = false;
		m_PendingUiRocket = default;
		m_EnteredRocketLauncherFireReady = false;
		m_HoldForTuning = false;
		OrderStateChanged?.Invoke();

		if (wasUiRocketInstall && !wasUiMirrorOnly)
			UiRocketModificationCompleted?.Invoke();
	}
	#endregion

	#region Private Methods - Selection
	private bool TryFindBestLauncher(out InventorySlotRuntimeData _slot, out int _bagIndex)
	{
		_slot = default;
		_bagIndex = -1;
		if (m_Inventory == null)
			return false;

		// Prefer loaded launcher that can fire now.
		for (int i = 0; i < m_Inventory.BagCount; i++)
		{
			InventorySlotRuntimeData slot = m_Inventory.BagItems[i];
			if (!IsUsableLauncher(slot))
				continue;

			if (IsLauncherLoaded(slot))
			{
				_slot = slot;
				_bagIndex = i;
				return true;
			}
		}

		// Then RPG that can be reloaded.
		for (int i = 0; i < m_Inventory.BagCount; i++)
		{
			InventorySlotRuntimeData slot = m_Inventory.BagItems[i];
			if (!IsUsableLauncher(slot))
				continue;

			if (m_RpgHandler != null && m_RpgHandler.ShouldReload(slot, m_Inventory))
			{
				_slot = slot;
				_bagIndex = i;
				return true;
			}
		}

		// Any launcher (even empty RPG without rocket — order will no-op fire).
		for (int i = 0; i < m_Inventory.BagCount; i++)
		{
			InventorySlotRuntimeData slot = m_Inventory.BagItems[i];
			if (!IsUsableLauncher(slot))
				continue;

			_slot = slot;
			_bagIndex = i;
			return true;
		}

		return false;
	}

	private bool TryFindLauncherForTuning(
		ItemDefinition _preferred,
		out InventorySlotRuntimeData _slot,
		out int _bagIndex)
	{
		_slot = default;
		_bagIndex = -1;
		if (m_Inventory == null)
			return false;

		if (_preferred != null)
		{
			for (int i = 0; i < m_Inventory.BagCount; i++)
			{
				InventorySlotRuntimeData slot = m_Inventory.BagItems[i];
				if (slot.IsEmpty || slot.Definition != _preferred)
					continue;
				if (!IsUsableLauncher(slot))
					continue;
				_slot = slot;
				_bagIndex = i;
				return true;
			}
		}

		return TryFindBestLauncher(out _slot, out _bagIndex);
	}

	private void EnsureLauncherLoadedForTuning(ref InventorySlotRuntimeData _slot, int _bagIndex)
	{
		if (_slot.Definition == null || _slot.Definition.RocketLauncherType != RocketLauncherType.Rpg7)
			return;
		if (m_RpgHandler == null)
			return;
		if (m_RpgHandler.IsLoaded(_slot))
			return;

		InventorySlotRuntimeData rocket = default;
		if (_slot.Definition.RpgRocketItemDefinition != null)
			rocket = InventorySlotRuntimeData.FromDefinition(_slot.Definition.RpgRocketItemDefinition);
		m_RpgHandler.MarkLoaded(ref _slot, rocket);
		if (_bagIndex >= 0 && m_Inventory != null)
			m_Inventory.TrySetBagItemAt(_bagIndex, _slot);
	}

	private static bool IsUsableLauncher(InventorySlotRuntimeData _slot)
	{
		return !_slot.IsEmpty && _slot.Definition != null && _slot.Definition.IsRocketLauncher;
	}

	private int FindRpgRocketBagIndex()
	{
		if (m_Inventory == null || m_ActiveDefinition == null)
			return -1;

		ItemDefinition rocketDef = m_ActiveDefinition.RpgRocketItemDefinition;
		for (int i = 0; i < m_Inventory.BagCount; i++)
		{
			InventorySlotRuntimeData slot = m_Inventory.BagItems[i];
			if (slot.IsEmpty || slot.Definition == null)
				continue;

			if (rocketDef != null && slot.Definition == rocketDef)
				return i;

			if (slot.Definition.IsRpgRocketAmmo)
				return i;
		}

		return -1;
	}

	private bool IsLauncherLoaded(InventorySlotRuntimeData _slot)
	{
		if (_slot.Definition == null)
			return false;

		if (_slot.Definition.RocketLauncherType == RocketLauncherType.Disposable)
			return true;

		if (m_RpgHandler != null)
			return m_RpgHandler.IsLoaded(_slot);

		RocketLauncherRuntimeState state = _slot.InstanceState != null
			? _slot.InstanceState.RocketLauncherState
			: null;
		return state != null && state.IsLoaded;
	}
	#endregion

	#region Private Methods - Visual / Fire
	/// <summary>Жёстко гасит огонь/дисциплину экипированного оружия на весь цикл гранатомёта.</summary>
	private void SuppressEquippedWeaponFire()
	{
		m_FireController?.StopFiring();
		m_FireDisciplineController?.InvalidateCurrentSeries();
	}

	/// <summary>Убрать override Aim_Point, чтобы не залипать в конце reload/fire без трубы.</summary>
	private void ReleaseAimLayerOverride()
	{
		if (m_WeaponAiming == null)
			m_WeaponAiming = GetComponent<UnitWeaponAiming>();

		m_WeaponAiming?.SnapAimLayerWeightOff();

		if (m_Animator == null)
			return;

		if (m_AimLayerIndex < 0)
			ResolveAimLayerIndex();

		if (m_AimLayerIndex >= 0)
			m_Animator.SetLayerWeight(m_AimLayerIndex, 0f);
	}

	/// <summary>Вернуть винтовку в правильную ветку локомоции + local pose + IK.</summary>
	private void RestoreEquippedWeaponPresentation()
	{
		if (m_Equipment != null)
			m_Equipment.RefreshHandIkTargets();

		m_EquippedWeaponPose?.ApplyImmediateFromEquipment();

		if (m_AnimatorWeaponMode == null)
			m_AnimatorWeaponMode = GetComponent<UnitAnimatorWeaponMode>();
		m_AnimatorWeaponMode?.ReplayLocomotionIdleCrossfade();
	}

	/// <summary>High ready + face target from aim phase (not only on fire).</summary>
	private void EnterRocketLauncherAimReady()
	{
		if (m_ReadyLayer == null || m_EnteredRocketLauncherFireReady)
			return;

		if (HasEquippedMainWeapon())
			m_ReadyLayer.SetReadyWanted(true, true);
		else
			m_ReadyLayer.BeginRocketLauncherFireReadyOverride();

		m_EnteredRocketLauncherFireReady = true;
	}

	private void ExitRocketLauncherAimReady()
	{
		if (!m_EnteredRocketLauncherFireReady)
			return;

		m_ReadyLayer?.ForceNotReadyAfterRocketLauncherFire();
		m_EnteredRocketLauncherFireReady = false;
	}

	/// <summary>Точка/направление для AimPitch во время приказа гранатомёта.</summary>
	public bool TryGetAimPitchOrigin(out Vector3 _origin, out Vector3 _forward)
	{
		_origin = ResolveMuzzleOrigin();
		if (m_MuzzleTransform != null)
		{
			_forward = m_MuzzleTransform.forward;
			return true;
		}

		if (m_HandLauncherInstance != null)
		{
			_forward = m_HandLauncherInstance.transform.forward;
			return true;
		}

		_forward = transform.forward;
		return HandLauncherRoot != null;
	}

	private bool HasEquippedMainWeapon()
	{
		if (m_Equipment == null)
			return false;

		ItemDefinition def = m_Equipment.EquippedDefinition;
		return def != null && def.IsEquipment && def.EquipmentKind == EquipmentKind.Weapon;
	}

	private void HideMainWeapon()
	{
		if (m_Equipment != null)
			m_Equipment.SetMainWeaponVisualActive(false);
	}

	private void ShowMainWeapon()
	{
		if (m_Equipment != null)
			m_Equipment.SetMainWeaponVisualActive(true);
	}

	private void SpawnHandLauncherVisual()
	{
		ClearHandLauncherVisual();

		Transform anchor = m_RightHandAnchor;
		if (anchor == null || m_ActiveDefinition == null)
			return;

		GameObject prefab = m_Data != null
			? m_Data.ResolveHandPrefab(m_ActiveDefinition)
			: m_ActiveDefinition.RocketLauncherHandPrefab;

		if (prefab == null)
			return;

		m_HandLauncherInstance = Instantiate(prefab, anchor);
		m_HandLauncherInstance.transform.localPosition = m_ActiveDefinition.RightHandLocalPosition;
		m_HandLauncherInstance.transform.localRotation = m_ActiveDefinition.RightHandLocalRotation;
		DisablePhysicsRecursive(m_HandLauncherInstance);
		CacheMuzzleAndBackblast(m_HandLauncherInstance.transform);
		SyncHandLauncherRocketVisual();
		EnsureGripRigTargets();

		UnitEquippedWeaponPoseRuntimeTuner poseTuner = GetComponent<UnitEquippedWeaponPoseRuntimeTuner>()
			?? GetComponentInParent<UnitEquippedWeaponPoseRuntimeTuner>();
		if (poseTuner != null && poseTuner.IsTuningActive)
			poseTuner.ApplyActiveTargetPoseToWeapon();
		else
			m_EquippedWeaponPose?.ApplyImmediateFromEquipment();
	}

	private void SpawnRpgRocketHandVisual()
	{
		ClearHandRocketVisual();

		Transform anchor = m_LeftHandAnchor != null ? m_LeftHandAnchor : m_RightHandAnchor;
		if (anchor == null || m_ActiveDefinition == null)
			return;

		GameObject prefab = m_Data != null
			? m_Data.ResolveRpgRocketHandPrefab(m_ActiveDefinition)
			: m_ActiveDefinition.RpgRocketHandPrefab;

		if (prefab == null && m_ActiveDefinition.RpgRocketItemDefinition != null)
			prefab = m_ActiveDefinition.RpgRocketItemDefinition.DropWorldPrefab;

		if (prefab == null)
			return;

		m_HandRocketInstance = Instantiate(prefab, anchor);

		ItemDefinition rocketPoseDef = m_ActiveDefinition.RpgRocketItemDefinition;
		if (rocketPoseDef != null)
		{
			m_HandRocketInstance.transform.localPosition = rocketPoseDef.RightHandLocalPosition;
			m_HandRocketInstance.transform.localRotation = rocketPoseDef.RightHandLocalRotation;
		}
		else
		{
			m_HandRocketInstance.transform.localPosition = Vector3.zero;
			m_HandRocketInstance.transform.localRotation = Quaternion.identity;
		}

		DisablePhysicsRecursive(m_HandRocketInstance);
	}

	private void TrySpawnProjectile()
	{
		if (m_FiredProjectile || m_ActiveDefinition == null)
			return;

		GameObject prefab = m_Data != null
			? m_Data.ResolveProjectilePrefab(m_ActiveDefinition)
			: m_ActiveDefinition.RocketProjectilePrefab;

		if (prefab == null)
			return;

		Vector3 origin = ResolveMuzzleOrigin();
		if (!TryAuthorizeRocketLaunch(origin, out ProjectileLaunchDeny deny))
		{
			LogProjectileAttempt(deny, origin);
			return;
		}

		RocketLauncherType type = ResolveActiveLauncherType();
		float speed = m_Data != null ? m_Data.GetMuzzleSpeed(type) : ProjectileLaunchPermit.RpgMuzzleSpeed;
		float gravity = m_Data != null ? m_Data.ProjectileGravity : 9.81f;
		float damping = m_Data != null ? m_Data.ProjectileLinearDamping : 0.02f;
		float life = m_Data != null
			? m_Data.ProjectileLifetimeSeconds
			: ProjectileLaunchPermit.RocketLifetimeSeconds;

		Vector3 perfectDirection = ResolveBallisticFireDirection(origin, speed, gravity);
		if (perfectDirection.sqrMagnitude < 0.0001f)
		{
			LogProjectileAttempt(ProjectileLaunchDeny.NoAimPoint, origin);
			return;
		}

		Vector3 direction = ApplyFireDispersion(perfectDirection);
		Quaternion rotation = direction.sqrMagnitude > 0.0001f
			? Quaternion.LookRotation(direction.normalized, Vector3.up)
			: transform.rotation;

		GameObject instance = Instantiate(prefab, origin, rotation);
		PrepareProjectilePhysics(instance);

		RocketProjectile projectile = instance.GetComponent<RocketProjectile>();
		if (projectile == null)
			projectile = instance.AddComponent<RocketProjectile>();

		Rigidbody rb = instance.GetComponent<Rigidbody>();
		if (rb == null)
			rb = instance.AddComponent<Rigidbody>();

		rb.useGravity = false;
		rb.isKinematic = false;
		rb.detectCollisions = true;

		projectile.Launch(direction, speed, life, gravity, damping, m_Data, gameObject, type);
		m_FiredProjectile = true;
		LogProjectileAttempt(ProjectileLaunchDeny.None, origin);

		// После выстрела труба пустая — скрываем встроенный меш ракеты.
		RocketLauncherVisualUtility.ApplyLoadedRocketVisual(m_HandLauncherInstance, false);
	}

	private Vector3 ResolveMuzzleOrigin()
	{
		if (m_MuzzleTransform != null)
			return m_MuzzleTransform.position;

		if (m_HandLauncherInstance != null)
			return m_HandLauncherInstance.transform.position + m_HandLauncherInstance.transform.forward * 0.6f;

		return transform.position + Vector3.up * 1.4f + transform.forward * 0.5f;
	}

	private Vector3 ResolveReloadAudioOrigin()
	{
		if (m_HandLauncherInstance != null)
			return m_HandLauncherInstance.transform.position;

		return transform.position + Vector3.up * 1.2f;
	}

	private void PlayFireAudio()
	{
		if (m_Data == null)
			return;

		RocketLauncherType type = ResolveActiveLauncherType();
		m_Data.PlayFireAudio(type, ResolveMuzzleOrigin(), transform);
	}

	private void PlayFireVfx()
	{
		RocketLauncherFireVfxUtility.PlayFireVfx(m_Data, m_MuzzleTransform, m_BackblastTransform);
	}

	private void PlayRpgReloadInsertAudio()
	{
		if (m_Data == null)
			return;

		m_Data.PlayRpgReloadInsertAudio(ResolveReloadAudioOrigin(), transform);
	}

	private bool TryGetAimTargetPoint(out Vector3 _aimPoint)
	{
		_aimPoint = Vector3.zero;
		if (m_TargetSelector == null)
			return false;

		if (m_TargetSelector.GetEngageableSelectedTarget() == null)
			return false;

		_aimPoint = m_TargetSelector.GetEngageableAimPointWorld();
		return _aimPoint != Vector3.zero;
	}

	/// <summary>
	/// Баллистическое направление: на дистанции прицеливается выше, чтобы ракета упала в цель.
	/// Без Observed AimPoint направление не подменяется muzzle.forward.
	/// </summary>
	private Vector3 ResolveBallisticFireDirection(Vector3 _origin, float _muzzleSpeed, float _gravity)
	{
		if (!TryGetAimTargetPoint(out Vector3 aimPoint))
			return Vector3.zero;

		float distance = Vector3.Distance(_origin, aimPoint);
		Vector3 leadPoint = ProjectileLaunchPermit.ApplyRocketLead(
			aimPoint,
			m_TargetSelector.SelectedTargetVelocity,
			distance,
			_muzzleSpeed);

		RocketBallistics.TrySolveAimDirection(
			_origin,
			leadPoint,
			_muzzleSpeed,
			_gravity,
			out Vector3 aimDirection,
			out _);
		return aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : Vector3.zero;
	}

	private bool TryAuthorizeRocketLaunch(Vector3 _origin, out ProjectileLaunchDeny _reason)
	{
		if (m_FireController == null)
		{
			_reason = ProjectileLaunchDeny.NoAimPoint;
			return false;
		}

		return m_FireController.TryAuthorizeProjectileLaunch(_origin, out _reason);
	}

	private void LogProjectileAttempt(ProjectileLaunchDeny _reason, Vector3 _origin)
	{
		if (!UnitActionLog.Enabled)
			return;

		Vector3 aim = Vector3.zero;
		TryGetAimTargetPoint(out aim);
		string tgt = m_TargetSelector != null && m_TargetSelector.SelectedTarget != null
			? UnitActionLog.Slot(m_TargetSelector.SelectedTarget)
			: "none";
		float distance = aim != Vector3.zero ? Vector3.Distance(_origin, aim) : 0f;
		UnitVision vision = GetComponent<UnitVision>();
		float visionRange = vision != null ? vision.ResolvedMaxRange : UnitVisionProfile.BaseRangeMeters;
		RocketLauncherType type = ResolveActiveLauncherType();
		float speed = m_Data != null ? m_Data.GetMuzzleSpeed(type) : ProjectileLaunchPermit.RpgMuzzleSpeed;
		float life = m_Data != null
			? m_Data.ProjectileLifetimeSeconds
			: ProjectileLaunchPermit.RocketLifetimeSeconds;
		string weapon = type == RocketLauncherType.Disposable ? "Disposable" : "Rpg7";
		string payload =
			"weapon=" + weapon +
			" tgt=" + tgt +
			" aim=" + UnitActionLog.Vec(aim) +
			" distance=" + UnitActionLog.F1(distance) +
			" visionRange=" + UnitActionLog.F1(visionRange) +
			" physicalRange=" + UnitActionLog.F1(
				ProjectileLaunchPermit.TheoreticalPhysicalRangeMeters(speed, life)) +
			" result=" + ProjectileLaunchPermit.FormatResult(_reason);
		UnitActionLog.Write(this, UnitActionLog.Projectile, payload);
	}

	private bool ShouldDrawAimTrajectoryGizmo()
	{
		if (!m_DrawAimTrajectoryGizmo)
			return false;

		if (m_Data != null && !m_Data.DrawAimTrajectoryGizmo)
			return false;

		return m_Phase == RocketLauncherOrderPhase.Aiming || m_Phase == RocketLauncherOrderPhase.Firing;
	}

	private bool TryBuildAimTrajectory(
		out Vector3 _origin,
		out Vector3 _aimDirection,
		out Vector3 _targetPoint,
		out bool _hasTarget,
		out int _pointCount,
		out bool _hitGeometry,
		out Vector3 _impactPoint)
	{
		_origin = ResolveMuzzleOrigin();
		_aimDirection = transform.forward;
		_targetPoint = Vector3.zero;
		_hasTarget = TryGetAimTargetPoint(out _targetPoint);
		_pointCount = 0;
		_hitGeometry = false;
		_impactPoint = _origin;

		RocketLauncherType type = ResolveActiveLauncherType();
		float speed = m_Data != null ? m_Data.GetMuzzleSpeed(type) : 115f;
		float gravity = m_Data != null ? m_Data.ProjectileGravity : 9.81f;
		float life = m_Data != null ? m_Data.ProjectileLifetimeSeconds : 12f;
		float step = m_Data != null ? m_Data.AimGizmoStepSeconds : 0.08f;
		int maxPoints = m_Data != null
			? Mathf.Clamp(m_Data.AimGizmoPointCount, 8, m_AimGizmoBuffer.Length)
			: m_AimGizmoBuffer.Length;

		_aimDirection = ResolveBallisticFireDirection(_origin, speed, gravity);
		Vector3 initialVelocity = _aimDirection * speed;

		// Временный буфер нужного размера через сэмпл в общий массив.
		Vector3[] sampleBuffer = m_AimGizmoBuffer;
		if (maxPoints < sampleBuffer.Length)
		{
			// Sample fills from index 0; limit by shrinking effective max time.
		}

		float maxTime = Mathf.Min(life, step * (maxPoints - 1));
		Collider[] ignore = GetComponentsInChildren<Collider>(true);
		_pointCount = RocketBallistics.SampleTrajectory(
			_origin,
			initialVelocity,
			gravity,
			maxTime,
			step,
			sampleBuffer,
			out _hitGeometry,
			out _impactPoint,
			~0,
			ignore);

		return _pointCount >= 2;
	}

	private Vector3 ApplyFireDispersion(Vector3 _perfectDirection)
	{
		if (_perfectDirection.sqrMagnitude < 0.0001f)
			return _perfectDirection;

		RocketLauncherType type = ResolveActiveLauncherType();
		float distanceMeters = EstimateTargetDistanceMeters();
		float halfAngle = m_Data != null
			? m_Data.GetHalfAngleDegrees(type, distanceMeters, CalculateExternalDispersionMultiplier())
			: 1f;

		return ApplyConeSpread(_perfectDirection.normalized, halfAngle);
	}

	private static Vector3 ApplyConeSpread(Vector3 _forward, float _halfAngleDegrees)
	{
		Vector3 f = _forward.normalized;
		if (_halfAngleDegrees <= 0.0001f)
			return f;

		float tan = Mathf.Tan(_halfAngleDegrees * Mathf.Deg2Rad);
		Vector2 rnd = Random.insideUnitCircle * tan;

		Vector3 up = Mathf.Abs(Vector3.Dot(f, Vector3.up)) > 0.98f ? Vector3.right : Vector3.up;
		Vector3 right = Vector3.Cross(up, f).normalized;
		Vector3 upOrtho = Vector3.Cross(f, right).normalized;
		return (f + right * rnd.x + upOrtho * rnd.y).normalized;
	}

	private void CacheMuzzleAndBackblast(Transform _root)
	{
		m_MuzzleTransform = null;
		m_BackblastTransform = null;
		if (_root == null)
			return;

		Transform muzzle = _root.Find("Muzzle");
		Transform backblast = _root.Find("Backblast");
		if (muzzle == null || backblast == null)
		{
			Transform[] children = _root.GetComponentsInChildren<Transform>(true);
			for (int i = 0; i < children.Length; i++)
			{
				Transform child = children[i];
				if (child == null)
					continue;

				string name = child.name;
				if (muzzle == null &&
				    string.Equals(name, "Muzzle", System.StringComparison.OrdinalIgnoreCase))
					muzzle = child;

				if (backblast == null &&
				    string.Equals(name, "Backblast", System.StringComparison.OrdinalIgnoreCase))
					backblast = child;
			}
		}

		if (muzzle == null)
		{
			Transform[] children = _root.GetComponentsInChildren<Transform>(true);
			for (int i = 0; i < children.Length; i++)
			{
				string name = children[i].name;
				if (name.IndexOf("Barrel", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
				    name.IndexOf("Fire", System.StringComparison.OrdinalIgnoreCase) >= 0)
				{
					muzzle = children[i];
					break;
				}
			}
		}

		if (backblast == null)
			backblast = EnsureBackblastEmpty(_root, muzzle);

		m_MuzzleTransform = muzzle != null ? muzzle : _root;
		m_BackblastTransform = backblast;
	}

	private static Transform EnsureBackblastEmpty(Transform _root, Transform _muzzleOrNull)
	{
		if (_root == null)
			return null;

		Transform existing = _root.Find("Backblast");
		if (existing != null)
			return existing;

		GameObject go = new GameObject("Backblast");
		Transform t = go.transform;
		t.SetParent(_root, false);

		if (_muzzleOrNull != null)
		{
			Vector3 muzzleLocal = _root.InverseTransformPoint(_muzzleOrNull.position);
			t.localPosition = new Vector3(muzzleLocal.x, muzzleLocal.y, -Mathf.Abs(muzzleLocal.z) * 0.85f);
		}
		else
			t.localPosition = new Vector3(0f, 0.08f, -0.45f);

		t.localRotation = Quaternion.Euler(0f, 180f, 0f);
		t.localScale = Vector3.one;
		return t;
	}

	private void SyncHandLauncherRocketVisual()
	{
		if (m_HandLauncherInstance == null)
			return;

		bool loaded = true;
		if (m_ActiveDefinition != null && m_ActiveDefinition.RocketLauncherType == RocketLauncherType.Rpg7)
			loaded = m_RpgHandler != null && m_RpgHandler.IsLoaded(m_ActiveSlot);

		RocketLauncherVisualUtility.ApplyLoadedRocketVisual(m_HandLauncherInstance, loaded);
	}

	private void ClearHandLauncherVisual()
	{
		if (m_HandLauncherInstance != null)
		{
			Destroy(m_HandLauncherInstance);
			m_HandLauncherInstance = null;
		}

		m_RightHandIkTargetTransform = null;
		m_RightHandIkTargetNotReadyTransform = null;
		m_LeftHandIkTargetTransform = null;
		m_LeftHandIkTargetNotReadyTransform = null;
		m_LauncherGripRig = null;
		m_GripLeftHand = null;
		m_GripRightReady = null;
		m_GripRightNotReady = null;
	}

	private void ClearHandRocketVisual()
	{
		if (m_HandRocketInstance != null)
		{
			Destroy(m_HandRocketInstance);
			m_HandRocketInstance = null;
		}
	}

	private static void DisablePhysicsRecursive(GameObject _root)
	{
		if (_root == null)
			return;

		Rigidbody[] bodies = _root.GetComponentsInChildren<Rigidbody>(true);
		for (int i = 0; i < bodies.Length; i++)
		{
			bodies[i].isKinematic = true;
			bodies[i].detectCollisions = false;
		}

		Collider[] colliders = _root.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < colliders.Length; i++)
			colliders[i].enabled = false;
	}

	private static void EnsureChildEmpty(Transform _parent, string _name, Vector3 _localPosition, Vector3 _localEulerAngles)
	{
		if (_parent == null || string.IsNullOrWhiteSpace(_name) || _parent.Find(_name) != null)
			return;

		GameObject target = new GameObject(_name);
		Transform targetTransform = target.transform;
		targetTransform.SetParent(_parent, false);
		targetTransform.localPosition = _localPosition;
		targetTransform.localRotation = Quaternion.Euler(_localEulerAngles);
		targetTransform.localScale = Vector3.one;
	}

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

	private static void SeedFromLegacyIfNeeded(
		Transform _weaponRoot,
		Transform _sReady,
		Transform _sNotReady,
		Transform _cReady,
		Transform _cNotReady,
		Transform _vReady,
		Transform _vNotReady,
		Transform _leftGrip,
		Transform _rightMarker)
	{
		Transform legacyReady = FindChildRecursive(_weaponRoot, "RightHandIkTarget");
		Transform legacyNotReady = FindChildRecursive(_weaponRoot, "RightHandIkTarget_NotReady");
		Transform legacyLeft = FindChildRecursive(_weaponRoot, "LeftHandIkTarget");

		if (legacyReady != null && IsIdentityLocal(_sReady))
			CopyLocal(legacyReady, _sReady);
		if (legacyNotReady != null && IsIdentityLocal(_sNotReady))
			CopyLocal(legacyNotReady, _sNotReady);

		if (IsIdentityLocal(_cReady))
			CopyLocal(_sReady, _cReady);
		if (IsIdentityLocal(_cNotReady))
			CopyLocal(_sNotReady, _cNotReady);
		if (IsIdentityLocal(_vReady))
			CopyLocal(_sReady, _vReady);
		if (IsIdentityLocal(_vNotReady))
			CopyLocal(_sNotReady, _vNotReady);

		if (legacyReady != null && IsIdentityLocal(_rightMarker))
			CopyLocal(legacyReady, _rightMarker);

		Transform authoredLeft = FindChildRecursive(_weaponRoot, WeaponGripRig.LeftHandIkName);
		if (authoredLeft != null && authoredLeft != _leftGrip && !IsIdentityLocal(authoredLeft) && IsIdentityLocal(_leftGrip))
			CopyLocal(authoredLeft, _leftGrip);
		else if (legacyLeft != null && IsIdentityLocal(_leftGrip))
			CopyLocal(legacyLeft, _leftGrip);
	}

	/// <summary>
	/// Canonical left IK is GripRig/LeftHandIK. A leftover LeftHandGrip (created by older Ensure)
	/// must not replace it — that is what made RPG/disposable overwrite each other in the tuner.
	/// </summary>
	private static Transform ResolveAuthoredLeftHandIk(WeaponGripRig _grip, Transform _gripRoot)
	{
		Transform authoredIk = _gripRoot != null ? _gripRoot.Find(WeaponGripRig.LeftHandIkName) : null;
		Transform current = _grip != null ? _grip.LeftHandIk : null;
		Transform leftoverGrip = _gripRoot != null ? _gripRoot.Find(WeaponGripRig.LeftHandGripName) : null;

		if (authoredIk != null)
		{
			if (current != null && current != authoredIk && IsIdentityLocal(authoredIk) && !IsIdentityLocal(current))
				CopyLocal(current, authoredIk);
			else if (leftoverGrip != null && leftoverGrip != authoredIk &&
			         IsIdentityLocal(authoredIk) && !IsIdentityLocal(leftoverGrip))
				CopyLocal(leftoverGrip, authoredIk);
			return authoredIk;
		}

		if (current != null)
			return current;
		if (leftoverGrip != null)
			return leftoverGrip;
		return EnsureNamedChild(_gripRoot, WeaponGripRig.LeftHandIkName);
	}

	private static bool IsIdentityLocal(Transform _t)
	{
		return _t != null
		       && _t.localPosition == Vector3.zero
		       && _t.localRotation == Quaternion.identity;
	}

	private static void CopyLocal(Transform _from, Transform _to)
	{
		if (_from == null || _to == null)
			return;
		_to.localPosition = _from.localPosition;
		_to.localRotation = _from.localRotation;
		_to.localScale = _from.localScale;
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
			Transform result = FindChildRecursive(_root.GetChild(i), _name);
			if (result != null)
				return result;
		}

		return null;
	}

	private static void PrepareProjectilePhysics(GameObject _root)
	{
		if (_root == null)
			return;

		WorldPickupItem[] pickups = _root.GetComponentsInChildren<WorldPickupItem>(true);
		for (int i = 0; i < pickups.Length; i++)
		{
			if (pickups[i] != null)
				Destroy(pickups[i]);
		}

		Collider[] colliders = _root.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < colliders.Length; i++)
		{
			if (colliders[i] == null)
				continue;

			colliders[i].enabled = true;
			colliders[i].isTrigger = false;
		}
	}
	#endregion

	#region Private Methods - Aim Timing
	/// <summary>
	/// База типа × кривая дистанции × множители юнита/ранга (как UnitWeaponAimProgressController).
	/// На 500 м не меньше 3 с. Aim-клип зациклен — длительность удержания задаёт этот таймер.
	/// </summary>
	private float CalculateAimReadySeconds()
	{
		RocketLauncherType type = ResolveActiveLauncherType();
		float distanceMeters = EstimateTargetDistanceMeters();
		float launcherAimTime = m_Data != null
			? m_Data.GetRequiredAimTimeSeconds(type, distanceMeters)
			: 2.3f;

		float unitMultiplier = m_CombatStats != null ? m_CombatStats.GetAimTimeMultiplier() : 1f;
		float individualMultiplier = m_IndividualTraits != null ? m_IndividualTraits.GetAimTimeMultiplier() : 1f;
		float conditionMultiplier = m_CombatCondition != null
			? m_CombatCondition.GetAimTimeMultiplier(IsMoving())
			: 1f;
		float postureMultiplier = m_StanceCombatModifiers != null
			? m_StanceCombatModifiers.GetAimTimeMultiplier()
			: 1f;

		float aimSeconds = launcherAimTime * unitMultiplier * individualMultiplier * conditionMultiplier * postureMultiplier;
		float minSeconds = m_Data != null ? m_Data.GetMinimumAimTimeSeconds(distanceMeters) : 0f;
		return Mathf.Max(0.05f, Mathf.Max(aimSeconds, minSeconds));
	}

	private float CalculateExternalDispersionMultiplier()
	{
		float unitMultiplier = m_CombatStats != null ? m_CombatStats.GetDispersionMultiplier() : 1f;
		float individualMultiplier = m_IndividualTraits != null ? m_IndividualTraits.GetDispersionMultiplier() : 1f;
		float conditionMultiplier = m_CombatCondition != null ? m_CombatCondition.GetDispersionMultiplier() : 1f;
		float postureMultiplier = m_StanceCombatModifiers != null ? m_StanceCombatModifiers.GetSpreadMultiplier() : 1f;
		return Mathf.Max(0.01f, unitMultiplier * individualMultiplier * conditionMultiplier * postureMultiplier);
	}

	private RocketLauncherType ResolveActiveLauncherType()
	{
		return m_ActiveDefinition != null
			? m_ActiveDefinition.RocketLauncherType
			: RocketLauncherType.Rpg7;
	}

	private float EstimateTargetDistanceMeters()
	{
		Transform target = m_TargetSelector != null ? m_TargetSelector.GetEngageableSelectedTarget() : null;
		if (target == null)
			return 0f;

		Vector3 targetPoint = m_TargetSelector.GetEngageableAimPointWorld();
		if (targetPoint == Vector3.zero)
			targetPoint = target.position;

		return Vector3.Distance(transform.position, targetPoint);
	}

	private bool IsMoving()
	{
		if (m_FallenDragController != null && m_FallenDragController.IsDragging)
			return false;

		if (m_LocomotionDriver != null && m_LocomotionDriver.enabled)
			return m_LocomotionDriver.HasMoveIntent;

		return m_ClickToMove != null && m_ClickToMove.enabled && m_ClickToMove.HasMoveIntent;
	}
	#endregion

	#region Private Methods - Animator
	private void SetAnimatorKind(RocketLauncherType _type)
	{
		if (m_Animator == null)
			return;

		int kind = _type == RocketLauncherType.Disposable ? c_KindDisposable : c_KindRpg;
		m_Animator.SetInteger(s_Kind, kind);
	}

	private void SetAimParameter(bool _aiming)
	{
		if (m_Animator == null)
			return;

		m_Animator.SetBool(s_Aim, _aiming);
	}

	private void MaintainTuningAimAnimation()
	{
		if (m_ActiveDefinition != null)
			SetAnimatorKind(m_ActiveDefinition.RocketLauncherType);

		SetAimParameter(true);

		if (m_AimLayerIndex < 0)
			ResolveAimLayerIndex();

		if (m_AimLayerIndex >= 0)
			m_Animator.SetLayerWeight(m_AimLayerIndex, 1f);
	}

	private void TriggerFire()
	{
		if (m_Animator == null)
			return;

		m_Animator.ResetTrigger(s_Fire);
		m_Animator.SetTrigger(s_Fire);
	}

	private void TriggerReload()
	{
		if (m_Animator == null)
			return;

		m_Animator.ResetTrigger(s_Reload);
		m_Animator.SetTrigger(s_Reload);
	}

	private void ResolveAimLayerIndex()
	{
		m_AimLayerIndex = m_Animator != null ? m_Animator.GetLayerIndex(AimLayerName) : -1;
	}
	#endregion

	#region Private Methods - Refs
	private void ResolveReferences()
	{
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_Inventory == null)
			m_Inventory = GetComponent<CharacterInventory>();
		if (m_BusyState == null)
			m_BusyState = GetComponent<UnitBusyState>();
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_ReadyLayer == null)
			m_ReadyLayer = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_TargetSelector == null)
			m_TargetSelector = GetComponent<TargetSelector>();
		if (m_RpgHandler == null)
			m_RpgHandler = GetComponent<UnitRpg7LauncherHandler>();
		if (m_DisposableHandler == null)
			m_DisposableHandler = GetComponent<UnitDisposableLauncherHandler>();
		if (m_CombatStats == null)
			m_CombatStats = GetComponent<UnitCombatStats>();
		if (m_IndividualTraits == null)
			m_IndividualTraits = GetComponent<UnitIndividualTraits>();
		if (m_CombatCondition == null)
			m_CombatCondition = GetComponent<UnitCombatCondition>();
		if (m_StanceCombatModifiers == null)
			m_StanceCombatModifiers = GetComponent<UnitStanceCombatModifiers>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		if (m_LocomotionDriver == null)
			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();
		if (m_FallenDragController == null)
			m_FallenDragController = GetComponent<UnitFallenDragController>();
		if (m_RuntimeTuner == null)
			m_RuntimeTuner = GetComponent<UnitEquippedWeaponPoseRuntimeTuner>();
		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = GetComponent<UnitEquippedWeaponPose>();
		if (m_Stance == null)
			m_Stance = GetComponent<UnitAnimatorStance>();
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_FireDisciplineController == null)
			m_FireDisciplineController = GetComponent<UnitWeaponFireDisciplineController>();
		if (m_WeaponAiming == null)
			m_WeaponAiming = GetComponent<UnitWeaponAiming>();
		if (m_AnimatorWeaponMode == null)
			m_AnimatorWeaponMode = GetComponent<UnitAnimatorWeaponMode>();

		if (m_RightHandAnchor == null && m_Animator != null && m_Animator.isHuman)
			m_RightHandAnchor = m_Animator.GetBoneTransform(HumanBodyBones.RightHand);

		if (m_LeftHandAnchor == null && m_Animator != null && m_Animator.isHuman)
			m_LeftHandAnchor = m_Animator.GetBoneTransform(HumanBodyBones.LeftHand);
	}
	#endregion
}
