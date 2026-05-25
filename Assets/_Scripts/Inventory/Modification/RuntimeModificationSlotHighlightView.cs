using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(InventorySlotView))]
public sealed class RuntimeModificationSlotHighlightView : MonoBehaviour
{
	#region Private Fields
	private readonly Color m_NormalColor = MissionPrepInventoryUiColors.CellBackground;
	private readonly Color m_CompatibleColor = MissionPrepInventoryUiColors.CompatibleHighlight;

	private RuntimeInventoryModificationCoordinator m_Coordinator;
	private InventorySlotView m_Slot;
	private Image m_BackgroundImage;
	#endregion

	#region Public Methods
	public void Bind(RuntimeInventoryModificationCoordinator _coordinator)
	{
		m_Coordinator = _coordinator;
		if (m_Slot == null)
			m_Slot = GetComponent<InventorySlotView>();

		EnsureBackgroundImage();
		RefreshHighlight();
	}

	public void RefreshHighlight()
	{
		EnsureBackgroundImage();
		if (m_BackgroundImage == null || m_Slot == null || !m_Slot.HasItem)
			return;

		if (m_Coordinator == null)
			m_Coordinator = RuntimeInventoryModificationCoordinator.Instance;

		bool compatible = m_Coordinator != null &&
		                  m_Coordinator.ShouldHighlightCompatibleWithModificationWeapon(m_Slot.Data);

		m_BackgroundImage.color = compatible ? m_CompatibleColor : m_NormalColor;
	}
	#endregion

	#region Private Methods
	private void EnsureBackgroundImage()
	{
		if (m_BackgroundImage != null)
			return;

		m_BackgroundImage = GetComponent<Image>();
		if (m_BackgroundImage != null)
			m_BackgroundImage.color = m_NormalColor;
	}
	#endregion
}
