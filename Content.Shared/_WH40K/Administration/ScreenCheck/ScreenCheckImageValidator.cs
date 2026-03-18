namespace Content.Shared._WH40K.Administration.ScreenCheck;

public static class ScreenCheckImageValidator
{
    public const int MaxImageBytes = 8 * 1024 * 1024;
    public const int MaxImageWidth = 6000;
    public const int MaxImageHeight = 6000;
    public const int MaxImagePixels = 12_000_000;

    public static bool IsAllowedDimensions(int width, int height)
    {
        return width > 0
            && height > 0
            && width <= MaxImageWidth
            && height <= MaxImageHeight
            && (long) width * height <= MaxImagePixels;
    }

    public static bool IsValidEncodedJpeg(byte[]? imageData)
    {
        if (imageData is not { Length: > 0 and <= MaxImageBytes })
            return false;

        return TryGetJpegDimensions(imageData, out var width, out var height)
               && IsAllowedDimensions(width, height);
    }

    public static bool TryGetJpegDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            return false;

        var index = 2;

        while (index + 1 < data.Length)
        {
            while (index < data.Length && data[index] != 0xFF)
            {
                index++;
            }

            if (index + 1 >= data.Length)
                return false;

            while (index < data.Length && data[index] == 0xFF)
            {
                index++;
            }

            if (index >= data.Length)
                return false;

            var marker = data[index++];

            if (marker == 0xD9)
                break;

            if (marker == 0xD8 || marker == 0x01 || marker is >= 0xD0 and <= 0xD7)
                continue;

            if (index + 1 >= data.Length)
                return false;

            var segmentLength = (data[index] << 8) | data[index + 1];
            if (segmentLength < 2)
                return false;

            var segmentEnd = index + segmentLength;
            if (segmentEnd > data.Length)
                return false;

            if (IsStartOfFrame(marker))
            {
                if (segmentLength < 7)
                    return false;

                height = (data[index + 3] << 8) | data[index + 4];
                width = (data[index + 5] << 8) | data[index + 6];
                return width > 0 && height > 0;
            }

            index = segmentEnd;
        }

        return false;
    }

    private static bool IsStartOfFrame(byte marker)
    {
        return marker is
            0xC0 or 0xC1 or 0xC2 or 0xC3 or
            0xC5 or 0xC6 or 0xC7 or
            0xC9 or 0xCA or 0xCB or
            0xCD or 0xCE or 0xCF;
    }
}
