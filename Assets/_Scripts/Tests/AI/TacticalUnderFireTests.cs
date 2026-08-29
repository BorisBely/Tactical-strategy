using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #14.6 Movement Under Fire. Overlay does not Move. Not a new UnitAIState. Not Flee.
	/// </summary>
	public sealed class TacticalUnderFireTests
	{
		#region A Continue nearby cover
		[Test]
		public void A_Continue_NearbyCover()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			overlay.Update(in sit, new[] { CoverAheadHop(2) });
			sit.UnderFire = NearbyCoverFire(2f);
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
			sit.Now = 2f;
			TacticalMovementDecision next = overlay.Update(in sit, new[] { CoverAheadHop(2) });
			Assert.AreEqual(TacticalUnderFireAction.Continue, next.UnderFireAction);
			Assert.AreEqual(TacticalUnderFireReason.CoverAhead, next.UnderFireReason);
			Assert.AreEqual(0, overlay.ReevaluationCount);
			Assert.AreEqual(1, overlay.UnderFireEvaluationCount);
		}
		#endregion

		#region B Replan
		[Test]
		public void B_Replan_AlternativeSafer()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			overlay.Update(in sit, new[] { ExposedDirect(1) });
			sit.UnderFire = DangerousWithAlt();
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
			sit.Now = 2f;
			TacticalMovementDecision next = overlay.Update(
				in sit, new[] { ExposedDirect(1), CoveredHop(2) });
			Assert.AreEqual(TacticalUnderFireAction.Replan, next.UnderFireAction);
			Assert.AreEqual(TacticalUnderFireReason.AlternativeSafer, next.UnderFireReason);
			Assert.AreEqual(1, overlay.ReevaluationCount);
			Assert.AreEqual(1, overlay.ReplacementCount);
			Assert.AreEqual(2, next.SelectedCandidateId);
		}
		#endregion

		#region C Emergency cover
		[Test]
		public void C_EmergencyCover_NoForwardRoute()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			overlay.Update(in sit, new[] { ExposedDirect(1) });
			sit.UnderFire = NearbyEmergency();
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
			sit.Now = 2f;
			TacticalMovementDecision next = overlay.Update(in sit, new[] { ExposedDirect(1) });
			Assert.AreEqual(TacticalUnderFireAction.EmergencyCover, next.UnderFireAction);
			Assert.AreEqual(TacticalUnderFireReason.RouteTooExposed, next.UnderFireReason);
			Assert.IsTrue(next.NeedsEmergencyCover);
			Assert.AreEqual(0, overlay.ReevaluationCount);
			Assert.AreEqual(TacticalUnderFireAction.EmergencyCover, overlay.LastUnderFire.Action);
		}
		#endregion

		#region D Already protected
		[Test]
		public void D_AlreadyProtected_Hold()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			overlay.Update(in sit, new[] { CoverAheadHop(2) });
			sit.UnderFire = new TacticalUnderFireSituation
			{
				Present = true,
				ImmediateThreat = true,
				Moving = true,
				CurrentPositionProtected = true,
				RemainingHopMeters = 1f,
				CoverAheadMeters = 1f,
				CoverAheadProtected = true,
				CurrentExposure01 = 0.15f
			};
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
			sit.Now = 2f;
			TacticalMovementDecision next = overlay.Update(in sit, new[] { CoverAheadHop(2) });
			Assert.AreEqual(TacticalUnderFireAction.Hold, next.UnderFireAction);
			Assert.AreEqual(TacticalUnderFireReason.AlreadyProtected, next.UnderFireReason);
			Assert.AreEqual(0, overlay.ReevaluationCount);
		}
		#endregion

		#region E Short exposed
		[Test]
		public void E_ShortExposed_Continue()
		{
			TacticalUnderFireDecision decision = TacticalUnderFireMath.Decide(new TacticalUnderFireSituation
			{
				Present = true,
				ImmediateThreat = true,
				Moving = true,
				RemainingHopMeters = 4f,
				CoverAheadMeters = 4f,
				CoverAheadProtected = true,
				CurrentExposure01 = 0.7f
			});
			Assert.AreEqual(TacticalUnderFireAction.Continue, decision.Action);
			Assert.AreEqual(TacticalUnderFireReason.ShortDash, decision.Reason);
		}
		#endregion

		#region F Long exposed
		[Test]
		public void F_LongExposed_Replan()
		{
			TacticalUnderFireDecision decision = TacticalUnderFireMath.Decide(DangerousWithAlt());
			Assert.AreEqual(TacticalUnderFireAction.Replan, decision.Action);
			Assert.AreEqual(TacticalUnderFireReason.AlternativeSafer, decision.Reason);
		}
		#endregion

		#region G No alternatives
		[Test]
		public void G_NoAlternative_ContinueFallback_NotFlee()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			overlay.Update(in sit, new[] { ExposedDirect(1) });
			sit.UnderFire = NoAlternative();
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
			sit.Now = 2f;
			TacticalMovementDecision next = overlay.Update(in sit, new[] { ExposedDirect(1) });
			Assert.AreEqual(TacticalUnderFireAction.Continue, next.UnderFireAction);
			Assert.AreEqual(TacticalUnderFireReason.NoAlternativeFallback, next.UnderFireReason);
			Assert.AreNotEqual(TacticalUnderFireAction.EmergencyCover, next.UnderFireAction);
			Assert.AreEqual(0, overlay.ReevaluationCount);
		}
		#endregion

		#region Goldens
		[Test]
		public void Golden_DontPanic_CoverAhead()
		{
			TacticalUnderFireDecision decision = TacticalUnderFireMath.Decide(NearbyCoverFire(1.5f));
			Assert.AreEqual(TacticalUnderFireAction.Continue, decision.Action);
			Assert.AreEqual(TacticalUnderFireReason.CoverAhead, decision.Reason);
		}

		[Test]
		public void Golden_DontSuicide_NearbyCover()
		{
			TacticalUnderFireDecision decision = TacticalUnderFireMath.Decide(NearbyEmergency());
			Assert.AreEqual(TacticalUnderFireAction.EmergencyCover, decision.Action);
			Assert.AreNotEqual(TacticalUnderFireAction.Continue, decision.Action);
		}
		#endregion

		#region H Coalesce
		[Test]
		public void H_TenEvents_OneDecision()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			overlay.Update(in sit, new[] { CoverAheadHop(2) });
			sit.UnderFire = NearbyCoverFire(2f);
			for (int i = 0; i < 10; i++)
				overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
			sit.Now = 2f;
			overlay.Update(in sit, new[] { CoverAheadHop(2) });
			Assert.AreEqual(10, overlay.EventsReceived);
			Assert.AreEqual(10, overlay.LastReplanCheck.CoalescedCount);
			Assert.AreEqual(1, overlay.UnderFireEvaluationCount);
			Assert.AreEqual(0, overlay.ReevaluationCount);
			Assert.AreEqual(TacticalUnderFireAction.Continue, overlay.Last.UnderFireAction);
		}
		#endregion

		#region I Cooldown
		[Test]
		public void I_Cooldown_NoThrashing()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			overlay.Update(in sit, new[] { CoverAheadHop(2) });
			sit.UnderFire = NearbyCoverFire(2f);
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
			sit.Now = 2f;
			overlay.Update(in sit, new[] { CoverAheadHop(2) });
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
			sit.Now = 2.1f;
			overlay.Update(in sit, new[] { CoverAheadHop(2) });
			Assert.AreEqual(1, overlay.UnderFireEvaluationCount);
			Assert.AreEqual(0, overlay.ReevaluationCount);
			Assert.AreEqual(TacticalUnderFireAction.Continue, overlay.Last.UnderFireAction);
		}
		#endregion

		#region J Command
		[Test]
		public void J_CommandOverride_RetreatWins()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			overlay.Update(in sit, new[] { SafeDirect(1) });
			sit.Destination = new Vector3(20f, 0f, 0f);
			sit.UnderFire = new TacticalUnderFireSituation
			{
				Present = true,
				ImmediateThreat = true,
				Moving = true,
				MissionOverride = true,
				RemainingHopMeters = 20f,
				CurrentExposure01 = 0.8f,
				HasNearbyEmergencyCover = true
			};
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
			sit.Now = 2f;
			TacticalRouteCandidate retreat = SafeDirect(1);
			retreat.SetDirect(1, Vector3.zero, sit.Destination);
			retreat.UseAuthoredMetrics = true;
			retreat.DistanceMeters = 20f;
			retreat.TravelTimeSeconds = 13.3f;
			retreat.Exposure01 = 0.15f;
			retreat.Cover01 = 0.8f;
			retreat.Danger01 = 0.1f;
			retreat.MissionProgress01 = 0.8f;
			TacticalMovementDecision next = overlay.Update(in sit, new[] { retreat });
			Assert.AreEqual(TacticalUnderFireReason.CommandOverride, next.UnderFireReason);
			Assert.AreEqual(TacticalUnderFireAction.None, next.UnderFireAction);
			Assert.AreEqual(TacticalReplanReason.MissionChanged, overlay.LastReplanCheck.Reason);
			Assert.AreEqual(sit.Destination, next.Destination);
			Assert.AreEqual(1, overlay.ReplacementCount);
		}
		#endregion

		#region K Reservation
		[Test]
		public void K_Reservation_SwapsOnEmergencyReroute()
		{
			var overlay = new TacticalMovementOverlay();
			var board = new CoverOccupancyBoard();
			CoverCandidate c1 = Cover(1, new Vector3(5f, 0f, 6f));
			CoverCandidate c7 = Cover(7, new Vector3(3f, 0f, -4f));
			TacticalRouteSituation sit = Sit(1f);
			sit.Occupancy = board;
			sit.OccupancyUnitId = 7;
			sit.CoverCandidates = new[] { c1, c7 };
			sit.FinalCoverCandidateId = 1;
			overlay.Update(in sit, new[] { CoverHopRoute(2, c1) });
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(c1, 1f));
			sit.UnderFire = NearbyEmergency();
			sit.UnderFire.HasEmergencyDestination = true;
			sit.UnderFire.EmergencyDestination = c7.Position;
			sit.UnderFire.EmergencyCoverCandidateId = 7;
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
			sit.Now = 2f;
			overlay.Update(in sit, new[] { CoverHopRoute(2, c1) });
			Assert.IsTrue(overlay.NeedsEmergencyCover);
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(c1, 2f));
			sit.Destination = c7.Position;
			sit.FinalCoverCandidateId = 7;
			sit.Now = 3f;
			TacticalRouteCandidate toCover = CoverHopRoute(4, c7, c7.Position.x);
			toCover.SetCoverHops(
				4,
				Vector3.zero,
				c7.Position,
				new[] { TacticalRouteWaypoint.CoverHop(c7.Position, c7.CandidateId, c7.RegionId) });
			toCover.UseAuthoredMetrics = true;
			toCover.DistanceMeters = 8f;
			toCover.TravelTimeSeconds = 5.3f;
			toCover.Exposure01 = 0.2f;
			toCover.Cover01 = 0.85f;
			toCover.Danger01 = 0.2f;
			toCover.MissionProgress01 = 0.7f;
			overlay.Update(in sit, new[] { toCover });
			Assert.AreEqual(CoverOccupancy.Available, board.GetState(c1, 3f));
			Assert.AreEqual(CoverOccupancy.Reserved, board.GetState(c7, 3f));
		}
		#endregion

		#region L Progress
		[Test]
		public void L_Progress_FromCurrentPosition()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalRouteSituation sit = Sit(1f);
			sit.Destination = new Vector3(20f, 0f, 0f);
			TacticalRouteCandidate start = AuthoredDirect(1, 20f, 13.3f, 0.85f, 0.1f, 0.8f, 0.4f);
			start.SetDirect(1, Vector3.zero, sit.Destination);
			start.UseAuthoredMetrics = true;
			start.DistanceMeters = 20f;
			start.TravelTimeSeconds = 13.3f;
			start.Exposure01 = 0.85f;
			start.Cover01 = 0.1f;
			start.Danger01 = 0.8f;
			start.MissionProgress01 = 0.4f;
			overlay.Update(in sit, new[] { start });
			sit.Origin = new Vector3(14f, 0f, 0f);
			sit.UnderFire = DangerousWithAlt();
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
			sit.Now = 2f;
			TacticalRouteCandidate later = AuthoredWaypoint(
				4,
				sit.Origin,
				sit.Destination,
				new Vector3(16f, 0f, -4f),
				8f, 5.3f, 0.2f, 0.8f, 0.2f, 0.7f);
			later.Origin = sit.Origin;
			later.Destination = sit.Destination;
			TacticalMovementDecision next = overlay.Update(in sit, new[] { later });
			Assert.AreEqual(1, overlay.ReplacementCount);
			Assert.GreaterOrEqual(next.Origin.x, 12f);
			Assert.AreEqual(sit.Origin, overlay.Route.Origin);
		}
		#endregion

		#region Overlay contract
		[Test]
		public void Overlay_DoesNotMove()
		{
			var go = new GameObject("AI146_NoMove");
			try
			{
				UnitAIController controller = go.AddComponent<UnitAIController>();
				UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
				controller.EnsureStarted();
				TacticalRouteSituation sit = Sit(1f);
				controller.TacticalMovement.Update(in sit, new[] { CoverAheadHop(2) });
				sit.UnderFire = NearbyCoverFire(2f);
				controller.TacticalMovement.NotifyEvent(
					TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
				sit.Now = 2f;
				controller.TacticalMovement.Update(in sit, new[] { CoverAheadHop(2) });
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

		private static TacticalUnderFireSituation NearbyCoverFire(float _meters)
		{
			return new TacticalUnderFireSituation
			{
				Present = true,
				ImmediateThreat = true,
				Moving = true,
				RemainingHopMeters = _meters,
				CoverAheadMeters = _meters,
				CoverAheadProtected = true,
				CurrentExposure01 = 0.35f
			};
		}

		private static TacticalUnderFireSituation DangerousWithAlt()
		{
			return new TacticalUnderFireSituation
			{
				Present = true,
				ImmediateThreat = true,
				Moving = true,
				RemainingHopMeters = 16f,
				CoverAheadMeters = 16f,
				CoverAheadProtected = false,
				CurrentExposure01 = 0.82f,
				HasSaferAlternative = true,
				AlternativeExposure01 = 0.31f
			};
		}

		private static TacticalUnderFireSituation NearbyEmergency()
		{
			return new TacticalUnderFireSituation
			{
				Present = true,
				ImmediateThreat = true,
				Moving = true,
				RemainingHopMeters = 20f,
				CoverAheadMeters = 20f,
				CoverAheadProtected = false,
				CurrentExposure01 = 0.8f,
				HasNearbyEmergencyCover = true,
				HasCoverCandidates = true
			};
		}

		private static TacticalUnderFireSituation NoAlternative()
		{
			return new TacticalUnderFireSituation
			{
				Present = true,
				ImmediateThreat = true,
				Moving = true,
				RemainingHopMeters = 20f,
				CoverAheadProtected = false,
				CurrentExposure01 = 0.8f
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

		private static TacticalRouteCandidate CoverAheadHop(int _id)
		{
			CoverCandidate cover = Cover(7, new Vector3(2f, 0f, 0f));
			return CoverHopRoute(_id, cover);
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
