using CombatVehicleSystem;
using UnityEngine;

namespace VehicleNavigation
{
	public enum CalibrationPhase
	{
		Settle,
		Sample,
		Stop,
		Done
	}

	public readonly struct CalibrationSampleResult
	{
		public readonly bool Success;
		public readonly float MeasuredRadiusM;
		public readonly float PathLengthM;
		public readonly float YawDeltaDeg;
		public readonly float SteerRiseSec;
		public readonly string Note;

		public CalibrationSampleResult(
			bool _success,
			float _measuredRadiusM,
			float _pathLengthM,
			float _yawDeltaDeg,
			float _steerRiseSec,
			string _note)
		{
			Success = _success;
			MeasuredRadiusM = _measuredRadiusM;
			PathLengthM = _pathLengthM;
			YawDeltaDeg = _yawDeltaDeg;
			SteerRiseSec = _steerRiseSec;
			Note = _note;
		}
	}

	/// <summary>
	/// Open-loop calibration session: constant creep + steer, measures R from path/yaw.
	/// </summary>
	public sealed class VehicleKinematicsCalibrationSession
	{
		public CalibrationPhase Phase { get; private set; } = CalibrationPhase.Settle;

		readonly bool m_Reverse;
		readonly bool m_SteerRiseTest;
		readonly float m_CreepSpeedKmh;
		readonly float m_MaxSteer;
		readonly float m_SettleSec;
		readonly float m_SampleSec;
		readonly float m_SteerRampSec;
		readonly float m_MinSampleArcM;
		readonly float m_MinSampleYawDeg;

		float m_PhaseTimer;
		Vector3 m_LastPos;
		float m_LastYaw;
		float m_PathLength;
		float m_YawDeltaDeg;
		float m_StablePathLength;
		float m_StableYawDeltaDeg;
		float m_SteerCmd;
		float m_SteerRiseStart = -1f;
		float m_SteerRiseDone = -1f;
		float m_SteerTarget;
		bool m_HasLast;

		public VehicleKinematicsCalibrationSession(
			bool _reverse,
			bool _steerRiseTest,
			float _creepSpeedKmh = 4f,
			float _maxSteer = 1f,
			float _settleSec = 1.5f,
			float _sampleSec = 5f,
			float _steerRampSec = 1f)
		{
			m_Reverse = _reverse;
			m_SteerRiseTest = _steerRiseTest;
			m_CreepSpeedKmh = _creepSpeedKmh;
			m_MaxSteer = Mathf.Clamp(_maxSteer, 0.1f, 1f);
			m_SettleSec = _settleSec;
			m_SampleSec = _sampleSec;
			m_SteerRampSec = _steerRampSec;
			m_MinSampleArcM = 2f;
			m_MinSampleYawDeg = 25f;
			m_SteerTarget = _steerRiseTest ? 0f : m_MaxSteer;
		}

