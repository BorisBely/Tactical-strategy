using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Drop юнита с места машины обратно в roster-колонку (только копия из слота).
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepUnitUnassignDropZone : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
	#region Constants
	private const string c_OverlayName = "UnassignDropHighlightOverlay";
	#endregion

	#region Private Fields
	private MissionPrepVehicleAssignmentStore m_Assignments;
	private MissionPrepSquadSpawner m_Spawner;
	private RectTransform m_Content;
	private Image m_Highlight;
	private Color m_NormalColor;
	private Color m_ActiveColor;
	private bool m_HasNormalColor;
	#endregion

	#region Public Methods
	public void Configure(
		MissionPrepVehicleAssignmentStore _assignments,
		MissionPrepSquadSpawner _spawner,
		RectTransform _content)
	{
		m_Assignments = _assignments;
		m_Spawner = _spawner;
		m_Content = _content;
		EnsureHighlightGraphic();
	}

	public bool TryAccept(GameObject _unitRoot)
	{
		if (_unitRoot == null || m_Assignments == null)
			return false;

		if (!m_Assignments.TryGetUnitAssignment(_unitRoot, out _, out _))
			return false;

		m_Assignments.ClearUnitFromAll(_unitRoot);
		return true;
	}

	/// <summary>
	/// Снятие с места только если тащат копию из слота машины, не строку roster-колонки.
	/// </summary>
	public bool TryAcceptFromDrag(MissionPrepUnitCellView _cell)
	{
		if (_cell == null || !_cell.IsInsideSeatSlot)
			return false;

		GameObject unitRoot = _cell.BoundUnitRoot;
		if (!TryAccept(unitRoot))
			return false;

		m_Spawner?.PlaceRosterUnitCellInColumn(unitRoot, m_Content);
		return true;
	}

	public static void ClearAllHighlights()
	{
		MissionPrepUnitUnassignDropZone[] zones =
			FindObjectsByType<MissionPrepUnitUnassignDropZone>(FindObjectsInactive.Include);
		for (int i = 0; i < zones.Length; i++)
			zones[i]?.ClearHighlight();
	}
	#endregion

	#region Event Handlers
	public void OnDrop(PointerEventData eventData)
	{
		MissionPrepUnitCellView cell = ResolveCell(eventData);
		if (TryAcceptFromDrag(cell))
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
		if (!TryResolveOccupiedSeatCell(eventData, out _))
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

	private static bool TryResolveOccupiedSeatCell(
		PointerEventData eventData,
		out MissionPrepUnitCellView _cell)
	{
		_cell = ResolveCell(eventData);
		return _cell != null && _cell.IsInsideSeatSlot;
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
