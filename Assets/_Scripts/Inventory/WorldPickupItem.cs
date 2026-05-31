using UnityEngine;

/// <summary>
/// Предмет в мире (лут). Попадание в <see cref="InventoryPickupZone"/> добавляет строку в панель «земля».
/// После успешного переноса в инвентарь вызывается <see cref="OnTransferredToCharacterInventory"/> — экземпляр лута
/// на сцене всегда уничтожается (<c>Destroy</c>); данные остаются в <see cref="CharacterInventory"/>.
/// Модули задаются в двух местах на префабе лута: <see cref="m_EquippedAttachments"/> (запись в <see cref="WeaponRuntimeState"/>)
/// и тот же набор на <see cref="EquippedWeapon"/> (визуал в руках / пресет). Списки должны совпадать; в состояние сначала идёт этот массив, иначе — с EquippedWeapon.
/// </summary>
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class WorldPickupItem : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private ItemDefinition m_Definition;
	[SerializeField] private ItemInstanceState m_InstanceState;

	[Tooltip("Те же модули, что на EquippedWeapon этого префаба. Параллельно WeaponDefinition.AttachmentSlots. Пишется в WeaponRuntimeState, пока там пусто (приоритет над списком на EquippedWeapon).")]
	[SerializeField] private WeaponAttachmentDefinition[] m_EquippedAttachments;
	#endregion

	#region Private Fields
	private bool m_ListedInGroundUi;
	private GameObject m_SpawnedWorldVisualRoot;
	#endregion

	#region Public Properties
	public bool IsListedInGroundUi => m_ListedInGroundUi;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		EnsureRuntimeStateInitialized();
		TryCopyEquippedAttachmentsToWeaponStateIfEmpty();
		RefreshVisualState();
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		if (Application.isPlaying)
			return;

		if (m_InstanceState == null || m_Definition == null)
			return;

		TryCopyEquippedAttachmentsToWeaponStateIfEmpty();
	}
