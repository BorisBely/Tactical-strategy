#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Copies authored right-hand HipFireWalk / HipFireCrouchWalk IK from Equipped_AK47_0
/// onto other equipped rifle/LMG prefabs. Other poses and left hand are left untouched.
/// </summary>
[InitializeOnLoad]
public static class CopyAk47HipFireWalkRightHand
{
	private const string c_PendingFlag = "Assets/.copy_ak47_hipfire_walk_hands_pending";
	private const string c_SourcePath = "Assets/Prefabs/Weapons/AK/Equipped/Equipped_AK47_0.prefab";

	private static readonly string[] c_EquippedRoots =
	{
		"Assets/Prefabs/Weapons/AK/Equipped",
		"Assets/Prefabs/Weapons/M4/Equipped",
		"Assets/Prefabs/Weapons/Standalone/Equipped",
	};

	static CopyAk47HipFireWalkRightHand()
	{
		EditorApplication.delayCall += TryRunPending;
	}

	[MenuItem("Polygone/Weapons/GripRig/Copy AK47 HipFire walk right hand to all guns")]
	public static void CopyMenu()
	{
		int count = CopyAll();
		EditorUtility.DisplayDialog(
			"HipFire walk right hand",
			$"Скопировано Standing/HipFireWalk и Crouch/HipFireCrouchWalk с AK47_0 на {count} префабов.",
			"OK");
	}

	private static void TryRunPending()
	{
		if (!File.Exists(c_PendingFlag))
			return;

		File.Delete(c_PendingFlag);
		int count = CopyAll();
		Debug.Log($"[CopyAk47HipFireWalkRightHand] Copied walk right-hand slots to {count} prefabs.");
	}

	public static int CopyAll()
	{
		if (!TryReadSource(out Vector3 walkPos, out Quaternion walkRot, out Vector3 crouchPos, out Quaternion crouchRot))
		{
			Debug.LogError("[CopyAk47HipFireWalkRightHand] Нет Standing/HipFireWalk или Crouch/HipFireCrouchWalk на AK47_0.");
			return 0;
		}

		int updated = 0;
		foreach (string root in c_EquippedRoots)
		{
			string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { root });
			foreach (string guid in guids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (path.Replace('\\', '/') == c_SourcePath)
					continue;
				if (!path.Contains("/Equipped_"))
					continue;
				if (ApplyToPrefab(path, walkPos, walkRot, crouchPos, crouchRot))
					updated++;
			}
		}

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		return updated;
	}

	private static bool TryReadSource(
		out Vector3 _walkPos,
		out Quaternion _walkRot,
		out Vector3 _crouchPos,
		out Quaternion _crouchRot)
	{
		_walkPos = Vector3.zero;
		_walkRot = Quaternion.identity;
		_crouchPos = Vector3.zero;
		_crouchRot = Quaternion.identity;

		GameObject root = PrefabUtility.LoadPrefabContents(c_SourcePath);
		if (root == null)
			return false;

		try
		{
			Transform right = FindRightHandRoot(root);
			if (right == null)
				return false;
			Transform standingWalk = right.Find(WeaponGripRig.StandingName + "/" + WeaponGripRig.HipFireWalkName);
			Transform crouchWalk = right.Find(WeaponGripRig.CrouchName + "/" + WeaponGripRig.HipFireCrouchWalkName);
			if (standingWalk == null || crouchWalk == null)
				return false;

			_walkPos = standingWalk.localPosition;
			_walkRot = standingWalk.localRotation;
			_crouchPos = crouchWalk.localPosition;
			_crouchRot = crouchWalk.localRotation;
			return true;
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(root);
		}
	}

	private static bool ApplyToPrefab(
		string _path,
		Vector3 _walkPos,
		Quaternion _walkRot,
		Vector3 _crouchPos,
		Quaternion _crouchRot)
	{
		GameObject root = PrefabUtility.LoadPrefabContents(_path);
		if (root == null)
			return false;

		try
		{
			WeaponGripRig grip = root.GetComponentInChildren<WeaponGripRig>(true);
			if (grip == null)
				return false;

			Transform right = FindRightHandRoot(root);
			if (right == null)
				return false;

			Transform standing = EnsureChild(right, WeaponGripRig.StandingName);
			Transform crouch = EnsureChild(right, WeaponGripRig.CrouchName);
			Transform vehicle = EnsureChild(right, WeaponGripRig.VehicleName);

			Transform standingWalk = EnsureChild(standing, WeaponGripRig.HipFireWalkName);
			standingWalk.localPosition = _walkPos;
			standingWalk.localRotation = _walkRot;

			Transform crouchWalk = EnsureChild(crouch, WeaponGripRig.HipFireCrouchWalkName);
			crouchWalk.localPosition = _crouchPos;
			crouchWalk.localRotation = _crouchRot;

			SeedFromHipFireIfNew(standing, WeaponGripRig.HipFireCrouchWalkName);
			SeedFromHipFireIfNew(crouch, WeaponGripRig.HipFireWalkName);
			SeedFromHipFireIfNew(vehicle, WeaponGripRig.HipFireWalkName);
			SeedFromHipFireIfNew(vehicle, WeaponGripRig.HipFireCrouchWalkName);

			grip.SetHipFireWalkPoseTargets(
				standing.Find(WeaponGripRig.HipFireWalkName),
				crouch.Find(WeaponGripRig.HipFireWalkName),
				vehicle.Find(WeaponGripRig.HipFireWalkName));
			grip.SetHipFireCrouchWalkPoseTargets(
				standing.Find(WeaponGripRig.HipFireCrouchWalkName),
				crouch.Find(WeaponGripRig.HipFireCrouchWalkName),
				vehicle.Find(WeaponGripRig.HipFireCrouchWalkName));

			PrefabUtility.SaveAsPrefabAsset(root, _path);
			return true;
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(root);
		}
	}

	private static void SeedFromHipFireIfNew(Transform _stance, string _slotName)
	{
		if (_stance == null)
			return;
		Transform slot = _stance.Find(_slotName);
		Transform hip = _stance.Find(WeaponGripRig.HipFireName);
		if (slot != null)
			return;
		slot = EnsureChild(_stance, _slotName);
		if (hip == null)
			return;
		slot.localPosition = hip.localPosition;
		slot.localRotation = hip.localRotation;
	}

	private static Transform FindRightHandRoot(GameObject _root)
	{
		WeaponGripRig grip = _root.GetComponentInChildren<WeaponGripRig>(true);
		if (grip != null && grip.RightHandIkRoot != null)
			return grip.RightHandIkRoot;
		return FindNamed(_root.transform, WeaponGripRig.RightHandIkRootName)
		       ?? FindNamed(_root.transform, WeaponGripRig.RightHandRootName);
	}

	private static Transform EnsureChild(Transform _parent, string _name)
	{
		Transform existing = _parent.Find(_name);
		if (existing != null)
			return existing;
		var go = new GameObject(_name);
		Transform t = go.transform;
		t.SetParent(_parent, false);
		t.localPosition = Vector3.zero;
		t.localRotation = Quaternion.identity;
		t.localScale = Vector3.one;
		return t;
	}

	private static Transform FindNamed(Transform _root, string _name)
	{
		foreach (Transform t in _root.GetComponentsInChildren<Transform>(true))
		{
			if (t != _root && t.name == _name)
				return t;
		}

		return null;
	}
}
#endif
