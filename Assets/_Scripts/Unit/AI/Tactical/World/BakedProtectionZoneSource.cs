using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Play-time #13.2C source: copy editor-baked zones. No physics query.
/// </summary>
public sealed class BakedProtectionZoneSource : IProtectionZoneSource
{
	#region Private Fields
	private readonly IReadOnlyList<BakedProtectionZoneRecord> m_Baked;
	#endregion

	#region Public Constructors
	public BakedProtectionZoneSource(IReadOnlyList<BakedProtectionZoneRecord> _baked)
	{
		m_Baked = _baked;
	}
	#endregion

	#region Public Methods
	public void Generate(Bounds _worldBounds, int _geometryVersion, List<ProtectionZone> _destination)
	{
		if (_destination == null || m_Baked == null)
			return;
		for (int i = 0; i < m_Baked.Count; i++)
		{
			ProtectionZone zone = m_Baked[i].ToZone();
			zone.GeometryVersion = _geometryVersion;
			_destination.Add(zone);
		}
	}
	#endregion
}
