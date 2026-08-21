using UnityEngine;

/// <summary>
/// Debug order-pick gate. RTS selection and ClickToMove skip world clicks while picking.
/// Command-point pending also sets picking so LMB destination does not clear selection.
/// </summary>
public static class TacticalDebugOrderSession
{
	#region Public Properties
	public static bool IsPicking { get; private set; }
	public static bool IsCommandPointPending { get; private set; }
	public static bool DidConsumeLeftClickThisFrame => s_ConsumedLeftClickFrame == Time.frameCount;
	public static bool DidConsumeRightClickThisFrame => s_ConsumedRightClickFrame == Time.frameCount;
	#endregion

	#region Private Fields
	private static int s_ConsumedLeftClickFrame = -1;
	private static int s_ConsumedRightClickFrame = -1;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics()
	{
		IsPicking = false;
		IsCommandPointPending = false;
		s_ConsumedLeftClickFrame = -1;
		s_ConsumedRightClickFrame = -1;
	}
	#endregion

	#region Public Methods
	public static void SetPicking(bool _picking)
	{
		IsPicking = _picking;
	}

	public static void SetCommandPointPending(bool _pending)
	{
		IsCommandPointPending = _pending;
	}

	public static void ConsumeLeftClick()
	{
		s_ConsumedLeftClickFrame = Time.frameCount;
	}

	public static void ConsumeRightClick()
	{
		s_ConsumedRightClickFrame = Time.frameCount;
	}
	#endregion
}
