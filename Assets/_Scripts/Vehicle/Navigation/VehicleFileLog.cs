using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// File-only vehicle logging. Test harness → <c>_Docs/Logs/Tests</c>;
	/// free-play vehicles → <c>_Docs/Logs/Runtime</c>. Never spams the Unity console.
	/// </summary>
	public static class VehicleFileLog
	{
		public const string TestsFolderRelative = "_Docs/Logs/Tests";
		public const string RuntimeFolderRelative = "_Docs/Logs/Runtime";

		private static StreamWriter s_TestWriter;
		private static readonly HashSet<EntityId> s_TestVehicleIds = new HashSet<EntityId>();
		private static readonly Dictionary<EntityId, StreamWriter> s_RuntimeWriters =
			new Dictionary<EntityId, StreamWriter>(8);
		private static StreamWriter s_SessionWriter;
		private static int s_LinesSinceFlush;
		private const int c_FlushBatch = 32;

		public static string GetTestsDirectory()
		{
			string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
			Directory.CreateDirectory(dir);
			return dir;
		}

		public static string GetRuntimeDirectory()
		{
			string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Runtime");
			Directory.CreateDirectory(dir);
			return dir;
		}

		public static void EnsureFolders()
		{
			GetTestsDirectory();
			GetRuntimeDirectory();
		}

		/// <summary>Test platform owns the writer; vehicle logs route here while bound.</summary>
		public static void AttachTestWriter(StreamWriter _writer)
		{
			EnsureFolders();
			s_TestWriter = _writer;
		}

		public static void DetachTestWriter(StreamWriter _writer)
		{
			if (s_TestWriter == _writer)
				s_TestWriter = null;
			s_TestVehicleIds.Clear();
		}

		public static void BindTestVehicle(Component _vehicleComponent)
		{
			EntityId id = ResolveVehicleId(_vehicleComponent);
			if (id.IsValid())
				s_TestVehicleIds.Add(id);
		}

		public static void UnbindTestVehicle(Component _vehicleComponent)
		{
			EntityId id = ResolveVehicleId(_vehicleComponent);
			if (id.IsValid())
				s_TestVehicleIds.Remove(id);
		}

		public static bool IsUnderTest(Component _vehicleComponent)
		{
			EntityId id = ResolveVehicleId(_vehicleComponent);
			return id.IsValid() && s_TestVehicleIds.Contains(id);
		}

		public static void WriteTest(string _line)
		{
			if (s_TestWriter == null)
				return;
			try
			{
				s_TestWriter.WriteLine(_line);
				NoteFlush(s_TestWriter);
			}
			catch
			{
				// ignore IO during teardown
			}
		}

		public static void Write(Component _context, string _line)
		{
			if (string.IsNullOrEmpty(_line))
				return;

			EntityId id = ResolveVehicleId(_context);
			if (id.IsValid() && s_TestVehicleIds.Contains(id) && s_TestWriter != null)
			{
				WriteTest(_line);
				return;
			}

			if (!id.IsValid())
			{
				WriteSession(_line);
				return;
			}

			StreamWriter writer = GetOrCreateRuntimeWriter(id, _context);
			if (writer == null)
				return;
			try
			{
				writer.WriteLine(_line);
				NoteFlush(writer);
			}
			catch
			{
				// ignore
			}
		}

		/// <summary>Planner / code without a vehicle component — test sink if active, else session file.</summary>
		public static void WriteActive(string _line)
		{
			if (s_TestWriter != null)
			{
				WriteTest(_line);
				return;
			}

			WriteSession(_line);
		}

		public static void CloseRuntimeFor(Component _context)
		{
			EntityId id = ResolveVehicleId(_context);
			if (!id.IsValid())
				return;
			if (!s_RuntimeWriters.TryGetValue(id, out StreamWriter writer))
				return;
			s_RuntimeWriters.Remove(id);
			CloseWriter(writer);
		}

		public static void CloseSession()
		{
			CloseWriter(s_SessionWriter);
			s_SessionWriter = null;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			s_TestWriter = null;
			s_TestVehicleIds.Clear();
			foreach (var kv in s_RuntimeWriters)
				CloseWriter(kv.Value);
			s_RuntimeWriters.Clear();
			CloseWriter(s_SessionWriter);
			s_SessionWriter = null;
			s_LinesSinceFlush = 0;
		}

		private static void WriteSession(string _line)
		{
			if (s_SessionWriter == null)
			{
				try
				{
					string dir = GetRuntimeDirectory();
					string path = Path.Combine(dir, $"VehicleSession_{DateTime.Now:yyyyMMdd_HHmmss}.log");
					s_SessionWriter = new StreamWriter(path, false, Encoding.UTF8) { AutoFlush = false };
					s_SessionWriter.WriteLine($"# Vehicle session log {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
				}
				catch
				{
					return;
				}
			}

			try
			{
				s_SessionWriter.WriteLine(_line);
				NoteFlush(s_SessionWriter);
			}
			catch
			{
				// ignore
			}
		}

		private static StreamWriter GetOrCreateRuntimeWriter(EntityId _id, Component _context)
		{
			if (s_RuntimeWriters.TryGetValue(_id, out StreamWriter existing) && existing != null)
				return existing;

			try
			{
				string dir = GetRuntimeDirectory();
				string safeName = _context != null ? _context.gameObject.name : "Vehicle";
				foreach (char c in Path.GetInvalidFileNameChars())
					safeName = safeName.Replace(c, '_');
				ulong idToken = EntityId.ToULong(_id);
				string path = Path.Combine(dir, $"Vehicle_{safeName}_{idToken}_{DateTime.Now:yyyyMMdd_HHmmss}.log");
				var writer = new StreamWriter(path, false, Encoding.UTF8) { AutoFlush = false };
				writer.WriteLine($"# Runtime vehicle log {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
				writer.WriteLine($"# Object: {safeName} id={idToken}");
				s_RuntimeWriters[_id] = writer;
				return writer;
			}
			catch
			{
				return null;
			}
		}

		private static EntityId ResolveVehicleId(Component _context)
		{
			if (_context == null)
				return default;

			if (_context is VehicleController vc)
				return vc.GetEntityId();

			Transform root = _context.transform.root;
			if (root.TryGetComponent(out VehicleController rootVc))
				return rootVc.GetEntityId();

			VehicleController parentVc = _context.GetComponentInParent<VehicleController>();
			if (parentVc != null)
				return parentVc.GetEntityId();

			return _context.GetEntityId();
		}

		private static void NoteFlush(StreamWriter _writer)
		{
			s_LinesSinceFlush++;
			if (s_LinesSinceFlush < c_FlushBatch)
				return;
			s_LinesSinceFlush = 0;
			try
			{
				_writer.Flush();
			}
			catch
			{
				// ignore
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
	}
}
