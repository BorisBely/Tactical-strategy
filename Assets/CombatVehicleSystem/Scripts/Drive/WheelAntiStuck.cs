using UnityEngine;

namespace CombatVehicleSystem
{
	[RequireComponent(typeof(WheelCollider))]
	public class WheelAntiStuck : MonoBehaviour
	{
		#region Serialized Fields
		[SerializeField] private bool m_Enabled = true;
		[SerializeField] private Transform m_WheelVisual;
		[SerializeField] private int m_RayCount = 24;
		[SerializeField] private float m_RayArcDegrees = 160f;
		[SerializeField] private float m_WheelWidth = 0.3f;
		[SerializeField] private float m_CorrectionSpeed = 6f;
		[SerializeField, Min(0f)] private float m_MaxRadiusOffset = 0.08f;
		[SerializeField, Min(0f)] private float m_MaxSpeedKmh = 5f;
		[SerializeField, Min(0f)] private float m_StuckHoldSeconds = 0.5f;
		[SerializeField, Min(0f)] private float m_StuckSpeedKmh = 0.3f;
		[SerializeField] private float m_ReturnSpeed = 1.5f;
		#endregion

		#region Private Fields
		private WheelCollider m_Collider;
		private float m_BaseRadius;
		private Transform m_Root;
		private Rigidbody m_Body;
		private int m_ObstacleMask;
		private float m_StuckTimer;
		private bool m_IsStuck;
		private float m_InflateTarget;
		#endregion

		#region Public Properties
		public bool IsEnabled
		{
			get => m_Enabled;
			set => m_Enabled = value;
		}
		#endregion

		#region Unity Lifecycle
		private void Awake()
		{
			m_Collider = GetComponent<WheelCollider>();
			m_BaseRadius = m_Collider.radius;
			VehicleBrain brain = GetComponentInParent<VehicleBrain>();
			m_Root = brain != null ? brain.transform : transform.root;
			m_Body = m_Root != null ? m_Root.GetComponent<Rigidbody>() : null;
			m_ObstacleMask = BuildObstacleMask();
		}

		private void FixedUpdate()
		{
			if (!m_Enabled || m_WheelVisual == null || m_Collider == null)
				return;

			if (m_Body == null)
				return;

			float speedKmh = m_Body.linearVelocity.magnitude * 3.6f;
			float dt = Time.fixedDeltaTime;

			if (speedKmh > m_MaxSpeedKmh)
			{
				m_StuckTimer = 0f;
				m_IsStuck = false;
				ReturnToBaseRadius(dt);
				return;
			}

			bool obstacleAhead = HasObstacle(out float neededOffset);
			bool motorActive = Mathf.Abs(m_Collider.motorTorque) > 0.01f;
			bool speedDead = speedKmh < m_StuckSpeedKmh;

			if (obstacleAhead && motorActive && speedDead)
			{
				m_StuckTimer += dt;
				if (m_StuckTimer >= m_StuckHoldSeconds)
				{
					m_IsStuck = true;
					m_InflateTarget = m_BaseRadius + Mathf.Min(neededOffset, m_MaxRadiusOffset);
				}
			}
			else
			{
				m_StuckTimer = Mathf.Max(0f, m_StuckTimer - dt * 0.5f);
				if (m_StuckTimer <= 0f)
					m_IsStuck = false;
			}

			if (m_IsStuck)
			{
				m_Collider.radius = m_InflateTarget;
			}
			else
			{
				ReturnToBaseRadius(dt);
			}
		}
		#endregion

		#region Public Methods
		public void BindVisual(Transform _visual)
		{
			m_WheelVisual = _visual;
		}

		public void ConfigureSoft(float _maxOffset, float _maxSpeedKmh, float _correctionSpeed)
		{
			m_MaxRadiusOffset = Mathf.Max(0f, _maxOffset);
			m_MaxSpeedKmh = Mathf.Max(1f, _maxSpeedKmh);
			m_CorrectionSpeed = Mathf.Max(0.1f, _correctionSpeed);
			m_Enabled = true;
			enabled = true;
		}
		#endregion

		#region Private Methods
		private void ReturnToBaseRadius(float _dt)
		{
			float current = m_Collider.radius;
			if (Mathf.Abs(current - m_BaseRadius) < 0.0005f)
			{
				m_Collider.radius = m_BaseRadius;
				return;
			}

			m_Collider.radius = Mathf.MoveTowards(current, m_BaseRadius, m_ReturnSpeed * _dt);
		}

		private bool HasObstacle(out float _neededOffset)
		{
			_neededOffset = 0f;
			for (int i = 0; i <= m_RayCount; i++)
			{
				float angle = i * (m_RayArcDegrees / m_RayCount) + ((180f - m_RayArcDegrees) * 0.5f);
				Vector3 rayDirection =
					Quaternion.AngleAxis(m_Collider.steerAngle, transform.up) *
					Quaternion.AngleAxis(angle, transform.right) *
					transform.up;

				Vector3 origin = m_WheelVisual.position;
				SampleRay(origin, rayDirection, ref _neededOffset);
				SampleRay(origin + m_WheelVisual.right * m_WheelWidth * 0.5f, rayDirection, ref _neededOffset);
				SampleRay(origin - m_WheelVisual.right * m_WheelWidth * 0.5f, rayDirection, ref _neededOffset);
			}

			return _neededOffset > 0.0005f;
		}

		private static int BuildObstacleMask()
		{
			int mask = ~0;
			int ground = LayerMask.NameToLayer("Ground");
			int vehicle = LayerMask.NameToLayer("Vehicle");
			int unit = LayerMask.NameToLayer("Unit");
			int ignore = LayerMask.NameToLayer("Ignore Raycast");
			if (ground >= 0)
				mask &= ~(1 << ground);
			if (vehicle >= 0)
				mask &= ~(1 << vehicle);
			if (unit >= 0)
				mask &= ~(1 << unit);
			if (ignore >= 0)
				mask &= ~(1 << ignore);
			return mask;
		}

		private void SampleRay(Vector3 _origin, Vector3 _direction, ref float _radiusOffset)
		{
			float maxDist = m_BaseRadius + m_MaxRadiusOffset;
			if (!Physics.Raycast(_origin, _direction, out RaycastHit hit, maxDist, m_ObstacleMask, QueryTriggerInteraction.Ignore))
				return;
			if (m_Root != null && hit.transform.IsChildOf(m_Root))
				return;

			float needed = m_BaseRadius - hit.distance;
			if (needed > _radiusOffset)
				_radiusOffset = needed;
		}
		#endregion
	}
}
