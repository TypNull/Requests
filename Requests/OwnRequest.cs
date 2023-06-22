using Requests.Options;

namespace Requests
{
    /// <summary>
    /// A Class to easy implement a <see cref="Request{TOptions, TCompleated, TFailed}"/> functionality without creating a new <see cref="Request{TOptions, TCompleated, TFailed}"/> child.
    /// </summary>
    public class OwnRequest : Request<RequestOptions<VoidStruct, VoidStruct>, VoidStruct, VoidStruct>
    {
        private readonly Func<CancellationToken, Task<bool>> _own;

        /// <summary>
        /// Constructor to create a <see cref="OwnRequest"/>.
        /// </summary>
        /// <param name="own">Function that contains a request</param>
        /// <param name="requestOptions">Options to modify the <see cref="OwnRequest"/></param>
        public OwnRequest(Func<CancellationToken, Task<bool>> own, RequestOptions<VoidStruct, VoidStruct>? requestOptions = null) : base(requestOptions)
        {
            _own = own;
            AutoStart();
        }

        /// <summary>
        /// Handles the <see cref="OwnRequest"/>.
        /// </summary>
        /// <returns>A item that indicates if the <see cref="OwnRequest"/> was succesful.</returns>
        protected override async Task<RequestReturn> RunRequestAsync() => new() { Successful = await _own.Invoke(Token) };
    }
}
