#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase A MATH + SIM_PLAY log. Does not require hanging scripts on a unit.
/// Writes Assets/_Docs/Logs/Tests/RecoilPlayBaseline_LAST.txt
///
/// НОВЫЙ ПРОГОН / ТЕСТ: не добавлять ещё один [MenuItem] в Tools/Tests.
/// Вешать на Tools/Tests/▶ Current Recoil Check (RecoilCurrentCheckEditor).
/// Этот файл — замороженная фаза A и реализация, которую Current вызывает.
/// </summary>
public static class RecoilPlayBaselineReportEditor
{
	#region Constants
	private const string c_MenuAuto = "Tools/Tests/Run Recoil Play Baseline (Auto)";
	private const string c_MenuMath = "Tools/Tests/Run Recoil Play Calibration (Math)";
	private const string c_MenuPrepare = "Tools/Tests/Prepare Recoil Play Baseline";
	private const string c_WeaponFolder = "Assets/GameData/Shooting";
	private const string c_LmgFormLogFile = "RecoilPlayLmgForm_LAST.txt";
	private const string c_SemiFormLogFile = "RecoilPlaySemiForm_LAST.txt";
	private const string c_AutoFormLogFile = "RecoilPlayAutoForm_LAST.txt";
	private const string c_B11CrossClassLogFile = "RecoilPlayB11CrossClass_LAST.txt";
	private const string c_BalanceSheetN7LogFile = "RecoilBalanceSheet_N7_LAST.txt";
	private const string c_B13SanityLogFile = "RecoilPlayB13Sanity_LAST.txt";
	private const string c_C1StanceFormLogFile = "RecoilPlayC1StanceForm_LAST.txt";
	private const string c_C2PoseFormLogFile = "RecoilPlayC2PoseForm_LAST.txt";
	private const string c_D1ModuleFormLogFile = "RecoilPlayD1ModuleForm_LAST.txt";
	private const string c_D2ModulePairLogFile = "RecoilPlayD2ModulePair_LAST.txt";
	private const string c_E1PlannerFormLogFile = "RecoilPlayE1PlannerForm_LAST.txt";
	private const string c_E2PlannerDistanceLogFile = "RecoilPlayE2PlannerDistance_LAST.txt";
	private const string c_F1SpecialFormLogFile = "RecoilPlayF1SpecialForm_LAST.txt";
	private const string c_F2BenelliSpreadLogFile = "RecoilPlayF2BenelliSpread_LAST.txt";
	private const string c_N9AutoSelectorLogFile = "RecoilPlayN9AutoSelector_LAST.txt";
	private const string c_N13RecoilControlLogFile = "RecoilPlayN13RecoilControl_LAST.txt";
	private const string c_N14HipFireCurveLogFile = "RecoilPlayN14HipFireCurve_LAST.txt";
	#endregion

	#region Menu
	[MenuItem(c_MenuAuto, false, 27)]
	public static void RunAutoFromMenu()
	{
		RunAutoAndWrite("Auto");
	}

	/// <summary>Реализация текущего F2. Пункт меню — только Current Recoil Check.</summary>
	public static void RunF2BenelliSpreadFromMenu()
	{
		WeaponDefinition benelli = LoadWeapon(RecoilPlayF2BenelliSpreadRunner.BenelliWeaponAssetName);
		AmmoDefinition ammo = LoadAmmo(RecoilPlayF2BenelliSpreadRunner.Ammo12GaugeAssetName);
		string report = RecoilPlayF2BenelliSpreadRunner.Run(benelli, ammo);
		Directory.CreateDirectory(RecoilPlayBaselineProtocol.LogFolder);
		string path = Path.Combine(RecoilPlayBaselineProtocol.LogFolder, c_F2BenelliSpreadLogFile);
		File.WriteAllText(path, report, Encoding.UTF8);
		AssetDatabase.Refresh();
		Debug.Log("[RecoilPlayF2BenelliSpread]\n" + report);
	}

