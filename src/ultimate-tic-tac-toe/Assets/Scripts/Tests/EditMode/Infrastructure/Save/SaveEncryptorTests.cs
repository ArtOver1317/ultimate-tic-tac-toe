using System;
using System.Reflection;
using System.Text;
using FluentAssertions;
using NUnit.Framework;

namespace Tests.EditMode.Infrastructure.Save
{
    [Category("Unit")]
    public class SaveEncryptorTests
    {
        private object _encryptor;
        private Type _encryptorType;

        [SetUp]
        public void Setup()
        {
            _encryptorType = Type.GetType("Runtime.Infrastructure.Save.SaveEncryptor, Runtime");
            _encryptorType.Should().NotBeNull();

            _encryptor = Activator.CreateInstance(_encryptorType);
            _encryptor.Should().NotBeNull();
        }

        [Test]
        public void WhenEncryptThenDecrypt_ThenReturnsOriginalPayload()
        {
            const string payload = "{\"version\":1,\"sections\":{\"locale\":{\"code\":\"en-US\"}}}";

            var encrypted = InvokeEncrypt(payload);
            var decrypted = InvokeDecrypt(encrypted);

            decrypted.Should().Be(payload);
        }

        [Test]
        public void WhenEncryptCalledWithNull_ThenThrowsArgumentNullException()
        {
            Action act = () => InvokeEncrypt(null);

            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenXxTeaEncryptThenDecrypt_ThenReturnsOriginalBytes()
        {
            const string payload = "{\"version\":1,\"sections\":{\"locale\":\"ru-RU\"}}";
            var plainBytes = Encoding.UTF8.GetBytes(payload);
            var keyField = _encryptorType.GetField("Key", BindingFlags.NonPublic | BindingFlags.Static);
            keyField.Should().NotBeNull();
            var key = (uint[])keyField.GetValue(null);

            var encryptMethod = _encryptorType.GetMethod("XXTeaEncrypt", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(byte[]), typeof(uint[]) }, null);
            var decryptMethod = _encryptorType.GetMethod("XXTeaDecrypt", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(byte[]), typeof(uint[]) }, null);

            encryptMethod.Should().NotBeNull();
            decryptMethod.Should().NotBeNull();

            var encrypted = (byte[])encryptMethod.Invoke(null, new object[] { plainBytes, key });
            var decrypted = (byte[])decryptMethod.Invoke(null, new object[] { encrypted, key });

            Encoding.UTF8.GetString(decrypted).Should().Be(payload);
        }

#if SAVE_ENCRYPTION_DISABLED || UNITY_EDITOR || DEVELOPMENT_BUILD
        [Test]
        public void WhenSaveEncryptionDisabledAndEncryptCalled_ThenReturnsInputWithoutModification()
        {
            const string payload = "{\"version\":1}";

            var encrypted = InvokeEncrypt(payload);

            encrypted.Should().Be(payload);
        }
#else
        [Test]
        public void WhenSaveEncryptionEnabledAndEncryptCalled_ThenReturnsDifferentValue()
        {
            const string payload = "{\"version\":1}";

            var encrypted = InvokeEncrypt(payload);

            encrypted.Should().NotBe(payload);
        }
#endif

        private string InvokeEncrypt(string value)
            => InvokeMethod("Encrypt", value);

        private string InvokeDecrypt(string value)
            => InvokeMethod("Decrypt", value);

        private string InvokeMethod(string methodName, string value)
        {
            var method = _encryptorType.GetMethod(methodName);
            method.Should().NotBeNull();

            try
            {
                return (string)method.Invoke(_encryptor, new object[] { value });
            }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }
    }
}