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
	public const string EquipmentDropReceiverObjectName = "EquipmentDropReceiver";
	/// <summary>Допуск в пикселях для сброса оружия на слот экипировки (EndDrag fallback).</summary>
	public const float MainHandEquipDropPaddingPixels = 20f;
	#endregion

	#region Private Fields
	private static readonly Vector3[] s_WorldCorners = new Vector3[4];
	private static readonly List<RaycastResult> s_RaycastResults = new List<RaycastResult>(16);
	private static Sprite s_SolidWhiteSprite;
	#endregion

	#region Public Methods
	public static InventorySlotView GetMainHandEquipmentSlot(InventoryPanelView _panel)
	{
		return GetLeadingEquipmentSlot(_panel, 0);
	}

	public static InventorySlotView GetHeadEquipmentSlot(InventoryPanelView _panel)
	{
		return GetLeadingEquipmentSlot(_panel, 1);
	}

	public static InventorySlotView GetBackEquipmentSlot(InventoryPanelView _panel)
	{
		return GetLeadingEquipmentSlot(_panel, 2);
	}

	public static InventorySlotView GetLeadingEquipmentSlot(InventoryPanelView _panel, int _slotIndex)
	{
		if (_panel == null || _panel.LeadingEquipmentSlotCount <= 0)
			return null;

		if (_slotIndex < 0 || _slotIndex >= _panel.LeadingEquipmentSlotCount)
			return null;

		Transform container = _panel.SlotsContainerTransform;
		if (container != null)
		{
			int foundIndex = 0;
			for (int i = 0; i < container.childCount; i++)
			{
				Transform child = container.GetChild(i);
				if (child == null || !child.gameObject.activeInHierarchy)
					continue;

				InventorySlotView slot = child.GetComponent<InventorySlotView>();
				if (slot == null)
					continue;

				if (foundIndex == _slotIndex)
					return slot;

				foundIndex++;
			}
		}

		_panel.RefreshSlotsFromHierarchy();
		IReadOnlyList<InventorySlotView> slots = _panel.Slots;
		return slots.Count > _slotIndex ? slots[_slotIndex] : null;
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

			if (candidate.gameObject.name == EquipHighlightOverlayObjectName ||
			    candidate.gameObject.name == EquipmentDropReceiverObjectName)
				continue;

			RectTransform candidateRect = candidate.rectTransform;
			RectTransform slotRect = _slot.transform as RectTransform;
			if (candidateRect != null && slotRect != null)
			{
				float candidateArea = candidateRect.rect.width * candidateRect.rect.height;
				float slotArea = slotRect.rect.width * slotRect.rect.height;
				if (candidateArea >= slotArea * 0.5f)
				{
					_backgroundImage = candidate;
					return true;
				}

				continue;
			}

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

		Image dropReceiver = EnsureEquipmentSlotDropReceiverImage(_slot);
		Image[] images = _slot.GetComponentsInChildren<Image>(true);
		for (int i = 0; i < images.Length; i++)
		{
			Image image = images[i];
			if (image == null)
				continue;

			image.raycastTarget = image == dropReceiver;
		}

		Canvas.ForceUpdateCanvases();
	}

	/// <summary>Полноразмерный приёмник сброса поверх ячейки (для <see cref="InventoryEquipmentSlotDropReceiver"/>).</summary>
	public static void EnsureEquipmentSlotDropReceiver(IInventoryEquipmentSlotDropHandler _dropHandler)
	{
		if (_dropHandler == null)
			return;

		MonoBehaviour behaviour = _dropHandler as MonoBehaviour;
		if (behaviour == null)
			return;

		InventorySlotView slot = behaviour.GetComponent<InventorySlotView>();
		if (slot == null)
			return;

		EnsureEquipmentSlotDropTarget(slot);

		Image receiverImage = EnsureEquipmentSlotDropReceiverImage(slot);
		InventoryEquipmentSlotDropReceiver receiver =
			receiverImage.GetComponent<InventoryEquipmentSlotDropReceiver>();
		if (receiver == null)
			receiver = receiverImage.gameObject.AddComponent<InventoryEquipmentSlotDropReceiver>();

		receiver.Bind(_dropHandler);
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
	public static void EnsureDescriptionHover(InventorySlotView _slot)
	{
		if (_slot == null)
			return;

		DisableTextRaycastTargets(_slot);

		GameObject hoverTarget = ResolveDescriptionHoverTarget(_slot);
		if (hoverTarget == null)
			return;

		InventorySlotDescriptionHover[] existingHovers = _slot.GetComponentsInChildren<InventorySlotDescriptionHover>(true);
		for (int i = 0; i < existingHovers.Length; i++)
		{
			InventorySlotDescriptionHover hover = existingHovers[i];
			if (hover == null || hover.gameObject == hoverTarget)
				continue;

			if (Application.isPlaying)
				Object.Destroy(hover);
			else
				Object.DestroyImmediate(hover);
		}

		if (hoverTarget.GetComponent<InventorySlotDescriptionHover>() == null)
			hoverTarget.AddComponent<InventorySlotDescriptionHover>();
	}

	private static GameObject ResolveDescriptionHoverTarget(InventorySlotView _slot)
	{
		Transform equipmentReceiver = _slot.transform.Find(EquipmentDropReceiverObjectName);
		if (equipmentReceiver != null)
			return equipmentReceiver.gameObject;

		if (_slot.TryGetComponent(out Image rootImage))
		{
			rootImage.raycastTarget = true;
			return _slot.gameObject;
		}

		return _slot.gameObject;
	}

	public static void ConfigureMainHandEquipmentSlot(
		InventorySlotView _slot,
		InventoryEquipmentSlotAppearance _appearance)
	{
		ConfigureEquipmentSlot(_slot, _appearance);
	}

	public static void ConfigureHeadEquipmentSlot(
		InventorySlotView _slot,
		InventoryEquipmentSlotAppearance _appearance)
	{
		ConfigureEquipmentSlot(_slot, _appearance);
	}

	public static void ConfigureBackEquipmentSlot(
		InventorySlotView _slot,
		InventoryEquipmentSlotAppearance _appearance)
	{
		ConfigureEquipmentSlot(_slot, _appearance);
	}

	public static void ConfigureEquipmentSlot(
		InventorySlotView _slot,
		InventoryEquipmentSlotAppearance _appearance)
	{
		if (_slot == null || _appearance == null)
			return;

		_appearance.ApplyNormal(_slot);
		EnsureEquipmentSlotDropTarget(_slot);
		DisableTextRaycastTargets(_slot);
		EnsureDescriptionHover(_slot);

		InventoryEquipmentSlotHighlightOverlay overlay = _slot.GetComponent<InventoryEquipmentSlotHighlightOverlay>();
		if (overlay == null)
			overlay = _slot.gameObject.AddComponent<InventoryEquipmentSlotHighlightOverlay>();

		overlay.EnsureOverlay();
		overlay.SetHighlighted(false);
	}

	public static bool IsWeaponEquipDragActive()
	{
		if (IsWeaponEquipDragFromPayload())
			return true;

		return InventoryEquipmentEquipHoverContext.HasActiveWeaponEquipHover;
	}

	public static bool IsHelmetEquipDragActive()
	{
		if (IsHelmetEquipDragFromPayload())
			return true;

		return InventoryEquipmentEquipHoverContext.HasActiveHelmetEquipHover;
	}

	public static bool IsBackpackEquipDragActive()
	{
		if (IsBackpackEquipDragFromPayload())
			return true;

		return InventoryEquipmentEquipHoverContext.HasActiveBackpackEquipHover;
	}

	private static bool IsWeaponEquipDragFromPayload()
	{
		if (RuntimeInventoryModificationDragContext.WasDropConsumed)
			return false;

		RuntimeInventoryModificationDragPayload runtimePayload = RuntimeInventoryModificationDragContext.Current;
		if (RuntimeInventoryModificationDragContext.IsWeaponEquipDragSource(runtimePayload.SourceKind) &&
		    WeaponEquipUtility.CanEquipToMainHand(runtimePayload.Item))
			return true;

		MissionPrepModificationDragPayload missionPrepPayload = MissionPrepModificationDragContext.Current;
		return MissionPrepWeaponEquipUtility.IsWeaponEquipDragSource(missionPrepPayload.SourceKind) &&
		       MissionPrepWeaponEquipUtility.CanEquipToMainHand(missionPrepPayload.Item);
	}

	private static bool IsHelmetEquipDragFromPayload()
	{
		if (RuntimeInventoryModificationDragContext.WasDropConsumed)
			return false;

		RuntimeInventoryModificationDragPayload runtimePayload = RuntimeInventoryModificationDragContext.Current;
		if (RuntimeInventoryModificationDragContext.IsHelmetEquipDragSource(runtimePayload.SourceKind) &&
		    HelmetEquipUtility.CanEquipToHead(runtimePayload.Item))
			return true;

		MissionPrepModificationDragPayload missionPrepPayload = MissionPrepModificationDragContext.Current;
		return MissionPrepHelmetEquipUtility.IsHelmetEquipDragSource(missionPrepPayload.SourceKind) &&
		       MissionPrepHelmetEquipUtility.CanEquipToHead(missionPrepPayload.Item);
	}

	private static bool IsBackpackEquipDragFromPayload()
	{
		if (RuntimeInventoryModificationDragContext.WasDropConsumed)
			return false;

		RuntimeInventoryModificationDragPayload runtimePayload = RuntimeInventoryModificationDragContext.Current;
		if (RuntimeInventoryModificationDragContext.IsBackpackEquipDragSource(runtimePayload.SourceKind) &&
		    BackpackEquipUtility.CanEquipToBack(runtimePayload.Item))
			return true;

		MissionPrepModificationDragPayload missionPrepPayload = MissionPrepModificationDragContext.Current;
		return MissionPrepBackpackEquipUtility.IsBackpackEquipDragSource(missionPrepPayload.SourceKind) &&
		       MissionPrepBackpackEquipUtility.CanEquipToBack(missionPrepPayload.Item);
	}

	public static void RefreshEquipmentSlotHighlights(InventoryPanelView _panel)
	{
		RefreshMainHandEquipHighlight(_panel);
		RefreshHeadEquipHighlight(_panel);
		RefreshBackEquipHighlight(_panel);
	}

	public static void RefreshMainHandEquipHighlight(InventoryPanelView _panel)
	{
		InventorySlotView mainHandSlot = GetMainHandEquipmentSlot(_panel);
		if (mainHandSlot == null)
			return;

		ApplyMainHandEquipmentSlotHighlight(mainHandSlot, IsWeaponEquipDragActive());
	}

	public static void RefreshHeadEquipHighlight(InventoryPanelView _panel)
	{
		InventorySlotView headSlot = GetHeadEquipmentSlot(_panel);
		if (headSlot == null)
			return;

		ApplyEquipmentSlotHighlight(headSlot, IsHelmetEquipDragActive());
	}

	public static void RefreshBackEquipHighlight(InventoryPanelView _panel)
	{
		InventorySlotView backSlot = GetBackEquipmentSlot(_panel);
		if (backSlot == null)
			return;

		ApplyBackEquipmentSlotHighlight(backSlot, IsBackpackEquipDragActive());
	}

	public static void ApplyMainHandEquipmentSlotHighlight(InventorySlotView _slot, bool _highlighted)
	{
		ApplyEquipmentSlotHighlight(_slot, _highlighted);
	}

	public static void ApplyHeadEquipmentSlotHighlight(InventorySlotView _slot, bool _highlighted)
	{
		ApplyEquipmentSlotHighlight(_slot, _highlighted);
	}

	public static void ApplyBackEquipmentSlotHighlight(InventorySlotView _slot, bool _highlighted)
	{
		ApplyEquipmentSlotHighlight(_slot, _highlighted);
	}

	public static void ApplyEquipmentSlotHighlight(InventorySlotView _slot, bool _highlighted)
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
		return IsScreenPointOverSlot(_slot, _screenPosition, _eventCamera, 0f);
	}

	public static bool IsScreenPointOverMainHandEquipmentSlot(
		InventorySlotView _slot,
		Vector2 _screenPosition,
		Camera _eventCamera)
	{
		return IsScreenPointOverSlot(_slot, _screenPosition, _eventCamera, MainHandEquipDropPaddingPixels);
	}

	public static bool IsScreenPointOverHeadEquipmentSlot(
		InventorySlotView _slot,
		Vector2 _screenPosition,
		Camera _eventCamera)
	{
		return IsScreenPointOverSlot(_slot, _screenPosition, _eventCamera, MainHandEquipDropPaddingPixels);
	}

	public static bool IsScreenPointOverBackEquipmentSlot(
		InventorySlotView _slot,
		Vector2 _screenPosition,
		Camera _eventCamera)
	{
		return IsScreenPointOverSlot(_slot, _screenPosition, _eventCamera, MainHandEquipDropPaddingPixels);
	}

	public static bool IsScreenPointOverSlot(
		InventorySlotView _slot,
		Vector2 _screenPosition,
		Camera _eventCamera,
		float _paddingPixels)
	{
		if (_slot == null)
			return false;

		RectTransform rect = _slot.transform as RectTransform;
		if (rect != null && IsScreenPointInsideRectTransform(rect, _screenPosition, _eventCamera, _paddingPixels))
			return true;

		return _paddingPixels <= 0f && IsScreenPointOverSlotRaycast(_slot, _screenPosition);
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
		return IsScreenPointInsideRectTransform(_rect, _screenPosition, _eventCamera, 0f);
	}

	public static bool IsScreenPointInsideRectTransform(
		RectTransform _rect,
		Vector2 _screenPosition,
		Camera _eventCamera,
		float _paddingPixels)
	{
		if (_rect == null)
			return false;

		Canvas.ForceUpdateCanvases();

		if (_paddingPixels <= 0f)
			return RectTransformUtility.RectangleContainsScreenPoint(_rect, _screenPosition, _eventCamera);

		_rect.GetWorldCorners(s_WorldCorners);
		Vector2 screenMin = RectTransformUtility.WorldToScreenPoint(_eventCamera, s_WorldCorners[0]);
		Vector2 screenMax = screenMin;

		for (int i = 1; i < 4; i++)
		{
			Vector2 corner = RectTransformUtility.WorldToScreenPoint(_eventCamera, s_WorldCorners[i]);
			screenMin = Vector2.Min(screenMin, corner);
			screenMax = Vector2.Max(screenMax, corner);
		}

		screenMin -= new Vector2(_paddingPixels, _paddingPixels);
		screenMax += new Vector2(_paddingPixels, _paddingPixels);

		return _screenPosition.x >= screenMin.x && _screenPosition.x <= screenMax.x &&
		       _screenPosition.y >= screenMin.y && _screenPosition.y <= screenMax.y;
	}
	#endregion

	#region Private Methods
	private static Image EnsureEquipmentSlotDropReceiverImage(InventorySlotView _slot)
	{
		Transform existing = _slot.transform.Find(EquipmentDropReceiverObjectName);
		Image receiverImage;
		if (existing != null)
		{
			receiverImage = existing.GetComponent<Image>();
			if (receiverImage == null)
				receiverImage = existing.gameObject.AddComponent<Image>();
		}
		else
		{
			var receiverObject = new GameObject(EquipmentDropReceiverObjectName, typeof(RectTransform), typeof(Image));
			receiverObject.transform.SetParent(_slot.transform, false);
			receiverObject.transform.SetAsLastSibling();
			receiverImage = receiverObject.GetComponent<Image>();
		}

		EnsureImageCanRenderSolidColor(receiverImage);
		Color color = receiverImage.color;
		color.a = 0.004f;
		receiverImage.color = color;
		receiverImage.raycastTarget = true;

		StretchToParent(receiverImage.rectTransform);
		return receiverImage;
	}
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
