using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ручная зарядка магазина в сумке патронами из коробок того же калибра.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(59)]
public sealed class UnitMagazineLoadingController : MonoBehaviour
{
	#region Constants
	public const string ParamIsLoadingMagazine = "IsLoadingMagazine";
	private static readonly int s_IsLoadingMagazine = Animator.StringToHash(ParamIsLoadingMagazine);
	#endregion

	#region Serialized Fields
	[Tooltip("Инвентарь юнита с магазинами и коробками патронов.")]
	[SerializeField] private CharacterInventory m_CharacterInventory;
	[Tooltip("Занятость юнита на время ручной зарядки.")]
	[SerializeField] private UnitBusyState m_BusyState;
	[SerializeField] private UnitEquipment m_UnitEquipment;
	[Tooltip("Для обновления UI, если инвентарь этого юнита сейчас открыт.")]
	[SerializeField] private InventoryScreenBindings m_InventoryBindings;
	[Tooltip("Animator юнита. На нём можно завести bool-параметр IsLoadingMagazine для loop-анимации зарядки.")]
	[SerializeField] private Animator m_Animator;
	[Tooltip("Якорь левой руки для временного визуала магазина во время зарядки. Если пусто, пробуем взять кость LeftHand у humanoid Animator.")]
	[SerializeField] private Transform m_LeftHandAnchor;

	[Header("Debug")]
	[SerializeField] private bool m_IsLoadingMagazine;
	[SerializeField] private int m_DebugTargetMagazineBagIndex = -1;
	[SerializeField] private string m_DebugLastFailureReason;
	[SerializeField] private int m_DebugLoadedRoundsThisSession;
	#endregion

	#region Private Fields
	private GameObject m_LeftHandMagazineVisualInstance;
	#endregion

