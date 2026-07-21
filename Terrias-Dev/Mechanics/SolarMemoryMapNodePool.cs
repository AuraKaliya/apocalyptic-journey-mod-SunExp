using System.Collections.Generic;
using Witch;

namespace Terrias.Dll.Mechanics;

public sealed class SolarMemoryMapNodePool
{
    public SolarMemoryMapNodePool(
        int layer,
        int sourceLevel,
        int defaultSegmentSize,
        int selectSegmentSize,
        IReadOnlyList<MapTree.Node> defaultNodes,
        IReadOnlyList<MapTree.Node> selectNodes)
    {
        Layer = layer;
        SourceLevel = sourceLevel;
        DefaultSegmentSize = defaultSegmentSize;
        SelectSegmentSize = selectSegmentSize;
        DefaultNodes = defaultNodes;
        SelectNodes = selectNodes;
    }

    public int Layer { get; }

    public int SourceLevel { get; }

    public int DefaultSegmentSize { get; }

    public int SelectSegmentSize { get; }

    public IReadOnlyList<MapTree.Node> DefaultNodes { get; }

    public IReadOnlyList<MapTree.Node> SelectNodes { get; }
}
