using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Пользовательские пресеты поверх встроенных записей каталога: имена, создание, переименование, удаление.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepRuntimePresetRegistry : MonoBehaviour
{
	#region Events
	public event Action RegistryChanged;
	#endregion

	#region Serializable Types
	[Serializable]
	private sealed class UserPresetRecord
	{
		[SerializeField] private string m_DisplayName = "New preset";

		public string DisplayName
		{
			get => m_DisplayName;
			set => m_DisplayName = value;
		}
	}
	#endregion

	#region Static Access
	private static MissionPrepRuntimePresetRegistry s_Instance;

	public static MissionPrepRuntimePresetRegistry Instance => s_Instance;
	#endregion

	#region Serialized Fields
	[SerializeField] private List<UserPresetRecord> m_UserPresets = new List<UserPresetRecord>();
	#endregion

	#region Private Fields
	private int m_BuiltInPresetCount;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (s_Instance != null && s_Instance != this)
		{
			Debug.LogWarning(
				$"{nameof(MissionPrepRuntimePresetRegistry)}: второй экземпляр на «{name}» игнорируется.",
				this);
			return;
		}

		s_Instance = this;
	}

	private void OnDestroy()
	{
		if (s_Instance == this)
			s_Instance = null;
	}
	#endregion

	#region Public Properties
	public int BuiltInPresetCount => Mathf.Max(0, m_BuiltInPresetCount);
	public int UserPresetCount => m_UserPresets != null ? m_UserPresets.Count : 0;
	public int TotalPresetCount => BuiltInPresetCount + UserPresetCount;
	#endregion

	#region Public Methods
	public static MissionPrepRuntimePresetRegistry GetOrCreate(MonoBehaviour _context)
	{
		if (s_Instance != null)
			return s_Instance;

		if (_context != null)
		{
			MissionPrepRuntimePresetRegistry onContext = _context.GetComponent<MissionPrepRuntimePresetRegistry>();
			if (onContext == null)
				onContext = _context.GetComponentInParent<MissionPrepRuntimePresetRegistry>();

			if (onContext != null)
			{
				s_Instance = onContext;
				return s_Instance;
			}
		}

		s_Instance = FindAnyObjectByType<MissionPrepRuntimePresetRegistry>();
		if (s_Instance != null)
			return s_Instance;

		if (_context == null)
			return null;

		s_Instance = _context.gameObject.AddComponent<MissionPrepRuntimePresetRegistry>();
		return s_Instance;
	}

	public void ConfigureBuiltInPresetCount(int _builtInPresetCount)
	{
		m_BuiltInPresetCount = Mathf.Max(0, _builtInPresetCount);
	}

	public void ClearAllUserPresets()
	{
		if (m_UserPresets == null || m_UserPresets.Count == 0)
			return;

		m_UserPresets.Clear();
		RegistryChanged?.Invoke();
	}

	public bool IsUserPreset(int _presetIndex)
	{
		return _presetIndex >= BuiltInPresetCount;
	}

	public bool CanDeletePreset(int _presetIndex)
	{
		return IsUserPreset(_presetIndex) && TotalPresetCount > 1;
	}

	public bool CanRenamePreset(int _presetIndex)
	{
		return IsUserPreset(_presetIndex);
	}

	public string GetPresetDisplayName(int _presetIndex, MissionPrepEquipmentPresetCatalog _catalog)
	{
		if (_presetIndex < 0 || _presetIndex >= TotalPresetCount)
			return string.Empty;

		if (!IsUserPreset(_presetIndex))
		{
			if (_catalog != null)
				return _catalog.GetPresetLabel(_presetIndex);

			return $"Preset {_presetIndex + 1}";
		}

		int userIndex = _presetIndex - BuiltInPresetCount;
		if (m_UserPresets == null || userIndex < 0 || userIndex >= m_UserPresets.Count || m_UserPresets[userIndex] == null)
			return string.Empty;

		return MissionPrepPresetNameUtility.Sanitize(m_UserPresets[userIndex].DisplayName);
	}

	public void CollectAllDisplayNames(MissionPrepEquipmentPresetCatalog _catalog, List<string> _outNames)
	{
		if (_outNames == null)
			return;

		_outNames.Clear();
		for (int i = 0; i < TotalPresetCount; i++)
			_outNames.Add(GetPresetDisplayName(i, _catalog));
	}

	public bool TryCreateUserPreset(string _proposedName, MissionPrepEquipmentPresetCatalog _catalog, out int _newPresetIndex, out string _sanitizedName)
	{
		_newPresetIndex = -1;
		_sanitizedName = string.Empty;

		var namesBuffer = new List<string>();
		CollectAllDisplayNames(_catalog, namesBuffer);

		string defaultBase = LocalizationManager.Get(
			"mission_prep.equipment.preset.new_default",
			"New preset");
		string candidate = string.IsNullOrWhiteSpace(_proposedName)
			? MissionPrepPresetNameUtility.MakeUniqueDefaultName(defaultBase, namesBuffer)
			: _proposedName;

		if (!MissionPrepPresetNameUtility.TryValidate(candidate, namesBuffer, -1, out _sanitizedName, out _))
			return false;

		if (m_UserPresets == null)
			m_UserPresets = new List<UserPresetRecord>();

		m_UserPresets.Add(new UserPresetRecord { DisplayName = _sanitizedName });
		_newPresetIndex = TotalPresetCount - 1;
		RegistryChanged?.Invoke();
		return true;
	}

	public bool TryRenameUserPreset(
		int _presetIndex,
		string _proposedName,
		MissionPrepEquipmentPresetCatalog _catalog,
		out string _sanitizedName)
	{
		_sanitizedName = string.Empty;
		if (!CanRenamePreset(_presetIndex))
			return false;

		var namesBuffer = new List<string>();
		CollectAllDisplayNames(_catalog, namesBuffer);

		if (!MissionPrepPresetNameUtility.TryValidate(_proposedName, namesBuffer, _presetIndex, out _sanitizedName, out _))
			return false;

		int userIndex = _presetIndex - BuiltInPresetCount;
		if (m_UserPresets == null || userIndex < 0 || userIndex >= m_UserPresets.Count || m_UserPresets[userIndex] == null)
			return false;

		m_UserPresets[userIndex].DisplayName = _sanitizedName;
		RegistryChanged?.Invoke();
		return true;
	}

	public bool TryDeleteUserPreset(int _presetIndex)
	{
		if (!CanDeletePreset(_presetIndex))
			return false;

		int userIndex = _presetIndex - BuiltInPresetCount;
		if (m_UserPresets == null || userIndex < 0 || userIndex >= m_UserPresets.Count)
			return false;

		m_UserPresets.RemoveAt(userIndex);
		RegistryChanged?.Invoke();
		return true;
	}

	/// <summary>
	/// Создаёт user-presets до нужного количества слотов (например, по одному на каждого player-юнита).
	/// </summary>
	public void EnsureMinimumPresetCount(
		int _minimumCount,
		MissionPrepEquipmentPresetCatalog _catalog,
		MissionPrepSharedPresetStore _sharedStore)
	{
		_minimumCount = Mathf.Max(0, _minimumCount);
		while (TotalPresetCount < _minimumCount)
		{
			string proposedName = $"Player-{TotalPresetCount + 1:D2}";
			if (!TryCreateUserPreset(proposedName, _catalog, out _, out _))
				break;

			_sharedStore?.EnsurePresetSnapshots(TotalPresetCount);
		}
	}
	#endregion
}
