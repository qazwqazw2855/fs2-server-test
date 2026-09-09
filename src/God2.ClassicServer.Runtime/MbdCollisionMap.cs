using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace God2.ClassicServer.Runtime;

public readonly record struct MbdCollisionCell(ushort Height, ushort Surface, ushort Flags, ushort Reserved)
{
    public bool IsWalkable => Height != ushort.MaxValue && Surface != ushort.MaxValue && Flags != ushort.MaxValue;
}

public sealed record MbdCollisionMap(
    string Magic,
    int MacroWidth,
    int MacroHeight,
    int Width,
    int Height,
    IReadOnlyList<uint> BlockOffsets,
    MbdCollisionCell[,] Cells,
    int MissingBlockCount,
    int OverlapConflictCount,
    string SourceSha256)
{
    public int WalkableCellCount
    {
        get
        {
            var count = 0;
            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    if (Cells[x, y].IsWalkable)
                    {
                        count++;
                    }
                }
            }
            return count;
        }
    }

    public NavigationGrid CreateNavigationGrid()
    {
        var walkable = new bool[Width, Height];
        for (var x = 0; x < Width; x++)
        {
            for (var y = 0; y < Height; y++)
            {
                walkable[x, y] = Cells[x, y].IsWalkable;
            }
        }
        return new NavigationGrid(walkable);
    }
}

public static class MbdCollisionMapReader
{
    public const int CellsPerBlockSide = 21;
    public const int BlockStride = 20;
    public const int CellSize = 8;
    public const int BlockSize = CellsPerBlockSide * CellsPerBlockSide * CellSize;
    public const long MaximumCellCount = 4_000_000;
    public const long MaximumSourceBytes = 64L * 1024 * 1024;
    private static readonly MbdCollisionCell MissingCell = new(ushort.MaxValue, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue);

    public static MbdCollisionMap Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException("MBD source file does not exist.", file.FullName);
        }
        if (file.Length > MaximumSourceBytes)
        {
            throw new InvalidDataException($"MBD source exceeds the {MaximumSourceBytes:N0}-byte input budget.");
        }
        var bytes = new byte[checked((int)file.Length)];
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
        {
            throw new InvalidDataException("MBD source changed while it was being read.");
        }
        return Read(bytes);
    }

    public static MbdCollisionMap Read(ReadOnlyMemory<byte> source)
    {
        var bytes = source.Span;
        if (bytes.Length > MaximumSourceBytes)
        {
            throw new InvalidDataException($"MBD source exceeds the {MaximumSourceBytes:N0}-byte input budget.");
        }
        if (bytes.Length < 16)
        {
            throw new InvalidDataException("MBD header is truncated.");
        }

        var magic = Encoding.ASCII.GetString(bytes[..8]).TrimEnd('\0');
        if (!string.Equals(magic, "MBD v1.2", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported MBD magic '{magic}'.");
        }

        var macroWidth = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(8, 4)));
        var macroHeight = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(12, 4)));
        if (macroWidth is < 1 or > 512 || macroHeight is < 1 or > 512)
        {
            throw new InvalidDataException("MBD macro dimensions are outside the bounded parser limit.");
        }

        var blockCount = checked(macroWidth * macroHeight);
        var pointerTableEnd = checked(16 + (blockCount * sizeof(uint)));
        if (pointerTableEnd > bytes.Length)
        {
            throw new InvalidDataException("MBD pointer table is truncated.");
        }

        var offsets = new uint[blockCount];
        for (var index = 0; index < blockCount; index++)
        {
            offsets[index] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(16 + (index * 4), 4));
        }

        var width = checked((macroWidth * BlockStride) + 1);
        var height = checked((macroHeight * BlockStride) + 1);
        if (checked((long)width * height) > MaximumCellCount)
        {
            throw new InvalidDataException($"MBD expanded cell count exceeds the {MaximumCellCount:N0}-cell allocation budget.");
        }

        var cells = new MbdCollisionCell[width, height];
        var assigned = new bool[width, height];
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                cells[x, y] = MissingCell;
            }
        }
        var missingBlocks = 0;
        var conflicts = 0;
        for (var blockY = 0; blockY < macroHeight; blockY++)
        {
            for (var blockX = 0; blockX < macroWidth; blockX++)
            {
                var blockOffset = offsets[(blockY * macroWidth) + blockX];
                if (blockOffset == 0)
                {
                    missingBlocks++;
                    continue;
                }
                if (blockOffset < pointerTableEnd)
                {
                    throw new InvalidDataException("MBD block pointer overlaps the header or pointer table.");
                }
                if ((ulong)blockOffset + BlockSize > (ulong)bytes.Length)
                {
                    throw new InvalidDataException("MBD block pointer falls outside the file.");
                }

                for (var localY = 0; localY < CellsPerBlockSide; localY++)
                {
                    for (var localX = 0; localX < CellsPerBlockSide; localX++)
                    {
                        var cellOffset = checked((int)blockOffset + (((localY * CellsPerBlockSide) + localX) * CellSize));
                        var cell = new MbdCollisionCell(
                            BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(cellOffset, 2)),
                            BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(cellOffset + 2, 2)),
                            BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(cellOffset + 4, 2)),
                            BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(cellOffset + 6, 2)));
                        var x = (blockX * BlockStride) + localX;
                        var y = (blockY * BlockStride) + localY;
                        if (assigned[x, y] && cells[x, y] != cell)
                        {
                            conflicts++;
                            // Shared block borders are duplicated. Collision conflicts fail closed:
                            // if either declaration is blocked, retain a blocked declaration.
                            if (cells[x, y].IsWalkable && !cell.IsWalkable)
                            {
                                cells[x, y] = cell;
                            }
                        }
                        else if (!assigned[x, y])
                        {
                            cells[x, y] = cell;
                            assigned[x, y] = true;
                        }
                    }
                }
            }
        }

        return new MbdCollisionMap(
            magic,
            macroWidth,
            macroHeight,
            width,
            height,
            offsets,
            cells,
            missingBlocks,
            conflicts,
            Convert.ToHexString(SHA256.HashData(bytes)));
    }
}
