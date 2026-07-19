using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Надёжная проверка «курсор над UI» для Input System
/// (IsPointerOverGameObject() без pointerId часто врёт).
/// </summary>
public static class UiPointerUtility
{
	#region Private Fields
	private static readonly List<RaycastResult> s_RaycastResults = new List<RaycastResult>(16);
	#endregion

	#region Public Methods
	public static bool IsPointerOverUi()
	{
		if (ActionPanelController.IsPointerOverPanelArea())
			return true;

		if (ShootingRangeUiController.IsPointerOverPanelArea())
			return true;

		EventSystem eventSystem = EventSystem.current;
		if (eventSystem == null)
			return false;

		// Input System: мышь = pointerId -1.
		if (eventSystem.IsPointerOverGameObject(-1))
			return true;

		if (Mouse.current != null && eventSystem.IsPointerOverGameObject(Mouse.current.deviceId))
			return true;

		Vector2 screenPosition = Mouse.current != null
			? Mouse.current.position.ReadValue()
			: Vector2.zero;
		return RaycastUi(screenPosition);
	}

	public static bool RaycastUi(Vector2 _screenPosition)
	{
		EventSystem eventSystem = EventSystem.current;
		if (eventSystem == null)
			return false;

		var pointerData = new PointerEventData(eventSystem)
		{
			position = _screenPosition
		};

		s_RaycastResults.Clear();
		eventSystem.RaycastAll(pointerData, s_RaycastResults);
		return s_RaycastResults.Count > 0;
	}
	#endregion
}
