using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Невидимая зона сброса на всю ячейку экипировки (поверх TMP/иконки).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UnityEngine.UI.Image))]
public sealed class InventoryEquipmentSlotDropReceiver : MonoBehaviour, IDropHandler
{
	#region Private Fields
	private IInventoryEquipmentSlotDropHandler m_DropHandler;
	#endregion

	#region Public Methods
	public void Bind(IInventoryEquipmentSlotDropHandler _dropHandler)
	{
		m_DropHandler = _dropHandler;
	}
	#endregion

	#region Event Handlers
	public void OnDrop(PointerEventData eventData)
	{
		if (m_DropHandler == null)
			m_DropHandler = GetComponentInParent<IInventoryEquipmentSlotDropHandler>();

		m_DropHandler?.HandleEquipmentSlotDrop(eventData);
	}
	#endregion
}
