using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Unified forward/reverse tracker for a VehicleTrajectory.
	/// Stops fully at gear cusps and approaches the goal with guaranteed braking.
	/// </summary>
	public sealed class TrajectoryTracker
	{
		public readonly struct Output
		{
			public readonly MotionCommand Command;
			public readonly float DistanceToEnd;
			public readonly float CrossTrack;
			public readonly float MotionCurvature;
			public readonly float WheelCurvature;
			public readonly int NearestIndex;
			public readonly int SegmentEndIndex;
			public readonly Vector3 LookAheadPoint;
			public readonly bool IsComplete;
			public readonly bool WaitingForStop;
			public readonly bool NeedHeadingReplan;
			public readonly bool NeedPathReplan;
			public readonly bool RequestSteeringReset;
			public readonly TrajectoryGear ActiveGear;
			/// <summary>Brake toward strict tolerance; not the same as IsComplete/Succeeded.</summary>
			public readonly bool RequestTerminalBrake;

			public Output(
				MotionCommand _command,
				float _distanceToEnd,
				float _crossTrack,
				float _motionCurvature,
				float _wheelCurvature,
				int _nearestIndex,
				int _segmentEndIndex,
				Vector3 _lookAheadPoint,
				bool _isComplete,
				bool _waitingForStop,
				bool _needHeadingReplan,
				bool _needPathReplan,
				bool _requestSteeringReset,
				TrajectoryGear _gear,
				bool _requestTerminalBrake = false)
			{
				Command = _command;
				DistanceToEnd = _distanceToEnd;
				CrossTrack = _crossTrack;
				MotionCurvature = _motionCurvature;
				WheelCurvature = _wheelCurvature;
				NearestIndex = _nearestIndex;
				SegmentEndIndex = _segmentEndIndex;
				LookAheadPoint = _lookAheadPoint;
				IsComplete = _isComplete;
				WaitingForStop = _waitingForStop;
				NeedHeadingReplan = _needHeadingReplan;
				NeedPathReplan = _needPathReplan;
				RequestSteeringReset = _requestSteeringReset;
				ActiveGear = _gear;
				RequestTerminalBrake = _requestTerminalBrake;
			}
		}

		private const float c_StopSpeedKmh = 1.5f;
		private const float c_ApproachZone = 1.5f;
		private const float c_CuspStopMargin = 0.12f;
		private const float c_CuspApproachBuffer = 0.55f;
		private const float c_CuspSwitchMaxSpeedKmh = 0.9f;
		private const float c_CuspCrawlMaxSpeedKmh = 2f;
		private const float c_MicroCreepMax = 0.7f;
		private const float c_CreepMinKmh = 0.4f;
		private const float c_CreepMaxKmh = 2f;
		private const float c_CreepGearHysteresisDeg = 25f;
		private const float c_OvershootReplanDist = 0.75f;
		private const float c_DivergenceCrossTrack = 1.2f;
		private const float c_DivergenceRecoverCrossTrack = 0.75f;
		private const float c_ImmediateReplanCrossTrack = 2.5f;
		private const int c_CrossTrackReplanTicks = 8;
		private const float c_StuckDistGoal = 1.2f;
		private const float c_SettleBandMax = 0.5f;
		private const float c_SettleExitMultiplier = 1.4f;
		private const float c_BrakeMarginM = 0.9f;
		private const float c_IndexLeadMaxM = 0.75f;
		private const float c_StraightRevCrossTrackDeadband = 0.05f;
		private const float c_LocalReverseMaxSpeedKmh = 12f;
		/// <summary>Multi-gear / staging / high-κ local paths must stay calm.</summary>
		private const float c_ComplexManeuverMaxKmh = 7.5f;
		private const float c_ComplexReverseMaxKmh = 5.5f;
		private const float c_ComplexCuspMaxKmh = 5f;
		private const float c_ManeuverComfortDecelMs2 = 0.95f;
		private const float c_DesiredSpeedSlewDownKmhPerSec = 5.5f;
		private const float c_DesiredSpeedSlewUpKmhPerSec = 4f;
		private const float c_WheelCurvatureDeadband = 0.003f;
		private const float c_HighCurvThreshold = 0.08f;
		private const float c_EntryYawHoldDeg = 10f;
		private const float c_EntryYawReleaseDeg = 8f;
		private const float c_EntryGateTimeoutSec = 1.2f;
		private const float c_EntryAlignHoldSec = 0.3f;
		private const float c_EntryWorsenWindowSec = 0.6f;
		private const float c_EntryGateMaxSpeedKmh = 4f;
		/// <summary>Tube trim engages above half-threshold; full trim at threshold.</summary>
		private const float c_CorrectCrossTrackM = 0.2f;
		private const float c_CorrectHeadingDeg = 10f;

		private VehicleTrajectory m_Trajectory;
		private int m_Index;
		private int m_SegmentEndIndex;
		private bool m_WaitingStop;
		private bool m_StraightReversePath;
		private GoalPose m_Goal;
		private float m_LookAhead;
		private float m_EntrySpeedKmh;
		private int m_ActivateTick;
		private int m_TickCounter;
		private TrajectoryGear m_CreepGear;
		private bool m_CreepGearLatched;
		private enum CuspPhase
		{
			None,
			Approach,
			Stopped,
			Switch,
			Depart
		}

		private CuspPhase m_CuspPhase = CuspPhase.None;
		private int m_CrossTrackExceededTicks;
		private float m_BestDistGoal = float.MaxValue;
		private int m_BestIndex;
		private float m_BestRemainingArc = float.MaxValue;
		private float m_StagingStuckTimer;
		private const float c_StagingStuckTimeout = 2.5f;
		private float m_LastCommandedCurvature;
		private bool m_TurnEntryGateActive;
		private float m_TurnEntryGateTimer;
		private float m_TurnEntryAlignOkTimer;
		private int m_TurnEntryHoldIndex;
		private float m_BestAbsXtrack = float.MaxValue;
		private float m_BestHeadingToPath = float.MaxValue;
		private float m_EntryWorsenTimer;
		private bool m_AllowTurnEntryProgress;
		private bool m_InSettleBand;
		private float m_SmoothedDesiredSpeed = -1f;
		private bool m_IsComplexPath;
		private float m_PathMaxAbsCurv;

		public bool HasTrajectory => m_Trajectory != null && m_Trajectory.IsValid;
		public VehicleTrajectory Trajectory => m_Trajectory;
		public int CurrentIndex => m_Index;
		public Output LastOutput { get; private set; }
		public LocalPosePlanner.PlanStats PlanStats { get; private set; }
		public bool TurnEntryGateActive => m_TurnEntryGateActive;
		public float PathYawAtIndex
		{
			get
			{
				if (!HasTrajectory || m_Trajectory.PointCount == 0)
					return 0f;
				int i = Mathf.Clamp(m_Index, 0, m_Trajectory.PointCount - 1);
				return m_Trajectory.Points[i].YawDegrees;
			}
		}
		public bool OnStagingSegment =>
			m_Trajectory != null &&
			m_Trajectory.IsValid &&
			m_Trajectory.GearSegmentCount > 1 &&
			m_Index <= m_SegmentEndIndex &&
			m_Trajectory.GearAt(m_Index) == m_Trajectory.Points[0].Gear;
		/// <summary>True when goal-distance logic must not terminal-stop / comfort-brake yet.</summary>
		private bool HasPendingPathWork(float _distToEnd, float _distGoal)
		{
			if (!HasTrajectory)
				return false;

			if (_distGoal <= m_Goal.PositionTolerance && _distToEnd > c_ApproachZone)
				return true;

			if (OnStagingSegment && _distToEnd > c_ApproachZone)
				return true;

			if (_distGoal < c_StuckDistGoal)
			{
				int cusp = m_Trajectory.FindNextCusp(m_Index);
				if (cusp >= 0)
				{
					float distCusp = m_Trajectory.Points[cusp].ArcLength -
					                 m_Trajectory.Points[m_Index].ArcLength;
					if (distCusp > c_CuspStopMargin + 0.05f && _distToEnd > c_ApproachZone)
						return true;
				}
			}

			return false;
		}

		private bool InNearGoalExecutionPhase(float _distToEnd, float _distGoal) =>
			!HasPendingPathWork(_distToEnd, _distGoal);
		/// <summary>True while multi-gear path advances by index/arc (goal distance may grow).</summary>
		public bool StagingMakingProgress =>
			HasTrajectory && OnStagingSegment && m_StagingStuckTimer < c_StagingStuckTimeout;

		public void Activate(VehicleTrajectory _trajectory, GoalPose _goal, float _lookAhead = 3f, float _entrySpeedKmh = 0f)
		{
			m_Trajectory = _trajectory;
			m_Goal = _goal;
			float pathLen = _trajectory != null ? _trajectory.TotalLength : _lookAhead;
			// Short local maneuvers need a short look-ahead or Pure Pursuit understeers
			// and the vehicle has to replan the same turn repeatedly.
			float capped = Mathf.Clamp(pathLen * 0.35f, 0.8f, _lookAhead);
			if (_trajectory != null && _trajectory.GearSegmentCount > 1)
				capped = Mathf.Min(capped, 1.6f);
			if (_trajectory != null && _trajectory.IsValid)
			{
				float maxAbsCurv = 0f;
				for (int i = 0; i < _trajectory.PointCount; i++)
					maxAbsCurv = Mathf.Max(maxAbsCurv, Mathf.Abs(_trajectory.Points[i].Curvature));
				if (maxAbsCurv > 0.08f)
					capped = Mathf.Min(capped, 1.1f);
			}
			m_LookAhead = Mathf.Max(0.7f, capped);
			m_EntrySpeedKmh = Mathf.Max(0f, _entrySpeedKmh);
			m_Index = 0;
			m_WaitingStop = false;
			m_CreepGearLatched = false;
			m_CuspPhase = CuspPhase.None;
			m_CrossTrackExceededTicks = 0;
			m_BestDistGoal = float.MaxValue;
			m_BestIndex = 0;
			m_BestRemainingArc = _trajectory != null && _trajectory.IsValid
				? _trajectory.TotalLength
				: float.MaxValue;
			m_StagingStuckTimer = 0f;
			m_SmoothedDesiredSpeed = -1f;
			m_PathMaxAbsCurv = 0f;
			m_IsComplexPath = false;
			// Seed commanded κ from the plan so the first frames are not stuck at κ=0
			// while LimitCurvatureRate slowly ramps (side reverse was logging STEER=0).
			m_LastCommandedCurvature = _trajectory != null && _trajectory.IsValid && _trajectory.PointCount > 0
				? _trajectory.Points[0].Curvature
				: 0f;
			if (_trajectory != null && _trajectory.IsValid)
			{
				for (int i = 0; i < _trajectory.PointCount; i++)
					m_PathMaxAbsCurv = Mathf.Max(m_PathMaxAbsCurv, Mathf.Abs(_trajectory.Points[i].Curvature));
				string reason = _trajectory.DebugReason ?? string.Empty;
				m_IsComplexPath = _trajectory.GearSegmentCount > 1 ||
				                  m_PathMaxAbsCurv > c_HighCurvThreshold ||
				                  reason.Contains("two-stage") ||
				                  reason.Contains("rev-staging") ||
				                  reason.Contains("three-point") ||
				                  reason.StartsWith("rs-") ||
				                  reason.Contains("merged");
			}
			m_TurnEntryGateActive = false;
			m_TurnEntryGateTimer = 0f;
			m_TurnEntryAlignOkTimer = 0f;
			m_TurnEntryHoldIndex = 0;
			m_BestAbsXtrack = float.MaxValue;
			m_BestHeadingToPath = float.MaxValue;
			m_EntryWorsenTimer = 0f;
			m_AllowTurnEntryProgress = false;
			m_InSettleBand = false;
			m_ActivateTick = m_TickCounter;
			m_StraightReversePath = _trajectory != null &&
			                        !string.IsNullOrEmpty(_trajectory.DebugReason) &&
			                        _trajectory.DebugReason.StartsWith("straight-rev");
			// Short two-stage / side reposition: turn-entry gate blocks reverse staging (logs: side 2m stall).
			if (_trajectory != null && _trajectory.IsValid &&
			    (_trajectory.GearSegmentCount > 1 && pathLen < 8f ||
			     (!string.IsNullOrEmpty(_trajectory.DebugReason) &&
			      (_trajectory.DebugReason.Contains("two-stage-side") ||
			       _trajectory.DebugReason.Contains("rev-staging")))))
				m_AllowTurnEntryProgress = true;
			UpdateSegmentBounds();
		}

		public void SetPlanStats(LocalPosePlanner.PlanStats _stats) => PlanStats = _stats;

		public void Deactivate()
		{
			m_Trajectory = null;
			m_Index = 0;
			m_SegmentEndIndex = 0;
			m_WaitingStop = false;
			m_StraightReversePath = false;
			m_CreepGearLatched = false;
			m_CuspPhase = CuspPhase.None;
			m_LastCommandedCurvature = 0f;
			m_CrossTrackExceededTicks = 0;
			m_BestDistGoal = float.MaxValue;
			m_BestIndex = 0;
			m_BestRemainingArc = float.MaxValue;
			m_StagingStuckTimer = 0f;
			m_EntrySpeedKmh = 0f;
			m_TurnEntryGateActive = false;
			m_TurnEntryGateTimer = 0f;
			m_TurnEntryAlignOkTimer = 0f;
			m_TurnEntryHoldIndex = 0;
			m_BestAbsXtrack = float.MaxValue;
			m_BestHeadingToPath = float.MaxValue;
			m_EntryWorsenTimer = 0f;
			m_AllowTurnEntryProgress = false;
			m_InSettleBand = false;
			m_SmoothedDesiredSpeed = -1f;
			m_IsComplexPath = false;
			m_PathMaxAbsCurv = 0f;
		}

		public Output Tick(
			Vector3 _position,
			float _yaw,
			float _speedKmh,
			VehicleParameters _params,
			float _speedFraction)
		{
			if (!HasTrajectory)
			{
				LastOutput = new Output(
					MotionCommand.Empty, 0f, 0f, 0f, 0f, 0, 0, _position,
					true, false, false, false, false, TrajectoryGear.Forward);
				return LastOutput;
			}

			m_TickCounter++;

			UpdateSegmentBounds();
			UpdateTurnEntryGate(_yaw);
			AdvanceIndex(_position, _yaw);

			float distGoal = BicycleKinematics.FlatDistance(_position, m_Goal.Position);
			float distToEnd = m_Trajectory.RemainingDistance(m_Index);
			bool pendingPathWork = HasPendingPathWork(distToEnd, distGoal);
			float signedCrossTrack = ComputeSignedCrossTrack(_position, _yaw);
			float crossTrack = Mathf.Abs(signedCrossTrack);
			bool positionOk = ArrivalPositionBand.IsInside(m_Goal, _position, _yaw);
			bool headingOk = !m_Goal.RequiresPosePlanning ||
			                 Mathf.Abs(Mathf.DeltaAngle(_yaw, m_Goal.YawDegrees)) <= m_Goal.HeadingToleranceDeg;
			bool poseOk = positionOk && headingOk;

			if (distGoal < m_BestDistGoal - 0.02f)
				m_BestDistGoal = distGoal;

			TrajectoryGear gear = m_Trajectory.GearAt(m_Index);
			bool reverse = gear == TrajectoryGear.Reverse;
			bool requestSteerReset = false;
			float pathYaw = PathYawAtIndex;
			float headingToPath = Mathf.Abs(Mathf.DeltaAngle(_yaw, pathYaw));
			float pathCurvNow = m_Trajectory.Points[Mathf.Clamp(m_Index, 0, m_Trajectory.PointCount - 1)].Curvature;
			if (UpdateEntryImproveAbort(crossTrack, headingToPath, pathCurvNow))
			{
				LastOutput = new Output(
					new MotionCommand(0f, 0f, reverse, StopIntent.None),
					Mathf.Min(distToEnd, distGoal),
					signedCrossTrack, 0f, 0f,
					m_Index, m_SegmentEndIndex, _position,
					false, false, false, true, false, gear);
				return LastOutput;
			}

			// Inside strict position tolerance: terminal only when path work is finished.
			if (positionOk && !pendingPathWork)
			{
				bool stopped = Mathf.Abs(_speedKmh) <= c_StopSpeedKmh;
				Vector3 laPos = GetLookAheadPoint(_position, m_LookAhead);
				float settleDesired = SmoothDesiredSpeed(
					stopped ? 0f : Mathf.Min(c_CreepMaxKmh, Mathf.Abs(_speedKmh) * 0.55f),
					_speedKmh);
				if (!headingOk && m_Goal.RequiresPosePlanning)
				{
					LastOutput = new Output(
						new MotionCommand(settleDesired, 0f, reverse, StopIntent.Goal),
						distGoal, signedCrossTrack, 0f, 0f,
						m_Index, m_SegmentEndIndex, laPos,
						false, !stopped, true, false, false, gear, true);
					return LastOutput;
				}

				bool isComplete = poseOk && stopped;
				LastOutput = new Output(
					new MotionCommand(settleDesired, 0f, false, StopIntent.Goal),
					distGoal, signedCrossTrack, 0f, 0f,
					m_Index, m_SegmentEndIndex, laPos,
					isComplete, !stopped, false, false, false, gear, true);
				return LastOutput;
			}

			if (crossTrack > c_DivergenceCrossTrack)
				m_CrossTrackExceededTicks++;
			else if (crossTrack < c_DivergenceRecoverCrossTrack)
				m_CrossTrackExceededTicks = 0;

			if (crossTrack >= c_ImmediateReplanCrossTrack ||
			    m_CrossTrackExceededTicks >= c_CrossTrackReplanTicks)
			{
				if (ShouldSuppressPathReplan(_position, distGoal, crossTrack, distToEnd))
				{
					// Continue executing current cusp path.
				}
				else
				{
					LastOutput = new Output(
						new MotionCommand(0f, 0f, reverse, StopIntent.None),
						Mathf.Min(distToEnd, distGoal),
						signedCrossTrack, 0f, 0f,
						m_Index, m_SegmentEndIndex, _position,
						false, false, false, true, false, gear);
					return LastOutput;
				}
			}

			// Path exhausted far from goal — replan. Near-goal band creeps instead (below).
			float settleEnter = Mathf.Max(c_SettleBandMax, m_Goal.PositionTolerance);
			if (InNearGoalExecutionPhase(distToEnd, distGoal))
				UpdateSettleBand(distGoal, settleEnter);
			else
				m_InSettleBand = false;
			if (distToEnd < 0.5f && distGoal > c_StuckDistGoal && !positionOk &&
			    !m_InSettleBand &&
			    !ShouldSuppressPathReplan(_position, distGoal, crossTrack, distToEnd))
			{
				LastOutput = new Output(
					new MotionCommand(0f, 0f, reverse, StopIntent.None),
					Mathf.Min(distToEnd, distGoal),
					signedCrossTrack, 0f, 0f,
					m_Index, m_SegmentEndIndex, _position,
					false, false, false, true, false, gear);
				return LastOutput;
			}

			// Near-goal settle: creep to strict tolerance — hysteresis avoids leave/re-approach loops.
			if (m_InSettleBand && distToEnd < 0.75f && InNearGoalExecutionPhase(distToEnd, distGoal))
			{
				LastOutput = BuildSettleCreepOutput(
					_position, _yaw, distGoal, signedCrossTrack, gear, reverse, _params, _speedKmh);
				return LastOutput;
			}

			int cusp = m_Trajectory.FindNextCusp(m_Index);
			float distToCusp = cusp >= 0
				? Mathf.Max(0f, m_Trajectory.Points[cusp].ArcLength - m_Trajectory.Points[m_Index].ArcLength)
				: float.MaxValue;
			float stopHorizon = Mathf.Min(distToEnd, distToCusp);
			// Softer decelerations for cusp/complex local paths — SoftBrake at 4–5 m/s² looks like a jolt.
			float comfortDecel = Mathf.Min(_params.ComfortBrakeDecelMs2, c_ManeuverComfortDecelMs2);
			if (m_IsComplexPath || cusp >= 0)
				comfortDecel = Mathf.Min(comfortDecel, 0.85f);
			float speedMs = Mathf.Abs(_speedKmh) / 3.6f;
			float brakeDistance = speedMs * speedMs / (2f * comfortDecel);
			bool approachingCusp = cusp >= 0 && distToCusp < brakeDistance + c_CuspApproachBuffer;
			bool atCuspStop = cusp >= 0 && distToCusp <= c_CuspStopMargin + 0.05f;

			if (m_WaitingStop || atCuspStop)
			{
				m_WaitingStop = true;
				m_CuspPhase = m_CuspPhase == CuspPhase.None ? CuspPhase.Approach : m_CuspPhase;
				if (_speedKmh > c_CuspSwitchMaxSpeedKmh)
				{
					m_CuspPhase = CuspPhase.Approach;
					// Do not slam DesiredSpeed=0 — SoftBrake then jerks the chassis.
					float softStop = Mathf.Min(
						c_CuspCrawlMaxSpeedKmh,
						Mathf.Max(0f, _speedKmh - c_DesiredSpeedSlewDownKmhPerSec * Time.fixedDeltaTime));
					softStop = SmoothDesiredSpeed(softStop, _speedKmh);
					LastOutput = new Output(
						new MotionCommand(softStop, 0f, reverse, StopIntent.GearChange),
						distToEnd, signedCrossTrack, 0f, 0f,
						m_Index, m_SegmentEndIndex, _position,
						false, true, false, false, false, gear);
					return LastOutput;
				}

				m_CuspPhase = CuspPhase.Stopped;
				// Always enter the NEXT gear after the cusp — never stay on the cusp
				// sample (same gear) or look-ahead will latch onto the following maneuver.
				if (cusp >= 0 && cusp + 1 < m_Trajectory.PointCount)
				{
					m_Index = cusp + 1;
					requestSteerReset = true;
				}
				else if (cusp >= 0)
				{
					m_Index = cusp;
					requestSteerReset = true;
				}

				m_WaitingStop = false;
				m_CuspPhase = CuspPhase.Depart;
				gear = m_Trajectory.GearAt(m_Index);
				reverse = gear == TrajectoryGear.Reverse;
				UpdateSegmentBounds();
				m_StraightReversePath = IsStraightReverseSegment();
				m_LastCommandedCurvature = m_Trajectory.Points[
					Mathf.Clamp(m_Index, 0, m_Trajectory.PointCount - 1)].Curvature;
				m_SmoothedDesiredSpeed = -1f;
			}

			Vector3 controlFwd = BicycleKinematics.YawToForward(_yaw);
			// Track from the same reference the planner used (vehicle root / start pose).
			// Rear-axle offset vs root-planned samples creates false curvature on straight-rev.
			Vector3 controlPos = _position;

			float maxCurv = 1f / Mathf.Max(0.5f, _params.EffectiveTurnRadius);
			float motionCurvature;
			float wheelCurvature;

			Vector3 toGoal = m_Goal.Position - controlPos;
			toGoal.y = 0f;

			if (InNearGoalExecutionPhase(distToEnd, distGoal) &&
			    distGoal > m_Goal.PositionTolerance && distGoal <= c_MicroCreepMax)
			{
				LastOutput = BuildSettleCreepOutput(
					_position, _yaw, distGoal, signedCrossTrack, gear, reverse, _params, _speedKmh,
					requestSteerReset);
				return LastOutput;
			}

			if (InNearGoalExecutionPhase(distToEnd, distGoal) &&
			    stopHorizon < 0.25f && distGoal > m_Goal.PositionTolerance && distGoal <= c_StuckDistGoal)
			{
				if (m_InSettleBand)
				{
					LastOutput = BuildSettleCreepOutput(
						_position, _yaw, distGoal, signedCrossTrack, gear, reverse, _params, _speedKmh);
					return LastOutput;
				}

				LastOutput = new Output(
					new MotionCommand(0f, 0f, reverse, StopIntent.None),
					Mathf.Min(distToEnd, distGoal),
					signedCrossTrack, 0f, 0f,
					m_Index, m_SegmentEndIndex, _position,
					false, false, false, true, requestSteerReset, gear);
				return LastOutput;
			}

			Vector3 lookPt = GetLookAheadPoint(
				_position, m_TurnEntryGateActive ? Mathf.Min(m_LookAhead, 0.8f) : m_LookAhead);
			bool overshot = stopHorizon < 0.25f && distGoal <= c_OvershootReplanDist &&
			                InNearGoalExecutionPhase(distToEnd, distGoal);
			if (overshot)
			{
				if (!m_Goal.RequiresPosePlanning && m_InSettleBand)
				{
					LastOutput = BuildSettleCreepOutput(
						_position, _yaw, distGoal, signedCrossTrack, gear, reverse, _params, _speedKmh);
					return LastOutput;
				}

				if (!m_Goal.RequiresPosePlanning)
				{
					LastOutput = BuildSettleCreepOutput(
						_position, _yaw, distGoal, signedCrossTrack, gear, reverse, _params, _speedKmh,
						requestSteerReset);
					return LastOutput;
				}

				reverse = ResolveCreepGear(reverse ? -controlFwd : controlFwd, toGoal, out gear);

				wheelCurvature = m_Goal.HasHeading
					? Mathf.Clamp(Mathf.DeltaAngle(_yaw, m_Goal.YawDegrees) * Mathf.Deg2Rad / 4f, -maxCurv * 0.35f, maxCurv * 0.35f)
					: 0f;

				float creep = Mathf.Clamp(distGoal * 6f, c_CreepMinKmh, c_CreepMaxKmh);
				wheelCurvature = LimitCurvatureRate(wheelCurvature, _params);
				LastOutput = new Output(
					new MotionCommand(creep, wheelCurvature, reverse),
					Mathf.Min(distToEnd, distGoal),
					signedCrossTrack, 0f, wheelCurvature,
					m_Index, m_SegmentEndIndex, lookPt,
					false, m_WaitingStop, false, false, requestSteerReset, gear);
				return LastOutput;
			}

			Vector3 toLook = lookPt - controlPos;
			toLook.y = 0f;
			float L2 = Mathf.Max(0.05f, toLook.sqrMagnitude);

			float motionYaw = reverse ? _yaw + 180f : _yaw;
			Vector3 motionFwd = BicycleKinematics.YawToForward(motionYaw);
			float cross = Vector3.Dot(new Vector3(motionFwd.z, 0f, -motionFwd.x), toLook);
			float ppCurv = Mathf.Clamp(2f * cross / L2, -maxCurv, maxCurv);
			motionCurvature = ppCurv;

			float pathCurv = m_Trajectory.Points[Mathf.Clamp(m_Index, 0, m_Trajectory.PointCount - 1)].Curvature;

			// Plan-first with proportional tube trim once error exceeds half-threshold.
			float xtTrim = Mathf.Max(0f, crossTrack - c_CorrectCrossTrackM * 0.5f);
			float hdgTrim = Mathf.Max(0f, headingToPath - c_CorrectHeadingDeg * 0.5f);
			bool needCorrect = xtTrim > 0f || hdgTrim > 0f;
			float trimScale = needCorrect
				? Mathf.Clamp01(Mathf.Max(xtTrim / c_CorrectCrossTrackM, hdgTrim / c_CorrectHeadingDeg))
				: 0f;

			if (m_StraightReversePath || IsStraightReverseSegment())
			{
				motionCurvature = 0f;
				if (Mathf.Abs(signedCrossTrack) > c_StraightRevCrossTrackDeadband)
					wheelCurvature = Mathf.Clamp(-signedCrossTrack * 0.6f, -maxCurv * 0.12f, maxCurv * 0.12f);
				else
					wheelCurvature = 0f;
			}
			else
			{
				wheelCurvature = pathCurv;
				if (needCorrect)
				{
					float ppWheel = reverse ? -ppCurv : ppCurv;
					float alignCurv = Mathf.Clamp(
						Mathf.DeltaAngle(_yaw, pathYaw) * Mathf.Deg2Rad / 5f,
						-maxCurv * 0.25f, maxCurv * 0.25f);
					float trim = Mathf.Clamp(
						(ppWheel * 0.35f + alignCurv) * trimScale, -maxCurv * 0.3f, maxCurv * 0.3f);
					wheelCurvature = Mathf.Clamp(pathCurv + trim, -maxCurv, maxCurv);
				}
				else if (Mathf.Abs(wheelCurvature) < c_WheelCurvatureDeadband)
				{
					wheelCurvature = 0f;
				}

				motionCurvature = reverse ? -wheelCurvature : wheelCurvature;
			}

			wheelCurvature = LimitCurvatureRate(wheelCurvature, _params);

			float maxSpeed = reverse ? _params.MaxReverseSpeedKmh : _params.MaxForwardSpeedKmh;
			if (reverse)
				maxSpeed = Mathf.Min(maxSpeed, c_LocalReverseMaxSpeedKmh);
			maxSpeed *= Mathf.Clamp01(_speedFraction);

			// Complex local maneuvers (multi-gear / staging / high κ): keep cruise modest.
			if (m_IsComplexPath)
			{
				maxSpeed = Mathf.Min(maxSpeed, reverse ? c_ComplexReverseMaxKmh : c_ComplexManeuverMaxKmh);
				if (approachingCusp || cusp >= 0)
					maxSpeed = Mathf.Min(maxSpeed, c_ComplexCuspMaxKmh);
			}
			else if (cusp >= 0 && distToCusp < 6f)
			{
				maxSpeed = Mathf.Min(maxSpeed, Mathf.Lerp(c_ComplexCuspMaxKmh, maxSpeed, distToCusp / 6f));
			}

			float planCurvAbs = Mathf.Max(Mathf.Abs(pathCurv), PreviewAbsCurvature(1.5f));
			float curvFactor = 1f;
			if (_params.CurvatureSpeedCurve != null)
				curvFactor = Mathf.Clamp01(_params.CurvatureSpeedCurve.Evaluate(planCurvAbs));
			else if (planCurvAbs > 0.05f)
				curvFactor = Mathf.Lerp(1f, 0.35f, Mathf.InverseLerp(0.05f, maxCurv, planCurvAbs));
			if (m_IsComplexPath)
				curvFactor = Mathf.Min(curvFactor, 0.55f);

			float cruiseDesired = maxSpeed * Mathf.Max(0.2f, curvFactor);

			float horizonDist = pendingPathWork ? stopHorizon : Mathf.Min(stopHorizon, distGoal);
			float comfortStopDist = brakeDistance + c_BrakeMarginM;
			float stopSpeed = Mathf.Sqrt(Mathf.Max(0f, 2f * comfortDecel * horizonDist)) * 3.6f;
			if (horizonDist < comfortStopDist)
				stopSpeed = Mathf.Min(stopSpeed, Mathf.Max(0f, _speedKmh - 1.5f));
			float desired = Mathf.Min(cruiseDesired, stopSpeed);

			if (m_TurnEntryGateActive)
				desired = Mathf.Min(desired, c_EntryGateMaxSpeedKmh);

			if (approachingCusp && distToCusp > c_CuspStopMargin)
			{
				float cuspSpeed = Mathf.Sqrt(Mathf.Max(0f, 2f * comfortDecel * (distToCusp - c_CuspStopMargin))) * 3.6f;
				cuspSpeed = Mathf.Min(cuspSpeed, c_ComplexCuspMaxKmh);
				if (_speedKmh > c_CuspCrawlMaxSpeedKmh)
					desired = Mathf.Min(desired, cuspSpeed);
				else
					desired = Mathf.Min(desired, Mathf.Max(cuspSpeed, c_CuspCrawlMaxSpeedKmh * 0.5f));
			}

			StopIntent stopIntent = StopIntent.None;
			if (approachingCusp && distToCusp > c_CuspStopMargin)
				stopIntent = StopIntent.GearChange;
			else if (distToEnd < c_ApproachZone ||
			         (!pendingPathWork && distGoal < c_ApproachZone) ||
			         (!pendingPathWork && horizonDist < comfortStopDist && stopSpeed < cruiseDesired - 0.05f))
				stopIntent = StopIntent.Goal;

			if (!pendingPathWork && distGoal < c_ApproachZone)
				desired = Mathf.Min(desired, Mathf.Sqrt(Mathf.Max(0f, 2f * comfortDecel * distGoal)) * 3.6f + 0.5f);

			// Do not re-boost entry speed on complex multi-gear paths — that causes the "spins up then jerks".
			if (!m_IsComplexPath && m_EntrySpeedKmh > 0.05f && m_TickCounter - m_ActivateTick <= 3)
				desired = Mathf.Max(desired, Mathf.Min(m_EntrySpeedKmh, maxSpeed));

			if (crossTrack > c_CorrectCrossTrackM * 2.5f)
				desired *= Mathf.Clamp01(1f - (crossTrack - c_CorrectCrossTrackM * 2f) * 0.15f);

			if (stopHorizon < 0.25f && distGoal > m_Goal.PositionTolerance * 1.5f && cusp < 0)
			{
				if (toGoal.sqrMagnitude > 1e-4f)
				{
					float goalAlign = Vector3.Angle(reverse ? -controlFwd : controlFwd, toGoal);
					if (goalAlign > 100f)
					{
						reverse = !reverse;
						gear = reverse ? TrajectoryGear.Reverse : TrajectoryGear.Forward;
					}
				}

				desired = Mathf.Min(desired, 1f);
				float headingErr = m_Goal.HasHeading
					? Mathf.DeltaAngle(_yaw, m_Goal.YawDegrees)
					: Vector3.SignedAngle(controlFwd, toGoal.normalized, Vector3.up);
				wheelCurvature = Mathf.Clamp(headingErr * Mathf.Deg2Rad / 3f, -maxCurv * 0.5f, maxCurv * 0.5f);
				motionCurvature = reverse ? -wheelCurvature : wheelCurvature;
			}

			desired = SmoothDesiredSpeed(desired, _speedKmh);

			var cmd = new MotionCommand(desired, wheelCurvature, reverse, stopIntent);
			bool complete = poseOk && !pendingPathWork && _speedKmh <= c_StopSpeedKmh;

			LastOutput = new Output(
				cmd,
				pendingPathWork ? distToEnd : Mathf.Min(distToEnd, distGoal),
				signedCrossTrack,
				motionCurvature,
				wheelCurvature,
				m_Index,
				m_SegmentEndIndex,
				lookPt,
				complete,
				m_WaitingStop,
				false,
				false,
				requestSteerReset,
				gear);
			return LastOutput;
		}

		private float SmoothDesiredSpeed(float _targetKmh, float _currentSpeedKmh)
		{
			float dt = Mathf.Max(1e-3f, Time.fixedDeltaTime);
			if (m_SmoothedDesiredSpeed < 0f)
			{
				m_SmoothedDesiredSpeed = Mathf.Min(_targetKmh, Mathf.Abs(_currentSpeedKmh) + 1f);
				return m_SmoothedDesiredSpeed;
			}

			float down = c_DesiredSpeedSlewDownKmhPerSec * dt;
			float up = c_DesiredSpeedSlewUpKmhPerSec * dt;
			if (_targetKmh < m_SmoothedDesiredSpeed)
				m_SmoothedDesiredSpeed = Mathf.Max(_targetKmh, m_SmoothedDesiredSpeed - down);
			else
				m_SmoothedDesiredSpeed = Mathf.Min(_targetKmh, m_SmoothedDesiredSpeed + up);
			return m_SmoothedDesiredSpeed;
		}

		private float PreviewAbsCurvature(float _lookAheadM)
		{
			if (!HasTrajectory)
				return 0f;
			float baseArc = m_Trajectory.Points[m_Index].ArcLength;
			float endArc = baseArc + Mathf.Max(0.5f, _lookAheadM);
			float peak = 0f;
			int end = Mathf.Min(m_SegmentEndIndex, m_Trajectory.PointCount - 1);
			for (int i = m_Index; i <= end; i++)
			{
				TrajectoryPoint p = m_Trajectory.Points[i];
				if (p.ArcLength > endArc)
					break;
				peak = Mathf.Max(peak, Mathf.Abs(p.Curvature));
			}
			return peak;
		}

		private bool ResolveCreepGear(Vector3 _controlFwd, Vector3 _toGoal, out TrajectoryGear _gear)
		{
			bool reverse = false;
			_gear = TrajectoryGear.Forward;
			if (_toGoal.sqrMagnitude <= 1e-4f)
				return false;

			float goalAlign = Vector3.Angle(_controlFwd, _toGoal);
			if (!m_CreepGearLatched)
			{
				m_CreepGear = goalAlign > 90f ? TrajectoryGear.Reverse : TrajectoryGear.Forward;
				m_CreepGearLatched = true;
			}
			else if (m_CreepGear == TrajectoryGear.Forward && goalAlign > 90f + c_CreepGearHysteresisDeg)
				m_CreepGear = TrajectoryGear.Reverse;
			else if (m_CreepGear == TrajectoryGear.Reverse && goalAlign < 90f - c_CreepGearHysteresisDeg)
				m_CreepGear = TrajectoryGear.Forward;

			reverse = m_CreepGear == TrajectoryGear.Reverse;
			_gear = m_CreepGear;
			return reverse;
		}

		/// <summary>
		/// Cap |dκ/dt| to what road wheels can achieve at SteerRate (δ̇ ≈ SteerRate).
		/// κ ≈ tan(δ)/L → κ̇ ≈ δ̇_rad / (L * cos²(δ)) ≤ δ̇_rad / L near center.
		/// </summary>
		private float LimitCurvatureRate(float _desiredCurvature, VehicleParameters _params)
		{
			float dt = Mathf.Max(1e-3f, Time.fixedDeltaTime);
			float wb = Mathf.Max(0.5f, _params.WheelBase);
			float steerRateRad = Mathf.Max(1f, _params.SteeringRateDegPerSec) * Mathf.Deg2Rad;
			float maxDelta = (steerRateRad / wb) * dt;
			float limited = Mathf.MoveTowards(m_LastCommandedCurvature, _desiredCurvature, maxDelta);
			m_LastCommandedCurvature = limited;
			return limited;
		}

		private void UpdateSegmentBounds()
		{
			if (!HasTrajectory)
			{
				m_SegmentEndIndex = 0;
				return;
			}

			m_Trajectory.GetSegmentBounds(m_Index, out _, out m_SegmentEndIndex);
		}

		private bool IsStraightReverseSegment()
		{
			if (!HasTrajectory || m_Trajectory.GearAt(m_Index) != TrajectoryGear.Reverse)
				return false;

			if (!string.IsNullOrEmpty(m_Trajectory.DebugReason) &&
			    m_Trajectory.DebugReason.StartsWith("straight-rev"))
				return true;

			int start = 0;
			m_Trajectory.GetSegmentBounds(m_Index, out start, out _);
			for (int i = start; i <= m_SegmentEndIndex; i++)
			{
				if (Mathf.Abs(m_Trajectory.Points[i].Curvature) > 0.02f)
					return false;
			}

			return true;
		}

		private bool ShouldSuppressPathReplan(Vector3 _position, float _distGoal, float _crossTrack, float _distToEnd)
		{
			if (!HasTrajectory)
				return false;

			bool multiSegment = m_Trajectory.GearSegmentCount > 1;
			if (multiSegment && m_CuspPhase != CuspPhase.None && m_CuspPhase != CuspPhase.Depart)
				return true;

			if (multiSegment && InNearGoalExecutionPhase(_distToEnd, _distGoal) &&
			    _distGoal < c_StuckDistGoal &&
			    _crossTrack < c_DivergenceRecoverCrossTrack && _distToEnd > 0.35f)
				return true;

			// Still closing on the goal: do not tear down a curved RS/two-stage path
			// just because Pure Pursuit lags the sample curvature.
			if (_distGoal <= m_BestDistGoal + 0.25f &&
			    _crossTrack < c_ImmediateReplanCrossTrack &&
			    _distToEnd > 0.4f)
				return true;

			if (HasPendingPathWork(_distToEnd, _distGoal) &&
			    _crossTrack < c_ImmediateReplanCrossTrack &&
			    _distGoal < c_StuckDistGoal)
				return true;

			// Inside/near arrival band: do not tear the path for micro cross-track noise.
			if (_distGoal <= m_Goal.PositionTolerance * 1.35f &&
			    _crossTrack < c_ImmediateReplanCrossTrack)
				return true;

			// Suppress XTRACK replan while staging advances; hang ends via stuck timeout.
			if (multiSegment && OnStagingSegment && _crossTrack < c_ImmediateReplanCrossTrack &&
			    m_StagingStuckTimer < c_StagingStuckTimeout)
				return true;

			return false;
		}

		private void AdvanceIndex(Vector3 _position, float _yaw)
		{
			if (!HasTrajectory || m_Trajectory.PointCount < 2)
				return;

			m_Trajectory.GetSegmentBounds(m_Index, out int segStart, out int segEnd);
			float currentArc = m_Trajectory.Points[m_Index].ArcLength;
			const float maxArcJump = 2.5f;
			bool lockedAtCusp = m_WaitingStop || m_CuspPhase == CuspPhase.Approach || m_CuspPhase == CuspPhase.Stopped;
			int gateCap = m_TurnEntryGateActive
				? Mathf.Clamp(m_TurnEntryHoldIndex, segStart, segEnd)
				: segEnd;

			int searchStart = Mathf.Max(segStart, m_Index - 2);
			int searchEnd = lockedAtCusp ? m_Index : m_Index;
			for (int i = m_Index; i <= segEnd; i++)
			{
				if (lockedAtCusp)
					break;
				if (m_Trajectory.Points[i].ArcLength - currentArc > maxArcJump)
					break;
				searchEnd = i;
			}

			searchEnd = Mathf.Min(searchEnd, gateCap);

			int best = m_Index;
			float bestDist = float.MaxValue;
			for (int i = searchStart; i <= searchEnd; i++)
			{
				float d = BicycleKinematics.FlatDistance(_position, m_Trajectory.Points[i].Position);
				if (d < bestDist)
				{
					bestDist = d;
					best = i;
				}
			}

			if (best > m_Index)
			{
				int cusp = m_Trajectory.FindNextCusp(m_Index);
				if (cusp >= 0 && best > cusp)
					best = cusp;
				if (best > m_SegmentEndIndex)
					best = m_SegmentEndIndex;
				best = CapIndexForTurnEntry(best, _yaw, gateCap);
				float vehicleArc = GetVehicleArcOnPath(_position);
				best = CapIndexToVehicleLead(best, vehicleArc);
				m_Index = best;
			}

			if (m_Index < segEnd && m_Index < gateCap)
			{
				float toNext = BicycleKinematics.FlatDistance(
					_position, m_Trajectory.Points[m_Index + 1].Position);
				if (toNext < 0.35f)
				{
					int next = CapIndexForTurnEntry(m_Index + 1, _yaw, gateCap);
					next = CapIndexToVehicleLead(next, GetVehicleArcOnPath(_position));
					m_Index = next;
				}
			}

			if (m_TurnEntryGateActive && m_Index > gateCap)
				m_Index = gateCap;

			bool progressed = false;
			if (m_Index > m_BestIndex)
			{
				m_BestIndex = m_Index;
				progressed = true;
			}

			float remArc = m_Trajectory.RemainingDistance(m_Index);
			if (remArc < m_BestRemainingArc - 0.02f)
			{
				m_BestRemainingArc = remArc;
				progressed = true;
			}

			if (OnStagingSegment)
			{
				if (progressed || m_CuspPhase != CuspPhase.None)
					m_StagingStuckTimer = 0f;
				else
					m_StagingStuckTimer += Time.fixedDeltaTime;
			}
			else
			{
				m_StagingStuckTimer = 0f;
			}
		}

		private int CapIndexForTurnEntry(int _candidate, float _yaw, int _gateCap)
		{
			int capped = Mathf.Min(_candidate, _gateCap);
			if (m_StraightReversePath || IsStraightReverseSegment() || m_AllowTurnEntryProgress)
				return capped;

			for (int i = m_Index + 1; i <= capped; i++)
			{
				if (Mathf.Abs(m_Trajectory.Points[i].Curvature) <= c_HighCurvThreshold)
					continue;

				float herr = Mathf.Abs(Mathf.DeltaAngle(_yaw, m_Trajectory.Points[i].YawDegrees));
				if (herr > c_EntryYawHoldDeg)
					return Mathf.Max(m_Index, i - 1);
			}

			return capped;
		}

		private void UpdateTurnEntryGate(float _yaw)
		{
			if (!HasTrajectory || m_StraightReversePath || IsStraightReverseSegment())
			{
				m_TurnEntryGateActive = false;
				m_TurnEntryGateTimer = 0f;
				m_TurnEntryAlignOkTimer = 0f;
				return;
			}

			float dt = Mathf.Max(1e-3f, Time.fixedDeltaTime);

			// Scan the upcoming high-κ window for the worst heading mismatch.
			// CapIndexForTurnEntry holds m_Index just before yaw diverges, so comparing
			// only Points[m_Index] never trips the gate — must look deeper into the turn.
			float baseArc = m_Trajectory.Points[m_Index].ArcLength;
			float endArc = baseArc + Mathf.Max(m_LookAhead, 2.5f);
			bool anyHigh = false;
			int firstHigh = -1;
			int lastLow = m_Index;
			float maxHeadingErr = 0f;

			for (int i = m_Index; i <= m_SegmentEndIndex; i++)
			{
				TrajectoryPoint p = m_Trajectory.Points[i];
				if (p.ArcLength > endArc)
					break;

				if (Mathf.Abs(p.Curvature) > c_HighCurvThreshold)
				{
					anyHigh = true;
					if (firstHigh < 0)
						firstHigh = i;
					float herr = Mathf.Abs(Mathf.DeltaAngle(_yaw, p.YawDegrees));
					if (herr > maxHeadingErr)
						maxHeadingErr = herr;
				}
				else if (firstHigh < 0)
				{
					lastLow = i;
				}
			}

			if (!anyHigh)
			{
				m_TurnEntryGateActive = false;
				m_TurnEntryGateTimer = 0f;
				m_TurnEntryAlignOkTimer = 0f;
				return;
			}

			if (maxHeadingErr > c_EntryYawHoldDeg)
			{
				if (!m_TurnEntryGateActive)
				{
					m_TurnEntryGateActive = true;
					m_TurnEntryGateTimer = 0f;
					m_TurnEntryAlignOkTimer = 0f;
					bool highHere = Mathf.Abs(m_Trajectory.Points[m_Index].Curvature) > c_HighCurvThreshold;
					m_TurnEntryHoldIndex = highHere
						? m_Index
						: Mathf.Clamp(lastLow, 0, m_SegmentEndIndex);
				}
			}

			if (!m_TurnEntryGateActive)
				return;

			m_TurnEntryGateTimer += dt;
			if (maxHeadingErr < c_EntryYawReleaseDeg)
			{
				m_TurnEntryAlignOkTimer += dt;
				if (m_TurnEntryAlignOkTimer >= c_EntryAlignHoldSec)
					m_TurnEntryGateActive = false;
			}
			else
			{
				m_TurnEntryAlignOkTimer = 0f;
			}

			// Timeout: release hold and allow limited index progress (improve-abort may still replan).
			if (m_TurnEntryGateTimer >= c_EntryGateTimeoutSec)
			{
				m_TurnEntryGateActive = false;
				m_AllowTurnEntryProgress = true;
			}
		}

		/// <summary>
		/// During turn entry, if both cross-track and heading-to-path worsen vs best for ~0.6s,
		/// request a softer replan. Bypasses staging "still closing" suppress.
		/// </summary>
		private bool UpdateEntryImproveAbort(float _absCrossTrack, float _headingToPathDeg, float _pathCurv)
		{
			if (!HasTrajectory || m_StraightReversePath || IsStraightReverseSegment())
			{
				m_EntryWorsenTimer = 0f;
				return false;
			}

			float arc = m_Trajectory.Points[Mathf.Clamp(m_Index, 0, m_Trajectory.PointCount - 1)].ArcLength;
			bool earlyEntry = arc < 2.5f;
			bool firstGearMonitor = m_Trajectory.GearSegmentCount == 1 || OnStagingSegment;
			bool monitor = firstGearMonitor &&
			               (m_TurnEntryGateActive ||
			                (Mathf.Abs(_pathCurv) > c_HighCurvThreshold && earlyEntry));

			// Seed bests on first sample.
			if (m_BestAbsXtrack > 1e20f)
			{
				m_BestAbsXtrack = _absCrossTrack;
				m_BestHeadingToPath = _headingToPathDeg;
				m_EntryWorsenTimer = 0f;
				return false;
			}

			bool xtImproved = _absCrossTrack < m_BestAbsXtrack - 0.01f;
			bool hdgImproved = _headingToPathDeg < m_BestHeadingToPath - 0.5f;
			if (xtImproved)
				m_BestAbsXtrack = _absCrossTrack;
			if (hdgImproved)
				m_BestHeadingToPath = _headingToPathDeg;

			if (!monitor)
			{
				m_EntryWorsenTimer = 0f;
				return false;
			}

			if (xtImproved || hdgImproved)
			{
				m_EntryWorsenTimer = 0f;
				return false;
			}

			float dt = Mathf.Max(1e-3f, Time.fixedDeltaTime);
			bool worsened = _absCrossTrack > m_BestAbsXtrack + 0.08f &&
			                _headingToPathDeg > m_BestHeadingToPath + 2f;
			if (worsened)
			{
				m_EntryWorsenTimer += dt;
				if (m_EntryWorsenTimer >= c_EntryWorsenWindowSec)
				{
					m_EntryWorsenTimer = 0f;
					return true;
				}
			}
			else
			{
				m_EntryWorsenTimer = Mathf.Max(0f, m_EntryWorsenTimer - dt * 0.25f);
			}

			return false;
		}

		private Output BuildSettleCreepOutput(
			Vector3 _position,
			float _yaw,
			float _distGoal,
			float _signedCrossTrack,
			TrajectoryGear _gear,
			bool _reverse,
			VehicleParameters _params,
			float _speedKmh,
			bool _requestSteerReset = false)
		{
			Vector3 fwd = BicycleKinematics.YawToForward(_yaw);
			Vector3 toGoal = m_Goal.Position - _position;
			toGoal.y = 0f;
			Vector3 lookPt = GetLookAheadPoint(_position, m_LookAhead);

			float distToEnd = HasTrajectory ? m_Trajectory.RemainingDistance(m_Index) : _distGoal;
			bool headingOk = !m_Goal.RequiresPosePlanning ||
			                 Mathf.Abs(Mathf.DeltaAngle(_yaw, m_Goal.YawDegrees)) <= m_Goal.HeadingToleranceDeg;
			if (ArrivalPositionBand.IsInside(m_Goal, _position, _yaw) && headingOk &&
			    !HasPendingPathWork(distToEnd, _distGoal))
			{
				return new Output(
					new MotionCommand(0f, 0f, _reverse, StopIntent.Goal),
					_distGoal, _signedCrossTrack, 0f, 0f,
					m_Index, m_SegmentEndIndex, lookPt,
					false, _speedKmh > c_StopSpeedKmh, false, false, _requestSteerReset, _gear, true);
			}

			bool reverse = _reverse;
			TrajectoryGear gear = _gear;
			float maxCurv = 1f / Mathf.Max(0.5f, _params.EffectiveTurnRadius);
			float wheelCurvature = 0f;

			if (HasPendingPathWork(distToEnd, _distGoal))
			{
				float pathCurv = m_Trajectory.Points[
					Mathf.Clamp(m_Index, 0, m_Trajectory.PointCount - 1)].Curvature;
				wheelCurvature = pathCurv;
				if (m_StraightReversePath || IsStraightReverseSegment())
					wheelCurvature = 0f;

				float creepDist = distToEnd;
				int cusp = m_Trajectory.FindNextCusp(m_Index);
				if (cusp >= 0)
				{
					float distCusp = m_Trajectory.Points[cusp].ArcLength -
					                 m_Trajectory.Points[m_Index].ArcLength;
					creepDist = Mathf.Min(creepDist, Mathf.Max(0f, distCusp));
				}

				float stagingSpeed = ComputeSettleCreepSpeed(creepDist, _speedKmh);
				wheelCurvature = LimitCurvatureRate(wheelCurvature, _params);
				return new Output(
					new MotionCommand(stagingSpeed, wheelCurvature, reverse),
					Mathf.Min(distToEnd, _distGoal), _signedCrossTrack,
					reverse ? -wheelCurvature : wheelCurvature, wheelCurvature,
					m_Index, m_SegmentEndIndex, lookPt,
					false, false, false, false, _requestSteerReset, gear, false);
			}

			if (m_Goal.RequiresPosePlanning && toGoal.sqrMagnitude > 1e-4f)
				reverse = ResolveCreepGear(fwd, toGoal, out gear);

			if (m_Goal.RequiresPosePlanning || m_Goal.HasAdvisoryHeading)
			{
				float headingErr = Mathf.DeltaAngle(_yaw, m_Goal.YawDegrees);
				wheelCurvature = Mathf.Clamp(
					headingErr * Mathf.Deg2Rad / 5f, -maxCurv * 0.25f, maxCurv * 0.25f);
			}
			else if (m_StraightReversePath || IsStraightReverseSegment())
			{
				wheelCurvature = 0f;
			}
			else if (toGoal.sqrMagnitude > 1e-4f)
			{
				float motionYaw = reverse ? _yaw + 180f : _yaw;
				float alignErr = Mathf.DeltaAngle(motionYaw,
					Quaternion.LookRotation(toGoal.normalized, Vector3.up).eulerAngles.y);
				wheelCurvature = Mathf.Clamp(
					alignErr * Mathf.Deg2Rad / 4f, -maxCurv * 0.2f, maxCurv * 0.2f);
			}

			float creep = ComputeSettleCreepSpeed(_distGoal, _speedKmh);
			return new Output(
				new MotionCommand(creep, wheelCurvature, reverse),
				_distGoal, _signedCrossTrack, reverse ? -wheelCurvature : wheelCurvature, wheelCurvature,
				m_Index, m_SegmentEndIndex, lookPt,
				false, false, false, false, _requestSteerReset, gear, false);
		}

		private void UpdateSettleBand(float _distGoal, float _enterDist)
		{
			float exitDist = _enterDist * c_SettleExitMultiplier;
			if (m_InSettleBand)
			{
				if (_distGoal > exitDist)
					m_InSettleBand = false;
			}
			else if (_distGoal <= _enterDist)
			{
				m_InSettleBand = true;
			}
		}

		private float ComputeSettleCreepSpeed(float _distGoal, float _speedKmh)
		{
			float target = Mathf.Clamp(_distGoal * 4f, c_CreepMinKmh, c_CreepMaxKmh);
			if (_speedKmh > target + 0.5f)
				target = Mathf.Max(c_CreepMinKmh, _speedKmh * 0.7f);
			return target;
		}

		private float GetVehicleArcOnPath(Vector3 _position)
		{
			if (!HasTrajectory)
				return 0f;

			m_Trajectory.GetSegmentBounds(m_Index, out int segStart, out int segEnd);
			float bestArc = m_Trajectory.Points[m_Index].ArcLength;
			float bestDist = float.MaxValue;
			for (int i = segStart; i < segEnd && i + 1 < m_Trajectory.PointCount; i++)
			{
				Vector3 a = m_Trajectory.Points[i].Position;
				Vector3 b = m_Trajectory.Points[i + 1].Position;
				Vector3 ab = b - a;
				ab.y = 0f;
				float lenSq = ab.sqrMagnitude;
				if (lenSq < 1e-6f)
					continue;

				float t = Mathf.Clamp01(Vector3.Dot(_position - a, ab) / lenSq);
				Vector3 proj = a + ab * t;
				float d = BicycleKinematics.FlatDistance(_position, proj);
				if (d >= bestDist)
					continue;

				bestDist = d;
				float span = m_Trajectory.Points[i + 1].ArcLength - m_Trajectory.Points[i].ArcLength;
				bestArc = m_Trajectory.Points[i].ArcLength + t * span;
			}

			return bestArc;
		}

		private int CapIndexToVehicleLead(int _candidate, float _vehicleArc)
		{
			if (!HasTrajectory || _candidate <= m_Index)
				return _candidate;

			float maxArc = _vehicleArc + c_IndexLeadMaxM;
			while (_candidate > m_Index && m_Trajectory.Points[_candidate].ArcLength > maxArc)
				_candidate--;
			return _candidate;
		}

		private Vector3 GetLookAheadPoint(Vector3 _position, float _lookAhead)
		{
			if (!HasTrajectory)
				return _position;

			float baseArc = m_Trajectory.Points[m_Index].ArcLength;
			float target = baseArc + _lookAhead;
			// Hard clamp to current gear segment end — never chase the next maneuver.
			int segEnd = Mathf.Clamp(m_SegmentEndIndex, m_Index, m_Trajectory.PointCount - 1);
			float segmentEndArc = m_Trajectory.Points[segEnd].ArcLength;
			target = Mathf.Min(target, segmentEndArc);

			for (int i = m_Index; i < segEnd; i++)
			{
				TrajectoryPoint a = m_Trajectory.Points[i];
				TrajectoryPoint b = m_Trajectory.Points[i + 1];
				if (b.Gear != a.Gear)
					return a.Position;
				if (b.ArcLength < target)
					continue;
				float span = b.ArcLength - a.ArcLength;
				if (span < 1e-4f)
					return b.Position;
				float t = Mathf.Clamp01((target - a.ArcLength) / span);
				return Vector3.Lerp(a.Position, b.Position, t);
			}

			return m_Trajectory.Points[segEnd].Position;
		}

		private float ComputeSignedCrossTrack(Vector3 _position, float _yaw)
		{
			if (!HasTrajectory)
				return 0f;

			Vector3 nearest = ProjectOnPath(_position);
			Vector3 err = _position - nearest;
			err.y = 0f;

			bool reverse = m_Trajectory.GearAt(m_Index) == TrajectoryGear.Reverse;
			float motionYaw = reverse ? _yaw + 180f : _yaw;
			Vector3 motionFwd = BicycleKinematics.YawToForward(motionYaw);
			return Vector3.Dot(new Vector3(motionFwd.z, 0f, -motionFwd.x), err);
		}

		private Vector3 ProjectOnPath(Vector3 _position)
		{
			if (!HasTrajectory)
				return _position;

			m_Trajectory.GetSegmentBounds(m_Index, out int segStart, out int segEnd);
			int best = m_Index;
			float bestDist = float.MaxValue;
			int start = Mathf.Max(segStart, m_Index - 4);
			int end = Mathf.Min(segEnd, m_Index + 8);
			for (int i = start; i <= end; i++)
			{
				float d = BicycleKinematics.FlatDistance(_position, m_Trajectory.Points[i].Position);
				if (d < bestDist)
				{
					bestDist = d;
					best = i;
				}
			}

			return m_Trajectory.Points[best].Position;
		}
	}
}
