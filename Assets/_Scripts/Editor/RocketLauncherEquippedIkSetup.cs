#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Добавляет на Equipped-префабы гранатомётов пустышки IK + Muzzle для тюнера и выстрела.
/// </summary>
public static class RocketLauncherEquippedIkSetup
{
	#region Constants
	private const string c_RpgPath = "Assets/Prefabs/Weapons/RocketLaunchers/Equipped/Equipped_Rpg7.prefab";
	private const string c_DisposablePath = "Assets/Prefabs/Weapons/RocketLaunchers/Equipped/Equipped_DisposableLauncher.prefab";
	private const string c_ItemRpg = "Assets/GameData/Inventory/RocketLaunchers/Item_Weapon_Rpg7.asset";
	private const string c_ItemDisposable = "Assets/GameData/Inventory/RocketLaunchers/Item_Weapon_DisposableRocketLauncher.asset";
	private const string c_MarkerPath = "Assets/.rocket_launcher_equipped_ik_setup_done";
	#endregion

	#region Bootstrap
	[InitializeOnLoadMethod]
	private static void AutoSetupOnce()
	{
		EditorApplication.delayCall += () =>
		{
			if (System.IO.File.Exists(c_MarkerPath))
				return;

			try
			{
				RunSetup();
			}
			catch (System.Exception ex)
			{
				Debug.LogWarning($"[RocketLauncherEquippedIkSetup] Deferred: {ex.Message}");
			}
		};
	}
	#endregion

	#region Menu
	[MenuItem("Polygone/Equipment/Setup Rocket Launcher Equipped IK Sockets")]
	public static void RunSetup()
	{
		SetupEquippedPrefab(
			c_RpgPath,
			new Vector3(0.06f, 0.02f, 0.04f),
			new Vector3(0.09f, -0.02f, -0.08f),
			new Vector3(-0.04f, 0.06f, 0.32f),
			new Vector3(-0.04f, 0.06f, 0.28f),
			new Vector3(0f, 0.09f, 0.95f),
			new Vector3(0f, 0.09f, -0.55f));

		SetupEquippedPrefab(
			c_DisposablePath,
			new Vector3(0.05f, 0.02f, 0.03f),
			new Vector3(0.08f, -0.02f, -0.06f),
			new Vector3(-0.04f, 0.05f, 0.28f),
			new Vector3(-0.04f, 0.05f, 0.24f),
			new Vector3(0f, 0.07f, 0.85f),
			new Vector3(0f, 0.07f, -0.4f));

		WireItemVisual(c_ItemRpg, c_RpgPath);
		WireItemVisual(c_ItemDisposable, c_DisposablePath);

		System.IO.File.WriteAllText(c_MarkerPath, System.DateTime.UtcNow.ToString("o"));
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log("[RocketLauncherEquippedIkSetup] Equipped_Rpg7 / Equipped_DisposableLauncher: IK + Muzzle + Backblast sockets ready.");
	}
	#endregion

	#region Private
	private static void SetupEquippedPrefab(
		string _path,
		Vector3 _rightReady,
		Vector3 _rightNotReady,
		Vector3 _leftReady,
		Vector3 _leftNotReady,
		Vector3 _muzzle,
		Vector3 _backblast)
	{
		GameObject root = PrefabUtility.LoadPrefabContents(_path);
		if (root == null)
		{
			Debug.LogError($"[RocketLauncherEquippedIkSetup] Missing prefab: {_path}");
			return;
		}

		try
		{
			Transform t = root.transform;
			EnsureEmpty(t, "RightHandIkTarget", _rightReady, Vector3.zero, false);
			EnsureEmpty(t, "RightHandIkTarget_NotReady", _rightNotReady, Vector3.zero, false);
			EnsureEmpty(t, "LeftHandIkTarget", _leftReady, Vector3.zero, false);
			EnsureEmpty(t, "LeftHandIkTarget_NotReady", _leftNotReady, Vector3.zero, false);
			EnsureEmpty(t, "Muzzle", _muzzle, Vector3.zero, false);
			// Always refresh Backblast pose so rear blast points backward.
			EnsureEmpty(t, "Backblast", _backblast, new Vector3(0f, 180f, 0f), true);
			EnsureRocketSocket(t);
			PrefabUtility.SaveAsPrefabAsset(root, _path);
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(root);
		}
	}

	private static void EnsureEmpty(
		Transform _parent,
		string _name,
		Vector3 _localPos,
		Vector3 _localEuler,
		bool _forcePose)
	{
		Transform existing = _parent.Find(_name);
		if (existing != null)
		{
			if (_forcePose)
			{
				existing.localPosition = _localPos;
				existing.localRotation = Quaternion.Euler(_localEuler);
				existing.localScale = Vector3.one;
			}

			return;
		}

		GameObject go = new GameObject(_name);
		Transform t = go.transform;
		t.SetParent(_parent, false);
		t.localPosition = _localPos;
		t.localRotation = Quaternion.Euler(_localEuler);
		t.localScale = Vector3.one;
	}

	private static void EnsureRocketSocket(Transform _root)
	{
		if (_root == null)
			return;

		Transform socket = _root.Find(RocketLauncherVisualUtility.RocketSocketName);
		if (socket == null)
		{
			GameObject go = new GameObject(RocketLauncherVisualUtility.RocketSocketName);
			socket = go.transform;
			socket.SetParent(_root, false);
			socket.localPosition = new Vector3(0f, 0.088f, 0.26f);
			socket.localRotation = Quaternion.identity;
			socket.localScale = Vector3.one;
		}

		for (int i = _root.childCount - 1; i >= 0; i--)
		{
			Transform child = _root.GetChild(i);
			if (child == null || child == socket)
				continue;

			string name = child.name;
			bool isRocketPart =
				name.IndexOf("Rocket", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
				name.IndexOf("Missile", System.StringComparison.OrdinalIgnoreCase) >= 0;
			if (!isRocketPart)
				continue;

			child.SetParent(socket, true);
			child.localPosition = Vector3.zero;
			child.localRotation = Quaternion.identity;
		}
	}

	private static void WireItemVisual(string _itemPath, string _equippedPrefabPath)
	{
		ItemDefinition item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(_itemPath);
		GameObject equipped = AssetDatabase.LoadAssetAtPath<GameObject>(_equippedPrefabPath);
		if (item == null || equipped == null)
			return;

		SerializedObject so = new SerializedObject(item);
		SerializedProperty hand = so.FindProperty("m_RocketLauncherHandPrefab");
		SerializedProperty visual = so.FindProperty("m_EquippedVisualPrefab");
		if (hand != null && hand.objectReferenceValue == null)
			hand.objectReferenceValue = equipped;
		if (visual != null)
			visual.objectReferenceValue = equipped;
		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(item);
	}
	#endregion
}
#endif
