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
        [SerializeField] private int m_LogEveryNFrames = 15;

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
        private Vector3 m_LastLoggedPos;
        private int m_FrameCounter;

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

            // Phase 1: Spawn
            m_Phase = Phase.Spawning;
            SpawnVehicle();
            yield return new WaitForSeconds(0.5f);

            // Wait for ready
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

            // Log vehicle parameters
            var p = m_Navigation.Context?.Params;
            if (p.HasValue)
                LogFrame($"VEHICLE_PARAMS | wheelBase={p.Value.WheelBase:F2} length={p.Value.Length:F2} width={p.Value.Width:F2} " +
                    $"turnRadius={p.Value.MinTurningRadius:F2} maxSteer={p.Value.MaxSteeringAngleDeg:F0}° " +
                    $"maxFwd={p.Value.MaxForwardSpeedKmh:F0}km/h maxRev={p.Value.MaxReverseSpeedKmh:F0}km/h");

            // Phase 2: Issue command
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
            m_LastLoggedPos = result.StartPos;
            m_FrameCounter = 0;

            LogTestStart(_test, result);

            float elapsed = 0f;
            float stagnantTime = 0f;
            float lastRemainingDist = float.MaxValue;

            while (elapsed < m_TestTimeout && !m_DestinationReached && !m_TestFailed)
            {
                elapsed = Time.time - m_TestStartTime;
                yield return new WaitForFixedUpdate();
                m_FrameCounter++;

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

                // Per-frame state logging every N frames
                if (m_FrameCounter % m_LogEveryNFrames == 0 ||
                    planReason != m_LastPlanReason ||
                    fsmState != m_LastFsmState ||
                    mode != m_LastMode)
                {
                    LogFrame($"t={elapsed:F1}s | state={fsmState} | mode={mode} | maneuver={maneuverType} | " +
                        $"pos=({pos.x:F2},{pos.z:F2}) | dist={currentDist:F2}m | remaining={remaining:F1}m | " +
                        $"speed={speed:F1}km/h | curv={curvature:F4} | plan={planReason}");

                    // Log waypoints if available
                    if (maneuver != null && maneuver.Waypoints != null && maneuver.Waypoints.Count > 0)
                    {
                        var wps = new StringBuilder("  waypoints: ");
                        for (int i = 0; i < Mathf.Min(maneuver.Waypoints.Count, 4); i++)
                            wps.Append($"({maneuver.Waypoints[i].x:F2},{maneuver.Waypoints[i].z:F2}) ");
                        LogFrame(wps.ToString());
                    }

                    m_LastLoggedPos = pos;
                }

                m_LastPlanReason = planReason;
                m_LastMode = mode;
                m_LastFsmState = fsmState;

                // Completion: Holding or Idle
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

                // Empty plan detection
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

                // Stagnation detection
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
            LogFrame($"RESULT | success={result.Success} time={result.CompletionTime:F1}s finalDist={result.FinalDistance:F2}m " +
                $"finalSpeed={result.FinalSpeed:F1}km/h mode={result.ChosenMode} note={result.Note}");
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

            // Group by type
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
                LogFrame($"BRAIN | ControlActive={m_Brain.ControlActive} Engine={m_Brain.EngineRunning} CanDrive={m_Brain.CanDrive} Ready={m_Brain.EngineReady}");
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
                    LogFrame($"BRAIN | waiting engine ready... {(m_Brain.EngineReady ? "YES" : "no")}");
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
            WriteLog("              VEHICLE TEST PLATFORM LOG                       ");
            WriteLog("==============================================================");
            WriteLog($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            WriteLog($"Test cases: {m_TestCases.Count}");
            WriteLog($"Timeout per test: {m_TestTimeout}s");
            WriteLog($"Log interval: every {m_LogEveryNFrames} frames + on state change");
            WriteLog("==============================================================");
            WriteLog("");
        }

        private void LogTestStart(TestCase _test, TestResult _result)
        {
            WriteLog("");
            WriteLog("--------------------------------------------------------------");
            WriteLog($"TEST #{m_CurrentTestIndex + 1}: {_test.Name}");
            WriteLog($"  Type: {_test.ManeuverType} | Direction: {_test.DirectionDeg:F0}° | Distance: {_test.Distance:F0}m");
            WriteLog($"  Start:  ({_result.StartPos.x:F3}, {_result.StartPos.y:F3}, {_result.StartPos.z:F3})");
            WriteLog($"  Target: ({_result.TargetPos.x:F3}, {_result.TargetPos.y:F3}, {_result.TargetPos.z:F3})");
            WriteLog($"  Heading required: {(_test.HasHeading ? $"yes ({_test.HeadingYaw:F0}°)" : "no")}");
            WriteLog("--------------------------------------------------------------");
        }

        private void LogTestEnd(TestResult _result)
        {
            WriteLog("--------------------------------------------------------------");
            WriteLog($"RESULT: {(_result.Success ? "PASS" : "FAIL")} | time={_result.CompletionTime:F1}s | finalDist={_result.FinalDistance:F3}m | speed={_result.FinalSpeed:F1}km/h");
            WriteLog($"  Chosen mode: {_result.ChosenMode} | Plan: {_result.PlanReason}");
            if (!string.IsNullOrEmpty(_result.Note))
                WriteLog($"  Note: {_result.Note}");
            WriteLog("--------------------------------------------------------------");
        }

        private void LogFrame(string _text)
        {
            WriteLog($"  [{DateTime.Now:HH:mm:ss.fff}] {_text}");
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

            GUILayout.BeginArea(new Rect(10, 10, 450, 340));
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
                if (m_Brain != null)
                    GUILayout.Label($"Brain: active={m_Brain.ControlActive} eng={m_Brain.EngineRunning} ready={m_Brain.EngineReady} canDrive={m_Brain.CanDrive}");
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
