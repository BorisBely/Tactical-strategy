using UnityEngine;

/// <summary>
/// Pure peek choice: opportunity vs usefulness. Gain vs risk. Minimum sufficient depth.
/// Weapon / rank may bias later (#15 / #15B); they do not cap physical lean here.
/// </summary>
public sealed class CoverPeekSolver
{
	#region Private Fields
	private CoverPeekSettings m_Settings = new CoverPeekSettings();
	private int m_EvaluateCount;
	private CoverPeekDecision m_Last;
	#endregion

	#region Public Properties
	public CoverPeekSettings Settings
	{
		get => m_Settings;
		set => m_Settings = value ?? new CoverPeekSettings();
	}

	public int EvaluateCount => m_EvaluateCount;
	public CoverPeekDecision Last => m_Last;
	#endregion

	#region Public Methods
	public void Invalidate()
	{
		m_Last = default;
	}

	public CoverPeekDecision Evaluate(
		CoverCandidate _candidate,
		in CoverSituation _situation,
		CoverPeekSides _sides,
		ICoverLineOfSightProbe _los)
	{
		m_EvaluateCount++;
		CoverPeekDecision decision = EvaluateCore(_candidate, in _situation, _sides, _los);
		m_Last = decision;
		return decision;
	}
	#endregion

	#region Private Methods
	private CoverPeekDecision EvaluateCore(
		CoverCandidate _candidate,
		in CoverSituation _situation,
		CoverPeekSides _sides,
		ICoverLineOfSightProbe _los)
	{
		CoverPeekDecision decision = new CoverPeekDecision
		{
			CandidateId = _candidate != null ? _candidate.CandidateId : 0,
			LeftAvailable = _sides.Left,
			RightAvailable = _sides.Right,
			PeekAvailable = _candidate != null && CoverPeekGeometry.CanPeek(_candidate.CoverType) && _sides.Any
		};
		decision.Snapshot.CandidateId = decision.CandidateId;
		decision.Snapshot.CoverType = _candidate != null ? _candidate.CoverType : CoverType.None;
		decision.Snapshot.LeftAvailable = _sides.Left;
		decision.Snapshot.RightAvailable = _sides.Right;

		if (_candidate == null || !CoverPeekGeometry.CanPeek(_candidate.CoverType))
		{
			decision.Reason = CoverPeekReason.NotApplicable;
			return decision;
		}

		if (!_sides.Any)
		{
			decision.Reason = CoverPeekReason.NoOpportunity;
			return decision;
		}

		decision.Opportunity.Available = true;

		if (!_situation.HasTarget)
		{
			decision.Reason = CoverPeekReason.NoBenefit;
			return decision;
		}

		CoverPeekSettings settings = m_Settings;
		Vector3 eye = CoverPeekGeometry.EyeWithoutLean(_candidate, _situation.Stance, settings);
		bool visible = _los != null && _los.HasClearLook(eye, _situation.TargetPosition);
		decision.VisibleWithoutLean = visible;
		decision.Snapshot.VisibleWithoutLean = visible;
		if (visible)
		{
			decision.Reason = CoverPeekReason.AlreadyVisible;
			return decision;
		}

		FillSamples(_candidate, in _situation, _sides, _los, settings, ref decision);

		CoverLeanLevel bestDepth = CoverLeanLevel.None;
		CoverPeekDirection bestDirection = CoverPeekDirection.None;
		float bestRisk = float.MaxValue;
		TryConsider(CoverPeekDirection.Left, CoverLeanLevel.Small, _sides.Left, in decision.Snapshot.LeftSmall,
			ref bestDepth, ref bestDirection, ref bestRisk);
		TryConsider(CoverPeekDirection.Right, CoverLeanLevel.Small, _sides.Right, in decision.Snapshot.RightSmall,
			ref bestDepth, ref bestDirection, ref bestRisk);
		if (bestDepth == CoverLeanLevel.None)
		{
			TryConsider(CoverPeekDirection.Left, CoverLeanLevel.Medium, _sides.Left, in decision.Snapshot.LeftMedium,
				ref bestDepth, ref bestDirection, ref bestRisk);
			TryConsider(CoverPeekDirection.Right, CoverLeanLevel.Medium, _sides.Right, in decision.Snapshot.RightMedium,
				ref bestDepth, ref bestDirection, ref bestRisk);
		}

		if (bestDepth == CoverLeanLevel.None)
		{
			TryConsider(CoverPeekDirection.Left, CoverLeanLevel.Deep, _sides.Left, in decision.Snapshot.LeftDeep,
				ref bestDepth, ref bestDirection, ref bestRisk);
			TryConsider(CoverPeekDirection.Right, CoverLeanLevel.Deep, _sides.Right, in decision.Snapshot.RightDeep,
				ref bestDepth, ref bestDirection, ref bestRisk);
		}

		if (bestDepth == CoverLeanLevel.None)
		{
			decision.Reason = CoverPeekReason.NoBenefit;
			decision.Opportunity.Direction = CoverPeekDirection.None;
			return decision;
		}

		decision.Kind = CoverPeekDecisionKind.Lean;
		decision.Direction = bestDirection;
		decision.Depth = bestDepth;
		decision.Reason = CoverPeekReason.TargetAccess;
		decision.VisibilityGain = 1f;
		decision.Risk = bestRisk;
		decision.Opportunity.Direction = bestDirection;
		decision.Opportunity.ExpectedVisibilityGain = 1f;
		decision.Opportunity.ExpectedExposure = bestRisk;
		decision.Opportunity.Risk = bestRisk;
		decision.Snapshot.SelectedDirection = bestDirection;
		decision.Snapshot.SelectedDepth = bestDepth;
		return decision;
	}

