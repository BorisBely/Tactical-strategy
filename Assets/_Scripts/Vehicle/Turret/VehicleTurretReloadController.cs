using System.Collections;
using UnityEngine;

/// <summary>
/// Оркестратор перезарядки M2/MK19 на турели: cover, pitch, IK-флаги, короб, рукоятка.
/// Тайминги animation events синхронизированы с Stand_Gunner_Reload @ 30 fps.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(45)]
public sealed class VehicleTurretReloadController : MonoBehaviour
{
	#region Constants
	public const string ParamIsGunnerReloadingM2 = "IsGunnerReloadingM2";
	public const string RightHandIkNotReadyHandleName = "RightHandIkTarget_NotReady_Handle";
	public const string LeftHandIkNotReadyHandleName = "LeftHandIkTarget_NotReady_Handle";
	private const float c_HandleRestLocalZ = -0.217f;
	private const float c_HandleOpenLocalZ = -0.3981f;
	private const float c_ReloadPitchUpDegrees = 40f;
	private const float c_DefaultMagAttachMaxHandDistance = 0.35f;
	private const float c_ReloadClipFps = 30f;
	private const float c_HandleFirstDownDuration = 14f / c_ReloadClipFps;
	private const float c_HandleFirstUpDuration = 15f / c_ReloadClipFps;
	private const float c_HandleSecondDownDuration = 9f / c_ReloadClipFps;
	private const float c_HandleSecondUpDuration = 17f / c_ReloadClipFps;

	private const float c_Mk19HandleRestLocalZ = 0.1667f;
	private const float c_Mk19HandleOpenLocalZ = -0.141f;
	private const float c_Mk19HandleRotateXDeg = -70f;
	private const float c_Mk19HandleRotateDuration = 0.18f;
	private const float c_Mk19HandleDownDuration = 0.4f;
	private const float c_Mk19HandleUpDuration = 0.4f;
	private const string c_Mk19HandleName = "GameObjectBolt";
	private static readonly int s_IsGunnerReloadingM2 = Animator.StringToHash(ParamIsGunnerReloadingM2);
	#endregion

	#region Serialized Fields
	[SerializeField] private VehicleController m_Vehicle;
	[SerializeField] private VehicleInventory m_Inventory;
	[SerializeField] private VehicleTurretHierarchyBinder m_Hierarchy;
	[SerializeField] private VehicleTurretVisualMount m_VisualMount;
	[SerializeField] private VehicleTurretAimController m_Aim;
	[SerializeField] private VehicleTurretEquipmentController m_Equipment;
	[SerializeField] private VehicleTurretBeltFeed m_BeltFeed;
	[SerializeField] private ItemDefinition m_M2MagazineBoxItem;
	[SerializeField] private ItemDefinition m_Mk19MagazineBoxItem;
	[SerializeField, Min(0.05f)] private float m_CoverPitchMoveDuration = 0.25f;
	[SerializeField, Min(0.1f)] private float m_ReloadPitchReturnDuration = 2f;
	[SerializeField, Min(0f)] private float m_PostReloadMinBlockSeconds = 1.5f;
	[SerializeField, Min(0f)] private float m_PostReloadAimStableSeconds = 0.45f;
	[SerializeField, Range(0.1f, 8f)] private float m_PostReloadAimToleranceDegrees = 2f;
	[Header("Magazine Grip")]
	[SerializeField] private Vector3 m_MagLeftHandLocalPosition = new Vector3(-0.0483f, -0.1482f, -0.0001f);
	[SerializeField] private Vector3 m_MagLeftHandLocalEuler = new Vector3(-1.725f, 179.976f, -68.553f);
	[SerializeField, Min(0f)] private float m_MagAttachBlendDuration = 0.12f;
	[SerializeField, Min(0.01f)] private float m_MagAttachMaxHandDistance = c_DefaultMagAttachMaxHandDistance;

	[Header("MK19 Magazine Grip")]
	[SerializeField] private Vector3 m_Mk19MagLeftHandLocalPosition = new Vector3(-0.092f, -0.046f, -0.088f);
	[SerializeField] private Vector3 m_Mk19MagLeftHandLocalEuler = new Vector3(17.745f, -112.385f, -13.41f);
	#endregion

