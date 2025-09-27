namespace UnitTest
{
    /// <summary>
    /// Test suite for the RequestOptions class.
    /// </summary>
    [TestFixture]
    public class RequestOptionsTests
    {
        private RequestOptions<string, Exception> _options = null!;

        [SetUp]
        public void SetUp()
        {
            _options = new RequestOptions<string, Exception>();
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
            _options.CancellationToken.Should().BeNull();
            _options.DeployDelay.Should().BeNull();
            _options.DelayBetweenAttemps.Should().BeNull();
            _options.SubsequentRequest.Should().BeNull();
        }

        [Test]
        public void Constructor_Copy_ShouldCopyAllProperties()
        {
            // Arrange
            RequestOptions<string, Exception> original = new()
            {
                AutoStart = false,
                Priority = RequestPriority.High,
                NumberOfAttempts = 5,
                CancellationToken = new CancellationToken(),
                DeployDelay = TimeSpan.FromSeconds(1),
                DelayBetweenAttemps = TimeSpan.FromMilliseconds(500),
                RequestStarted = (req) => { },
                RequestCompleted = (req, result) => { },
                RequestFailed = (req, error) => { },
                RequestCancelled = (req) => { },
                RequestExceptionOccurred = (req, ex) => { }
            };

            // Act
            RequestOptions<string, Exception> copy = original with { };

            // Assert
            copy.AutoStart.Should().Be(original.AutoStart);
            copy.Priority.Should().Be(original.Priority);
            copy.NumberOfAttempts.Should().Be(original.NumberOfAttempts);
            copy.CancellationToken.Should().Be(original.CancellationToken);
            copy.DeployDelay.Should().Be(original.DeployDelay);
            copy.DelayBetweenAttemps.Should().Be(original.DelayBetweenAttemps);
            copy.RequestStarted.Should().Be(original.RequestStarted);
            copy.RequestCompleted.Should().Be(original.RequestCompleted);
            copy.RequestExceptionOccurred.Should().Be(original.RequestExceptionOccurred);
            copy.RequestFailed.Should().Be(original.RequestFailed);
            copy.RequestCancelled.Should().Be(original.RequestCancelled);
        }

        #endregion

        #region Property Tests

        [Test]
        public void AutoStart_SetValue_ShouldReturnSetValue()
        {
            // Act
            _options.AutoStart = false;

            // Assert
            _options.AutoStart.Should().BeFalse();
        }

        [Test]
        public void Priority_SetValue_ShouldReturnSetValue()
        {
            // Act
            _options.Priority = RequestPriority.Low;

            // Assert
            _options.Priority.Should().Be(RequestPriority.Low);
        }

        [Test]
        public void NumberOfAttempts_SetValue_ShouldReturnSetValue()
        {
            // Act
            _options.NumberOfAttempts = 10;

            // Assert
            _options.NumberOfAttempts.Should().Be(10);
        }

        [Test]
        public void CancellationToken_SetValue_ShouldReturnSetValue()
        {
            // Arrange
            CancellationToken token = new(true);

            // Act
            _options.CancellationToken = token;

            // Assert
            _options.CancellationToken.Should().Be(token);
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
        public void DelayBetweenAttemps_SetValue_ShouldReturnSetValue()
        {
            // Arrange
            TimeSpan delay = TimeSpan.FromSeconds(5);

            // Act
            _options.DelayBetweenAttemps = delay;

            // Assert
            _options.DelayBetweenAttemps.Should().Be(delay);
        }

        #endregion

        #region Event Handler Tests

        [Test]
        public void RequestStarted_SetValue_ShouldReturnSetValue()
        {
            // Arrange
            bool eventFired = false;
            void Handler(IRequest req) => eventFired = true;

            // Act
            _options.RequestStarted = Handler;
            _options.RequestStarted?.Invoke(null!);

            // Assert
            _options.RequestStarted.Should().Be(Handler);
            eventFired.Should().BeTrue();
        }

        [Test]
        public void RequestCompleted_SetValue_ShouldReturnSetValue()
        {
            // Arrange
            bool eventFired = false;
            void Handler(IRequest req, string result) => eventFired = true;

            // Act
            _options.RequestCompleted = Handler;
            _options.RequestCompleted?.Invoke(null!, "test");

            // Assert
            _options.RequestCompleted.Should().Be(Handler);
            eventFired.Should().BeTrue();
        }

        [Test]
        public void RequestFailed_SetValue_ShouldReturnSetValue()
        {
            // Arrange
            bool eventFired = false;
            void Handler(IRequest req, Exception ex) => eventFired = true;

            // Act
            _options.RequestFailed = Handler;
            _options.RequestFailed?.Invoke(null!, new Exception());

            // Assert
            _options.RequestFailed.Should().Be(Handler);
            eventFired.Should().BeTrue();
        }

        [Test]
        public void RequestCancelled_SetValue_ShouldReturnSetValue()
        {
            // Arrange
            bool eventFired = false;
            void Handler(IRequest req) => eventFired = true;

            // Act
            _options.RequestCancelled = Handler;
            _options.RequestCancelled?.Invoke(null!);

            // Assert
            _options.RequestCancelled.Should().Be(Handler);
            eventFired.Should().BeTrue();
        }

        [Test]
        public void EventHandlers_MultipleSubscriptions_ShouldSupportMulticast()
        {
            // Arrange
            int count = 0;
            void Handler1(IRequest req) => count++;
            void Handler2(IRequest req) => count++;

            // Act
            _options.RequestStarted += Handler1;
            _options.RequestStarted += Handler2;
            _options.RequestStarted?.Invoke(null!);

            // Assert
            count.Should().Be(2);
        }

        #endregion

        #region Record Functionality Tests

        [Test]
        public void WithExpression_ModifyProperty_ShouldCreateNewInstance()
        {
            // Arrange
            RequestOptions<string, Exception> original = new()
            { AutoStart = true };

            // Act
            RequestOptions<string, Exception> modified = original with { AutoStart = false };

            // Assert
            original.AutoStart.Should().BeTrue();
            modified.AutoStart.Should().BeFalse();
            modified.Should().NotBeSameAs(original);
        }

        [Test]
        public void Equality_SameValues_ShouldBeEqual()
        {
            // Arrange
            RequestOptions<string, Exception> options1 = new()
            {
                AutoStart = false,
                Priority = RequestPriority.High,
                NumberOfAttempts = 5
            };
            RequestOptions<string, Exception> options2 = new()
            {
                AutoStart = false,
                Priority = RequestPriority.High,
                NumberOfAttempts = 5
            };

            // Act & Assert
            options1.Should().Be(options2);
            (options1 == options2).Should().BeTrue();
            options1.GetHashCode().Should().Be(options2.GetHashCode());
        }

        [Test]
        public void Equality_DifferentValues_ShouldNotBeEqual()
        {
            // Arrange
            RequestOptions<string, Exception> options1 = new()
            { AutoStart = true };
            RequestOptions<string, Exception> options2 = new()
            { AutoStart = false };

            // Act & Assert
            options1.Should().NotBe(options2);
            (options1 == options2).Should().BeFalse();
        }

        #endregion

        #region Edge Cases Tests

        [Test]
        public void NumberOfAttempts_ZeroValue_ShouldBeAllowed()
        {
            // Act
            _options.NumberOfAttempts = 0;

            // Assert
            _options.NumberOfAttempts.Should().Be(0);
        }

        [Test]
        public void NumberOfAttempts_MaxValue_ShouldBeAllowed()
        {
            // Act
            _options.NumberOfAttempts = byte.MaxValue;

            // Assert
            _options.NumberOfAttempts.Should().Be(byte.MaxValue);
        }

        [Test]
        public void DelayBetweenAttemps_ZeroValue_ShouldBeAllowed()
        {
            // Act
            _options.DelayBetweenAttemps = TimeSpan.Zero;

            // Assert
            _options.DelayBetweenAttemps.Should().Be(TimeSpan.Zero);
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
    }
}
