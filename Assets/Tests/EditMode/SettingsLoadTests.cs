using Ecosphere.Authoring;
using Ecosphere.Core.Simulation;
using NUnit.Framework;
using UnityEditor;

namespace Ecosphere.Tests.EditMode
{
    /// <summary>
    /// Acceptance: settings load — WorldSettings and GameBalance ScriptableObjects are
    /// committed assets, loadable in EditMode with the brief's defaults.
    /// </summary>
    [TestFixture]
    public class SettingsLoadTests
    {
        private const string WorldSettingsPath = "Assets/Ecosphere/Config/WorldSettings.asset";
        private const string GameBalancePath = "Assets/Ecosphere/Config/GameBalance.asset";

        [Test]
        public void WorldSettings_LoadsWithBriefDefaults()
        {
            var settings = AssetDatabase.LoadAssetAtPath<WorldSettings>(WorldSettingsPath);
            Assert.IsNotNull(settings, $"Missing committed asset at {WorldSettingsPath}.");

            Assert.AreEqual(42UL, settings.WorldSeed);
            Assert.AreEqual(120u, settings.SecondsPerGameDay);
            Assert.AreEqual(10u, settings.SimTicksPerSecond);
            Assert.AreEqual(15u, settings.DaysPerSeason);
            Assert.AreEqual(4u, settings.SeasonsPerYear);
            Assert.AreEqual(10000, settings.MaxOrganisms);
            Assert.AreEqual(Locale.En, settings.Locale);
        }

        [Test]
        public void WorldSettings_ConvertsToClockConfig()
        {
            var settings = AssetDatabase.LoadAssetAtPath<WorldSettings>(WorldSettingsPath);
            Assert.IsNotNull(settings);

            ClockConfig cfg = settings.ToClockConfig();
            Assert.AreEqual(1200u, cfg.TicksPerDay);
            Assert.AreEqual(60u, cfg.DaysPerYear);
            Assert.AreEqual(0.1, cfg.SecondsPerTick, 1e-9);
        }

        [Test]
        public void WorldSettings_DefaultComponentData_MatchesAsset()
        {
            var settings = AssetDatabase.LoadAssetAtPath<WorldSettings>(WorldSettingsPath);
            Assert.IsNotNull(settings);

            var fromAsset = settings.ToComponentData();
            var fromDefaults = WorldSettings.DefaultComponentData();
            Assert.AreEqual(fromDefaults.WorldSeed, fromAsset.WorldSeed);
            Assert.AreEqual(fromDefaults.SecondsPerGameDay, fromAsset.SecondsPerGameDay);
            Assert.AreEqual(fromDefaults.SimTicksPerSecond, fromAsset.SimTicksPerSecond);
            Assert.AreEqual(fromDefaults.DaysPerSeason, fromAsset.DaysPerSeason);
            Assert.AreEqual(fromDefaults.SeasonsPerYear, fromAsset.SeasonsPerYear);
        }

        [Test]
        public void GameBalance_LoadsWithAllPlaceholderSections()
        {
            var balance = AssetDatabase.LoadAssetAtPath<GameBalance>(GameBalancePath);
            Assert.IsNotNull(balance, $"Missing committed asset at {GameBalancePath}.");

            Assert.IsNotNull(balance.Climate);
            Assert.IsNotNull(balance.Plants);
            Assert.IsNotNull(balance.Animals);
            Assert.IsNotNull(balance.Evolution);
            Assert.AreEqual(1f, balance.Climate.TemperatureScale);
            Assert.AreEqual(1f, balance.Plants.GrowthRateScale);
            Assert.AreEqual(1f, balance.Animals.MetabolismScale);
            Assert.AreEqual(1f, balance.Evolution.MutationRateScale);
        }
    }
}
