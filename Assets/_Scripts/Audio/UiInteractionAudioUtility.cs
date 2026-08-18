using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 2D one-shot для hover/click-звуков интерактивных UI-элементов.
/// </summary>
public static class UiInteractionAudioUtility
{
	#region Constants
	private const string c_SettingsResourcesPath = "Audio/UiInteractionAudioSettings";
	private const string c_DropdownListChildName = "Dropdown List";
	#endregion

	#region Static Fields
	private static UiInteractionAudioSettings s_Settings;
	private static AudioSource s_UiAudioSource;
	#endregion

	#region Public Methods
	public static void PlayGenericButtonHover()
	{
		UiInteractionAudioSettings settings = GetSettings();
		if (settings != null)
			PlayOneShot(settings.GenericButtonHoverClip, settings.GenericButtonHoverVolume);
	}

	public static void PlayGenericButtonClick()
	{
		UiInteractionAudioSettings settings = GetSettings();
		if (settings != null)
			PlayOneShot(settings.GenericButtonClickClip, settings.GenericButtonClickVolume);
	}

	public static void EnsureHoverSoundOn(GameObject _gameObject)
	{
		if (_gameObject == null)
			return;

		if (_gameObject.GetComponent<UiHoverClickSound>() != null)
			return;

		if (!TryGetHoverSoundTarget(_gameObject, out _))
			return;

		_gameObject.AddComponent<UiHoverClickSound>();
	}

	public static void WireAllHoverSoundsInLoadedScenes()
	{
		Selectable[] selectables = Object.FindObjectsByType<Selectable>(
			FindObjectsInactive.Include);
		for (int i = 0; i < selectables.Length; i++)
			EnsureHoverSoundOn(selectables[i].gameObject);

		WireExpandedDropdownItems();
	}

	public static void WireExpandedDropdownItems()
	{
		TMP_Dropdown[] dropdowns = Object.FindObjectsByType<TMP_Dropdown>(
			FindObjectsInactive.Include);
		for (int i = 0; i < dropdowns.Length; i++)
		{
			if (dropdowns[i] == null)
				continue;

			WireDropdownListItems(dropdowns[i].transform);
		}

		Dropdown[] legacyDropdowns = Object.FindObjectsByType<Dropdown>(
			FindObjectsInactive.Include);
		for (int i = 0; i < legacyDropdowns.Length; i++)
		{
			if (legacyDropdowns[i] == null)
				continue;

			WireDropdownListItems(legacyDropdowns[i].transform);
		}
	}
	#endregion

	#region Private Methods
	private static UiInteractionAudioSettings GetSettings()
	{
		if (s_Settings != null)
			return s_Settings;

		s_Settings = Resources.Load<UiInteractionAudioSettings>(c_SettingsResourcesPath);
		return s_Settings;
	}

	private static void EnsureUiAudioSource()
	{
		if (s_UiAudioSource != null)
			return;

		GameObject audioRoot = new GameObject("UiInteractionAudio");
		Object.DontDestroyOnLoad(audioRoot);
		s_UiAudioSource = audioRoot.AddComponent<AudioSource>();
		s_UiAudioSource.playOnAwake = false;
		s_UiAudioSource.spatialBlend = 0f;
		s_UiAudioSource.dopplerLevel = 0f;
	}

	private static void PlayOneShot(AudioClip _clip, float _volume)
	{
		if (_clip == null || _volume <= 0f)
			return;

		EnsureUiAudioSource();
		s_UiAudioSource.PlayOneShot(_clip, Mathf.Clamp01(_volume));
	}

	private static bool TryGetHoverSoundTarget(GameObject _gameObject, out Selectable _selectable)
	{
		if (_gameObject == null)
		{
			_selectable = null;
			return false;
		}

		return _gameObject.TryGetComponent(out _selectable);
	}

	private static void WireDropdownListItems(Transform _dropdownTransform)
	{
		if (_dropdownTransform == null)
			return;

		Transform dropdownList = _dropdownTransform.Find(c_DropdownListChildName);
		if (dropdownList == null || !dropdownList.gameObject.activeInHierarchy)
			return;

		Selectable[] selectables = dropdownList.GetComponentsInChildren<Selectable>(true);
		for (int i = 0; i < selectables.Length; i++)
			EnsureHoverSoundOn(selectables[i].gameObject);
	}
	#endregion
}

/// <summary>
/// Проигрывает hover/click-щелчки для интерактивного UI-элемента.
/// </summary>
[DisallowMultipleComponent]
public sealed class UiHoverClickSound : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler, ISubmitHandler
{
	#region Private Fields
	private Selectable m_Selectable;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Selectable = GetComponent<Selectable>();
	}
	#endregion

	#region Event Handlers
	public void OnPointerEnter(PointerEventData _eventData)
	{
		if (_eventData == null || !isActiveAndEnabled)
			return;

		if (!IsInteractable())
			return;

		UiInteractionAudioUtility.PlayGenericButtonHover();
	}

	public void OnPointerClick(PointerEventData _eventData)
	{
		if (_eventData == null || _eventData.button != PointerEventData.InputButton.Left || !isActiveAndEnabled)
			return;

		if (!IsInteractable())
			return;

		UiInteractionAudioUtility.PlayGenericButtonClick();
	}

	public void OnSubmit(BaseEventData _eventData)
	{
		if (!isActiveAndEnabled || !IsInteractable())
			return;

		UiInteractionAudioUtility.PlayGenericButtonClick();
	}
	#endregion

	#region Private Methods
	private bool IsInteractable() => m_Selectable == null || m_Selectable.IsInteractable();
	#endregion
}

/// <summary>
/// Автоматически добавляет UI-звуки на Selectable и элементы открытых dropdown.
/// </summary>
[DefaultExecutionOrder(-850)]
[DisallowMultipleComponent]
public sealed class UiHoverClickSoundBootstrap : MonoBehaviour
{
	#region Private Fields
	private float m_NextScanTime;
	#endregion

	#region Bootstrap
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Install()
	{
		if (FindAnyObjectByType<UiHoverClickSoundBootstrap>() != null)
			return;

		GameObject bootstrapRoot = new GameObject(nameof(UiHoverClickSoundBootstrap));
		bootstrapRoot.AddComponent<UiHoverClickSoundBootstrap>();
	}

	private void Awake()
	{
		DontDestroyOnLoad(gameObject);
		SceneManager.sceneLoaded += HandleSceneLoaded;
		UiInteractionAudioUtility.WireAllHoverSoundsInLoadedScenes();
	}

	private void OnDestroy()
	{
		SceneManager.sceneLoaded -= HandleSceneLoaded;
	}

	private void Update()
	{
		if (Time.unscaledTime < m_NextScanTime)
			return;

		m_NextScanTime = Time.unscaledTime + 0.5f;
		UiInteractionAudioUtility.WireAllHoverSoundsInLoadedScenes();
	}
	#endregion

	#region Private Methods
	private static void HandleSceneLoaded(Scene _scene, LoadSceneMode _mode)
	{
		UiInteractionAudioUtility.WireAllHoverSoundsInLoadedScenes();
	}
	#endregion
}
