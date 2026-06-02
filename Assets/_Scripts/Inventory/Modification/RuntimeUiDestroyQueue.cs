using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Откладывает Destroy UI-объектов на следующий кадр — безопасно во время drag/drop и layout rebuild.
/// </summary>
[DisallowMultipleComponent]
public sealed class RuntimeUiDestroyQueue : MonoBehaviour
{
	#region Static Access
	private static RuntimeUiDestroyQueue s_Instance;

	public static void EnsureOn(MonoBehaviour _host)
	{
		if (_host == null)
			return;

		if (s_Instance != null)
			return;

		if (!_host.TryGetComponent(out RuntimeUiDestroyQueue queue))
			queue = _host.gameObject.AddComponent<RuntimeUiDestroyQueue>();

		s_Instance = queue;
	}

	public static void Enqueue(Object _object, Transform _panelSubtreeRoot = null)
	{
		if (_object == null)
			return;

		if (s_Instance == null || !Application.isPlaying)
		{
			DestroyNow(_object, _panelSubtreeRoot);
			return;
		}

		s_Instance.EnqueueInternal(_object, _panelSubtreeRoot);
	}
	#endregion

	#region Private Types
	private struct PendingDestroy
	{
		public Object Target;
		public Transform PanelSubtreeRoot;
	}
	#endregion

	#region Private Fields
	private readonly List<PendingDestroy> m_Pending = new List<PendingDestroy>(16);
	private Coroutine m_FlushCoroutine;
	#endregion

	#region Private Methods
	private void EnqueueInternal(Object _object, Transform _panelSubtreeRoot)
	{
		m_Pending.Add(new PendingDestroy { Target = _object, PanelSubtreeRoot = _panelSubtreeRoot });
		if (m_FlushCoroutine == null)
			m_FlushCoroutine = StartCoroutine(FlushNextFrame());
	}

	private IEnumerator FlushNextFrame()
	{
		yield return null;
		m_FlushCoroutine = null;

		for (int i = 0; i < m_Pending.Count; i++)
		{
			PendingDestroy entry = m_Pending[i];
			Object pending = entry.Target;
			if (pending == null)
				continue;

			GameObject go = pending as GameObject;
			if (go == null && pending is Component component)
				go = component.gameObject;

			DestroyNow(pending, entry.PanelSubtreeRoot);
		}

		m_Pending.Clear();
	}

	private static void DestroyNow(Object _object, Transform _panelSubtreeRoot)
	{
		if (_object == null)
			return;

		GameObject go = _object as GameObject;
		if (go == null && _object is Component component)
			go = component.gameObject;

		if (go != null)
		{
			EditorSelectionGuard.DestroyRuntimeSpawnedSlot(go, _panelSubtreeRoot);
			return;
		}

		Object.Destroy(_object);
#if UNITY_EDITOR
		EditorSelectionGuard.ScheduleSanitizeSelectionAfterDestroy();
#endif
	}
	#endregion

	#region Unity Lifecycle
	private void OnDestroy()
	{
		if (s_Instance == this)
			s_Instance = null;
	}
	#endregion
}
