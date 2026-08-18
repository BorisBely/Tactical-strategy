using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Блок места в машине: заголовок + компактный пустой слот (как мод h=32) или хост под UnitCell (h=80).
/// </summary>
public static class MissionPrepVehicleSeatSlotUiBuilder
{
	public const float EmptyRowHeight = 32f;
	public const float CellHeight = 80f;
	public const float CellWidth = 400f;
	public const float LeftIndent = 20f;
	private const float c_IconSize = 24f;

	public static void BuildBlock(
		GameObject _root,
		out InventoryPanelSectionHeader _seatHeader,
		out RectTransform _contentHost,
		out Image _emptyBackground,
		out TMP_Text _emptyLabel)
	{
		RectTransform rootRt = _root.GetComponent<RectTransform>();
		if (rootRt == null)
			rootRt = _root.AddComponent<RectTransform>();

		rootRt.anchorMin = new Vector2(0f, 1f);
		rootRt.anchorMax = new Vector2(0f, 1f);
		rootRt.pivot = new Vector2(0f, 1f);
		rootRt.anchoredPosition = Vector2.zero;
		rootRt.sizeDelta = new Vector2(CellWidth, InventoryUiTheme.SectionHeaderHeight + EmptyRowHeight);
		rootRt.localScale = Vector3.one;

		LayoutElement rootLayout = _root.GetComponent<LayoutElement>();
		if (rootLayout == null)
			rootLayout = _root.AddComponent<LayoutElement>();
		rootLayout.minWidth = CellWidth;
		rootLayout.preferredWidth = CellWidth;
		rootLayout.flexibleWidth = 0f;
		ApplyRootHeight(rootLayout, EmptyRowHeight);

		VerticalLayoutGroup vlg = _root.GetComponent<VerticalLayoutGroup>();
		if (vlg == null)
			vlg = _root.AddComponent<VerticalLayoutGroup>();
		vlg.padding = new RectOffset(0, 0, 0, 0);
		vlg.spacing = 0f;
		vlg.childAlignment = TextAnchor.UpperLeft;
		vlg.childControlWidth = true;
		vlg.childControlHeight = true;
		vlg.childForceExpandWidth = true;
		vlg.childForceExpandHeight = false;

		_seatHeader = InventoryPanelSectionHeader.Ensure(
			_root.transform,
			"SeatHeader",
			string.Empty,
			string.Empty);

		GameObject hostGo = EnsureChild(_root.transform, "ContentHost");
		_contentHost = hostGo.GetComponent<RectTransform>();
		LayoutElement hostLayout = hostGo.GetComponent<LayoutElement>();
		if (hostLayout == null)
			hostLayout = hostGo.AddComponent<LayoutElement>();
		ApplyContentHeight(hostLayout, EmptyRowHeight);

		GameObject emptyGo = EnsureChild(hostGo.transform, "EmptyDrop");
		RectTransform emptyRt = emptyGo.GetComponent<RectTransform>();
		emptyRt.anchorMin = new Vector2(0f, 1f);
		emptyRt.anchorMax = new Vector2(0f, 1f);
		emptyRt.pivot = new Vector2(0f, 1f);
		emptyRt.anchoredPosition = Vector2.zero;
		emptyRt.sizeDelta = new Vector2(CellWidth, EmptyRowHeight);
		emptyRt.localScale = Vector3.one;

		LayoutElement emptyLayout = emptyGo.GetComponent<LayoutElement>();
		if (emptyLayout == null)
			emptyLayout = emptyGo.AddComponent<LayoutElement>();
		emptyLayout.minHeight = EmptyRowHeight;
		emptyLayout.preferredHeight = EmptyRowHeight;
		emptyLayout.flexibleHeight = 0f;
		emptyLayout.minWidth = CellWidth;
		emptyLayout.preferredWidth = CellWidth;

		_emptyBackground = emptyGo.GetComponent<Image>();
		if (_emptyBackground == null)
			_emptyBackground = emptyGo.AddComponent<Image>();
		InventorySlotUiUtility.EnsureImageCanRenderSolidColor(_emptyBackground);
		_emptyBackground.color = InventoryUiTheme.TitleBar;
		_emptyBackground.raycastTarget = true;

		EnsureIcon("ItemIcon", emptyGo.transform);

		GameObject labelGo = EnsureChild(emptyGo.transform, "ItemLabel");
		RectTransform labelRt = labelGo.GetComponent<RectTransform>();
		float leftInset = LeftIndent + c_IconSize + 6f;
		labelRt.anchorMin = Vector2.zero;
		labelRt.anchorMax = Vector2.one;
		labelRt.pivot = new Vector2(0.5f, 0.5f);
		labelRt.anchoredPosition = new Vector2((leftInset - 6f) * 0.5f, 0f);
		labelRt.sizeDelta = new Vector2(-(leftInset + 6f), -4f);

		_emptyLabel = labelGo.GetComponent<TextMeshProUGUI>();
		if (_emptyLabel == null)
			_emptyLabel = labelGo.AddComponent<TextMeshProUGUI>();
		_emptyLabel.fontSize = 12f;
		_emptyLabel.color = InventoryUiTheme.PrimaryText;
		_emptyLabel.alignment = TextAlignmentOptions.MidlineLeft;
		_emptyLabel.textWrappingMode = TextWrappingModes.NoWrap;
		_emptyLabel.overflowMode = TextOverflowModes.Ellipsis;
		_emptyLabel.raycastTarget = false;
	}

