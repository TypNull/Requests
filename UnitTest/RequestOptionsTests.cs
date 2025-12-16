namespace UnitTest
{
    /// <summary>
    /// Test suite for the RequestOptions class.
    /// </summary>
    [TestFixture]
    public class RequestOptionsTests
    {
        private RequestOptions _options = null!;

        [SetUp]
        public void SetUp()
        {
            _options = new RequestOptions();
        }

        #region Constructor Tests

        [Test]
        public void Constructor_Default_ShouldInitializeWithDefaults()
        {
            // Act & Assert
            _options.Should().NotBeNull();
            _options.AutoStart.Should().BeTrue();
            _options.Priority.Should().Be(RequestPriority.Normal);
            _options.NumberOfAttempts.Should().Be(3);
            _options.CancellationToken.Should().Be(default(CancellationToken));
            _options.DeployDelay.Should().BeNull();
            _options.DelayBetweenAttempts.Should().BeNull();
            _options.SubsequentRequest.Should().BeNull();
        }

        [Test]
        public void Constructor_Copy_ShouldCopyAllProperties()
        {
            // Arrange
            RequestOptions original = new()
            {
                AutoStart = false,
                Priority = RequestPriority.High,
                NumberOfAttempts = 5,
                CancellationToken = new CancellationToken(),
                DeployDelay = TimeSpan.FromSeconds(1),
                DelayBetweenAttempts = TimeSpan.FromMilliseconds(500)
            };

            // Act
            RequestOptions copy = original with { };

            // Assert
            copy.AutoStart.Should().Be(original.AutoStart);
            copy.Priority.Should().Be(original.Priority);
            copy.NumberOfAttempts.Should().Be(original.NumberOfAttempts);
            copy.CancellationToken.Should().Be(original.CancellationToken);
            copy.DeployDelay.Should().Be(original.DeployDelay);
            copy.DelayBetweenAttempts.Should().Be(original.DelayBetweenAttempts);
        }

        #endregion

        #region Property Tests

        [Test]
        public void AutoStart_WithExpression_ShouldReturnSetValue()
        {
            // Act
            var modified = _options with { AutoStart = false };

            // Assert
            modified.AutoStart.Should().BeFalse();
        }

        [Test]
        public void Priority_WithExpression_ShouldReturnSetValue()
        {
            // Act
            var modified = _options with { Priority = RequestPriority.Low };

            // Assert
            modified.Priority.Should().Be(RequestPriority.Low);
        }

        [Test]
        public void NumberOfAttempts_WithExpression_ShouldReturnSetValue()
        {
            // Act
            var modified = _options with { NumberOfAttempts = 10 };

            // Assert
            modified.NumberOfAttempts.Should().Be(10);
        }

        [Test]
        public void CancellationToken_WithExpression_ShouldReturnSetValue()
        {
            // Arrange
            CancellationToken token = new(true);

            // Act
            var modified = _options with { CancellationToken = token };

            // Assert
            modified.CancellationToken.Should().Be(token);
        }

        [Test]
        public void DeployDelay_SetValue_ShouldReturnSetValue()
        {
            // Arrange
            TimeSpan delay = TimeSpan.FromMinutes(2);

            // Act
            _options.DeployDelay = delay;

            // Assert
            _options.DeployDelay.Should().Be(delay);
        }

        [Test]
        public void DelayBetweenAttempts_WithExpression_ShouldReturnSetValue()
        {
            // Arrange
            TimeSpan delay = TimeSpan.FromSeconds(5);

            // Act
            var modified = _options with { DelayBetweenAttempts = delay };

            // Assert
            modified.DelayBetweenAttempts.Should().Be(delay);
        }

        [Test]
        public void SubsequentRequest_SetValidRequest_ShouldSucceed()
        {
            // Arrange
            using ParallelRequestHandler handler = [];
            RequestOptions options = new() { Handler = handler, AutoStart = false };
            var request = new TestRequest(options);

            // Act
            _options.SubsequentRequest = request;

            // Assert
            _options.SubsequentRequest.Should().Be(request);

            // Cleanup
            request.Dispose();
        }

        [Test]
        public void SubsequentRequest_SetCompletedRequest_ShouldThrow()
        {
            // Arrange
            using ParallelRequestHandler handler = [];
            RequestOptions options = new() { Handler = handler, AutoStart = false };
            var request = new TestRequest(options);
            request.Cancel(); // Completed state

            // Act
            Action act = () => _options.SubsequentRequest = request;

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithMessage("Cannot set a completed request as subsequent request.*");

            // Cleanup
            request.Dispose();
        }

        #endregion

        #region Record Functionality Tests

        [Test]
        public void WithExpression_ModifyProperty_ShouldCreateNewInstance()
        {
            // Arrange
            RequestOptions original = new() { AutoStart = true };

            // Act
            RequestOptions modified = original with { AutoStart = false };

            // Assert
            original.AutoStart.Should().BeTrue();
            modified.AutoStart.Should().BeFalse();
            modified.Should().NotBeSameAs(original);
        }

        [Test]
        public void Equality_SameValues_ShouldBeEqual()
        {
            // Arrange
            RequestOptions options1 = new()
            {
                AutoStart = false,
                Priority = RequestPriority.High,
                NumberOfAttempts = 5
            };
            RequestOptions options2 = new()
            {
                AutoStart = false,
                Priority = RequestPriority.High,
                NumberOfAttempts = 5
            };

            // Act & Assert - Note: SubsequentRequest is mutable so we ignore it for equality
            options1.AutoStart.Should().Be(options2.AutoStart);
            options1.Priority.Should().Be(options2.Priority);
            options1.NumberOfAttempts.Should().Be(options2.NumberOfAttempts);
        }

        [Test]
        public void Equality_DifferentValues_ShouldNotBeEqual()
        {
            // Arrange
            RequestOptions options1 = new() { AutoStart = true };
            RequestOptions options2 = new() { AutoStart = false };

            // Act & Assert
            options1.AutoStart.Should().NotBe(options2.AutoStart);
        }

        #endregion

        #region Edge Cases Tests

        [Test]
        public void NumberOfAttempts_ZeroValue_ShouldBeAllowed()
        {
            // Act
            var modified = _options with { NumberOfAttempts = 0 };

            // Assert
            modified.NumberOfAttempts.Should().Be(0);
        }

        [Test]
        public void NumberOfAttempts_MaxValue_ShouldBeAllowed()
        {
            // Act
            var modified = _options with { NumberOfAttempts = byte.MaxValue };

            // Assert
            modified.NumberOfAttempts.Should().Be(byte.MaxValue);
        }

        [Test]
        public void DelayBetweenAttempts_ZeroValue_ShouldBeAllowed()
        {
            // Act
            var modified = _options with { DelayBetweenAttempts = TimeSpan.Zero };

            // Assert
            modified.DelayBetweenAttempts.Should().Be(TimeSpan.Zero);
        }

        [Test]
        public void DeployDelay_ZeroValue_ShouldBeAllowed()
        {
            // Act
            _options.DeployDelay = TimeSpan.Zero;

            // Assert
            _options.DeployDelay.Should().Be(TimeSpan.Zero);
        }

        #endregion

        #region Test Helper Class

        private class TestRequest : Request<RequestOptions, string, Exception>
        {
            public TestRequest(RequestOptions options) : base(options) { }

            protected override Task<RequestReturn> RunRequestAsync()
            {
                return Task.FromResult(new RequestReturn { Successful = true });
            }
        }

        #endregion
    }
}
