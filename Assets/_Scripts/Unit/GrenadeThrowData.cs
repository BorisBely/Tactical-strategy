using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Данные для механики броска гранаты: префабы, дальности, аудио, цвета маркеров.
/// Поддерживает per-item маппинги для конкретных гранат (РГД-5, Ф-1 и т.д.).
/// </summary>
[CreateAssetMenu(fileName = "GrenadeThrowData", menuName = "Polygone/Combat/Grenade Throw Data", order = 0)]
public sealed class GrenadeThrowData : ScriptableObject
{
	#region Types
	[Serializable]
	public sealed class GrenadeThrowMapping
	{
		[Tooltip("ItemDefinition гранаты (ScriptableObject из GameData/Inventory/Grenades).")]
		public ItemDefinition Item;
		[Tooltip("Префаб бросаемого снаряда с GrenadeProjectile + Rigidbody.")]
		public GameObject ThrownPrefab;
		[Tooltip("Префаб гранаты в руке во время анимации.")]
		public GameObject HandPrefab;
		[Tooltip("Клипы взрыва для этой конкретной гранаты. Логика проигрывания подключается отдельно.")]
		public WeaponRandomAudioClipSet ExplosionClips = new WeaponRandomAudioClipSet();
		[Range(0f, 1f)] public float ExplosionVolume = 1f;
		[Tooltip("Множитель масштаба VFX взрыва относительно дефолта типа. 1 = без изменений.")]
		[Min(0.01f)] public float ExplosionVfxScaleMultiplier = 1f;
		[Tooltip("Множитель длительности VFX взрыва. 1 = без изменений.")]
		[Min(0.01f)] public float ExplosionVfxLifetimeMultiplier = 1f;
		[Tooltip("Доп. yaw (град.) при спавне взрыва — лёгкое визуальное отличие силуэта.")]
		[Range(-180f, 180f)] public float ExplosionVfxYawOffsetDegrees = 0f;
	}
	#endregion

	#region Serialized Fields
	[Header("Per-Item Mappings (Override per-type defaults)")]
	[Tooltip("Список конкретных гранат с собственными префабами броска и руки. Если для предмета нет записи — используется дефолт по типу.")]
	[SerializeField] private List<GrenadeThrowMapping> m_ItemMappings = new List<GrenadeThrowMapping>();

	[Header("Default Thrown Prefabs (Fallback by Type)")]
	[SerializeField] private GameObject m_ThrownFragPrefab;
	[SerializeField] private GameObject m_ThrownFlashPrefab;
	[SerializeField] private GameObject m_ThrownSmokePrefab;

	[Header("Default Attach Prefabs (Fallback by Type)")]
	[SerializeField] private GameObject m_HandFragPrefab;
	[SerializeField] private GameObject m_HandFlashPrefab;
	[SerializeField] private GameObject m_HandSmokePrefab;

	[Header("Throw Parameters")]
	[SerializeField, Min(5f)] private float m_MaxRange = 35f;
	[SerializeField, Min(1f)] private float m_MinRange = 5f;
	[SerializeField, Min(0.5f)] private float m_ReleaseHeight = 1.5f;
	[SerializeField, Min(0.5f)] private float m_ArcHeight = 3f;
	[SerializeField, Min(1f)] private float m_ProjectileLifetime = 60f;

	[Header("Roll Prediction")]
	[Tooltip("Доля от радиуса точности, резервируемая под прокат гранаты после приземления (0–1).")]
	[SerializeField, Range(0f, 0.9f)] private float m_RollReserveFactor = 0.4f;
	[Tooltip("Абсолютный минимум в метрах, резервируемый под прокат.")]
	[SerializeField, Min(0f)] private float m_RollReserveAbsolute = 0.3f;

	[Header("Post-Land Roll")]
	[Tooltip("Линейный демпинг (drag), применяемый к гранате после приземления для быстрого замедления.")]
	[SerializeField, Min(0f)] private float m_LandingDrag = 4f;
	[Tooltip("Демпинг после падения на сыпучую поверхность (песок / земля).")]
	[SerializeField, Min(0f)] private float m_SoftLandingDrag = 14f;
	[Tooltip("Множитель горизонтальной скорости при падении на сыпучую поверхность.")]
	[SerializeField, Range(0.05f, 1f)] private float m_SoftLandingHorizontalScale = 0.35f;
	[Tooltip("Множитель отскока по нормали на сыпучей поверхности (0 = почти без прыжка).")]
	[SerializeField, Range(0f, 1f)] private float m_SoftLandingBounceScale = 0.08f;
	[Tooltip("Максимальная скорость (м/с), при которой граната считается остановившейся после приземления.")]
	[SerializeField, Min(0.01f)] private float m_RollStopSpeed = 0.1f;

