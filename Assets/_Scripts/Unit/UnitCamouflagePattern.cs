using UnityEngine;

/// <summary>
/// Палитра камуфляжа Polygon (01–04). A/B/C на материале — оттенок кожи, не камуфляж.
/// </summary>
public enum UnitCamouflagePattern
{
	Desert = 0,
	Urban = 1,
	Forest = 2,
	Jungle = 3
}

public static class UnitCamouflagePatternUtility
{
	public const int PatternCount = 4;

	public static int ClampIndex(int _index)
	{
		return Mathf.Clamp(_index, 0, PatternCount - 1);
	}

	public static UnitCamouflagePattern FromIndex(int _index)
	{
		return (UnitCamouflagePattern)ClampIndex(_index);
	}

	public static string GetLocalizationKey(UnitCamouflagePattern _pattern)
	{
		return _pattern switch
		{
			UnitCamouflagePattern.Urban => "mission_prep.equipment.camouflage.urban",
			UnitCamouflagePattern.Forest => "mission_prep.equipment.camouflage.forest",
			UnitCamouflagePattern.Jungle => "mission_prep.equipment.camouflage.jungle",
			_ => "mission_prep.equipment.camouflage.desert"
		};
	}

	public static string GetLocalizedLabel(UnitCamouflagePattern _pattern)
	{
		return LocalizationManager.Get(GetLocalizationKey(_pattern), _pattern.ToString());
	}

	public static string GetDescriptionLocalizationKey(UnitCamouflagePattern _pattern)
	{
		return GetLocalizationKey(_pattern) + ".description";
	}

	public static string GetLocalizedDescription(UnitCamouflagePattern _pattern)
	{
		return LocalizationManager.Get(GetDescriptionLocalizationKey(_pattern), string.Empty);
	}

	public static int GetPaletteNumber(UnitCamouflagePattern _pattern)
	{
		return ClampIndex((int)_pattern) + 1;
	}

	public static char GetSkinToneSuffix(UnitSkinTone _skinTone)
	{
		return _skinTone switch
		{
			UnitSkinTone.Medium => 'B',
			UnitSkinTone.Dark => 'C',
			_ => 'A'
		};
	}

	public static string BuildMaterialAssetName(UnitCamouflagePattern _pattern, UnitSkinTone _skinTone)
	{
		return $"PolygonMilitary_Mat_{GetPaletteNumber(_pattern):00}_{GetSkinToneSuffix(_skinTone)}";
	}
}
