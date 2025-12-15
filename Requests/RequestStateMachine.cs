using Requests.Options;

namespace Requests
{

    /// <summary>
    /// Thread-safe state machine for request state management.
    /// Enforces valid state transitions and prevents race conditions.
    /// </summary>
    public sealed class RequestStateMachine
    {
        private int _stateInt;
        private readonly Action<RequestState, RequestState> _onStateChanged;

        /// <summary>
        /// Initializes a new thread-safe state machine for managing request states.
        /// </summary>
        /// <param name="initialState">Initial state of the request.</param>
        /// <param name="onStateChanged">Callback invoked on state changes, with (oldState, newState).</param>
        public RequestStateMachine(RequestState initialState, Action<RequestState, RequestState> onStateChanged)
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
        /// Validates state transitions.
        /// </summary>
        private static bool IsValidTransition(RequestState from, RequestState to)
        {
            // Terminal states cannot transition
            if (from is RequestState.Completed or RequestState.Failed or RequestState.Cancelled)
                return false;

            return (from, to) switch
            {
                // From Paused
                (RequestState.Paused, RequestState.Idle or RequestState.Waiting or RequestState.Cancelled) => true,

                // From Idle
                (RequestState.Idle, RequestState.Running or RequestState.Cancelled) => true,

                // From Waiting
                (RequestState.Waiting, RequestState.Idle or RequestState.Cancelled) => true,

                // From Running can transition to any state except Running itself
                (RequestState.Running, RequestState.Running) => false,
                (RequestState.Running, _) => true,

                // Invalid transitions
                _ => false
            };
        }
    }
}