	/// <summary>Stage 1 N9 Auto selector. Пункт меню — только Current Recoil Check.</summary>
	public static void RunN9AutoSelectorFromMenu()
	{
		WeaponDefinition m4 = LoadWeapon(RecoilPlayN9AutoSelectorRunner.M4WeaponAssetName);
		WeaponDefinition ak47 = LoadWeapon(RecoilPlayN9AutoSelectorRunner.Ak47WeaponAssetName);
		AmmoDefinition ammo556 = LoadAmmo(RecoilPlayN9AutoSelectorRunner.Ammo556AssetName);
		AmmoDefinition ammo762 = LoadAmmo(RecoilPlayN9AutoSelectorRunner.Ammo762AssetName);
		string report = RecoilPlayN9AutoSelectorRunner.Run(m4, ammo556, ak47, ammo762);
		WriteNamedLog(c_N9AutoSelectorLogFile, report, "RecoilPlayN9AutoSelector");
	}

	/// <summary>Stage 1 N13 RecoilControl. Пункт меню — только Current Recoil Check.</summary>
	public static void RunN13RecoilControlFromMenu()
	{
		WeaponDefinition m4 = LoadWeapon(RecoilPlayN13RecoilControlRunner.M4WeaponAssetName);
		AmmoDefinition ammo556 = LoadAmmo(RecoilPlayN13RecoilControlRunner.Ammo556AssetName);
		string report = RecoilPlayN13RecoilControlRunner.Run(m4, ammo556);
		WriteNamedLog(c_N13RecoilControlLogFile, report, "RecoilPlayN13RecoilControl");
	}

	/// <summary>Stage 1 N14 HipFire distance curve. Пункт меню — только Current Recoil Check.</summary>
	public static void RunN14HipFireCurveFromMenu()
	{
		WeaponDefinition m4 = LoadWeapon(RecoilPlayN14HipFireCurveRunner.M4WeaponAssetName);
		AmmoDefinition ammo556 = LoadAmmo(RecoilPlayN14HipFireCurveRunner.Ammo556AssetName);
		string report = RecoilPlayN14HipFireCurveRunner.Run(m4, ammo556);
		WriteNamedLog(c_N14HipFireCurveLogFile, report, "RecoilPlayN14HipFireCurve");
	}

	/// <summary>Replay F1 special weapons MATH. Не Current.</summary>
	public static void RunF1SpecialFormFromMenu()
	{
		WeaponDefinition benelli = LoadWeapon(RecoilPlayF1SpecialFormRunner.BenelliWeaponAssetName);
		WeaponDefinition m2 = LoadWeapon(RecoilPlayF1SpecialFormRunner.M2WeaponAssetName);
		WeaponDefinition mk19 = LoadWeapon(RecoilPlayF1SpecialFormRunner.Mk19WeaponAssetName);
		string report = RecoilPlayF1SpecialFormRunner.Run(benelli, m2, mk19);
		Directory.CreateDirectory(RecoilPlayBaselineProtocol.LogFolder);
		string path = Path.Combine(RecoilPlayBaselineProtocol.LogFolder, c_F1SpecialFormLogFile);
		File.WriteAllText(path, report, Encoding.UTF8);
		AssetDatabase.Refresh();
		Debug.Log("[RecoilPlayF1SpecialForm]\n" + report);
	}

	/// <summary>Replay E2 distance sweep. Не Current.</summary>
	public static void RunE2PlannerDistanceFromMenu()
	{
		WeaponDefinition m4 = LoadWeapon(RecoilPlayE2PlannerDistanceRunner.M4WeaponAssetName);
		WeaponDefinition ak47 = LoadWeapon(RecoilPlayE2PlannerDistanceRunner.Ak47WeaponAssetName);
		string report = RecoilPlayE2PlannerDistanceRunner.Run(m4, ak47);
		Directory.CreateDirectory(RecoilPlayBaselineProtocol.LogFolder);
		string path = Path.Combine(RecoilPlayBaselineProtocol.LogFolder, c_E2PlannerDistanceLogFile);
		File.WriteAllText(path, report, Encoding.UTF8);
		AssetDatabase.Refresh();
		Debug.Log("[RecoilPlayE2PlannerDistance]\n" + report);
	}

	/// <summary>Replay E1 planner spot. Не Current.</summary>
	public static void RunE1PlannerFormFromMenu()
	{
		WeaponDefinition m4 = LoadWeapon(RecoilPlayE1PlannerFormRunner.M4WeaponAssetName);
		WeaponDefinition ak47 = LoadWeapon(RecoilPlayE1PlannerFormRunner.Ak47WeaponAssetName);
		string report = RecoilPlayE1PlannerFormRunner.Run(m4, ak47);
		Directory.CreateDirectory(RecoilPlayBaselineProtocol.LogFolder);
		string path = Path.Combine(RecoilPlayBaselineProtocol.LogFolder, c_E1PlannerFormLogFile);
		File.WriteAllText(path, report, Encoding.UTF8);
		AssetDatabase.Refresh();
		Debug.Log("[RecoilPlayE1PlannerForm]\n" + report);
	}

