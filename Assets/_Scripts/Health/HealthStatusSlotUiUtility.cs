using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class HealthStatusSlotUiUtility
{
	public static void EnsureDescriptionHover(HealthStatusSlotView _slot)
	{
		if (_slot == null)
			return;

		DisableTextRaycasts(_slot.transform);

		Graphic raycastTarget = _slot.GetComponent<Graphic>();
		if (raycastTarget == null)
			raycastTarget = _slot.gameObject.AddComponent<Image>();

		raycastTarget.raycastTarget = true;

		if (!_slot.TryGetComponent(out HealthStatusSlotDescriptionHover hover))
			hover = _slot.gameObject.AddComponent<HealthStatusSlotDescriptionHover>();
	}

	public static bool IsScreenPointOverSlotRaycast(HealthStatusSlotView _slot, Vector2 _screenPosition)
	{
		if (_slot == null || EventSystem.current == null)
			return false;

		PointerEventData pointerData = new PointerEventData(EventSystem.current)
		{
			position = _screenPosition
		};

		var results = new System.Collections.Generic.List<RaycastResult>();
		EventSystem.current.RaycastAll(pointerData, results);

		for (int i = 0; i < results.Count; i++)
		{
			if (results[i].gameObject == null)
				continue;

			if (results[i].gameObject.transform.IsChildOf(_slot.transform) ||
			    results[i].gameObject.transform == _slot.transform)
				return true;
		}

		return false;
	}

	private static void DisableTextRaycasts(Transform _root)
	{
		if (_root == null)
			return;

		TMP_Text[] texts = _root.GetComponentsInChildren<TMP_Text>(true);
		for (int i = 0; i < texts.Length; i++)
		{
			if (texts[i] != null)
				texts[i].raycastTarget = false;
		}
	}
}
