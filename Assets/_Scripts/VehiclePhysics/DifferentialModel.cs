using UnityEngine;

public sealed class DifferentialModel
{
	#region Private Fields
	private readonly VehiclePhysicsProfile.DifferentialSettings m_Settings;
	private float m_LockFactor;
	#endregion

	#region Constructor
	public DifferentialModel(VehiclePhysicsProfile.DifferentialSettings settings)
	{
		m_Settings = settings;
	}
	#endregion

	#region Public Properties
	public float LockFactor => m_LockFactor;
	#endregion

	#region Public Methods
	public void Distribute(
		float axleTorque,
		float rpmLeft, float rpmRight,
		IWheelInterface wheelLeft, IWheelInterface wheelRight,
		out float torqueLeft, out float torqueRight)
	{
		float deltaRPM = Mathf.Abs(rpmLeft - rpmRight);
		m_LockFactor = ComputeLockFactor(deltaRPM);

		switch (m_Settings.DiffType)
		{
			case VehiclePhysicsProfile.DifferentialSettings.Type.Locked:
				torqueLeft = axleTorque * 0.5f;
				torqueRight = axleTorque * 0.5f;
				break;

			case VehiclePhysicsProfile.DifferentialSettings.Type.LimitedSlip:
				DistributeLimitedSlip(axleTorque, rpmLeft, rpmRight, wheelLeft, wheelRight,
					out torqueLeft, out torqueRight);
				break;

			case VehiclePhysicsProfile.DifferentialSettings.Type.Open:
			default:
				torqueLeft = axleTorque * 0.5f;
				torqueRight = axleTorque * 0.5f;
				break;
		}
	}
	#endregion

	#region Private Methods
	private float ComputeLockFactor(float deltaRPM)
	{
		if (m_Settings.DiffType != VehiclePhysicsProfile.DifferentialSettings.Type.LimitedSlip)
			return m_Settings.DiffType == VehiclePhysicsProfile.DifferentialSettings.Type.Locked ? 1f : 0f;

		if (m_Settings.LockThreshold <= 0f)
			return 0f;

		float ratio = Mathf.Clamp01(deltaRPM / m_Settings.LockThreshold);
		return ratio * m_Settings.LockStrength;
	}

	private void DistributeLimitedSlip(
		float axleTorque, float rpmLeft, float rpmRight,
		IWheelInterface wheelLeft, IWheelInterface wheelRight,
		out float torqueLeft, out float torqueRight)
	{
		bool leftGrounded = wheelLeft != null && wheelLeft.IsGrounded;
		bool rightGrounded = wheelRight != null && wheelRight.IsGrounded;

		// grip-based split
		float leftShare = leftGrounded ? 0.5f : 0f;
		float rightShare = rightGrounded ? 0.5f : 0f;

		if (!leftGrounded && rightGrounded)
		{
			leftShare = 0f;
			rightShare = 1f;
		}
		else if (leftGrounded && !rightGrounded)
		{
			leftShare = 1f;
			rightShare = 0f;
		}
		else if (leftGrounded && rightGrounded)
		{
			leftShare = 0.5f;
			rightShare = 0.5f;
		}

		// lock toward 50/50
		leftShare = Mathf.Lerp(leftShare, 0.5f, m_LockFactor);
		rightShare = Mathf.Lerp(rightShare, 0.5f, m_LockFactor);

		float total = leftShare + rightShare;
		if (total > 0.001f)
		{
			leftShare /= total;
			rightShare /= total;
		}
		else
		{
			leftShare = 0.5f;
			rightShare = 0.5f;
		}

		torqueLeft = axleTorque * leftShare;
		torqueRight = axleTorque * rightShare;
	}
	#endregion
}
