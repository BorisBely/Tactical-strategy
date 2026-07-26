using UnityEngine;

public sealed class DrivetrainModel
{
	#region Private Fields
	private readonly VehiclePhysicsProfile.DrivetrainSettings m_Settings;
	#endregion

	#region Constructor
	public DrivetrainModel(VehiclePhysicsProfile.DrivetrainSettings settings)
	{
		m_Settings = settings;
	}
	#endregion

	#region Public Properties
	public int AxleCount => m_Settings.TorqueSplit != null ? m_Settings.TorqueSplit.Length : 1;
	#endregion

	#region Public Methods
	public void Distribute(float driveshaftTorque, float[] axleTorques)
	{
		if (m_Settings.TorqueSplit == null || m_Settings.TorqueSplit.Length == 0)
		{
			if (axleTorques.Length > 0)
				axleTorques[0] = driveshaftTorque;
			return;
		}

		float totalSplit = 0f;
		for (int i = 0; i < m_Settings.TorqueSplit.Length; i++)
			totalSplit += m_Settings.TorqueSplit[i];

		if (totalSplit < 0.001f)
		{
			float eq = driveshaftTorque / axleTorques.Length;
			for (int i = 0; i < axleTorques.Length; i++)
				axleTorques[i] = eq;
			return;
		}

		for (int i = 0; i < axleTorques.Length && i < m_Settings.TorqueSplit.Length; i++)
		{
			axleTorques[i] = driveshaftTorque * (m_Settings.TorqueSplit[i] / totalSplit);
		}
	}

	public float GetAxleRatio(int axleIndex)
	{
		if (m_Settings.TorqueSplit == null || m_Settings.TorqueSplit.Length == 0)
			return 1f;

		if (axleIndex < 0 || axleIndex >= m_Settings.TorqueSplit.Length)
			return 0f;

		float total = 0f;
		for (int i = 0; i < m_Settings.TorqueSplit.Length; i++)
			total += m_Settings.TorqueSplit[i];

		return total > 0.001f ? m_Settings.TorqueSplit[axleIndex] / total : 1f / m_Settings.TorqueSplit.Length;
	}
	#endregion
}
