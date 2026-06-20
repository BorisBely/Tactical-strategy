using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Отложенные действия drag (unparent), когда Unity запрещает SetParent во время OnDisable родителя.
/// </summary>
internal sealed class UnitFallenDragDeferredRunner : MonoBehaviour
{
	private static UnitFallenDragDeferredRunner s_Instance;
	private readonly List<Action> m_Actions = new List<Action>(4);

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Bootstrap()
	{
		EnsureInstance();
	}

	public static void Enqueue(Action _action)
	{
		if (_action == null)
			return;

		EnsureInstance();
		s_Instance.m_Actions.Add(_action);
	}

	private static void EnsureInstance()
	{
		if (s_Instance != null)
			return;

		var runnerObject = new GameObject(nameof(UnitFallenDragDeferredRunner));
		runnerObject.hideFlags = HideFlags.HideAndDontSave;
		DontDestroyOnLoad(runnerObject);
		s_Instance = runnerObject.AddComponent<UnitFallenDragDeferredRunner>();
	}

	private void LateUpdate()
	{
		if (m_Actions.Count == 0)
			return;

		Action[] batch = m_Actions.ToArray();
		m_Actions.Clear();

		for (int i = 0; i < batch.Length; i++)
			batch[i]?.Invoke();
	}
}
