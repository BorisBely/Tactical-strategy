using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Session debug ingest for editor-time tactical wiring. Not production logging.
/// </summary>
internal static class AgentDebugNdjson
{
	#region Constants
	private const string c_SessionId = "6642e0";
	private const string c_LogPath = @"d:\UnityProjects\My project 001\debug-6642e0.log";
	#endregion

	#region Public Methods
	public static void Write(
		string _hypothesisId,
		string _location,
		string _message,
		string _data)
	{
		try
		{
			long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			var line = new StringBuilder(256);
			line.Append("{\"sessionId\":\"").Append(c_SessionId).Append("\"");
			line.Append(",\"hypothesisId\":\"").Append(Esc(_hypothesisId)).Append("\"");
			line.Append(",\"location\":\"").Append(Esc(_location)).Append("\"");
			line.Append(",\"message\":\"").Append(Esc(_message)).Append("\"");
			line.Append(",\"data\":").Append(string.IsNullOrEmpty(_data) ? "{}" : _data);
			line.Append(",\"timestamp\":").Append(now.ToString(CultureInfo.InvariantCulture));
			line.Append("}\n");
			File.AppendAllText(c_LogPath, line.ToString());
		}
		catch (Exception)
		{
			// Ignore debug I/O.
		}
	}
	#endregion

	#region Private Methods
	private static string Esc(string _value)
	{
		if (string.IsNullOrEmpty(_value))
			return "";
		return _value.Replace("\\", "\\\\").Replace("\"", "\\\"");
	}
	#endregion
}
