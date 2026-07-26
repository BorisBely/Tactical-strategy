using System.Collections.Generic;
using UnityEngine;

public sealed class SurfacePhysics
{
	#region Private Fields
	private readonly List<SurfacePhysicsDefinition> m_Profiles;
	private readonly SurfacePhysicsDefinition m_DefaultProfile;
	private readonly Dictionary<int, SurfacePhysicsDefinition> m_CacheByInstanceId = new();
	#endregion

	#region Constructor
	public SurfacePhysics(List<SurfacePhysicsDefinition> profiles, SurfacePhysicsDefinition defaultProfile)
	{
		m_Profiles = profiles ?? new List<SurfacePhysicsDefinition>();
		m_DefaultProfile = defaultProfile;
	}
	#endregion

	#region Public Methods
	public SurfacePhysicsDefinition Resolve(Collider collider)
	{
		if (collider == null)
			return m_DefaultProfile;

		int id = collider.GetInstanceID();
		if (m_CacheByInstanceId.TryGetValue(id, out var cached))
			return cached;

		var result = ResolveUncached(collider);
		m_CacheByInstanceId[id] = result;
		return result;
	}

	public void ClearCache()
	{
		m_CacheByInstanceId.Clear();
	}
	#endregion

	#region Private Methods
	private SurfacePhysicsDefinition ResolveUncached(Collider collider)
	{
		PhysicsMaterial mat = collider.sharedMaterial;
		string raw = mat != null ? mat.name : collider.name;
		if (string.IsNullOrEmpty(raw))
			return m_DefaultProfile;

		string key = raw.ToLowerInvariant();

		for (int i = 0; i < m_Profiles.Count; i++)
		{
			var profile = m_Profiles[i];
			if (profile == null || profile.MatchKeywords == null)
				continue;

			for (int j = 0; j < profile.MatchKeywords.Length; j++)
			{
				if (!string.IsNullOrEmpty(profile.MatchKeywords[j]) &&
				    key.Contains(profile.MatchKeywords[j].ToLowerInvariant()))
					return profile;
			}
		}

		return m_DefaultProfile;
	}
	#endregion
}