	/// <summary>Replay D2 module pairs. Не Current.</summary>
	public static void RunD2ModulePairFromMenu()
	{
		WeaponDefinition m4ModA2 = LoadWeapon(RecoilPlayD2ModulePairRunner.M4ModA2WeaponAssetName);
		WeaponAttachmentDefinition muzzle = LoadAttachment(RecoilPlayD2ModulePairRunner.M4MuzzleBrakeAssetName);
		WeaponAttachmentDefinition foreGrip = LoadAttachment(RecoilPlayD2ModulePairRunner.M4ForeGripAssetName);
		WeaponAttachmentDefinition stock = LoadAttachment(RecoilPlayD2ModulePairRunner.M4StockAssetName);
		string report = RecoilPlayD2ModulePairRunner.Run(m4ModA2, muzzle, foreGrip, stock);
		Directory.CreateDirectory(RecoilPlayBaselineProtocol.LogFolder);
		string path = Path.Combine(RecoilPlayBaselineProtocol.LogFolder, c_D2ModulePairLogFile);
		File.WriteAllText(path, report, Encoding.UTF8);
		AssetDatabase.Refresh();
		Debug.Log("[RecoilPlayD2ModulePair]\n" + report);
	}

	/// <summary>Replay D1 single module. Не Current.</summary>
	public static void RunD1ModuleFormFromMenu()
	{
		WeaponDefinition m4 = LoadWeapon(RecoilPlayBaselineProtocol.ReferenceWeaponAssetName);
		WeaponDefinition ak47 = LoadWeapon(RecoilPlayD1ModuleFormRunner.Ak47WeaponAssetName);
		WeaponDefinition m249 = LoadWeapon(RecoilPlayBaselineProtocol.M249WeaponAssetName);
		WeaponDefinition pkm = LoadWeapon(RecoilPlayBaselineProtocol.PkmWeaponAssetName);
		WeaponAttachmentDefinition m4Module = LoadAttachment(RecoilPlayD1ModuleFormRunner.M4MuzzleBrakeAssetName);
		WeaponAttachmentDefinition akModule = LoadAttachment(RecoilPlayD1ModuleFormRunner.AkMuzzleBrakeAssetName);
		string report = RecoilPlayD1ModuleFormRunner.Run(m4, m4Module, ak47, akModule, m249, pkm);
		Directory.CreateDirectory(RecoilPlayBaselineProtocol.LogFolder);
		string path = Path.Combine(RecoilPlayBaselineProtocol.LogFolder, c_D1ModuleFormLogFile);
		File.WriteAllText(path, report, Encoding.UTF8);
		AssetDatabase.Refresh();
		Debug.Log("[RecoilPlayD1ModuleForm]\n" + report);
	}

	/// <summary>Replay C2 pose. Не Current.</summary>
	public static void RunC2PoseFormFromMenu()
	{
		WeaponDefinition m4 = LoadWeapon(RecoilPlayBaselineProtocol.ReferenceWeaponAssetName);
		WeaponDefinition ak47 = LoadWeapon(RecoilPlayC2PoseFormRunner.Ak47WeaponAssetName);
		WeaponDefinition m249 = LoadWeapon(RecoilPlayBaselineProtocol.M249WeaponAssetName);
		WeaponDefinition pkm = LoadWeapon(RecoilPlayBaselineProtocol.PkmWeaponAssetName);
		string report = RecoilPlayC2PoseFormRunner.Run(m4, ak47, m249, pkm);
		Directory.CreateDirectory(RecoilPlayBaselineProtocol.LogFolder);
		string path = Path.Combine(RecoilPlayBaselineProtocol.LogFolder, c_C2PoseFormLogFile);
		File.WriteAllText(path, report, Encoding.UTF8);
		AssetDatabase.Refresh();
		Debug.Log("[RecoilPlayC2PoseForm]\n" + report);
	}