	[Header("Detail LOD")]
	[Tooltip("Дистанция от камеры (м), дальше которой отделяемые части гранаты (чека, скоба) не показываются.")]
	[SerializeField, Min(1f)] private float m_GrenadeDetailCullDistance = 12f;

	[Header("Explosion VFX")]
	[Tooltip("F1 / RGD / Frag — FX_Grenade_Explosion_01.")]
	[SerializeField] private GameObject m_FragExplosionPrefab;
	[Tooltip("Flash — FX_Grenade_Explosion_02.")]
	[SerializeField] private GameObject m_FlashExplosionPrefab;
	[Tooltip("Smoke — только дымовой FX.")]
	[SerializeField] private GameObject m_SmokePrefab;
	[SerializeField, Min(0.1f)] private float m_ExplosionFuseSeconds = 3.5f;
	[SerializeField, Min(0f)] private float m_ExplosionMaxDistanceMeters = 200f;
	[SerializeField, Min(5f)] private float m_ExplosionAudioMaxDistance = 220f;

	[Header("VFX Tuning By Type")]
	[SerializeField, Min(0.01f)] private float m_FragExplosionScale = 1.25f;
	[SerializeField, Min(0.05f)] private float m_FragExplosionLifetimeSeconds = 5f;
	[SerializeField, Min(0.01f)] private float m_FlashExplosionScale = 0.9f;
	[SerializeField, Min(0.05f)] private float m_FlashExplosionLifetimeSeconds = 2.2f;
	[SerializeField, Min(0.01f)] private float m_SmokeScale = 1.35f;
	[Tooltip("Длительность активной эмиссии дыма до начала рассеивания.")]
	[SerializeField, Min(0.05f)] private float m_SmokeLifetimeSeconds = 32f;
	[Tooltip("Сколько секунд плавно рассеивать дым после таймера.")]
	[SerializeField, Min(0.5f)] private float m_SmokeDissipateSeconds = 10f;
	[SerializeField, Min(5f)] private float m_SmokeMaxDistanceMeters = 110f;
	[Header("Smoke Audio")]
	[SerializeField] private AudioClip m_SmokeLoopClip;
	[SerializeField, Range(0f, 1f)] private float m_SmokeLoopVolume = 0.31f;
	[SerializeField, Min(5f)] private float m_SmokeLoopMaxDistance = 42f;
	[SerializeField, Min(0.05f)] private float m_SmokeAudioFadeInSeconds = 1.4f;
	[SerializeField, Min(0.05f)] private float m_SmokeAudioFadeOutSeconds = 3f;
	[SerializeField, Min(0.5f)] private float m_SmokeAudioCrossfadeSeconds = 2.2f;

	[Header("Audio")]
	[SerializeField] private WeaponRandomAudioClipSet m_PinPullClips = new WeaponRandomAudioClipSet();
	[SerializeField, Range(0f, 1f)] private float m_PinPullVolume = 0.55f;
	[SerializeField, Min(2f)] private float m_PinPullMaxDistance = 16f;
	[SerializeField] private WeaponRandomAudioClipSet m_LeverReleaseClips = new WeaponRandomAudioClipSet();
	[SerializeField, Range(0f, 1f)] private float m_LeverReleaseVolume = 0.55f;
	[SerializeField, Min(2f)] private float m_LeverReleaseMaxDistance = 16f;
	[SerializeField] private WeaponRandomAudioClipSet m_ThrowClips = new WeaponRandomAudioClipSet();
	[SerializeField, Range(0f, 1f)] private float m_ThrowVolume = 0.85f;
	[Tooltip("Падение на твёрдые поверхности: Concrete / Metal / Wood / Glass.")]
	[SerializeField] private WeaponRandomAudioClipSet m_HardImpactClips = new WeaponRandomAudioClipSet();
	[Tooltip("Падение на сыпучие: Sand / Dirt (и Gravel).")]
	[SerializeField] private WeaponRandomAudioClipSet m_SoftImpactClips = new WeaponRandomAudioClipSet();
	[SerializeField, Range(0f, 1f)] private float m_ImpactVolume = 0.7f;
	[SerializeField, Min(2f)] private float m_ImpactMaxDistance = 28f;
	[Tooltip("PhysicsMaterial сыпучих поверхностей. Если пусто — определяются по имени Sand/Dirt/Gravel.")]
	[SerializeField] private PhysicsMaterial[] m_SoftSurfaceMaterials = Array.Empty<PhysicsMaterial>();

