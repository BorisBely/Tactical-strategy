#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Восстанавливает M2 combat sockets и (при необходимости) компоненты турели на Light_Armored_Car.
/// Не перезаписывает уже настроенные позиции сокетов и ссылки EquippedWeapon.
/// </summary>
public static class VehicleTurretCombatSocketsSetup
{
	private const string c_VehiclePrefabPath = "Assets/Prefabs/Vehicles/Light_Armored_Car.prefab";

	[MenuItem("Tools/Vehicle/Turret/Restore M2 Combat Sockets (Light Armored Car)")]
	public static void RestoreOnLightArmoredCarPrefab()
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
			Transform pitch = FindM2PitchTransform(prefabContents.transform);
			if (pitch == null)
			{
				EditorUtility.DisplayDialog("Combat sockets", "GameObjectGun.12.7 not found in prefab hierarchy.", "OK");
				return;
			}

			Undo.RegisterFullObjectHierarchyUndo(prefabContents, "Restore M2 combat sockets");

			bool createdSockets = VehicleTurretCombatSockets.EnsureMissingM2SocketsOnPitch(pitch);
			bool addedWeaponComponent = false;
			bool wiredWeaponRefs = false;

			if (!pitch.TryGetComponent(out EquippedWeapon weapon))
			{
				weapon = Undo.AddComponent<EquippedWeapon>(pitch.gameObject);
				addedWeaponComponent = true;
			}

			SerializedObject so = new SerializedObject(weapon);
			SerializedProperty barrelProp = so.FindProperty("m_Barrel");
			SerializedProperty shellProp = so.FindProperty("m_ShellEject");

			if (barrelProp.objectReferenceValue == null)
			{
				Transform muzzle = VehicleTurretCombatSockets.FindMuzzleExit(pitch);
				if (muzzle != null)
				{
					barrelProp.objectReferenceValue = muzzle;
					wiredWeaponRefs = true;
				}
			}

			if (shellProp.objectReferenceValue == null)
			{
				Transform shell = VehicleTurretCombatSockets.FindShellEject(pitch);
				if (shell != null)
				{
					shellProp.objectReferenceValue = shell;
					wiredWeaponRefs = true;
				}
			}

			if (wiredWeaponRefs)
				so.ApplyModifiedPropertiesWithoutUndo();

			bool changed = createdSockets || addedWeaponComponent || wiredWeaponRefs;
			if (!changed)
			{
				Debug.Log(
					$"[{nameof(VehicleTurretCombatSocketsSetup)}] '{c_VehiclePrefabPath}' already has M2 combat sockets.",
					prefabRoot);
				return;
			}

			EditorUtility.SetDirty(prefabContents);
			PrefabUtility.SaveAsPrefabAsset(prefabContents, c_VehiclePrefabPath);
			Debug.Log(
				$"[{nameof(VehicleTurretCombatSocketsSetup)}] Updated '{c_VehiclePrefabPath}': " +
				$"sockets={(createdSockets ? "created missing" : "ok")}, " +
				$"equippedWeapon={(addedWeaponComponent ? "added" : "ok")}, " +
				$"wiredRefs={(wiredWeaponRefs ? "filled empty" : "ok")}. " +
				"Turret runtime components are added by VehicleController at play mode.",
				prefabRoot);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(prefabContents);
		}
	}

	private static Transform FindM2PitchTransform(Transform _root)
	{
		if (_root == null)
			return null;

		Transform[] all = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < all.Length; i++)
		{
			if (all[i] != null && all[i].name == VehicleTurretHierarchyBinder.Gun127ObjectName)
				return all[i];
		}

		return null;
	}
	[MenuItem("Tools/Vehicle/Fix Inspector (clear stale selection)")]
	public static void ClearStaleInspectorSelection()
	{
		EditorSelectionGuard.FixStaleInspectorEditors();
		Debug.Log("[Vehicle] Inspector selection cleared. Re-select Light_Armored_Car if needed.");
	}
}

/// <summary>Batch: -executeMethod VehicleTurretCombatSocketsSetupRunner.Run</summary>
public static class VehicleTurretCombatSocketsSetupRunner
{
	public static void Run()
	{
		VehicleTurretCombatSocketsSetup.RestoreOnLightArmoredCarPrefab();
		EditorApplication.Exit(0);
	}
}
#endif
