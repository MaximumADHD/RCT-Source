using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;

namespace RobloxClientTracker
{
    public class ExtractQtResources : DataMiner
    {
        public override ConsoleColor LogColor => ConsoleColor.Magenta;
        private readonly record struct Chunk(int Data, int Names, int Tree, int Version);
        
        private byte[] binary;
        private PEHeaders headers;
        private string outputDir;
        private int written;

        private readonly string[] signatures =
        [
            "48 83 EC 28 4C 8D 0D ?? ?? ?? ?? 4C 8D 05 ?? ?? ?? ?? 48 8D 15 ?? ?? ?? ?? B9 ?? ?? ?? ?? E8 ?? ?? ?? ?? B8 01 00 00 00 48 83 C4 28 C3",
            "48 83 EC 28 4C 8D 0D ?? ?? ?? ?? B9 ?? ?? ?? ?? 4C 8D 05 ?? ?? ?? ?? 48 8D 15 ?? ?? ?? ?? E8",
            "48 83 EC 28 4C 8D 0D ?? ?? ?? ?? B9 ?? ?? ?? ?? 4C 8D 05 ?? ?? ?? ?? 48 8D 15 ?? ?? ?? ?? FF 15",
        ];

        private ReadOnlySpan<byte> slice(int offset, int length)
        {
            if (offset < 0 || length < 0 || offset > binary.Length - length)
                throw new InvalidDataException("Qt resource points outside the executable.");
            
            return binary.AsSpan(offset, length);
        }

        private int readInt(int offset) => checked((int)BinaryPrimitives.ReadUInt32BigEndian(slice(offset, 4)));

        private int resolveRelative(int offset)
        {
            var section = headers.SectionHeaders.First(section => offset >= section.PointerToRawData
                && offset < section.PointerToRawData + section.SizeOfRawData);
            
            long address = section.VirtualAddress + (long)offset - section.PointerToRawData + 4
                + BinaryPrimitives.ReadInt32LittleEndian(slice(offset, 4));
            
            foreach (var target in headers.SectionHeaders)
                if (address >= target.VirtualAddress && address < target.VirtualAddress + (long)target.SizeOfRawData)
                    return checked((int)(address - target.VirtualAddress + target.PointerToRawData));
            
            throw new InvalidDataException("Invalid Qt resource RVA.");
        }

        private IEnumerable<Chunk> findChunks()
        {
            var seen = new HashSet<int>();

            foreach (string signature in signatures)
            {
                var pattern = signature.Split(' ')
                    .Select(value => value == "??" ? -1 : Convert.ToInt32(value, 16))
                    .ToArray();

                foreach (var section in headers.SectionHeaders.Where(section => (section.SectionCharacteristics & SectionCharacteristics.ContainsCode) != 0))
                {
                    int end = checked(section.PointerToRawData + section.SizeOfRawData);
                    slice(section.PointerToRawData, section.SizeOfRawData);

                    for (int offset = section.PointerToRawData; offset <= end - pattern.Length; offset++)
                    {
                        if (binary[offset] != pattern[0])
                            continue;
                        
                        bool matches = true;

                        for (int index = 1; index < pattern.Length; index++)
                        {
                            if (pattern[index] >= 0 && binary[offset + index] != pattern[index])
                            {
                                matches = false;
                                break;
                            }
                        }

                        if (!matches)
                            continue;
                        
                        bool consecutive = pattern[11] == 0x4C;
                        int data = resolveRelative(offset + 7);

                        int names = resolveRelative(offset + (consecutive ? 14 : 19));
                        int tree = resolveRelative(offset + (consecutive ? 21 : 26));

                        int version = BinaryPrimitives.ReadInt32LittleEndian(slice(offset + (consecutive ? 26 : 12), 4));

                        if (version < 1 || version > 3)
                            throw new InvalidDataException("Unsupported Qt resource version: " + version);
                        
                        if (seen.Add(data))
                        {
                            yield return new Chunk(data, names, tree, version);
                        }
                    }
                }
            }
        }

