using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #14.5 Event-driven Replanning. Gate + commitment. Overlay does not Move. Not #14.6.
	/// </summary>
	public sealed class TacticalReplanTests
	{
		#region Nested
		private sealed class BlockedHopProbe : ITacticalRoutePathProbe
		{
			public Vector3 Blocked;

			public bool IsDestinationValid(Vector3 _destination)
			{
				return TacticalRouteViability.IsFinitePoint(_destination);
			}

			public bool IsReachable(
				Vector3 _origin,
				Vector3 _destination,
				IReadOnlyList<TacticalRouteWaypoint> _intermediates)
			{
				if (_intermediates == null)
					return true;
				for (int i = 0; i < _intermediates.Count; i++)
				{
					if (CoverSpatialMath.PlanarDistanceSqr(_intermediates[i].Position, Blocked) < 0.36f)
						return false;
				}

				return true;
			}
		}
		#endregion

		#region A No event
		[Test]
		public void A_NoEvent_NoReplan()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			overlay.Update(in sit, new[] { SafeDirect(1) });
			int evals = overlay.Evaluator.EvaluationCount;
			for (int i = 0; i < 100; i++)
			{
				sit.Now = 1f + i * 0.05f;
				overlay.Update(in sit, new[] { SafeDirect(1) });
			}

			Assert.AreEqual(evals, overlay.Evaluator.EvaluationCount);
			Assert.AreEqual(0, overlay.ReevaluationCount);
			Assert.AreEqual(0, overlay.ReplacementCount);
			Assert.AreEqual(TacticalReplanReason.NoEvent, overlay.LastReplanCheck.Reason);
			Assert.AreEqual(TacticalRouteCommitStatus.Committed, overlay.CommitStatus);
		}
		#endregion

		#region B Minor
		[Test]
		public void B_MinorExposure_NoReplan()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			overlay.Update(in sit, new[] { SafeDirect(1) });
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.EnemyMoved, 0.02f));
			sit.Now = 2f;
			overlay.Update(in sit, new[] { SafeDirect(1) });
			Assert.AreEqual(1, overlay.Evaluator.EvaluationCount);
			Assert.AreEqual(0, overlay.ReevaluationCount);
			Assert.IsFalse(overlay.LastReplanCheck.ShouldReevaluate);
			Assert.AreEqual(TacticalReplanReason.DeltaTooSmall, overlay.LastReplanCheck.Reason);
		}
		#endregion

		#region C Major
		[Test]
		public void C_MajorExposure_Replans()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			overlay.Update(in sit, new[] { ExposedDirect(1) });
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.EnemyMoved, 0.47f));
			sit.Now = 2f;
			TacticalMovementDecision next = overlay.Update(
				in sit, new[] { ExposedDirect(1), CoveredHop(2) });
			Assert.AreEqual(1, overlay.ReevaluationCount);
			Assert.AreEqual(1, overlay.ReplacementCount);
			Assert.AreEqual(TacticalReplanAction.Replace, next.ReplanAction);
			Assert.AreEqual(2, next.SelectedCandidateId);
		}
		#endregion

		#region D ImmediateThreat
		[Test]
		public void D_ImmediateThreat_Reassesses()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			overlay.Update(in sit, new[] { SafeDirect(1) });
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
			sit.Now = 2f;
			overlay.Update(in sit, new[] { SafeDirect(1) });
			Assert.AreEqual(1, overlay.ReevaluationCount);
			Assert.AreEqual(TacticalReplanEventKind.ImmediateThreat, overlay.LastReplanCheck.EventKind);
		}
		#endregion

		#region E Invalid
		[Test]
		public void E_RouteBlocked_MandatoryReplan()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			Vector3 hop = new Vector3(5f, 0f, 6f);
			overlay.Update(in sit, new[] { CoveredHop(2) });
			Assert.AreEqual(2, overlay.Last.SelectedCandidateId);
			overlay.BindPathProbe(new BlockedHopProbe { Blocked = hop });
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.RouteBlocked, 1f));
			sit.Now = 2f;
			TacticalMovementDecision next = overlay.Update(
				in sit, new[] { SafeDirect(1), CoveredHop(2) });
			Assert.IsTrue(overlay.LastReplanCheck.Mandatory);
			Assert.AreEqual(1, overlay.ReplacementCount);
			Assert.AreEqual(1, next.SelectedCandidateId);
			Assert.AreEqual(TacticalRouteKind.Direct, next.Kind);
		}
		#endregion

		#region F Geometry
		[Test]
		public void F1_GeometryOffRoute_NoReplan()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			sit.GeometryVersion = 1;
			overlay.Update(in sit, new[] { SafeDirect(1) });
			overlay.NotifyEvent(TacticalReplanEvent.Geometry(false, 2));
			sit.Now = 2f;
			sit.GeometryVersion = 2;
			overlay.Update(in sit, new[] { SafeDirect(1) });
			Assert.AreEqual(0, overlay.ReevaluationCount);
			Assert.AreEqual(TacticalReplanReason.GeometryOffRoute, overlay.LastReplanCheck.Reason);
		}

		[Test]
		public void F2_GeometryOnRoute_Replans()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			sit.GeometryVersion = 1;
			overlay.Update(in sit, new[] { SafeDirect(1) });
			overlay.NotifyEvent(TacticalReplanEvent.Geometry(true, 2));
			sit.Now = 2f;
			sit.GeometryVersion = 2;
			overlay.Update(in sit, new[] { SafeDirect(1) });
			Assert.AreEqual(1, overlay.ReevaluationCount);
			Assert.AreEqual(TacticalReplanEventKind.GeometryChanged, overlay.LastReplanCheck.EventKind);
			Assert.IsTrue(overlay.LastReplanCheck.ShouldReevaluate);
			Assert.AreEqual(TacticalReplanAction.Keep, overlay.Last.ReplanAction);
			Assert.AreEqual(TacticalReplanReason.SameRoute, overlay.Last.ReplanReason);
		}
		#endregion

		#region G Command
		[Test]
		public void G_DestinationChange_NewRoute()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			overlay.Update(in sit, new[] { SafeDirect(1) });
			sit.Destination = new Vector3(20f, 0f, 0f);
			sit.Now = 2f;
			TacticalRouteCandidate nextDest = SafeDirect(1);
			nextDest.SetDirect(1, Vector3.zero, sit.Destination);
			nextDest.UseAuthoredMetrics = true;
			nextDest.DistanceMeters = 20f;
			nextDest.TravelTimeSeconds = 13.3f;
			nextDest.Exposure01 = 0.15f;
			nextDest.Cover01 = 0.8f;
			nextDest.Danger01 = 0.1f;
			nextDest.MissionProgress01 = 0.8f;
			TacticalMovementDecision next = overlay.Update(in sit, new[] { nextDest });
			Assert.AreEqual(1, overlay.ReplacementCount);
			Assert.AreEqual(TacticalReplanReason.MissionChanged, overlay.LastReplanCheck.Reason);
			Assert.AreEqual(sit.Destination, next.Destination);
			Assert.AreNotEqual(new Vector3(10f, 0f, 0f), next.Destination);
		}
		#endregion

		#region H Coalesce
		[Test]
		public void H_FiveEvents_OneReplan()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			overlay.Update(in sit, new[] { SafeDirect(1) });
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.EnemyMoved, 0.1f));
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.Sound, 0.05f));
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
			overlay.NotifyEvent(TacticalReplanEvent.Geometry(true, 2));
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.CoverInvalid, 1f));
			sit.Now = 2f;
			overlay.Update(in sit, new[] { SafeDirect(1), CoveredHop(2) });
			Assert.AreEqual(5, overlay.EventsReceived);
			Assert.AreEqual(5, overlay.LastReplanCheck.CoalescedCount);
			Assert.AreEqual(1, overlay.ReevaluationCount);
		}
		#endregion

		#region I Cooldown
		[Test]
		public void I_Cooldown_BlocksMinor()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			overlay.Update(in sit, new[] { ExposedDirect(1) });
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.EnemyMoved, 0.47f));
			sit.Now = 2f;
			overlay.Update(in sit, new[] { ExposedDirect(1), CoveredHop(2) });
			Assert.AreEqual(1, overlay.ReevaluationCount);
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.EnemyMoved, 0.4f));
			sit.Now = 2.1f;
			overlay.Update(in sit, new[] { ExposedDirect(1), CoveredHop(2) });
			Assert.AreEqual(1, overlay.ReevaluationCount);
			Assert.AreEqual(TacticalReplanReason.Cooldown, overlay.LastReplanCheck.Reason);
		}
		#endregion

		#region J Emergency bypass
		[Test]
		public void J_ImmediateThreat_BypassesCooldown()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			overlay.Update(in sit, new[] { ExposedDirect(1) });
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.EnemyMoved, 0.47f));
			sit.Now = 2f;
			overlay.Update(in sit, new[] { ExposedDirect(1), CoveredHop(2) });
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
			sit.Now = 2.1f;
			overlay.Update(in sit, new[] { SafeDirect(1), CoveredHop(2) });
			Assert.AreEqual(2, overlay.ReevaluationCount);
			Assert.IsTrue(overlay.LastReplanCheck.EmergencyBypass);
		}
		#endregion

		#region K L Reservations
		[Test]
		public void K_OldReservations_Released()
		{
			CoverCandidate c1 = Cover(1, new Vector3(5f, 0f, 6f));
			CoverCandidate c3 = Cover(3, new Vector3(5f, 0f, -6f));
			var board = new CoverOccupancyBoard();
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			sit.Occupancy = board;
			sit.OccupancyUnitId = 7;
			sit.CoverCandidates = new[] { c1, c3 };
			overlay.Update(in sit, new[] { CoverHopRoute(2, c1) });
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(c1, 1f));
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.RouteBlocked, 1f));
			sit.Now = 2f;
			overlay.Update(in sit, new[] { CoverHopRoute(4, c3) });
			Assert.AreEqual(CoverOccupancy.Available, board.GetState(c1, 2f));
			Assert.AreEqual(1, overlay.ReplacementCount);
		}

		[Test]
		public void L_NewReservations_Acquired()
		{
			CoverCandidate c1 = Cover(1, new Vector3(5f, 0f, 6f));
			CoverCandidate c3 = Cover(3, new Vector3(5f, 0f, -6f));
			var board = new CoverOccupancyBoard();
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			sit.Occupancy = board;
			sit.OccupancyUnitId = 7;
			sit.CoverCandidates = new[] { c1, c3 };
			overlay.Update(in sit, new[] { CoverHopRoute(2, c1) });
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.RouteBlocked, 1f));
			sit.Now = 2f;
			overlay.Update(in sit, new[] { CoverHopRoute(4, c3) });
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(c3, 2f));
			Assert.AreEqual(3, overlay.ReservedCoverCandidateId);
		}
		#endregion

		#region M Progress
		[Test]
		public void M_Progress_StartsFromCurrent()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			sit.Destination = new Vector3(20f, 0f, 0f);
			overlay.Update(in sit, new[] { CoverHopRoute(2, Cover(1, new Vector3(10f, 0f, 6f)), 20f) });
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.RouteBlocked, 1f));
			sit.Origin = new Vector3(14f, 0f, 0f);
			sit.Now = 2f;
			TacticalRouteCandidate next = CoverHopRoute(4, Cover(3, new Vector3(16f, 0f, -4f)), 20f);
			next.Origin = sit.Origin;
			next.Destination = sit.Destination;
			TacticalMovementDecision decision = overlay.Update(in sit, new[] { next });
			Assert.AreEqual(1, overlay.ReplacementCount);
			Assert.GreaterOrEqual(decision.Origin.x, 12f);
			Assert.AreEqual(sit.Origin, overlay.Route.Origin);
			Assert.GreaterOrEqual(
				TacticalReplanMath.Progress01(Vector3.zero, sit.Destination, sit.Origin),
				0.65f);
		}
		#endregion

		#region N Same route
		[Test]
		public void N_SameRoute_KeepWithoutReplace()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			overlay.Update(in sit, new[] { SafeDirect(1) });
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
			sit.Now = 2f;
			TacticalMovementDecision next = overlay.Update(in sit, new[] { SafeDirect(1) });
			Assert.AreEqual(1, overlay.ReevaluationCount);
			Assert.AreEqual(0, overlay.ReplacementCount);
			Assert.AreEqual(TacticalReplanAction.Keep, next.ReplanAction);
			Assert.AreEqual(TacticalReplanReason.SameRoute, next.ReplanReason);
			Assert.AreEqual(1, next.SelectedCandidateId);
		}
		#endregion

		#region Overlay contract
		[Test]
		public void Overlay_DoesNotMove()
		{
			var go = new GameObject("AI145_NoMove");
			try
			{
				UnitAIController controller = go.AddComponent<UnitAIController>();
				UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
				controller.EnsureStarted();
				TacticalRouteSituation sit = Sit(1f);
				controller.TacticalMovement.Update(in sit, new[] { SafeDirect(1) });
				controller.TacticalMovement.NotifyEvent(
					TacticalReplanEvent.Of(TacticalReplanEventKind.EnemyMoved, 0.5f));
				sit.Now = 2f;
				controller.TacticalMovement.Update(in sit, new[] { SafeDirect(1), CoveredHop(2) });
				Assert.AreEqual(0, recorder.MoveCount);
				Assert.IsFalse(controller.TacticalNavigationIssued);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}
		#endregion

		#region Helpers
		private static TacticalRouteSituation Sit(float _now)
		{
			return new TacticalRouteSituation
			{
				Origin = Vector3.zero,
				Destination = new Vector3(10f, 0f, 0f),
				HasDestination = true,
				Mode = TacticalMovementMode.Tactical,
				WalkSpeedMetersPerSecond = TacticalRouteScoreMath.DefaultWalkSpeed,
				Now = _now
			};
		}

		private static TacticalRouteCandidate SafeDirect(int _id)
		{
			return AuthoredDirect(_id, 10f, 6.7f, 0.15f, 0.8f, 0.1f, 0.8f);
		}

		private static TacticalRouteCandidate ExposedDirect(int _id)
		{
			return AuthoredDirect(_id, 10f, 6.7f, 0.9f, 0.1f, 0.8f, 0.5f);
		}

		private static TacticalRouteCandidate CoveredHop(int _id)
		{
			return AuthoredWaypoint(
				_id,
				Vector3.zero,
				new Vector3(10f, 0f, 0f),
				new Vector3(5f, 0f, 6f),
				16f, 10.7f, 0.15f, 0.85f, 0.2f, 0.7f);
		}

		private static TacticalRouteCandidate CoverHopRoute(
			int _id,
			CoverCandidate _cover,
			float _destX = 10f)
		{
			Vector3 destination = new Vector3(_destX, 0f, 0f);
			var candidate = new TacticalRouteCandidate();
			candidate.SetCoverHops(
				_id,
				Vector3.zero,
				destination,
				new[]
				{
					TacticalRouteWaypoint.CoverHop(
						_cover.Position, _cover.CandidateId, _cover.RegionId)
				});
			candidate.UseAuthoredMetrics = true;
			candidate.DistanceMeters = 16f;
			candidate.TravelTimeSeconds = 10.7f;
			candidate.Exposure01 = 0.2f;
			candidate.Cover01 = 0.85f;
			candidate.Danger01 = 0.2f;
			candidate.MissionProgress01 = 0.6f;
			return candidate;
		}

		private static TacticalRouteCandidate AuthoredDirect(
			int _id,
			float _distance,
			float _time,
			float _exposure,
			float _cover,
			float _danger,
			float _mission)
		{
			var candidate = new TacticalRouteCandidate();
			candidate.SetDirect(_id, Vector3.zero, new Vector3(10f, 0f, 0f));
			candidate.UseAuthoredMetrics = true;
			candidate.DistanceMeters = _distance;
			candidate.TravelTimeSeconds = _time;
			candidate.Exposure01 = _exposure;
			candidate.Cover01 = _cover;
			candidate.Danger01 = _danger;
			candidate.MissionProgress01 = _mission;
			return candidate;
		}

		private static TacticalRouteCandidate AuthoredWaypoint(
			int _id,
			Vector3 _origin,
			Vector3 _destination,
			Vector3 _hop,
			float _distance,
			float _time,
			float _exposure,
			float _cover,
			float _danger,
			float _mission)
		{
			var candidate = new TacticalRouteCandidate();
			candidate.SetWaypoint(_id, _origin, _destination, _hop);
			candidate.UseAuthoredMetrics = true;
			candidate.DistanceMeters = _distance;
			candidate.TravelTimeSeconds = _time;
			candidate.Exposure01 = _exposure;
			candidate.Cover01 = _cover;
			candidate.Danger01 = _danger;
			candidate.MissionProgress01 = _mission;
			return candidate;
		}

		private static CoverCandidate Cover(int _id, Vector3 _position)
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
				StandingProfile = new CoverProtectionProfile
				{
					Head = 1f, Torso = 1f, Pelvis = 1f, Legs = 1f
				},
				CrouchProfile = new CoverProtectionProfile
				{
					Head = 1f, Torso = 1f, Pelvis = 1f, Legs = 1f
				},
				GeometryVersion = 1
			};
		}
		#endregion
	}
}
