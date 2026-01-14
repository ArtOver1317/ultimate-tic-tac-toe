using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class GameModeWizardObservableExtensionsTests
    {
        [Test]
        public void WhenVersionChanged_ThenDistinctUntilVersionChangedEmits()
        {
            // Arrange
            using var subject = new Subject<GameModeSessionSnapshot>();
            var observer = new RecordingObserver<GameModeSessionSnapshot>();

            using var sub = subject
                .DistinctUntilVersionChanged()
                .Subscribe(observer);

            // Act
            subject.OnNext(GameModeSessionSnapshot.Default.WithVersion(1));
            subject.OnNext(GameModeSessionSnapshot.Default.WithVersion(2));

            // Assert
            observer.Values.Should().HaveCount(2);
            observer.Values[0].Version.Should().Be(1);
            observer.Values[1].Version.Should().Be(2);
        }

        [Test]
        public void WhenVersionSame_ThenDistinctUntilVersionChangedIgnores()
        {
            // Arrange
            using var subject = new Subject<GameModeSessionSnapshot>();
            var observer = new RecordingObserver<GameModeSessionSnapshot>();

            using var sub = subject
                .DistinctUntilVersionChanged()
                .Subscribe(observer);

            // Act
            subject.OnNext(GameModeSessionSnapshot.Default.WithVersion(1));
            subject.OnNext(GameModeSessionSnapshot.Default.WithVersion(1));
            subject.OnNext(GameModeSessionSnapshot.Default.WithVersion(1));

            // Assert
            observer.Values.Should().HaveCount(1);
            observer.Values[0].Version.Should().Be(1);
        }

        [Test]
        public void WhenSourceEmitsNullSnapshot_ThenDistinctUntilVersionChangedSkipsNull()
        {
            // Arrange
            using var subject = new Subject<GameModeSessionSnapshot>();
            var observer = new RecordingObserver<GameModeSessionSnapshot>();

            using var sub = subject
                .DistinctUntilVersionChanged()
                .Subscribe(observer);

            // Act
            subject.OnNext(null);
            subject.OnNext(GameModeSessionSnapshot.Default.WithVersion(1));

            // Assert
            observer.Values.Should().HaveCount(1);
            observer.Values[0].Version.Should().Be(1);
        }

        [Test]
        public void WhenSourceCompletes_ThenDistinctUntilVersionChangedCompletes()
        {
            // Arrange
            using var subject = new Subject<GameModeSessionSnapshot>();
            var observer = new RecordingObserver<GameModeSessionSnapshot>();

            using var sub = subject
                .DistinctUntilVersionChanged()
                .Subscribe(observer);

            // Act
            subject.OnCompleted();

            // Assert
            observer.IsCompleted.Should().BeTrue();
        }

        [Test]
        public void WhenSourceErrors_ThenDistinctUntilVersionChangedPropagatesError()
        {
            // Arrange
            using var subject = new Subject<GameModeSessionSnapshot>();
            var observer = new RecordingObserver<GameModeSessionSnapshot>();

            using var sub = subject
                .DistinctUntilVersionChanged()
                .Subscribe(observer);

            var ex = new InvalidOperationException("boom");

            // Act
            subject.OnErrorResume(ex);

            // Assert
            observer.Error.Should().BeSameAs(ex);
        }

        [Test]
        public void WhenSourceIsNull_ThenDistinctUntilVersionChangedThrowsArgumentNullException()
        {
            // Arrange
            Observable<GameModeSessionSnapshot> source = null;

            // Act
            Action act = () => source.DistinctUntilVersionChanged();

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenSelectDistinctCalled_ThenEmitsOnlyChanges()
        {
            // Arrange
            using var subject = new Subject<GameModeSessionSnapshot>();
            var observer = new RecordingObserver<string>();

            using var sub = subject
                .SelectDistinct(s => s.SelectedModeId)
                .Subscribe(observer);

            // Act
            subject.OnNext(GameModeSessionSnapshot.Default.WithSelectedModeId("Classic"));
            subject.OnNext(GameModeSessionSnapshot.Default.WithSelectedModeId("Classic"));
            subject.OnNext(GameModeSessionSnapshot.Default.WithSelectedModeId("Ultimate"));

            // Assert
            observer.Values.Should().BeEquivalentTo(new[] { "Classic", "Ultimate" }, options => options.WithStrictOrdering());
        }

        [Test]
        public void WhenSourceEmitsNullSnapshot_ThenSelectDistinctSkipsNull()
        {
            // Arrange
            using var subject = new Subject<GameModeSessionSnapshot>();
            var observer = new RecordingObserver<string>();

            using var sub = subject
                .SelectDistinct(s => s.SelectedModeId)
                .Subscribe(observer);

            // Act
            subject.OnNext(null);
            subject.OnNext(GameModeSessionSnapshot.Default.WithSelectedModeId("Classic"));

            // Assert
            observer.Values.Should().BeEquivalentTo(new[] { "Classic" });
        }

        [Test]
        public void WhenSourceCompletes_ThenSelectDistinctCompletes()
        {
            // Arrange
            using var subject = new Subject<GameModeSessionSnapshot>();
            var observer = new RecordingObserver<string>();

            using var sub = subject
                .SelectDistinct(s => s.SelectedModeId)
                .Subscribe(observer);

            // Act
            subject.OnCompleted();

            // Assert
            observer.IsCompleted.Should().BeTrue();
        }

        [Test]
        public void WhenSourceErrors_ThenSelectDistinctPropagatesError()
        {
            // Arrange
            using var subject = new Subject<GameModeSessionSnapshot>();
            var observer = new RecordingObserver<string>();

            using var sub = subject
                .SelectDistinct(s => s.SelectedModeId)
                .Subscribe(observer);

            var ex = new Exception("fail");

            // Act
            subject.OnErrorResume(ex);

            // Assert
            observer.Error.Should().BeSameAs(ex);
        }

        [Test]
        public void WhenSourceIsNull_ThenSelectDistinctThrowsArgumentNullException()
        {
            // Arrange
            Observable<GameModeSessionSnapshot> source = null;

            // Act
            Action act = () => source.SelectDistinct(s => s.Version);

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenSelectorIsNull_ThenSelectDistinctThrowsArgumentNullException()
        {
            // Arrange
            using var subject = new Subject<GameModeSessionSnapshot>();

            // Act
            Action act = () => subject.SelectDistinct<string>(selector: null);

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenComparerProvided_ThenSelectDistinctUsesComparer()
        {
            // Arrange
            using var subject = new Subject<GameModeSessionSnapshot>();
            var observer = new RecordingObserver<string>();

            using var sub = subject
                .SelectDistinct(s => s.SelectedModeId, comparer: StringComparer.OrdinalIgnoreCase)
                .Subscribe(observer);

            // Act
            subject.OnNext(GameModeSessionSnapshot.Default.WithSelectedModeId("classic"));
            subject.OnNext(GameModeSessionSnapshot.Default.WithSelectedModeId("CLASSIC"));
            subject.OnNext(GameModeSessionSnapshot.Default.WithSelectedModeId("ultimate"));

            // Assert
            observer.Values.Should().BeEquivalentTo(new[] { "classic", "ultimate" }, options => options.WithStrictOrdering());
        }

        private sealed class RecordingObserver<T> : Observer<T>
        {
            public List<T> Values { get; } = new();
            public Exception Error { get; private set; }
            public bool IsCompleted { get; private set; }

            protected override void OnNextCore(T value) => Values.Add(value);

            protected override void OnErrorResumeCore(Exception error) => Error = error;

            protected override void OnCompletedCore(Result result) => IsCompleted = true;
        }
    }
}
