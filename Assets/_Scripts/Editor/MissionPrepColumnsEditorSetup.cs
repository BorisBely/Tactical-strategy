#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// One-shot editor setup: collapsible columns row (base roster / mission roster / equipment). No runtime layout building.
/// </summary>
public static class MissionPrepColumnsEditorSetup
{
	private const string c_ScenePath = "Assets/Scenes/SampleScene.unity";
	private const string c_MarkerName = "PrepColumnsRow";
	private const float c_ColumnWidth = 400f;
	private const float c_ColumnHeight = 1320f;
	private const float c_StatsColumnHeight = 300f;
	private const float c_CollapsedWidth = 40f;
	private const float c_Spacing = 12f;

	[MenuItem("Polygone/Mission Prep/Setup Collapsible Columns (SampleScene)")]
	public static void SetupFromMenu()
	{
		if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
			return;

		Scene scene = EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);
		SetupInOpenScene();
		EditorSceneManager.MarkSceneDirty(scene);
		EditorSceneManager.SaveScene(scene);
		Debug.Log("[MissionPrepColumns] Setup complete and scene saved.");
	}

	/// <summary>Batchmode entry: Unity -batchmode -executeMethod MissionPrepColumnsEditorSetup.SetupAndSaveBatch</summary>
	public static void SetupAndSaveBatch()
	{
		Scene scene = EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);
		SetupInOpenScene();
		EditorSceneManager.MarkSceneDirty(scene);
		EditorSceneManager.SaveScene(scene);
		AssetDatabase.SaveAssets();
		Debug.Log("[MissionPrepColumns] Batch setup complete.");
		EditorApplication.Exit(0);
	}

	public static void SetupInOpenScene()
	{
		MissionPrepScreenController ctrl =
			Object.FindAnyObjectByType<MissionPrepScreenController>(FindObjectsInactive.Include);
		GameObject screenRootGo = ctrl != null ? ctrl.gameObject : null;
		if (screenRootGo == null)
			screenRootGo = FindSceneObjectByName("MissionPrepScreenRoot");

		if (screenRootGo == null)
			throw new System.Exception("MissionPrepScreenRoot not found.");

		RectTransform screenRt = screenRootGo.GetComponent<RectTransform>();
		Transform unitListT = FindDeepChild(screenRt, "PrepUnitList");
		Transform equipmentT = FindDeepChild(screenRt, "PrepPresetEquipmentPanel");
		Transform availableT = FindDeepChild(screenRt, "PrepAvailableEquipmentPanel");
		Transform statsT = FindDeepChild(screenRt, "PrepStatsPanel");
		if (unitListT == null || equipmentT == null || availableT == null || statsT == null)
			throw new System.Exception("One or more Prep* panels missing under MissionPrepScreenRoot.");

		// Idempotent: reuse previous row / vehicle list if re-running.
		Transform existingRow = FindDeepChild(screenRt, c_MarkerName);
		Transform existingVehicle = FindDeepChild(screenRt, "PrepVehicleList");

		RectTransform rowRt;
		if (existingRow != null)
			rowRt = existingRow as RectTransform;
		else
		{
			GameObject rowGo = new GameObject(c_MarkerName, typeof(RectTransform));
			rowRt = rowGo.GetComponent<RectTransform>();
			rowRt.SetParent(screenRt, false);
			rowRt.SetSiblingIndex(0);
		}

		ConfigureRow(rowRt);

		GameObject vehicleListGo;
		if (existingVehicle != null)
			vehicleListGo = existingVehicle.gameObject;
		else
			vehicleListGo = Object.Instantiate(unitListT.gameObject, rowRt);
		vehicleListGo.name = "PrepVehicleList";

		// Reparent columns into row in order.
		ReparentUnder(rowRt, vehicleListGo.transform);
		ReparentUnder(rowRt, unitListT);
		ReparentUnder(rowRt, equipmentT);
		ReparentUnder(rowRt, availableT);
		ReparentUnder(rowRt, statsT);

		vehicleListGo.transform.SetSiblingIndex(0);
		unitListT.SetSiblingIndex(1);
		equipmentT.SetSiblingIndex(2);
		availableT.SetSiblingIndex(3);
		statsT.SetSiblingIndex(4);

		NormalizePanelRoot(vehicleListGo.transform as RectTransform, c_ColumnHeight);
		NormalizePanelRoot(unitListT as RectTransform, c_ColumnHeight);
		NormalizePanelRoot(equipmentT as RectTransform, c_ColumnHeight);
		NormalizePanelRoot(availableT as RectTransform, c_ColumnHeight);
		NormalizePanelRoot(statsT as RectTransform, c_StatsColumnHeight);

		// Pin available/equipment chrome so expand/collapse does not float controls.
		FixAvailableEquipmentChrome(availableT);
		FixEquipmentDropdownChrome(equipmentT);

		SetupVehicleListClone(vehicleListGo);
		FixVehicleScrollChrome(vehicleListGo);
		SetPanelTitleKey(unitListT.gameObject, "mission_prep.column.mission", "На задание");
		SetPanelTitleKey(equipmentT.gameObject, "mission_prep.column.equipment", "Экипировка");
		SetPanelTitleKey(availableT.gameObject, "mission_prep.column.available", "Доступное");
		SetPanelTitleKey(statsT.gameObject, "mission_prep.column.stats", "Характеристики");

		WireCollapsible(vehicleListGo, "mission_prep.column.base", "На базе", c_ColumnHeight);
		WireCollapsible(unitListT.gameObject, "mission_prep.column.mission", "На задание", c_ColumnHeight);
		WireCollapsible(equipmentT.gameObject, "mission_prep.column.equipment", "Экипировка", c_ColumnHeight);
		WireCollapsible(availableT.gameObject, "mission_prep.column.available", "Доступное", c_ColumnHeight);
		WireCollapsible(statsT.gameObject, "mission_prep.column.stats", "Характеристики", c_StatsColumnHeight);

		WireSpawnerAndController(vehicleListGo);

		EditorUtility.SetDirty(screenRootGo);
	}

	private static void ConfigureRow(RectTransform _row)
	{
		_row.anchorMin = new Vector2(0f, 0.5f);
		_row.anchorMax = new Vector2(0f, 0.5f);
		_row.pivot = new Vector2(0f, 0.5f);
		_row.anchoredPosition = new Vector2(24f, 0f);
		_row.sizeDelta = new Vector2(c_ColumnWidth * 5f + c_Spacing * 4f, c_ColumnHeight);

		HorizontalLayoutGroup hlg = _row.GetComponent<HorizontalLayoutGroup>();
		if (hlg == null)
			hlg = _row.gameObject.AddComponent<HorizontalLayoutGroup>();
		hlg.spacing = c_Spacing;
		hlg.childAlignment = TextAnchor.LowerLeft;
		hlg.childControlWidth = true;
		hlg.childControlHeight = true;
		hlg.childForceExpandWidth = false;
		hlg.childForceExpandHeight = false;
		hlg.padding = new RectOffset(0, 0, 0, 0);

		ContentSizeFitter fitter = _row.GetComponent<ContentSizeFitter>();
		if (fitter == null)
			fitter = _row.gameObject.AddComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
	}

	private static void NormalizePanelRoot(RectTransform _rt, float _height)
	{
		if (_rt == null)
			return;

		_rt.anchorMin = new Vector2(0f, 0.5f);
		_rt.anchorMax = new Vector2(0f, 0.5f);
		_rt.pivot = new Vector2(0f, 0.5f);
		_rt.anchoredPosition = Vector2.zero;
		_rt.sizeDelta = new Vector2(c_ColumnWidth, _height);
		_rt.localScale = Vector3.one;
		_rt.localRotation = Quaternion.identity;

		LayoutElement le = _rt.GetComponent<LayoutElement>();
		if (le == null)
			le = _rt.gameObject.AddComponent<LayoutElement>();
		le.minWidth = c_ColumnWidth;
		le.preferredWidth = c_ColumnWidth;
		le.flexibleWidth = 0f;
		le.minHeight = _height;
		le.preferredHeight = _height;
		le.flexibleHeight = 0f;
	}

	private static void FixAvailableEquipmentChrome(Transform _available)
	{
		if (_available == null)
			return;

		Transform content = FindDeepChild(_available, "ColumnContent") ?? _available;
		const float filterHeight = 36f;
		const float scrollTopInset = 44f;

		// CollapseToggle already shows the column name — hide duplicate content title.
		for (int i = 0; i < content.childCount; i++)
		{
			Transform child = content.GetChild(i);
			if (child != null && child.name == "Text (TMP)")
				child.gameObject.SetActive(false);
		}

		RectTransform filterRt = FindDeepChild(content, "CategoryFilterRow") as RectTransform;
		if (filterRt != null)
		{
			filterRt.anchorMin = new Vector2(0.5f, 1f);
			filterRt.anchorMax = new Vector2(0.5f, 1f);
			filterRt.pivot = new Vector2(0.5f, 1f);
			filterRt.anchoredPosition = new Vector2(0f, -4f);
			filterRt.sizeDelta = new Vector2(392f, filterHeight);
		}

		RectTransform availableScroll =
			FindDeepChild(content, "PrepAvailableEquipmentPanelScroll") as RectTransform;
		if (availableScroll != null)
		{
			availableScroll.anchorMin = Vector2.zero;
			availableScroll.anchorMax = Vector2.one;
			availableScroll.pivot = new Vector2(0.5f, 0.5f);
			availableScroll.anchoredPosition = new Vector2(0f, -scrollTopInset * 0.5f);
			availableScroll.sizeDelta = new Vector2(0f, -scrollTopInset);
		}
	}

	private static void FixEquipmentDropdownChrome(Transform _equipment)
	{
		if (_equipment == null)
			return;

		Transform content = FindDeepChild(_equipment, "ColumnContent") ?? _equipment;
		for (int i = 0; i < content.childCount; i++)
		{
			Transform child = content.GetChild(i);
			if (child != null && child.name == "Text (TMP)")
				child.gameObject.SetActive(false);
		}

		PinTopStrip(content.Find("PrepPresetDropdown") as RectTransform, 4f, 40f);
		PinTopStrip(content.Find("PrepArmorDropdown") as RectTransform, 48f, 40f);
		PinTopStrip(content.Find("PrepCamouflageDropdown") as RectTransform, 92f, 40f);

		const float scrollTopInset = 136f;
		RectTransform scroll = content.Find("PrepPresetEquipmentPanelScroll") as RectTransform;
		if (scroll != null)
		{
			scroll.anchorMin = Vector2.zero;
			scroll.anchorMax = Vector2.one;
			scroll.pivot = new Vector2(0.5f, 0.5f);
			scroll.anchoredPosition = new Vector2(0f, -scrollTopInset * 0.5f);
			scroll.sizeDelta = new Vector2(0f, -scrollTopInset);
		}
	}

	private static void PinTopStrip(RectTransform _rt, float _topInset, float _height)
	{
		if (_rt == null)
			return;

		_rt.anchorMin = new Vector2(0f, 1f);
		_rt.anchorMax = new Vector2(1f, 1f);
		_rt.pivot = new Vector2(0.5f, 1f);
		_rt.anchoredPosition = new Vector2(0f, -_topInset);
		_rt.sizeDelta = new Vector2(0f, _height);
	}

	private static void HideDuplicateColumnTitles(Transform _content)
	{
		if (_content == null)
			return;

		for (int i = 0; i < _content.childCount; i++)
		{
			Transform child = _content.GetChild(i);
			if (child != null && child.name == "Text (TMP)")
				child.gameObject.SetActive(false);
		}
	}

	private static void StretchNamedScroll(Transform _content)
	{
		if (_content == null)
			return;

		for (int i = 0; i < _content.childCount; i++)
		{
			Transform child = _content.GetChild(i);
			if (child == null || child.name.IndexOf("Scroll", System.StringComparison.OrdinalIgnoreCase) < 0)
				continue;
			if (child.name.IndexOf("Available", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
			    child.name.IndexOf("PresetEquipment", System.StringComparison.OrdinalIgnoreCase) >= 0)
				continue;

			RectTransform scroll = child as RectTransform;
			if (scroll == null)
				continue;

			scroll.anchorMin = Vector2.zero;
			scroll.anchorMax = Vector2.one;
			scroll.pivot = new Vector2(0.5f, 0.5f);
			scroll.anchoredPosition = Vector2.zero;
			scroll.sizeDelta = Vector2.zero;
		}
	}

	private static void SetPanelTitleKey(GameObject _panel, string _key, string _fallback)
	{
		if (_panel == null)
			return;

		// Only the collapse toggle label — never overwrite filter/button texts under the panel.
		Transform toggle = _panel.transform.Find("CollapseToggle");
		if (toggle == null)
			return;

		LocalizedTextMeshProUGUI[] locs = toggle.GetComponentsInChildren<LocalizedTextMeshProUGUI>(true);
		for (int i = 0; i < locs.Length; i++)
		{
			if (locs[i] == null)
				continue;
			locs[i].SetLocalizationKey(_key);
			TMP_Text tmp = locs[i].GetComponent<TMP_Text>();
			if (tmp != null)
				tmp.text = _fallback;
			EditorUtility.SetDirty(locs[i]);
		}
	}

	private static void SetupVehicleListClone(GameObject _vehicleList)
	{
		// Title localization
		LocalizedTextMeshProUGUI[] locs = _vehicleList.GetComponentsInChildren<LocalizedTextMeshProUGUI>(true);
		for (int i = 0; i < locs.Length; i++)
		{
			if (locs[i] != null && locs[i].TryGetLocalizationKey(out string key) &&
			    (key == "mission_prep.unit_list.title" ||
			     key == "mission_prep.column.vehicles" ||
			     key == "mission_prep.column.base"))
			{
				locs[i].SetLocalizationKey("mission_prep.column.base");
				TMP_Text tmp = locs[i].GetComponent<TMP_Text>();
				if (tmp != null)
					tmp.text = "На базе";
			}
		}

		// Rename scroll
		Transform scroll = FindDeepChild(_vehicleList.transform, "PrepUnitListScroll");
		if (scroll != null)
			scroll.name = "PrepVehicleListScroll";

		MissionPrepUnitListView listView = _vehicleList.GetComponentInChildren<MissionPrepUnitListView>(true);
		if (listView == null)
		{
			Transform scrollT = FindDeepChild(_vehicleList.transform, "PrepVehicleListScroll");
			if (scrollT != null)
				listView = scrollT.GetComponent<MissionPrepUnitListView>() ??
				           scrollT.gameObject.AddComponent<MissionPrepUnitListView>();
		}

		// Clear any leftover runtime children in content (keep Content empty for spawn).
		RectTransform content = FindContentUnder(_vehicleList.transform);
		if (content != null)
		{
			for (int i = content.childCount - 1; i >= 0; i--)
				Object.DestroyImmediate(content.GetChild(i).gameObject);
		}
	}

	private static void FixVehicleScrollChrome(GameObject _vehicleList)
	{
		if (_vehicleList == null)
			return;

		Transform scrollT = FindDeepChild(_vehicleList.transform, "PrepVehicleListScroll");
		if (scrollT == null)
			return;

		Image scrollImage = scrollT.GetComponent<Image>();
		if (scrollImage != null)
		{
			scrollImage.sprite = null;
			scrollImage.type = Image.Type.Simple;
			InventoryUiTheme.ApplyImageColor(scrollImage, InventoryUiTheme.ScrollInset);
			EditorUtility.SetDirty(scrollImage);
		}

		// Cloned from equipment panels with spacing=5 — that looked like a fat cell divider.
		RectTransform content = FindContentUnder(_vehicleList.transform);
		if (content != null)
		{
			VerticalLayoutGroup vlg = content.GetComponent<VerticalLayoutGroup>();
			if (vlg != null)
			{
				vlg.spacing = 0f;
				vlg.padding = new RectOffset(0, 0, 0, 0);
				vlg.childForceExpandHeight = false;
				EditorUtility.SetDirty(vlg);
			}
		}

		Transform viewport = FindDeepChild(scrollT, "Viewport");
		if (viewport == null)
			return;

		Mask legacyMask = viewport.GetComponent<Mask>();
		if (legacyMask != null)
			Object.DestroyImmediate(legacyMask);

		RectMask2D rectMask = viewport.GetComponent<RectMask2D>();
		if (rectMask == null)
			rectMask = viewport.gameObject.AddComponent<RectMask2D>();

		Image viewportImage = viewport.GetComponent<Image>();
		if (viewportImage != null)
		{
			viewportImage.sprite = null;
			viewportImage.type = Image.Type.Simple;
			viewportImage.color = new Color(1f, 1f, 1f, 0f);
			viewportImage.raycastTarget = true;
			EditorUtility.SetDirty(viewportImage);
		}

		EditorUtility.SetDirty(viewport.gameObject);
	}

	private static void WireCollapsible(GameObject _panel, string _locKey, string _fallback, float _height)
	{
		RectTransform panelRt = _panel.GetComponent<RectTransform>();
		MissionPrepCollapsibleColumn column = _panel.GetComponent<MissionPrepCollapsibleColumn>();
		if (column == null)
			column = _panel.AddComponent<MissionPrepCollapsibleColumn>();

		Transform content = _panel.transform.Find("ColumnContent");
		if (content == null)
		{
			GameObject contentGo = new GameObject("ColumnContent", typeof(RectTransform));
			content = contentGo.transform;
			content.SetParent(_panel.transform, false);
			// Move existing non-toggle children into content.
			for (int i = _panel.transform.childCount - 1; i >= 0; i--)
			{
				Transform child = _panel.transform.GetChild(i);
				if (child == content || child.name == "CollapseToggle")
					continue;
				child.SetParent(content, true);
			}
		}

		RectTransform contentRt = content as RectTransform;
		contentRt.anchorMin = Vector2.zero;
		contentRt.anchorMax = Vector2.one;
		contentRt.pivot = new Vector2(0.5f, 0.5f);
		// Leave room for CollapseToggle strip at the top.
		contentRt.offsetMin = Vector2.zero;
		contentRt.offsetMax = new Vector2(0f, -36f);

		Transform toggleT = _panel.transform.Find("CollapseToggle");
		GameObject toggleGo;
		if (toggleT == null)
		{
			toggleGo = new GameObject("CollapseToggle", typeof(RectTransform), typeof(Image), typeof(Button));
			toggleT = toggleGo.transform;
			toggleT.SetParent(_panel.transform, false);
			toggleT.SetAsFirstSibling();
		}
		else
		{
			toggleGo = toggleT.gameObject;
		}

		RectTransform toggleRt = toggleGo.GetComponent<RectTransform>();
		toggleRt.anchorMin = new Vector2(0f, 1f);
		toggleRt.anchorMax = new Vector2(1f, 1f);
		toggleRt.pivot = new Vector2(0.5f, 1f);
		toggleRt.anchoredPosition = Vector2.zero;
		toggleRt.sizeDelta = new Vector2(0f, 36f);

		Image img = toggleGo.GetComponent<Image>();
		InventoryUiTheme.ApplyImageColor(img, InventoryUiTheme.TitleBar);
		img.color = InventoryUiTheme.TitleBar;
		img.raycastTarget = true;

		Button button = toggleGo.GetComponent<Button>();
		ColorBlock colors = button.colors;
		colors.normalColor = Color.white;
		colors.highlightedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
		colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
		button.colors = colors;
		button.targetGraphic = img;

		Transform labelT = toggleT.Find("Label");
		TMP_Text label;
		if (labelT == null)
		{
			GameObject labelGo = new GameObject("Label", typeof(RectTransform));
			labelT = labelGo.transform;
			labelT.SetParent(toggleT, false);
			label = labelGo.AddComponent<TextMeshProUGUI>();
		}
		else
		{
			label = labelT.GetComponent<TMP_Text>();
			if (label == null)
				label = labelT.gameObject.AddComponent<TextMeshProUGUI>();
		}

		RectTransform labelRt = label.transform as RectTransform;
		labelRt.anchorMin = Vector2.zero;
		labelRt.anchorMax = Vector2.one;
		labelRt.offsetMin = new Vector2(6f, 2f);
		labelRt.offsetMax = new Vector2(-6f, -2f);
		label.fontSize = 14f;
		label.fontStyle = FontStyles.Bold;
		label.alignment = TextAlignmentOptions.MidlineLeft;
		label.color = InventoryUiTheme.PrimaryText;
		label.raycastTarget = false;
		label.text = _fallback;

		// Shrink content below toggle bar.
		contentRt.offsetMax = new Vector2(0f, -36f);

		column.Configure(
			contentRt,
			button,
			label,
			_locKey,
			_fallback,
			c_ColumnWidth,
			c_CollapsedWidth,
			_height,
			true);

		EditorUtility.SetDirty(_panel);
	}

	[MenuItem("Polygone/Mission Prep/Fix Columns Visuals (SampleScene)")]
	public static void FixVisualsFromMenu()
	{
		Scene scene = EditorSceneManager.OpenScene(c_ScenePath, OpenSceneMode.Single);
		GameObject screenRootGo = FindSceneObjectByName("MissionPrepScreenRoot");
		if (screenRootGo == null)
			throw new System.Exception("MissionPrepScreenRoot not found.");

		Transform vehicle = FindDeepChild(screenRootGo.transform, "PrepVehicleList");
		Transform stats = FindDeepChild(screenRootGo.transform, "PrepStatsPanel");
		Transform row = FindDeepChild(screenRootGo.transform, c_MarkerName);

		if (vehicle != null)
			FixVehicleScrollChrome(vehicle.gameObject);

		if (stats != null)
		{
			NormalizePanelRoot(stats as RectTransform, c_StatsColumnHeight);
			WireCollapsible(stats.gameObject, "mission_prep.column.stats", "Характеристики", c_StatsColumnHeight);
		}

		Transform available = FindDeepChild(screenRootGo.transform, "PrepAvailableEquipmentPanel");
		if (available != null)
			FixAvailableEquipmentChrome(available);

		Transform equipment = FindDeepChild(screenRootGo.transform, "PrepPresetEquipmentPanel");
		if (equipment != null)
			FixEquipmentDropdownChrome(equipment);

		MissionPrepCollapsibleColumn[] columns =
			screenRootGo.GetComponentsInChildren<MissionPrepCollapsibleColumn>(true);
		for (int i = 0; i < columns.Length; i++)
		{
			if (columns[i] == null)
				continue;
			Image img = columns[i].GetComponentInChildren<Button>(true)?.GetComponent<Image>();
			if (img != null)
				InventoryUiTheme.ApplyImageColor(img, InventoryUiTheme.TitleBar);

			Transform content = columns[i].ContentRoot != null
				? columns[i].ContentRoot
				: columns[i].transform.Find("ColumnContent");
			HideDuplicateColumnTitles(content);
			StretchNamedScroll(content);
		}

		InventoryUiScrollbarUtility.ConfigureAllUnder(screenRootGo.transform);

		if (row != null)
		{
			HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
			if (hlg != null)
				hlg.childAlignment = TextAnchor.LowerLeft;
		}

		EditorSceneManager.MarkSceneDirty(scene);
		EditorSceneManager.SaveScene(scene);
		Debug.Log("[MissionPrepColumns] Visual fixes saved.");
	}

	private static void WireSpawnerAndController(GameObject _vehicleList)
	{
		MissionPrepUnitListView vehicleListView =
			_vehicleList.GetComponentInChildren<MissionPrepUnitListView>(true);
		RectTransform vehicleContent = FindContentUnder(_vehicleList.transform);

		MissionPrepSquadSpawner spawner =
			Object.FindAnyObjectByType<MissionPrepSquadSpawner>(FindObjectsInactive.Include);
		if (spawner != null)
		{
			SerializedObject so = new SerializedObject(spawner);
			so.FindProperty("m_VehicleList").objectReferenceValue = vehicleListView;
			so.FindProperty("m_VehicleCellsContentParent").objectReferenceValue = vehicleContent;
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(spawner);
		}

		MissionPrepScreenController screen =
			Object.FindAnyObjectByType<MissionPrepScreenController>(FindObjectsInactive.Include);
		if (screen != null)
		{
			SerializedObject so = new SerializedObject(screen);
			so.FindProperty("m_VehicleList").objectReferenceValue = vehicleListView;
			so.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(screen);
		}
	}

	private static RectTransform FindContentUnder(Transform _root)
	{
		Transform scroll = FindDeepChild(_root, "PrepVehicleListScroll") ??
		                   FindDeepChild(_root, "PrepUnitListScroll");
		if (scroll == null)
			return null;

		ScrollRect sr = scroll.GetComponent<ScrollRect>();
		if (sr != null && sr.content != null)
			return sr.content;

		Transform viewport = scroll.Find("Viewport");
		if (viewport != null)
		{
			Transform content = viewport.Find("Content");
			if (content != null)
				return content as RectTransform;
		}

		return FindDeepChild(_root, "Content") as RectTransform;
	}

	private static void ReparentUnder(Transform _parent, Transform _child)
	{
		if (_child == null || _child.parent == _parent)
			return;
		_child.SetParent(_parent, false);
	}

	private static GameObject FindSceneObjectByName(string _name)
	{
		GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
		for (int i = 0; i < roots.Length; i++)
		{
			Transform found = FindDeepChild(roots[i].transform, _name);
			if (found != null)
				return found.gameObject;
		}

		return null;
	}

	private static Transform FindChild(Transform _parent, string _name)
	{
		if (_parent == null)
			return null;
		for (int i = 0; i < _parent.childCount; i++)
		{
			Transform child = _parent.GetChild(i);
			if (child.name == _name)
				return child;
		}

		return null;
	}

	private static Transform FindDeepChild(Transform _parent, string _name)
	{
		if (_parent == null)
			return null;
		if (_parent.name == _name)
			return _parent;
		for (int i = 0; i < _parent.childCount; i++)
		{
			Transform found = FindDeepChild(_parent.GetChild(i), _name);
			if (found != null)
				return found;
		}

		return null;
	}
}
#endif
