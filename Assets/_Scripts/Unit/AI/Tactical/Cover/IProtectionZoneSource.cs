using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Editor / Play source of baked protection zones. Does not scan physics.
/// </summary>
public interface IProtectionZoneSource
{
	void Generate(Bounds _worldBounds, int _geometryVersion, List<ProtectionZone> _destination);
}
