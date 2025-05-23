﻿using System;

namespace PKHeX.Core.Injection;

public static class Decoder
{
    private static bool IsNum(char c) => (uint)(c - '0') <= 9;

    private static bool IsHexUpper(char c) => (uint)(c - 'A') <= 5;

    public static byte[] ConvertHexByteStringToBytes(ReadOnlySpan<byte> bytes)
    {
        var dest = new byte[bytes.Length / 2];
        ConvertHexByteStringToBytes(bytes, dest);
        return dest;
    }

    public static void ConvertHexByteStringToBytes(ReadOnlySpan<byte> bytes, Span<byte> dest)
    {
        for (int i = 0; i < dest.Length; i++)
        {
            var _0 = (char)bytes[(i * 2) + 0];
            var _1 = (char)bytes[(i * 2) + 1];
            dest[i] = DecodeTuple(_0, _1);
        }
    }

    public static byte[] StringToByteArray(ReadOnlySpan<char> hex) => Convert.FromHexString(hex);

    private static byte DecodeTuple(char _0, char _1)
    {
        byte result;
        if (IsNum(_0))
            result = (byte)((_0 - '0') << 4);
        else if (IsHexUpper(_0))
            result = (byte)((_0 - 'A' + 10) << 4);
        else
            throw new ArgumentOutOfRangeException(nameof(_0));

        if (IsNum(_1))
            result |= (byte)(_1 - '0');
        else if (IsHexUpper(_1))
            result |= (byte)(_1 - 'A' + 10);
        else
            throw new ArgumentOutOfRangeException(nameof(_1));

        return result;
    }
}
