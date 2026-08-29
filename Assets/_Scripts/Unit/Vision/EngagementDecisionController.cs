using UnityEngine;

/// <summary>
/// G6: names what to do with <see cref="TargetSelector.SelectedTarget"/>.
/// Optional AI-1A gate: if <see cref="UnitAIController"/> is present and force is denied, Fire/Aim become Ignore.
/// Optional CombatIntent: if a source is present and says Hold, Fire/Aim become Ignore. Missing source = G6 unchanged.
/// Does not change <see cref="EngagementDecisionMath"/>. Does not shoot. Stage 2 FROZEN.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(30)]
[RequireComponent(typeof(UnitPerception))]
[RequireComponent(typeof(DetectionProcessor))]
[RequireComponent(typeof(TargetSelector))]
public sealed class EngagementDecisionController : MonoBehaviour
{
	#region Private Fields
	[SerializeField] private TargetSelector m_TargetSelector;
	[SerializeField] private DetectionProcessor m_ContactRegistry;
	[SerializeField] private UnitWeaponFireController m_FireController;
	private UnitAIController m_Ai;

	[Header("Debug")]
	[SerializeField] private EngagementDecision m_DebugDecision;

	private readonly IEngagementPolicy m_Policy = new DefaultCombatEngagementPolicy();
	private EngagementDecision m_CurrentDecision;
	private ForcePermission m_LastForcePermission;
	private bool m_ForceGateApplied;
	private ICombatIntentSource m_IntentSource;
	private CombatIntent m_LastCombatIntent;
	private bool m_CombatIntentGateApplied;
	private bool m_EngageTargetMismatch;
	private string m_EngageTargetMismatchReason = string.Empty;
	private EngagementDecision m_LastLoggedRaw;
	private EngagementDecision m_LastLoggedFinal;
	private CombatIntent m_LastLoggedIntent;
	private bool m_LastLoggedForceApplied;
	private bool m_LastLoggedForceAllowed = true;
	private bool m_LastLoggedMismatch;
	private EntityId m_LastLoggedSelectedId;
	#endregion

	#region Public Properties
	public EngagementDecision CurrentDecision => m_CurrentDecision;

	public bool IsFireContact =>
		m_CurrentDecision == EngagementDecision.Aim ||
		m_CurrentDecision == EngagementDecision.Fire;