	[Header("Marker Colors")]
	[SerializeField] private Color m_FragColor = new Color(1f, 0.25f, 0.2f, 0.9f);
	[SerializeField] private Color m_FlashColor = new Color(1f, 0.95f, 0.2f, 0.9f);
	[SerializeField] private Color m_SmokeColor = new Color(0.65f, 0.65f, 0.65f, 0.9f);
	#endregion

	#region Public Properties
	public float MaxRange => m_MaxRange;
	public float MinRange => m_MinRange;
	public float ReleaseHeight => m_ReleaseHeight;
	public float ArcHeight => m_ArcHeight;
	public float ProjectileLifetime => m_ProjectileLifetime;
	public float RollReserveFactor => m_RollReserveFactor;
	public float RollReserveAbsolute => m_RollReserveAbsolute;
	public float LandingDrag => m_LandingDrag;
	public float SoftLandingDrag => m_SoftLandingDrag;
	public float SoftLandingHorizontalScale => m_SoftLandingHorizontalScale;
	public float SoftLandingBounceScale => m_SoftLandingBounceScale;
	public float RollStopSpeed => m_RollStopSpeed;
	public float GrenadeDetailCullDistance => m_GrenadeDetailCullDistance;
	public GameObject FragExplosionPrefab => m_FragExplosionPrefab;
	public GameObject FlashExplosionPrefab => m_FlashExplosionPrefab;
	public GameObject SmokePrefab => m_SmokePrefab;
	public float ExplosionFuseSeconds => m_ExplosionFuseSeconds;
	public float ExplosionMaxDistanceMeters => m_ExplosionMaxDistanceMeters;
	public float ExplosionAudioMaxDistance => m_ExplosionAudioMaxDistance;
	public float FragExplosionScale => m_FragExplosionScale;
	public float FragExplosionLifetimeSeconds => m_FragExplosionLifetimeSeconds;
	public float FlashExplosionScale => m_FlashExplosionScale;
	public float FlashExplosionLifetimeSeconds => m_FlashExplosionLifetimeSeconds;
	public float SmokeScale => m_SmokeScale;
	public float SmokeLifetimeSeconds => m_SmokeLifetimeSeconds;
	public float SmokeDissipateSeconds => m_SmokeDissipateSeconds;
	public float SmokeMaxDistanceMeters => m_SmokeMaxDistanceMeters;
	public AudioClip SmokeLoopClip => m_SmokeLoopClip;
	public float SmokeLoopVolume => m_SmokeLoopVolume;
	public float SmokeLoopMaxDistance => m_SmokeLoopMaxDistance;
	public float SmokeAudioFadeInSeconds => m_SmokeAudioFadeInSeconds;
	public float SmokeAudioFadeOutSeconds => m_SmokeAudioFadeOutSeconds;
	public float SmokeAudioCrossfadeSeconds => m_SmokeAudioCrossfadeSeconds;
	public IReadOnlyList<GrenadeThrowMapping> ItemMappings => m_ItemMappings;

	public Color FragColor => m_FragColor;
	public Color FlashColor => m_FlashColor;
	public Color SmokeColor => m_SmokeColor;
	#endregion

	#region Public Methods
	public GameObject GetThrownPrefab(ItemDefinition _item)
	{
		if (_item != null)
		{
			for (int i = 0; i < m_ItemMappings.Count; i++)
			{
				if (m_ItemMappings[i] != null && m_ItemMappings[i].Item == _item && m_ItemMappings[i].ThrownPrefab != null)
					return m_ItemMappings[i].ThrownPrefab;
			}
		}

		return GetThrownPrefabByType(_item != null ? _item.GrenadeType : GrenadeType.Unknown);
	}

