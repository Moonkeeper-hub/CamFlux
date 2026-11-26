# Build and test CamFlux v0.1.0

On Windows x64 with the .NET 10 SDK, run the same build and test commands as Windows CI.

```powershell
dotnet build CamFlux.slnx -c Release
dotnet test CamFlux.slnx -c Release --no-build
dotnet publish src/CamFlux.App -c Release -r win-x64 --self-contained true -o artifacts/CamFlux-win-x64
```

Keep the entire published directory together. It can be copied to a Windows x64 system without a separately installed .NET runtime. CI publishes it as a ZIP. Local profiles and managed FFmpeg live under `%LOCALAPPDATA%\CamFlux`. Profile credentials use DPAPI CurrentUser and can be restored by the original Windows user. Reader credentials are entered for the current session and copied only in the OBS reader URL. The clipboard contains the reader credentials when authentication is used.

## FFmpeg provenance

Automatic setup pins the [FFmpeg-Builds Windows x64 GPL archive](https://github.com/FFmpeg-Builds/FFmpeg-Builds/releases/tag/autobuild-2026-11-26-12-56) `ffmpeg-n8.1.3-41-ga8029b6528-win64-gpl-8.1.zip`, built from FFmpeg revision `a8029b6528`. CamFlux verifies the complete archive against the release asset SHA-256 `dbbd7275987024a34bfad96b3c3cc148a47bea604ff7100705d8f14ab1cabfb8` before extracting and running FFmpeg. The build includes GPL components such as libx264. See the release for upstream licensing and build sources and the [FFmpeg license information](https://ffmpeg.org/legal.html). FFmpeg is downloaded separately from the CamFlux ZIP. Existing or manually selected executables undergo capability validation.

## Manual Windows acceptance

Record the Windows version, camera models and connection topology, capture modes, bitrates, FFmpeg build, MediaMTX version, OBS version, and outcomes.

1. On a clean Windows x64 user account, open the portable build with FFmpeg absent. Confirm automatic setup, camera discovery and subsequent reuse of the installed executable. Repeat with a manually chosen compatible executable and verify that CamFlux rejects an incompatible one.
2. Connect two physical cameras, ideally of the same model, and a microphone. Verify distinct camera identities and usable driver-reported raw and compressed modes where supported. Select a mode per camera, a different path per stream, and optional audio on one stream.
3. Configure MediaMTX internal authentication and UDP reachability as described in [MediaMTX setup](MEDIAMTX.md). Start both rows and add two OBS Media Sources using the copied read URLs. Verify that both feeds show the correct camera, and confirm audio on only the selected source. A locally advancing frame counter does not verify reception.
4. Keep both feeds running for 60 minutes. Observe OBS continuity, frame progress, CPU, memory, and actual network throughput. Compare network throughput with the UI estimate, which includes a 30% allowance on enabled video and configured audio bitrates.
5. Stop and restart each row independently while the other continues receiving. Interrupt the network or MediaMTX and restore it during retries. Verify recovery in OBS. Repeat with an extended interruption and verify the five retry delays (1, 2, 4, 8 and 16 seconds), the final failed state and manual restart. Stop a row during backoff and verify no further launches for it.
6. Change the language, save a profile, and restart the app. Load the profile and confirm the endpoint, rows, capture settings, language and publishing credentials. Inspect the saved JSON and diagnostics for plaintext credentials. Confirm reader links use the supplied read account.
7. Close the app during streaming and during retry. Confirm in Task Manager that its FFmpeg processes exit and verify that both cameras can be opened by another app. Also close the window during first-run setup and confirm that startup work cancels.

Windows CI exercises the build and unit tests and creates the portable package. Physical cameras, DirectShow driver timing, live MediaMTX and OBS reception, and the 60-minute run require manual verification.
