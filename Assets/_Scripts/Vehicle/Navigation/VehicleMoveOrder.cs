using System;
using UnityEngine;

namespace VehicleNavigation
{
	public enum OrderState
	{
		Pending,
		Executing,
		Completed,
		Aborted,
		Expired,
		Interrupting
	}

	public sealed class VehicleMoveOrder
	{
		private static long s_NextOrderId = 1;

		public long OrderId { get; }
		public long? ParentOrderId { get; }
		public VehicleOrderType Type { get; }
		public OrderState State { get; private set; }

		public Vector3 Destination { get; }
		public bool HasDestination { get; }

		public float DesiredHeadingYaw { get; }
		public bool HasDesiredHeading { get; }

		public ArrivalFacingMode FacingMode { get; }
		public VehicleSpeedMode SpeedMode { get; set; }

		public bool AllowReverse { get; }
		public bool AllowTurnAround { get; }
		public bool AllowThreePointTurn { get; }

		public int Priority { get; }
		public bool IsCancelable { get; }

		public float TimeoutSeconds { get; }
		public float CreatedTime { get; private set; }

		public string SourceTag { get; }

		private VehicleMoveOrder(
			long _orderId,
			long? _parentOrderId,
			VehicleOrderType _type,
			Vector3 _destination,
			bool _hasDestination,
			float _desiredHeadingYaw,
			bool _hasDesiredHeading,
			ArrivalFacingMode _facingMode,
			VehicleSpeedMode _speedMode,
			bool _allowReverse,
			bool _allowTurnAround,
			bool _allowThreePointTurn,
			int _priority,
			bool _isCancelable,
			float _timeoutSeconds,
			string _sourceTag)
		{
			OrderId = _orderId;
			ParentOrderId = _parentOrderId;
			Type = _type;
			Destination = _destination;
			HasDestination = _hasDestination;
			DesiredHeadingYaw = _desiredHeadingYaw;
			HasDesiredHeading = _hasDesiredHeading;
			FacingMode = _facingMode;
			SpeedMode = _speedMode;
			AllowReverse = _allowReverse;
			AllowTurnAround = _allowTurnAround;
			AllowThreePointTurn = _allowThreePointTurn;
			Priority = _priority;
			IsCancelable = _isCancelable;
			TimeoutSeconds = _timeoutSeconds;
			SourceTag = _sourceTag ?? string.Empty;
			State = OrderState.Pending;
		}

		internal void MarkStarted(float _timeNow)
		{
			CreatedTime = _timeNow;
			State = OrderState.Executing;
		}

		internal void MarkCompleted()
		{
			if (State == OrderState.Executing)
				State = OrderState.Completed;
		}

		internal void MarkAborted()
		{
			if (State == OrderState.Executing || State == OrderState.Pending)
				State = OrderState.Aborted;
		}

		internal void MarkExpired()
		{
			if (State == OrderState.Pending || State == OrderState.Executing)
				State = OrderState.Expired;
		}

		internal void MarkInterrupting()
		{
			if (State == OrderState.Pending)
				State = OrderState.Interrupting;
		}

		#region Factory Methods
		public static VehicleMoveOrder CreateMove(Vector3 _destination, VehicleSpeedMode _speedMode)
		{
			return new VehicleMoveOrder(
				NextId(), null,
				VehicleOrderType.Move,
				_destination, true,
				0f, false,
				ArrivalFacingMode.None,
				_speedMode,
				true, true, true,
				0, true,
				0f,
				"user");
		}

		public static VehicleMoveOrder CreateMove(Vector3 _destination, float _headingYaw, VehicleSpeedMode _speedMode)
		{
			return new VehicleMoveOrder(
				NextId(), null,
				VehicleOrderType.Move,
				_destination, true,
				_headingYaw, true,
				ArrivalFacingMode.FaceHeading,
				_speedMode,
				true, true, true,
				0, true,
				0f,
				"user-heading");
		}

		public static VehicleMoveOrder CreateStop()
		{
			return new VehicleMoveOrder(
				NextId(), null,
				VehicleOrderType.Stop,
				Vector3.zero, false,
				0f, false,
				ArrivalFacingMode.None,
				VehicleSpeedMode.Medium,
				false, false, false,
				1, false,
				0f,
				"system");
		}

		public static VehicleMoveOrder CreateHold()
		{
			return new VehicleMoveOrder(
				NextId(), null,
				VehicleOrderType.Hold,
				Vector3.zero, false,
				0f, false,
				ArrivalFacingMode.KeepCurrent,
				VehicleSpeedMode.Medium,
				false, false, false,
				0, true,
				0f,
				"system");
		}

		public static VehicleMoveOrder CreateEmergencyStop()
		{
			return new VehicleMoveOrder(
				NextId(), null,
				VehicleOrderType.EmergencyStop,
				Vector3.zero, false,
				0f, false,
				ArrivalFacingMode.None,
				VehicleSpeedMode.Medium,
				false, false, false,
				int.MaxValue, false,
				0f,
				"emergency");
		}

		public static VehicleMoveOrder FromMoveGoal(VehicleMoveGoal _goal)
		{
			return new VehicleMoveOrder(
				NextId(), null,
				VehicleOrderType.Move,
				_goal.Position, true,
				_goal.HeadingYawDegrees, _goal.HasHeading,
				_goal.HasHeading ? ArrivalFacingMode.FaceHeading : ArrivalFacingMode.None,
				_goal.SpeedMode,
				true, true, true,
				0, true,
				0f,
				"legacy-move-goal");
		}
		#endregion

		private static long NextId()
		{
			long id = s_NextOrderId;
			s_NextOrderId++;
			return id;
		}

		public override string ToString()
		{
			return $"[Order #{OrderId}] {Type} → {Destination} state={State} src={SourceTag}";
		}
	}
}
