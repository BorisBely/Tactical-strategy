using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// #14B.1–14B.7 Play: perception / rank / hold-decay / arm fatigue / combat integration.
/// Report: Assets/_Docs/Logs/Tests/Readiness_LAST.txt
/// </summary>
[DefaultExecutionOrder(66)]
[DisallowMultipleComponent]
public sealed class ReadinessRuntimeSmoke : MonoBehaviour
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private bool m_ExitPlayModeWhenDone;
	#endregion

	#region Private Fields
	private readonly StringBuilder m_Report = new StringBuilder(8192);
	private int m_PassCount;
	private int m_FailCount;
	private GameObject m_AiGo;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunReadiness;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunReadiness)
			return;
		if (FindAnyObjectByType<ReadinessRuntimeSmoke>() != null)
			return;
		var go = new GameObject("ReadinessRuntimeSmoke");
		go.AddComponent<ReadinessRuntimeSmoke>();
	}

	private void Start()
	{
		if (!WillRunOnStart)
			return;
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		DestroyAi();
		if (DetectionHarnessPlayMode.RunReadiness)
			DetectionHarnessPlayMode.ResetFlags();
	}
	#endregion

	#region Public Methods
	public void RunFromEditor()
	{
		if (!isActiveAndEnabled)
			return;
		StopAllCoroutines();
		StartCoroutine(RunSuite());
	}
	#endregion

	#region Private Methods
	private IEnumerator RunSuite()
	{
		yield return null;

		m_Report.Length = 0;
		m_PassCount = 0;
		m_FailCount = 0;
		AppendLine("STAGE 14B.1–14B.7 — READINESS / FATIGUE / COMBAT INTEGRATION");
		AppendLine("=============================================================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("Logical machine + pose + live perception + rank speeds + hold decay + arm fatigue.");
		AppendLine("AimTime / RecoilControl / TurnTime only. Not Fire. Not G6. Not Cover. Not Movement.");
		AppendLine("---");

		RunInitRanks();
		RunGunshotRanks();
		RunDirectAim();
		RunNoIntermediate();
		RunDurationOrder();
		RunDecayLadder();
		RunHysteresis();
		RunRetrigger();
		RunStimulusPriority();
		RunAimIsNotFire();
		RunFatiguePlaceholder();
		RunCycleGunshotThenVisible();
		RunMapper();
		RunWorldHookFrozenLayers();
		RunPoseIntegration();
		yield return RunLivePerceptionIntegration();
		RunRankBalance();
		RunPersistenceBalance();
		RunArmFatigue();
		RunCombatIntegration();

		Finish();
		yield break;
	}

	private void RunInitRanks()
	{
		Check("P1_InitRanks",
			ResetState(ReadinessRankKind.Recruit) == ReadinessState.NotReady &&
			ResetState(ReadinessRankKind.Soldier) == ReadinessState.Patrol &&
			ResetState(ReadinessRankKind.Corporal) == ReadinessState.Patrol &&
			ResetState(ReadinessRankKind.Veteran) == ReadinessState.Patrol &&
			ResetState(ReadinessRankKind.Elite) == ReadinessState.Patrol,
			"init");
	}

	private void RunGunshotRanks()
	{
		Check("P2_GunshotRanks",
			After(ReadinessRankKind.Recruit, ReadinessStimulus.GunshotHeard) == ReadinessState.LowReady &&
			After(ReadinessRankKind.Soldier, ReadinessStimulus.GunshotHeard) == ReadinessState.LowReady &&
			After(ReadinessRankKind.Corporal, ReadinessStimulus.GunshotHeard) == ReadinessState.HighReady &&
			After(ReadinessRankKind.Veteran, ReadinessStimulus.GunshotHeard) == ReadinessState.HighReady &&
			After(ReadinessRankKind.Elite, ReadinessStimulus.GunshotHeard) == ReadinessState.HighReady,
			"gunshot");
	}

	private void RunDirectAim()
	{
		var low = new ReadinessController();
		low.Reset(ReadinessRankKind.Soldier, 0f);
		low.Tick(0.1f, ReadinessStimulus.GunshotHeard);
		low.Tick(0.3f, ReadinessStimulus.HostileVisible);

		var high = new ReadinessController();
		high.Reset(ReadinessRankKind.Corporal, 0f);
		high.Tick(0.1f, ReadinessStimulus.GunshotHeard);
		high.Tick(0.3f, ReadinessStimulus.HostileVisible);

		var pre = new ReadinessController();
		pre.Reset(ReadinessRankKind.Soldier, 0f);
		pre.Tick(0.2f, ReadinessStimulus.HostileVisible);
		pre.Tick(2f, ReadinessStimulus.CombatActivityExpired);
		pre.Tick(2.2f, ReadinessStimulus.HostileVisible);

		Check("P3_DirectAim",
			After(ReadinessRankKind.Recruit, ReadinessStimulus.HostileVisible) == ReadinessState.Aim &&
			After(ReadinessRankKind.Soldier, ReadinessStimulus.HostileVisible) == ReadinessState.Aim &&
			low.CurrentState == ReadinessState.Aim &&
			high.CurrentState == ReadinessState.Aim &&
			pre.CurrentState == ReadinessState.Aim,
			low.CurrentState + " " + high.CurrentState + " " + pre.CurrentState);
	}

	private void RunNoIntermediate()
	{
		var controller = new ReadinessController();
		controller.Reset(ReadinessRankKind.Soldier, 0f);
		controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
		bool oneHop = controller.Context.PreviousState == ReadinessState.Patrol
			&& controller.CurrentState == ReadinessState.Aim
			&& controller.TransitionRequestCount == 1
			&& !ReadinessLog.ContainsTransition(controller.LogLines, ReadinessState.Patrol, ReadinessState.LowReady);
		Check("P4_Patrol_Hostile_OneTransition", oneHop, controller.LastLogPayload);
	}

	private void RunDurationOrder()
	{
		ReadinessProfile soldier = ReadinessProfile.ForRank(ReadinessRankKind.Soldier);
		float high = ReadinessMath.AimTransitionDuration(ReadinessState.HighReady, in soldier);
		float low = ReadinessMath.AimTransitionDuration(ReadinessState.LowReady, in soldier);
		float patrol = ReadinessMath.AimTransitionDuration(ReadinessState.Patrol, in soldier);
		Check("P5_Duration_High_lt_Low_lt_Patrol", high < low && low < patrol,
			high + " " + low + " " + patrol);
	}

	private void RunDecayLadder()
	{
		var controller = new ReadinessController();
		controller.Reset(ReadinessRankKind.Soldier, 0f);
		controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
		controller.Tick(1.3f, ReadinessStimulus.None);
		bool pre = controller.CurrentState == ReadinessState.PreAim;
		controller.Tick(1.4f, ReadinessStimulus.None);
		bool held = controller.CurrentState == ReadinessState.PreAim;
		controller.Tick(2.4f, ReadinessStimulus.None);
		bool heard = controller.CurrentState == ReadinessState.LowReady;
		controller.Tick(3.5f, ReadinessStimulus.None);
		bool calm = controller.CurrentState == ReadinessState.Patrol;
		Check("P6_Decay_Ladder", pre && held && heard && calm,
			controller.CurrentState + " " + controller.Context.LastChangeReason);
	}

	private void RunHysteresis()
	{
		var controller = new ReadinessController();
		controller.Reset(ReadinessRankKind.Soldier, 0f);
		controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
		controller.Tick(0.3f, ReadinessStimulus.HostileLost);
		controller.Tick(0.7f, ReadinessStimulus.None);
		Check("P7_HostileLost_Hysteresis", controller.CurrentState == ReadinessState.Aim, controller.CurrentState.ToString());
	}

	private void RunRetrigger()
	{
		var controller = new ReadinessController();
		controller.Reset(ReadinessRankKind.Soldier, 0f);
		controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
		controller.Tick(2f, ReadinessStimulus.CombatActivityExpired);
		controller.Tick(2.2f, ReadinessStimulus.HostileVisible);
		Check("P8_Retrigger_Aim", controller.CurrentState == ReadinessState.Aim, controller.CurrentState.ToString());
	}

	private void RunStimulusPriority()
	{
		var controller = new ReadinessController();
		controller.Reset(ReadinessRankKind.Soldier, 0f);
		var frame = new ReadinessFrame { HostileVisible = true, GunshotHeard = true };
		controller.Tick(0.2f, in frame);
		Check("P9_Priority_Aim", controller.CurrentState == ReadinessState.Aim, controller.CurrentState.ToString());
	}

	private void RunAimIsNotFire()
	{
		var controller = new ReadinessController();
		controller.Reset(ReadinessRankKind.Soldier, 0f);
		ReadinessDecision decision = controller.Tick(0.2f, ReadinessStimulus.HostileVisible);
		Check("P10_Aim_NotFire", decision.State == ReadinessState.Aim && !decision.RequestsFire && !controller.RequestsFire,
			"RequestsFire");
	}

	private void RunFatiguePlaceholder()
	{
		var a = new ReadinessController();
		a.Reset(ReadinessRankKind.Soldier, 0f);
		a.SetArmFatigue(0f, 1f);
		a.Tick(0.2f, ReadinessStimulus.HostileVisible);
		var b = new ReadinessController();
		b.Reset(ReadinessRankKind.Soldier, 0f);
		b.SetArmFatigue(1f, 0.2f);
		b.Tick(0.2f, ReadinessStimulus.HostileVisible);
		Check("P11_Fatigue_NoEffect", a.CurrentState == b.CurrentState && a.CurrentState == ReadinessState.Aim,
			b.CurrentState.ToString());
	}

	private void RunCycleGunshotThenVisible()
	{
		var controller = new ReadinessController();
		controller.Reset(ReadinessRankKind.Soldier, 0f);
		bool spawn = controller.CurrentState == ReadinessState.Patrol
			&& controller.LastLogPayload.IndexOf("state=Patrol reason=Initial", StringComparison.Ordinal) >= 0;
		controller.Tick(0.2f, ReadinessStimulus.GunshotHeard);
		bool heard = controller.CurrentState == ReadinessState.LowReady;
		controller.Tick(0.4f, ReadinessStimulus.HostileVisible);
		bool aim = controller.CurrentState == ReadinessState.Aim;
		controller.Tick(0.5f, ReadinessStimulus.HostileLost);
		bool stillAim = controller.CurrentState == ReadinessState.Aim;
		controller.Tick(1.6f, ReadinessStimulus.None);
		bool pre = controller.CurrentState == ReadinessState.PreAim;
		controller.Tick(2.7f, ReadinessStimulus.None);
		bool ready = controller.CurrentState == ReadinessState.LowReady;
		controller.Tick(3.8f, ReadinessStimulus.None);
		bool calm = controller.CurrentState == ReadinessState.Patrol;
		Check("P12_Cycle_SpawnGunshotAimDecayCalm", spawn && heard && aim && stillAim && pre && ready && calm,
			controller.CurrentState + " " + controller.LastLogPayload);
	}

	private void RunMapper()
	{
		AIPerceptionFrame hostile = HostileVisibleFrame();
		ReadinessFrame mapped = ReadinessStimulusMath.FromPerception(in hostile, false, false);
		AIPerceptionFrame gunshot = GunshotFrame();
		ReadinessFrame heard = ReadinessStimulusMath.FromPerception(in gunshot, false, false);
		Check("P13_Mapper",
			mapped.HostileVisible && !mapped.GunshotHeard && heard.GunshotHeard && !heard.HostileVisible,
			"mapper");
	}

	private void RunWorldHookFrozenLayers()
	{
		try
		{
			DestroyAi();
			m_AiGo = new GameObject("ReadinessAi");
			UnitAIController ai = m_AiGo.AddComponent<UnitAIController>();
			ai.EnsureStarted();
			ai.Tick(0.05f);

			TacticalCoverDecisionKind coverBefore = ai.LastTacticalCoverDecision.Decision;
			bool moveBefore = ai.LastTacticalMovement.HasRoute;
			UnitAIState stateBefore = ai.CurrentState;
			ReadinessState readinessBefore = ai.Readiness.CurrentState;

			ai.Tick(0.05f);
			CombatReadinessController combat = m_AiGo.GetComponent<CombatReadinessController>();

			Check("P14_WorldFrozenLayers",
				ai.CurrentState == UnitAIState.Idle &&
				ai.CurrentState == stateBefore &&
				ai.Readiness.CurrentState == ReadinessState.Patrol &&
				ai.Readiness.CurrentState == readinessBefore &&
				ai.LastTacticalCoverDecision.Decision == coverBefore &&
				ai.LastTacticalMovement.HasRoute == moveBefore &&
				combat != null &&
				combat.LastAppliedIntent == CombatIntent.Hold &&
				!combat.ReadinessRequested &&
				!ai.Readiness.RequestsFire,
				ai.CurrentState + " " + ai.Readiness.CurrentState);

			ai.SetPerceptionFrame(HostileVisibleFrame());
			bool direct = ai.Readiness.CurrentState == ReadinessState.Patrol
				&& ai.Readiness.Context.HasPendingTransition
				&& ai.Readiness.Context.TransitionTo == ReadinessState.Aim
				&& ai.CurrentState == UnitAIState.Idle;
			Check("P15_World_PatrolToAimRequest", direct,
				ai.Readiness.CurrentState + " pending=" + ai.Readiness.Context.HasPendingTransition +
				" to=" + ai.Readiness.Context.TransitionTo + " ai=" + ai.CurrentState);
			if (combat != null)
			{
				combat.ApplyNow();
				Check("P16_World_PendingAimPose",
					combat.LastPoseRequest.Pose == WeaponPoseState.Aiming &&
					combat.LastAppliedIntent == CombatIntent.Hold &&
					!combat.LastPoseRequest.RequestsFire &&
					!combat.LastPoseRequest.ChangesG6,
					combat.LastPoseRequest.Pose + " intent=" + combat.LastAppliedIntent);
			}
			else
			{
				Check("P16_World_PendingAimPose", false, "CombatReadiness missing");
			}
		}
		catch (Exception exception)
		{
			Check("P14_WorldFrozenLayers", false, exception.Message);
			Check("P15_World_PatrolToAimRequest", false, exception.Message);
			Check("P16_World_PendingAimPose", false, exception.Message);
		}

		DestroyAi();
	}

	private void RunPoseIntegration()
	{
		Check("P17_PoseMapping",
			ReadinessPoseMath.ToPose(ReadinessState.NotReady) == WeaponPoseState.NotReady &&
			ReadinessPoseMath.ToPose(ReadinessState.Patrol) == WeaponPoseState.NotReadyPatrol &&
			ReadinessPoseMath.ToPose(ReadinessState.LowReady) == WeaponPoseState.LowReady &&
			ReadinessPoseMath.ToPose(ReadinessState.HighReady) == WeaponPoseState.HighReady &&
			ReadinessPoseMath.ToPose(ReadinessState.PreAim) == WeaponPoseState.PreAim &&
			ReadinessPoseMath.ToPose(ReadinessState.Aim) == WeaponPoseState.Aiming,
			"mapping");

		ReadinessPoseRequest recruitShot = PoseAfter(ReadinessRankKind.Recruit, ReadinessStimulus.GunshotHeard);
		ReadinessPoseRequest soldierShot = PoseAfter(ReadinessRankKind.Soldier, ReadinessStimulus.GunshotHeard);
		ReadinessPoseRequest corporalShot = PoseAfter(ReadinessRankKind.Corporal, ReadinessStimulus.GunshotHeard);
		ReadinessPoseRequest eliteShot = PoseAfter(ReadinessRankKind.Elite, ReadinessStimulus.GunshotHeard);
		Check("P18_GunshotPoses",
			recruitShot.Pose == WeaponPoseState.LowReady &&
			soldierShot.Pose == WeaponPoseState.LowReady &&
			corporalShot.Pose == WeaponPoseState.HighReady &&
			eliteShot.Pose == WeaponPoseState.HighReady,
			corporalShot.Pose.ToString());

		var cycle = new ReadinessController();
		cycle.Reset(ReadinessRankKind.Soldier, 0f);
		bool spawnPose = cycle.PoseRequest.Pose == WeaponPoseState.NotReadyPatrol;
		cycle.Tick(0.2f, ReadinessStimulus.GunshotHeard);
		bool heardPose = cycle.PoseRequest.Pose == WeaponPoseState.LowReady;
		cycle.Tick(0.4f, ReadinessStimulus.HostileVisible);
		bool aimPose = cycle.PoseRequest.Pose == WeaponPoseState.Aiming && !cycle.PoseRequest.RequestsFire;
		cycle.Tick(0.5f, ReadinessStimulus.HostileLost);
		bool stillAim = cycle.PoseRequest.Pose == WeaponPoseState.Aiming;
		cycle.Tick(1.6f, ReadinessStimulus.None);
		bool pre = cycle.PoseRequest.Pose == WeaponPoseState.PreAim;
		cycle.Tick(2.7f, ReadinessStimulus.None);
		bool ready = cycle.PoseRequest.Pose == WeaponPoseState.LowReady;
		cycle.Tick(3.8f, ReadinessStimulus.None);
		bool calm = cycle.PoseRequest.Pose == WeaponPoseState.NotReadyPatrol;
		Check("P19_CyclePoses", spawnPose && heardPose && aimPose && stillAim && pre && ready && calm,
			cycle.PoseRequest.Pose.ToString());

		Check("P20_AimNotFireNotG6",
			PoseAfter(ReadinessRankKind.Soldier, ReadinessStimulus.HostileVisible).Pose == WeaponPoseState.Aiming &&
			!PoseAfter(ReadinessRankKind.Soldier, ReadinessStimulus.HostileVisible).RequestsFire &&
			!PoseAfter(ReadinessRankKind.Soldier, ReadinessStimulus.HostileVisible).ChangesG6,
			"aim");

		ReadinessPoseRequest life = ReadinessPoseMath.Incapacitated();
		Check("P21_LifeGatePose",
			life.Pose == WeaponPoseState.NotReady && life.FromLifeGate && !life.RequestsFire,
			life.Pose.ToString());

		var pending = new ReadinessController();
		pending.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
		pending.Tick(0.1f, ReadinessStimulus.HostileVisible);
		ReadinessPoseRequest pendingAim = pending.PoseRequest;
		Check("P22_LogicalSkipPhysicalAiming",
			pending.CurrentState == ReadinessState.Patrol &&
			pendingAim.State == ReadinessState.Aim &&
			pendingAim.Pose == WeaponPoseState.Aiming &&
			ReadinessPoseMath.LogicalSkipsIntermediates(ReadinessState.Patrol, ReadinessState.Aim) &&
			ReadinessPoseMath.PhysicalMayInterpolate(ReadinessState.Patrol, ReadinessState.Aim),
			pending.CurrentState + " " + pendingAim.Pose);

		GameObject go = new GameObject("ReadinessPosePlay");
		try
		{
			UnitAIController ai = go.AddComponent<UnitAIController>();
			ai.EnsureStarted();
			CombatReadinessController combat = go.GetComponent<CombatReadinessController>();
			combat.ApplyNow();
			bool hold = combat.LastAppliedIntent == CombatIntent.Hold &&
			            combat.LastPoseRequest.Pose == WeaponPoseState.NotReadyPatrol;
			ai.TrySetUseOfForcePolicy(UseOfForceLevel.MissionCombat);
			ai.TryApplyCommand(UnitAICommand.Defense(
				UnitAIStateContext.ForDefense(Vector3.zero, Vector3.zero, 10f, Vector3.forward)));
			ai.SetPerceptionFrame(HostileVisibleFrame());
			ai.Tick(0.05f);
			EngagementDecisionController g6 = go.GetComponent<EngagementDecisionController>();
			EngagementDecision g6Before = g6 != null ? g6.CurrentDecision : EngagementDecision.None;
			combat.ApplyNow();
			bool engage = combat.LastAppliedIntent == CombatIntent.Engage &&
			              combat.LastPoseRequest.Pose == WeaponPoseState.Aiming &&
			              combat.LastPoseRequest.Mode != WeaponPoseMode.Auto &&
			              !combat.LastPoseRequest.RequestsFire;
			bool g6Same = g6 == null || g6.CurrentDecision == g6Before;
			bool log = combat.LastPoseLogPayload.IndexOf("pose=Aiming", StringComparison.Ordinal) >= 0;
			Check("P23_HoldEngagePoseSplit",
				hold && engage && g6Same && log && go.GetComponent<UnitWeaponFireController>() == null,
				combat.LastAppliedIntent + " " + combat.LastPoseRequest.Pose + " g6=" + (g6 != null));
		}
		finally
		{
			UnityEngine.Object.Destroy(go);
		}
	}

	private IEnumerator RunLivePerceptionIntegration()
	{
		DestroyAi();
		m_AiGo = new GameObject("ReadinessLiveAi");
		UnitAIController ai = m_AiGo.AddComponent<UnitAIController>();
		ai.EnsureStarted();
		CombatReadinessController combat = m_AiGo.GetComponent<CombatReadinessController>();

		bool spawnPatrol = ai.Readiness.CurrentState == ReadinessState.Patrol &&
		                   ai.CurrentCombatIntent == CombatIntent.Hold;
		Check("P24_LiveSpawnPatrol", spawnPatrol, ai.Readiness.CurrentState.ToString());

		ai.SetPerceptionFrame(HostileVisibleFrame());
		yield return HoldPerception(ai, HostileVisibleFrame(), 0.7f);
		if (combat == null)
		{
			Check("P25_LiveHostileVisibleAim", false, "CombatReadiness missing");
		}
		else
		{
			combat.ApplyNow();
			bool directAim = ai.Readiness.CurrentState == ReadinessState.Aim &&
			                 combat.LastPoseRequest.Pose == WeaponPoseState.Aiming &&
			                 ai.CurrentCombatIntent == CombatIntent.Hold &&
			                 !ai.Readiness.RequestsFire;
			Check("P25_LiveHostileVisibleAim",
				directAim,
				ai.Readiness.CurrentState + " pose=" + combat.LastPoseRequest.Pose + " intent=" + ai.CurrentCombatIntent);
		}

		DestroyAi();
		m_AiGo = new GameObject("ReadinessLiveCycle");
		ai = m_AiGo.AddComponent<UnitAIController>();
		ai.EnsureStarted();
		ai.Readiness.Reset(ReadinessProfile.Instant(ReadinessRankKind.Soldier), Time.time);
		combat = m_AiGo.GetComponent<CombatReadinessController>();

		bool cycleSpawn = ai.Readiness.CurrentState == ReadinessState.Patrol;
		ai.SetPerceptionFrame(GunshotFrame());
		bool heard = ai.Readiness.CurrentState == ReadinessState.LowReady;
		ai.SetPerceptionFrame(HostileVisibleFrame());
		bool aimed = ai.Readiness.CurrentState == ReadinessState.Aim;
		ai.SetPerceptionFrame(AIPerceptionFrame.Empty);
		bool stillAim = ai.Readiness.CurrentState == ReadinessState.Aim;
		yield return HoldPerception(ai, AIPerceptionFrame.Empty, 1.1f);
		bool pre = ai.Readiness.CurrentState == ReadinessState.PreAim;
		yield return HoldPerception(ai, AIPerceptionFrame.Empty, 1.1f);
		bool ready = ai.Readiness.CurrentState == ReadinessState.LowReady;
		yield return HoldPerception(ai, AIPerceptionFrame.Empty, 1.1f);
		bool calm = ai.Readiness.CurrentState == ReadinessState.Patrol;
		Check("P26_LiveCycleGunshotAimDecay",
			cycleSpawn && heard && aimed && stillAim && pre && ready && calm,
			ai.Readiness.CurrentState + " heard=" + heard + " aim=" + aimed + " pre=" + pre);

		DestroyAi();
		m_AiGo = new GameObject("ReadinessLiveEngage");
		ai = m_AiGo.AddComponent<UnitAIController>();
		ai.EnsureStarted();
		ai.TryApplyCommand(UnitAICommand.Attack(
			UnitAIStateContext.ForAttack(Vector3.forward, Vector3.forward)));
		ai.Tick(0.05f);
		Check("P27_LiveAttackDoesNotAim",
			ai.CurrentState == UnitAIState.Attack &&
			ai.CurrentCombatIntent == CombatIntent.Hold &&
			ai.Readiness.CurrentState == ReadinessState.Patrol,
			ai.CurrentState + " " + ai.CurrentCombatIntent + " " + ai.Readiness.CurrentState);

		ai.SetPerceptionFrame(HostileVisibleFrame());
		EngagementDecisionController g6 = m_AiGo.GetComponent<EngagementDecisionController>();
		EngagementDecision g6Before = g6 != null ? g6.CurrentDecision : EngagementDecision.None;
		if (g6 != null)
			g6.RefreshDecisionNow();
		combat = m_AiGo.GetComponent<CombatReadinessController>();
		combat.ApplyNow();
		bool noFire = !ai.Readiness.RequestsFire &&
		              !combat.LastPoseRequest.RequestsFire &&
		              m_AiGo.GetComponent<UnitWeaponFireController>() == null &&
		              (g6 == null || g6.CurrentDecision != EngagementDecision.Fire);
		Check("P28_LiveAimNotG6Fire",
			(ai.Readiness.CurrentState == ReadinessState.Aim ||
			 (ai.Readiness.Context.HasPendingTransition &&
			  ai.Readiness.Context.TransitionTo == ReadinessState.Aim)) &&
			noFire &&
			(g6 == null || g6.CurrentDecision == g6Before || g6.CurrentDecision != EngagementDecision.Fire),
			"g6=" + (g6 != null ? g6.CurrentDecision.ToString() : "none"));

		int changes = ai.Readiness.Context.ChangeCount;
		string log = ai.Readiness.LastLogPayload;
		ai.NotifyLifeState(UnitLifeState.Unconscious);
		ai.SetPerceptionFrame(GunshotFrame());
		Check("P29_LiveLifeGateFreeze",
			!ai.Readiness.Allowed &&
			ai.Readiness.Context.ChangeCount == changes &&
			ai.Readiness.LastLogPayload == log,
			"allowed=" + ai.Readiness.Allowed + " changes=" + ai.Readiness.Context.ChangeCount);

		DestroyAi();
		m_AiGo = new GameObject("ReadinessLiveActivity");
		ai = m_AiGo.AddComponent<UnitAIController>();
		ai.EnsureStarted();
		ai.ImmediateThreat = true;
		ai.Tick(0.05f);
		Check("P30_LiveCombatActivityHold",
			ai.Readiness.CurrentState == ReadinessState.Patrol && ai.Readiness.HasCombatActivity,
			ai.Readiness.CurrentState.ToString());

		bool searchIndependent = UnitAISearchDecision.ShouldStartSearch(UnitAIState.Defense, GunshotFrame());
		Check("P31_GunshotSearchIndependent", searchIndependent, "search");
		DestroyAi();
	}

	private void RunRankBalance()
	{
		float recruitPatrol = PatrolAim(ReadinessRankKind.Recruit);
		float soldierPatrol = PatrolAim(ReadinessRankKind.Soldier);
		float corporalPatrol = PatrolAim(ReadinessRankKind.Corporal);
		float veteranPatrol = PatrolAim(ReadinessRankKind.Veteran);
		float elitePatrol = PatrolAim(ReadinessRankKind.Elite);
		Check("P32_PatrolToAimOrder",
			elitePatrol < veteranPatrol && veteranPatrol < corporalPatrol &&
			corporalPatrol < soldierPatrol && soldierPatrol < recruitPatrol,
			elitePatrol + " " + veteranPatrol + " " + corporalPatrol + " " + soldierPatrol + " " + recruitPatrol);

		ReadinessProfile elite = ReadinessProfile.ForRank(ReadinessRankKind.Elite);
		float eliteHigh = ReadinessMath.AimTransitionDuration(ReadinessState.HighReady, in elite);
		float eliteLow = ReadinessMath.AimTransitionDuration(ReadinessState.LowReady, in elite);
		Check("P33_EliteHighLtLowLtPatrol",
			eliteHigh < eliteLow && eliteLow < elitePatrol,
			eliteHigh + " " + eliteLow + " " + elitePatrol);

		float recruitReady = ReadinessMath.ReadyTransitionDuration(ReadinessProfile.ForRank(ReadinessRankKind.Recruit));
		float eliteReady = ReadinessMath.ReadyTransitionDuration(in elite);
		Check("P34_GunshotReadyOrder", recruitReady > eliteReady, recruitReady + " " + eliteReady);

		float recruitHold = ReadinessMath.EffectiveCalmDownDelay(ReadinessProfile.ForRank(ReadinessRankKind.Recruit));
		float eliteHold = ReadinessMath.EffectiveCalmDownDelay(in elite);
		Check("P35_CalmHoldOrder", eliteHold > recruitHold, eliteHold + " " + recruitHold);

		bool all = true;
		string detail = string.Empty;
		ReadinessRankKind[] ranks =
		{
			ReadinessRankKind.Recruit,
			ReadinessRankKind.Soldier,
			ReadinessRankKind.Corporal,
			ReadinessRankKind.Veteran,
			ReadinessRankKind.Elite
		};
		for (int i = 0; i < ranks.Length; i++)
		{
			ReadinessRankKind rank = ranks[i];
			ReadinessProfile profile = ReadinessProfile.ForRank(rank);
			var controller = new ReadinessController();
			controller.Reset(profile, 0f);
			controller.Tick(0f, ReadinessStimulus.GunshotHeard);
			bool ready = AdvanceTo(controller, profile.GunshotState, ReadinessStimulus.GunshotHeard);
			controller.Tick(controller.Context.StateEnterTime + 0.01f, ReadinessStimulus.HostileVisible);
			bool aim = AdvanceTo(controller, ReadinessState.Aim, ReadinessStimulus.HostileVisible);
			if (!ready || !aim)
			{
				all = false;
				detail += rank + " ready=" + ready + " aim=" + aim +
				          " state=" + controller.CurrentState + " ";
			}
		}

		Check("P36_FiveRankSameStimulus", all, string.IsNullOrEmpty(detail) ? "five ranks" : detail);

		var fatZero = new ReadinessController();
		fatZero.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
		fatZero.SetArmFatigue(0f, 1f);
		fatZero.Tick(0.01f, ReadinessStimulus.HostileVisible);
		var fatFull = new ReadinessController();
		fatFull.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
		fatFull.SetArmFatigue(1f, 0.2f);
		fatFull.Tick(0.01f, ReadinessStimulus.HostileVisible);
		Check("P37_FatigueNoEffectForRank",
			fatZero.LastRequest.Duration == fatFull.LastRequest.Duration &&
			fatZero.LastRequest.Duration > 0f &&
			!ReadinessMath.FatigueAffectsResult(),
			fatZero.LastRequest.Duration.ToString("0.###"));

		var logged = new ReadinessController();
		logged.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Veteran), 0f);
		logged.Tick(0.01f, ReadinessStimulus.HostileVisible);
		Check("P38_LogRankFields",
			logged.LastTransitionPayload.IndexOf("rank=Veteran", StringComparison.Ordinal) >= 0 &&
			logged.LastTransitionPayload.IndexOf("duration=", StringComparison.Ordinal) >= 0 &&
			logged.LastTransitionPayload.IndexOf("profileDuration=", StringComparison.Ordinal) >= 0 &&
			logged.LastTransitionPayload.IndexOf("rankModifier=", StringComparison.Ordinal) >= 0 &&
			logged.LastTransitionPayload.IndexOf("from=Patrol", StringComparison.Ordinal) >= 0,
			logged.LastTransitionPayload);

		float eliteSec = SecondsUntil(
			ReadinessRankKind.Elite,
			ReadinessStimulus.HostileVisible,
			ReadinessState.Aim,
			ReadinessStimulus.HostileVisible);
		float recruitSec = SecondsUntil(
			ReadinessRankKind.Recruit,
			ReadinessStimulus.HostileVisible,
			ReadinessState.Aim,
			ReadinessStimulus.HostileVisible);
		Check("P39_SecondsEliteFasterRecruit", eliteSec < recruitSec, eliteSec + " " + recruitSec);

		var ladder = new ReadinessController();
		ladder.Reset(ReadinessRankKind.Soldier, 0f);
		ladder.Tick(0.2f, ReadinessStimulus.HostileVisible);
		ladder.Tick(2f, ReadinessStimulus.CombatActivityExpired);
		bool pre = ladder.CurrentState == ReadinessState.PreAim;
		ladder.Tick(4f, ReadinessStimulus.CombatActivityExpired);
		bool readyState = ladder.CurrentState == ReadinessState.LowReady;
		ladder.Tick(6f, ReadinessStimulus.CombatActivityExpired);
		bool calm = ladder.CurrentState == ReadinessState.Patrol;
		Check("P40_DecayLadderUnchanged", pre && readyState && calm, ladder.CurrentState.ToString());
	}

	private void RunPersistenceBalance()
	{
		ReadinessProfile soldier = ReadinessProfile.ForRank(ReadinessRankKind.Soldier);
		ReadinessController hold = AimThenLost(soldier);
		float oneSecond = hold.LastCombatActivityTime + 1f;
		hold.Tick(oneSecond, ReadinessStimulus.None);
		Check("P41_HoldOneSecondStaysAim",
			hold.CurrentState == ReadinessState.Aim && hold.Last.DecayPhase == ReadinessDecayPhase.Hold,
			hold.CurrentState.ToString());

		ReadinessController ladder = AimThenLost(soldier);
		bool toPre = StepAfterHold(ladder, ReadinessState.PreAim);
		bool toReady = StepAfterHold(ladder, ReadinessState.LowReady);
		bool toCalm = StepAfterHold(ladder, ReadinessState.Patrol);
		Check("P42_ForRankStepDownLadder", toPre && toReady && toCalm, ladder.CurrentState.ToString());

		ReadinessController refresh = AimThenLost(soldier);
		float holdTime = ReadinessMath.EffectiveHoldTime(ReadinessState.Aim, in soldier);
		float almost = refresh.LastCombatActivityTime + holdTime - 0.05f;
		refresh.Tick(almost, ReadinessStimulus.GunshotHeard);
		refresh.Tick(almost + holdTime * 0.4f, ReadinessStimulus.None);
		Check("P43_GunshotRefreshStaysAim", refresh.CurrentState == ReadinessState.Aim, refresh.CurrentState.ToString());

		ReadinessController reacquire = AimThenLost(soldier);
		StepAfterHold(reacquire, ReadinessState.PreAim);
		reacquire.Tick(reacquire.Context.StateEnterTime + 0.05f, ReadinessStimulus.HostileVisible);
		FinishRaise(reacquire, ReadinessStimulus.HostileVisible);
		Check("P44_ReacquirePreAimToAim",
			reacquire.CurrentState == ReadinessState.Aim &&
			reacquire.Context.PreviousState == ReadinessState.PreAim,
			reacquire.CurrentState + " prev=" + reacquire.Context.PreviousState);

		var chatter = new ReadinessController();
		chatter.Reset(ReadinessRankKind.Soldier, 0f);
		chatter.Tick(0.2f, ReadinessStimulus.GunshotHeard);
		int raises = chatter.TransitionRequestCount;
		bool stayed = true;
		for (int i = 0; i < 6; i++)
		{
			float t = 0.3f + i * 0.12f;
			chatter.Tick(t, i % 2 == 0 ? ReadinessStimulus.HostileLost : ReadinessStimulus.GunshotHeard);
			if (chatter.CurrentState != ReadinessState.LowReady)
				stayed = false;
		}

		Check("P45_NoOscillationGunshotLost",
			stayed && chatter.TransitionRequestCount == raises, chatter.CurrentState.ToString());

		bool structure = true;
		ReadinessRankKind[] ranks =
		{
			ReadinessRankKind.Recruit,
			ReadinessRankKind.Soldier,
			ReadinessRankKind.Corporal,
			ReadinessRankKind.Veteran,
			ReadinessRankKind.Elite
		};
		for (int i = 0; i < ranks.Length; i++)
		{
			ReadinessProfile profile = ReadinessProfile.ForRank(ranks[i]);
			if (ReadinessMath.NextDecayState(ReadinessState.Aim, in profile) != ReadinessState.PreAim)
				structure = false;
			if (ReadinessMath.NextDecayState(ReadinessState.PreAim, in profile) != profile.HeardThreatState)
				structure = false;
			if (ReadinessMath.NextDecayState(profile.HeardThreatState, in profile) != profile.CalmState)
				structure = false;
		}

		Check("P46_RankDecayStructureSame", structure, "ladder");

		ReadinessCalmDownProfile recruitCalm = ReadinessProfile.ForRank(ReadinessRankKind.Recruit).CalmDownProfile;
		ReadinessCalmDownProfile eliteCalm = ReadinessProfile.ForRank(ReadinessRankKind.Elite).CalmDownProfile;
		Check("P47_CalmDownProfileShared",
			recruitCalm.AimHoldTime == eliteCalm.AimHoldTime &&
			recruitCalm.HighReadyHoldTime == eliteCalm.HighReadyHoldTime,
			recruitCalm.AimHoldTime.ToString("0.##"));

		var fatZero = new ReadinessController();
		fatZero.Reset(soldier, 0f);
		fatZero.SetArmFatigue(0f, 1f);
		var fatFull = new ReadinessController();
		fatFull.Reset(soldier, 0f);
		fatFull.SetArmFatigue(1f, 0.2f);
		Check("P48_FatigueNoHoldChange",
			ReadinessMath.EffectiveHoldTime(ReadinessState.Aim, fatZero.Profile) ==
			ReadinessMath.EffectiveHoldTime(ReadinessState.Aim, fatFull.Profile) &&
			!ReadinessMath.FatigueAffectsResult(),
			"fatigue");

		ReadinessController logged = AimThenLost(soldier);
		string holdPayload = logged.LastDecayHoldPayload;
		logged.Tick(logged.LastCombatActivityTime + 0.2f, ReadinessStimulus.None);
		Check("P49_HoldLogPhaseChange",
			holdPayload.IndexOf("hold state=Aim", StringComparison.Ordinal) >= 0 &&
			holdPayload == logged.LastDecayHoldPayload,
			holdPayload);

		float raise = ReadinessMath.AimTransitionDuration(ReadinessState.Patrol, in soldier);
		Check("P50_RisingFasterThanHold",
			ReadinessMath.EffectiveHoldTime(ReadinessState.Aim, in soldier) > raise * 5f,
			raise.ToString("0.###"));

		ReadinessController scenarioA = AimThenLost(soldier);
		bool aHold = scenarioA.CurrentState == ReadinessState.Aim;
		bool aPre = StepAfterHold(scenarioA, ReadinessState.PreAim);
		bool aReady = StepAfterHold(scenarioA, ReadinessState.LowReady);
		bool aCalm = StepAfterHold(scenarioA, ReadinessState.Patrol);
		Check("P51_ScenarioA_ContactThenCalm", aHold && aPre && aReady && aCalm, scenarioA.CurrentState.ToString());

		ReadinessController scenarioB = AimThenLost(soldier);
		float bHold = ReadinessMath.EffectiveHoldTime(ReadinessState.Aim, in soldier);
		float bAlmost = scenarioB.LastCombatActivityTime + bHold * 0.85f;
		scenarioB.Tick(bAlmost, ReadinessStimulus.GunshotHeard);
		bool bHeld = scenarioB.CurrentState == ReadinessState.Aim;
		scenarioB.Tick(bAlmost + 0.05f, ReadinessStimulus.HostileLost);
		bool bStill = scenarioB.CurrentState == ReadinessState.Aim;
		bool bPre = StepAfterHold(scenarioB, ReadinessState.PreAim);
		bool bReady = StepAfterHold(scenarioB, ReadinessState.LowReady);
		bool bCalm = StepAfterHold(scenarioB, ReadinessState.Patrol);
		Check("P52_ScenarioB_InterruptThenCalm",
			bHeld && bStill && bPre && bReady && bCalm, scenarioB.CurrentState.ToString());

		ReadinessController life = AimThenLost(soldier);
		int changes = life.Context.ChangeCount;
		float lifeHold = ReadinessMath.EffectiveHoldTime(ReadinessState.Aim, in soldier);
		life.SetAllowed(false);
		life.Tick(life.LastCombatActivityTime + lifeHold + 3f, ReadinessStimulus.None);
		Check("P53_LifeGateFreezesDecay",
			!life.Allowed && life.CurrentState == ReadinessState.Aim && life.Context.ChangeCount == changes,
			life.CurrentState.ToString());

		Check("P54_ForRankDecayTimed",
			ReadinessMath.DecayTransitionDuration(ReadinessState.Aim, ReadinessState.PreAim, in soldier) > 0f,
			"duration");

		ReadinessProfile instant = ReadinessProfile.Instant(ReadinessRankKind.Soldier);
		Check("P55_InstantDecayStillSnap",
			ReadinessMath.DecayTransitionDuration(ReadinessState.Aim, ReadinessState.PreAim, in instant) == 0f &&
			instant.AimHoldTime == 1f,
			instant.AimHoldTime.ToString("0.##"));
	}

	private void RunArmFatigue()
	{
		ArmFatigueProfile play = ArmFatigueProfile.PlayPrototype();
		Check("P56_LoadOrder",
			ArmFatigueMath.LoadRate(ReadinessState.Patrol, in play) == 0f &&
			ArmFatigueMath.LoadRate(ReadinessState.LowReady, in play) <
			ArmFatigueMath.LoadRate(ReadinessState.HighReady, in play) &&
			ArmFatigueMath.LoadRate(ReadinessState.HighReady, in play) <
			ArmFatigueMath.LoadRate(ReadinessState.PreAim, in play) &&
			ArmFatigueMath.LoadRate(ReadinessState.PreAim, in play) <
			ArmFatigueMath.LoadRate(ReadinessState.Aim, in play),
			"load");

		float aimLoad = ArmFatigueMath.EffectiveLoadRate(ReadinessState.Aim, false, in play);
		float fireLoad = ArmFatigueMath.EffectiveLoadRate(ReadinessState.Aim, true, in play);
		Check("P57_FiringMaxLoad", fireLoad > aimLoad && fireLoad == play.LoadRateFiring, fireLoad.ToString("0.##"));

		bool recoveredLoaded;
		float recovered = ArmFatigueMath.Step(0.5f, 1f, ReadinessState.Patrol, false, true, in play, out recoveredLoaded);
		Check("P58_Recovery", !recoveredLoaded && recovered < 0.5f && recovered > 0f, recovered.ToString("0.###"));

		Check("P59_Clamp",
			ArmFatigueMath.Clamp01(-0.2f) == 0f && ArmFatigueMath.Clamp01(1.4f) == 1f,
			"clamp");

		Check("P60_AimTimeUp",
			ArmFatigueMath.FinalAimTime(1f, 1f, in play) > ArmFatigueMath.FinalAimTime(1f, 0f, in play),
			ArmFatigueMath.FinalAimTime(1f, 1f, in play).ToString("0.###"));

		Check("P61_RecoilControlDown",
			ArmFatigueMath.EffectiveRecoilControl(50f, 1f, in play) <
			ArmFatigueMath.EffectiveRecoilControl(50f, 0f, in play),
			ArmFatigueMath.EffectiveRecoilControl(50f, 1f, in play).ToString("0.##"));

		Check("P62_TurnTimeUp",
			ArmFatigueMath.FinalTurnToTargetTime(1f, in play) > ArmFatigueMath.FinalTurnToTargetTime(0f, in play),
			ArmFatigueMath.FinalTurnToTargetTime(1f, in play).ToString("0.###"));

		var instant = new ReadinessController();
		instant.Reset(ReadinessRankKind.Soldier, 0f);
		instant.Tick(0.2f, ReadinessStimulus.HostileVisible);
		instant.Tick(4f, ReadinessStimulus.HostileVisible);
		Check("P63_InstantNoAccumulate",
			instant.CurrentState == ReadinessState.Aim && instant.ArmFatigue == 0f,
			instant.ArmFatigue.ToString("0.###"));

		ReadinessController forRank = AimThenLost(ReadinessProfile.ForRank(ReadinessRankKind.Soldier));
		float before = forRank.ArmFatigue;
		forRank.Tick(forRank.Context.StateEnterTime + 2f, ReadinessStimulus.HostileVisible);
		Check("P64_ForRankAccumulates",
			forRank.CurrentState == ReadinessState.Aim && forRank.ArmFatigue > before && forRank.ArmFatigue > 0f,
			forRank.ArmFatigue.ToString("0.###"));

		ReadinessController life = AimThenLost(ReadinessProfile.ForRank(ReadinessRankKind.Soldier));
		life.Tick(life.Context.StateEnterTime + 0.4f, ReadinessStimulus.HostileVisible);
		float frozen = life.ArmFatigue;
		life.SetAllowed(false);
		life.Tick(life.Context.StateEnterTime + 6f, ReadinessStimulus.HostileVisible);
		Check("P65_LifeGateFreezesFatigue",
			!life.Allowed && life.ArmFatigue == frozen && life.CurrentState == ReadinessState.Aim,
			life.ArmFatigue.ToString("0.###"));

		var fatZero = new ReadinessController();
		fatZero.Reset(ReadinessRankKind.Soldier, 0f);
		fatZero.SetArmFatigue(0f, 1f);
		fatZero.Tick(0.2f, ReadinessStimulus.HostileVisible);
		var fatFull = new ReadinessController();
		fatFull.Reset(ReadinessRankKind.Soldier, 0f);
		fatFull.SetArmFatigue(1f, 0.2f);
		fatFull.Tick(0.2f, ReadinessStimulus.HostileVisible);
		Check("P66_FatigueNotAState",
			fatZero.CurrentState == ReadinessState.Aim &&
			fatFull.CurrentState == ReadinessState.Aim &&
			fatZero.LastRequest.Duration == fatFull.LastRequest.Duration &&
			!ReadinessMath.FatigueAffectsResult(),
			fatFull.CurrentState.ToString());

		Check("P67_IndependenceFlags",
			!ArmFatigueMath.AffectsReadinessState() &&
			!ArmFatigueMath.AffectsPerception() &&
			!ArmFatigueMath.AffectsG6() &&
			!ArmFatigueMath.AffectsCover() &&
			!ArmFatigueMath.AffectsMovement(),
			"flags");

		Check("P68_ArmLoadMultiplierOne",
			play.ArmLoadMultiplier == 1f &&
			ReadinessProfile.ForRank(ReadinessRankKind.Recruit).ArmFatigue.FatigueLoadModifier == 1f &&
			ReadinessProfile.ForRank(ReadinessRankKind.Elite).ArmFatigue.FatigueRecoveryModifier == 1f,
			play.ArmLoadMultiplier.ToString("0.##"));

		ReadinessController quiet = AimThenLost(ReadinessProfile.ForRank(ReadinessRankKind.Soldier));
		float qt = quiet.Context.StateEnterTime;
		for (int i = 0; i < 6; i++)
		{
			qt += 0.05f;
			quiet.Tick(qt, ReadinessStimulus.HostileVisible);
		}

		Check("P69_LogsNotEveryTick",
			quiet.ArmFatigue < 0.25f && string.IsNullOrEmpty(quiet.LastFatiguePayload),
			quiet.LastFatiguePayload);

		var recover = new ReadinessController();
		recover.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
		recover.RequestTransition(ReadinessState.HighReady, ReadinessChangeReason.Gunshot, 0f);
		recover.Tick(0.3f, ReadinessStimulus.None);
		recover.Tick(2f, ReadinessStimulus.None);
		recover.RequestTransition(ReadinessState.Patrol, ReadinessChangeReason.Calm, 2f);
		recover.Tick(2.8f, ReadinessStimulus.None);
		Check("P70_RecoveryStartLog",
			recover.CurrentState == ReadinessState.Patrol &&
			recover.LastFatiguePayload == "recovery-start" &&
			recover.ArmFatigue > 0f,
			recover.LastFatiguePayload);
	}

	private void RunCombatIntegration()
	{
		GameObject host = new GameObject("ReadinessCombatIntegration");
		UnitAIController ai = host.AddComponent<UnitAIController>();
		UnitCombatStats stats = host.AddComponent<UnitCombatStats>();
		stats.ApplySkills(50f, 50f, 50f);
		host.AddComponent<UnitWeaponAimProgressController>();
		host.AddComponent<UnitWeaponAiming>();
		host.AddComponent<UnitWeaponRecoilController>();
		ai.EnsureStarted();

		ai.Readiness.SetArmFatigue(0f, 1f);
		float freshAim = CombatFatigueProbe.SampleAimTimeSeconds(host.transform);
		float freshTurn = CombatFatigueProbe.SampleTurnSeconds(
			host.transform, CombatFatigueProbe.DefaultTurnDeltaDegrees);
		float freshRecovery = CombatFatigueProbe.SampleSkillRecoveryMultiplier(host.transform);
		float freshLeft = CombatFatigueProbe.RemainingRecoilAfter(
			freshRecovery, CombatFatigueProbe.RecoilProbeSeconds);
		ai.Readiness.SetArmFatigue(1f, 1f);
		float tiredAim = CombatFatigueProbe.SampleAimTimeSeconds(host.transform);
		float tiredTurn = CombatFatigueProbe.SampleTurnSeconds(
			host.transform, CombatFatigueProbe.DefaultTurnDeltaDegrees);
		float tiredRecovery = CombatFatigueProbe.SampleSkillRecoveryMultiplier(host.transform);
		float tiredLeft = CombatFatigueProbe.RemainingRecoilAfter(
			tiredRecovery, CombatFatigueProbe.RecoilProbeSeconds);

		Check("P71_TestA_AimTime_TiredSlower", tiredAim > freshAim, tiredAim.ToString("0.###"));
		Check("P72_TestA_Turn_TiredSlower", tiredTurn > freshTurn, tiredTurn.ToString("0.###"));
		Check("P73_TestA_Recoil_TiredRecoversSlower", tiredLeft > freshLeft, tiredLeft.ToString("0.###"));

		ai.Readiness.SetArmFatigue(0.5f, 1f);
		float halfAim = CombatFatigueProbe.SampleAimTimeSeconds(host.transform);
		float halfTurn = CombatFatigueProbe.SampleTurnSeconds(
			host.transform, CombatFatigueProbe.DefaultTurnDeltaDegrees);
		float halfControl = CombatFatigueProbe.SampleEffectiveRecoilControl(host.transform);
		ai.Readiness.SetArmFatigue(0f, 1f);
		float zeroControl = CombatFatigueProbe.SampleEffectiveRecoilControl(host.transform);
		ai.Readiness.SetArmFatigue(1f, 1f);
		float fullControl = CombatFatigueProbe.SampleEffectiveRecoilControl(host.transform);
		Check("P74_AimTime_0_lt_half_lt_1", freshAim < halfAim && halfAim < tiredAim, halfAim.ToString("0.###"));
		Check("P75_Turn_0_lt_half_lt_1", freshTurn < halfTurn && halfTurn < tiredTurn, halfTurn.ToString("0.###"));
		Check("P76_RecoilControl_Decreases", zeroControl > halfControl && halfControl > fullControl,
			halfControl.ToString("0.##"));

		Check("P77_Load_Patrol_lt_Ready_lt_Aim",
			LoadDelta(ReadinessState.Patrol, 1f) < LoadDelta(ReadinessState.LowReady, 1f) &&
			LoadDelta(ReadinessState.LowReady, 1f) < LoadDelta(ReadinessState.Aim, 1f),
			"load");

		ReadinessController aim = ForRankAimController();
		ReadinessController firing = ForRankAimController();
		float fireT = aim.Context.StateEnterTime + 1f;
		aim.Tick(fireT, ReadinessStimulus.HostileVisible);
		firing.Tick(fireT, new ReadinessFrame { HostileVisible = true, Firing = true });
		Check("P78_Load_Aim_lt_Firing", firing.ArmFatigue > aim.ArmFatigue, firing.ArmFatigue.ToString("0.###"));

		ReadinessController fight = ForRankAimController();
		float fightBefore = fight.ArmFatigue;
		fight.Tick(fight.Context.StateEnterTime + 2f, new ReadinessFrame { HostileVisible = true, Firing = true });
		Check("P79_TestC_LongFirefight", fight.ArmFatigue > fightBefore, fight.ArmFatigue.ToString("0.###"));

		ReadinessController cease = RecoveringPlay();
		float t1 = cease.ArmFatigue;
		cease.Tick(3.5f, ReadinessStimulus.None);
		Check("P80_TestD_CeasefireRecovers",
			cease.ArmFatigue < t1 && cease.ArmFatigue > 0f && cease.CurrentState == ReadinessState.Patrol,
			cease.ArmFatigue.ToString("0.###"));

		ReadinessController interrupt = RecoveringPlay();
		float recovered = interrupt.ArmFatigue;
		interrupt.Tick(3.4f, ReadinessStimulus.HostileVisible);
		FinishRaise(interrupt, ReadinessStimulus.HostileVisible);
		interrupt.Tick(interrupt.Context.StateEnterTime + 1f, ReadinessStimulus.HostileVisible);
		Check("P81_TestE_InterruptGrowsAgain",
			interrupt.CurrentState == ReadinessState.Aim && interrupt.ArmFatigue > recovered,
			interrupt.ArmFatigue.ToString("0.###"));

		var logicalZero = new ReadinessController();
		logicalZero.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
		logicalZero.SetArmFatigue(0f, 1f);
		logicalZero.Tick(0.01f, ReadinessStimulus.HostileVisible);
		var logicalFull = new ReadinessController();
		logicalFull.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
		logicalFull.SetArmFatigue(1f, 1f);
		logicalFull.Tick(0.01f, ReadinessStimulus.HostileVisible);
		Check("P82_LogicalAimUnchanged",
			logicalZero.LastRequest.Duration == logicalFull.LastRequest.Duration &&
			logicalZero.LastRequest.ToState == ReadinessState.Aim,
			logicalZero.LastRequest.Duration.ToString("0.###"));

		ai.Readiness.SetArmFatigue(1f, 1f);
		ai.Tick(0.05f);
		Check("P83_AiStateUnchanged", ai.CurrentState == UnitAIState.Idle, ai.CurrentState.ToString());
		Check("P84_CombatIntentUnchanged", ai.CurrentCombatIntent == CombatIntent.Hold, ai.CurrentCombatIntent.ToString());
		Check("P85_G6NotDriven", !ArmFatigueMath.AffectsG6(), "g6");
		Check("P86_RoEUnchanged", true, ai.CurrentUseOfForceLevel.ToString());
		Check("P87_CoverMovementFlags",
			!ArmFatigueMath.AffectsCover() && !ArmFatigueMath.AffectsMovement() &&
			!ArmFatigueMath.AffectsReadinessState(),
			"flags");

		ReadinessController life = ForRankAimController();
		life.Tick(life.Context.StateEnterTime + 0.3f, ReadinessStimulus.HostileVisible);
		float frozen = life.ArmFatigue;
		life.SetAllowed(false);
		life.Tick(life.Context.StateEnterTime + 5f, ReadinessStimulus.HostileVisible);
		Check("P88_LifeGateFreeze", life.ArmFatigue == frozen && !life.Allowed, life.ArmFatigue.ToString("0.###"));

		bool ranks = true;
		float overlay = ArmFatigueMath.AimTimeMultiplier(
			0.5f, ReadinessProfile.ForRank(ReadinessRankKind.Soldier).ArmFatigue);
		ReadinessRankKind[] allRanks =
		{
			ReadinessRankKind.Recruit,
			ReadinessRankKind.Soldier,
			ReadinessRankKind.Corporal,
			ReadinessRankKind.Veteran,
			ReadinessRankKind.Elite
		};
		for (int i = 0; i < allRanks.Length; i++)
		{
			if (Mathf.Abs(
				    ArmFatigueMath.AimTimeMultiplier(0.5f, ReadinessProfile.ForRank(allRanks[i]).ArmFatigue) -
				    overlay) > 0.0001f)
				ranks = false;
		}

		Check("P89_FiveRanksSameOverlay", ranks, overlay.ToString("0.###"));

		var chain = new ReadinessController();
		chain.Reset(ReadinessRankKind.Soldier, 0f);
		chain.SetArmFatigue(0.5f, 1f);
		chain.Tick(0.2f, ReadinessStimulus.HostileVisible);
		Check("P90_ChainLog",
			chain.CurrentState == ReadinessState.Aim &&
			chain.LastFatigueValuePayload.IndexOf("value=", StringComparison.Ordinal) >= 0 &&
			chain.LastReadinessEffectPayload.IndexOf("aimMultiplier=", StringComparison.Ordinal) >= 0 &&
			!ReadinessMath.FatigueAffectsResult(),
			chain.LastReadinessEffectPayload);

		Destroy(host);
	}

	private static float LoadDelta(ReadinessState _state, float _seconds)
	{
		var controller = new ReadinessController();
		controller.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
		if (_state != ReadinessState.Patrol)
		{
			ReadinessChangeReason reason = _state == ReadinessState.Aim
				? ReadinessChangeReason.HostileVisible
				: ReadinessChangeReason.Gunshot;
			controller.RequestTransition(_state, reason, 0f);
			ReadinessStimulus hold = _state == ReadinessState.Aim
				? ReadinessStimulus.HostileVisible
				: ReadinessStimulus.None;
			FinishRaise(controller, hold);
		}

		float start = controller.ArmFatigue;
		float t = controller.Context.StateEnterTime + _seconds;
		ReadinessStimulus keep = _state == ReadinessState.Aim
			? ReadinessStimulus.HostileVisible
			: ReadinessStimulus.None;
		controller.Tick(t, keep);
		return controller.ArmFatigue - start;
	}

	private static ReadinessController ForRankAimController()
	{
		ReadinessController controller = AimThenLost(ReadinessProfile.ForRank(ReadinessRankKind.Soldier));
		controller.Tick(controller.Context.StateEnterTime + 0.02f, ReadinessStimulus.HostileVisible);
		return controller;
	}

	private static ReadinessController RecoveringPlay()
	{
		var controller = new ReadinessController();
		controller.Reset(ReadinessProfile.ForRank(ReadinessRankKind.Soldier), 0f);
		controller.RequestTransition(ReadinessState.HighReady, ReadinessChangeReason.Gunshot, 0f);
		controller.Tick(0.3f, ReadinessStimulus.None);
		controller.Tick(2f, ReadinessStimulus.None);
		controller.RequestTransition(ReadinessState.Patrol, ReadinessChangeReason.Calm, 2f);
		controller.Tick(2.8f, ReadinessStimulus.None);
		return controller;
	}

	private static ReadinessController AimThenLost(ReadinessProfile _profile)
	{
		var controller = new ReadinessController();
		controller.Reset(_profile, 0f);
		controller.Tick(0f, ReadinessStimulus.HostileVisible);
		FinishRaise(controller, ReadinessStimulus.HostileVisible);
		controller.Tick(controller.Context.StateEnterTime + 0.01f, ReadinessStimulus.HostileLost);
		return controller;
	}

	private static void FinishRaise(ReadinessController _controller, ReadinessStimulus _hold)
	{
		if (_controller.CurrentState == ReadinessState.Aim && !_controller.Context.HasPendingTransition)
			return;

		float t = _controller.Context.HasPendingTransition
			? _controller.Context.TransitionStartTime + _controller.Context.TransitionDuration + 0.05f
			: _controller.Context.StateEnterTime + 0.05f;
		_controller.Tick(t, _hold);
	}

	private static bool StepAfterHold(ReadinessController _controller, ReadinessState _target)
	{
		float hold = ReadinessMath.EffectiveHoldTime(_controller.CurrentState, _controller.Profile);
		float step = ReadinessMath.DecayTransitionDuration(
			_controller.CurrentState,
			_target,
			_controller.Profile);
		float t = _controller.LastCombatActivityTime + hold + 0.05f;
		_controller.Tick(t, ReadinessStimulus.None);
		if (_controller.CurrentState != _target)
		{
			t += step + 0.05f;
			_controller.Tick(t, ReadinessStimulus.None);
		}

		return _controller.CurrentState == _target;
	}

	private static float PatrolAim(ReadinessRankKind _rank)
	{
		ReadinessProfile profile = ReadinessProfile.ForRank(_rank);
		return ReadinessMath.AimTransitionDuration(ReadinessState.Patrol, in profile);
	}

	private static bool AdvanceTo(
		ReadinessController _controller,
		ReadinessState _target,
		ReadinessStimulus _hold)
	{
		if (_controller.CurrentState == _target)
			return true;

		float t = _controller.Context.HasPendingTransition
			? _controller.Context.TransitionStartTime
			: _controller.Context.StateEnterTime;
		for (int i = 0; i < 200; i++)
		{
			t += 0.05f;
			_controller.Tick(t, _hold);
			if (_controller.CurrentState == _target)
				return true;
		}

		return false;
	}

	private static float SecondsUntil(
		ReadinessRankKind _rank,
		ReadinessStimulus _first,
		ReadinessState _target,
		ReadinessStimulus _hold)
	{
		var controller = new ReadinessController();
		controller.Reset(ReadinessProfile.ForRank(_rank), 0f);
		controller.Tick(0f, _first);
		if (controller.CurrentState == _target)
			return 0f;

		float t = 0f;
		for (int i = 0; i < 400; i++)
		{
			t += 0.02f;
			controller.Tick(t, _hold);
			if (controller.CurrentState == _target)
				return t;
		}

		return t;
	}

	private static IEnumerator HoldPerception(
		UnitAIController _ai,
		AIPerceptionFrame _frame,
		float _seconds)
	{
		float until = Time.time + _seconds;
		while (Time.time < until)
		{
			_ai.SetPerceptionFrame(in _frame);
			yield return null;
		}

		_ai.SetPerceptionFrame(in _frame);
	}

	private static ReadinessPoseRequest PoseAfter(ReadinessRankKind _rank, ReadinessStimulus _stimulus)
	{
		var controller = new ReadinessController();
		controller.Reset(_rank, 0f);
		controller.Tick(0.2f, _stimulus);
		return controller.PoseRequest;
	}

	private static ReadinessState ResetState(ReadinessRankKind _rank)
	{
		var controller = new ReadinessController();
		controller.Reset(_rank, 0f);
		return controller.CurrentState;
	}

	private static ReadinessState After(ReadinessRankKind _rank, ReadinessStimulus _stimulus)
	{
		var controller = new ReadinessController();
		controller.Reset(_rank, 0f);
		controller.Tick(0.2f, _stimulus);
		return controller.CurrentState;
	}

	private static AIPerceptionFrame HostileVisibleFrame()
	{
		AIContactKnowledge contact = new AIContactKnowledge(
			null,
			DetectionState.Detected,
			ObservationState.Observed,
			PerceivedIdentity.Hostile,
			1f,
			PerceivedRelationship.Hostile,
			ThreatLevel.High,
			Vector3.zero,
			Vector3.zero,
			0f,
			1f,
			true,
			false,
			false,
			true,
			false,
			false,
			true,
			false,
			false,
			true,
			false,
			false,
			false,
			true);
		return new AIPerceptionFrame(
			new[] { contact },
			new[] { contact },
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			new[] { contact },
			Array.Empty<AIContactKnowledge>(),
			ThreatLevel.High);
	}

	private static AIPerceptionFrame GunshotFrame()
	{
		var sound = new AISoundContact(
			null,
			Vector3.zero,
			SoundEventType.Gunshot,
			1f,
			0f,
			0f,
			true);
		return new AIPerceptionFrame(
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			Array.Empty<AIContactKnowledge>(),
			ThreatLevel.None,
			new[] { sound },
			Array.Empty<AIReportContact>());
	}

	private void Check(string _id, bool _pass, string _detail)
	{
		if (_pass)
		{
			m_PassCount++;
			AppendLine("PASS " + _id);
			return;
		}

		m_FailCount++;
		AppendLine("FAIL " + _id + " " + _detail);
	}

	private void DestroyAi()
	{
		if (m_AiGo == null)
			return;
		Destroy(m_AiGo);
		m_AiGo = null;
	}

	private void AppendLine(string _line)
	{
		m_Report.AppendLine(_line);
	}

	private void Finish()
	{
		AppendLine("---");
		AppendLine("RESULT=" + (m_FailCount == 0 ? "PASS" : "FAIL") +
		           " pass=" + m_PassCount + " fail=" + m_FailCount);
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string path = Path.Combine(dir, "Readiness_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			"[Readiness] " + (m_FailCount == 0 ? "PASS" : "FAIL") +
			" pass=" + m_PassCount + " fail=" + m_FailCount + " → " + path,
			this);

#if UNITY_EDITOR
		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunReadiness;
		if (exitPlay && EditorApplication.isPlaying)
			EditorApplication.isPlaying = false;
#endif
	}
	#endregion
}
