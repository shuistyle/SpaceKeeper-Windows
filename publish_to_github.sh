#!/usr/bin/env bash
# Publishes SpaceKeeper for Windows to a new PUBLIC GitHub repository.
# Run this on your Mac (no Windows needed): GitHub then builds the Windows app.
#
#   bash publish_to_github.sh                      -> repository "SpaceKeeper-Windows"
#   bash publish_to_github.sh MyRepoName           -> choose another name
#   bash publish_to_github.sh SpaceKeeper-Windows v1.0.0
#                                                  -> also make a Release with download links
#
# What it does:
#   1. Installs the GitHub command-line tool (gh) with Homebrew if needed.
#   2. Signs you in to GitHub in your browser (first time only).
#   3. Commits the code (using your private GitHub "noreply" email address).
#   4. Creates the repository and uploads the code. GitHub then builds the
#      app automatically (see .github/workflows/build.yml).
#   5. With a version (e.g. v1.0.0): tags it so GitHub publishes a Release.
set -euo pipefail
cd "$(dirname "$0")"

REPO_NAME="${1:-SpaceKeeper-Windows}"
VERSION="${2:-}"
DESCRIPTION="Windows 11 tray app to name, pin, reorder, add and remove virtual desktops (WinUI 3, .NET 10)."

if ! command -v git >/dev/null 2>&1; then
  echo "Git isn't installed. Run: xcode-select --install   then run this script again."
  exit 1
fi
if ! command -v gh >/dev/null 2>&1; then
  if command -v brew >/dev/null 2>&1; then
    echo "> Installing the GitHub command-line tool (gh)..."
    brew install gh
  else
    echo "Install Homebrew from https://brew.sh or gh from https://cli.github.com, then run this again."
    exit 1
  fi
fi
if ! gh auth status >/dev/null 2>&1; then
  echo "> Signing in to GitHub (a browser window will open)..."
  gh auth login --hostname github.com --git-protocol https --web
fi
gh auth setup-git >/dev/null 2>&1 || true
LOGIN="$(gh api user --jq .login)"
USER_ID="$(gh api user --jq .id)"
FULL_NAME="$(gh api user --jq '.name // .login')"

[ -d .git ] || git init -b main >/dev/null
git config user.name "${FULL_NAME}"
git config user.email "${USER_ID}+${LOGIN}@users.noreply.github.com"

git add -A
if git diff --cached --quiet; then
  echo "> Nothing new to commit."
else
  if git rev-parse --verify HEAD >/dev/null 2>&1; then
    git commit -m "Update SpaceKeeper for Windows" >/dev/null
  else
    git commit -m "Initial commit: SpaceKeeper for Windows" >/dev/null
  fi
  echo "> Committed: $(git log -1 --pretty=%s)"
fi

if git remote get-url origin >/dev/null 2>&1; then
  echo "> Uploading..."
  git push -u origin main
else
  echo "> Creating public repository ${LOGIN}/${REPO_NAME}..."
  gh repo create "${REPO_NAME}" --public --description "${DESCRIPTION}" --source . --remote origin --push
fi

if [ -n "${VERSION}" ]; then
  echo "> Tagging ${VERSION} (GitHub will build it and publish a Release)..."
  git tag -a "${VERSION}" -m "SpaceKeeper for Windows ${VERSION}"
  git push origin "${VERSION}"
fi

echo ""
echo "Done!"
echo "  Code:   https://github.com/${LOGIN}/${REPO_NAME}"
echo "  Builds: https://github.com/${LOGIN}/${REPO_NAME}/actions  (about 5-10 minutes)"
if [ -n "${VERSION}" ]; then
  echo "  Download: https://github.com/${LOGIN}/${REPO_NAME}/releases/tag/${VERSION}"
fi
