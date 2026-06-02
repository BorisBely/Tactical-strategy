using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// При перетаскивании modifiable-оружия inline-ряды модификации скрываются и восстанавливаются после drop.
/// </summary>
public static class RuntimeInlineModificationDragHelper
{
	#region Constants
	private const string c_DragStashName = "RuntimeModDragStash";
	#endregion

	public sealed class DragAttachment
	{
		public RectTransform MoveTarget;
		public RectTransform WeaponRect;
		public Transform OriginalContentParent;
		public int WeaponOriginalSiblingIndex;
		public InventoryPanelView SourcePanel;
		public readonly List<GameObject> HiddenModRows = new List<GameObject>(4);
	}

	public static DragAttachment Attach(
		InventorySlotView _weaponSlot,
		RectTransform _weaponRect,
		Canvas _rootCanvas,
		InventoryPanelView _sourcePanel)
	{
		var attachment = new DragAttachment
		{
			WeaponRect = _weaponRect,
			MoveTarget = _weaponRect,
			OriginalContentParent = _weaponRect != null ? _weaponRect.parent : null,
			WeaponOriginalSiblingIndex = _weaponRect != null ? _weaponRect.GetSiblingIndex() : -1,
			SourcePanel = _sourcePanel
		};

		if (_weaponSlot == null || _weaponRect == null || _rootCanvas == null || attachment.OriginalContentParent == null)
			return attachment;

		if (ItemModificationUtility.IsModifiableWeapon(_weaponSlot.Data.Definition))
		{
			Transform stash = GetOrCreateDragStash(_rootCanvas);
			HideFollowingModRows(attachment.OriginalContentParent, attachment.WeaponOriginalSiblingIndex, stash,
				attachment.HiddenModRows);
		}

		ReparentToCanvas(_weaponRect, _rootCanvas);

		return attachment;
	}

	public static Vector2 ComputeDragOffsetLocal(
		DragAttachment _attachment,
		PointerEventData _eventData,
		Canvas _rootCanvas)
	{
		if (_attachment?.MoveTarget == null || _rootCanvas == null || _eventData == null)
			return Vector2.zero;

		Camera cam = GetDragCamera(_eventData, _rootCanvas);
		RectTransform canvasRt = _rootCanvas.transform as RectTransform;
		if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
			    canvasRt, _eventData.pressPosition, cam, out Vector2 pressLocal))
			return Vector2.zero;

		return (Vector2)_attachment.MoveTarget.localPosition - pressLocal;
	}

	public static void UpdateDragPosition(
		DragAttachment _attachment,
		PointerEventData _eventData,
		Canvas _rootCanvas,
		Vector2 _dragOffsetLocal)
	{
		if (_attachment?.MoveTarget == null || _rootCanvas == null || _eventData == null)
			return;

		Camera cam = GetDragCamera(_eventData, _rootCanvas);
		RectTransform canvasRt = _rootCanvas.transform as RectTransform;
		if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
			    canvasRt, _eventData.position, cam, out Vector2 pointerLocal))
			return;

		Vector3 pos = _attachment.MoveTarget.localPosition;
		_attachment.MoveTarget.localPosition = new Vector3(
			pointerLocal.x + _dragOffsetLocal.x,
			pointerLocal.y + _dragOffsetLocal.y,
			pos.z);
	}

	public static void RestoreToContent(DragAttachment _attachment, InventoryPanelView _panel)
	{
		if (_attachment?.WeaponRect == null || _attachment.OriginalContentParent == null)
			return;

		_attachment.WeaponRect.SetParent(_attachment.OriginalContentParent, false);
		_attachment.WeaponRect.SetSiblingIndex(_attachment.WeaponOriginalSiblingIndex);

		if (_panel != null)
		{
			_panel.RefreshSlotsFromHierarchy();
			_panel.RebuildContentLayout();
		}

		QueueDestroyHiddenModRows(_attachment);
		RuntimeInventoryModificationCoordinator.Instance?.ScheduleRefreshInlineModificationRowsAfterDrag();
	}

	public static void CleanupAfterDrop(DragAttachment _attachment)
	{
		QueueDestroyHiddenModRows(_attachment);
		RuntimeInventoryModificationCoordinator.Instance?.ScheduleRefreshInlineModificationRowsAfterDrag();
	}

	private static void HideFollowingModRows(
		Transform _contentParent,
		int _weaponSiblingIndex,
		Transform _stash,
		List<GameObject> _hiddenOut)
	{
		_hiddenOut.Clear();
		if (_contentParent == null || _stash == null)
			return;

		for (int i = _weaponSiblingIndex + 1; i < _contentParent.childCount;)
		{
			Transform child = _contentParent.GetChild(i);
			if (child == null || child.GetComponent<RuntimeModificationSlotView>() == null)
				break;

			GameObject row = child.gameObject;
			row.SetActive(false);
			row.transform.SetParent(_stash, false);
			_hiddenOut.Add(row);
		}
	}

	private static void QueueDestroyHiddenModRows(DragAttachment _attachment)
	{
		if (_attachment?.HiddenModRows == null || _attachment.HiddenModRows.Count == 0)
			return;

		Transform panelRoot = _attachment.SourcePanel != null ? _attachment.SourcePanel.transform : null;
		for (int i = 0; i < _attachment.HiddenModRows.Count; i++)
			RuntimeUiDestroyQueue.Enqueue(_attachment.HiddenModRows[i], panelRoot);

		_attachment.HiddenModRows.Clear();
	}

	private static Transform GetOrCreateDragStash(Canvas _rootCanvas)
	{
		Transform canvasTransform = _rootCanvas.transform;
		Transform existing = canvasTransform.Find(c_DragStashName);
		if (existing != null)
			return existing;

		var stash = new GameObject(c_DragStashName, typeof(RectTransform));
		stash.SetActive(false);
		stash.transform.SetParent(canvasTransform, false);
		return stash.transform;
	}

	private static void ReparentToCanvas(RectTransform _weaponRect, Canvas _rootCanvas)
	{
		_weaponRect.SetParent(_rootCanvas.transform, true);
		_weaponRect.SetAsLastSibling();
	}

	private static Camera GetDragCamera(PointerEventData _eventData, Canvas _rootCanvas)
	{
		if (_rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
			return null;

		return _eventData.pressEventCamera != null ? _eventData.pressEventCamera : _rootCanvas.worldCamera;
	}
}
