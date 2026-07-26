using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// RTS-движение машины по NavMesh: bicycle-руление, задний ход на коротком отрезке назад.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public sealed class VehicleMotor : MonoBehaviour
{
	#region Constants
	private const float c_ArriveDistance = 1.25f;
	private const float c_CornerReachDistance = 2.5f;
	private const float c_ReverseAngleDegrees = 120f;
	private const float c_ReverseMaxSegmentLength = 8f;
	#endregion

	#region Serialized Fields
	[Header("Speeds (m/s)")]
	[SerializeField, Min(0.1f)] private float m_MaxSpeed = 22f;
	[SerializeField, Min(0.1f)] private float m_CruiseSpeed = 14f;
	[SerializeField, Min(0.1f)] private float m_ReverseSpeed = 5f;
	[SerializeField, Min(0.1f)] private float m_Acceleration = 6f;
	[SerializeField, Min(0.1f)] private float m_Deceleration = 10f;

	[Header("Steering")]
	[SerializeField, Range(5f, 45f)] private float m_MaxSteerAngle = 26f;
	[SerializeField, Min(0.5f)] private float m_Wheelbase = 3.2f;
	[SerializeField, Min(1f)] private float m_MaxYawRateDegrees = 70f;
	[SerializeField, Min(0.5f)] private float m_MinTurnSpeedFactor = 0.45f;
	[SerializeField, Range(0f, 8f)] private float m_MaxCornerLeanDegrees = 3.5f;
	#endregion

	#region Private Fields
	private NavMeshAgent m_Agent;
	private NavMeshPath m_Path;
	private Vector3[] m_Corners = Array.Empty<Vector3>();
	private int m_CornerIndex;
	private float m_CurrentSpeed;
	private float m_SteerAngle;
	private float m_YawDegrees;
	private float m_LeanDegrees;
	private bool m_IsReversing;
	private bool m_HasDestination;
	private Vector3 m_Destination;
	#endregion

	#region Events
	public event Action Arrived;
	#endregion

	#region Public Properties
	public bool IsMoving => m_HasDestination && m_CurrentSpeed > 0.05f;
	public bool HasDestination => m_HasDestination;
	public float CurrentSpeed => m_CurrentSpeed;
	public float SignedSpeed => m_IsReversing ? -m_CurrentSpeed : m_CurrentSpeed;
	public float SteerAngle => m_SteerAngle;
	public float MaxSteerAngle => m_MaxSteerAngle;
	public float CruiseSpeed => m_CruiseSpeed;
	public bool IsReversing => m_IsReversing;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Agent = GetComponent<NavMeshAgent>();
		m_Path = new NavMeshPath();
		m_Agent.updatePosition = false;
		m_Agent.updateRotation = false;
		m_Agent.speed = m_CruiseSpeed;
		m_Agent.acceleration = m_Acceleration;
		m_Agent.angularSpeed = m_MaxYawRateDegrees;
		m_Agent.autoBraking = true;
		m_Agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
		m_YawDegrees = transform.eulerAngles.y;
	}

	private void Update()
	{
		if (!m_HasDestination)
		{
			m_CurrentSpeed = Mathf.MoveTowards(m_CurrentSpeed, 0f, m_Deceleration * Time.deltaTime);
			m_SteerAngle = Mathf.MoveTowards(m_SteerAngle, 0f, 90f * Time.deltaTime);
			ApplyRotation(Time.deltaTime);
			return;
		}

		if (!EnsurePathValid())
		{
			Stop();
			return;
		}

		AdvanceCorners();
		if (m_CornerIndex >= m_Corners.Length)
		{
			Arrive();
			return;
		}

		Vector3 target = m_Corners[m_CornerIndex];
		Vector3 toTarget = target - transform.position;
		toTarget.y = 0f;
		float distance = toTarget.magnitude;
		if (distance < 0.01f)
		{
			m_CornerIndex++;
			return;
		}

		Vector3 forward = Quaternion.Euler(0f, m_YawDegrees, 0f) * Vector3.forward;
		Vector3 desiredDir = toTarget / distance;
		float angle = Vector3.SignedAngle(forward, desiredDir, Vector3.up);

		EvaluateReverse(distance, angle);

		float steerTarget = Mathf.Clamp(angle, -m_MaxSteerAngle, m_MaxSteerAngle);
		if (m_IsReversing)
			steerTarget = -steerTarget;
		m_SteerAngle = Mathf.MoveTowards(m_SteerAngle, steerTarget, 120f * Time.deltaTime);

		float speedFactor = Mathf.Lerp(m_MinTurnSpeedFactor, 1f, 1f - Mathf.Abs(m_SteerAngle) / m_MaxSteerAngle);
		float targetSpeed = (m_IsReversing ? m_ReverseSpeed : m_CruiseSpeed) * speedFactor;
		if (distance < 6f)
			targetSpeed = Mathf.Min(targetSpeed, Mathf.Lerp(2f, targetSpeed, distance / 6f));
		targetSpeed = Mathf.Min(targetSpeed, m_MaxSpeed);

		float accel = targetSpeed > m_CurrentSpeed ? m_Acceleration : m_Deceleration;
		m_CurrentSpeed = Mathf.MoveTowards(m_CurrentSpeed, targetSpeed, accel * Time.deltaTime);

		// Bicycle: ω = v * tan(δ) / L  →  R = L / tan(δ) ≈ 6.6 м при δ=26°, L=3.2
		float steerRad = m_SteerAngle * Mathf.Deg2Rad;
		float signedV = m_IsReversing ? -m_CurrentSpeed : m_CurrentSpeed;
		float yawRateDeg = 0f;
		if (Mathf.Abs(steerRad) > 0.0001f && m_Wheelbase > 0.01f)
		{
			yawRateDeg = signedV * Mathf.Tan(steerRad) / m_Wheelbase * Mathf.Rad2Deg;
			yawRateDeg = Mathf.Clamp(yawRateDeg, -m_MaxYawRateDegrees, m_MaxYawRateDegrees);
		}

		m_YawDegrees += yawRateDeg * Time.deltaTime;
		ApplyRotation(Time.deltaTime);

		Vector3 moveDir = m_IsReversing
			? -(Quaternion.Euler(0f, m_YawDegrees, 0f) * Vector3.forward)
			: (Quaternion.Euler(0f, m_YawDegrees, 0f) * Vector3.forward);
		moveDir.y = 0f;
		Vector3 nextPos = transform.position + moveDir.normalized * (m_CurrentSpeed * Time.deltaTime);
		if (NavMesh.SamplePosition(nextPos, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
			nextPos = hit.position;
		transform.position = nextPos;
		m_Agent.nextPosition = transform.position;

		if ((m_Destination - transform.position).sqrMagnitude <= c_ArriveDistance * c_ArriveDistance)
			Arrive();
	}
	#endregion

	#region Public Methods
	public void SetDestination(Vector3 _worldPosition)
	{
		if (!NavMesh.SamplePosition(_worldPosition, out NavMeshHit hit, 4f, NavMesh.AllAreas))
			return;

		m_Destination = hit.position;
		m_HasDestination = true;
		RebuildPath();
	}

	public void Stop()
	{
		m_HasDestination = false;
		m_CornerIndex = 0;
		m_Corners = Array.Empty<Vector3>();
		m_IsReversing = false;
		if (m_Agent != null && m_Agent.enabled && m_Agent.isOnNavMesh)
			m_Agent.ResetPath();
	}
	#endregion

	#region Private Methods
	private void ApplyRotation(float _dt)
	{
		float leanTarget = 0f;
		if (m_MaxCornerLeanDegrees > 0.01f && m_MaxSteerAngle > 0.01f)
		{
			float steerNorm = Mathf.Clamp(m_SteerAngle / m_MaxSteerAngle, -1f, 1f);
			float speedNorm = Mathf.Clamp01(m_CurrentSpeed / Mathf.Max(1f, m_CruiseSpeed));
			leanTarget = -steerNorm * speedNorm * m_MaxCornerLeanDegrees;
		}

		m_LeanDegrees = Mathf.MoveTowards(m_LeanDegrees, leanTarget, 40f * _dt);
		transform.rotation = Quaternion.Euler(0f, m_YawDegrees, m_LeanDegrees);
	}

	private bool EnsurePathValid()
	{
		if (m_Corners != null && m_Corners.Length > 0 && m_CornerIndex < m_Corners.Length)
			return true;
		return RebuildPath();
	}

	private bool RebuildPath()
	{
		if (m_Agent == null || !m_Agent.isOnNavMesh)
		{
			if (NavMesh.SamplePosition(transform.position, out NavMeshHit warp, 3f, NavMesh.AllAreas))
			{
				transform.position = warp.position;
				m_Agent.Warp(warp.position);
			}
			else
				return false;
		}

		if (!NavMesh.CalculatePath(transform.position, m_Destination, NavMesh.AllAreas, m_Path) ||
		    m_Path.status == NavMeshPathStatus.PathInvalid ||
		    m_Path.corners == null ||
		    m_Path.corners.Length == 0)
		{
			return false;
		}

		m_Corners = m_Path.corners;
		m_CornerIndex = m_Corners.Length > 1 ? 1 : 0;
		return true;
	}

	private void AdvanceCorners()
	{
		while (m_CornerIndex < m_Corners.Length - 1)
		{
			Vector3 flat = m_Corners[m_CornerIndex] - transform.position;
			flat.y = 0f;
			if (flat.sqrMagnitude > c_CornerReachDistance * c_CornerReachDistance)
				break;
			m_CornerIndex++;
		}
	}

	private void EvaluateReverse(float _segmentLength, float _signedAngle)
	{
		bool wantsReverse = Mathf.Abs(_signedAngle) >= c_ReverseAngleDegrees &&
		                    _segmentLength <= c_ReverseMaxSegmentLength &&
		                    m_CornerIndex >= m_Corners.Length - 1;
		m_IsReversing = wantsReverse;
	}

	private void Arrive()
	{
		m_HasDestination = false;
		m_CurrentSpeed = 0f;
		m_SteerAngle = 0f;
		m_IsReversing = false;
		Arrived?.Invoke();
	}
	#endregion
}
