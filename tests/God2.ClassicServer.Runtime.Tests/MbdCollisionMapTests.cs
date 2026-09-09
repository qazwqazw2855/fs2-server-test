using System.Buffers.Binary;
using System.Text;
using God2.ClassicServer.Runtime;

namespace God2.ClassicServer.Runtime.Tests;

public sealed class MbdCollisionMapTests
{
    [Fact]
    public void Reader_recovers_v12_dimensions_cells_and_navigation_path()
    {
        var bytes = CreateMap(2, 1, (_, _, x, y) =>
            x == 10 && y != 10
                ? new MbdCollisionCell(ushort.MaxValue, ushort.MaxValue, ushort.MaxValue, 0)
                : new MbdCollisionCell(2, 1, 0, 0));

        var map = MbdCollisionMapReader.Read(bytes);
        var path = map.CreateNavigationGrid().FindPath(new NavigationPoint(1, 10), new NavigationPoint(39, 10));

        Assert.Equal("MBD v1.2", map.Magic);
        Assert.Equal(2, map.MacroWidth);
        Assert.Equal(41, map.Width);
        Assert.Equal(21, map.Height);
        Assert.Equal(NavigationResultCode.Success, path.ResultCode);
        Assert.All(path.Path, point => Assert.True(map.Cells[point.X, point.Y].IsWalkable));
    }

    [Fact]
    public void Reader_rejects_bad_magic_and_out_of_range_pointer()
    {
        Assert.Throws<InvalidDataException>(() => MbdCollisionMapReader.Read(new byte[16]));
        var bytes = CreateMap(1, 1, (_, _, _, _) => new MbdCollisionCell(2, 1, 0, 0));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => MbdCollisionMapReader.Read(bytes));
    }

    [Fact]
    public void Reader_merges_shared_border_deterministically()
    {
        var bytes = CreateMap(2, 1, (blockX, _, localX, _) =>
            blockX == 0 && localX == 20
                ? new MbdCollisionCell(ushort.MaxValue, ushort.MaxValue, ushort.MaxValue, 0)
                : new MbdCollisionCell(2, 1, 0, 0));

        var map = MbdCollisionMapReader.Read(bytes);

        Assert.Equal(21, map.OverlapConflictCount);
        Assert.False(map.Cells[20, 10].IsWalkable);
    }

    [Fact]
    public void Missing_block_cells_are_fail_closed_and_not_walkable()
    {
        var bytes = CreateMap(2, 1, (_, _, _, _) => new MbdCollisionCell(2, 1, 0, 0));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20, 4), 0);

        var map = MbdCollisionMapReader.Read(bytes);

        Assert.Equal(1, map.MissingBlockCount);
        Assert.False(map.Cells[39, 10].IsWalkable);
        Assert.False(map.CreateNavigationGrid().IsWalkable(new NavigationPoint(39, 10)));
    }

    [Fact]
    public void Reader_rejects_expanded_maps_above_allocation_budget()
    {
        const int side = 200;
        var bytes = new byte[16 + (side * side * 4)];
        Encoding.ASCII.GetBytes("MBD v1.2").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), side);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12, 4), side);

        Assert.Throws<InvalidDataException>(() => MbdCollisionMapReader.Read(bytes));
    }

    [Fact]
    public void Reader_rejects_block_pointer_into_header()
    {
        var bytes = CreateMap(1, 1, (_, _, _, _) => new MbdCollisionCell(2, 1, 0, 0));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 16);

        Assert.Throws<InvalidDataException>(() => MbdCollisionMapReader.Read(bytes));
    }

    [Fact]
    public void File_reader_rejects_oversized_source_before_loading_it()
    {
        var path = Path.Combine(Path.GetTempPath(), $"god2-oversized-{Guid.NewGuid():N}.mbd");
        try
        {
            using (var stream = File.Create(path))
            {
                stream.SetLength(MbdCollisionMapReader.MaximumSourceBytes + 1);
            }

            Assert.Throws<InvalidDataException>(() => MbdCollisionMapReader.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] CreateMap(
        int macroWidth,
        int macroHeight,
        Func<int, int, int, int, MbdCollisionCell> cellFactory)
    {
        var blocks = macroWidth * macroHeight;
        var header = 16 + (blocks * 4);
        var bytes = new byte[header + (blocks * MbdCollisionMapReader.BlockSize)];
        Encoding.ASCII.GetBytes("MBD v1.2").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), checked((uint)macroWidth));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12, 4), checked((uint)macroHeight));
        for (var blockY = 0; blockY < macroHeight; blockY++)
        {
            for (var blockX = 0; blockX < macroWidth; blockX++)
            {
                var blockIndex = (blockY * macroWidth) + blockX;
                var blockOffset = header + (blockIndex * MbdCollisionMapReader.BlockSize);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16 + (blockIndex * 4), 4), checked((uint)blockOffset));
                for (var y = 0; y < MbdCollisionMapReader.CellsPerBlockSide; y++)
                {
                    for (var x = 0; x < MbdCollisionMapReader.CellsPerBlockSide; x++)
                    {
                        var cell = cellFactory(blockX, blockY, x, y);
                        var offset = blockOffset + (((y * MbdCollisionMapReader.CellsPerBlockSide) + x) * MbdCollisionMapReader.CellSize);
                        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), cell.Height);
                        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 2, 2), cell.Surface);
                        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 4, 2), cell.Flags);
                        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 6, 2), cell.Reserved);
                    }
                }
            }
        }
        return bytes;
    }
}
