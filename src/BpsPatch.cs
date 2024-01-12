using System.IO.Hashing;
using System.Text;

namespace BpsNet
{
    public class BpsPatch
    {
        private byte[] Actions { get; init; }

        enum ActionType
        {
            SourceRead = 0, TargetRead = 1, SourceCopy = 2, TargetCopy = 3
        }

        public int SourceSize { get; init; }
        public int TargetSize { get; init; }
        public String Metadata { get; init; }
        public uint SourceChecksum { get; init; }
        public uint TargetChecksum { get; init; }
        public uint? PatchChecksum { get; private set; }

        public BpsPatch(byte[] data)
        {
            if (data[0] != 'B' || data[1] != 'P' || data[2] != 'S' || data[3] != '1')
                throw new InvalidDataException("Patch header is not BPS1");

            int readIndex = 4;
            SourceSize = (int)ReadNumber(data, ref readIndex);
            TargetSize = (int)ReadNumber(data, ref readIndex);
            int metadataSize = (int)ReadNumber(data, ref readIndex);
            Metadata = System.Text.Encoding.UTF8.GetString(data, (int)readIndex, (int)metadataSize);
            int actionStartIndex = readIndex + metadataSize;
            Actions = new byte[data.Length - 12 - actionStartIndex];
            Array.Copy(data, actionStartIndex, Actions, 0, Actions.Length);
            readIndex = data.Length - 12;
            SourceChecksum = ReadUint(data, ref readIndex);
            TargetChecksum = ReadUint(data, ref readIndex);
            PatchChecksum = ReadUint(data, ref readIndex);

            uint computedPatchChecksum = Crc32.HashToUInt32(new ReadOnlySpan<byte>(data, 0, data.Length - 4));
            if (PatchChecksum != computedPatchChecksum)
            {
                throw new InvalidDataException("Patch checksum is invalid");
            }
        }

        private BpsPatch(byte[] source, byte[] target, string metadata, byte[] actions)
        {
            SourceSize = source.Length;
            TargetSize = target.Length;
            Metadata = metadata;
            SourceChecksum = Crc32.HashToUInt32(source);
            TargetChecksum = Crc32.HashToUInt32(target);
            Actions = actions;
            PatchChecksum = 0;
        }

        public byte[] GetBytes()
        {
            var memoryStream = new MemoryStream();
            memoryStream.SetLength(128);
            memoryStream.Write(new char[] { 'B', 'P', 'S', '1' }.Select(c => Convert.ToByte(c)).ToArray());
            WriteNumber((uint)SourceSize, memoryStream);
            WriteNumber((uint)TargetSize, memoryStream);
            byte[] metadata = UTF8Encoding.UTF8.GetBytes(Metadata);
            WriteNumber((uint)metadata.Length, memoryStream);

            var variableHeaderSize = memoryStream.Position;

            var hasher = new Crc32();
            var header = memoryStream.ToArray();
            hasher.Append(new ReadOnlySpan<byte>(header, 0, (int)variableHeaderSize));

            memoryStream.SetLength(variableHeaderSize + metadata.Length + Actions.Length + 12);
            memoryStream.Write(metadata);
            hasher.Append(metadata);
            memoryStream.Write(Actions);
            hasher.Append(Actions);
            var sourceChecksumBytes = UintToByte(SourceChecksum);
            memoryStream.Write(sourceChecksumBytes);
            hasher.Append(sourceChecksumBytes);
            var targetChecksumBytes = UintToByte(TargetChecksum);
            memoryStream.Write(targetChecksumBytes);
            hasher.Append(targetChecksumBytes);

            memoryStream.Write(UintToByte(hasher.GetCurrentHashAsUInt32()));

            return memoryStream.ToArray();
        }

        public byte[] Apply(byte[] source)
        {
            byte[] target = new byte[TargetSize];
            int outputOffset = 0;
            int sourceRelativeOffset = 0;
            int targetRelativeOffset = 0;

            int actionIndex = 0;
            int maxActionIndex = Actions.Length;
            while (actionIndex < maxActionIndex)
            {
                uint data = ReadNumber(Actions, ref actionIndex);
                uint command = data & 3;
                uint length = (data >> 2) + 1;
                switch ((ActionType)command)
                {
                    case ActionType.SourceRead:
                        while (length-- > 0)
                        {
                            target[outputOffset] = source[outputOffset];
                            outputOffset++;
                        }
                        break;
                    case ActionType.TargetRead:
                        while (length-- > 0)
                        {
                            target[outputOffset++] = Actions[actionIndex++];
                        }
                        break;
                    case ActionType.SourceCopy:
                        data = ReadNumber(Actions, ref actionIndex);
                        if ((data & 1) == 0)
                            sourceRelativeOffset += (int)(data >> 1);
                        else
                            sourceRelativeOffset -= (int)(data >> 1);
                        while (length-- > 0)
                        {
                            target[outputOffset++] = source[sourceRelativeOffset++];
                        }
                        break;
                    case ActionType.TargetCopy:
                        data = ReadNumber(Actions, ref actionIndex);
                        if ((data & 1) == 0)
                            targetRelativeOffset += (int)(data >> 1);
                        else
                            targetRelativeOffset -= (int)(data >> 1);
                        while (length-- > 0)
                        {
                            target[outputOffset++] = target[targetRelativeOffset++];
                        }
                        break;
                }
            }
            return target;
        }

