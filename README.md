# VRChat Outfit Tools

Unity editor tools for building Modular Avatar outfit-toggle menus, generating toggle icons, cleaning names with Apple Intelligence, and clustering generated toggles.

## Distribution

The intended release channels are:

- a VCC/VPM package ZIP (recommended); and
- a manual `.unitypackage` for projects that already have the VRChat SDK and Modular Avatar installed.

The public distribution source is [GitHub: `gryphprime/vrchat-outfit-tools`](https://github.com/gryphprime/vrchat-outfit-tools). Add this VCC listing URL in VCC:

<https://gryphprime.github.io/vrchat-outfit-tools/index.json>

GitHub Actions validates the package and publishes a versioned VPM ZIP with its SHA-256 file. To release, update the package version and immutable GitHub release-asset URL, merge to `main`, then push the matching `v<version>` tag. The release workflow verifies the tag, creates the GitHub release, adds the manifest to the VCC listing, commits that listing to `main`, and deploys it to GitHub Pages. Enable GitHub Pages with **GitHub Actions** as its source once in repository settings; GitHub does not permit the workflow's `GITHUB_TOKEN` to perform that one-time repository setting.

The `.unitypackage` channel remains manual and is not CI-built: it needs a macOS runner configured with Unity 2022.3+, VCC, the VRChat SDK, and Modular Avatar. [Forgejo](https://git.orpine.net/gryphprime/vrchat-outfit-tools) is only a private development mirror.

## Local install

Clone this repository, then copy `Packages/net.orpine.gryphprime.vrchat-outfit-tools` into the target Unity project's `Packages/` directory. Open the project in VCC so it can install the package requirements: VRChat Avatars 3.10.4+ and Modular Avatar 1.18.3+. Select outfit roots or a submenu with **MA Menu Item → Children**, then use **Tools → Avatar Outfit Toggles**.

## Apple Intelligence bridge

Apple Intelligence features require macOS 26+, Apple Silicon, and Apple Intelligence enabled. The included arm64 bundle is ad-hoc signed for local use. Rebuild it after editing the Swift source:

```zsh
zsh Packages/net.orpine.gryphprime.vrchat-outfit-tools/AppleIntelligence/build-macos.sh
```

Restart Unity after rebuilding the native bundle.
