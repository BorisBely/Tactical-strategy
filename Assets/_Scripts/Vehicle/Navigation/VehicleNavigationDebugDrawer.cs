using System.Collections.Generic;
using System.Text;
using CombatVehicleSystem;
using VehicleNavigation;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Отрисовывает в Gizmos всю отладочную информацию о навигации машины:
/// путь NavMesh, вейпоинты манёвра, точку преследования, кривизну, зонды геометрии.
/// А так же пишет подробные логи о принятии решений.
/// </summary>
[DisallowMultipleComponent]
public sealed class VehicleNavigationDebugDrawer : MonoBehaviour
{
	#region Toggles
	[Header("Визуализация")]
	[SerializeField] private bool m_DrawNavMeshPath = true;
	[SerializeField] private bool m_DrawManeuverWaypoints = true;
	[SerializeField] private bool m_DrawPursuitTarget = true;
	[SerializeField] private bool m_DrawCurvatureArc = true;
	[SerializeField] private bool m_DrawGeometryProbes = true;
	[SerializeField] private bool m_DrawVehicleInfo = true;
		[SerializeField] private bool m_DrawDestination = true;
		[SerializeField] private bool m_DrawLookAheadRing = true;
		[SerializeField] private bool m_DrawDiagonalProbes = true;
		[SerializeField] private bool m_DrawFeasibilityInfo = true;
		[SerializeField] private bool m_DrawQueuePreview = true;
		[SerializeField] private bool m_DrawArrivalDebug = true;

	[Header("Логирование")]
	[SerializeField] private bool m_LogPlanRebuild = true;
	[SerializeField] private bool m_LogPursuitEveryFrame = false;
	[SerializeField] private float m_LogPursuitPeriodSeconds = 2f;
	[SerializeField] private bool m_LogManeuverTransitions = true;
	[SerializeField] private bool m_LogArrival = true;
	[SerializeField] private bool m_LogGeometry = false;

	[Header("Размеры (Gizmos)")]
	[SerializeField] private float m_WaypointSphereRadius = 0.3f;
	[SerializeField] private float m_CornerSphereRadius = 0.25f;
	[SerializeField] private float m_TargetSphereRadius = 0.45f;
	[SerializeField] private float m_DestinationSphereRadius = 0.6f;
	[SerializeField] private float m_ProbeRayLength = 10f;
	#endregion

