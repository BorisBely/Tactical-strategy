using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class RuntimeModificationPanelUtility
{
	public static bool IsSlotOnPanel(InventorySlotView _slot, InventoryPanelView _panel)
	{
		if (_slot == null || _panel == null)
			return false;

		return _slot.GetComponentInParent<InventoryPanelView>() == _panel;
	}

	public static bool IsScreenPointOverPanel(InventoryPanelView _panel, Vector2 _screenPosition)
	{
		if (_panel == null || EventSystem.current == null)
			return false;

		Transform panelRoot = _panel.transform;
		var results = new System.Collections.Generic.List<RaycastResult>();
		var pointerData = new PointerEventData(EventSystem.current)
		{
			position = _screenPosition
		};

		EventSystem.current.RaycastAll(pointerData, results);
		for (int i = 0; i < results.Count; i++)
		{
			Transform hit = results[i].gameObject.transform;
			if (hit == panelRoot || hit.IsChildOf(panelRoot))
				return true;
		}

		return false;
	}
}
