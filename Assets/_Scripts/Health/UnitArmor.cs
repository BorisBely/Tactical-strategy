using System;
using UnityEngine;

/// <summary>
/// Боевой статус брони юнита: тип, прочность, строка для UI и проверка блока попаданий.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitArmor : MonoBehaviour
{
	#region Events
	public event Action Changed;
	#endregion

	#region Serialized Fields
	[SerializeField] private UnitArmorType m_ArmorType = UnitArmorType.None;
	[SerializeField, Min(0f)] private float m_MaxDurability = UnitArmorCombatDesign.MaxDurability;
	[SerializeField, Min(0f)] private float m_Durability;

	[Header("Debug")]
	[SerializeField] private bool m_LogArmor;
	#endregion

	#region Public Properties
	public UnitArmorType ArmorType => m_ArmorType;
	public float MaxDurability => m_MaxDurability;
	public float Durability => m_Durability;
	public bool HasArmor => m_ArmorType != UnitArmorType.None;
	public bool IsDestroyed => HasArmor && m_Durability <= 0f;
	public UnitArmorCondition Condition => ResolveCondition();
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (HasArmor && m_Durability <= 0f)
			m_Durability = m_MaxDurability;
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		m_MaxDurability = Mathf.Max(0f, m_MaxDurability);
		m_Durability = Mathf.Clamp(m_Durability, 0f, m_MaxDurability);
	}
