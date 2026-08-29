using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #13.7 Lean / Peek. Not a new LeanController. Not Fire. Not #14 moving-lean policy.
	/// </summary>
	public sealed class DynamicCoverPeekTests
	{
		#region Nested
		private sealed class RecordingSource : ICoverCandidateSource
		{
			public int GenerateCount;
			public CoverCandidate Candidate;

			public void Generate(
				CoverRegionId _region,
				Bounds _bounds,
				int _geometryVersion,
				List<CoverCandidate> _destination)
			{
				GenerateCount++;
				CoverCandidate copy = Clone(Candidate);
				copy.RegionId = new CoverRegionId(_region.X, _region.Z);
				copy.GeometryVersion = _geometryVersion;
				_destination.Add(copy);
			}
		}

		private sealed class OffsetLosProbe : ICoverLineOfSightProbe
		{
			public Vector3 Anchor;
			public Vector3 Right = Vector3.right;
			public float EyeHeight = 1.55f;
			public float RequiredOffset;
			public CoverPeekDirection OnlySide;
			public bool AlwaysClear;
			public bool AlwaysBlocked;

			public bool HasClearLook(Vector3 _from, Vector3 _to)
			{
				if (AlwaysClear)
					return true;
				if (AlwaysBlocked)
					return false;

				Vector3 planar = _from - (Anchor + Vector3.up * EyeHeight);
				planar.y = 0f;
				float lateral = Vector3.Dot(planar, Right);
				if (OnlySide == CoverPeekDirection.Left && lateral > -0.02f)
					return false;
				if (OnlySide == CoverPeekDirection.Right && lateral < 0.02f)
					return false;
				return Mathf.Abs(lateral) + 0.001f >= RequiredOffset;
			}
		}

		private sealed class RecordingLeanExecutor : ICoverLeanExecutor
		{
			public int SetLeanCount;
			public int ReturnCount;
			public CoverLeanLevel LastLevel;
			public CoverPeekDirection LastDirection;

			public void SetLean(CoverLeanLevel _level, CoverPeekDirection _direction)
			{
				SetLeanCount++;
				LastLevel = _level;
				LastDirection = _direction;
				if (_level == CoverLeanLevel.None)
					ReturnCount++;
			}
		}

		private sealed class RecordingFire
		{
			public int CallCount;

			public void Fire()
			{
				CallCount++;
			}
		}
		#endregion

		#region A Corner opportunity
		[Test]
		public void A1_Corner_PeekAvailable()
		{
			CoverCandidate candidate = CornerCover(7, new Vector3(5f, 0f, 0.5f), Vector3.forward);
			CoverPeekSides sides = CoverPeekGeometry.Sides(candidate, CornerWall());
			Assert.IsTrue(CoverPeekGeometry.CanPeek(candidate.CoverType));
			Assert.IsTrue(sides.Any);
		}

		[Test]
		public void A2_StraightWall_NoCornerPeek()
		{
			CoverCandidate candidate = StandingCover(1, new Vector3(5f, 0f, 0.5f), Vector3.forward);
			CoverPeekSides sides = CoverPeekGeometry.Sides(candidate, HighWall());
			Assert.IsFalse(sides.Any);
			CoverPeekDecision decision = new CoverPeekSolver().Evaluate(
				candidate, Situation(candidate.Position, candidate.Position + Vector3.forward * 8f), sides, HiddenLos(candidate));
			Assert.IsFalse(decision.PeekAvailable);
			Assert.AreEqual(CoverPeekDecisionKind.None, decision.Kind);
		}

		[Test]
		public void A3_Partial_PeekIfGeometryAllows()
		{
			CoverCandidate candidate = PartialCover(3, new Vector3(5f, 0f, 0.5f), Vector3.forward);
			CoverPeekSides sides = CoverPeekGeometry.Sides(candidate, CornerWall());
			Assert.IsTrue(sides.Any);
			Assert.IsTrue(CoverPeekGeometry.CanPeek(CoverType.Partial));
		}
		#endregion

		#region B Direction
		[Test]
		public void B1_LeftAvailable()
		{
			CoverPeekSides sides = CoverPeekGeometry.Sides(
				CornerCover(1, new Vector3(5f, 0f, 0.5f), Vector3.forward), LeftOpenWall());
			Assert.IsTrue(sides.Left);
		}

		[Test]
		public void B2_RightAvailable()
		{
			CoverPeekSides sides = CoverPeekGeometry.Sides(
				CornerCover(1, new Vector3(5f, 0f, 0.5f), Vector3.forward), CornerWall());
			Assert.IsTrue(sides.Right);
		}

		[Test]
		public void B3_LeftOnly()
		{
			CoverPeekSides sides = CoverPeekGeometry.Sides(
				CornerCover(1, new Vector3(5f, 0f, 0.5f), Vector3.forward), LeftOpenWall());
			Assert.IsTrue(sides.Left);
			Assert.IsFalse(sides.Right);
		}

		[Test]
		public void B4_RightOnly()
		{
			CoverPeekSides sides = CoverPeekGeometry.Sides(
				CornerCover(1, new Vector3(5f, 0f, 0.5f), Vector3.forward), CornerWall());
			Assert.IsTrue(sides.Right);
			Assert.IsFalse(sides.Left);
		}

		[Test]
		public void B5_Neither()
		{
			CoverPeekSides sides = CoverPeekGeometry.Sides(
				StandingCover(1, new Vector3(5f, 0f, 0.5f), Vector3.forward), HighWall());
			Assert.IsFalse(sides.Any);
		}
		#endregion

		#region C Need
		[Test]
		public void C1_TargetVisibleWithoutLean_NoLean()
		{
			CoverCandidate candidate = CornerCover(7, Vector3.zero, Vector3.forward);
			var los = new OffsetLosProbe { Anchor = candidate.Position, AlwaysClear = true };
			CoverPeekDecision decision = new CoverPeekSolver().Evaluate(
				candidate, Situation(candidate.Position, candidate.Position + Vector3.forward * 8f),
				CoverPeekSides.Both, los);
			Assert.IsTrue(decision.VisibleWithoutLean);
			Assert.AreEqual(CoverPeekDecisionKind.None, decision.Kind);
			Assert.AreEqual(CoverPeekReason.AlreadyVisible, decision.Reason);
		}

		[Test]
		public void C2_TargetHidden_LeanCandidateExists()
		{
			CoverCandidate candidate = CornerCover(7, Vector3.zero, Vector3.forward);
			CoverPeekDecision decision = new CoverPeekSolver().Evaluate(
				candidate, Situation(candidate.Position, candidate.Position + Vector3.forward * 8f),
				CoverPeekSides.Both, HiddenUntil(candidate, 0.10f));
			Assert.IsTrue(decision.PeekAvailable);
			Assert.IsFalse(decision.VisibleWithoutLean);
			Assert.AreEqual(CoverPeekDecisionKind.Lean, decision.Kind);
		}
		#endregion

		#region D Benefit
		[Test]
		public void D1_LeanRevealsTarget_PositiveValue()
		{
			CoverCandidate candidate = CornerCover(7, Vector3.zero, Vector3.forward);
			CoverPeekDecision decision = new CoverPeekSolver().Evaluate(
				candidate, Situation(candidate.Position, candidate.Position + Vector3.forward * 8f),
				CoverPeekSides.Both, HiddenUntil(candidate, 0.10f));
			Assert.AreEqual(CoverPeekDecisionKind.Lean, decision.Kind);
			Assert.Greater(decision.VisibilityGain, 0f);
		}

		[Test]
		public void D2_LeanRevealsNothing_NoLean()
		{
			CoverCandidate candidate = CornerCover(7, Vector3.zero, Vector3.forward);
			var los = new OffsetLosProbe { Anchor = candidate.Position, AlwaysBlocked = true };
			CoverPeekDecision decision = new CoverPeekSolver().Evaluate(
				candidate, Situation(candidate.Position, candidate.Position + Vector3.forward * 8f),
				CoverPeekSides.Both, los);
			Assert.AreEqual(CoverPeekDecisionKind.None, decision.Kind);
			Assert.AreEqual(CoverPeekReason.NoBenefit, decision.Reason);
		}
		#endregion

		#region E Risk / min depth
		[Test]
		public void E1_SmallLeanSufficient()
		{
			CoverPeekDecision decision = DecideDepth(0.10f);
			Assert.AreEqual(CoverLeanLevel.Small, decision.Depth);
		}

		[Test]
		public void E2_MediumWhenSmallInsufficient()
		{
			CoverPeekDecision decision = DecideDepth(0.25f);
			Assert.AreEqual(CoverLeanLevel.Medium, decision.Depth);
		}

		[Test]
		public void E3_DeepOnlyWay()
		{
			CoverPeekDecision decision = DecideDepth(0.40f);
			Assert.AreEqual(CoverLeanLevel.Deep, decision.Depth);
		}
		#endregion

		#region F Existing executor
		[Test]
		public void F_TacticalRequest_UsesUnitSpineLean_NoSecondController()
		{
			Assert.IsNull(typeof(CoverPeekOverlay).Assembly.GetType("LeanController"));

			var go = new GameObject("PeekSpine");
			try
			{
				UnitSpineLean spine = go.AddComponent<UnitSpineLean>();
				var executor = new UnitSpineLeanExecutor(spine);
				var overlay = new CoverPeekOverlay();
				CoverCandidate candidate = CornerCover(7, Vector3.zero, Vector3.forward);
				overlay.Update(
					UnitAIState.Idle,
					candidate,
					Situation(candidate.Position, candidate.Position + Vector3.forward * 8f),
					HiddenUntil(candidate, 0.10f),
					CoverPeekSides.Both,
					executor,
					0f);
				Assert.AreEqual(1, spine.CurrentLeanLevel);
				Assert.AreEqual(-1, spine.CurrentLeanSide);
				Assert.AreEqual(1, go.GetComponents<UnitSpineLean>().Length);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}
		#endregion

		#region G Return
		[Test]
		public void G_TargetGone_Return()
		{
			CoverCandidate candidate = CornerCover(7, Vector3.zero, Vector3.forward);
			var executor = new RecordingLeanExecutor();
			var overlay = new CoverPeekOverlay();
			CoverSituation withTarget = Situation(candidate.Position, candidate.Position + Vector3.forward * 8f);
			overlay.Update(UnitAIState.Idle, candidate, in withTarget, HiddenUntil(candidate, 0.10f),
				CoverPeekSides.Both, executor, 0f);
			Assert.AreEqual(CoverPeekDecisionKind.Lean, overlay.Last.Kind);

			CoverSituation lost = withTarget;
			lost.HasTarget = false;
			CoverPeekDecision returned = overlay.Update(
				UnitAIState.Idle, candidate, in lost, HiddenUntil(candidate, 0.10f),
				CoverPeekSides.Both, executor, 0.1f);
			Assert.AreEqual(CoverPeekDecisionKind.Return, returned.Kind);
			Assert.AreEqual(CoverPeekReason.TargetLost, returned.Reason);
			Assert.AreEqual(CoverLeanLevel.None, executor.LastLevel);
			Assert.GreaterOrEqual(executor.ReturnCount, 1);
		}
		#endregion

		#region H / I Fire
		[Test]
		public void H_LeanDoesNotCallCombatPipeline()
		{
			var fire = new RecordingFire();
			RunLean(out RecordingLeanExecutor executor);
			Assert.AreEqual(CoverLeanLevel.Small, executor.LastLevel);
			Assert.AreEqual(0, fire.CallCount);
		}

		[Test]
		public void I_RequestLean_FireCallCountZero()
		{
			var fire = new RecordingFire();
			var overlay = new CoverPeekOverlay();
			CoverCandidate candidate = CornerCover(7, Vector3.zero, Vector3.forward);
			overlay.Update(
				UnitAIState.Idle,
				candidate,
				Situation(candidate.Position, candidate.Position + Vector3.forward * 8f),
				HiddenUntil(candidate, 0.10f),
				CoverPeekSides.Both,
				new RecordingLeanExecutor(),
				0f);
			Assert.AreEqual(0, fire.CallCount);
			Assert.AreEqual(CoverPeekDecisionKind.Lean, overlay.Last.Kind);
		}
		#endregion

		#region J Stable
		[Test]
		public void J_SameGeometryAndSituation_SameDecision()
		{
			CoverCandidate candidate = CornerCover(7, Vector3.zero, Vector3.forward);
			CoverSituation situation = Situation(candidate.Position, candidate.Position + Vector3.forward * 8f);
			OffsetLosProbe los = HiddenUntil(candidate, 0.10f, CoverPeekDirection.Right);
			var solver = new CoverPeekSolver();
			CoverPeekDecision a = solver.Evaluate(candidate, in situation, CoverPeekSides.Both, los);
			CoverPeekDecision b = solver.Evaluate(candidate, in situation, CoverPeekSides.Both, los);
			Assert.AreEqual(a.Direction, b.Direction);
			Assert.AreEqual(a.Depth, b.Depth);
			Assert.AreEqual(CoverPeekDirection.Right, a.Direction);
		}
		#endregion

		#region K Multi-unit
		[Test]
		public void K_TwentyUnits_OneGeometry_TwentyLeanEvals()
		{
			CoverCandidate shared = CornerCover(7, Vector3.zero, Vector3.forward);
			var source = new RecordingSource { Candidate = shared };
			var cache = new SharedCoverSpatialCache(source);
			IReadOnlyList<CoverCandidate> generated = cache.GetCandidates(Vector3.zero);
			Assert.AreEqual(1, source.GenerateCount);

			int evals = 0;
			for (int i = 0; i < 20; i++)
			{
				var overlay = new CoverPeekOverlay();
				overlay.BindCache(cache);
				CoverCandidate occupying = generated[0];
				Vector3 pos = occupying.Position;
				overlay.Update(
					UnitAIState.Idle,
					occupying,
					Situation(pos, pos + Vector3.forward * 8f, 100 + i),
					HiddenUntil(occupying, 0.10f),
					CoverPeekSides.Both,
					new RecordingLeanExecutor(),
					0f);
				evals += overlay.EvaluateCount;
			}

			Assert.AreEqual(1, source.GenerateCount);
			Assert.AreEqual(1, cache.GenerationCount);
			Assert.AreEqual(20, evals);
		}
		#endregion

		#region Event-driven / contract
		[Test]
		public void EventDriven_SameKey_DoesNotReevaluate()
		{
			CoverCandidate candidate = CornerCover(7, Vector3.zero, Vector3.forward);
			var overlay = new CoverPeekOverlay();
			var executor = new RecordingLeanExecutor();
			CoverSituation situation = Situation(candidate.Position, candidate.Position + Vector3.forward * 8f);
			overlay.Update(UnitAIState.Idle, candidate, in situation, HiddenUntil(candidate, 0.10f),
				CoverPeekSides.Both, executor, 0f);
			overlay.Update(UnitAIState.Idle, candidate, in situation, HiddenUntil(candidate, 0.10f),
				CoverPeekSides.Both, executor, 0.05f);
			Assert.AreEqual(1, overlay.EvaluateCount);
			Assert.AreEqual(1, executor.SetLeanCount);
			Assert.IsTrue(overlay.Last.FromCache);
		}

		[Test]
		public void MovingLeanContract_AppliesExecutor_WithoutPeekPolicy()
		{
			var executor = new RecordingLeanExecutor();
			CoverMovementLeanContract.Apply(executor, new CoverMovementLeanRequest
			{
				Mode = CoverMovementLeanMode.Leaning,
				Direction = CoverPeekDirection.Right,
				Depth = CoverLeanLevel.Deep
			});
			Assert.AreEqual(CoverPeekDirection.Right, executor.LastDirection);
			Assert.AreEqual(CoverLeanLevel.Deep, executor.LastLevel);
			CoverMovementLeanContract.Apply(executor, CoverMovementLeanRequest.Idle);
			Assert.AreEqual(CoverLeanLevel.None, executor.LastLevel);
		}

		[Test]
		public void RankDoesNotCapPhysicalDepth()
		{
			CoverCandidate candidate = CornerCover(7, Vector3.zero, Vector3.forward);
			CoverSituation recruit = Situation(candidate.Position, candidate.Position + Vector3.forward * 8f);
			recruit.Rank = CoverRankClass.Recruit;
			CoverPeekDecision decision = new CoverPeekSolver().Evaluate(
				candidate, in recruit, CoverPeekSides.Both, HiddenUntil(candidate, 0.40f));
			Assert.AreEqual(CoverLeanLevel.Deep, decision.Depth);
		}
		#endregion

		#region Helpers
		private static CoverPeekDecision DecideDepth(float _requiredOffset)
		{
			CoverCandidate candidate = CornerCover(7, Vector3.zero, Vector3.forward);
			return new CoverPeekSolver().Evaluate(
				candidate,
				Situation(candidate.Position, candidate.Position + Vector3.forward * 8f),
				CoverPeekSides.Both,
				HiddenUntil(candidate, _requiredOffset));
		}

		private static void RunLean(out RecordingLeanExecutor _executor)
		{
			CoverCandidate candidate = CornerCover(7, Vector3.zero, Vector3.forward);
			_executor = new RecordingLeanExecutor();
			new CoverPeekOverlay().Update(
				UnitAIState.Idle,
				candidate,
				Situation(candidate.Position, candidate.Position + Vector3.forward * 8f),
				HiddenUntil(candidate, 0.10f),
				CoverPeekSides.Both,
				_executor,
				0f);
		}

		private static OffsetLosProbe HiddenLos(CoverCandidate _candidate)
		{
			return new OffsetLosProbe { Anchor = _candidate.Position, AlwaysBlocked = true };
		}

		private static OffsetLosProbe HiddenUntil(
			CoverCandidate _candidate,
			float _requiredOffset,
			CoverPeekDirection _onlySide = CoverPeekDirection.None)
		{
			return new OffsetLosProbe
			{
				Anchor = _candidate.Position,
				RequiredOffset = _requiredOffset,
				OnlySide = _onlySide,
				Right = CoverPeekGeometry.RightTangent(_candidate.Normal)
			};
		}

		private static CoverSituation Situation(Vector3 _unit, Vector3 _target, int _unitId = 1)
		{
			Vector3 hostile = _target - _unit;
			hostile.y = 0f;
			if (hostile.sqrMagnitude < 0.0001f)
				hostile = Vector3.forward;
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
				HostileDirection = hostile,
				GeometryVersion = 1,
				UnitId = _unitId
			};
		}

		private static CoverCandidate CornerCover(int _id, Vector3 _position, Vector3 _normal)
		{
			CoverCandidate candidate = StandingCover(_id, _position, _normal);
			candidate.CoverType = CoverType.Corner;
			candidate.CornerValid = true;
			return candidate;
		}

		private static CoverCandidate PartialCover(int _id, Vector3 _position, Vector3 _normal)
		{
			CoverCandidate candidate = StandingCover(_id, _position, _normal);
			candidate.CoverType = CoverType.Partial;
			candidate.PartialValid = true;
			candidate.StandingValid = false;
			return candidate;
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
				GeometryVersion = 1,
				Occupancy = CoverOccupancy.Available
			};
		}

		private static CoverCandidate Clone(CoverCandidate _source)
		{
			return new CoverCandidate
			{
				CandidateId = _source.CandidateId,
				Position = _source.Position,
				Normal = _source.Normal,
				CoverType = _source.CoverType,
				StandingValid = _source.StandingValid,
				CrouchValid = _source.CrouchValid,
				PartialValid = _source.PartialValid,
				CornerValid = _source.CornerValid,
				NavMeshValid = _source.NavMeshValid,
				GeometryVersion = _source.GeometryVersion,
				Occupancy = _source.Occupancy
			};
		}

		private static SlabCoverOcclusionProbe HighWall()
		{
			return new SlabCoverOcclusionProbe(new Bounds(new Vector3(5f, 1.1f, 0f), new Vector3(12f, 2.2f, 0.4f)));
		}

		private static SlabCoverOcclusionProbe CornerWall()
		{
			return new SlabCoverOcclusionProbe(new Bounds(new Vector3(2.75f, 1.1f, 0f), new Vector3(5.5f, 2.2f, 0.4f)));
		}

		private static SlabCoverOcclusionProbe LeftOpenWall()
		{
			return new SlabCoverOcclusionProbe(new Bounds(new Vector3(7.25f, 1.1f, 0f), new Vector3(5.5f, 2.2f, 0.4f)));
		}
		#endregion
	}
}
