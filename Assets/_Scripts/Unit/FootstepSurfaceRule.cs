using UnityEngine;

/// <summary>
/// Правило клипов шага по слою и/или Physics Material коллайдера пола.
/// Порядок в <see cref="UnitFootsteps"/> — первое совпадение.
/// </summary>
[System.Serializable]
public sealed class FootstepSurfaceRule
{
	public LayerMask Layers;
	public PhysicsMaterial PhysicsMaterial;
	public AudioClip[] Clips;
}
