using UnityEngine;

/// <summary>
/// #12 target stability. Selection score is G5; this gate decides whether to switch.
/// Slightly better is not enough. Lost current is not hysteresis.
/// </summary>
public enum TargetSwitchReason
{
	None = 0,
	InitialSelect = 1,
	HigherScore = 2,
	Hysteresis = 3,
	LostCurrent = 4,
	ForcedPriority = 5,
	WeaponMaintenanceRetain = 6
}

/// <summary>
/// Snapshot of one TargetSelector pass. Selection ≠ Engageable ≠ Fire.
/// </summary>
public struct TargetSelectionSnapshot
{
	public Transform Selected;
	public Transform RunnerUp;
	public float SelectedScore;
	public float RunnerUpScore;
	public float CurrentScore;
	public float CandidateScore;
	public float SwitchThreshold;
	public bool Switched;
	public TargetSwitchReason SwitchReason;
	public bool Engageable;
	public bool HasAimPoint;
	public int ScoredCount;
	public int RegistryCount;
	public string RejectSummary;
}

/// <summary>
/// Pure hysteresis: NewScore &gt; CurrentScore + SwitchThreshold.
/// Does not change <see cref="TargetSelectionMath.Score"/>.
/// </summary>
public static class TargetSwitchMath
{
	#region Constants
	public const float DefaultSwitchThreshold = 0.45f;
	#endregion

	#region Public Methods
	public static bool ShouldSwitch(
		Transform _current,
		bool _currentEligible,
		float _currentScore,
		Transform _candidate,
		float _candidateScore,
		float _switchThreshold,
		out TargetSwitchReason _reason)
	{
		if (_candidate == null)
		{
			if (_current == null)
			{
				_reason = TargetSwitchReason.None;
				return false;
			}

			_reason = _currentEligible ? TargetSwitchReason.None : TargetSwitchReason.LostCurrent;
			return !_currentEligible;
		}

		if (_current == null)
		{
			_reason = TargetSwitchReason.InitialSelect;
			return true;
		}

		if (ReferenceEquals(_current, _candidate))
		{
			_reason = TargetSwitchReason.None;
			return false;
		}

		if (!_currentEligible)
		{
			_reason = TargetSwitchReason.LostCurrent;
			return true;
		}

		float threshold = Mathf.Max(0f, _switchThreshold);
		if (_candidateScore > _currentScore + threshold)
		{
			_reason = TargetSwitchReason.HigherScore;
			return true;
		}

		_reason = TargetSwitchReason.Hysteresis;
		return false;
	}
	#endregion
}
