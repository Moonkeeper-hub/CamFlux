# Architecture
src/CamFlux.App: WPF MVVM UI and composition root.
src/CamFlux.Core: models, validation, profiles, URL construction, process supervision contracts.
src/CamFlux.Infrastructure: FFmpeg installation, DirectShow enumeration, process runner, DPAPI storage.
tests/CamFlux.Tests: unit tests with fake process runner and device fixtures.
One FFmpeg process per camera; asynchronous stdout progress and stderr drain. Explicit states Idle, Starting, Running, Retrying, Stopping, Failed. Use cancellation and bounded backoff; stop must suppress retry. Avoid concurrent ownership of a camera. Use atomic profile writes. Separate capture device identity from display name and stream path. No preview process competing for camera ownership.
Network: camera -> FFmpeg -> SRT caller -> MediaMTX listener -> OBS caller. Independent streams are not frame synchronized. Aggregate bitrate estimate is not measured bandwidth.