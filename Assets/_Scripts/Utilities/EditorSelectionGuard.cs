using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// В редакторе при удалении UI-слотов <see cref="Object.Destroy"/> откладывается до конца кадра — инспектор успевает
/// вызвать <c>Behaviour.enabled</c> на уже уничтоженном <see cref="UnityEngine.UI.Image"/>,
/// <see cref="UnityEngine.UI.ScrollRect"/> или <see cref="TMPro.TextMeshProUGUI"/>.
/// В Play Mode в редакторе используем отложенный <see cref="Object.Destroy"/> (не <see cref="Object.DestroyImmediate"/> —
/// он запрещён в animation event, physics и других callback). Очистка Selection — через <c>delayCall</c>.
/// Также чинит SerializedObjectNotCreatableException после выхода из Play Mode (мёртвые Inspector Editor’ы).
/// </summary>
public static class EditorSelectionGuard
{
	public static void ClearHierarchySelectionIfUnderTransform(Transform _panelSubtreeRoot)
	{
		if (_panelSubtreeRoot == null)
			return;

#if UNITY_EDITOR
		Object[] objs = UnityEditor.Selection.objects;
		if (objs == null || objs.Length == 0)
			return;

		for (int i = 0; i < objs.Length; i++)
		{
			Transform t = ResolveTransform(objs[i]);
			if (t == null)
				continue;

			if (t == _panelSubtreeRoot || t.IsChildOf(_panelSubtreeRoot))
			{
				UnityEditor.Selection.objects = System.Array.Empty<Object>();
				break;
			}
		}
#endif
	}

	/// <summary>Один рантайм-слот (иконка/строка), порождённый из префаба.</summary>
	public static void DestroyRuntimeSpawnedSlot(GameObject _slotRoot, Transform _inventoryPanelRoot)
	{
		if (_slotRoot == null)
			return;

#if UNITY_EDITOR
		ClearHierarchySelectionIfUnderTransform(_inventoryPanelRoot);
		if (Application.isPlaying)
		{
			// DestroyImmediate запрещён в animation event / physics callbacks — только отложенный Destroy.
			_slotRoot.SetActive(false);
			Object.Destroy(_slotRoot);
			ScheduleSanitizeSelectionAfterDestroy();
			return;
		}

		Object.Destroy(_slotRoot);
		ScheduleSanitizeSelectionAfterDestroy();
#else
		Object.Destroy(_slotRoot);
#endif
	}

	/// <summary>Массовое удаление слотов после <c>m_SpawnedSlots.Clear()</c> в учётных списках.</summary>
	public static void DestroyRuntimeSpawnedSlotsBatch(List<GameObject> _slotRoots, Transform _inventoryPanelRoot)
	{
		if (_slotRoots == null || _slotRoots.Count == 0)
			return;

#if UNITY_EDITOR
		ClearHierarchySelectionIfUnderTransform(_inventoryPanelRoot);
		if (Application.isPlaying)
		{
			for (int i = 0; i < _slotRoots.Count; i++)
			{
				GameObject go = _slotRoots[i];
				if (go == null)
					continue;

				go.SetActive(false);
				Object.Destroy(go);
			}

			ScheduleSanitizeSelectionAfterDestroy();
			return;
		}

		for (int i = 0; i < _slotRoots.Count; i++)
		{
			GameObject go = _slotRoots[i];
			if (go != null)
				Object.Destroy(go);
		}

		ScheduleSanitizeSelectionAfterDestroy();
#else
		for (int i = 0; i < _slotRoots.Count; i++)
		{
			GameObject go = _slotRoots[i];
			if (go != null)
				Object.Destroy(go);
		}
#endif
	}

	public static void ScheduleSanitizeSelectionAfterDestroy()
	{
#if UNITY_EDITOR
		if (s_ScheduledSanitize)
			return;

		s_ScheduledSanitize = true;
		UnityEditor.EditorApplication.delayCall += StripDestroyedObjectsFromSelectionDelayed;
#endif
	}

	/// <summary>Clears destroyed scene objects from Selection (Play Mode exit, runtime AddComponent destroy).</summary>
	public static void SanitizeSelectionRemovingDestroyedObjects()
	{
#if UNITY_EDITOR
		RepairStaleInspectorState();
#endif
	}

	/// <summary>Full Inspector repair: unlock tracker, clear selection, rebuild editors.</summary>
	public static void FixStaleInspectorEditors()
	{
#if UNITY_EDITOR
		UnlockInspectorTracker();
		ClearSelectionOnly();
		RebuildInspectorEditors();
#endif
	}

#if UNITY_EDITOR
	private static bool s_ScheduledSanitize;
	private static int s_PlayModeSanitizePassesRemaining;
	private static bool s_SelectionHookRegistered;
	private static bool s_Repairing;

	[UnityEditor.InitializeOnLoadMethod]
	private static void RegisterPlayModeSelectionSanitizer()
	{
		UnityEditor.EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
		UnityEditor.EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;

		if (!s_SelectionHookRegistered)
		{
			s_SelectionHookRegistered = true;
			UnityEditor.Selection.selectionChanged += HandleSelectionChanged;
		}
	}

