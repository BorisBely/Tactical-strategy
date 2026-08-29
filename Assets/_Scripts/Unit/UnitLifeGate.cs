using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Owns Alive / Unconscious / Dead gates. Not an AI state. Does not Destroy the unit.
/// Health / ragdoll / visuals stay on. Cover is released on Unconscious and Dead.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-20)]
public sealed class UnitLifeGate : MonoBehaviour
{
	#region Nested
	private struct SavedEnabled
	{
		public bool Captured;
		public bool Ai;
		public bool Detection;
		public bool Selector;
		public bool G6;
		public bool Discipline;
		public bool Fire;
		public bool Readiness;
	}
	#endregion

	#region Private Fields
	private UnitHealth m_Health;
	private UnitConsciousness m_Consciousness;
	private UnitAIController m_Ai;
	private UnitNavLocomotionDriver m_Driver;
	private NavMeshAgent m_Agent;
	private DetectionProcessor m_Detection;
	private TargetSelector m_Selector;
	private EngagementDecisionController m_G6;
	private UnitWeaponFireDisciplineController m_Discipline;
	private UnitWeaponFireController m_Fire;
	private CombatReadinessController m_Readiness;
	private UnitLifeState m_State = UnitLifeState.Alive;
	private SavedEnabled m_Saved;
	private bool m_GatesApplied;
	#endregion

