# Public Suffix List snapshots

The `psl-121.dat.gz` and `psl-151.dat.gz` resources contain unchanged UTF-8 Public
Suffix List source data from the matching Chromium releases. The original
license, attribution and Chromium annotations remain inside each compressed
file. Decompress them with any gzip utility to inspect or modify the source.

The data is licensed under the Mozilla Public License 2.0:
https://mozilla.org/MPL/2.0/

`public-suffix-snapshots.json` records fixed source URLs, Git blob identities and
SHA-256 digests. The data includes ICANN and private suffix rules, wildcard rules
and exceptions. It is used only for the partition backend's schemeful-site
normalization; Chromium 84's basic Cookie path does not load it.

MonkeySharp code remains under its repository MIT license.
