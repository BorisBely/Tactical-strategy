using UnityEngine;

/// <summary>
/// Перезарядка оружия магазином из сумки по одной общей анимации:
/// если магазин уже был в оружии, он возвращается в сумку по animation event;
/// если магазина не было, этот шаг просто ничего не делает.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(58)]
public sealed class UnitWeaponReloadController : MonoBehaviour
{
	#region Constants
	public const string ParamIsReloadingWeapon = "IsReloadingWeapon";
	private static readonly int s_IsReloadingWeapon = Animator.StringToHash(ParamIsReloadingWeapon);
	#endregion

	#region Serialized Fields
	[SerializeField] private CharacterInventory m_CharacterInventory;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitBusyState m_BusyState;
	[SerializeField] private InventoryScreenBindings m_InventoryBindings;
	[SerializeField] private Animator m_Animator;
	[SerializeField] private Transform m_LeftHandAnchor;

	[Header("Debug")]
	[SerializeField] private bool m_IsReloadingWeapon;
	[SerializeField] private bool m_HasEjectedCurrentMagazine;
	[SerializeField] private int m_DebugSourceBagIndex = -1;
	[SerializeField] private string m_DebugLastFailureReason;
	#endregion

	#region Private Fields
	private InventorySlotRuntimeData m_PendingReplacementMagazine;
	private GameObject m_LeftHandMagazineVisualInstance;
	#endregion

