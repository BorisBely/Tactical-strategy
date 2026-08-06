using UnityEngine;

namespace VehicleNavigation
{
	public enum StagnationKind
	{
		None,
		NoMotion,
		Diverging,
		NoPathProgress,
		ControllerOscillation
	}

	public readonly struct NavigationProgressSnapshot
	{
		public readonly float ArcLengthRemaining;
		public readonly float DistanceToGoal;
		public readonly float YawErrorDeg;
		public readonly float PoseError;
		public readonly float AlongTrackProgress;
		public readonly int SegmentIndex;
		public readonly int PathRevision;
		public readonly int ReplanCount;
		public readonly VehicleDrivingMode SelectedMode;
		public readonly string PlanReason;
		public readonly string FailureReason;
		public readonly StagnationKind Stagnation;

		public NavigationProgressSnapshot(
			float _arcLengthRemaining,
			float _distanceToGoal,
			float _yawErrorDeg,
			float _alongTrackProgress,
			int _segmentIndex,
			int _pathRevision,
			int _replanCount,
			VehicleDrivingMode _selectedMode,
			string _planReason,
			string _failureReason,
			StagnationKind _stagnation)
		{
			ArcLengthRemaining = _arcLengthRemaining;
			DistanceToGoal = _distanceToGoal;
			YawErrorDeg = _yawErrorDeg;
			PoseError = _distanceToGoal + Mathf.Abs(_yawErrorDeg) * 0.05f;
			AlongTrackProgress = _alongTrackProgress;
			SegmentIndex = _segmentIndex;
			PathRevision = _pathRevision;
			ReplanCount = _replanCount;
			SelectedMode = _selectedMode;
			PlanReason = _planReason ?? string.Empty;
			FailureReason = _failureReason ?? string.Empty;
			Stagnation = _stagnation;
		}

		public static NavigationProgressSnapshot Empty => new NavigationProgressSnapshot(
			0f, 0f, 0f, 0f, 0, 0, 0,
			VehicleDrivingMode.Forward, string.Empty, string.Empty, StagnationKind.None);
	}
}
