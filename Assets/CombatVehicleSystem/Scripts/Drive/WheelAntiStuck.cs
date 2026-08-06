using UnityEngine;

namespace CombatVehicleSystem
{
	/// <summary>
	/// Soft LPVC-style radius inflate against obstacles (not ground) to reduce curb stuck.
	/// Caps offset so it cannot launch the chassis like the old unrestricted wake inflate.
	/// </summary>
	[RequireComponent(typeof(WheelCollider))]
	public class WheelAntiStuck : MonoBehaviour
	{
		#region Serialized Fields
		[SerializeField] private bool m_Enabled = true;
		[SerializeField] private bool m_DemandOnly = true;
		[SerializeField] private Transform m_WheelVisual;
		[SerializeField] private int m_RayCount = 24;
		[SerializeField] private float m_RayArcDegrees = 160f;
		[SerializeField] private float m_WheelWidth = 0.3f;
		[SerializeField] private float m_CorrectionSpeed = 6f;
		[SerializeField, Min(0f)] private float m_MaxRadiusOffset = 0.08f;
		[SerializeField, Min(0f)] private float m_MaxSpeedKmh = 25f;
		#endregion

		#region Private Fields
		private WheelCollider m_Collider;
		private float m_BaseRadius;
		private Transform m_Root;
		private Rigidbody m_Body;
		private int m_ObstacleMask;
		private float m_DemandTimer;
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

			float dt = Time.fixedDeltaTime;
			if (m_DemandTimer > 0f)
				m_DemandTimer = Mathf.Max(0f, m_DemandTimer - dt);

			if (m_DemandOnly && m_DemandTimer <= 0f)
			{
				m_Collider.radius = Mathf.Lerp(
					m_Collider.radius,
					m_BaseRadius,
					dt * m_CorrectionSpeed);
				return;
			}

			if (m_Body != null)
			{
				float speedKmh = m_Body.linearVelocity.magnitude * 3.6f;
				if (speedKmh > m_MaxSpeedKmh)
				{
					m_Collider.radius = Mathf.Lerp(
						m_Collider.radius,
						m_BaseRadius,
						dt * m_CorrectionSpeed);
					return;
				}
			}

			float radiusOffset = 0f;
			for (int i = 0; i <= m_RayCount; i++)
			{
				Vector3 rayDirection =
					Quaternion.AngleAxis(m_Collider.steerAngle, transform.up) *
					Quaternion.AngleAxis(
						i * (m_RayArcDegrees / m_RayCount) + ((180f - m_RayArcDegrees) * 0.5f),
						transform.right) *
					transform.up;

				SampleRay(m_WheelVisual.position, rayDirection, ref radiusOffset);
				SampleRay(
					m_WheelVisual.position + m_WheelVisual.right * m_WheelWidth * 0.5f,
					rayDirection,
					ref radiusOffset);
				SampleRay(
					m_WheelVisual.position - m_WheelVisual.right * m_WheelWidth * 0.5f,
					rayDirection,
					ref radiusOffset);
			}

			radiusOffset = Mathf.Min(radiusOffset, m_MaxRadiusOffset);
			m_Collider.radius = Mathf.Lerp(
				m_Collider.radius,
				m_BaseRadius + radiusOffset,
				dt * m_CorrectionSpeed);
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

		/// <summary>
		/// Enable dense wheel fan for a short window (stuck / recovery only).
		/// </summary>
		public void RequestAssist(float _durationSeconds = 1.25f)
		{
			m_DemandTimer = Mathf.Max(m_DemandTimer, Mathf.Max(0.1f, _durationSeconds));
		}

		public void SetDemandOnly(bool _demandOnly) => m_DemandOnly = _demandOnly;
		#endregion

		#region Private Methods
		private static int BuildObstacleMask()
		{
			// Inflate only against props / default geometry — not ground planes (that launches).
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
			if (!Physics.Raycast(
				    _origin,
				    _direction,
				    out RaycastHit hit,
				    m_BaseRadius + m_MaxRadiusOffset,
				    m_ObstacleMask,
				    QueryTriggerInteraction.Ignore))
				return;
			if (m_Root != null && hit.transform.IsChildOf(m_Root))
				return;

			_radiusOffset = Mathf.Max(_radiusOffset, m_BaseRadius - hit.distance);
		}
		#endregion
	}
}
