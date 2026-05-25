using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// В редакторе при удалении UI-слотов <see cref="Object.Destroy"/> откладывается до конца кадра — инспектор успевает
/// вызвать <c>Behaviour.enabled</c> на уже уничтоженном <see cref="UnityEngine.UI.Image"/> / <see cref="UnityEngine.UI.ScrollRect"/>.
/// В Play Mode в редакторе используем отложенный <see cref="Object.Destroy"/> (не <see cref="Object.DestroyImmediate"/> —
/// он запрещён в animation event, physics и других callback). Очистка Selection — через <c>delayCall</c>.
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
				if (go != null)
					Object.DestroyImmediate(go);
			}

			SanitizeSelectionRemovingDestroyedObjects();
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

#if UNITY_EDITOR
	private static bool s_ScheduledSanitize;

	private static void StripDestroyedObjectsFromSelectionDelayed()
	{
		s_ScheduledSanitize = false;
		SanitizeSelectionRemovingDestroyedObjects();
	}

	private static void SanitizeSelectionRemovingDestroyedObjects()
	{
		Object[] objs = UnityEditor.Selection.objects;
		if (objs == null || objs.Length == 0)
			return;

		int validCount = 0;
		for (int i = 0; i < objs.Length; i++)
		{
			if (objs[i] != null)
				validCount++;
		}

		if (validCount == objs.Length)
			return;

		if (validCount == 0)
		{
			UnityEditor.Selection.objects = System.Array.Empty<Object>();
			return;
		}

		Object[] filtered = new Object[validCount];
		int w = 0;
		for (int i = 0; i < objs.Length; i++)
		{
			Object o = objs[i];
			if (o != null)
				filtered[w++] = o;
		}

		UnityEditor.Selection.objects = filtered;
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
