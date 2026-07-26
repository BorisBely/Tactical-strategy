using System;
using UnityEngine;

public struct MassContributor
{
	public float Mass;
	public Vector3 LocalOffset;
	public string Name;
}

public interface IMassContributor
{
	float Mass { get; }
	Vector3 LocalOffset { get; }
	string Name { get; }
	event Action OnMassChanged;
}

public sealed class SimpleMassContributor : IMassContributor
{
	public float Mass { get; set; }
	public Vector3 LocalOffset { get; set; }
	public string Name { get; set; }

	public event Action OnMassChanged;

	public SimpleMassContributor(float mass, Vector3 localOffset, string name)
	{
		Mass = mass;
		LocalOffset = localOffset;
		Name = name;
	}

	public void SetMass(float mass)
	{
		if (Mathf.Approximately(Mass, mass))
			return;
		Mass = mass;
		OnMassChanged?.Invoke();
	}

	public void SetOffset(Vector3 offset)
	{
		if (LocalOffset == offset)
			return;
		LocalOffset = offset;
		OnMassChanged?.Invoke();
	}
}