	#region Private
	private VehicleNavigation.VehicleNavigation m_Nav;
	private StringBuilder m_Sb = new StringBuilder(512);
	private float m_LastPursuitLogTime = -999f;
	private int m_LastManeuverIndex = -1;
	private string m_LastPlanReason = string.Empty;
	private VehicleNavigation.DriverFSM.State m_LastFsmState;
	private VehicleManeuverType m_LastManeuverType;
	private Vector3 m_LastDestination;
	private float m_FrameCounter;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Nav = GetComponent<VehicleNavigation.VehicleNavigation>();
	}

	private void Update()
	{
		if (m_Nav == null || !m_Nav.HasDestination)
			return;

		m_FrameCounter += Time.deltaTime;

		LogPlanChanges();
		LogManeuverChanges();
		LogPursuitPeriodic();
		LogArrivalChange();
		LogGeometryPeriodic();
	}
	#endregion

	#region Logging
	private void LogPlanChanges()
	{
		if (!m_LogPlanRebuild)
			return;

		string reason = m_Nav.ActivePlanReason;
		if (reason == m_LastPlanReason)
			return;
		m_LastPlanReason = reason;

		var ctx = m_Nav.Context;
		if (ctx == null || !ctx.HasPath)
			return;

		var plan = ctx.Plan;
		var path = ctx.Path;

		m_Sb.Clear();
		m_Sb.AppendLine($"══════ [NavDebug:{name}] ПЛАН ПЕРЕСТРОЕН ══════");
		m_Sb.AppendLine($"  Режим: {reason}");
		m_Sb.AppendLine($"  Точка назначения: {m_Nav.Destination}");
		m_Sb.AppendLine($"  Дистанция до цели (2D): {FlatDist(transform.position, m_Nav.Destination):F1} м");
		m_Sb.AppendLine($"  NavMesh углов: {path.Corners?.Length ?? 0}, длина пути: {path.Length:F1} м");

		if (plan.Maneuvers != null)
		{
			m_Sb.AppendLine($"  Манёвры ({plan.Maneuvers.Count}):");
			for (int i = 0; i < plan.Maneuvers.Count; i++)
			{
				var m = plan.Maneuvers[i];
				int wpCount = m.Waypoints?.Count ?? 0;
				m_Sb.AppendLine($"    [{i}] {m.Type}, вейпоинтов={wpCount}, масштаб скорости={m.SpeedScale:F2}, разворот={m.AllowReverse}");
			}
		}

		var geo = m_Nav.Geometry;
		m_Sb.AppendLine($"  Геометрия: фронт={geo.FrontClearance:F1}м зад={geo.RearClearance:F1}м лево={geo.LeftClearance:F1}м право={geo.RightClearance:F1}м");

		VehicleFileLog.Write(this, m_Sb.ToString());
	}

	private void LogManeuverChanges()
	{
		if (!m_LogManeuverTransitions)
			return;

		var ctx = m_Nav.Context;
		if (ctx == null)
			return;

		var maneuver = ctx.CurrentManeuver;
		if (maneuver == null)
			return;

		int idx = ctx.CurrentManeuverIndex;
		var mType = maneuver.Type;

		if (idx != m_LastManeuverIndex || mType != m_LastManeuverType)
		{
			m_LastManeuverIndex = idx;
			m_LastManeuverType = mType;

			m_Sb.Clear();
			m_Sb.AppendLine($"── [NavDebug:{name}] МАНЁВР #{idx} ──");
			m_Sb.AppendLine($"  Тип: {mType}");
			m_Sb.AppendLine($"  Вейпоинтов: {maneuver.Waypoints?.Count ?? 0}");
			m_Sb.AppendLine($"  Масштаб скорости: {maneuver.SpeedScale:F2}");
			m_Sb.AppendLine($"  Разрешён задний ход: {maneuver.AllowReverse}");
			m_Sb.AppendLine($"  Прибытие: {maneuver.IsArrivalManeuver}");

			if (maneuver.Waypoints != null && maneuver.Waypoints.Count > 0)
			{
				var first = maneuver.Waypoints[0];
				var last = maneuver.Waypoints[maneuver.Waypoints.Count - 1];
				m_Sb.AppendLine($"  От {first} до {last}");
				m_Sb.AppendLine($"  Дистанция манёвра (2D): {FlatDist(first, last):F1} м");
			}

			VehicleFileLog.Write(this, m_Sb.ToString());
		}
	}

	private void LogArrivalChange()
	{
		if (!m_LogArrival)
			return;

		var state = m_Nav.DriverState;
		if (state != m_LastFsmState)
		{
			m_LastFsmState = state;

			m_Sb.Clear();
			m_Sb.Append($"── [NavDebug:{name}] СОСТОЯНИЕ: {state} ── ");
			if (state == VehicleNavigation.DriverFSM.State.Arrival ||
			    state == VehicleNavigation.DriverFSM.State.FollowingTrajectory)
			{
				m_Sb.Append($"дист.до цели={FlatDist(transform.position, m_Nav.Destination):F2}м");
				var traj = m_Nav.ActiveTrajectory;
				if (traj != null && traj.IsValid)
					m_Sb.Append($" | localPose len={traj.TotalLength:F1}m segs={traj.GearSegmentCount}");
			}
			else if (state == VehicleNavigation.DriverFSM.State.Idle)
			{
				m_Sb.Append("МАШИНА ПРИБЫЛА!");
			}
			else if (state == VehicleNavigation.DriverFSM.State.Recovery)
			{
				m_Sb.Append("ЗАСТРЯЛА — пытаюсь выбраться!");
			}
			VehicleFileLog.Write(this, m_Sb.ToString());
		}
	}

	private void LogPursuitPeriodic()
	{
		if (!m_LogPursuitEveryFrame && Time.time - m_LastPursuitLogTime < m_LogPursuitPeriodSeconds)
			return;

		m_LastPursuitLogTime = Time.time;

		var debug = m_Nav.PursuitDebug;
		var ctx = m_Nav.Context;
		if (ctx == null)
			return;

		var maneuver = ctx.CurrentManeuver;
		if (maneuver == null)
			return;

		m_Sb.Clear();
		m_Sb.AppendLine($"── [NavDebug:{name}] PURSUIT (t={Time.time:F1}) ──");
		m_Sb.AppendLine($"  Манёвр: {maneuver.Type}, вейпоинтов всего: {debug.TotalWaypoints}");
		m_Sb.AppendLine($"  Ближайший вейпоинт: #{debug.NearestWaypointIndex}");
		m_Sb.AppendLine($"  Точка преследования: #{debug.LookAheadTargetIndex} → {debug.LookAheadTargetPoint}");
		m_Sb.AppendLine($"  LookAhead дистанция: {debug.LookAheadDistance:F2} м");
		m_Sb.AppendLine($"  Cross-track ошибка: {debug.CrossTrackError:F3} м");
		m_Sb.AppendLine($"  Кривизна: raw={debug.RawCurvature:F4} clamped={debug.ClampedCurvature:F4}");
		m_Sb.AppendLine($"  Предпросмотр кривизны (вперёд): {debug.PreviewCurvature:F4}");
		m_Sb.AppendLine($"  Кап скорости: {debug.CappedSpeedKmh:F1} км/ч");
		m_Sb.AppendLine($"  Множ.кривизны: {debug.CurvatureFraction:F2}, множ.прибытия: {debug.ArrivalScale:F2}, рампа: {debug.LaunchRamp:F2}");
		m_Sb.AppendLine($"  Желаемая скорость (до реверса): {debug.DesiredSpeedBeforeReverse:F1} км/ч");
		m_Sb.AppendLine($"  Итоговая желаемая скорость: {ctx.DesiredSpeedKmh:F1} км/ч");
		m_Sb.AppendLine($"  Текущая скорость: {ctx.State.SpeedKmh:F1} км/ч");
		m_Sb.AppendLine($"  Реверс: {debug.IsReversing}, оставшаяся дистанция: {ctx.RemainingDistance:F1} м");
		m_Sb.AppendLine($"  Газ: {m_Nav.ThrottleCommand:F2}, руль: {m_Nav.SteerCommand:F2}, задний ход: {m_Nav.IsReversing}");

		VehicleFileLog.Write(this, m_Sb.ToString());
	}

	private void LogGeometryPeriodic()
	{
		if (!m_LogGeometry)
			return;
		if (Time.frameCount % 60 != 0)
			return;

		var geo = m_Nav.Geometry;
		m_Sb.Clear();
		m_Sb.AppendLine($"── [NavDebug:{name}] ГЕОМЕТРИЯ ──");
		m_Sb.AppendLine($"  Перед: {geo.FrontClearance:F1}м | Зад: {geo.RearClearance:F1}м");
		m_Sb.AppendLine($"  Лево: {geo.LeftClearance:F1}м | Право: {geo.RightClearance:F1}м");
		m_Sb.AppendLine($"  Предпочитаемый поворот: {(geo.PreferredTurnSign < 0 ? "ЛЕВО" : geo.PreferredTurnSign > 0 ? "ПРАВО" : "НЕТ")}");
		VehicleFileLog.Write(this, m_Sb.ToString());
	}
	#endregion

	#region Gizmos