	private static void FillSamples(
		CoverCandidate _candidate,
		in CoverSituation _situation,
		CoverPeekSides _sides,
		ICoverLineOfSightProbe _los,
		CoverPeekSettings _settings,
		ref CoverPeekDecision _decision)
	{
		if (_sides.Left)
		{
			_decision.Snapshot.LeftSmall = Sample(_candidate, in _situation, CoverPeekDirection.Left,
				CoverLeanLevel.Small, _los, _settings);
			_decision.Snapshot.LeftMedium = Sample(_candidate, in _situation, CoverPeekDirection.Left,
				CoverLeanLevel.Medium, _los, _settings);
			_decision.Snapshot.LeftDeep = Sample(_candidate, in _situation, CoverPeekDirection.Left,
				CoverLeanLevel.Deep, _los, _settings);
		}

		if (_sides.Right)
		{
			_decision.Snapshot.RightSmall = Sample(_candidate, in _situation, CoverPeekDirection.Right,
				CoverLeanLevel.Small, _los, _settings);
			_decision.Snapshot.RightMedium = Sample(_candidate, in _situation, CoverPeekDirection.Right,
				CoverLeanLevel.Medium, _los, _settings);
			_decision.Snapshot.RightDeep = Sample(_candidate, in _situation, CoverPeekDirection.Right,
				CoverLeanLevel.Deep, _los, _settings);
		}
	}

	private static CoverPeekDepthSample Sample(
		CoverCandidate _candidate,
		in CoverSituation _situation,
		CoverPeekDirection _direction,
		CoverLeanLevel _level,
		ICoverLineOfSightProbe _los,
		CoverPeekSettings _settings)
	{
		Vector3 eye = CoverPeekGeometry.EyeWithLean(_candidate, _situation.Stance, _direction, _level, _settings);
		float exposure = _settings.Exposure(_level);
		return new CoverPeekDepthSample
		{
			Visible = _los != null && _los.HasClearLook(eye, _situation.TargetPosition),
			Exposure = exposure,
			Risk = exposure
		};
	}

	private static void TryConsider(
		CoverPeekDirection _direction,
		CoverLeanLevel _depth,
		bool _available,
		in CoverPeekDepthSample _sample,
		ref CoverLeanLevel _bestDepth,
		ref CoverPeekDirection _bestDirection,
		ref float _bestRisk)
	{
		if (!_available || !_sample.Visible)
			return;
		if (_bestDepth != CoverLeanLevel.None)
			return;
		_bestDepth = _depth;
		_bestDirection = _direction;
		_bestRisk = _sample.Risk;
	}
	#endregion
}
