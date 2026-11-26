# MVP v0.1
Goal: portable Windows x64 app that can send two independent camera feeds to MediaMTX simultaneously.
- WPF GUI with server host, UDP port (8890), publish credentials, latency in milliseconds (default 500), camera discovery, enabled rows, unique path, resolution, FPS, capture format, bitrate, start/stop per row and all.
- First-run FFmpeg setup: discover local executable or download a pinned verified build into user-writable application data. Validate SRT and H.264 encoder availability. Provide manual executable selection.
- Discover DirectShow devices and supported modes; preserve alternative device identifiers for identical cameras. Handle enumeration stderr and expected nonzero exit codes.
- Initial encoder: libx264, H.264 yuv420p, MPEG-TS, 2-second GOP, bitrate controls. Audio optional on one selected stream; video-only is acceptable for initial hardware test.
- Build SRT caller URLs with streamid publish:path:user:pass, latency converted from ms to microseconds, pkt_size=1316. Validate path and credentials; reject unsupported delimiters rather than constructing ambiguous stream IDs.
- Save profiles locally, protect credentials with DPAPI. Generate read URLs using separately supplied reader credentials; never include publish secrets in reader exports.
- Show process state, FFmpeg progress and redacted errors; independent stop/restart, bounded retry with backoff, cancellation on manual stop. Process running is not proof of server reception.
- Russian and English UI; portable self-contained Windows x64 release.
Acceptance: Windows CI build and unit tests pass; manually verify two physical cameras concurrently for 60 minutes, independent stop/restart, reconnect after interruption, OBS reception and clean exit. Hardware test is not replaceable by CI.
Out of scope: live preview, recording, hardware encoders, scene export, frame synchronization.