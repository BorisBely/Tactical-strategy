using UnityEngine;

/// <summary>
/// Правило выбора клипов шага: слой коллайдера и/или Physics Material.
/// Оба условия должны выполняться, если поля заданы.
/// Порядок в списке на <see cref="UnitClickToMove"/> — сначала более специфичные правила.
/// </summary>
[System.Serializable]
public sealed class FootstepSurfaceRule
{
	[Tooltip("Если не 0 — слой объекта под ногами должен попадать в маску.")]
	public LayerMask Layers;

	[Tooltip("Если задан — должен совпадать с Physics Material коллайдера пола.")]
	public PhysicsMaterial PhysicsMaterial;

	public AudioClip[] Clips;
}
