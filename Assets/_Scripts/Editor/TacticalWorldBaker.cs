#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Editor bake of #13 candidates into <see cref="TacticalWorld"/>. Play only reads the bake.
/// </summary>
public static class TacticalWorldBaker
{
	#region Public Methods
	public static int Bake(TacticalWorld _world)
	{
		if (_world == null)
			return 0;
		Bounds worldBounds = _world.ResolveWorldBakeBounds();
		float regionSize = CoverSpatialMath.DefaultRegionSizeMeters;
		int minX = Mathf.FloorToInt(worldBounds.min.x / regionSize);
		int maxX = Mathf.FloorToInt(worldBounds.max.x / regionSize);
		int minZ = Mathf.FloorToInt(worldBounds.min.z / regionSize);
		int maxZ = Mathf.FloorToInt(worldBounds.max.z / regionSize);

		Physics.SyncTransforms();
		var settings = new CoverGenerationSettings { ConfirmSurfaceWithPhysics = true };
		var generator = new CoverCandidateGenerator(
			new PhysicsCoverGeometrySource(),
			new NavMeshCoverProbe(1.2f),
			new PhysicsCoverClearanceProbe(),
			settings,
			new PhysicsCoverOcclusionProbe(),
			new CoverClassificationSettings());
		var baked = new List<BakedCoverCandidateRecord>(256);
		int regionTotal = Mathf.Max(1, (maxX - minX + 1) * (maxZ - minZ + 1));
		int regionIndex = 0;
		try
		{
			for (int x = minX; x <= maxX; x++)
			{
				for (int z = minZ; z <= maxZ; z++)
				{
					regionIndex++;
					EditorUtility.DisplayProgressBar(
						"Bake Cover",
						"region " + regionIndex + "/" + regionTotal,
						regionIndex / (float)regionTotal);
					var region = new CoverRegionId(x, z);
					Bounds regionBounds = CoverSpatialMath.RegionBounds(region, regionSize);
					var slot = new List<CoverCandidate>(16);
					generator.Generate(region, regionBounds, 1, slot);
					for (int i = 0; i < slot.Count; i++)
					{
						CoverCandidate candidate = slot[i];
						if (candidate == null)
							continue;
						baked.Add(BakedCoverCandidateRecord.FromCandidate(candidate));
					}
				}
			}
		}
		finally
		{
			EditorUtility.ClearProgressBar();
		}

		return _world.ReplaceBake(baked);
	}

	public static bool NavMeshReachable(Bounds _worldBounds)
	{
		return NavMesh.SamplePosition(_worldBounds.center, out _, 8f, NavMesh.AllAreas);
	}
	#endregion
}
#endif
