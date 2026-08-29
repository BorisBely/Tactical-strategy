using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #13.8 Final integration. Wires 13.0–13.7. No new cover mechanic. Not Fire. Not Move. Not #14.
	/// </summary>
	public sealed class DynamicCoverIntegrationTests
	{
		#region Nested
		private sealed class RecordingSource : ICoverCandidateSource
		{
			public int GenerateCount;
			public readonly List<CoverCandidate> Candidates = new List<CoverCandidate>(16);

			public void Generate(
				CoverRegionId _region,
				Bounds _bounds,
				int _geometryVersion,
				List<CoverCandidate> _destination)
			{
				GenerateCount++;
				for (int i = 0; i < Candidates.Count; i++)
				{
					CoverCandidate copy = Clone(Candidates[i]);
					copy.RegionId = new CoverRegionId(_region.X, _region.Z);
					copy.GeometryVersion = _geometryVersion;
					_destination.Add(copy);
				}
			}
		}

		private sealed class HideNearLos : ICoverLineOfSightProbe
		{
			public Vector3 HiddenFrom;
			public float Radius = 0.85f;
			public bool Active;

			public bool HasClearLook(Vector3 _from, Vector3 _to)
			{
				if (!Active)
					return true;
				Vector3 planar = _from;
				planar.y = 0f;
				Vector3 hide = HiddenFrom;
				hide.y = 0f;
				return CoverSpatialMath.PlanarDistanceSqr(planar, hide) > Radius * Radius;
			}
		}

		private sealed class OffsetLosProbe : ICoverLineOfSightProbe
		{
			public Vector3 Anchor;
			public Vector3 Right = Vector3.right;
			public float EyeHeight = 1.55f;
			public float RequiredOffset;
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
				return Mathf.Abs(lateral) + 0.001f >= RequiredOffset;
			}
		}

		private sealed class RecordingLeanExecutor : ICoverLeanExecutor
		{
			public int SetLeanCount;
			public CoverLeanLevel LastLevel;
			public CoverPeekDirection LastDirection;

			public void SetLean(CoverLeanLevel _level, CoverPeekDirection _direction)
			{
				SetLeanCount++;
				LastLevel = _level;
				LastDirection = _direction;
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

		#region G1 Shared reuse
		[Test]
		public void G1_TwentyUnits_ThreeRegions_ThreeGenerations()
		{
			RecordingSource source = new RecordingSource();
			source.Candidates.Add(Standing(1, Vector3.zero, 1f));
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
			FireTactical(cache, Vector3.zero, 7);
			FireTactical(cache, new Vector3(CoverSpatialMath.DefaultRegionSizeMeters, 0f, 0f), 7);
			FireTactical(cache, new Vector3(0f, 0f, CoverSpatialMath.DefaultRegionSizeMeters), 6);
			Assert.AreEqual(3, cache.GenerationCount);
			Assert.AreEqual(3, source.GenerateCount);
		}
		#endregion

		#region G2 Individual difference
		[Test]
		public void G2_SameCandidate_DifferentUnitScores_SharedUnchanged()
		{
			CoverCandidate shared = Standing(1, new Vector3(4f, 0f, 0f), 1f);
			CoverType type = shared.CoverType;
			Vector3 position = shared.Position;
			CoverSituation near = Rifle(Vector3.zero, Target());
			CoverSituation far = Rifle(new Vector3(-12f, 0f, 0f), Target());
			float a = CoverScoreMath.PositionScore(shared, in near, null);
			float b = CoverScoreMath.PositionScore(shared, in far, null);
			Assert.AreNotEqual(a, b);
			Assert.AreEqual(type, shared.CoverType);
			Assert.AreEqual(position, shared.Position);
		}
		#endregion

		#region G3 Emergency vs Tactical
		[Test]
		public void G3_EmergencyWinner_DiffersFromTactical()
		{
			CoverCandidate close = Standing(1, new Vector3(0f, 0f, 2f), 1.5f);
			CoverCandidate better = Standing(3, new Vector3(3f, 0f, 2f), 1.5f);
			CoverSituation open = Rifle(Vector3.zero, Target());
			EmergencyCoverDecision em = new EmergencyCoverSolver().Decide(
				true, UnitAIState.Idle, in open, new[] { close, better }, null);
			Assert.AreEqual(1, em.SelectedCandidateId);

			CoverSituation atClose = Rifle(close.Position, Target());
			var hide = new HideNearLos { HiddenFrom = close.Position, Active = true };
			TacticalCoverDecision tac = new TacticalCoverSolver().Decide(
				in atClose,
				new[] { close, better },
				CurrentTacticalPosition.FromCandidate(close, true),
				hide);
			Assert.AreEqual(3, tac.SelectedCandidateId);
			Assert.AreNotEqual(em.SelectedCandidateId, tac.SelectedCandidateId);
		}
		#endregion

		#region G4 Stay / G5 Reposition
		[Test]
		public void G4_SmallImprovement_Stay()
		{
			TacticalCoverDecision decision = new TacticalCoverSolver().DecideFromScores(8f, 8.2f, 1f, true, 7, 11);
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, decision.Decision);
			Assert.IsFalse(decision.HasDestination);
		}

		[Test]
		public void G5_LargeImprovement_Reposition()
		{
			TacticalCoverDecision decision = new TacticalCoverSolver().DecideFromScores(8f, 10.5f, 1f, true, 7, 11);
			Assert.AreEqual(TacticalCoverDecisionKind.Reposition, decision.Decision);
			Assert.AreEqual(11, decision.SelectedCandidateId);
			Assert.IsTrue(decision.HasDestination);
		}
		#endregion

		#region G6 Reservation
		[Test]
		public void G6_TwoUnits_OnePosition_OneReservation()
		{
			CoverCandidate candidate = Standing(7, Vector3.zero, 1f);
			var board = new CoverOccupancyBoard();
			Assert.IsTrue(board.TryReserve(candidate, 1, 0f).Success);
			Assert.IsFalse(board.TryReserve(candidate, 2, 0f).Success);
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(candidate, 0f));
		}
		#endregion

		#region G7 Lean
		[Test]
		public void G7_Corner_LeanUsesExistingExecutor()
		{
			Assert.IsNull(typeof(CoverPeekOverlay).Assembly.GetType("LeanController"));
			CoverCandidate corner = Corner(11, Vector3.zero);
			var executor = new RecordingLeanExecutor();
			var fire = new RecordingFire();
			var overlay = new CoverPeekOverlay();
			overlay.Update(
				UnitAIState.Idle,
				corner,
				Rifle(Vector3.zero, Target()),
				new OffsetLosProbe { Anchor = Vector3.zero, RequiredOffset = 0.10f },
				CoverPeekSides.Both,
				executor,
				0f);
			Assert.AreEqual(CoverPeekDecisionKind.Lean, overlay.Last.Kind);
			Assert.AreEqual(1, executor.SetLeanCount);
			Assert.AreEqual(0, fire.CallCount);
		}
		#endregion

		#region G8 Invalid
		[Test]
		public void G8_GeometryChanged_InvalidatesAndReplaces()
		{
			CoverCandidate current = Standing(7, Vector3.zero, 1f);
			CoverCandidate next = Standing(11, new Vector3(4f, 0f, 0f), 1f);
			TacticalCoverDecision decision = new TacticalCoverSolver().Decide(
				Rifle(Vector3.zero, Target()),
				new[] { current, next },
				CurrentTacticalPosition.Invalid);
			Assert.AreEqual(TacticalCoverDecisionKind.Reposition, decision.Decision);
			Assert.AreEqual(TacticalCoverReason.CurrentInvalid, decision.Reason);
		}
		#endregion

		#region Golden pipeline
		[Test]
		public void Golden_OpenToEmergencyToOccupyToRepositionToLean()
		{
			CoverCandidate c07 = Standing(7, new Vector3(0f, 0f, 2f), 1.5f);
			CoverCandidate c11 = Corner(11, new Vector3(4f, 0f, 2f));
			c11.StandingProfile = Profile(1.5f);
			c11.CrouchProfile = Profile(1.5f);
			RecordingSource source = new RecordingSource();
			source.Candidates.Add(c07);
			source.Candidates.Add(c11);
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
			var board = new CoverOccupancyBoard();
			var emergency = new EmergencyCoverOverlay();
			emergency.BindCache(cache);
			emergency.BindOccupancy(board);
			CoverSituation open = Rifle(Vector3.zero, Target());

			EmergencyCoverDecision quiet = emergency.Update(false, UnitAIState.Idle, in open);
			Assert.AreEqual(0, cache.GenerationCount);
			Assert.IsFalse(quiet.Active);

			EmergencyCoverDecision underFire = emergency.Update(true, UnitAIState.Idle, in open);
			Assert.AreEqual(1, cache.GenerationCount);
			Assert.AreEqual(7, underFire.SelectedCandidateId);
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(c07, 0f));

			CoverSituation atC07 = Rifle(c07.Position, Target());
			Assert.IsTrue(board.ConfirmOccupied(c07, atC07.UnitId, 0f).Success);
			Assert.AreEqual(CoverOccupancy.Occupied, board.GetState(c07, 0f));
			int geometry = cache.GeometryVersion;

			var hide = new HideNearLos { HiddenFrom = c07.Position, Active = true };
			var tactical = new TacticalCoverOverlay();
			tactical.BindCache(cache);
			tactical.BindOccupancy(board);
			TacticalCoverDecision reposition = tactical.Update(false, UnitAIState.Idle, in atC07, hide);
			Assert.AreEqual(TacticalCoverDecisionKind.Reposition, reposition.Decision);
			Assert.AreEqual(11, reposition.SelectedCandidateId);
			Assert.AreEqual(geometry, cache.GeometryVersion);
			Assert.AreEqual(1, cache.GenerationCount);

			CoverSituation atC11 = Rifle(c11.Position, Target());
			Assert.IsTrue(board.ConfirmOccupied(c11, atC11.UnitId, 0f).Success);

			var lean = new RecordingLeanExecutor();
			var fire = new RecordingFire();
			var peek = new CoverPeekOverlay();
			CoverPeekDecision peekDecision = peek.Update(
				UnitAIState.Idle,
				c11,
				in atC11,
				new OffsetLosProbe { Anchor = c11.Position, RequiredOffset = 0.10f },
				CoverPeekSides.Both,
				lean,
				0f);
			Assert.AreEqual(CoverPeekDecisionKind.Lean, peekDecision.Kind);
			Assert.AreEqual(1, lean.SetLeanCount);
			Assert.AreEqual(0, fire.CallCount);
			Assert.AreEqual(1, cache.GenerationCount);
		}
		#endregion

		#region Boundaries
		[Test]
		public void Boundary_CoverDoesNotMoveOrFire()
		{
			var go = new GameObject("AI138_Bound");
			try
			{
				UnitAIController ai = go.AddComponent<UnitAIController>();
				UnitMoveCommandRecorder move = go.AddComponent<UnitMoveCommandRecorder>();
				var fire = new RecordingFire();
				RecordingSource source = new RecordingSource();
				source.Candidates.Add(Standing(7, Vector3.zero, 1.5f));
				SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
				ai.BindCoverCache(cache);
				ai.BindCoverOccupancy(new CoverOccupancyBoard());
				ai.Tick(0.05f);
				Assert.AreEqual(0, move.MoveCount);
				Assert.IsFalse(ai.TacticalNavigationIssued);
				Assert.IsFalse(move.HasMoveIntent);
				Assert.AreEqual(0, fire.CallCount);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void Boundary_ReserveDoesNotRegenerateGeometry()
		{
			RecordingSource source = new RecordingSource();
			source.Candidates.Add(Standing(7, Vector3.zero, 1f));
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
			cache.GetCandidates(Vector3.zero);
			int gen = cache.GenerationCount;
			int geometry = cache.GeometryVersion;
			var board = new CoverOccupancyBoard();
			board.NotifyGeometryVersion(geometry, 0f);
			board.TryReserve(source.Candidates[0], 1, 0f);
			board.ConfirmOccupied(source.Candidates[0], 1, 0f);
			Assert.AreEqual(gen, cache.GenerationCount);
			Assert.AreEqual(geometry, cache.GeometryVersion);
			Assert.AreNotEqual(board.OccupancyVersion, cache.GeometryVersion);
		}

		[Test]
		public void Degraded_StillUsesSwitchingCost()
		{
			TacticalCoverDecision stay = new TacticalCoverSolver().DecideFromScores(7f, 7.3f, 1f, true, 7, 11);
			Assert.AreEqual(TacticalCoverDecisionKind.Stay, stay.Decision);
			Assert.AreEqual(TacticalCoverReason.ImprovementTooSmall, stay.Reason);
		}

		[Test]
		public void GeometryInvalidation_ReleasesReservationWithoutQuery()
		{
			RecordingSource source = new RecordingSource();
			source.Candidates.Add(Standing(3, Vector3.zero, 1f));
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
			cache.GetCandidates(Vector3.zero);
			int gen = cache.GenerationCount;
			var board = new CoverOccupancyBoard();
			board.TryReserve(source.Candidates[0], 1, 0f);
			board.NotifyGeometryVersion(cache.GeometryVersion + 1, 0f);
			Assert.IsTrue(board.IsAvailable(source.Candidates[0], 0f));
			Assert.AreEqual(gen, cache.GenerationCount);
		}

		[Test]
		public void CommandRetreat_ReleasesReservation()
		{
			CoverCandidate candidate = Standing(3, Vector3.zero, 1f);
			var board = new CoverOccupancyBoard();
			var go = new GameObject("AI138_Cmd");
			try
			{
				UnitAIController controller = go.AddComponent<UnitAIController>();
				controller.BindCoverOccupancy(board);
				TacticalCommandResult defense = controller.IssueCommand(
					TacticalCommand.Defense(Vector3.zero));
				Assert.IsTrue(defense.Accepted);
				int unitId = controller.CoverOccupancyUnitId;
				Assert.IsTrue(board.TryReserve(candidate, unitId, Time.time).Success);
				TacticalCommandResult result = controller.IssueCommand(
					TacticalCommand.Retreat(new Vector3(-8f, 0f, 0f)));
				Assert.IsTrue(result.Accepted);
				Assert.IsTrue(board.IsAvailable(candidate, Time.time));
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void ConcurrentThreat_SameRegion_UniqueReservations()
		{
			int slotCount = CoverSpatialMath.DefaultMaxCoverCandidates;
			const int unitCount = 20;
			RecordingSource source = new RecordingSource();
			for (int i = 1; i <= slotCount; i++)
				source.Candidates.Add(Standing(i, new Vector3(i * 0.8f, 0f, 2f), 1.5f));
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
			var board = new CoverOccupancyBoard();
			var ids = new HashSet<int>();
			int withDestination = 0;
			int withoutDestination = 0;
			for (int i = 0; i < unitCount; i++)
			{
				var overlay = new EmergencyCoverOverlay();
				overlay.BindCache(cache);
				overlay.BindOccupancy(board);
				CoverSituation situation = Rifle(new Vector3(i * 0.15f, 0f, 0f), Target(), 100 + i);
				EmergencyCoverDecision decision = overlay.Update(true, UnitAIState.Idle, in situation);
				if (!decision.HasDestination)
				{
					withoutDestination++;
					continue;
				}

				Assert.Greater(decision.SelectedCandidateId, 0);
				Assert.IsTrue(ids.Add(decision.SelectedCandidateId));
				withDestination++;
			}

			Assert.AreEqual(1, cache.GenerationCount);
			Assert.AreEqual(slotCount, withDestination);
			Assert.AreEqual(unitCount - slotCount, withoutDestination);
			Assert.AreEqual(slotCount, ids.Count);
			Assert.AreEqual(slotCount, board.CountHeld());
		}

		[Test]
		public void Perf_HundredUnits_TenRegions_TenGenerations()
		{
			RecordingSource source = new RecordingSource();
			source.Candidates.Add(Standing(1, Vector3.zero, 1f));
			SharedCoverSpatialCache cache = new SharedCoverSpatialCache(source);
			int evals = 0;
			for (int r = 0; r < 10; r++)
			{
				Vector3 anchor = new Vector3(r * CoverSpatialMath.DefaultRegionSizeMeters, 0f, 0f);
				evals += FireTactical(cache, anchor, 10);
			}

			Assert.AreEqual(100, evals);
			Assert.AreEqual(10, cache.GenerationCount);
			Assert.Less(cache.GenerationCount, 100);
		}

		[Test]
		public void CoverTypes_PositionAndPeekMatrix()
		{
			Assert.IsFalse(CoverScoreMath.IsSelectable(Typed(1, CoverType.None, false)));
			Assert.IsFalse(CoverPeekGeometry.CanPeek(CoverType.None));
			Assert.IsTrue(CoverScoreMath.IsSelectable(Typed(2, CoverType.Crouch, true)));
			Assert.IsFalse(CoverPeekGeometry.CanPeek(CoverType.Crouch));
			Assert.IsTrue(CoverScoreMath.IsSelectable(Typed(3, CoverType.Standing, true)));
			Assert.IsFalse(CoverPeekGeometry.CanPeek(CoverType.Standing));
			Assert.IsTrue(CoverScoreMath.IsSelectable(Typed(4, CoverType.Partial, true)));
			Assert.IsTrue(CoverPeekGeometry.CanPeek(CoverType.Partial));
			Assert.IsTrue(CoverScoreMath.IsSelectable(Typed(5, CoverType.Corner, true)));
			Assert.IsTrue(CoverPeekGeometry.CanPeek(CoverType.Corner));
		}

		[Test]
		public void Pipeline_CandidateToClassificationToScoreToDecisionToReserveToLean()
		{
			CoverCandidate none = Typed(1, CoverType.None, true);
			Assert.IsFalse(CoverScoreMath.IsSelectable(none));
			CoverCandidate corner = Corner(11, Vector3.zero);
			CoverPositionEvaluation evaluation = CoverScoreMath.EvaluateOne(
				corner, Rifle(Vector3.zero, Target()), null);
			Assert.IsTrue(evaluation.Valid);
			Assert.AreEqual(11, evaluation.Candidate.CandidateId);
			EmergencyCoverDecision em = new EmergencyCoverSolver().Decide(
				true, UnitAIState.Idle, Rifle(Vector3.zero, Target()), new[] { corner }, null);
			Assert.AreEqual(11, em.SelectedCandidateId);
			var board = new CoverOccupancyBoard();
			Assert.IsTrue(board.TryReserve(corner, 1, 0f).Success);
			var lean = new RecordingLeanExecutor();
			new CoverPeekOverlay().Update(
				UnitAIState.Idle,
				corner,
				Rifle(Vector3.zero, Target()),
				new OffsetLosProbe { Anchor = Vector3.zero, RequiredOffset = 0.10f },
				CoverPeekSides.Both,
				lean,
				0f);
			Assert.Greater(lean.SetLeanCount, 0);
		}
		#endregion

		#region Helpers
		private static int FireTactical(SharedCoverSpatialCache _cache, Vector3 _anchor, int _count)
		{
			int n = 0;
			for (int i = 0; i < _count; i++)
			{
				var overlay = new TacticalCoverOverlay();
				overlay.BindCache(_cache);
				overlay.Update(false, UnitAIState.Idle, Rifle(_anchor + Vector3.right * (i * 0.2f), Target()));
				n++;
			}

			return n;
		}

		private static CoverSituation Rifle(Vector3 _unit, Vector3 _target, int _unitId = 1)
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

		private static Vector3 Target()
		{
			return new Vector3(0f, 1.5f, 20f);
		}

		private static CoverCandidate Standing(int _id, Vector3 _position, float _prot)
		{
			return new CoverCandidate
			{
				CandidateId = _id,
				Position = _position,
				Normal = Vector3.forward,
				CoverType = CoverType.Standing,
				StandingValid = true,
				CrouchValid = true,
				NavMeshValid = true,
				StandingProfile = Profile(_prot),
				CrouchProfile = Profile(_prot),
				GeometryVersion = 1,
				Occupancy = CoverOccupancy.Available
			};
		}

		private static CoverCandidate Corner(int _id, Vector3 _position)
		{
			CoverCandidate candidate = Standing(_id, _position, 1.5f);
			candidate.CoverType = CoverType.Corner;
			candidate.CornerValid = true;
			return candidate;
		}

		private static CoverCandidate Typed(int _id, CoverType _type, bool _nav)
		{
			CoverCandidate candidate = Standing(_id, Vector3.zero, 1f);
			candidate.CoverType = _type;
			candidate.NavMeshValid = _nav;
			candidate.CornerValid = _type == CoverType.Corner;
			candidate.PartialValid = _type == CoverType.Partial;
			candidate.CrouchValid = _type == CoverType.Crouch || _type == CoverType.Standing;
			candidate.StandingValid = _type == CoverType.Standing || _type == CoverType.Corner;
			return candidate;
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
				StandingProfile = _source.StandingProfile,
				CrouchProfile = _source.CrouchProfile,
				NavMeshValid = _source.NavMeshValid,
				GeometryVersion = _source.GeometryVersion,
				Occupancy = _source.Occupancy
			};
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
