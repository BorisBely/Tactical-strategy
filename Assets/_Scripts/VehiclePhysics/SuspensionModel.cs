using UnityEngine;

public sealed class SuspensionModel
{
	#region Private Fields
	private readonly VehiclePhysicsProfile.SuspensionSettings m_Settings;
	private float m_SpringRate;
	private float m_DamperCompression;
	private float m_DamperRebound;
	private bool m_Calculated;
	#endregion

	#region Constructor
	public SuspensionModel(VehiclePhysicsProfile.SuspensionSettings settings)
	{
		m_Settings = settings;
	}
	#endregion

	#region Public Properties
	public float SpringRate => m_SpringRate;
	public float DamperCompression => m_DamperCompression;
	public float DamperRebound => m_DamperRebound;
	public float Travel => m_Settings.Travel;
	public float TargetPosition => m_Settings.TargetPosition;
	public float AntiRollStiffness => m_Settings.AntiRollStiffness;
	public float RideHeight => m_Settings.RideHeight;
	#endregion

	#region Public Methods
	public void CalculateForMass(float totalMass, int wheelCount)
	{
		if (wheelCount <= 0)
			return;

		float staticLoadPerWheel = (totalMass * Physics.gravity.magnitude) / wheelCount;

		m_SpringRate = staticLoadPerWheel / Mathf.Max(0.001f, m_Settings.DesiredSag);

		float massPerWheel = totalMass / wheelCount;
		float criticalDamping = 2f * Mathf.Sqrt(m_SpringRate * massPerWheel);

		m_DamperCompression = criticalDamping * m_Settings.DampingRatio;
		m_DamperRebound = m_DamperCompression * Mathf.Max(1f, m_Settings.DamperReboundRatio);

		m_Calculated = true;
	}

	public SuspensionState GetState()
	{
		return new SuspensionState
		{
			springRate = m_Calculated ? m_SpringRate : 35000f,
			damperCompression = m_Calculated ? m_DamperCompression : 4500f,
			damperRebound = m_Calculated ? m_DamperRebound : 9000f,
			travel = m_Settings.Travel,
			targetPosition = m_Settings.TargetPosition
		};
	}
	#endregion
}
