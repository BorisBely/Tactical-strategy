using CombatVehicleSystem;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Gathers all vehicle/environment state once per physics step.
	/// </summary>
	public sealed class FeedbackSystem
	{
		private readonly Transform m_Transform;
		private readonly Rigidbody m_Body;
		private readonly WheeledMotor m_WheeledMotor;
		private readonly LayerMask m_GeometryMask;
		private readonly float m_VehicleWidth;
		private readonly float m_StuckSpeedKmh;
		private readonly float m_StuckTime;
		private readonly float m_AirborneTime;
		private readonly bool m_LightweightProbes;

		private float m_StuckTimer;
		private float m_AirborneTimer;
		private bool m_WasReversing;

		public float StuckTimerValue => m_StuckTimer;

		public FeedbackSystem(
			Transform _transform,
			Rigidbody _body,
			WheeledMotor _wheeledMotor,
			LayerMask _geometryMask,
			float _vehicleWidth,
			float _stuckSpeedKmh = 1.2f,
			float _stuckTime = 3f,
			float _airborneTime = 0.35f,
			bool _lightweightProbes = true)
		{
			m_Transform = _transform;
			m_Body = _body;
			m_WheeledMotor = _wheeledMotor;
			m_GeometryMask = _geometryMask;
			m_VehicleWidth = _vehicleWidth;
			m_StuckSpeedKmh = _stuckSpeedKmh;
			m_StuckTime = _stuckTime;
			m_AirborneTime = _airborneTime;
			m_LightweightProbes = _lightweightProbes;
		}

		public FeedbackState Update(float _dt, bool _isReversing)
		{
			Vector3 position = m_Transform.position;
			Vector3 forward = FlatDir(m_Transform.forward);
			Vector3 right = FlatDir(m_Transform.right);
			float yaw = m_Transform.eulerAngles.y;

			Vector3 velocity = m_Body != null ? m_Body.linearVelocity : Vector3.zero;
			float signedSpeed = Vector3.Dot(velocity, forward) * 3.6f;
			float speedKmh = velocity.magnitude * 3.6f;
			bool reversing = _isReversing;

			int grounded = CountGroundedWheels();
			bool airborne = grounded == 0 && speedKmh > 0.2f;
			if (airborne)
				m_AirborneTimer += _dt;
			else
				m_AirborneTimer = Mathf.Max(0f, m_AirborneTimer - _dt * 0.5f);

			bool isStopped = speedKmh < 0.2f;
			if (speedKmh < m_StuckSpeedKmh)
				m_StuckTimer += _dt;
			else
				m_StuckTimer = Mathf.Max(0f, m_StuckTimer - _dt * 0.7f);

			bool isStuck = m_StuckTimer >= m_StuckTime && !isStopped;
			bool isUpright = Vector3.Dot(m_Transform.up, Vector3.up) > 0.45f;

			VehicleLocalGeometry.Sample geometry = m_LightweightProbes
				? VehicleLocalGeometry.ProbeLightweight(m_Transform, m_VehicleWidth, m_GeometryMask)
				: VehicleLocalGeometry.Probe(m_Transform, m_VehicleWidth, m_GeometryMask);

			m_WasReversing = reversing;

			return new FeedbackState(
				position,
				forward,
				right,
				yaw,
				speedKmh,
				signedSpeed,
				velocity.sqrMagnitude,
				reversing,
				isStopped,
				isStuck,
				m_AirborneTimer >= m_AirborneTime,
				isUpright,
				geometry,
				null);
		}

		public void ResetStuckTimer()
		{
			m_StuckTimer = 0f;
		}

		private int CountGroundedWheels()
		{
			if (m_WheeledMotor == null || m_WheeledMotor.Axles == null)
				return 0;

			int grounded = 0;
			for (int i = 0; i < m_WheeledMotor.Axles.Length; i++)
			{
				if (m_WheeledMotor.Axles[i]?.Collider != null &&
				    m_WheeledMotor.Axles[i].Collider.GetGroundHit(out _))
					grounded++;
			}

			return grounded;
		}

		private static Vector3 FlatDir(Vector3 _v)
		{
			_v.y = 0f;
			return _v.sqrMagnitude > 0.0001f ? _v.normalized : Vector3.forward;
		}
	}
}
