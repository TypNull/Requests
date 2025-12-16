using Requests.Options;

namespace Requests
{
    /// <summary>
    /// A class that simplifies implementing <see cref="Request{TOptions, TCompleted, TFailed}"/> functionality without creating a new child of <see cref="Request{TOptions, TCompleted, TFailed}"/>.
    /// </summary>
    public class OwnRequest : Request<RequestOptions, object, object>
    {
        private readonly Func<CancellationToken, Task<bool>> _own;

        /// <summary>
        /// Constructor to create an instance of <see cref="OwnRequest"/>.
        /// </summary>
        /// <param name="own">Function that contains the request logic.</param>
        /// <param name="requestOptions">Options to modify the behavior of <see cref="OwnRequest"/>.</param>
        public OwnRequest(Func<CancellationToken, Task<bool>> own, RequestOptions? requestOptions = null) : base(requestOptions)
        {
            _own = own;
            AutoStart();
        }

        /// <summary>
        /// Handles the execution of the <see cref="OwnRequest"/>.
        /// </summary>
        /// <returns>An item indicating whether the <see cref="OwnRequest"/> was successful.</returns>
        protected override async Task<RequestReturn> RunRequestAsync() => new() { Successful = await _own.Invoke(Token) };
    }
}
