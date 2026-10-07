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
#   3. Commits the code (using your private GitHub "noreply" email address),
#      after checking nothing looks like a key or secret, then lists the
#      files that will become public and asks you to confirm.
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

# Safety check before anything is published: refuse files that look like
# keys, certificates or secrets (by name or by content), even if .gitignore
# missed them.
RISKY_NAMES='\.(p12|pfx|pem|key|cer|crt|snk|mobileprovision|provisionprofile|ips)$|(^|/)\.env|crash\.log$|-map\.txt$|state\.json$|id_(rsa|ed25519)'
RISKY_TEXT='BEGIN ([A-Z ]*)PRIVATE KEY|ghp_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|AKIA[0-9A-Z]{16}|xox[baprs]-[A-Za-z0-9-]{10,}'
risky="$(git diff --cached --name-only | grep -E "$RISKY_NAMES" || true)"
secrets="$(git diff --cached -G"$RISKY_TEXT" --name-only || true)"
if [ -n "$risky$secrets" ]; then
  echo "✗ Stopped: these files look like keys or secrets and must not be published:"
  printf '%s\n' $risky $secrets | sort -u | sed 's/^/    /'
  echo "  Remove them from the folder (or add them to .gitignore), then run this again."
  git reset -q
  exit 1
fi

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

# Show what will become PUBLIC and ask before uploading.
if git remote get-url origin >/dev/null 2>&1; then
  git fetch -q origin main 2>/dev/null || true
fi
if git rev-parse --verify -q origin/main >/dev/null; then RANGE="origin/main..HEAD"; else RANGE="HEAD"; fi
CHANGED="$(git log --name-status --pretty=format: "$RANGE" 2>/dev/null | sed '/^$/d' | sort -u)"
if [ -n "$CHANGED" ]; then
  echo ""
  echo "These files will be published to a PUBLIC repository (A = added, M = changed, D = deleted):"
  echo "$CHANGED" | sed 's/^/    /'
  echo ""
  read -r -p "Publish them? [y/N] " answer
  case "$answer" in
    [yY]|[yY][eE][sS]) ;;
    *) echo "Not published. Your commit is kept locally; run this again when ready."; exit 0 ;;
  esac
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
