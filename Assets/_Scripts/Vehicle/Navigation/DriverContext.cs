using UnityEngine;

namespace VehicleNavigation
{
	public enum GearDirection
	{
		Forward,
		Reverse,
		Neutral
	}

	public enum DriverIntent
	{
		DriveForward,
		Reverse,
		TurnAround,
		ThreePointTurn,
		Parking,
		Wait
	}

	/// <summary>
	/// Unified vehicle state passed to all subsystems each FixedUpdate.
	/// No subsystem reads Transform / Rigidbody directly — everything comes from here.
	/// </summary>
	public sealed class DriverContext
	{
		public Vector3 Position;
		public Vector3 Forward;
		public Vector3 Right;
		public float Yaw;
		public float SpeedKmh;
		public float SpeedSignedKmh;
		public Vector3 Velocity;
		public float VelocitySqr;

		public Vector3 RearAxlePosition => Position - Forward * (WheelBase * 0.5f);
		public Vector3 FrontAxlePosition => Position + Forward * (WheelBase * 0.5f);

		public float WheelBase;
		public float Width;
		public float Length;
		public float TurnRadius;
		public float MaxSteeringAngleDeg;
		public float MaxSteeringAngleRad;
		public float SteeringRateDegPerSec;

		public float CurrentSteerAngle;
		public float CurrentThrottle;
		public GearDirection CurrentGear;

		public float RemainingDistance;
		public int CurrentPathSegment;
		public Vector3 NearestPathPoint;

		public VehicleLocalGeometry.Sample Geometry;
		public bool IsStuck;
		public float StuckTimer;
		public bool IsStopped;
		public bool IsAirborne;
		public bool IsUpright;
		public bool IsReversing;

		public float MaxForwardSpeedKmh;
		public float MaxReverseSpeedKmh;
		public float WheelMotorSpeedCapKmh;
		public AnimationCurve CurvatureSpeedCurve;
		public AnimationCurve ReverseSteeringLimitCurve;

		public NavigationRequest Request;
		public PathResult Path;

		public DriverContext()
		{
			if (ReverseSteeringLimitCurve == null)
				ReverseSteeringLimitCurve = CreateDefaultSteeringLimitCurve();
		}

		public void UpdateFrom(FeedbackState _fb, VehicleParameters _p, NavigationRequest _req, PathResult _path)
		{
			Position = _fb.Position;
			Forward = _fb.Forward;
			Right = _fb.Right;
			Yaw = _fb.Yaw;
			SpeedKmh = _fb.SpeedKmh;
			SpeedSignedKmh = _fb.SpeedSignedKmh;
			VelocitySqr = _fb.VelocitySqr;
			IsReversing = _fb.IsReversing;
			IsStopped = _fb.IsStopped;
			IsStuck = _fb.IsStuck;
			IsAirborne = _fb.IsAirborne;
			IsUpright = _fb.IsUpright;
			Geometry = _fb.Geometry;

			WheelBase = _p.WheelBase;
			Width = _p.Width;
			Length = _p.Length;
			TurnRadius = _p.MinTurningRadius;
			MaxSteeringAngleDeg = _p.MaxSteeringAngleDeg;
			MaxSteeringAngleRad = _p.MaxSteeringAngleRad;
			SteeringRateDegPerSec = _p.SteeringRateDegPerSec;
			MaxForwardSpeedKmh = _p.MaxForwardSpeedKmh;
			MaxReverseSpeedKmh = _p.MaxReverseSpeedKmh;
			CurvatureSpeedCurve = _p.CurvatureSpeedCurve;

			Request = _req;
			Path = _path;

			if (ReverseSteeringLimitCurve == null)
				ReverseSteeringLimitCurve = CreateDefaultSteeringLimitCurve();
		}

		public void SetSpeedCap(float _capKmh)
		{
			WheelMotorSpeedCapKmh = _capKmh;
		}

		private static AnimationCurve CreateDefaultSteeringLimitCurve()
		{
			return new AnimationCurve(
				new Keyframe(0f, 1f),
				new Keyframe(5f, 0.9f),
				new Keyframe(10f, 0.7f),
				new Keyframe(15f, 0.5f),
				new Keyframe(20f, 0.3f));
		}
	}
}
