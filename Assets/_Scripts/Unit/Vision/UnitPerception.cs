using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Current perception state for a unit. Accepts observation frames from any producer
/// (Vision, Sound, Shared) via typed Apply* APIs. Does not depend on UnitVision.
/// Vision, sound, and shared lists stay separate — Sound does not pretend to be Vision.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public sealed class UnitPerception : MonoBehaviour
{
	#region Private Fields
	private readonly List<VisionObservation> m_Observations = new List<VisionObservation>(32);
	private readonly List<SoundObservation> m_SoundEvents = new List<SoundObservation>(8);
	private readonly List<SharedObservation> m_SharedEvents = new List<SharedObservation>(8);
	#endregion

	#region Public Properties
	public IReadOnlyList<VisionObservation> Observations => m_Observations;

	public IReadOnlyList<SoundObservation> SoundEvents => m_SoundEvents;

	public IReadOnlyList<SharedObservation> SharedEvents => m_SharedEvents;

	public int ObservationCount => m_Observations.Count;

	public bool HasAnyObservation => m_Observations.Count > 0;
	#endregion

	#region Public Events
	/// <summary>Fired when observation content differs from the previous frame.</summary>
	public event Action PerceptionChanged;

	/// <summary>Fired on every <see cref="ApplyVisionFrame"/> call (even if content unchanged).</summary>
	public event Action PerceptionFrameApplied;

	public event Action SoundEventsApplied;

	public event Action SharedEventsApplied;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (GetComponent<TargetSelector>() == null)
			gameObject.AddComponent<TargetSelector>();
	}
	#endregion

	#region Public Methods
	/// <summary>Replace the current perception frame with the latest detections.</summary>
	public void ApplyVisionFrame(IReadOnlyList<VisionObservation> _frame)
	{
		bool changed = !FramesEqual(m_Observations, _frame);

		m_Observations.Clear();
		if (_frame != null)
		{
			for (int i = 0; i < _frame.Count; i++)
				m_Observations.Add(_frame[i]);
		}

		if (changed)
			PerceptionChanged?.Invoke();

		PerceptionFrameApplied?.Invoke();
	}

	public void ApplySoundEvents(IReadOnlyList<SoundObservation> _events)
	{
		m_SoundEvents.Clear();
		if (_events != null)
		{
			for (int i = 0; i < _events.Count; i++)
				m_SoundEvents.Add(_events[i]);
		}

		SoundEventsApplied?.Invoke();
	}

	public void ApplySharedEvents(IReadOnlyList<SharedObservation> _events)
	{
		m_SharedEvents.Clear();
		if (_events != null)
		{
			for (int i = 0; i < _events.Count; i++)
				m_SharedEvents.Add(_events[i]);
		}

		SharedEventsApplied?.Invoke();
	}

	public bool TryGetObservation(Transform _target, out VisionObservation _observation)
	{
		_observation = default;
		if (_target == null)
			return false;

		for (int i = 0; i < m_Observations.Count; i++)
		{
			if (m_Observations[i].Target == _target)
			{
				_observation = m_Observations[i];
				return true;
			}
		}

		return false;
	}
	#endregion

	#region Private Methods
	private static bool FramesEqual(
		IReadOnlyList<VisionObservation> _a,
		IReadOnlyList<VisionObservation> _b)
	{
		int countA = _a != null ? _a.Count : 0;
		int countB = _b != null ? _b.Count : 0;
		if (countA != countB)
			return false;

		for (int i = 0; i < countA; i++)
		{
			VisionObservation left = _a[i];
			VisionObservation right = _b[i];
			if (left.Target != right.Target ||
			    left.HasAimPoint != right.HasAimPoint ||
			    left.IsVisible != right.IsVisible)
				return false;

			if (left.HasAimPoint && (left.AimPoint - right.AimPoint).sqrMagnitude > 0.0001f)
				return false;
		}

		return true;
	}
	#endregion
}
