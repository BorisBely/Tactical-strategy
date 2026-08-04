using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CombatVehicleSystem;
using UnityEngine;

namespace VehicleNavigation
{
    public enum TestManeuverType
    {
        Forward,
        Reverse,
        SideApproach,
        UTurn,
        All
    }

    [Serializable]
    public struct TestCase
    {
        public string Name;
        public TestManeuverType ManeuverType;
        public float DirectionDeg;
        public float Distance;
        public bool HasHeading;
        public float HeadingYaw;

        public Vector3 GetTargetPosition(Vector3 _origin)
        {
            float rad = DirectionDeg * Mathf.Deg2Rad;
            return _origin + new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * Distance;
        }
    }

    public sealed class VehicleTestPlatform : MonoBehaviour
    {
        [Header("Vehicle")]
        [SerializeField] private GameObject m_VehiclePrefab;
        [SerializeField] private Transform m_SpawnPoint;

        [Header("Test Settings")]
        [SerializeField] private float m_TestTimeout = 30f;
        [SerializeField] private float m_InterTestDelay = 2f;
        [SerializeField] private bool m_RespawnBetweenTests = true;
        [SerializeField] private Vector3 m_StartPosition = Vector3.zero;
        [SerializeField] private float m_StartYaw;
        [SerializeField] private int m_LogEveryNFrames = 1;

        [Header("Camera")]
        [SerializeField] private Camera m_TestCamera;
        [SerializeField] private Vector3 m_CameraOffset = new Vector3(0f, 25f, -15f);
        [SerializeField] private bool m_UseOverheadCamera = true;

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
        private GameObject m_VehicleInstance;
        private readonly List<TestCase> m_TestCases = new List<TestCase>();
        private readonly List<TestResult> m_Results = new List<TestResult>();
        private StreamWriter m_LogWriter;

        private float m_TestStartTime;
        private bool m_DestinationReached;
        private bool m_TestFailed;
        private string m_LastPlanReason;
        private VehicleDrivingMode m_LastMode;
        private DriverFSM.State m_LastFsmState;
        private string m_LastManeuverType;
        private Vector3 m_LastLoggedPos;
        private int m_FrameCounter;
        private int m_TestFrameCounter;

        private enum Phase { Idle, Spawning, WaitingReady, Driving, Arrived, Respawning, Completed }
        private Phase m_Phase;

        [Serializable]
        public struct TestResult
        {
            public string TestName;
            public TestManeuverType ManeuverType;
            public Vector3 StartPos;
            public Vector3 TargetPos;
            public float Distance;
            public float DirectionDeg;
            public VehicleDrivingMode ChosenMode;
            public string PlanReason;
            public float CompletionTime;
            public float FinalDistance;
            public float FinalSpeed;
            public bool Success;
            public string Note;
        }

        private void Awake()
        {
            GenerateTestCases();
            SetupCamera();
            if (m_AutoStart)
                StartCoroutine(RunAllTests());
        }

        private void SetupCamera()
        {
            if (!m_UseOverheadCamera) return;
            if (m_VehiclePrefab == null) return;

            if (m_TestCamera == null)
                m_TestCamera = Camera.main;

            if (m_TestCamera == null) return;

            Vector3 origin = m_SpawnPoint != null ? m_SpawnPoint.position : m_StartPosition;
            m_TestCamera.transform.position = origin + m_CameraOffset;
            m_TestCamera.transform.LookAt(origin);
        }

        private void GenerateTestCases()
        {
            m_TestCases.Clear();
            float[] distances = { 2f, 5f, 10f, 20f };
            float[] directions = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };

            foreach (float dist in distances)
            {
                foreach (float dir in directions)
                {
                    float absAngle = Mathf.Abs(Mathf.DeltaAngle(0f, dir));
                    TestManeuverType type;

                    if (absAngle <= 60f)
                        type = TestManeuverType.Forward;
                    else if (absAngle >= 120f)
                        type = dist <= 5f ? TestManeuverType.Reverse : TestManeuverType.UTurn;
                    else
                        type = TestManeuverType.SideApproach;

                    if (!ShouldRunType(type)) continue;

                    m_TestCases.Add(new TestCase
                    {
                        Name = $"{type}_{dir:F0}deg_{dist:F0}m",
                        ManeuverType = type,
                        DirectionDeg = dir,
                        Distance = dist,
                        HasHeading = type == TestManeuverType.Forward && dist > 5f,
                        HeadingYaw = dir
                    });
                }
            }

