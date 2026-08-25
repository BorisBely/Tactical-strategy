using UnityEngine;

/// <summary>
/// Per-unit ImmediateThreat window. Written by <see cref="ImmediateThreatSignal"/>
/// (including Gunshot/Hit mapped from <see cref="CombatEventHub"/>).
/// Does not read ThreatLevel, TargetSelector, or UseOfForceEvaluator.
/// Does not call Fire.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(24)]
public sealed class ImmediateThreatSource : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField, Min(0.05f)] private float m_DurationSeconds = 4f;
	#endregion

	#region Private Fields
	private UnitAIController m_Ai;
	private bool m_WindowActive;
	private float m_RemainingSeconds;
	private Transform m_LastAttacker;
	private ImmediateThreatCause m_LastCause;
	private float m_AgeSeconds;
	#endregion

	#region Public Properties
	public float DurationSeconds
	{
		get => m_DurationSeconds;
		set => m_DurationSeconds = Mathf.Max(0.05f, value);
	}

	public bool WindowActive => m_WindowActive;
	public Transform LastAttacker => m_LastAttacker;
	public ImmediateThreatCause LastCause => m_LastCause;
	public float RemainingSeconds => m_WindowActive ? Mathf.Max(0f, m_RemainingSeconds) : 0f;
	public float AgeSeconds => m_WindowActive ? m_AgeSeconds : 0f;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		TryGetComponent(out m_Ai);
	}
	#endregion

	#region Public Methods
	public void NotifyHostileAttack(Component _attacker, ImmediateThreatCause _cause)
	{
		if (_attacker == null)
			return;
		if (!UnitTeam.AreHostile(_attacker, this))
			return;

		bool wasActive = m_WindowActive;
		m_WindowActive = true;
		m_RemainingSeconds = m_DurationSeconds;
		m_LastAttacker = _attacker.transform;
		m_LastCause = _cause;
		if (!wasActive)
			m_AgeSeconds = 0f;

		BindAi();
		if (m_Ai != null)
			m_Ai.SetImmediateThreatFlag(true);

		if (!wasActive)
			LogChange(true, "set");
	}

	public void Tick(float _dt)
	{
		if (!m_WindowActive)
			return;
		if (_dt < 0f)
			_dt = 0f;
		m_RemainingSeconds -= _dt;
		m_AgeSeconds += _dt;
		if (m_RemainingSeconds > 0f)
			return;

		Expire("Expired");
	}

	public void ClearWindow()
	{
		if (!m_WindowActive)
			return;
		Expire("Cleared");
	}
	#endregion

	#region Private Methods
	private void BindAi()
	{
		if (m_Ai == null)
			TryGetComponent(out m_Ai);
	}

	private void Expire(string _reason)
	{
		m_WindowActive = false;
		m_RemainingSeconds = 0f;
		BindAi();
		if (m_Ai != null)
			m_Ai.SetImmediateThreatFlag(false);
		LogChange(false, _reason);
	}

	private void LogChange(bool _immediate, string _reason)
	{
		if (!UnitActionLog.Enabled)
			return;

		string source = m_LastAttacker != null ? UnitActionLog.Slot(m_LastAttacker) : "none";
		if (_immediate)
		{
			float expires = Time.time + m_RemainingSeconds;
			UnitActionLog.Write(
				this,
				UnitActionLog.Threat,
				"source=" + source +
				" type=" + m_LastCause +
				" immediate=1 expires=" + expires.ToString("0.000"));
			UnitActionLog.Timeline(
				UnitActionLog.Threat,
				"actor=" + UnitActionLog.Slot(this) +
				" source=" + source +
				" type=" + m_LastCause +
				" immediate=1");
			return;
		}

		UnitActionLog.Write(
			this,
			UnitActionLog.Threat,
			"source=" + source + " immediate=0 reason=" + _reason);
		UnitActionLog.Timeline(
			UnitActionLog.Threat,
			"actor=" + UnitActionLog.Slot(this) +
			" source=" + source +
			" immediate=0 reason=" + _reason);
	}
	#endregion
}
