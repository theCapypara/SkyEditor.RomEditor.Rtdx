﻿using SkyEditor.IO.Binary;
using SkyEditor.RomEditor.Domain.Common.Structures;
using SkyEditor.RomEditor.Infrastructure;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SkyEditor.RomEditor.Domain.Rtdx.Structures
{
    public class MessageBinEntry
    {
        // Always seems to be the same value, at least for the entries in script.bin inside message_us.bin
        public const int UnknownValue = 0x3020003;
        public const int EntryLength = 0x10;

        private ICodeTable? codeTable;

        public MessageBinEntry()
        {
            Strings = new Dictionary<long, List<MessageBinString>>();
            OrderedHashes = new List<int>();
        }

        public MessageBinEntry(IReadOnlyBinaryDataAccessor data, ICodeTable? codeTable = null): this()
        {
            this.codeTable = codeTable;

            var sir0 = new Sir0(data);
            var entryCount1 = sir0.SubHeader.ReadInt32(0);
            var entryCount2 = sir0.SubHeader.ReadInt32(4);
            var entriesOffset = sir0.SubHeader.ReadInt32(8);

            var hashes = new Dictionary<long, int>();
            for (int i = 0; i < entryCount1; i++)
            {
                var entryOffset = entriesOffset + (i * EntryLength);
                var stringOffset = sir0.Data.ReadInt64(entryOffset);

                var hash = sir0.Data.ReadInt32(entryOffset + 8);
                var unknown = sir0.Data.ReadInt32(entryOffset + 0xC);

                // Read the string manually since ReadNullTerminatedUnicodeString automatically converts invalid Unicode characters to 0xFFFD
                var sb = new StringBuilder();
                var strOffset = stringOffset;
                ushort ch;
                while ((ch = sir0.Data.ReadUInt16(strOffset)) != 0) {
                    sb.Append((char)ch);
                    strOffset += 2;
                }
                var value = sb.ToString();
                
                //var value = sir0.Data.ReadNullTerminatedUnicodeString(stringOffset);
                if (codeTable != null)
                {
                    value = codeTable.UnicodeDecode(value);
                }
                AddString(new MessageBinString
                {
                    Value = value,
                    Hash = hash,
                    Unknown = unknown,
                    StringOffset = stringOffset
                });
                hashes.Add(stringOffset, hash);
            }
            OrderedHashes = hashes.OrderBy(h => h.Key).Select(h => h.Value).ToArray();
        }

        public MessageBinEntry(byte[] data, ICodeTable? codeTable = null) : this(new BinaryFile(data), codeTable)
        {
        }

        // The original message_us.bin seems to contain some duplicate hashes,
        // so there's a list of strings for each hash.
        // TODO: investigate if we actually need to support this (seems to be a rare case with garbage values)
        public Dictionary<long, List<MessageBinString>> Strings { get; }
        public IReadOnlyList<int> OrderedHashes { get; }

        public string GetStringByHash(int hash) => Strings.ContainsKey(hash) ? Strings[hash].FirstOrDefault()!.Value : "";

        public void AddString(string key, string value)
        {
            AddString((int) Crc32Hasher.Crc32Hash(key), value);
        }

        public void AddString(int hash, string value)
        {
            AddString(new MessageBinString
            {
                Value = value,
                Hash = hash
            });
        }

        public void AddString(MessageBinString str)
        {
            if (Strings.ContainsKey(str.Hash))
            {
                Strings[str.Hash].Add(str);
            }
            else
            {
                Strings.Add(str.Hash, new List<MessageBinString>{ str });
            }
        }

        public void SetString(string key, string value)
        {
            SetString((int) Crc32Hasher.Crc32Hash(key), value);
        }

        public void SetString(int hash, string value)
        {
            if (Strings.ContainsKey(hash))
            {
                Strings[hash].First().Value = value;
            }
            else
            {
                AddString(hash, value);
            }
        }

        public Sir0 ToSir0()
        {
            var sir0 = new Sir0Builder(8);

            var allStringValues = Strings.Values.SelectMany(strList => strList).ToArray();

            // Ensure that the order in the generated message.bin matches the original if possible
            var orderedStrings = allStringValues.OrderBy(str => str.Hash).ToArray();
            foreach (var entry in orderedStrings)
            {
                entry.StringOffset = sir0.Length;
                var value = codeTable != null ? codeTable.UnicodeEncode(entry.Value) : entry.Value;
                sir0.WriteNullTerminatedString(sir0.Length, Encoding.Unicode, value);
            }
            sir0.Align(8);

            int entriesOffset = sir0.Length;

            foreach (var entry in allStringValues)
            {
                sir0.MarkPointer(sir0.Length);
                sir0.WriteInt64(sir0.Length, entry.StringOffset);
                sir0.WriteInt32(sir0.Length, entry.Hash);
                sir0.WriteInt32(sir0.Length, entry.Unknown);
            }

            sir0.SubHeaderOffset = sir0.Length;
            sir0.WriteInt32(sir0.Length, allStringValues.Length);
            sir0.WriteInt32(sir0.Length, allStringValues.Length);

            sir0.MarkPointer(sir0.Length);
            sir0.WriteInt64(sir0.Length, entriesOffset);
            
            return sir0.Build();
        }

        public byte[] ToByteArray() => ToSir0().Data.ReadArray();

        public class MessageBinString
        {
            public string Value { get; set; } = "";
            public int Hash { get; set; }
            public int Unknown { get; set; } = UnknownValue;
            public long StringOffset { get; set; }
        }
    }
}