            if (m_RunUTurnTests)
            {
                foreach (float dist in new[] { 3f, 6f, 10f })
                {
                    m_TestCases.Add(new TestCase
                    {
                        Name = $"UTurn_explicit_{dist:F0}m",
                        ManeuverType = TestManeuverType.UTurn,
                        DirectionDeg = 180f,
                        Distance = dist,
                        HasHeading = false
                    });
                }
            }

            if (m_ShuffleTests)
                Shuffle(m_TestCases);

            Debug.Log($"[TestPlatform] Generated {m_TestCases.Count} test cases");
        }

        private bool ShouldRunType(TestManeuverType _type)
        {
            return _type switch
            {
                TestManeuverType.Forward => m_RunForwardTests,
                TestManeuverType.Reverse => m_RunReverseTests,
                TestManeuverType.SideApproach => m_RunSideTests,
                TestManeuverType.UTurn => m_RunUTurnTests,
                _ => true
            };
        }

        private System.Collections.IEnumerator RunAllTests()
        {
            OpenLogFile();

            for (m_CurrentTestIndex = 0; m_CurrentTestIndex < m_TestCases.Count; m_CurrentTestIndex++)
            {
                yield return StartCoroutine(RunSingleTest(m_TestCases[m_CurrentTestIndex]));
                if (!m_LoopTests && m_CurrentTestIndex >= m_TestCases.Count - 1)
                    break;
            }

            if (m_LoopTests)
            {
                m_CurrentTestIndex = 0;
                StartCoroutine(RunAllTests());
            }
            else
            {
                yield return StartCoroutine(FinishAllTests());
            }
        }

