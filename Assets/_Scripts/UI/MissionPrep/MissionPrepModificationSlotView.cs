using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class MissionPrepModificationSlotView : MonoBehaviour, IDropHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
	#region Constants
	private const float c_DoubleClickMaxDelaySeconds = 0.35f;
	#endregion

	#region Private Fields
	private readonly Color m_NormalColor = MissionPrepInventoryUiColors.CellBackground;
	private readonly Color m_CompatibleColor = MissionPrepInventoryUiColors.CompatibleHighlight;

	private MissionPrepLoadoutCoordinator m_Coordinator;
	private ItemModificationSlotDescriptor m_Descriptor;
	private InventorySlotRuntimeData m_WeaponSlot;
	private bool m_WeaponIsMainHand;
	private int m_WeaponBagIndex;
	private Image m_BackgroundImage;
	private Image m_IconImage;
	private TMP_Text m_LabelText;
	private TMP_Text m_ItemText;
	private float m_LastLeftClickUnscaledTime = -1f;
	private bool m_IsGraphPreviewHovering;
	#endregion

	#region Public Properties
	public ItemModificationSlotDescriptor Descriptor => m_Descriptor;
	public bool WeaponIsMainHand => m_WeaponIsMainHand;
	public int WeaponBagIndex => m_WeaponBagIndex;
	public ItemDefinition WeaponDefinitionHint => m_WeaponSlot.Definition;
	public bool HasInstalledItem => TryGetInstalledItem(out _);
	#endregion

	#region Public Methods
	public bool TryGetInstalledItem(out InventorySlotRuntimeData _installedItem)
	{
		return ItemModificationUtility.TryGetInstalledItem(m_Descriptor, m_WeaponSlot, out _installedItem);
	}

	public void Configure(
		MissionPrepLoadoutCoordinator _coordinator,
		ItemModificationSlotDescriptor _descriptor,
		InventorySlotRuntimeData _weaponSlot,
		bool _weaponIsMainHand,
		int _weaponBagIndex)
	{
		m_Coordinator = _coordinator;
		m_Descriptor = _descriptor;
		m_WeaponSlot = _weaponSlot;
		m_WeaponIsMainHand = _weaponIsMainHand;
		m_WeaponBagIndex = _weaponBagIndex;

		EnsureUi();
		Refresh();
	}

	public void Refresh()
	{
		EnsureUi();

		if (ItemModificationUtility.TryGetInstalledItem(m_Descriptor, m_WeaponSlot, out InventorySlotRuntimeData installedItem))
			ApplyInstalledItem(installedItem);
		else
			ApplyEmptyItem();

		RefreshHighlight();
	}

	public void RefreshHighlight()
	{
		if (m_BackgroundImage == null)
			return;

		MissionPrepModificationDragPayload payload = MissionPrepModificationDragContext.Current;
		bool compatible = payload.HasItem && ItemModificationUtility.CanAcceptItem(m_Descriptor, m_WeaponSlot, payload.Item);
		m_BackgroundImage.color = compatible ? m_CompatibleColor : m_NormalColor;
	}
	#endregion

	#region Event Handlers
	public void OnDrop(PointerEventData eventData)
	{
		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;

		if (m_Coordinator == null)
			return;

		if (!m_Coordinator.TryInstallModificationFromDrag(m_Descriptor, m_WeaponIsMainHand, m_WeaponBagIndex))
			return;

		NotifyDragDropAccepted(eventData);
	}

	public void OnPointerClick(PointerEventData eventData)
	{
		if (eventData.button != PointerEventData.InputButton.Left)
			return;

		float now = Time.unscaledTime;
		bool unityReportsDouble = eventData.clickCount >= 2;
		bool timedDouble = m_LastLeftClickUnscaledTime >= 0f &&
		                   (now - m_LastLeftClickUnscaledTime) <= c_DoubleClickMaxDelaySeconds;
		m_LastLeftClickUnscaledTime = now;

		if (!unityReportsDouble && !timedDouble)
			return;

		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;

		if (m_Coordinator == null)
			return;

		bool cleared = m_Coordinator.TryClearModificationSlot(
			m_Descriptor,
			m_WeaponIsMainHand,
			m_WeaponBagIndex,
			_addToBag: true,
			WeaponDefinitionHint);
		if (!cleared)
			ItemModificationDiagnostics.LogClearRejected(
				"MissionPrepModificationSlotView.OnPointerClick",
				m_Descriptor,
				m_WeaponSlot,
				"double-click clear failed (see prior [WeaponMod] logs)");
		else if (m_IsGraphPreviewHovering)
			ClearGraphPreviewHover();
	}

	public void OnPointerEnter(PointerEventData eventData)
	{
		if (eventData != null && eventData.dragging)
			return;

		if (!TryGetInstalledItem(out InventorySlotRuntimeData installedItem) ||
		    !ItemModificationUtility.IsAttachmentItem(installedItem))
			return;

		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;
		if (m_Coordinator == null)
			return;

		m_IsGraphPreviewHovering = true;
		m_Coordinator.SetHoveredModificationPreviewCandidate(installedItem);
	}

	public void OnPointerExit(PointerEventData eventData)
	{
		if (!m_IsGraphPreviewHovering)
			return;

		ClearGraphPreviewHover();
	}
	#endregion

	#region Unity Lifecycle
	private void OnDisable()
	{
		if (m_IsGraphPreviewHovering)
			ClearGraphPreviewHover();
	}
	#endregion

	#region Private Methods
	private void ClearGraphPreviewHover()
	{
		m_IsGraphPreviewHovering = false;
		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;

		if (TryGetInstalledItem(out InventorySlotRuntimeData installedItem))
			m_Coordinator?.ClearHoveredModificationPreviewCandidate(installedItem);
		else
			m_Coordinator?.ClearHoveredModificationPreviewCandidate(default);
	}

	private void EnsureUi()
	{
		if (m_BackgroundImage != null)
			return;

		InventoryModificationSlotUiBuilder.BuildRow(
			gameObject,
			m_NormalColor,
			out m_BackgroundImage,
			out m_IconImage,
			out m_LabelText,
			out m_ItemText);

		if (GetComponent<MissionPrepModificationSlotDrag>() == null)
			gameObject.AddComponent<MissionPrepModificationSlotDrag>();
	}

	private void ApplyInstalledItem(InventorySlotRuntimeData _item)
	{
		if (m_ItemText != null)
			m_ItemText.text = FormatItemLabel(_item);

		if (m_IconImage == null)
			return;

		Sprite icon = _item.Definition != null ? _item.Definition.Icon : null;
		m_IconImage.sprite = icon;
		m_IconImage.enabled = icon != null;
		m_IconImage.gameObject.SetActive(true);
	}

	private void ApplyEmptyItem()
	{
		if (m_ItemText != null)
		{
			WeaponDefinition weapon = m_WeaponSlot.Definition != null ? m_WeaponSlot.Definition.WeaponDefinition : null;
			m_ItemText.text = ItemModificationUtility.FormatEmptySlotLabel(m_Descriptor, weapon);
		}

		if (m_IconImage != null)
		{
			m_IconImage.sprite = null;
			m_IconImage.enabled = false;
		}
	}

	private static string FormatItemLabel(InventorySlotRuntimeData _item)
	{
		if (!string.IsNullOrWhiteSpace(_item.LocalizationKey))
			return LocalizationManager.Get(_item.LocalizationKey, _item.DisplayName);

		if (_item.Definition != null)
			return _item.Definition.GetLocalizedDisplayName();

		return string.IsNullOrWhiteSpace(_item.DisplayName)
			? LocalizationManager.Get("item.generic", "Item")
			: _item.DisplayName;
	}

	private static void NotifyDragDropAccepted(PointerEventData eventData)
	{
		if (eventData?.pointerDrag == null)
			return;

		if (eventData.pointerDrag.TryGetComponent(out MissionPrepAvailableToPresetDrag availableDrag))
			availableDrag.NotifyDropAccepted();

		if (eventData.pointerDrag.TryGetComponent(out MissionPrepPresetToAvailableDrag presetDrag))
			presetDrag.NotifyDropAccepted();
	}
	#endregion
}
