#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Добавляет вторую группу NotReady IK на рукоятку M2 (дети Gun_Handle).
/// Не трогает Left/RightHandIkTarget (Ready) на pitch.
/// </summary>
public static class VehicleTurretIkNotReadySetup
{
	private const string c_VehiclePrefabPath = "Assets/Prefabs/Vehicles/Light_Armored_Car.prefab";
	public const string RightHandHandleIkName = VehicleTurretReloadController.RightHandIkNotReadyHandleName;
	public const string LeftHandHandleIkName = VehicleTurretReloadController.LeftHandIkNotReadyHandleName;

	[MenuItem("Polygone/Vehicles/Add Turret NotReady IK Targets (pitch)")]
	public static void AddPitchNotReadyIkTargets()
	{
		GameObject prefabRoot = PrefabUtility.LoadPrefabContents(c_VehiclePrefabPath);
		if (prefabRoot == null)
		{
			Debug.LogError("[VehicleTurretIkNotReadySetup] Prefab not found.");
			return;
		}

		int added = 0;
		added += EnsureNotReadyPair(FindIkHost(prefabRoot.transform, "GameObjectGun.12.7"));
		added += EnsureNotReadyPair(FindIkHost(prefabRoot.transform, "MK19"));

		PrefabUtility.SaveAsPrefabAsset(prefabRoot, c_VehiclePrefabPath);
		PrefabUtility.UnloadPrefabContents(prefabRoot);
		Debug.Log($"[VehicleTurretIkNotReadySetup] Pitch NotReady pairs added/verified: {added}.");
	}

	[MenuItem("Polygone/Vehicles/Add Turret Handle NotReady IK Targets")]
	public static void AddHandleNotReadyIkTargets()
	{
		GameObject prefabRoot = PrefabUtility.LoadPrefabContents(c_VehiclePrefabPath);
		if (prefabRoot == null)
		{
			Debug.LogError("[VehicleTurretIkNotReadySetup] Prefab not found.");
			return;
		}

		Transform handle = FindDeepChild(prefabRoot.transform, "SM_Veh_Pickup_Technical_01_Gun_Handle");
		int added = EnsureHandleNotReadyPair(handle);

		PrefabUtility.SaveAsPrefabAsset(prefabRoot, c_VehiclePrefabPath);
		PrefabUtility.UnloadPrefabContents(prefabRoot);
		Debug.Log(
			$"[VehicleTurretIkNotReadySetup] Handle NotReady IK under Gun_Handle: added {added} " +
			$"(Ready IK on pitch untouched).");
	}

	/// <summary>Runtime-safe: create handle NotReady empties if missing. Never moves Ready IK.</summary>
	public static int EnsureHandleNotReadyIkRuntime(Transform _handle)
	{
		return EnsureHandleNotReadyPair(_handle);
	}

	private static Transform FindIkHost(Transform _root, string _weaponRootName)
	{
		Transform weaponRoot = FindDeepChild(_root, _weaponRootName);
		if (weaponRoot == null)
			return null;

		Transform leftReady = weaponRoot.Find("LeftHandIkTarget");
		return leftReady != null ? leftReady.parent : weaponRoot;
	}

	private static Transform FindDeepChild(Transform _root, string _name)
	{
		if (_root == null)
			return null;

		Transform[] all = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < all.Length; i++)
		{
			if (all[i].name == _name)
				return all[i];
		}

		return null;
	}

	private static int EnsureNotReadyPair(Transform _host)
	{
		if (_host == null)
			return 0;

		int count = 0;
		Transform leftReady = _host.Find("LeftHandIkTarget");
		Transform rightReady = _host.Find("RightHandIkTarget");
		if (leftReady != null && EnsureNotReadyChild(_host, leftReady, "LeftHandIkTarget_NotReady"))
			count++;
		if (rightReady != null && EnsureNotReadyChild(_host, rightReady, "RightHandIkTarget_NotReady"))
			count++;
		return count;
	}

	private static int EnsureHandleNotReadyPair(Transform _handle)
	{
		if (_handle == null)
			return 0;

		int count = 0;
		if (EnsureEmptyChild(_handle, LeftHandHandleIkName))
			count++;
		if (EnsureEmptyChild(_handle, RightHandHandleIkName))
			count++;
		return count;
	}

	private static bool EnsureNotReadyChild(Transform _parent, Transform _ready, string _name)
	{
		Transform existing = _parent.Find(_name);
		if (existing != null)
			return false;

		GameObject go = new GameObject(_name);
		Transform t = go.transform;
		t.SetParent(_parent, false);
		t.localPosition = _ready.localPosition;
		t.localRotation = _ready.localRotation;
		t.localScale = Vector3.one;
		return true;
	}

	private static bool EnsureEmptyChild(Transform _parent, string _name)
	{
		if (_parent == null || string.IsNullOrEmpty(_name))
			return false;

		Transform existing = _parent.Find(_name);
		if (existing != null)
			return false;

		GameObject go = new GameObject(_name);
		Transform t = go.transform;
		t.SetParent(_parent, false);
		t.localPosition = Vector3.zero;
		t.localRotation = Quaternion.identity;
		t.localScale = Vector3.one;
		return true;
	}
}
#endif
