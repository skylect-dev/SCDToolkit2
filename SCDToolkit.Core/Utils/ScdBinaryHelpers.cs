using System;
using System.Linq;
using System.Text;

namespace SCDToolkit.Core.Utils
{
    public static class ScdBinaryHelpers
    {
        public static uint Read(byte[] file, int bits, int position)
        {
            int bytes = bits / 8;
            byte[] value = new byte[bytes];
            for (int i = 0; i < bytes; i++)
            {
                value[i] = file[position + i];
            }
            return bits switch
            {
                8 => value[0],
                16 => BitConverter.ToUInt16(value, 0),
                32 => BitConverter.ToUInt32(value, 0),
                _ => 0
            };
        }

        public static void Write(byte[] file, int value, int bits, int position)
        {
            int bytes = bits / 8;
            byte[] val = BitConverter.GetBytes((uint)value);
            for (int i = 0; i < bytes; i++)
            {
                file[position + i] = val[i];
            }
        }

        public static int SearchBytePattern(int position, byte[] data, byte[] pattern)
        {
            int patternLength = pattern.Length;
            int totalLength = data.Length;
            byte firstMatchByte = pattern[0];
            for (int i = position; i < totalLength; i++)
            {
                if (firstMatchByte == data[i] && totalLength - i >= patternLength)
                {
                    byte[] match = new byte[patternLength];
                    Array.Copy(data, i, match, 0, patternLength);
                    if (match.SequenceEqual(pattern))
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        public static int SearchTag(string tag, byte[] data)
        {
            byte[] pattern = Encoding.ASCII.GetBytes(tag);
            int value = SearchBytePattern(0, data, pattern);
            if (value != -1)
            {
                value = value + pattern.Length;
                value = GetTagData(value, data);
            }
            return value;
        }

        public static int GetTagData(int position, byte[] data)
        {
            if (position < 0 || position >= data.Length)
            {
                return -1;
            }

            // Skip non-digit characters
            while (position < data.Length && (data[position] - 0x30 < 0 || data[position] - 0x30 > 9))
            {
                position = position + 1;
            }

            if (position >= data.Length)
            {
                return -1;
            }

            int initialPosition = position;

            // Read digit characters
            while (position < data.Length && data[position] - 0x30 >= 0 && data[position] - 0x30 <= 9)
            {
                position = position + 1;
            }

            if (position == initialPosition)
            {
                return -1;
            }

            byte[] number = new byte[position - initialPosition];
            for (int i = 0; i < number.Length; i++)
            {
                number[i] = data[i + initialPosition];
            }
            string value = Encoding.ASCII.GetString(number);
            int tagData = Convert.ToInt32(value);
            return tagData;
        }
    }
}
