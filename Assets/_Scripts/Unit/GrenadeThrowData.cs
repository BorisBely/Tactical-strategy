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
	[Tooltip("Максимальная скорость (м/с), при которой граната считается остановившейся после приземления.")]
	[SerializeField, Min(0.01f)] private float m_RollStopSpeed = 0.1f;

	[Header("Detail LOD")]
	[Tooltip("Дистанция от камеры (м), дальше которой отделяемые части гранаты (чека, скоба) не показываются.")]
	[SerializeField, Min(1f)] private float m_GrenadeDetailCullDistance = 12f;

	[Header("Audio")]
	[SerializeField] private WeaponRandomAudioClipSet m_PinPullClips = new WeaponRandomAudioClipSet();
	[SerializeField, Range(0f, 1f)] private float m_PinPullVolume = 0.9f;
	[SerializeField] private WeaponRandomAudioClipSet m_ThrowClips = new WeaponRandomAudioClipSet();
	[SerializeField, Range(0f, 1f)] private float m_ThrowVolume = 0.85f;
	[SerializeField] private WeaponRandomAudioClipSet m_ImpactClips = new WeaponRandomAudioClipSet();
	[SerializeField, Range(0f, 1f)] private float m_ImpactVolume = 0.7f;

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
	public float RollStopSpeed => m_RollStopSpeed;
	public float GrenadeDetailCullDistance => m_GrenadeDetailCullDistance;
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
	public bool TryPickThrowSound(out AudioClip _clip) => m_ThrowClips.TryPickClip(out _clip);
	public bool TryPickImpactSound(out AudioClip _clip) => m_ImpactClips.TryPickClip(out _clip);

	public float PinPullVolume => m_PinPullVolume;
	public float ThrowVolume => m_ThrowVolume;
	public float ImpactVolume => m_ImpactVolume;

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
			HandPrefab = _handPrefab
		});
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
