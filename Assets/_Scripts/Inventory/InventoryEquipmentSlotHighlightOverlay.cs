using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Overlay поверх ячейки экипировки: подсветка видна поверх TMP/иконки.
/// Добавляется при создании leading-ячейки 0 в <see cref="InventoryPanelView"/>.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public sealed class InventoryEquipmentSlotHighlightOverlay : MonoBehaviour
{
	#region Constants
	private const string c_OverlayObjectName = InventorySlotUiUtility.EquipHighlightOverlayObjectName;
	#endregion

	#region Private Fields
	private InventorySlotView m_Slot;
	private Image m_OverlayImage;
	#endregion

	#region Public Methods
	public void EnsureOverlay()
	{
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();

		if (m_OverlayImage != null)
			return;

		Transform existing = transform.Find(c_OverlayObjectName);
		if (existing != null)
		{
			m_OverlayImage = existing.GetComponent<Image>();
			if (m_OverlayImage != null)
				return;
		}

		var overlayObject = new GameObject(c_OverlayObjectName, typeof(RectTransform), typeof(Image));
		overlayObject.transform.SetParent(transform, false);
		overlayObject.transform.SetAsLastSibling();

		RectTransform overlayRect = overlayObject.transform as RectTransform;
		if (overlayRect != null)
		{
			overlayRect.anchorMin = Vector2.zero;
			overlayRect.anchorMax = Vector2.one;
			overlayRect.offsetMin = Vector2.zero;
			overlayRect.offsetMax = Vector2.zero;
		}

		m_OverlayImage = overlayObject.GetComponent<Image>();
		m_OverlayImage.raycastTarget = false;
		m_OverlayImage.enabled = false;

		InventoryEquipmentSlotAppearance appearance = InventorySlotUiUtility.ResolveEquipmentSlotAppearance(m_Slot);
		if (appearance.HighlightBackgroundSprite != null)
			m_OverlayImage.sprite = appearance.HighlightBackgroundSprite;
		else if (appearance.NormalBackgroundSprite != null)
			m_OverlayImage.sprite = appearance.NormalBackgroundSprite;

		InventorySlotUiUtility.EnsureImageCanRenderSolidColor(m_OverlayImage);
	}

	public void SetHighlighted(bool _highlighted)
	{
		EnsureOverlay();
		if (m_OverlayImage == null || m_Slot == null)
			return;

		InventoryEquipmentSlotAppearance appearance = InventorySlotUiUtility.ResolveEquipmentSlotAppearance(m_Slot);
		if (_highlighted)
		{
			m_OverlayImage.enabled = true;
			appearance.ApplyHighlight(m_OverlayImage);
			return;
		}

		m_OverlayImage.enabled = false;
		appearance.ApplyNormal(m_Slot);
	}
	#endregion
}
