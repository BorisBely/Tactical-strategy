#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Компактный layout инвентаря / Mission Prep (не fullscreen):
/// прежние позиции с лёгкой симметрией, rename, скроллбар без перекрытия ячеек.
/// Tools/Inventory/Apply Compact Inventory Layout
/// </summary>
public static class InventoryUiStage1LayoutBootstrap
{
	private const string c_ScenePath = "Assets/Scenes/SampleScene.unity";
	private const float c_ScrollbarWidth = 10f;

	[MenuItem("Tools/Inventory/Apply Compact Inventory Layout")]
	public static void ApplyFromMenu()
	{
		if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
			return;

		Scene scene = EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);
		int changed = ApplyCompactLayout();
		EditorSceneManager.MarkSceneDirty(scene);
		EditorSceneManager.SaveScene(scene);
		Debug.Log($"InventoryUiStage1LayoutBootstrap: compact layout applied, touches≈{changed}");
	}

	[MenuItem("Tools/Inventory/Apply Stage1 Layout And Renames")]
	public static void ApplyFromMenuLegacyAlias()
	{
		ApplyFromMenu();
	}

	public static void ApplyBatchmode()
	{
		Scene scene = EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);
		ApplyCompactLayout();
		EditorSceneManager.SaveScene(scene);
	}

	private static int ApplyCompactLayout()
	{
		int touches = 0;
		Transform inventoryRoot = FindNamed("InventoryRoot");
		Transform prepRoot = FindNamed("MissionPrepScreenRoot");

		if (inventoryRoot != null)
			touches += RestoreRuntimeCompact(inventoryRoot);
		if (prepRoot != null)
			touches += RestorePrepCompact(prepRoot);

		touches += FixScrollBars(inventoryRoot);
		touches += FixScrollBars(prepRoot);

		string marker = "Assets/.inventory_ui_stage1_layout_done";
		if (System.IO.File.Exists(System.IO.Path.Combine(Application.dataPath, ".inventory_ui_stage1_layout_done")))
		{
			AssetDatabase.DeleteAsset(marker);
			touches++;
		}

		return touches;
	}

	private static int RestoreRuntimeCompact(Transform _root)
	{
		int touches = 0;
		UnwrapColumns(_root, new[] { "RuntimeColumnsRow", "RuntimeUnitColumn", "RuntimeGroundColumn" }, ref touches);

		// User compact cluster (left); free space on the right to see the unit.
		PlaceCenter(_root, "RuntimeGroundOrPartnerPanel", "Ground", -798f, 225f, 400f, 700f, ref touches);
		PlaceCenter(_root, "RuntimeUnitInventoryPanel", "UnitInventory", -398f, 225f, 400f, 700f, ref touches);
		PlaceCenter(_root, "RuntimePartnerSummary", "unit_list (1)", -798f, 616f, 400f, 82f, ref touches);
		PlaceCenter(_root, "RuntimeUnitSummary", "unit_list", -398f, 616f, 400f, 82f, ref touches);
		PlaceCenter(_root, "RuntimeUnitHealthPanel", "Health", -86f, 266f, 225f, 782f, ref touches);
		PlaceCenter(_root, "RuntimePartnerHealthPanel", "Health 2", -1110f, 266f, 225f, 782f, ref touches);

		RemoveLayoutBehavioursUnder(_root, ref touches);
		return touches;
	}

	private static int RestorePrepCompact(Transform _root)
	{
		int touches = 0;
		UnwrapColumns(_root, new[] { "PrepColumnsRow" }, ref touches);

		// Left/center compact panels; right side stays open for unit preview.
		PlaceCenter(_root, "PrepUnitList", "unit_list", -1012f, 0f, 400f, 1320f, ref touches);
		PlaceCenter(_root, "PrepPresetEquipmentPanel", "PresetEquipmentPanel", -600f, 0f, 400f, 1320f, ref touches);
		PlaceCenter(_root, "PrepAvailableEquipmentPanel", "AvailableEquipmentPanel", -188f, -70f, 400f, 1180f, ref touches);
		PlaceCenter(_root, "PrepStatsPanel", "Units (2)", 224f, -510f, 400f, 300f, ref touches);

		RenameChild(_root, "PrepPresetEquipmentPanel", "PrepPresetDropdown", "UnitPreset", ref touches);
		RenameChild(_root, "PrepPresetEquipmentPanel", "PrepArmorDropdown", "UnitPreset (1)", ref touches);
		RenameChild(_root, "PrepPresetEquipmentPanel", "PrepCamouflageDropdown", "UnitCamouflage", ref touches);

		RemoveLayoutBehavioursUnder(_root, ref touches);
		return touches;
	}

	private static void UnwrapColumns(Transform _root, string[] _wrapperNames, ref int _touches)
	{
		for (int i = 0; i < _wrapperNames.Length; i++)
		{
			Transform wrapper = FindDeep(_root, _wrapperNames[i]);
			if (wrapper == null)
				continue;

			while (wrapper.childCount > 0)
			{
				Transform child = wrapper.GetChild(0);
				child.SetParent(_root, true);
				_touches++;
			}

			Object.DestroyImmediate(wrapper.gameObject);
			_touches++;
		}
	}

	private static void PlaceCenter(
		Transform _root,
		string _preferredName,
		string _legacyName,
		float _x,
		float _y,
		float _w,
		float _h,
		ref int _touches)
	{
		Transform t = FindDeep(_root, _preferredName) ?? FindDeep(_root, _legacyName);
		if (t == null)
		{
			Debug.LogWarning($"Compact layout: missing {_preferredName}/{_legacyName}");
			return;
		}

		if (t.name != _preferredName)
		{
			t.name = _preferredName;
			_touches++;
		}

		if (t.parent != _root)
		{
			t.SetParent(_root, false);
			_touches++;
		}

		RectTransform rt = t as RectTransform;
		if (rt == null)
			return;

		rt.anchorMin = new Vector2(0.5f, 0.5f);
		rt.anchorMax = new Vector2(0.5f, 0.5f);
		rt.pivot = new Vector2(0.5f, 0.5f);
		rt.anchoredPosition = new Vector2(_x, _y);
		rt.sizeDelta = new Vector2(_w, _h);
		rt.localScale = Vector3.one;
		rt.localRotation = Quaternion.identity;
		EditorUtility.SetDirty(rt);
		_touches++;
	}

	private static void RenameChild(
		Transform _root,
		string _parentPreferred,
		string _newName,
		string _legacyName,
		ref int _touches)
	{
		Transform parent = FindDeep(_root, _parentPreferred);
		if (parent == null)
			return;
		Transform child = parent.Find(_newName) ?? parent.Find(_legacyName) ?? FindDeep(parent, _legacyName);
		if (child == null || child.name == _newName)
			return;
		child.name = _newName;
		_touches++;
		EditorUtility.SetDirty(child.gameObject);
	}

	private static void RemoveLayoutBehavioursUnder(Transform _root, ref int _touches)
	{
		if (_root == null)
			return;

		HorizontalLayoutGroup[] h = _root.GetComponentsInChildren<HorizontalLayoutGroup>(true);
		for (int i = 0; i < h.Length; i++)
		{
			// Keep layout groups that belong to cell content (e.g. inside UnitCell), only strip on panel roots / columns.
			if (!IsPanelChrome(h[i].transform))
				continue;
			Object.DestroyImmediate(h[i]);
			_touches++;
		}

		VerticalLayoutGroup[] v = _root.GetComponentsInChildren<VerticalLayoutGroup>(true);
		for (int i = 0; i < v.Length; i++)
		{
			if (!IsPanelChrome(v[i].transform))
				continue;
			Object.DestroyImmediate(v[i]);
			_touches++;
		}

		LayoutElement[] le = _root.GetComponentsInChildren<LayoutElement>(true);
		for (int i = 0; i < le.Length; i++)
		{
			if (!IsPanelChrome(le[i].transform))
				continue;
			Object.DestroyImmediate(le[i]);
			_touches++;
		}
	}

	private static bool IsPanelChrome(Transform _t)
	{
		string n = _t.name;
		return n.StartsWith("Runtime") || n.StartsWith("Prep") || n.Contains("Column") || n.Contains("ColumnsRow");
	}

	private static int FixScrollBars(Transform _root)
	{
		if (_root == null)
			return 0;

		int touches = 0;
		ScrollRect[] scrolls = _root.GetComponentsInChildren<ScrollRect>(true);
		for (int i = 0; i < scrolls.Length; i++)
		{
			ScrollRect sr = scrolls[i];
			sr.horizontal = false;
			sr.horizontalScrollbar = null;
			sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
			sr.verticalScrollbarSpacing = 2f;
			if (sr.scrollSensitivity < 30f)
				sr.scrollSensitivity = 40f;

			if (sr.verticalScrollbar != null)
			{
				RectTransform barRt = sr.verticalScrollbar.transform as RectTransform;
				if (barRt != null)
				{
					Vector2 size = barRt.sizeDelta;
					size.x = c_ScrollbarWidth;
					barRt.sizeDelta = size;
				}
			}

			Transform horizontal = sr.transform.Find("Scrollbar Horizontal");
			if (horizontal != null && horizontal.gameObject.activeSelf)
			{
				horizontal.gameObject.SetActive(false);
				touches++;
			}

			EditorUtility.SetDirty(sr);
			touches++;
		}

		return touches;
	}

	private static Transform FindNamed(string _name)
	{
		GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
		for (int i = 0; i < roots.Length; i++)
		{
			Transform found = FindDeep(roots[i].transform, _name);
			if (found != null)
				return found;
		}

		return null;
	}

	private static Transform FindDeep(Transform _parent, string _name)
	{
		if (_parent == null)
			return null;
		if (_parent.name == _name)
			return _parent;
		for (int i = 0; i < _parent.childCount; i++)
		{
			Transform f = FindDeep(_parent.GetChild(i), _name);
			if (f != null)
				return f;
		}

		return null;
	}
}
#endif
