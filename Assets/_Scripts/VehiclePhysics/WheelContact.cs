using UnityEngine;

public sealed class WheelContact
{
	#region Private Fields
	private readonly IWheelInterface m_Wheel;
	private readonly VehiclePhysicsProfile.TireSettings m_TireSettings;
	private float m_StaticLoad;
	private float m_Load;
	private float m_AvailableGrip;
	private float m_SlipRatio;
	#endregion

	#region Constructor
	public WheelContact(IWheelInterface wheel, VehiclePhysicsProfile.TireSettings tireSettings, float staticLoad)
	{
		m_Wheel = wheel;
		m_TireSettings = tireSettings;
		m_StaticLoad = staticLoad;
	}
	#endregion

	#region Public Properties
	public float Load => m_Load;
	public float StaticLoad => m_StaticLoad;
	public float LoadRatio => m_StaticLoad > 0.001f ? m_Load / m_StaticLoad : 1f;
	public float AvailableGrip => m_AvailableGrip;
	public float SlipRatio => m_SlipRatio;
	#endregion

	#region Public Methods
	public void Update(float totalMass, Vector3 centerOfMass, Rigidbody body,
		SurfacePhysicsDefinition surface, float dt)
	{
		if (m_Wheel != null && m_Wheel.IsGrounded)
		{
			// нагрузка = сила реакции опоры ≈ масса_на_колесо * g * cos(уклон)
			m_Load = Mathf.Max(0f, m_StaticLoad);

			Vector3 hitNormal = m_Wheel.HitNormal;
			float slopeFactor = Mathf.Abs(Vector3.Dot(hitNormal, Vector3.up));
			m_Load *= slopeFactor;

			if (body != null)
			{
				float bounceCompensation = Mathf.Max(0f, -body.linearVelocity.y) * 0.1f * totalMass;
				m_Load += bounceCompensation;
			}
		}
		else
		{
			m_Load = 0f;
		}

		// доступное сцепление = коэффициент × нагрузка × степень 0.8 (нелинейность)
		float gripCoeff = m_TireSettings.ForwardGrip;
		if (surface != null)
		{
			gripCoeff *= surface.ForwardGripMultiplier;
		}

		m_AvailableGrip = gripCoeff * Mathf.Pow(Mathf.Max(0.1f, m_Load), 0.8f);

		// slip ratio
		if (m_Wheel != null)
		{
			m_SlipRatio = Mathf.Abs(m_Wheel.SlipForward) + Mathf.Abs(m_Wheel.SlipSideways) * 0.5f;
		}
	}

	public void SetStaticLoad(float load)
	{
		m_StaticLoad = load;
	}
	#endregion
}
