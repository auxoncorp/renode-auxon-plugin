# renode-auxon-plugin

Renode Logging backend for Auxon Modality. Currently only expected to work on Linux.

## Installation
- Build with `dotnet build`
- Install by copying RenodeAuxonPlugin.dll into the renode bin directory.
- Configuration:
  - Set the `MODALITY_INGEST_URL` and `MODALITY_AUTH_TOKEN` environment variables before using the plugin. You can optionally set `MODALITY_RUN_ID` as well (if not set, a uuid will be generated).
  - Enable the plugin in renode with `plugins EnablePlugin "Modality Logger"`. You only need to do this once; it will remain enabled in subsequent renode sessions.