	#region Public Properties
	public bool IsLoadingMagazine => m_IsLoadingMagazine;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_CharacterInventory == null)
			m_CharacterInventory = GetComponent<CharacterInventory>();
		if (m_BusyState == null)
			m_BusyState = GetComponent<UnitBusyState>();
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponent<UnitEquipment>();
		if (m_InventoryBindings == null)
			m_InventoryBindings = InventoryScreenBindings.Instance;
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_LeftHandAnchor == null && m_Animator != null && m_Animator.isHuman)
			m_LeftHandAnchor = m_Animator.GetBoneTransform(HumanBodyBones.LeftHand);
	}

	private void OnDisable()
	{
		StopLoadingInternal();
	}
	#endregion

	#region Public Methods
	public bool TryStartLoadingMagazineFromAmmoBoxes()
	{
		m_DebugLastFailureReason = null;

		if (m_IsLoadingMagazine)
		{
			m_DebugLastFailureReason = "Already loading";
			return false;
		}

		if (m_CharacterInventory == null)
		{
			m_DebugLastFailureReason = "Missing inventory";
			return false;
		}

		if (m_BusyState != null && m_BusyState.IsBusy)
		{
			m_DebugLastFailureReason = $"Unit is busy: {m_BusyState.Reasons}";
			return false;
		}

		if (!TryFindBestMagazineToLoad(out int targetMagazineIndex, out MagazineRuntimeState targetMagazineState))
		{
			m_DebugLastFailureReason = "No compatible magazine to load";
			return false;
		}

		if (!TryFindBestAmmoBoxIndex(targetMagazineState.Definition.SupportedCaliber, out _))
		{
			m_DebugLastFailureReason = "No ammo box with matching caliber";
			return false;
		}

		m_IsLoadingMagazine = true;
		m_DebugTargetMagazineBagIndex = targetMagazineIndex;
		m_DebugLoadedRoundsThisSession = 0;
		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.Reload, true);
		m_UnitEquipment?.SetMainWeaponVisualActive(false);
		AttachCurrentLoadingMagazineVisualToLeftHand();
		SyncAnimatorState();
		RefreshInventoryUiIfActive();
		return true;
	}

	public void StopLoading()
	{
		StopLoadingInternal();
	}

	/// <summary>
	/// Вызывается анимационным ивентом в момент вставки одного патрона в магазин.
	/// </summary>
	public void AnimationEvent_LoadOneRoundIntoMagazine()
	{
		if (!m_IsLoadingMagazine)
			return;

		if (!TryFindCurrentLoadingMagazine(out MagazineRuntimeState targetMagazineState))
		{
			StopLoadingInternal();
			return;
		}

		if (!TryFindBestAmmoBoxIndex(targetMagazineState.Definition.SupportedCaliber, out int ammoBoxIndex))
		{
			StopLoadingInternal();
			return;
		}

		if (!TryConsumeRoundFromAmmoBox(ammoBoxIndex, out AmmoDefinition ammoDefinition))
		{
			StopLoadingInternal();
			return;
		}

		if (!targetMagazineState.TryLoadRound(ammoDefinition))
		{
			StopLoadingInternal();
			return;
		}

		m_DebugLoadedRoundsThisSession++;
		RefreshInventoryUiIfActive();

		if (targetMagazineState.CurrentAmmoCount >= targetMagazineState.Definition.Capacity ||
			!HasAmmoBoxForCaliber(targetMagazineState.Definition.SupportedCaliber))
		{
			StopLoadingInternal();
		}
	}

	/// <summary>
	/// Опциональный ивент на конец цикла анимации, если нужно корректно выйти из loop, когда заряжать уже нечего.
	/// </summary>
	public void AnimationEvent_FinishMagazineLoadingLoopIfDone()
	{
		if (!m_IsLoadingMagazine)
			return;

		if (!TryFindCurrentLoadingMagazine(out MagazineRuntimeState targetMagazineState))
		{
			StopLoadingInternal();
			return;
		}

		if (targetMagazineState.CurrentAmmoCount >= targetMagazineState.Definition.Capacity ||
			!HasAmmoBoxForCaliber(targetMagazineState.Definition.SupportedCaliber))
		{
			StopLoadingInternal();
		}
	}
	#endregion

	#region Private Methods
	private bool TryFindBestMagazineToLoad(out int _bagIndex, out MagazineRuntimeState _magazineState)
	{
		_bagIndex = -1;
		_magazineState = null;

		if (m_CharacterInventory == null)
			return false;

		IReadOnlyList<InventorySlotRuntimeData> bagItems = m_CharacterInventory.BagItems;
		int bestAmmoCount = int.MaxValue;

		for (int i = 0; i < bagItems.Count; i++)
		{
			InventorySlotRuntimeData item = bagItems[i];
			MagazineRuntimeState magazineState = item.InstanceState != null ? item.InstanceState.MagazineState : null;
			MagazineDefinition magazineDefinition = item.Definition != null ? item.Definition.MagazineDefinition : null;
			if (magazineState == null || magazineDefinition == null)
				continue;
			if (magazineState.Definition == null)
				magazineState.Configure(magazineDefinition, magazineState.LoadedAmmoDefinition, magazineState.CurrentAmmoCount);
			if (magazineState.Definition == null)
				continue;
			if (magazineState.CurrentAmmoCount >= magazineState.Definition.Capacity)
				continue;
			if (!HasAmmoBoxForCaliber(magazineState.Definition.SupportedCaliber))
				continue;
			if (magazineState.CurrentAmmoCount >= bestAmmoCount)
				continue;

			bestAmmoCount = magazineState.CurrentAmmoCount;
			_bagIndex = i;
			_magazineState = magazineState;
		}

		return _bagIndex >= 0 && _magazineState != null;
	}

	private bool TryFindCurrentLoadingMagazine(out MagazineRuntimeState _magazineState)
	{
		_magazineState = null;

		if (m_CharacterInventory == null || m_DebugTargetMagazineBagIndex < 0 || m_DebugTargetMagazineBagIndex >= m_CharacterInventory.BagCount)
			return false;

		InventorySlotRuntimeData item = m_CharacterInventory.BagItems[m_DebugTargetMagazineBagIndex];
		_magazineState = item.InstanceState != null ? item.InstanceState.MagazineState : null;
		return _magazineState != null && _magazineState.Definition != null;
	}

	private bool HasAmmoBoxForCaliber(CaliberType _caliber)
	{
		return TryFindBestAmmoBoxIndex(_caliber, out _);
	}

	private bool TryFindBestAmmoBoxIndex(CaliberType _caliber, out int _bagIndex)
	{
		_bagIndex = -1;

		if (m_CharacterInventory == null)
			return false;

		IReadOnlyList<InventorySlotRuntimeData> bagItems = m_CharacterInventory.BagItems;
		int bestAmmoCount = -1;

		for (int i = 0; i < bagItems.Count; i++)
		{
			InventorySlotRuntimeData item = bagItems[i];
			AmmoContainerRuntimeState ammoContainerState = item.InstanceState != null ? item.InstanceState.AmmoContainerState : null;
			AmmoDefinition ammoDefinition = item.Definition != null ? item.Definition.AmmoDefinition : null;
			if (ammoContainerState == null || ammoDefinition == null)
				continue;
			if (!ammoContainerState.HasAmmo)
				continue;
			if (ammoDefinition.Caliber != _caliber)
				continue;
			if (ammoContainerState.CurrentAmmoCount <= bestAmmoCount)
				continue;

			bestAmmoCount = ammoContainerState.CurrentAmmoCount;
			_bagIndex = i;
		}

		return _bagIndex >= 0;
	}

	private bool TryConsumeRoundFromAmmoBox(int _bagIndex, out AmmoDefinition _ammoDefinition)
	{
		_ammoDefinition = null;

		if (m_CharacterInventory == null || _bagIndex < 0 || _bagIndex >= m_CharacterInventory.BagCount)
			return false;

		InventorySlotRuntimeData bagItem = m_CharacterInventory.BagItems[_bagIndex];
		AmmoContainerRuntimeState ammoContainerState = bagItem.InstanceState != null ? bagItem.InstanceState.AmmoContainerState : null;
		if (ammoContainerState == null || !ammoContainerState.HasAmmo)
			return false;

		_ammoDefinition = ammoContainerState.AmmoDefinition;
		if (!ammoContainerState.TryConsumeRound())
			return false;

		if (!ammoContainerState.HasAmmo)
		{
			bool removed = m_CharacterInventory.TryRemoveBagAt(_bagIndex, out _);
			if (removed && _bagIndex < m_DebugTargetMagazineBagIndex)
				m_DebugTargetMagazineBagIndex--;
			return removed;
		}

		return m_CharacterInventory.TrySetBagItemAt(_bagIndex, bagItem);
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

	private void StopLoadingInternal()
	{
		m_IsLoadingMagazine = false;
		m_DebugTargetMagazineBagIndex = -1;
		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.Reload, false);
		m_UnitEquipment?.SetMainWeaponVisualActive(true);
		ClearLeftHandMagazineVisual();
		SyncAnimatorState();
		RefreshInventoryUiIfActive();
	}

	private void SyncAnimatorState()
	{
		if (m_Animator != null)
			m_Animator.SetBool(s_IsLoadingMagazine, m_IsLoadingMagazine);
	}

	private void AttachCurrentLoadingMagazineVisualToLeftHand()
	{
		ClearLeftHandMagazineVisual();

		if (m_LeftHandAnchor == null || m_CharacterInventory == null)
			return;
		if (m_DebugTargetMagazineBagIndex < 0 || m_DebugTargetMagazineBagIndex >= m_CharacterInventory.BagCount)
			return;

		InventorySlotRuntimeData magazineItem = m_CharacterInventory.BagItems[m_DebugTargetMagazineBagIndex];
		ItemDefinition magazineDefinition = magazineItem.Definition;
		if (magazineDefinition == null || magazineDefinition.EquippedVisualPrefab == null)
			return;

		m_LeftHandMagazineVisualInstance = Instantiate(magazineDefinition.EquippedVisualPrefab, m_LeftHandAnchor);
		m_LeftHandMagazineVisualInstance.transform.localPosition = magazineDefinition.RightHandLocalPosition;
		m_LeftHandMagazineVisualInstance.transform.localRotation = magazineDefinition.RightHandLocalRotation;
		DisablePhysicsOnLoadingVisual(m_LeftHandMagazineVisualInstance);
	}

	private void ClearLeftHandMagazineVisual()
	{
		if (m_LeftHandMagazineVisualInstance == null)
			return;

		Destroy(m_LeftHandMagazineVisualInstance);
		m_LeftHandMagazineVisualInstance = null;
	}
	private static void DisablePhysicsOnLoadingVisual(GameObject _root)
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
