using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VehicleNavigation
{
	/// <summary>
	/// Gizmos visualization for reverse driving: path, rear axle, look-behind point, prediction arc.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class ReverseDebugger : MonoBehaviour
	{
		[SerializeField] private bool m_DrawReversePath = true;
		[SerializeField] private bool m_DrawRearAxle = true;
		[SerializeField] private bool m_DrawLookBehind = true;
		[SerializeField] private bool m_DrawPredictionArc = true;

		private VehicleNavigation m_Nav;
		private ReversePath m_CachedPath;

		private void Awake()
		{
			m_Nav = GetComponent<VehicleNavigation>();
		}

#if UNITY_EDITOR
		private void OnDrawGizmos()
		{
			if (m_Nav == null || !m_Nav.HasDestination)
				return;

			DrawReversePathGizmos();
			DrawRearAxleGizmos();
			DrawLookBehindGizmos();
			DrawPredictionArcGizmos();
		}

		private void DrawReversePathGizmos()
		{
			if (!m_DrawReversePath)
				return;

			var maneuver = m_Nav.CurrentManeuver;
			if (maneuver == null || !(maneuver is ReverseIntentManeuver revMvr))
				return;

			var path = revMvr.Path;
			if (path == null || !path.IsValid)
				return;

			m_CachedPath = path;

			Gizmos.color = new Color(0.9f, 0.5f, 0.9f, 0.7f);
			for (int i = 0; i < path.Points.Count - 1; i++)
			{
				var a = path.Points[i].Position;
				var b = path.Points[i + 1].Position;
				Gizmos.DrawLine(a, b);

				float r = (i == path.CurrentSegment) ? 0.35f : 0.15f;
				Color c = (i == path.CurrentSegment) ? new Color(1f, 0.2f, 1f, 0.9f) : new Color(0.7f, 0.4f, 0.9f, 0.5f);
				Gizmos.color = c;
				Gizmos.DrawSphere(a, r);

				Handles.Label(a + Vector3.up * 0.5f, $"{i}", EditorStyles.miniLabel);
			}

			var last = path.Points[path.Points.Count - 1].Position;
			Gizmos.color = Color.magenta;
			Gizmos.DrawSphere(last, 0.4f);
			Handles.Label(last + Vector3.up * 0.6f, "REV-TARGET", EditorStyles.miniLabel);
		}

		private void DrawRearAxleGizmos()
		{
			if (!m_DrawRearAxle)
				return;

			Vector3 rearAxle = transform.position - transform.forward * (m_Nav.Settings?.TurnRadius ?? 7f) * 0.25f;
			Gizmos.color = Color.red;
			Gizmos.DrawSphere(rearAxle, 0.25f);
			Handles.Label(rearAxle + Vector3.up * 0.4f, "REAR AXLE", EditorStyles.miniLabel);

			Vector3 frontAxle = transform.position + transform.forward * (m_Nav.Settings?.TurnRadius ?? 7f) * 0.25f;
			Gizmos.color = Color.cyan;
			Gizmos.DrawSphere(frontAxle, 0.2f);
		}

		private void DrawLookBehindGizmos()
		{
			if (!m_DrawLookBehind || m_CachedPath == null || !m_CachedPath.IsValid)
				return;

			var debug = m_Nav.PursuitDebug;
			if (debug.TotalWaypoints == 0)
				return;

			Vector3 rearAxle = transform.position - transform.forward * (m_Nav.Settings?.TurnRadius ?? 7f) * 0.25f;
			float lookBehind = ReverseLookBehind(debug.LookAheadDistance);

			var pursuitTarget = m_CachedPath.GetLookBehind(rearAxle, lookBehind);
			Gizmos.color = Color.yellow;
			Gizmos.DrawSphere(pursuitTarget, 0.3f);
			Handles.DrawDottedLine(rearAxle, pursuitTarget, 4f);
			Handles.Label(pursuitTarget + Vector3.up * 0.5f, "LOOK-BEHIND", EditorStyles.miniLabel);
		}

		private void DrawPredictionArcGizmos()
		{
			if (!m_DrawPredictionArc)
				return;

			float steerAngle = m_Nav.SteerCommand * (m_Nav.Settings?.TurnRadius != null ? 32f : 30f);
			float speed = m_Nav.CurrentSpeed * 3.6f;
			if (speed < 0.5f)
				return;

			float wheelBase = 3.5f;
			float yaw = transform.eulerAngles.y * Mathf.Deg2Rad;
			Vector3 pos = transform.position - transform.forward * wheelBase * 0.5f;

			for (int step = 0; step <= 8; step++)
			{
				float t = step / 8f;
				float speedMs = speed / 3.6f;
				float dt = 0.2f;

				Vector3 prev = pos;
				float steerRad = Mathf.Clamp(steerAngle * Mathf.Deg2Rad, -0.7f, 0.7f);
				float omega = (speedMs / wheelBase) * Mathf.Tan(steerRad);
				yaw += omega * dt;
				pos += new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * speedMs * dt;

				if (step > 0)
				{
					bool collision = Physics.CheckSphere(pos, 0.8f, ~0, QueryTriggerInteraction.Ignore);
					Gizmos.color = collision ? Color.red : Color.green;
					Gizmos.DrawLine(prev, pos);
					if (step == 8)
					{
						Gizmos.DrawSphere(pos, 0.3f);
						if (collision)
							Handles.Label(pos + Vector3.up * 0.5f, "COLLISION!", EditorStyles.boldLabel);
					}
				}
			}
		}

		private static float ReverseLookBehind(float _speedLookAhead)
		{
			return Mathf.Clamp(_speedLookAhead * 0.5f, 2f, 8f);
		}
#endif
	}
}
