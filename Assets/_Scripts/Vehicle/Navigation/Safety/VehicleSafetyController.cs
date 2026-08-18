using System.Collections.Generic;
using CombatVehicleSystem;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Last filter before physics. Ensures no dangerous command reaches VehicleBrain.
	/// Does NOT change plans, routing, or pursuit — only clamps unsafe commands.
	/// </summary>
	public sealed class VehicleSafetyController
	{
		public static bool DebugLog = false;
		private readonly List<ISafetyLimiter> m_Limiters = new List<ISafetyLimiter>();

		public VehicleSafetyController(VehicleParameters _params, WheeledMotor _motor)
		{
			m_Limiters.Add(new CommandSanitizer());
			m_Limiters.Add(new DynamicsLimiter(6f, 1.2f));
			m_Limiters.Add(new StabilityLimiter(_motor));
			m_Limiters.Add(new AirborneProtection());
			m_Limiters.Add(new RecoveryProtection());
		}

		public SafetyOutput Apply(
			FeedbackState _state,
			VehicleParameters _params,
			VehicleCommand _proposed,
			float _dt,
			Vector3 _eulerAngles,
			bool _isRecovering)
		{
			var input = new SafetyInput
			{
				State = _state,
				Params = _params,
				ProposedCommand = _proposed,
				DeltaTime = _dt,
				IsRecovering = _isRecovering,
				EulerAngles = _eulerAngles
			};

			SafetyOutput lastOutput = null;
			VehicleCommand current = _proposed;

			foreach (var limiter in m_Limiters)
			{
				input = new SafetyInput
				{
					State = _state,
					Params = _params,
					ProposedCommand = current,
					DeltaTime = _dt,
					IsRecovering = _isRecovering,
					EulerAngles = _eulerAngles
				};

				lastOutput = limiter.Apply(input);
				current = lastOutput.Command;

				if (lastOutput.Triggered && DebugLog && !string.IsNullOrEmpty(lastOutput.Warning))
					VehicleFileLog.WriteActive($"[Safety] {limiter.GetType().Name}: {lastOutput.Warning}");

				if (lastOutput.ShouldAbortRecovery)
					break;
			}

			return lastOutput ?? new SafetyOutput { Command = current };
		}
	}
}
