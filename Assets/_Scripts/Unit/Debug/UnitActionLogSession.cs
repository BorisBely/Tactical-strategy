using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Owns one Play-session folder under <c>Assets/_Docs/Logs/Runtime/Infantry_*</c>.
/// File-only. Closed on Play exit / domain reload.
/// </summary>
public static class UnitActionLogSession
{
	#region Constants
	private const int c_FlushBatch = 32;
	private const float c_ContinuousMoveEpsilon = 0.75f;
	#endregion

	#region Private Fields
	private static bool s_Enabled;
	private static string s_Folder;
	private static StreamWriter s_IndexWriter;
	private static StreamWriter s_TimelineWriter;
	private static readonly Dictionary<EntityId, StreamWriter> s_Writers = new Dictionary<EntityId, StreamWriter>(32);
	private static readonly Dictionary<EntityId, Vector3> s_LastMoveDest = new Dictionary<EntityId, Vector3>(32);
	private static readonly HashSet<EntityId> s_Registered = new HashSet<EntityId>();
	private static int s_LinesSinceFlush;
	private static GameObject s_Host;
	private static bool s_Closing;
	#endregion

	#region Public Properties
	public static bool IsEnabled => s_Enabled && !string.IsNullOrEmpty(s_Folder);
	public static string Folder => s_Folder;
	#endregion

