using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Фон ячейки, подсветка и попадание курсора для слотов инвентаря (в т.ч. экипировки).
/// </summary>
public static class InventorySlotUiUtility
{
	#region Constants
	public const string EquipHighlightOverlayObjectName = "EquipHighlightOverlay";
	#endregion

	#region Private Fields
	private static readonly Vector3[] s_WorldCorners = new Vector3[4];
	private static readonly List<RaycastResult> s_RaycastResults = new List<RaycastResult>(16);
	private static Sprite s_SolidWhiteSprite;
	#endregion

	#region Public Methods
	public static InventorySlotView GetMainHandEquipmentSlot(InventoryPanelView _panel)
	{
		if (_panel == null || _panel.LeadingEquipmentSlotCount <= 0)
			return null;

		_panel.RefreshSlotsFromHierarchy();
		IReadOnlyList<InventorySlotView> slots = _panel.Slots;
		return slots.Count > 0 ? slots[0] : null;
	}

	/// <summary>1×1 спрайт для заливки Image без назначенного sprite в префабе.</summary>
	public static Sprite GetSolidUISprite()
	{
		if (s_SolidWhiteSprite != null)
			return s_SolidWhiteSprite;

		var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
		texture.SetPixel(0, 0, Color.white);
		texture.Apply();
		s_SolidWhiteSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 100f);
		return s_SolidWhiteSprite;
	}

	public static void EnsureImageCanRenderSolidColor(Image _image)
	{
		if (_image == null)
			return;

		if (_image.sprite == null)
			_image.sprite = GetSolidUISprite();

		_image.type = Image.Type.Simple;
	}

	public static bool TryGetSlotBackgroundImage(InventorySlotView _slot, out Image _backgroundImage)
	{
		_backgroundImage = null;
		if (_slot == null)
			return false;

		_backgroundImage = _slot.GetComponent<Image>();
		if (_backgroundImage != null)
			return true;

		Image[] images = _slot.GetComponentsInChildren<Image>(true);
		for (int i = 0; i < images.Length; i++)
		{
			Image candidate = images[i];
			if (candidate == null || candidate.gameObject == _slot.gameObject)
				continue;

			if (candidate.gameObject.name == EquipHighlightOverlayObjectName)
				continue;

			_backgroundImage = candidate;
			return true;
		}

		_backgroundImage = _slot.gameObject.AddComponent<Image>();
		_backgroundImage.color = MissionPrepInventoryUiColors.CellBackground;
		StretchToParent(_slot.transform as RectTransform);
		return true;
	}

	public static void EnsureEquipmentSlotDropTarget(InventorySlotView _slot)
	{
		if (_slot == null)
			return;

		if (!TryGetSlotBackgroundImage(_slot, out Image background))
			return;

		Image[] images = _slot.GetComponentsInChildren<Image>(true);
		for (int i = 0; i < images.Length; i++)
		{
			Image image = images[i];
			if (image == null)
				continue;

			image.raycastTarget = image == background;
		}

		Canvas.ForceUpdateCanvases();
	}

	public static void ApplySlotBackgroundColor(InventorySlotView _slot, Color _color)
	{
		if (TryGetSlotBackgroundImage(_slot, out Image background))
			background.color = _color;
	}

	public static InventoryEquipmentSlotAppearance ResolveEquipmentSlotAppearance(InventorySlotView _slot)
	{
		if (_slot == null)
			return new InventoryEquipmentSlotAppearance();

		InventoryPanelView panel = _slot.GetComponentInParent<InventoryPanelView>();
		return panel != null ? panel.EquipmentSlotAppearance : new InventoryEquipmentSlotAppearance();
	}

	/// <summary>Вызывается при SpawnNewSlotFromPrefab для leading-ячейки 0 (слот экипировки).</summary>
	public static void ConfigureMainHandEquipmentSlot(
		InventorySlotView _slot,
		InventoryEquipmentSlotAppearance _appearance)
	{
		if (_slot == null || _appearance == null)
			return;

		_appearance.ApplyNormal(_slot);
		EnsureEquipmentSlotDropTarget(_slot);
		DisableTextRaycastTargets(_slot);

		InventoryEquipmentSlotHighlightOverlay overlay = _slot.GetComponent<InventoryEquipmentSlotHighlightOverlay>();
		if (overlay == null)
			overlay = _slot.gameObject.AddComponent<InventoryEquipmentSlotHighlightOverlay>();

		overlay.EnsureOverlay();
		overlay.SetHighlighted(false);
	}

	public static bool IsWeaponEquipDragActive()
	{
		RuntimeInventoryModificationDragPayload runtimePayload = RuntimeInventoryModificationDragContext.Current;
		if (RuntimeInventoryModificationDragContext.IsWeaponEquipDragSource(runtimePayload.SourceKind) &&
		    WeaponEquipUtility.CanEquipToMainHand(runtimePayload.Item))
			return true;

		MissionPrepModificationDragPayload missionPrepPayload = MissionPrepModificationDragContext.Current;
		return MissionPrepWeaponEquipUtility.IsWeaponEquipDragSource(missionPrepPayload.SourceKind) &&
		       MissionPrepWeaponEquipUtility.CanEquipToMainHand(missionPrepPayload.Item);
	}

	public static void RefreshMainHandEquipHighlight(InventoryPanelView _panel)
	{
		InventorySlotView mainHandSlot = GetMainHandEquipmentSlot(_panel);
		if (mainHandSlot == null)
			return;

		ApplyMainHandEquipmentSlotHighlight(mainHandSlot, IsWeaponEquipDragActive());
	}

	public static void ApplyMainHandEquipmentSlotHighlight(InventorySlotView _slot, bool _highlighted)
	{
		if (_slot == null)
			return;

		InventoryEquipmentSlotHighlightOverlay overlay = _slot.GetComponent<InventoryEquipmentSlotHighlightOverlay>();
		if (overlay != null)
		{
			overlay.SetHighlighted(_highlighted);
			return;
		}

		InventoryEquipmentSlotAppearance appearance = ResolveEquipmentSlotAppearance(_slot);
		if (_highlighted)
			appearance.ApplyHighlight(_slot);
		else
			appearance.ApplyNormal(_slot);
	}

	public static bool IsScreenPointOverSlot(
		InventorySlotView _slot,
		Vector2 _screenPosition,
		Camera _eventCamera)
	{
		if (_slot == null)
			return false;

		RectTransform rect = _slot.transform as RectTransform;
		if (rect != null && IsScreenPointInsideRectTransform(rect, _screenPosition, _eventCamera))
			return true;

		return IsScreenPointOverSlotRaycast(_slot, _screenPosition);
	}

	public static bool IsScreenPointOverSlotRaycast(InventorySlotView _slot, Vector2 _screenPosition)
	{
		if (_slot == null || EventSystem.current == null)
			return false;

		s_RaycastResults.Clear();
		var pointerData = new PointerEventData(EventSystem.current)
		{
			position = _screenPosition
		};

		EventSystem.current.RaycastAll(pointerData, s_RaycastResults);
		Transform slotRoot = _slot.transform;
		for (int i = 0; i < s_RaycastResults.Count; i++)
		{
			Transform hit = s_RaycastResults[i].gameObject.transform;
			if (hit == slotRoot || hit.IsChildOf(slotRoot))
				return true;
		}

		return false;
	}

	public static bool IsScreenPointInsideRectTransform(
		RectTransform _rect,
		Vector2 _screenPosition,
		Camera _eventCamera)
	{
		if (_rect == null)
			return false;

		Canvas.ForceUpdateCanvases();
		_rect.GetWorldCorners(s_WorldCorners);
		Vector2 screenMin = RectTransformUtility.WorldToScreenPoint(_eventCamera, s_WorldCorners[0]);
		Vector2 screenMax = screenMin;

		for (int i = 1; i < 4; i++)
		{
			Vector2 corner = RectTransformUtility.WorldToScreenPoint(_eventCamera, s_WorldCorners[i]);
			screenMin = Vector2.Min(screenMin, corner);
			screenMax = Vector2.Max(screenMax, corner);
		}

		return _screenPosition.x >= screenMin.x && _screenPosition.x <= screenMax.x &&
		       _screenPosition.y >= screenMin.y && _screenPosition.y <= screenMax.y;
	}
	#endregion

	#region Private Methods
	private static void DisableTextRaycastTargets(InventorySlotView _slot)
	{
		if (_slot == null)
			return;

		TMPro.TMP_Text[] texts = _slot.GetComponentsInChildren<TMPro.TMP_Text>(true);
		for (int i = 0; i < texts.Length; i++)
		{
			if (texts[i] != null)
				texts[i].raycastTarget = false;
		}
	}

	private static void StretchToParent(RectTransform _rect)
	{
		if (_rect == null)
			return;

		_rect.anchorMin = Vector2.zero;
		_rect.anchorMax = Vector2.one;
		_rect.offsetMin = Vector2.zero;
		_rect.offsetMax = Vector2.zero;
	}
	#endregion
}
