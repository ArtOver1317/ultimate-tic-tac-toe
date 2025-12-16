using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace StripLog.Tests
{
    public sealed class LogTests
    {
        private RecordingLogHandler _handler;

        [SetUp]
        public void SetUp()
        {
            Log.ResetForTests();
            _handler = new RecordingLogHandler();
            Log.Handler = _handler;
            Log.MinLevel = LogLevel.Debug;
        }

        [TearDown]
        public void TearDown() => Log.ResetForTests();

        [Test]
        public void WhenMinLevelIsWarning_ThenInfoDoesNotLog()
        {
            Log.MinLevel = LogLevel.Warning;

            Log.Info("Test", "Hello");

            Assert.That(_handler.Entries.Count, Is.EqualTo(0));
        }

        [Test]
        public void WhenMinLevelIsWarning_ThenWarningLogs()
        {
            Log.MinLevel = LogLevel.Warning;

            Log.Warning("Test", "Hello");

            Assert.That(_handler.Entries.Count, Is.EqualTo(1));
            Assert.That(_handler.Entries[0].Level, Is.EqualTo(LogLevel.Warning));
        }

        [Test]
        public void WhenTagIsMuted_ThenLogIsSuppressed()
        {
            Log.MuteTag("Muted");

            Log.Info("Muted", "Hello");

            Assert.That(_handler.Entries.Count, Is.EqualTo(0));
        }

        [Test]
        public void WhenTagIsUnmuted_ThenLogIsAllowed()
        {
            Log.MuteTag("Muted");
            Log.UnmuteTag("Muted");

            Log.Info("Muted", "Hello");

            Assert.That(_handler.Entries.Count, Is.EqualTo(1));
        }

        [Test]
        public void WhenHandlerIsSetToNull_ThenItIsReplacedWithUnityLogHandler()
        {
            Log.Handler = null;

            Assert.That(Log.Handler, Is.Not.Null);
        }

        [Test]
        public void WhenExceptionIsOperationCanceled_ThenLogsAsInfo()
        {
            Log.Exception(new OperationCanceledException("cancel"), tag: "Net");

            Assert.That(_handler.Entries.Count, Is.EqualTo(1));
            Assert.That(_handler.Entries[0].Level, Is.EqualTo(LogLevel.Info));
            Assert.That(_handler.Entries[0].Tag, Is.EqualTo("Net"));
        }

        [Test]
        public void WhenExceptionIsRegular_ThenLogsAsError()
        {
            Log.Exception(new InvalidOperationException("boom"), tag: "Core");

            Assert.That(_handler.Entries.Count, Is.EqualTo(1));
            Assert.That(_handler.Entries[0].Level, Is.EqualTo(LogLevel.Error));
            Assert.That(_handler.Entries[0].Tag, Is.EqualTo("Core"));
        }

        [Test]
        public void WhenMuteTagIsCalledFromMultipleThreads_ThenDoesNotThrow()
        {
            var tag = "Concurrent";

            Parallel.For(0, 2000, i =>
            {
                if ((i & 1) == 0)
                    Log.MuteTag(tag);
                else
                    Log.UnmuteTag(tag);

                _ = Log.IsTagMuted(tag);
            });

            Log.UnmuteTag(tag);
            Assert.That(Log.IsTagMuted(tag), Is.False);
        }

        [Test]
        public void WhenLazyMessageFactoryIsUsed_ThenItIsNotInvokedIfFilteredOut()
        {
            Log.MinLevel = LogLevel.Error;

            var called = false;
            Log.Debug("Test", () =>
            {
                called = true;
                return "expensive";
            });

            Assert.That(called, Is.False);
            Assert.That(_handler.Entries.Count, Is.EqualTo(0));
        }

        [Test]
        public void WhenLazyMessageFactoryIsUsed_ThenItIsInvokedIfEnabled()
        {
            var called = false;
            Log.Debug("Test", () =>
            {
                called = true;
                return "expensive";
            });

            Assert.That(called, Is.True);
            Assert.That(_handler.Entries.Count, Is.EqualTo(1));
        }

        [Test]
        public void WhenErrorDevIsCalled_ThenLogsAsError()
        {
            Log.ErrorDev("Test", "Oops");

            Assert.That(_handler.Entries.Count, Is.EqualTo(1));
            Assert.That(_handler.Entries[0].Level, Is.EqualTo(LogLevel.Error));
            Assert.That(_handler.Entries[0].Tag, Is.EqualTo("Test"));
        }

        [Test]
        public void WhenScopedLoggerIsUsed_ThenItPassesItsTag()
        {
            var logger = new ScopedLogger("MyTag");

            logger.Info("Hello");

            Assert.That(_handler.Entries.Count, Is.EqualTo(1));
            Assert.That(_handler.Entries[0].Tag, Is.EqualTo("MyTag"));
            Assert.That(_handler.Entries[0].Message, Is.EqualTo("Hello"));
        }
    }
}