#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Starting Vertical/Horizontal/Recovery values for the offset recoil model (degrees).
/// RecoilRecoveryPerSecond here is °/s, not the old dimensionless P units.
/// </summary>
public static class WeaponRecoilAssetDefaults
{
	#region Nested Types
	public readonly struct Values
	{
		public readonly float Vertical;
		public readonly float Horizontal;
		public readonly float RecoveryPerSecond;
		public readonly float PatternSeed;

		public Values(float _vertical, float _horizontal, float _recoveryPerSecond, float _patternSeed)
		{
			Vertical = _vertical;
			Horizontal = _horizontal;
			RecoveryPerSecond = _recoveryPerSecond;
			PatternSeed = _patternSeed;
		}
	}
	#endregion

	#region Public Methods
	public static Values ForAssetName(string _weaponAssetName)
	{
		string name = _weaponAssetName ?? string.Empty;
		float seed = PatternSeedFromName(name);
		if (Contains(name, "MK19"))
			return new Values(0.22f, 0.12f, 0.28f, seed);
		if (Contains(name, "M2Browning"))
			return new Values(0.18f, 0.10f, 0.35f, seed);
		if (Contains(name, "PKM"))
			return new Values(0.08f, 0.13f, 0.50f, seed);
		if (Contains(name, "M249"))
			return new Values(0.07f, 0.11f, 0.50f, seed);
		if (Contains(name, "RPK74"))
			return new Values(0.08f, 0.085f, 0.80f, seed);
		if (Contains(name, "RPK47"))
			return new Values(0.09f, 0.09f, 0.75f, seed);
		if (Contains(name, "AK47"))
			return new Values(0.14f, 0.075f, 0.55f, seed);
		if (Contains(name, "AK74U"))
			return new Values(0.115f, 0.055f, 0.65f, seed);
		if (Contains(name, "AK74"))
			return new Values(0.12f, 0.06f, 0.60f, seed);
		if (Contains(name, "SVD"))
			return new Values(0.16f, 0.04f, 0.50f, seed);
		if (Contains(name, "Mosin"))
			return new Values(0.22f, 0.05f, 0.40f, seed);
		if (Contains(name, "Sniper762"))
			return new Values(0.14f, 0.035f, 0.55f, seed);
		if (Contains(name, "Benelli"))
			return new Values(0.20f, 0.08f, 0.60f, seed);
		if (Contains(name, "MK12"))
			return new Values(0.08f, 0.03f, 0.55f, seed);
		if (Contains(name, "MK18"))
			return new Values(0.09f, 0.035f, 0.75f, seed);
		if (Contains(name, "M16") || Contains(name, "M4"))
			return new Values(0.09f, 0.035f, 0.70f, seed);
		return new Values(0.10f, 0.04f, 0.65f, seed);
	}

	public static void Write(SerializedObject _weaponSo, string _weaponAssetName)
	{
		if (_weaponSo == null)
			return;

		Values values = ForAssetName(_weaponAssetName);
		SetFloat(_weaponSo, "m_VerticalRecoil", values.Vertical);
		SetFloat(_weaponSo, "m_HorizontalRecoil", values.Horizontal);
		SetFloat(_weaponSo, "m_RecoilPatternSeed", values.PatternSeed);
		SetFloat(_weaponSo, "m_RecoilRecoveryPerSecond", values.RecoveryPerSecond);
	}

	[MenuItem("Polygone/Shooting/Migrate Recoil Offset Fields")]
	private static void MigrateAllWeaponAssets()
	{
		EditorUtility.DisplayDialog(
			"Migrate Recoil Offset Fields — blocked",
			"Глобальная запись V/H/Rec на все стволы заморожена после A10.\n" +
			"Она затрёт калибровку M249 / PKM / MK12.\n\n" +
			"Сверка и точечный §23.2: Polygone/Shooting/Recoil Doc Migration.",
			"OK");
		Debug.LogWarning(
			"[WeaponRecoilAssetDefaults] Global migrate blocked. Use Recoil Doc Migration instead.");
	}
	#endregion

	#region Private Methods
	private static void SetFloat(SerializedObject _so, string _propertyName, float _value)
	{
		SerializedProperty property = _so.FindProperty(_propertyName);
		if (property != null)
			property.floatValue = _value;
	}

	private static bool Contains(string _name, string _token)
	{
		return _name.IndexOf(_token, System.StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static float PatternSeedFromName(string _name)
	{
		unchecked
		{
			int hash = 23;
			for (int i = 0; i < _name.Length; i++)
				hash = hash * 31 + _name[i];
			return Mathf.Abs(hash % 1000) * 0.01f;
		}
	}
	#endregion
}
#endif
