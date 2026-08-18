using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Общая палитра runtime-инвентаря и Mission Prep (этап 2 UX).
/// </summary>
public static class InventoryUiTheme
{
	public const float CellHeight = 48f;
	public const float IconSize = 40f;
	public const float CellFontSize = 16f;
	public const float SectionHeaderHeight = 28f;
	public const float DividerHeight = 1f;
	public const float CompactEmptyRowHeight = 32f;
	public const float CompactEmptyIconSize = 24f;
	public const float CompactEmptyFontSize = 12f;
	public const float CompactEmptyLeftIndent = 20f;

	// Opaque panel chrome — semi-transparent alphas looked uneven over the 3D scene.
	public static readonly Color PanelBackground = new Color(0.11764706f, 0.11764706f, 0.11764706f, 1f);
	public static readonly Color ScrollInset = new Color(0.07058824f, 0.07058824f, 0.07058824f, 1f);
	public static readonly Color CellBackground = new Color(0.16470589f, 0.16470589f, 0.16470589f, 1f);
	public static readonly Color CellHover = new Color(0.20392157f, 0.20392157f, 0.20392157f, 1f);
	/// <summary>Приглушённый оливковый акцент совместимости (вместо кислотно-зелёного).</summary>
	public static readonly Color CompatibleHighlight = new Color(0.29f, 0.42f, 0.33f, 0.50f);
	public static readonly Color TitleBar = new Color(0.08627451f, 0.08627451f, 0.08627451f, 1f);
	public static readonly Color TooltipBackground = new Color(0.07843138f, 0.07843138f, 0.07843138f, 1f);
	public static readonly Color Divider = new Color(1f, 1f, 1f, 0.08f);
	public static readonly Color SectionHeaderText = new Color(0.78f, 0.78f, 0.78f, 1f);

	public static readonly Color UnitCellNormal = CellBackground;
	public static readonly Color UnitCellHover = CellHover;
	/// <summary>Выделение выбранного юнита — в той же палитре, без синего «выпадающего» акцента.</summary>
	public static readonly Color UnitCellSelected = new Color(0.27f, 0.36f, 0.31f, 1f);
	/// <summary>Юнит закреплён за машиной — тёмная «готовая» ячейка, как title bar.</summary>
	public static readonly Color UnitCellAssigned = TitleBar;
	public static readonly Color UnitCellAssignedHover = CellBackground;
	/// <summary>Юнит вне машины — светлее, чтобы было видно, что место ещё не занято.</summary>
	public static readonly Color UnitCellUnassigned = new Color(0.22f, 0.22f, 0.22f, 1f);
	public static readonly Color UnitCellUnassignedHover = new Color(0.26f, 0.26f, 0.26f, 1f);
	public const float UnitCellHeight = 80f;
	public const float UnitCellWidth = 400f;

	/// <summary>Текст на панелях действий / контекстных меню.</summary>
	public static readonly Color PrimaryText = new Color(0.9f, 0.9f, 0.9f, 1f);
	/// <summary>Подпись горячей клавиши — приглушённый оливковый, без синего акцента.</summary>
	public static readonly Color HotkeyText = new Color(0.55f, 0.68f, 0.58f, 1f);
	/// <summary>Hover кнопок action-bar / ПКМ-меню (opaque, как выделение ячеек).</summary>
	public static readonly Color MenuItemHover = UnitCellSelected;
	public static readonly Color MenuItemPressed = new Color(0.12f, 0.12f, 0.12f, 1f);

	public static void ApplyImageColor(Image _image, Color _color)
	{
		if (_image == null)
			return;

		InventorySlotUiUtility.EnsureImageCanRenderSolidColor(_image);
		_image.color = _color;
		_image.enabled = true;
	}

	/// <summary>
	/// Фон панели. Viewport: убираем UIMask-скругление (обрезало ячейки) и не заливаем inset-цветом.
	/// </summary>
	public static void ApplyPanelChrome(GameObject _panelRoot)
	{
		if (_panelRoot == null)
			return;

		if (_panelRoot.TryGetComponent(out Image panelImage))
			ApplyImageColor(panelImage, PanelBackground);

		ScrollRect[] scrolls = _panelRoot.GetComponentsInChildren<ScrollRect>(true);
		for (int i = 0; i < scrolls.Length; i++)
		{
			ScrollRect scroll = scrolls[i];
			if (scroll == null || scroll.viewport == null)
				continue;

			bool isDropdownTemplate = IsUnderDropdownTemplate(scroll.transform);
			FixScrollViewportMask(scroll.viewport);

			if (isDropdownTemplate)
			{
				FixDropdownTemplateChrome(scroll.transform);
				continue;
			}

			DisableHorizontalScrollbar(scroll);
			scroll.scrollSensitivity = InventoryPanelView.c_DefaultScrollSensitivity;
			InventoryUiScrollbarUtility.ConfigureScrollRect(scroll);
		}
	}

	/// <summary>Убирает скруглённую Knob/UIMask-рамку у выпадающего списка TMP_Dropdown.</summary>
	private static void FixDropdownTemplateChrome(Transform _templateOrChild)
	{
		Transform template = _templateOrChild;
		while (template != null && template.name != "Template")
			template = template.parent;
		if (template == null)
			return;

		if (template.TryGetComponent(out Image templateImage))
		{
			InventorySlotUiUtility.EnsureImageCanRenderSolidColor(templateImage);
			templateImage.color = new Color(0.12f, 0.12f, 0.12f, 0.98f);
			templateImage.type = Image.Type.Simple;
		}

		Transform viewport = template.Find("Viewport");
		if (viewport != null)
			FixScrollViewportMask(viewport as RectTransform);
	}

	private static void DisableHorizontalScrollbar(ScrollRect _scroll)
	{
		if (_scroll == null)
			return;

		_scroll.horizontal = false;
		_scroll.horizontalScrollbar = null;

		Transform horizontal = _scroll.transform.Find("Scrollbar Horizontal");
		if (horizontal != null && horizontal.gameObject.activeSelf)
			horizontal.gameObject.SetActive(false);
	}

	private static bool IsUnderDropdownTemplate(Transform _t)
	{
		Transform cur = _t;
		while (cur != null)
		{
			if (cur.name == "Template")
				return true;
			cur = cur.parent;
		}

		return false;
	}

	private static void FixScrollViewportMask(RectTransform _viewport)
	{
		if (_viewport == null)
			return;

		if (_viewport.TryGetComponent(out Image viewportImage))
		{
			viewportImage.sprite = null;
			viewportImage.type = Image.Type.Simple;
			viewportImage.color = new Color(1f, 1f, 1f, 0f);
			viewportImage.raycastTarget = true;
		}

		if (_viewport.TryGetComponent(out Mask mask))
		{
			mask.showMaskGraphic = false;
			mask.enabled = false;
		}
	}
}
