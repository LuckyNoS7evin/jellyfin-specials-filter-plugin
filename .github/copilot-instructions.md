# Copilot Instructions

## Build & Publish

```sh
# Build
dotnet build Jellyfin.Plugin.SpecialsFilter/Jellyfin.Plugin.SpecialsFilter.csproj -c Release

# Publish (produces the deployable DLL)
dotnet publish -c Release
```

There are no automated tests in this repository. The publish CI workflow (`.github/workflows/publish.yml`) runs on push to `main` and deploys to `gh-pages` as a Jellyfin plugin manifest.

## Architecture

This is a single-project Jellyfin plugin targeting .NET 9 (`net9.0`). Jellyfin plugin references (`Jellyfin.Controller`, `Jellyfin.Model`) are marked `ExcludeAssets=runtime` — they are compile-time only and must never be bundled in the output DLL.

### Removal trigger

Specials removal is driven by a single entry point:

**`SpecialsRemovalTask`** (`ILibraryPostScanTask`) — runs after every library scan via Jellyfin's post-scan hook, delegating to `SpecialsRemovalService` for all removal logic. Jellyfin has only one scan path (`ValidateMediaLibrary` → `PerformLibraryValidation`) so this covers all scan scenarios.

### Configuration model (`PluginConfiguration`)

Three orthogonal settings layers, applied in priority order (highest first):

| Layer | Type | Stored in |
|---|---|---|
| Per-episode blacklist | `string[]` of episode IDs | `EpisodeBlacklist` |
| Per-show override | `ShowSetting[]` (Remove / Keep / Default) | `ShowSettings` |
| Per-library toggle | `LibrarySetting[]` (bool) | `LibrarySettings` |

`ShowSetting` entries with `Handling == Default` are stripped on save (`SaveConfig` in the controller) — they are never persisted.

### API layer

`SpecialsFilterController` (route prefix `SpecialsFilter`, requires `RequiresElevation`) serves the embedded HTML config page at `Pages/configurationpage.html`. The page fetches data from these endpoints:

- `GET /SpecialsFilter/Libraries` — TV libraries with current toggle state
- `GET /SpecialsFilter/Libraries/{libraryId}/Shows` — shows in a library with handling override
- `GET /SpecialsFilter/Shows/{showId}/Specials` — Season 0 episodes with blacklist status
- `GET /SpecialsFilter/Config` — full config blob
- `POST /SpecialsFilter/Config` — save config

### DI registration

`PluginServiceRegistrator` (`IPluginServiceRegistrator`) registers:
- `SpecialsRemovalService` as singleton
- `SpecialsRemovalTask` as `ILibraryPostScanTask` singleton

### Key invariant

`DeleteItem` is always called with `DeleteFileLocation = false`. Removal only affects Jellyfin's database; no media files are ever touched. After a re-scan, removed items reappear and the plugin removes them again.

## Versioning & Release

Version is set in `Jellyfin.Plugin.SpecialsFilter.csproj` (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`). The CI workflow reads this value to name the release zip (`specials-filter_<VERSION>.zip`) and update `manifest.json` on `gh-pages`. Bump all three version properties together when releasing.

Plugin GUID is `c7f2d3a1-4b8e-4f9d-a2c5-1e3f7b9d0e2a` — do not change it.
