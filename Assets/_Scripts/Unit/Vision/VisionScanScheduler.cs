using UnityEngine;

/// <summary>
/// Per-frame T3 (LOS/detail) budget. ImmediateScan always bypasses the cap.
/// Overflow defers work — it must not apply an empty vision frame.
/// </summary>
public static class VisionScanScheduler
{
	private static int s_Frame = -1;
	private static int s_Used;
	private static int s_SlotLimit = VisionLodMath.DefaultDetailSlotsPerFrame;

	public static int DetailSlotsPerFrame
	{
		get => s_SlotLimit;
		set => s_SlotLimit = Mathf.Max(1, value);
	}

	public static int UsedThisFrame => s_Used;

	public static bool TryAcquireDetailSlot(bool _forceImmediate)
	{
		if (_forceImmediate)
			return true;

		int frame = Time.frameCount;
		if (s_Frame != frame)
		{
			s_Frame = frame;
			s_Used = 0;
		}

		if (s_Used >= s_SlotLimit)
			return false;

		s_Used++;
		return true;
	}

	public static void ResetForTests()
	{
		s_Frame = -1;
		s_Used = 0;
		s_SlotLimit = VisionLodMath.DefaultDetailSlotsPerFrame;
	}
}