	/// <summary>Replay C1 stance. Не Current.</summary>
	public static void RunC1StanceFormFromMenu()
	{
		WeaponDefinition m4 = LoadWeapon(RecoilPlayBaselineProtocol.ReferenceWeaponAssetName);
		WeaponDefinition ak47 = LoadWeapon(RecoilPlayC1StanceFormRunner.Ak47WeaponAssetName);
		WeaponDefinition m249 = LoadWeapon(RecoilPlayBaselineProtocol.M249WeaponAssetName);
		WeaponDefinition pkm = LoadWeapon(RecoilPlayBaselineProtocol.PkmWeaponAssetName);
		string report = RecoilPlayC1StanceFormRunner.Run(m4, ak47, m249, pkm);
		Directory.CreateDirectory(RecoilPlayBaselineProtocol.LogFolder);
		string path = Path.Combine(RecoilPlayBaselineProtocol.LogFolder, c_C1StanceFormLogFile);
		File.WriteAllText(path, report, Encoding.UTF8);
		AssetDatabase.Refresh();
		Debug.Log("[RecoilPlayC1StanceForm]\n" + report);
	}

	/// <summary>Replay B13 sanity. Не Current.</summary>
	public static void RunB13SanityFromMenu()
	{
		WeaponDefinition m4 = LoadWeapon(RecoilPlayBaselineProtocol.ReferenceWeaponAssetName);
		WeaponDefinition ak47 = LoadWeapon(RecoilPlayB13SanityRunner.Ak47WeaponAssetName);
		WeaponDefinition m249 = LoadWeapon(RecoilPlayBaselineProtocol.M249WeaponAssetName);
		WeaponDefinition pkm = LoadWeapon(RecoilPlayBaselineProtocol.PkmWeaponAssetName);
		WeaponDefinition mk12 = LoadWeapon(RecoilPlayB13SanityRunner.Mk12WeaponAssetName);
		string report = RecoilPlayB13SanityRunner.Run(m4, ak47, m249, pkm, mk12);
		Directory.CreateDirectory(RecoilPlayBaselineProtocol.LogFolder);
		string path = Path.Combine(RecoilPlayBaselineProtocol.LogFolder, c_B13SanityLogFile);
		File.WriteAllText(path, report, Encoding.UTF8);
		AssetDatabase.Refresh();
		Debug.Log("[RecoilPlayB13Sanity]\n" + report);
	}

	/// <summary>Replay B12 N7 balance sheet. Не Current.</summary>
	public static void RunBalanceSheetN7FromMenu()
	{
		string[] guids = AssetDatabase.FindAssets("t:WeaponDefinition", new[] { c_WeaponFolder });
		if (guids.Length == 0)
			guids = AssetDatabase.FindAssets("t:WeaponDefinition");

		var weapons = new System.Collections.Generic.List<WeaponDefinition>(guids.Length);
		for (int i = 0; i < guids.Length; i++)
		{
			string path = AssetDatabase.GUIDToAssetPath(guids[i]);
			if (path.Replace('\\', '/').IndexOf("/Test/", System.StringComparison.OrdinalIgnoreCase) >= 0)
				continue;

			WeaponDefinition weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
			if (weapon != null)
				weapons.Add(weapon);
		}

		string report = RecoilBalanceSheetN7Runner.Build(weapons);
		Directory.CreateDirectory(RecoilPlayBaselineProtocol.LogFolder);
		string pathOut = Path.Combine(RecoilPlayBaselineProtocol.LogFolder, c_BalanceSheetN7LogFile);
		File.WriteAllText(pathOut, report, Encoding.UTF8);
		AssetDatabase.Refresh();
		Debug.Log("[RecoilBalanceSheet N7]\n" + report);
	}

	/// <summary>Replay B11 cross-class. Не Current.</summary>
	public static void RunB11CrossClassFromMenu()
	{
		WeaponDefinition m4 = LoadWeapon(RecoilPlayAutoFormRunner.M4WeaponAssetName);
		WeaponDefinition ak74 = LoadWeapon(RecoilPlayAutoFormRunner.Ak74WeaponAssetName);
		WeaponDefinition ak47 = LoadWeapon(RecoilPlayAutoFormRunner.Ak47WeaponAssetName);
		WeaponDefinition m249 = LoadWeapon(RecoilPlayBaselineProtocol.M249WeaponAssetName);
		WeaponDefinition pkm = LoadWeapon(RecoilPlayBaselineProtocol.PkmWeaponAssetName);
		string report = RecoilPlayB11CrossClassRunner.Run(m4, ak74, ak47, m249, pkm);
		Directory.CreateDirectory(RecoilPlayBaselineProtocol.LogFolder);
		string path = Path.Combine(RecoilPlayBaselineProtocol.LogFolder, c_B11CrossClassLogFile);
		File.WriteAllText(path, report, Encoding.UTF8);
		AssetDatabase.Refresh();
		Debug.Log("[RecoilPlayB11CrossClass]\n" + report);
	}

