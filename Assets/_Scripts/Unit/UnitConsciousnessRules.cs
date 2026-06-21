using System.Collections;
using UnityEngine;

/// <summary>
/// Боевая v1: переводит юнита в бессознание по травмам из <see cref="UnitHealth"/>.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UnitConsciousness))]
[RequireComponent(typeof(UnitHealth))]
public sealed class UnitConsciousnessRules : MonoBehaviour
{
	#region Constants
	private static readonly string[] s_InstantKnockoutInjuryKeys =
	{
		"health.injury.neck_bleeding",
		"health.injury.lung_damage",
		"health.injury.internal_bleeding",
		"health.injury.head_wound",
		"health.injury.concussion"
	};
	#endregion

	#region Serialized Fields
	[SerializeField] private UnitConsciousness m_Consciousness;
	[SerializeField] private UnitHealth m_UnitHealth;

	[Header("Instant Knockout")]
	[SerializeField] private int m_CriticalSortPriorityThreshold = 15;

	[Header("Delayed Knockout")]
	[SerializeField] private int m_SeriousSortPriorityThreshold = 25;
	[SerializeField, Min(2)] private int m_SeriousInjuryCountForKnockout = 2;
	[SerializeField, Min(2)] private int m_TotalInjuryCountForKnockout = 3;
	[SerializeField, Min(0f)] private float m_DelayedKnockoutMinSeconds = 8f;
	[SerializeField, Min(0f)] private float m_DelayedKnockoutMaxSeconds = 20f;

	[Header("Fall Impulse")]
	[SerializeField, Min(0f)] private float m_HitImpulse = 1.2f;
	[SerializeField, Min(0f)] private float m_HitUpImpulse = 0f;

	[Header("Debug")]
	[SerializeField] private bool m_LogConsciousness = true;
	#endregion