		public VehicleCommand Tick(
			Vector3 _position,
			float _yawDeg,
			float _speedKmh,
			float _actualSteerNorm,
			float _dt)
		{
			if (Phase == CalibrationPhase.Done)
				return VehicleCommand.SoftPark;

			if (!m_HasLast)
			{
				m_LastPos = _position;
				m_LastYaw = _yawDeg;
				m_HasLast = true;
			}

			float stepDist = Vector3.Distance(_position, m_LastPos);
			float stepYaw = Mathf.Abs(Mathf.DeltaAngle(m_LastYaw, _yawDeg));
			m_PathLength += stepDist;
			m_YawDeltaDeg += stepYaw;
			m_LastPos = _position;
			m_LastYaw = _yawDeg;

			m_PhaseTimer += _dt;

			switch (Phase)
			{
				case CalibrationPhase.Settle:
					if (m_PhaseTimer >= m_SettleSec && Mathf.Abs(_speedKmh) < 1f)
					{
						ResetSampleAccumulators();
						m_PhaseTimer = 0f;
						Phase = CalibrationPhase.Sample;
						if (m_SteerRiseTest)
						{
							m_SteerCmd = 0f;
							m_SteerRiseStart = Time.realtimeSinceStartup;
						}
					}

					return BrakeCommand(0f);

				case CalibrationPhase.Sample:
					if (m_SteerRiseTest)
					{
						float t = m_PhaseTimer / Mathf.Max(0.05f, m_SteerRampSec);
						m_SteerCmd = Mathf.Lerp(0f, m_MaxSteer, Mathf.Clamp01(t));
						if (m_SteerRiseDone < 0f &&
						    Mathf.Abs(_actualSteerNorm) >= m_MaxSteer * 0.9f)
							m_SteerRiseDone = Time.realtimeSinceStartup - m_SteerRiseStart;

						if (m_PhaseTimer >= m_SampleSec + m_SteerRampSec)
						{
							Phase = CalibrationPhase.Stop;
							m_PhaseTimer = 0f;
						}

						return DriveCommand(m_SteerCmd);
					}

					m_StablePathLength += stepDist;
					m_StableYawDeltaDeg += stepYaw;

					if (m_PhaseTimer >= 1.5f &&
					    (m_PhaseTimer >= m_SampleSec ||
					     (m_StablePathLength >= m_MinSampleArcM &&
					      m_StableYawDeltaDeg >= m_MinSampleYawDeg)))
					{
						Phase = CalibrationPhase.Stop;
						m_PhaseTimer = 0f;
					}

					return DriveCommand(m_MaxSteer);

				case CalibrationPhase.Stop:
					if (m_PhaseTimer >= 1f || Mathf.Abs(_speedKmh) < 0.5f)
						Phase = CalibrationPhase.Done;
					return BrakeCommand(0f);

				default:
					return VehicleCommand.SoftPark;
			}
		}

		public CalibrationSampleResult BuildResult(VehicleKinematicsProfile _profile)
		{
			float yawRad = m_StableYawDeltaDeg * Mathf.Deg2Rad;
			float measuredR = yawRad > 0.01f ? m_StablePathLength / yawRad : 0f;
			float riseSec = m_SteerRiseDone >= 0f ? m_SteerRiseDone : 0f;

			bool ok = Phase == CalibrationPhase.Done;
			string note = "";
			if (m_SteerRiseTest)
			{
				ok = ok && riseSec > 0f;
				note = ok
					? $"steerRise={riseSec:F2}s maxSteer={m_MaxSteer:F2}"
					: "steer rise not reached";
			}
			else
			{
				ok = ok && measuredR > 0.5f;
				float trackable = _profile.EffectiveTurnRadius * 1.15f;
				note = ok
					? $"measuredR={measuredR:F2}m eff={_profile.EffectiveTurnRadius:F2}m trackable={trackable:F2}m"
					: "insufficient arc sample";
			}

			return new CalibrationSampleResult(ok, measuredR, m_StablePathLength, m_StableYawDeltaDeg, riseSec, note);
		}

		public float InstantRadius =>
			m_YawDeltaDeg > 0.5f ? m_PathLength / (m_YawDeltaDeg * Mathf.Deg2Rad) : 0f;

		void ResetSampleAccumulators()
		{
			m_PathLength = 0f;
			m_YawDeltaDeg = 0f;
			m_StablePathLength = 0f;
			m_StableYawDeltaDeg = 0f;
		}

		VehicleCommand DriveCommand(float _steer)
		{
			float throttle = m_Reverse ? -CreepThrottle() : CreepThrottle();
			return new VehicleCommand
			{
				Steer = _steer,
				Throttle = throttle,
				BrakeMode = VehicleBrakeMode.None,
				Phase = DrivingPhase.Cruise
			};
		}

		VehicleCommand BrakeCommand(float _steer)
		{
			return new VehicleCommand
			{
				Steer = _steer,
				Throttle = 0f,
				BrakeMode = VehicleBrakeMode.Soft,
				Phase = DrivingPhase.Parking
			};
		}

		float CreepThrottle()
		{
			// Normalized throttle for ~4 km/h creep; tuned for test platform vehicles.
			return Mathf.Clamp(m_CreepSpeedKmh / 12f, 0.15f, 0.45f);
		}
	}
}
