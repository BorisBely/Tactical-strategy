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

	public static void Enqueue(Object _object)
	{
		if (_object == null)
			return;

		if (s_Instance == null || !Application.isPlaying)
		{
			Object.Destroy(_object);
			return;
		}

		s_Instance.EnqueueInternal(_object);
	}
	#endregion

	#region Private Fields
	private readonly List<Object> m_Pending = new List<Object>(16);
	private Coroutine m_FlushCoroutine;
	#endregion

	#region Private Methods
	private void EnqueueInternal(Object _object)
	{
		m_Pending.Add(_object);
		if (m_FlushCoroutine == null)
			m_FlushCoroutine = StartCoroutine(FlushNextFrame());
	}

	private IEnumerator FlushNextFrame()
	{
		yield return null;
		m_FlushCoroutine = null;

		for (int i = 0; i < m_Pending.Count; i++)
		{
			Object pending = m_Pending[i];
			if (pending != null)
				Destroy(pending);
		}

		m_Pending.Clear();
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
