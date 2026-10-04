using System;
using Ecosphere.Core.Simulation;
using NUnit.Framework;

namespace Ecosphere.Tests.EditMode
{
    [TestFixture]
    public class EvolutionModelTests
    {
        [Test]
        public void TraitMoments_AddRemoveAndVarianceRemainStable()
        {
            var moments = new TraitMoments();
            moments.Add(new EvolutionTraitVector { Size = 0.2f, ColdTolerance = 0.4f });
            moments.Add(new EvolutionTraitVector { Size = 0.8f, ColdTolerance = 0.6f });
            Assert.AreEqual(2, moments.Count);
            Assert.AreEqual(0.5f, moments.Mean.Size, 1e-6f);
            Assert.AreEqual(0.5f, moments.Mean.ColdTolerance, 1e-6f);
            Assert.AreEqual(0.18f, moments.Variance.Size, 1e-6f);

            moments.Remove(new EvolutionTraitVector { Size = 0.2f, ColdTolerance = 0.4f });
            Assert.AreEqual(1, moments.Count);
            Assert.AreEqual(0.8f, moments.Mean.Size, 1e-6f);
            Assert.AreEqual(0f, moments.Variance.Size, 0f);
        }

        [Test]
        public void SpeciesPhylogeny_RecordsExtinctionAndKeepsAnAcyclicTree()
        {
            var tree = new SpeciesPhylogeny();
            tree.RegisterFounder(1u, GeneKingdom.Animal, 0UL);
            tree.Fork(1u, 2u, GeneKingdom.Animal, 100UL, EvolutionCause.AdaptiveDivergence);
            tree.Fork(2u, 3u, GeneKingdom.Animal, 200UL, EvolutionCause.AdaptiveDivergence);
            tree.Extinguish(2u, 300UL);

            Assert.IsTrue(tree.IsAcyclic());
            Assert.AreEqual(2, tree.GetAncestors(3u).Count);
            Assert.IsTrue(tree.Nodes[1].IsExtinct);
            Assert.AreEqual(300UL, tree.Nodes[1].ExtinctAtTick);
            Assert.Throws<ArgumentException>(() =>
                tree.Fork(3u, 1u, GeneKingdom.Animal, 400UL, EvolutionCause.AdaptiveDivergence));
        }

        [Test]
        public void CellSpeciesLedger_AppliesBirthDeathAndMovementDeltas()
        {
            var ledger = new CellSpeciesPopulationLedger(4);
            ledger.ApplyDelta(0, 7u, 3, birthsDelta: 3, biomassDelta: 2f);
            ledger.ApplyDelta(1, 7u, 1, birthsDelta: 1, biomassDelta: 0.5f);
            ledger.ApplyDelta(0, 7u, -1, deathsDelta: 1, biomassDelta: -0.5f);
            Assert.AreEqual(3, ledger.TotalPopulation);
            Assert.AreEqual(2, ledger.Entries.Count);
            var byCell = ledger.AggregateByCell();
            Assert.AreEqual(2, byCell[0]);
            Assert.AreEqual(1, byCell[1]);
        }

        [Test]
        public void EnergyAuditChecksAllTrophicLedgers()
        {
            var audit = new EnergyFlowAudit
            {
                ProducerNpp = 100f,
                ProducerRespiration = 20f,
                HerbivoreIntake = 60f,
                ProducerStockDelta = 10f,
                ProducerDetritus = 10f,
                HerbivoreRespiration = 24f,
                PredatorIntake = 18f,
                HerbivoreStockDelta = 12f,
                HerbivoreDetritus = 6f,
                PredatorRespiration = 7.2f,
                PredatorStockDelta = 9f,
                PredatorDetritus = 1.8f,
                FertilityConversion = 12.46f,
                DetritusStockDelta = 5.34f,
                EcosystemExport = 0f
            };
            Assert.IsTrue(audit.IsBalanced(1e-4f));
            audit.PredatorStockDelta += 5f;
            Assert.IsFalse(audit.IsBalanced(1e-4f, 0.001f));
        }

        [Test]
        public void ScenarioFitnessProducesExpectedColdAndDroughtSelectionTrends()
        {
            EvolutionScenarioSettings ice = EvolutionScenarioSettings.Create(EvolutionScenarioKind.IceAgeTrend);
            ice.WorldSeed = 202604UL;
            ice.Years = 60;
            ice.PopulationSize = 64;
            HeadlessEvolutionResult coldResult = HeadlessEvolutionRunner.Run(ice);
            Assert.Greater(MeanTrait(coldResult, 60, vector => vector.ColdTolerance),
                MeanTrait(coldResult, 1, vector => vector.ColdTolerance) + 0.015f);

            EvolutionScenarioSettings drought = EvolutionScenarioSettings.Create(EvolutionScenarioKind.Drought);
            drought.WorldSeed = 202604UL;
            drought.Years = 60;
            drought.PopulationSize = 64;
            HeadlessEvolutionResult droughtResult = HeadlessEvolutionRunner.Run(drought);
            Assert.Greater(MeanTrait(droughtResult, 60, vector => vector.RootDepth),
                MeanTrait(droughtResult, 1, vector => vector.RootDepth) + 0.015f);
            Assert.Greater(MeanTrait(droughtResult, 60, vector => vector.WaterEfficiency),
                MeanTrait(droughtResult, 1, vector => vector.WaterEfficiency) + 0.01f);
        }

