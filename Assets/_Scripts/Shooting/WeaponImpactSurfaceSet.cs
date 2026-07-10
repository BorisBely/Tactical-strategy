using UnityEngine;

/// <summary>
/// Набор декалей и звуков попадания для одной физической поверхности.
/// </summary>
[System.Serializable]
public sealed class WeaponImpactSurfaceSet
{
	#region Serialized Fields
	[Tooltip("Имя для инспектора / логов (Concrete, Metal, Wood, Glass).")]
	public string SurfaceName = "Concrete";

	[Tooltip("Physics Material коллайдера. Если совпал — берём этот набор.")]
	public PhysicsMaterial PhysicsMaterial;

	[Tooltip("Варианты декали; при попадании выбирается случайный непустой префаб.")]
	public GameObject[] DecalPrefabs;

	[Tooltip("Варианты звука попадания по этой поверхности.")]
	public AudioClip[] ImpactSounds;

	[Range(0f, 1f)] public float ImpactVolume = 0.85f;
	#endregion

	#region Public Methods
	public GameObject PickRandomDecal()
	{
		return PickRandom(DecalPrefabs);
	}

	public bool HasAnyImpactSound()
	{
		if (ImpactVolume <= 0f || ImpactSounds == null || ImpactSounds.Length == 0)
			return false;

		for (int i = 0; i < ImpactSounds.Length; i++)
		{
			if (ImpactSounds[i] != null)
				return true;
		}

		return false;
	}

	public bool TryPickImpactSound(out AudioClip _clip, out float _volume)
	{
		_volume = ImpactVolume;
		_clip = PickRandom(ImpactSounds);
		return _clip != null && _volume > 0f;
	}
	#endregion

	#region Private Methods
	private static T PickRandom<T>(T[] _items) where T : Object
	{
		if (_items == null || _items.Length == 0)
			return null;

		int validCount = 0;
		for (int i = 0; i < _items.Length; i++)
		{
			if (_items[i] != null)
				validCount++;
		}

		if (validCount == 0)
			return null;

		int pick = Random.Range(0, validCount);
		for (int i = 0; i < _items.Length; i++)
		{
			T item = _items[i];
			if (item == null)
				continue;

			if (pick == 0)
				return item;

			pick--;
		}

		return null;
	}
	#endregion
}