	#region Private Fields
	private RtsUnitMember m_ActiveGunner;
	private UnitVehicleTurretReloadEvents m_ActiveEvents;
	private CharacterInventory m_GunnerInventory;
	private bool m_IsReloading;
	private bool m_AwaitingPostReloadAim;
	private float m_PostReloadGateStartTime;
	private float m_PostReloadAimStableSince = -1f;
	private bool m_SavedGunnerCover;
	private bool m_TrackedCoverForPitch;
	private bool m_ReloadPitchOverrideStarted;
	private bool m_PendingFromGunnerBag;
	private int m_PendingBagIndex = -1;
	private InventorySlotRuntimeData m_PendingFullBox;
	private Transform m_MagTransform;
	private Transform m_MagOriginalParent;
	private Vector3 m_MagOriginalLocalPosition;
	private Quaternion m_MagOriginalLocalRotation;
	private Transform m_HandleTransform;
	private Transform m_RightHandleIkTarget;
	private Transform m_LeftHandleIkTarget;
	private Coroutine m_MagAttachRoutine;
	private Coroutine m_HandleMoveRoutine;
	private Coroutine m_HandleRotateRoutine;
	private bool m_UseLeftHandIk;
	private bool m_UseRightHandIk;
	private bool m_UseNotReadyIkTargets;
	private bool m_UseHandleNotReadyIkTargets;
	private bool m_IsMk19;
	private float m_Mk19HandleRestXAngle;
	private Vector3 m_Mk19HandleRestLocalEuler;
	#endregion

