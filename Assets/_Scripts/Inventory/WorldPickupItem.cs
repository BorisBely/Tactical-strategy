using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

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
		// Instantiate нельзя вызывать из OnValidate (SendMessage / иерархия) — отложить на следующий тик редактора.
		EditorApplication.delayCall += EditorDelayedRefreshVisualState;
	}

	private void EditorDelayedRefreshVisualState()
	{
		if (this == null)
			return;

		RefreshVisualState();
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
		m_InstanceState = _data.InstanceState ?? ItemInstanceState.CreateForDefinition(_data.Definition);
		m_ListedInGroundUi = false;
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
		if (m_InstanceState == null || m_InstanceState.WeaponState == null)
			return null;

		InventorySlotRuntimeData currentMagazineItem = m_InstanceState.WeaponState.CurrentMagazineItem;
		if (currentMagazineItem.IsEmpty || currentMagazineItem.InstanceState == null || currentMagazineItem.InstanceState.MagazineState == null)
			return null;

		return currentMagazineItem.Definition;
	}
	#endregion
}
