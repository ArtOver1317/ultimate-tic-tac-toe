using System;
using System.Text;

namespace Runtime.Infrastructure.Save
{
    internal class SaveEncryptor
    {
        private static readonly uint[] Key = { 0xD3A7B19Fu, 0x8C4E2D71u, 0xA95F6B23u, 0x17CE84DAu };

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
            var encrypted = XXTeaEncrypt(plainBytes, Key);
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
            var plainBytes = XXTeaDecrypt(encryptedBytes, Key);
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
            var count = values.Length;
            if (count < 2)
                return values;

            const uint delta = 0x9E3779B9u;
            var rounds = 6 + 52 / count;
            uint sum = 0;
            var z = values[count - 1];

            while (rounds-- > 0)
            {
                sum += delta;
                var e = (sum >> 2) & 3;

                for (var p = 0; p < count - 1; p++)
                {
                    var y = values[p + 1];
                    var mx = ((z >> 5) ^ (y << 2)) + ((y >> 3) ^ (z << 4));
                    mx ^= (sum ^ y) + (key[(p & 3) ^ e] ^ z);
                    z = values[p] += mx;
                }

                var yLast = values[0];
                var mxLast = ((z >> 5) ^ (yLast << 2)) + ((yLast >> 3) ^ (z << 4));
                mxLast ^= (sum ^ yLast) + (key[((count - 1) & 3) ^ e] ^ z);
                z = values[count - 1] += mxLast;
            }

            return values;
        }

        private static uint[] XXTeaDecrypt(uint[] values, uint[] key)
        {
            var count = values.Length;
            if (count < 2)
                return values;

            const uint delta = 0x9E3779B9u;
            var rounds = 6 + 52 / count;
            uint sum = (uint)(rounds * delta);
            var y = values[0];

            while (sum != 0)
            {
                var e = (sum >> 2) & 3;

                for (var p = count - 1; p > 0; p--)
                {
                    var z = values[p - 1];
                    var mx = ((z >> 5) ^ (y << 2)) + ((y >> 3) ^ (z << 4));
                    mx ^= (sum ^ y) + (key[(p & 3) ^ e] ^ z);
                    y = values[p] -= mx;
                }

                var zLast = values[count - 1];
                var mxLast = ((zLast >> 5) ^ (y << 2)) + ((y >> 3) ^ (zLast << 4));
                mxLast ^= (sum ^ y) + (key[e] ^ zLast);
                y = values[0] -= mxLast;
                sum -= delta;
            }

            return values;
        }

        private static uint[] ToUInt32Array(byte[] data, bool includeLength)
        {
            var length = data.Length;
            var resultLength = (length & 3) == 0 ? (length >> 2) : ((length >> 2) + 1);

            uint[] result;
            if (includeLength)
            {
                result = new uint[resultLength + 1];
                result[resultLength] = (uint)length;
            }
            else
            {
                result = new uint[resultLength];
            }

            for (var index = 0; index < length; index++)
            {
                result[index >> 2] |= (uint)data[index] << ((index & 3) << 3);
            }

            return result;
        }

        private static byte[] ToByteArray(uint[] data, bool includeLength)
        {
            var length = data.Length << 2;

            if (includeLength)
            {
                var m = data[data.Length - 1];
                if (m > length - 4)
                    return Array.Empty<byte>();

                length = (int)m;
            }

            var result = new byte[length];
            for (var index = 0; index < length; index++)
            {
                result[index] = (byte)(data[index >> 2] >> ((index & 3) << 3));
            }

            return result;
        }
    }
}