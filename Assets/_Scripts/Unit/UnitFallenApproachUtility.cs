using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Подход к сражённому: цель 0.7 м; если ближе нельзя — старт до 1.6 м; дальше — отмена.
/// </summary>
public static class UnitFallenApproachUtility
{
	#region Constants
	public const float ArriveDistanceMeters = 0.7f;
	public const float MaxInteractDistanceMeters = 1.6f;
	public const float StandoffMeters = 0.65f;
	/// <summary>Достаточно близко и стоим — начинаем взаимодействие.</summary>
	public const float StuckSeconds = 0.55f;
	/// <summary>
	/// Слишком далеко и стоим — отмена. Дольше, чем вставание из лёжа/приседа,
	/// иначе подход рвётся на 2–4 м во время stance-transition.
	/// </summary>
	public const float AbortStuckSeconds = 2.75f;
	public const float StuckMoveEpsilonMeters = 0.04f;
	public const float ProgressEpsilonMeters = 0.05f;
	public const float NavSampleRadiusMeters = 1.75f;
	public const float RetargetIntervalSeconds = 0.45f;
	public const float RetargetMoveEpsilonMeters = 0.2f;
	#endregion

	#region Public Methods
	public static float HorizontalDistance(Vector3 _a, Vector3 _b)
	{
		float dx = _a.x - _b.x;
		float dz = _a.z - _b.z;
		return Mathf.Sqrt(dx * dx + dz * dz);
	}

	/// <summary>
	/// Точка тела жертвы для дистанции/подхода (hips у humanoid, иначе root).
	/// У лежащего/ragdoll root часто далеко от торса.
	/// </summary>
	public static Vector3 ResolveApproachFocusPosition(Transform _victim)
	{
		if (_victim == null)
			return Vector3.zero;

		Animator animator = _victim.GetComponentInChildren<Animator>(true);
		if (animator != null && animator.isHuman)
		{
			Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
			if (hips != null)
				return hips.position;
		}

		if (_victim.TryGetComponent(out CapsuleCollider capsule) && capsule != null)
			return _victim.TransformPoint(capsule.center);

		return _victim.position;
	}

	public static Vector3 ComputeNavMeshApproachPoint(Transform _helper, Transform _victim, float _standoffMeters)
	{
		Vector3 helperPosition = _helper.position;
		Vector3 victimPosition = ResolveApproachFocusPosition(_victim);
		Vector3 toVictim = victimPosition - helperPosition;
		toVictim.y = 0f;

		if (toVictim.sqrMagnitude < 0.04f)
			toVictim = _victim != null ? _victim.forward : Vector3.forward;

		toVictim.Normalize();
		Vector3 idealPoint = victimPosition - toVictim * _standoffMeters;

		if (NavMesh.SamplePosition(idealPoint, out NavMeshHit hit, NavSampleRadiusMeters, NavMesh.AllAreas) &&
		    HorizontalDistance(hit.position, victimPosition) <= MaxInteractDistanceMeters)
			return hit.position;

		// Ideal недоступен / sample увёл далеко — ищем ближе к жертве вдоль отрезка.
		float totalDistance = HorizontalDistance(helperPosition, victimPosition);
		if (totalDistance > 0.05f)
		{
			const int c_Steps = 8;
			for (int i = c_Steps; i >= 1; i--)
			{
				float t = i / (float)c_Steps;
				float distanceFromVictim = Mathf.Lerp(_standoffMeters, totalDistance, 1f - t);
				if (distanceFromVictim > MaxInteractDistanceMeters)
					continue;

				Vector3 candidate = victimPosition - toVictim * distanceFromVictim;
				if (NavMesh.SamplePosition(candidate, out hit, 0.75f, NavMesh.AllAreas) &&
				    HorizontalDistance(hit.position, victimPosition) <= MaxInteractDistanceMeters)
					return hit.position;
			}
		}

		// Ближайшая точка NavMesh у самой жертвы (лучше, чем «остаться на месте»).
		if (NavMesh.SamplePosition(victimPosition, out hit, NavSampleRadiusMeters, NavMesh.AllAreas))
			return hit.position;

		if (NavMesh.SamplePosition(helperPosition, out hit, NavSampleRadiusMeters, NavMesh.AllAreas))
			return hit.position;

		return idealPoint;
	}

	public static bool HasArrivedOrStuckCloseEnough(float _distanceMeters, float _stuckSeconds)
	{
		if (_distanceMeters <= ArriveDistanceMeters)
			return true;

		// Ближе 0.7 м нельзя — если уже в зоне взаимодействия и стоим, начинаем здесь.
		return _distanceMeters <= MaxInteractDistanceMeters && _stuckSeconds >= StuckSeconds;
	}

	public static bool ShouldAbortApproach(float _distanceMeters, float _stuckSeconds)
	{
		return _distanceMeters > MaxInteractDistanceMeters && _stuckSeconds >= AbortStuckSeconds;
	}

	public static bool IsWithinInteractRange(float _distanceMeters)
	{
		return _distanceMeters <= MaxInteractDistanceMeters;
	}

	/// <summary>
	/// Обновляет счётчик «застрял»: сброс при сближении с жертвой или при реальном сдвиге.
	/// </summary>
	public static float UpdateStuckSeconds(
		float _stuckSeconds,
		float _distanceMeters,
		ref float _bestDistanceMeters,
		float _movedMeters)
	{
		if (_distanceMeters < _bestDistanceMeters - ProgressEpsilonMeters)
		{
			_bestDistanceMeters = _distanceMeters;
			return 0f;
		}

		if (_movedMeters < StuckMoveEpsilonMeters)
			return _stuckSeconds + Time.deltaTime;

		return 0f;
	}

	public static bool ShouldRetargetApproach(Vector3 _previousPoint, Vector3 _nextPoint)
	{
		return HorizontalDistance(_previousPoint, _nextPoint) >= RetargetMoveEpsilonMeters;
	}
	#endregion
}
