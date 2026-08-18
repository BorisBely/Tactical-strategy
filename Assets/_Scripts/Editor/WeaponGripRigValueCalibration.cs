#if UNITY_EDITOR
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Calibrates GripRig locals from mesh + legacy IK targets (values only, not IK code).
/// Weapon ItemDefinition poses are left untouched — calibrate those via the Play Mode tuner.
/// Menu: Polygone/Weapons/GripRig/Calibrate Grip Values From Mesh+Legacy
/// </summary>
public static class WeaponGripRigValueCalibration
{
	private static readonly string[] c_EquippedRoots =
	{
		"Assets/Prefabs/Weapons/M4/Equipped",
		"Assets/Prefabs/Weapons/AK/Equipped",
		"Assets/Prefabs/Weapons/Standalone/Equipped"
	};

	private static readonly Vector3 c_M4RightEu = new Vector3(8.034843f, 7.1254754f, 263.0723f);
	private static readonly Vector3 c_M4LeftEu = new Vector3(1.41893f, 44.277588f, 163.04305f);
	private static readonly Vector3 c_AkRightEu = new Vector3(347.7769f, 328.60794f, 265.94165f);
	private static readonly Vector3 c_AkLeftEu = new Vector3(19.565f, 33.029f, 143.007f);

	[MenuItem("Polygone/Weapons/GripRig/Calibrate Grip Values From Mesh+Legacy")]
	public static void CalibrateMenu()
	{
		int count = CalibrateAll();
		EditorUtility.DisplayDialog(
			"GripRig Value Calibration",
			$"Updated {count} equipped prefabs.\nItemDefinition weapon poses were not modified.",
			"OK");
	}

	public static int CalibrateAll()
	{
		int updated = 0;
		var report = new StringBuilder(4096);

		foreach (string root in c_EquippedRoots)
		{
			string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { root });
			foreach (string guid in guids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (!path.Contains("/Equipped_"))
					continue;
				if (CalibratePrefab(path, report))
					updated++;
			}
		}

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();