	#region Public Properties
	public UnitLifeState State => m_State;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		Cache();
	}

	private void OnEnable()
	{
		Cache();
		Subscribe();
		Apply(UnitLifeStateMath.Resolve(m_Health, m_Consciousness), false);
	}

	private void OnDisable()
	{
		Unsubscribe();
	}
	#endregion

	#region Public Methods
	public void Refresh()
	{
		Apply(UnitLifeStateMath.Resolve(m_Health, m_Consciousness), false);
	}
	#endregion

	#region Private Methods
	private void Cache()
	{
		if (m_Health == null)
			TryGetComponent(out m_Health);
		if (m_Consciousness == null)
			TryGetComponent(out m_Consciousness);
		if (m_Ai == null)
			TryGetComponent(out m_Ai);
		if (m_Driver == null)
			TryGetComponent(out m_Driver);
		if (m_Agent == null)
			TryGetComponent(out m_Agent);
		if (m_Detection == null)
			TryGetComponent(out m_Detection);
		if (m_Selector == null)
			TryGetComponent(out m_Selector);
		if (m_G6 == null)
			TryGetComponent(out m_G6);
		if (m_Discipline == null)
			TryGetComponent(out m_Discipline);
		if (m_Fire == null)
			TryGetComponent(out m_Fire);
		if (m_Readiness == null)
			TryGetComponent(out m_Readiness);
	}

	private void Subscribe()
	{
		if (m_Health != null)
		{
			m_Health.Changed -= HandleChanged;
			m_Health.Changed += HandleChanged;
		}

		if (m_Consciousness != null)
		{
			m_Consciousness.ConsciousnessChanged -= HandleConsciousness;
			m_Consciousness.ConsciousnessChanged += HandleConsciousness;
		}
	}

	private void Unsubscribe()
	{
		if (m_Health != null)
			m_Health.Changed -= HandleChanged;
		if (m_Consciousness != null)
			m_Consciousness.ConsciousnessChanged -= HandleConsciousness;
	}

	private void HandleChanged()
	{
		Apply(UnitLifeStateMath.Resolve(m_Health, m_Consciousness), true);
	}

	private void HandleConsciousness(bool _)
	{
		Apply(UnitLifeStateMath.Resolve(m_Health, m_Consciousness), true);
	}

	private void Apply(UnitLifeState _next, bool _log)
	{
		if (_next == m_State && m_GatesApplied == UnitLifeStateMath.RequiresCoverRelease(_next))
			return;

		UnitLifeState previous = m_State;
		m_State = _next;
		if (m_Ai != null)
			m_Ai.NotifyLifeState(_next);

		if (UnitLifeStateMath.RequiresCoverRelease(_next))
			ApplyIncapacitatedGates();
		else if (previous != UnitLifeState.Alive)
			RestoreGates();

		if (_log || previous != _next)
			LogLife(previous);
	}

	private void ApplyIncapacitatedGates()
	{
		Cache();
		if (!m_Saved.Captured)
		{
			m_Saved.Ai = m_Ai != null && m_Ai.enabled;
			m_Saved.Detection = m_Detection != null && m_Detection.enabled;
			m_Saved.Selector = m_Selector != null && m_Selector.enabled;
			m_Saved.G6 = m_G6 != null && m_G6.enabled;
			m_Saved.Discipline = m_Discipline != null && m_Discipline.enabled;
			m_Saved.Fire = m_Fire != null && m_Fire.enabled;
			m_Saved.Readiness = m_Readiness != null && m_Readiness.enabled;
			m_Saved.Captured = true;
		}

		if (m_Driver != null)
			m_Driver.HardStop();
		else if (m_Agent != null && m_Agent.enabled && m_Agent.isOnNavMesh)
		{
			m_Agent.isStopped = true;
			m_Agent.ResetPath();
		}

		SetEnabled(m_Ai, false);
		SetEnabled(m_Detection, false);
		SetEnabled(m_Selector, false);
		SetEnabled(m_G6, false);
		SetEnabled(m_Discipline, false);
		SetEnabled(m_Fire, false);
		SetEnabled(m_Readiness, false);
		m_GatesApplied = true;
	}

	private void RestoreGates()
	{
		if (!m_Saved.Captured)
			return;

		SetEnabled(m_Ai, m_Saved.Ai);
		SetEnabled(m_Detection, m_Saved.Detection);
		SetEnabled(m_Selector, m_Saved.Selector);
		SetEnabled(m_G6, m_Saved.G6);
		SetEnabled(m_Discipline, m_Saved.Discipline);
		SetEnabled(m_Fire, m_Saved.Fire);
		SetEnabled(m_Readiness, m_Saved.Readiness);
		m_Saved = default;
		m_GatesApplied = false;
	}

	private static void SetEnabled(Behaviour _behaviour, bool _enabled)
	{
		if (_behaviour != null)
			_behaviour.enabled = _enabled;
	}

	private void LogLife(UnitLifeState _previous)
	{
		if (!UnitActionLog.Enabled)
			return;

		string payload =
			"life=" + m_State +
			" was=" + _previous +
			" reason=Damage" +
			" health=" + (m_Health != null && m_Health.IsDead ? "0" : "1") +
			" consciousness=" + (m_Consciousness != null && m_Consciousness.IsConscious ? "1" : "0") +
			" ai=" + Activity(m_Ai, UnitLifeStateMath.AllowsTactical(m_State)) +
			" vision=" + (UnitLifeStateMath.AllowsPerception(m_State) ? "on" : "off") +
			" combat=" + Activity(m_G6, UnitLifeStateMath.AllowsCombatDecision(m_State)) +
			" move=" + (UnitLifeStateMath.AllowsMovement(m_State) ? "on" : "off") +
			" cover=" + (UnitLifeStateMath.RequiresCoverRelease(m_State) ? "Released" : "held") +
			" coverReleased=" + (UnitLifeStateMath.RequiresCoverRelease(m_State) ? "1" : "0") +
			" navStopped=" + (UnitLifeStateMath.AllowsMovement(m_State) ? "0" : "1");
		UnitActionLog.Write(this, UnitActionLog.Life, payload);
		UnitActionLog.Timeline(
			UnitActionLog.Life,
			"actor=" + UnitActionLog.Slot(this) + " " + payload);
	}

	private static string Activity(Behaviour _behaviour, bool _allowed)
	{
		if (!_allowed)
			return "off";
		if (_behaviour == null)
			return "none";
		return _behaviour.enabled ? "on" : "off";
	}
	#endregion
}
