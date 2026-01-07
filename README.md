# Layer4LoadBalancer

A small .NET solution that demonstrates a simple layer-4 TCP load balancer and supporting test fixtures.

## Projects

- `LoadBalancer` — a .NET Worker Service implementing an L4 load balancer. Key types:
  - `ClientListener` — a `BackgroundService` that listens for incoming TCP client connections and delegates them to the backend manager.
  - `BackendManager` — manages configured backend endpoints, selects a backend using an `IBackendSelector`, and forwards traffic between client and backend.
  - `DataTransfer` — handles asynchronous bidirectional data transfer between client and backend TCP connections.
  - `ITcpClientFactory` / `DefaultTcpClientFactory` — factory abstraction used when creating `TcpClient` instances (allows tests to inject test sockets).
  - Backend selectors live under the `LoadBalancer.BackendSelectors` namespace (e.g. `RoundRobinSelector`).

- `SimpleBackendService` — a tiny console TCP echo server used when running the load balancer manually to represent backend services.

- `LoadBalancerUnitTests` — NUnit test project with tests for selectors, the `ClientListener`, `BackendManager`, and `DataTransfer`. Uses `Moq` for mocking and injects test factories where needed.

## Configuration

Configuration is read via `IConfiguration` in `BackendManager` and `ClientListener`.
Important keys:

- `Backends` — comma-separated list of backend endpoints in the form `ip:port`, e.g. `127.0.0.1:5001,127.0.0.1:5002`.
- `ListenPort` — (optional) port the `ClientListener` should listen on (defaults to `9000`).

These values can be supplied via `appsettings.json`, user secrets, environment variables, or any `IConfiguration` source.

## How it works (high level)

1. `ClientListener` listens for incoming TCP connections.
2. When a client connects, `ClientListener` calls `IBackendManager.StartNewBackendConnection`.
3. `BackendManager` uses the configured `IBackendSelector` to pick a backend from the configured list.
4. `BackendManager` opens a connection to the chosen backend and calls `DataTransfer.DoDataTransferAsync` to relay data bidirectionally until either side closes the connection.

To make tests deterministic the code accepts an `ITcpClientFactory` so tests can provide a `TcpClient` already connected to a test backend or a fake implementation.

## Running

To build the solution:

```
dotnet build
```

To run the echo backend locally (example):

```
cd SimpleBackendService
dotnet run -- 5001
```

To run the load balancer (set `Backends` in `appsettings.json` or provide configuration):

```
cd LoadBalancer
dotnet run
```

To run tests:

```
dotnet test LoadBalancerUnitTests
```

## Tests

- Selector tests validate selection logic (e.g. `RoundRobinSelector`).
- `ClientListener` tests run the `ExecuteAsync` loop against a short-lived listener and verify `IBackendManager` is invoked (mocks provided via `Moq`).
- `BackendManager` tests validate parsing of backends, selector initialization, and startup behavior. Tests exercise the `ITcpClientFactory` injection so real socket connections can be avoided or controlled.
- `DataTransfer` tests validate bidirectional data relay, error handling, and connection closure scenarios using mock TCP clients and memory streams.

## Notes and potential improvements

- The `DataTransfer` class can be extended to support protocol-specific transformations or logging at the data level.
- The `ITcpClientFactory` abstraction was added to simplify unit testing. You may extend it to create `NetworkStream` wrappers or to inject fake streams for fully in-memory tests.
- Consider adding health checks and retry/backoff behavior for backends.

If you want, I can:
- Add a simple `docker-compose` sample to run multiple `SimpleBackendService` instances and the load balancer.
- Add support for protocol-level filtering or transformation.