        private string readName(Chunk chunk, int offset)
        {
            int position = checked(chunk.Names + offset);
            int length = BinaryPrimitives.ReadUInt16BigEndian(slice(position, 2));
            string name = Encoding.BigEndianUnicode.GetString(slice(checked(position + 6), checked(length * 2)));

            if (name is "." or ".." || name.IndexOfAny(['/', '\\', ':', '\0']) >= 0)
                throw new InvalidDataException("Unsafe Qt resource name.");
            
            return name;
        }

        private void extractNode(Chunk chunk, int node, string parent, HashSet<int> visited, int depth)
        {
            if (depth > 256 || !visited.Add(node))
                throw new InvalidDataException("Invalid Qt resource tree.");
            
            int position = checked(chunk.Tree + node * (chunk.Version >= 2 ? 22 : 14));
            slice(position, chunk.Version >= 2 ? 22 : 14);

            string name = readName(chunk, readInt(position));
            int flags = BinaryPrimitives.ReadUInt16BigEndian(slice(position + 4, 2));

            if ((flags & ~7) != 0 || (name.Length == 0 && node != 0))
                throw new InvalidDataException("Invalid Qt resource node.");
            
            string path = Path.Combine(parent, name);

            if ((flags & 2) != 0)
            {
                int count = readInt(position + 6);
                int first = readInt(position + 10);

                if (count > binary.Length / 14)
                    throw new InvalidDataException("Invalid Qt resource child count.");
                
                for (int index = 0; index < count; index++)
                    extractNode(chunk, checked(first + index), path, visited, depth + 1);
                
                return;
            }

            int data = checked(chunk.Data + readInt(position + 10));
            int size = readInt(data);

            var payload = slice(checked(data + 4), size);
            string relative = Path.GetRelativePath(outputDir, path);
            var components = relative.Split(Path.DirectorySeparatorChar);

            if (components.Length > 1)
                relative = Path.Combine(components.Skip(1).ToArray());
            
            path = Path.Combine(outputDir, relative);

            if (Path.GetDirectoryName(relative) == "")
                path = Path.Combine(outputDir, "RobloxStyle", relative);
            
            createDirectory(Path.GetDirectoryName(path));

            if ((flags & 4) != 0)
                throw new InvalidDataException("Zstandard Qt resources are not supported.");
            
            if ((flags & 1) != 0)
            {
                if (size < 4)
                    throw new InvalidDataException("Truncated compressed Qt resource.");
                
                int expected = readInt(data + 4);

                using var input = new MemoryStream(payload.Slice(4).ToArray());
                using var decoder = new ZLibStream(input, CompressionMode.Decompress);
                using var output = File.Create(path);

                var buffer = new byte[81920];
                int total = 0;
                int count;

                while ((count = decoder.Read(buffer)) > 0)
                {
                    total = checked(total + count);

                    if (total > expected)
                        throw new InvalidDataException("Qt resource exceeds its declared size.");
                    
                    output.Write(buffer, 0, count);
                }

                if (total != expected)
                {
                    throw new InvalidDataException("Qt resource size mismatch.");
                }
            }
            else
            {
                File.WriteAllBytes(path, payload.ToArray());
            }
            
            written++;
        }

        public override void ExecuteRoutine()
        {
            binary = File.ReadAllBytes(studioPath);
            using var stream = new MemoryStream(binary, false);

            using var reader = new PEReader(stream);
            headers = reader.PEHeaders;

            if (headers.CoffHeader.Machine != Machine.Amd64)
                throw new InvalidDataException("Qt extraction requires an x64 PE executable.");

            var chunks = findChunks().ToArray();

            if (chunks.Length == 0)
                throw new InvalidDataException("No Qt resource registrations found.");

            outputDir = resetDirectory(stageDir, "QtResources");

            foreach (var chunk in chunks)
                extractNode(chunk, 0, outputDir, new HashSet<int>(), 0);

            if (written == 0)
                throw new InvalidDataException("Qt extraction produced no resources.");

            print($"Extracted {written} Qt resources from {chunks.Length} chunks.");
        }
    }
}