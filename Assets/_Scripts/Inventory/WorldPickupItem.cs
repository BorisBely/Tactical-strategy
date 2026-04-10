using UnityEngine;

/// <summary>
/// Предмет в мире (лут). Попадание в <see cref="InventoryPickupZone"/> добавляет строку в панель «земля».
/// После успешного переноса в инвентарь вызывается <see cref="OnTransferredToCharacterInventory"/> — экземпляр лута
/// на сцене всегда уничтожается (<c>Destroy</c>); данные остаются в <see cref="CharacterInventory"/>.
/// </summary>
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class WorldPickupItem : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private ItemDefinition m_Definition;
	[SerializeField] private ItemInstanceState m_InstanceState;
	#endregion

	#region Private Fields
	private bool m_ListedInGroundUi;
	#endregion

	#region Public Properties
	public bool IsListedInGroundUi => m_ListedInGroundUi;
	#endregion

	#region Public Methods
	public InventorySlotRuntimeData BuildSlotData()
	{
		if (m_Definition != null)
		{
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
}
