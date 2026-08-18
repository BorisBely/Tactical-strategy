using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Thin themed vertical scrollbars + correct Content layout so lists actually scroll.
/// </summary>
public static class InventoryUiScrollbarUtility
{
	public const float ScrollbarWidth = 10f;
	public const float ScrollbarSpacing = 2f;
	/// <summary>Extra Handle padding beyond the sliding-area rect (Unity default is 20 — too fat).</summary>
	public const float HandlePadX = 2f;
	public const float HandlePadY = 12f;

	public static readonly Color TrackColor = new Color(0.07f, 0.07f, 0.07f, 1f);
	public static readonly Color HandleColor = new Color(0.35f, 0.42f, 0.37f, 0.95f);
	public static readonly Color HandleHoverColor = new Color(0.45f, 0.55f, 0.48f, 1f);
	public static readonly Color HandlePressedColor = new Color(0.28f, 0.34f, 0.30f, 1f);

	public static void ConfigureScrollRect(ScrollRect _scroll)
	{
		if (_scroll == null)
			return;

		_scroll.horizontal = false;
		_scroll.horizontalScrollbar = null;
		_scroll.vertical = true;
		_scroll.movementType = ScrollRect.MovementType.Clamped;
		_scroll.inertia = true;
		_scroll.scrollSensitivity = Mathf.Max(_scroll.scrollSensitivity, 40f);
		_scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
		_scroll.verticalScrollbarSpacing = ScrollbarSpacing;

		Transform horizontal = _scroll.transform.Find("Scrollbar Horizontal");
		if (horizontal != null && horizontal.gameObject.activeSelf)
			horizontal.gameObject.SetActive(false);

		if (_scroll.viewport != null)
			FixViewport(_scroll.viewport);

		if (_scroll.content != null)
			FixScrollContent(_scroll.content);

		if (_scroll.verticalScrollbar != null)
			StyleVerticalScrollbar(_scroll.verticalScrollbar);
	}

	public static void ConfigureAllUnder(Transform _root)
	{
		if (_root == null)
			return;

		ScrollRect[] scrolls = _root.GetComponentsInChildren<ScrollRect>(true);
		for (int i = 0; i < scrolls.Length; i++)
		{
			ScrollRect scroll = scrolls[i];
			if (scroll == null || IsUnderDropdownTemplate(scroll.transform))
				continue;
			ConfigureScrollRect(scroll);
		}
	}

	public static void FixScrollContent(RectTransform _content)
	{
		if (_content == null)
			return;

		// Top-stretched width, height driven by ContentSizeFitter — never stretch vertically
		// with a huge negative sizeDelta (that made squad content shorter than the viewport).
		_content.anchorMin = new Vector2(0f, 1f);
		_content.anchorMax = new Vector2(1f, 1f);
		_content.pivot = new Vector2(0f, 1f);
		_content.anchoredPosition = Vector2.zero;
		_content.sizeDelta = new Vector2(0f, Mathf.Max(0f, _content.sizeDelta.y));
		_content.localScale = Vector3.one;

		VerticalLayoutGroup vlg = _content.GetComponent<VerticalLayoutGroup>();
		if (vlg != null)
		{
			vlg.spacing = 0f;
			vlg.padding = new RectOffset(0, 0, 0, 0);
			vlg.childAlignment = TextAnchor.UpperLeft;
			vlg.reverseArrangement = false;
			// Don't control width via LayoutElement — UnitCell may not have preferredWidth.
			vlg.childControlWidth = false;
			vlg.childControlHeight = true;
			vlg.childForceExpandWidth = true;
			vlg.childForceExpandHeight = false;
		}

		ContentSizeFitter fitter = _content.GetComponent<ContentSizeFitter>();
		if (fitter == null)
			fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
	}

