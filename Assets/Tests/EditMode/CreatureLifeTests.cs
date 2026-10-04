// Ecosphere — EditMode tests for Stage 05 (Creature life).
// Needs drain/refill math, utility scoring preferences (starving -> Forage wins, blizzard -> TakeShelter/Flee),
// gait/speed from morphology, photosynthesis formula, allocation by season, dormancy trigger,
// predation resolution, detritus decay -> fertility.

using System;
using Ecosphere.Core.Simulation;
using Ecosphere.Life;
using NUnit.Framework;
using Unity.Mathematics;

namespace Ecosphere.Tests.EditMode
{
    [TestFixture]
    public class CreatureLifeTests
    {
        private Phenotype _animalPheno;
        private Phenotype _plantPheno;

        [SetUp]
        public void SetUp()
        {
            _animalPheno = new Phenotype
            {
                Size = 1.0f,
                SizeMultiplier = 1.0f,
                LimbCount = 4f,
                LimbLength = 0.5f,
                MetabolicRate = 1.0f,
                Endothermy = 1.0f, // Warm-blooded
                WaterNeed = 0.8f,
                DietHerbivory = 0.9f,
                DietCarnivory = 0.1f,
                DietScavenging = 0.2f,
                TemperatureOptimum = 20.0f,
                TemperatureTolerance = 10.0f,
                DigestiveEfficiency = 0.8f,
                MouthSize = 0.5f,
                InstinctFear = 0.5f,
                InstinctAggression = 0.2f,
                InstinctCuriosity = 0.5f,
                InstinctSociability = 0.5f
            };

            _plantPheno = new Phenotype
            {
                Size = 1.0f,
                SizeMultiplier = 1.0f,
                MetabolicRate = 0.5f,
                LeafCount = 20f,
                LeafSize = 0.5f,
                RootDepth = 0.6f,
                RootSpread = 0.6f,
                StemHeight = 0.8f,
                Woodiness = 0.5f,
                TemperatureOptimum = 22.0f,
                TemperatureTolerance = 12.0f,
                SeasonalGrowthGate = 0.8f
            };
        }

        [Test]
        public void Needs_AnimalDrainMath_DepletesEnergyHydrationRest()
        {
            var needs = NeedsData.CreateAnimalDefault();
            float initialEnergy = needs.Energy;
            float initialWater = needs.Hydration;
            float initialRest = needs.Rest;

            // Drain over 10 ticks at moving speed
            NeedsMath.DrainAnimalNeeds(ref needs, _animalPheno, LifeStage.Adult, 1.0f, 20.0f, 0.0f, 10.0f);

            Assert.Less(needs.Energy, initialEnergy, "Energy must decrease due to metabolism and movement.");
            Assert.Less(needs.Hydration, initialWater, "Hydration must decrease due to water need.");
            Assert.Less(needs.Rest, initialRest, "Rest must decrease when moving.");
            Assert.GreaterOrEqual(needs.ThermalComfort, 0.9f, "At optimum temperature, thermal comfort remains high.");
        }

        [Test]
        public void Needs_EndothermCold_BurnsMoreEnergy()
        {
            float warmFactor = NeedsMath.TemperatureMetabolismMultiplier(20.0f, 1.0f, 20.0f, 10.0f);
            float coldFactor = NeedsMath.TemperatureMetabolismMultiplier(0.0f, 1.0f, 20.0f, 10.0f);

            Assert.Greater(coldFactor, warmFactor, "Endotherm in freezing cold must ramp up metabolism for thermoregulation.");
        }

        [Test]
        public void UtilityScoring_StarvingHerbivore_PrefersGrazeOrForage()
        {
            var needs = NeedsData.CreateAnimalDefault();
            needs.Energy = 0.05f; // Starving!
            var weights = new NeedWeightsData { Energy = 2.0f, Safety = 1.0f, Hydration = 1.0f, Rest = 1.0f };
            var senses = new SensoryData { FoodQuantity = 1.0f, NearestPredatorDist = 999f, WaterDistance = 999f };
            var mlpBiases = new ActionBiases { Graze = 0.5f, Forage = 0.5f };

            float grazeScore = UtilityScoring.EvaluateAction(
                CreatureAction.Graze, needs, weights, senses, mlpBiases,
                20f, 0.5f, false, _animalPheno.DietHerbivory, _animalPheno.DietCarnivory, _animalPheno.DietScavenging, _animalPheno.Endothermy,
                out byte domNeed);

            float restScore = UtilityScoring.EvaluateAction(
                CreatureAction.Rest, needs, weights, senses, mlpBiases,
                20f, 0.5f, false, _animalPheno.DietHerbivory, _animalPheno.DietCarnivory, _animalPheno.DietScavenging, _animalPheno.Endothermy,
                out byte _);

            Assert.Greater(grazeScore, restScore, "Starving herbivore must strongly favor grazing over resting.");
            Assert.AreEqual(0, domNeed, "Dominant need must be Energy (index 0).");
        }

