using UnityEngine;

public sealed class AerodynamicsModel
{
	#region Private Fields
	private readonly VehiclePhysicsProfile.AerodynamicsSettings m_Settings;
	#endregion

	#region Constructor
	public AerodynamicsModel(VehiclePhysicsProfile.AerodynamicsSettings settings)
	{
		m_Settings = settings;
	}
	#endregion

	#region Public Properties
	public float CurrentDragForce { get; private set; }
	#endregion

	#region Public Methods
	public void Apply(Rigidbody body)
	{
		if (body == null)
			return;

		float speed = body.linearVelocity.magnitude;
		float speedSq = speed * speed;
		float dragForce = 0.5f * m_Settings.AirDensity * m_Settings.DragCoefficient * m_Settings.FrontalArea * speedSq;

		if (speed > 0.001f)
		{
			Vector3 dragDir = -body.linearVelocity.normalized;
			Vector3 dragPoint = body.worldCenterOfMass;
			body.AddForceAtPosition(dragDir * dragForce, dragPoint, ForceMode.Force);
		}

		CurrentDragForce = dragForce;
	}
	#endregion
}