#endif
	#endregion

	#region Public Methods
	public void SetArmorFromPresetIndex(int _armorIndex)
	{
		UnitArmorType type = _armorIndex == MissionPrepUnitArmorVisualController.HeavyArmorIndex
			? UnitArmorType.Heavy
			: UnitArmorType.Light;

		SetArmor(type, true);
	}

	public void SetArmor(UnitArmorType _type, bool _resetDurability)
	{
		m_ArmorType = _type;
		m_MaxDurability = Mathf.Max(0f, m_MaxDurability);
		m_Durability = _type == UnitArmorType.None
			? 0f
			: (_resetDurability ? m_MaxDurability : Mathf.Clamp(m_Durability, 0f, m_MaxDurability));

		if (m_LogArmor)
		{
			Debug.Log(
				$"[Броня] {name} | установлена {_type} | прочность {m_Durability:F0}/{m_MaxDurability:F0} | {Condition}",
				this);
		}

		NotifyChanged();
	}

	public void ClearArmor()
	{
		SetArmor(UnitArmorType.None, true);
	}

	public string GetLocalizedStatusText()
	{
		if (!HasArmor)
			return string.Empty;

		if (IsDestroyed)
			return LocalizationManager.Get("armor.status.destroyed", "Броня разрушена");

		if (Condition == UnitArmorCondition.Damaged)
			return LocalizationManager.Get("armor.status.damaged", "Броня повреждена");

		return m_ArmorType == UnitArmorType.Heavy
			? LocalizationManager.Get("armor.status.heavy", LocalizationManager.Get("mission_prep.equipment.armor.heavy", "Тяжёлая броня"))
			: LocalizationManager.Get("armor.status.light", LocalizationManager.Get("mission_prep.equipment.armor.light", "Лёгкая броня"));
	}

	public ArmorMitigationResult TryMitigateBullet(BodyPartType _bodyPart, AmmoDefinition _ammo)
	{
		if (!HasArmor || IsDestroyed || _ammo == null)
		{
			if (m_LogArmor)
			{
				string reason = !HasArmor
					? "броня не экипирована"
					: IsDestroyed
						? "броня разрушена"
						: "нет данных патрона";
				Debug.Log(
					$"[Броня] {name} | зона={FormatBodyPart(_bodyPart)} | не проверяется ({reason})",
					this);
			}

			return ArmorMitigationResult.NotProtected;
		}

		float blockChance = GetBulletBlockChance(_bodyPart);
		if (blockChance <= 0f)
		{
			if (m_LogArmor)
			{
				Debug.Log(
					$"[Броня] {name} | зона={FormatBodyPart(_bodyPart)} | не защищена (тип={m_ArmorType})",
					this);
			}

			return ArmorMitigationResult.NotProtected;
		}

		float armorDamage = ResolveArmorDamage(_ammo);
		float appliedArmorDamage = armorDamage;
		bool blocked = UnityEngine.Random.value < blockChance;
		if (!blocked)
			appliedArmorDamage *= UnitArmorCombatDesign.FailedBlockArmorDamageMultiplier;

		float durabilityBefore = m_Durability;
		ApplyDurabilityDamage(appliedArmorDamage);

		if (m_LogArmor)
		{
			string tier = _ammo.Penetration >= UnitArmorCombatDesign.SniperPenetrationThreshold ? "снайпер" : "винтовка";
			Debug.Log(
				$"[Броня] {name} | зона={FormatBodyPart(_bodyPart)} | тип={m_ArmorType} | патрон={_ammo.name} ({tier}) | " +
				$"шанс={blockChance:P0} | результат={(blocked ? "БЛОК" : "ПРОБИТИЕ")} | урон плите={appliedArmorDamage:F1} | " +
				$"прочность {durabilityBefore:F0}->{m_Durability:F0} | {Condition}",
				this);
		}

		return blocked
			? ArmorMitigationResult.Blocked
			: ArmorMitigationResult.Penetrated;
	}

	public bool TryMitigateFragmentExplosive(BodyPartType _bodyPart, DamageSourceType _source)
	{
		if (m_ArmorType != UnitArmorType.Heavy || IsDestroyed)
			return false;
		if (_source != DamageSourceType.Fragment && _source != DamageSourceType.Explosive)
			return false;
		if (_bodyPart != BodyPartType.Chest && _bodyPart != BodyPartType.Neck && _bodyPart != BodyPartType.Abdomen)
			return false;

		return UnityEngine.Random.value < UnitArmorCombatDesign.HeavyFragmentExplosiveBlockChance;
	}
	#endregion

	#region Private Methods
	private UnitArmorCondition ResolveCondition()
	{
		if (IsDestroyed)
			return UnitArmorCondition.Destroyed;
		if (!HasArmor || m_MaxDurability <= 0f)
			return UnitArmorCondition.Intact;

		float ratio = m_Durability / m_MaxDurability;
		return ratio > UnitArmorCombatDesign.DamagedDurabilityRatio
			? UnitArmorCondition.Intact
			: UnitArmorCondition.Damaged;
	}

	private float GetBulletBlockChance(BodyPartType _bodyPart)
	{
		switch (m_ArmorType)
		{
			case UnitArmorType.Light:
				return _bodyPart == BodyPartType.Chest
					? UnitArmorCombatDesign.LightChestBulletBlockChance
					: 0f;

			case UnitArmorType.Heavy:
				switch (_bodyPart)
				{
					case BodyPartType.Chest:
						return UnitArmorCombatDesign.HeavyChestBulletBlockChance;
					case BodyPartType.Abdomen:
						return UnitArmorCombatDesign.HeavyAbdomenBulletBlockChance;
					default:
						return 0f;
				}

			default:
				return 0f;
		}
	}

	private float ResolveArmorDamage(AmmoDefinition _ammo)
	{
		float armorDamage = Mathf.Max(1f, _ammo.ArmorDamage);
		if (_ammo.Penetration >= UnitArmorCombatDesign.SniperPenetrationThreshold)
			armorDamage *= UnitArmorCombatDesign.SniperArmorDamageMultiplier;

		return armorDamage;
	}

	private void ApplyDurabilityDamage(float _damage)
	{
		if (_damage <= 0f)
			return;

		m_Durability = Mathf.Max(0f, m_Durability - _damage);
		NotifyChanged();
	}

	private void NotifyChanged()
	{
		Changed?.Invoke();
	}

	private static string FormatBodyPart(BodyPartType _bodyPart) =>
		BodyPartTypeUtility.GetDisplayName(_bodyPart);
	#endregion
}

public readonly struct ArmorMitigationResult
{
	public readonly bool WasProtected;
	public readonly bool FullyBlocked;

	private ArmorMitigationResult(bool _wasProtected, bool _fullyBlocked)
	{
		WasProtected = _wasProtected;
		FullyBlocked = _fullyBlocked;
	}

	public static ArmorMitigationResult NotProtected => new ArmorMitigationResult(false, false);
	public static ArmorMitigationResult Penetrated => new ArmorMitigationResult(true, false);
	public static ArmorMitigationResult Blocked => new ArmorMitigationResult(true, true);
}
