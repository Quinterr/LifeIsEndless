using System;
using System.IO;
using Unity.Entities;

namespace Ecosphere.Core.ECS.Save
{
    /// <summary>
    /// Architectural seam for snapshot save/load (full format arrives with stage 07).
    /// Keeping the interface now means stage 02+ systems can be written with
    /// serialization-friendliness in mind (plain component data, no pointers).
    /// </summary>
    public interface IWorldSerializer
    {
        void Save(World world, Stream destination);
        void Load(World world, Stream source);
    }

    /// <summary>
    /// Placeholder implementation: documents the contract and fails loudly if anything
    /// tries to persist a world before the real serializer exists.
    /// </summary>
    public sealed class WorldSerializerStub : IWorldSerializer
    {
        public void Save(World world, Stream destination)
        {
            throw new NotSupportedException(
                "World serialization is not implemented yet; the save format ships with stage 07 (Product UX & optimization).");
        }

        public void Load(World world, Stream source)
        {
            throw new NotSupportedException(
                "World deserialization is not implemented yet; the save format ships with stage 07 (Product UX & optimization).");
        }
    }
}
