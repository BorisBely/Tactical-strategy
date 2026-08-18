using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed class LocalizationManager : MonoBehaviour
{
	#region Constants
	private const string c_ResourcesPath = "Localization";
	private const string c_PlayerPrefsLanguageKey = "LocalizationManager.CurrentLanguage";
	#endregion

	#region Private Fields
	private static LocalizationManager s_Instance;
	private static bool s_ApplicationIsQuitting;

	private readonly Dictionary<GameLanguage, Dictionary<string, string>> m_Tables =
		new Dictionary<GameLanguage, Dictionary<string, string>>();

	[SerializeField] private GameLanguage m_CurrentLanguage = GameLanguage.English;
	#endregion

	#region Events
	public static event Action LanguageChanged;
	#endregion

	#region Public Properties
	public static bool HasInstance => s_Instance != null;

	public static LocalizationManager Instance
	{
		get
		{
			if (s_ApplicationIsQuitting)
				return s_Instance;

			return EnsureInstance();
		}
	}

	public static GameLanguage CurrentLanguage =>
		HasInstance ? s_Instance.m_CurrentLanguage : GameLanguage.English;
	#endregion

	#region Bootstrap
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics()
	{
		s_Instance = null;
		s_ApplicationIsQuitting = false;
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Bootstrap()
	{
		EnsureInstance();
	}

#if UNITY_EDITOR
	[UnityEditor.InitializeOnLoadMethod]
	private static void RegisterEditorPlayModeCleanup()
	{
		UnityEditor.EditorApplication.playModeStateChanged -= HandleEditorPlayModeStateChanged;
		UnityEditor.EditorApplication.playModeStateChanged += HandleEditorPlayModeStateChanged;
	}

	private static void HandleEditorPlayModeStateChanged(UnityEditor.PlayModeStateChange _state)
	{
		switch (_state)
		{
			case UnityEditor.PlayModeStateChange.ExitingPlayMode:
				s_ApplicationIsQuitting = true;
				DestroyRuntimeInstance();
				break;

			case UnityEditor.PlayModeStateChange.EnteredEditMode:
				s_ApplicationIsQuitting = false;
				DestroyAllLocalizationManagerObjects();
				break;
		}
	}
#endif
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (s_Instance != null && s_Instance != this)
		{
			Destroy(gameObject);
			return;
		}

		s_Instance = this;

#if !UNITY_EDITOR
		DontDestroyOnLoad(gameObject);
#endif

		LoadLocalizationTables();
		RestoreLanguage();
	}

	private void OnApplicationQuit()
	{
		s_ApplicationIsQuitting = true;
	}

	private void OnDestroy()
	{
		if (s_Instance == this)
			s_Instance = null;
	}

	private void Update()
	{
		Keyboard keyboard = Keyboard.current;
		if (keyboard == null || !keyboard.lKey.wasPressedThisFrame)
			return;

		ToggleLanguage();
	}
	#endregion

	#region Public Methods
	public static string Get(string _key)
	{
		if (s_ApplicationIsQuitting || !HasInstance)
			return _key ?? string.Empty;

		return Instance.GetInternal(_key);
	}

	public static string Get(string _key, string _fallback)
	{
		if (s_ApplicationIsQuitting || !HasInstance)
			return _fallback ?? _key ?? string.Empty;

		string value = Instance.GetInternal(_key);
		return value == _key ? _fallback : value;
	}

	public void ToggleLanguage()
	{
		SetLanguage(m_CurrentLanguage == GameLanguage.English ? GameLanguage.Russian : GameLanguage.English);
	}

	public void SetLanguage(GameLanguage _language)
	{
		if (m_CurrentLanguage == _language)
			return;

		m_CurrentLanguage = _language;
		PlayerPrefs.SetInt(c_PlayerPrefsLanguageKey, (int)m_CurrentLanguage);
		PlayerPrefs.Save();
		LanguageChanged?.Invoke();
	}
	#endregion

	#region Private Methods
	private static LocalizationManager EnsureInstance()
	{
		if (s_ApplicationIsQuitting)
			return s_Instance;

		if (s_Instance != null)
			return s_Instance;

		GameObject root = new GameObject(nameof(LocalizationManager));
		s_Instance = root.AddComponent<LocalizationManager>();
		return s_Instance;
	}

	private static void DestroyRuntimeInstance()
	{
		if (s_Instance == null)
			return;

		GameObject root = s_Instance.gameObject;
		s_Instance = null;

		if (root == null)
			return;

		DestroyObjectImmediate(root);
	}

#if UNITY_EDITOR
	private static void DestroyAllLocalizationManagerObjects()
	{
		LocalizationManager[] managers = UnityEngine.Object.FindObjectsByType<LocalizationManager>(
			FindObjectsInactive.Include);

		for (int i = 0; i < managers.Length; i++)
		{
			if (managers[i] == null)
				continue;

			DestroyObjectImmediate(managers[i].gameObject);
		}

		s_Instance = null;
	}
#endif

	private static void DestroyObjectImmediate(GameObject _root)
	{
		if (_root == null)
			return;

#if UNITY_EDITOR
		UnityEngine.Object.DestroyImmediate(_root);
#else
		UnityEngine.Object.Destroy(_root);
#endif
	}

	private string GetInternal(string _key)
	{
		if (string.IsNullOrWhiteSpace(_key))
			return string.Empty;

		if (TryGetValue(m_CurrentLanguage, _key, out string localizedValue))
			return localizedValue;

		if (m_CurrentLanguage != GameLanguage.English && TryGetValue(GameLanguage.English, _key, out localizedValue))
			return localizedValue;

		return _key;
	}

	private bool TryGetValue(GameLanguage _language, string _key, out string _value)
	{
		_value = null;
		if (!m_Tables.TryGetValue(_language, out Dictionary<string, string> table))
			return false;

		return table.TryGetValue(_key, out _value);
	}

	private void RestoreLanguage()
	{
		if (PlayerPrefs.HasKey(c_PlayerPrefsLanguageKey))
		{
			m_CurrentLanguage = (GameLanguage)PlayerPrefs.GetInt(c_PlayerPrefsLanguageKey, (int)GameLanguage.English);
			return;
		}

		m_CurrentLanguage = Application.systemLanguage == SystemLanguage.Russian
			? GameLanguage.Russian
			: GameLanguage.English;
	}

	private void LoadLocalizationTables()
	{
		m_Tables.Clear();

		TextAsset[] assets = Resources.LoadAll<TextAsset>(c_ResourcesPath);
		for (int i = 0; i < assets.Length; i++)
		{
			TextAsset asset = assets[i];
			if (asset == null || string.IsNullOrWhiteSpace(asset.text))
				continue;

			LocalizationFileData fileData = JsonUtility.FromJson<LocalizationFileData>(asset.text);
			if (fileData == null || !TryParseLanguage(fileData.language, out GameLanguage language))
				continue;

			Dictionary<string, string> table = new Dictionary<string, string>(StringComparer.Ordinal);
			if (fileData.entries != null)
			{
				for (int entryIndex = 0; entryIndex < fileData.entries.Length; entryIndex++)
				{
					LocalizationEntryData entry = fileData.entries[entryIndex];
					if (entry == null || string.IsNullOrWhiteSpace(entry.key))
						continue;

					table[entry.key] = entry.value ?? string.Empty;
				}
			}

			m_Tables[language] = table;
		}
	}

	private static bool TryParseLanguage(string _language, out GameLanguage _result)
	{
		_result = GameLanguage.English;
		if (string.IsNullOrWhiteSpace(_language))
			return false;

		switch (_language.Trim().ToLowerInvariant())
		{
			case "en":
			case "eng":
			case "english":
				_result = GameLanguage.English;
				return true;

			case "ru":
			case "rus":
			case "russian":
			case "russianru":
				_result = GameLanguage.Russian;
				return true;

			default:
				return false;
		}
	}
	#endregion

	#region Serialization
	[Serializable]
	private sealed class LocalizationFileData
	{
		public string language;
		public LocalizationEntryData[] entries;
	}

	[Serializable]
	private sealed class LocalizationEntryData
	{
		public string key;
		public string value;
	}
	#endregion
}

public enum GameLanguage
{
	English = 0,
	Russian = 1
}
