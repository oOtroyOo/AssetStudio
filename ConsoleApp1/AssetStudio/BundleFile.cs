using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lz4;

namespace AssetStudio
{
    public class BundleFile:IDisposable
    {
        public class Header
        {
            public string signature;
            public uint version;
            public string unityVersion;
            public string unityRevision;
            public long size;
            public uint compressedBlocksInfoSize;
            public uint uncompressedBlocksInfoSize;
            public uint flags;
        }

        public class StorageBlock
        {
            public uint offset;
            public uint compressedSize;
            public uint uncompressedSize;
            public ushort flags;
        }

        public class Node
        {
            public long offset;
            public long size;
            public uint flags;
            public string path;
        }

        public Header m_Header;
        private StorageBlock[] m_BlocksInfo;
        private Node[] m_DirectoryInfo;
        public Node[] DirectoryInfo => m_DirectoryInfo;
        private EndianBinaryReader reader;
        public BundleFile(string path)
        {
            reader = new EndianBinaryReader(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            m_Header = new Header();
            m_Header.signature = reader.ReadStringToNull();
            m_Header.version = reader.ReadUInt32();
            m_Header.unityVersion = reader.ReadStringToNull();
            m_Header.unityRevision = reader.ReadStringToNull();
            switch (m_Header.signature)
            {
                case "UnityFS":
                    ReadHeader(reader);
                    ReadBlocksInfoAndDirectory(reader);
                    break;
            }
        }

        private void ReadHeader(EndianBinaryReader reader)
        {
            m_Header.size = reader.ReadInt64();
            m_Header.compressedBlocksInfoSize = reader.ReadUInt32();
            m_Header.uncompressedBlocksInfoSize = reader.ReadUInt32();
            m_Header.flags = reader.ReadUInt32();
            if (m_Header.signature != "UnityFS")
            {
                reader.ReadByte();
            }
        }

        private void ReadBlocksInfoAndDirectory(EndianBinaryReader reader)
        {
            byte[] blocksInfoBytes;
            if (m_Header.version >= 7)
            {
                reader.AlignStream(16);
            }
            if ((m_Header.flags & 0x80) != 0) //kArchiveBlocksInfoAtTheEnd
            {
                var position = reader.Position;
                reader.Position = reader.BaseStream.Length - m_Header.compressedBlocksInfoSize;
                blocksInfoBytes = reader.ReadBytes((int)m_Header.compressedBlocksInfoSize);
                reader.Position = position;
            }
            else //0x40 kArchiveBlocksAndDirectoryInfoCombined
            {
                blocksInfoBytes = reader.ReadBytes((int)m_Header.compressedBlocksInfoSize);
            }
            var blocksInfoCompressedStream = new MemoryStream(blocksInfoBytes);
            MemoryStream blocksInfoUncompresseddStream;
            switch (m_Header.flags & 0x3F) //kArchiveCompressionTypeMask
            {
                default: //None
                    {
                        blocksInfoUncompresseddStream = blocksInfoCompressedStream;
                        break;
                    }
                case 2: //LZ4
                case 3: //LZ4HC
                    {
                        var uncompressedBytes = new byte[m_Header.uncompressedBlocksInfoSize];
                        using (var decoder = new Lz4DecoderStream(blocksInfoCompressedStream))
                        {
                            decoder.Read(uncompressedBytes, 0, uncompressedBytes.Length);
                        }
                        blocksInfoUncompresseddStream = new MemoryStream(uncompressedBytes);
                        break;
                    }
            }
            using (var blocksInfoReader = new EndianBinaryReader(blocksInfoUncompresseddStream))
            {
                var uncompressedDataHash = blocksInfoReader.ReadBytes(16);
                var blocksInfoCount = blocksInfoReader.ReadInt32();
                m_BlocksInfo = new StorageBlock[blocksInfoCount];
                uint offset = 0;
                for (int i = 0; i < blocksInfoCount; i++)
                {

                    m_BlocksInfo[i] = new StorageBlock
                    {
                        offset = offset,
                        uncompressedSize = blocksInfoReader.ReadUInt32(),
                        compressedSize = blocksInfoReader.ReadUInt32(),
                        flags = blocksInfoReader.ReadUInt16()
                    };
                    offset += m_BlocksInfo[i].compressedSize;
                }

                var nodesCount = blocksInfoReader.ReadInt32();
                m_DirectoryInfo = new Node[nodesCount];
                for (int i = 0; i < nodesCount; i++)
                {
                    m_DirectoryInfo[i] = new Node
                    {
                        offset = blocksInfoReader.ReadInt64(),
                        size = blocksInfoReader.ReadInt64(),
                        flags = blocksInfoReader.ReadUInt32(),
                        path = blocksInfoReader.ReadStringToNull(),
                    };
                }
            }
        }

        public Stream OpenAsset(string assetName)
        {
            MemoryStream fileStream = null;
            var fileNode = Array.Find(m_DirectoryInfo, node => node.path == assetName);
            if (fileNode != null)
            {
                long skipUncompressed = 0;
                foreach (var blockInfo in m_BlocksInfo)
                {
                    if (fileStream == null)
                    {
                        if (skipUncompressed + blockInfo.uncompressedSize >= fileNode.offset)
                        {
                            fileStream = new MemoryStream();
                        }
                        else
                        {
                            skipUncompressed += blockInfo.uncompressedSize;
                        }
                    }

                    if (fileStream != null)
                    {
                        reader.Position = blockInfo.offset;
                        var compressedStream = new MemoryStream(reader.ReadBytes((int)blockInfo.compressedSize));
                        byte[] buffer = new byte[blockInfo.uncompressedSize];

                        using (MemoryStream unCompressStream = new MemoryStream())
                        {
                            using (var lz4Stream = new Lz4DecoderStream(compressedStream))
                            {
                                lz4Stream.CopyTo(unCompressStream);
                            }
                            long begin = 0;
                            if (fileStream.Length == 0)
                            {
                                begin = fileNode.offset - skipUncompressed;
                                if (begin < 0) begin = 0;
                            }
                            unCompressStream.Seek(0, 0);

                            int end = (int)(blockInfo.uncompressedSize - (fileNode.size - fileStream.Position));
                            if (end < 0) end = 0;

                            int size = (int)(blockInfo.uncompressedSize - begin - end);
                            int count = unCompressStream.Read(buffer, (int)begin, size);
                            fileStream.Write(buffer, 0, count);
                            if (fileStream.Length >= fileNode.size)
                            {
                                break;
                            }
                        }
                    }
                }
            }
            fileStream?.Seek(0, 0);
            return fileStream;
        }

        public void Dispose()
        {
            this.m_BlocksInfo = null;
            this.m_Header = null;
            this.m_DirectoryInfo = null;
            reader?.Dispose();
        }
    }
}
