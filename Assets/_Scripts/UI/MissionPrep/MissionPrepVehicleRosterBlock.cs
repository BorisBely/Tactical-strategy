using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Связывает ячейку машины с заголовками и местами.
/// В свёрнутом виде под машиной остаются строки назначенных юнитов — как установленные моды у оружия.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepVehicleRosterBlock : MonoBehaviour, IDropHandler
{
	#region Private Fields
	private readonly List<RectTransform> m_Members = new List<RectTransform>(16);
	private readonly List<RectTransform> m_CollapsibleMembers = new List<RectTransform>(12);
	private bool m_Expanded;
	#endregion

	#region Public Properties
	public IReadOnlyList<RectTransform> Members => m_Members;
	public bool IsExpanded => m_Expanded;
	#endregion

	#region Public Methods
	public void AddMember(RectTransform _member)
	{
		AddMember(_member, _collapsible: false);
	}

	public void AddCollapsibleMember(RectTransform _member)
	{
		AddMember(_member, _collapsible: true);
	}

	public void ToggleExpanded()
	{
		SetExpanded(!m_Expanded);
	}

	public void SetExpanded(bool _expanded)
	{
		if (_expanded)
			CollapseSiblings();

		if (m_Expanded == _expanded)
		{
			ApplyExpandedState();
			return;
		}

		m_Expanded = _expanded;
		ApplyExpandedState();
	}

	public bool MoveTo(RectTransform _content)
	{
		if (_content == null || m_Members.Count == 0)
			return false;

		bool moved = false;
		for (int i = 0; i < m_Members.Count; i++)
		{
			RectTransform member = m_Members[i];
			if (member == null)
				continue;

			if (member.parent != _content)
			{
				member.SetParent(_content, false);
				moved = true;
			}

			member.SetAsLastSibling();
		}

		if (moved)
			ApplyExpandedState();

		return moved;
	}

	public bool TryAcceptUnit(GameObject _unitRoot)
	{
		if (_unitRoot == null)
			return false;

		MissionPrepVehicleSeatSlotView emptySeat = FindFirstEmptySeat();
		return emptySeat != null && emptySeat.TryAcceptUnit(_unitRoot);
	}

	public bool HasEmptySeat()
	{
		return FindFirstEmptySeat() != null;
	}

	public void SetDropHighlight(bool _highlighted)
	{
		MissionPrepUnitCellView cell = GetComponent<MissionPrepUnitCellView>();
		cell?.SetDropTargetHighlight(_highlighted);
	}

	public static void SortVehiclesThenUnits(RectTransform _content)
	{
		if (_content == null)
			return;

		var blocks = new List<MissionPrepVehicleRosterBlock>(8);
		var units = new List<RectTransform>(16);

		for (int i = 0; i < _content.childCount; i++)
		{
			Transform child = _content.GetChild(i);
			if (child == null)
				continue;

			if (child.TryGetComponent(out MissionPrepVehicleRosterBlock block) &&
			    !blocks.Contains(block))
				blocks.Add(block);

			if (child.TryGetComponent(out MissionPrepUnitCellView cell) &&
			    !cell.IsVehicleCell &&
			    !cell.IsInsideSeatSlot)
				units.Add(child as RectTransform);
		}

		int sibling = 0;
		for (int b = 0; b < blocks.Count; b++)
		{
			IReadOnlyList<RectTransform> members = blocks[b].Members;
			for (int m = 0; m < members.Count; m++)
			{
				RectTransform member = members[m];
				if (member == null || member.parent != _content)
					continue;

				member.SetSiblingIndex(sibling++);
			}
		}

		for (int u = 0; u < units.Count; u++)
		{
			RectTransform unit = units[u];
			if (unit == null)
				continue;

			unit.SetSiblingIndex(sibling++);
		}

		for (int i = 0; i < _content.childCount; i++)
		{
			Transform child = _content.GetChild(i);
			if (child == null)
				continue;

			LayoutElement layout = child.GetComponent<LayoutElement>();
			if (layout == null || !layout.ignoreLayout)
				continue;

			child.SetAsFirstSibling();
		}

		LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
	}
	#endregion

	#region Event Handlers
	public void OnDrop(PointerEventData eventData)
	{
		GameObject unitRoot = ResolveUnitRoot(eventData);
		if (unitRoot == null)
			return;

		if (!TryAcceptUnit(unitRoot))
			return;

		MissionPrepUnitCellDrag drag = ResolveDrag(eventData);
		drag?.NotifyDropAccepted();
	}
	#endregion

	#region Private Methods
	private void AddMember(RectTransform _member, bool _collapsible)
	{
		if (_member == null || m_Members.Contains(_member))
			return;

		m_Members.Add(_member);
		if (!_collapsible)
			return;

		m_CollapsibleMembers.Add(_member);
		if (m_Expanded)
			return;

		if (_member.TryGetComponent(out MissionPrepVehicleSeatSlotView seat))
			seat.SetRosterExpanded(false);
		else
			_member.gameObject.SetActive(false);
	}

	private void ApplyExpandedState()
	{
		for (int i = 0; i < m_CollapsibleMembers.Count; i++)
		{
			RectTransform member = m_CollapsibleMembers[i];
			if (member == null)
				continue;

			if (member.TryGetComponent(out MissionPrepVehicleSeatSlotView seat))
			{
				seat.SetRosterExpanded(m_Expanded);
				continue;
			}

			if (member.gameObject.activeSelf != m_Expanded)
				member.gameObject.SetActive(m_Expanded);
		}

		RectTransform content = transform.parent as RectTransform;
		if (content != null)
			LayoutRebuilder.ForceRebuildLayoutImmediate(content);
	}

	private void CollapseSiblings()
	{
		Transform parent = transform.parent;
		if (parent == null)
			return;

		MissionPrepVehicleRosterBlock[] blocks =
			parent.GetComponentsInChildren<MissionPrepVehicleRosterBlock>(true);
		for (int i = 0; i < blocks.Length; i++)
		{
			MissionPrepVehicleRosterBlock block = blocks[i];
			if (block == null || block == this || !block.m_Expanded)
				continue;

			block.m_Expanded = false;
			block.ApplyExpandedState();
		}
	}

	private MissionPrepVehicleSeatSlotView FindFirstEmptySeat()
	{
		for (int i = 0; i < m_CollapsibleMembers.Count; i++)
		{
			RectTransform member = m_CollapsibleMembers[i];
			if (member == null ||
			    !member.TryGetComponent(out MissionPrepVehicleSeatSlotView seat) ||
			    seat.Vehicle == null ||
			    seat.IsOccupied)
				continue;

			return seat;
		}

		return null;
	}

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

	private static GameObject ResolveUnitRoot(PointerEventData eventData)
	{
		MissionPrepUnitCellDrag drag = ResolveDrag(eventData);
		if (drag == null || drag.Cell == null || drag.Cell.IsVehicleCell)
			return null;

		return drag.UnitRoot;
	}
	#endregion
}