        static private uint ReadNumber(byte[] data, ref int readIndex)
        {
            ulong result = 0, shift = 1;
            while (true)
            {
                byte x = data[readIndex++];
                result += (byte)(x & 0x7f) * shift;
                if ((x & (byte)0x80) != 0) break;
                shift <<= 7;
                result += shift;

                if (result > UInt32.MaxValue)
                    throw new InvalidDataException("Number is out of allowed range");
            }
            return (uint)result;
        }

        static void WriteNumber(uint number, Stream data)
        {
            while (true)
            {
                byte x = (byte)(number & 0x7f);
                number >>= 7;
                if (number == 0)
                {
                    data.WriteByte((byte)(x | 0x80));
                    break;
                }
                data.WriteByte(x);
                number--;
            }
        }

        static private uint ReadUint(byte[] data, ref int readIndex)
        {
            uint result = data[readIndex] + (uint)(data[readIndex + 1] << 8) + (uint)(data[readIndex + 2] << 16) + (uint)(data[readIndex + 3] << 24);
            readIndex += 4;
            return result;
        }

        static private byte[] UintToByte(uint value)
        {
            byte[] result = new byte[4];
            result[0] = (byte)value;
            result[1] = (byte)(value >> 8);
            result[2] = (byte)(value >> 16);
            result[3] = (byte)(value >> 24);
            return result;
        }

        record PatchAction(ActionType Type, int Length, List<byte> Data, int RelativeOffset);

        static public BpsPatch Create(byte[] sourceData, byte[] targetData, string metadata, bool delta = true)
        {
            if (delta)
                return CreateDelta(sourceData, targetData, metadata);
            return CreateLinear(sourceData, targetData, metadata);
        }

        static public BpsPatch CreateLinear(byte[] sourceData, byte[] targetData, string metadata)
        {
            var patchActions = new List<PatchAction>();

            /* references to match original beat code */
            var sourceSize = sourceData.Length;
            var targetSize = targetData.Length;
            var Granularity = 1;

            var targetRelativeOffset = 0;
            var outputOffset = 0;
            var targetReadLength = 0;

            var targetReadFlush = () =>
            {
                if (targetReadLength > 0)
                {
                    var action = new PatchAction(ActionType.TargetRead, targetReadLength, new(), 0);
                    patchActions.Add(action);
                    var offset = outputOffset - targetReadLength;
                    while (targetReadLength > 0)
                    {
                        action.Data.Add(targetData[offset++]);
                        targetReadLength--;
                    }
                }
            };

            while (outputOffset < targetSize)
            {
                var sourceLength = 0;
                for (var n = 0; outputOffset + n < Math.Min(sourceSize, targetSize); n++)
                {
                    if (sourceData[outputOffset + n] != targetData[outputOffset + n]) break;
                    sourceLength++;
                }

                var rleLength = 0;
                for (var n = 1; outputOffset + n < targetSize; n++)
                {
                    if (targetData[outputOffset] != targetData[outputOffset + n]) break;
                    rleLength++;
                }

                if (rleLength >= 4)
                {
                    //write byte to repeat
                    targetReadLength++;
                    outputOffset++;
                    targetReadFlush();

                    //copy starting from repetition byte);
                    var relativeOffset = (outputOffset - 1) - targetRelativeOffset;
                    patchActions.Add(new PatchAction(ActionType.TargetCopy, rleLength, [], relativeOffset));
                    outputOffset += rleLength;
                    targetRelativeOffset = outputOffset - 1;
                }
                else if (sourceLength >= 4)
                {
                    targetReadFlush();
                    patchActions.Add(new PatchAction(ActionType.SourceRead, sourceLength, [], 0));
                    outputOffset += sourceLength;
                }
                else
                {
                    targetReadLength += Granularity;
                    outputOffset += Granularity;
                }
            }

            targetReadFlush();

            return new BpsPatch(sourceData, targetData, metadata, GetBytesFromActions(patchActions));
        }

        private static byte[] GetBytesFromActions(IEnumerable<PatchAction> actions)
        {
            var memoryStream = new MemoryStream();
            memoryStream.SetLength(1 * 1024 * 1024);

            foreach (var action in actions)
            {
                while (memoryStream.Position + action.Data.Count + 64 > memoryStream.Length)
                {
                    memoryStream.SetLength(memoryStream.Length + 1024 * 1024);
                }
                WriteNumber((((uint)action.Length - 1) << 2) + (uint)action.Type, memoryStream);
                if (action.Type == ActionType.TargetRead)
                {
                    memoryStream.Write(action.Data.ToArray());
                }
                else if (action.Type == ActionType.SourceCopy || action.Type == ActionType.TargetCopy)
                {
                    WriteNumber((uint)(Math.Abs(action.RelativeOffset) << 1) + (uint)(action.RelativeOffset < 0 ? 1 : 0), memoryStream);
                }
            }

            memoryStream.SetLength(memoryStream.Position);
            return memoryStream.ToArray();
        }

