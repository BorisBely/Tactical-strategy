using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Per-unit heartbeat and SPAWN header. Added at spawn / vision register. Does not change combat or AI.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public sealed class UnitActionLogBinder : MonoBehaviour
{
	#region Constants
	private const float c_SnapIntervalSeconds = 0.5f;
	#endregion

	#region Private Fields
	private float m_NextSnapTime;
	private bool m_SpawnWritten;
	private bool m_AiAttachedLogged;
	private NavMeshAgent m_Agent;
	private UnitTeam m_Team;
	private DetectionProcessor m_Detection;
	private TargetSelector m_Selector;
	private EngagementDecisionController m_G6;
	private UnitWeaponFireController m_Fire;
	private UnitWeaponReadyHandsLayer m_ReadyHands;
	private UnitAnimatorStance m_Stance;
	private UnitAIController m_Ai;
	private IUnitMoveCommand m_Move;
	private UnitEquipment m_Equipment;
	private UnitWeaponRuntime m_WeaponRuntime;
	private UnitClickToMove m_ClickToMove;
	private readonly StringBuilder m_Scratch = new StringBuilder(512);
	private readonly List<string> m_ContactScratch = new List<string>(16);
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		Cache();
	}

	private void Start()
	{
		if (!UnitActionLog.Enabled)
			return;
		UnitActionLogSession.RegisterUnit(this);
		if (!m_SpawnWritten)
			WriteSpawnFromLiveState(null);
		m_NextSnapTime = Time.time + c_SnapIntervalSeconds;
	}

	private void Update()
	{
		if (!UnitActionLog.Enabled)
			return;
		if (!m_AiAttachedLogged && TryGetComponent(out UnitAIController ai) && ai != null)
		{
			m_Ai = ai;
			m_AiAttachedLogged = true;
			UnitActionLog.Write(
				this,
				UnitActionLog.Ai,
				"attached=1 state=" + ai.CurrentState +
				" action=" + ai.CurrentAction +
				" intent=" + ai.CurrentCombatIntent +
				" roe=" + ai.CurrentUseOfForceLevel);
			UnitActionLog.Timeline(
				UnitActionLog.Ai,
				"actor=" + UnitActionLog.Slot(this) + " attached=1 state=" + ai.CurrentState);
		}

		if (Time.time < m_NextSnapTime)
			return;
		m_NextSnapTime = Time.time + c_SnapIntervalSeconds;
		WriteSnap();
	}

	#endregion

	#region Public Methods
	public void NotifyConfigured(UnitSpawnConfig _config)
	{
		if (!UnitActionLog.Enabled)
			return;
		Cache();
		UnitActionLogSession.RegisterUnit(this);
		if (!m_SpawnWritten)
			WriteSpawnFromLiveState(_config);
	}
	#endregion

	#region Private Methods
	private void Cache()
	{
		if (m_Team == null)
			TryGetComponent(out m_Team);
		if (m_Detection == null)
			TryGetComponent(out m_Detection);
		if (m_Selector == null)
			TryGetComponent(out m_Selector);
		if (m_G6 == null)
			TryGetComponent(out m_G6);
		if (m_Fire == null)
			TryGetComponent(out m_Fire);
		if (m_ReadyHands == null)
			TryGetComponent(out m_ReadyHands);
		if (m_Stance == null)
			TryGetComponent(out m_Stance);
		if (m_Ai == null)
			TryGetComponent(out m_Ai);
		if (m_Move == null)
			TryGetComponent(out m_Move);
		if (m_Equipment == null)
			TryGetComponent(out m_Equipment);
		if (m_WeaponRuntime == null)
			TryGetComponent(out m_WeaponRuntime);
		if (m_ClickToMove == null)
			TryGetComponent(out m_ClickToMove);
		if (m_Agent == null)
			TryGetComponent(out m_Agent);
	}

	private void WriteSpawnFromLiveState(UnitSpawnConfig _config)
	{
		m_SpawnWritten = true;
		UnitTeamId team = m_Team != null ? m_Team.Team : UnitTeamId.Neutral;
		string look = "?";
		if (TryGetComponent(out VisualIdentityEvidence evidence) && evidence != null)
			look = evidence.PrimaryAffiliation.ToString();

		string body = "?";
		if (TryGetComponent(out UnitBodyMeshSelector mesh) && mesh != null)
			body = mesh.CurrentArchetype.ToString();
		else if (_config != null)
			body = _config.BodyMeshArchetype.ToString();

		string weapon = ResolveWeaponLabel(_config);
		bool hasAi = m_Ai != null || TryGetComponent(out m_Ai);
		m_AiAttachedLogged = hasAi;
		bool isNeutral = team == UnitTeamId.Neutral;
		string scan = isNeutral
			? "none (Neutral never scans / never a vision candidate)"
			: "opponents";

		string payload =
			"slot=" + UnitActionLog.Slot(this) +
			" team=" + team +
			" look=" + look +
			" body=" + body +
			" weapon=" + weapon +
			" ai=" + (hasAi ? "UnitAIController" : "none") +
			" scanCandidates=" + scan +
			" pos=" + UnitActionLog.Vec(transform.position) +
			" go=" + name;
		UnitActionLog.Write(this, UnitActionLog.Spawn, payload);
		UnitActionLog.Timeline(UnitActionLog.Spawn, "actor=" + UnitActionLog.Slot(this) + " " + payload);
	}

	private string ResolveWeaponLabel(UnitSpawnConfig _config)
	{
		if (_config?.Loadout?.MainHandWeapon != null)
			return _config.Loadout.MainHandWeapon.name;
		if (m_WeaponRuntime != null && m_WeaponRuntime.CurrentWeaponDefinition != null)
			return m_WeaponRuntime.CurrentWeaponDefinition.name;
		if (m_Equipment != null && m_Equipment.EquippedWeapon != null)
			return m_Equipment.EquippedWeapon.name;
		return "none";
	}

	private void WriteSnap()
	{
		Cache();
		Vector3 vel = Vector3.zero;
		string dest = "none";
		string remaining = "-";
		string path = "none";
		if (m_Agent != null && m_Agent.enabled && m_Agent.isOnNavMesh)
		{
			vel = m_Agent.velocity;
			path = UnitActionLog.AgentPath(m_Agent);
			remaining = UnitActionLog.AgentRemaining(m_Agent);
			if (m_Agent.hasPath || m_Agent.pathPending)
				dest = UnitActionLog.Vec(m_Agent.destination);
		}

		string selected = m_Selector != null && m_Selector.SelectedTarget != null
			? UnitActionLog.Slot(m_Selector.SelectedTarget)
			: "none";
		string engageable = m_Selector != null && m_Selector.GetEngageableSelectedTarget() != null ? "1" : "0";
		string g6 = m_G6 != null ? m_G6.CurrentDecision.ToString() : "n/a";
		string pose = m_ReadyHands != null ? m_ReadyHands.EffectivePoseState.ToString() : "?";
		string stance = m_Stance != null ? m_Stance.CurrentStance.ToString() : "?";
		string gate = m_Fire != null ? m_Fire.LastShotAttemptResult.ToString() : "n/a";
		string reason = m_Move != null ? m_Move.Reason.ToString() : "None";
		if (m_ClickToMove != null && m_ClickToMove.enabled && (m_Move == null || m_Move.Reason == UnitNavigationReason.None))
		{
			if (m_ClickToMove.HasMoveIntent)
				reason = "Rts";
		}

		string aiPart = "ai=none";
		if (m_Ai != null)
		{
			string engage = m_Ai.CurrentEngageTarget != null ? UnitActionLog.Slot(m_Ai.CurrentEngageTarget) : "none";
			aiPart = "ai=" + m_Ai.CurrentState + "/" + m_Ai.CurrentAction +
			         " intent=" + m_Ai.CurrentCombatIntent +
			         " roe=" + m_Ai.CurrentUseOfForceLevel +
			         " engage=" + engage;
		}

		int vis = 0;
		int mem = 0;
		m_ContactScratch.Clear();
		if (m_Detection != null)
		{
			foreach (KeyValuePair<Transform, PerceivedContact> pair in m_Detection.Contacts)
			{
				PerceivedContact contact = pair.Value;
				if (contact == null || contact.Target == null)
					continue;
				if (contact.ObservationState == ObservationState.Observed)
					vis++;
				else if (contact.LastSeenConfidence > 0.25f)
					mem++;
				if (m_ContactScratch.Count < 12)
					m_ContactScratch.Add(UnitActionLog.CompactContact(contact));
			}
		}

		m_Scratch.Length = 0;
		m_Scratch.Append("pos=").Append(UnitActionLog.Vec(transform.position));
		m_Scratch.Append(" vel=").Append(UnitActionLog.F1(vel.magnitude));
		m_Scratch.Append(" stance=").Append(stance);
		m_Scratch.Append(" pose=").Append(pose);
		m_Scratch.Append(" g6=").Append(g6);
		m_Scratch.Append(" selected=").Append(selected);
		m_Scratch.Append(" engageable=").Append(engageable);
		m_Scratch.Append(" dest=").Append(dest);
		m_Scratch.Append(" remaining=").Append(remaining);
		m_Scratch.Append(" path=").Append(path);
		m_Scratch.Append(" reason=").Append(reason);
		m_Scratch.Append(" gate=").Append(gate);
		m_Scratch.Append(' ').Append(aiPart);
		m_Scratch.Append(" contacts=").Append(m_Detection != null ? m_Detection.Contacts.Count : 0);
		m_Scratch.Append(" vis=").Append(vis);
		m_Scratch.Append(" mem=").Append(mem);
		if (m_ContactScratch.Count > 0)
		{
			m_Scratch.Append(" |");
			for (int i = 0; i < m_ContactScratch.Count; i++)
				m_Scratch.Append(' ').Append(m_ContactScratch[i]);
		}

		UnitActionLog.Write(this, UnitActionLog.Snap, m_Scratch.ToString());
	}
	#endregion
}
