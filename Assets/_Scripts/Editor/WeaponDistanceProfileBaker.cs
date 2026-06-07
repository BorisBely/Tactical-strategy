#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Запекает дистанционные кривые оружия и таблицу авто-очереди из <see cref="WeaponDistanceCurveLibrary"/>.
/// </summary>
public static class WeaponDistanceProfileBaker
{
	private const string c_ShootingRoot = "Assets/GameData/Shooting";

	[MenuItem("Polygone/Combat Balance/Bake Weapon Distance Profiles")]
	public static void BakeAllWeaponProfiles()
	{
		string[] guids = AssetDatabase.FindAssets("t:WeaponDefinition", new[] { c_ShootingRoot });
		int baked = 0;

		for (int i = 0; i < guids.Length; i++)
		{
			string path = AssetDatabase.GUIDToAssetPath(guids[i]);
			var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
			if (weapon == null)
				continue;

			WeaponDistanceCurveLibrary.ApplyToWeapon(weapon);
			EditorUtility.SetDirty(weapon);
			baked++;
		}

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log($"Weapon combat balance baked for {baked} weapon definitions.");
	}
}
#endif
