using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Drop юнита или машины в roster-колонку (на базе / на задание). Только UI-списки.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepRosterColumnDropZone : MonoBehaviour,
	IDropHandler,
	IPointerEnterHandler,
	IPointerExitHandler
{
	#region Constants
	private const string c_OverlayName = "ColumnDropHighlightOverlay";
	#endregion

	#region Private Fields
	private MissionPrepSquadSpawner m_Spawner;
	private RectTransform m_Content;
	private Image m_Highlight;
	private Color m_NormalColor;
	private Color m_ActiveColor;
	private bool m_HasNormalColor;
	#endregion

	#region Public Properties
	public RectTransform Content => m_Content;
	#endregion

	#region Public Methods
	public void Configure(MissionPrepSquadSpawner _spawner, RectTransform _content)
	{
		m_Spawner = _spawner;
		m_Content = _content;
		EnsureHighlightGraphic();
	}

	public bool TryAccept(MissionPrepUnitCellView _cell)
	{
		if (m_Spawner == null || m_Content == null)
			return false;

		return m_Spawner.TryMoveRosterCellToColumn(_cell, m_Content);
	}

	public bool OwnsCell(MissionPrepUnitCellView _cell)
	{
		if (_cell == null || m_Content == null)
			return false;

		Transform root = _cell.transform;
		return root.IsChildOf(m_Content);
	}

	public static void ClearAllHighlights()
	{
		MissionPrepRosterColumnDropZone[] zones =
			FindObjectsByType<MissionPrepRosterColumnDropZone>(FindObjectsInactive.Include);
		for (int i = 0; i < zones.Length; i++)
			zones[i]?.ClearHighlight();
	}
	#endregion

	#region Event Handlers
	public void OnDrop(PointerEventData eventData)
	{
		MissionPrepUnitCellView cell = ResolveCell(eventData);
		if (TryAccept(cell))
		{
			MissionPrepUnitCellDrag drag = ResolveDrag(eventData);
			drag?.NotifyDropAccepted();
		}

		ClearHighlight();
	}

	public void OnPointerEnter(PointerEventData eventData)
	{
		if (eventData == null || !eventData.dragging)
			return;

		MissionPrepUnitCellView cell = ResolveCell(eventData);
		if (cell == null || cell.IsInsideSeatSlot || OwnsCell(cell))
			return;

		EnsureHighlightGraphic();
		if (m_Highlight != null)
			m_Highlight.color = m_ActiveColor;
	}

	public void OnPointerExit(PointerEventData eventData)
	{
		ClearHighlight();
	}
	#endregion

	#region Private Methods
	private static MissionPrepUnitCellDrag ResolveDrag(PointerEventData eventData)
	{
		if (eventData?.pointerDrag == null)
			return null;

		MissionPrepUnitCellDrag drag = eventData.pointerDrag.GetComponent<MissionPrepUnitCellDrag>();
		if (drag == null)
			drag = eventData.pointerDrag.GetComponentInParent<MissionPrepUnitCellDrag>();
		if (drag == null)
			drag = eventData.pointerDrag.GetComponentInChildren<MissionPrepUnitCellDrag>(true);
		return drag;
	}

	private static MissionPrepUnitCellView ResolveCell(PointerEventData eventData)
	{
		MissionPrepUnitCellDrag drag = ResolveDrag(eventData);
		return drag != null ? drag.Cell : null;
	}

	private void EnsureHighlightGraphic()
	{
		if (m_Highlight != null)
			return;

		Transform existing = transform.Find(c_OverlayName);
		GameObject go = existing != null ? existing.gameObject : new GameObject(c_OverlayName, typeof(RectTransform));
		if (existing == null)
			go.transform.SetParent(transform, false);

		RectTransform rt = go.transform as RectTransform;
		rt.anchorMin = Vector2.zero;
		rt.anchorMax = Vector2.one;
		rt.pivot = new Vector2(0.5f, 0.5f);
		rt.offsetMin = Vector2.zero;
		rt.offsetMax = Vector2.zero;
		rt.SetAsFirstSibling();

		LayoutElement layout = go.GetComponent<LayoutElement>();
		if (layout == null)
			layout = go.AddComponent<LayoutElement>();
		layout.ignoreLayout = true;

		m_Highlight = go.GetComponent<Image>();
		if (m_Highlight == null)
			m_Highlight = go.AddComponent<Image>();
		InventorySlotUiUtility.EnsureImageCanRenderSolidColor(m_Highlight);
		m_Highlight.raycastTarget = false;
		m_NormalColor = new Color(
			InventoryUiTheme.UnitCellSelected.r,
			InventoryUiTheme.UnitCellSelected.g,
			InventoryUiTheme.UnitCellSelected.b,
			0f);
		m_ActiveColor = new Color(
			InventoryUiTheme.UnitCellSelected.r,
			InventoryUiTheme.UnitCellSelected.g,
			InventoryUiTheme.UnitCellSelected.b,
			0.28f);
		m_Highlight.color = m_NormalColor;
		m_HasNormalColor = true;
	}

	private void OnDisable()
	{
		ClearHighlight();
	}

	private void ClearHighlight()
	{
		if (m_Highlight != null && m_HasNormalColor)
			m_Highlight.color = m_NormalColor;
	}
	#endregion
}
