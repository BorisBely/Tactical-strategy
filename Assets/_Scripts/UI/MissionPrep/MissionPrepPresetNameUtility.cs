using System;
using System.Collections.Generic;

/// <summary>
/// Ограничения и нормализация имён пресетов снаряжения.
/// </summary>
public static class MissionPrepPresetNameUtility
{
	#region Constants
	public const int MinLength = 1;
	public const int MaxLength = 32;
	#endregion

	#region Public Methods
	public static string Sanitize(string _raw)
	{
		if (string.IsNullOrEmpty(_raw))
			return string.Empty;

		string trimmed = _raw.Trim();
		if (trimmed.Length > MaxLength)
			trimmed = trimmed.Substring(0, MaxLength);

		return trimmed;
	}

	public static bool TryValidate(string _raw, IReadOnlyList<string> _existingNames, int _ignoreIndex, out string _sanitized, out string _errorKey)
	{
		_sanitized = Sanitize(_raw);
		_errorKey = string.Empty;

		if (_sanitized.Length < MinLength)
		{
			_errorKey = "mission_prep.equipment.preset_name.error.empty";
			return false;
		}

		if (ContainsDuplicateName(_sanitized, _existingNames, _ignoreIndex))
		{
			_errorKey = "mission_prep.equipment.preset_name.error.duplicate";
			return false;
		}

		return true;
	}

	public static string MakeUniqueDefaultName(string _baseName, IReadOnlyList<string> _existingNames)
	{
		string baseName = Sanitize(_baseName);
		if (string.IsNullOrEmpty(baseName))
			baseName = "Preset";

		if (!ContainsDuplicateName(baseName, _existingNames, -1))
			return baseName;

		for (int i = 2; i < 1000; i++)
		{
			string suffix = $" ({i})";
			int maxBaseLength = MaxLength - suffix.Length;
			string truncatedBase = baseName.Length > maxBaseLength
				? baseName.Substring(0, maxBaseLength)
				: baseName;
			string candidate = truncatedBase + suffix;
			if (!ContainsDuplicateName(candidate, _existingNames, -1))
				return candidate;
		}

		return baseName.Substring(0, Math.Min(baseName.Length, MaxLength));
	}

	public static bool NamesEqual(string _left, string _right)
	{
		return string.Equals(Sanitize(_left), Sanitize(_right), StringComparison.OrdinalIgnoreCase);
	}
	#endregion

	#region Private Methods
	private static bool ContainsDuplicateName(string _candidate, IReadOnlyList<string> _existingNames, int _ignoreIndex)
	{
		if (_existingNames == null || _existingNames.Count == 0)
			return false;

		for (int i = 0; i < _existingNames.Count; i++)
		{
			if (i == _ignoreIndex)
				continue;

			if (NamesEqual(_candidate, _existingNames[i]))
				return true;
		}

		return false;
	}
	#endregion
}
