[![NuGet](https://img.shields.io/nuget/vpre/Shard.Requests)](https://www.nuget.org/packages/Shard.Requests) [![Downloads](https://img.shields.io/nuget/dt/Shard.Requests)](https://www.nuget.org/packages/Shard.Requests) [![License](https://img.shields.io/github/license/TypNull/requests.svg)](https://github.com/TypNull/requests/blob/master/LICENSE) ![Maintainability](https://img.shields.io/badge/Maintainability%20Index-86%25-brightgreen)
# Requests
## 🌟 What Is Requests?

**Requests** is library for C# .NET 6; it's your trusty sidekick in the world of handling requests. Imagine a friendly companion that takes care of your requests, ensuring they're processed efficiently and systematically. Whether you're dealing with HTTP requests or tackling CPU-intensive tasks like directory searching.

## 🚀 Why Choose Requests?

- **Priority Magic**: Our priority channel ensures that high-priority requests get the VIP treatment—they're processed before the rest. No more waiting in line!
- **Flexibility at Its Best**: Requests is designed to be as flexible as possible. Customize it to fit your specific needs, whatever you're building.
- **Parallel Asynchronous Awesomeness**: Handle requests in parallel, like a symphony of asynchronous harmony. 🎶

## 📦 Installation

Getting started with **Requests** is a breeze:
1. Open your NuGet Package Manager.
2. Search for "Shard.Requests"
3. Install it. Voilà! 🎉

 - [Nuget](https://www.nuget.org/packages/Shard.Requests)
 - [GitHub](https://github.com/TypNull/Requests)

## Usage

To utilize the Requests library in C#, begin by importing it:

```csharp
using Shard.Requests;
```

Next, instantiate a `Request` object, and it will automatically be included in the `RequestHandler`. If a request encounters an error, the `RequestHandler` will automatically retry the request based on the specified retry settings.

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
>
> Because handling requests should be as delightful as a warm cup of cocoa on a winter day.

 For additional information, refer to the Requests [Wiki](https://github.com/TypNull/Requests/wiki/).

## Examples

Meet our star, the `OwnRequest` class:

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

OwnRequest is a straightforward implementation of a child class of Request. It doesn’t overwhelm you with complexity, but it’s incredibly useful for quick implementations:

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

Create your own requests with a sprinkle of magic! ✨

## 🌟 Contributing

Join our quest! If you'd like to contribute to this library, submit a pull request or open an issue. We appreciate your help in making `Requests` the best it can be!

## 📜 License

**Requests** is licensed under the MIT license. 

## **Free Code** and **Free to Use**
#### Have fun!