# Architecture

The builder reads and validates `installer.json`, rejects source paths outside its manifest folder and reparse points, then writes the application files, normalized manifest, license, and self-contained uninstaller to a standard ZIP. It copies a self-contained WPF setup host and appends that ZIP with a fixed footer containing a marker and length.

At launch, Setup reads the footer, extracts the ZIP only after each entry has been resolved beneath a private temporary folder, displays the license, copies application files, creates shortcuts and a HKCU uninstall entry, and writes `.od.installer-installed.json`. The local manifest is the authoritative deletion list.

Uninstaller confirms the operation, removes listed shortcuts and files, removes registry keys, deletes only empty directories, and schedules deletion of its own executable after exit. Unknown files in the install folder are deliberately preserved.

The principal trade-off is a full archive per installer rather than differential update logic. It keeps the package inspectable with standard ZIP tools and the implementation easy to debug.
