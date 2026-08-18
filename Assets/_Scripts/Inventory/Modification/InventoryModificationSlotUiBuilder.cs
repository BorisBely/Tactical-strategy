using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Вёрстка строки слота мода как у ячейки инвентаря: ширина 400, [иконка] [название].
/// Явная ширина обязательна: Content + ContentSizeFitter(Preferred) + VLG без ControlWidth
/// схлопывает stretch-якоря с sizeDelta.x=0.
/// </summary>
public static class InventoryModificationSlotUiBuilder
{
	public const float RowHeight = InventoryUiTheme.CellHeight;
	public const float RowWidth = 400f;
	private const float c_IconSize = InventoryUiTheme.IconSize;
	private const float c_LeftIndent = 20f;

	public static void BuildRow(
		GameObject _row,
		Color _backgroundColor,
		out Image _background,
		out Image _icon,
		out TMP_Text _slotLabel,
		out TMP_Text _itemLabel)
	{
		RectTransform rowRt = _row.GetComponent<RectTransform>();
		if (rowRt == null)
			rowRt = _row.AddComponent<RectTransform>();

		// Как InventoryCell / SectionHeader: top-left + явная ширина (не stretch).
		rowRt.anchorMin = new Vector2(0f, 1f);
		rowRt.anchorMax = new Vector2(0f, 1f);
		rowRt.pivot = new Vector2(0f, 1f);
		rowRt.anchoredPosition = Vector2.zero;
		rowRt.sizeDelta = new Vector2(RowWidth, RowHeight);
		rowRt.localScale = Vector3.one;

		LayoutElement rowLayout = _row.GetComponent<LayoutElement>();
		if (rowLayout == null)
			rowLayout = _row.AddComponent<LayoutElement>();
		rowLayout.minHeight = RowHeight;
		rowLayout.preferredHeight = RowHeight;
		rowLayout.flexibleHeight = 0f;
		rowLayout.minWidth = RowWidth;
		rowLayout.preferredWidth = RowWidth;
		rowLayout.flexibleWidth = 0f;

		_background = _row.GetComponent<Image>();
		if (_background == null)
			_background = _row.AddComponent<Image>();
		InventorySlotUiUtility.EnsureImageCanRenderSolidColor(_background);
		_background.color = _backgroundColor;
		_background.raycastTarget = true;

		// Без HLG: абсолютная раскладка как у InventoryCell, чтобы текст занял всю ширину строки.
		HorizontalLayoutGroup existingLayout = _row.GetComponent<HorizontalLayoutGroup>();
		if (existingLayout != null)
			Object.Destroy(existingLayout);

		_slotLabel = null;
		_icon = EnsureIcon("ItemIcon", _row.transform);
		_itemLabel = EnsureNameText("ItemLabel", _row.transform);
		EnsureDivider(_row.transform);
	}

	private static TMP_Text EnsureNameText(string _name, Transform _parent)
	{
		Transform existing = _parent.Find(_name);
		GameObject go = existing != null ? existing.gameObject : new GameObject(_name, typeof(RectTransform));
		if (existing == null)
			go.transform.SetParent(_parent, false);

		RectTransform rt = go.transform as RectTransform;
		rt.anchorMin = Vector2.zero;
		rt.anchorMax = Vector2.one;
		rt.pivot = new Vector2(0.5f, 0.5f);
		// Слева место под иконку + indent 20; справа небольшой отступ.
		float leftInset = c_LeftIndent + c_IconSize + 8f;
		rt.anchoredPosition = new Vector2((leftInset - 8f) * 0.5f, 0f);
		rt.sizeDelta = new Vector2(-(leftInset + 8f), -6f);

		TMP_Text text = go.GetComponent<TextMeshProUGUI>();
		if (text == null)
			text = go.AddComponent<TextMeshProUGUI>();
		text.fontSize = InventoryUiTheme.CellFontSize;
		text.color = Color.white;
		text.alignment = TextAlignmentOptions.MidlineLeft;
		text.textWrappingMode = TextWrappingModes.NoWrap;
		text.overflowMode = TextOverflowModes.Overflow;
		text.raycastTarget = false;

		LayoutElement layout = go.GetComponent<LayoutElement>();
		if (layout != null)
			Object.Destroy(layout);

		return text;
	}

	private static Image EnsureIcon(string _name, Transform _parent)
	{
		Transform existing = _parent.Find(_name);
		GameObject go = existing != null ? existing.gameObject : new GameObject(_name, typeof(RectTransform));
		if (existing == null)
			go.transform.SetParent(_parent, false);

		RectTransform rt = go.transform as RectTransform;
		rt.anchorMin = new Vector2(0f, 0.5f);
		rt.anchorMax = new Vector2(0f, 0.5f);
		rt.pivot = new Vector2(0.5f, 0.5f);
		rt.anchoredPosition = new Vector2(c_LeftIndent + c_IconSize * 0.5f, 0f);
		rt.sizeDelta = new Vector2(c_IconSize, c_IconSize);

		Image image = go.GetComponent<Image>();
		if (image == null)
			image = go.AddComponent<Image>();
		image.preserveAspect = true;
		image.raycastTarget = false;
		image.enabled = false;

		LayoutElement layout = go.GetComponent<LayoutElement>();
		if (layout != null)
			Object.Destroy(layout);

		return image;
	}

	private static void EnsureDivider(Transform _parent)
	{
		Transform existing = _parent.Find("Image");
		GameObject go = existing != null ? existing.gameObject : new GameObject("Image", typeof(RectTransform));
		if (existing == null)
			go.transform.SetParent(_parent, false);

		RectTransform rt = go.transform as RectTransform;
		rt.anchorMin = new Vector2(0f, 0f);
		rt.anchorMax = new Vector2(1f, 0f);
		rt.pivot = new Vector2(0.5f, 0.5f);
		rt.anchoredPosition = new Vector2(0f, 0.5f);
		rt.sizeDelta = new Vector2(0f, InventoryUiTheme.DividerHeight);

		Image image = go.GetComponent<Image>();
		if (image == null)
			image = go.AddComponent<Image>();
		InventorySlotUiUtility.EnsureImageCanRenderSolidColor(image);
		image.color = InventoryUiTheme.Divider;
		image.raycastTarget = false;
	}
}
