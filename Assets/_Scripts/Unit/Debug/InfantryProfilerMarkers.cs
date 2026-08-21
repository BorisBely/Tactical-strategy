using Unity.Profiling;

/// <summary>
/// Shared Profiler markers for infantry hot paths. Filter by <c>Infantry.</c> in the Profiler.
/// </summary>
public static class InfantryProfilerMarkers
{
	#region Public Fields
	public static readonly ProfilerMarker VisionScan = new ProfilerMarker("Infantry.Vision.Scan");
	public static readonly ProfilerMarker DetectionTick = new ProfilerMarker("Infantry.Detection.Tick");
	public static readonly ProfilerMarker TargetSelect = new ProfilerMarker("Infantry.Combat.TargetSelect");
	public static readonly ProfilerMarker AiTick = new ProfilerMarker("Infantry.AI.Tick");
	public static readonly ProfilerMarker Hitscan = new ProfilerMarker("Infantry.Combat.Hitscan");
	public static readonly ProfilerMarker LineOfFire = new ProfilerMarker("Infantry.Combat.LineOfFire");
	#endregion
}
