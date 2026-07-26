using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Pure geometric analysis of the vehicle relative to the target.
	/// Does NOT make decisions — only reports what is true.
	/// </summary>
	public sealed class ArrivalAnalysis
	{
		public float Distance;                    // flat distance to target
		public float HeadingError;                // degrees between nose and target direction
		public float LateralOffset;               // lateral distance to target line
		public bool TargetInFront;                // target is in front hemisphere
		public bool TargetInsideTurningCircle;    // target is closer than turn radius
		public bool TargetInsideRearTurningCircle; // target is closer than turn radius from rear
		public bool CanReachForward;              // can reach by driving forward
		public bool CanReachReverse;              // can reach by driving backward

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
			r.TargetInFront = Mathf.Abs(signedAngle) <= 90f;
			r.TargetInsideTurningCircle = r.Distance < _turnRadius * 0.9f;

			Vector3 rearForward = Quaternion.Euler(0f, _yaw + 180f, 0f) * Vector3.forward;
			float signedAngleRear = Vector3.SignedAngle(rearForward, toTargetDir, Vector3.up);
			r.TargetInsideRearTurningCircle = r.Distance < _turnRadius * 0.7f && Mathf.Abs(signedAngleRear) < 40f;

			r.LateralOffset = Mathf.Abs(Mathf.Sin(signedAngle * Mathf.Deg2Rad)) * r.Distance;

			r.CanReachForward = r.TargetInFront && r.Distance >= _turnRadius * 0.5f;
			r.CanReachReverse = !r.TargetInFront && r.Distance < _turnRadius * 2f && r.Distance >= _turnRadius * 0.3f;

			return r;
		}
	}
}
