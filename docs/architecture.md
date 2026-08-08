# Architecture

The builder reads and validates `installer.json`, rejects source paths outside its manifest folder and reparse points, then writes the application files, normalized manifest, license, and self-contained uninstaller to a standard ZIP. It copies a self-contained WPF setup host and appends that ZIP with a footer containing a format-versioned marker and the payload length.

Several source folders can be mapped to destinations (`source.folders`): the folders mapped to the installation root or to a relative subfolder are stored as one union tree under `app/` with the destination baked into the entry paths, while folders mapped to an absolute destination are stored under `external/<index>/`; the normalized manifest carries the folder list and destinations.

At launch, Setup revalidates the extracted manifest against the packaged content (defense in depth), extracts the ZIP only after each entry has been resolved beneath a private temporary folder, displays the license, copies the `app/` tree into the installation root and each `external/` bucket to its resolved absolute destination, creates shortcuts and a HKCU uninstall entry, and writes `.od.installer-installed.json`. The local manifest is the authoritative deletion list; files installed outside the root are recorded as absolute paths and removed by the uninstaller.

Uninstaller confirms the operation, removes listed shortcuts and files, removes registry keys, deletes only empty directories, and schedules deletion of its own executable after exit. Unknown files in the install folder are deliberately preserved.

The principal trade-off is a full archive per installer rather than differential update logic. It keeps the package inspectable with standard ZIP tools and the implementation easy to debug.

## Payload footer and code signing

The payload footer is written immediately after the ZIP's end-of-central-directory record: an `ODINST02` marker, a format version (2), and the payload length. When opening an installer, the payload is located by scanning the last 128 KB of the file backwards for the marker, validating the format version and the ZIP end-of-central-directory signature right before the footer. Version 1 installers (marker `ODINST01`, no version field) are still readable.

Because the marker is not required to sit at the absolute end of the file, a code-signature certificate table appended at the end of a finished installer does not hide the payload: the finished EXE can be signed with `signtool` after the package is built, and Setup still locates and reads the payload. The trailing-data tolerance is bounded to 128 KB; keep signatures within that size.
