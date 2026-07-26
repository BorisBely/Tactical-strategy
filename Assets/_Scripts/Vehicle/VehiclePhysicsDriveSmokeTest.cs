using CombatVehicleSystem;
using UnityEngine;
using UnityEngine.AI;

public sealed class VehiclePhysicsDriveSmokeTest : MonoBehaviour
{
	[SerializeField] private float m_IdleWaitSeconds = 2f;
	[SerializeField] private bool m_RunOnStart = true;
	[SerializeField] private bool m_DestroyAfter = true;

	private VehicleController m_Vehicle;

	private void Awake()
	{
		m_Vehicle = GetComponent<VehicleController>();
	}

	private void Start()
	{
		if (!m_RunOnStart)
			return;

		StartCoroutine(RunTests());
	}

	[ContextMenu("Run Smoke Test")]
	public void RunFromMenu()
	{
		StartCoroutine(RunTests());
	}

	private System.Collections.IEnumerator RunTests()
	{
		if (m_Vehicle == null)
		{
			LogFail("VehicleController not found on this GameObject.");
			yield break;
		}

		// ── Test 1: EnsurePhysicsDrive disables NavMeshAgent ──
		LogHeader("Test 1: agent.enabled == false");
		m_Vehicle.EnsurePhysicsDrive();
		NavMeshAgent agent = GetComponent<NavMeshAgent>();
		if (agent == null)
		{
			LogFail("NavMeshAgent not found on vehicle.");
			yield break;
		}

		bool agentDisabled = !agent.enabled;
		if (agentDisabled)
			LogPass("agent.enabled = false  (fix applied)");
		else
			LogFail($"agent.enabled = true  (fix MISSING — micro-impulses will leak onto RB)");

		// ── Test 2: Rigidbody idle stability ──
		LogHeader("Test 2: idle stability (no micro-drift)");
		Rigidbody body = GetComponent<Rigidbody>();
		if (body == null)
		{
			LogFail("Rigidbody not found.");
			yield break;
		}

		body.linearVelocity = Vector3.zero;
		body.angularVelocity = Vector3.zero;

		if (TryGetComponent(out VehicleBrain brain))
		{
			brain.SetControlActive(false);
			brain.SetCommand(VehicleCommand.Idle);
		}

		float elapsed = 0f;
		float maxSpeed = 0f;
		float maxAng = 0f;
		while (elapsed < m_IdleWaitSeconds)
		{
			yield return new WaitForFixedUpdate();
			elapsed += Time.fixedDeltaTime;
			float s = body.linearVelocity.magnitude;
			float a = body.angularVelocity.magnitude;
			if (s > maxSpeed) maxSpeed = s;
			if (a > maxAng) maxAng = a;
		}

		Debug.Log($"  max linearVelocity = {maxSpeed:F5} m/s  |  max angularVelocity = {maxAng:F5} rad/s");

		const float speedThreshold = 0.01f;
		const float angThreshold = 0.1f;

		if (agentDisabled && maxSpeed < speedThreshold && maxAng < angThreshold)
			LogPass($"Idle stable: speed={maxSpeed:F5} < {speedThreshold}, ang={maxAng:F5} < {angThreshold}");
		else if (!agentDisabled)
			LogFail($"Agent still enabled — drift cannot be verified.");
		else
			LogFail($"DRIFT DETECTED: speed={maxSpeed:F5} (max {speedThreshold}), ang={maxAng:F5} (max {angThreshold})");

		// ── Summary ──
		LogHeader(agentDisabled && maxSpeed < speedThreshold && maxAng < angThreshold
			? "SMOKE TEST PASSED"
			: "SMOKE TEST FAILED");

		if (m_DestroyAfter)
			Destroy(gameObject, 0.1f);
	}

	private void LogHeader(string msg)
	{
		Debug.Log($"[VehicleSmoke] ======== {msg} ========");
	}

	private void LogPass(string msg)
	{
		Debug.Log($"[VehicleSmoke]   PASS  {msg}");
	}

	private void LogFail(string msg)
	{
		Debug.LogError($"[VehicleSmoke]   FAIL  {msg}");
	}
}
