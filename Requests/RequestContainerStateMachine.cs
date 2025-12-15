using Requests.Options;

namespace Requests
{
    /// <summary>
    /// Thread-safe state machine for request container state management.
    /// Enforces valid state transitions specific to containers.
    /// </summary>
    public sealed class RequestContainerStateMachine
    {
        private int _stateInt;
        private readonly Action<RequestState, RequestState> _onStateChanged;

        /// <summary>
        /// Initializes a new thread-safe state machine for request containers.
        /// </summary>
        /// <param name="initialState">Initial state of the container.</param>
        /// <param name="onStateChanged">Callback for state changes, receiving (oldState, newState).</param>
        public RequestContainerStateMachine(RequestState initialState, Action<RequestState, RequestState> onStateChanged)
        {
            _stateInt = (int)initialState;
            _onStateChanged = onStateChanged;
        }

        /// <summary>
        /// Gets the current state.
        /// </summary>
        public RequestState Current => (RequestState)Volatile.Read(ref _stateInt);

        /// <summary>
        /// Attempts to transition to a new state atomically.
        /// Returns true if successful, false if transition is invalid or already in terminal state.
        /// </summary>
        public bool TryTransition(RequestState to)
        {
            while (true)
            {
                int currentInt = Volatile.Read(ref _stateInt);
                RequestState from = (RequestState)currentInt;

                // Validate transition
                if (!IsValidTransition(from, to))
                    return false;

                // Attempt atomic swap
                int oldInt = Interlocked.CompareExchange(ref _stateInt, (int)to, currentInt);

                if (oldInt == currentInt)
                {
                    // Success, notify about state change
                    _onStateChanged(from, to);
                    return true;
                }
                // CAS failed, retry
            }
        }

        /// <summary>
        /// Validates state transitions for request containers (handlers).
        /// Containers have different transition rules than individual requests.
        /// </summary>
        private static bool IsValidTransition(RequestState from, RequestState to) => (from, to) switch
        {
            // From Idle: can start running, pause, or be cancelled
            (RequestState.Idle, RequestState.Running or RequestState.Paused or RequestState.Cancelled) => true,

            // From Running
            (RequestState.Running, RequestState.Idle or RequestState.Paused or RequestState.Cancelled) => true,

            // From Paused
            (RequestState.Paused, RequestState.Idle or RequestState.Cancelled) => true,

            // From Cancelled
            (RequestState.Cancelled, RequestState.Idle) => true,

            // From Waiting
            (RequestState.Waiting, RequestState.Idle or RequestState.Cancelled) => true,

            // From Completed/Failed
            (RequestState.Completed or RequestState.Failed, RequestState.Idle) => true,

            // Invalid transitions
            _ => false
        };
    }
}