using UnityEngine;

/// <summary>
/// Триггер на персонаже: объекты с <see cref="WorldPickupItem"/> попадают в панель «земля».
/// </summary>
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class InventoryPickupZone : MonoBehaviour
{
	#region Serialized Fields
	[Tooltip("Общая панель «земля» в UI (одна на сцену). Перетащите сюда один и тот же InventoryPanelView со всех юнитов.")]
	[SerializeField] private InventoryPanelView m_GroundPanel;
	[Tooltip("Если задано — подбирать только объекты на этих слоях.")]
	[SerializeField] private LayerMask m_ItemLayerMask;
	[SerializeField] private bool m_UseLayerMask;
	[Tooltip("Если true — одинаковые предметы (тот же ItemDefinition и имя) сливаются в одну ячейку на «земле». Если false — каждый подбор в новую ячейку.")]
	[SerializeField] private bool m_TryStackOnGround = true;
	#endregion

	#region Unity Lifecycle
	private void Reset()
	{
		var c = GetComponent<Collider>();
		c.isTrigger = true;
	}
	#endregion

	#region Private Methods
	private void OnTriggerEnter(Collider _other)
	{
		if (m_GroundPanel == null)
			return;

		if (m_UseLayerMask && m_ItemLayerMask != 0)
		{
			int bit = 1 << _other.gameObject.layer;
			if ((m_ItemLayerMask.value & bit) == 0)
				return;
		}

		if (!TryGetPickup(_other, out WorldPickupItem pickup))
			return;

		var data = pickup.BuildSlotData();
		bool ok = m_TryStackOnGround ? m_GroundPanel.TryStackOrAdd(data) : m_GroundPanel.TryAdd(data);
		if (ok)
			pickup.OnPickedIntoInventory();
	}

	private static bool TryGetPickup(Collider _col, out WorldPickupItem _pickup)
	{
		_pickup = _col.GetComponent<WorldPickupItem>();
		if (_pickup == null)
			_pickup = _col.GetComponentInParent<WorldPickupItem>();
		return _pickup != null;
	}
	#endregion
}

