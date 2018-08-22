﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Text;
using System.Collections;

namespace Makaretu.Dns
{
    /// <summary>
    ///   Methods to write DNS data items.
    /// </summary>
    public class DnsWriter
    {
        const int maxPointer = 0x3FFF; 
        Stream stream;
        Dictionary<string, int> pointers = new Dictionary<string, int>();
        Stack<Stream> scopes = new Stack<Stream>();

        /// <summary>
        ///   The writer relative position within the stream.
        /// </summary>
        public int Position;

        /// <summary>
        ///   Creates a new instance of the <see cref="DnsWriter"/> on the
        ///   specified <see cref="Stream"/>.
        /// </summary>
        /// <param name="stream">
        ///   The destination for data items.
        /// </param>
        public DnsWriter(Stream stream)
        {
            this.stream = stream;
        }

        /// <summary>
        ///   Start a length prefixed stream.
        /// </summary>
        /// <remarks>
        ///   A memory stream is created for writing.  When it is popped,
        ///   the memory stream's position is writen as an UInt16 and its
        ///   contents are copied to the current stream.
        /// </remarks>
        public void PushLengthPrefixedScope()
        {
            scopes.Push(stream);
            stream = new MemoryStream();
            Position += 2; // count the length prefix
        }

        /// <summary>
        ///   Start a length prefixed stream.
        /// </summary>
        /// <remarks>
        ///   A memory stream is created for writing.  When it is popped,
        ///   the memory stream's position is writen as an UInt16 and its
        ///   contents are copied to the current stream.
        /// </remarks>
        public ushort PopLengthPrefixedScope()
        {
            var lp = stream;
            var length = (ushort)lp.Position;
            stream = scopes.Pop();
            WriteUInt16(length);
            Position -= 2;
            lp.Position = 0;
            lp.CopyTo(stream);

            return length;
        }

        /// <summary>
        ///   Write a byte.
        /// </summary>
        public void WriteByte(byte value)
        {
            stream.WriteByte(value);
            ++Position;
        }

        /// <summary>
        ///   Write a sequence of bytes.
        /// </summary>
        /// <param name="bytes">
        ///   A sequence of bytes to write.
        /// </param>
        public void WriteBytes(byte[] bytes)
        {
            if (bytes != null)
            {
                stream.Write(bytes, 0, bytes.Length);
                Position += bytes.Length;
            }
        }

        /// <summary>
        ///   Write a sequence of bytes prefixed with the length as a byte.
        /// </summary>
        /// <param name="bytes">
        ///   A sequence of bytes to write.
        /// </param>
        public void WriteByteLengthPrefixedBytes(byte[] bytes)
        {
            var length = bytes?.Length ?? 0;
            if (length > byte.MaxValue)
                throw new ArgumentException($"Bytes length can not exceed {byte.MaxValue}.");

            WriteByte((byte)length);
            WriteBytes(bytes);
        }

        /// <summary>
        ///   Write an unsigned short.
        /// </summary>
        public void WriteUInt16(ushort value)
        {
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
            Position += 2;
        }

        /// <summary>
        ///   Write an unsigned int.
        /// </summary>
        public void WriteUInt32(uint value)
        {
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
            Position += 4;
        }

        /// <summary>
        ///   Write a domain name.
        /// </summary>
        /// <param name="name">
        ///   The name to write.
        /// </param>
        /// <param name="uncompressed">
        ///   Determines if the <paramref name="name"/> must be uncompressed.  The
        ///   defaultl is false (allow compression).
        /// </param>
        /// <remarks>
        ///   A domain name is represented as a sequence of labels, where
        ///   each label consists of a length octet followed by that
        ///   number of octets.The domain name terminates with the
        ///   zero length octet for the null label of the root. Note
        ///   that this field may be an odd number of octets; no
        ///   padding is used.
        /// </remarks>
        public void WriteDomainName(string name, bool uncompressed = false)
        {
            if (string.IsNullOrEmpty(name))
            {
                stream.WriteByte(0); // terminating byte
                ++Position;
                return;
            }

            var labels = name.Split('.');
            for (var i = 0; i < labels.Length; ++i)
            {
                var label = labels[i];
                var bytes = Encoding.UTF8.GetBytes(label);
                if (bytes.Length > 63)
                    throw new InvalidDataException($"Label '{label}' cannot exceed 63 octets.");

                // Check for qualified name already used.
                var qn = string.Join(".", labels, i, labels.Length - i);
                if (!uncompressed && pointers.TryGetValue(qn, out int pointer))
                {
                    WriteUInt16((ushort)(0xC000 | pointer));
                    return;
                }
                if (Position <= maxPointer)
                {
                    pointers[qn] = Position;
                }

                // Add the label
                stream.WriteByte((byte)bytes.Length);
                stream.Write(bytes, 0, bytes.Length);
                Position += bytes.Length + 1;
            }

            stream.WriteByte(0); // terminating byte
            ++Position;
        }

        /// <summary>
        ///   Write a string.
        /// </summary>
        /// <remarks>
        ///   Strings are encoded with a length prefixed byte.  All strings are treated
        ///   as UTF-8.
        /// </remarks>
        public void WriteString(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
            Position += bytes.Length + 1;
        }

        /// <summary>
        ///   Write a time span.
        /// </summary>
        /// <remarks>
        ///   Represented as 32-bit unsigned int (in seconds).
        /// </remarks>
        public void WriteTimeSpan(TimeSpan value)
        {
            WriteUInt32((uint)value.TotalSeconds);
        }

        /// <summary>
        ///   Write an IP address.
        /// </summary>
        /// <param name="value"></param>
        public void WriteIPAddress(IPAddress value)
        {
            WriteBytes(value.GetAddressBytes());
        }

        /// <summary>
        ///   Write the bitmap(s) for the values.
        /// </summary>
        /// <param name="values">
        ///   The sequence of values to encode into a bitmap.
        /// </param>
        public void WriteBitmap(IEnumerable<ushort> values)
        {
            var windows = values
                // Convert values into Window and Mask
                .Select(v =>
                {
                    var w = new { Window = v / 256, Mask = new BitArray(256) };
                    w.Mask[v & 0xff] = true;
                    return w;
                })
                // Group by Window and merge the Masks
                .GroupBy(w => w.Window)
                .Select(g => new
                {
                    Window = g.Key,
                    Mask = g.Select(w => w.Mask).Aggregate((a, b) => a.Or(b))
                })
                .OrderBy(w => w.Window)
                .ToArray();

            foreach (var window in windows)
            {
                // BitArray to byte array and remove trailing zeros.
                var mask = ToBytes(window.Mask, true).ToList();
                for (int i = mask.Count - 1; i > 0; --i)
                {
                    if (mask[i] != 0)
                        break;
                    mask.RemoveAt(i);
                }

                stream.WriteByte((byte)window.Window);
                stream.WriteByte((byte)mask.Count);
                Position += 2;
                WriteBytes(mask.ToArray());
            }
        }

        static IEnumerable<Byte> ToBytes(BitArray bits, bool MSB = false)
        {
            int bitCount = 7;
            int outByte = 0;

            foreach (bool bitValue in bits)
            {
                if (bitValue)
                    outByte |= MSB ? 1 << bitCount : 1 << (7 - bitCount);
                if (bitCount == 0)
                {
                    yield return (byte)outByte;
                    bitCount = 8;
                    outByte = 0;
                }
                bitCount--;
            }
            // Last partially decoded byte
            if (bitCount < 7)
                yield return (byte)outByte;
        }
    }
}
