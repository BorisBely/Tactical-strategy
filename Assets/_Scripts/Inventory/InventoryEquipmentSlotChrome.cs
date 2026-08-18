using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Заголовок слота экипировки (как место в машине): название сверху, drop на заголовок и ячейку.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public sealed class InventoryEquipmentSlotChrome : MonoBehaviour
{
	#region Constants
	public const string HeaderObjectNamePrefix = "EquipSlotHeader_";
	#endregion

	#region Private Fields
	private InventorySlotView m_Slot;
	private InventoryPanelSectionHeader m_Header;
	private Image m_HeaderBackground;
	private Color m_HeaderNormalColor = InventoryUiTheme.TitleBar;
	#endregion

	#region Public Properties
	public InventoryPanelSectionHeader Header => m_Header;
	public RectTransform HeaderRect => m_Header != null ? m_Header.transform as RectTransform : null;
	#endregion

	#region Public Methods
	public static string GetHeaderObjectName(int _equipmentSlotIndex)
	{
		return $"{HeaderObjectNamePrefix}{_equipmentSlotIndex}";
	}

	public void Configure(int _equipmentSlotIndex, bool _vehicleEquipment)
	{
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();

		Transform container = transform.parent;
		if (container == null)
			return;

		string titleKey = InventorySlotUiUtility.GetEquipmentSlotTitleLocalizationKey(
			_equipmentSlotIndex, _vehicleEquipment);
		string titleFallback = InventorySlotUiUtility.GetEquipmentSlotTitleFallback(
			_equipmentSlotIndex, _vehicleEquipment);

		m_Header = InventoryPanelSectionHeader.Ensure(
			container,
			GetHeaderObjectName(_equipmentSlotIndex),
			titleKey,
			titleFallback);

		if (m_Header == null)
			return;

		m_Header.gameObject.SetActive(true);
		m_Header.SetRaycastTarget(true);

		int slotSibling = transform.GetSiblingIndex();
		int headerSibling = m_Header.transform.GetSiblingIndex();
		m_Header.transform.SetSiblingIndex(
			headerSibling < slotSibling ? slotSibling - 1 : slotSibling);

		m_HeaderBackground = m_Header.GetComponent<Image>();
		if (m_HeaderBackground != null)
		{
			m_HeaderNormalColor = InventoryUiTheme.TitleBar;
			m_HeaderBackground.color = m_HeaderNormalColor;
		}

		EnsureDropRelay();
	}

	public void EnsureDropRelay()
	{
		if (m_Header == null || m_Slot == null)
			return;

		m_Header.SetRaycastTarget(true);

		HeaderDropRelay relay = m_Header.GetComponent<HeaderDropRelay>();
		if (relay == null)
			relay = m_Header.gameObject.AddComponent<HeaderDropRelay>();

		relay.Initialize(m_Slot);
	}

	public void SetDropHighlight(bool _highlighted)
	{
		if (m_HeaderBackground == null && m_Header != null)
			m_HeaderBackground = m_Header.GetComponent<Image>();

		if (m_HeaderBackground == null)
			return;

		m_HeaderBackground.color = _highlighted
			? InventoryUiTheme.UnitCellSelected
			: m_HeaderNormalColor;
	}
	#endregion

	private sealed class HeaderDropRelay : MonoBehaviour, IDropHandler
	{
		private InventorySlotView m_Slot;

		public void Initialize(InventorySlotView _slot)
		{
			m_Slot = _slot;
		}

		public void OnDrop(PointerEventData eventData)
		{
			if (m_Slot == null)
				return;

			IInventoryEquipmentSlotDropHandler handler =
				m_Slot.GetComponent<IInventoryEquipmentSlotDropHandler>();
			handler?.HandleEquipmentSlotDrop(eventData);
		}
	}
}