#endif
	#endregion

	#region Public Methods
	public InventorySlotRuntimeData BuildSlotData()
	{
		if (m_Definition != null)
		{
			EnsureRuntimeStateInitialized();
			TryCopyEquippedAttachmentsToWeaponStateIfEmpty();
			InventorySlotRuntimeData data = InventorySlotRuntimeData.FromDefinition(m_Definition);
			if (m_InstanceState == null)
				m_InstanceState = data.InstanceState;
			data.InstanceState = m_InstanceState;
			data.WorldSource = this;
			return data;
		}

		InventorySlotRuntimeData fallbackData = InventorySlotRuntimeData.FromDisplayName(gameObject.name);
		fallbackData.WorldSource = this;
		return fallbackData;
	}

	public void RegisterListedInGroundUi()
	{
		m_ListedInGroundUi = true;
	}

	public void ClearGroundUiListing()
	{
		m_ListedInGroundUi = false;
	}

	/// <summary>После спавна при выбросе из рюкзака (данные из инвентаря).</summary>
	public void ConfigureForDroppedFromInventory(InventorySlotRuntimeData _data)
	{
		m_Definition = _data.Definition;
		m_InstanceState = _data.InstanceState != null
			? MissionPrepInventoryCopyUtility.CloneInstanceState(_data.InstanceState)
			: ItemInstanceState.CreateForDefinition(_data.Definition);
		m_ListedInGroundUi = false;
		RefreshVisualState();
	}

	/// <summary>Записать изменения из UI «земля» (модули, магазин и т.д.) в состояние лута в мире.</summary>
	public void ApplyInventorySlotData(InventorySlotRuntimeData _data)
	{
		if (_data.IsEmpty)
			return;

		if (_data.Definition != null)
			m_Definition = _data.Definition;

		if (_data.InstanceState != null)
			m_InstanceState = MissionPrepInventoryCopyUtility.CloneInstanceState(_data.InstanceState);
		else if (m_Definition != null)
			m_InstanceState = ItemInstanceState.CreateForDefinition(m_Definition);

		RefreshVisualState();
	}

	/// <summary>
	/// Вызывается координатором после добавления предмета в <see cref="CharacterInventory"/>.
	/// Уничтожает этот GameObject (весь префаб лута, если скрипт на корне экземпляра).
	/// </summary>
	public void OnTransferredToCharacterInventory()
	{
		m_ListedInGroundUi = false;
		Destroy(gameObject);
	}
	#endregion

	#region Private Methods
	private void EnsureRuntimeStateInitialized()
	{
		if (m_Definition == null || m_InstanceState != null)
			return;

		m_InstanceState = ItemInstanceState.CreateForDefinition(m_Definition);
	}

	private void TryCopyEquippedAttachmentsToWeaponStateIfEmpty()
	{
		EnsureRuntimeStateInitialized();
		if (m_InstanceState?.WeaponState == null || m_Definition?.WeaponDefinition == null)
			return;

		if (HasAnyNonNullAttachment(m_InstanceState.WeaponState.EquippedAttachments))
			return;

		if (HasAnyNonNullAttachment(m_EquippedAttachments))
		{
			m_InstanceState.WeaponState.SetEquippedAttachments(m_EquippedAttachments);
			return;
		}

		EquippedWeapon equippedWeapon = GetComponentInChildren<EquippedWeapon>(true);
		equippedWeapon?.TryCopyEquippedAttachmentsPresetToWeaponStateIfEmpty(m_InstanceState.WeaponState);
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

	private void RefreshVisualState()
	{
		if (!Application.isPlaying)
			return;

		EnsureWorldPickupVisual();

		EquippedWeapon equippedWeapon = GetComponentInChildren<EquippedWeapon>(true);
		if (equippedWeapon == null)
			return;

		ItemDefinition currentMagazineDefinition = GetInsertedMagazineDefinition();
		if (currentMagazineDefinition == null)
			equippedWeapon.ClearInsertedMagazineVisual();
		else
			equippedWeapon.SetInsertedMagazineVisual(currentMagazineDefinition);

		if (m_Definition != null && m_Definition.WeaponDefinition != null)
			equippedWeapon.RefreshAttachmentVisualsFromState(m_Definition.WeaponDefinition, m_InstanceState?.WeaponState);
		else
			equippedWeapon.ClearAttachmentVisuals();
	}

	private ItemDefinition GetInsertedMagazineDefinition()
	{
		WeaponRuntimeState weaponState = m_InstanceState?.WeaponState;
		if (weaponState == null)
			return null;

		ItemDefinition definition = weaponState.InsertedMagazineDefinition;
		if (definition != null)
			return definition;

		InventorySlotRuntimeData currentMagazineItem = weaponState.CurrentMagazineItem;
		if (currentMagazineItem.IsEmpty)
			return null;

		return currentMagazineItem.Definition;
	}

	/// <summary>
	/// DropWorldPrefab — коллайдер/физика; меш оружия берётся из <see cref="ItemDefinition.EquippedVisualPrefab"/>.
	/// </summary>
	private void EnsureWorldPickupVisual()
	{
		EquippedWeapon equippedWeapon = GetComponentInChildren<EquippedWeapon>(true);
		if (equippedWeapon == null)
		{
			if (m_Definition == null || m_Definition.EquippedVisualPrefab == null)
			{
				EnableWeaponRenderers(gameObject);
				return;
			}

			if (m_SpawnedWorldVisualRoot == null)
			{
				m_SpawnedWorldVisualRoot = Instantiate(m_Definition.EquippedVisualPrefab, transform);
				m_SpawnedWorldVisualRoot.name = "WorldPickupVisual";
				m_SpawnedWorldVisualRoot.transform.localPosition = Vector3.zero;
				m_SpawnedWorldVisualRoot.transform.localRotation = Quaternion.identity;
				m_SpawnedWorldVisualRoot.transform.localScale = Vector3.one;
				DisablePhysicsAndNestedPickupOnWorldVisual(m_SpawnedWorldVisualRoot);
			}

			equippedWeapon = m_SpawnedWorldVisualRoot.GetComponentInChildren<EquippedWeapon>(true);
		}

		if (equippedWeapon != null)
		{
			EnableWeaponRenderers(equippedWeapon.gameObject);
			HidePlaceholderMeshOnPickupRoot();
		}
	}

	private void HidePlaceholderMeshOnPickupRoot()
	{
		MeshRenderer rootRenderer = GetComponent<MeshRenderer>();
		if (rootRenderer != null)
			rootRenderer.enabled = false;
	}

	private static void EnableWeaponRenderers(GameObject _root)
	{
		if (_root == null)
			return;

		MeshRenderer[] meshRenderers = _root.GetComponentsInChildren<MeshRenderer>(true);
		for (int i = 0; i < meshRenderers.Length; i++)
		{
			if (meshRenderers[i] != null)
				meshRenderers[i].enabled = true;
		}

		SkinnedMeshRenderer[] skinnedRenderers = _root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
		for (int i = 0; i < skinnedRenderers.Length; i++)
		{
			if (skinnedRenderers[i] != null)
				skinnedRenderers[i].enabled = true;
		}
	}

	private static void DisablePhysicsAndNestedPickupOnWorldVisual(GameObject _root)
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