        internal record class BpsNode(int offset, BpsNode? next);

        static public BpsPatch CreateDelta(byte[] sourceData, byte[] targetData, string metadata)
        {
            var patchActions = new List<PatchAction>();

            /* references to match original beat code */
            var sourceSize = sourceData.Length;
            var targetSize = targetData.Length;
            var Granularity = 1;

            var sourceRelativeOffset = 0;
            var targetRelativeOffset = 0;
            var outputOffset = 0;



            var sourceTree = new BpsNode?[65536];
            var targetTree = new BpsNode?[65536];
            for (var n = 0; n < 65536; n++)
            {
                sourceTree[n] = null;
                targetTree[n] = null;
            }

            //source tree creation
            for (var offset = 0; offset < sourceSize; offset++)
            {
                int symbol = sourceData[offset + 0];
                //sourceChecksum = crc32_adjust(sourceChecksum, symbol);
                if (offset < sourceSize - 1)
                    symbol |= sourceData[offset + 1] << 8;
                var node = new BpsNode(offset, sourceTree[symbol]);
                sourceTree[symbol] = node;
            }

            var targetReadLength = 0;

            var targetReadFlush = () =>
            {
                if (targetReadLength > 0)
                {
                    var action = new PatchAction(ActionType.TargetRead, targetReadLength, [], 0);
                    patchActions.Add(action);
                    var offset = outputOffset - targetReadLength;
                    while (targetReadLength > 0)
                    {
                        action.Data.Add(targetData[offset++]);
                        targetReadLength--;
                    }
                }
            };

            while (outputOffset < targetSize)
            {
                var maxLength = 0;
                var maxOffset = 0;
                var mode = ActionType.TargetRead;

                int symbol = targetData[outputOffset + 0];
                if (outputOffset < targetSize - 1)
                    symbol |= targetData[outputOffset + 1] << 8;

                { //source read
                    var length = 0;
                    var offset = outputOffset;
                    while (offset < sourceSize && offset < targetSize && sourceData[offset] == targetData[offset])
                    {
                        length++;
                        offset++;
                    }
                    if (length > maxLength)
                    {
                        maxLength = length;
                        mode = ActionType.SourceRead;
                    }
                }

                { //source copy
                    var node = sourceTree[symbol];
                    while (node != null)
                    {
                        var length = 0;
                        var x = node.offset;
                        var y = outputOffset;

                        while (x < sourceSize && y < targetSize && sourceData[x++] == targetData[y++])
                            length++;

                        if (length > maxLength)
                        {
                            maxLength = length;
                            maxOffset = node.offset;
                            mode = ActionType.SourceCopy;
                        }
                        node = node.next;
                    }
                }

                { //target copy
                    var node = targetTree[symbol];
                    while (node != null)
                    {
                        var length = 0;
                        var x = node.offset;
                        var y = outputOffset;

                        while (y < targetSize && targetData[x++] == targetData[y++])
                            length++;

                        if (length > maxLength)
                        {
                            maxLength = length;
                            maxOffset = node.offset;
                            mode = ActionType.TargetCopy;
                        }
                        node = node.next;
                    }

                    //target tree append
                    node = new BpsNode(outputOffset, targetTree[symbol]);
                    targetTree[symbol] = node;
                }

                { //target read
                    if (maxLength < 4)
                    {
                        maxLength = Math.Min(Granularity, targetSize - outputOffset);
                        mode = ActionType.TargetRead;
                    }
                }

                if (mode != ActionType.TargetRead)
                    targetReadFlush();

                switch (mode)
                {
                    case ActionType.SourceRead:
                        //encode(ActionType.SourceRead | ((maxLength - 1) << 2));
                        patchActions.Add(new PatchAction(ActionType.SourceRead, maxLength, [], 0));
                        break;
                    case ActionType.TargetRead:
                        //delay write to group sequential TargetRead commands into one
                        targetReadLength += maxLength;
                        break;
                    case ActionType.SourceCopy:
                    case ActionType.TargetCopy:
                        //encode(mode | ((maxLength - 1) << 2));
                        int relativeOffset;
                        if (mode == ActionType.SourceCopy)
                        {
                            relativeOffset = maxOffset - sourceRelativeOffset;
                            sourceRelativeOffset = maxOffset + maxLength;
                        }
                        else
                        {
                            relativeOffset = maxOffset - targetRelativeOffset;
                            targetRelativeOffset = maxOffset + maxLength;
                        }
                        //encode((relativeOffset < 0) | (abs(relativeOffset) << 1));
                        patchActions.Add(new PatchAction(mode, maxLength, [], relativeOffset));
                        break;
                }

                outputOffset += maxLength;
            }

            targetReadFlush();

            return new BpsPatch(sourceData, targetData, metadata, GetBytesFromActions(patchActions));
        }
    }

}