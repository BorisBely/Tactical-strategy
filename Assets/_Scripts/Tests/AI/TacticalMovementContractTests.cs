using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #14.0 Tactical Movement Contract. Destination ≠ Route. Not a second locomotion stack.
	/// </summary>
	public sealed class TacticalMovementContractTests
	{
		#region Nested
		private sealed class RecordingFire
		{
			public int CallCount;

			public void Fire()
			{
				CallCount++;
			}
		}
		#endregion

		#region A1 Destination intact
		[Test]
		public void A1_DirectRoute_DestinationRemainsIntact()
		{
			Vector3 origin = Vector3.zero;
			Vector3 destination = new Vector3(12f, 0f, 3f);
			var overlay = new TacticalMovementOverlay();
			TacticalMovementDecision decision = overlay.Update(
				TacticalRouteMath.Goal(origin, destination, TacticalMovementMode.Normal));
			Assert.IsTrue(decision.HasRoute);
			Assert.AreEqual(TacticalRouteKind.Direct, decision.Kind);
			Assert.AreEqual(destination, decision.Destination);
			Assert.AreEqual(destination, decision.CurrentHop);
			Assert.AreEqual(0, decision.IntermediateCount);
			Assert.IsTrue(TacticalRouteMath.DestinationUnchanged(in decision, destination));
		}

		[Test]
		public void A1_AttackCommand_DoesNotRewriteDestination()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			Vector3 destination = new Vector3(20f, 0f, 0f);
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Attack(AttackCtx(destination))));
				Assert.AreEqual(destination, controller.CurrentContext.Destination);
				Assert.IsTrue(controller.CurrentContext.HasDestination);
				Assert.AreEqual(destination, recorder.LastDestination);
				Assert.AreEqual(TacticalRouteKind.Direct, controller.LastTacticalMovement.Kind);
				Assert.AreEqual(destination, controller.LastTacticalMovement.Destination);
			}
			finally
			{
				Object.DestroyImmediate(controller.gameObject);
			}
		}
		#endregion

		#region A2 Intermediate points
		[Test]
		public void A2_Route_CanContainIntermediatePoints()
		{
			Vector3 origin = Vector3.zero;
			Vector3 mid = new Vector3(4f, 0f, 1f);
			Vector3 destination = new Vector3(12f, 0f, 0f);
			var overlay = new TacticalMovementOverlay();
			TacticalMovementDecision decision = overlay.Adopt(
				origin,
				destination,
				new[] { TacticalRouteWaypoint.At(mid) },
				TacticalMovementMode.Tactical);
			Assert.AreEqual(TacticalRouteKind.Waypoint, decision.Kind);
			Assert.AreEqual(destination, decision.Destination);
			Assert.AreEqual(mid, decision.CurrentHop);
			Assert.AreNotEqual(decision.CurrentHop, decision.Destination);
			Assert.AreEqual(1, decision.IntermediateCount);
			Assert.IsTrue(TacticalRouteMath.DestinationUnchanged(in decision, destination));
		}
		#endregion

		#region A3 Executor still executes
		[Test]
		public void A3_Executor_WalksCurrentHop()
		{
			Vector3 origin = Vector3.zero;
			Vector3 mid = new Vector3(5f, 0f, 0f);
			Vector3 destination = new Vector3(14f, 0f, 0f);
			var overlay = new TacticalMovementOverlay();
			overlay.Adopt(
				origin,
				destination,
				new[] { TacticalRouteWaypoint.At(mid) },
				TacticalMovementMode.Normal);
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			try
			{
				var nav = new TacticalNavigationExecutor();
				nav.Begin();
				nav.Tick(
					controller,
					true,
					overlay.Last.CurrentHop,
					TacticalNavigationMath.DefaultPointArrivalRadius,
					UnitNavigationReason.Attack);
				Assert.AreEqual(1, recorder.MoveCount);
				Assert.AreEqual(mid, recorder.LastDestination);
				Assert.AreEqual(destination, overlay.Last.Destination);
				Assert.IsTrue(nav.Issued);
			}
			finally
			{
				Object.DestroyImmediate(controller.gameObject);
			}
		}
		#endregion

		#region A4 Overlay does not move
		[Test]
		public void A4_Overlay_DoesNotMoveOrFire()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder) = CreateWithRecorder();
			var fire = new RecordingFire();
			try
			{
				controller.TacticalMovement.Update(
					TacticalRouteMath.Goal(Vector3.zero, new Vector3(8f, 0f, 0f), TacticalMovementMode.Normal));
				Assert.AreEqual(0, recorder.MoveCount);
				Assert.IsFalse(recorder.HasMoveIntent);
				Assert.IsFalse(controller.TacticalNavigationIssued);
				Assert.AreEqual(0, fire.CallCount);
			}
			finally
			{
				Object.DestroyImmediate(controller.gameObject);
			}
		}

		[Test]
		public void A4_NoSecondLocomotionStack()
		{
			Assert.IsNull(typeof(TacticalNavigationExecutor).Assembly.GetType("TacticalLocomotionDriver"));
			Assert.IsNull(typeof(TacticalNavigationExecutor).Assembly.GetType("TacticalMovementDriver"));
			Assert.IsNotNull(typeof(TacticalNavigationExecutor));
			Assert.IsNotNull(typeof(UnitNavLocomotionDriver));
			Assert.IsFalse(TacticalRouteContext.Single(TacticalMovementMode.Normal).Formation.Present);
		}
		#endregion

		#region Helpers
		private static (UnitAIController controller, UnitMoveCommandRecorder recorder) CreateWithRecorder()
		{
			var go = new GameObject("AI140_Contract");
			UnitAIController controller = go.AddComponent<UnitAIController>();
			UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
			controller.EnsureStarted();
			return (controller, recorder);
		}

		private static UnitAIStateContext AttackCtx(Vector3 _destination)
		{
			return UnitAIStateContext.ForAttack(_destination, Vector3.forward);
		}
		#endregion
	}
}