	public GameObject GetHandPrefab(ItemDefinition _item)
	{
		if (_item != null)
		{
			for (int i = 0; i < m_ItemMappings.Count; i++)
			{
				if (m_ItemMappings[i] != null && m_ItemMappings[i].Item == _item && m_ItemMappings[i].HandPrefab != null)
					return m_ItemMappings[i].HandPrefab;
			}
		}

		return GetHandPrefabByType(_item != null ? _item.GrenadeType : GrenadeType.Unknown);
	}

	public GameObject GetThrownPrefabByType(GrenadeType _type)
	{
		return _type switch
		{
			GrenadeType.Fragmentation => m_ThrownFragPrefab,
			GrenadeType.Flash => m_ThrownFlashPrefab,
			GrenadeType.Smoke => m_ThrownSmokePrefab,
			_ => null
		};
	}

	public GameObject GetHandPrefabByType(GrenadeType _type)
	{
		return _type switch
		{
			GrenadeType.Fragmentation => m_HandFragPrefab,
			GrenadeType.Flash => m_HandFlashPrefab,
			GrenadeType.Smoke => m_HandSmokePrefab,
			_ => null
		};
	}

	public Color GetMarkerColor(GrenadeType _type)
	{
		return _type switch
		{
			GrenadeType.Fragmentation => m_FragColor,
			GrenadeType.Flash => m_FlashColor,
			GrenadeType.Smoke => m_SmokeColor,
			_ => Color.white
		};
	}

	public bool TryPickPinPullSound(out AudioClip _clip) => m_PinPullClips.TryPickClip(out _clip);
	public bool TryPickLeverReleaseSound(out AudioClip _clip) => m_LeverReleaseClips.TryPickClip(out _clip);
	public bool TryPickThrowSound(out AudioClip _clip) => m_ThrowClips.TryPickClip(out _clip);

	public bool TryPickImpactSound(Collider _hitCollider, out AudioClip _clip, out bool _isSoftSurface)
	{
		_clip = null;
		_isSoftSurface = IsSoftSurface(_hitCollider);
		WeaponRandomAudioClipSet set = _isSoftSurface ? m_SoftImpactClips : m_HardImpactClips;
		if (set != null && set.TryPickClip(out _clip))
			return true;

		WeaponRandomAudioClipSet fallback = _isSoftSurface ? m_HardImpactClips : m_SoftImpactClips;
		return fallback != null && fallback.TryPickClip(out _clip);
	}

