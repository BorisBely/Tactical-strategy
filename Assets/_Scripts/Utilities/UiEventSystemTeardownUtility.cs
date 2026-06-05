using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Отключает UI EventSystem до уничтожения UI, чтобы InputSystemUIInputModule
/// не обращался к уже удалённым RectTransform при выходе из Play Mode.
/// </summary>
public static class UiEventSystemTeardownUtility
{
	#region Private Fields
	private static bool s_ReleaseRequested;
	#endregion

	#region Public Methods
	public static void ReleaseAllPointers()
	{
		if (s_ReleaseRequested)
			return;

		s_ReleaseRequested = true;

		EventSystem[] eventSystems = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include);
		for (int i = 0; i < eventSystems.Length; i++)
			ReleaseEventSystem(eventSystems[i]);

		s_ReleaseRequested = false;
	}
	#endregion

	#region Private Methods
	private static void ReleaseEventSystem(EventSystem _eventSystem)
	{
		if (_eventSystem == null)
			return;

		_eventSystem.SetSelectedGameObject(null);

		BaseInputModule[] modules = _eventSystem.GetComponents<BaseInputModule>();
		for (int i = 0; i < modules.Length; i++)
		{
			if (modules[i] != null)
				modules[i].enabled = false;
		}

#if ENABLE_INPUT_SYSTEM
		InputSystemUIInputModule[] inputSystemModules =
			_eventSystem.GetComponents<InputSystemUIInputModule>();
		for (int i = 0; i < inputSystemModules.Length; i++)
		{
			if (inputSystemModules[i] != null)
				inputSystemModules[i].enabled = false;
		}
#endif

		_eventSystem.enabled = false;
	}
	#endregion
}

[DefaultExecutionOrder(-32000)]
[DisallowMultipleComponent]
public sealed class UiEventSystemPlayModeGuard : MonoBehaviour
{
	#region Unity Lifecycle
	private void OnApplicationQuit()
	{
		UiEventSystemTeardownUtility.ReleaseAllPointers();
	}
	#endregion

	#region Bootstrap
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void EnsureInstalled()
	{
		EventSystem eventSystem = EventSystem.current;
		if (eventSystem == null)
			return;

		if (eventSystem.GetComponent<UiEventSystemPlayModeGuard>() != null)
			return;

		eventSystem.gameObject.AddComponent<UiEventSystemPlayModeGuard>();
	}

#if UNITY_EDITOR
	[UnityEditor.InitializeOnLoadMethod]
	private static void RegisterEditorPlayModeHook()
	{
		UnityEditor.EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
		UnityEditor.EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
	}

	private static void HandlePlayModeStateChanged(UnityEditor.PlayModeStateChange _state)
	{
		if (_state != UnityEditor.PlayModeStateChange.ExitingPlayMode)
			return;

		UiEventSystemTeardownUtility.ReleaseAllPointers();
	}
#endif
	#endregion
}
