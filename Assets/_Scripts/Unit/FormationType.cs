/// <summary>Тип группового строя для RTS-юнитов (2+ в выделении).</summary>
public enum FormationType
{
	/// <summary>Не используется для группы; одиночный юнит.</summary>
	None = 0,
	/// <summary>1. По одному — колонна один за другим.</summary>
	SingleFile = 1,
	/// <summary>2. По двое — колонна парами.</summary>
	DoubleFile = 2,
	/// <summary>3. Тактическая колонна — зигзаг.</summary>
	TacticalColumn = 3,
	/// <summary>4. Клин.</summary>
	Wedge = 4,
	/// <summary>5. Широкий клин разведки.</summary>
	WideReconWedge = 5,
	/// <summary>6. Линия — шеренга.</summary>
	Line = 6,
	/// <summary>7. Алмаз / многоугольник.</summary>
	Diamond = 7,
}
