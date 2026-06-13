using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Двойной клик по оружию на панели земли — экипировка в слот основной руки.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public sealed class InventoryGroundEquipDoubleClick : MonoBehaviour, IPointerClickHandler
{
	#region Constants
	private const float c_DoubleClickMaxDelaySeconds = 0.35f;
	#endregion

	#region Serialized Fields
	[SerializeField] private InventorySlotView m_Slot;
	#endregion

	#region Private Fields
	private float m_LastLeftClickUnscaledTime = -1f;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();
	}
	#endregion

	#region IPointerClickHandler
	public void OnPointerClick(PointerEventData eventData)
	{
		if (eventData.button != PointerEventData.InputButton.Left)
			return;

		if (m_Slot == null || !m_Slot.HasItem)
			return;

		bool canEquipWeapon = WeaponEquipUtility.CanEquipToMainHand(m_Slot.Data);
		bool canEquipHelmet = HelmetEquipUtility.CanEquipToHead(m_Slot.Data);
		bool canEquipBackpack = BackpackEquipUtility.CanEquipToBack(m_Slot.Data);
		if (!canEquipWeapon && !canEquipHelmet && !canEquipBackpack)
			return;

		float now = Time.unscaledTime;
		bool unityReportsDouble = eventData.clickCount >= 2;
		bool timedDouble = m_LastLeftClickUnscaledTime >= 0f &&
		                   (now - m_LastLeftClickUnscaledTime) <= c_DoubleClickMaxDelaySeconds;
		m_LastLeftClickUnscaledTime = now;

		if (!unityReportsDouble && !timedDouble)
			return;

		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		RtsUnitSelectionManager selectionManager = bindings != null ? bindings.SelectionManager : null;
		if (selectionManager == null)
			return;

		selectionManager.TryEquipFromGroundDoubleClick(m_Slot);
	}
	#endregion
}
