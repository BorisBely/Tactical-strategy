#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public static class UnitCharacterMaterialPaletteBuilder
{
	[MenuItem("Polygone/Character/Build Unit Material Palette")]
	public static void BuildUnitMaterialPalette()
	{
		const string resourcesPath = "Assets/Resources/Character/UnitCharacterMaterialPalette.asset";
		EnsureDirectory("Assets/Resources/Character");
		EnsureDirectory("Assets/GameData/Character");

		UnitCharacterMaterialPalette palette = AssetDatabase.LoadAssetAtPath<UnitCharacterMaterialPalette>(resourcesPath);
		if (palette == null)
		{
			palette = ScriptableObject.CreateInstance<UnitCharacterMaterialPalette>();
			AssetDatabase.CreateAsset(palette, resourcesPath);
		}

		for (int patternIndex = 0; patternIndex < UnitCamouflagePatternUtility.PatternCount; patternIndex++)
		{
			var pattern = UnitCamouflagePatternUtility.FromIndex(patternIndex);
			Material[] row = new Material[3];
			row[0] = LoadMaterial(pattern, UnitSkinTone.Light);
			row[1] = LoadMaterial(pattern, UnitSkinTone.Medium);
			row[2] = LoadMaterial(pattern, UnitSkinTone.Dark);
			palette.SetRow(pattern, row);
		}

		EditorUtility.SetDirty(palette);
		AssetDatabase.SaveAssets();

		string gameDataPath = UnitCharacterMaterialPalette.DefaultAssetPath;
		if (!File.Exists(gameDataPath))
		{
			AssetDatabase.CopyAsset(resourcesPath, gameDataPath);
			AssetDatabase.SaveAssets();
		}

		Debug.Log($"[UnitCharacterMaterialPaletteBuilder] Palette saved to {resourcesPath}");
	}

	private static Material LoadMaterial(UnitCamouflagePattern _pattern, UnitSkinTone _skinTone)
	{
		string assetName = UnitCamouflagePatternUtility.BuildMaterialAssetName(_pattern, _skinTone);
		string path = $"Assets/PolygonMilitary/Materials/{assetName}.mat";
		Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
		if (material == null)
			Debug.LogWarning($"[UnitCharacterMaterialPaletteBuilder] Material not found: {path}");

		return material;
	}

	private static void EnsureDirectory(string _path)
	{
		if (!AssetDatabase.IsValidFolder(_path))
		{
			string parent = Path.GetDirectoryName(_path)?.Replace('\\', '/');
			string folderName = Path.GetFileName(_path);
			if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(folderName))
				AssetDatabase.CreateFolder(parent, folderName);
		}
	}
}
#endif