	private static void HandlePlayModeStateChanged(UnityEditor.PlayModeStateChange _state)
	{
		// ExitingPlayMode: clear selection ONLY — do NOT ForceRebuild.
		// Rebuild here recreates Editors against play-mode instances that die a moment later
		// → SerializedObjectNotCreatableException on next Inspector draw.
		if (_state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
		{
			UnlockInspectorTracker();
			ClearSelectionOnly();
			SchedulePlayModeSelectionSanitizeBurst(48);
			return;
		}

		if (_state == UnityEditor.PlayModeStateChange.EnteredEditMode)
		{
			UnlockInspectorTracker();
			ClearSelectionOnly();
			RebuildInspectorEditors();
			SchedulePlayModeSelectionSanitizeBurst(48);
			UnityEditor.EditorApplication.delayCall += () =>
			{
				UnlockInspectorTracker();
				ClearSelectionOnly();
				RebuildInspectorEditors();
			};
		}
	}

	private static void HandleSelectionChanged()
	{
		if (s_Repairing)
			return;

		RepairStaleInspectorState();
	}

	private static void SchedulePlayModeSelectionSanitizeBurst(int _passes)
	{
		s_PlayModeSanitizePassesRemaining = Mathf.Max(s_PlayModeSanitizePassesRemaining, _passes);
		ScheduleSanitizeSelectionAfterDestroy();
		UnityEditor.EditorApplication.update -= SanitizeSelectionOnEditorUpdateBurst;
		UnityEditor.EditorApplication.update += SanitizeSelectionOnEditorUpdateBurst;
	}

	private static void SanitizeSelectionOnEditorUpdateBurst()
	{
		RepairStaleInspectorState();

		if (s_PlayModeSanitizePassesRemaining > 0)
			s_PlayModeSanitizePassesRemaining--;

		if (s_PlayModeSanitizePassesRemaining <= 0)
			UnityEditor.EditorApplication.update -= SanitizeSelectionOnEditorUpdateBurst;
	}

	private static void StripDestroyedObjectsFromSelectionDelayed()
	{
		s_ScheduledSanitize = false;
		RepairStaleInspectorState();
	}

	private static void RepairStaleInspectorState()
	{
		if (s_Repairing)
			return;

		s_Repairing = true;
		try
		{
			bool destroyedSelection = HasDestroyedSelection();
			bool staleEditors = HasStaleActiveEditors();

			if (!destroyedSelection && !staleEditors)
				return;

			UnlockInspectorTracker();

			if (destroyedSelection)
				ClearSelectionOnly();

			// Always rebuild when editors are stale — even if Hierarchy selection is a valid Edit Mode object.
			// That is the usual post-Play failure mode: Selection OK, ActiveEditorTracker still holds dead Editors.
			RebuildInspectorEditors();
		}
		finally
		{
			s_Repairing = false;
		}
	}

	private static bool HasDestroyedSelection()
	{
		Object active = UnityEditor.Selection.activeObject;
		if (active != null && !IsSelectableEditorObject(active))
			return true;

		Object[] objs = UnityEditor.Selection.objects;
		if (objs == null)
			return false;

		for (int i = 0; i < objs.Length; i++)
		{
			if (!IsSelectableEditorObject(objs[i]))
				return true;
		}

		return false;
	}

	private static bool HasStaleActiveEditors()
	{
		try
		{
			UnityEditor.Editor[] editors = UnityEditor.ActiveEditorTracker.sharedTracker?.activeEditors;
			if (editors == null)
				return false;

			for (int i = 0; i < editors.Length; i++)
			{
				UnityEditor.Editor editor = editors[i];
				if (editor == null)
					continue;

				Object[] targets;
				try
				{
					targets = editor.targets;
				}
				catch
				{
					return true;
				}

				if (targets == null || targets.Length == 0)
					return true;

				for (int t = 0; t < targets.Length; t++)
				{
					if (!IsSelectableEditorObject(targets[t]))
						return true;
				}
			}
		}
		catch
		{
			return true;
		}

		return false;
	}

	private static void ClearSelectionOnly()
	{
		UnityEditor.Selection.activeObject = null;
		UnityEditor.Selection.objects = System.Array.Empty<Object>();
	}

	private static void UnlockInspectorTracker()
	{
		try
		{
			UnityEditor.ActiveEditorTracker tracker = UnityEditor.ActiveEditorTracker.sharedTracker;
			if (tracker != null && tracker.isLocked)
				tracker.isLocked = false;
		}
		catch
		{
			// Tracker may be unavailable during domain reload.
		}
	}

	private static void RebuildInspectorEditors()
	{
		try
		{
			UnityEditor.ActiveEditorTracker.sharedTracker?.ForceRebuild();
		}
		catch
		{
			// Ignore rebuild failures during play-mode teardown.
		}
	}

	private static bool IsSelectableEditorObject(Object _object)
	{
		// Unity fake-null: destroyed objects compare equal to null via overloaded ==.
		if (_object == null)
			return false;

		if (_object is GameObject go)
			return go;

		if (_object is Component component)
			return component && component.gameObject;

		return true;
	}
#endif

	private static Transform ResolveTransform(Object _o)
	{
		if (_o == null)
			return null;

		switch (_o)
		{
			case GameObject go:
				return go.transform;
			case Component c:
				return c.transform;
			default:
				return null;
		}
	}
}