	/// <summary>Replay B10 Auto×. Не Current.</summary>
	public static void RunAutoFormFromMenu()
	{
		WeaponDefinition m4 = LoadWeapon(RecoilPlayAutoFormRunner.M4WeaponAssetName);
		WeaponDefinition ak74 = LoadWeapon(RecoilPlayAutoFormRunner.Ak74WeaponAssetName);
		WeaponDefinition ak47 = LoadWeapon(RecoilPlayAutoFormRunner.Ak47WeaponAssetName);
		WeaponDefinition m249 = LoadWeapon(RecoilPlayBaselineProtocol.M249WeaponAssetName);
		WeaponDefinition pkm = LoadWeapon(RecoilPlayBaselineProtocol.PkmWeaponAssetName);
		string report = RecoilPlayAutoFormRunner.Run(m4, ak74, ak47, m249, pkm);
		Directory.CreateDirectory(RecoilPlayBaselineProtocol.LogFolder);
		string path = Path.Combine(RecoilPlayBaselineProtocol.LogFolder, c_AutoFormLogFile);
		File.WriteAllText(path, report, Encoding.UTF8);
		AssetDatabase.Refresh();
		Debug.Log("[RecoilPlayAutoForm]\n" + report);
	}

	/// <summary>Replay B9 Semi×. Не Current.</summary>
	public static void RunSemiFormFromMenu()
	{
		WeaponDefinition m4 = LoadWeapon(RecoilPlaySemiFormRunner.M4WeaponAssetName);
		WeaponDefinition ak47 = LoadWeapon(RecoilPlaySemiFormRunner.Ak47WeaponAssetName);
		WeaponDefinition ak74 = LoadWeapon(RecoilPlaySemiFormRunner.Ak74WeaponAssetName);
		WeaponDefinition mk12 = LoadWeapon(RecoilPlaySemiFormRunner.Mk12WeaponAssetName);
		WeaponDefinition svd = LoadWeapon(RecoilPlaySemiFormRunner.SvdWeaponAssetName);
		string report = RecoilPlaySemiFormRunner.Run(m4, ak47, ak74, mk12, svd);
		Directory.CreateDirectory(RecoilPlayBaselineProtocol.LogFolder);
		string path = Path.Combine(RecoilPlayBaselineProtocol.LogFolder, c_SemiFormLogFile);
		File.WriteAllText(path, report, Encoding.UTF8);
		AssetDatabase.Refresh();
		Debug.Log("[RecoilPlaySemiForm]\n" + report);
	}

	/// <summary>Replay B7/B8 LMG. Не Current — только если нужна регрессия LMG.</summary>
	public static void RunLmgFormFromMenu()
	{
		WeaponDefinition m249 = LoadWeapon(RecoilPlayBaselineProtocol.M249WeaponAssetName);
		WeaponDefinition pkm = LoadWeapon(RecoilPlayBaselineProtocol.PkmWeaponAssetName);
		string report = RecoilPlayLmgFormRunner.Run(m249, pkm);
		Directory.CreateDirectory(RecoilPlayBaselineProtocol.LogFolder);
		string path = Path.Combine(RecoilPlayBaselineProtocol.LogFolder, c_LmgFormLogFile);
		File.WriteAllText(path, report, Encoding.UTF8);
		AssetDatabase.Refresh();
		Debug.Log("[RecoilPlayLmgForm]\n" + report);
	}

	[MenuItem(c_MenuMath, false, 28)]
	private static void RunMathFromMenu()
	{
		RunAutoAndWrite("Math/Auto");
	}