        [Test]
        public void UtilityScoring_PredatorNearbyOrBlizzard_PrefersFleeOrTakeShelter()
        {
            var needs = NeedsData.CreateAnimalDefault();
            needs.Safety = 0.1f; // High panic/safety urgency
            var weights = new NeedWeightsData { Safety = 2.5f, Energy = 1.0f, Rest = 1.0f };
            var senses = new SensoryData { NearestPredatorDist = 3.0f, WeatherHazard = 0.85f };
            var mlpBiases = new ActionBiases { Flee = 0.8f, TakeShelter = 0.7f };

            float fleeScore = UtilityScoring.EvaluateAction(
                CreatureAction.Flee, needs, weights, senses, mlpBiases,
                -5f, 0.5f, false, _animalPheno.DietHerbivory, _animalPheno.DietCarnivory, _animalPheno.DietScavenging, _animalPheno.Endothermy,
                out byte domNeed);

            float wanderScore = UtilityScoring.EvaluateAction(
                CreatureAction.Wander, needs, weights, senses, mlpBiases,
                -5f, 0.5f, false, _animalPheno.DietHerbivory, _animalPheno.DietCarnivory, _animalPheno.DietScavenging, _animalPheno.Endothermy,
                out byte _);

            Assert.Greater(fleeScore, wanderScore, "Impending predator and blizzard must trigger Flee over Wander.");
            Assert.AreEqual(4, domNeed, "Dominant need must be Safety (index 4).");
        }

        [Test]
        public void Locomotion_DeriveParamsFromMorphology_MatchesBudget()
        {
            LocomotionMath.DeriveParams(in _animalPheno, 1.0f, out LocomotionMode mode, out float baseSpeed, out float strideLen, out float strideFreq);

            Assert.AreEqual(LocomotionMode.Walk, mode, "Quadruped with 4 limbs must use Walk mode.");
            Assert.Greater(strideLen, 0.2f, "Stride length must reflect limb length and body scale.");
            Assert.Greater(strideFreq, 0.5f, "Stride frequency must reflect metabolic rate.");
            Assert.Greater(baseSpeed, 0.1f, "Base speed must be positive.");
        }

        [Test]
        public void PlantLife_PhotosynthesisAndSeasonalAllocation_WorksDeterministically()
        {
            float gross = PlantLifeMath.ComputePhotosynthesis(
                1.0f, 0.1f, 1.5f, 0.8f, 22.0f, _plantPheno.TemperatureOptimum, _plantPheno.TemperatureTolerance, _plantPheno.MetabolicRate,
                out float respiration);

            Assert.Greater(gross, respiration, "In peak sunlight at optimal temperature, photosynthesis must exceed respiration.");

            // Seasonal biomass allocation
            PlantLifeMath.ComputeBiomassAllocation(Season.Spring, _plantPheno, out float rSpring, out float sSpring, out float lSpring, out float fSpring);
            PlantLifeMath.ComputeBiomassAllocation(Season.Autumn, _plantPheno, out float rAutumn, out float sAutumn, out float lAutumn, out float fAutumn);

            Assert.Greater(lSpring, lAutumn, "Spring allocation must prioritize leaf biomass.");
            Assert.Greater(rAutumn, rSpring, "Autumn allocation must prioritize root/seed storage biomass.");
        }

        [Test]
        public void PlantLife_WinterDormancyTrigger()
        {
            bool winterDormant = PlantLifeMath.ShouldBeDormant(Season.Winter, -2.0f, _plantPheno.SeasonalGrowthGate);
            bool summerActive = PlantLifeMath.ShouldBeDormant(Season.Summer, 24.0f, _plantPheno.SeasonalGrowthGate);

            Assert.IsTrue(winterDormant, "Winter season must trigger plant dormancy.");
            Assert.IsFalse(summerActive, "Summer warm weather must remain active (not dormant).");
        }

        [Test]
        public void Ecology_PredationHandshake_ResolvesWithSpeedAndAggression()
        {
            var rng = SimRandom.ForStream(12345UL, 1);
            // Fast, aggressive predator vs slow prey
            bool kill = ResourceEcologyMath.ResolvePredatorAttack(2.0f, 1.0f, 0.2f, 0.1f, ref rng);
            Assert.IsTrue(kill, "Fast and aggressive predator must successfully kill slow defenseless prey.");
        }

        [Test]
        public void Ecology_DetritusDecaysToSoilFertility()
        {
            var res = new CellResources
            {
                VegetationBiomass = 1.0f,
                Detritus = 5.0f,
                SoilFertility = 0.2f
            };

            ResourceEcologyMath.DecayDetritusToFertility(ref res, 20.0f, 0.5f, 10.0f);

            Assert.Less(res.Detritus, 5.0f, "Detritus must decay over time.");
            Assert.Greater(res.SoilFertility, 0.2f, "Detritus decomposition must increase soil fertility, closing the ecosystem loop.");
        }
    }
}
