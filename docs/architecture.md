# Architecture

The builder reads and validates `installer.json`, rejects source paths outside its manifest folder and reparse points, then writes the application files, normalized manifest, license, and self-contained uninstaller to a standard ZIP. It copies a self-contained WPF setup host and appends that ZIP with a footer containing a format-versioned marker and the payload length.

Several source folders can be mapped to destinations (`source.folders`): the folders mapped to the installation root or to a relative subfolder are stored as one union tree under `app/` with the destination baked into the entry paths, while folders mapped to an absolute destination are stored under `external/<index>/`; the normalized manifest carries the folder list and destinations.

At launch, Setup revalidates the extracted manifest against the packaged content (defense in depth) and extracts the ZIP only after each entry has been resolved beneath a private temporary folder. For a new installation, or an update with the same application id and target directory, Setup prepares the complete `app/` tree, icon, uninstaller, and `.od.installer-installed.json` in a sibling staging directory. It checks the active installation for locked files before an update, then renames the active directory to a backup and activates the staging directory. Files absent from the new package disappear with the old directory rather than surviving as stale binaries.

Absolute destinations are updated file by file. Before each overwrite, Setup saves the original file in a private rollback area. The new installed manifest merges the previous and current external-file inventories, so an obsolete external file remains known to the uninstaller even though updates do not proactively delete it. Shortcuts are likewise backed up before they are replaced or removed. The HKCU uninstall key is snapshotted and written last. Any failure before completion restores the previous application directory, external files, shortcuts, and registry values.

Update prompts distinguish upgrades, reinstalls, downgrades, and repairs. A registered installation in another directory is not silently orphaned: the user must select its existing directory or uninstall it first. The application root is considered fully managed and must not contain user data; persistent data belongs under locations such as `%AppData%` or `%LocalAppData%`.

Uninstaller confirms the operation, removes listed shortcuts and external files, removes registry keys, deletes empty external directories, and schedules recursive deletion of the managed installation directory after its own process exits.

The principal trade-off is a full archive per installer rather than differential update logic. It keeps the package inspectable with standard ZIP tools and the implementation easy to debug.

## Payload footer and code signing

The payload footer is written immediately after the ZIP's end-of-central-directory record: an `ODINST02` marker, a format version (2), and the payload length. When opening an installer, the payload is located by scanning the last 128 KB of the file backwards for the marker, validating the format version and the ZIP end-of-central-directory signature right before the footer. Version 1 installers (marker `ODINST01`, no version field) are still readable.

Because the marker is not required to sit at the absolute end of the file, a code-signature certificate table appended at the end of a finished installer does not hide the payload: the finished EXE can be signed with `signtool` after the package is built, and Setup still locates and reads the payload. The trailing-data tolerance is bounded to 128 KB; keep signatures within that size.