        [Test]
        public void PredatorPressure_ProducesExpectedDefenseTraitTrend()
        {
            EvolutionScenarioSettings settings = EvolutionScenarioSettings.Create(EvolutionScenarioKind.PredatorPressure);
            settings.WorldSeed = 202604UL;
            settings.Years = 60;
            settings.PopulationSize = 64;
            HeadlessEvolutionResult result = HeadlessEvolutionRunner.Run(settings);

            Assert.Greater(MeanTrait(result, 60, vector => vector.Speed),
                MeanTrait(result, 1, vector => vector.Speed) + 0.015f);
            Assert.Greater(MeanTrait(result, 60, vector => vector.Vigilance),
                MeanTrait(result, 1, vector => vector.Vigilance) + 0.01f);
            Assert.AreEqual(61, result.PreySeries.Length);
            Assert.AreEqual(61, result.PredatorSeries.Length);
        }

        [Test]
        public void ControlledRegionalDivergence_RecordsSpeciationAndAcyclicPhylogeny()
        {
            EvolutionScenarioSettings settings = EvolutionScenarioSettings.Create(EvolutionScenarioKind.Default);
            settings.WorldSeed = 117UL;
            settings.Years = 6;
            settings.PopulationSize = 96;
            settings.SpeciationDriftThreshold = 0.01f;
            settings.MateCompatibilityThreshold = 0.01f;
            settings.SpeciationFailureFraction = 0f;
            settings.MinimumSpeciationPopulation = 2;
            HeadlessEvolutionResult result = HeadlessEvolutionRunner.Run(settings);

            bool foundSpeciation = false;
            for (int i = 0; i < result.Events.Count; i++)
                foundSpeciation |= result.Events[i].Kind == EvolutionEventKind.Speciation;
            Assert.IsTrue(foundSpeciation, "The controlled divergence should append a speciation event.");
            Assert.Greater(result.Phylogeny.Edges.Count, 0);
            Assert.IsTrue(result.Phylogeny.IsAcyclic());
        }

        [Test]
        public void DefaultHundredYearRun_HasSurvivorsFiniteMetricsAndExportableEvents()
        {
            EvolutionScenarioSettings settings = EvolutionScenarioSettings.Create(EvolutionScenarioKind.Default);
            HeadlessEvolutionResult result = HeadlessEvolutionRunner.Run(settings);

            Assert.AreEqual(100, settings.Years);
            Assert.Greater(result.FinalPopulation, 0);
            Assert.Greater(result.LivingSpecies, 0);
            Assert.IsTrue(result.HasFiniteMetrics);
            Assert.IsTrue(result.Phylogeny.IsAcyclic());
            Assert.AreEqual(settings.Years, result.EnergyAudits.Count);
            for (int i = 0; i < result.EnergyAudits.Count; i++)
                Assert.IsTrue(result.EnergyAudits[i].IsBalanced(1e-4f));
            Assert.Greater(result.Events.Count, 0);
            StringAssert.StartsWith("year,species_id,parent_species_id,population", result.ToCsv());
            Assert.IsTrue(result.ToCsv().Contains("\n100,"));
            foreach (EvolutionEventRecord value in result.Events)
                if (value.Kind == EvolutionEventKind.Extinction)
                    Assert.AreNotEqual(EvolutionCause.Unspecified, value.Cause);
        }

        [Test]
        public void MassExtinctionScenario_EmitsDieOffRecoveryAndRetainsAcyclicPhylogeny()
        {
            EvolutionScenarioSettings settings = EvolutionScenarioSettings.Create(EvolutionScenarioKind.MassExtinctionRecovery);
            settings.WorldSeed = 81UL;
            settings.Years = 12;
            settings.PopulationSize = 32;
            settings.BottleneckStartYear = 3;
            settings.BottleneckEndYear = 4;
            HeadlessEvolutionResult result = HeadlessEvolutionRunner.Run(settings);

            bool sawDieOff = false, sawRecovery = false;
            for (int i = 0; i < result.Events.Count; i++)
            {
                sawDieOff |= result.Events[i].Kind == EvolutionEventKind.MassDieOff;
                sawRecovery |= result.Events[i].Kind == EvolutionEventKind.Recovery;
            }
            Assert.IsTrue(sawDieOff);
            Assert.IsTrue(sawRecovery);
            Assert.Greater(result.FinalPopulation, 0);
            Assert.IsTrue(result.Phylogeny.IsAcyclic());
            Assert.IsTrue(result.HasFiniteMetrics);
        }

        private static float MeanTrait(HeadlessEvolutionResult result, int year,
            Func<EvolutionTraitVector, float> selector)
        {
            double sum = 0;
            int population = 0;
            for (int i = 0; i < result.Metrics.Count; i++)
            {
                EvolutionMetricsRecord metric = result.Metrics[i];
                if (metric.Year != year) continue;
                sum += selector(metric.MeanTraits) * metric.Population;
                population += metric.Population;
            }
            Assert.Greater(population, 0, "Expected a population metric for year " + year);
            return (float)(sum / population);
        }
    }
}
