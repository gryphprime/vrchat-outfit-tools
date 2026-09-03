# AvatarPoseLibrary distribution research

## What AvatarPoseLibrary publishes

AvatarPoseLibrary (APL) recommends VCC: users add `https://HhotateA.github.io/AvatarPoseLibrary/index.json` and then install from **Manage Project**. Its documented fallback is downloading and importing a `.unitypackage`; that route requires the VRChat, NDMF, and Modular Avatar dependencies first. [APL README](https://github.com/HhotateA/AvatarPoseLibrary/blob/main/README.md)

| Channel | Published artifact | Evidence |
|---|---|---|
| VCC/VPM | Versioned package ZIP | The live listing maps `com.hhotatea.avatar-pose-library` version `1.2.43` to a GitHub Release ZIP and includes its `zipSHA256`. [Live `index.json`](https://HhotateA.github.io/AvatarPoseLibrary/index.json) |
| Manual Unity import | Versioned `.unitypackage` | The same release publishes `com.hhotatea.avatar-pose-library-1.2.43.unitypackage`. [Release 1.2.43](https://github.com/HhotateA/AvatarPoseLibrary/releases/tag/1.2.43) |
| Metadata | `package.json` | The release also attaches `package.json`; the source manifest supplies the package ID, version, Unity 2022.3 target, and VPM dependencies. [Manifest](https://github.com/HhotateA/AvatarPoseLibrary/blob/main/Packages/com.hhotatea.avatar-pose-library/package.json) |

### VCC listing mechanics

APL's listing declares the repository name, ID (`com.hhotatea.avatar-pose-library.listing`), URL, package ID, and a `versions` map. Each version is the package manifest plus the release ZIP URL and checksum. This matches VCC's required listing shape: `name`, `author`, `id`, `url`, and `packages → package ID → versions → full VPM manifest`. [VCC repository format](https://vcc.docs.vrchat.com/vpm/repos/)

Its GitHub workflow builds the ZIP from `Packages/${PACKAGE_NAME}`, excludes tests, creates the `.unitypackage` from package `.meta` files, tags the manifest version, and uploads ZIP, `.unitypackage`, and `package.json`. A separate workflow invokes `vrchat-community/package-list-action` and deploys the generated `Website/index.json` to GitHub Pages. Both use the repository variable `PACKAGE_NAME`. [Release workflow](https://github.com/HhotateA/AvatarPoseLibrary/blob/main/.github/workflows/release.yml) · [Listing workflow](https://github.com/HhotateA/AvatarPoseLibrary/blob/main/.github/workflows/build-listing.yml)

The source `package.json` does **not** contain release `url` or `zipSHA256`; the generated listing adds them. That is appropriate for `zipSHA256`, which VCC says belongs in the listing rather than the package file. [VCC package format](https://vcc.docs.vrchat.com/vpm/packages/#vpm-manifest-additions)

### Unity installation choices

1. **Supported/recommended:** VCC consumes the VPM ZIP and resolves `vpmDependencies`.
2. **Supported fallback:** import the release `.unitypackage`; dependencies must already be installed, as APL's README says. Unity defines `.unitypackage` as an asset-package archive intended for importing assets/tools. [Unity asset packages](https://docs.unity3d.com/2022.3/Documentation/Manual/AssetPackagesCreate.html)
3. **Technically possible, not advertised by APL:** Unity's Package Manager accepts a Git URL with a subfolder, e.g. `https://github.com/HhotateA/AvatarPoseLibrary.git?path=/Packages/com.hhotatea.avatar-pose-library`. [Unity Git URL packages](https://docs.unity3d.com/2022.3/Documentation/Manual/upm-ui-giturl.html) This is not a replacement for VCC: APL's manifest has VPM-specific dependencies, not ordinary UPM `dependencies`, so a stock Unity install will not resolve the VRChat/Modular Avatar requirements.

## Smallest reusable plan for `vrchat-outfit-tools`

The checkout is currently an `Assets/OutfitToggleGenerator` asset folder, not a package. Convert it with VCC's Package Maker (or perform the same layout): move editor code to `Packages/<package-id>/Editor`, all remaining source/assets/native bundle to `Runtime`, add a VPM manifest, and add assembly definitions. Those are VCC's stated conversion requirements. [VCC conversion guide](https://vcc.docs.vrchat.com/guides/convert-unitypackage/)

| Add/configure | Exact requirement for this repository |
|---|---|
| `Packages/<unique reverse-DNS package-id>/package.json` | Set a unique ID, display name, SemVer version, tested Unity version, author name/email, description, SPDX license, and `vpmDependencies` for **`com.vrchat.avatars`** and **`nadena.dev.modular-avatar`**. The source directly imports those SDKs; it does not directly import NDMF, which Modular Avatar declares as its own VPM dependency. [Modular Avatar manifest](https://github.com/bdunderscore/modular-avatar/blob/main/package.json) Do not copy APL's version floors without testing. |
| `Runtime/<package-id>.Runtime.asmdef` | Needed for `OutfitToggleGeneratedMenu.cs`; it needs no external SDK reference. |
| `Editor/<package-id>.Editor.asmdef` | Editor-only; reference the runtime assembly plus `VRC.SDK3A`, `VRC.SDKBase`, and `nadena.dev.modular-avatar.core`. |
| Migrated source/assets | Preserve every existing `.meta`, especially `Plugins/macOS/OutfitToggleAppleIntelligence.bundle.meta`, so GUIDs and plugin import settings survive. Include the compiled macOS bundle. |
| `README.md` and `LICENSE.md` in the package | Recommended for a shareable Unity/VPM package; VCC specifically recommends an SPDX license declaration. [VCC package format](https://vcc.docs.vrchat.com/vpm/packages/#vpm-manifest-additions) |
| Legacy migration | Add `legacyFolders` for `Assets/OutfitToggleGenerator` before the first VPM release so old manual installs are removed instead of compiling duplicate scripts. The folder's root `.meta` is absent from this checkout, so first let Unity generate and commit it if a GUID-safe migration is wanted. [VCC legacy migration fields](https://vcc.docs.vrchat.com/vpm/packages/#vpm-manifest-additions) |

Publish these per-version outputs:

- `<package-id>-<version>.zip`: package root contents, including `package.json` and `.meta` files, excluding tests; this is the VCC artifact.
- `<package-id>-<version>.unitypackage`: generated from the package's `.meta` file list; this is the manual-import artifact.
- A public static `index.json`: retain all old versions and embed the full manifest, immutable ZIP URL, and SHA-256 for each one. VCC warns against removing old published versions. [VCC listing guide](https://vcc.docs.vrchat.com/guides/create-listing/#4-run-the-build-repo-listing-action)

## Can Forgejo publish both?

**Yes.** A single Forgejo pipeline can build the ZIP and `.unitypackage`, create/tag a Forgejo release, upload both assets, generate/update `index.json`, and deploy that file to any public static URL. VCC explicitly supports self-hosted services when the listing is publicly accessible, and Forgejo's API supports creating releases and release assets. [VCC self-hosting](https://vcc.docs.vrchat.com/guides/create-listing/#using-your-own-services) · [Forgejo releases/API](https://forgejo.org/docs/latest/user/repository/releases/#creating-a-release-through-the-api)

Do **not** copy APL's workflows unchanged: they depend on GitHub Actions, GitHub Pages, and a listing action that discovers GitHub releases. Reuse the package layout and artifact contract instead. Configure the Forgejo job with `PACKAGE_ID`, `LISTING_URL`, a public release-asset base URL, release/API credentials, and static-host deployment credentials; never overwrite or delete a version already referenced by `index.json`.
