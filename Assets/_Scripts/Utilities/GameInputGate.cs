using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Единая проверка: пауза или UI-поле ввода захватило фокус клавиатуры.
/// </summary>
public static class GameInputGate
{
	#region Public Methods
	public static bool ShouldBlockGameplayInput()
	{
		if (PauseMenuController.IsPaused)
			return true;

		return HasBlockingUiInputFocus();
	}

	public static void ReleaseUiInputCapture()
	{
		EventSystem eventSystem = EventSystem.current;
		if (eventSystem == null)
			return;

		eventSystem.SetSelectedGameObject(null);
	}
	#endregion

	#region Private Methods
	private static bool HasBlockingUiInputFocus()
	{
		EventSystem eventSystem = EventSystem.current;
		if (eventSystem == null || eventSystem.currentSelectedGameObject == null)
			return false;

		GameObject selected = eventSystem.currentSelectedGameObject;
		if (selected.GetComponent<TMP_InputField>() != null)
			return true;

		return selected.GetComponentInParent<TMP_InputField>() != null;
	}
	#endregion
}