        private System.Collections.IEnumerator RunSingleTest(TestCase _test)
        {
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

            var p = m_Navigation.Context?.Params;
            if (p.HasValue)
                LogVehicleParams(p.Value);

            m_Phase = Phase.Driving;
            if (_test.HasHeading)
                m_Navigation.SetDestination(target, _test.HeadingYaw, VehicleSpeedMode.Medium);
            else
                m_Navigation.SetDestination(target, VehicleSpeedMode.Medium);

            m_DestinationReached = false;
            m_TestFailed = false;
            m_TestStartTime = Time.time;
            m_LastPlanReason = "";
            m_LastMode = VehicleDrivingMode.Forward;
            m_LastFsmState = DriverFSM.State.Idle;
            m_LastManeuverType = "";
            m_LastLoggedPos = result.StartPos;
            m_FrameCounter = 0;
            m_TestFrameCounter = 0;

            LogTestStart(_test, result);
            LogFrameHeader();

            float elapsed = 0f;
            float stagnantTime = 0f;
            float lastRemainingDist = float.MaxValue;

            while (elapsed < m_TestTimeout && !m_DestinationReached && !m_TestFailed)
            {
                elapsed = Time.time - m_TestStartTime;
                yield return new WaitForFixedUpdate();
                m_FrameCounter++;
                m_TestFrameCounter++;

                if (m_Navigation == null || m_VehicleInstance == null) break;

                Vector3 pos = m_Navigation.transform.position;
                float currentDist = Vector3.Distance(pos, target);
                var fsmState = m_Navigation.DriverState;
                float remaining = m_Navigation.Context?.RemainingDistance ?? 0f;
                float speed = m_Navigation.CurrentSpeed * 3.6f;
                var mode = m_Navigation.ActivePlan.DrivingMode;
                var planReason = m_Navigation.ActivePlanReason;
                var maneuver = m_Navigation.CurrentManeuver;
                string maneuverType = maneuver?.Type.ToString() ?? "-";
                float curvature = m_Navigation.Context?.CurrentCurvature ?? 0f;

                if (m_TestFrameCounter % m_LogEveryNFrames == 0)
                    LogPerFrameData(pos, target, currentDist, elapsed);

                if (maneuverType != m_LastManeuverType)
                {
                    LogManeuverChange(maneuver);
                    m_LastManeuverType = maneuverType;
                }

                m_LastPlanReason = planReason;
                m_LastMode = mode;
                m_LastFsmState = fsmState;

                if (fsmState == DriverFSM.State.Holding || fsmState == DriverFSM.State.Idle)
                {
                    m_DestinationReached = true;
                    result.FinalDistance = currentDist;
                    result.FinalSpeed = speed;
                    result.ChosenMode = mode;
                    result.PlanReason = planReason;
                    result.CompletionTime = elapsed;
                    result.Success = currentDist < 1.5f;
                    if (!result.Success)
                        result.Note = $"Stopped at {currentDist:F1}m from target";
                    break;
                }

                if (fsmState == DriverFSM.State.Driving && elapsed > 3f &&
                    (string.IsNullOrEmpty(planReason) || planReason == "empty"))
                {
                    m_TestFailed = true;
                    result.FinalDistance = currentDist;
                    result.CompletionTime = elapsed;
                    result.Success = false;
                    result.Note = "Plan empty — navigation not executing";
                    break;
                }

                if (remaining < lastRemainingDist - 0.05f)
                {
                    stagnantTime = 0f;
                    lastRemainingDist = remaining;
                }
                else if (elapsed > 3f)
                {
                    stagnantTime += Time.fixedDeltaTime;
                    if (stagnantTime > 5f)
                    {
                        m_TestFailed = true;
                        result.FinalDistance = currentDist;
                        result.ChosenMode = mode;
                        result.PlanReason = planReason;
                        result.CompletionTime = elapsed;
                        result.Success = false;
                        result.Note = $"Stagnant at {currentDist:F1}m (remaining={remaining:F1}m)";
                        break;
                    }
                }
            }

            if (elapsed >= m_TestTimeout && !m_DestinationReached && !m_TestFailed)
            {
                result.Success = false;
                result.Note = "Timeout";
                result.CompletionTime = elapsed;
                result.FinalDistance = Vector3.Distance(m_Navigation.transform.position, target);
                if (m_Navigation != null)
                {
                    result.ChosenMode = m_Navigation.ActivePlan.DrivingMode;
                    result.PlanReason = m_Navigation.ActivePlanReason;
                }
            }

            m_Phase = Phase.Arrived;
            LogTestEnd(result);

            yield return new WaitForSeconds(m_InterTestDelay);

            m_Phase = Phase.Respawning;
            if (m_RespawnBetweenTests)
            {
                DespawnVehicle();
                yield return new WaitForSeconds(0.3f);
            }

            m_Results.Add(result);
        }

        private System.Collections.IEnumerator FinishAllTests()
        {
            m_Phase = Phase.Completed;

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("==============================================================");
            sb.AppendLine("                     TEST RESULTS SUMMARY                     ");
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
                sb.AppendLine($"  {(r.Success ? "PASS" : "FAIL")} | {r.TestName,-30} | mode={r.ChosenMode,-10} | dist={r.Distance:F0}m dir={r.DirectionDeg:F0}° | time={r.CompletionTime:F1}s | finalDist={r.FinalDistance:F2}m | {r.Note}");
            }
            sb.AppendLine("--------------------------------------------------------------");
            sb.AppendLine($"Passed: {passed}/{m_Results.Count}  Failed: {m_Results.Count - passed}/{m_Results.Count}");

            string summary = sb.ToString();
            Debug.Log(summary);
            WriteLog(summary);

            m_LogWriter?.Flush();
            m_LogWriter?.Close();

