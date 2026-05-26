using UnityEngine.EventSystems;

/// <summary>Обработчик сброса оружия на слот экипировки (runtime / MissionPrep).</summary>
public interface IInventoryEquipmentSlotDropHandler
{
	void HandleEquipmentSlotDrop(PointerEventData eventData);
}
