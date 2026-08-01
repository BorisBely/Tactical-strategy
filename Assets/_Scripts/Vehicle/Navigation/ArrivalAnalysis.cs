using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Pure geometric analysis of the vehicle relative to the target.
	/// Does NOT make decisions — only reports what is true.
	/// </summary>
	public enum TargetSide { Front, Rear, Left, Right }
	public enum ArrivalStatus { TooFar, Approaching, AtGoal }

	public sealed class ArrivalAnalysis
	{
		public float Distance;
		public float HeadingError;
		public float LateralOffset;
		public bool TargetInFront;
		public bool TargetInsideTurningCircle;
		public bool TargetInsideRearTurningCircle;
		public bool CanReachForward;
		public bool CanReachReverse;
		public TargetSide Side;
		public ArrivalStatus Status;

		public static ArrivalAnalysis Compute(
			Vector3 _position, float _yaw, float _turnRadius,
			Vector3 _target, float? _targetHeading)
		{
			var r = new ArrivalAnalysis();

			Vector3 toTarget = _target - _position;
			toTarget.y = 0f;
			r.Distance = toTarget.magnitude;

			Vector3 forward = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
			Vector3 toTargetDir = r.Distance > 0.01f ? toTarget / r.Distance : forward;

			float signedAngle = Vector3.SignedAngle(forward, toTargetDir, Vector3.up);
			r.HeadingError = signedAngle;
			float absAngle = Mathf.Abs(signedAngle);
			r.TargetInFront = absAngle <= 90f;
			r.TargetInsideTurningCircle = r.Distance < _turnRadius * 0.9f;

			r.Side = absAngle <= 60f ? TargetSide.Front :
			         absAngle >= 120f ? TargetSide.Rear :
			         signedAngle > 0 ? TargetSide.Left : TargetSide.Right;

			Vector3 rearForward = Quaternion.Euler(0f, _yaw + 180f, 0f) * Vector3.forward;
			float signedAngleRear = Vector3.SignedAngle(rearForward, toTargetDir, Vector3.up);
			r.TargetInsideRearTurningCircle = r.Distance < _turnRadius * 0.7f && Mathf.Abs(signedAngleRear) < 40f;

		r.LateralOffset = Mathf.Abs(Mathf.Sin(signedAngle * Mathf.Deg2Rad)) * r.Distance;

		r.CanReachForward = r.TargetInFront && r.Distance >= _turnRadius * 0.5f;
		r.CanReachReverse = !r.TargetInFront && r.Distance < _turnRadius * 2f && r.Distance >= _turnRadius * 0.3f;

		r.Status = r.Distance < 0.15f && absAngle < 3f
			? ArrivalStatus.AtGoal
			: ArrivalStatus.Approaching;

		return r;
		}
	}
}
