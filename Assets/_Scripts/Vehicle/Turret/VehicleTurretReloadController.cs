using System.Collections;
using UnityEngine;

/// <summary>
/// Оркестратор перезарядки M2 на турели: cover, pitch, IK-флаги, короб, рукоятка.
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
	private static readonly int s_IsGunnerReloadingM2 = Animator.StringToHash(ParamIsGunnerReloadingM2);
	#endregion

	#region Serialized Fields
	[SerializeField] private VehicleController m_Vehicle;
	[SerializeField] private VehicleInventory m_Inventory;
	[SerializeField] private VehicleTurretHierarchyBinder m_Hierarchy;
	[SerializeField] private VehicleTurretVisualMount m_VisualMount;
	[SerializeField] private VehicleTurretAimController m_Aim;
	[SerializeField] private VehicleTurretEquipmentController m_Equipment;
	[SerializeField] private ItemDefinition m_M2MagazineBoxItem;
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
	private bool m_UseLeftHandIk;
	private bool m_UseRightHandIk;
	private bool m_UseNotReadyIkTargets;
	private bool m_UseHandleNotReadyIkTargets;
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
	}

	private void Update()
	{
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
		AnimateHandle(c_HandleOpenLocalZ, c_HandleFirstDownDuration);
	}

	public void AnimationEvent_TurretHandleFirstReturnUp()
	{
		AnimateHandle(c_HandleRestLocalZ, c_HandleFirstUpDuration);
	}

	public void AnimationEvent_TurretHandleSecondYankDown()
	{
		AnimateHandle(c_HandleOpenLocalZ, c_HandleSecondDownDuration);
	}

	public void AnimationEvent_TurretHandleSecondReturnUp()
	{
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
		if (weaponSlot.IsEmpty || weaponSlot.Definition == null ||
		    weaponSlot.Definition.TurretWeaponVariant != TurretWeaponVariant.Browning127)
			return false;

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
		if (m_Vehicle != null && !m_SavedGunnerCover)
			m_Vehicle.IsGunnerCover = true;

		m_Aim?.BeginReloadPitchOverride(c_ReloadPitchUpDegrees, m_CoverPitchMoveDuration);

		Animator animator = m_ActiveGunner != null ? m_ActiveGunner.GetComponentInChildren<Animator>() : null;
		if (animator != null)
			animator.SetBool(s_IsGunnerReloadingM2, true);

		m_VisualMount?.CaptureSnapshotsIfNeeded(_force: true);
		CaptureMagOriginalPose();
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
		RestoreHandleAndIkParents();

		Animator animator = m_ActiveGunner != null ? m_ActiveGunner.GetComponentInChildren<Animator>() : null;
		if (animator != null)
			animator.SetBool(s_IsGunnerReloadingM2, false);

		m_Aim?.EndReloadPitchOverride(m_ReloadPitchReturnDuration);

		if (m_Vehicle != null)
			m_Vehicle.IsGunnerCover = m_SavedGunnerCover;

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
		EndReloadPresentationImmediate();
		ClearReloadState();
	}

	private void EndReloadPresentationImmediate()
	{
		Animator animator = m_ActiveGunner != null ? m_ActiveGunner.GetComponentInChildren<Animator>() : null;
		if (animator != null)
			animator.SetBool(s_IsGunnerReloadingM2, false);

		m_Aim?.EndReloadPitchOverride(m_CoverPitchMoveDuration);

		if (m_Vehicle != null)
			m_Vehicle.IsGunnerCover = m_SavedGunnerCover;

		m_ActiveEvents?.Unbind(this);
	}

	private void ClearReloadState()
	{
		m_IsReloading = false;
		m_AwaitingPostReloadAim = false;
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
		if (_bag == null || m_M2MagazineBoxItem == null)
			return false;

		for (int i = 0; i < _bag.Count; i++)
		{
			InventorySlotRuntimeData item = _bag[i];
			if (item.IsEmpty || item.Definition != m_M2MagazineBoxItem)
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
		if (m_Inventory == null || m_M2MagazineBoxItem == null)
			return;

		InventorySlotRuntimeData emptyBox = InventorySlotRuntimeData.FromDefinition(m_M2MagazineBoxItem);
		EnsureMagazineRuntimeState(ref emptyBox);
		MagazineRuntimeState magState = emptyBox.InstanceState?.MagazineState;
		if (magState != null && m_M2MagazineBoxItem.MagazineDefinition != null)
			magState.Configure(m_M2MagazineBoxItem.MagazineDefinition, null, 0);

		m_Inventory.ForceAddToBag(emptyBox);
	}
	#endregion

	#region Private Methods — Visual / IK phases
	private void AttachMagToLeftHand()
	{
		if (m_MagTransform == null || m_ActiveGunner == null)
			return;

		Animator animator = m_ActiveGunner.GetComponentInChildren<Animator>();
		Transform leftHand = animator != null && animator.isHuman
			? animator.GetBoneTransform(HumanBodyBones.LeftHand)
			: null;
		if (leftHand == null)
			return;

		StopMagAttachRoutine();

		Quaternion targetLocalRotation = Quaternion.Euler(m_MagLeftHandLocalEuler);
		m_MagTransform.SetParent(leftHand, true);

		if (m_MagAttachBlendDuration <= 0.001f)
		{
			m_MagTransform.localPosition = m_MagLeftHandLocalPosition;
			m_MagTransform.localRotation = targetLocalRotation;
			return;
		}

		m_MagAttachRoutine = StartCoroutine(AnimateLocalTransformRoutine(
			m_MagTransform,
			m_MagLeftHandLocalPosition,
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
		EnsureHandleIkTargetsResolved();
	}

	private void EndHandleGripPhase()
	{
		m_UseHandleNotReadyIkTargets = false;
		SetRightHandIk(false);
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
		StopMagAttachRoutine();
		m_UseHandleNotReadyIkTargets = false;

		if (m_HandleTransform != null)
		{
			Vector3 pos = m_HandleTransform.localPosition;
			pos.z = c_HandleRestLocalZ;
			m_HandleTransform.localPosition = pos;
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
	}

	private bool TryResolveReloadTransforms(out Transform _mag, out Transform _handle)
	{
		_mag = null;
		_handle = null;
		if (m_Hierarchy == null)
			return false;

		m_Hierarchy.EnsureBound();
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

		EnsureEmptyChild(_handle, RightHandIkNotReadyHandleName);
		m_RightHandleIkTarget = _handle.Find(RightHandIkNotReadyHandleName);

		Transform leftOnHandle = _handle.Find(LeftHandIkNotReadyHandleName);
		if (leftOnHandle != null)
		{
			// M2 reload uses right handle IK only; left empty may exist disabled on Gun_Handle.
			leftOnHandle.gameObject.SetActive(false);
		}

		m_LeftHandleIkTarget = null;
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

		AmmoDefinition ammo = TurretContentCatalog.Get()?.Ammo127;
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
	#endregion
}
