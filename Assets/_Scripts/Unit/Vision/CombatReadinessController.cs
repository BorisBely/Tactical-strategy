using UnityEngine;

/// <summary>
/// Stage 2 FROZEN: CombatIntent → existing ReadyHands request. Does not SetPose, does not Fire().
/// Missing <see cref="ICombatIntentSource"/> = do not touch pose (player / RTS keep control).
/// Engage raises Auto. Hold keeps the current pose (game default is Aiming).
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
	#endregion

	#region Public Properties
	public CombatIntent LastAppliedIntent => m_LastApplied;
	public bool ReadinessRequested => m_LastApplied == CombatIntent.Engage;
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

		GUI.Box(new Rect(12f, 430f, 420f, 150f), "CombatIntent");
		GUI.Label(new Rect(24f, 454f, 400f, 120f),
			$"AI {(m_Ai != null ? m_Ai.CurrentState.ToString() : "-")} / {(m_Ai != null ? m_Ai.CurrentAction.ToString() : "-")}\n" +
			$"Intent={m_Source.CurrentCombatIntent}  ROE={roe}\n" +
			$"Decision={decision}  PoseWanted={pose}\n" +
			$"AI.Engage={engage}  Combat.Selected={selected}\n" +
			$"Mismatch={mismatch}");
	}
	#endregion

	#region Public Methods
	public void ApplyNow()
	{
		Bind();
		if (m_Source == null || m_ReadyHands == null)
			return;

		CombatIntent intent = m_Source.CurrentCombatIntent;
		if (intent == CombatIntent.Engage)
			RequestCombatReadiness(true);
		else
			RequestCombatReadiness(false);

		m_LastApplied = intent;
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
