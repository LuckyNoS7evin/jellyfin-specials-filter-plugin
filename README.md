# Jellyfin Specials Filter Plugin

A Jellyfin plugin that automatically removes **Specials (Season 0)** entries from your TV show libraries after each library scan — configurable per library and per individual show.

## Why?

Metadata providers (TVDB, TMDB) often include large numbers of specials for shows like *Game of Thrones* that you may have no interest in. At the same time, some shows — especially Anime — have specials that are integral to the story. This plugin lets you draw that line yourself.

## How it works

After every library scan completes, the plugin checks each configured library and show, then removes Season 0 items from Jellyfin's database for anything you've flagged. **No files are ever deleted from disk.** Because metadata is re-fetched on every scan, the plugin runs after every scan to keep specials removed.

## Configuration

Navigate to **Dashboard → Plugins → Specials Filter**.

### Libraries

Toggle **Remove Specials** for any TV library. All shows inside that library will have their specials removed after scanning unless overridden at the show level.

### Per-Show Overrides

Select a library from the dropdown to see its shows. Each show can be set to one of three states:

| Setting | Behaviour |
|---|---|
| **Default** | Inherits the library setting |
| **Remove Specials** | Always removes specials for this show |
| **Keep Specials** | Always keeps specials for this show, even if the library says to remove them |

This lets you, for example, set an Anime library to **Remove Specials** by default and then override individual shows like *Fullmetal Alchemist: Brotherhood* to **Keep Specials**.

## Installation

1. Build the project:
   ```
   dotnet publish -c Release
   ```
2. Copy `Jellyfin.Plugin.SpecialsFilter.dll` to your Jellyfin plugins folder (e.g. `~/.config/jellyfin/plugins/SpecialsFilter_1.0.0.0/`).
3. Restart Jellyfin.

## Requirements

- Jellyfin 10.11.6 or later
- .NET 9
