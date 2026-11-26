# CamFlux

CamFlux v0.1.0 is a Windows x64 control panel for sending independent DirectShow camera feeds to MediaMTX over SRT. OBS receives each feed as a separate Media Source.

Download the `CamFlux-win-x64` artifact from Windows CI, extract the inner ZIP and run `CamFlux.App.exe`. The ZIP includes the .NET desktop runtime. CamFlux discovers an existing FFmpeg or installs a verified build on first run. No administrator access or system PATH changes are needed.

Enter the server and publishing credentials, scan devices, and use **Find modes** for each camera. Choose a mode, a video bitrate and a unique stream path. Enable the cameras you want to send and select **Start enabled**. Optionally choose a microphone on one stream. Enter the separate OBS reader credentials and use **Copy OBS URL** on each row. Save your settings under a named local profile. Switch languages from the window header.

See the [MVP](docs/MVP.md), [architecture](docs/ARCHITECTURE.md), [roadmap](docs/ROADMAP.md), [MediaMTX setup](docs/MEDIAMTX.md) and [testing instructions](docs/TESTING.md).