	#region Unity Hooks
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics()
	{
		Close();
		s_Enabled = false;
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void BeforeSceneLoad()
	{
		if (!Application.isPlaying)
			return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		s_Enabled = true;
		EnsureFolder();
		EnsureHost();
#else
		s_Enabled = false;
#endif
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void AfterSceneLoad()
	{
		if (!s_Enabled || !Application.isPlaying)
			return;

		BindExistingUnits();
	}
	#endregion

	#region Public Methods
	public static UnitActionLogBinder EnsureBinder(GameObject _unitRoot)
	{
		if (!s_Enabled || _unitRoot == null)
			return null;
		if (!_unitRoot.TryGetComponent(out UnitActionLogBinder binder))
			binder = _unitRoot.AddComponent<UnitActionLogBinder>();
		return binder;
	}

	public static void RegisterUnit(Component _unit)
	{
		if (!s_Enabled || _unit == null)
			return;
		EnsureFolder();
		Transform root = UnitActionLogIdentity.ResolveUnitRoot(_unit.transform);
		if (root == null)
			return;
		EntityId id = root.GetEntityId();
		if (s_Registered.Contains(id))
			return;
		s_Registered.Add(id);
		GetOrCreateWriter(root);
	}

	public static void WriteActor(Component _actor, string _channel, string _payload)
	{
		if (!s_Enabled || _actor == null)
			return;
		EnsureFolder();
		Transform root = UnitActionLogIdentity.ResolveUnitRoot(_actor.transform);
		if (root == null)
			return;
		StreamWriter writer = GetOrCreateWriter(root);
		if (writer == null)
			return;
		WriteLine(writer, FormatLine(_channel, _payload));
	}

	public static void WriteTimeline(string _channel, string _payload)
	{
		if (!s_Enabled)
			return;
		EnsureFolder();
		if (s_TimelineWriter == null)
			return;
		WriteLine(s_TimelineWriter, FormatLine(_channel, _payload));
	}

	public static bool ShouldLogMove(Component _actor, Vector3 _dest, bool _continuous)
	{
		if (!s_Enabled || _actor == null)
			return false;
		Transform root = UnitActionLogIdentity.ResolveUnitRoot(_actor.transform);
		if (root == null)
			return true;
		EntityId id = root.GetEntityId();
		if (!_continuous)
		{
			s_LastMoveDest[id] = _dest;
			return true;
		}

		if (!s_LastMoveDest.TryGetValue(id, out Vector3 last) ||
		    (last - _dest).sqrMagnitude >= c_ContinuousMoveEpsilon * c_ContinuousMoveEpsilon)
		{
			s_LastMoveDest[id] = _dest;
			return true;
		}

		return false;
	}

	public static void Close()
	{
		if (s_Closing)
			return;
		s_Closing = true;
		foreach (KeyValuePair<EntityId, StreamWriter> pair in s_Writers)
			CloseWriter(pair.Value);
		s_Writers.Clear();
		CloseWriter(s_IndexWriter);
		s_IndexWriter = null;
		CloseWriter(s_TimelineWriter);
		s_TimelineWriter = null;
		s_LastMoveDest.Clear();
		s_Registered.Clear();
		s_Folder = null;
		s_LinesSinceFlush = 0;
		s_Enabled = false;
		UnitActionLogIdentity.ResetStatics();
		if (s_Host != null)
		{
			GameObject host = s_Host;
			s_Host = null;
			UnityEngine.Object.Destroy(host);
		}
		s_Closing = false;
	}

	public static void AppendIndex(string _line)
	{
		if (s_IndexWriter == null)
			return;
		WriteLine(s_IndexWriter, _line);
		try
		{
			s_IndexWriter.Flush();
		}
		catch
		{
			// ignore
		}
	}
	#endregion

	#region Private Methods
	private static void EnsureFolder()
	{
		if (!string.IsNullOrEmpty(s_Folder))
			return;

		try
		{
			string runtime = Path.Combine(Application.dataPath, "_Docs", "Logs", "Runtime");
			Directory.CreateDirectory(runtime);
			string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
			s_Folder = Path.Combine(runtime, "Infantry_" + stamp);
			Directory.CreateDirectory(s_Folder);
			Directory.CreateDirectory(Path.Combine(s_Folder, "Player"));
			Directory.CreateDirectory(Path.Combine(s_Folder, "Enemy"));
			Directory.CreateDirectory(Path.Combine(s_Folder, "Neutral"));
			Directory.CreateDirectory(Path.Combine(s_Folder, "Other"));

			s_IndexWriter = new StreamWriter(Path.Combine(s_Folder, "_index.txt"), false, Encoding.UTF8)
			{
				AutoFlush = false
			};
			s_IndexWriter.WriteLine("# Infantry action log session " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
			s_IndexWriter.WriteLine("# folder=" + s_Folder);
			s_IndexWriter.WriteLine("# columns: slot team look callsign iid go file ai");
			s_IndexWriter.Flush();

			s_TimelineWriter = new StreamWriter(Path.Combine(s_Folder, "_timeline.log"), false, Encoding.UTF8)
			{
				AutoFlush = false
			};
			s_TimelineWriter.WriteLine("# Cross-unit timeline. Per-unit files have the full chain.");
			s_TimelineWriter.Flush();
		}
		catch
		{
			s_Enabled = false;
			s_Folder = null;
		}
	}

	private static void EnsureHost()
	{
		if (s_Host != null)
			return;
		s_Host = new GameObject("UnitActionLogSessionHost");
		UnityEngine.Object.DontDestroyOnLoad(s_Host);
		s_Host.hideFlags = HideFlags.HideAndDontSave;
		s_Host.AddComponent<UnitActionLogSessionHost>();
	}

	private static void BindExistingUnits()
	{
#if UNITY_2023_1_OR_NEWER
		UnitVision[] visions = UnityEngine.Object.FindObjectsByType<UnitVision>(FindObjectsInactive.Exclude);
#else
		UnitVision[] visions = UnityEngine.Object.FindObjectsOfType<UnitVision>();
#endif
		for (int i = 0; i < visions.Length; i++)
		{
			if (visions[i] != null)
				EnsureBinder(visions[i].gameObject);
		}
	}

	private static StreamWriter GetOrCreateWriter(Transform _unitRoot)
	{
		EntityId id = _unitRoot.GetEntityId();
		if (s_Writers.TryGetValue(id, out StreamWriter existing) && existing != null)
			return existing;

		try
		{
			UnitTeamId teamId = UnitTeamId.Neutral;
			if (_unitRoot.TryGetComponent(out UnitTeam team) && team != null)
				teamId = team.Team;
			else
				teamId = (UnitTeamId)(-1);

			string slot = UnitActionLogIdentity.Slot(_unitRoot);
			string callsign = UnitActionLogIdentity.Callsign(_unitRoot);
			string folder = teamId == (UnitTeamId)(-1)
				? "Other"
				: UnitActionLogIdentity.TeamFolder(teamId);
			string fileName = slot + "_" + UnitActionLogIdentity.SanitizeFileName(callsign) + ".log";
			string path = Path.Combine(s_Folder, folder, fileName);
			var writer = new StreamWriter(path, false, Encoding.UTF8) { AutoFlush = false };
			writer.WriteLine("# Infantry unit log " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
			writer.WriteLine("# slot=" + slot + " iid=" + id + " go=" + _unitRoot.name + " callsign=" + callsign);
			s_Writers[id] = writer;
			s_Registered.Add(id);

			string look = "?";
			if (_unitRoot.TryGetComponent(out VisualIdentityEvidence evidence) && evidence != null)
				look = evidence.PrimaryAffiliation.ToString();
			bool hasAi = _unitRoot.GetComponent<UnitAIController>() != null;
			string relative = folder + "/" + fileName;
			string teamLabel = teamId == (UnitTeamId)(-1) ? "None" : teamId.ToString();
			AppendIndex(
				slot + "  team=" + teamLabel +
				" look=" + look +
				" callsign=" + callsign +
				" iid=" + id +
				" go=" + _unitRoot.name +
				" file=" + relative +
				" ai=" + (hasAi ? "UnitAIController" : "none"));
			return writer;
		}
		catch
		{
			return null;
		}
	}

	private static string FormatLine(string _channel, string _payload)
	{
		string channel = string.IsNullOrEmpty(_channel) ? "?" : _channel;
		if (channel.Length < 6)
			channel = channel.PadRight(6);
		return UnitActionLog.TimeNow() + "  " + channel + "  " + (_payload ?? string.Empty);
	}

	private static void WriteLine(StreamWriter _writer, string _line)
	{
		if (_writer == null)
			return;
		try
		{
			_writer.WriteLine(_line);
			s_LinesSinceFlush++;
			if (s_LinesSinceFlush < c_FlushBatch)
				return;
			s_LinesSinceFlush = 0;
			_writer.Flush();
		}
		catch
		{
			// ignore IO during teardown
		}
	}

	private static void CloseWriter(StreamWriter _writer)
	{
		if (_writer == null)
			return;
		try
		{
			_writer.Flush();
			_writer.Close();
		}
		catch
		{
			// ignore
		}
	}
	#endregion
}

[DisallowMultipleComponent]
internal sealed class UnitActionLogSessionHost : MonoBehaviour
{
	private void OnApplicationQuit()
	{
		UnitActionLogSession.Close();
	}

	private void OnDestroy()
	{
		UnitActionLogSession.Close();
	}
}
