#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Re-attach the Play Mode weapon pose / hand IK tuner to Unit when you need to retune.
/// Gameplay does not need the tuner on Unit — see <see cref="UnitEquippedWeaponPoseRuntimeTuner"/>.
/// </summary>
public static class UnitEquippedWeaponPoseRuntimeTunerSetup
{
	private const string c_UnitPrefabPath = "Assets/Prefabs/Characters/Unit.prefab";

	[MenuItem("Polygone/Weapons/Add Weapon Pose Runtime Tuner To Unit")]
	public static void AddToUnitPrefab()
	{
		GameObject root = PrefabUtility.LoadPrefabContents(c_UnitPrefabPath);
		if (root == null)
		{
			Debug.LogError($"Missing prefab: {c_UnitPrefabPath}");
			return;
		}

		if (root.GetComponent<UnitEquippedWeaponPoseRuntimeTuner>() == null)
			root.AddComponent<UnitEquippedWeaponPoseRuntimeTuner>();

		PrefabUtility.SaveAsPrefabAsset(root, c_UnitPrefabPath);
		PrefabUtility.UnloadPrefabContents(root);
		AssetDatabase.SaveAssets();
		Debug.Log(
			"Added UnitEquippedWeaponPoseRuntimeTuner to Unit prefab. " +
			"Play Mode → Enable Runtime Tuning → Save To Asset / Save Left IK To Foregrip Prefab. " +
			"Remove the component from Unit when finished.");
	}
}
#endif