	#region Public Properties
	public bool IsReloadingWeapon => m_IsReloadingWeapon;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_CharacterInventory == null)
			m_CharacterInventory = GetComponent<CharacterInventory>();
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_BusyState == null)
			m_BusyState = GetComponent<UnitBusyState>();
		if (m_InventoryBindings == null)
			m_InventoryBindings = InventoryScreenBindings.Instance;
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_LeftHandAnchor == null && m_Animator != null && m_Animator.isHuman)
			m_LeftHandAnchor = m_Animator.GetBoneTransform(HumanBodyBones.LeftHand);
	}

	private void OnDisable()
	{
		StopReloadInternal(true);
	}
	#endregion

	#region Public Methods
	public bool TryStartReload()
	{
		m_DebugLastFailureReason = null;

		if (m_IsReloadingWeapon)
		{
			m_DebugLastFailureReason = "Already reloading";
			return false;
		}

		if (m_CharacterInventory == null || m_WeaponRuntime == null || m_WeaponRuntime.RuntimeState == null)
		{
			m_DebugLastFailureReason = "Missing runtime references";
			return false;
		}

		if (m_BusyState != null && m_BusyState.IsBusy)
		{
			m_DebugLastFailureReason = $"Unit is busy: {m_BusyState.Reasons}";
			return false;
		}

		if (!TryTakeBestReplacementMagazine(out int sourceBagIndex, out InventorySlotRuntimeData replacementMagazine))
		{
			m_DebugLastFailureReason = "No compatible magazine in bag";
			return false;
		}

		m_IsReloadingWeapon = true;
		m_HasEjectedCurrentMagazine = false;
		m_DebugSourceBagIndex = sourceBagIndex;
		m_PendingReplacementMagazine = replacementMagazine;
		m_FireController?.StopFiring();
		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.Reload, true);
		AttachPendingMagazineVisualToLeftHand();
		SyncAnimatorState();
		RefreshInventoryUiIfActive();
		return true;
	}

	public void StopReload()
	{
		StopReloadInternal(true);
	}

	/// <summary>
	/// Ивент на момент, когда старый магазин должен выйти из оружия.
	/// При пустом оружии просто ничего не делает.
	/// </summary>
	public void AnimationEvent_EjectCurrentWeaponMagazineToInventory()
	{
		if (!m_IsReloadingWeapon || m_HasEjectedCurrentMagazine || m_WeaponRuntime == null)
			return;

		if (m_WeaponRuntime.TryEjectMagazine(out InventorySlotRuntimeData ejectedMagazine))
			m_CharacterInventory?.TryAdd(ejectedMagazine);

		m_HasEjectedCurrentMagazine = true;
		RefreshInventoryUiIfActive();
	}

	/// <summary>
	/// Ивент на момент вставки нового магазина в оружие.
	/// </summary>
	public void AnimationEvent_InsertPendingMagazineIntoWeapon()
	{
		if (!m_IsReloadingWeapon || m_WeaponRuntime == null || m_PendingReplacementMagazine.IsEmpty)
			return;

		if (!m_WeaponRuntime.TryInsertMagazine(m_PendingReplacementMagazine))
		{
			StopReloadInternal(true);
			return;
		}

		m_PendingReplacementMagazine = default;
		ClearLeftHandMagazineVisual();
		RefreshInventoryUiIfActive();
	}

	/// <summary>
	/// Финальный ивент клипа: снимает busy и возвращает контроллер в idle.
	/// </summary>
	public void AnimationEvent_FinishWeaponReload()
	{
		if (!m_IsReloadingWeapon)
			return;

		StopReloadInternal(true);
	}
	#endregion

	#region Private Methods
	private bool TryTakeBestReplacementMagazine(out int _bagIndex, out InventorySlotRuntimeData _magazineItem)
	{
		_bagIndex = -1;
		_magazineItem = default;

		if (m_CharacterInventory == null || m_WeaponRuntime == null || m_WeaponRuntime.RuntimeState == null)
			return false;

		int bestAmmoCount = -1;
		bool bestIsFull = false;

		for (int i = 0; i < m_CharacterInventory.BagCount; i++)
		{
			InventorySlotRuntimeData candidate = m_CharacterInventory.BagItems[i];
			if (!m_WeaponRuntime.RuntimeState.CanAcceptMagazineItem(candidate))
				continue;

			MagazineRuntimeState candidateState = candidate.InstanceState != null ? candidate.InstanceState.MagazineState : null;
			MagazineDefinition candidateDefinition = candidate.Definition != null ? candidate.Definition.MagazineDefinition : null;
			if (candidateState == null || candidateDefinition == null)
				continue;

			bool isFull = candidateState.CurrentAmmoCount >= candidateDefinition.Capacity;
			int ammoCount = candidateState.CurrentAmmoCount;

			if (_bagIndex >= 0)
			{
				if (bestIsFull != isFull && !isFull)
					continue;
				if (bestIsFull == isFull && ammoCount <= bestAmmoCount)
					continue;
				if (!bestIsFull && isFull)
				{
					// full magazine always wins over partial
				}
			}

			bestIsFull = isFull;
			bestAmmoCount = ammoCount;
			_bagIndex = i;
			_magazineItem = candidate;
		}

		if (_bagIndex < 0 || _magazineItem.IsEmpty)
			return false;

		return m_CharacterInventory.TryRemoveBagAt(_bagIndex, out _);
	}

	private void RefreshInventoryUiIfActive()
	{
		if (m_InventoryBindings == null)
			m_InventoryBindings = InventoryScreenBindings.Instance;
		if (m_InventoryBindings == null)
			return;
		if (m_InventoryBindings.ActiveCharacterInventory != m_CharacterInventory)
			return;

		m_InventoryBindings.RefreshActiveCharacterPanel();
	}

	private void StopReloadInternal(bool _restorePendingMagazineToBag)
	{
		if (_restorePendingMagazineToBag && !m_PendingReplacementMagazine.IsEmpty && m_CharacterInventory != null)
			m_CharacterInventory.TryAdd(m_PendingReplacementMagazine);

		m_PendingReplacementMagazine = default;
		m_IsReloadingWeapon = false;
		m_HasEjectedCurrentMagazine = false;
		m_DebugSourceBagIndex = -1;
		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.Reload, false);
		ClearLeftHandMagazineVisual();
		SyncAnimatorState();
		RefreshInventoryUiIfActive();
	}

	private void SyncAnimatorState()
	{
		if (m_Animator != null)
			m_Animator.SetBool(s_IsReloadingWeapon, m_IsReloadingWeapon);
	}

	private void AttachPendingMagazineVisualToLeftHand()
	{
		ClearLeftHandMagazineVisual();

		if (m_LeftHandAnchor == null || m_PendingReplacementMagazine.IsEmpty)
			return;

		ItemDefinition magazineDefinition = m_PendingReplacementMagazine.Definition;
		if (magazineDefinition == null || magazineDefinition.EquippedVisualPrefab == null)
			return;

		m_LeftHandMagazineVisualInstance = Instantiate(magazineDefinition.EquippedVisualPrefab, m_LeftHandAnchor);
		m_LeftHandMagazineVisualInstance.transform.localPosition = magazineDefinition.RightHandLocalPosition;
		m_LeftHandMagazineVisualInstance.transform.localRotation = magazineDefinition.RightHandLocalRotation;
		DisablePhysicsOnVisual(m_LeftHandMagazineVisualInstance);
	}

	private void ClearLeftHandMagazineVisual()
	{
		if (m_LeftHandMagazineVisualInstance == null)
			return;

		Destroy(m_LeftHandMagazineVisualInstance);
		m_LeftHandMagazineVisualInstance = null;
	}

	private static void DisablePhysicsOnVisual(GameObject _root)
	{
		Rigidbody[] bodies = _root.GetComponentsInChildren<Rigidbody>(true);
		for (int i = 0; i < bodies.Length; i++)
		{
			bodies[i].isKinematic = true;
			bodies[i].detectCollisions = false;
		}

		Collider[] colliders = _root.GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < colliders.Length; i++)
			colliders[i].enabled = false;

		WorldPickupItem[] pickups = _root.GetComponentsInChildren<WorldPickupItem>(true);
		for (int i = 0; i < pickups.Length; i++)
			pickups[i].enabled = false;
	}
	#endregion
}
