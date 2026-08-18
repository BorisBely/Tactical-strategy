using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Hybrid A* / state-lattice planner that finds a kinematically feasible
	/// trajectory to a GoalPose (position, or position+heading).
	/// </summary>
	public sealed class LocalPosePlanner
	{
		public static bool DebugLog = false;

		public struct PlanStats
		{
			public int Expanded;
			public int Generated;
			public float BestCost;
			public float BestPosError;
			public float BestYawError;
			public int SnapshotRays;
			public int CollisionQueries;
			public int PrimitiveCollisionQueries;
			public int TrajectoryCollisionQueries;
			public int CandidatesTried;
			public int CandidatesGenerated;
			public int RejectedInvalidGeometry;
			public int RejectedSanitary;
			public int RejectedLengthBudget;
			public int RejectedCollision;
			public int RejectedTolerance;
			public int AnalyticShots;
			public int RsFormulasGenerated;
			public int RsIntegrationRejected;
			public int RsEndpointRejected;
			public int RsSanitationRejected;
			public int RsValidCandidates;
			public float PlanDurationMs;
			public bool BudgetTerminated;
			public string BudgetReason;
			public string Phase;
			public int StepIndex;
			public int AnalyticGenerated;
			public int AnalyticValid;
			public string Reason;
			public string TopCandidatesSummary;
		}

		internal struct SessionStats
		{
			public int CandidatesGenerated;
			public int CandidatesTried;
			public int RejectedInvalidGeometry;
			public int RejectedSanitary;
			public int RejectedLengthBudget;
			public int RejectedCollision;
			public int RejectedTolerance;
			public int RsFormulasGenerated;
			public int RsIntegrationRejected;
			public int RsEndpointRejected;
			public int RsSanitationRejected;
			public int RsValidCandidates;
			public string TopCandidatesSummary;
			public string Reason;
		}

		public PlanStats LastStats { get; private set; }

		private readonly SweptVolumeChecker m_Collision = new SweptVolumeChecker();

		// Must be finer than the close-goal primitive (step * 0.55, minimum 0.1925 m),
		// otherwise every curved successor quantizes back onto its parent state.
		private const float c_XyResolution = 0.2f;
		private const float c_YawBins = 64f;
		private const float c_ReversePenalty = 1.35f;
		private const float c_GearSwitchPenalty = 2.5f;
		private const float c_SteerChangePenalty = 0.35f;
		private const float c_MaxExpansions = 4500f;
		private const float c_PlanPositionTolerance = 0.25f;
		/// <summary>Endpoint refine / accept band — match practical arrival (~0.45), not 10cm.</summary>
		private const float c_ExecutionPositionTolerance = 0.35f;
		private const int c_MaxPrimitiveCollisionQueries = 1600;
		private const int c_ShortGoalPrimitiveCollisionQueries = 700;
		private const int c_AnalyticQueryReserve = 600;
		private const float c_MaxDetourRatio = 2.5f;
		/// <summary>
		/// Plan arcs slightly larger than the hard min radius so Pure Pursuit / steer lag
		/// can still track the path instead of saturating and losing heading after turn 1.
		/// </summary>
		private const float c_TrackableTurnRadiusScale = 1.15f;
		private const int c_MaxTrajectoryCollisionQueries = 800;
		private const int c_AnalyticShotStride = 20;
		private const int c_MaxAnalyticShots = 28;
		private const float c_RuntimeSliceBudgetMs = 1.5f;
		private const float c_RuntimeTotalPlanBudgetMs = 350f;
		private const int c_MaxExpansionsPerStep = 24;
		private const int c_MaxPrimitiveQueriesPerSlice = 120;

		internal sealed class Node
		{
			public Vector3 Position;
			public float Yaw;
			public TrajectoryGear Gear;
			public float G;
			public float F;
			public float Curvature;
			public float ArcLength;
			public Node Parent;
			public int Id;
		}

		private sealed class NodeComparer : IComparer<Node>
		{
			public int Compare(Node a, Node b)
			{
				int c = a.F.CompareTo(b.F);
				if (c != 0) return c;
				return a.Id.CompareTo(b.Id);
			}
		}

		public static float RuntimeSliceBudgetMs => c_RuntimeSliceBudgetMs;
		public static float RuntimeTotalPlanBudgetMs => c_RuntimeTotalPlanBudgetMs;
		// Legacy alias used by older call sites.
		public static float RuntimeMaxPlanDurationMs => c_RuntimeSliceBudgetMs;

		public LocalPlanningSession CreateSession(
			Vector3 _startPos,
			float _startYaw,
			GoalPose _goal,
			VehicleKinematicsProfile _profile,
			PlanningObstacleSnapshot _snapshot,
			bool _allowReverse,
			float _stepLength,
			float _maxPlanDurationMs)
		{
			float wb = _profile != null ? _profile.WheelBase : 3.5f;
			VehicleTrajectory.DensifyWheelBase = Mathf.Max(0.5f, wb);
			BicycleKinematics.ConfigureSteerRampFromProfile(_profile, BicycleKinematics.SteerRateDegPerSec);
			float hardRadius = _profile != null ? _profile.EffectiveTurnRadius : 6.5f;
			return new LocalPlanningSession
			{
				StartPos = _startPos,
				StartYaw = _startYaw,
				Goal = _goal,
				Profile = _profile,
				Snapshot = _snapshot,
				AllowReverse = _allowReverse,
				StepLength = _stepLength,
				MaxPlanDurationMs = _maxPlanDurationMs,
				TurnRadius = hardRadius * c_TrackableTurnRadiusScale,
				WheelBase = wb,
				Step = Mathf.Clamp(_stepLength, 0.35f, 1.0f),
				StartDist = BicycleKinematics.FlatDistance(_startPos, _goal.Position),
				Session = default,
				CurrentPhase = LocalPlanningSession.Phase.Init
			};
		}

		public PlanStepResult StepPlan(LocalPlanningSession _session, float _maxStepMs)
		{
			if (_session == null)
				return PlanStepResult.Failed(0f, 0);

			var stepSw = Stopwatch.StartNew();
			_session.StepIndex++;
			VehicleTrajectory.DensifyWheelBase = Mathf.Max(0.5f, _session.WheelBase);

			if (_session.StepIndex == 1)
			{
				m_Collision.ResetCounters();
				LastStats = default;
				_session.MaxSliceMs = 0f;
				_session.AccumulatedCpuMs = 0f;
			}

			// ≤25ms totals: one-shot cheap analytic + settle. Avoids slice/lattice churn and
			// "budget trips before first eval" races that drop straight-rev (cand=1 tried=0).
			if (_session.MaxPlanDurationMs > 0f && _session.MaxPlanDurationMs <= 25f)
				return StepPlanTightBudget(_session, stepSw);

			bool HasSessionBudget()
			{
				if (_session.MaxPlanDurationMs > 0f &&
				    GetPlanCpuMs(_session, stepSw) >= _session.MaxPlanDurationMs)
				{
					_session.BudgetTerminated = true;
					if (string.IsNullOrEmpty(_session.BudgetReason))
						_session.BudgetReason = $"total CPU budget {_session.MaxPlanDurationMs:F0}ms";
					return false;
				}
				return true;
			}

			bool HasStepBudget() =>
				HasSessionBudget() &&
				(_maxStepMs >= float.MaxValue / 4f ||
				 stepSw.Elapsed.TotalMilliseconds < _maxStepMs);

			while (HasStepBudget())
			{
				switch (_session.CurrentPhase)
				{
					case LocalPlanningSession.Phase.Init:
						if (!AdvanceInitPhase(_session))
							return FinishStep(_session, PlanStepResult.Failed(stepSw.ElapsedMilliseconds, _session.StepIndex), stepSw);
						break;

					case LocalPlanningSession.Phase.Analytic:
					{
						var analyticResult = AdvanceAnalyticPhase(_session, HasStepBudget);
						if (analyticResult != null)
							return FinishStep(_session, PlanStepResult.Ready(analyticResult, stepSw.ElapsedMilliseconds, _session.StepIndex), stepSw);
						if (!_session.AnalyticComplete)
						{
							if (!HasStepBudget())
								return FinishStep(_session, PlanStepResult.Pending(stepSw.ElapsedMilliseconds, _session.StepIndex), stepSw);
							break;
						}
						// Under tight CPU headroom skip lattice — cheap analytic already ran.
						_session.CurrentPhase = CanAffordExpensiveAnalytic(_session)
							? LocalPlanningSession.Phase.Lattice
							: LocalPlanningSession.Phase.PostProcess;
						break;
					}

					case LocalPlanningSession.Phase.Lattice:
					{
						bool latticeDone = AdvanceLatticePhase(_session, HasStepBudget);
						if (!HasStepBudget() && !latticeDone)
						{
							if (_session.BudgetTerminated)
							{
								_session.CurrentPhase = LocalPlanningSession.Phase.PostProcess;
								break;
							}
							return FinishStep(_session, PlanStepResult.Pending(stepSw.ElapsedMilliseconds, _session.StepIndex), stepSw);
						}
						if (!latticeDone)
							break;
						_session.CurrentPhase = LocalPlanningSession.Phase.PostProcess;
						break;
					}

					case LocalPlanningSession.Phase.PostProcess:
					{
						var final = FinalizePlanning(_session);
						_session.CurrentPhase = LocalPlanningSession.Phase.Complete;
						if (final != null && final.IsValid)
							return FinishStep(_session, PlanStepResult.Ready(final, stepSw.ElapsedMilliseconds, _session.StepIndex), stepSw);
						return FinishStep(_session, PlanStepResult.Failed(stepSw.ElapsedMilliseconds, _session.StepIndex), stepSw);
					}

					default:
						return FinishStep(_session, PlanStepResult.Failed(stepSw.ElapsedMilliseconds, _session.StepIndex), stepSw);
				}
			}

			if (_session.BudgetTerminated && _session.CurrentPhase != LocalPlanningSession.Phase.Complete)
			{
				_session.CurrentPhase = LocalPlanningSession.Phase.PostProcess;
				var final = FinalizePlanning(_session);
				_session.CurrentPhase = LocalPlanningSession.Phase.Complete;
				if (final != null && final.IsValid)
					return FinishStep(_session, PlanStepResult.Ready(final, stepSw.ElapsedMilliseconds, _session.StepIndex), stepSw);
				return FinishStep(_session, PlanStepResult.Failed(stepSw.ElapsedMilliseconds, _session.StepIndex), stepSw);
			}

			return FinishStep(_session, PlanStepResult.Pending(stepSw.ElapsedMilliseconds, _session.StepIndex), stepSw);
		}

		private PlanStepResult FinishStep(LocalPlanningSession _session, PlanStepResult _result, Stopwatch _stepSw = null)
		{
			if (_stepSw != null && _stepSw.IsRunning)
			{
				float sliceMs = (float)_stepSw.Elapsed.TotalMilliseconds;
				_session.LastSliceMs = sliceMs;
				_session.MaxSliceMs = Mathf.Max(_session.MaxSliceMs, sliceMs);
				_session.AccumulatedCpuMs += sliceMs;
				_stepSw.Stop();
			}

			RefreshStatsDuration(_session);

			if (_result.Status != PlanStepStatus.Pending)
				_session.CurrentPhase = LocalPlanningSession.Phase.Complete;
			return _result;
		}

		/// <summary>
		/// Force post-process/finalize after wall timeout. Does not restart the session.
		/// </summary>
		public PlanStepResult ForceFinalize(LocalPlanningSession _session, string _budgetReason = "wall timeout")
		{
			if (_session == null)
				return PlanStepResult.Failed(0f, 0);

			_session.BudgetTerminated = true;
			_session.BudgetReason = _budgetReason;

			var stepSw = Stopwatch.StartNew();
			_session.CurrentPhase = LocalPlanningSession.Phase.PostProcess;
			VehicleTrajectory final = FinalizePlanning(_session);
			_session.CurrentPhase = LocalPlanningSession.Phase.Complete;
			float stepMs = (float)stepSw.Elapsed.TotalMilliseconds;
			_session.AccumulatedCpuMs += stepMs;
			_session.LastSliceMs = stepMs;
			_session.MaxSliceMs = Mathf.Max(_session.MaxSliceMs, stepMs);
			RefreshStatsDuration(_session);

			if (final != null && final.IsValid)
				return PlanStepResult.Ready(final, stepMs, _session.StepIndex);
			return PlanStepResult.Failed(stepMs, _session.StepIndex);
		}

		private void RefreshStatsDuration(LocalPlanningSession _session)
		{
			var stats = LastStats;
			stats.PlanDurationMs = _session.AccumulatedCpuMs;
			stats.BudgetReason = _session.BudgetReason;
			stats.Phase = _session.PhaseName;
			stats.StepIndex = _session.StepIndex;
			stats.AnalyticGenerated = _session.AnalyticCandidateCount;
			stats.AnalyticValid = _session.AnalyticValidCount;
			stats.BudgetTerminated = _session.BudgetTerminated || stats.BudgetTerminated;
			LastStats = stats;
		}

		private static float GetPlanCpuMs(LocalPlanningSession _session, Stopwatch _stepSw = null)
		{
			float ms = _session != null ? _session.AccumulatedCpuMs : 0f;
			if (_stepSw != null && _stepSw.IsRunning)
				ms += (float)_stepSw.Elapsed.TotalMilliseconds;
			return ms;
		}

		/// <summary>
		/// Completes planning in a single StepPlan for tiny totals (≤25ms): cheap families only,
		/// no lattice/heavy RS, and always evaluate already-built candidates before failing.
		/// </summary>
		private PlanStepResult StepPlanTightBudget(LocalPlanningSession _session, Stopwatch _stepSw)
		{
			if (_session.CurrentPhase == LocalPlanningSession.Phase.Complete)
			{
				if (_session.ResultTrajectory != null && _session.ResultTrajectory.IsValid)
					return FinishStep(_session, PlanStepResult.Ready(_session.ResultTrajectory, _stepSw.ElapsedMilliseconds, _session.StepIndex), _stepSw);
				return FinishStep(_session, PlanStepResult.Failed(_stepSw.ElapsedMilliseconds, _session.StepIndex), _stepSw);
			}

			if (_session.CurrentPhase == LocalPlanningSession.Phase.Init)
			{
				if (!AdvanceInitPhase(_session))
					return FinishStep(_session, PlanStepResult.Failed(_stepSw.ElapsedMilliseconds, _session.StepIndex), _stepSw);
			}

			if (_session.ResultTrajectory != null && _session.ResultTrajectory.IsValid)
			{
				_session.CurrentPhase = LocalPlanningSession.Phase.Complete;
				return FinishStep(_session, PlanStepResult.Ready(_session.ResultTrajectory, _stepSw.ElapsedMilliseconds, _session.StepIndex), _stepSw);
			}

			if (_session.CurrentPhase == LocalPlanningSession.Phase.Init ||
			    _session.CurrentPhase == LocalPlanningSession.Phase.Analytic)
			{
				_session.CurrentPhase = LocalPlanningSession.Phase.Analytic;

				if (!_session.AnalyticCandidatesBuilt)
				{
					var stats = _session.Session;
					// Null budget callback: always emit cheap candidates (straight-rev etc.).
					// tightCpu inside AddCheap still skips Dubins/CSC/two-stage pose sweeps.
					bool openField = _session.Snapshot == null || !_session.Snapshot.IsValid;
					_session.AnalyticCandidates = BuildCheapAnalyticCandidateList(
						_session.StartPos, _session.StartYaw, _session.Goal, _session.TurnRadius,
						_session.WheelBase, _session.AllowReverse, ref stats, null, _session.MaxPlanDurationMs,
						openField);
					_session.Session = stats;
					_session.AnalyticCandidatesBuilt = true;
					_session.AnalyticHeavyPending = false;
					_session.AnalyticEvalIndex = 0;
					_session.AnalyticValid = new List<(VehicleTrajectory, float, float)>(8);
					_session.AnalyticShots++;
				}

				float dist = BicycleKinematics.FlatDistance(_session.StartPos, _session.Goal.Position);
				float posTol = PlanPosTolerance(_session.Goal);
				var evalStats = _session.Session;

				// Evaluate every built candidate; lists are tiny under tightCpu.
				while (_session.AnalyticEvalIndex < (_session.AnalyticCandidates?.Count ?? 0))
				{
					var c = _session.AnalyticCandidates[_session.AnalyticEvalIndex++];
					if (c == null || !c.IsValid)
					{
						evalStats.RejectedInvalidGeometry++;
						continue;
					}

					if (!TrajectoryKinematicsValidator.Validate(c, _session.TurnRadius, out _))
					{
						evalStats.RejectedInvalidGeometry++;
						continue;
					}

					evalStats.CandidatesTried++;
					if (!ReedsSheppPathBuilder.IsSanitary(c, dist, _session.TurnRadius))
					{
						evalStats.RejectedSanitary++;
						continue;
					}

					TrajectoryPoint end = c.Points[c.PointCount - 1];
					float posErr = BicycleKinematics.FlatDistance(end.Position, _session.Goal.Position);
					if (posErr > posTol)
					{
						evalStats.RejectedTolerance++;
						continue;
					}

					if (_session.Goal.RequiresPosePlanning &&
					    Mathf.Abs(Mathf.DeltaAngle(end.YawDegrees, _session.Goal.YawDegrees)) >
					    _session.Goal.HeadingToleranceDeg)
					{
						evalStats.RejectedTolerance++;
						continue;
					}

					float selectCost = ComputeSelectionCost(c, _session.StartPos, _session.StartYaw, _session.Goal);
					_session.AnalyticValid.Add((c, selectCost, posErr));
				}

				_session.Session = evalStats;
				_session.AnalyticComplete = true;

				VehicleTrajectory best = SelectBestAnalyticCandidate(
					_session.AnalyticValid, dist, _session.TurnRadius,
					_session.StartPos, _session.StartYaw, _session.Goal, ref evalStats, true);
				_session.Session = evalStats;

				if (best != null && AcceptTrajectory(best, _session.TurnRadius, ref evalStats))
				{
					best = EnsureExecutionEndpoint(
						best, _session.Goal, _session.TurnRadius, _session.WheelBase, ref evalStats);
					if (best != null && AcceptTrajectory(best, _session.TurnRadius, ref evalStats))
					{
						_session.Session = evalStats;
						_session.ResultTrajectory = best;
						_session.CurrentPhase = LocalPlanningSession.Phase.Complete;
						LastStats = BuildStats(evalStats, 0, best.PointCount, best.Cost, 0f, 0f,
							_session.Snapshot, _session.AnalyticShots, (float)_session.AccumulatedCpuMs,
							false, best.DebugReason);
						return FinishStep(_session, PlanStepResult.Ready(best, _stepSw.ElapsedMilliseconds, _session.StepIndex), _stepSw);
					}
				}
			}

			_session.BudgetTerminated = true;
			if (string.IsNullOrEmpty(_session.BudgetReason))
				_session.BudgetReason = $"total CPU budget {_session.MaxPlanDurationMs:F0}ms";
			_session.AnalyticComplete = true;
			_session.CurrentPhase = LocalPlanningSession.Phase.Complete;

			float bestPos = _session.BestAny != null
				? BicycleKinematics.FlatDistance(_session.BestAny.Position, _session.Goal.Position)
				: float.MaxValue;
			string failReason = FormatFailReason(_session, _session.Session, bestPos);
			LastStats = BuildStats(_session.Session, _session.Expanded, _session.Generated, 0f, bestPos, 0f,
				_session.Snapshot, _session.AnalyticShots, (float)_session.AccumulatedCpuMs, true, failReason);
			return FinishStep(_session, PlanStepResult.Failed(_stepSw.ElapsedMilliseconds, _session.StepIndex), _stepSw);
		}

		private bool AdvanceInitPhase(LocalPlanningSession _s)
		{
			if (TrajectoryKinematicsValidator.IsAtGoal(_s.StartPos, _s.StartYaw, _s.Goal))
			{
				var trivial = BuildTrivial(_s.StartPos, _s.StartYaw, _s.Goal);
				if (trivial == null || !trivial.IsValid)
				{
					LastStats = BuildStats(_s.Session, 0, 0, 0f, _s.StartDist, 0f, _s.Snapshot,
						0, (float)_s.AccumulatedCpuMs, false, "already at goal");
					_s.Session.Reason = "already at goal";
					_s.CurrentPhase = LocalPlanningSession.Phase.Complete;
					return false;
				}

				LastStats = BuildStats(_s.Session, 0, 1, 0f, _s.StartDist, 0f, _s.Snapshot,
					0, (float)_s.AccumulatedCpuMs, false, "already at goal");
				_s.ResultTrajectory = trivial;
				_s.LatticeInitialized = true;
				_s.PostProcessStarted = true;
				_s.BestAny = null;
				_s.CurrentPhase = LocalPlanningSession.Phase.PostProcess;
				_s.Session.Reason = "already at goal";
				return true;
			}

			_s.CurrentPhase = LocalPlanningSession.Phase.Analytic;
			return true;
		}

		private VehicleTrajectory AdvanceAnalyticPhase(LocalPlanningSession _s, System.Func<bool> _hasBudget)
		{
			if (_s.AnalyticComplete)
				return null;

			if (!_s.AnalyticCandidatesBuilt)
			{
				if (_s.MaxPlanDurationMs > 0f &&
				    _s.AccumulatedCpuMs >= _s.MaxPlanDurationMs)
				{
					_s.BudgetTerminated = true;
					_s.AnalyticComplete = true;
					return null;
				}

				// Runtime slices are ~1.5ms. If the step budget is already gone (Init ate it),
				// do NOT mark candidates built — otherwise analyticGen stays 0 forever.
				if (_hasBudget != null && !_hasBudget())
					return null;

				var stats = _s.Session;
				// Pass 1: cheap families must finish even if they overrun the slice. Skipping
				// mid-build left Play with analyticGen=0 for 2m side/oblique (lattice-only FAIL).
				bool openField = _s.Snapshot == null || !_s.Snapshot.IsValid;
				_s.AnalyticCandidates = BuildCheapAnalyticCandidateList(
					_s.StartPos, _s.StartYaw, _s.Goal, _s.TurnRadius, _s.WheelBase,
					_s.AllowReverse, ref stats, null, _s.MaxPlanDurationMs, openField);
				_s.Session = stats;
				_s.AnalyticCandidatesBuilt = true;
				_s.AnalyticHeavyPending = CanAffordExpensiveAnalytic(_s);
				_s.AnalyticEvalIndex = 0;
				_s.AnalyticValid = new List<(VehicleTrajectory, float, float)>(16);
				_s.AnalyticShots++;
			}

			float dist = BicycleKinematics.FlatDistance(_s.StartPos, _s.Goal.Position);
			float posTol = PlanPosTolerance(_s.Goal);
			var session = _s.Session;

			VehicleTrajectory evalHit = EvaluateAnalyticCandidates(_s, dist, posTol, ref session, _hasBudget, out bool evalPaused);
			_s.Session = session;
			if (evalHit != null)
				return evalHit;
			if (evalPaused)
				return null;
			// Budget abort already flushed or gave up — do not run SelectBest/Dubins after expiry.
			if (_s.AnalyticComplete)
				return null;

			// Pass 2: only if cheap families produced nothing usable.
			if (_s.AnalyticHeavyPending &&
			    (_s.AnalyticValid == null || _s.AnalyticValid.Count == 0) &&
			    CanAffordExpensiveAnalytic(_s))
			{
				if (!_hasBudget())
					return null;

				bool heavyDone = AppendHeavyAnalyticCandidates(_s, ref session, _hasBudget);
				_s.Session = session;
				if (!heavyDone)
					return null;

				_s.AnalyticHeavyPending = false;
				evalHit = EvaluateAnalyticCandidates(_s, dist, posTol, ref session, _hasBudget, out evalPaused);
				_s.Session = session;
				if (evalHit != null)
					return evalHit;
				if (evalPaused)
					return null;
				if (_s.AnalyticComplete)
					return null;
			}
			else if (!_s.AnalyticHeavyPending ||
			         (_s.AnalyticValid != null && _s.AnalyticValid.Count > 0))
			{
				_s.AnalyticHeavyPending = false;
			}

			_s.AnalyticComplete = true;
			_s.Session = session;

			VehicleTrajectory best = SelectBestAnalyticCandidate(
				_s.AnalyticValid, dist, _s.TurnRadius, _s.StartPos, _s.StartYaw, _s.Goal, ref session, true);
			_s.Session = session;

			if (best != null && AcceptTrajectory(best, _s.TurnRadius, ref session))
			{
				best = EnsureExecutionEndpoint(best, _s.Goal, _s.TurnRadius, _s.WheelBase, ref session);
				if (best != null && AcceptTrajectory(best, _s.TurnRadius, ref session))
				{
					float alignBest = ReedsSheppPathBuilder.GetTravelAlignment(
						_s.StartPos, _s.StartYaw, _s.Goal.Position);
					bool frontOblique = !_s.Goal.RequiresPosePlanning &&
					                    alignBest > 12f && alignBest < 70f;
					bool reverseFirst = best.PointCount > 0 &&
					                    best.Points[0].Gear == TrajectoryGear.Reverse;
					// Keep reverse in AnalyticValid for FinalizePlanning, but let lattice
					// hunt for forward-first while CPU remains (unlimited EditMode / early Play).
					if (frontOblique && reverseFirst && CanAffordExpensiveAnalytic(_s))
					{
						_s.Session = session;
						return null;
					}

					_s.Session = session;
					LastStats = BuildStats(_s.Session, 0, best.PointCount, best.Cost, 0f, 0f,
						_s.Snapshot, _s.AnalyticShots, (float)_s.AccumulatedCpuMs, false, best.DebugReason);
					if (DebugLog)
						VehicleFileLog.WriteActive($"[LocalPosePlanner] analytic OK len={best.TotalLength:F1}m segs={best.GearSegmentCount} reason={best.DebugReason}");
					return best;
				}
			}

			// Last analytic resort for inside-circle front-oblique before falling into lattice.
			float align = ReedsSheppPathBuilder.GetTravelAlignment(_s.StartPos, _s.StartYaw, _s.Goal.Position);
			if (!_s.Goal.RequiresPosePlanning && align > 12f && align < 70f)
			{
				VehicleTrajectory dub = ReedsSheppPathBuilder.TryBuildBestDubinsToPosition(
					_s.StartPos, _s.StartYaw, _s.Goal.Position, _s.TurnRadius, _s.WheelBase,
					_s.Goal.PositionTolerance);
				if (dub != null && dub.IsValid && AcceptTrajectory(dub, _s.TurnRadius, ref session))
				{
					_s.Session = session;
					LastStats = BuildStats(session, 0, dub.PointCount, dub.Cost, 0f, 0f,
						_s.Snapshot, _s.AnalyticShots, (float)_s.AccumulatedCpuMs, false, dub.DebugReason);
					return dub;
				}
			}

			_s.Session = session;
			return null;
		}

		private VehicleTrajectory EvaluateAnalyticCandidates(
			LocalPlanningSession _s,
			float _dist,
			float _posTol,
			ref SessionStats _session,
			System.Func<bool> _hasBudget,
			out bool _paused)
		{
			_paused = false;
			if (_s.AnalyticCandidates == null)
				return null;

			while (_s.AnalyticEvalIndex < _s.AnalyticCandidates.Count)
			{
				if (!_hasBudget())
				{
					// Slice-only pause: resume next StepPlan. Do not treat as total-budget grace.
					if (!_s.BudgetTerminated)
					{
						_paused = true;
						return null;
					}

					// Total CPU expired — flush already-validated picks, then stop immediately.
					if (_s.AnalyticValid != null && _s.AnalyticValid.Count > 0)
					{
						VehicleTrajectory flushed = SelectBestAnalyticCandidate(
							_s.AnalyticValid, _dist, _s.TurnRadius, _s.StartPos, _s.StartYaw, _s.Goal,
							ref _session, false);
						if (flushed != null && AcceptTrajectory(flushed, _s.TurnRadius, ref _session))
						{
							// Skip EnsureExecutionEndpoint after expiry — refine can dominate the overshoot.
							_s.AnalyticComplete = true;
							_s.AnalyticHeavyPending = false;
							_s.Session = _session;
							LastStats = BuildStats(_session, 0, flushed.PointCount, flushed.Cost, 0f, 0f,
								_s.Snapshot, _s.AnalyticShots,
								(float)_s.AccumulatedCpuMs, true, flushed.DebugReason);
							return flushed;
						}
					}

					_s.AnalyticComplete = true;
					_s.AnalyticHeavyPending = false;
					return null;
				}

				var c = _s.AnalyticCandidates[_s.AnalyticEvalIndex++];
				if (c == null || !c.IsValid)
				{
					_session.RejectedInvalidGeometry++;
					continue;
				}

				if (!TrajectoryKinematicsValidator.Validate(c, _s.TurnRadius, out string kinReason))
				{
					_session.RejectedInvalidGeometry++;
					if (DebugLog && _session.CandidatesTried < 2)
						VehicleFileLog.WriteActive($"[LocalPosePlanner] reject kinematic {c.DebugReason}: {kinReason}");
					continue;
				}

				_session.CandidatesTried++;
				if (!ReedsSheppPathBuilder.IsSanitary(c, _dist, _s.TurnRadius))
				{
					_session.RejectedSanitary++;
					continue;
				}

				if (_s.Snapshot != null && _s.Snapshot.IsValid &&
				    !m_Collision.IsTrajectorySafe(c, _s.Profile, _s.Snapshot))
				{
					_session.RejectedCollision++;
					continue;
				}

				TrajectoryPoint end = c.Points[c.PointCount - 1];
				float posErr = BicycleKinematics.FlatDistance(end.Position, _s.Goal.Position);
				if (posErr > _posTol)
				{
					_session.RejectedTolerance++;
					continue;
				}

				if (_s.Goal.RequiresPosePlanning &&
				    Mathf.Abs(Mathf.DeltaAngle(end.YawDegrees, _s.Goal.YawDegrees)) > _s.Goal.HeadingToleranceDeg)
				{
					_session.RejectedTolerance++;
					continue;
				}

				// Pose RS that starts by diverging hard from the goal is rarely recoverable.
				if (_s.Goal.RequiresPosePlanning &&
				    c.PointCount > 4 &&
				    _s.StartDist < 8f)
				{
					float midDist = BicycleKinematics.FlatDistance(
						c.Points[c.PointCount / 2].Position, _s.Goal.Position);
					if (midDist > _s.StartDist * 2.2f + 2f)
					{
						_session.RejectedSanitary++;
						continue;
					}
				}

				float selectCost = ComputeSelectionCost(c, _s.StartPos, _s.StartYaw, _s.Goal);
				_s.AnalyticValid.Add((c, selectCost, posErr));
			}

			return null;
		}

		private bool AdvanceLatticePhase(LocalPlanningSession _s, System.Func<bool> _hasBudget)
		{
			if (!_s.LatticeInitialized)
			{
				_s.Open = new SortedSet<Node>(new NodeComparer());
				_s.BestG = new Dictionary<long, float>(4096);
				_s.NodeId = 0;
				_s.Expanded = 0;
				_s.Generated = 0;
				_s.Start = new Node
				{
					Position = _s.StartPos,
					Yaw = BicycleKinematics.NormalizeYaw(_s.StartYaw),
					Gear = TrajectoryGear.Forward,
					G = 0f,
					F = BicycleKinematics.HeuristicDistance(
						_s.StartPos, _s.StartYaw, _s.Goal.Position,
						GoalUsesYaw(_s.Goal) ? _s.Goal.YawDegrees : (float?)null, _s.TurnRadius),
					Curvature = 0f,
					ArcLength = 0f,
					Parent = null,
					Id = _s.NodeId++
				};
				_s.Open.Add(_s.Start);
				_s.BestG[StateKey(_s.Start.Position, _s.Start.Yaw, _s.Start.Gear, _s.Start.Curvature)] = 0f;
				float travelAlign = ReedsSheppPathBuilder.GetTravelAlignment(
					_s.StartPos, _s.StartYaw, _s.Goal.Position);
				if (_s.AllowReverse && travelAlign >= 100f)
				{
					var revStart = new Node
					{
						Position = _s.StartPos,
						Yaw = _s.Start.Yaw,
						Gear = TrajectoryGear.Reverse,
						G = 0f,
						F = _s.Start.F,
						Curvature = 0f,
						ArcLength = 0f,
						Parent = null,
						Id = _s.NodeId++
					};
					_s.Open.Add(revStart);
					_s.BestG[StateKey(revStart.Position, revStart.Yaw, revStart.Gear, revStart.Curvature)] = 0f;
				}
				_s.BestGoal = null;
				_s.DeferredForwardGoal = null;
				_s.BestAny = _s.Start;
				_s.BestAnyScore = ScoreToGoal(_s.Start, _s.Goal);
				_s.LatticeInitialized = true;
			}

			float[] curvatureFractions = BicycleKinematics.DefaultCurvatureFractions;
			int expansionsThisStep = 0;
			int primitiveQueriesAtStepStart = m_Collision.PrimitiveQueries;

			while (_s.Open.Count > 0 && _s.Expanded < c_MaxExpansions && expansionsThisStep < c_MaxExpansionsPerStep)
			{
				if (!_hasBudget())
					return false;

				if (m_Collision.PrimitiveQueries - primitiveQueriesAtStepStart >= c_MaxPrimitiveQueriesPerSlice)
					return false;

				int primCap = _s.StartDist <= 5f
					? c_ShortGoalPrimitiveCollisionQueries
					: c_MaxPrimitiveCollisionQueries;
				int trajCap = _s.StartDist <= 5f
					? c_MaxTrajectoryCollisionQueries / 2
					: c_MaxTrajectoryCollisionQueries;
				if (m_Collision.PrimitiveQueries >= primCap ||
				    m_Collision.TrajectoryQueries >= trajCap)
				{
					_s.BudgetTerminated = true;
					_s.BudgetReason = $"colQ budget (primQ={m_Collision.PrimitiveQueries} trajQ={m_Collision.TrajectoryQueries})";
					return true;
				}

				Node current = _s.Open.Min;
				_s.Open.Remove(current);
				_s.Expanded++;
				expansionsThisStep++;

				float posErr = BicycleKinematics.FlatDistance(current.Position, _s.Goal.Position);
				float yawErr = _s.Goal.RequiresPosePlanning
					? Mathf.Abs(Mathf.DeltaAngle(current.Yaw, _s.Goal.YawDegrees))
					: 0f;

				bool bestAnyImproved = false;
				float anyScore = ScoreToGoal(current, _s.Goal);
				if (anyScore < _s.BestAnyScore)
				{
					bestAnyImproved = true;
					_s.BestAnyScore = anyScore;
					_s.BestAny = current;
				}

				if (posErr <= PlanPosTolerance(_s.Goal) &&
				    (!_s.Goal.RequiresPosePlanning || yawErr <= _s.Goal.HeadingToleranceDeg))
				{
					bool preferReverse = _s.AllowReverse && !_s.Goal.RequiresPosePlanning &&
					                     ReedsSheppPathBuilder.GetTravelAlignment(
						                     _s.StartPos, _s.StartYaw, _s.Goal.Position) >= 100f;
					if (preferReverse && GetFirstGearFromNode(current) == TrajectoryGear.Forward)
					{
						if (_s.DeferredForwardGoal == null || current.G < _s.DeferredForwardGoal.G)
							_s.DeferredForwardGoal = current;
					}
					else
					{
						_s.BestGoal = current;
						return true;
					}
				}

				if (ShouldTryAnalyticShot(bestAnyImproved, _s.Expanded, posErr, _s.TurnRadius, ref _s.AnalyticShots))
				{
					if (!_hasBudget())
						return false;

					if (CanAffordExpensiveAnalytic(_s))
					{
						_s.AnalyticShots++;
						var stats = _s.Session;
						VehicleTrajectory shot = TryAnalyticClose(
							current.Position, current.Yaw, _s.Goal, _s.TurnRadius, _s.WheelBase,
							_s.Profile, _s.Snapshot, _s.AllowReverse, ref stats, false,
							_hasBudget, () => CanAffordExpensiveAnalytic(_s));
						_s.Session = stats;
						if (shot != null && shot.IsValid)
						{
							VehicleTrajectory merged = MergePath(current, shot, _s.WheelBase, _s.StepLength);
							var mergeStats = _s.Session;
							if (merged != null && merged.IsValid &&
							    AcceptTrajectory(merged, _s.TurnRadius, ref mergeStats))
							{
								_s.Session = mergeStats;
								LastStats = BuildStats(_s.Session, _s.Expanded, _s.Generated + shot.PointCount,
									merged.Cost, 0f, 0f, _s.Snapshot, _s.AnalyticShots,
									(float)_s.AccumulatedCpuMs, _s.BudgetTerminated, "hybrid+analytic");
								_s.BestGoal = current;
								_s.ResultTrajectory = merged;
								return true;
							}
							_s.Session = mergeStats;
						}
					}
				}

				float localStep = posErr < 3f ? _s.Step * 0.55f : _s.Step;
				var primitives = BicycleKinematics.Expand(
					current.Position, current.Yaw, _s.WheelBase, _s.TurnRadius,
					localStep, current.ArcLength, _s.AllowReverse, curvatureFractions);

				for (int pi = 0; pi < primitives.Count; pi++)
				{
					var prim = primitives[pi];
					_s.Generated++;
					float g = current.G + prim.Length *
					          (prim.Gear == TrajectoryGear.Reverse ? c_ReversePenalty : 1f);
					if (prim.Gear != current.Gear)
						g += c_GearSwitchPenalty;
					if (Mathf.Abs(prim.Curvature - current.Curvature) > 0.01f)
						g += c_SteerChangePenalty;

					long key = StateKey(prim.EndPosition, prim.EndYawDegrees, prim.Gear, prim.Curvature);
					if (_s.BestG.TryGetValue(key, out float prevG) && prevG <= g)
						continue;

					if (_s.Snapshot != null && _s.Snapshot.IsValid &&
					    !m_Collision.IsPrimitiveSafe(prim, _s.Profile, _s.Snapshot))
						continue;

					_s.BestG[key] = g;
					float h = BicycleKinematics.HeuristicDistance(
						prim.EndPosition, prim.EndYawDegrees, _s.Goal.Position,
						GoalUsesYaw(_s.Goal) ? _s.Goal.YawDegrees : (float?)null, _s.TurnRadius);
					var child = new Node
					{
						Position = prim.EndPosition,
						Yaw = prim.EndYawDegrees,
						Gear = prim.Gear,
						G = g,
						F = g + h,
						Curvature = prim.Curvature,
						ArcLength = current.ArcLength + prim.Length,
						Parent = current,
						Id = _s.NodeId++
					};
					_s.Open.Add(child);
				}
			}

			return _s.Open.Count == 0 || _s.Expanded >= c_MaxExpansions;
		}

		private static TrajectoryGear GetFirstGearFromNode(LocalPosePlanner.Node _goal)
		{
			var stack = new List<LocalPosePlanner.Node>(16);
			for (LocalPosePlanner.Node n = _goal; n != null; n = n.Parent)
				stack.Add(n);
			stack.Reverse();
			return stack.Count > 1 ? stack[1].Gear : stack[0].Gear;
		}

		private VehicleTrajectory FinalizePlanning(LocalPlanningSession _s)
		{
			if (_s.Session.Reason == "already at goal")
			{
				var trivial = BuildTrivial(_s.StartPos, _s.StartYaw, _s.Goal);
				return trivial != null && trivial.IsValid ? trivial : null;
			}

			int analyticShots = _s.AnalyticShots;
			var session = _s.Session;
			GoalPose _goal = _s.Goal;

			// Front-oblique inside the turning circle: prefer pure forward Dubins over lattice "merged".
			float finalizeAlign = ReedsSheppPathBuilder.GetTravelAlignment(
				_s.StartPos, _s.StartYaw, _goal.Position);
			if (!_goal.RequiresPosePlanning && finalizeAlign > 12f && finalizeAlign < 70f)
			{
				VehicleTrajectory dub = ReedsSheppPathBuilder.TryBuildBestDubinsToPosition(
					_s.StartPos, _s.StartYaw, _goal.Position, _s.TurnRadius, _s.WheelBase,
					_goal.PositionTolerance);
				if (dub != null && dub.IsValid && AcceptTrajectory(dub, _s.TurnRadius, ref session))
				{
					LastStats = BuildStats(session, _s.Expanded, dub.PointCount, dub.Cost, 0f, 0f,
						_s.Snapshot, analyticShots, (float)_s.AccumulatedCpuMs, _s.BudgetTerminated,
						dub.DebugReason);
					return dub;
				}
			}

			if (_s.ResultTrajectory != null && _s.ResultTrajectory.IsValid &&
			    AcceptTrajectory(_s.ResultTrajectory, _s.TurnRadius, ref session))
			{
				VehicleTrajectory refinedResult = EnsureExecutionEndpoint(
					_s.ResultTrajectory, _goal, _s.TurnRadius, _s.WheelBase, ref session);
				if (refinedResult != null && AcceptTrajectory(refinedResult, _s.TurnRadius, ref session))
				{
					_s.Session = session;
					return refinedResult;
				}
			}

			if (_s.BestGoal == null && _s.DeferredForwardGoal != null)
				_s.BestGoal = _s.DeferredForwardGoal;

			System.Func<bool> hasBudget = () =>
				_s.MaxPlanDurationMs <= 0f ||
				_s.AccumulatedCpuMs < _s.MaxPlanDurationMs;

			// Hybrid analytic close is expensive — never start it after total budget expiry
			// or without enough remaining headroom for candidate generation.
			bool allowHybridClose = !_s.BudgetTerminated && CanAffordExpensiveAnalytic(_s);
			if (allowHybridClose &&
			    _s.BestGoal == null && _s.BestAny != null && _s.BestAny != _s.Start && hasBudget())
			{
				analyticShots++;
				VehicleTrajectory shot = TryAnalyticClose(
					_s.BestAny.Position, _s.BestAny.Yaw, _goal, _s.TurnRadius, _s.WheelBase,
					_s.Profile, _s.Snapshot, _s.AllowReverse, ref session, false,
					hasBudget, () => CanAffordExpensiveAnalytic(_s));
				if (shot != null && shot.IsValid)
				{
					VehicleTrajectory merged = MergePath(_s.BestAny, shot, _s.WheelBase, _s.StepLength);
					if (merged != null && merged.IsValid &&
					    AcceptTrajectory(merged, _s.TurnRadius, ref session))
					{
						string reason = _s.BudgetTerminated
							? $"hybrid+analytic ({_s.BudgetReason})"
							: "hybrid+analytic";
						LastStats = BuildStats(session, _s.Expanded, _s.Generated + shot.PointCount,
							merged.Cost, 0f, 0f, _s.Snapshot, analyticShots,
							(float)_s.AccumulatedCpuMs, _s.BudgetTerminated, reason);
						return merged;
					}
				}
			}

			if (_s.BestGoal != null)
			{
				VehicleTrajectory traj = Reconstruct(_s.BestGoal, _s.WheelBase, _s.StepLength);
				if (traj != null && traj.IsValid && AcceptTrajectory(traj, _s.TurnRadius, ref session))
				{
					traj = EnsureExecutionEndpoint(traj, _goal, _s.TurnRadius, _s.WheelBase, ref session);
					if (traj != null && AcceptTrajectory(traj, _s.TurnRadius, ref session))
					{
						LastStats = BuildStats(session, _s.Expanded, _s.Generated, _s.BestGoal.G,
							BicycleKinematics.FlatDistance(_s.BestGoal.Position, _goal.Position),
							_goal.RequiresPosePlanning ? Mathf.Abs(Mathf.DeltaAngle(_s.BestGoal.Yaw, _goal.YawDegrees)) : 0f,
							_s.Snapshot, analyticShots, (float)_s.AccumulatedCpuMs, _s.BudgetTerminated, "lattice");
						return traj;
					}
				}
			}

			// Budget often expires during lattice with no BestGoal — keep a valid analytic pick.
			if (_s.AnalyticValid != null && _s.AnalyticValid.Count > 0)
			{
				float distAnalytic = BicycleKinematics.FlatDistance(_s.StartPos, _s.Goal.Position);
				VehicleTrajectory analyticBest = SelectBestAnalyticCandidate(
					_s.AnalyticValid, distAnalytic, _s.TurnRadius, _s.StartPos, _s.StartYaw, _s.Goal,
					ref session, false);
				if (analyticBest != null && AcceptTrajectory(analyticBest, _s.TurnRadius, ref session))
				{
					analyticBest = EnsureExecutionEndpoint(
						analyticBest, _goal, _s.TurnRadius, _s.WheelBase, ref session);
					if (analyticBest != null && AcceptTrajectory(analyticBest, _s.TurnRadius, ref session))
					{
						LastStats = BuildStats(session, _s.Expanded, analyticBest.PointCount, analyticBest.Cost,
							0f, 0f, _s.Snapshot, analyticShots,
							(float)_s.AccumulatedCpuMs, _s.BudgetTerminated, analyticBest.DebugReason);
						return analyticBest;
					}
				}
			}

			float bestPos = _s.BestAny != null
				? BicycleKinematics.FlatDistance(_s.BestAny.Position, _goal.Position)
				: float.MaxValue;
			if (bestPos < _goal.PositionTolerance * 2f && _s.BestAny != _s.Start && !_goal.RequiresPosePlanning)
			{
				VehicleTrajectory near = Reconstruct(_s.BestAny, _s.WheelBase, _s.StepLength);
				if (near.IsValid && m_Collision.IsTrajectorySafe(near, _s.Profile, _s.Snapshot) &&
				    AcceptTrajectory(near, _s.TurnRadius, ref session))
				{
					TrajectoryPoint end = near.Points[near.PointCount - 1];
					float posErr = BicycleKinematics.FlatDistance(end.Position, _goal.Position);
					if (posErr <= _goal.PositionTolerance)
					{
						LastStats = BuildStats(session, _s.Expanded, _s.Generated, _s.BestAny.G, posErr, 0f,
							_s.Snapshot, analyticShots, (float)_s.AccumulatedCpuMs,
							_s.BudgetTerminated, "near-goal lattice");
						return near;
					}
				}
			}

			string failReason = FormatFailReason(_s, session, bestPos);

			float startDist = _s.StartDist;
			float startAlign = ReedsSheppPathBuilder.GetTravelAlignment(_s.StartPos, _s.StartYaw, _goal.Position);
			bool directFrontOrRear = startAlign <= 25f || startAlign >= 155f;
			// Required-heading goals never accept position-only partial stubs.
			if (!_goal.RequiresPosePlanning &&
			    bestPos <= startDist * 0.4f &&
			    _s.BestAny != null &&
			    _s.BestAny != _s.Start)
			{
				VehicleTrajectory partial = ReconstructPartial(_s.BestAny, _s.WheelBase, _s.StepLength);
				bool uselessPartial = partial == null || !partial.IsValid ||
				                      partial.TotalLength < startDist * 0.6f ||
				                      (directFrontOrRear && partial.TotalLength < startDist * 0.7f);
				float endErr = partial != null && partial.IsValid
					? BicycleKinematics.FlatDistance(partial.Points[partial.PointCount - 1].Position, _goal.Position)
					: float.MaxValue;
				bool nearCorrection = endErr <= c_PlanPositionTolerance + 0.15f;
				if (!uselessPartial &&
				    nearCorrection &&
				    m_Collision.IsTrajectorySafe(partial, _s.Profile, _s.Snapshot) &&
				    AcceptTrajectory(partial, _s.TurnRadius, ref session))
				{
					LastStats = BuildStats(session, _s.Expanded, _s.Generated, _s.BestAny.G, bestPos,
						0f, _s.Snapshot, analyticShots,
						(float)_s.AccumulatedCpuMs, _s.BudgetTerminated, "partial");
					return partial;
				}
			}

			if (_s.BestAny == null)
			{
				LastStats = BuildStats(session, _s.Expanded, _s.Generated, 0f, bestPos, 0f,
					_s.Snapshot, analyticShots, (float)_s.AccumulatedCpuMs, _s.BudgetTerminated, failReason);
				return VehicleTrajectory.Invalid(LastStats.Reason, _s.Expanded);
			}

			LastStats = BuildStats(session, _s.Expanded, _s.Generated, _s.BestAny.G, bestPos,
				_goal.RequiresPosePlanning ? Mathf.Abs(Mathf.DeltaAngle(_s.BestAny.Yaw, _goal.YawDegrees)) : 0f,
				_s.Snapshot, analyticShots, (float)_s.AccumulatedCpuMs, _s.BudgetTerminated, failReason);
			if (DebugLog)
				VehicleFileLog.WriteActive($"[LocalPosePlanner] FAIL expanded={_s.Expanded} bestPos={bestPos:F2}m {failReason}");
			return VehicleTrajectory.Invalid(LastStats.Reason, _s.Expanded);
		}

		public VehicleTrajectory Plan(
			Vector3 _startPos,
			float _startYaw,
			GoalPose _goal,
			VehicleKinematicsProfile _profile,
			PlanningObstacleSnapshot _snapshot,
			bool _allowReverse,
			float _stepLength = 0.6f,
			float _maxPlanDurationMs = 0f)
		{
			m_Collision.ResetCounters();
			LastStats = default;

			float totalBudgetMs = _maxPlanDurationMs;
			var session = CreateSession(
				_startPos, _startYaw, _goal, _profile, _snapshot, _allowReverse, _stepLength, totalBudgetMs);

			int maxSteps = totalBudgetMs > 0f
				? Mathf.Max(8, Mathf.CeilToInt(totalBudgetMs / c_RuntimeSliceBudgetMs) + 4)
				: 10000;
			float sliceBudgetMs = _snapshot == null ? 50f : c_RuntimeSliceBudgetMs;
			PlanStepResult result;
			int steps = 0;
			do
			{
				result = StepPlan(session, sliceBudgetMs);
				steps++;
			}
			while (result.Status == PlanStepStatus.Pending && steps < maxSteps);

			if (result.Status == PlanStepStatus.Ready && result.Trajectory != null && result.Trajectory.IsValid)
				return result.Trajectory;

			return VehicleTrajectory.Invalid(LastStats.Reason, LastStats.Expanded);
		}


		private static bool ShouldTryAnalyticShot(
			bool _bestAnyImproved,
			int _expanded,
			float _posErr,
			float _turnRadius,
			ref int _shotCount)
		{
			if (_shotCount >= c_MaxAnalyticShots)
				return false;
			if (_posErr >= Mathf.Max(4f, _turnRadius * 1.2f))
				return false;
			if (_bestAnyImproved)
				return true;
			if (_expanded % c_AnalyticShotStride == 0)
				return true;
			return false;
		}

		private PlanStats BuildStats(
			SessionStats _session,
			int _expanded,
			int _generated,
			float _bestCost,
			float _bestPosError,
			float _bestYawError,
			PlanningObstacleSnapshot _snapshot,
			int _analyticShots,
			float _planMs,
			bool _budgetTerminated,
			string _reason)
		{
			return new PlanStats
			{
				Expanded = _expanded,
				Generated = _generated,
				BestCost = _bestCost,
				BestPosError = _bestPosError,
				BestYawError = _bestYawError,
				SnapshotRays = _snapshot != null ? _snapshot.RayCount : 0,
				CollisionQueries = m_Collision.PhysicsQueries,
				PrimitiveCollisionQueries = m_Collision.PrimitiveQueries,
				TrajectoryCollisionQueries = m_Collision.TrajectoryQueries,
				CandidatesTried = _session.CandidatesTried,
				CandidatesGenerated = _session.CandidatesGenerated,
				RejectedInvalidGeometry = _session.RejectedInvalidGeometry,
				RejectedSanitary = _session.RejectedSanitary,
				RejectedLengthBudget = _session.RejectedLengthBudget,
				RejectedCollision = _session.RejectedCollision,
				RejectedTolerance = _session.RejectedTolerance,
				RsFormulasGenerated = _session.RsFormulasGenerated,
				RsIntegrationRejected = _session.RsIntegrationRejected,
				RsEndpointRejected = _session.RsEndpointRejected,
				RsSanitationRejected = _session.RsSanitationRejected,
				RsValidCandidates = _session.RsValidCandidates,
				TopCandidatesSummary = _session.TopCandidatesSummary,
				AnalyticShots = _analyticShots,
				PlanDurationMs = _planMs,
				BudgetTerminated = _budgetTerminated,
				BudgetReason = null,
				Phase = null,
				StepIndex = 0,
				AnalyticGenerated = _session.CandidatesGenerated,
				AnalyticValid = 0,
				Reason = _reason
			};
		}

		private List<VehicleTrajectory> BuildCheapAnalyticCandidateList(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _turnRadius,
			float _wheelBase,
			bool _allowReverse,
			ref SessionStats _session,
			System.Func<bool> _hasBudget,
			float _maxPlanDurationMs = 0f,
			bool _allowLeadIn = false)
		{
			float dist = BicycleKinematics.FlatDistance(_from, _goal.Position);
			if (dist < 0.05f)
			{
				if (TrajectoryKinematicsValidator.IsAtGoal(_from, _fromYaw, _goal))
				{
					var trivial = BuildTrivial(_from, _fromYaw, _goal);
					return trivial != null && trivial.IsValid
						? new List<VehicleTrajectory> { trivial }
						: new List<VehicleTrajectory>();
				}
				return new List<VehicleTrajectory>();
			}

			var candidates = new List<VehicleTrajectory>(16);
			float align = ReedsSheppPathBuilder.GetTravelAlignment(_from, _fromYaw, _goal.Position);
			bool sideAlign = align >= 55f && align <= 125f;
			bool allowLeadIn = _allowLeadIn &&
			                   (_maxPlanDurationMs <= 0f || _maxPlanDurationMs > 25f);

			if (_hasBudget == null || _hasBudget())
				AddCheapAnalyticCandidates(
					candidates, _from, _fromYaw, _goal, _turnRadius, _wheelBase, _allowReverse, dist, align,
					_maxPlanDurationMs, _hasBudget, allowLeadIn);

			// Side two-stage is compact but not free (~10–40ms). Keep it out of tiny budgets
			// so CPU accounting tests / wall-starved sessions still terminate cleanly.
			bool allowMedium = _maxPlanDurationMs <= 0f || _maxPlanDurationMs > 40f;
			if (allowMedium &&
			    (_hasBudget == null || _hasBudget()) &&
			    sideAlign && dist < _turnRadius * 2.8f)
			{
				TryAddSideTwoStageCandidates(
					candidates, _from, _fromYaw, _goal, _turnRadius, _wheelBase, dist);
			}

			_session.CandidatesGenerated += candidates.Count;
			return candidates;
		}

		/// <returns>true when all heavy families finished; false if paused mid-family for budget.</returns>
		private bool AppendHeavyAnalyticCandidates(
			LocalPlanningSession _s,
			ref SessionStats _session,
			System.Func<bool> _hasBudget)
		{
			if (_s.AnalyticCandidates == null)
				_s.AnalyticCandidates = new List<VehicleTrajectory>(16);

			int before = _s.AnalyticCandidates.Count;
			float dist = BicycleKinematics.FlatDistance(_s.StartPos, _s.Goal.Position);
			float align = ReedsSheppPathBuilder.GetTravelAlignment(_s.StartPos, _s.StartYaw, _s.Goal.Position);
			bool sideClose = dist < 3f && align >= 55f && align <= 125f;
			bool sideAlign = align >= 55f && align <= 125f;

			while (_s.HeavyFamilyIndex < 4)
			{
				if (_hasBudget != null && !_hasBudget())
				{
					_session.CandidatesGenerated += Mathf.Max(0, _s.AnalyticCandidates.Count - before);
					return false;
				}

				switch (_s.HeavyFamilyIndex)
				{
					case 0:
						if (sideAlign && dist < _s.TurnRadius * 2.8f)
						{
							TryAddSideTwoStageCandidates(
								_s.AnalyticCandidates, _s.StartPos, _s.StartYaw, _s.Goal,
								_s.TurnRadius, _s.WheelBase, dist);
						}
						break;
					case 1:
						AddExtendedCandidates(
							_s.AnalyticCandidates, _s.StartPos, _s.StartYaw, _s.Goal,
							_s.TurnRadius, _s.WheelBase, _s.AllowReverse, dist);
						break;
					case 2:
						if (sideClose || (align >= 35f && align <= 145f))
							ReedsSheppPathBuilder.AddSymmetricCandidates(
								_s.AnalyticCandidates, _s.StartPos, _s.StartYaw, _s.Goal,
								_s.TurnRadius, _s.WheelBase, _s.AllowReverse);
						else
							ReedsSheppPathBuilder.AddCandidates(
								_s.AnalyticCandidates, _s.StartPos, _s.StartYaw, _s.Goal,
								_s.TurnRadius, _s.WheelBase, _s.AllowReverse);
						break;
					case 3:
						if (_s.Goal.RequiresPosePlanning && _s.AllowReverse && dist < _s.TurnRadius * 2.8f)
						{
							VehicleTrajectory rs = ReedsSheppClosePoseSolver.Build(
								_s.StartPos, _s.StartYaw, _s.Goal, _s.TurnRadius, _s.WheelBase,
								out ReedsSheppClosePoseSolver.BuildStats rsStats);
							_session.RsFormulasGenerated += rsStats.FormulasGenerated;
							_session.RsIntegrationRejected += rsStats.IntegrationRejected;
							_session.RsEndpointRejected += rsStats.EndpointRejected;
							_session.RsSanitationRejected += rsStats.SanitationRejected;
							_session.RsValidCandidates += rsStats.ValidCandidates;
							ReedsSheppPathBuilder.TryAddCandidate(
								_s.AnalyticCandidates, rs, _s.Goal, dist, _s.TurnRadius);
						}
						break;
				}

				_s.HeavyFamilyIndex++;
			}

			_session.CandidatesGenerated += Mathf.Max(0, _s.AnalyticCandidates.Count - before);
			return true;
		}

		private List<VehicleTrajectory> BuildAnalyticCandidates(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _turnRadius,
			float _wheelBase,
			VehicleKinematicsProfile _profile,
			PlanningObstacleSnapshot _snapshot,
			bool _allowReverse,
			ref SessionStats _session,
			System.Func<bool> _hasBudget = null,
			System.Func<bool> _hasBudgetForExpensive = null)
		{
			// Used by hybrid analytic close — keep a single-pass cheap→heavy build.
			System.Func<bool> canAffordExpensive = _hasBudgetForExpensive ?? _hasBudget;
			bool openField = _snapshot == null || !_snapshot.IsValid;
			var candidates = BuildCheapAnalyticCandidateList(
				_from, _fromYaw, _goal, _turnRadius, _wheelBase, _allowReverse, ref _session, _hasBudget,
				0f, openField);

			bool affordHeavy = canAffordExpensive == null || canAffordExpensive();
			if (!affordHeavy)
				return candidates;

			var tmp = new LocalPlanningSession
			{
				StartPos = _from,
				StartYaw = _fromYaw,
				Goal = _goal,
				TurnRadius = _turnRadius,
				WheelBase = _wheelBase,
				AllowReverse = _allowReverse,
				AnalyticCandidates = candidates
			};
			AppendHeavyAnalyticCandidates(tmp, ref _session, _hasBudget);
			return tmp.AnalyticCandidates ?? candidates;
		}

		private static void AddCheapAnalyticCandidates(
			List<VehicleTrajectory> _candidates,
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _turnRadius,
			float _wheelBase,
			bool _allowReverse,
			float _dist,
			float _align,
			float _maxPlanDurationMs = 0f,
			System.Func<bool> _hasBudget = null,
			bool _allowLeadIn = false)
		{
			float posTol = PlanPosTolerance(_goal);
			// Tiny totals cannot absorb Dubins/CSC/two-stage pose sweeps (~30–80ms).
			bool tightCpu = _maxPlanDurationMs > 0f && _maxPlanDurationMs <= 25f;
			bool CanContinue() => _hasBudget == null || _hasBudget();

			if (_align <= 12f)
			{
				ReedsSheppPathBuilder.TryAddCandidate(
					_candidates,
					BuildStraightSegment(_from, _fromYaw, _goal.Position, TrajectoryGear.Forward, _wheelBase, posTol),
					_goal, _dist, _turnRadius);
			}

			if (_allowReverse && _align >= 168f)
			{
				ReedsSheppPathBuilder.TryAddCandidate(
					_candidates,
					BuildStraightSegment(_from, _fromYaw, _goal.Position, TrajectoryGear.Reverse, _wheelBase, posTol),
					_goal, _dist, _turnRadius);
			}

			// Front-oblique / diagonal (45° & 315° mirrors): forward first, reverse as fallback.
			// Reverse must not win while lattice still has CPU (see FrontObliqueTwoMeters_StartsForward);
			// AdvanceAnalyticPhase defers reverse-first when CanAffordExpensiveAnalytic.
			if (!tightCpu && !_goal.RequiresPosePlanning && _align > 12f && _align < 90f && CanContinue())
			{
				ReedsSheppPathBuilder.TryAddCandidate(
					_candidates,
					BuildFrontObliqueClearanceApproach(
						_from, _fromYaw, _goal, _turnRadius, _wheelBase, posTol),
					_goal, _dist, _turnRadius);
				ReedsSheppPathBuilder.AddLightCsCandidates(
					_candidates, _from, _fromYaw, _goal, _turnRadius, _wheelBase, _allowReverse);
				ReedsSheppPathBuilder.AddLightDubinsPositionCandidates(
					_candidates, _from, _fromYaw, _goal, _turnRadius, _wheelBase);
				ReedsSheppPathBuilder.TryAddCandidate(
					_candidates,
					BuildArcThenStraight(
						_from, _fromYaw, _goal.Position, _fromYaw,
						_turnRadius, _wheelBase, TrajectoryGear.Forward, false, posTol),
					_goal, _dist, _turnRadius);

				// Open field: short straight then turn so wheels are rolling before max κ.
				if (_allowLeadIn && _dist <= 6f && CanContinue())
				{
					ReedsSheppPathBuilder.TryAddCandidate(
						_candidates,
						BuildLeadInThenArcApproach(
							_from, _fromYaw, _goal.Position, _turnRadius, _wheelBase, posTol),
						_goal, _dist, _turnRadius);
				}

				// Mirror-safe fallback when forward CS/Dubins cannot reach inside the circle.
				if (_allowReverse && _dist < _turnRadius * 1.5f && CanContinue())
				{
					TryAddSideTwoStageCandidates(
						_candidates, _from, _fromYaw, _goal, _turnRadius, _wheelBase, _dist);
					ReedsSheppPathBuilder.TryAddCandidate(
						_candidates,
						BuildReverseStagingApproach(_from, _fromYaw, _goal, _turnRadius, _wheelBase),
						_goal, _dist, _turnRadius);
				}
			}

			// Rear-oblique (135°/225°): reverse arc + reverse staging before heavy RS.
			if (!tightCpu && !_goal.RequiresPosePlanning && _allowReverse && _align > 110f && _align < 168f &&
			    CanContinue())
			{
				ReedsSheppPathBuilder.TryAddCandidate(
					_candidates,
					BuildArcThenStraight(
						_from, _fromYaw, _goal.Position, _fromYaw,
						_turnRadius, _wheelBase, TrajectoryGear.Reverse, false, posTol),
					_goal, _dist, _turnRadius);
				ReedsSheppPathBuilder.AddLightCsCandidates(
					_candidates, _from, _fromYaw, _goal, _turnRadius, _wheelBase, true);
				ReedsSheppPathBuilder.TryAddCandidate(
					_candidates,
					BuildReverseStagingApproach(_from, _fromYaw, _goal, _turnRadius, _wheelBase),
					_goal, _dist, _turnRadius);
				// Medium rear-diagonal (5–15 m): three-point is cheap and beats empty lattice.
				if (_dist >= 3f && _dist <= 15f && CanContinue())
				{
					ReedsSheppPathBuilder.TryAddCandidate(
						_candidates,
						BuildThreePoint(_from, _fromYaw, _goal, _turnRadius, _wheelBase),
						_goal, _dist, _turnRadius);
				}
			}

			// Position-only side (90°/270°): two-stage must live in the cheap list, not only
			// behind a second hasBudget gate after Dubins burned the slice.
			if (!tightCpu && !_goal.RequiresPosePlanning && _allowReverse &&
			    _align >= 55f && _align <= 125f && _dist < _turnRadius * 2.8f && CanContinue())
			{
				TryAddSideTwoStageCandidates(
					_candidates, _from, _fromYaw, _goal, _turnRadius, _wheelBase, _dist);
				if (_dist >= 10f && _dist <= 25f && CanContinue())
				{
					ReedsSheppPathBuilder.TryAddCandidate(
						_candidates,
						BuildThreePoint(_from, _fromYaw, _goal, _turnRadius, _wheelBase),
						_goal, _dist, _turnRadius);
				}
			}

			// Long rear / UTurn position goals (15–25 m): three-point loop beats short forward chord.
			if (!tightCpu && !_goal.RequiresPosePlanning && _allowReverse &&
			    _align >= 100f && _align <= 160f && _dist >= 10f && _dist <= 25f && CanContinue())
			{
				ReedsSheppPathBuilder.TryAddCandidate(
					_candidates,
					BuildThreePoint(_from, _fromYaw, _goal, _turnRadius, _wheelBase),
					_goal, _dist, _turnRadius);
			}

			// Explicit pose: joint position+yaw cheap families (Dubins/CSC), not position-only.
			if (_goal.RequiresPosePlanning)
			{
				if (_align <= 12f &&
				    Mathf.Abs(Mathf.DeltaAngle(_fromYaw, _goal.YawDegrees)) <= 12f)
				{
					ReedsSheppPathBuilder.TryAddCandidate(
						_candidates,
						BuildStraightSegment(_from, _fromYaw, _goal.Position, TrajectoryGear.Forward, _wheelBase, posTol),
						_goal, _dist, _turnRadius);
				}

				if (_allowReverse && _align >= 168f &&
				    Mathf.Abs(Mathf.DeltaAngle(_fromYaw + 180f, _goal.YawDegrees)) <= 12f)
				{
					ReedsSheppPathBuilder.TryAddCandidate(
						_candidates,
						BuildStraightSegment(_from, _fromYaw, _goal.Position, TrajectoryGear.Reverse, _wheelBase, posTol),
						_goal, _dist, _turnRadius);
				}

				// Under ≤25ms totals skip Dubins/CSC/two-stage — termination beats a late path.
				if (tightCpu || !CanContinue())
					return;

				ReedsSheppPathBuilder.AddLightPoseCandidates(
					_candidates, _from, _fromYaw, _goal, _turnRadius, _wheelBase, _allowReverse);

				// Compact parking / side-pose two-stage when close.
				if (_dist < _turnRadius * 2.8f && _align >= 35f && _align <= 145f && CanContinue())
				{
					float side = Mathf.Sign(Vector3.Dot(
						_goal.Position - _from,
						BicycleKinematics.YawToForward(_fromYaw + 90f)));
					if (Mathf.Abs(side) < 0.1f)
						side = 1f;
					ReedsSheppPathBuilder.TryAddCandidate(
						_candidates,
						BuildTwoStageSideApproach(_from, _fromYaw, _goal, _turnRadius, _wheelBase, side),
						_goal, _dist, _turnRadius);
					if (!CanContinue())
						return;
					ReedsSheppPathBuilder.TryAddCandidate(
						_candidates,
						BuildTwoStageSideApproach(_from, _fromYaw, _goal, _turnRadius, _wheelBase, -side),
						_goal, _dist, _turnRadius);
				}
			}
		}

		private static VehicleTrajectory SelectBestAnalyticCandidate(
			List<(VehicleTrajectory traj, float selectCost, float posErr)> _valid,
			float _dist,
			float _turnRadius,
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			ref SessionStats _session,
			bool _logFailure)
		{
			if (_valid == null || _valid.Count == 0)
				return null;

			float align = ReedsSheppPathBuilder.GetTravelAlignment(_from, _fromYaw, _goal.Position);
			float shortestLen = float.MaxValue;
			float shortestForwardLen = float.MaxValue;
			for (int i = 0; i < _valid.Count; i++)
			{
				float len = _valid[i].traj.TotalLength;
				shortestLen = Mathf.Min(shortestLen, len);
				if (_valid[i].traj.PointCount > 0 &&
				    _valid[i].traj.Points[0].Gear == TrajectoryGear.Forward)
					shortestForwardLen = Mathf.Min(shortestForwardLen, len);
			}

			// Front hemisphere: don't let a short reverse path set a length budget that
			// rejects all forward-first candidates (the usual cause of reverse-first on 45°).
			float budgetBase = shortestLen;
			if (align < 55f && shortestForwardLen < float.MaxValue)
				budgetBase = shortestForwardLen;

			float lengthBudget = budgetBase < float.MaxValue
				? budgetBase * 1.35f + Mathf.Max(2f, _dist * 0.5f)
				: float.MaxValue;
			// Pose goals: tighter detour — 2.5× (17m for a 7m goal) is what produces multi-cusp crawls.
			float detourRatio = _goal.RequiresPosePlanning && _dist <= 12f
				? 1.9f
				: c_MaxDetourRatio;
			float absoluteBudget = Mathf.Max(_dist * detourRatio, _turnRadius * Mathf.PI + _dist);
			lengthBudget = budgetBase < float.MaxValue
				? Mathf.Min(lengthBudget, absoluteBudget)
				: absoluteBudget;

			VehicleTrajectory best = null;
			float bestCost = float.MaxValue;
			int rejectedLengthBudget = 0;
			_valid.Sort((a, b) => a.selectCost.CompareTo(b.selectCost));
			var topSummary = new System.Text.StringBuilder();
			bool frontOblique = align > 12f && align < 70f && !_goal.RequiresPosePlanning;
			bool sideAlign = align >= 55f && align <= 125f;
			bool posePreferForward = _goal.RequiresPosePlanning && align < 90f;

			bool PassesLengthBudget(VehicleTrajectory traj, string reason)
			{
				bool relaxLength = frontOblique &&
				                   (reason.Contains("cs-fwd") || reason.Contains("arc-fwd") ||
				                    reason.Contains("dubins") || reason.Contains("csc") ||
				                    reason.Contains("leadin")) &&
				                   traj.TotalLength <= Mathf.Max(
					                   lengthBudget, _turnRadius * Mathf.PI * 1.6f + _dist * 2f);
				return traj.TotalLength <= lengthBudget || relaxLength;
			}

			bool hasForwardArcOrCs = false;
			bool hasTwoStageSide = false;
			bool hasUsableForwardFirst = false;
			bool hasCompactForward = false;
			if (frontOblique || sideAlign || posePreferForward)
			{
				for (int i = 0; i < _valid.Count; i++)
				{
					string r = _valid[i].traj.DebugReason ?? string.Empty;
					if (!PassesLengthBudget(_valid[i].traj, r))
						continue;
					if (_valid[i].traj.PointCount > 0 &&
					    _valid[i].traj.Points[0].Gear == TrajectoryGear.Forward)
					{
						hasUsableForwardFirst = true;
						if (_valid[i].traj.GearSegmentCount <= 2 &&
						    _valid[i].traj.TotalLength <= _dist * 2.2f + 2f)
							hasCompactForward = true;
					}
					if (frontOblique &&
					    (r.Contains("arc-fwd") || r.Contains("cs-fwd") || r.Contains("csc") ||
					     r.Contains("dubins") || r.StartsWith("straight-fwd") ||
					     r.Contains("leadin")))
						hasForwardArcOrCs = true;
					if (sideAlign && r.Contains("two-stage-side"))
						hasTwoStageSide = true;
				}
			}

			for (int i = 0; i < _valid.Count; i++)
			{
				var entry = _valid[i];
				string reason = entry.traj.DebugReason ?? string.Empty;

				if (!PassesLengthBudget(entry.traj, reason))
				{
					rejectedLengthBudget++;
					continue;
				}

				// Close front-oblique: never accept three-point (executes as max-κ multi-cusp junk).
				if (frontOblique && _dist <= 6f && reason.Contains("three-point"))
					continue;
				if (frontOblique && hasForwardArcOrCs && reason.Contains("three-point"))
					continue;

				// Front-oblique: skip reverse only when a usable forward path survives length budget.
				// Otherwise short rejected forwards block rev-staging and lattice fails under slice CPU
				// (315° @ 5m: aGen>0, bestPos~2.6, NoFeasible).
				if (frontOblique && hasUsableForwardFirst &&
				    entry.traj.PointCount > 0 &&
				    entry.traj.Points[0].Gear == TrajectoryGear.Reverse)
					continue;

				// Side pose/position: keep compact two-stage over asymmetric RS / lattice leftovers.
				if (sideAlign && hasTwoStageSide &&
				    (reason.StartsWith("rs-") || reason.Contains("merged") || reason.Contains("lattice")))
					continue;

				// Pose goal with a compact forward path available: drop reverse-first multi-cusp RS
				// (log: 6.8m → rs-lrlr2 17m segs=3, ~30 gear flips, crawl ≤4 km/h).
				if (posePreferForward && hasCompactForward &&
				    entry.traj.PointCount > 0 &&
				    entry.traj.Points[0].Gear == TrajectoryGear.Reverse &&
				    entry.traj.GearSegmentCount >= 3 &&
				    reason.StartsWith("rs-"))
					continue;

				// Long side/rear position goals: reject short forward chord (arc/cs) that
				// reaches the point sideways without a reposition loop (logs: ratio~0.4 yaw~125°).
				if (!_goal.RequiresPosePlanning && _dist >= 10f && align >= 55f &&
				    IsShortChordForwardArc(entry.traj, _dist, reason))
					continue;

				if (i < 3)
				{
					if (topSummary.Length > 0)
						topSummary.Append('|');
					topSummary.Append(entry.traj.DebugReason)
						.Append(':').Append(entry.traj.TotalLength.ToString("F1"))
						.Append('m').Append(':').Append(entry.selectCost.ToString("F1"))
						.Append(":err=").Append(entry.posErr.ToString("F2"));
				}

				if (entry.selectCost < bestCost)
				{
					bestCost = entry.selectCost;
					best = entry.traj;
				}
			}

			// Front-oblique fallback: if every forward path was length-rejected, take reverse staging.
			if (best == null && frontOblique)
			{
				for (int i = 0; i < _valid.Count; i++)
				{
					var entry = _valid[i];
					string reason = entry.traj.DebugReason ?? string.Empty;
					if (entry.traj.PointCount == 0 ||
					    entry.traj.Points[0].Gear != TrajectoryGear.Reverse)
						continue;
					if (!(reason.Contains("rev-staging") || reason.Contains("two-stage-side") ||
					      reason.Contains("cs-rev") || reason.StartsWith("rs-")))
						continue;
					if (entry.selectCost < bestCost)
					{
						bestCost = entry.selectCost;
						best = entry.traj;
					}
				}
			}

			_session.RejectedLengthBudget += rejectedLengthBudget;
			if (topSummary.Length > 0)
				_session.TopCandidatesSummary = topSummary.ToString();

			string rsDetail = $"rs=f{_session.RsFormulasGenerated}/i{_session.RsIntegrationRejected}/e{_session.RsEndpointRejected}/s{_session.RsSanitationRejected}/ok{_session.RsValidCandidates}";
			string failDetail = $"analytic fail tried={_session.CandidatesTried} inv={_session.RejectedInvalidGeometry} col={_session.RejectedCollision} tol={_session.RejectedTolerance} san={_session.RejectedSanitary} len={rejectedLengthBudget} {rsDetail}";
			if (best != null)
				_session.Reason = best.DebugReason;
			else if (_logFailure)
				_session.Reason = failDetail;

			if (DebugLog && _logFailure && best == null)
				VehicleFileLog.WriteActive($"[LocalPosePlanner] {failDetail} dist={_dist:F2} r={_turnRadius:F2}");

			return best;
		}

		private VehicleTrajectory TryAnalyticClose(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _turnRadius,
			float _wheelBase,
			VehicleKinematicsProfile _profile,
			PlanningObstacleSnapshot _snapshot,
			bool _allowReverse,
			ref SessionStats _session,
			bool _logFailure,
			System.Func<bool> _hasBudget = null,
			System.Func<bool> _hasBudgetForExpensive = null)
		{
			if (_hasBudget != null && !_hasBudget())
				return null;

			var candidates = BuildAnalyticCandidates(
				_from, _fromYaw, _goal, _turnRadius, _wheelBase, _profile, _snapshot, _allowReverse,
				ref _session, _hasBudget, _hasBudgetForExpensive ?? _hasBudget);

			float dist = BicycleKinematics.FlatDistance(_from, _goal.Position);
			float posTol = PlanPosTolerance(_goal);
			var valid = new List<(VehicleTrajectory traj, float selectCost, float posErr)>(candidates.Count);

			for (int i = 0; i < candidates.Count; i++)
			{
				if (_hasBudget != null && !_hasBudget())
					break;

				var c = candidates[i];
				if (c == null || !c.IsValid)
				{
					_session.RejectedInvalidGeometry++;
					continue;
				}

				if (!TrajectoryKinematicsValidator.Validate(c, _turnRadius, out string kinReason))
				{
					_session.RejectedInvalidGeometry++;
					if (DebugLog && _session.CandidatesTried < 2)
						VehicleFileLog.WriteActive($"[LocalPosePlanner] reject kinematic {c.DebugReason}: {kinReason}");
					continue;
				}

				_session.CandidatesTried++;
				if (!ReedsSheppPathBuilder.IsSanitary(c, dist, _turnRadius))
				{
					_session.RejectedSanitary++;
					continue;
				}

				if (_snapshot != null && _snapshot.IsValid && !m_Collision.IsTrajectorySafe(c, _profile, _snapshot))
				{
					_session.RejectedCollision++;
					continue;
				}

				TrajectoryPoint end = c.Points[c.PointCount - 1];
				float posErr = BicycleKinematics.FlatDistance(end.Position, _goal.Position);
				if (posErr > posTol)
				{
					_session.RejectedTolerance++;
					continue;
				}

				if (_goal.RequiresPosePlanning &&
				    Mathf.Abs(Mathf.DeltaAngle(end.YawDegrees, _goal.YawDegrees)) > _goal.HeadingToleranceDeg)
				{
					_session.RejectedTolerance++;
					continue;
				}

				float selectCost = ComputeSelectionCost(c, _from, _fromYaw, _goal);
				valid.Add((c, selectCost, posErr));
			}

			return SelectBestAnalyticCandidate(
				valid, dist, _turnRadius, _from, _fromYaw, _goal, ref _session, _logFailure);
		}

		private static float TrajectoryYawSwing(VehicleTrajectory _traj)
		{
			if (_traj == null || !_traj.IsValid || _traj.PointCount < 2)
				return 0f;
			return Mathf.Abs(Mathf.DeltaAngle(
				_traj.Points[0].YawDegrees,
				_traj.Points[_traj.PointCount - 1].YawDegrees));
		}

		/// <summary>
		/// Single-segment forward arc/CS whose path length is far shorter than the straight
		/// chord — typical "drive sideways into the goal" without a turn loop.
		/// </summary>
		private static bool IsShortChordForwardArc(
			VehicleTrajectory _traj,
			float _directDist,
			string _reason)
		{
			if (_traj == null || !_traj.IsValid || _traj.GearSegmentCount > 1)
				return false;
			if (_traj.PointCount == 0 || _traj.Points[0].Gear != TrajectoryGear.Forward)
				return false;
			if (!(_reason.Contains("arc-fwd") || _reason.Contains("cs-fwd")))
				return false;

			float ratio = _traj.TotalLength / Mathf.Max(0.5f, _directDist);
			if (ratio >= 0.7f)
				return false;
			return TrajectoryYawSwing(_traj) < 90f;
		}

		private static float ComputeSelectionCost(
			VehicleTrajectory _traj,
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal)
		{
			if (_traj == null || !_traj.IsValid)
				return float.MaxValue;

			float align = ReedsSheppPathBuilder.GetTravelAlignment(_from, _fromYaw, _goal.Position);
			TrajectoryGear first = _traj.Points[0].Gear;
			float direct = BicycleKinematics.FlatDistance(_from, _goal.Position);
			string reason = _traj.DebugReason ?? string.Empty;
			int segs = _traj.GearSegmentCount;

			// Length + gear changes (each cusp is expensive for the driver).
			float cost = _traj.TotalLength + Mathf.Max(0, segs - 1) * (c_GearSwitchPenalty + 1.5f);

			if (reason.StartsWith("straight-") || reason == "trivial")
				cost -= 0.5f;

			// --- Front hemisphere: stay forward, avoid reverse-first / long RS ---
			if (align < 55f)
			{
				if (first == TrajectoryGear.Reverse)
					cost += 45f;
				if (segs >= 3)
					cost += 18f;
				if (reason.Contains("two-stage-side") || reason.Contains("rev-staging"))
					cost += 22f;
				if (reason.Contains("arc-fwd") || reason.Contains("cs-fwd") || reason.Contains("dubins") ||
				    reason.Contains("csc"))
					cost -= 6f;
				if (reason.Contains("three-point"))
					cost += direct <= 6f ? 80f : 25f;
				if (reason.Contains("leadin-arc") || reason.Contains("leadin-cs"))
					cost -= 10f;
				if (reason.StartsWith("rs-") && first == TrajectoryGear.Reverse)
					cost += 30f;
				if (reason.StartsWith("rs-") && _traj.TotalLength > direct * 2f)
					cost += 25f;
			}

			// --- Side: compact reverse-first two-stage (not long RS loops) ---
			if (align >= 55f && align <= 125f)
			{
				// Prefer the same compact family on left/right mirrors; RS CSC can
				// otherwise pick 3-seg on one side and 4-seg on the other.
				if (reason.Contains("two-stage-side"))
					cost -= _goal.RequiresPosePlanning ? 28f : 12f;
				if (direct >= 10f && !_goal.RequiresPosePlanning)
				{
					if (reason.Contains("three-point"))
						cost -= 18f;
					if (reason.Contains("two-stage-side"))
						cost -= 6f;
					if (IsShortChordForwardArc(_traj, direct, reason))
						cost += 95f;
				}
				if (reason.Contains("arc-rev") && segs == 1)
					cost -= 5f;
				if (first == TrajectoryGear.Reverse && segs <= 2)
					cost -= 4f;
				if (first == TrajectoryGear.Forward && segs > 1 && !_goal.RequiresPosePlanning)
					cost += 8f;
				if (segs >= 3)
					cost += 12f;
				if (segs >= 4)
					cost += 18f;
				if (reason.StartsWith("rs-"))
					cost += _goal.RequiresPosePlanning ? 24f : 10f;
				if (reason.StartsWith("rs-") && _traj.TotalLength > direct * 2.0f)
					cost += 22f;
				if (reason.StartsWith("rs-") && direct <= 6f && _traj.TotalLength > direct * 2.5f)
					cost += 30f;
				if (_goal.RequiresPosePlanning && reason.StartsWith("rs-") && segs >= 3)
					cost += 20f;
			}

			// --- Rear / rear-oblique: reverse approach is more efficient than turning around ---
			if (align >= 100f)
			{
				if (first == TrajectoryGear.Forward)
					cost += align >= 135f ? 14f : 8f;
				if (direct >= 10f && !_goal.RequiresPosePlanning)
				{
					if (reason.Contains("three-point"))
						cost -= 16f;
					if (reason.Contains("two-stage-side") || reason.Contains("one-cusp"))
						cost -= 8f;
					if (IsShortChordForwardArc(_traj, direct, reason))
						cost += 100f;
				}
				if (first == TrajectoryGear.Reverse)
					cost -= 3f;
				if (reason.Contains("one-cusp") || reason.Contains("rev-staging") ||
				    reason.Contains("cs-rev") || reason.Contains("straight-rev"))
					cost -= 4f;
				if (reason.StartsWith("rs-") && segs >= 3 && _traj.TotalLength > direct * 2.5f)
					cost += 12f;
			}

			// Front-oblique close: prefer short forward arc/Dubins over multi-cusp junk.
			if (align > 12f && align < 70f && !_goal.RequiresPosePlanning)
			{
				if (reason.Contains("arc-fwd") || reason.Contains("cs-fwd") || reason.Contains("dubins") ||
				    reason.Contains("leadin-"))
					cost -= 14f;
				if (reason.Contains("three-point"))
					cost += 90f;
				if (reason.Contains("two-stage-side"))
					cost += 14f;
				if (reason.Contains("merged") || reason.Contains("lattice"))
					cost += 5f;
			}

			// Heading goals ahead: prefer forward Dubins/CSC over reverse RS.
			if (_goal.RequiresPosePlanning && align < 90f)
			{
				if (first == TrajectoryGear.Forward && segs <= 2)
					cost -= 16f;
				if (first == TrajectoryGear.Reverse)
					cost += 28f;
				if (reason.StartsWith("rs-") && first == TrajectoryGear.Reverse)
					cost += 28f;
				if (segs >= 3)
					cost += 24f;
				if (reason.Contains("dubins") || reason.Contains("csc") || reason.Contains("asa-fwd") ||
				    reason.Contains("arc-straight") || reason.Contains("arc-fwd") || reason.Contains("cs-fwd"))
					cost -= 12f;
				if (direct <= 10f && _traj.TotalLength > direct * 1.85f)
					cost += (_traj.TotalLength / Mathf.Max(0.5f, direct) - 1.85f) * 40f;
			}

			if (_goal.HasAdvisoryHeading)
			{
				TrajectoryPoint end = _traj.Points[_traj.PointCount - 1];
				cost += _goal.AdvisoryYawPenalty(end.YawDegrees) * 0.02f;
			}

			float ratio = _traj.TotalLength / Mathf.Max(0.5f, direct);
			if (ratio > 1.6f)
				cost += (ratio - 1.6f) * 16f;
			if (ratio > 2.5f)
				cost += (ratio - 2.5f) * 28f;
			if (direct <= 5f && ratio > 2.2f)
				cost += (ratio - 2.2f) * 35f;

			// Open-field short approaches: prefer gentler peak κ when space allows.
			if (align < 70f && direct <= 6f && segs <= 2)
			{
				float peakK = 0f;
				for (int i = 0; i < _traj.PointCount; i++)
					peakK = Mathf.Max(peakK, Mathf.Abs(_traj.Points[i].Curvature));
				if (peakK > 0.12f)
					cost += (peakK - 0.12f) * 35f;
				else if (peakK < 0.1f)
					cost -= 2f;
			}

			return cost;
		}

		private static void AddExtendedCandidates(
			List<VehicleTrajectory> _candidates,
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _turnRadius,
			float _wheelBase,
			bool _allowReverse,
			float _dist)
		{
			if (_dist >= _turnRadius * 2.8f)
				return;

			float align = ReedsSheppPathBuilder.GetTravelAlignment(_from, _fromYaw, _goal.Position);

			if (_goal.RequiresPosePlanning)
			{
				ReedsSheppPathBuilder.TryAddCandidate(_candidates,
					BuildArcStraightArc(_from, _fromYaw, _goal, _turnRadius, _wheelBase, TrajectoryGear.Forward),
					_goal, _dist, _turnRadius);
				if (_allowReverse)
				{
					ReedsSheppPathBuilder.TryAddCandidate(_candidates,
						BuildArcStraightArc(_from, _fromYaw, _goal, _turnRadius, _wheelBase, TrajectoryGear.Reverse),
						_goal, _dist, _turnRadius);
				}
			}

			// arc-fwd is also added in AddCheapAnalyticCandidates; keep a second pass here for
			// front-oblique cases that may have skipped cheap due to align banding.
			if (!_goal.RequiresPosePlanning && align < 55f)
			{
				float posTol = PlanPosTolerance(_goal);
				ReedsSheppPathBuilder.TryAddCandidate(_candidates,
					BuildArcThenStraight(
						_from, _fromYaw, _goal.Position, _fromYaw,
						_turnRadius, _wheelBase, TrajectoryGear.Forward, false, posTol),
					_goal, _dist, _turnRadius);
			}

			// three-point: inside-circle fallback only — never for short front-oblique (logs: 7.9m
			// three-point for 2m@45°). Prefer CS/Dubins/lead-in/lattice instead.
			bool shortFrontOblique = !_goal.RequiresPosePlanning &&
			                         _dist <= 6f && align > 12f && align < 70f;
			bool rearOblique = !_goal.RequiresPosePlanning &&
			                 align >= 100f && align <= 160f && _dist <= 25f;
			if (!_goal.RequiresPosePlanning &&
			    ((align <= 90f && !shortFrontOblique) || rearOblique))
			{
				ReedsSheppPathBuilder.TryAddCandidate(_candidates,
					BuildThreePoint(_from, _fromYaw, _goal, _turnRadius, _wheelBase),
					_goal, _dist, _turnRadius);
			}

			if (_allowReverse && !_goal.RequiresPosePlanning && align >= 100f)
			{
				ReedsSheppPathBuilder.TryAddCandidate(_candidates,
					BuildReverseStagingApproach(_from, _fromYaw, _goal, _turnRadius, _wheelBase),
					_goal, _dist, _turnRadius);
			}
		}

		private static float PlanPosTolerance(GoalPose _goal) =>
			Mathf.Max(c_PlanPositionTolerance, _goal.PositionTolerance);

		private static VehicleTrajectory BuildReverseStagingApproach(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _turnRadius,
			float _wheelBase)
		{
			Vector3 toGoal = _goal.Position - _from;
			toGoal.y = 0f;
			float side = Mathf.Sign(Vector3.Dot(toGoal, BicycleKinematics.YawToForward(_fromYaw + 90f)));
			if (Mathf.Abs(side) < 0.1f)
				side = 1f;

			float r = Mathf.Max(1f, _turnRadius);
			VehicleTrajectory best = null;
			float bestLen = float.MaxValue;
			float posTol = PlanPosTolerance(_goal);
			float[] pulls = { 0.5f, 0.8f, 1.0f, 1.2f, 1.6f, 2.0f, 2.5f, 3.0f, 4.0f, 5.0f };
			float direct = toGoal.magnitude;

			for (int pi = 0; pi < pulls.Length; pi++)
			{
				float pull = pulls[pi];
				// Longer goals need deeper reverse staging pulls to reach rear-oblique targets.
				if (pull + 1.5f < direct * 0.25f && pi < pulls.Length - 2)
					continue;
				for (int turn = -1; turn <= 1; turn += 2)
				{
					var stage1 = BicycleKinematics.Integrate(
						_from, _fromYaw, side * turn / r, TrajectoryGear.Reverse, pull, _wheelBase, 0f);

					for (int gi = 0; gi < 2; gi++)
					{
						TrajectoryGear finish = gi == 0 ? TrajectoryGear.Reverse : TrajectoryGear.Forward;
						var stage2 = BuildArcThenStraight(
							stage1.EndPosition, stage1.EndYawDegrees, _goal.Position, stage1.EndYawDegrees,
							r, _wheelBase, finish, false, posTol);
						if (stage2 == null || !stage2.IsValid)
							continue;

						var pts = new List<TrajectoryPoint>(stage1.Samples);
						if (pts.Count > 0)
						{
							TrajectoryPoint cusp = pts[pts.Count - 1];
							pts[pts.Count - 1] = new TrajectoryPoint(
								cusp.Position, cusp.YawDegrees, cusp.Curvature, cusp.Gear, cusp.ArcLength, true);
						}

						float baseArc = pts[pts.Count - 1].ArcLength;
						for (int i = 1; i < stage2.PointCount; i++)
						{
							TrajectoryPoint p = stage2.Points[i];
							pts.Add(new TrajectoryPoint(
								p.Position, p.YawDegrees, p.Curvature, p.Gear, baseArc + p.ArcLength, p.IsCusp));
						}

						float posErr = BicycleKinematics.FlatDistance(pts[pts.Count - 1].Position, _goal.Position);
						if (posErr > posTol)
							continue;

						float len = pts[pts.Count - 1].ArcLength;
						// Prefer reverse finish for rear approaches (no U-turn).
						if (finish == TrajectoryGear.Reverse)
							len *= 0.85f;
						if (len >= bestLen)
							continue;

						bestLen = len;
						var t = new VehicleTrajectory();
						t.Build(pts, pull * c_ReversePenalty + stage2.Cost + c_GearSwitchPenalty, 0, "rev-staging");
						best = t;
					}
				}
			}

			return best;
		}

		private static VehicleTrajectory BuildTrivial(Vector3 _pos, float _yaw, GoalPose _goal)
		{
			if (!TrajectoryKinematicsValidator.IsAtGoal(_pos, _yaw, _goal))
				return VehicleTrajectory.Invalid("trivial rejected: goal not satisfied", 0);

			float endYaw = GoalUsesYaw(_goal) ? _goal.YawDegrees : _yaw;
			var pts = new List<TrajectoryPoint>
			{
				new TrajectoryPoint(_pos, _yaw, 0f, TrajectoryGear.Forward, 0f),
				new TrajectoryPoint(_goal.Position, endYaw, 0f, TrajectoryGear.Forward, 0.001f)
			};
			var t = new VehicleTrajectory();
			t.Build(pts, 0.001f, 0, "trivial");
			return t;
		}

		private bool AcceptTrajectory(VehicleTrajectory _traj, float _turnRadius, ref SessionStats _session)
		{
			if (_traj == null || !_traj.IsValid)
				return false;

			if (TrajectoryKinematicsValidator.Validate(_traj, _turnRadius, out string reason))
				return true;

			_session.RejectedInvalidGeometry++;
			if (DebugLog)
				VehicleFileLog.WriteActive($"[LocalPosePlanner] reject {_traj.DebugReason}: {reason}");
			return false;
		}

		/// <summary>
		/// Search may finish inside 0.25 m; execution requires ≤0.1 m via real kinematics (no teleport).
		/// </summary>
		private VehicleTrajectory EnsureExecutionEndpoint(
			VehicleTrajectory _traj,
			GoalPose _goal,
			float _turnRadius,
			float _wheelBase,
			ref SessionStats _session)
		{
			if (_traj == null || !_traj.IsValid || _traj.PointCount < 1)
				return null;

			VehicleTrajectory original = _traj;
			TrajectoryPoint origEnd = original.Points[original.PointCount - 1];
			float origPosErr = BicycleKinematics.FlatDistance(origEnd.Position, _goal.Position);

			// Physical steer slew: reshape κ steps into ramps before endpoint refine.
			if (BicycleKinematics.EnableSteerRamp &&
			    !(_traj.DebugReason != null &&
			      (_traj.DebugReason.StartsWith("straight-") || _traj.DebugReason == "trivial")))
			{
				VehicleTrajectory physical = BicycleKinematics.ResampleWithSteerRamp(_traj, _wheelBase);
				if (physical != null && physical.IsValid && AcceptTrajectory(physical, _turnRadius, ref _session))
				{
					TrajectoryPoint pend = physical.Points[physical.PointCount - 1];
					float pErr = BicycleKinematics.FlatDistance(pend.Position, _goal.Position);
					// Keep clothoid only when endpoint does not blow out past the analytic path.
					if (pErr <= origPosErr + 0.2f)
						_traj = physical;
				}
			}

			TrajectoryPoint end = _traj.Points[_traj.PointCount - 1];
			float posErr = BicycleKinematics.FlatDistance(end.Position, _goal.Position);
			bool yawOk = !_goal.RequiresPosePlanning ||
			             Mathf.Abs(Mathf.DeltaAngle(end.YawDegrees, _goal.YawDegrees)) <= _goal.HeadingToleranceDeg;
			if (posErr <= c_ExecutionPositionTolerance && yawOk)
				return _traj;

			VehicleTrajectory refined = TryRefineEndpoint(_traj, _goal, _turnRadius, _wheelBase);
			if (refined != null && refined.IsValid)
			{
				TrajectoryPoint rend = refined.Points[refined.PointCount - 1];
				float rPos = BicycleKinematics.FlatDistance(rend.Position, _goal.Position);
				bool rYaw = !_goal.RequiresPosePlanning ||
				            Mathf.Abs(Mathf.DeltaAngle(rend.YawDegrees, _goal.YawDegrees)) <=
				            _goal.HeadingToleranceDeg;
				if (rPos <= c_ExecutionPositionTolerance && rYaw)
					return refined;
			}

			// Prefer execution-tight endpoints; still accept search-tol when refine cannot close.
			float searchTol = PlanPosTolerance(_goal);
			if (posErr <= searchTol && yawOk)
				return _traj;

			// Clothoid may have worsened endpoint — fall back to original analytic within tol.
			bool origYawOk = !_goal.RequiresPosePlanning ||
			                 Mathf.Abs(Mathf.DeltaAngle(origEnd.YawDegrees, _goal.YawDegrees)) <=
			                 _goal.HeadingToleranceDeg;
			if (origPosErr <= searchTol && origYawOk)
				return original;

			_session.RejectedTolerance++;
			return null;
		}

		private static VehicleTrajectory TryRefineEndpoint(
			VehicleTrajectory _traj,
			GoalPose _goal,
			float _turnRadius,
			float _wheelBase)
		{
			TrajectoryPoint end = _traj.Points[_traj.PointCount - 1];
			float posErr = BicycleKinematics.FlatDistance(end.Position, _goal.Position);
			if (posErr < 0.02f)
				return _traj;

			// Short straight/arc tail only — never snap the last sample to the goal.
			VehicleTrajectory tail = null;
			TrajectoryGear gear = end.Gear;
			tail = BuildStraightSegment(
				end.Position, end.YawDegrees, _goal.Position, gear, _wheelBase, c_ExecutionPositionTolerance);
			if (tail == null || !tail.IsValid)
			{
				tail = BuildArcThenStraight(
					end.Position, end.YawDegrees, _goal.Position,
					_goal.RequiresPosePlanning ? _goal.YawDegrees : end.YawDegrees,
					_turnRadius, _wheelBase, gear, _goal.RequiresPosePlanning, c_ExecutionPositionTolerance);
			}

			if ((tail == null || !tail.IsValid) && gear == TrajectoryGear.Forward)
			{
				tail = BuildStraightSegment(
					end.Position, end.YawDegrees, _goal.Position, TrajectoryGear.Reverse, _wheelBase,
					c_ExecutionPositionTolerance);
			}

			if (tail == null || !tail.IsValid || tail.TotalLength > 0.55f)
				return null;

			var pts = new List<TrajectoryPoint>(_traj.PointCount + tail.PointCount);
			for (int i = 0; i < _traj.PointCount; i++)
				pts.Add(_traj.Points[i]);

			float baseArc = pts[pts.Count - 1].ArcLength;
			for (int i = 1; i < tail.PointCount; i++)
			{
				TrajectoryPoint p = tail.Points[i];
				pts.Add(new TrajectoryPoint(
					p.Position, p.YawDegrees, p.Curvature, p.Gear, baseArc + p.ArcLength, p.IsCusp));
			}

			var merged = new VehicleTrajectory();
			merged.Build(pts, _traj.Cost + tail.Cost, _traj.ExpandedNodes, _traj.DebugReason + "+refine");
			return merged;
		}

		private static VehicleTrajectory BuildStraightSegment(
			Vector3 _from,
			float _fromYaw,
			Vector3 _to,
			TrajectoryGear _gear,
			float _wheelBase,
			float _posTol = c_PlanPositionTolerance)
		{
			Vector3 delta = _to - _from;
			delta.y = 0f;
			float len = delta.magnitude;
			if (len < 0.05f)
				return null;

			float travelYaw = Quaternion.LookRotation(delta.normalized, Vector3.up).eulerAngles.y;
			float alignErr = Mathf.Abs(Mathf.DeltaAngle(_fromYaw, travelYaw));
			if (_gear == TrajectoryGear.Forward && alignErr > 12f)
				return null;
			if (_gear == TrajectoryGear.Reverse && alignErr < 168f)
				return null;

			var prim = BicycleKinematics.Integrate(_from, _fromYaw, 0f, _gear, len, _wheelBase, 0f);
			float posErr = BicycleKinematics.FlatDistance(prim.EndPosition, _to);
			if (posErr > _posTol)
				return null;

			var t = new VehicleTrajectory();
			float cost = len * (_gear == TrajectoryGear.Reverse ? c_ReversePenalty : 1f);
			t.Build(prim.Samples, cost, 0, _gear == TrajectoryGear.Reverse ? "straight-rev" : "straight-fwd");
			return t;
		}

		private static VehicleTrajectory BuildArcThenStraight(
			Vector3 _from,
			float _fromYaw,
			Vector3 _to,
			float _targetYaw,
			float _turnRadius,
			float _wheelBase,
			TrajectoryGear _gear,
			bool _alignHeadingAtEnd,
			float _posTol = c_PlanPositionTolerance)
		{
			Vector3 toGoal = _to - _from;
			toGoal.y = 0f;
			float dist = toGoal.magnitude;
			if (dist < 0.05f)
				return null;

			float approachYaw = Quaternion.LookRotation(toGoal.normalized, Vector3.up).eulerAngles.y;
			float headingErr = Mathf.DeltaAngle(_fromYaw, approachYaw);
			if (_gear == TrajectoryGear.Reverse)
				headingErr = Mathf.DeltaAngle(_fromYaw, approachYaw + 180f);

			if (Mathf.Abs(headingErr) < 3f)
				return BuildStraightSegment(_from, _fromYaw, _to, _gear, _wheelBase, _posTol);

			float preferredSign = headingErr >= 0f ? 1f : -1f;
			VehicleTrajectory best = null;
			float bestCost = float.MaxValue;

			// Preferred turn first, then opposite (sometimes shorter geometrically).
			TryArcThenStraightSigned(
				_from, _fromYaw, _to, _targetYaw, _turnRadius, _wheelBase, _gear,
				_alignHeadingAtEnd, _posTol, preferredSign, ref best, ref bestCost);
			TryArcThenStraightSigned(
				_from, _fromYaw, _to, _targetYaw, _turnRadius, _wheelBase, _gear,
				_alignHeadingAtEnd, _posTol, -preferredSign, ref best, ref bestCost);

			return best;
		}

		/// <summary>
		/// Arc until the goal lies on the current drive axis (tangent condition), then straight.
		/// Unlike turning to the initial bearing, this stays valid after the vehicle moves on the circle.
		/// </summary>
		private static void TryArcThenStraightSigned(
			Vector3 _from,
			float _fromYaw,
			Vector3 _to,
			float _targetYaw,
			float _turnRadius,
			float _wheelBase,
			TrajectoryGear _gear,
			bool _alignHeadingAtEnd,
			float _posTol,
			float _sign,
			ref VehicleTrajectory _best,
			ref float _bestCost)
		{
			float radius = Mathf.Max(1f, _turnRadius);
			float curv = _sign / radius;
			if (_gear == TrajectoryGear.Reverse)
				curv = -curv;

			// Walk the arc until the goal is ahead with acceptable lateral error.
			float maxArc = radius * 170f * Mathf.Deg2Rad;
			float step = Mathf.Clamp(radius * 0.04f, 0.08f, 0.25f);
			float traveled = 0f;
			Vector3 pos = _from;
			float yaw = _fromYaw;
			bool found = false;
			float along = 0f;

			while (traveled + 0.01f < maxArc)
			{
				float chunk = Mathf.Min(step, maxArc - traveled);
				var prim = BicycleKinematics.Integrate(pos, yaw, curv, _gear, chunk, _wheelBase, traveled);
				pos = prim.EndPosition;
				yaw = prim.EndYawDegrees;
				traveled += chunk;

				Vector3 driveFwd = BicycleKinematics.YawToForward(yaw);
				if (_gear == TrajectoryGear.Reverse)
					driveFwd = -driveFwd;

				Vector3 toEnd = _to - pos;
				toEnd.y = 0f;
				along = Vector3.Dot(toEnd, driveFwd);
				float lateralErr = Vector3.Cross(driveFwd, toEnd).magnitude;
				if (along > 0.05f && lateralErr <= _posTol)
				{
					found = true;
					break;
				}
			}

			if (!found || traveled < 0.05f)
				return;

			var arc = BicycleKinematics.Integrate(_from, _fromYaw, curv, _gear, traveled, _wheelBase, 0f);
			Vector3 mid = arc.EndPosition;
			float midYaw = arc.EndYawDegrees;

			Vector3 midFwd = BicycleKinematics.YawToForward(midYaw);
			if (_gear == TrajectoryGear.Reverse)
				midFwd = -midFwd;
			Vector3 rem = _to - mid;
			rem.y = 0f;
			along = Vector3.Dot(rem, midFwd);
			if (along < 0.05f)
				return;
			if (Vector3.Cross(midFwd, rem).magnitude > _posTol)
				return;

			var pts = new List<TrajectoryPoint>(arc.Samples);
			if (along > 0.05f)
			{
				var line = BicycleKinematics.Integrate(mid, midYaw, 0f, _gear, along, _wheelBase, arc.Length);
				for (int i = 1; i < line.Samples.Count; i++)
					pts.Add(line.Samples[i]);
			}

			if (pts.Count < 2)
				return;

			float posErr = BicycleKinematics.FlatDistance(pts[pts.Count - 1].Position, _to);
			if (posErr > _posTol)
				return;

			if (_alignHeadingAtEnd)
			{
				float finalYawErr = Mathf.DeltaAngle(pts[pts.Count - 1].YawDegrees, _targetYaw);
				if (Mathf.Abs(finalYawErr) > 3f)
				{
					float finalSign = finalYawErr >= 0f ? 1f : -1f;
					float finalArcLen = radius * Mathf.Abs(finalYawErr) * Mathf.Deg2Rad;
					float finalCurv = finalSign / radius;
					if (_gear == TrajectoryGear.Reverse)
						finalCurv = -finalCurv;

					TrajectoryPoint last = pts[pts.Count - 1];
					var finalArc = BicycleKinematics.Integrate(
						last.Position, last.YawDegrees, finalCurv, _gear, finalArcLen, _wheelBase, last.ArcLength);
					float drift = BicycleKinematics.FlatDistance(finalArc.EndPosition, _to);
					if (drift > _posTol)
						return;

					for (int i = 1; i < finalArc.Samples.Count; i++)
						pts.Add(finalArc.Samples[i]);
				}
			}

			float cost = traveled + along;
			if (_alignHeadingAtEnd && pts.Count > 0)
				cost += radius * Mathf.Abs(Mathf.DeltaAngle(pts[pts.Count - 1].YawDegrees, _targetYaw)) * Mathf.Deg2Rad;
			if (_gear == TrajectoryGear.Reverse)
				cost *= c_ReversePenalty;

			if (cost >= _bestCost)
				return;

			var t = new VehicleTrajectory();
			t.Build(pts, cost, 0, _gear == TrajectoryGear.Reverse ? "arc-rev" : "arc-fwd");
			_best = t;
			_bestCost = cost;
		}

		private static VehicleTrajectory BuildArcStraightArc(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _turnRadius,
			float _wheelBase,
			TrajectoryGear _gear)
		{
			if (!_goal.RequiresPosePlanning)
				return null;

			Vector3 toGoal = _goal.Position - _from;
			toGoal.y = 0f;
			if (toGoal.sqrMagnitude < 0.01f)
				return null;

			float approachYaw = Quaternion.LookRotation(toGoal.normalized, Vector3.up).eulerAngles.y;
			float posTol = PlanPosTolerance(_goal);
			var approach = BuildArcThenStraight(
				_from, _fromYaw, _goal.Position, approachYaw, _turnRadius, _wheelBase, _gear, false, posTol);
			if (approach == null || !approach.IsValid)
				return null;

			TrajectoryPoint end = approach.Points[approach.PointCount - 1];
			float yawErr = Mathf.DeltaAngle(end.YawDegrees, _goal.YawDegrees);
			if (Mathf.Abs(yawErr) <= _goal.HeadingToleranceDeg)
				return approach;

			float radius = Mathf.Max(1f, _turnRadius);
			float sign = yawErr >= 0f ? 1f : -1f;
			float arcLen = radius * Mathf.Abs(yawErr) * Mathf.Deg2Rad;
			float curv = sign / radius;
			if (_gear == TrajectoryGear.Reverse)
				curv = -curv;

			var pts = new List<TrajectoryPoint>(approach.Points);
			var finalArc = BicycleKinematics.Integrate(
				end.Position, end.YawDegrees, curv, _gear, arcLen, _wheelBase, end.ArcLength);
			float posErr = BicycleKinematics.FlatDistance(finalArc.EndPosition, _goal.Position);
			if (posErr > posTol)
				return null;

			for (int i = 1; i < finalArc.Samples.Count; i++)
				pts.Add(finalArc.Samples[i]);

			var t = new VehicleTrajectory();
			t.Build(pts, approach.Cost + arcLen, 0, "arc-straight-arc");
			return t;
		}

		private static void TryAddSideTwoStageCandidates(
			List<VehicleTrajectory> _candidates,
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _turnRadius,
			float _wheelBase,
			float _dist)
		{
			Vector3 toGoal = _goal.Position - _from;
			toGoal.y = 0f;
			float goalSide = Mathf.Sign(Vector3.Dot(toGoal, BicycleKinematics.YawToForward(_fromYaw + 90f)));
			if (Mathf.Abs(goalSide) < 0.1f)
				goalSide = 1f;

			ReedsSheppPathBuilder.TryAddCandidate(_candidates,
				BuildTwoStageSideApproach(_from, _fromYaw, _goal, _turnRadius, _wheelBase, goalSide),
				_goal, _dist, _turnRadius);
			ReedsSheppPathBuilder.TryAddCandidate(_candidates,
				BuildTwoStageSideApproach(_from, _fromYaw, _goal, _turnRadius, _wheelBase, -goalSide),
				_goal, _dist, _turnRadius);

			// Pure reverse arc→straight: often shorter than turning around for side/rear-side goals.
			float posTol = PlanPosTolerance(_goal);
			ReedsSheppPathBuilder.TryAddCandidate(_candidates,
				BuildArcThenStraight(
					_from, _fromYaw, _goal.Position,
					_goal.RequiresPosePlanning ? _goal.YawDegrees : _fromYaw,
					_turnRadius, _wheelBase, TrajectoryGear.Reverse,
					_goal.RequiresPosePlanning, posTol),
				_goal, _dist, _turnRadius);
		}

		/// <summary>
		/// Open-field setup: roll straight 1.0–2.0m then CS/arc so body yaw can catch before max κ.
		/// </summary>
		private static VehicleTrajectory BuildLeadInThenArcApproach(
			Vector3 _from,
			float _fromYaw,
			Vector3 _to,
			float _turnRadius,
			float _wheelBase,
			float _posTol)
		{
			float dist = BicycleKinematics.FlatDistance(_from, _to);
			if (dist < 1.2f || dist > 8f)
				return null;

			float lead = Mathf.Clamp(dist * 0.4f, 1.0f, 2.0f);

			var leadPrim = BicycleKinematics.Integrate(
				_from, _fromYaw, 0f, TrajectoryGear.Forward, lead, _wheelBase, 0f);
			if (leadPrim.Samples == null || leadPrim.Samples.Count < 2)
				return null;

			Vector3 mid = leadPrim.EndPosition;
			float midYaw = leadPrim.EndYawDegrees;

			VehicleTrajectory rest = ReedsSheppPathBuilder.TryBuildBestCs(
				mid, midYaw, _to, _turnRadius, _wheelBase, TrajectoryGear.Forward);
			if (rest == null || !rest.IsValid)
			{
				rest = BuildArcThenStraight(
					mid, midYaw, _to, midYaw, _turnRadius, _wheelBase,
					TrajectoryGear.Forward, false, _posTol);
			}

			if (rest == null || !rest.IsValid || rest.PointCount < 2)
				return null;

			var pts = new List<TrajectoryPoint>(leadPrim.Samples.Count + rest.PointCount);
			for (int i = 0; i < leadPrim.Samples.Count; i++)
				pts.Add(leadPrim.Samples[i]);

			float baseArc = pts[pts.Count - 1].ArcLength;
			for (int i = 1; i < rest.PointCount; i++)
			{
				TrajectoryPoint p = rest.Points[i];
				pts.Add(new TrajectoryPoint(
					p.Position, p.YawDegrees, p.Curvature, p.Gear, baseArc + p.ArcLength, p.IsCusp));
			}

			float endErr = BicycleKinematics.FlatDistance(pts[pts.Count - 1].Position, _to);
			if (endErr > _posTol)
				return null;

			var t = new VehicleTrajectory();
			string tag = (rest.DebugReason != null && rest.DebugReason.Contains("cs"))
				? "leadin-cs-fwd"
				: "leadin-arc-fwd";
			t.Build(pts, lead + rest.TotalLength, 0, tag);
			return t.IsValid ? t : null;
		}

		private static VehicleTrajectory BuildFrontObliqueClearanceApproach(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _turnRadius,
			float _wheelBase,
			float _posTol)
		{
			Vector3 toGoal = _goal.Position - _from;
			toGoal.y = 0f;
			float dist = toGoal.magnitude;
			if (dist < 0.05f || dist > _turnRadius * 1.5f)
				return null;

			float side = Mathf.Sign(Vector3.Dot(toGoal, BicycleKinematics.YawToForward(_fromYaw + 90f)));
			if (Mathf.Abs(side) < 0.1f)
				side = 1f;

			float r = Mathf.Max(1f, _turnRadius);
			VehicleTrajectory best = null;
			float bestLen = float.MaxValue;
			float[] pulls = { 0.8f, 1.2f, 1.6f, 2.0f, 2.5f, 3.0f, 3.5f, 4.0f, 5.0f, 6.0f };
			float[] steerSigns = { -side, side, 0f };

			for (int si = 0; si < steerSigns.Length; si++)
			{
				float steer = steerSigns[si];
				for (int pi = 0; pi < pulls.Length; pi++)
				{
					float pull = pulls[pi];
					float curv = Mathf.Abs(steer) < 0.1f ? 0f : steer / r;
					var stage1 = BicycleKinematics.Integrate(
						_from, _fromYaw, curv, TrajectoryGear.Forward, pull, _wheelBase, 0f);

					VehicleTrajectory stage2 = ReedsSheppPathBuilder.TryBuildBestCs(
						stage1.EndPosition, stage1.EndYawDegrees, _goal.Position, r, _wheelBase,
						TrajectoryGear.Forward);
					if (stage2 == null || !stage2.IsValid)
					{
						stage2 = BuildArcThenStraight(
							stage1.EndPosition, stage1.EndYawDegrees, _goal.Position, stage1.EndYawDegrees,
							r, _wheelBase, TrajectoryGear.Forward, false, _posTol);
					}

					if (stage2 == null || !stage2.IsValid)
						continue;

					var pts = new List<TrajectoryPoint>(stage1.Samples);
					float baseArc = pts.Count > 0 ? pts[pts.Count - 1].ArcLength : 0f;
					for (int i = 1; i < stage2.PointCount; i++)
					{
						TrajectoryPoint p = stage2.Points[i];
						pts.Add(new TrajectoryPoint(
							p.Position, p.YawDegrees, p.Curvature, p.Gear, baseArc + p.ArcLength, p.IsCusp));
					}

					if (pts.Count < 2)
						continue;
					float posErr = BicycleKinematics.FlatDistance(pts[pts.Count - 1].Position, _goal.Position);
					if (posErr > _posTol)
						continue;
					if (!TrajectoryKinematicsValidator.Validate(
						    BuildTempTrajectory(pts, pts[pts.Count - 1].ArcLength, "arc-fwd"),
						    r, out _))
						continue;

					float len = pts[pts.Count - 1].ArcLength;
					if (len >= bestLen)
						continue;

					bestLen = len;
					string reason = (stage2.DebugReason ?? string.Empty).Contains("cs-")
						? "cs-fwd"
						: "arc-fwd";
					var t = new VehicleTrajectory();
					t.Build(pts, len, 0, reason);
					best = t;
				}
			}

			return best;
		}

		private static VehicleTrajectory BuildTempTrajectory(
			List<TrajectoryPoint> _pts, float _cost, string _reason)
		{
			var t = new VehicleTrajectory();
			t.Build(_pts, _cost, 0, _reason);
			return t;
		}

		private static VehicleTrajectory BuildTwoStageSideApproach(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _turnRadius,
			float _wheelBase,
			float _steerSign)
		{
			float r = Mathf.Max(1f, _turnRadius);
			float posTol = PlanPosTolerance(_goal);
			float endYaw = _goal.RequiresPosePlanning ? _goal.YawDegrees : _fromYaw;
			// Large trackable R (~6.4m) needs pulls past ~2.5m to free a forward finish onto a 2m side goal.
			float[] pulls = _goal.RequiresPosePlanning
				? new[] { 0.5f, 0.8f, 1.0f, 1.2f, 1.5f, 1.8f, 2.2f, 2.6f, 3.0f, 3.5f, 4.0f, 5.0f }
				: new[] { 0.5f, 0.8f, 1.0f, 1.2f, 1.6f, 2.0f, 2.5f, 3.0f, 3.5f, 4.0f, 5.0f };
			TrajectoryGear[] finishGears = new[] { TrajectoryGear.Forward, TrajectoryGear.Reverse };

			VehicleTrajectory best = null;
			float bestCost = float.MaxValue;

			for (int pi = 0; pi < pulls.Length; pi++)
			{
				float pull = Mathf.Clamp(pulls[pi], 0.5f, 5.5f);
				var stage1 = BicycleKinematics.Integrate(
					_from, _fromYaw, _steerSign / r, TrajectoryGear.Reverse, pull, _wheelBase, 0f);

				for (int gi = 0; gi < finishGears.Length; gi++)
				{
					TrajectoryGear finish = finishGears[gi];
					VehicleTrajectory stage2 = null;
					if (_goal.RequiresPosePlanning)
					{
						// ASA preserves heading better than arc+final-yaw (which drifts off pose).
						stage2 = BuildArcStraightArc(
							stage1.EndPosition, stage1.EndYawDegrees, _goal, r, _wheelBase, finish);
					}

					if (stage2 == null || !stage2.IsValid)
					{
						stage2 = BuildArcThenStraight(
							stage1.EndPosition, stage1.EndYawDegrees, _goal.Position, endYaw,
							r, _wheelBase, finish, _goal.RequiresPosePlanning, posTol);
					}

					if (stage2 == null || !stage2.IsValid)
						continue;

					var pts = new List<TrajectoryPoint>(stage1.Samples);
					if (pts.Count > 0)
					{
						TrajectoryPoint cusp = pts[pts.Count - 1];
						pts[pts.Count - 1] = new TrajectoryPoint(
							cusp.Position, cusp.YawDegrees, cusp.Curvature, cusp.Gear, cusp.ArcLength, true);
					}

					float baseArc = pts[pts.Count - 1].ArcLength;
					for (int i = 1; i < stage2.PointCount; i++)
					{
						TrajectoryPoint p = stage2.Points[i];
						pts.Add(new TrajectoryPoint(
							p.Position, p.YawDegrees, p.Curvature, p.Gear, baseArc + p.ArcLength, p.IsCusp));
					}

					TrajectoryPoint end = pts[pts.Count - 1];
					if (BicycleKinematics.FlatDistance(end.Position, _goal.Position) > posTol)
						continue;
					if (_goal.RequiresPosePlanning &&
					    Mathf.Abs(Mathf.DeltaAngle(end.YawDegrees, _goal.YawDegrees)) > _goal.HeadingToleranceDeg)
						continue;

					float cost = pull * c_ReversePenalty + stage2.Cost + c_GearSwitchPenalty;
					if (!_goal.RequiresPosePlanning && finish == TrajectoryGear.Forward)
						cost -= 0.5f;
					if (cost >= bestCost)
						continue;

					bestCost = cost;
					var t = new VehicleTrajectory();
					t.Build(pts, cost, 0, "two-stage-side");
					best = t;
				}
			}

			return best;
		}

		private static VehicleTrajectory BuildThreePoint(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _turnRadius,
			float _wheelBase)
		{
			Vector3 toGoal = _goal.Position - _from;
			toGoal.y = 0f;
			float side = Mathf.Sign(Vector3.Dot(toGoal, BicycleKinematics.YawToForward(_fromYaw + 90f)));
			if (Mathf.Abs(side) < 0.1f) side = 1f;

			float r = Mathf.Max(1f, _turnRadius);
			float dist = toGoal.magnitude;
			float leg = Mathf.Clamp(Mathf.Min(r * 0.45f, dist * 1.1f + 0.6f), 0.8f, 2.8f);

			var s1 = BicycleKinematics.Integrate(
				_from, _fromYaw, side / r, TrajectoryGear.Forward, leg, _wheelBase, 0f);
			var s2 = BicycleKinematics.Integrate(
				s1.EndPosition, s1.EndYawDegrees, -side / r, TrajectoryGear.Reverse, leg * 1.05f, _wheelBase, s1.Length);

			float endYaw = GoalUsesYaw(_goal) ? _goal.YawDegrees : s2.EndYawDegrees;
			var s3 = BuildArcThenStraight(
				s2.EndPosition, s2.EndYawDegrees, _goal.Position, endYaw, r, _wheelBase, TrajectoryGear.Forward,
				_goal.RequiresPosePlanning, PlanPosTolerance(_goal));
			if (s3 == null || !s3.IsValid)
				return null;

			var pts = new List<TrajectoryPoint>();
			AppendWithCusp(pts, s1.Samples, false);
			AppendWithCusp(pts, s2.Samples, true);
			float baseArc = pts[pts.Count - 1].ArcLength;
			for (int i = 1; i < s3.PointCount; i++)
			{
				TrajectoryPoint p = s3.Points[i];
				pts.Add(new TrajectoryPoint(
					p.Position, p.YawDegrees, p.Curvature, p.Gear, baseArc + p.ArcLength, p.IsCusp));
			}

			var t = new VehicleTrajectory();
			t.Build(pts, leg + leg * 1.05f * c_ReversePenalty + s3.Cost + c_GearSwitchPenalty * 2f, 0, "three-point");
			return t;
		}

		private static void AppendWithCusp(List<TrajectoryPoint> _dst, List<TrajectoryPoint> _src, bool _markCuspAtStart)
		{
			if (_src == null || _src.Count == 0)
				return;
			int start = _dst.Count == 0 ? 0 : 1;
			for (int i = start; i < _src.Count; i++)
			{
				TrajectoryPoint p = _src[i];
				bool cusp = _markCuspAtStart && i == start;
				_dst.Add(new TrajectoryPoint(p.Position, p.YawDegrees, p.Curvature, p.Gear, p.ArcLength, cusp || p.IsCusp));
			}
			if (_dst.Count > 0 && _markCuspAtStart)
			{
				// Ensure previous point marked cusp too
				int prev = _dst.Count - (_src.Count - start) - 1;
				if (prev >= 0)
				{
					TrajectoryPoint p = _dst[prev];
					_dst[prev] = new TrajectoryPoint(p.Position, p.YawDegrees, p.Curvature, p.Gear, p.ArcLength, true);
				}
			}
		}

		private static string FormatFailReason(LocalPlanningSession _s, SessionStats _session, float _bestPos)
		{
			float yawErr = _s.Goal.RequiresPosePlanning && _s.BestAny != null
				? Mathf.Abs(Mathf.DeltaAngle(_s.BestAny.Yaw, _s.Goal.YawDegrees))
				: 0f;
			string budget = _s.BudgetReason != null ? _s.BudgetReason : "none";
			return
				$"no path (bestPos={_bestPos:F2} yawErr={yawErr:F1} expanded={_s.Expanded} " +
				$"cand={_session.CandidatesGenerated} tried={_session.CandidatesTried} " +
				$"inv={_session.RejectedInvalidGeometry} tol={_session.RejectedTolerance} " +
				$"san={_session.RejectedSanitary} len={_session.RejectedLengthBudget} " +
				$"rs=ok{_session.RsValidCandidates}/f{_session.RsFormulasGenerated} budget={budget})";
		}

		private static VehicleTrajectory ReconstructPartial(Node _goal, float _wheelBase, float _stepLength)
		{
			var traj = Reconstruct(_goal, _wheelBase, _stepLength);
			if (traj == null || !traj.IsValid)
				return traj;

			var pts = new List<TrajectoryPoint>(traj.Points);
			var rebuilt = new VehicleTrajectory();
			rebuilt.Build(pts, traj.Cost, 0, "partial");
			return rebuilt;
		}

		private static VehicleTrajectory Reconstruct(Node _goal, float _wheelBase, float _stepLength)
		{
			var stack = new List<Node>(64);
			for (Node n = _goal; n != null; n = n.Parent)
				stack.Add(n);
			stack.Reverse();

			if (stack.Count < 2)
				return VehicleTrajectory.Invalid(stack.Count == 0 ? "empty lattice" : "degenerate lattice", 0);

			var pts = new List<TrajectoryPoint>(stack.Count * 4);
			Node root = stack[0];
			TrajectoryGear startGear = stack[1].Gear;
			float startCurv = stack[1].Curvature;
			pts.Add(new TrajectoryPoint(root.Position, root.Yaw, startCurv, startGear, 0f));

			for (int i = 1; i < stack.Count; i++)
			{
				Node prev = stack[i - 1];
				Node curr = stack[i];
				float segLen = curr.ArcLength - prev.ArcLength;
				if (segLen < 0.05f)
					segLen = Mathf.Max(_stepLength, BicycleKinematics.FlatDistance(prev.Position, curr.Position));
				segLen = Mathf.Max(0.05f, segLen);

				float baseArc = pts[pts.Count - 1].ArcLength;
				var prim = BicycleKinematics.Integrate(
					prev.Position, prev.Yaw, curr.Curvature, curr.Gear, segLen, _wheelBase, baseArc);

				if (i > 1 && curr.Gear != stack[i - 1].Gear && pts.Count > 0)
				{
					TrajectoryPoint cusp = pts[pts.Count - 1];
					pts[pts.Count - 1] = new TrajectoryPoint(
						cusp.Position, cusp.YawDegrees, cusp.Curvature, cusp.Gear, cusp.ArcLength, true);
				}

				for (int j = 1; j < prim.Samples.Count; j++)
					pts.Add(prim.Samples[j]);
			}

			var t = new VehicleTrajectory();
			t.Build(pts, _goal.G, 0, "lattice");
			return t;
		}

		private static VehicleTrajectory MergePath(
			Node _prefixEnd,
			VehicleTrajectory _suffix,
			float _wheelBase,
			float _stepLength)
		{
			var prefix = Reconstruct(_prefixEnd, _wheelBase, _stepLength);
			if (!prefix.IsValid || _suffix == null || !_suffix.IsValid)
				return null;

			var pts = new List<TrajectoryPoint>(prefix.Points);
			float baseArc = pts[pts.Count - 1].ArcLength;
			for (int i = 1; i < _suffix.PointCount; i++)
			{
				TrajectoryPoint p = _suffix.Points[i];
				pts.Add(new TrajectoryPoint(
					p.Position, p.YawDegrees, p.Curvature, p.Gear, baseArc + p.ArcLength, p.IsCusp));
			}

			NormalizeArcLengths(pts);
			var t = new VehicleTrajectory();
			t.Build(pts, _prefixEnd.G + _suffix.Cost, 0, "merged");
			return t;
		}

		private static void NormalizeArcLengths(List<TrajectoryPoint> _points)
		{
			if (_points == null || _points.Count == 0)
				return;

			float arc = 0f;
			for (int i = 0; i < _points.Count; i++)
			{
				TrajectoryPoint p = _points[i];
				if (i > 0)
					arc += BicycleKinematics.FlatDistance(_points[i - 1].Position, p.Position);
				_points[i] = new TrajectoryPoint(
					p.Position, p.YawDegrees, p.Curvature, p.Gear, arc, p.IsCusp);
			}
		}

		private static bool CanAffordExpensiveAnalytic(LocalPlanningSession _s)
		{
			if (_s == null || _s.MaxPlanDurationMs <= 0f)
				return true;
			// Tight total deadlines cannot absorb a 15–40 ms analytic sweep.
			if (_s.MaxPlanDurationMs <= 25f)
				return false;
			float remaining = _s.MaxPlanDurationMs - _s.AccumulatedCpuMs;
			return remaining >= 8f;
		}

		private static bool GoalUsesYaw(GoalPose _goal) =>
			_goal.RequiresPosePlanning || _goal.HasAdvisoryHeading;

		private static float ScoreToGoal(Node _n, GoalPose _goal)
		{
			float pos = BicycleKinematics.FlatDistance(_n.Position, _goal.Position);
			float yaw = _goal.RequiresPosePlanning
				? Mathf.Abs(Mathf.DeltaAngle(_n.Yaw, _goal.YawDegrees)) * 0.02f
				: _goal.HasAdvisoryHeading
					? Mathf.Abs(Mathf.DeltaAngle(_n.Yaw, _goal.YawDegrees)) * 0.005f
					: 0f;
			return pos + yaw;
		}

		private static long StateKey(
			Vector3 _pos,
			float _yaw,
			TrajectoryGear _gear,
			float _curvature)
		{
			int x = Mathf.RoundToInt(_pos.x / c_XyResolution);
			int z = Mathf.RoundToInt(_pos.z / c_XyResolution);
			int yawBin = Mathf.RoundToInt(BicycleKinematics.NormalizeYaw(_yaw) / (360f / c_YawBins)) % (int)c_YawBins;
			if (yawBin < 0) yawBin += (int)c_YawBins;
			int gear = _gear == TrajectoryGear.Reverse ? 1 : 0;
			// Curvature is part of the search state because steering-change cost and
			// the next reachable paths depend on it. Five primitive curvatures fit
			// comfortably in this signed fixed-point field.
			int curvatureBin = Mathf.Clamp(Mathf.RoundToInt(_curvature * 10000f), -2048, 2047) + 2048;
			return ((long)x & 0xFFFFF) |
			       (((long)z & 0xFFFFF) << 20) |
			       (((long)yawBin & 0x3F) << 40) |
			       ((long)gear << 46) |
			       (((long)curvatureBin & 0xFFF) << 47);
		}
	}
}
