#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Этап 2: цвета панелей + выравнивание дропдаунов пресета.
/// Tools/Inventory/Apply Stage2 Theme Colors
/// </summary>
public static class InventoryUiStage2ThemeBootstrap
{
	private const string c_ScenePath = "Assets/Scenes/SampleScene.unity";

	[MenuItem("Tools/Inventory/Apply Stage2 Theme Colors")]
	public static void ApplyFromMenu()
	{
		if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
			return;

		Scene scene = EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);
		int touches = ApplyTheme();
		EditorSceneManager.MarkSceneDirty(scene);
		EditorSceneManager.SaveScene(scene);
		Debug.Log($"InventoryUiStage2ThemeBootstrap: applied, touches={touches}");
	}

	private static int ApplyTheme()
	{
		int touches = 0;
		Transform inventoryRoot = FindNamed("InventoryRoot");
		Transform prepRoot = FindNamed("MissionPrepScreenRoot");

		if (inventoryRoot != null)
		{
			EnsureApplier(inventoryRoot.gameObject, ref touches);
			InventoryUiThemeApplier.ApplyUnder(inventoryRoot);
			touches++;
		}

		if (prepRoot != null)
		{
			EnsureApplier(prepRoot.gameObject, ref touches);
			InventoryUiThemeApplier.ApplyUnder(prepRoot);
			AlignPrepDropdowns(prepRoot, ref touches);
			touches++;
		}

		return touches;
	}

	private static void EnsureApplier(GameObject _root, ref int _touches)
	{
		if (_root.GetComponent<InventoryUiThemeApplier>() != null)
			return;

		_root.AddComponent<InventoryUiThemeApplier>();
		_touches++;
		EditorUtility.SetDirty(_root);
	}

	private static void AlignPrepDropdowns(Transform _prepRoot, ref int _touches)
	{
		Transform panel = FindDeep(_prepRoot, "PrepPresetEquipmentPanel")
			?? FindDeep(_prepRoot, "PresetEquipmentPanel");
		if (panel == null)
			return;

		// Якорь top-left (0,1): x=200 при width=400 — по ширине панели.
		// Внутри враппера лежит child "Dropdown" — растягиваем на весь враппер.
		PlaceLocal(panel, "PrepPresetDropdown", "UnitPreset", 200f, -70f, 400f, 48f, ref _touches);
		PlaceLocal(panel, "PrepArmorDropdown", "UnitPreset (1)", 200f, -122.5f, 400f, 45f, ref _touches);
		PlaceLocal(panel, "PrepCamouflageDropdown", "UnitCamouflage", 200f, -167.5f, 400f, 45f, ref _touches);
		StretchInnerDropdown(panel, "PrepPresetDropdown", ref _touches);
		StretchInnerDropdown(panel, "PrepArmorDropdown", ref _touches);
		StretchInnerDropdown(panel, "PrepCamouflageDropdown", ref _touches);

		Transform strayCard = panel.Find("PrepDropdownCard");
		if (strayCard != null)
		{
			Undo.DestroyObjectImmediate(strayCard.gameObject);
			_touches++;
		}
	}

	private static void StretchInnerDropdown(Transform _panel, string _wrapperName, ref int _touches)
	{
		Transform wrapper = _panel.Find(_wrapperName);
		if (wrapper == null)
			return;

		Transform inner = wrapper.Find("Dropdown");
		if (inner == null)
			return;

		RectTransform rt = inner as RectTransform;
		if (rt == null)
			return;

		rt.anchorMin = Vector2.zero;
		rt.anchorMax = Vector2.one;
		rt.pivot = new Vector2(0.5f, 0.5f);
		rt.anchoredPosition = Vector2.zero;
		rt.sizeDelta = Vector2.zero;
		EditorUtility.SetDirty(rt);
		_touches++;
	}

	private static void PlaceLocal(
		Transform _panel,
		string _preferred,
		string _legacy,
		float _x,
		float _y,
		float _w,
		float _h,
		ref int _touches)
	{
		Transform t = _panel.Find(_preferred) ?? _panel.Find(_legacy) ?? FindDeep(_panel, _preferred) ?? FindDeep(_panel, _legacy);
		if (t == null)
			return;

		if (t.name != _preferred)
		{
			t.name = _preferred;
			_touches++;
		}

		RectTransform rt = t as RectTransform;
		if (rt == null)
			return;

		rt.anchorMin = new Vector2(0f, 1f);
		rt.anchorMax = new Vector2(0f, 1f);
		rt.pivot = new Vector2(0.5f, 0.5f);
		rt.anchoredPosition = new Vector2(_x, _y);
		rt.sizeDelta = new Vector2(_w, _h);
		EditorUtility.SetDirty(rt);
		_touches++;
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
			Transform found = FindDeep(_parent.GetChild(i), _name);
			if (found != null)
				return found;
		}

		return null;
	}
}
#endif
