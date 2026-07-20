#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Создаёт пустые якоря Holster_Weapon_Cell_Left/Right на Spine_02 и вешает UnitBackWeaponHolsterVisuals на Unit.prefab.
/// </summary>
public static class BackWeaponHolsterSetup
{
	#region Constants
	private const string c_UnitPrefabPath = "Assets/Prefabs/Characters/Unit.prefab";
	private const string c_MarkerPath = "Assets/.back_weapon_holster_setup_done";
	private const string c_Spine02Name = "Spine_02";
	#endregion

	#region Bootstrap
	[InitializeOnLoadMethod]
	private static void AutoSetupOnce()
	{
		EditorApplication.delayCall += () =>
		{
			if (System.IO.File.Exists(c_MarkerPath))
				return;

			RunSetup();
		};
	}
	#endregion

	#region Menu
	[MenuItem("Polygone/Equipment/Setup Back Weapon Holsters")]
	public static void RunSetup()
	{
		GameObject unitRoot = PrefabUtility.LoadPrefabContents(c_UnitPrefabPath);
		if (unitRoot == null)
		{
			Debug.LogError($"[BackWeaponHolsterSetup] Unit prefab missing: {c_UnitPrefabPath}");
			return;
		}

		try
		{
			Transform spine02 = FindChildByName(unitRoot.transform, c_Spine02Name);
			if (spine02 == null)
			{
				Debug.LogError("[BackWeaponHolsterSetup] Spine_02 not found on Unit.prefab.");
				return;
			}

			Transform left = EnsureCell(
				spine02,
				UnitBackWeaponHolsterVisuals.LeftCellName,
				new Vector3(-0.18f, 0.05f, -0.2f),
				new Vector3(0f, 0f, 15f));
			Transform right = EnsureCell(
				spine02,
				UnitBackWeaponHolsterVisuals.RightCellName,
				new Vector3(0.18f, 0.05f, -0.2f),
				new Vector3(0f, 0f, -15f));

			UnitBackWeaponHolsterVisuals holster = unitRoot.GetComponent<UnitBackWeaponHolsterVisuals>();
			if (holster == null)
				holster = unitRoot.AddComponent<UnitBackWeaponHolsterVisuals>();

			SerializedObject so = new SerializedObject(holster);
			so.FindProperty("m_LeftCell").objectReferenceValue = left;
			so.FindProperty("m_RightCell").objectReferenceValue = right;
			so.FindProperty("m_HideDistanceMeters").floatValue = 45f;
			so.FindProperty("m_LodCheckIntervalSeconds").floatValue = 0.2f;
			so.ApplyModifiedPropertiesWithoutUndo();

			PrefabUtility.SaveAsPrefabAsset(unitRoot, c_UnitPrefabPath);
			System.IO.File.WriteAllText(c_MarkerPath, System.DateTime.UtcNow.ToString("o"));
			AssetDatabase.Refresh();
			Debug.Log("[BackWeaponHolsterSetup] Unit.prefab: Holster_Weapon_Cell_Left/Right + UnitBackWeaponHolsterVisuals ready.");
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(unitRoot);
		}
	}
	#endregion

	#region Helpers
	private static Transform EnsureCell(Transform _parent, string _name, Vector3 _localPos, Vector3 _localEuler)
	{
		Transform existing = _parent.Find(_name);
		if (existing != null)
			return existing;

		GameObject cellObject = new GameObject(_name);
		Transform cell = cellObject.transform;
		cell.SetParent(_parent, false);
		cell.localPosition = _localPos;
		cell.localRotation = Quaternion.Euler(_localEuler);
		cell.localScale = Vector3.one;
		return cell;
	}

	private static Transform FindChildByName(Transform _root, string _name)
	{
		if (_root == null)
			return null;

		if (_root.name == _name)
			return _root;

		for (int i = 0; i < _root.childCount; i++)
		{
			Transform found = FindChildByName(_root.GetChild(i), _name);
			if (found != null)
				return found;
		}

		return null;
	}
	#endregion
}
#endif