	[MenuItem(c_MenuPrepare, false, 29)]
	private static void PreparePlaySession()
	{
		Debug.LogWarning(
			"[RecoilPlayBaseline] Auto runner does not need Prepare or unit hang. " +
			"Use Tools/Tests/Run Recoil Play Baseline (Auto). Prepare remains only for optional human range.");
		if (!Application.isPlaying)
			return;

		ShootingRangeManager range = Object.FindAnyObjectByType<ShootingRangeManager>();
		GameObject host = range != null ? range.gameObject : new GameObject("RecoilPlayBaselineSession");
		RecoilPlayBaselineSession session = host.GetComponent<RecoilPlayBaselineSession>();
		if (session == null)
			session = host.AddComponent<RecoilPlayBaselineSession>();
		session.RefreshConditions();
		ShootingRangeHitLogger.LoggingEnabled = true;
		Debug.Log("[RecoilPlayBaseline] Optional human session on " + host.name);
	}
	#endregion

	#region Private Methods
	private static void RunAutoAndWrite(string _source)
	{
		WeaponDefinition m4 = LoadWeapon(RecoilPlayBaselineProtocol.ReferenceWeaponAssetName);
		WeaponDefinition m249 = LoadWeapon(RecoilPlayBaselineProtocol.M249WeaponAssetName);
		WeaponDefinition pkm = LoadWeapon(RecoilPlayBaselineProtocol.PkmWeaponAssetName);
		RecoilPlayBaselineAutoRunner.RunResult auto = RecoilPlayBaselineAutoRunner.Run(m4, m249, pkm);
		string report = RecoilPlayBaselineReport.Build(
			m4,
			m249,
			pkm,
			auto.PlaySection,
			auto.N8Section,
			auto.A1FiveShotGroupCm);
		WriteLog(report);
		Debug.Log("[RecoilPlayBaseline " + _source + "]\n" + report);
	}

	private static AmmoDefinition LoadAmmo(string _assetName)
	{
		string[] guids = AssetDatabase.FindAssets("t:AmmoDefinition " + _assetName, new[] { c_WeaponFolder });
		if (guids.Length == 0)
			guids = AssetDatabase.FindAssets("t:AmmoDefinition " + _assetName);
		for (int i = 0; i < guids.Length; i++)
		{
			string path = AssetDatabase.GUIDToAssetPath(guids[i]);
			AmmoDefinition ammo = AssetDatabase.LoadAssetAtPath<AmmoDefinition>(path);
			if (ammo != null && ammo.name == _assetName)
				return ammo;
		}

		return null;
	}

	private static WeaponDefinition LoadWeapon(string _assetName)
	{
		string[] guids = AssetDatabase.FindAssets("t:WeaponDefinition " + _assetName, new[] { c_WeaponFolder });
		if (guids.Length == 0)
			guids = AssetDatabase.FindAssets("t:WeaponDefinition " + _assetName);
		for (int i = 0; i < guids.Length; i++)
		{
			string path = AssetDatabase.GUIDToAssetPath(guids[i]);
			WeaponDefinition weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
			if (weapon != null && weapon.name == _assetName)
				return weapon;
		}

		return null;
	}

	private static WeaponAttachmentDefinition LoadAttachment(string _assetName)
	{
		string[] guids = AssetDatabase.FindAssets("t:WeaponAttachmentDefinition " + _assetName, new[] { c_WeaponFolder });
		if (guids.Length == 0)
			guids = AssetDatabase.FindAssets("t:WeaponAttachmentDefinition " + _assetName);
		for (int i = 0; i < guids.Length; i++)
		{
			string path = AssetDatabase.GUIDToAssetPath(guids[i]);
			WeaponAttachmentDefinition attachment = AssetDatabase.LoadAssetAtPath<WeaponAttachmentDefinition>(path);
			if (attachment != null && attachment.name == _assetName)
				return attachment;
		}

		return null;
	}

	private static void WriteLog(string _report)
	{
		WriteNamedLog(RecoilPlayBaselineProtocol.LogFileName, _report, "RecoilPlayBaseline");
	}

	private static void WriteNamedLog(string _fileName, string _report, string _logTag)
	{
		Directory.CreateDirectory(RecoilPlayBaselineProtocol.LogFolder);
		string path = Path.Combine(RecoilPlayBaselineProtocol.LogFolder, _fileName);
		File.WriteAllText(path, _report, Encoding.UTF8);
		AssetDatabase.Refresh();
		Debug.Log("[" + _logTag + "]\n" + _report);
	}
	#endregion
}
#endif
