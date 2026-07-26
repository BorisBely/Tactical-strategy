using UnityEngine;

public sealed class TransmissionModel
{
	#region Private Fields
	private readonly VehiclePhysicsProfile.TransmissionSettings m_Settings;
	private int m_CurrentGear = 0; // 0 = нейтраль, 1..N = передачи, -1 = задний ход
	private float m_ShiftTimer;
	private float m_ClutchEngagement = 1f;
	#endregion

	#region Constructor
	public TransmissionModel(VehiclePhysicsProfile.TransmissionSettings settings)
	{
		m_Settings = settings;
		if (settings.GearRatios.Length > 1)
			m_CurrentGear = 1;
	}
	#endregion

	#region Public Properties
	public int CurrentGear => m_CurrentGear;
	public float CurrentRatio => GetRatio(m_CurrentGear);
	public float ClutchEngagement => m_ClutchEngagement;
	public bool IsShifting => m_ShiftTimer > 0f;
	#endregion

	#region Public Methods
	public void Tick(float engineRPM, float throttle, float speedKmh, float dt)
	{
		if (m_ShiftTimer > 0f)
		{
			m_ShiftTimer -= dt;
			m_ClutchEngagement = m_ShiftTimer <= 0f
				? 1f
				: 1f - Mathf.Clamp01(m_ShiftTimer / m_Settings.ShiftTime);
			return;
		}

		m_ClutchEngagement = 1f;

		bool forward = throttle >= 0f;
		int maxGear = m_Settings.GearRatios.Length - 1;

		if (m_CurrentGear == 0)
			return;

		if (m_CurrentGear > 0 && engineRPM > m_Settings.ShiftUpRPM && m_CurrentGear < maxGear)
		{
			Shift(m_CurrentGear + 1);
		}
		else if (m_CurrentGear > 1 && engineRPM < m_Settings.ShiftDownRPM)
		{
			Shift(m_CurrentGear - 1);
		}
	}

	public void SetReverse()
	{
		if (m_ShiftTimer > 0f)
			return;
		m_CurrentGear = -1;
	}

	public void SetNeutral()
	{
		m_CurrentGear = 0;
		m_ShiftTimer = 0f;
		m_ClutchEngagement = 1f;
	}
	#endregion

	#region Private Methods
	private void Shift(int targetGear)
	{
		m_CurrentGear = targetGear;
		m_ShiftTimer = m_Settings.ShiftTime;
		m_ClutchEngagement = 0f;
	}

	private float GetRatio(int gear)
	{
		if (m_Settings.GearRatios == null || m_Settings.GearRatios.Length == 0)
			return 1f;

		// gear = -1 → первое (задний ход)
		// gear = 1 → второе (первая передача вперёд), и т.д.
		int index = gear <= 0 ? 0 : gear;
		if (index >= m_Settings.GearRatios.Length)
			index = m_Settings.GearRatios.Length - 1;

		return Mathf.Abs(m_Settings.GearRatios[index]);
	}
	#endregion
}
