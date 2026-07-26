using UnityEngine;

/// <summary>
/// Creates and updates 4 WheelColliders. Reads WheelState for each.
/// One wheel = one independent calculation.
/// </summary>
public class VehicleSuspension
{
	private readonly VehicleData m_Data;
	private readonly WheelCollider[] m_Wheels;
	private readonly WheelState[] m_States;

	public WheelCollider[] Wheels => m_Wheels;
	public WheelState[] States => m_States;

	public VehicleSuspension(VehicleData data)
	{
		m_Data = data;
		m_Wheels = new WheelCollider[4];
		m_States = new WheelState[4];
	}

	/// <summary>
	/// Create 4 WheelCollider GameObjects as children of parent.
	/// Call once in Awake.
	/// </summary>
	public void CreateWheels(Transform parent)
	{
		string[] names = { "WC_FL", "WC_FR", "WC_RL", "WC_RR" };

		for (int i = 0; i < 4; i++)
		{
			GameObject go = new GameObject(names[i]);
			go.transform.SetParent(parent, false);
			go.transform.localPosition = m_Data.WheelLocalPositions[i];
			go.transform.localRotation = Quaternion.identity;
			go.layer = parent.gameObject.layer;

			WheelCollider wc = go.AddComponent<WheelCollider>();
			wc.radius = m_Data.WheelRadius;
			wc.mass = m_Data.WheelMass;
			wc.center = Vector3.zero;
			wc.wheelDampingRate = m_Data.WheelDampingRate;
			wc.suspensionDistance = m_Data.SuspensionTravel;
			wc.forceAppPointDistance = m_Data.ForceAppPointDistance;

			JointSpring s = wc.suspensionSpring;
			s.spring = m_Data.SpringRate;
			s.damper = m_Data.DamperRate;
			s.targetPosition = m_Data.RestCompression;
			wc.suspensionSpring = s;

			WheelFrictionCurve fwd = wc.forwardFriction;
			fwd.extremumSlip = m_Data.ForwardExtremumSlip;
			fwd.extremumValue = 1f;
			fwd.asymptoteSlip = m_Data.ForwardAsymptoteSlip;
			fwd.asymptoteValue = m_Data.ForwardAsymptoteValue;
			fwd.stiffness = m_Data.ForwardStiffness;
			wc.forwardFriction = fwd;

			WheelFrictionCurve side = wc.sidewaysFriction;
			side.extremumSlip = m_Data.SidewaysExtremumSlip;
			side.extremumValue = 1f;
			side.asymptoteSlip = m_Data.SidewaysAsymptoteSlip;
			side.asymptoteValue = m_Data.SidewaysAsymptoteValue;
			side.stiffness = m_Data.SidewaysStiffness;
			wc.sidewaysFriction = side;

			if (m_Data.UseVehicleSubsteps)
				wc.ConfigureVehicleSubsteps(
					m_Data.SubstepsSpeedThreshold,
					m_Data.SubstepsBelow,
					m_Data.SubstepsAbove);

			m_Wheels[i] = wc;
		}

		// Distribute sprung mass after all wheels exist
		if (m_Wheels[0] != null)
			m_Wheels[0].ResetSprungMasses();
	}

	/// <summary>
	/// Apply engine output to wheels, read back WheelState.
	/// Call every FixedUpdate.
	/// </summary>
	public void Update(VehicleEngine engine, float currentSpeedMs, Rigidbody body)
	{
		for (int i = 0; i < 4; i++)
		{
			WheelCollider wc = m_Wheels[i];
			if (wc == null) continue;

			if (m_Data.SteerAxles[i])
				wc.steerAngle = engine.SteerAngle;

			if (wc.GetGroundHit(out _))
			{
				wc.motorTorque = engine.MotorTorque;
				wc.brakeTorque = engine.BrakeTorque;
			}
			else
			{
				wc.motorTorque = 0f;
				wc.brakeTorque = 0f;
			}

			if (m_Data.AntiStuckEnabled)
				UpdateAntiStuck(wc, i, currentSpeedMs);

			m_States[i] = ReadWheelState(wc);
		}
	}

	private static bool TestWheelInAxle(int i, TestAxle axle)
	{
		return axle switch
		{
			TestAxle.All => true,
			TestAxle.Front => i == 0 || i == 1,
			TestAxle.Rear => i == 2 || i == 3,
			TestAxle.Left => i == 0 || i == 2,
			TestAxle.Right => i == 1 || i == 3,
			_ => true,
		};
	}

	private void UpdateAntiStuck(WheelCollider wc, int index, float speedMs)
	{
		float speedKmh = speedMs * 3.6f;
		if (speedKmh > m_Data.AntiStuckMaxSpeedKmh)
		{
			// Smooth return to base radius
			wc.radius = Mathf.MoveTowards(wc.radius, m_Data.WheelRadius, 1.5f * Time.fixedDeltaTime);
			return;
		}
		// Simple check: if wheel has zero rpm while motor is active and speed is dead
		if (Mathf.Abs(wc.motorTorque) > 1f && speedKmh < 0.5f && Mathf.Abs(wc.rpm) < 1f)
		{
			wc.radius = Mathf.Min(wc.radius + 0.01f, m_Data.WheelRadius + m_Data.AntiStuckMaxOffset);
		}
		else
		{
			wc.radius = Mathf.MoveTowards(wc.radius, m_Data.WheelRadius, 1.5f * Time.fixedDeltaTime);
		}
	}

	private WheelState ReadWheelState(WheelCollider wc)
	{
		WheelState s = new WheelState();
		wc.GetWorldPose(out Vector3 pos, out _);
		s.WorldCenter = pos;

		if (wc.GetGroundHit(out WheelHit hit))
		{
			s.HasContact = true;
			s.ContactPoint = hit.point;
			s.ContactNormal = hit.normal;
			s.SuspensionForce = hit.force;
			s.SlipRatio = hit.forwardSlip;
			s.SidewaysSlip = hit.sidewaysSlip;
		}

		s.Rpm = wc.rpm;
		s.SteerAngleDeg = wc.steerAngle;

		// Suspension compression: 0=full droop, 1=full compression
		Transform body = wc.transform.parent;
		float hubNoLoad = body.position.y + wc.transform.localPosition.y;
		s.SuspensionCompression = Mathf.Clamp01(1f - (hubNoLoad - pos.y) / wc.suspensionDistance);

		return s;
	}
}
