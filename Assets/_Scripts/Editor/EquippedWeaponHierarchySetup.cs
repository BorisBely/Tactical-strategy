#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Копирует пустышки, <see cref="EquippedWeapon"/> и связи с эталонного Equipped_M4_ModA_2 на корень оружия в сцене (без сохранения префаба).
/// </summary>
public static class EquippedWeaponHierarchySetup
{
	private const string c_TemplatePrefabPath = "Assets/Prefabs/Weapons/M4/Equipped/Equipped_M4_ModA_2.prefab";

	private static readonly string[] c_RootSocketNames =
	{
		"Visual",
		"ShellEject",
		"MuzzleModuleVisualSocket",
		"OpticModuleVisualSocket",
		"SideRailModuleVisualSocket",
		"MagazineSocket",
		"SecondaryMagazineSocket",
		"StockSocket",
		"UnderBarrelSocket",
		"RailSocket_1",
		"RailSocket_2",
		"RailSocket_3",
	};

	[MenuItem("GameObject/Weapons/Apply Equipped Rig (M4 template)", false, 10)]
	private static void ApplyFromSelection()
	{
		if (Selection.activeTransform == null)
		{
			EditorUtility.DisplayDialog("Equipped rig", "Выбери корень оружия (например AK47) в Hierarchy.", "OK");
			return;
		}

		ApplyToRoot(Selection.activeTransform.gameObject);
	}

	/// <summary>Batch: -executeMethod EquippedWeaponHierarchySetupRunner.RunOnSampleSceneAk47</summary>
	public static void ApplyToRoot(GameObject _root)
	{
		var templateRoot = AssetDatabase.LoadAssetAtPath<GameObject>(c_TemplatePrefabPath);
		if (templateRoot == null)
		{
			Debug.LogError($"Template not found: {c_TemplatePrefabPath}");
			return;
		}

		var templateWeapon = templateRoot.GetComponent<EquippedWeapon>();
		if (templateWeapon == null)
		{
			Debug.LogError("Template has no EquippedWeapon.");
			return;
		}

		Undo.RegisterFullObjectHierarchyUndo(_root, "Apply Equipped Weapon Rig");

		var rootTransform = _root.transform;
		var socketSet = new HashSet<string>(c_RootSocketNames);

		Transform visual = EnsureChild(rootTransform, "Visual");
		CopyLocalTransform(templateRoot.transform.Find("Visual"), visual);

		var meshChildren = new List<Transform>();
		for (var i = 0; i < rootTransform.childCount; i++)
		{
			var child = rootTransform.GetChild(i);
			if (child == visual || socketSet.Contains(child.name))
				continue;
			meshChildren.Add(child);
		}

		foreach (var meshChild in meshChildren)
			meshChild.SetParent(visual, true);

		var shellEject = EnsureSocket(rootTransform, templateRoot.transform, "ShellEject");
		var magazineSocket = EnsureSocket(rootTransform, templateRoot.transform, "MagazineSocket");
		var secondaryMagazineSocket = EnsureChild(rootTransform, "SecondaryMagazineSocket");
		var muzzleSocket = EnsureSocket(rootTransform, templateRoot.transform, "MuzzleModuleVisualSocket");
		var opticSocket = EnsureSocket(rootTransform, templateRoot.transform, "OpticModuleVisualSocket");
		var sideRailSocket = EnsureSocket(rootTransform, templateRoot.transform, "SideRailModuleVisualSocket");
		if (sideRailSocket == opticSocket)
			sideRailSocket = EnsureChild(rootTransform, "SideRailModuleVisualSocket");
		var stockSocket = EnsureSocket(rootTransform, templateRoot.transform, "StockSocket");
		var underBarrelSocket = EnsureSocket(rootTransform, templateRoot.transform, "UnderBarrelSocket");
		var rail1 = EnsureSocket(rootTransform, templateRoot.transform, "RailSocket_1");
		var rail2 = EnsureSocket(rootTransform, templateRoot.transform, "RailSocket_2");
		var rail3 = EnsureSocket(rootTransform, templateRoot.transform, "RailSocket_3");

		var weapon = _root.GetComponent<EquippedWeapon>();
		if (weapon == null)
			weapon = Undo.AddComponent<EquippedWeapon>(_root);

		var so = new SerializedObject(weapon);
		so.FindProperty("m_Barrel").objectReferenceValue = muzzleSocket;
		so.FindProperty("m_ShellEject").objectReferenceValue = shellEject;
		so.FindProperty("m_SightPivot").objectReferenceValue = opticSocket;
		so.FindProperty("m_MagazineSocket").objectReferenceValue = magazineSocket;
		so.FindProperty("m_SecondaryMagazineSocket").objectReferenceValue = secondaryMagazineSocket;
		so.FindProperty("m_MuzzleModuleVisualSocket").objectReferenceValue = muzzleSocket;
		so.FindProperty("m_OpticModuleVisualSocket").objectReferenceValue = opticSocket;
		so.FindProperty("m_SideRailModuleVisualSocket").objectReferenceValue = sideRailSocket;
		so.FindProperty("m_StockSocket").objectReferenceValue = stockSocket;
		so.FindProperty("m_UnderBarrelSocket").objectReferenceValue = underBarrelSocket;
		so.FindProperty("m_RailSockets").arraySize = 3;
		so.FindProperty("m_RailSockets").GetArrayElementAtIndex(0).objectReferenceValue = rail1;
		so.FindProperty("m_RailSockets").GetArrayElementAtIndex(1).objectReferenceValue = rail2;
		so.FindProperty("m_RailSockets").GetArrayElementAtIndex(2).objectReferenceValue = rail3;

		var defaultOptics = CollectDefaultVisuals(visual, "Stock", "Mag");
		var defaultStocks = FindGameObjectsByNameContains(visual, "Stock");
		so.FindProperty("m_DefaultOpticVisuals").arraySize = defaultOptics.Count;
		for (var i = 0; i < defaultOptics.Count; i++)
			so.FindProperty("m_DefaultOpticVisuals").GetArrayElementAtIndex(i).objectReferenceValue = defaultOptics[i];
		so.FindProperty("m_DefaultStockVisuals").arraySize = defaultStocks.Count;
		for (var i = 0; i < defaultStocks.Count; i++)
			so.FindProperty("m_DefaultStockVisuals").GetArrayElementAtIndex(i).objectReferenceValue = defaultStocks[i];

		so.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(_root);

		Debug.Log($"Equipped rig applied to '{_root.name}' (template: {templateRoot.name}). Reposition sockets for AK geometry.", _root);
	}

