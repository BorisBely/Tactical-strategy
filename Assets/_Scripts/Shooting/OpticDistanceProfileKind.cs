/// <summary>
/// Класс дистанционного профиля оптического модуля.
/// Sweet spot задаёт диапазон, где множители остаются близкими к лучшему значению в нуле.
/// </summary>
public enum OpticDistanceProfileKind
{
	/// <summary>Коллиматор: лучше всего 0 м, приемлемо до 15 м.</summary>
	Collimator = 0,

	/// <summary>Голограф: лучше всего 0 м, приемлемо до 20 м.</summary>
	Holographic = 1,

	/// <summary>Гибрид: точность 0–20 и 40–70 м, но медленнее специализированных модулей.</summary>
	Hybrid = 2,

	/// <summary>Переменная кратность 1–6x: sweet spot 10–60 м.</summary>
	VariableMagnification = 3,

	/// <summary>Фиксированный 3x: sweet spot 20–40 м.</summary>
	Scope3x = 4,

	/// <summary>Фиксированный 4x: sweet spot 40–50 м.</summary>
	Scope4x = 5,

	/// <summary>Дальний Scope4: sweet spot 60–70 м.</summary>
	Scope4Long = 6,

	/// <summary>Дальний Scope5: sweet spot 70–80 м.</summary>
	Scope5Long = 7,

	/// <summary>Дальний Scope9: sweet spot 80–100 м.</summary>
	Scope9Long = 8,

	/// <summary>Коллиматор AK на боковой планке: 0–15 м.</summary>
	AkCollimator = 9,

	/// <summary>PSO / боковой оптический прицел AK: 50–60 м.</summary>
	AkPso = 10
}