#if UNITY_EDITOR
	private void OnDrawGizmos()
	{
		if (m_Nav == null)
			m_Nav = GetComponent<VehicleNavigation.VehicleNavigation>();
		if (m_Nav == null || !m_Nav.HasDestination)
			return;

		DrawNavMeshPath();
		DrawManeuverWaypoints();
		DrawLocalTrajectory();
		DrawPursuitTarget();
		DrawCurvatureArc();
		DrawGeometryProbes();
		DrawVehicleInfo();
			DrawDestination();
			DrawLookAheadRing();
			DrawDiagonalProbes();
			DrawFeasibilityInfo();
			DrawQueuePreview();
			DrawArrivalDebug();
	}

	private void DrawLocalTrajectory()
	{
		var traj = m_Nav.ActiveTrajectory;
		if (traj == null || !traj.IsValid || traj.PointCount < 2)
			return;

		Vector3 prev = traj.Points[0].Position;
		for (int i = 1; i < traj.PointCount; i++)
		{
			var p = traj.Points[i];
			Gizmos.color = p.Gear == TrajectoryGear.Reverse
				? new Color(1f, 0.4f, 0.1f, 0.9f)
				: new Color(0.2f, 0.95f, 0.35f, 0.9f);
			Gizmos.DrawLine(prev, p.Position);
			if (p.IsCusp)
				Gizmos.DrawWireSphere(p.Position, m_WaypointSphereRadius * 1.4f);
			prev = p.Position;
		}
	}

	private void DrawNavMeshPath()
	{
		if (!m_DrawNavMeshPath)
			return;

		var corners = m_Nav.PathCorners;
		if (corners == null || corners.Count < 2)
			return;

		Gizmos.color = new Color(0.3f, 0.5f, 0.9f, 0.7f);

		Vector3 prev = transform.position;
		for (int i = 0; i < corners.Count; i++)
		{
			Gizmos.DrawLine(prev, corners[i]);
			Gizmos.DrawWireSphere(corners[i], m_CornerSphereRadius);
			Handles.Label(corners[i] + Vector3.up * 0.6f, $"#{i}", EditorStyles.miniLabel);
			prev = corners[i];
		}
	}

	private void DrawManeuverWaypoints()
	{
		if (!m_DrawManeuverWaypoints)
			return;

		var maneuver = m_Nav.CurrentManeuver;
		if (maneuver == null || maneuver.Waypoints == null || maneuver.Waypoints.Count == 0)
			return;

		var wps = maneuver.Waypoints;
		Color color = ManeuverColor(maneuver.Type);
		Gizmos.color = color;

		Vector3 prev = transform.position;
		for (int i = 0; i < wps.Count; i++)
		{
			Vector3 p = wps[i];
			if (i == 0 && maneuver.Type != VehicleManeuverType.Parking)
				p = transform.position;

			Gizmos.DrawLine(prev, p);
			float r = (i == wps.Count - 1) ? m_WaypointSphereRadius * 1.3f : m_WaypointSphereRadius;
			Gizmos.DrawSphere(p, r);

			Handles.Label(p + Vector3.up * 0.7f,
				$"{maneuver.Type}\n{i}/{wps.Count - 1}",
				EditorStyles.miniLabel);

			prev = p;
		}
	}

	private void DrawPursuitTarget()
	{
		if (!m_DrawPursuitTarget)
			return;

		Vector3 pos = transform.position;
		Vector3 target;
		float lookDist;
		float crossTrack;
		float curv;
		string label;

		if (m_Nav.DriverState == DriverFSM.State.FollowingTrajectory)
		{
			var trk = m_Nav.LastTrackerOutput;
			target = trk.LookAheadPoint;
			if (target == Vector3.zero)
				return;
			lookDist = Vector3.Distance(pos, target);
			crossTrack = trk.CrossTrack;
			curv = trk.WheelCurvature;
			label = $"tracker LA idx={trk.NearestIndex}\nкрив={curv:F3}";
		}
		else
		{
			var debug = m_Nav.PursuitDebug;
			if (debug.TotalWaypoints == 0)
				return;
			target = debug.LookAheadTargetPoint;
			lookDist = debug.LookAheadDistance;
			crossTrack = debug.CrossTrackError;
			curv = debug.ClampedCurvature;
			label = $"цель-пресл #{debug.LookAheadTargetIndex}\nкрив={curv:F3}";
		}

		Gizmos.color = Color.yellow;
		Gizmos.DrawSphere(target, m_TargetSphereRadius);

		Vector3 mid = (pos + target) * 0.5f;
		Vector3 dir = (target - pos).normalized;
		DrawArrow(mid, dir, 0.4f, 0.25f, Color.yellow);

		Handles.color = new Color(1f, 1f, 0f, 0.6f);
		Handles.DrawDottedLine(pos, target, 4f);

		Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
		Handles.DrawWireDisc(pos, Vector3.up, Mathf.Max(0.2f, lookDist));

		if (Mathf.Abs(crossTrack) > 0.01f)
		{
			Vector3 fwd = transform.forward;
			fwd.y = 0f;
			fwd.Normalize();
			Vector3 proj = pos + fwd * Vector3.Dot(target - pos, fwd);
			Gizmos.color = new Color(1f, 0f, 1f, 0.8f);
			Gizmos.DrawLine(proj, target);
			Handles.Label((proj + target) * 0.5f + Vector3.up * 0.3f,
				$"cross:{crossTrack:F2}м", EditorStyles.miniLabel);
		}

		Handles.Label(target + Vector3.up * 1.0f, label, EditorStyles.miniLabel);
	}

	private void DrawCurvatureArc()
	{
		if (!m_DrawCurvatureArc)
			return;

		var debug = m_Nav.PursuitDebug;
		if (debug.TotalWaypoints == 0)
			return;

		float curv = debug.ClampedCurvature;
		if (Mathf.Abs(curv) < 0.001f)
			return;

		float radius = 1f / Mathf.Abs(curv);
		Vector3 pos = transform.position;
		Vector3 right = transform.right;
		right.y = 0f;
		right.Normalize();

		// Center of turning circle (to the left if curvature positive, right if negative)
		Vector3 center = pos + right * Mathf.Sign(curv) * radius;
		center.y = pos.y;

		Color arcColor = Mathf.Abs(curv) > 0.15f ? new Color(1f, 0.2f, 0.2f, 0.5f) : new Color(0.2f, 0.8f, 0.2f, 0.5f);
		Handles.color = arcColor;

		// Draw arc (45 degrees forward)
		Vector3 toCenter = (center - pos).normalized;
		float startAngle = Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg;
		float arcAngle = 50f * Mathf.Sign(curv);
		Handles.DrawWireArc(center, Vector3.up, pos - center, arcAngle, radius);

		// Draw center point
		Gizmos.color = arcColor;
		Gizmos.DrawSphere(center, 0.15f);

		Handles.Label(center + Vector3.up * 0.4f,
			$"R={radius:F1}м", EditorStyles.miniLabel);
	}

	private void DrawGeometryProbes()
	{
		if (!m_DrawGeometryProbes)
			return;

		var geo = m_Nav.Geometry;
		Vector3 pos = transform.position + Vector3.up * 0.6f;

		DrawProbeRay(pos, transform.forward, geo.FrontClearance, "ПЕРЕД");
		DrawProbeRay(pos, -transform.forward, geo.RearClearance, "ЗАД");
		DrawProbeRay(pos, -transform.right, geo.LeftClearance, "ЛЕВО");
		DrawProbeRay(pos, transform.right, geo.RightClearance, "ПРАВО");
	}

	private void DrawProbeRay(Vector3 _origin, Vector3 _dir, float _clearance, string _label)
	{
		_dir.y = 0f;
		_dir.Normalize();

		float displayLen = Mathf.Min(_clearance, m_ProbeRayLength);
		float alpha = _clearance > 4f ? 0.7f : _clearance > 2f ? 0.5f : 0.9f;
		Color color = _clearance > 4f ? new Color(0.2f, 1f, 0.2f, alpha)
			: _clearance > 2f ? new Color(1f, 0.9f, 0.2f, alpha)
			: new Color(1f, 0.2f, 0.2f, alpha);

		Gizmos.color = color;
		Gizmos.DrawRay(_origin, _dir * displayLen);

		Vector3 end = _origin + _dir * displayLen;
		Handles.Label(end + Vector3.up * 0.2f, $"{_label}:{_clearance:F1}м", EditorStyles.miniLabel);
	}

	private void DrawVehicleInfo()
	{
		if (!m_DrawVehicleInfo)
			return;

		Vector3 pos = transform.position;
		Vector3 fwd = transform.forward;
		fwd.y = 0f;
		fwd.Normalize();

		// Forward arrow (red)
		Gizmos.color = Color.red;
		DrawArrow(pos + Vector3.up * 0.15f, fwd, 2.5f, 0.2f, Color.red);

		// Vehicle position sphere
		Gizmos.color = Color.white;
		Gizmos.DrawSphere(pos + Vector3.up * 0.3f, 0.3f);

		// Info label above vehicle
		var ctx = m_Nav.Context;
		string stateStr = m_Nav.DriverState.ToString();
		string speedStr = ctx != null ? ctx.State.SpeedKmh.ToString("F1") : "?";
		string throttleStr = m_Nav.ThrottleCommand.ToString("F2");
		string steerStr = m_Nav.SteerCommand.ToString("F2");
		string revStr = m_Nav.IsReversing ? "←ЗАД" : "ВПЕРЁД→";
		string stuckStr = m_Nav.IsStuck ? " [ЗАСТРЯЛ!]" : "";

		Handles.Label(pos + Vector3.up * 2.5f,
			$"[{name}]\nСостояние: {stateStr}{stuckStr}\nСкорость: {speedStr} км/ч\nГаз: {throttleStr} | Руль: {steerStr}\n{revStr}",
			EditorStyles.boldLabel);
	}

	private void DrawDestination()
	{
		if (!m_DrawDestination)
			return;

		Vector3 dest = m_Nav.Destination;
		if (dest == Vector3.zero)
			return;

		Gizmos.color = new Color(1f, 0.55f, 0f, 0.9f);
		Gizmos.DrawSphere(dest, m_DestinationSphereRadius);

		// Draw a cross/star at destination
		float s = m_DestinationSphereRadius * 1.4f;
		Gizmos.DrawLine(dest + Vector3.left * s, dest + Vector3.right * s);
		Gizmos.DrawLine(dest + Vector3.forward * s, dest + Vector3.back * s);
		Gizmos.DrawLine(dest + Vector3.up * s, dest + Vector3.down * s);

		float dist = FlatDist(transform.position, dest);
		Handles.Label(dest + Vector3.up * 1.2f,
			$"ЦЕЛЬ\n{dist:F1}м\nрежим:{m_Nav.ActiveSpeedMode}",
			EditorStyles.boldLabel);

		// Heading arrow at destination if set
		if (m_Nav.HasGoalHeading)
		{
			float hdg = m_Nav.GoalHeadingYaw;
			Vector3 hDir = Quaternion.Euler(0f, hdg, 0f) * Vector3.forward;
			Vector3 arrowStart = dest + hDir * 0.3f;
			Vector3 arrowEnd = dest + hDir * 2.0f;

			Handles.color = new Color(1f, 0.55f, 0f, 0.9f);
			Handles.DrawLine(arrowStart, arrowEnd);
			Handles.DrawLine(arrowEnd, arrowEnd - hDir * 0.35f + transform.right * 0.2f);
			Handles.DrawLine(arrowEnd, arrowEnd - hDir * 0.35f - transform.right * 0.2f);

			Handles.Label(arrowEnd + Vector3.up * 0.3f,
				$"направление: {hdg:F0}°", EditorStyles.miniLabel);
		}
	}

	private void DrawLookAheadRing()
	{
		if (!m_DrawLookAheadRing)
			return;

		var debug = m_Nav.PursuitDebug;
		if (debug.TotalWaypoints == 0)
			return;

		Vector3 pos = transform.position;
		// Distance ring colored by curvature
		float maxCurv = Mathf.Max(Mathf.Abs(debug.ClampedCurvature), debug.PreviewCurvature);
		Color ringColor = maxCurv > 0.2f ? new Color(1f, 0.5f, 0f, 0.25f)
			: maxCurv > 0.1f ? new Color(1f, 1f, 0f, 0.25f)
			: new Color(0f, 1f, 0.5f, 0.25f);

		Handles.color = ringColor;
		Handles.DrawWireDisc(pos, Vector3.up, debug.LookAheadDistance * 0.5f);
		Handles.DrawWireDisc(pos, Vector3.up, debug.LookAheadDistance);
	}

	private void DrawDiagonalProbes()
	{
		if (!m_DrawDiagonalProbes)
			return;

		var geo = m_Nav.Geometry;
		Vector3 pos = transform.position + Vector3.up * 0.6f;

		Vector3 diagFL = Quaternion.Euler(0f, -30f, 0f) * transform.forward;
		Vector3 diagFR = Quaternion.Euler(0f,  30f, 0f) * transform.forward;
		Vector3 diagRL = Quaternion.Euler(0f, -150f, 0f) * transform.forward;
		Vector3 diagRR = Quaternion.Euler(0f,  150f, 0f) * transform.forward;

		DrawProbeRay(pos, diagFL, geo.FrontDiagonalLeftClearance,  "L-F");
		DrawProbeRay(pos, diagFR, geo.FrontDiagonalRightClearance, "R-F");
		DrawProbeRay(pos, diagRL, geo.RearDiagonalLeftClearance,   "L-R");
		DrawProbeRay(pos, diagRR, geo.RearDiagonalRightClearance,  "R-R");
	}

	private void DrawFeasibilityInfo()
	{
		if (!m_DrawFeasibilityInfo)
			return;

		var feas = m_Nav.LastFeasibility;
		if (feas == null)
			return;

		Vector3 pos = transform.position;

		string label;
		Color col;
		if (!feas.IsValid)
		{
			label = $"F:INVALID ({feas.FailureReason})";
			col = Color.red;
		}
		else if (!feas.IsFullySafe)
		{
			label = $"F:RISK {feas.RiskScore:F1} clr={feas.MinClearance:F1}m";
			col = Color.yellow;
		}
		else
		{
			label = "F:SAFE";
			col = Color.green;
		}

		Handles.Label(pos + Vector3.up * 3.2f, label,
			new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = col } });
	}

	private void DrawQueuePreview()
	{
		if (!m_DrawQueuePreview)
			return;

		var queue = m_Nav.OrderQueue;
		if (queue == null)
			return;

		var orders = queue.QueuedOrders;
		if (orders == null || orders.Count == 0)
			return;

		Vector3 pos = transform.position;
		Vector3 labelPos = pos + Vector3.up * 3.7f;

		var sb = new System.Text.StringBuilder();
		sb.AppendLine($"Очередь ({orders.Count}):");
		for (int i = 0; i < orders.Count; i++)
		{
			var o = orders[i];
			sb.AppendLine($"  [{i}] {o.Type} → {o.Destination:F0} st={o.State}");
		}
		Handles.Label(labelPos, sb.ToString(), EditorStyles.miniLabel);
	}

	private void DrawArrivalDebug()
	{
		if (!m_DrawArrivalDebug) return;
		Vector3 dest = m_Nav.Destination;
		if (dest == Vector3.zero) return;
		Vector3 pos = transform.position;
		float r = m_Nav.Context?.Params.MinTurningRadius ?? 6f;
		float planningDist = Mathf.Max(4f * r, 6f);
		Vector3 flatD = new Vector3(dest.x, pos.y, dest.z);
		Handles.color = new Color(0f, 1f, 0f, 0.25f);
		Handles.DrawWireDisc(flatD, Vector3.up, 0.5f);
		Handles.color = new Color(1f, 1f, 0f, 0.15f);
		Handles.DrawWireDisc(flatD, Vector3.up, planningDist);
		float d = Vector3.Distance(new Vector3(pos.x, 0, pos.z), new Vector3(dest.x, 0, dest.z));
		if (d < r) { Handles.color = new Color(1f, 0.2f, 0.2f, 0.3f); Handles.DrawWireDisc(flatD, Vector3.up, r); }
	}
