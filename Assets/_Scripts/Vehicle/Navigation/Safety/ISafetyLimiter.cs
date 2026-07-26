using CombatVehicleSystem;
using UnityEngine;

namespace VehicleNavigation
{
	public struct SafetyInput
	{
		public FeedbackState State;
		public VehicleParameters Params;
		public VehicleCommand ProposedCommand;
		public float DeltaTime;
		public bool IsRecovering;
		public bool IsReversing;
		public Vector3 EulerAngles;
	}

	public sealed class SafetyOutput
	{
		public VehicleCommand Command;
		public bool Triggered;
		public string Warning;
		public bool ShouldAbortRecovery;
	}

	public interface ISafetyLimiter
	{
		SafetyOutput Apply(SafetyInput input);
	}
}
