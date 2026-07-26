using UnityEngine;

/// <summary>
/// Smooth visual follower. Place on a SEPARATE GameObject (not the vehicle body).
/// Follows the vehicle Rigidbody position/rotation with low-pass filter.
/// Camera and visual elements reference THIS smoothed transform, not the physics body.
/// </summary>
public class BodySmooth : MonoBehaviour
{
	public Rigidbody TargetRigidbody;
	[SerializeField] private float m_PositionSmooth = 8f;
	[SerializeField] private float m_RotationSmooth = 10f;

	public Vector3 SmoothPosition { get; private set; }
	public Quaternion SmoothRotation { get; private set; }

	private bool m_Init;

	private void Start()
	{
		if (TargetRigidbody != null)
		{
			SmoothPosition = TargetRigidbody.position;
			SmoothRotation = TargetRigidbody.rotation;
			m_Init = true;
		}
	}

	private void LateUpdate()
	{
		if (TargetRigidbody == null) return;

		if (!m_Init)
		{
			SmoothPosition = TargetRigidbody.position;
			SmoothRotation = TargetRigidbody.rotation;
			m_Init = true;
			transform.SetPositionAndRotation(SmoothPosition, SmoothRotation);
			return;
		}

		float dt = Time.deltaTime;
		float tPos = 1f - Mathf.Exp(-m_PositionSmooth * dt);
		float tRot = 1f - Mathf.Exp(-m_RotationSmooth * dt);

		SmoothPosition = Vector3.Lerp(SmoothPosition, TargetRigidbody.position, tPos);
		SmoothRotation = Quaternion.Slerp(SmoothRotation, TargetRigidbody.rotation, tRot);

		transform.SetPositionAndRotation(SmoothPosition, SmoothRotation);
	}
}
