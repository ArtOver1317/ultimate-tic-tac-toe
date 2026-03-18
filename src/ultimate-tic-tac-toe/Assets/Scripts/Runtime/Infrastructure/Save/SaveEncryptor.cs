using System;
using System.Text;

namespace Runtime.Infrastructure.Save
{
    internal sealed class SaveEncryptor
    {
        private const uint _teaDelta = 0x9E3779B9u;
        private const int _minimumWordCount = 2;
        private const int _baseRoundCount = 6;
        private const int _roundFactor = 52;
        private const int _keyIndexMask = 3;
        private const int _bytesPerUInt32 = sizeof(uint);
        private const int _bitsPerByte = 8;

        private static readonly uint[] _xxTeaKey =
        {
            0xD3A7B19Fu,
            0x8C4E2D71u,
            0xA95F6B23u,
            0x17CE84DAu,
        };

        public string Encrypt(string plainJson)
        {
            if (plainJson == null)
                throw new ArgumentNullException(nameof(plainJson));

#if SAVE_ENCRYPTION_DISABLED || UNITY_EDITOR || DEVELOPMENT_BUILD
            return plainJson;
#else
            if (plainJson.Length == 0)
                return string.Empty;

            var plainBytes = Encoding.UTF8.GetBytes(plainJson);
            var encrypted = XXTeaEncrypt(plainBytes, _xxTeaKey);
            return Convert.ToBase64String(encrypted);
#endif
        }

        public string Decrypt(string base64)
        {
            if (base64 == null)
                throw new ArgumentNullException(nameof(base64));

#if SAVE_ENCRYPTION_DISABLED || UNITY_EDITOR || DEVELOPMENT_BUILD
            return base64;
#else
            if (base64.Length == 0)
                return string.Empty;

            var encryptedBytes = Convert.FromBase64String(base64);
            var plainBytes = XXTeaDecrypt(encryptedBytes, _xxTeaKey);
            return Encoding.UTF8.GetString(plainBytes);
#endif
        }

        private static byte[] XXTeaEncrypt(byte[] data, uint[] key)
        {
            var values = ToUInt32Array(data, true);
            var encrypted = XXTeaEncrypt(values, key);
            return ToByteArray(encrypted, false);
        }

        private static byte[] XXTeaDecrypt(byte[] data, uint[] key)
        {
            var values = ToUInt32Array(data, false);
            var decrypted = XXTeaDecrypt(values, key);
            return ToByteArray(decrypted, true);
        }

        private static uint[] XXTeaEncrypt(uint[] values, uint[] key)
        {
            if (values.Length < _minimumWordCount)
                return values;

            var rounds = GetRoundCount(values.Length);
            uint sum = 0;
            var previousValue = values[^1];

            while (rounds-- > 0)
            {
                sum += _teaDelta;
                var keyOffset = GetKeyOffset(sum);

                for (var index = 0; index < values.Length - 1; index++)
                {
                    var nextValue = values[index + 1];
                    var keyValue = key[GetKeyIndex(index, keyOffset)];
                    previousValue = values[index] += ComputeMix(previousValue, nextValue, sum, keyValue);
                }

                var firstValue = values[0];
                var lastKeyValue = key[GetKeyIndex(values.Length - 1, keyOffset)];
                previousValue = values[^1] += ComputeMix(previousValue, firstValue, sum, lastKeyValue);
            }

            return values;
        }

        private static uint[] XXTeaDecrypt(uint[] values, uint[] key)
        {
            if (values.Length < _minimumWordCount)
                return values;

            var rounds = GetRoundCount(values.Length);
            var sum = (uint)(rounds * _teaDelta);
            var nextValue = values[0];

            while (sum != 0)
            {
                var keyOffset = GetKeyOffset(sum);

                for (var index = values.Length - 1; index > 0; index--)
                {
                    var previousValue = values[index - 1];
                    var keyValue = key[GetKeyIndex(index, keyOffset)];
                    nextValue = values[index] -= ComputeMix(previousValue, nextValue, sum, keyValue);
                }

                var lastValue = values[^1];
                var firstKeyValue = key[GetKeyIndex(0, keyOffset)];
                nextValue = values[0] -= ComputeMix(lastValue, nextValue, sum, firstKeyValue);
                sum -= _teaDelta;
            }

            return values;
        }

        private static uint[] ToUInt32Array(byte[] data, bool includeLength)
        {
            var valueCount = GetValueCount(data.Length);
            var result = includeLength ? new uint[valueCount + 1] : new uint[valueCount];

            if (includeLength)
                result[valueCount] = (uint)data.Length;

            for (var index = 0; index < data.Length; index++)
            {
                result[GetValueIndex(index)] |= (uint)data[index] << GetByteShift(index);
            }

            return result;
        }

        private static byte[] ToByteArray(uint[] data, bool includeLength)
        {
            var length = data.Length * _bytesPerUInt32;

            if (includeLength)
            {
                var originalLength = data[^1];

                if (originalLength > length - _bytesPerUInt32)
                    return Array.Empty<byte>();

                length = (int)originalLength;
            }

            var result = new byte[length];

            for (var index = 0; index < length; index++)
            {
                result[index] = (byte)(data[GetValueIndex(index)] >> GetByteShift(index));
            }

            return result;
        }

        private static int GetRoundCount(int valueCount)
            => _baseRoundCount + (_roundFactor / valueCount);

        private static uint GetKeyOffset(uint sum)
            => (sum >> 2) & _keyIndexMask;

        private static int GetKeyIndex(int position, uint keyOffset)
            => (position & _keyIndexMask) ^ (int)keyOffset;

        private static int GetValueCount(int byteCount)
            => (byteCount + _bytesPerUInt32 - 1) / _bytesPerUInt32;

        private static int GetValueIndex(int byteIndex)
            => byteIndex / _bytesPerUInt32;

        private static int GetByteShift(int byteIndex)
            => (byteIndex % _bytesPerUInt32) * _bitsPerByte;

        private static uint ComputeMix(uint previousValue, uint nextValue, uint sum, uint keyValue)
        {
            var neighborMix = ((previousValue >> 5) ^ (nextValue << 2)) + ((nextValue >> 3) ^ (previousValue << 4));
            return neighborMix ^ ((sum ^ nextValue) + (keyValue ^ previousValue));
        }
    }
}