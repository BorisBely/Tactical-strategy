using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #13.3 Individual Evaluation. Score is per unit. Shared candidates have no score. Not Fire. Not Move.
	/// </summary>
	public sealed class DynamicCoverEvaluationTests
	{
		#region Nested
		private sealed class RecordingSource : ICoverCandidateSource
		{
			public int GenerateCount;

			public void Generate(
				CoverRegionId _region,
				Bounds _bounds,
				int _geometryVersion,
				List<CoverCandidate> _destination)
			{
				GenerateCount++;
				_destination.Add(StandingCover(1, _bounds.center, Vector3.forward));
			}
		}

		private sealed class BlockingLos : ICoverLineOfSightProbe
		{
			public Vector3 BlockedFrom;
			public float Radius = 0.4f;

			public bool HasClearLook(Vector3 _from, Vector3 _to)
			{
				return CoverSpatialMath.PlanarDistanceSqr(_from, BlockedFrom) > Radius * Radius;
			}
		}
		#endregion

		#region A Individual score
		[Test]
		public void A1_SameCandidate_DifferentUnitScores()
		{
			CoverCandidate candidate = StandingCover(1, new Vector3(4f, 0f, 0f), Vector3.forward);
			CoverSituation near = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 20f));
			CoverSituation far = RifleAt(new Vector3(-12f, 0f, 0f), new Vector3(0f, 1.5f, 20f));
			float scoreNear = CoverScoreMath.PositionScore(candidate, in near, null);
			float scoreFar = CoverScoreMath.PositionScore(candidate, in far, null);
			Assert.AreNotEqual(scoreNear, scoreFar);
			Assert.Greater(scoreNear, scoreFar);
		}

		[Test]
		public void A2_SameUnit_DeterministicScore()
		{
			CoverCandidate candidate = StandingCover(1, new Vector3(3f, 0f, 2f), Vector3.forward);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 18f));
			float a = CoverScoreMath.PositionScore(candidate, in situation, null);
			float b = CoverScoreMath.PositionScore(candidate, in situation, null);
			Assert.AreEqual(a, b);
		}

		[Test]
		public void A3_ScoreDecomposition_SumsCorrectly()
		{
			CoverCandidate candidate = StandingCover(1, new Vector3(5f, 0f, 1f), Vector3.forward);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 16f));
			CoverPositionEvaluation evaluation = CoverScoreMath.EvaluateOne(candidate, in situation, null);
			Assert.AreEqual(evaluation.Factors.Total, evaluation.Score, 0.0001f);
		}
		#endregion

		#region B Protection
		[Test]
		public void B1_BetterProtection_HigherScore()
		{
			CoverCandidate weak = StandingCover(1, new Vector3(4f, 0f, 0f), Vector3.forward);
			weak.StandingProfile = Profile(0.2f);
			weak.CrouchProfile = Profile(0.2f);
			CoverCandidate strong = StandingCover(2, new Vector3(4f, 0f, 0f), Vector3.forward);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 18f));
			Assert.Greater(
				CoverScoreMath.PositionScore(strong, in situation, null),
				CoverScoreMath.PositionScore(weak, in situation, null));
		}

		[Test]
		public void B2_SamePosition_DifferentStance_DifferentScore()
		{
			CoverCandidate low = CrouchCover(1, new Vector3(3f, 0f, 0f), Vector3.forward);
			CoverSituation standing = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 16f));
			standing.Stance = CoverStance.Standing;
			CoverSituation crouch = standing;
			crouch.Stance = CoverStance.Crouch;
			Assert.AreNotEqual(
				CoverScoreMath.ProtectionScore(low, in standing),
				CoverScoreMath.ProtectionScore(low, in crouch));
			Assert.Greater(
				CoverScoreMath.PositionScore(low, in crouch, null),
				CoverScoreMath.PositionScore(low, in standing, null));
		}
		#endregion

		#region C Visibility
		[Test]
		public void C1_VisibleTarget_HigherThanBlocked()
		{
			CoverCandidate candidate = StandingCover(1, new Vector3(2f, 0f, 4f), Vector3.forward);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 20f));
			var blocked = new BlockingLos { BlockedFrom = candidate.Position + Vector3.up * CoverScoreMath.EyeHeightMeters };
			Assert.Greater(
				CoverScoreMath.VisibilityScore(candidate, in situation, null),
				CoverScoreMath.VisibilityScore(candidate, in situation, blocked));
		}

		[Test]
		public void C2_BlockedTarget_LowersScore()
		{
			CoverCandidate candidate = StandingCover(1, new Vector3(2f, 0f, 4f), Vector3.forward);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 20f));
			var blocked = new BlockingLos { BlockedFrom = candidate.Position + Vector3.up * CoverScoreMath.EyeHeightMeters };
			Assert.Greater(
				CoverScoreMath.PositionScore(candidate, in situation, null),
				CoverScoreMath.PositionScore(candidate, in situation, blocked));
		}
		#endregion

		#region D Travel
		[Test]
		public void D1_NearerCandidate_PreferredWhenEqual()
		{
			CoverCandidate near = StandingCover(1, new Vector3(3f, 0f, 0f), Vector3.forward);
			CoverCandidate far = StandingCover(2, new Vector3(14f, 0f, 0f), Vector3.forward);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 20f));
			var evaluator = new CoverPositionEvaluator();
			CoverEvaluationResult result = evaluator.Evaluate(new[] { near, far }, in situation);
			Assert.IsTrue(result.HasBest);
			Assert.AreEqual(1, result.Best.Candidate.CandidateId);
		}

		[Test]
		public void D2_FarCandidate_CanWinIfSubstantiallyBetter()
		{
			CoverCandidate nearWeak = StandingCover(1, new Vector3(3f, 0f, 0f), Vector3.forward);
			nearWeak.StandingProfile = Profile(0.1f);
			nearWeak.CrouchProfile = Profile(0.1f);
			nearWeak.StandingValid = false;
			nearWeak.CrouchValid = true;
			nearWeak.CoverType = CoverType.Partial;
			CoverCandidate farStrong = StandingCover(2, new Vector3(12f, 0f, 0f), Vector3.forward);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 20f));
			var evaluator = new CoverPositionEvaluator();
			CoverEvaluationResult result = evaluator.Evaluate(new[] { nearWeak, farStrong }, in situation);
			Assert.AreEqual(2, result.Best.Candidate.CandidateId);
		}
		#endregion

		#region E Current position
		[Test]
		public void E1_CurrentIsGood_NoForcedMove()
		{
			CoverCandidate current = StandingCover(1, Vector3.zero, Vector3.forward);
			CoverCandidate slightly = StandingCover(2, new Vector3(1.2f, 0f, 0f), Vector3.forward);
			slightly.StandingProfile = Profile(1f);
			slightly.CrouchProfile = Profile(1f);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 18f));
			var evaluator = new CoverPositionEvaluator();
			CoverEvaluationResult result = evaluator.Evaluate(new[] { current, slightly }, in situation);
			Assert.IsFalse(result.RepositionRecommended);
		}

		[Test]
		public void E2_CandidateSubstantiallyBetter_Wins()
		{
			CoverCandidate cover = StandingCover(1, new Vector3(4f, 0f, 0f), Vector3.forward);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 18f));
			var evaluator = new CoverPositionEvaluator();
			CoverEvaluationResult result = evaluator.Evaluate(new[] { cover }, in situation);
			Assert.IsTrue(result.HasBest);
			Assert.AreEqual(1, result.Best.Candidate.CandidateId);
			Assert.IsTrue(result.RepositionRecommended);
			Assert.IsTrue(CoverSwitchMath.ShouldReposition(
				result.Current.Score,
				result.Best.Score,
				CoverSwitchMath.DefaultSwitchingCost));
		}
		#endregion

		#region F Weapon interface
		[Test]
		public void F1_WeaponProfile_ReceivesCandidate()
		{
			CoverCandidate candidate = StandingCover(1, new Vector3(12f, 0f, 0f), Vector3.forward);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 40f));
			situation.Weapon = CoverWeaponClass.Sniper;
			float score = CoverScoreMath.WeaponScore(candidate, in situation);
			Assert.Greater(score, 0f);
		}

		[Test]
		public void F2_BaselineWeaponScore_Deterministic()
		{
			CoverCandidate candidate = StandingCover(1, new Vector3(8f, 0f, 0f), Vector3.forward);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 30f));
			Assert.AreEqual(
				CoverScoreMath.WeaponScore(candidate, in situation),
				CoverScoreMath.WeaponScore(candidate, in situation));
		}

		[Test]
		public void F3_ChangingWeapon_CanChangeScore()
		{
			CoverCandidate candidate = StandingCover(1, new Vector3(14f, 0f, 0f), Vector3.forward);
			CoverSituation rifle = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 40f));
			CoverSituation sniper = rifle;
			sniper.Weapon = CoverWeaponClass.Sniper;
			Assert.AreNotEqual(
				CoverScoreMath.PositionScore(candidate, in rifle, null),
				CoverScoreMath.PositionScore(candidate, in sniper, null));
		}
		#endregion

		#region G Mission
		[Test]
		public void G1_SameCandidate_DifferentMission_DifferentScore()
		{
			CoverCandidate candidate = StandingCover(1, new Vector3(0f, 0f, 10f), Vector3.forward);
			CoverSituation defense = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 30f));
			defense.Mission = CoverMissionIntent.Defense;
			CoverSituation attack = defense;
			attack.Mission = CoverMissionIntent.Attack;
			Assert.AreNotEqual(
				CoverScoreMath.MissionScore(candidate, in defense),
				CoverScoreMath.MissionScore(candidate, in attack));
			Assert.AreNotEqual(
				CoverScoreMath.PositionScore(candidate, in defense, null),
				CoverScoreMath.PositionScore(candidate, in attack, null));
		}
		#endregion

		#region H Cache
		[Test]
		public void H1_SameVersion_ReusesEvaluation()
		{
			CoverCandidate candidate = StandingCover(1, new Vector3(4f, 0f, 0f), Vector3.forward);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 18f));
			var evaluator = new CoverPositionEvaluator();
			CoverEvaluationResult first = evaluator.Evaluate(new[] { candidate }, in situation);
			CoverEvaluationResult second = evaluator.Evaluate(new[] { candidate }, in situation);
			Assert.AreEqual(1, evaluator.EvaluateCount);
			Assert.IsTrue(second.FromCache);
			Assert.AreEqual(first.Best.Score, second.Best.Score);
		}

		[Test]
		public void H2_TargetChanged_Invalidates()
		{
			CoverCandidate candidate = StandingCover(1, new Vector3(4f, 0f, 0f), Vector3.forward);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 18f));
			var evaluator = new CoverPositionEvaluator();
			evaluator.Evaluate(new[] { candidate }, in situation);
			situation.TargetPosition = new Vector3(8f, 1.5f, 18f);
			evaluator.Evaluate(new[] { candidate }, in situation);
			Assert.AreEqual(2, evaluator.EvaluateCount);
		}

		[Test]
		public void H3_GeometryVersionChanged_Invalidates()
		{
			CoverCandidate candidate = StandingCover(1, new Vector3(4f, 0f, 0f), Vector3.forward);
			candidate.GeometryVersion = 1;
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 18f));
			situation.GeometryVersion = 1;
			var evaluator = new CoverPositionEvaluator();
			evaluator.Evaluate(new[] { candidate }, in situation);
			candidate.GeometryVersion = 2;
			situation.GeometryVersion = 2;
			evaluator.Evaluate(new[] { candidate }, in situation);
			Assert.AreEqual(2, evaluator.EvaluateCount);
		}

		[Test]
		public void H4_MissionChanged_Invalidates()
		{
			CoverCandidate candidate = StandingCover(1, new Vector3(4f, 0f, 0f), Vector3.forward);
			CoverSituation situation = RifleAt(Vector3.zero, new Vector3(0f, 1.5f, 18f));
			situation.Mission = CoverMissionIntent.Defense;
			var evaluator = new CoverPositionEvaluator();
			evaluator.Evaluate(new[] { candidate }, in situation);
			situation.Mission = CoverMissionIntent.Attack;
			evaluator.Evaluate(new[] { candidate }, in situation);
			Assert.AreEqual(2, evaluator.EvaluateCount);
		}
		#endregion

		#region I Multi-unit
		[Test]
		public void I1_TwentyUnits_ThreeRegions_SharedGeometry_IndependentScores()
		{
			var source = new RecordingSource();
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
			int evaluations = 0;
			Vector3 r1 = Vector3.zero;
			Vector3 r2 = new Vector3(CoverSpatialMath.DefaultRegionSizeMeters, 0f, 0f);
			Vector3 r3 = new Vector3(0f, 0f, CoverSpatialMath.DefaultRegionSizeMeters);
			evaluations += EvaluateUnits(cache, r1, 7);
			evaluations += EvaluateUnits(cache, r2, 7);
			evaluations += EvaluateUnits(cache, r3, 6);
			Assert.AreEqual(3, cache.GenerationCount);
			Assert.AreEqual(3, source.GenerateCount);
			Assert.AreEqual(20, evaluations);
			Assert.IsFalse(HasScoreOnSharedCandidate(cache.GetCandidates(r1)[0]));
		}
		#endregion

		#region Helpers
		private static int EvaluateUnits(SharedCoverSpatialCache _cache, Vector3 _anchor, int _count)
		{
			int n = 0;
			for (int i = 0; i < _count; i++)
			{
				Vector3 pos = _anchor + Vector3.right * (i * 1.1f);
				IReadOnlyList<CoverCandidate> candidates = _cache.GetCandidates(pos);
				CoverSituation situation = RifleAt(pos, pos + new Vector3(0f, 1.5f, 20f));
				var evaluator = new CoverPositionEvaluator();
				evaluator.Evaluate(candidates, in situation);
				n += evaluator.EvaluateCount;
			}

			return n;
		}

		private static bool HasScoreOnSharedCandidate(CoverCandidate _candidate)
		{
			return _candidate.GetType().GetField("Score") != null;
		}

		private static CoverSituation RifleAt(Vector3 _unit, Vector3 _target)
		{
			return new CoverSituation
			{
				UnitPosition = _unit,
				Stance = CoverStance.Standing,
				Mission = CoverMissionIntent.Hold,
				Weapon = CoverWeaponClass.Rifle,
				Rank = CoverRankClass.Soldier,
				TargetPosition = _target,
				HasTarget = true,
				SectorForward = Vector3.forward,
				GeometryVersion = 1
			};
		}

		private static CoverCandidate StandingCover(int _id, Vector3 _position, Vector3 _normal)
		{
			return new CoverCandidate
			{
				CandidateId = _id,
				Position = _position,
				Normal = _normal,
				CoverType = CoverType.Standing,
				StandingValid = true,
				CrouchValid = true,
				NavMeshValid = true,
				StandingProfile = Profile(1f),
				CrouchProfile = Profile(1f),
				GeometryVersion = 1
			};
		}

		private static CoverCandidate CrouchCover(int _id, Vector3 _position, Vector3 _normal)
		{
			CoverCandidate candidate = StandingCover(_id, _position, _normal);
			candidate.CoverType = CoverType.Crouch;
			candidate.StandingValid = false;
			candidate.StandingProfile = Profile(0.1f);
			candidate.CrouchProfile = Profile(1f);
			return candidate;
		}

		private static CoverProtectionProfile Profile(float _value)
		{
			return new CoverProtectionProfile
			{
				Head = _value,
				Torso = _value,
				Pelvis = _value,
				Legs = _value
			};
		}
		#endregion
	}
}
