# File Link Manager

[中文 README](README.md)

A portable Windows WPF utility that creates equivalent access paths for existing files or directories using native Windows file-link mechanisms, and records operations performed by the application.

Download the Windows x64 single-file executable and its SHA-256 checksum from the [GitHub Release](https://github.com/RovingAimer/file-link-manager/releases/tag/v1.1.0).

## Overview

File links do not copy source content. After the user provides an existing source path and a link path that does not yet exist, the application creates a link entry. The source remains in place, while the link path provides access to the same content.

## Core features

- Create file symbolic links, directory symbolic links, file hard links, and directory junctions.
- Enter, paste, or choose Windows paths with the native path pickers.
- Validate source and link paths in real time, including file/directory type, parent directory, volume, and network-path constraints.
- View link status for the current device, other devices, and offline devices.
- Inspect registered links and delete only link entries created by this application and passing identity checks.
- Keep deletion history; removing a deleted history record changes application data only and does not operate on disk paths again.
- Use portable JSON data and a backup file without a database service.
- Optionally name records when creating links (up to 20 Unicode text elements, supporting Chinese, punctuation, and special characters), and edit names or groups later. Names do not follow Windows filename restrictions and never rename disk paths or files.
- Choose a group directly on the create page. Add groups, or rename and delete them next to their names. Deleting a group keeps its records under **Ungrouped** without deleting disk links.
- Filter by group and search names, source paths, or link paths. Records within each group appear newest first by creation time; equal timestamps retain their original order. There is no manual sorting UI.

## Four native Windows link types

| Type | Behavior and storage impact | Main limitations |
| --- | --- | --- |
| Directory symbolic link | A cross-volume-equivalent path to a directory; adds only small filesystem metadata and does not copy content. | The source must be a directory. |
| Directory junction | Redirects a directory path to another local directory; adds only small filesystem metadata and does not copy content. | Directories only; local volumes are supported; mapped network volumes are not. |
| File symbolic link | A cross-volume-equivalent path to a file; adds only small filesystem metadata and does not copy content. | The source must be a file. |
| File hard link | Shares the same file content without copying data; adds only a small directory entry/metadata cost, and content space is released only after the last hard link is deleted. | Files only; source and link must be on the same volume. |

Symbolic links and junctions add only a small amount of filesystem metadata, so the source data occupies one copy. Hard links share the same file content; deleting one link does not release the content, and the content space is released only when the last hard link is deleted.

## UI and portable data

The application has three pages:

1. **Create Link**: choose a type, enter paths, see live validation, and review the creation preview; optionally enter a name and select or create a group.
2. **Link Records**: filter by device, status, and group; search names or paths, edit names/groups, inspect links, delete link entries, or remove deleted history records.
3. **About**: view application information, a current-device identity summary, and the data location.

The data directory is `data/` beside the executable. Its main files are `link-records.json` and `link-records.json.bak`. They contain device summaries, source and link paths, statuses, timestamps, custom names, and groups. Treat them as sensitive local application data; do not commit or publish them.

Version 1.1.0 reads schema 1 without rewriting it on load. The first normal save (including a save after inspection) writes schema 2. Version 1.0.0 opens schema 2 read-only to avoid losing the new metadata during a save. Before upgrading, manually back up the entire `data/` directory, keeping both the JSON primary and `.bak` files. Close the old version before starting the new one. To reuse existing records, place the new EXE beside the existing `data/` directory. The application does not move the data to another location.

## Requirements and permissions

- Windows 10 or Windows 11, x64.
- Building from source requires the .NET 10 SDK. Published builds are self-contained and do not require a separate .NET runtime on the target machine.
- The application manifest requests administrator elevation (`requireAdministrator`) to cover the permission boundary needed by Windows link creation and inspection.
- Symbolic-link availability also depends on local security policy, administrator approval, and Windows configuration. The application does not weaken security policy to bypass failures.

## Quick start

1. Start the application and choose a link type on **Create Link**.
2. Choose or enter an existing source file or directory.
3. Choose a link parent directory and enter a link path that does not exist yet.
4. Optionally enter a name and choose a group under record information. Blank names and ungrouped records are supported. Entering more than 20 text elements displays an inline error and disables creating the link or saving the edit without truncating the input.
5. Confirm that the card limitations and live validation pass, then select **Create Link**.
6. Inspect the result, edit names/groups, or manage groups on **Link Records**. Deletion targets the link entry only and never deletes the source content.

Test paths and permissions in a dedicated test directory before handling important data. Do not point source or link paths at uncertain system locations.

## Build from source

Run these commands from the repository root with the .NET 10 SDK:

```powershell
dotnet restore FileLinkManager.sln
dotnet test FileLinkManager.sln --no-restore
dotnet build FileLinkManager.sln --no-restore -c Release -p:Platform=x64 -warnaserror
```

The window test `CreatePage_PathStatusesRefreshInRealLayoutAndFollowSelectedType` can hang in the current validation environment. Protect full test runs with `--blame-hang-timeout 30s --blame-hang-dump-type none`. Run the non-window suite separately if needed; its result does not establish that the full window suite passed:

```powershell
dotnet test FileLinkManager.sln --no-restore --filter 'FullyQualifiedName!~FileLinkManager.App.Tests.Presentation.MainWindowTests'
```

Publish a Release, x64, win-x64, self-contained single-file build:

```powershell
dotnet publish src/FileLinkManager.App/FileLinkManager.App.csproj `
  --no-restore -c Release -p:Platform=x64 -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:DebugType=None -p:DebugSymbols=false -o publish
```

Use a new empty output directory for each publish. For a single-file acceptance package, verify that the directory contains only `FileLinkManager.App.exe`, with no PDB, DLL, runtimeconfig, deps, or runtime `data/` directory.

## Project structure

```text
src/FileLinkManager.Domain          Domain models and link status
src/FileLinkManager.Application     Use cases, record queries, and storage boundaries
src/FileLinkManager.Infrastructure  Windows APIs, paths, devices, and JSON storage
src/FileLinkManager.App             WPF UI, ViewModels, and composition root
tests/                              Domain, Application, Infrastructure, and App tests
docs/architecture/                  Product architecture and design notes
```

## Data and privacy

The application reads local Windows identity material, derives a stable device-key summary, and stores the device display name with link records. The raw MachineGuid is not written to JSON; however, paths, device display names, and timestamps may still reveal local information. Remove the `data/` directory and any paths from logs before sharing a bug report.

## Security notes

- Create, inspect, and delete links only for paths you own or are explicitly authorized to operate on.
- Hard links, symbolic links, and junctions have different semantics; confirm the type, volume, and target path before creating one.
- The delete flow validates link identity and deletes only the link entry; it does not recursively delete source directories or files.
- Do not run untrusted packages, and do not upload JSON data, crash dumps, or logs containing paths to public issues.

## License

This project is licensed under the [MIT License](LICENSE).

## Known limitations

- Windows only; Linux, macOS, and cross-device remote creation are not supported.
- Directory hard links, batch creation, cloud synchronization, and automatic import of links created by other tools are not supported.
- Junctions do not support mapped network volumes; file hard links must remain on one volume.
- Symbolic-link and other native filesystem behavior remains subject to Windows permissions, policy, volume type, and filesystem state.
- Names and groups organize application records only. They do not rename files or create additional disk directories; groups are single-level and global within the portable database.
- File and directory symbolic links are still deleted by path. A race remains if another process replaces the link entry between validation and deletion. Avoid concurrent changes to that path during deletion.
- Snipaste's F1 screenshot shortcut has been reported to stop working while File Link Manager has focus. Static inspection found no explicit F1 binding, interception, or registration in this app. The cause is unknown, and this release does not fix the issue.

## Contributing

Before submitting changes, run the full solution tests, the strict Release x64 build, and `git diff --check`. Do not commit `bin/`, `obj/`, `data/`, publish directories, logs, dumps, or private local work records.
