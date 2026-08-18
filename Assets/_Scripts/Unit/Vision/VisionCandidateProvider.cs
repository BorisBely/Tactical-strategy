using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Supplies vision candidates (opposing units + optional shooting-range targets).
/// Only answers: which objects are worth checking. No LOS/FOV.
/// </summary>
public sealed class VisionCandidateProvider
{
	#region Nested Types
	public readonly struct Candidate
	{
		public readonly Transform Root;
		public readonly UnitBodyHitZone[] HitZones;
		public readonly Collider LegacyCollider;

		public Candidate(Transform _root, UnitBodyHitZone[] _hitZones, Collider _legacyCollider)
		{
			Root = _root;
			HitZones = _hitZones ?? Array.Empty<UnitBodyHitZone>();
			LegacyCollider = _legacyCollider;
		}

		public bool HasHitZones => HitZones.Length > 0;
	}
	#endregion

	#region Private Fields
	private readonly UnitVision m_Self;
	private UnitTeam m_Team;
	private UnitVisionRegistry m_Registry;
	private ShootingRangeTargetRegistry m_RangeTargetRegistry;
	private readonly List<UnitVision> m_OpponentBuffer = new List<UnitVision>(128);
	private readonly List<ShootingRangeTarget> m_RangeTargetBuffer = new List<ShootingRangeTarget>(32);
	#endregion

	#region Construction
	public VisionCandidateProvider(UnitVision _self)
	{
		m_Self = _self;
	}
	#endregion

	#region Public Methods
	public void Bind(UnitTeam _team, UnitVisionRegistry _registry, ShootingRangeTargetRegistry _rangeTargetRegistry)
	{
		m_Team = _team;
		m_Registry = _registry;
		m_RangeTargetRegistry = _rangeTargetRegistry;
	}

	/// <summary>Fill <paramref name="_out"/> with alive/targetable candidates for the current viewer.</summary>
	public void Collect(List<Candidate> _out, Func<Transform, bool> _shouldSkip)
	{
		_out.Clear();
		if (m_Registry == null || m_Team == null || m_Self == null)
			return;

		m_Registry.GetOpponents(m_Team.Team, m_OpponentBuffer);
		for (int i = 0; i < m_OpponentBuffer.Count; i++)
		{
			UnitVision other = m_OpponentBuffer[i];
			if (other == null || other == m_Self || !other.isActiveAndEnabled)
				continue;
			if (!UnitConsciousness.IsTargetableTarget(other.transform))
				continue;
			if (other.TryGetComponent(out DamageableTarget damageable) && !damageable.IsAlive)
				continue;
			if (_shouldSkip != null && _shouldSkip(other.transform))
				continue;

			UnitBodyHitZone[] zones = other.GetBodyHitZonesArray();
			Collider legacy = null;
			if (zones.Length == 0)
			{
				legacy = other.BodyCollider != null
					? other.BodyCollider
					: other.GetComponentInChildren<Collider>();
				if (legacy == null)
					continue;
			}

			_out.Add(new Candidate(other.transform, zones, legacy));
		}

		if (m_Team.Team == UnitTeamId.Player && m_RangeTargetRegistry != null)
		{
			m_RangeTargetRegistry.GetActiveTargets(m_RangeTargetBuffer);
			for (int i = 0; i < m_RangeTargetBuffer.Count; i++)
			{
				ShootingRangeTarget rangeTarget = m_RangeTargetBuffer[i];
				if (rangeTarget == null || !rangeTarget.IsAvailableForTargeting)
					continue;
				if (_shouldSkip != null && _shouldSkip(rangeTarget.transform))
					continue;

				Collider targetCol = rangeTarget.TargetCollider;
				if (targetCol == null)
					continue;

				_out.Add(new Candidate(rangeTarget.transform, Array.Empty<UnitBodyHitZone>(), targetCol));
			}
		}
	}

	public void CollectOpponentsRaw(List<UnitVision> _buffer)
	{
		_buffer.Clear();
		if (m_Registry == null || m_Team == null)
			return;
		m_Registry.GetOpponents(m_Team.Team, _buffer);
	}
	#endregion
}
