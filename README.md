[![NuGet](https://img.shields.io/nuget/vpre/Shard.Requests)](https://www.nuget.org/packages/Shard.Requests) [![Downloads](https://img.shields.io/nuget/dt/Shard.Requests)](https://www.nuget.org/packages/Shard.Requests) [![License](https://img.shields.io/github/license/TypNull/requests.svg)](https://github.com/TypNull/requests/blob/master/LICENSE) ![Maintainability](https://img.shields.io/badge/Maintainability%20Index-86%25-brightgreen)
# Requests

Requests is a software library for C# .NET 6 that enables handling of requests in a parallel asynchronous state as Request objects.
The library utilizes a priority channel to efficiently and systematically handle requests. 
The priority channel ensures that high-priority requests are processed before low-priority requests. 
This library is versatile and can be used for HTTP requests or other CPU-intensive tasks such as directory searching. 

It has been specifically designed to be flexible and customizable, allowing developers to tailor the library to their specific needs. 
Requests is an efficient and organized solution that simplifies the process of handling requests in C# .NET 6-based applications.

## Installation

You can install Requests by searching for "Requests" in the NuGet Package Manager.
[Nuget](https://www.nuget.org/packages/Shard.Requests)
[GitHub](https://github.com/TypNull/Requests)

## Usage

To use Requests, first import the library:

```cs
using Shard.Requests;
```

Then, create a Request object and it will automaticly be added to the `RequestHandler`
If a request fails, the RequestHandler will automatically retry the request according to the retry settings specified.

## Classes

This library includes the following classes:

- **Request:** Main abstract class that can be used to expand functionality on a class-based level.
    - All subclasses have a retry function ♾️
    - A priority function 🔝
    - Delegates to notify when a `Request` started, failed, completed, or canceled 📢
    - Implementation for custom `CancellationToken` and a main `CancellationTokenSource` to cancel the request.
- **OwnRequest:** Wrapper around your own request. It is an easy-to-expand class for handling the requests without the creation of a specific class.
- **RequestContainer:** A container class to merge requests together and to start, pause, and await them.
- **ProgressableContainer:** A container class to merge requests together that are using a `Progress` object to report the progress.
- **RequestHandler:** A class to handle requests. Every handler is independent of any other handler.

> Expand and use as you like!

## Examples

Here is an example of creating a child class of `Request`, called `OwnRequest`:

```cs
public class OwnRequest : Request<RequestOptions<VoidStruct, VoidStruct>, VoidStruct, VoidStruct>
{
    private readonly Func<CancellationToken, Task<bool>> _own;

    public OwnRequest(Func<CancellationToken, Task<bool>> own, RequestOptions<VoidStruct, VoidStruct>? requestOptions = null) : base(requestOptions)
    {
        _own = own;
        AutoStart();
    }
        
    protected override async Task<RequestReturn> RunRequestAsync() 
    { 
        return new RequestReturn() { Successful = await _own.Invoke(Token) };
    }
}
```
To create an `OwnRequest`, use the following code:

```cs
// Create an object and pass as a parameter an action that uses a CancellationToken
new OwnRequest(async (token) =>
{
    using HttpClient client = new();
    // Create your request message. Here the body of google.com
    HttpRequestMessage requestMessage = new(HttpMethod.Get, "https://www.google.com");
    // Send your request and get the result. Pass the CancellationToken for handling it later over the Request object
    HttpResponseMessage response = await client.SendAsync(requestMessage, token);
    // If the response does not succeed
    if (!response.IsSuccessStatusCode)
        return false; // Return false to retry and call the failed method
                      // If the response succeeds. Do what you want and return to finish the request
    Console.WriteLine("Finished");
    return true;
});
```

## Contributing

If you would like to contribute to this library, please submit a pull request or open an issue. We welcome all contributions and appreciate your help in making Requests the best library it can be!

## License

Requests is licensed under the MIT license.