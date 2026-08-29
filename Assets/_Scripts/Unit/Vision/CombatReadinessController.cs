using UnityEngine;

/// <summary>
/// CombatIntent + #14B.2 Readiness pose executor. Does not SetPose itself — ReadyHands does.
/// Does not Fire(), does not write G6 / TargetSelector.
/// Missing <see cref="ICombatIntentSource"/> = do not touch pose (player / RTS keep control).
/// No AI Readiness: Engage → Auto (Stage 2). With Readiness: pose from <see cref="ReadinessPoseRequest"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(27)]
public sealed class CombatReadinessController : MonoBehaviour
{
	#region Private Fields
	[SerializeField] private bool m_DrawDebugHud;
	private ICombatIntentSource m_Source;
	private UnitWeaponReadyHandsLayer m_ReadyHands;
	private TargetSelector m_Selector;
	private EngagementDecisionController m_Engagement;
	private UnitAIController m_Ai;
	private CombatIntent m_LastApplied = CombatIntent.Hold;
	private ReadinessPoseRequest m_LastPoseRequest;
	private WeaponPoseState m_LastAppliedPose = (WeaponPoseState)(-1);
	private ReadinessState m_LastAppliedReadiness = (ReadinessState)(-1);
	private bool m_LastAppliedLifeGate;
	private string m_LastPoseLogPayload = string.Empty;
	#endregion

	#region Public Properties
	public CombatIntent LastAppliedIntent => m_LastApplied;
	public bool ReadinessRequested => m_LastApplied == CombatIntent.Engage;
	public ReadinessPoseRequest LastPoseRequest => m_LastPoseRequest;
	public string LastPoseLogPayload => m_LastPoseLogPayload;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		Bind();
	}

	private void Update()
	{
		ApplyNow();
	}

	private void OnDisable()
	{
		if (!UnitLifeStateMath.AllowsCombatDecision(UnitLifeStateMath.Resolve(this)))
			ApplyIncapacitatedPose();
	}

	private void OnGUI()
	{
		if (!m_DrawDebugHud || Application.isBatchMode)
			return;
		if (m_Source == null)
			return;

		string selected = m_Selector != null && m_Selector.SelectedTarget != null
			? m_Selector.SelectedTarget.name
			: "-";
		string engage = m_Ai != null && m_Ai.CurrentEngageTarget != null
			? m_Ai.CurrentEngageTarget.name
			: "-";
		string decision = m_Engagement != null ? m_Engagement.CurrentDecision.ToString() : "-";
		string roe = m_Ai != null ? m_Ai.CurrentUseOfForceLevel.ToString() : "-";
		string pose = m_ReadyHands != null ? m_ReadyHands.WantedMode.ToString() : "-";
		bool mismatch = m_Engagement != null && m_Engagement.EngageTargetMismatch;

		GUI.Box(new Rect(12f, 430f, 420f, 168f), "CombatIntent");
		GUI.Label(new Rect(24f, 454f, 400f, 140f),
			$"AI {(m_Ai != null ? m_Ai.CurrentState.ToString() : "-")} / {(m_Ai != null ? m_Ai.CurrentAction.ToString() : "-")}\n" +
			$"Intent={m_Source.CurrentCombatIntent}  ROE={roe}\n" +
			$"Decision={decision}  PoseWanted={pose}\n" +
			$"ReadinessPose={m_LastPoseRequest.Pose}\n" +
			$"AI.Engage={engage}  Combat.Selected={selected}\n" +
			$"Mismatch={mismatch}");
	}
	#endregion

	#region Public Methods
	public void ApplyNow()
	{
		UnitLifeState life = UnitLifeStateMath.Resolve(this);
		if (!UnitLifeStateMath.AllowsCombatDecision(life))
		{
			ApplyIncapacitatedPose();
			return;
		}

		Bind();
		if (m_Source == null)
			return;

		CombatIntent intent = m_Source.CurrentCombatIntent;
		m_LastApplied = intent;

		if (m_Ai != null)
		{
			ReadinessPoseRequest request = ReadinessPoseMath.FromController(m_Ai.Readiness);
			ApplyPoseRequest(in request);
			if (intent == CombatIntent.Engage && m_ReadyHands != null)
				m_ReadyHands.NotifyCombatAlert();
			return;
		}

		if (m_ReadyHands == null)
			return;

		if (intent == CombatIntent.Engage)
			RequestCombatReadiness(true);
	}

	public void ApplyIncapacitatedPose()
	{
		Bind();
		ReadinessPoseRequest request = ReadinessPoseMath.Incapacitated();
		ApplyPoseRequest(in request);
	}

	public void ApplyPoseRequest(in ReadinessPoseRequest _request)
	{
		m_LastPoseRequest = _request;
		bool changed = _request.Pose != m_LastAppliedPose ||
		               _request.State != m_LastAppliedReadiness ||
		               _request.FromLifeGate != m_LastAppliedLifeGate;
		if (changed)
		{
			m_LastAppliedPose = _request.Pose;
			m_LastAppliedReadiness = _request.State;
			m_LastAppliedLifeGate = _request.FromLifeGate;
			m_LastPoseLogPayload = ReadinessPoseLog.Format(in _request);
			ReadinessPoseLog.Emit(this, m_LastPoseLogPayload);
		}

		if (m_ReadyHands == null)
			TryGetComponent(out m_ReadyHands);
		if (m_ReadyHands == null)
			return;

		if (_request.IsPeaceful)
		{
			m_ReadyHands.SetPeacefulCarryPose(_request.Pose);
			return;
		}

		m_ReadyHands.SetPoseModeWanted(_request.Mode, true);
	}

	public void RequestCombatReadiness(bool _ready)
	{
		if (m_ReadyHands == null)
			TryGetComponent(out m_ReadyHands);
		if (m_ReadyHands == null)
			return;

		if (_ready)
		{
			if (m_ReadyHands.WantedMode != WeaponPoseMode.Auto)
				m_ReadyHands.SetPoseModeWanted(WeaponPoseMode.Auto, true);
			m_ReadyHands.NotifyCombatAlert();
		}
	}
	#endregion

	#region Private Methods
	private void Bind()
	{
		if (m_Source == null)
			TryGetComponent(out m_Source);
		if (m_ReadyHands == null)
			TryGetComponent(out m_ReadyHands);
		if (m_Selector == null)
			TryGetComponent(out m_Selector);
		if (m_Engagement == null)
			TryGetComponent(out m_Engagement);
		if (m_Ai == null)
			TryGetComponent(out m_Ai);
	}
	#endregion
}
