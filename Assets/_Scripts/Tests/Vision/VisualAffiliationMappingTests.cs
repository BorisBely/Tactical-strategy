using NUnit.Framework;

namespace Vision.Tests
{
	public sealed class VisualAffiliationMappingTests
	{
		[TestCase(VisualAffiliation.Unknown, UnitTeamId.Player, ObservableAffiliation.Unknown)]
		[TestCase(VisualAffiliation.Unknown, UnitTeamId.Enemy, ObservableAffiliation.Unknown)]
		[TestCase(VisualAffiliation.Unknown, UnitTeamId.Neutral, ObservableAffiliation.Unknown)]
		[TestCase(VisualAffiliation.Civilian, UnitTeamId.Player, ObservableAffiliation.Neutral)]
		[TestCase(VisualAffiliation.Civilian, UnitTeamId.Enemy, ObservableAffiliation.Neutral)]
		[TestCase(VisualAffiliation.Civilian, UnitTeamId.Neutral, ObservableAffiliation.Neutral)]
		[TestCase(VisualAffiliation.Player, UnitTeamId.Player, ObservableAffiliation.Friendly)]
		[TestCase(VisualAffiliation.Player, UnitTeamId.Enemy, ObservableAffiliation.Hostile)]
		[TestCase(VisualAffiliation.Player, UnitTeamId.Neutral, ObservableAffiliation.Neutral)]
		[TestCase(VisualAffiliation.Enemy, UnitTeamId.Player, ObservableAffiliation.Hostile)]
		[TestCase(VisualAffiliation.Enemy, UnitTeamId.Enemy, ObservableAffiliation.Friendly)]
		[TestCase(VisualAffiliation.Enemy, UnitTeamId.Neutral, ObservableAffiliation.Neutral)]
		public void ToCue_MatchesObserverSideTable(
			VisualAffiliation _look,
			UnitTeamId _observerSide,
			ObservableAffiliation _expected)
		{
			Assert.AreEqual(_expected, VisualAffiliationMapping.ToCue(_look, _observerSide));
		}

		[Test]
		public void DefaultLookForTeam_IsContentOnlyMapping()
		{
			Assert.AreEqual(VisualAffiliation.Player, VisualAffiliationMapping.DefaultLookForTeam(UnitTeamId.Player));
			Assert.AreEqual(VisualAffiliation.Enemy, VisualAffiliationMapping.DefaultLookForTeam(UnitTeamId.Enemy));
			Assert.AreEqual(VisualAffiliation.Civilian, VisualAffiliationMapping.DefaultLookForTeam(UnitTeamId.Neutral));
		}
	}
}
