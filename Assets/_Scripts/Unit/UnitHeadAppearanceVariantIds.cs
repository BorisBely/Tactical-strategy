public static class UnitHeadAppearanceVariantIds
{
	#region Male Hair
	public const int MaleHairBald = 0;
	public const int MaleHairShort04 = 1;
	public const int MaleHairLongBack02 = 2;
	public const int MaleHairRaised03 = 3;
	public const int MaleHairCurly05 = 4;
	public const int MaleHairMessy06 = 5;
	public const int MaleHairStylish07 = 6;
	public const int MaleHairShavedSides08 = 7;
	public const int MaleHairShavedSidesLong10 = 8;
	#endregion

	#region Female Hair
	public const int FemaleHair01 = 0;
	public const int FemaleHair02 = 1;
	public const int FemaleHair03 = 2;
	public const int FemaleHair04 = 3;
	public const int FemaleHairCap02 = 4;
	public const int FemaleHairCap02Alt = 5;
	public const int FemaleHairHelmetShort05 = 6;
	#endregion

	#region Hats
	public const int HatNone = 0;
	public const int Hat02 = 1;
	public const int Hat03 = 2;
	public const int Hat04 = 3;
	public const int Hat05 = 4;
	public const int Beanie01 = 5;
	#endregion

	#region Beard
	public const int BeardNone = 0;
	public const int Beard01 = 1;
	public const int Beard04Mustache = 2;
	public const int Beard04 = 3;
	public const int Beard09Mustache = 4;
	public const int Beard09 = 5;
	public const int Beard10 = 6;
	public const int Beard11 = 7;
	public const int Beard12 = 8;
	public const int Mustache01 = 9;
	#endregion

	#region Public Methods
	public static bool IsMaleHairHelmetCompatible(int _variant)
	{
		return _variant == MaleHairBald || _variant == MaleHairShort04;
	}

	public static bool CanUseStandaloneHat(CharacterGender _gender, int _hairVariant)
	{
		if (_gender == CharacterGender.Male)
			return _hairVariant == MaleHairBald || _hairVariant == MaleHairShort04;

		return _hairVariant == FemaleHairHelmetShort05;
	}
	#endregion
}