	public bool IsSoftSurface(Collider _hitCollider)
	{
		if (_hitCollider == null)
			return false;

		PhysicsMaterial material = _hitCollider.sharedMaterial;
		if (material == null)
			return false;

		if (m_SoftSurfaceMaterials != null)
		{
			for (int i = 0; i < m_SoftSurfaceMaterials.Length; i++)
			{
				if (m_SoftSurfaceMaterials[i] != null && m_SoftSurfaceMaterials[i] == material)
					return true;
			}
		}

		string name = material.name;
		return name.IndexOf("Sand", StringComparison.OrdinalIgnoreCase) >= 0 ||
		       name.IndexOf("Dirt", StringComparison.OrdinalIgnoreCase) >= 0 ||
		       name.IndexOf("Gravel", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	public bool TryPickExplosionSound(ItemDefinition _item, out AudioClip _clip)
	{
		_clip = null;
		GrenadeThrowMapping mapping = FindMapping(_item);
		if (mapping == null || mapping.ExplosionClips == null)
			return false;

		return mapping.ExplosionClips.TryPickClip(out _clip);
	}

	public float GetExplosionVolume(ItemDefinition _item)
	{
		GrenadeThrowMapping mapping = FindMapping(_item);
		return mapping != null ? mapping.ExplosionVolume : 1f;
	}

	public GameObject PickExplosionPrefab(ItemDefinition _item)
	{
		GrenadeType type = _item != null ? _item.GrenadeType : GrenadeType.Fragmentation;
		return type switch
		{
			GrenadeType.Fragmentation => m_FragExplosionPrefab,
			GrenadeType.Flash => m_FlashExplosionPrefab != null ? m_FlashExplosionPrefab : m_FragExplosionPrefab,
			GrenadeType.Smoke => null,
			_ => m_FragExplosionPrefab
		};
	}

	public bool ShouldSpawnSmokeOnDetonation(GrenadeType _type)
	{
		return _type == GrenadeType.Smoke && m_SmokePrefab != null;
	}

	public float GetDetonationVfxScale(ItemDefinition _item)
	{
		GrenadeType type = _item != null ? _item.GrenadeType : GrenadeType.Fragmentation;
		float baseScale = type switch
		{
			GrenadeType.Flash => m_FlashExplosionScale,
			GrenadeType.Smoke => m_SmokeScale,
			_ => m_FragExplosionScale
		};

		GrenadeThrowMapping mapping = FindMapping(_item);
		float multiplier = mapping != null ? Mathf.Max(0.01f, mapping.ExplosionVfxScaleMultiplier) : 1f;
		return baseScale * multiplier;
	}

	public float GetDetonationVfxLifetimeSeconds(ItemDefinition _item)
	{
		GrenadeType type = _item != null ? _item.GrenadeType : GrenadeType.Fragmentation;
		float baseLifetime = type switch
		{
			GrenadeType.Flash => m_FlashExplosionLifetimeSeconds,
			GrenadeType.Smoke => m_SmokeLifetimeSeconds,
			_ => m_FragExplosionLifetimeSeconds
		};

		GrenadeThrowMapping mapping = FindMapping(_item);
		float multiplier = mapping != null ? Mathf.Max(0.01f, mapping.ExplosionVfxLifetimeMultiplier) : 1f;
		return baseLifetime * multiplier;
	}

	public float GetExplosionVfxYawOffsetDegrees(ItemDefinition _item)
	{
		GrenadeThrowMapping mapping = FindMapping(_item);
		return mapping != null ? mapping.ExplosionVfxYawOffsetDegrees : 0f;
	}

	public float PinPullVolume => m_PinPullVolume;
	public float PinPullMaxDistance => m_PinPullMaxDistance;
	public float LeverReleaseVolume => m_LeverReleaseVolume;
	public float LeverReleaseMaxDistance => m_LeverReleaseMaxDistance;
	public float ThrowVolume => m_ThrowVolume;
	public float ImpactVolume => m_ImpactVolume;
	public float ImpactMaxDistance => m_ImpactMaxDistance;

	public void AddMapping(ItemDefinition _item, GameObject _thrownPrefab, GameObject _handPrefab)
	{
		for (int i = 0; i < m_ItemMappings.Count; i++)
		{
			if (m_ItemMappings[i] != null && m_ItemMappings[i].Item == _item)
			{
				m_ItemMappings[i].ThrownPrefab = _thrownPrefab;
				m_ItemMappings[i].HandPrefab = _handPrefab;
				return;
			}
		}

		m_ItemMappings.Add(new GrenadeThrowMapping
		{
			Item = _item,
			ThrownPrefab = _thrownPrefab,
			HandPrefab = _handPrefab,
			ExplosionClips = new WeaponRandomAudioClipSet(),
			ExplosionVolume = 1f,
			ExplosionVfxScaleMultiplier = 1f,
			ExplosionVfxLifetimeMultiplier = 1f,
			ExplosionVfxYawOffsetDegrees = 0f
		});
	}

	private GrenadeThrowMapping FindMapping(ItemDefinition _item)
	{
		if (_item == null)
			return null;

		for (int i = 0; i < m_ItemMappings.Count; i++)
		{
			GrenadeThrowMapping mapping = m_ItemMappings[i];
			if (mapping != null && mapping.Item == _item)
				return mapping;
		}

		return null;
	}

	public void SetTypeDefaults(GameObject _thrownFrag, GameObject _thrownFlash, GameObject _thrownSmoke,
		GameObject _handFrag, GameObject _handFlash, GameObject _handSmoke)
	{
		m_ThrownFragPrefab = _thrownFrag;
		m_ThrownFlashPrefab = _thrownFlash;
		m_ThrownSmokePrefab = _thrownSmoke;
		m_HandFragPrefab = _handFrag;
		m_HandFlashPrefab = _handFlash;
		m_HandSmokePrefab = _handSmoke;
	}
	#endregion
}
