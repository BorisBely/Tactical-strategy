using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CombatVehicleSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VehicleNavigation
{
	public enum TestManeuverType
	{
		Forward,
		Reverse,
		SideApproach,
		UTurn,
		KinematicsCalibration,
		All
	}

	public enum VehicleTestSuite
	{
		OpenFieldBasic,
		PoseArrival,
		Reverse,
		RouteCorners,
		TightSpace
	}

	public enum VehicleTestVariant
	{
		Variant1_OpenField = 1,
		Variant2_PoseArrival = 2,
		Variant5_KinematicsCalibration = 5
	}

	[Serializable]
	public struct TestCase
	{
		public string Name;
		public TestManeuverType ManeuverType;
		public VehicleTestSuite Suite;
		public float DirectionDeg;
		public float Distance;
		public bool HasHeading;
		public float HeadingYaw;
		public string ExpectedManeuverFamily;

		public Vector3 GetTargetPosition(Vector3 _origin)
		{
			float rad = DirectionDeg * Mathf.Deg2Rad;
			return _origin + new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * Distance;
		}

		public float ResolveTargetYaw(float _startYaw)
		{
			if (!HasHeading)
				return _startYaw;
			return HeadingYaw;
		}

		public GoalHeadingSource ResolveHeadingSource() =>
			HasHeading ? GoalHeadingSource.RequiredExplicit : GoalHeadingSource.None;
	}

	public sealed class VehicleTestPlatform : MonoBehaviour
	{
		[Header("Vehicle")]
		[SerializeField] private GameObject m_VehiclePrefab;
		[SerializeField] private Transform m_SpawnPoint;

		[Header("Test Settings")]
		[SerializeField] private VehicleTestVariant m_TestVariant = VehicleTestVariant.Variant1_OpenField;
		[SerializeField] private int m_PlatformId = 1;
		[SerializeField] private int m_ShardIndex;
		[SerializeField] private int m_ShardCount = 1;
		[SerializeField] private float m_TestTimeout = 60f;
		[SerializeField] private float m_InterTestDelay = 2f;
		[SerializeField] private bool m_RespawnBetweenTests = true;
		[SerializeField] private Vector3 m_StartPosition = Vector3.zero;
		[SerializeField] private float m_StartYaw;
		[SerializeField] private int m_LogEveryNFrames = 15;
		[SerializeField] private float m_PositionTolerance = 0.45f;
		[SerializeField] private float m_LongitudinalTolerance = 0.1f;
		[SerializeField] private float m_LateralTolerance = 0.45f;
		[SerializeField] private float m_HeadingTolerance = 5f;
		[SerializeField] private float m_MaxArrivalSpeedKmh = 1f;

		[Header("Camera")]
		[SerializeField] private Camera m_TestCamera;
		[SerializeField] private Vector3 m_CameraOffset = new Vector3(0f, 40f, -30f);
		[SerializeField] private bool m_UseOverheadCamera = false;

		[Header("Categories")]
		[SerializeField] private bool m_RunForwardTests = true;
		[SerializeField] private bool m_RunReverseTests = true;
		[SerializeField] private bool m_RunSideTests = true;
		[SerializeField] private bool m_RunUTurnTests = true;
		[SerializeField] private bool m_ShuffleTests;

		[Header("State")]
		[SerializeField] private int m_CurrentTestIndex;
		[SerializeField] private bool m_AutoStart = true;
		[SerializeField] private bool m_LoopTests;

		private VehicleNavigation m_Navigation;
		private VehicleBrain m_Brain;
		private VehicleController m_VehicleController;
		private bool m_NavigationWasEnabled;
		private GameObject m_VehicleInstance;
		private readonly List<TestCase> m_TestCases = new List<TestCase>();
		private readonly List<TestResult> m_Results = new List<TestResult>();
		private StreamWriter m_LogWriter;
		private bool m_SummaryWritten;

		private float m_TestStartTime;
		private bool m_TestFailed;
		private string m_LastManeuverType;
		private int m_FrameCounter;
		private int m_TestFrameCounter;
		private float m_BestPoseError = float.MaxValue;
		private float m_StagnantTimer;
		private Vector3 m_LastProgressPos;
		private int m_SteerFlipCount;
		private float m_LastSteer;
		private float m_LastSpeedKmh;
		private float m_BrakingStartTime = -1f;
		private float m_PeakDecelMs2;
		private bool m_UsedHardBrake;
		private int m_LastLoggedPathRevision = -1;
		private TrajectoryGear? m_FirstActiveGear;
		private float m_MaxCrossTrack;
		private float m_PlannedLength;
		private float m_CoastBrakeDuration;
		private float m_SoftBrakeDuration;
		private float m_HardBrakeDuration;
		private float m_MaxPlanMs;
		private float m_MaxSliceMs;
		private float m_TotalPlanCpuMs;
		private int m_PlanSliceCount;
		private float m_PlanningWallSec;
		private float m_PlanningWallStart = -1f;
		private int m_MaxCollisionQueries;
		private long m_StartGcBytes;
		private readonly List<float> m_FrameMs = new List<float>(4096);
		private float m_LastRealtime;
		private int m_FramesOver16Ms;
		private int m_FramesOver33Ms;
		private int m_FramesOver50Ms;
		private int m_MaxReplanCount;
		private readonly StringBuilder m_FrameLogSb = new StringBuilder(512);
		private bool m_PerformanceRunActive;
		private bool m_DriverFsmDebugSaved;
		private bool m_PlannerDebugSaved;
		private int m_LogLinesSinceFlush;
		private const int c_LogFlushBatch = 32;
		private readonly List<CalibrationSampleResult> m_CalibrationResults = new List<CalibrationSampleResult>();
		private VehicleKinematicsCalibrationSession m_CalibrationSession;

		private enum Phase { Idle, Spawning, Driving, Arrived, Respawning, Completed }
		private Phase m_Phase;

		[Serializable]
		public struct TestResult
		{
			public string TestName;
			public TestManeuverType ManeuverType;
			public Vector3 StartPos;
			public Vector3 TargetPos;
			public float TargetYaw;
			public float Distance;
			public float DirectionDeg;
			public VehicleDrivingMode ChosenMode;
			public string PlanReason;
			public float CompletionTime;
			public float FinalDistance;
			public float FinalHeadingError;
			public float FinalSpeed;
			public float PostStopDrift;
			public float PeakDecelMs2;
			public float BrakeDurationSec;
			public float CoastBrakeDurationSec;
			public float SoftBrakeDurationSec;
			public float HardBrakeDurationSec;
			public bool UsedHardBrakeOnArrival;
			public float MaxPlanDurationMs;
			public float TotalPlanCpuMs;
			public float MaxSliceMs;
			public float PlanningWallSec;
			public int PlanSliceCount;
			public int MaxCollisionQueries;
			public long GcAllocBytes;
			public float MedianFrameMs;
			public float P95FrameMs;
			public float P99FrameMs;
			public float WorstFrameMs;
			public float EffectiveFps;
			public int FramesOver16Ms;
			public int FramesOver33Ms;
			public int FramesOver50Ms;
			public int MaxReplanCount;
			public bool Success;
			public string Note;
			public StagnationKind Stagnation;
			public string ExpectedManeuverFamily;
			public string ActualManeuverFamily;
			public bool ExpectedFamilyMatched;
			public TrajectoryGear FirstActiveGear;
			public float PlannedLength;
			public float LengthRatio;
		}

		private void Update()
		{
			if (m_Phase != Phase.Driving || m_Navigation == null)
				return;

			float now = Time.realtimeSinceStartup;
			if (m_LastRealtime > 0f)
			{
				float frameMs = (now - m_LastRealtime) * 1000f;
				m_FrameMs.Add(frameMs);
				if (frameMs > 16.7f) m_FramesOver16Ms++;
				if (frameMs > 33.3f) m_FramesOver33Ms++;
				if (frameMs > 50f) m_FramesOver50Ms++;
			}
			m_LastRealtime = now;
		}

		private void ResetPerformanceMetrics()
		{
			m_FrameMs.Clear();
			m_LastRealtime = Time.realtimeSinceStartup;
			m_FramesOver16Ms = 0;
			m_FramesOver33Ms = 0;
			m_FramesOver50Ms = 0;
			m_MaxPlanMs = 0f;
			m_MaxSliceMs = 0f;
			m_TotalPlanCpuMs = 0f;
			m_PlanSliceCount = 0;
			m_PlanningWallSec = 0f;
			m_PlanningWallStart = -1f;
			m_MaxCollisionQueries = 0;
			m_StartGcBytes = System.GC.GetTotalMemory(false);
		}

		private void TrackPlanningPerformance()
		{
			if (m_Navigation == null)
				return;

			bool planning = m_Navigation.DriverState == DriverFSM.State.Planning;
			if (planning)
			{
				if (m_PlanningWallStart < 0f)
					m_PlanningWallStart = Time.realtimeSinceStartup;
			}
			else if (m_PlanningWallStart >= 0f)
			{
				m_PlanningWallSec += Time.realtimeSinceStartup - m_PlanningWallStart;
				m_PlanningWallStart = -1f;
			}

			var stats = m_Navigation.LastLocalPlanStats;
			if (stats.PlanDurationMs > 0f)
			{
				m_TotalPlanCpuMs = Mathf.Max(m_TotalPlanCpuMs, stats.PlanDurationMs);
				m_MaxCollisionQueries = Mathf.Max(m_MaxCollisionQueries, stats.CollisionQueries);
				if (stats.BudgetTerminated)
					m_PlanSliceCount++;
			}
		}

		private void ApplyPerformanceMetrics(ref TestResult _result)
		{
			_result.TotalPlanCpuMs = m_TotalPlanCpuMs;
			_result.MaxPlanDurationMs = m_MaxSliceMs;
			_result.MaxSliceMs = m_MaxSliceMs;
			_result.PlanningWallSec = m_PlanningWallSec +
			                          (m_PlanningWallStart >= 0f
				                          ? Time.realtimeSinceStartup - m_PlanningWallStart
				                          : 0f);
			_result.PlanSliceCount = m_PlanSliceCount;
			_result.MaxCollisionQueries = m_MaxCollisionQueries;
			_result.GcAllocBytes = System.GC.GetTotalMemory(false) - m_StartGcBytes;
			_result.FramesOver16Ms = m_FramesOver16Ms;
			_result.FramesOver33Ms = m_FramesOver33Ms;
			_result.FramesOver50Ms = m_FramesOver50Ms;
			_result.MaxReplanCount = m_MaxReplanCount;

			if (m_FrameMs.Count == 0)
				return;

			m_FrameMs.Sort();
			int n = m_FrameMs.Count;
			_result.MedianFrameMs = m_FrameMs[n / 2];
			_result.P95FrameMs = m_FrameMs[Mathf.Min(n - 1, Mathf.CeilToInt(n * 0.95f) - 1)];
			_result.P99FrameMs = m_FrameMs[Mathf.Min(n - 1, Mathf.CeilToInt(n * 0.99f) - 1)];
			_result.WorstFrameMs = m_FrameMs[n - 1];
			float avgMs = 0f;
			for (int i = 0; i < n; i++)
				avgMs += m_FrameMs[i];
			avgMs /= n;
			_result.EffectiveFps = avgMs > 0.001f ? 1000f / avgMs : 0f;
		}

		private void Awake()
		{
			// Oval accept: tight along chassis, wider sideways. Old 0.1 circular → bump lateral.
			if (m_LongitudinalTolerance <= 0.05f)
				m_LongitudinalTolerance = 0.1f;
			else
				m_LongitudinalTolerance = Mathf.Clamp(m_LongitudinalTolerance, 0.08f, 0.2f);

			if (m_LateralTolerance <= 0.15f)
				m_LateralTolerance = 0.45f;
			else
				m_LateralTolerance = Mathf.Clamp(m_LateralTolerance, 0.25f, 0.6f);

			if (m_PositionTolerance <= 0.15f)
				m_PositionTolerance = m_LateralTolerance;
			else
				m_PositionTolerance = Mathf.Max(m_PositionTolerance, m_LateralTolerance);

			GenerateTestCases();
			SetupCamera();
			if (m_AutoStart)
				StartCoroutine(RunAllTests());
		}

		private void OnDestroy()
		{
			RestorePerformanceRunMode();
			if (!m_SummaryWritten && m_Results.Count > 0)
				WriteSummary("OnDestroy");
			CloseLog();
		}

		private void EnterPerformanceRunMode()
		{
			if (m_PerformanceRunActive)
				return;
			m_DriverFsmDebugSaved = DriverFSM.DebugLog;
			m_PlannerDebugSaved = LocalPosePlanner.DebugLog;
			DriverFSM.DebugLog = false;
			LocalPosePlanner.DebugLog = false;
			m_PerformanceRunActive = true;
		}

		private void RestorePerformanceRunMode()
		{
			if (!m_PerformanceRunActive)
				return;
			DriverFSM.DebugLog = m_DriverFsmDebugSaved;
			LocalPosePlanner.DebugLog = m_PlannerDebugSaved;
			m_PerformanceRunActive = false;
		}

		private void SetupCamera()
		{
			if (!m_UseOverheadCamera)
				return;

			if (m_TestCamera == null)
				m_TestCamera = Camera.main;
			if (m_TestCamera == null)
				return;

			Vector3 look = m_SpawnPoint != null ? m_SpawnPoint.position : m_StartPosition;
			m_TestCamera.transform.position = look + m_CameraOffset;
			m_TestCamera.transform.LookAt(look);
		}

		private void GenerateTestCases()
		{
			m_TestCases.Clear();
			if (m_TestVariant == VehicleTestVariant.Variant5_KinematicsCalibration)
			{
				GenerateCalibrationTestCases();
				return;
			}

			float[] distances = { 2f, 5f, 10f, 15f, 20f };
			float[] directions = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };

			foreach (float dist in distances)
			{
				foreach (float dir in directions)
				{
					float absAngle = Mathf.Abs(Mathf.DeltaAngle(0f, dir));
					TestManeuverType type;
					string family;

					if (absAngle <= 60f)
					{
						type = TestManeuverType.Forward;
						family = "Forward";
					}
					else if (absAngle >= 120f)
					{
						type = dist <= 5f ? TestManeuverType.Reverse : TestManeuverType.UTurn;
						// Position-only rear: straight-rev is valid; TurnAround is not required.
						family = type == TestManeuverType.Reverse
							? "Reverse"
							: "TurnAround|Reverse|Reposition";
					}
					else
					{
						type = TestManeuverType.SideApproach;
						family = "Reposition|TurnAround";
					}

					if (!ShouldRunType(type))
						continue;

					m_TestCases.Add(new TestCase
					{
						Name = $"{type}_{dir:F0}deg_{dist:F0}m",
						ManeuverType = type,
						Suite = VehicleTestSuite.OpenFieldBasic,
						DirectionDeg = dir,
						Distance = dist,
						HasHeading = false,
						HeadingYaw = 0f,
						ExpectedManeuverFamily = family
					});

					if (dist <= 15f)
					{
						m_TestCases.Add(new TestCase
						{
							Name = $"{type}_{dir:F0}deg_{dist:F0}m_heading",
							ManeuverType = type,
							Suite = VehicleTestSuite.PoseArrival,
							DirectionDeg = dir,
							Distance = dist,
							HasHeading = true,
							HeadingYaw = NormalizeYaw(dir),
							ExpectedManeuverFamily = family == "Forward" || family == "Reverse"
								? family + "|Reposition"
								: family
						});
					}

					if (dist <= 5f)
					{
						m_TestCases.Add(new TestCase
						{
							Name = $"{type}_{dir:F0}deg_{dist:F0}m_park90",
							ManeuverType = type,
							Suite = VehicleTestSuite.PoseArrival,
							DirectionDeg = dir,
							Distance = dist,
							HasHeading = true,
							HeadingYaw = NormalizeYaw(dir + 90f),
							ExpectedManeuverFamily = "Reposition|TurnAround"
						});
					}
				}
			}

			if (m_RunUTurnTests)
			{
				foreach (float dist in new[] { 3f, 6f, 10f, 15f })
				{
					m_TestCases.Add(new TestCase
					{
						Name = $"UTurn_explicit_{dist:F0}m",
						ManeuverType = TestManeuverType.UTurn,
						Suite = VehicleTestSuite.OpenFieldBasic,
						DirectionDeg = 180f,
						Distance = dist,
						HasHeading = true,
						HeadingYaw = 180f,
						ExpectedManeuverFamily = "TurnAround"
					});
				}
			}

			if (m_ShuffleTests)
				Shuffle(m_TestCases);

			FilterTestCasesByVariant();
			ApplyShardFilter();

			// Logged after OpenLogFile in RunAllTests (Awake runs before the file exists).
		}

		private void GenerateCalibrationTestCases()
		{
			m_TestCases.Add(new TestCase
			{
				Name = "Calib_ForwardCircle_MaxSteer",
				ManeuverType = TestManeuverType.KinematicsCalibration,
				Suite = VehicleTestSuite.OpenFieldBasic,
				DirectionDeg = 0f,
				Distance = 0f,
				HasHeading = false,
				ExpectedManeuverFamily = "Calibration"
			});
			m_TestCases.Add(new TestCase
			{
				Name = "Calib_ReverseCircle_MaxSteer",
				ManeuverType = TestManeuverType.KinematicsCalibration,
				Suite = VehicleTestSuite.OpenFieldBasic,
				DirectionDeg = 180f,
				Distance = 0f,
				HasHeading = false,
				ExpectedManeuverFamily = "Calibration"
			});
			m_TestCases.Add(new TestCase
			{
				Name = "Calib_SteerRise_Forward",
				ManeuverType = TestManeuverType.KinematicsCalibration,
				Suite = VehicleTestSuite.OpenFieldBasic,
				DirectionDeg = 0f,
				Distance = 0f,
				HasHeading = false,
				ExpectedManeuverFamily = "Calibration"
			});
			m_TestCases.Add(new TestCase
			{
				Name = "Calib_SteerRise_Reverse",
				ManeuverType = TestManeuverType.KinematicsCalibration,
				Suite = VehicleTestSuite.OpenFieldBasic,
				DirectionDeg = 180f,
				Distance = 0f,
				HasHeading = false,
				ExpectedManeuverFamily = "Calibration"
			});
		}

		private void ApplyShardFilter()
		{
			if (m_ShardCount <= 1 || m_TestVariant == VehicleTestVariant.Variant5_KinematicsCalibration)
				return;

			for (int i = m_TestCases.Count - 1; i >= 0; i--)
			{
				int bucket = StableTestHash(m_TestCases[i].Name) % m_ShardCount;
				if (bucket != m_ShardIndex)
					m_TestCases.RemoveAt(i);
			}
		}

		private static int StableTestHash(string _name)
		{
			unchecked
			{
				int hash = 17;
				for (int i = 0; i < _name.Length; i++)
					hash = hash * 31 + _name[i];
				return Mathf.Abs(hash);
			}
		}

		private void FilterTestCasesByVariant()
		{
			if (m_TestVariant == VehicleTestVariant.Variant5_KinematicsCalibration)
				return;

			bool wantHeading = m_TestVariant == VehicleTestVariant.Variant2_PoseArrival;
			for (int i = m_TestCases.Count - 1; i >= 0; i--)
			{
				if (m_TestCases[i].HasHeading != wantHeading)
					m_TestCases.RemoveAt(i);
			}
		}

		private static float NormalizeYaw(float _yaw)
		{
			while (_yaw < 0f) _yaw += 360f;
			while (_yaw >= 360f) _yaw -= 360f;
			return _yaw;
		}

		private bool ShouldRunType(TestManeuverType _type)
		{
			return _type switch
			{
				TestManeuverType.Forward => m_RunForwardTests,
				TestManeuverType.Reverse => m_RunReverseTests,
				TestManeuverType.SideApproach => m_RunSideTests,
				TestManeuverType.UTurn => m_RunUTurnTests,
				TestManeuverType.KinematicsCalibration => m_TestVariant == VehicleTestVariant.Variant5_KinematicsCalibration,
				_ => true
			};
		}

		private System.Collections.IEnumerator RunAllTests()
		{
			EnterPerformanceRunMode();
			OpenLogFile();
			WriteLog($"[TestPlatform] Generated {m_TestCases.Count} test cases ({m_TestVariant} P{m_PlatformId})");

			for (m_CurrentTestIndex = 0; m_CurrentTestIndex < m_TestCases.Count; m_CurrentTestIndex++)
				yield return StartCoroutine(RunSingleTest(m_TestCases[m_CurrentTestIndex]));

			m_Phase = Phase.Completed;
			WriteSummary("FinishAllTests");

			if (m_LoopTests)
			{
				m_CurrentTestIndex = 0;
				m_Results.Clear();
				m_SummaryWritten = false;
				GenerateTestCases();
				StartCoroutine(RunAllTests());
			}
		}

		private System.Collections.IEnumerator RunSingleTest(TestCase _test)
		{
			if (_test.ManeuverType == TestManeuverType.KinematicsCalibration)
			{
				yield return RunCalibrationTest(_test);
				yield break;
			}

			var result = new TestResult
			{
				TestName = _test.Name,
				ManeuverType = _test.ManeuverType,
				Distance = _test.Distance,
				DirectionDeg = _test.DirectionDeg
			};

			m_Phase = Phase.Spawning;
			SpawnVehicle();
			yield return new WaitForSeconds(0.5f);

			float readyWait = 0f;
			while (readyWait < 5f && !IsVehicleReady())
			{
				readyWait += Time.fixedDeltaTime;
				yield return new WaitForFixedUpdate();
			}

			if (!IsVehicleReady())
			{
				result.Success = false;
				result.Note = "Vehicle not ready";
				result.CompletionTime = readyWait;
				m_Results.Add(result);
				LogTestEnd(result);
				yield return new WaitForSeconds(m_InterTestDelay);
				DespawnVehicle();
				yield break;
			}

			result.StartPos = m_Navigation.transform.position;
			Vector3 target = _test.GetTargetPosition(result.StartPos);
			target.y = result.StartPos.y;
			result.TargetPos = target;
			float targetYaw = _test.ResolveTargetYaw(m_StartYaw);
			result.TargetYaw = targetYaw;
			result.ExpectedManeuverFamily = _test.ExpectedManeuverFamily;

			var p = m_Navigation.Context?.Params;
			if (p.HasValue)
				LogVehicleParams(p.Value);

			m_Phase = Phase.Driving;
			if (_test.HasHeading)
				m_Navigation.SetDestination(target, targetYaw, VehicleSpeedMode.Medium);
			else
				m_Navigation.SetDestination(target, VehicleSpeedMode.Medium);

			m_TestFailed = false;
			m_TestStartTime = Time.time;
			m_LastManeuverType = "";
			m_FrameCounter = 0;
			m_TestFrameCounter = 0;
			m_BestPoseError = float.MaxValue;
			m_StagnantTimer = 0f;
			m_LastProgressPos = result.StartPos;
			m_SteerFlipCount = 0;
			m_LastSteer = 0f;
			m_LastSpeedKmh = 0f;
			m_BrakingStartTime = -1f;
			m_PeakDecelMs2 = 0f;
			m_UsedHardBrake = false;
			m_CoastBrakeDuration = 0f;
			m_SoftBrakeDuration = 0f;
			m_HardBrakeDuration = 0f;
			m_MaxReplanCount = 0;
			m_LogLinesSinceFlush = 0;
			ResetPerformanceMetrics();
			m_LastLoggedPathRevision = -1;
			m_FirstActiveGear = null;
			m_MaxCrossTrack = 0f;
			m_PlannedLength = 0f;

			LogTestStart(_test, result);
			LogFrameHeader();

			float elapsed = 0f;
			StagnationKind stagnation = StagnationKind.None;
			float testTimeout = Mathf.Max(m_TestTimeout, 4f + _test.Distance * 1.2f);
			float progressGraceDelay = Mathf.Max(3f, _test.Distance * 0.5f);

			while (elapsed < testTimeout && !m_TestFailed)
			{
				elapsed = Time.time - m_TestStartTime;
				yield return new WaitForFixedUpdate();
				m_FrameCounter++;
				m_TestFrameCounter++;

				if (m_Navigation == null || m_VehicleInstance == null)
					break;

				int pathRevision = m_Navigation.ProgressSnapshot.PathRevision;
				if (pathRevision != m_LastLoggedPathRevision)
				{
					WriteLog($"--- PATH_REVISION: {pathRevision} replans={m_Navigation.ProgressSnapshot.ReplanCount}");
					LogLocalPoseStatsIfAny(pathRevision);
					m_LastLoggedPathRevision = pathRevision;
					var loggedTraj = m_Navigation.ActiveTrajectory;
					if (loggedTraj != null && loggedTraj.IsValid)
						m_PlannedLength = loggedTraj.TotalLength;
				}

				Vector3 pos = m_Navigation.transform.position;
				float currentDist = FlatDistanceXZ(pos, target);
				float headingErr = Mathf.Abs(Mathf.DeltaAngle(m_Navigation.Context.State.Yaw, targetYaw));
				float speed = m_Navigation.CurrentSpeed * 3.6f;
				var maneuver = m_Navigation.CurrentManeuver;
				string maneuverType = maneuver?.Type.ToString() ?? "-";
				var activeTraj = m_Navigation.ActiveTrajectory;
				var trackerOut = m_Navigation.LastTrackerOutput;
				float poseError = currentDist + (_test.HasHeading ? headingErr * 0.05f : 0f);

				if (m_Navigation.DriverState == DriverFSM.State.FollowingTrajectory &&
				    activeTraj != null && activeTraj.IsValid)
				{
					progressGraceDelay = Mathf.Max(progressGraceDelay, m_PlannedLength > 0.01f ? m_PlannedLength * 0.5f : _test.Distance * 0.5f);
					poseError = trackerOut.DistanceToEnd + (_test.HasHeading ? headingErr * 0.05f : 0f);
				}

				if (m_TestFrameCounter % m_LogEveryNFrames == 0)
					LogPerFrameData(pos, target, targetYaw, currentDist, headingErr, elapsed);

				if (!m_FirstActiveGear.HasValue &&
				    m_Navigation.DriverState == DriverFSM.State.FollowingTrajectory)
				{
					m_FirstActiveGear = trackerOut.ActiveGear;
				}

				m_MaxCrossTrack = Mathf.Max(m_MaxCrossTrack, Mathf.Abs(trackerOut.CrossTrack));

				TrackPlanningPerformance();
				m_MaxReplanCount = Mathf.Max(m_MaxReplanCount, m_Navigation.ProgressSnapshot.ReplanCount);

				float planWallLimit = m_Navigation.Settings != null &&
				                      m_Navigation.Settings.LocalPlanWallTimeoutSec > 0f
					? m_Navigation.Settings.LocalPlanWallTimeoutSec
					: 6f;
				if (m_Navigation.DriverState == DriverFSM.State.Planning &&
				    m_PlanningWallStart >= 0f &&
				    Time.realtimeSinceStartup - m_PlanningWallStart > planWallLimit + 1f &&
				    speed < 0.5f)
				{
					m_TestFailed = true;
					result.FinalDistance = currentDist;
					result.FinalHeadingError = headingErr;
					result.CompletionTime = elapsed;
					result.Success = false;
					result.Stagnation = StagnationKind.NoMotion;
					result.Note = "PlanningTimeout";
					break;
				}

				if (maneuverType != m_LastManeuverType)
				{
					LogManeuverChange(maneuver);
					m_LastManeuverType = maneuverType;
				}

				if (poseError < m_BestPoseError - 0.03f)
				{
					m_BestPoseError = poseError;
					m_StagnantTimer = 0f;
					m_LastProgressPos = pos;
				}
				else if (elapsed > progressGraceDelay)
				{
					m_StagnantTimer += Time.fixedDeltaTime;
				}

				float steer = m_Navigation.SteerCommand;
				if (Mathf.Sign(steer) != 0f && Mathf.Sign(m_LastSteer) != 0f &&
				    Mathf.Sign(steer) != Mathf.Sign(m_LastSteer))
					m_SteerFlipCount++;
				m_LastSteer = steer;

				TrackArrivalComfortMetrics(currentDist, speed, elapsed);

				float vehicleYaw = m_Navigation.Context.State.Yaw;
				bool poseSuccess = ArrivalPositionBand.IsInside(
					                   pos, vehicleYaw, target,
					                   m_LongitudinalTolerance, m_LateralTolerance) &&
				                   (!_test.HasHeading || headingErr <= m_HeadingTolerance) &&
				                   speed <= m_MaxArrivalSpeedKmh;

				if (m_Navigation.NavigationOutcome == NavigationOutcome.Succeeded)
				{
					Vector3 holdStart = pos;
					float settleElapsed = 0f;
					float maxDrift = 0f;
					while (settleElapsed < 0.5f &&
					       m_Navigation != null &&
					       m_VehicleInstance != null)
					{
						yield return new WaitForFixedUpdate();
						settleElapsed += Time.fixedDeltaTime;
						if (m_Navigation == null)
							break;
						maxDrift = Mathf.Max(maxDrift,
							Vector3.Distance(holdStart, m_Navigation.transform.position));
					}

					pos = m_Navigation.transform.position;
					currentDist = FlatDistanceXZ(pos, target);
					headingErr = Mathf.Abs(Mathf.DeltaAngle(
						m_Navigation.Context.State.Yaw, targetYaw));
					speed = m_Navigation.CurrentSpeed * 3.6f;
					poseSuccess = ArrivalPositionBand.IsInside(
						              pos, m_Navigation.Context.State.Yaw, target,
						              m_LongitudinalTolerance, m_LateralTolerance) &&
					              (!_test.HasHeading || headingErr <= m_HeadingTolerance) &&
					              speed <= m_MaxArrivalSpeedKmh &&
					              maxDrift <= 0.05f;

					result.FinalDistance = currentDist;
					result.FinalHeadingError = headingErr;
					result.FinalSpeed = speed;
					result.PostStopDrift = maxDrift;
					result.PeakDecelMs2 = m_PeakDecelMs2;
					result.BrakeDurationSec = m_CoastBrakeDuration + m_SoftBrakeDuration + m_HardBrakeDuration;
					result.CoastBrakeDurationSec = m_CoastBrakeDuration;
					result.SoftBrakeDurationSec = m_SoftBrakeDuration;
					result.HardBrakeDurationSec = m_HardBrakeDuration;
					result.MaxPlanDurationMs = m_MaxPlanMs;
					result.MaxReplanCount = m_MaxReplanCount;
					result.UsedHardBrakeOnArrival = m_UsedHardBrake;
					result.ChosenMode = m_Navigation.ActivePlan.DrivingMode;
					result.PlanReason = m_Navigation.ActivePlanReason;
					result.CompletionTime = elapsed;
					result.Success = poseSuccess;
					if (!result.Success)
						result.Note = $"Goal hold failed dist={currentDist:F2}m yaw={headingErr:F1}° speed={speed:F2}km/h drift={maxDrift:F2}m";
					break;
				}

				if (m_Navigation.NavigationOutcome == NavigationOutcome.NoPath ||
				    m_Navigation.NavigationOutcome == NavigationOutcome.NoFeasibleManeuver ||
				    m_Navigation.NavigationOutcome == NavigationOutcome.Stuck)
				{
					m_TestFailed = true;
					result.FinalDistance = currentDist;
					result.FinalHeadingError = headingErr;
					result.CompletionTime = elapsed;
					result.Success = false;
					result.Note = m_Navigation.NavigationOutcome.ToString();
					break;
				}

				if (m_StagnantTimer > 5f)
				{
					m_TestFailed = true;
					result.FinalDistance = currentDist;
					result.FinalHeadingError = headingErr;
					result.FinalSpeed = speed;
					result.ChosenMode = m_Navigation.ActivePlan.DrivingMode;
					result.PlanReason = m_Navigation.ActivePlanReason;
					result.CompletionTime = elapsed;
					result.Success = false;
					bool skipDiverging = m_Navigation.DriverState == DriverFSM.State.FollowingTrajectory &&
					                     activeTraj != null && activeTraj.IsValid &&
					                     activeTraj.GearSegmentCount > 1 &&
					                     Mathf.Abs(trackerOut.CrossTrack) < 1f;
					if (m_Navigation.DriverState == DriverFSM.State.Planning && speed < 0.5f)
						stagnation = StagnationKind.NoMotion;
					else
						stagnation = !skipDiverging && currentDist > m_BestPoseError + 0.5f
						? StagnationKind.Diverging
						: Vector3.Distance(pos, m_LastProgressPos) < 0.05f
							? StagnationKind.NoMotion
							: m_SteerFlipCount > 20
								? StagnationKind.ControllerOscillation
								: StagnationKind.NoPathProgress;
					result.Stagnation = stagnation;
					result.Note = $"{stagnation} at {currentDist:F1}m (best={m_BestPoseError:F1})";
					break;
				}
			}

			if (elapsed >= testTimeout && !m_TestFailed && !result.Success)
			{
				result.Success = false;
				result.Note = "Timeout";
				result.CompletionTime = elapsed;
				result.FinalDistance = Vector3.Distance(m_Navigation.transform.position, target);
				result.FinalHeadingError = Mathf.Abs(Mathf.DeltaAngle(
					m_Navigation.transform.eulerAngles.y, targetYaw));
				result.ChosenMode = m_Navigation.ActivePlan.DrivingMode;
				result.PlanReason = m_Navigation.ActivePlanReason;
			}

			m_Phase = Phase.Arrived;
			result.PeakDecelMs2 = m_PeakDecelMs2;
			result.BrakeDurationSec = m_CoastBrakeDuration + m_SoftBrakeDuration + m_HardBrakeDuration;
			result.CoastBrakeDurationSec = m_CoastBrakeDuration;
			result.SoftBrakeDurationSec = m_SoftBrakeDuration;
			result.HardBrakeDurationSec = m_HardBrakeDuration;
			result.MaxPlanDurationMs = m_MaxPlanMs;
			result.MaxReplanCount = m_MaxReplanCount;
			result.UsedHardBrakeOnArrival = m_UsedHardBrake;
			ApplyPerformanceMetrics(ref result);
			ApplyTestDiagnostics(_test, ref result, result.Success);
			LogTestEnd(result);
			FlushLog();
			m_Results.Add(result);
			yield return new WaitForSeconds(m_InterTestDelay);

			m_Phase = Phase.Respawning;
			if (m_RespawnBetweenTests)
			{
				DespawnVehicle();
				yield return new WaitForSeconds(0.3f);
			}
		}

		private System.Collections.IEnumerator RunCalibrationTest(TestCase _test)
		{
			var result = new TestResult
			{
				TestName = _test.Name,
				ManeuverType = _test.ManeuverType,
				ExpectedManeuverFamily = _test.ExpectedManeuverFamily
			};

			m_Phase = Phase.Spawning;
			SpawnVehicle();
			yield return new WaitForSeconds(0.5f);

			float readyWait = 0f;
			while (readyWait < 5f && !IsVehicleReady())
			{
				readyWait += Time.fixedDeltaTime;
				yield return new WaitForFixedUpdate();
			}

			if (!IsVehicleReady())
			{
				result.Success = false;
				result.Note = "Vehicle not ready";
				result.CompletionTime = readyWait;
				m_Results.Add(result);
				LogTestEnd(result);
				yield return new WaitForSeconds(m_InterTestDelay);
				DespawnVehicle();
				yield break;
			}

			if (m_Navigation != null)
			{
				m_Navigation.Stop();
				m_NavigationWasEnabled = m_Navigation.enabled;
				m_Navigation.enabled = false;
			}

			if (m_VehicleController != null)
				m_VehicleController.SetExternalDriveHoldOverride(true);

			float engineWait = 0f;
			while (engineWait < 8f && (m_Brain == null || !m_Brain.CanDrive))
			{
				engineWait += Time.fixedDeltaTime;
				yield return new WaitForFixedUpdate();
			}

			if (m_Brain == null || !m_Brain.CanDrive)
			{
				result.Success = false;
				result.Note = "Engine not ready for calibration";
				result.CompletionTime = engineWait;
				if (m_VehicleController != null)
					m_VehicleController.SetExternalDriveHoldOverride(false);
				m_Results.Add(result);
				LogTestEnd(result);
				yield return new WaitForSeconds(m_InterTestDelay);
				DespawnVehicle();
				yield break;
			}

			WriteLog($"BRAIN ready | CanDrive={m_Brain.CanDrive} Engine={m_Brain.EngineRunning} Ready={m_Brain.EngineReady}");

			bool reverse = _test.Name.Contains("Reverse");
			bool steerRise = _test.Name.Contains("SteerRise");
			m_CalibrationSession = new VehicleKinematicsCalibrationSession(reverse, steerRise);
			result.StartPos = m_VehicleInstance.transform.position;

			WriteLog("");
			WriteLog("==============================================================");
			WriteLog($"TEST #{m_CurrentTestIndex + 1}: {_test.Name}");
			WriteLog($"  Type: Calibration | reverse={reverse} | steerRise={steerRise}");
			WriteLog("==============================================================");
			WriteLog("# CALIB|TIME|PHASE|SPD|STEER_CMD|ACT_STEER|YAW|X|Z|R_INST");

			m_TestStartTime = Time.time;
			float timeout = 25f;
			int frame = 0;
			var rb = m_VehicleInstance.GetComponent<Rigidbody>();
			var motor = m_VehicleInstance.GetComponentInChildren<WheeledMotor>();

			while (Time.time - m_TestStartTime < timeout &&
			       m_CalibrationSession.Phase != CalibrationPhase.Done &&
			       m_VehicleInstance != null)
			{
				yield return new WaitForFixedUpdate();
				frame++;

				Vector3 pos = m_VehicleInstance.transform.position;
				float yaw = m_VehicleInstance.transform.eulerAngles.y;
				float speed = rb != null ? rb.linearVelocity.magnitude * 3.6f : 0f;
				float actSteer = motor != null ? motor.CurrentSteerNormalized : 0f;
				float elapsed = Time.time - m_TestStartTime;

				var cmd = m_CalibrationSession.Tick(
					pos, yaw, speed, actSteer, Time.fixedDeltaTime);
				m_Brain.SetCommand(cmd);

				if (frame % m_LogEveryNFrames == 0)
				{
					WriteLog(
						$"{frame}|{elapsed:F2}|{m_CalibrationSession.Phase}|{speed:F2}|{cmd.Steer:F3}|{actSteer:F3}|{yaw:F1}|{pos.x:F2}|{pos.z:F2}|{m_CalibrationSession.InstantRadius:F2}");
				}
			}

			var profile = BuildKinematicsProfile();
			var calib = m_CalibrationSession.BuildResult(profile);
			m_CalibrationResults.Add(calib);

			result.CompletionTime = Time.time - m_TestStartTime;
			result.Success = calib.Success;
			result.Note = calib.Note;
			result.FinalDistance = calib.MeasuredRadiusM;

			WriteLog(
				$"RESULT: {_test.Name} | success={calib.Success} | {calib.Note} | " +
				$"path={calib.PathLengthM:F2}m yaw={calib.YawDeltaDeg:F1}° rise={calib.SteerRiseSec:F2}s");
			LogTestEnd(result);
			FlushLog();
			m_Results.Add(result);
			yield return new WaitForSeconds(m_InterTestDelay);

			m_Phase = Phase.Respawning;
			if (m_RespawnBetweenTests)
			{
				DespawnVehicle();
				yield return new WaitForSeconds(0.3f);
			}
		}

		private VehicleKinematicsProfile BuildKinematicsProfile()
		{
			if (m_Brain != null && m_Brain.Tuning != null)
			{
				var t = m_Brain.Tuning;
				return new VehicleKinematicsProfile(
					t.WheelBase, 4.8f, 2.4f, t.DefaultSteerAngle);
			}

			return new VehicleKinematicsProfile(3.5f, 4.8f, 2.4f, 32f);
		}

		private void WriteSummary(string _reason)
		{
			if (m_SummaryWritten || m_Results.Count == 0)
				return;

			var sb = new StringBuilder();
			sb.AppendLine();
			sb.AppendLine("==============================================================");
			sb.AppendLine($"              TEST RESULTS SUMMARY ({_reason})");
			sb.AppendLine("==============================================================");
			sb.AppendLine($"Total: {m_Results.Count}");
			int passed = 0;

			foreach (TestManeuverType type in Enum.GetValues(typeof(TestManeuverType)))
			{
				if (type == TestManeuverType.All) continue;
				int t = 0, p = 0;
				foreach (var r in m_Results)
				{
					if (r.ManeuverType != type) continue;
					t++;
					if (r.Success) p++;
				}
				if (t > 0) sb.AppendLine($"  {type,-12}: {p}/{t} passed");
			}

			sb.AppendLine("--------------------------------------------------------------");
			foreach (var r in m_Results)
			{
				if (r.Success) passed++;
				sb.AppendLine($"  {(r.Success ? "PASS" : "FAIL")} | {r.TestName,-30} | mode={r.ChosenMode,-10} | dist={r.Distance:F0}m dir={r.DirectionDeg:F0}° | time={r.CompletionTime:F1}s | finalDist={r.FinalDistance:F2}m yawErr={r.FinalHeadingError:F1}° drift={r.PostStopDrift:F2}m decel={r.PeakDecelMs2:F2}m/s² brake={r.BrakeDurationSec:F1}s coast={r.CoastBrakeDurationSec:F1}s soft={r.SoftBrakeDurationSec:F1}s hard={r.HardBrakeDurationSec:F1}s planMs={r.MaxPlanDurationMs:F0} replans={r.MaxReplanCount} hardBrk={(r.UsedHardBrakeOnArrival ? "Y" : "N")} | {r.Note}");
			}
			sb.AppendLine("--------------------------------------------------------------");
			sb.AppendLine($"Passed: {passed}/{m_Results.Count}  Failed: {m_Results.Count - passed}/{m_Results.Count}");

			if (m_TestVariant == VehicleTestVariant.Variant5_KinematicsCalibration &&
			    m_CalibrationResults.Count > 0)
			{
				sb.AppendLine();
				sb.AppendLine("--- CALIBRATION SUMMARY ---");
				var profile = BuildKinematicsProfile();
				sb.AppendLine(
					$"  theoreticalMinR={profile.MinTurningRadius:F2}m effectiveR={profile.EffectiveTurnRadius:F2}m " +
					$"trackableR={profile.EffectiveTurnRadius * 1.15f:F2}m");
				foreach (var c in m_CalibrationResults)
				{
					sb.AppendLine(
						$"  {(c.Success ? "OK" : "FAIL")} | R={c.MeasuredRadiusM:F2}m path={c.PathLengthM:F2}m " +
						$"yaw={c.YawDeltaDeg:F1}° rise={c.SteerRiseSec:F2}s | {c.Note}");
				}

				float fwdR = 0f;
				float revR = 0f;
				foreach (var r in m_Results)
				{
					if (r.TestName.Contains("ForwardCircle") && r.Success)
						fwdR = r.FinalDistance;
					if (r.TestName.Contains("ReverseCircle") && r.Success)
						revR = r.FinalDistance;
				}

				float measured = Mathf.Max(fwdR, revR);
				if (measured > profile.EffectiveTurnRadius * 1.15f)
					sb.AppendLine(
						$"  RECOMMENDATION: measuredR {measured:F2}m > trackable {profile.EffectiveTurnRadius * 1.15f:F2}m — consider increasing TurnRadiusMultiplier");
				else
					sb.AppendLine("  RECOMMENDATION: measured R within trackable envelope — no multiplier change needed");
			}

			string summary = sb.ToString();
			WriteLog(summary);
			if (m_TestVariant != VehicleTestVariant.Variant5_KinematicsCalibration && m_PlatformId <= 1)
				WriteBaselineComparison();
			m_SummaryWritten = true;
			FlushLog();
			CloseLog();
		}

		private void SpawnVehicle()
		{
			if (m_VehiclePrefab == null)
			{
				WriteLog("[TestPlatform] ERROR: Vehicle prefab not assigned!");
				return;
			}

			Vector3 spawnPos = m_SpawnPoint != null ? m_SpawnPoint.position : m_StartPosition;
			Quaternion spawnRot = Quaternion.Euler(0f, m_StartYaw, 0f);
			m_VehicleInstance = Instantiate(m_VehiclePrefab, spawnPos, spawnRot);

			m_Navigation = m_VehicleInstance.GetComponent<VehicleNavigation>() ??
			               m_VehicleInstance.GetComponentInChildren<VehicleNavigation>();
			m_Brain = m_VehicleInstance.GetComponent<VehicleBrain>();
			m_VehicleController = m_VehicleInstance.GetComponent<VehicleController>();
			if (m_VehicleController != null)
				VehicleFileLog.BindTestVehicle(m_VehicleController);

			if (m_Brain != null)
			{
				m_Brain.SetControlActive(true);
				m_Brain.StartEngine();
				LogLine($"BRAIN | ControlActive={m_Brain.ControlActive} Engine={m_Brain.EngineRunning} CanDrive={m_Brain.CanDrive} Ready={m_Brain.EngineReady}");
			}

			if (m_Navigation == null)
				WriteLog("[TestPlatform] ERROR: VehicleNavigation component not found on prefab!");
		}

		private void DespawnVehicle()
		{
			if (m_VehicleController != null)
			{
				VehicleFileLog.UnbindTestVehicle(m_VehicleController);
				m_VehicleController.SetExternalDriveHoldOverride(false);
			}

			if (m_Navigation != null)
			{
				if (!m_Navigation.enabled && m_NavigationWasEnabled)
					m_Navigation.enabled = true;
				m_Navigation.Stop();
			}

			m_NavigationWasEnabled = false;

			if (m_VehicleInstance != null)
			{
				PlanningObstacleSnapshot.ClearColliderCache(m_VehicleInstance.transform);
				if (m_VehicleInstance.TryGetComponent(out VehicleController vehicle))
					VehicleUnitBlocker.DestroyFor(vehicle);

				Destroy(m_VehicleInstance);
				m_VehicleInstance = null;
			}

			m_Navigation = null;
			m_Brain = null;
			m_VehicleController = null;
		}

		private bool IsVehicleReady()
		{
			if (m_Navigation == null || m_VehicleInstance == null) return false;
			if (m_Brain != null && !m_Brain.CanDrive)
				return false;
			return true;
		}

		private void OpenLogFile()
		{
			string dir = VehicleFileLog.GetTestsDirectory();
			string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
			string variantSuffix = GetVariantLogSuffix();
			string variantLabel = GetVariantLabel();
			string path = Path.Combine(dir, $"VehicleTest_{stamp}{variantSuffix}.log");
			m_LogWriter = new StreamWriter(path, false, Encoding.UTF8);
			VehicleFileLog.AttachTestWriter(m_LogWriter);
			WriteLog("==============================================================");
			WriteLog("         VEHICLE TEST PLATFORM — DETAILED LOG");
			WriteLog("==============================================================");
			WriteLog($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
			WriteLog($"Scene: {SceneManager.GetActiveScene().name}");
			WriteLog($"FixedDeltaTime: {Time.fixedDeltaTime:F4}");
			WriteLog($"Prefab: {(m_VehiclePrefab != null ? m_VehiclePrefab.name : "null")}");
			WriteLog($"TestVariant: {variantLabel}");
			WriteLog($"PlatformId: P{m_PlatformId} | Shard: {m_ShardIndex}/{m_ShardCount}");
			WriteLog($"Test cases: {m_TestCases.Count}");
			Vector3 spawn = m_SpawnPoint != null ? m_SpawnPoint.position : m_StartPosition;
			WriteLog($"Spawn: ({spawn.x:F1}, {spawn.z:F1})");
			WriteLog($"Pose tolerance: oval lon={m_LongitudinalTolerance:F2}m lat={m_LateralTolerance:F2}m (equiv={m_PositionTolerance:F2}m) yaw={m_HeadingTolerance:F1}° speed={m_MaxArrivalSpeedKmh:F1}km/h");
			WriteLog($"Timeout per test: {m_TestTimeout}s | Loop: {m_LoopTests}");
			WriteLog("==============================================================");
			WriteLog("");
			FlushLog();
		}

		private string GetVariantLogSuffix()
		{
			switch (m_TestVariant)
			{
				case VehicleTestVariant.Variant2_PoseArrival:
					return m_PlatformId > 0 ? $"_V2_P{m_PlatformId}" : "_V2";
				case VehicleTestVariant.Variant5_KinematicsCalibration:
					return "_V5";
				default:
					return m_PlatformId > 0 ? $"_V1_P{m_PlatformId}" : "_V1";
			}
		}

		private string GetVariantLabel()
		{
			switch (m_TestVariant)
			{
				case VehicleTestVariant.Variant2_PoseArrival:
					return "V2";
				case VehicleTestVariant.Variant5_KinematicsCalibration:
					return "V5";
				default:
					return "V1";
			}
		}

		private void CloseLog()
		{
			if (m_LogWriter == null)
				return;
			VehicleFileLog.DetachTestWriter(m_LogWriter);
			try
			{
				m_LogWriter.Flush();
				m_LogWriter.Close();
			}
			catch
			{
				// ignored on teardown
			}
			m_LogWriter = null;
		}

		private void LogVehicleParams(VehicleParameters _p)
		{
			WriteLog("");
			WriteLog("--- VEHICLE PARAMETERS ---");
			WriteLog($"  wheelBase={_p.WheelBase:F2}m | length={_p.Length:F2}m | width={_p.Width:F2}m");
			WriteLog($"  turnRadius={_p.MinTurningRadius:F2}m effective={_p.EffectiveTurnRadius:F2}m | maxSteer={_p.MaxSteeringAngleDeg:F0}deg");
			WriteLog($"  maxFwd={_p.MaxForwardSpeedKmh:F0}km/h | maxRev={_p.MaxReverseSpeedKmh:F0}km/h");
			WriteLog("");
		}

		private void LogTestStart(TestCase _test, TestResult _result)
		{
			WriteLog("");
			WriteLog("==============================================================");
			WriteLog($"TEST #{m_CurrentTestIndex + 1}: {_test.Name}");
			WriteLog($"  Type: {_test.ManeuverType} | Suite: {_test.Suite} | Dir: {_test.DirectionDeg:F0}deg | Dist: {_test.Distance:F0}m");
			WriteLog($"  Expected family: {_test.ExpectedManeuverFamily}");
			WriteLog($"  Heading required: {_test.HasHeading} policy={_test.ResolveHeadingSource()} effectiveYaw={_result.TargetYaw:F1}");
			WriteLog($"  Start:  ({_result.StartPos.x:F3}, {_result.StartPos.y:F3}, {_result.StartPos.z:F3}) yaw={m_StartYaw:F1}");
			WriteLog($"  Target: ({_result.TargetPos.x:F3}, {_result.TargetPos.y:F3}, {_result.TargetPos.z:F3}) yaw={_result.TargetYaw:F1}");
			WriteLog("==============================================================");
		}

		private void LogFrameHeader()
		{
			WriteLog("# FRAME|TIME|FSM|OUTCOME|PLAN_MODE|PATH_REV|POS_X|POS_Z|YAW|SPD|REM_DIST|DST_TGT|YAW_ERR|POSE_ERR|STAG|THR|STEER|ACT_STEER|BRK|TRK_IDX|TRK_GEAR|CMD_REV|WAIT_STOP|NEED_REPLAN|XTRACK|PATH_CURV|WHEEL_CURV|LOOK_X|LOOK_Z|PLAN_X|PLAN_Z|PLAN_YAW|GATE|PATH_YAW|PLAN");
		}

		private void LogLocalPoseStatsIfAny(int _pathRevision)
		{
			if (m_Navigation == null)
				return;
			var stats = m_Navigation.LastLocalPlanStats;
			var traj = m_Navigation.ActiveTrajectory;
			WriteLog($"--- LOCAL_POSE rev={_pathRevision} valid={(traj != null && traj.IsValid)}");
			if (traj == null || !traj.IsValid)
			{
				WriteLog($"--- LOCAL_POSE: invalid reason={stats.Reason} expanded={stats.Expanded} tried={stats.CandidatesTried} gen={stats.CandidatesGenerated} analyticGen={stats.AnalyticGenerated} analyticValid={stats.AnalyticValid} colRej={stats.RejectedCollision} tolRej={stats.RejectedTolerance} rays={stats.SnapshotRays} colQ={stats.CollisionQueries} primQ={stats.PrimitiveCollisionQueries} trajQ={stats.TrajectoryCollisionQueries} planCpuMs={stats.PlanDurationMs:F0} phase={stats.Phase} step={stats.StepIndex} shots={stats.AnalyticShots} budget={stats.BudgetTerminated} budgetReason={stats.BudgetReason}");
				if (!string.IsNullOrEmpty(stats.TopCandidatesSummary))
					WriteLog($"--- LOCAL_POSE TOP: {stats.TopCandidatesSummary}");
				return;
			}

			WriteLog($"--- LOCAL_POSE: len={traj.TotalLength:F2}m segs={traj.GearSegmentCount} pts={traj.PointCount} cost={traj.Cost:F1} expanded={stats.Expanded} gen={stats.Generated} tried={stats.CandidatesTried} analyticGen={stats.AnalyticGenerated} analyticValid={stats.AnalyticValid} colRej={stats.RejectedCollision} tolRej={stats.RejectedTolerance} rays={stats.SnapshotRays} colQ={stats.CollisionQueries} primQ={stats.PrimitiveCollisionQueries} trajQ={stats.TrajectoryCollisionQueries} planCpuMs={stats.PlanDurationMs:F0} phase={stats.Phase} step={stats.StepIndex} shots={stats.AnalyticShots} budget={stats.BudgetTerminated} budgetReason={stats.BudgetReason} reason={traj.DebugReason}");
			if (!string.IsNullOrEmpty(stats.TopCandidatesSummary))
				WriteLog($"--- LOCAL_POSE TOP: {stats.TopCandidatesSummary}");

			int segStart = 0;
			for (int seg = 0; seg < traj.GearSegmentCount; seg++)
			{
				int segEnd = traj.PointCount - 1;
				for (int c = 0; c < traj.CuspIndices.Count; c++)
				{
					if (traj.CuspIndices[c] > segStart)
					{
						segEnd = traj.CuspIndices[c];
						break;
					}
				}

				var p0 = traj.Points[segStart];
				var p1 = traj.Points[segEnd];
				float segLen = p1.ArcLength - p0.ArcLength;
				WriteLog($"--- SEG[{seg}] gear={p0.Gear} len={segLen:F2}m start=({p0.Position.x:F2},{p0.Position.z:F2}) yaw={p0.YawDegrees:F1} end=({p1.Position.x:F2},{p1.Position.z:F2}) yaw={p1.YawDegrees:F1}");
				segStart = segEnd;
			}
		}

		private void TrackArrivalComfortMetrics(float _distToGoal, float _speedKmh, float _elapsed)
		{
			if (m_Navigation == null)
				return;

			var brake = m_Navigation.LastCommand.BrakeMode;
			float dt = Time.fixedDeltaTime;
			bool nearGoal = _distToGoal <= 3f ||
			                m_Navigation.DriverState == DriverFSM.State.Arrival ||
			                m_Navigation.DriverState == DriverFSM.State.Holding ||
			                (m_Navigation.DriverState == DriverFSM.State.FollowingTrajectory &&
			                 _distToGoal <= 4f);

			if (nearGoal && brake != VehicleBrakeMode.None)
			{
				if (m_BrakingStartTime < 0f)
					m_BrakingStartTime = _elapsed;

				switch (brake)
				{
					case VehicleBrakeMode.Coast:
						m_CoastBrakeDuration += dt;
						break;
					case VehicleBrakeMode.Soft:
						m_SoftBrakeDuration += dt;
						break;
					case VehicleBrakeMode.Hard:
						m_HardBrakeDuration += dt;
						m_UsedHardBrake = true;
						break;
				}
			}

			if (nearGoal && brake == VehicleBrakeMode.Hard)
				m_UsedHardBrake = true;

			if (nearGoal && brake != VehicleBrakeMode.None &&
			    dt > 0.0001f && m_LastSpeedKmh > 0.05f)
			{
				float decelMs2 = (_speedKmh - m_LastSpeedKmh) / 3.6f / dt;
				if (decelMs2 < 0f)
					m_PeakDecelMs2 = Mathf.Max(m_PeakDecelMs2, -decelMs2);
			}

			m_MaxPlanMs = Mathf.Max(m_MaxPlanMs, m_Navigation.LastLocalPlanStats.PlanDurationMs);
			m_MaxReplanCount = Mathf.Max(m_MaxReplanCount, m_Navigation.ProgressSnapshot.ReplanCount);
			m_LastSpeedKmh = _speedKmh;
		}

		private void LogPerFrameData(Vector3 _pos, Vector3 _target, float _targetYaw, float _dist, float _yawErr, float _elapsed)
		{
			if (m_Navigation == null) return;

			var snap = m_Navigation.ProgressSnapshot;
			float speedKmh = m_Navigation.CurrentSpeed * 3.6f;
			float poseErr = _dist + _yawErr * 0.05f;
			var trk = m_Navigation.LastTrackerOutput;
			Vector3 look = trk.LookAheadPoint;
			float actSteer = m_Navigation.ActualSteerNormalized;

			float planX = 0f, planZ = 0f, planYaw = 0f;
			float pathCurv = 0f;
			var traj = m_Navigation.ActiveTrajectory;
			if (traj != null && traj.IsValid && traj.PointCount > 0)
			{
				int idx = Mathf.Clamp(trk.NearestIndex, 0, traj.PointCount - 1);
				var nearest = traj.Points[idx];
				planX = nearest.Position.x;
				planZ = nearest.Position.z;
				planYaw = nearest.YawDegrees;
				pathCurv = nearest.Curvature;
			}

			var sb = m_FrameLogSb;
			sb.Clear();
			sb.Append($"{m_TestFrameCounter}|{_elapsed:F3}|{m_Navigation.DriverState}|{m_Navigation.NavigationOutcome}|");
			sb.Append($"{m_Navigation.ActivePlan.DrivingMode}|{snap.PathRevision}|{_pos.x:F2}|{_pos.z:F2}|");
			sb.Append($"{m_Navigation.Context.State.Yaw:F1}|{speedKmh:F1}|");
			sb.Append($"{snap.ArcLengthRemaining:F2}|{_dist:F2}|{_yawErr:F1}|{poseErr:F2}|{snap.Stagnation}|");
			sb.Append($"{m_Navigation.ThrottleCommand:F3}|{m_Navigation.SteerCommand:F3}|{actSteer:F3}|");
			sb.Append($"{m_Navigation.LastCommand.BrakeMode}|{trk.NearestIndex}|{trk.ActiveGear}|");
			sb.Append($"{(trk.Command.Reverse ? 1 : 0)}|{(trk.WaitingForStop ? 1 : 0)}|{(trk.NeedPathReplan ? 1 : 0)}|{trk.CrossTrack:F3}|");
			sb.Append($"{pathCurv:F3}|{trk.WheelCurvature:F3}|{look.x:F2}|{look.z:F2}|");
			sb.Append($"{planX:F2}|{planZ:F2}|{planYaw:F1}|");
			sb.Append($"{(m_Navigation.TurnEntryGateActive ? 1 : 0)}|{m_Navigation.PathYawAtIndex:F1}|");
			sb.Append(m_Navigation.ActivePlanReason);
			if (m_Navigation.DriverState == DriverFSM.State.Planning)
			{
				var stats = m_Navigation.LastLocalPlanStats;
				float wallMs = m_PlanningWallStart >= 0f
					? (Time.realtimeSinceStartup - m_PlanningWallStart) * 1000f
					: 0f;
				sb.Append(
					$"|planCpu={stats.PlanDurationMs:F0}|planWall={wallMs:F0}|phase={stats.Phase}|step={stats.StepIndex}|aGen={stats.AnalyticGenerated}|aVal={stats.AnalyticValid}|budgetReason={stats.BudgetReason}");
			}
			WriteLog(sb.ToString());
		}

		private void LogManeuverChange(Maneuver _maneuver)
		{
			if (_maneuver == null) return;
			WriteLog($"--- MANEUVER START: {_maneuver.Type} | allowReverse={_maneuver.AllowReverse} | isArrival={_maneuver.IsArrivalManeuver}");
		}

		private void LogTestEnd(TestResult _result)
		{
			WriteLog("--------------------------------------------------------------");
			WriteLog($"RESULT: {(_result.Success ? "PASS" : "FAIL")} | time={_result.CompletionTime:F1}s | finalDist={_result.FinalDistance:F3}m | yawErr={_result.FinalHeadingError:F1}° | speed={_result.FinalSpeed:F1}km/h | drift={_result.PostStopDrift:F3}m | peakDecel={_result.PeakDecelMs2:F2}m/s² | brakeDur={_result.BrakeDurationSec:F1}s coast={_result.CoastBrakeDurationSec:F1}s soft={_result.SoftBrakeDurationSec:F1}s hard={_result.HardBrakeDurationSec:F1}s | totalPlanCpuMs={_result.TotalPlanCpuMs:F0} maxSliceMs={_result.MaxSliceMs:F1} planWall={_result.PlanningWallSec:F2}s slices={_result.PlanSliceCount} colQ={_result.MaxCollisionQueries} replans={_result.MaxReplanCount} | fps={_result.EffectiveFps:F1} med={_result.MedianFrameMs:F1} p95={_result.P95FrameMs:F1} p99={_result.P99FrameMs:F1} worst={_result.WorstFrameMs:F1} >16={_result.FramesOver16Ms} >33={_result.FramesOver33Ms} >50={_result.FramesOver50Ms} gc={_result.GcAllocBytes} | hardBrake={(_result.UsedHardBrakeOnArrival ? "yes" : "no")}");
			WriteLog($"  Chosen mode: {_result.ChosenMode} | Plan: {_result.PlanReason}");
			WriteLog($"  Family: expected={_result.ExpectedManeuverFamily} actual={_result.ActualManeuverFamily} matched={_result.ExpectedFamilyMatched} | firstGear={_result.FirstActiveGear} | plannedLen={_result.PlannedLength:F2}m ratio={_result.LengthRatio:F2}");
			if (!string.IsNullOrEmpty(_result.Note))
				WriteLog($"  Note: {_result.Note}");
			WriteLog("--------------------------------------------------------------");
			WriteLog("");
		}

		private void ApplyTestDiagnostics(TestCase _test, ref TestResult _result, bool _poseSuccess)
		{
			_result.Success = _poseSuccess;
			_result.PlannedLength = m_PlannedLength;
			_result.LengthRatio = _test.Distance > 0.01f && m_PlannedLength > 0.01f
				? m_PlannedLength / _test.Distance
				: 0f;
			_result.FirstActiveGear = m_FirstActiveGear ?? TrajectoryGear.Forward;
			_result.ActualManeuverFamily = InferManeuverFamily(
				m_Navigation != null ? m_Navigation.ActiveTrajectory : null,
				_result.FirstActiveGear,
				m_Navigation != null ? m_Navigation.CurrentManeuver : null);
			_result.ExpectedFamilyMatched = MatchesExpectedFamily(
				_test.ExpectedManeuverFamily, _result.ActualManeuverFamily);

			if (!_result.Success)
				return;

			if (!_result.ExpectedFamilyMatched)
			{
				_result.Success = false;
				_result.Note = $"Family mismatch expected={_test.ExpectedManeuverFamily} actual={_result.ActualManeuverFamily}";
				return;
			}

			float turnRadius = m_Navigation?.Context?.Params.EffectiveTurnRadius ?? 6.5f;
			float lengthBudget = ComputeLengthBudget(_test, turnRadius);
			if (m_PlannedLength > lengthBudget && m_PlannedLength > 0.01f)
			{
				_result.Success = false;
				_result.Note = $"Planned length {m_PlannedLength:F1}m exceeds budget {lengthBudget:F1}m";
				return;
			}

			if (m_MaxCrossTrack > 1f)
			{
				_result.Success = false;
				_result.Note = $"Max cross-track {m_MaxCrossTrack:F2}m > 1m";
			}
		}

		private static float ComputeLengthBudget(TestCase _test, float _turnRadius)
		{
			float d = _test.Distance;
			float r = Mathf.Max(1f, _turnRadius);
			if (_test.ManeuverType == TestManeuverType.Forward &&
			    (Mathf.Abs(_test.DirectionDeg) <= 15f || Mathf.Abs(_test.DirectionDeg - 360f) <= 15f))
				return d * 1.1f + 0.1f;
			if (!_test.HasHeading)
				return d + r * 2.5f + 2f;
			return d + r * 3f + 2f;
		}

		private const float c_TurnAroundSwingDeg = 110f;

		private static float FlatDistanceXZ(Vector3 _a, Vector3 _b)
		{
			_a.y = 0f;
			_b.y = 0f;
			return Vector3.Distance(_a, _b);
		}

		private static float TrajectoryYawSwing(VehicleTrajectory _t)
		{
			if (_t == null || !_t.IsValid || _t.PointCount < 2)
				return 0f;
			return Mathf.Abs(Mathf.DeltaAngle(_t.Points[0].YawDegrees, _t.Points[_t.PointCount - 1].YawDegrees));
		}

		private static string InferManeuverFamily(
			VehicleTrajectory _traj,
			TrajectoryGear _firstGear,
			Maneuver _maneuver = null)
		{
			if ((_traj == null || !_traj.IsValid) &&
			    _maneuver != null &&
			    _maneuver.Type == VehicleManeuverType.TurnAround)
				return "TurnAround";

			if (_traj != null && _traj.IsValid)
			{
				string reason = _traj.DebugReason ?? string.Empty;
				if (reason.Contains("two-stage") || reason.Contains("three-point") ||
				    reason.Contains("one-cusp") || reason.Contains("rev-staging") ||
				    reason.StartsWith("rs-") || reason.Contains("merged"))
					return "Reposition";

				if (_traj.GearSegmentCount > 1)
					return "Reposition";

				if (!string.IsNullOrEmpty(reason))
				{
					if (reason.StartsWith("straight-rev"))
						return "Reverse";
					if (reason.StartsWith("straight-fwd"))
						return "Forward";
				}

				float swing = TrajectoryYawSwing(_traj);
				if (swing >= c_TurnAroundSwingDeg)
					return "TurnAround";
			}

			return _firstGear == TrajectoryGear.Reverse ? "Reverse" : "Forward";
		}

		private static bool MatchesExpectedFamily(string _expected, string _actual)
		{
			if (string.IsNullOrEmpty(_expected))
				return true;

			string[] parts = _expected.Split('|');
			for (int i = 0; i < parts.Length; i++)
			{
				string part = parts[i].Trim();
				if (string.Equals(part, _actual, StringComparison.OrdinalIgnoreCase))
					return true;
			}

			return false;
		}

		private void LogLine(string _text) => WriteLog($"[{DateTime.Now:HH:mm:ss.fff}] {_text}");

		private void WriteLog(string _text)
		{
			m_LogWriter?.WriteLine(_text);
			m_LogLinesSinceFlush++;
			if (m_LogLinesSinceFlush >= c_LogFlushBatch)
				FlushLog();
		}

		private void FlushLog()
		{
			m_LogWriter?.Flush();
			m_LogLinesSinceFlush = 0;
		}

		private void WriteBaselineComparison()
		{
			WriteBaselineComparisonFor("VehicleTest_20260805_215436.log", "215436 baseline");
			WriteBaselineComparisonFor("VehicleTest_20260805_162523.log", "162523 legacy");
		}

		private void WriteBaselineComparisonFor(string _fileName, string _label)
		{
			string testsDir = VehicleFileLog.GetTestsDirectory();
			string baselinePath = Path.Combine(testsDir, _fileName);
			if (!File.Exists(baselinePath))
				baselinePath = Path.Combine(Application.dataPath, "_Docs", _fileName);
			if (!File.Exists(baselinePath))
				return;

			var baseline = ParseBaselineResults(baselinePath);
			var sb = new StringBuilder();
			sb.AppendLine();
			sb.AppendLine($"--- BASELINE COMPARISON ({_label}: {_fileName}) ---");
			foreach (var r in m_Results)
			{
				string status = r.Success ? "PASS" : "FAIL";
				string oldStatus = baseline.TryGetValue(r.TestName, out var old)
					? old
					: "N/A";
				sb.AppendLine($"  {r.TestName,-32} | {oldStatus,-4} -> {status,-4} | {r.Note}");
			}

			string report = sb.ToString();
			WriteLog(report);
			FlushLog();

			string reportPath = Path.Combine(
				testsDir,
				$"ComparisonReport_{_label.Replace(' ', '_')}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
			File.WriteAllText(reportPath, report, Encoding.UTF8);
		}

		private static Dictionary<string, string> ParseBaselineResults(string _path)
		{
			var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (string line in File.ReadLines(_path))
			{
				if (!line.StartsWith("RESULT:", StringComparison.Ordinal))
					continue;

				int passIdx = line.IndexOf("PASS", StringComparison.Ordinal);
				int failIdx = line.IndexOf("FAIL", StringComparison.Ordinal);
				if (passIdx < 0 && failIdx < 0)
					continue;
			}

			bool inSummary = false;
			foreach (string line in File.ReadLines(_path))
			{
				if (line.Contains("TEST RESULTS SUMMARY"))
					inSummary = true;
				if (!inSummary)
					continue;
				if (!line.Contains("PASS") && !line.Contains("FAIL"))
					continue;
				if (line.Contains("Passed:"))
					break;

				int bar = line.IndexOf('|');
				if (bar < 0)
					continue;
				string left = line.Substring(0, bar).Trim();
				bool pass = left.Contains("PASS");
				int nameStart = line.IndexOf('|') + 1;
				int nameEnd = line.IndexOf('|', nameStart + 1);
				if (nameEnd <= nameStart)
					continue;
				string name = line.Substring(nameStart, nameEnd - nameStart).Trim();
				if (!string.IsNullOrEmpty(name))
					map[name] = pass ? "PASS" : "FAIL";
			}

			return map;
		}

		private static void Shuffle<T>(List<T> _list)
		{
			var rng = new System.Random();
			int n = _list.Count;
			while (n > 1) { n--; int k = rng.Next(n + 1); (_list[k], _list[n]) = (_list[n], _list[k]); }
		}

		private void OnDrawGizmosSelected()
		{
			if (m_TestCases.Count == 0) return;
			Vector3 origin = m_SpawnPoint != null ? m_SpawnPoint.position : m_StartPosition;
			foreach (var tc in m_TestCases)
			{
				Vector3 target = tc.GetTargetPosition(origin);
				Gizmos.color = tc.ManeuverType switch
				{
					TestManeuverType.Forward => Color.green,
					TestManeuverType.Reverse => Color.magenta,
					TestManeuverType.SideApproach => Color.yellow,
					TestManeuverType.UTurn => Color.red,
					TestManeuverType.KinematicsCalibration => Color.cyan,
					_ => Color.gray
				};
				Gizmos.DrawWireSphere(target, 0.3f);
				Gizmos.DrawLine(origin, target);
			}
		}

		private void OnGUI()
		{
			if (!Application.isPlaying) return;

			bool isV1 = m_TestVariant == VehicleTestVariant.Variant1_OpenField;
			bool isV5 = m_TestVariant == VehicleTestVariant.Variant5_KinematicsCalibration;
			string variantLabel = isV5 ? "V5 Calibration" : isV1 ? "V1 OpenField" : "V2 PoseArrival";
			float y = 10f + (m_PlatformId - 1) * 130f;
			if (y > 400f) y = 10f + ((m_PlatformId - 1) % 3) * 130f;
			GUILayout.BeginArea(new Rect(10, y, 560, 120));
			GUILayout.Box($"Test Platform P{m_PlatformId} | {variantLabel} | {name}");
			int done = m_Results.Count;
			int pass = 0;
			foreach (var r in m_Results)
				if (r.Success) pass++;
			GUILayout.Label($"Phase: {m_Phase} | test {m_CurrentTestIndex + 1}/{m_TestCases.Count} | {pass}/{done} passed");
			if (m_Navigation != null)
				GUILayout.Label($"FSM={m_Navigation.DriverState} | {m_Navigation.ActivePlanReason}");
			else if (m_CalibrationSession != null)
				GUILayout.Label($"Calib phase={m_CalibrationSession.Phase}");
			GUILayout.EndArea();
		}
	}
}
