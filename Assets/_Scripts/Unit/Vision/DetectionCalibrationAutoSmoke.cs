using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Diagnostic A–H detection calibration (math only). V1.8c Q→time is in the same report.
/// Writes Assets/_Docs/Logs/Tests/DetectionCalibration_LAST.txt.
/// Not a G-stage. Does not run on Play by default.
/// </summary>
[DefaultExecutionOrder(55)]
[DisallowMultipleComponent]
[RequireComponent(typeof(DetectionTestController))]
public sealed class DetectionCalibrationAutoSmoke : MonoBehaviour
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart = false;
	#endregion

	#region Unity Lifecycle
	private void Start()
	{
		if (m_RunOnStart)
			RunAndWrite(this);
	}
	#endregion

	#region Public Methods
	public static DetectionCalibrationScenarios.ReportResult RunAndWrite(UnityEngine.Object _logContext = null)
	{
		DetectionCalibrationScenarios.ReportResult result = DetectionCalibrationScenarios.BuildReport();
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string latest = Path.Combine(dir, "DetectionCalibration_LAST.txt");
		File.WriteAllText(latest, result.Body, Encoding.UTF8);
		string verdict = result.FailCount == 0 ? "PASS" : "FAIL";
		string line =
			$"[DetectionCalibrationAutoSmoke] wrote {latest} RESULT={verdict} pass={result.PassCount} fail={result.FailCount}";
		if (_logContext != null)
			Debug.Log(line, _logContext);
		else
			Debug.Log(line);
		return result;
	}
	#endregion
}
