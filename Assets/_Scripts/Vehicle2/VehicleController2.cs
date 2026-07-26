using System.Text;
using UnityEngine;

/// <summary>
/// Root facade. Receives commands, delegates to subsystems.
/// Does NOT compute physics — only orchestrates.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class VehicleController2 : MonoBehaviour
{
	[SerializeField] private VehicleData m_Data;

	private Rigidbody m_Body;
	private VehicleEngine m_Engine;
	private VehicleSuspension m_Suspension;
	private VehicleMovement m_Movement;
	private VehicleVisual m_Visual;

	private DriveCommand m_Command;
	private int m_FrameCount;

	public VehicleData Data => m_Data;
	public float SpeedMs => m_Movement?.SpeedMs ?? 0f;

	public void SetCommand(float throttle, float steer, bool brake)
	{
		m_Command.Throttle = Mathf.Clamp(throttle, -1f, 1f);
		m_Command.Steer = Mathf.Clamp(steer, -1f, 1f);
		m_Command.Brake = brake;
	}

	private void Awake()
	{
		if (m_Data == null)
		{
			Debug.LogError($"[{name}] VehicleData is null — cannot init", this);
			return;
		}

		m_Body = GetComponent<Rigidbody>();
		m_Body.mass = m_Data.Mass;
		m_Body.centerOfMass = m_Data.CenterOfMass;
		m_Body.angularDamping = m_Data.AngularDamping;
		m_Body.maxAngularVelocity = m_Data.MaxAngularVelocity;
		m_Body.interpolation = RigidbodyInterpolation.Interpolate;
		m_Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

		m_Engine = new VehicleEngine(m_Data);
		m_Suspension = new VehicleSuspension(m_Data);
		m_Suspension.CreateWheels(transform);
		m_Movement = new VehicleMovement(m_Body);

		if (!TryGetComponent(out VehicleSafety _))
			gameObject.AddComponent<VehicleSafety>();

		m_Visual = new VehicleVisual(transform);

		// Snap chassis so wheels touch ground at rest.
		SnapToGround();
	}

	private void FixedUpdate()
	{
		if (m_Data == null) return;

		float dt = Time.fixedDeltaTime;

		m_Engine.Update(m_Command, dt, m_Movement.SpeedMs);
		m_Suspension.Update(m_Engine, m_Movement.SpeedMs, m_Body);
		m_Movement.Update(m_Suspension.States);

		// Diagnostic: log first 5 frames
		if (m_FrameCount < 5)
		{
			m_FrameCount++;
			var sb = new System.Text.StringBuilder(128);
			sb.Append($"F{m_FrameCount} y={transform.position.y:F3} v={m_Movement.SpeedMs*3.6f:F0}km/h");
			for (int i = 0; i < m_Suspension.States.Length; i++)
			{
				var s = m_Suspension.States[i];
				sb.Append($" [W{i} g={s.HasContact} F={s.SuspensionForce:F0}]");
			}
			Debug.Log(sb.ToString(), this);
		}
	}

	private void LateUpdate()
	{
		m_Visual?.Update(m_Suspension.States);
	}

	private void SnapToGround()
	{
		float groundY = 0f;
		float hubLocalY = m_Data.WheelLocalPositions[0].y;
		float neededY = groundY - hubLocalY + m_Data.SuspensionTravel + m_Data.WheelRadius + 0.05f;

		Vector3 pos = transform.position;
		pos.y = neededY;
		transform.position = pos;
		Physics.SyncTransforms();
	}
}
