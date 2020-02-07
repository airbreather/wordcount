using System;
using System.Buffers;
using System.Buffers.Text;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Collections.Extensions;

using Pipelines.Sockets.Unofficial;

namespace WordCountProgram
{
    internal static class Program
    {
        private static async Task Main()
        {
            var results = new DictionarySlim<Stringish, int>();

            await using (var inputStream = Console.OpenStandardInput())
            {
                var reader = StreamConnection.GetReader(inputStream);

                byte[] stops = new byte[] { (byte)' ', (byte)'\t', (byte)'\r', (byte)'\n' };
                ReadResult read;
                while (true)
                {
                    read = await reader.ReadAsync().ConfigureAwait(false);
                    reader.AdvanceTo(Consume(read.Buffer), read.Buffer.End);
                    SequencePosition Consume(ReadOnlySequence<byte> buf)
                    {
                        var seqReader = new SequenceReader<byte>(read.Buffer);
                        while (seqReader.TryReadToAny(out ReadOnlySequence<byte> wordSeq, stops))
                        {
                            int len = checked((int)wordSeq.Length);
                            if (len == 0)
                            {
                                continue;
                            }

                            byte[] b = ArrayPool<byte>.Shared.Rent(len);
                            wordSeq.CopyTo(b.AsSpan(0, len));
                            ref int idx = ref results.GetOrAddValueRef(new Stringish(b.AsMemory(0, len)));
                            if (idx++ != 0)
                            {
                                ArrayPool<byte>.Shared.Return(b);
                            }
                        }

                        return seqReader.Position;
                    }

                    if (read.IsCompleted)
                    {
                        break;
                    }
                }
            }

            Write(results);
            static void Write(DictionarySlim<Stringish, int> results)
            {
                int newLineLength = Console.OutputEncoding.GetByteCount(Environment.NewLine);

                Span<byte> countData = stackalloc byte[newLineLength + newLineLength + 11];
                countData[0] = (byte)'\t';
                var numData = countData.Slice(1, countData.Length - newLineLength - 1);
                var newLine = countData.Slice(countData.Length - newLineLength, newLineLength);
                Console.OutputEncoding.GetBytes(Environment.NewLine, newLine);
                using (var outputStream = new BufferedStream(Console.OpenStandardOutput()))
                {
                    foreach (var (data, cnt) in results.OrderByDescending(kvp => kvp.Value).ThenBy(kvp => kvp.Key))
                    {
                        outputStream.Write(data.Data.Span);

                        Utf8Formatter.TryFormat(cnt, numData, out int countLength);
                        newLine.CopyTo(numData.Slice(countLength, newLine.Length));
                        outputStream.Write(countData.Slice(0, countLength + newLine.Length + 1));
                    }
                }
            }
        }
    }

    internal readonly struct Stringish : IEquatable<Stringish>, IComparable<Stringish>
    {
        public readonly ReadOnlyMemory<byte> Data;

        public Stringish(ReadOnlyMemory<byte> data)
            => Data = data;

        public int CompareTo(Stringish other)
            => Data.Span.SequenceCompareTo(other.Data.Span);

        public bool Equals(Stringish other)
            => Data.Span.SequenceEqual(other.Data.Span);

        public override int GetHashCode()
            => xxHash64.Hash(Data.Span).GetHashCode();
    }
}