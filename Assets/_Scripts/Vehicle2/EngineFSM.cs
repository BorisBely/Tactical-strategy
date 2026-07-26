using UnityEngine;

/// <summary>
/// FSM: Drive/Coast/Hold/Brake. Uses rawThrottle (not smoothed).
/// Coast→Drive is INSTANT (driver wants go). Drive→Coast has delay (hysteresis).
/// </summary>
public class EngineFSM
{
	private readonly float m_CoastThr;
	private readonly float m_DriveThr;
	private readonly float m_HoldSpd;
	private readonly float m_DownDelay;   // Drive→Coast delay
	private readonly float m_MinDriveTime; // minimum Drive time

	private DriveMode m_Mode = DriveMode.Hold;
	private float m_DownTimer;
	private float m_DriveAge;

	public DriveMode Current => m_Mode;
	public DriveMode Pending { get; private set; }
	public float TransitionTimer => m_DownTimer;

	public EngineFSM(float coastThr, float driveThr, float holdSpd, float downDelay, float minDriveTime)
	{
		m_CoastThr = coastThr;
		m_DriveThr = driveThr;
		m_HoldSpd = holdSpd;
		m_DownDelay = downDelay;
		m_MinDriveTime = minDriveTime;
		Pending = m_Mode;
	}

	public void Update(bool brake, float rawThrottle, float speedMs, float dt)
	{
		float absRaw = Mathf.Abs(rawThrottle);

		if (brake)
		{
			m_Mode = DriveMode.Brake; Pending = m_Mode;
			m_DownTimer = 0f; m_DriveAge = 0f;
			return;
		}

		m_DriveAge += dt;

		// === IMMEDIATE UP: Coast/Hold → Drive when raw > driveThr ===
		if ((m_Mode == DriveMode.Coast || m_Mode == DriveMode.Hold) && absRaw > m_DriveThr)
		{
			m_Mode = DriveMode.Drive; Pending = m_Mode;
			m_DownTimer = 0f; m_DriveAge = 0f;
			return;
		}

		// === IMMEDIATE UP: Brake → Drive/Coast ===
		if (m_Mode == DriveMode.Brake)
		{
			m_Mode = absRaw > m_DriveThr ? DriveMode.Drive : DriveMode.Coast;
			Pending = m_Mode; m_DownTimer = 0f; m_DriveAge = 0f;
			return;
		}

		// === HOLD → COAST ===
		if (m_Mode == DriveMode.Hold && (absRaw > m_CoastThr || speedMs > m_HoldSpd))
		{
			m_Mode = DriveMode.Coast; Pending = m_Mode;
			m_DownTimer = 0f; m_DriveAge = 0f;
			return;
		}

		// === DRIVE → COAST (delayed) ===
		if (m_Mode == DriveMode.Drive && absRaw < m_CoastThr && m_DriveAge >= m_MinDriveTime)
		{
			m_DownTimer += dt;
			if (m_DownTimer >= m_DownDelay)
			{
				m_Mode = DriveMode.Coast; Pending = m_Mode;
				m_DownTimer = 0f; m_DriveAge = 0f;
			}
			return;
		}

		// === COAST → HOLD ===
		if (m_Mode == DriveMode.Coast && absRaw < m_CoastThr && speedMs < m_HoldSpd)
		{
			m_Mode = DriveMode.Hold; Pending = m_Mode;
			m_DownTimer = 0f; m_DriveAge = 0f;
			return;
		}

		Pending = m_Mode;
		if (m_Mode != DriveMode.Drive) m_DownTimer = 0f;
	}

	public void Force(DriveMode m) { m_Mode = m; Pending = m; m_DownTimer = 0f; m_DriveAge = 0f; }
}
