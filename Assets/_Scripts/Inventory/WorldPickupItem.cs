using UnityEngine;

/// <summary>
/// Предмет в мире (лут). Один компонент для любых префабов: «простой лут» и «лежащее оружие/броня»
/// отличаются только мешем, коллайдером и ссылкой на <see cref="ItemDefinition"/>.
/// После подбора объект уничтожается или отключается — коллайдер инвентаря больше не нужен;
/// экипировка на теле создаётся отдельно через <see cref="UnitEquipment"/>.
/// Для срабатывания триггера у игрока или предмета нужен <see cref="Rigidbody"/> на одной из сторон.
/// </summary>
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class WorldPickupItem : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private ItemDefinition m_Definition;
	[SerializeField] private string m_OverrideDisplayName;
	[SerializeField] private int m_StackCount = 1;
	[Tooltip("Уничтожить объект после успешного добавления в панель.")]
	[SerializeField] private bool m_DestroyAfterPickup = true;
	[Tooltip("Если не уничтожать — отключить коллайдеры и рендер.")]
	[SerializeField] private bool m_DisableRenderersWhenKept = true;
	#endregion

	#region Public Methods
	public InventorySlotRuntimeData BuildSlotData()
	{
		if (m_Definition != null)
		{
			var data = InventorySlotRuntimeData.FromDefinition(m_Definition, m_StackCount);
			if (!string.IsNullOrWhiteSpace(m_OverrideDisplayName))
				data.DisplayName = m_OverrideDisplayName;
			return data;
		}

		string name = string.IsNullOrWhiteSpace(m_OverrideDisplayName) ? gameObject.name : m_OverrideDisplayName;
		return InventorySlotRuntimeData.FromDisplayName(name, m_StackCount);
	}

	public void OnPickedIntoInventory()
	{
		if (m_DestroyAfterPickup)
		{
			Destroy(gameObject);
			return;
		}

		var colliders = GetComponentsInChildren<Collider>();
		for (int i = 0; i < colliders.Length; i++)
			colliders[i].enabled = false;

		if (m_DisableRenderersWhenKept)
		{
			var renderers = GetComponentsInChildren<Renderer>();
			for (int i = 0; i < renderers.Length; i++)
				renderers[i].enabled = false;
		}

		enabled = false;
	}
	#endregion
}
