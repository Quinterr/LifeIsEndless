// Ecosphere — stage 07: managed gene catalog for the UI.
//
// The simulation reads the baked GeneCatalogBlob from the ECS singleton; the inspector wants
// the readable definition table (names, ranges, groups) on the managed side. Both are built
// from the same GeneCatalog.Create() table, so the genome viewer and the simulation can never
// disagree about what a gene means.

using Ecosphere.Core.Simulation.Genetics;
using UnityEngine;

namespace Ecosphere.UX
{
    /// <summary>Lazily built, read-only gene catalog shared by the panels.</summary>
    public static class GeneCatalogShared
    {
        private static GeneCatalog _catalog;

        public static GeneCatalog Current
        {
            get
            {
                if (_catalog != null) return _catalog;
                try
                {
                    _catalog = GeneCatalog.Create();
                }
                catch (System.Exception exception)
                {
                    Debug.LogWarning("Ecosphere: gene catalog unavailable (" + exception.Message + "); " +
                                     "the genome viewer falls back to raw values.");
                }
                return _catalog;
            }
        }
    }
}