	#region Private Fields
	private Coroutine m_DelayedKnockoutRoutine;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveReferences();
	}

	private void OnDisable()
	{
		CancelDelayedKnockout();
	}
	#endregion

	#region Public Methods
	public void EvaluateAfterInjury(DamageHitInfo _hitInfo, InjuryUiEntry _newInjury)
	{
		ResolveReferences();
		if (m_Consciousness == null || m_UnitHealth == null)
		{
			Log("пропуск: нет Consciousness или UnitHealth");
			return;
		}

		if (!m_Consciousness.IsConscious)
		{
			Log("пропуск: юнит уже без сознания");
			return;
		}

		UnitRagdollController.RagdollFallProfile fallProfile = ResolveFallProfile(_hitInfo.BodyPart, _newInjury, _hitInfo.IncomingDirection);
		int seriousCount = m_UnitHealth.CountInjuriesWithPriorityAtMost(m_SeriousSortPriorityThreshold);

		if (ShouldKnockoutInstantly(_newInjury))
		{
			Log(
				$"мгновенное падение | травма={_newInjury.StatusLocalizationKey} | " +
				$"profile={fallProfile} | minPriority={m_UnitHealth.MinInjurySortPriority} | serious={seriousCount} | total={m_UnitHealth.InjuryCount}");
			CancelDelayedKnockout();
			m_Consciousness.EnterUnconscious(_hitInfo, fallProfile);
			return;
		}

		if (!ShouldScheduleDelayedKnockout())
		{
			Log(
				$"без падения | травма={_newInjury.StatusLocalizationKey} | " +
				$"minPriority={m_UnitHealth.MinInjurySortPriority} | serious={seriousCount}/{m_SeriousInjuryCountForKnockout} | " +
				$"total={m_UnitHealth.InjuryCount}/{m_TotalInjuryCountForKnockout}");
			return;
		}

		float minDelay = Mathf.Min(m_DelayedKnockoutMinSeconds, m_DelayedKnockoutMaxSeconds);
		float maxDelay = Mathf.Max(m_DelayedKnockoutMinSeconds, m_DelayedKnockoutMaxSeconds);
		float delay = maxDelay <= minDelay ? minDelay : Random.Range(minDelay, maxDelay);
		Log(
			$"отложенное падение через {delay:F1}с | serious={seriousCount} | total={m_UnitHealth.InjuryCount}");
		ScheduleDelayedKnockout(_hitInfo, fallProfile, delay);
	}
	#endregion

	#region Private Methods
	private void ResolveReferences()
	{
		if (m_Consciousness == null)
			m_Consciousness = GetComponent<UnitConsciousness>();
		if (m_UnitHealth == null)
			m_UnitHealth = GetComponent<UnitHealth>();
	}

	private bool ShouldKnockoutInstantly(InjuryUiEntry _newInjury)
	{
		if (IsInstantKnockoutInjury(_newInjury.StatusLocalizationKey))
			return true;

		return m_UnitHealth.MinInjurySortPriority <= m_CriticalSortPriorityThreshold;
	}

	private bool ShouldScheduleDelayedKnockout()
	{
		if (m_UnitHealth.InjuryCount >= m_TotalInjuryCountForKnockout)
			return true;

		return m_UnitHealth.CountInjuriesWithPriorityAtMost(m_SeriousSortPriorityThreshold) >=
		       m_SeriousInjuryCountForKnockout;
	}

	private static bool IsInstantKnockoutInjury(string _localizationKey)
	{
		if (string.IsNullOrWhiteSpace(_localizationKey))
			return false;

		for (int i = 0; i < s_InstantKnockoutInjuryKeys.Length; i++)
		{
			if (_localizationKey == s_InstantKnockoutInjuryKeys[i])
				return true;
		}

		return false;
	}

	private void ScheduleDelayedKnockout(
		DamageHitInfo _hitInfo,
		UnitRagdollController.RagdollFallProfile _fallProfile,
		float _delay)
	{
		if (m_DelayedKnockoutRoutine != null)
			return;

		m_DelayedKnockoutRoutine = StartCoroutine(DelayedKnockoutRoutine(_hitInfo, _fallProfile, _delay));
	}

	private IEnumerator DelayedKnockoutRoutine(
		DamageHitInfo _hitInfo,
		UnitRagdollController.RagdollFallProfile _fallProfile,
		float _delay)
	{
		yield return new WaitForSeconds(_delay);

		m_DelayedKnockoutRoutine = null;
		if (m_Consciousness == null || !m_Consciousness.IsConscious)
		{
			Log("отложенное падение отменено: юнит уже без сознания или сознание отсутствует");
			yield break;
		}

		Log("отложенное падение выполнено");
		m_Consciousness.EnterUnconscious(_hitInfo, _fallProfile);
	}

	private void CancelDelayedKnockout()
	{
		if (m_DelayedKnockoutRoutine == null)
			return;

		StopCoroutine(m_DelayedKnockoutRoutine);
		m_DelayedKnockoutRoutine = null;
	}

	private Vector3 ResolveImpulse(DamageHitInfo _hitInfo)
	{
		Vector3 direction = _hitInfo.IncomingDirection;
		if (direction.sqrMagnitude < 0.0001f)
			return Vector3.zero;

		return direction.normalized * m_HitImpulse + Vector3.up * m_HitUpImpulse;
	}

	private UnitRagdollController.RagdollFallProfile ResolveFallProfile(BodyPartType _bodyPart, InjuryUiEntry _injury, Vector3 _hitDirection)
	{
		float forwardness = _hitDirection.sqrMagnitude > 0.0001f
			? Vector3.Dot(_hitDirection.normalized, transform.forward)
			: 0f;

		if (m_LogConsciousness)
		{
			Debug.Log(
				$"[Сознание] {name} | выбор профиля: bodyPart={_bodyPart} | " +
				$"hitDir={_hitDirection.normalized:F2} | forwardness={forwardness:F2} | " +
				$"injury={_injury.StatusLocalizationKey}",
				this);
		}

		if (_bodyPart == BodyPartType.LeftLeg || _bodyPart == BodyPartType.RightLeg)
			return UnitRagdollController.RagdollFallProfile.LegBuckle;

		if (_bodyPart == BodyPartType.Head || _bodyPart == BodyPartType.Neck)
		{
			if (forwardness > 0.3f)
				return UnitRagdollController.RagdollFallProfile.BackwardKnockback;
			if (forwardness < -0.3f)
				return UnitRagdollController.RagdollFallProfile.ForwardCollapse;
			return Mathf.Abs(forwardness) < 0.3f
				? UnitRagdollController.RagdollFallProfile.SideSpin
				: UnitRagdollController.RagdollFallProfile.HeavyDrop;
		}

		if (Mathf.Abs(forwardness) < 0.3f)
			return UnitRagdollController.RagdollFallProfile.SideSpin;

		if (forwardness > 0f)
			return UnitRagdollController.RagdollFallProfile.BackwardKnockback;

		return UnitRagdollController.RagdollFallProfile.ForwardCollapse;
	}

	private void Log(string _message)
	{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		if (!m_LogConsciousness)
			return;

		Debug.Log($"[Сознание] {name} | {_message}", this);
#endif
	}
	#endregion
}
