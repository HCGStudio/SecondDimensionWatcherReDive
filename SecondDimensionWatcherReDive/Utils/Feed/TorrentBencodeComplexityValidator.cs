using System.Buffers.Text;

namespace SecondDimensionWatcherReDive.Utils.Feed;

internal static class TorrentBencodeComplexityValidator
{
    internal const int MaximumDepth = 64;
    internal const int MaximumNodes = 100_000;
    internal const int MaximumEntries = 100_000;
    internal const int MaximumStringBytes = 8 * 1024 * 1024;
    private const int MaximumIntegerBytes = 20;

    public static void Validate(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            throw Invalid("The bencode document is empty.");

        Span<ContainerFrame> stack = stackalloc ContainerFrame[MaximumDepth];
        var depth = 0;
        var index = 0;
        var nodes = 0;
        var entries = 0;
        var rootStarted = false;
        var rootCompleted = false;

        while (index < data.Length)
        {
            if (rootCompleted)
                throw Invalid("Trailing bytes follow the root value.");

            if (depth > 0 && stack[depth - 1].Kind == ContainerKind.Dictionary &&
                stack[depth - 1].ExpectingKey)
            {
                if (data[index] == (byte)'e')
                {
                    index++;
                    depth--;
                    if (depth == 0)
                        rootCompleted = true;
                    continue;
                }

                if (!IsDigit(data[index]))
                    throw Invalid("Dictionary keys must be byte strings.");

                ParseString(data, ref index);
                IncrementNode(ref nodes);
                stack[depth - 1].ExpectingKey = false;
                continue;
            }

            var token = data[index];
            if (token == (byte)'e')
            {
                if (depth == 0)
                    throw Invalid("An end marker has no matching container.");
                if (stack[depth - 1].Kind == ContainerKind.Dictionary &&
                    !stack[depth - 1].ExpectingKey)
                    throw Invalid("A dictionary key has no value.");

                index++;
                depth--;
                if (depth == 0)
                    rootCompleted = true;
                continue;
            }

            RegisterValue(stack, depth, ref entries, ref rootStarted);
            IncrementNode(ref nodes);
            switch (token)
            {
                case (byte)'l':
                case (byte)'d':
                    if (depth == MaximumDepth)
                        throw Invalid($"Container depth exceeds {MaximumDepth}.");
                    index++;
                    stack[depth++] = new ContainerFrame(
                        token == (byte)'d' ? ContainerKind.Dictionary : ContainerKind.List,
                        token == (byte)'d');
                    break;

                case (byte)'i':
                    ParseInteger(data, ref index);
                    if (depth == 0)
                        rootCompleted = true;
                    break;

                default:
                    if (!IsDigit(token))
                        throw Invalid($"Invalid bencode token at byte {index}.");
                    ParseString(data, ref index);
                    if (depth == 0)
                        rootCompleted = true;
                    break;
            }
        }

        if (!rootStarted || !rootCompleted || depth != 0)
            throw Invalid("The bencode document is incomplete.");
    }

    private static void RegisterValue(
        Span<ContainerFrame> stack,
        int depth,
        ref int totalEntries,
        ref bool rootStarted)
    {
        if (depth == 0)
        {
            if (rootStarted)
                throw Invalid("The bencode document has multiple root values.");
            rootStarted = true;
            return;
        }

        ref var parent = ref stack[depth - 1];
        if (parent.Kind == ContainerKind.Dictionary && parent.ExpectingKey)
            throw Invalid("Dictionary keys must be byte strings.");
        if (++parent.EntryCount > MaximumEntries || ++totalEntries > MaximumEntries)
            throw Invalid($"Container entries exceed {MaximumEntries}.");
        if (parent.Kind == ContainerKind.Dictionary)
            parent.ExpectingKey = true;
    }

    private static void ParseString(ReadOnlySpan<byte> data, ref int index)
    {
        var lengthStart = index;
        long length = 0;
        while (index < data.Length && IsDigit(data[index]))
        {
            if (index > lengthStart && data[lengthStart] == (byte)'0')
                throw Invalid("Byte-string lengths cannot contain leading zeroes.");
            var digit = data[index] - (byte)'0';
            if (length > (MaximumStringBytes - digit) / 10)
                throw Invalid($"A byte string exceeds {MaximumStringBytes} bytes.");
            length = length * 10 + digit;
            if (length > MaximumStringBytes)
                throw Invalid($"A byte string exceeds {MaximumStringBytes} bytes.");
            index++;
        }

        if (index == lengthStart || index >= data.Length || data[index] != (byte)':')
            throw Invalid("A byte string has an invalid length prefix.");
        index++;
        if (length > data.Length - index)
            throw Invalid("A byte string extends beyond the document boundary.");
        index += (int)length;
    }

    private static void ParseInteger(ReadOnlySpan<byte> data, ref int index)
    {
        index++;
        var valueStart = index;
        if (index < data.Length && data[index] == (byte)'-')
            index++;
        var digitsStart = index;
        while (index < data.Length && IsDigit(data[index]))
            index++;

        var valueLength = index - valueStart;
        var digitLength = index - digitsStart;
        if (digitLength == 0 || index >= data.Length || data[index] != (byte)'e')
            throw Invalid("An integer has invalid syntax or is unterminated.");
        if (digitLength > 1 && data[digitsStart] == (byte)'0')
            throw Invalid("Integers cannot contain leading zeroes.");
        if (data[valueStart] == (byte)'-' && data[digitsStart] == (byte)'0')
            throw Invalid("Negative zero is not valid bencode.");
        if (valueLength > MaximumIntegerBytes ||
            !Utf8Parser.TryParse(data.Slice(valueStart, valueLength), out long _, out var consumed) ||
            consumed != valueLength)
            throw Invalid("The integer is outside the supported 64-bit range.");
        index++;
    }

    private static void IncrementNode(ref int nodes)
    {
        if (++nodes > MaximumNodes)
            throw Invalid($"Bencode nodes exceed {MaximumNodes}.");
    }

    private static bool IsDigit(byte value) => value is >= (byte)'0' and <= (byte)'9';

    private static FormatException Invalid(string message) => new(message);

    private enum ContainerKind : byte
    {
        List,
        Dictionary
    }

    private struct ContainerFrame(ContainerKind kind, bool expectingKey)
    {
        public ContainerKind Kind { get; } = kind;
        public bool ExpectingKey { get; set; } = expectingKey;
        public int EntryCount { get; set; }
    }
}
