using System;
using System.Reflection;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Infrastructure.Save;

namespace Tests.EditMode.Infrastructure.Save
{
    [Category("Unit")]
    public class SaveEncryptorTests
    {
        private SaveEncryptor _encryptor;

        [SetUp]
        public void SetUp() => _encryptor = new SaveEncryptor();

        [Test]
        public void WhenEncryptThenDecrypt_ThenReturnsOriginalPayload()
        {
            const string payload = "{\"version\":1,\"sections\":{\"locale\":{\"code\":\"en-US\"}}}";

            var encrypted = _encryptor.Encrypt(payload);
            var decrypted = _encryptor.Decrypt(encrypted);

            decrypted.Should().Be(payload);
        }

        [Test]
        public void WhenEncryptCalledWithNull_ThenThrowsArgumentNullException()
        {
            Action act = () => _encryptor.Encrypt(null);

            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenDecryptCalledWithNull_ThenThrowsArgumentNullException()
        {
            Action act = () => _encryptor.Decrypt(null);

            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenXxTeaEncryptThenDecrypt_ThenReturnsOriginalBytes()
        {
            const string payload = "{\"version\":1,\"sections\":{\"locale\":\"ru-RU\"}}";
            var plainBytes = Encoding.UTF8.GetBytes(payload);
            var type = typeof(SaveEncryptor);

            var keyField = type.GetField("_xxTeaKey", BindingFlags.NonPublic | BindingFlags.Static);
            keyField.Should().NotBeNull();
            var key = (uint[])keyField.GetValue(null);

            var encryptMethod = type.GetMethod("XXTeaEncrypt", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(byte[]), typeof(uint[]) }, null);
            var decryptMethod = type.GetMethod("XXTeaDecrypt", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(byte[]), typeof(uint[]) }, null);

            encryptMethod.Should().NotBeNull();
            decryptMethod.Should().NotBeNull();

            var encrypted = (byte[])encryptMethod.Invoke(null, new object[] { plainBytes, key });
            var decrypted = (byte[])decryptMethod.Invoke(null, new object[] { encrypted, key });

            encrypted.Should().NotEqual(plainBytes);
            decrypted.Should().Equal(plainBytes);
        }

#if SAVE_ENCRYPTION_DISABLED || UNITY_EDITOR || DEVELOPMENT_BUILD
        [Test]
        public void WhenSaveEncryptionDisabledAndEncryptCalled_ThenReturnsInputWithoutModification()
        {
            const string payload = "{\"version\":1}";

            var encrypted = _encryptor.Encrypt(payload);

            encrypted.Should().Be(payload);
        }
#else
        [Test]
        public void WhenSaveEncryptionEnabledAndEncryptCalled_ThenReturnsDifferentValue()
        {
            const string payload = "{\"version\":1}";

            var encrypted = _encryptor.Encrypt(payload);

            encrypted.Should().NotBe(payload);
        }
#endif
    }
}
