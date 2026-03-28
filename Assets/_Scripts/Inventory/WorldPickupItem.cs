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
	[SerializeField] private string m_OverrideDisplayName;
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
		InventorySlotRuntimeData data;

		if (m_Definition != null)
		{
			data = InventorySlotRuntimeData.FromDefinition(m_Definition);
			if (!string.IsNullOrWhiteSpace(m_OverrideDisplayName))
				data.DisplayName = m_OverrideDisplayName;
		}
		else
		{
			string name = string.IsNullOrWhiteSpace(m_OverrideDisplayName) ? gameObject.name : m_OverrideDisplayName;
			data = InventorySlotRuntimeData.FromDisplayName(name);
		}

		data.WorldSource = this;
		return data;
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
	public void ConfigureForDroppedFromInventory(ItemDefinition _definition, string _displayName)
	{
		m_Definition = _definition;
		if (string.IsNullOrWhiteSpace(_displayName) || (_definition != null && _displayName == _definition.DisplayName))
			m_OverrideDisplayName = null;
		else
			m_OverrideDisplayName = _displayName;
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