		string outPath = "Assets/_DebugLogs/WeaponPoseCalibration/calibrate_grip_values_editor_report.txt";
		System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outPath) ?? "Assets/_DebugLogs");
		System.IO.File.WriteAllText(outPath, report.ToString());
		Debug.Log($"[GripRigValueCalibration] Updated {updated} prefabs. Report: {outPath}");
		return updated;
	}

	private static bool CalibratePrefab(string _path, StringBuilder _report)
	{
		GameObject root = PrefabUtility.LoadPrefabContents(_path);
		if (root == null)
			return false;

		bool changed = false;
		try
		{
			WeaponGripRig gripRig = root.GetComponent<WeaponGripRig>();
			if (gripRig == null)
				gripRig = root.AddComponent<WeaponGripRig>();

			Transform gripRoot = EnsureChild(root.transform, WeaponGripRig.GripRigChildName);
			Transform rightGrip = EnsureChild(gripRoot, WeaponGripRig.RightHandGripName);
			Transform leftGrip = EnsureChild(gripRoot, WeaponGripRig.LeftHandGripName);
			gripRig.SetGrips(rightGrip, leftGrip);

			string fam = FamilyOf(_path);
			_report.AppendLine($"=== {_path} ({fam}) ===");

			Transform gripMesh = FindNameRegex(root.transform, @"SM_Wep.*Grip_\d+|.*_Grip_\d+");
			Transform handguard = FindNameRegex(root.transform, @"SM_Wep.*Handguard_Lower|.*Handguard_Lower|SM_Wep.*Handguard");
			Transform legacyLeft = FindChildRecursive(root.transform, "LeftHandIkTarget");

			Vector3 rPos = rightGrip.localPosition;
			Vector3 rEu = rightGrip.localEulerAngles;
			Vector3 lPos = leftGrip.localPosition;
			Vector3 lEu = leftGrip.localEulerAngles;

			if (fam == "ak")
			{
				if (IsNearIdentityEuler(rEu))
					rEu = c_AkRightEu;
				Vector3 meshLocal = Vector3.zero;
				bool hasMesh = gripMesh != null;
				if (hasMesh)
				{
					meshLocal = root.transform.InverseTransformPoint(gripMesh.position);
					rPos = meshLocal + new Vector3(0.055f, -0.005f, -0.04f);
					_report.AppendLine($"  RightHandGrip from mesh {gripMesh.name} -> {rPos} eu={rEu}");
				}

				bool hgForward = false;
				Vector3 hg = Vector3.zero;
				if (handguard != null)
				{
					hg = root.transform.InverseTransformPoint(handguard.position);
					hgForward = hasMesh && Mathf.Abs(hg.z - meshLocal.z) > 0.05f;
				}

				if (hgForward)
				{
					lPos = hg + new Vector3(-0.08f, 0.01f, 0.03f);
					lEu = legacyLeft != null && !IsNearIdentityEuler(legacyLeft.localEulerAngles)
						? (Quaternion.Inverse(root.transform.rotation) * legacyLeft.rotation).eulerAngles
						: c_AkLeftEu;
					_report.AppendLine($"  LeftHandGrip from handguard {handguard.name} -> {lPos}");
				}
				else if (legacyLeft != null && !IsNearIdentityEuler(legacyLeft.localEulerAngles))
				{
					lPos = root.transform.InverseTransformPoint(legacyLeft.position);
					lEu = (Quaternion.Inverse(root.transform.rotation) * legacyLeft.rotation).eulerAngles;
					_report.AppendLine($"  LeftHandGrip from LeftHandIkTarget {lPos} eu={lEu}");
				}
				else if (legacyLeft != null)
				{
					lPos = root.transform.InverseTransformPoint(legacyLeft.position);
					lEu = c_AkLeftEu;
					_report.AppendLine($"  LeftHandGrip from LeftHandIkTarget pos + AK euler");
				}
				else
				{
					lEu = c_AkLeftEu;
				}
			}
			else if (fam == "m4")
			{
				if (IsNearIdentityEuler(rEu))
					rEu = c_M4RightEu;
				_report.AppendLine($"  RightHandGrip M4 baseline pos={rPos} eu={rEu}");

				if (legacyLeft != null && !IsNearIdentityEuler(legacyLeft.localEulerAngles))
				{
					Vector3 ikPos = root.transform.InverseTransformPoint(legacyLeft.position);
					lEu = (Quaternion.Inverse(root.transform.rotation) * legacyLeft.rotation).eulerAngles;
					if (Vector3.Distance(lPos, ikPos) > 0.04f)
						lPos = Vector3.Lerp(lPos, ikPos, 0.5f);
				}
				else if (IsNearIdentityEuler(lEu))
					lEu = c_M4LeftEu;
				_report.AppendLine($"  LeftHandGrip M4 baseline pos={lPos} eu={lEu}");
			}
			else
			{
				if (gripMesh != null)
				{
					Vector3 meshLocal = root.transform.InverseTransformPoint(gripMesh.position);
					rPos = meshLocal + new Vector3(0.02f, 0.01f, -0.05f);
				}
				if (IsNearIdentityEuler(rEu))
					rEu = c_M4RightEu;

				if (legacyLeft != null)
					lPos = root.transform.InverseTransformPoint(legacyLeft.position);
				else if (handguard != null)
				{
					Vector3 hg = root.transform.InverseTransformPoint(handguard.position);
					lPos = hg + new Vector3(-0.05f, 0f, 0.05f);
				}

				if (IsNearIdentityEuler(lEu))
					lEu = c_M4LeftEu;
				_report.AppendLine($"  Standalone Right={rPos} Left={lPos} euL={lEu}");
			}

			if (rightGrip.localPosition != rPos || rightGrip.localEulerAngles != rEu ||
			    leftGrip.localPosition != lPos || leftGrip.localEulerAngles != lEu)
			{
				rightGrip.localPosition = rPos;
				rightGrip.localRotation = Quaternion.Euler(rEu);
				leftGrip.localPosition = lPos;
				leftGrip.localRotation = Quaternion.Euler(lEu);
				changed = true;
			}

			if (changed)
				PrefabUtility.SaveAsPrefabAsset(root, _path);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(root);
		}

		return changed;
	}

	private static string FamilyOf(string path)
	{
		string n = path.ToLowerInvariant();
		if (n.Contains("benelli"))
			return "standalone";
		if (n.Contains("/ak/") || n.Contains("rpk") || n.Contains("ak47") || n.Contains("ak74"))
			return "ak";
		if (n.Contains("m16") || n.Contains("mk12") || n.Contains("mk18") || n.Contains("m249") ||
		    n.Contains("m4_moda") || n.Contains("/m4/equipped"))
			return "m4";
		return "standalone";
	}

	private static bool IsNearIdentityEuler(Vector3 eu, float tol = 1f)
	{
		float Wrap(float a)
		{
			a %= 360f;
			if (a > 180f) a -= 360f;
			return a;
		}

		return Mathf.Abs(Wrap(eu.x)) < tol && Mathf.Abs(Wrap(eu.y)) < tol && Mathf.Abs(Wrap(eu.z)) < tol;
	}

	private static Transform EnsureChild(Transform parent, string name)
	{
		Transform existing = parent.Find(name);
		if (existing != null)
			return existing;
		var go = new GameObject(name);
		Transform t = go.transform;
		t.SetParent(parent, false);
		t.localPosition = Vector3.zero;
		t.localRotation = Quaternion.identity;
		t.localScale = Vector3.one;
		return t;
	}

	private static Transform FindChildRecursive(Transform root, string name)
	{
		foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
		{
			if (t != root && t.name == name)
				return t;
		}

		return null;
	}

	private static Transform FindNameRegex(Transform root, string pattern)
	{
		var rx = new Regex(pattern, RegexOptions.IgnoreCase);
		foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
		{
			if (t == root)
				continue;
			if (t.name is "RightHandGrip" or "LeftHandGrip" or "GripRig")
				continue;
			if (rx.IsMatch(t.name))
				return t;
		}

		return null;
	}
}
#endif
