using UnityEngine;

/// <summary>
/// Parameters of the current tactical state. Not a copy of Vision / AI-0 knowledge.
/// Unused fields stay default for the active state.
/// </summary>
public struct UnitAIStateContext
{
	public Vector3 AnchorPosition;
	public Vector3 AreaCenter;
	public float AreaRadius;
	public Vector3 Facing;

	/// <summary>Attack / Defense / Retreat / Flee point. Search uses <see cref="SearchPosition"/>.</summary>
	public Vector3 Destination;
	public bool HasDestination;
	public Transform TargetEntity;
	public Vector3 AttackDirection;

	public Vector3 SearchOrigin;
	public Vector3 SearchPosition;
	public UnitAIState ResumeState;

	public Vector3 EscapeDirection;

	public static UnitAIStateContext Empty => default;

	public static UnitAIStateContext ForDefense(Vector3 _anchor, Vector3 _areaCenter, float _areaRadius, Vector3 _facing)
	{
		return new UnitAIStateContext
		{
			AnchorPosition = _anchor,
			Destination = _anchor,
			HasDestination = true,
			AreaCenter = _areaCenter,
			AreaRadius = Mathf.Max(0f, _areaRadius),
			Facing = _facing
		};
	}

	public static UnitAIStateContext ForAttack(
		Vector3 _destination,
		Vector3 _attackDirection,
		Transform _targetEntity = null,
		Vector3 _areaCenter = default,
		float _areaRadius = 0f)
	{
		return new UnitAIStateContext
		{
			Destination = _destination,
			HasDestination = true,
			AttackDirection = _attackDirection,
			TargetEntity = _targetEntity,
			AreaCenter = _areaCenter,
			AreaRadius = Mathf.Max(0f, _areaRadius)
		};
	}

	public static UnitAIStateContext ForSearch(
		Vector3 _origin,
		Vector3 _searchPosition,
		float _areaRadius,
		UnitAIState _resumeState = UnitAIState.Idle)
	{
		return new UnitAIStateContext
		{
			SearchOrigin = _origin,
			SearchPosition = _searchPosition,
			AreaCenter = _searchPosition,
			AreaRadius = Mathf.Max(0f, _areaRadius),
			AnchorPosition = _origin,
			ResumeState = _resumeState
		};
	}

	public static UnitAIStateContext ForRetreat(Vector3 _destination)
	{
		return new UnitAIStateContext
		{
			Destination = _destination,
			HasDestination = true
		};
	}

	public static UnitAIStateContext ForFlee(Vector3 _escapeDirection)
	{
		return new UnitAIStateContext { EscapeDirection = _escapeDirection };
	}

	public static UnitAIStateContext ForFlee(Vector3 _escapeDirection, Vector3 _destination)
	{
		return new UnitAIStateContext
		{
			EscapeDirection = _escapeDirection,
			Destination = _destination,
			HasDestination = true
		};
	}
}
