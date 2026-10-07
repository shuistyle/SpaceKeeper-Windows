# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

## What is signed

Only `SpaceKeeper.exe`, `SpaceKeeper.dll` and `SpaceKeeper.Core.dll`, built from the source code in this repository by its public GitHub Actions workflow ([`.github/workflows/build.yml`](.github/workflows/build.yml)). Third-party libraries included in the download (such as the Windows App SDK) are not re-signed by this project.

Every release is signed only after a person on the team below manually approves the signing request.

## Team roles

| Role | Members |
|---|---|
| Committers and reviewers | [Andrew Flowerdew (@shuistyle)](https://github.com/shuistyle) — see [repository collaborators](https://github.com/shuistyle/SpaceKeeper-Windows/graphs/contributors) |
| Approvers | [Andrew Flowerdew (@shuistyle)](https://github.com/shuistyle) |

All team members use multi-factor authentication for GitHub and SignPath.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

(SpaceKeeper has no network code. Its settings stay in one file on your PC: `%LOCALAPPDATA%\SpaceKeeper\state.json`.)
