#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Editor bake of #13.2C ProtectionGeometry into <see cref="TacticalWorld"/>. Play only reads the bake.
/// Candidate point bake is cleared: #13 selection still uses injected sources / leftover records.
/// </summary>
public static class TacticalWorldBaker
{
	#region Public Methods
	public static int Bake(TacticalWorld _world)
	{
		if (_world == null)
			return 0;
		Bounds worldBounds = _world.ResolveWorldBakeBounds();
		Physics.SyncTransforms();
		var settings = new CoverGenerationSettings { ConfirmSurfaceWithPhysics = true };
		var generator = new ProtectionZoneGenerator(
			new PhysicsCoverGeometrySource(),
			new NavMeshCoverProbe(1.2f),
			new PhysicsCoverClearanceProbe(),
			settings,
			new PhysicsCoverOcclusionProbe(),
			new CoverClassificationSettings(),
			new PhysicsCoverWindowProbe());
		var zones = new List<ProtectionZone>(64);
		try
		{
			EditorUtility.DisplayProgressBar("Bake Protection Zones", "collecting geometry", 0.35f);
			generator.Generate(worldBounds, 1, zones);
			EditorUtility.DisplayProgressBar("Bake Protection Zones", "writing " + zones.Count + " zones", 0.85f);
		}
		finally
		{
			EditorUtility.ClearProgressBar();
		}

		var records = new List<BakedProtectionZoneRecord>(zones.Count);
		for (int i = 0; i < zones.Count; i++)
			records.Add(BakedProtectionZoneRecord.FromZone(zones[i]));
		_world.ReplaceBake(null);
		return _world.ReplaceZones(records);
	}

	public static bool NavMeshReachable(Bounds _worldBounds)
	{
		return NavMesh.SamplePosition(_worldBounds.center, out _, 8f, NavMesh.AllAreas);
	}
	#endregion
}
#endif