            yield return null;
        }

        private void SpawnVehicle()
        {
            if (m_VehiclePrefab == null)
            {
                Debug.LogError("[TestPlatform] Vehicle prefab not assigned!");
                return;
            }

            Vector3 spawnPos = m_SpawnPoint != null ? m_SpawnPoint.position : m_StartPosition;
            Quaternion spawnRot = Quaternion.Euler(0f, m_StartYaw, 0f);

            m_VehicleInstance = Instantiate(m_VehiclePrefab, spawnPos, spawnRot);

            m_Navigation = m_VehicleInstance.GetComponent<VehicleNavigation>();
            if (m_Navigation == null)
                m_Navigation = m_VehicleInstance.GetComponentInChildren<VehicleNavigation>();

            m_Brain = m_VehicleInstance.GetComponent<VehicleBrain>();

            if (m_Brain != null)
            {
                m_Brain.SetControlActive(true);
                m_Brain.StartEngine();
                LogLine($"BRAIN | ControlActive={m_Brain.ControlActive} Engine={m_Brain.EngineRunning} CanDrive={m_Brain.CanDrive} Ready={m_Brain.EngineReady}");
            }

            if (m_Navigation != null)
            {
                m_Navigation.DestinationReached += OnDestinationReached;
            }
            else
            {
                Debug.LogError("[TestPlatform] VehicleNavigation component not found on prefab!");
            }
        }

        private void DespawnVehicle()
        {
            if (m_Navigation != null)
            {
                m_Navigation.DestinationReached -= OnDestinationReached;
                m_Navigation.Stop();
            }

            if (m_VehicleInstance != null)
            {
                Destroy(m_VehicleInstance);
                m_VehicleInstance = null;
            }

            m_Navigation = null;
            m_Brain = null;
        }

        private bool IsVehicleReady()
        {
            if (m_Navigation == null || m_VehicleInstance == null) return false;
            if (m_Brain != null && !m_Brain.CanDrive)
            {
                if (m_Brain.ControlActive && m_Brain.EngineRunning && !m_Brain.EngineReady)
                    LogLine($"BRAIN | waiting engine ready... {(m_Brain.EngineReady ? "YES" : "no")}");
                return false;
            }
            return true;
        }

        private void OnDestinationReached() { m_DestinationReached = true; }

        private void OpenLogFile()
        {
            string dir = Path.Combine(Application.dataPath, "_Docs");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"VehicleTest_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            m_LogWriter = new StreamWriter(path, false, Encoding.UTF8);
            WriteLog("==============================================================");
            WriteLog("              VEHICLE TEST PLATFORM — DETAILED LOG            ");
            WriteLog("==============================================================");
            WriteLog($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            WriteLog($"Test cases: {m_TestCases.Count}");
            WriteLog($"Timeout per test: {m_TestTimeout}s");
            WriteLog($"Log interval: every {m_LogEveryNFrames} frame(s)");
            WriteLog($"Columns: FRAME|TIME|FSM|MODE|PHASE|POS_X|POS_Z|YAW|SPD|SIGNED_SPD|VEL_SQ|REV|STOP|STUCK|AIR|UP|MANEUVER|MVR_IDX/N|REM_DIST|DST_TGT|CURV|DES_SPD|TGT_SPD|SPD_LIMIT|STOP_REASON|P_LAD|P_NEAR|P_TGT|P_CTE|P_RAW_CURV|P_CLAMP_CURV|P_PREV_CURV|P_ARR_S|P_LAUNCH|P_CAP|P_REV|P_WP_TTL|THR|STEER|BRAKE|PLAN");
            WriteLog("==============================================================");
            WriteLog("");
        }

        private void LogVehicleParams(VehicleParameters _p)
        {
            WriteLog("");
            WriteLog("--- VEHICLE PARAMETERS ---");
            WriteLog($"  wheelBase={_p.WheelBase:F2}m | length={_p.Length:F2}m | width={_p.Width:F2}m");
            WriteLog($"  turnRadius={_p.MinTurningRadius:F2}m | maxSteer={_p.MaxSteeringAngleDeg:F0}deg");
            WriteLog($"  maxFwd={_p.MaxForwardSpeedKmh:F0}km/h | maxRev={_p.MaxReverseSpeedKmh:F0}km/h");
            WriteLog($"  steerRate={_p.SteeringRateDegPerSec:F0}deg/s | hardBrake={_p.HardBrakeDecelMs2:F1}m/s^2");
            WriteLog("");
        }

        private void LogTestStart(TestCase _test, TestResult _result)
        {
            WriteLog("");
            WriteLog("==============================================================");
            WriteLog($"TEST #{m_CurrentTestIndex + 1}: {_test.Name}");
            WriteLog($"  Type: {_test.ManeuverType} | Dir: {_test.DirectionDeg:F0}deg | Dist: {_test.Distance:F0}m");
            WriteLog($"  Start:  ({_result.StartPos.x:F3}, {_result.StartPos.y:F3}, {_result.StartPos.z:F3})");
            WriteLog($"  Target: ({_result.TargetPos.x:F3}, {_result.TargetPos.y:F3}, {_result.TargetPos.z:F3})");
            WriteLog($"  Heading: {(_test.HasHeading ? $"yes ({_test.HeadingYaw:F0}deg)" : "no")}");
            WriteLog("==============================================================");
        }

        private void LogFrameHeader()
        {
            WriteLog("# FRAME | TIME | FSM | MODE | PHASE | POS_X | POS_Z | YAW | SPD | SIGNED_SPD | VEL_SQ | REV | STOP | STUCK | AIR | UP");
            WriteLog("#       | MANEUVER | MVR_IDX/N | REM_DIST | DST_TGT | CURV | DES_SPD | TGT_SPD | SPD_LIMIT | STOP_REASON");
            WriteLog("#       | P_LAD | P_NEAR | P_TGT | P_CTE | P_RAW_CURV | P_CLAMP_CURV | P_PREV_CURV | P_ARR_S | P_LAUNCH | P_CAP | P_REV | P_WP_TTL");
            WriteLog("#       | THR | STEER | BRAKE | PLAN");
        }

        private void LogPerFrameData(Vector3 _pos, Vector3 _target, float _distToTarget, float _elapsed)
        {
            if (m_Navigation == null) return;

            var ctx = m_Navigation.Context;
            var fb = ctx?.State ?? default;
            var plan = m_Navigation.ActivePlan;
            var maneuver = m_Navigation.CurrentManeuver;
            var pursuit = m_Navigation.PursuitDebug;
            var cmd = m_Navigation.LastCommand;
            var fsmState = m_Navigation.DriverState;

            float speedKmh = m_Navigation.CurrentSpeed * 3.6f;

            string maneuverType = maneuver?.Type.ToString() ?? "-";
            int maneuverIdx = ctx?.CurrentManeuverIndex ?? 0;
            int maneuverTotal = plan.Maneuvers?.Count ?? 0;
            string maneuverIdxStr = maneuverTotal > 0 ? $"{maneuverIdx + 1}/{maneuverTotal}" : "-/-";

            float remaining = ctx?.RemainingDistance ?? 0f;
            float curvature = ctx?.CurrentCurvature ?? 0f;
            float desiredSpeed = ctx?.DesiredSpeedKmh ?? 0f;
            float targetSpeed = ctx?.TargetSpeedKmh ?? 0f;

            string speedLimit = ctx != null ? ctx.ActiveLimit.Reason.ToString() : "-";
            string stopReason = ctx != null ? ctx.ActiveStopReason.ToString() : "-";

            var sb = new StringBuilder();
            sb.Append($"{m_TestFrameCounter}|{_elapsed:F3}|{fsmState}|{plan.DrivingMode}|{cmd.Phase}|");
            sb.Append($"{_pos.x:F2}|{_pos.z:F2}|{fb.Yaw:F1}|");
            sb.Append($"{speedKmh:F1}|{fb.SpeedSignedKmh:F1}|{fb.VelocitySqr:F2}|");
            sb.Append($"{(fb.IsReversing ? 1 : 0)}|{(fb.IsStopped ? 1 : 0)}|{(fb.IsStuck ? 1 : 0)}|{(fb.IsAirborne ? 1 : 0)}|{(fb.IsUpright ? 1 : 0)}|");
            sb.Append($"{maneuverType}|{maneuverIdxStr}|{remaining:F2}|{_distToTarget:F2}|");
            sb.Append($"{curvature:F4}|{desiredSpeed:F1}|{targetSpeed:F1}|{speedLimit}|{stopReason}|");

            sb.Append($"{pursuit.LookAheadDistance:F2}|{pursuit.NearestWaypointIndex}|{pursuit.LookAheadTargetIndex}|");
            sb.Append($"{pursuit.CrossTrackError:F3}|{pursuit.RawCurvature:F4}|{pursuit.ClampedCurvature:F4}|");
            sb.Append($"{pursuit.PreviewCurvature:F4}|{pursuit.ArrivalScale:F3}|{pursuit.LaunchRamp:F3}|");
            sb.Append($"{pursuit.CappedSpeedKmh:F1}|{(pursuit.IsReversing ? 1 : 0)}|{pursuit.TotalWaypoints}|");

            sb.Append($"{cmd.Throttle:F3}|{cmd.Steer:F3}|{cmd.BrakeMode}|{plan.Reason}");

            WriteLog(sb.ToString());
        }

        private void LogManeuverChange(Maneuver _maneuver)
        {
            if (_maneuver == null || _maneuver.Waypoints == null || _maneuver.Waypoints.Count == 0)
                return;

            var sb = new StringBuilder();
            sb.Append($"--- MANEUVER START: {_maneuver.Type} | allowReverse={_maneuver.AllowReverse} | speedScale={_maneuver.SpeedScale:F2} | isArrival={_maneuver.IsArrivalManeuver}");
            WriteLog(sb.ToString());

            var wps = new StringBuilder("    waypoints[" + _maneuver.Waypoints.Count + "]: ");
            int maxShow = Mathf.Min(_maneuver.Waypoints.Count, 20);
            for (int i = 0; i < maxShow; i++)
            {
                wps.Append($"({_maneuver.Waypoints[i].x:F2},{_maneuver.Waypoints[i].z:F2})");
                if (i < maxShow - 1) wps.Append(" ");
            }
            if (_maneuver.Waypoints.Count > 20)
                wps.Append($" ... (+{_maneuver.Waypoints.Count - 20} more)");
            WriteLog(wps.ToString());

            var corners = m_Navigation?.PathCorners;
            if (corners != null && corners.Count > 0)
            {
                var cps = new StringBuilder("    path_corners[" + corners.Count + "]: ");
                for (int i = 0; i < corners.Count; i++)
                {
                    cps.Append($"({corners[i].x:F2},{corners[i].z:F2})");
                    if (i < corners.Count - 1) cps.Append(" ");
                }
                WriteLog(cps.ToString());
            }

            var plan = m_Navigation?.ActivePlan;
            if (plan != null && plan.IsValid)
            {
                var pi = new StringBuilder("    plan_info: ");
                pi.Append($"mode={plan.DrivingMode} | reason={plan.Reason} | cost={plan.TotalCost:F1}");
                pi.Append($" | estDist={plan.EstimatedDistance:F1}m | revDist={plan.ReverseDistance:F1}m");
                pi.Append($" | turns={plan.TurnCount} | risk={plan.Risk:F2}");
                if (plan.Feasibility != null && !plan.Feasibility.IsValid)
                    pi.Append($" | feasibility=FAIL({plan.Feasibility.FailureReason})");
                WriteLog(pi.ToString());

                if (plan.Maneuvers != null && plan.Maneuvers.Count > 0)
                {
                    var ml = new StringBuilder("    plan_maneuvers: ");
                    for (int i = 0; i < plan.Maneuvers.Count; i++)
                        ml.Append($"[{i}]{plan.Maneuvers[i]?.Type} ");
                    WriteLog(ml.ToString());
                }
            }
        }

        private void LogTestEnd(TestResult _result)
        {
            WriteLog("--------------------------------------------------------------");
            WriteLog($"RESULT: {(_result.Success ? "PASS" : "FAIL")} | time={_result.CompletionTime:F1}s | finalDist={_result.FinalDistance:F3}m | speed={_result.FinalSpeed:F1}km/h");
            WriteLog($"  Chosen mode: {_result.ChosenMode} | Plan: {_result.PlanReason}");
            if (!string.IsNullOrEmpty(_result.Note))
                WriteLog($"  Note: {_result.Note}");
            WriteLog($"  Total test frames: {m_TestFrameCounter} (logged every {m_LogEveryNFrames})");
            WriteLog("--------------------------------------------------------------");
            WriteLog("");
        }

        private void LogLine(string _text)
        {
            WriteLog($"[{DateTime.Now:HH:mm:ss.fff}] {_text}");
        }

        private void WriteLog(string _text)
        {
            m_LogWriter?.WriteLine(_text);
            m_LogWriter?.Flush();
        }

        private void OnDrawGizmosSelected()
        {
            if (m_TestCases.Count == 0) return;
            Vector3 origin = m_SpawnPoint != null ? m_SpawnPoint.position : m_StartPosition;

            foreach (var tc in m_TestCases)
            {
                Vector3 target = tc.GetTargetPosition(origin);
                Color c = tc.ManeuverType switch
                {
                    TestManeuverType.Forward => Color.green,
                    TestManeuverType.Reverse => Color.magenta,
                    TestManeuverType.SideApproach => Color.yellow,
                    TestManeuverType.UTurn => Color.red,
                    _ => Color.gray
                };
                Gizmos.color = c;
                Gizmos.DrawWireSphere(target, 0.3f);
                Gizmos.DrawLine(origin, target);
            }

            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(origin, 0.5f);
            Vector3 fwd = Quaternion.Euler(0f, m_StartYaw, 0f) * Vector3.forward;
            Gizmos.DrawRay(origin, fwd * 3f);
        }

        private static void Shuffle<T>(List<T> _list)
        {
            var rng = new System.Random();
            int n = _list.Count;
            while (n > 1) { n--; int k = rng.Next(n + 1); (_list[k], _list[n]) = (_list[n], _list[k]); }
        }

        private void OnGUI()
        {
            if (!Application.isPlaying) return;

            GUILayout.BeginArea(new Rect(10, 10, 500, 400));
            GUILayout.Box($"Test Platform | Phase: {m_Phase} | Timeout: {m_TestTimeout}s");
            GUILayout.Label($"Test: {m_CurrentTestIndex + 1}/{m_TestCases.Count}");
            if (m_CurrentTestIndex < m_TestCases.Count)
                GUILayout.Label($"Current: {m_TestCases[m_CurrentTestIndex].Name}");

            if (m_Navigation != null && m_VehicleInstance != null)
            {
                GUILayout.Label($"FSM: {m_Navigation.DriverState} | Plan: {m_Navigation.ActivePlanReason}");
                GUILayout.Label($"Mode: {m_Navigation.ActivePlan.DrivingMode} | " +
                    $"Maneuver: {m_Navigation.CurrentManeuver?.Type.ToString() ?? "-"}");
                float dist = Vector3.Distance(m_Navigation.transform.position,
                    m_Navigation.Context?.Request.Destination ?? m_Navigation.transform.position);
                GUILayout.Label($"Dist: {dist:F1}m | Remaining: {m_Navigation.Context?.RemainingDistance ?? 0f:F1}m | Speed: {m_Navigation.CurrentSpeed * 3.6f:F1}km/h");
                GUILayout.Label($"Frame: {m_TestFrameCounter} | LogEvery: {m_LogEveryNFrames}");
                if (m_Brain != null)
                    GUILayout.Label($"Brain: active={m_Brain.ControlActive} eng={m_Brain.EngineRunning} ready={m_Brain.EngineReady} canDrive={m_Brain.CanDrive}");

                var pursuit = m_Navigation.PursuitDebug;
                GUILayout.Label($"Pursuit: LA={pursuit.LookAheadDistance:F1}m nearWP={pursuit.NearestWaypointIndex} tgtWP={pursuit.LookAheadTargetIndex} CTE={pursuit.CrossTrackError:F3} curv={pursuit.ClampedCurvature:F4}");
            }

            int p = 0; foreach (var r in m_Results) if (r.Success) p++;
            GUILayout.Label($"Results: {p}/{m_Results.Count} passed");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Skip Test")) m_TestFailed = true;
            if (GUILayout.Button("Stop All")) StopAllCoroutines();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
