// Ecosphere — Spatial hash for organisms on planet icosphere cells (stage 05).
// Bounded native multi-map or cell head-next link array avoiding O(N^2) scans.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Ecosphere.Life
{
    /// <summary>
    /// Compact spatial hash mapping cellIndex -> linked list of entities.
    /// Burst-friendly, zero per-tick managed allocation.
    /// Rebuilt or updated each tick.
    /// </summary>
    public struct CellSpatialHash
    {
        // First entity index in cell (-1 if none)
        public NativeArray<int> CellHead;
        // Next entry index in linked list (-1 if end)
        public NativeArray<int> EntryNext;
        public NativeArray<Entity> EntryEntity;
        public NativeArray<int> EntryCell;
        public NativeArray<byte> EntryKingdom;
        public NativeArray<byte> EntryArchetype;
        public int Count;

        public CellSpatialHash(int cellCount, int maxEntries, Allocator allocator)
        {
            CellHead = new NativeArray<int>(cellCount, allocator);
            EntryNext = new NativeArray<int>(maxEntries, allocator);
            EntryEntity = new NativeArray<Entity>(maxEntries, allocator);
            EntryCell = new NativeArray<int>(maxEntries, allocator);
            EntryKingdom = new NativeArray<byte>(maxEntries, allocator);
            EntryArchetype = new NativeArray<byte>(maxEntries, allocator);
            Count = 0;
            Clear();
        }

        public void Clear()
        {
            for (int i = 0; i < CellHead.Length; i++)
            {
                CellHead[i] = -1;
            }
            Count = 0;
        }

        public bool TryAdd(int cellIndex, Entity entity, byte kingdom, byte archetype)
        {
            if (cellIndex < 0 || cellIndex >= CellHead.Length || Count >= EntryNext.Length)
                return false;

            int index = Count++;
            EntryEntity[index] = entity;
            EntryCell[index] = cellIndex;
            EntryKingdom[index] = kingdom;
            EntryArchetype[index] = archetype;
            EntryNext[index] = CellHead[cellIndex];
            CellHead[cellIndex] = index;
            return true;
        }

        public void Dispose()
        {
            if (CellHead.IsCreated) CellHead.Dispose();
            if (EntryNext.IsCreated) EntryNext.Dispose();
            if (EntryEntity.IsCreated) EntryEntity.Dispose();
            if (EntryCell.IsCreated) EntryCell.Dispose();
            if (EntryKingdom.IsCreated) EntryKingdom.Dispose();
            if (EntryArchetype.IsCreated) EntryArchetype.Dispose();
        }
    }
}
