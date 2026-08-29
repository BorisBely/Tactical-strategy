#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Vision freeze / AI handoff check. Does not retune numbers.
/// Tools/Tests/Archive/Vision/Verify Vision Freeze
/// </summary>
public static class VisionFreezeVerify
{
	[MenuItem("Tools/Tests/Archive/Vision/Verify Vision Freeze", false, 172)]
	public static void RunFromMenu()
	{
		VisionFreezeBaseline.ReportResult result = VisionFreezeBaseline.BuildReport();
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string latest = Path.Combine(dir, "VisionFreeze_LAST.txt");
		File.WriteAllText(latest, result.Body, Encoding.UTF8);
		int resultAt = result.Body.LastIndexOf("RESULT=", StringComparison.Ordinal);
		string resultLine = resultAt >= 0 ? result.Body.Substring(resultAt).Trim() : "RESULT=UNKNOWN";
		Debug.Log($"[VisionFreezeVerify] wrote {latest} {resultLine}\n{result.Body}");
	}
}
#endif