	#region Public Properties
	public bool IsReloading => m_IsReloading;
	public bool IsReloadBusy => m_IsReloading || m_AwaitingPostReloadAim;
	public bool UseLeftHandIk => m_IsReloading && m_UseLeftHandIk;
	public bool UseRightHandIk => m_IsReloading && m_UseRightHandIk;
	public bool UseNotReadyIkTargets => m_IsReloading && m_UseNotReadyIkTargets;
	public bool UseHandleNotReadyIkTargets => m_IsReloading && m_UseHandleNotReadyIkTargets;
	public Transform RightHandHandleIkTarget => m_RightHandleIkTarget;
	public Transform LeftHandHandleIkTarget => m_LeftHandleIkTarget;
	public RtsUnitMember ActiveGunner => m_ActiveGunner;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveRefs();
		if (m_M2MagazineBoxItem == null)
			m_M2MagazineBoxItem = TurretContentCatalog.Get()?.M2MagazineBox;
		if (m_Mk19MagazineBoxItem == null)
			m_Mk19MagazineBoxItem = TurretContentCatalog.Get()?.Mk19MagazineBox;
	}

	private void Update()
	{
		if (m_IsReloading)
			SyncPitchToCurrentCover();

		if (!m_AwaitingPostReloadAim)
			return;

		if (!IsPostReloadAimReady(out _))
			return;

		m_AwaitingPostReloadAim = false;
		ClearReloadState();
	}

	private void OnDisable()
	{
		if (m_IsReloading || m_AwaitingPostReloadAim)
			ForceCancelReload();
	}
	#endregion

	#region Public Methods
	public void Configure(VehicleController _vehicle)
	{
		m_Vehicle = _vehicle;
		ResolveRefs();
	}

	public bool TryStartReload(RtsUnitMember _gunner)
	{
		if (!TryPrepareReload(_gunner))
			return false;

		if (!TryFindReloadBox(_gunner, out m_PendingFromGunnerBag, out m_PendingBagIndex, out m_PendingFullBox))
			return false;

		return BeginPreparedReload(_gunner);
	}

	public bool TryStartReloadWithReservedBox(RtsUnitMember _gunner, InventorySlotRuntimeData _fullBox)
	{
		if (_fullBox.IsEmpty || _fullBox.Definition == null)
			return false;

		if (!TryPrepareReload(_gunner))
			return false;

		m_PendingFromGunnerBag = false;
		m_PendingBagIndex = -1;
		m_PendingFullBox = _fullBox;
		EnsureFullM2BoxRuntimeState(ref m_PendingFullBox);
		return BeginPreparedReload(_gunner);
	}

	public void AnimationEvent_TurretAttachMagToLeftHand()
	{
		AttachMagToLeftHand();
	}

	public void AnimationEvent_TurretShowBelt()
	{
		ShowReloadBeltVisual();
	}

	public void AnimationEvent_TurretDisableRightHandIk()
	{
		SetRightHandIk(false);
	}

	public void AnimationEvent_TurretSwapEmptyForFullMag()
	{
		SwapEmptyForFullMag();
	}

	public void AnimationEvent_TurretEnableRightHandIk()
	{
		m_UseHandleNotReadyIkTargets = false;
		SetRightHandIk(true);
	}

	public void AnimationEvent_TurretReturnMagToWeapon()
	{
		ReturnMagToWeapon();
	}

	public void AnimationEvent_TurretEnableLeftHandIk()
	{
		SetLeftHandIk(true);
	}

	public void AnimationEvent_TurretHandToHandle()
	{
		BeginHandleGripPhase();
	}

	public void AnimationEvent_TurretHandleYankDown()
	{
		if (m_IsMk19)
			AnimateMk19HandleYank();
		else
			AnimateHandle(c_HandleOpenLocalZ, c_HandleFirstDownDuration);
	}

	public void AnimationEvent_TurretHandleFirstReturnUp()
	{
		if (m_IsMk19)
			AnimateMk19HandleReturn();
		else
			AnimateHandle(c_HandleRestLocalZ, c_HandleFirstUpDuration);
	}

	public void AnimationEvent_TurretHandleSecondYankDown()
	{
		if (m_IsMk19)
			AnimateMk19HandleYank();
		else
			AnimateHandle(c_HandleOpenLocalZ, c_HandleSecondDownDuration);
	}

	public void AnimationEvent_TurretHandleSecondReturnUp()
	{
		if (m_IsMk19)
			AnimateMk19HandleReturn();
		else
			AnimateHandle(c_HandleRestLocalZ, c_HandleSecondUpDuration);
	}

	public void AnimationEvent_TurretReleaseHandleIk()
	{
		EndHandleGripPhase();
	}

	public void AnimationEvent_TurretFinishReload()
	{
		CompleteReloadAfterAnimation();
	}

	// Legacy aliases for старых клипов.
	public void AnimationEvent_TurretHandleReturnUp() => AnimationEvent_TurretHandleFirstReturnUp();
	#endregion

	#region Private Methods — Start / Finish
	private bool TryPrepareReload(RtsUnitMember _gunner)
	{
		if (IsReloadBusy || _gunner == null)
			return false;

		if (m_Inventory == null || !m_Inventory.HasTurretWeapon)
			return false;

		InventorySlotRuntimeData weaponSlot = m_Inventory.TurretWeapon;
		if (weaponSlot.IsEmpty || weaponSlot.Definition == null)
			return false;

		TurretWeaponVariant variant = weaponSlot.Definition.TurretWeaponVariant;
		if (variant != TurretWeaponVariant.Browning127 && variant != TurretWeaponVariant.Mk19)
			return false;

		m_IsMk19 = variant == TurretWeaponVariant.Mk19;

		if (!TryResolveReloadTransforms(out m_MagTransform, out m_HandleTransform))
		{
			Debug.LogWarning(
				$"[TurretReload] Mag/Handle not found (mag={(m_MagTransform != null)}, handle={(m_HandleTransform != null)}).",
				this);
			return false;
		}

		return true;
	}

	private bool BeginPreparedReload(RtsUnitMember _gunner)
	{
		m_ActiveGunner = _gunner;
		m_GunnerInventory = _gunner.GetComponent<CharacterInventory>();
		m_ActiveEvents = UnitVehicleTurretReloadEvents.GetOrAdd(_gunner.gameObject);
		m_ActiveEvents.Bind(this);

		m_SavedGunnerCover = m_Vehicle != null && m_Vehicle.IsGunnerCover;
		m_TrackedCoverForPitch = m_SavedGunnerCover;
		m_AwaitingPostReloadAim = false;
		m_UseLeftHandIk = false;
		m_UseRightHandIk = true;
		m_UseNotReadyIkTargets = false;
		m_UseHandleNotReadyIkTargets = false;
		m_IsReloading = true;

		UnitWeaponFireController fire = _gunner.GetComponent<UnitWeaponFireController>();
		fire?.StopFiring();

		BeginReloadPresentation();
		return true;
	}

	private void BeginReloadPresentation()
	{
		if (m_TrackedCoverForPitch)
		{
			m_Aim?.BeginReloadPitchOverride(c_ReloadPitchUpDegrees, m_CoverPitchMoveDuration);
			m_ReloadPitchOverrideStarted = true;
		}

		Animator animator = m_ActiveGunner != null ? m_ActiveGunner.GetComponentInChildren<Animator>() : null;
		if (animator != null)
			animator.SetBool(s_IsGunnerReloadingM2, true);

		m_VisualMount?.CaptureSnapshotsIfNeeded(_force: true);
		CaptureMagOriginalPose();
	}

	private void SyncPitchToCurrentCover()
	{
		if (m_Vehicle == null)
			return;

		bool currentCover = m_Vehicle.IsGunnerCover;
		if (currentCover == m_TrackedCoverForPitch)
			return;

		m_TrackedCoverForPitch = currentCover;

		if (currentCover)
		{
			m_Aim?.BeginReloadPitchOverride(c_ReloadPitchUpDegrees, m_CoverPitchMoveDuration);
			m_ReloadPitchOverrideStarted = true;
		}
		else if (m_ReloadPitchOverrideStarted)
		{
			m_Aim?.EndReloadPitchOverride(m_ReloadPitchReturnDuration);
			m_ReloadPitchOverrideStarted = false;
		}
	}

	private void CompleteReloadAfterAnimation()
	{
		if (!m_IsReloading)
			return;

		StopMagAttachRoutine();
		StopHandleMoveRoutine();
		m_UseNotReadyIkTargets = false;
		m_UseHandleNotReadyIkTargets = false;
		ApplyFullBoxToWeapon();
		m_BeltFeed?.ClearReloadBeltVisualOverride();
		RestoreHandleAndIkParents();

		if (m_Vehicle != null)
			m_Vehicle.IsGunnerCover = m_SavedGunnerCover;

		Animator animator = m_ActiveGunner != null ? m_ActiveGunner.GetComponentInChildren<Animator>() : null;
		if (animator != null)
			animator.SetBool(s_IsGunnerReloadingM2, false);

		if (m_ReloadPitchOverrideStarted)
		{
			m_Aim?.EndReloadPitchOverride(m_ReloadPitchReturnDuration);
			m_ReloadPitchOverrideStarted = false;
		}

		m_IsReloading = false;
		m_PostReloadGateStartTime = Time.time;
		m_PostReloadAimStableSince = -1f;
		m_AwaitingPostReloadAim = true;
	}

	private void ForceCancelReload()
	{
		StopMagAttachRoutine();
		RestoreHandleAndIkParents();
		StopHandleMoveRoutine();
		m_BeltFeed?.ClearReloadBeltVisualOverride();
		EndReloadPresentationImmediate();
		ClearReloadState();
	}

	private void EndReloadPresentationImmediate()
	{
		if (m_Vehicle != null)
			m_Vehicle.IsGunnerCover = m_SavedGunnerCover;

		Animator animator = m_ActiveGunner != null ? m_ActiveGunner.GetComponentInChildren<Animator>() : null;
		if (animator != null)
			animator.SetBool(s_IsGunnerReloadingM2, false);

		if (m_ReloadPitchOverrideStarted)
		{
			m_Aim?.EndReloadPitchOverride(m_CoverPitchMoveDuration);
			m_ReloadPitchOverrideStarted = false;
		}

		m_ActiveEvents?.Unbind(this);
	}

	private void ClearReloadState()
	{
		m_IsReloading = false;
		m_IsMk19 = false;
		m_AwaitingPostReloadAim = false;
		m_ReloadPitchOverrideStarted = false;
		m_TrackedCoverForPitch = false;
		m_UseLeftHandIk = false;
		m_UseRightHandIk = false;
		m_UseNotReadyIkTargets = false;
		m_UseHandleNotReadyIkTargets = false;
		m_ActiveGunner = null;
		m_GunnerInventory = null;
		m_ActiveEvents?.Unbind(this);
		m_ActiveEvents = null;
		m_PendingFullBox = default;
		m_PendingBagIndex = -1;
		m_PendingFromGunnerBag = false;
		m_MagTransform = null;
		m_HandleTransform = null;
		m_RightHandleIkTarget = null;
		m_LeftHandleIkTarget = null;
		m_MagOriginalParent = null;
	}

	private bool IsPostReloadAimReady(out string _blockReason)
	{
		_blockReason = null;
		float elapsed = Time.time - m_PostReloadGateStartTime;

		if (elapsed < m_PostReloadMinBlockSeconds)
		{
			_blockReason = $"min block {elapsed:F1}/{m_PostReloadMinBlockSeconds:F1}s";
			return false;
		}

		if (m_Aim == null)
		{
			if (elapsed < m_ReloadPitchReturnDuration)
			{
				_blockReason = $"pitch return window {elapsed:F1}/{m_ReloadPitchReturnDuration:F1}s";
				return false;
			}

			return true;
		}

		if (m_Aim.IsReloadPitchOverrideActive)
		{
			m_PostReloadAimStableSince = -1f;
			_blockReason = "pitch returning";
			return false;
		}

		if (elapsed < m_ReloadPitchReturnDuration)
		{
			m_PostReloadAimStableSince = -1f;
			_blockReason = $"pitch settle {elapsed:F1}/{m_ReloadPitchReturnDuration:F1}s";
			return false;
		}

		if (m_Aim.HasAimPoint)
		{
			if (!m_Aim.IsBarrelAlignedTo(m_Aim.AimPoint, m_PostReloadAimToleranceDegrees))
			{
				m_PostReloadAimStableSince = -1f;
				_blockReason = "barrel not on target";
				return false;
			}

			if (m_PostReloadAimStableSeconds > 0f)
			{
				if (m_PostReloadAimStableSince < 0f)
					m_PostReloadAimStableSince = Time.time;

				float stableFor = Time.time - m_PostReloadAimStableSince;
				if (stableFor < m_PostReloadAimStableSeconds)
				{
					_blockReason = $"aim stable {stableFor:F2}/{m_PostReloadAimStableSeconds:F2}s";
					return false;
				}
			}
		}

		return true;
	}

	private void ApplyFullBoxToWeapon()
	{
		if (m_Inventory == null || m_PendingFullBox.IsEmpty)
			return;

		InventorySlotRuntimeData weaponSlot = m_Inventory.TurretWeapon;
		if (weaponSlot.IsEmpty || weaponSlot.InstanceState?.WeaponState == null)
			return;

		WeaponRuntimeState weaponState = weaponSlot.InstanceState.WeaponState;
		InventorySlotRuntimeData magCopy = m_PendingFullBox;
		EnsureMagazineRuntimeState(ref magCopy);
		EnsureFullM2BoxRuntimeState(ref magCopy);
		weaponState.TryInsertMagazine(magCopy);
		if (!weaponState.HasRoundInChamber)
			weaponState.TryChamberRoundFromMagazine();

		m_Equipment?.RefreshFromInventory();
	}
	#endregion

	#region Private Methods — Inventory
	private bool TryFindReloadBox(
		RtsUnitMember _gunner,
		out bool _fromGunnerBag,
		out int _bagIndex,
		out InventorySlotRuntimeData _box)
	{
		_fromGunnerBag = false;
		_bagIndex = -1;
		_box = default;

		CharacterInventory gunnerInv = _gunner.GetComponent<CharacterInventory>();
		if (gunnerInv != null && TryFindBoxInBag(gunnerInv.BagItems, out _bagIndex, out _box))
		{
			_fromGunnerBag = true;
			return true;
		}

		if (m_Inventory != null && TryFindBoxInBag(m_Inventory.BagItems, out _bagIndex, out _box))
			return true;

		return false;
	}

	private bool TryFindBoxInBag(
		System.Collections.Generic.IReadOnlyList<InventorySlotRuntimeData> _bag,
		out int _index,
		out InventorySlotRuntimeData _box)
	{
		_index = -1;
		_box = default;
		ItemDefinition magItem = m_IsMk19 ? m_Mk19MagazineBoxItem : m_M2MagazineBoxItem;
		if (_bag == null || magItem == null)
			return false;

		for (int i = 0; i < _bag.Count; i++)
		{
			InventorySlotRuntimeData item = _bag[i];
			if (item.IsEmpty || item.Definition != magItem)
				continue;

			MagazineRuntimeState magState = item.InstanceState?.MagazineState;
			if (magState != null && !magState.HasAmmo)
				continue;

			_index = i;
			_box = item;
			return true;
		}

		return false;
	}

	private void ConsumeReservedFullBox()
	{
		if (m_PendingBagIndex < 0)
			return;

		if (m_PendingFromGunnerBag)
		{
			if (m_GunnerInventory != null)
				m_GunnerInventory.TryRemoveBagAt(m_PendingBagIndex, out _);
			return;
		}

		m_Inventory?.TryRemoveBagAt(m_PendingBagIndex, out _);
	}

	private void ReturnEmptyBoxToVehicle()
	{
		ItemDefinition magItem = m_IsMk19 ? m_Mk19MagazineBoxItem : m_M2MagazineBoxItem;
		if (m_Inventory == null || magItem == null)
			return;

		InventorySlotRuntimeData emptyBox = InventorySlotRuntimeData.FromDefinition(magItem);
		EnsureMagazineRuntimeState(ref emptyBox);
		MagazineRuntimeState magState = emptyBox.InstanceState?.MagazineState;
		if (magState != null && magItem.MagazineDefinition != null)
			magState.Configure(magItem.MagazineDefinition, null, 0);

		m_Inventory.ForceAddToBag(emptyBox);
	}
	#endregion

	#region Private Methods — Visual / IK phases
	private void AttachMagToLeftHand()
	{
		m_BeltFeed?.HideBeltForReload();

		if (m_MagTransform == null || m_ActiveGunner == null)
			return;

		Animator animator = m_ActiveGunner.GetComponentInChildren<Animator>();
		Transform leftHand = animator != null && animator.isHuman
			? animator.GetBoneTransform(HumanBodyBones.LeftHand)
			: null;
		if (leftHand == null)
			return;

		StopMagAttachRoutine();

		Vector3 targetPos = m_IsMk19 ? m_Mk19MagLeftHandLocalPosition : m_MagLeftHandLocalPosition;
		Quaternion targetLocalRotation = Quaternion.Euler(m_IsMk19 ? m_Mk19MagLeftHandLocalEuler : m_MagLeftHandLocalEuler);
		m_MagTransform.SetParent(leftHand, true);

		float handDistance = Vector3.Distance(m_MagTransform.position, leftHand.position);
		if (handDistance > m_MagAttachMaxHandDistance || m_MagAttachBlendDuration <= 0.001f)
		{
			m_MagTransform.localPosition = targetPos;
			m_MagTransform.localRotation = targetLocalRotation;
			return;
		}

		m_MagAttachRoutine = StartCoroutine(AnimateLocalTransformRoutine(
			m_MagTransform,
			targetPos,
			targetLocalRotation,
			m_MagAttachBlendDuration,
			() => m_MagAttachRoutine = null));
	}

	private void SwapEmptyForFullMag()
	{
		if (m_Inventory != null &&
		    m_Inventory.TryGetEquipmentSlot(VehicleEquipmentSlotId.TurretWeapon, out InventorySlotRuntimeData weaponSlot) &&
		    weaponSlot.InstanceState?.WeaponState != null &&
		    weaponSlot.InstanceState.WeaponState.TryEjectMagazine(out InventorySlotRuntimeData ejected))
		{
			MagazineRuntimeState magState = ejected.InstanceState?.MagazineState;
			if (magState != null)
				magState.Configure(magState.Definition, magState.LoadedAmmoDefinition, 0);
			m_Inventory.ForceAddToBag(ejected);
		}
		else
		{
			ReturnEmptyBoxToVehicle();
		}

		ConsumeReservedFullBox();
	}

	private void ReturnMagToWeapon()
	{
		SetRightHandIk(false);
		if (m_MagTransform == null)
			return;

		StopMagAttachRoutine();
		if (m_MagOriginalParent == null)
		{
			CaptureMagOriginalPose();
			if (m_MagOriginalParent == null)
				return;
		}

		m_MagTransform.SetParent(m_MagOriginalParent, false);
		m_MagTransform.localPosition = m_MagOriginalLocalPosition;
		m_MagTransform.localRotation = m_MagOriginalLocalRotation;
	}

	private void BeginHandleGripPhase()
	{
		m_UseNotReadyIkTargets = false;
		m_UseHandleNotReadyIkTargets = true;
		SetRightHandIk(true);
		if (m_IsMk19)
			SetLeftHandIk(true);
		EnsureHandleIkTargetsResolved();
	}

	private void EndHandleGripPhase()
	{
		m_UseHandleNotReadyIkTargets = false;
		SetRightHandIk(false);
		if (m_IsMk19)
			SetLeftHandIk(false);
	}

	private void AnimateMk19HandleYank()
	{
		if (m_HandleTransform == null)
			return;

		StopHandleMoveRoutine();
		StopHandleRotateRoutine();

		Quaternion rotateTarget = Quaternion.Euler(c_Mk19HandleRotateXDeg, m_Mk19HandleRestLocalEuler.y, m_Mk19HandleRestLocalEuler.z);
		m_HandleRotateRoutine = StartCoroutine(AnimateLocalRotationRoutine(
			m_HandleTransform,
			m_HandleTransform.localRotation,
			rotateTarget,
			c_Mk19HandleRotateDuration,
			() =>
			{
				m_HandleRotateRoutine = null;
				Vector3 target = m_HandleTransform.localPosition;
				target.z = c_Mk19HandleOpenLocalZ;
				m_HandleMoveRoutine = StartCoroutine(AnimateLocalPositionRoutine(
					m_HandleTransform,
					m_HandleTransform.localPosition,
					target,
					c_Mk19HandleDownDuration,
					() => m_HandleMoveRoutine = null));
			}));
	}

	private void AnimateMk19HandleReturn()
	{
		if (m_HandleTransform == null)
			return;

		StopHandleMoveRoutine();
		StopHandleRotateRoutine();

		Vector3 posTarget = m_HandleTransform.localPosition;
		posTarget.z = c_Mk19HandleRestLocalZ;
		m_HandleMoveRoutine = StartCoroutine(AnimateLocalPositionRoutine(
			m_HandleTransform,
			m_HandleTransform.localPosition,
			posTarget,
			c_Mk19HandleUpDuration,
			() =>
			{
				m_HandleMoveRoutine = null;
				Quaternion rotTarget = Quaternion.Euler(m_Mk19HandleRestLocalEuler);
				m_HandleRotateRoutine = StartCoroutine(AnimateLocalRotationRoutine(
					m_HandleTransform,
					m_HandleTransform.localRotation,
					rotTarget,
					c_Mk19HandleRotateDuration,
					() => m_HandleRotateRoutine = null));
			}));
	}

	private void AnimateHandle(float _targetLocalZ, float _duration)
	{
		if (m_HandleTransform == null)
			return;

		StopHandleMoveRoutine();
		Vector3 target = m_HandleTransform.localPosition;
		target.z = _targetLocalZ;
		Vector3 start = m_HandleTransform.localPosition;
		m_HandleMoveRoutine = StartCoroutine(AnimateLocalPositionRoutine(
			m_HandleTransform,
			start,
			target,
			_duration,
			() => m_HandleMoveRoutine = null));
	}

	private void RestoreHandleAndIkParents()
	{
		StopHandleMoveRoutine();
		StopHandleRotateRoutine();
		StopMagAttachRoutine();
		m_UseHandleNotReadyIkTargets = false;

		if (m_HandleTransform != null)
		{
			if (m_IsMk19)
			{
				Vector3 pos = m_HandleTransform.localPosition;
				pos.z = c_Mk19HandleRestLocalZ;
				m_HandleTransform.localPosition = pos;
				m_HandleTransform.localEulerAngles = m_Mk19HandleRestLocalEuler;
			}
			else
			{
				Vector3 pos = m_HandleTransform.localPosition;
				pos.z = c_HandleRestLocalZ;
				m_HandleTransform.localPosition = pos;
			}
		}

		if (m_MagTransform != null && m_MagOriginalParent != null)
		{
			if (m_MagTransform.parent != m_MagOriginalParent)
				m_MagTransform.SetParent(m_MagOriginalParent, false);

			m_MagTransform.localPosition = m_MagOriginalLocalPosition;
			m_MagTransform.localRotation = m_MagOriginalLocalRotation;
		}
	}

	private void SetLeftHandIk(bool _enabled) => m_UseLeftHandIk = _enabled;
	private void SetRightHandIk(bool _enabled) => m_UseRightHandIk = _enabled;

	private void CaptureMagOriginalPose()
	{
		if (m_MagTransform == null || m_MagOriginalParent != null)
			return;

		m_MagOriginalParent = m_MagTransform.parent;
		m_MagOriginalLocalPosition = m_MagTransform.localPosition;
		m_MagOriginalLocalRotation = m_MagTransform.localRotation;
	}
	#endregion

	#region Private Methods — Helpers
	private void ShowReloadBeltVisual()
	{
		if (m_BeltFeed == null)
			return;

		InventorySlotRuntimeData box = m_PendingFullBox;
		EnsureMagazineRuntimeState(ref box);
		EnsureFullM2BoxRuntimeState(ref box);

		int visualAmmo = box.InstanceState?.MagazineState?.CurrentAmmoCount ?? -1;
		if (visualAmmo < 0 && box.Definition?.MagazineDefinition != null)
			visualAmmo = box.Definition.MagazineDefinition.Capacity;

		m_BeltFeed.ShowBeltForReload(visualAmmo);
	}

	private void ResolveRefs()
	{
		if (m_Vehicle == null)
			TryGetComponent(out m_Vehicle);
		if (m_Inventory == null)
			TryGetComponent(out m_Inventory);
		if (m_Hierarchy == null)
			TryGetComponent(out m_Hierarchy);
		if (m_VisualMount == null)
			TryGetComponent(out m_VisualMount);
		if (m_Aim == null)
			TryGetComponent(out m_Aim);
		if (m_Equipment == null)
			TryGetComponent(out m_Equipment);
		if (m_BeltFeed == null)
			TryGetComponent(out m_BeltFeed);
	}

	private bool TryResolveReloadTransforms(out Transform _mag, out Transform _handle)
	{
		_mag = null;
		_handle = null;
		if (m_Hierarchy == null)
			return false;

		m_Hierarchy.EnsureBound();

		if (m_IsMk19)
			return TryResolveMk19Transforms(out _mag, out _handle);

		_mag = m_Hierarchy.Mag127;
		if (_mag == null)
			return false;

		Transform gun = _mag.parent;
		if (gun != null)
			_handle = gun.Find("SM_Veh_Pickup_Technical_01_Gun_Handle");

		if (_handle == null)
		{
			Transform pitch = m_Hierarchy.GetActiveWeaponPitch(TurretWeaponVariant.Browning127);
			_handle = FindDeepChild(pitch, "SM_Veh_Pickup_Technical_01_Gun_Handle");
		}

		if (_handle != null)
			EnsureHandleIkTargetsOnHandle(_handle);

		return _handle != null;
	}

	private bool TryResolveMk19Transforms(out Transform _mag, out Transform _handle)
	{
		_mag = m_Hierarchy.MagMk19;
		_handle = null;
		if (_mag == null)
			return false;

		Transform pitch = m_Hierarchy.GetActiveWeaponPitch(TurretWeaponVariant.Mk19);
		_handle = FindDeepChild(pitch, c_Mk19HandleName);

		if (_handle != null)
		{
			m_Mk19HandleRestLocalEuler = _handle.localEulerAngles;
			m_Mk19HandleRestXAngle = m_Mk19HandleRestLocalEuler.x;
			EnsureHandleIkTargetsOnHandle(_handle);
		}

		return _handle != null;
	}

	private void EnsureHandleIkTargetsResolved()
	{
		if (m_HandleTransform == null)
			return;
		EnsureHandleIkTargetsOnHandle(m_HandleTransform);
	}

	private void EnsureHandleIkTargetsOnHandle(Transform _handle)
	{
		if (_handle == null)
			return;

		m_RightHandleIkTarget = FindDeepChild(_handle, RightHandIkNotReadyHandleName);
		if (m_RightHandleIkTarget == null)
		{
			EnsureEmptyChild(_handle, RightHandIkNotReadyHandleName);
			m_RightHandleIkTarget = _handle.Find(RightHandIkNotReadyHandleName);
		}

		Transform leftOnHandle = FindDeepChild(_handle, LeftHandIkNotReadyHandleName);
		if (m_IsMk19)
		{
			m_LeftHandleIkTarget = leftOnHandle;
			if (m_LeftHandleIkTarget == null)
			{
				EnsureEmptyChild(_handle, LeftHandIkNotReadyHandleName);
				m_LeftHandleIkTarget = _handle.Find(LeftHandIkNotReadyHandleName);
			}

			if (m_LeftHandleIkTarget != null)
				m_LeftHandleIkTarget.gameObject.SetActive(true);
		}
		else
		{
			m_LeftHandleIkTarget = null;
			if (leftOnHandle != null)
				leftOnHandle.gameObject.SetActive(false);
		}
	}

	private static bool EnsureEmptyChild(Transform _parent, string _name)
	{
		if (_parent == null || string.IsNullOrEmpty(_name))
			return false;
		if (_parent.Find(_name) != null)
			return false;

		Transform t = new GameObject(_name).transform;
		t.SetParent(_parent, false);
		t.localPosition = Vector3.zero;
		t.localRotation = Quaternion.identity;
		t.localScale = Vector3.one;
		return true;
	}

	private static Transform FindDeepChild(Transform _root, string _name)
	{
		if (_root == null || string.IsNullOrEmpty(_name))
			return null;

		Transform[] all = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < all.Length; i++)
		{
			if (all[i] != null && all[i].name == _name)
				return all[i];
		}

		return null;
	}

	private static void EnsureMagazineRuntimeState(ref InventorySlotRuntimeData _slot)
	{
		if (_slot.IsEmpty || _slot.Definition == null)
			return;
		if (_slot.InstanceState != null)
			return;
		_slot.InstanceState = ItemInstanceState.CreateForDefinition(_slot.Definition);
	}

	private void EnsureFullM2BoxRuntimeState(ref InventorySlotRuntimeData _slot)
	{
		if (_slot.IsEmpty || _slot.Definition == null || _slot.Definition.MagazineDefinition == null)
			return;

		EnsureMagazineRuntimeState(ref _slot);
		MagazineRuntimeState magState = _slot.InstanceState?.MagazineState;
		if (magState == null || magState.HasAmmo)
			return;

		AmmoDefinition ammo = m_IsMk19
			? TurretContentCatalog.Get()?.Ammo40
			: TurretContentCatalog.Get()?.Ammo127;
		if (ammo == null)
			return;

		magState.Configure(
			_slot.Definition.MagazineDefinition,
			ammo,
			_slot.Definition.MagazineDefinition.Capacity);
	}

	private void StopMagAttachRoutine()
	{
		if (m_MagAttachRoutine == null)
			return;
		StopCoroutine(m_MagAttachRoutine);
		m_MagAttachRoutine = null;
	}

	private void StopHandleMoveRoutine()
	{
		if (m_HandleMoveRoutine == null)
			return;
		StopCoroutine(m_HandleMoveRoutine);
		m_HandleMoveRoutine = null;
	}

	private void StopHandleRotateRoutine()
	{
		if (m_HandleRotateRoutine == null)
			return;
		StopCoroutine(m_HandleRotateRoutine);
		m_HandleRotateRoutine = null;
	}

	private static IEnumerator AnimateLocalTransformRoutine(
		Transform _target,
		Vector3 _localPosition,
		Quaternion _localRotation,
		float _duration,
		System.Action _onComplete)
	{
		if (_target == null)
		{
			_onComplete?.Invoke();
			yield break;
		}

		Vector3 startPos = _target.localPosition;
		Quaternion startRot = _target.localRotation;
		float elapsed = 0f;
		float duration = Mathf.Max(0.0001f, _duration);
		while (elapsed < duration)
		{
			elapsed += Time.deltaTime;
			float t = Mathf.Clamp01(elapsed / duration);
			t = t * t * (3f - 2f * t);
			_target.localPosition = Vector3.Lerp(startPos, _localPosition, t);
			_target.localRotation = Quaternion.Slerp(startRot, _localRotation, t);
			yield return null;
		}

		_target.localPosition = _localPosition;
		_target.localRotation = _localRotation;
		_onComplete?.Invoke();
	}

	private static IEnumerator AnimateLocalPositionRoutine(
		Transform _target,
		Vector3 _start,
		Vector3 _end,
		float _duration,
		System.Action _onComplete)
	{
		if (_target == null)
		{
			_onComplete?.Invoke();
			yield break;
		}

		float elapsed = 0f;
		float duration = Mathf.Max(0.0001f, _duration);
		while (elapsed < duration)
		{
			elapsed += Time.deltaTime;
			float t = Mathf.Clamp01(elapsed / duration);
			t = t * t * (3f - 2f * t);
			_target.localPosition = Vector3.Lerp(_start, _end, t);
			yield return null;
		}

		_target.localPosition = _end;
		_onComplete?.Invoke();
	}

	private static IEnumerator AnimateLocalRotationRoutine(
		Transform _target,
		Quaternion _start,
		Quaternion _end,
		float _duration,
		System.Action _onComplete)
	{
		if (_target == null)
		{
			_onComplete?.Invoke();
			yield break;
		}

		float elapsed = 0f;
		float duration = Mathf.Max(0.0001f, _duration);
		while (elapsed < duration)
		{
			elapsed += Time.deltaTime;
			float t = Mathf.Clamp01(elapsed / duration);
			t = t * t * (3f - 2f * t);
			_target.localRotation = Quaternion.Slerp(_start, _end, t);
			yield return null;
		}

		_target.localRotation = _end;
		_onComplete?.Invoke();
	}
	#endregion
}
