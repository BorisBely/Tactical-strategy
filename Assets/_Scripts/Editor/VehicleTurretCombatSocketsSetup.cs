#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Восстанавливает M2/MK19 combat sockets и EquippedWeapon на Light_Armored_Car.
/// Не перезаписывает уже настроенные позиции сокетов и ссылки EquippedWeapon.
/// </summary>
public static class VehicleTurretCombatSocketsSetup
{
	private const string c_VehiclePrefabPath = "Assets/Prefabs/Vehicles/Light_Armored_Car.prefab";

	[MenuItem("Polygone/Vehicles/Setup Turret EquippedWeapon (M2 + MK19)")]
	public static void SetupTurretEquippedWeaponsOnLightArmoredCar()
	{
		RestoreOnLightArmoredCarPrefab(includeMk19: true);
	}

	[MenuItem("Tools/Vehicle/Turret/Restore M2 Combat Sockets (Light Armored Car)")]
	public static void RestoreOnLightArmoredCarPrefabMenu()
	{
		RestoreOnLightArmoredCarPrefab(includeMk19: false);
	}

	public static void RestoreOnLightArmoredCarPrefab(bool includeMk19 = false)
	{
		GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(c_VehiclePrefabPath);
		if (prefabRoot == null)
		{
			EditorUtility.DisplayDialog("Combat sockets", $"Prefab not found:\n{c_VehiclePrefabPath}", "OK");
			return;
		}

		GameObject prefabContents = PrefabUtility.LoadPrefabContents(c_VehiclePrefabPath);
		try
		{
			bool changed = false;
			changed |= EnsurePitchEquippedWeapon(
				prefabContents.transform,
				VehicleTurretHierarchyBinder.Gun127ObjectName,
				TurretWeaponVariant.Browning127);
			if (includeMk19)
			{
				changed |= EnsurePitchEquippedWeapon(
					prefabContents.transform,
					"MK19",
					TurretWeaponVariant.Mk19);
			}

			if (!changed)
			{
				Debug.Log(
					$"[{nameof(VehicleTurretCombatSocketsSetup)}] '{c_VehiclePrefabPath}' already has turret EquippedWeapon setup.",
					prefabRoot);
				return;
			}

			EditorUtility.SetDirty(prefabContents);
			PrefabUtility.SaveAsPrefabAsset(prefabContents, c_VehiclePrefabPath);
			Debug.Log(
				$"[{nameof(VehicleTurretCombatSocketsSetup)}] Updated '{c_VehiclePrefabPath}' " +
				$"(M2=yes, MK19={(includeMk19 ? "yes" : "skipped")}).",
				prefabRoot);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(prefabContents);
		}
	}

	public static bool EnsurePitchEquippedWeapon(
		Transform _root,
		string _pitchName,
		TurretWeaponVariant _variant)
	{
		Transform pitch = FindPitchTransform(_root, _pitchName);
		if (pitch == null)
		{
			Debug.LogWarning(
				$"[{nameof(VehicleTurretCombatSocketsSetup)}] Pitch '{_pitchName}' not found.",
				_root);
			return false;
		}

		bool changed = _variant == TurretWeaponVariant.Mk19
			? VehicleTurretCombatSockets.EnsureMissingMk19SocketsOnPitch(pitch)
			: VehicleTurretCombatSockets.EnsureMissingM2SocketsOnPitch(pitch);

		if (!pitch.TryGetComponent(out EquippedWeapon weapon))
		{
			weapon = pitch.gameObject.AddComponent<EquippedWeapon>();
			changed = true;
		}

		changed |= WireEquippedWeaponRefs(weapon, pitch, _variant);
		return changed;
	}

	private static bool WireEquippedWeaponRefs(
		EquippedWeapon _weapon,
		Transform _pitch,
		TurretWeaponVariant _variant)
	{
		if (_weapon == null || _pitch == null)
			return false;

		SerializedObject so = new SerializedObject(_weapon);
		SerializedProperty barrelProp = so.FindProperty("m_Barrel");
		SerializedProperty shellProp = so.FindProperty("m_ShellEject");
		bool changed = false;

		if (barrelProp.objectReferenceValue == null)
		{
			Transform muzzle = VehicleTurretCombatSockets.FindMuzzleExit(_pitch);
			if (muzzle != null)
			{
				barrelProp.objectReferenceValue = muzzle;
				changed = true;
			}
		}

		if (shellProp.objectReferenceValue == null)
		{
			Transform shell = _variant == TurretWeaponVariant.Mk19
				? VehicleTurretCombatSockets.FindMk19ShellEject(_pitch)
				: VehicleTurretCombatSockets.FindShellEject(_pitch);
			if (shell != null)
			{
				shellProp.objectReferenceValue = shell;
				changed = true;
			}
		}

		if (changed)
			so.ApplyModifiedPropertiesWithoutUndo();

		changed |= VehicleTurretCombatSockets.TryWireEquippedWeaponIfMissing(_weapon, _pitch);
		return changed;
	}

	private static Transform FindPitchTransform(Transform _root, string _pitchName)
	{
		if (_root == null || string.IsNullOrEmpty(_pitchName))
			return null;

		Transform[] all = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < all.Length; i++)
		{
			if (all[i] != null && all[i].name == _pitchName)
				return all[i];
		}

		return null;
	}

	[MenuItem("Tools/Vehicle/Fix Inspector (clear stale selection)")]
	public static void ClearStaleInspectorSelection()
	{
		EditorSelectionGuard.FixStaleInspectorEditors();
		Selection.activeObject = null;
		Debug.Log("[Vehicle] Inspector selection cleared. Re-select Light_Armored_Car if needed.");
	}
}
#endif

/// <summary>Batch: -executeMethod VehicleTurretCombatSocketsSetupRunner.Run</summary>
public static class VehicleTurretCombatSocketsSetupRunner
{
	public static void Run()
	{
		VehicleTurretCombatSocketsSetup.SetupTurretEquippedWeaponsOnLightArmoredCar();
		EditorApplication.Exit(0);
	}
}
