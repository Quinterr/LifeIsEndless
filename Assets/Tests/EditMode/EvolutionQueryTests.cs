using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using NUnit.Framework;
using Unity.Entities;

namespace Ecosphere.Tests.EditMode
{
    [TestFixture]
    public class EvolutionQueryTests
    {
        private World _world;
        private Entity _planet;

        [SetUp]
        public void SetUp()
        {
            _world = new World("EvolutionQueryTests");
            EntityManager em = _world.EntityManager;
            _planet = em.CreateEntity();
            em.AddComponentData(_planet, EvolutionStateData.Default);
            em.AddBuffer<SpeciesPopulationRecord>(_planet);
            em.AddBuffer<CellSpeciesPopulation>(_planet);
            em.AddBuffer<EvolutionEventElement>(_planet);
            em.AddBuffer<EvolutionMetricElement>(_planet);
            em.AddBuffer<EvolutionPhylogenyEdge>(_planet);
            em.AddBuffer<LineageRecord>(_planet);
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public void QueryReturnsCopiedSpeciesPopulationMetricsEventsAndLineage()
        {
            EntityManager em = _world.EntityManager;
            em.GetBuffer<SpeciesPopulationRecord>(_planet).Add(new SpeciesPopulationRecord
            {
                SpeciesId = 1u,
                Kingdom = GeneKingdom.Animal,
                Population = 4
            });
            em.GetBuffer<SpeciesPopulationRecord>(_planet).Add(new SpeciesPopulationRecord
            {
                SpeciesId = 2u,
                ParentSpeciesId = 1u,
                Kingdom = GeneKingdom.Animal,
                Population = 2
            });
            em.GetBuffer<CellSpeciesPopulation>(_planet).Add(new CellSpeciesPopulation
            {
                SpeciesId = 2u,
                CellIndex = 7,
                Population = 2
            });
            em.GetBuffer<EvolutionPhylogenyEdge>(_planet).Add(new EvolutionPhylogenyEdge
            {
                ParentSpeciesId = 1u,
                ChildSpeciesId = 2u,
                Tick = 99UL,
                Cause = EvolutionCause.AdaptiveDivergence
            });
            em.GetBuffer<EvolutionMetricElement>(_planet).Add(new EvolutionMetricElement
            {
                Value = new EvolutionMetricsRecord { Year = 3, SpeciesId = 2u, Population = 2 }
            });
            em.GetBuffer<EvolutionEventElement>(_planet).Add(new EvolutionEventElement
            {
                Value = new EvolutionEventRecord
                {
                    Kind = EvolutionEventKind.Speciation,
                    SpeciesId = 2u,
                    ParentSpeciesId = 1u
                }
            });
            em.GetBuffer<LineageRecord>(_planet).Add(new LineageRecord
            {
                OrganismId = 101UL,
                MotherId = 50UL,
                FatherId = 51UL,
                SpeciesId = 2u
            });

            var query = new EvolutionQuery(em, _planet);
            Assert.AreEqual(6, query.TotalPopulation);
            Assert.AreEqual(2, query.LivingSpeciesCount);
            Assert.IsTrue(query.TryGetSpecies(2u, out SpeciesPopulationRecord species));
            Assert.AreEqual(1u, species.ParentSpeciesId);
            Assert.AreEqual(1, query.GetCellPopulation(7).Count);
            Assert.AreEqual(1, query.GetEvents(2u).Count);
            Assert.IsTrue(query.TryGetLatestMetric(2u, out EvolutionMetricsRecord metric));
            Assert.AreEqual(3, metric.Year);
            Assert.AreEqual(1, query.GetLineage(101UL).Count);
            Assert.IsTrue(query.IsPhylogenyAcyclic());

            var snapshot = query.GetSpecies();
            snapshot.Clear();
            Assert.AreEqual(2, query.GetSpecies().Count, "Returned data is a detached snapshot.");
        }

        [Test]
        public void QueryRejectsAnEntityWithoutEvolutionState()
        {
            Entity invalid = _world.EntityManager.CreateEntity();
            Assert.Throws<System.ArgumentException>(() => new EvolutionQuery(_world.EntityManager, invalid));
        }
    }
}
