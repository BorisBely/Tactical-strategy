using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	public enum PlanStepStatus
	{
		Pending,
		Ready,
		Failed
	}

	public readonly struct PlanStepResult
	{
		public readonly PlanStepStatus Status;
		public readonly VehicleTrajectory Trajectory;
		public readonly float StepDurationMs;
		public readonly int StepIndex;

		public PlanStepResult(PlanStepStatus _status, VehicleTrajectory _trajectory, float _stepMs, int _stepIndex)
		{
			Status = _status;
			Trajectory = _trajectory;
			StepDurationMs = _stepMs;
			StepIndex = _stepIndex;
		}

		public static PlanStepResult Pending(float _stepMs, int _stepIndex) =>
			new PlanStepResult(PlanStepStatus.Pending, null, _stepMs, _stepIndex);

		public static PlanStepResult Ready(VehicleTrajectory _traj, float _stepMs, int _stepIndex) =>
			new PlanStepResult(PlanStepStatus.Ready, _traj, _stepMs, _stepIndex);

		public static PlanStepResult Failed(float _stepMs, int _stepIndex) =>
			new PlanStepResult(PlanStepStatus.Failed, null, _stepMs, _stepIndex);
	}

	/// <summary>
	/// Resumable local pose planning state shared between frames.
	/// </summary>
	public sealed class LocalPlanningSession
	{
		internal enum Phase
		{
			Init,
			Analytic,
			Lattice,
			PostProcess,
			Complete
		}

		internal Phase CurrentPhase = Phase.Init;
		internal bool AnalyticComplete;
		internal List<VehicleTrajectory> AnalyticCandidates;
		internal int AnalyticEvalIndex;
		internal bool AnalyticCandidatesBuilt;
		internal bool AnalyticHeavyPending;
		/// <summary>0=side, 1=extended, 2=rs-family, 3=close-pose, 4=done</summary>
		internal int HeavyFamilyIndex;
		internal int AnalyticSampleIndex;
		internal List<(VehicleTrajectory traj, float selectCost, float posErr)> AnalyticValid;
		internal bool LatticeInitialized;
		internal bool PostProcessStarted;

		internal Vector3 StartPos;
		internal float StartYaw;
		internal GoalPose Goal;
		internal VehicleKinematicsProfile Profile;
		internal PlanningObstacleSnapshot Snapshot;
		internal bool AllowReverse;
		internal float StepLength;
		internal float MaxPlanDurationMs;

		internal float TurnRadius;
		internal float WheelBase;
		internal float Step;
		internal float StartDist;

		internal LocalPosePlanner.SessionStats Session;
		internal int AnalyticShots;
		internal bool BudgetTerminated;
		internal string BudgetReason;

		internal SortedSet<LocalPosePlanner.Node> Open;
		internal Dictionary<long, float> BestG;
		internal int NodeId;
		internal int Expanded;
		internal int Generated;
		internal LocalPosePlanner.Node Start;
		internal LocalPosePlanner.Node BestGoal;
		internal LocalPosePlanner.Node DeferredForwardGoal;
		internal LocalPosePlanner.Node BestAny;
		internal float BestAnyScore;

		/// <summary>Sum of StepPlan slice durations only (excludes idle time between frames).</summary>
		internal float AccumulatedCpuMs;
		internal int StepIndex;
		internal int PlanningFrameCount;
		internal float LastSliceMs;
		internal float MaxSliceMs;
		internal VehicleTrajectory ResultTrajectory;

		public bool IsActive => CurrentPhase != Phase.Complete;
		public float TotalPlanCpuMs => AccumulatedCpuMs;
		public float SessionMaxSliceMs => MaxSliceMs;
		public string PhaseName => CurrentPhase.ToString();
		public int AnalyticCandidateCount => AnalyticCandidates != null ? AnalyticCandidates.Count : 0;
		public int AnalyticValidCount => AnalyticValid != null ? AnalyticValid.Count : 0;
	}
}
