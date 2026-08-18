using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Current perception state for a unit. Accepts observation frames from any producer
/// (today: UnitVision; later: sound / shared info) via <see cref="ApplyVisionFrame"/>.
/// Does not depend on UnitVision — Vision pushes data here, Perception does not require Vision.
///
/// Today stores only the current scan frame (CurrentlyObserved).
/// Later may add LastObserved / memory without changing Vision API — do not implement here yet.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public sealed class UnitPerception : MonoBehaviour
{
	#region Private Fields
	private readonly List<VisionObservation> m_Observations = new List<VisionObservation>(32);
	#endregion

	#region Public Properties
	public IReadOnlyList<VisionObservation> Observations => m_Observations;

	public int ObservationCount => m_Observations.Count;

	public bool HasAnyObservation => m_Observations.Count > 0;
	#endregion

	#region Public Events
	/// <summary>Fired when observation content differs from the previous frame.</summary>
	public event Action PerceptionChanged;

	/// <summary>Fired on every <see cref="ApplyVisionFrame"/> call (even if content unchanged).</summary>
	public event Action PerceptionFrameApplied;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		// TargetSelector lives beside Perception and selects from PerceptionFrameApplied.
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
