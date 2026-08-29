using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AI.Tests
{
	/// <summary>
	/// #14C Threat Direction Knowledge. Spawn estimate, event updates, no polling, no Cover/Move/Aim.
	/// </summary>
	[Category("ThreatDirection")]
	public sealed class ThreatDirectionTests
	{
		#region Constants
		private static readonly Vector3 s_Origin = Vector3.zero;
		private static readonly Vector3 s_NorthPoint = new Vector3(0f, 0f, 10f);
		private static readonly Vector3 s_EastPoint = new Vector3(10f, 0f, 0f);
		private static readonly Vector3 s_NorthEastPoint = new Vector3(10f, 0f, 10f);
		#endregion

		#region A Contract
		[Test]
		public void A1_Expected_StateValid()
		{
			ThreatDirectionController controller = StartedNorth();
			Assert.IsTrue(controller.TryGetThreatDirection(out ThreatDirectionKnowledge knowledge));
			Assert.AreEqual(ThreatDirectionState.Expected, knowledge.State);
			Assert.AreEqual(ThreatDirectionSource.InitialEstimate, knowledge.Source);
			Assert.AreEqual(ThreatDirectionCompass.North, knowledge.Compass);
			Assert.Greater(knowledge.Confidence, 0f);
			Assert.Greater(knowledge.UncertaintyDegrees, 0f);
		}

		[Test]
		public void A2_Known_StateValid()
		{
			ThreatDirectionController controller = StartedNorth();
			Assert.IsTrue(controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f));
			Assert.IsTrue(controller.TryGetThreatDirection(out ThreatDirectionKnowledge knowledge));
			Assert.AreEqual(ThreatDirectionState.Known, knowledge.State);
			Assert.AreEqual(ThreatDirectionSource.Visual, knowledge.Source);
			Assert.AreEqual(ThreatDirectionCompass.NorthEast, knowledge.Compass);
		}

		[Test]
		public void A3_Stale_StateValid()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
			Assert.IsTrue(controller.ApplyHostileLost(2f));
			Assert.AreEqual(ThreatDirectionState.Stale, controller.CurrentState);
			Assert.AreEqual(ThreatDirectionCompass.NorthEast, controller.GetThreatCompass());
		}

		[Test]
		public void A4_None_StateValid()
		{
			var controller = new ThreatDirectionController();
			Assert.AreEqual(ThreatDirectionState.None, controller.CurrentState);
			Assert.IsFalse(controller.HasThreatDirection);
			Assert.IsFalse(controller.TryGetThreatDirection(out _));
		}

		[Test]
		public void A5_SourceRank_VisualThenSoundThenReportThenInitial()
		{
			Assert.Greater(
				ThreatDirectionMath.SourceRank(ThreatDirectionState.Known, ThreatDirectionSource.Visual),
				ThreatDirectionMath.SourceRank(ThreatDirectionState.Known, ThreatDirectionSource.Sound));
			Assert.Greater(
				ThreatDirectionMath.SourceRank(ThreatDirectionState.Known, ThreatDirectionSource.Sound),
				ThreatDirectionMath.SourceRank(ThreatDirectionState.Known, ThreatDirectionSource.AllyReport));
			Assert.Greater(
				ThreatDirectionMath.SourceRank(ThreatDirectionState.Known, ThreatDirectionSource.AllyReport),
				ThreatDirectionMath.SourceRank(ThreatDirectionState.Expected, ThreatDirectionSource.InitialEstimate));
		}

		[Test]
		public void A6_Compass_NorthEastSouthWest()
		{
			Assert.AreEqual(ThreatDirectionCompass.North, ThreatDirectionEstimator.CompassFrom(Vector3.forward));
			Assert.AreEqual(ThreatDirectionCompass.East, ThreatDirectionEstimator.CompassFrom(Vector3.right));
			Assert.AreEqual(ThreatDirectionCompass.South, ThreatDirectionEstimator.CompassFrom(Vector3.back));
			Assert.AreEqual(ThreatDirectionCompass.West, ThreatDirectionEstimator.CompassFrom(Vector3.left));
		}

		[Test]
		public void A7_Compass_NorthEast()
		{
			Assert.AreEqual(
				ThreatDirectionCompass.NorthEast,
				ThreatDirectionEstimator.CompassFrom(new Vector3(1f, 0f, 1f)));
		}

		[Test]
		public void A8_Api_HasGetSectorConfidenceUncertainty()
		{
			ThreatDirectionController controller = StartedNorth();
			Assert.IsTrue(controller.HasThreatDirection);
			Assert.Greater(controller.GetThreatDirection().z, 0.9f);
			Assert.AreEqual(ThreatDirectionMath.ExpectedConfidence, controller.GetThreatConfidence(), 0.001f);
			Assert.AreEqual(
				ThreatDirectionMath.ExpectedUncertaintyDegrees,
				controller.GetThreatUncertainty(),
				0.001f);
			Assert.AreEqual(
				ThreatDirectionMath.ExpectedUncertaintyDegrees,
				controller.GetThreatSector().HalfAngleDegrees,
				0.001f);
		}
		#endregion

		#region B Spawn estimate
		[Test]
		public void B1_PlayerSpawnCenter_ToEnemyCenter_North()
		{
			Assert.IsTrue(ThreatDirectionEstimator.TryExpectedDirection(
				s_Origin,
				s_NorthPoint,
				out Vector3 direction));
			Assert.AreEqual(ThreatDirectionCompass.North, ThreatDirectionEstimator.CompassFrom(direction));
		}

		[Test]
		public void B2_Symmetry_OppositeDirections()
		{
			ThreatDirectionEstimator.TryExpectedDirection(s_Origin, s_NorthPoint, out Vector3 player);
			ThreatDirectionEstimator.TryExpectedDirection(s_NorthPoint, s_Origin, out Vector3 enemy);
			Assert.AreEqual(ThreatDirectionCompass.North, ThreatDirectionEstimator.CompassFrom(player));
			Assert.AreEqual(ThreatDirectionCompass.South, ThreatDirectionEstimator.CompassFrom(enemy));
			Assert.Less(Vector3.Dot(player, enemy), -0.99f);
		}

		[Test]
		public void B3_ShiftedEnemyGroup_CorrectDirection()
		{
			var player = new[] { s_Origin };
			var enemy = new[] { new Vector3(10f, 0f, 10f), new Vector3(12f, 0f, 8f) };
			Assert.IsTrue(ThreatDirectionEstimator.TryAverage(player, out Vector3 own));
			Assert.IsTrue(ThreatDirectionEstimator.TryAverage(enemy, out Vector3 other));
			Assert.IsTrue(ThreatDirectionEstimator.TryExpectedDirection(own, other, out Vector3 direction));
			Assert.AreEqual(ThreatDirectionCompass.NorthEast, ThreatDirectionEstimator.CompassFrom(direction));
		}

		[Test]
		public void B4_DifferentGroupWidths_StableCenter()
		{
			var narrow = new[] { new Vector3(-2f, 0f, 10f), new Vector3(2f, 0f, 10f) };
			var wide = new[] { new Vector3(-50f, 0f, 10f), new Vector3(50f, 0f, 10f) };
			ThreatDirectionEstimator.TryAverage(narrow, out Vector3 narrowCenter);
			ThreatDirectionEstimator.TryAverage(wide, out Vector3 wideCenter);
			Assert.AreEqual(ThreatDirectionCompass.North, CompassFromOwnTo(s_Origin, narrowCenter));
			Assert.AreEqual(ThreatDirectionCompass.North, CompassFromOwnTo(s_Origin, wideCenter));
		}

		[Test]
		public void B5_SingleSpawn_ValidDirection()
		{
			var one = new[] { s_NorthPoint };
			Assert.IsTrue(ThreatDirectionEstimator.TryAverage(one, out Vector3 center));
			Assert.IsTrue(ThreatDirectionEstimator.TryExpectedDirection(s_Origin, center, out Vector3 direction));
			Assert.Greater(direction.sqrMagnitude, 0.5f);
		}

		[Test]
		public void B6_NoThreatDirectionSceneObjects_VectorCentersWork()
		{
			var controller = new ThreatDirectionController();
			Assert.IsTrue(controller.ApplyBattleStart(s_Origin, s_NorthPoint, 0f));
			Assert.AreEqual(ThreatDirectionState.Expected, controller.CurrentState);
		}

		[Test]
		public void B7_SameCenters_StayNone()
		{
			var controller = new ThreatDirectionController();
			Assert.IsFalse(controller.ApplyBattleStart(s_Origin, s_Origin, 0f));
			Assert.AreEqual(ThreatDirectionState.None, controller.CurrentState);
		}

		[Test]
		public void B8_NeutralTeam_NoCenters()
		{
			Assert.IsFalse(
				ThreatDirectionSpawnQuery.TryGetCenters(UnitTeamId.Neutral, out _, out _));
		}

		[Test]
		public void B9_ExistingSpawnMarkers_NoExtraObjects()
		{
			ThreatDirectionSpawnQuery.Invalidate();
			CombatTestSpawnMarker[] existing =
				Object.FindObjectsByType<CombatTestSpawnMarker>(FindObjectsInactive.Exclude);
			if (existing != null && existing.Length > 0)
			{
				Assert.IsTrue(ThreatDirectionSpawnQuery.TryGetPlayerAndEnemyCenters(
					out Vector3 playerCenter,
					out Vector3 enemyCenter));
				Assert.AreEqual(
					ThreatDirectionCompass.South,
					CompassFromOwnTo(enemyCenter, playerCenter));
				Assert.Less(
					Vector3.Dot(
						Direction(playerCenter, enemyCenter),
						Direction(enemyCenter, playerCenter)),
					-0.99f);
				return;
			}

			GameObject player = Marker(CombatTestSpawnMarker.MarkerSide.Player, s_Origin);
			GameObject enemy = Marker(CombatTestSpawnMarker.MarkerSide.Enemy, s_NorthPoint);
			try
			{
				ThreatDirectionSpawnQuery.Invalidate();
				Assert.IsTrue(ThreatDirectionSpawnQuery.TryGetCenters(
					UnitTeamId.Player,
					out Vector3 own,
					out Vector3 other));
				Assert.AreEqual(ThreatDirectionCompass.North, CompassFromOwnTo(own, other));
			}
			finally
			{
				Object.DestroyImmediate(player);
				Object.DestroyImmediate(enemy);
				ThreatDirectionSpawnQuery.Invalidate();
			}
		}
		#endregion

		#region C Confidence / uncertainty
		[Test]
		public void C1_Expected_ConfidenceAndUncertaintyPositive()
		{
			ThreatDirectionController controller = StartedNorth();
			Assert.Greater(controller.GetThreatConfidence(), 0f);
			Assert.Greater(controller.GetThreatUncertainty(), 0f);
		}

		[Test]
		public void C2_Visual_ConfidenceHigherThanExpected()
		{
			ThreatDirectionController controller = StartedNorth();
			float expected = controller.GetThreatConfidence();
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
			Assert.Greater(controller.GetThreatConfidence(), expected);
		}

		[Test]
		public void C3_Visual_UncertaintyLowerThanExpected()
		{
			ThreatDirectionController controller = StartedNorth();
			float expected = controller.GetThreatUncertainty();
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
			Assert.Less(controller.GetThreatUncertainty(), expected);
		}

		[Test]
		public void C4_Stale_ConfidenceLowerThanKnown()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
			float known = controller.GetThreatConfidence();
			controller.ApplyHostileLost(2f);
			controller.Tick(4f);
			Assert.Less(controller.GetThreatConfidence(), known);
		}

		[Test]
		public void C5_Stale_UncertaintyHigherThanKnown()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
			float known = controller.GetThreatUncertainty();
			controller.ApplyHostileLost(2f);
			controller.Tick(4f);
			Assert.Greater(controller.GetThreatUncertainty(), known);
		}
		#endregion

		#region D Visual override
		[Test]
		public void D1_ExpectedNorth_HostileVisibleNorthEast_KnownNorthEast()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
			Assert.AreEqual(ThreatDirectionState.Known, controller.CurrentState);
			Assert.AreEqual(ThreatDirectionCompass.NorthEast, controller.GetThreatCompass());
		}

		[Test]
		public void D2_HostileLost_StaleKeepsDirection()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
			controller.ApplyHostileLost(2f);
			Assert.AreEqual(ThreatDirectionState.Stale, controller.CurrentState);
			Assert.AreEqual(ThreatDirectionCompass.NorthEast, controller.GetThreatCompass());
		}

		[Test]
		public void D3_EnemyMoveWithoutEvent_DoesNotChangeDirection()
		{
			ThreatDirectionController controller = StartedNorth();
			Vector3 before = controller.GetThreatDirection();
			controller.Tick(1f, s_Origin, AIPerceptionFrame.Empty);
			controller.Tick(2f, s_Origin, VisualFrame(s_EastPoint, false));
			Assert.AreEqual(ThreatDirectionCompass.North, controller.GetThreatCompass());
			Assert.AreEqual(before, controller.GetThreatDirection());
		}

		[Test]
		public void D4_HeldVisual_DoesNotPollNewLastKnown()
		{
			var controller = new ThreatDirectionController();
			controller.Tick(1f, s_Origin, VisualFrame(s_NorthEastPoint, true));
			Assert.AreEqual(ThreatDirectionCompass.NorthEast, controller.GetThreatCompass());
			controller.Tick(2f, s_Origin, VisualFrame(s_EastPoint, true));
			Assert.AreEqual(ThreatDirectionCompass.NorthEast, controller.GetThreatCompass());
		}

		[Test]
		public void D5_Visual_OverridesExpected()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyHostileVisible(s_Origin, s_EastPoint, 1f);
			Assert.AreEqual(ThreatDirectionSource.Visual, controller.CurrentSource);
			Assert.AreEqual(ThreatDirectionCompass.East, controller.GetThreatCompass());
		}

		[Test]
		public void D6_VisualFromNone_BecomesKnown()
		{
			var controller = new ThreatDirectionController();
			Assert.IsTrue(controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f));
			Assert.AreEqual(ThreatDirectionState.Known, controller.CurrentState);
		}
		#endregion

		#region E Sound / report fallback
		[Test]
		public void E1_SoundFallback_EastWhenNoVisual()
		{
			ThreatDirectionController controller = StartedNorth();
			Assert.IsTrue(controller.ApplyGunshot(s_Origin, s_EastPoint, 1f));
			Assert.AreEqual(ThreatDirectionSource.Sound, controller.CurrentSource);
			Assert.AreEqual(ThreatDirectionCompass.East, controller.GetThreatCompass());
		}

		[Test]
		public void E2_VisualPriority_OverSound()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyGunshot(s_Origin, s_EastPoint, 1f);
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 2f);
			Assert.AreEqual(ThreatDirectionSource.Visual, controller.CurrentSource);
			Assert.AreEqual(ThreatDirectionCompass.NorthEast, controller.GetThreatCompass());
		}

		[Test]
		public void E3_ReportFallback_WhenNoStrongerSource()
		{
			ThreatDirectionController controller = StartedNorth();
			Assert.IsTrue(controller.ApplyAllyReport(s_Origin, s_EastPoint, 1f));
			Assert.AreEqual(ThreatDirectionSource.AllyReport, controller.CurrentSource);
			Assert.AreEqual(ThreatDirectionCompass.East, controller.GetThreatCompass());
		}

		[Test]
		public void E4_Visual_OverReport()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyAllyReport(s_Origin, s_EastPoint, 1f);
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 2f);
			Assert.AreEqual(ThreatDirectionCompass.NorthEast, controller.GetThreatCompass());
			Assert.AreEqual(ThreatDirectionSource.Visual, controller.CurrentSource);
		}

		[Test]
		public void E5_Sound_OverReport()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyAllyReport(s_Origin, s_NorthPoint, 1f);
			Assert.IsTrue(controller.ApplyGunshot(s_Origin, s_EastPoint, 2f));
			Assert.AreEqual(ThreatDirectionSource.Sound, controller.CurrentSource);
			Assert.AreEqual(ThreatDirectionCompass.East, controller.GetThreatCompass());
		}

		[Test]
		public void E6_SoundIgnored_WhileVisualKnown()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
			Assert.IsFalse(controller.ApplyGunshot(s_Origin, s_EastPoint, 2f));
			Assert.AreEqual(ThreatDirectionCompass.NorthEast, controller.GetThreatCompass());
		}

		[Test]
		public void E7_SoundIgnored_WhileVisualStale()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
			controller.ApplyHostileLost(2f);
			Assert.IsFalse(controller.ApplyGunshot(s_Origin, s_EastPoint, 3f));
			Assert.AreEqual(ThreatDirectionSource.Visual, controller.CurrentSource);
			Assert.AreEqual(ThreatDirectionCompass.NorthEast, controller.GetThreatCompass());
		}

		[Test]
		public void E8_ReportIgnored_WhileSoundKnown()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyGunshot(s_Origin, s_EastPoint, 1f);
			Assert.IsFalse(controller.ApplyAllyReport(s_Origin, s_NorthPoint, 2f));
			Assert.AreEqual(ThreatDirectionCompass.East, controller.GetThreatCompass());
		}
		#endregion

		#region F Expiry / decay
		[Test]
		public void F1_Known_ToStale_ToExpectedFallback()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
			controller.ApplyHostileLost(2f);
			controller.Tick(2f + ThreatDirectionMath.VisualStaleToFallbackSeconds + 0.1f);
			Assert.AreEqual(ThreatDirectionState.Expected, controller.CurrentState);
			Assert.AreEqual(ThreatDirectionCompass.North, controller.GetThreatCompass());
			Assert.AreEqual(ThreatDirectionSource.InitialEstimate, controller.CurrentSource);
		}

		[Test]
		public void F2_Stale_ToNone_WithoutEstimate()
		{
			var controller = new ThreatDirectionController();
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
			controller.ApplyHostileLost(2f);
			controller.Tick(2f + ThreatDirectionMath.VisualStaleToFallbackSeconds + 0.1f);
			Assert.AreEqual(ThreatDirectionState.None, controller.CurrentState);
			Assert.IsFalse(controller.HasThreatDirection);
		}

		[Test]
		public void F3_Expected_DoesNotExpire()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.Tick(120f);
			Assert.AreEqual(ThreatDirectionState.Expected, controller.CurrentState);
			Assert.AreEqual(ThreatDirectionCompass.North, controller.GetThreatCompass());
		}

		[Test]
		public void F4_TickWithoutEvent_KeepsDirection()
		{
			ThreatDirectionController controller = StartedNorth();
			Vector3 before = controller.GetThreatDirection();
			controller.Tick(3f);
			controller.Tick(6f);
			Assert.AreEqual(before, controller.GetThreatDirection());
		}

		[Test]
		public void F5_Sound_AgesToStaleThenExpected()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyGunshot(s_Origin, s_EastPoint, 1f);
			controller.Tick(1f + ThreatDirectionMath.SoundKnownToStaleSeconds + 0.05f);
			Assert.AreEqual(ThreatDirectionState.Stale, controller.CurrentState);
			controller.Tick(
				1f + ThreatDirectionMath.SoundKnownToStaleSeconds +
				ThreatDirectionMath.SoundStaleToFallbackSeconds + 0.1f);
			Assert.AreEqual(ThreatDirectionState.Expected, controller.CurrentState);
			Assert.AreEqual(ThreatDirectionCompass.North, controller.GetThreatCompass());
		}

		[Test]
		public void F6_AgeIncreases_WithoutDirectionChange()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.TryGetThreatDirection(out ThreatDirectionKnowledge first);
			controller.Tick(4f);
			controller.TryGetThreatDirection(out ThreatDirectionKnowledge later);
			Assert.Greater(later.Age, first.Age);
			Assert.AreEqual(first.Compass, later.Compass);
		}
		#endregion

		#region G Logs / independence
		[Test]
		public void G1_Log_OnExpected()
		{
			ThreatDirectionController controller = StartedNorth();
			Assert.IsTrue(controller.LastLogPayload.IndexOf("source=Initial", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(controller.LastLogPayload.IndexOf("state=Expected", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(controller.LastLogPayload.IndexOf("dir=N confidence=", StringComparison.Ordinal) >= 0);
		}

		[Test]
		public void G2_Log_OnVisualKnown()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
			Assert.IsTrue(controller.LastLogPayload.IndexOf("source=Visual", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(controller.LastLogPayload.IndexOf("state=Known", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(controller.LastLogPayload.IndexOf("dir=NE", StringComparison.Ordinal) >= 0);
		}

		[Test]
		public void G3_Log_OnStale()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
			controller.ApplyHostileLost(2f);
			Assert.IsTrue(controller.LastLogPayload.IndexOf("state=Stale", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(controller.LastLogPayload.IndexOf("dir=NE", StringComparison.Ordinal) >= 0);
		}

		[Test]
		public void G4_NoLog_EveryTick()
		{
			ThreatDirectionController controller = StartedNorth();
			int afterStart = controller.LogCount;
			controller.Tick(1f);
			controller.Tick(2f);
			controller.Tick(3f);
			Assert.AreEqual(afterStart, controller.LogCount);
		}

		[Test]
		public void G5_DoesNotChangeReadiness()
		{
			var readiness = new ReadinessController();
			readiness.Reset(ReadinessRankKind.Soldier, 0f);
			ReadinessState before = readiness.CurrentState;
			ThreatDirectionController threat = StartedNorth();
			threat.ApplyHostileVisible(s_Origin, s_NorthEastPoint, 1f);
			threat.ApplyGunshot(s_Origin, s_EastPoint, 2f);
			Assert.AreEqual(before, readiness.CurrentState);
			Assert.AreEqual(ReadinessState.Patrol, readiness.CurrentState);
		}

		[Test]
		public void G6_Channel_IsThreatDirection()
		{
			Assert.AreEqual("THREAT_DIRECTION", ThreatDirectionLog.Channel);
			Assert.AreEqual(UnitActionLog.ThreatDirection, ThreatDirectionLog.Channel);
		}

		[Test]
		public void G7_SoundLog_KnownEast()
		{
			ThreatDirectionController controller = StartedNorth();
			controller.ApplyGunshot(s_Origin, s_EastPoint, 1f);
			Assert.IsTrue(controller.LastLogPayload.IndexOf("source=Sound", StringComparison.Ordinal) >= 0);
			Assert.IsTrue(controller.LastLogPayload.IndexOf("dir=E", StringComparison.Ordinal) >= 0);
		}
		#endregion

		#region Private Methods
		private static ThreatDirectionController StartedNorth()
		{
			var controller = new ThreatDirectionController();
			controller.ApplyBattleStart(s_Origin, s_NorthPoint, 0f);
			return controller;
		}

		private static ThreatDirectionCompass CompassFromOwnTo(Vector3 _own, Vector3 _other)
		{
			return ThreatDirectionEstimator.CompassFrom(Direction(_own, _other));
		}

		private static Vector3 Direction(Vector3 _own, Vector3 _other)
		{
			ThreatDirectionEstimator.TryExpectedDirection(_own, _other, out Vector3 direction);
			return direction;
		}

		private static GameObject Marker(CombatTestSpawnMarker.MarkerSide _side, Vector3 _position)
		{
			var go = new GameObject("ThreatDirectionSpawnPin");
			go.transform.position = _position;
			CombatTestSpawnMarker marker = go.AddComponent<CombatTestSpawnMarker>();
			marker.Side = _side;
			return go;
		}

		private static AIPerceptionFrame VisualFrame(Vector3 _lastKnown, bool _visibleNow)
		{
			AIContactKnowledge contact = new AIContactKnowledge(
				null,
				_visibleNow ? DetectionState.Detected : DetectionState.Undetected,
				_visibleNow ? ObservationState.Observed : ObservationState.Lost,
				PerceivedIdentity.Hostile,
				1f,
				PerceivedRelationship.Hostile,
				ThreatLevel.High,
				_lastKnown,
				_lastKnown,
				0f,
				_visibleNow ? 1f : 0.4f,
				_visibleNow,
				!_visibleNow,
				!_visibleNow,
				true,
				false,
				false,
				true,
				false,
				false,
				true,
				false,
				false,
				false,
				true);
			return new AIPerceptionFrame(
				new[] { contact },
				_visibleNow ? new[] { contact } : Array.Empty<AIContactKnowledge>(),
				_visibleNow ? Array.Empty<AIContactKnowledge>() : new[] { contact },
				Array.Empty<AIContactKnowledge>(),
				new[] { contact },
				Array.Empty<AIContactKnowledge>(),
				ThreatLevel.High);
		}
		#endregion
	}
}