#endif
	#endregion

	#region Helpers
#if UNITY_EDITOR
	private static void DrawArrow(Vector3 _pos, Vector3 _dir, float _length, float _headSize, Color _color)
	{
		Handles.color = _color;
		Vector3 end = _pos + _dir * _length;
		Handles.DrawLine(_pos, end);
		Vector3 right = Vector3.Cross(Vector3.up, _dir).normalized;
		Handles.DrawLine(end, end - _dir * _headSize + right * _headSize * 0.4f);
		Handles.DrawLine(end, end - _dir * _headSize - right * _headSize * 0.4f);
	}

	private static Color ManeuverColor(VehicleManeuverType _type)
	{
		switch (_type)
		{
			case VehicleManeuverType.Forward: return new Color(0.2f, 0.9f, 0.2f, 0.8f);
			case VehicleManeuverType.Reverse: return new Color(0.9f, 0.2f, 0.9f, 0.8f);
			case VehicleManeuverType.TurnAround: return new Color(0.9f, 0.6f, 0.1f, 0.8f);
			case VehicleManeuverType.ThreePointTurn: return new Color(0.9f, 0.7f, 0.2f, 0.8f);
			case VehicleManeuverType.Parking: return new Color(0.2f, 0.8f, 0.9f, 0.8f);
			case VehicleManeuverType.Unstuck: return new Color(1f, 0.3f, 0.3f, 0.8f);
			case VehicleManeuverType.Stop: return new Color(0.6f, 0.6f, 0.6f, 0.8f);
			default: return new Color(0.7f, 0.7f, 0.7f, 0.8f);
		}
	}
#endif

	private static float FlatDist(Vector3 _a, Vector3 _b)
	{
		_a.y = 0f;
		_b.y = 0f;
		return Vector3.Distance(_a, _b);
	}
	#endregion
}
