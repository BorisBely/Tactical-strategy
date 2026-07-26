using UnityEngine;

public sealed class TireModel
{
	#region Private Fields
	private readonly VehiclePhysicsProfile.TireSettings m_Settings;
	#endregion

	#region Constructor
	public TireModel(VehiclePhysicsProfile.TireSettings settings)
	{
		m_Settings = settings;
	}
	#endregion

	#region Public Methods
	public TireFrictionParams ComputeFriction(SurfacePhysicsDefinition surface, float wetness = 0f)
	{
		float surfaceGrip = surface != null ? surface.ForwardGripMultiplier : 1f;
		float wetPenalty = Mathf.Lerp(1f, m_Settings.WetPenalty, Mathf.Clamp01(wetness));

		float peakGrip = m_Settings.ForwardGrip * surfaceGrip * wetPenalty;
		float lateralGrip = m_Settings.LateralGrip * (surface != null ? surface.LateralGripMultiplier : 1f) * wetPenalty;

		float extremumSlip, asymptoteSlip, asymptoteGrip;

		switch (m_Settings.TireType)
		{
			case VehiclePhysicsProfile.TireSettings.TireTypeEnum.Road:
				extremumSlip = 0.8f;
				asymptoteSlip = 0.6f;
				asymptoteGrip = 0.5f;
				break;
			case VehiclePhysicsProfile.TireSettings.TireTypeEnum.OffRoad:
				extremumSlip = 2.0f;
				asymptoteSlip = 1.0f;
				asymptoteGrip = 0.7f;
				break;
			case VehiclePhysicsProfile.TireSettings.TireTypeEnum.Mud:
				extremumSlip = 3.0f;
				asymptoteSlip = 1.5f;
				asymptoteGrip = 0.65f;
				break;
			case VehiclePhysicsProfile.TireSettings.TireTypeEnum.Sand:
				extremumSlip = 2.5f;
				asymptoteSlip = 1.2f;
				asymptoteGrip = 0.5f;
				break;
			case VehiclePhysicsProfile.TireSettings.TireTypeEnum.AllTerrain:
			default:
				extremumSlip = 1.5f;
				asymptoteSlip = 0.8f;
				asymptoteGrip = 0.6f;
				break;
		}

		float stiffness = peakGrip * 2.5f;

		return new TireFrictionParams
		{
			extremumSlip = extremumSlip,
			extremumValue = peakGrip,
			asymptoteSlip = asymptoteSlip,
			asymptoteValue = peakGrip * asymptoteGrip,
			stiffness = Mathf.Max(0.5f, stiffness)
		};
	}

	public float ComputeRollingResistance(SurfacePhysicsDefinition surface, float load)
	{
		float rollingResist = m_Settings.RollingResistance;
		if (surface != null)
			rollingResist *= surface.RollingResistanceMultiplier;

		return rollingResist * load;
	}
	#endregion
}