	public ForcePermission LastForcePermission => m_LastForcePermission;
	public bool ForceGateApplied => m_ForceGateApplied;
	public CombatIntent LastCombatIntent => m_LastCombatIntent;
	public bool CombatIntentGateApplied => m_CombatIntentGateApplied;
	public bool EngageTargetMismatch => m_EngageTargetMismatch;
	public string EngageTargetMismatchReason => m_EngageTargetMismatchReason;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_TargetSelector == null)
			m_TargetSelector = GetComponent<TargetSelector>();
		if (m_ContactRegistry == null)
			m_ContactRegistry = GetComponent<DetectionProcessor>();
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		TryGetComponent(out m_Ai);
		TryGetComponent(out m_IntentSource);
	}

	private void OnEnable()
	{
		if (m_TargetSelector != null)
			m_TargetSelector.SelectedTargetChanged += HandleSelectionOrContactsChanged;
		if (m_ContactRegistry != null)
			m_ContactRegistry.ContactsChanged += HandleSelectionOrContactsChanged;
		RefreshDecision();
	}

	private void OnDisable()
	{
		if (m_TargetSelector != null)
			m_TargetSelector.SelectedTargetChanged -= HandleSelectionOrContactsChanged;
		if (m_ContactRegistry != null)
			m_ContactRegistry.ContactsChanged -= HandleSelectionOrContactsChanged;
	}

	private void Update()
	{
		RefreshDecision();
	}
	#endregion

	#region Public Methods
	public void RefreshDecisionNow()
	{
		RefreshDecision();
	}
	#endregion

	#region Private Methods
	private void HandleSelectionOrContactsChanged()
	{
		RefreshDecision();
	}

	private void HandleSelectionOrContactsChanged(Transform _)
	{
		RefreshDecision();
	}

	private void RefreshDecision()
	{
		if (!UnitLifeStateMath.AllowsCombatDecision(UnitLifeStateMath.Resolve(this)))
			return;
		BindCombatRefs();
		EngagementDecision g6 = m_Policy.Evaluate(BuildContext());
		m_ForceGateApplied = TryResolveAi();
		if (m_ForceGateApplied)
		{
			Transform selected = m_TargetSelector != null ? m_TargetSelector.SelectedTarget : null;
			PerceivedContact contact = null;
			bool hasContact = selected != null &&
			                  m_ContactRegistry != null &&
			                  m_ContactRegistry.TryGetContact(selected, out contact) &&
			                  contact != null;
			PerceivedRelationship relationship = hasContact ? contact.Relationship : PerceivedRelationship.Unknown;
			Transform target = hasContact ? contact.Target : selected;
			m_LastForcePermission = m_Ai.EvaluateForce(hasContact, relationship, target);
			if (!m_LastForcePermission.Allowed &&
			    (g6 == EngagementDecision.Fire || g6 == EngagementDecision.Aim))
			{
				m_CurrentDecision = EngagementDecision.Ignore;
			}
			else
			{
				m_CurrentDecision = g6;
			}
		}
		else
		{
			m_LastForcePermission = default;
			m_CurrentDecision = g6;
		}

		ApplyCombatIntentGate();
		RefreshEngageTargetMismatch();
		m_DebugDecision = m_CurrentDecision;
		LogDecisionIfChanged(g6);
	}

	private void BindCombatRefs()
	{
		if (m_TargetSelector == null)
			TryGetComponent(out m_TargetSelector);
		if (m_ContactRegistry == null)
			TryGetComponent(out m_ContactRegistry);
		if (m_FireController == null)
			TryGetComponent(out m_FireController);
	}

	private bool TryResolveAi()
	{
		if (m_Ai != null)
			return true;
		return TryGetComponent(out m_Ai) && m_Ai != null;
	}

	private void ApplyCombatIntentGate()
	{
		m_CombatIntentGateApplied = TryResolveIntentSource();
		if (!m_CombatIntentGateApplied)
		{
			m_LastCombatIntent = CombatIntent.Hold;
			return;
		}

		m_LastCombatIntent = m_IntentSource.CurrentCombatIntent;
		m_CurrentDecision = CombatIntentMath.ApplyHoldVeto(m_CurrentDecision, m_LastCombatIntent);
	}

	private bool TryResolveIntentSource()
	{
		if (m_IntentSource != null)
			return true;
		return TryGetComponent(out m_IntentSource) && m_IntentSource != null;
	}

	private void RefreshEngageTargetMismatch()
	{
		TryGetComponent(out m_Ai);
		if (m_TargetSelector == null)
			TryGetComponent(out m_TargetSelector);

		Transform aiTarget = m_Ai != null ? m_Ai.CurrentEngageTarget : null;
		Transform combatTarget = m_TargetSelector != null ? m_TargetSelector.SelectedTarget : null;
		m_EngageTargetMismatch = TargetCombatMismatch.IsMismatch(aiTarget, combatTarget);
		m_EngageTargetMismatchReason = TargetCombatMismatch.Describe(aiTarget, combatTarget);
	}

	private EngagementDecisionContext BuildContext()
	{
		Transform selected = m_TargetSelector != null ? m_TargetSelector.SelectedTarget : null;
		bool hasSelected = selected != null;

		PerceivedContact contact = null;
		bool hasContact = hasSelected &&
		                  m_ContactRegistry != null &&
		                  m_ContactRegistry.TryGetContact(selected, out contact) &&
		                  contact != null;

		bool weaponOk = m_FireController == null || m_FireController.EvaluateWeaponCanFireEventually();
		bool aimReady = m_FireController == null || m_FireController.EvaluateAimReadyToFire();

		return new EngagementDecisionContext
		{
			HasSelectedTarget = hasSelected,
			HasContact = hasContact,
			Identity = hasContact ? contact.Identity : PerceivedIdentity.Unknown,
			Relationship = hasContact ? contact.Relationship : PerceivedRelationship.Unknown,
			Threat = hasContact ? contact.Threat : ThreatLevel.None,
			ObservationState = hasContact ? contact.ObservationState : ObservationState.Lost,
			LastSeenConfidence = hasContact ? contact.LastSeenConfidence : 0f,
			HasKnowledge = hasContact && contact.HasKnowledge,
			IsWorldEngageable = hasSelected && TargetEngageability.IsEngageable(selected),
			HasLosConfirmedAim = hasSelected && m_TargetSelector != null && m_TargetSelector.HasSelectedAimPoint,
			WeaponCanFireEventually = weaponOk,
			AimReadyToFire = aimReady
		};
	}

	private void LogDecisionIfChanged(EngagementDecision _raw)
	{
		if (!UnitActionLog.Enabled)
			return;

		Transform selected = m_TargetSelector != null ? m_TargetSelector.SelectedTarget : null;
		EntityId selectedId = selected != null ? selected.GetEntityId() : default;
		bool forceAllowed = !m_ForceGateApplied || m_LastForcePermission.Allowed;
		if (_raw == m_LastLoggedRaw &&
		    m_CurrentDecision == m_LastLoggedFinal &&
		    m_LastCombatIntent == m_LastLoggedIntent &&
		    m_ForceGateApplied == m_LastLoggedForceApplied &&
		    forceAllowed == m_LastLoggedForceAllowed &&
		    m_EngageTargetMismatch == m_LastLoggedMismatch &&
		    selectedId == m_LastLoggedSelectedId)
			return;

		m_LastLoggedRaw = _raw;
		m_LastLoggedFinal = m_CurrentDecision;
		m_LastLoggedIntent = m_LastCombatIntent;
		m_LastLoggedForceApplied = m_ForceGateApplied;
		m_LastLoggedForceAllowed = forceAllowed;
		m_LastLoggedMismatch = m_EngageTargetMismatch;
		m_LastLoggedSelectedId = selectedId;

		bool los = m_TargetSelector != null && m_TargetSelector.HasSelectedAimPoint;
		bool weaponOk = m_FireController == null || m_FireController.EvaluateWeaponCanFireEventually();
		bool aimReady = m_FireController == null || m_FireController.EvaluateAimReadyToFire();
		string roe = m_ForceGateApplied ? m_LastForcePermission.ToString() : "n/a";
		string intent = m_CombatIntentGateApplied ? m_LastCombatIntent.ToString() : "n/a";
		string payload =
			"raw=" + _raw +
			" final=" + m_CurrentDecision +
			" selected=" + (selected != null ? UnitActionLog.Slot(selected) : "none") +
			" los=" + (los ? "1" : "0") +
			" weaponOk=" + (weaponOk ? "1" : "0") +
			" aimReady=" + (aimReady ? "1" : "0") +
			" roe=" + roe +
			" intent=" + intent +
			" mismatch=" + (m_EngageTargetMismatch ? "1" : "0");
		if (m_EngageTargetMismatch)
			payload += " mismatchReason=AiHostileVisibleMaxThreat_vs_G5Hysteresis";
		UnitActionLog.Write(this, UnitActionLog.G6, payload);
		UnitActionLog.Timeline(UnitActionLog.G6, "actor=" + UnitActionLog.Slot(this) + " " + payload);
	}
	#endregion
}