	public static void StyleVerticalScrollbar(Scrollbar _scrollbar)
	{
		if (_scrollbar == null)
			return;

		RectTransform barRt = _scrollbar.transform as RectTransform;
		if (barRt != null)
		{
			barRt.anchorMin = new Vector2(1f, 0f);
			barRt.anchorMax = new Vector2(1f, 1f);
			barRt.pivot = new Vector2(1f, 1f);
			barRt.anchoredPosition = Vector2.zero;
			barRt.sizeDelta = new Vector2(ScrollbarWidth, 0f);
		}

		_scrollbar.direction = Scrollbar.Direction.BottomToTop;
		_scrollbar.transition = Selectable.Transition.ColorTint;

		ColorBlock colors = _scrollbar.colors;
		colors.normalColor = Color.white;
		colors.highlightedColor = new Color(1f, 1f, 1f, 0.92f);
		colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
		colors.selectedColor = Color.white;
		colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
		colors.colorMultiplier = 1f;
		_scrollbar.colors = colors;

		Image track = _scrollbar.GetComponent<Image>();
		if (track != null)
		{
			// Keep the built-in Background sprite (old look), only tint the track.
			ApplyBuiltinUiSprite(track, "UI/Skin/Background.psd", Image.Type.Sliced);
			track.color = TrackColor;
			track.raycastTarget = true;
		}

		// Sliding area fills the track (small inset so the handle sits inside).
		Transform slidingArea = _scrollbar.transform.Find("Sliding Area");
		if (slidingArea != null)
		{
			RectTransform areaRt = slidingArea as RectTransform;
			areaRt.anchorMin = Vector2.zero;
			areaRt.anchorMax = Vector2.one;
			areaRt.offsetMin = new Vector2(1f, 1f);
			areaRt.offsetMax = new Vector2(-1f, -1f);
		}

		if (_scrollbar.handleRect != null)
		{
			RectTransform handleRt = _scrollbar.handleRect;
			// sizeDelta pads the knob: X widens past the track, Y lengthens the thumb.
			// Default (20,20) was far too wide; (0,0) looked tiny — use a moderate pad.
			handleRt.sizeDelta = new Vector2(HandlePadX, HandlePadY);

			if (handleRt.TryGetComponent(out Image handle))
			{
				// Restore the default UISprite knob instead of a solid 1×1 fill.
				ApplyBuiltinUiSprite(handle, "UI/Skin/UISprite.psd", Image.Type.Sliced);
				handle.color = HandleColor;
				handle.pixelsPerUnitMultiplier = 1f;
				_scrollbar.targetGraphic = handle;
			}
		}
	}

	private static void ApplyBuiltinUiSprite(Image _image, string _builtinPath, Image.Type _type)
	{
		if (_image == null)
			return;

		Sprite solid = InventorySlotUiUtility.GetSolidUISprite();
		bool needsBuiltin = _image.sprite == null || _image.sprite == solid;
		if (needsBuiltin)
		{
			Sprite builtin = Resources.GetBuiltinResource<Sprite>(_builtinPath);
			if (builtin != null)
				_image.sprite = builtin;
			else if (_image.sprite == null)
				_image.sprite = solid;
		}

		_image.type = _type;
	}

	private static void FixViewport(RectTransform _viewport)
	{
		if (_viewport == null)
			return;

		// Legacy Mask + UIMask sprite draws a rounded frame. RectMask2D clips without chrome.
		if (_viewport.TryGetComponent(out Mask mask))
		{
			mask.showMaskGraphic = false;
			mask.enabled = false;
			Object.Destroy(mask);
		}

		if (_viewport.GetComponent<RectMask2D>() == null)
			_viewport.gameObject.AddComponent<RectMask2D>();

		if (_viewport.TryGetComponent(out Image viewportImage))
		{
			viewportImage.sprite = null;
			viewportImage.type = Image.Type.Simple;
			viewportImage.color = new Color(1f, 1f, 1f, 0f);
			viewportImage.raycastTarget = true;
		}
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
}
