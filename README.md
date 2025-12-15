[![NuGet](https://img.shields.io/nuget/vpre/Shard.Requests)](https://www.nuget.org/packages/Shard.Requests) [![Downloads](https://img.shields.io/nuget/dt/Shard.Requests)](https://www.nuget.org/packages/Shard.Requests) [![License](https://img.shields.io/github/license/TypNull/requests.svg)](https://github.com/TypNull/requests/blob/master/LICENSE) ![Maintainability](https://img.shields.io/badge/Maintainability%20Index-86%25-brightgreen)

# Requests

Performance async task management with priority queues, automatic retries, and pause/resume capabilities for .NET

## Why Requests?

**Requests** transforms chaotic async workflows into manageable,
priority-driven operations. Built for developers who need more than
basic task scheduling—think game engines, batch processors, API
orchestrators, and complex data pipelines.

### Key Features

**Priority Scheduling**: High-priority requests jump the queue automatically
**Smart Retries**: Configurable retry logic with exponential backoff
**Pause & Resume**: Stop and restart long-running operations without data loss
**Progress Tracking**: Real-time aggregated progress across multiple operations
**Dynamic Parallelism**: Auto-adjusts concurrency based on system load
**Zero Lock-in**: Simple wrapper pattern around your existing async code

## Quick Start

```bash
dotnet add package Shard.Requests
```

```csharp
using Shard.Requests;

// Wrap any async operation
var request = new OwnRequest(async token =>
{
    var response = await httpClient.GetAsync(url, token);
    return response.IsSuccessStatusCode;
}, new() {
    Priority = RequestPriority.High,
    NumberOfAttempts = 3,
    DelayBetweenAttempts = TimeSpan.FromSeconds(2)
});

// Request auto-starts with built-in retry and priority handling
await request.Task;
```

## Architecture at a Glance

- **Request**: Base class for all operations with state machine and lifecycle hooks
- **RequestHandler**: Parallel or sequential execution engines with priority channels
- **RequestContainer**: Group multiple requests with unified control
- **ProgressableContainer**: Track aggregated progress across request batches
- **OwnRequest**: Zero-boilerplate wrapper for ad-hoc async operations

## Advanced Capabilities

- **Cooperative Cancellation**:`await Request.Yield()` for graceful interruption
- **Subsequent Requests**: Chain operations without re-queuing
- **Quaternary Heap**: O(log n) priority queue with FIFO ordering within priority levels

## Real-World Use Cases

- **Batch Processing**: Download 1000 files with 10 parallel workers and retry failed transfers
- **Game Development**: Priority-ordered asset loading with pause when backgrounded
- **API Rate Limiting**: Throttle concurrent requests with dynamic parallelism control
- **Long Operations**: Multi-hour processes with save/resume support

## Documentation

**[Wiki](https://github.com/TypNull/Requests/wiki/)** Architecture deep-dives, examples, and API reference

## **Free Code** and **Free to Use**
#### Have fun!

*Built for developers who need industrial-strength async control without the complexity*
