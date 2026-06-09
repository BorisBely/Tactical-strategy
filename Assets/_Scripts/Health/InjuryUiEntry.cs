using System;
using System.Collections.Generic;
using System.Linq;

[Serializable]
public struct InjuryUiEntry
{
	public string StatusDisplayName;
	public string StatusLocalizationKey;
	public string ConditionDisplayName;
	public string ConditionLocalizationKey;
	public string DescriptionDisplayName;
	public string DescriptionLocalizationKey;
	public string[] DebuffLocalizationKeys;
	public string DebuffsDisplayText;
	public int SortPriority;

	public HealthStatusEntryData ToEntryData()
	{
		return new HealthStatusEntryData
		{
			StatusDisplayName = StatusDisplayName,
			StatusLocalizationKey = StatusLocalizationKey,
			ConditionDisplayName = ConditionDisplayName,
			ConditionLocalizationKey = ConditionLocalizationKey,
			DescriptionDisplayName = DescriptionDisplayName,
			DescriptionLocalizationKey = DescriptionLocalizationKey,
			DebuffLocalizationKeys = DebuffLocalizationKeys,
			DebuffsDisplayText = DebuffsDisplayText,
			SortPriority = SortPriority
		};
	}

	public static InjuryUiEntry FromLocalizedKeys(
		string _statusKey,
		string _conditionKey = null,
		string _descriptionKey = null,
		string[] _debuffKeys = null,
		int _sortPriority = 100)
	{
		return new InjuryUiEntry
		{
			StatusLocalizationKey = _statusKey,
			ConditionLocalizationKey = _conditionKey,
			DescriptionLocalizationKey = _descriptionKey,
			DebuffLocalizationKeys = _debuffKeys,
			SortPriority = _sortPriority
		};
	}
}

public static class InjuryUiEntryUtility
{
	public static IReadOnlyList<InjuryUiEntry> SortByPriority(IEnumerable<InjuryUiEntry> _entries)
	{
		if (_entries == null)
			return Array.Empty<InjuryUiEntry>();

		return _entries
			.OrderBy(_entry => _entry.SortPriority)
			.ThenBy(_entry => _entry.GetLocalizedStatusText())
			.ToList();
	}
}

internal static class InjuryUiEntryLocalizationExtensions
{
	public static string GetLocalizedStatusText(this InjuryUiEntry _entry)
	{
		if (!string.IsNullOrWhiteSpace(_entry.StatusLocalizationKey))
			return LocalizationManager.Get(_entry.StatusLocalizationKey, _entry.StatusDisplayName);

		return _entry.StatusDisplayName ?? string.Empty;
	}

	public static string GetLocalizedConditionText(this InjuryUiEntry _entry)
	{
		if (!string.IsNullOrWhiteSpace(_entry.ConditionLocalizationKey))
			return LocalizationManager.Get(_entry.ConditionLocalizationKey, _entry.ConditionDisplayName);

		return _entry.ConditionDisplayName ?? string.Empty;
	}
}
