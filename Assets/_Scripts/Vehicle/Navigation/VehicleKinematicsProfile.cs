using CombatVehicleSystem;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Single source of truth for vehicle geometry used by planning and control.
	/// </summary>
	public sealed class VehicleKinematicsProfile
	{
		public float WheelBase { get; }
		public float Length { get; }
		public float Width { get; }
		public float FrontAxleOffset { get; }
		public float RearAxleOffset { get; }
		public float FrontOverhang { get; }
		public float RearOverhang { get; }
		public float MaxSteeringAngleDeg { get; }
		public float MinTurningRadius { get; }
		public float RearAxleTurningRadius { get; }
		public float SafetyMargin { get; }
		public float TurnRadiusMultiplier { get; }

		public float EffectiveTurnRadius => MinTurningRadius * TurnRadiusMultiplier;
		public float NavAgentRadius => Width * 0.5f + SafetyMargin;

		public VehicleKinematicsProfile(
			float _wheelBase,
			float _length,
			float _width,
			float _maxSteeringAngleDeg,
			float _safetyMargin = 0.3f,
			float _turnRadiusMultiplier = 1f)
		{
			WheelBase = Mathf.Max(0.5f, _wheelBase);
			Length = Mathf.Max(WheelBase + 0.2f, _length);
			Width = Mathf.Max(0.5f, _width);
			MaxSteeringAngleDeg = Mathf.Clamp(_maxSteeringAngleDeg, 10f, 60f);
			SafetyMargin = Mathf.Max(0f, _safetyMargin);
			TurnRadiusMultiplier = Mathf.Max(1f, _turnRadiusMultiplier);

			FrontAxleOffset = WheelBase * 0.5f;
			RearAxleOffset = WheelBase * 0.5f;
			FrontOverhang = Mathf.Max(0f, (Length - WheelBase) * 0.5f);
			RearOverhang = FrontOverhang;

			float steerRad = MaxSteeringAngleDeg * Mathf.Deg2Rad;
			MinTurningRadius = WheelBase / Mathf.Tan(steerRad);
			RearAxleTurningRadius = Mathf.Sqrt(
				MinTurningRadius * MinTurningRadius + RearAxleOffset * RearAxleOffset);
		}

		public static VehicleKinematicsProfile FromVehicle(
			Transform _root,
			VehicleTuning _tuning,
			VehicleNavigationSettings _settings = null)
		{
			float wheelBase = _tuning != null ? _tuning.WheelBase : 3.5f;
			float maxSteer = _tuning != null ? _tuning.DefaultSteerAngle : 30f;
			float length = 4.8f;
			float width = 2.4f;

			if (_root != null)
			{
				Bounds? bounds = TryGetBounds(_root);
				if (bounds.HasValue)
				{
					length = Mathf.Max(length, bounds.Value.size.z);
					width = Mathf.Max(width, bounds.Value.size.x);
				}
			}

			float margin = _settings != null ? _settings.SafetyMargin : 0.3f;
			float multiplier = _settings != null ? _settings.TurnRadiusMultiplier : 1f;
			return new VehicleKinematicsProfile(
				wheelBase, length, width, maxSteer, margin, multiplier);
		}

		public VehicleParameters ToVehicleParameters(VehicleTuning _tuning)
		{
			if (_tuning == null)
				return VehicleParameters.Default;

			float brakeDecel = _tuning.HardBrakeTorque / Mathf.Max(1f, _tuning.RigidbodyMass);
			if (brakeDecel < 1f) brakeDecel = 5.5f;

			return new VehicleParameters(
				Length, Width, WheelBase,
				_tuning.TopSpeedKmh,
				_tuning.TopSpeedKmh * 0.35f,
				MaxSteeringAngleDeg,
				_tuning.SteerRate,
				brakeDecel,
				_tuning.CurvatureSpeedCurve,
				this);
		}

		public Vector3 FrontAxlePosition(Vector3 _position, Vector3 _forward)
		{
			return _position + _forward.normalized * FrontAxleOffset;
		}

		public Vector3 RearAxlePosition(Vector3 _position, Vector3 _forward)
		{
			return _position - _forward.normalized * RearAxleOffset;
		}

		private static Bounds? TryGetBounds(Transform _root)
		{
			Collider[] colliders = _root.GetComponentsInChildren<Collider>();
			if (colliders == null || colliders.Length == 0)
				return null;

			Bounds bounds = colliders[0].bounds;
			for (int i = 1; i < colliders.Length; i++)
				bounds.Encapsulate(colliders[i].bounds);
			return bounds;
		}
	}
}