	public static void ApplyContentHeight(LayoutElement _hostLayout, float _contentHeight)
	{
		if (_hostLayout == null)
			return;

		_hostLayout.minHeight = _contentHeight;
		_hostLayout.preferredHeight = _contentHeight;
		_hostLayout.flexibleHeight = 0f;
		_hostLayout.minWidth = CellWidth;
		_hostLayout.preferredWidth = CellWidth;
	}

	public static void ApplyRootHeight(LayoutElement _rootLayout, float _contentHeight)
	{
		if (_rootLayout == null)
			return;

		float total = InventoryUiTheme.SectionHeaderHeight + _contentHeight;
		_rootLayout.minHeight = total;
		_rootLayout.preferredHeight = total;
		_rootLayout.flexibleHeight = 0f;
	}

	public static void LayoutOccupiedUnitCell(RectTransform _rt)
	{
		if (_rt == null)
			return;

		// Keep UnitCell native top-left size — stretch anchors break its internal layout.
		_rt.anchorMin = new Vector2(0f, 1f);
		_rt.anchorMax = new Vector2(0f, 1f);
		_rt.pivot = new Vector2(0f, 1f);
		_rt.anchoredPosition = Vector2.zero;
		_rt.sizeDelta = new Vector2(CellWidth, CellHeight);
		_rt.localScale = Vector3.one;

		LayoutElement layout = _rt.GetComponent<LayoutElement>();
		if (layout == null)
			layout = _rt.gameObject.AddComponent<LayoutElement>();
		layout.minHeight = CellHeight;
		layout.preferredHeight = CellHeight;
		layout.flexibleHeight = 0f;
		layout.minWidth = CellWidth;
		layout.preferredWidth = CellWidth;
		layout.flexibleWidth = 0f;

		// Button/Image divider must stay pinned after reparent into seat host.
		Transform divider = _rt.Find("Button/Image");
		if (divider != null && divider.TryGetComponent(out Image dividerImage))
		{
			InventorySlotUiUtility.EnsureImageCanRenderSolidColor(dividerImage);
			dividerImage.type = Image.Type.Simple;
			dividerImage.color = InventoryUiTheme.Divider;
			dividerImage.raycastTarget = false;

			const float dividerHeight = 3f;
			RectTransform dividerRt = divider as RectTransform;
			dividerRt.anchorMin = new Vector2(0f, 0f);
			dividerRt.anchorMax = new Vector2(1f, 0f);
			dividerRt.pivot = new Vector2(0.5f, 0.5f);
			dividerRt.anchoredPosition = new Vector2(0f, dividerHeight * 0.5f);
			dividerRt.sizeDelta = new Vector2(0f, dividerHeight);
			dividerRt.localScale = Vector3.one;
		}
	}

	private static void EnsureIcon(string _name, Transform _parent)
	{
		Transform existing = _parent.Find(_name);
		GameObject go = existing != null ? existing.gameObject : new GameObject(_name, typeof(RectTransform));
		if (existing == null)
			go.transform.SetParent(_parent, false);

		RectTransform rt = go.transform as RectTransform;
		rt.anchorMin = new Vector2(0f, 0.5f);
		rt.anchorMax = new Vector2(0f, 0.5f);
		rt.pivot = new Vector2(0.5f, 0.5f);
		rt.anchoredPosition = new Vector2(LeftIndent + c_IconSize * 0.5f, 0f);
		rt.sizeDelta = new Vector2(c_IconSize, c_IconSize);

		Image image = go.GetComponent<Image>();
		if (image == null)
			image = go.AddComponent<Image>();
		image.preserveAspect = true;
		image.raycastTarget = false;
		image.enabled = false;
	}

	private static GameObject EnsureChild(Transform _parent, string _name)
	{
		Transform existing = _parent.Find(_name);
		if (existing != null)
			return existing.gameObject;

		GameObject go = new GameObject(_name, typeof(RectTransform));
		go.transform.SetParent(_parent, false);
		return go;
	}
}
