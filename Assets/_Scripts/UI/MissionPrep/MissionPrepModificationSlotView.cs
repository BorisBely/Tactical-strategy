using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class MissionPrepModificationSlotView : MonoBehaviour, IDropHandler, IPointerClickHandler
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

		if (m_LabelText != null)
		{
			WeaponDefinition weapon = m_WeaponSlot.Definition != null ? m_WeaponSlot.Definition.WeaponDefinition : null;
			m_LabelText.text = ItemModificationUtility.GetSlotLabel(m_Descriptor, weapon);
		}

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
	}
	#endregion

	#region Private Methods
	private void EnsureUi()
	{
		if (m_BackgroundImage != null)
			return;

		RectTransform rowRt = gameObject.GetComponent<RectTransform>();
		if (rowRt == null)
			rowRt = gameObject.AddComponent<RectTransform>();
		rowRt.sizeDelta = new Vector2(400f, 30f);

		m_BackgroundImage = gameObject.GetComponent<Image>();
		if (m_BackgroundImage == null)
			m_BackgroundImage = gameObject.AddComponent<Image>();
		m_BackgroundImage.color = m_NormalColor;
		m_BackgroundImage.raycastTarget = true;

		HorizontalLayoutGroup layout = gameObject.GetComponent<HorizontalLayoutGroup>();
		if (layout == null)
			layout = gameObject.AddComponent<HorizontalLayoutGroup>();
		layout.padding = new RectOffset(18, 6, 3, 3);
		layout.spacing = 6f;
		layout.childAlignment = TextAnchor.MiddleLeft;
		layout.childControlWidth = false;
		layout.childForceExpandWidth = false;
		layout.childControlHeight = true;
		layout.childForceExpandHeight = true;

		m_LabelText = CreateText("SlotLabel", transform, 82f, TextAlignmentOptions.Left);
		m_IconImage = CreateIcon("ItemIcon", transform);
		m_ItemText = CreateText("ItemLabel", transform, 132f, TextAlignmentOptions.Left);

		if (GetComponent<MissionPrepModificationSlotDrag>() == null)
			gameObject.AddComponent<MissionPrepModificationSlotDrag>();
	}

	private TMP_Text CreateText(string _name, Transform _parent, float _preferredWidth, TextAlignmentOptions _alignment)
	{
		GameObject go = new GameObject(_name, typeof(RectTransform));
		go.transform.SetParent(_parent, false);
		TMP_Text text = go.AddComponent<TextMeshProUGUI>();
		text.fontSize = 13f;
		text.color = Color.white;
		text.alignment = _alignment;
		text.textWrappingMode = TextWrappingModes.NoWrap;
		text.overflowMode = TextOverflowModes.Ellipsis;

		LayoutElement layout = go.AddComponent<LayoutElement>();
		layout.preferredWidth = _preferredWidth;
		layout.flexibleWidth = _preferredWidth > 100f ? 1f : 0f;
		return text;
	}

	private Image CreateIcon(string _name, Transform _parent)
	{
		GameObject go = new GameObject(_name, typeof(RectTransform));
		go.transform.SetParent(_parent, false);
		Image image = go.AddComponent<Image>();
		image.preserveAspect = true;
		image.raycastTarget = false;

		LayoutElement layout = go.AddComponent<LayoutElement>();
		layout.preferredWidth = 22f;
		layout.preferredHeight = 22f;
		return image;
	}

	private void ApplyInstalledItem(InventorySlotRuntimeData _item)
	{
		if (m_ItemText != null)
			m_ItemText.text = FormatItemLabel(_item);

		if (m_IconImage == null)
			return;

		Sprite icon = _item.Definition != null ? _item.Definition.Icon : null;
		m_IconImage.sprite = icon;
		m_IconImage.gameObject.SetActive(icon != null);
	}

	private void ApplyEmptyItem()
	{
		if (m_ItemText != null)
			m_ItemText.text = LocalizationManager.Get("weapon.mod_slot.empty", "Empty");

		if (m_IconImage != null)
		{
			m_IconImage.sprite = null;
			m_IconImage.gameObject.SetActive(false);
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
