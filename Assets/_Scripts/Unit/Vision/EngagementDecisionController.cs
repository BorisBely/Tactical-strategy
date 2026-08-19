using UnityEngine;

/// <summary>
/// G6: names what to do with <see cref="TargetSelector.SelectedTarget"/>.
/// Optional AI-1A gate: if <see cref="UnitAIController"/> is present and force is denied, Fire/Aim become Ignore.
/// Does not change <see cref="EngagementDecisionMath"/>. Does not shoot.
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
	#endregion

	#region Public Properties
	public EngagementDecision CurrentDecision => m_CurrentDecision;

	public bool IsFireContact =>
		m_CurrentDecision == EngagementDecision.Aim ||
		m_CurrentDecision == EngagementDecision.Fire;

	public ForcePermission LastForcePermission => m_LastForcePermission;
	public bool ForceGateApplied => m_ForceGateApplied;
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

		m_DebugDecision = m_CurrentDecision;
	}

	private bool TryResolveAi()
	{
		if (m_Ai != null)
			return true;
		return TryGetComponent(out m_Ai) && m_Ai != null;
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
	#endregion
}
