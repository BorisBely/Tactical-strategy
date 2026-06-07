/// <summary>
/// Класс дистанционного профиля оптического модуля.
/// Sweet spot задаёт диапазон, где множители остаются близкими к лучшему значению модуля.
/// </summary>
public enum OpticDistanceProfileKind
{
	/// <summary>Коллиматорный прицел: 0–15 м.</summary>
	Collimator = 0,

	/// <summary>Голографический прицел: 0–20 м.</summary>
	Holographic = 1,

	/// <summary>Гибридный прицел: 0–20 м и 35–45 м, с провалом между режимами.</summary>
	Hybrid = 2,

	/// <summary>Оптический прицел 1–6x: 0–60 м, медленнее 1–4x.</summary>
	VariableMagnification = 3,

	/// <summary>Оптический прицел 3x: 35–45 м.</summary>
	Scope3x = 4,

	/// <summary>Оптический прицел 4x: 40–50 м.</summary>
	Scope4x = 5,

	/// <summary>Снайперский прицел: 60–70 м.</summary>
	Scope4Long = 6,

	/// <summary>Снайперский прицел мод 1: 70–80 м.</summary>
	Scope5Long = 7,

	/// <summary>Снайперский прицел мод 2: 80–100 м.</summary>
	Scope9Long = 8,

	/// <summary>Стандартный коллиматорный прицел на боковую планку AK: 0–15 м.</summary>
	AkCollimator = 9,

	/// <summary>4-кратный оптический прицел на боковую планку AK: 40–50 м.</summary>
	AkPso = 10
}
