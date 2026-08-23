using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-frame T3 (LOS/detail) budget. ImmediateScan always bypasses the cap.
/// Non-immediate Detail is two-phase: Request in Update, Flush in LateUpdate by score + starve.
/// Overflow defers work — it must not apply an empty vision frame.
/// </summary>
public static class VisionScanScheduler
{
	#region Nested
	private struct PendingRequest
	{
		public int ObserverId;
		public float Score;
	}
	#endregion

	#region Private Fields
	private static int s_Frame = -1;
	private static int s_Used;
	private static int s_SlotLimit = VisionLodMath.DefaultDetailSlotsPerFrame;
	private static readonly List<PendingRequest> s_Requests = new List<PendingRequest>(64);
	private static readonly HashSet<int> s_Granted = new HashSet<int>();
	private static bool s_Flushed;
	private static bool s_UseTestFrame;
	private static int s_TestFrame;
	#endregion

	#region Public Properties
	public static int DetailSlotsPerFrame
	{
		get => s_SlotLimit;
		set => s_SlotLimit = Mathf.Max(1, value);
	}

	public static int UsedThisFrame => s_Used;
	#endregion

	#region Public Methods
	public static bool TryAcquireDetailSlot(bool _forceImmediate)
	{
		if (_forceImmediate)
			return true;

		EnsureFrame();
		if (s_Used >= s_SlotLimit)
			return false;

		s_Used++;
		return true;
	}

	public static void RequestDetailSlot(int _observerId, float _score)
	{
		EnsureFrame();
		if (s_Flushed)
			return;

		for (int i = 0; i < s_Requests.Count; i++)
		{
			PendingRequest existing = s_Requests[i];
			if (existing.ObserverId != _observerId)
				continue;
			if (_score > existing.Score)
			{
				existing.Score = _score;
				s_Requests[i] = existing;
			}

			return;
		}

		s_Requests.Add(new PendingRequest
		{
			ObserverId = _observerId,
			Score = _score
		});
	}

	public static void FlushPendingDetailIfNeeded()
	{
		EnsureFrame();
		if (s_Flushed)
			return;

		s_Flushed = true;
		s_Requests.Sort(CompareRequests);
		int remaining = s_SlotLimit - s_Used;
		if (remaining < 0)
			remaining = 0;

		for (int i = 0; i < s_Requests.Count && remaining > 0; i++)
		{
			s_Granted.Add(s_Requests[i].ObserverId);
			s_Used++;
			remaining--;
		}

		s_Requests.Clear();
	}

	public static bool WasGranted(int _observerId)
	{
		return s_Granted.Contains(_observerId);
	}

	public static void BeginFrameForTests(int _frame)
	{
		s_UseTestFrame = true;
		s_TestFrame = _frame;
		s_Frame = _frame;
		s_Used = 0;
		s_Requests.Clear();
		s_Granted.Clear();
		s_Flushed = false;
	}

	public static void ResetForTests()
	{
		s_Frame = -1;
		s_Used = 0;
		s_SlotLimit = VisionLodMath.DefaultDetailSlotsPerFrame;
		s_Requests.Clear();
		s_Granted.Clear();
		s_Flushed = false;
		s_UseTestFrame = false;
		s_TestFrame = -1;
	}
	#endregion

	#region Private Methods
	private static int CurrentFrame => s_UseTestFrame ? s_TestFrame : Time.frameCount;

	private static void EnsureFrame()
	{
		int frame = CurrentFrame;
		if (s_Frame == frame)
			return;

		s_Frame = frame;
		s_Used = 0;
		s_Requests.Clear();
		s_Granted.Clear();
		s_Flushed = false;
	}

	private static int CompareRequests(PendingRequest _a, PendingRequest _b)
	{
		int cmp = _b.Score.CompareTo(_a.Score);
		if (cmp != 0)
			return cmp;
		return _a.ObserverId.CompareTo(_b.ObserverId);
	}
	#endregion
}