	private static Transform EnsureChild(Transform _parent, string _name)
	{
		var existing = _parent.Find(_name);
		if (existing != null)
			return existing;

		var go = new GameObject(_name);
		Undo.RegisterCreatedObjectUndo(go, "Create " + _name);
		go.transform.SetParent(_parent, false);
		return go.transform;
	}

	private static Transform EnsureSocket(Transform _root, Transform _templateRoot, string _socketName)
	{
		var templateSocket = _templateRoot.Find(_socketName);
		if (templateSocket == null)
		{
			Debug.LogWarning($"Socket missing on template: {_socketName}");
			return EnsureChild(_root, _socketName);
		}

		var socket = EnsureChild(_root, _socketName);
		CopyLocalTransform(templateSocket, socket);
		return socket;
	}

	private static void CopyLocalTransform(Transform _from, Transform _to)
	{
		if (_from == null || _to == null)
			return;

		_to.localPosition = _from.localPosition;
		_to.localRotation = _from.localRotation;
		_to.localScale = _from.localScale;
	}

	private static List<GameObject> CollectDefaultVisuals(Transform _visualRoot, params string[] _excludeNameParts)
	{
		var result = new List<GameObject>();
		foreach (var renderer in _visualRoot.GetComponentsInChildren<Renderer>(true))
		{
			var go = renderer.gameObject;
			var n = go.name;
			if (n.Contains("Stock"))
				continue;
			foreach (var part in _excludeNameParts)
			{
				if (n.Contains(part))
					goto next;
			}

			if (n.Contains("Iron") || n.Contains("Sight") || n.Contains("Ironsight"))
				result.Add(go);
			next: ;
		}

		return result;
	}

	private static List<GameObject> FindGameObjectsByNameContains(Transform _root, string _substring)
	{
		var result = new List<GameObject>();
		foreach (var t in _root.GetComponentsInChildren<Transform>(true))
		{
			if (t.name.Contains(_substring))
				result.Add(t.gameObject);
		}

		return result;
	}
}

public static class EquippedWeaponHierarchySetupRunner
{
	public static void RunOnSampleSceneAk47()
	{
		var scenePath = "Assets/Scenes/SampleScene.unity";
		var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
		var ak47 = GameObject.Find("AK47");
		if (ak47 == null)
		{
			Debug.LogError("AK47 not found in SampleScene.");
			EditorApplication.Exit(1);
			return;
		}

		EquippedWeaponHierarchySetup.ApplyToRoot(ak47);
		EditorSceneManager.MarkSceneDirty(scene);
		if (!EditorSceneManager.SaveScene(scene))
		{
			Debug.LogError("Failed to save SampleScene.");
			EditorApplication.Exit(1);
			return;
		}

		Debug.Log("AK47 equipped rig saved to SampleScene.");
		EditorApplication.Exit(0);
	}
}
#endif
