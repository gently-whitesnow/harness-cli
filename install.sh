#!/bin/sh
# Installs the harness CLI. Its release must match a repository's contract; by default this
# installs the newest release, while HARNESS_VERSION selects an exact matching release.
#
#   curl -fsSL https://raw.githubusercontent.com/gently-whitesnow/harness-cli/master/install.sh | sh
#
# Environment:
#   HARNESS_VERSION      release to install, such as 2.0.0 (default: the latest release)
#   HARNESS_INSTALL_DIR  where to put the binary (default: ~/.local/bin)
#   HARNESS_NO_SETUP     set to any value to skip `harness setup` in the current clone
set -eu

REPOSITORY="gently-whitesnow/harness-cli"
VERSION="${HARNESS_VERSION:-latest}"
INSTALL_DIR="${HARNESS_INSTALL_DIR:-}"
NO_SETUP="${HARNESS_NO_SETUP:-}"
SCOPE="user"

while [ $# -gt 0 ]; do
  case "$1" in
    --version | --dir | --scope)
      [ $# -ge 2 ] || { echo "harness install: '$1' requires a value." >&2; exit 2; }
      case "$1" in
        --version) VERSION="$2" ;;
        --dir) INSTALL_DIR="$2" ;;
        --scope) SCOPE="$2" ;;
      esac
      shift 2
      ;;
    --no-setup) NO_SETUP=1; shift ;;
    *) echo "harness install: unknown option '$1'" >&2; exit 2 ;;
  esac
done

fail() { echo "harness install: $*" >&2; exit 1; }

need() { command -v "$1" >/dev/null 2>&1 || fail "'$1' is required but not installed."; }

case "$SCOPE" in
  user)
    INSTALL_DIR="${INSTALL_DIR:-$HOME/.local/bin}"
    ;;
  clone)
    need git
    common_dir=$(git rev-parse --git-common-dir 2>/dev/null) \
      || fail "--scope clone must be run inside a Git clone."
    case "$common_dir" in
      /*) ;;
      *) common_dir=$(cd "$common_dir" && pwd -P) \
        || fail "could not resolve the clone's common Git directory." ;;
    esac
    INSTALL_DIR="$common_dir/harness/bin"
    ;;
  *)
    echo "harness install: --scope must be 'user' or 'clone'." >&2
    exit 2
    ;;
esac

if [ "$SCOPE" = "clone" ] && [ -n "$NO_SETUP" ]; then
  fail "--no-setup is incompatible with --scope clone."
fi

need curl
need tar

# NativeAOT artifacts are per RID, and a glibc binary does not run on musl, so the C library
# is part of the identity of a Linux build rather than a detail.
detect_runtime_identifier() {
  os=$(uname -s)
  machine=$(uname -m)

  case "$machine" in
    arm64 | aarch64) architecture="arm64" ;;
    x86_64 | amd64) architecture="x64" ;;
    *) fail "unsupported architecture '$machine'." ;;
  esac

  case "$os" in
    Darwin) echo "osx-$architecture" ;;
    Linux)
      if [ -n "$(find /lib /usr/lib -maxdepth 1 -name 'ld-musl-*' -print -quit 2>/dev/null)" ]; then
        echo "linux-musl-$architecture"
      else
        echo "linux-$architecture"
      fi
      ;;
    *) fail "unsupported operating system '$os'." ;;
  esac
}

verify_checksum() {
  archive="$1"
  expected="$2"

  if command -v sha256sum >/dev/null 2>&1; then
    actual=$(sha256sum "$archive" | cut -d' ' -f1)
  elif command -v shasum >/dev/null 2>&1; then
    actual=$(shasum -a 256 "$archive" | cut -d' ' -f1)
  else
    fail "'sha256sum' or 'shasum' is required to verify the download."
  fi

  [ "$actual" = "$expected" ] || fail "checksum mismatch: expected $expected, got $actual."
}

runtime_identifier=$(detect_runtime_identifier)
archive_name="harness-$runtime_identifier.tar.gz"

if [ "$VERSION" = "latest" ]; then
  base_url="https://github.com/$REPOSITORY/releases/latest/download"
else
  base_url="https://github.com/$REPOSITORY/releases/download/v$VERSION"
fi

work=""
lock_directory=""
lock_acquired=""

cleanup() {
  if [ -n "$lock_acquired" ]; then
    rm -f "$lock_directory/pid"
    rmdir "$lock_directory" 2>/dev/null || true
  fi
  if [ -n "$work" ]; then
    rm -rf "$work"
  fi
}
trap cleanup EXIT
trap 'exit 1' HUP INT TERM

if [ "$SCOPE" = "clone" ]; then
  mkdir -p "$common_dir/harness"
  lock_directory="$common_dir/harness/install.lock"
  attempts=0
  while ! mkdir "$lock_directory" 2>/dev/null; do
    if [ "$attempts" -eq 0 ]; then
      echo "Waiting for another clone-local harness installation..."
    fi
    attempts=$((attempts + 1))
    [ "$attempts" -lt 60 ] \
      || fail "timed out waiting for clone-local installation lock '$lock_directory'."
    sleep 1
  done
  lock_acquired=1
  printf '%s\n' "$$" >"$lock_directory/pid"
fi

work=$(mktemp -d)

echo "Downloading harness ($VERSION, $runtime_identifier)..."
curl -fsSL "$base_url/$archive_name" -o "$work/$archive_name" \
  || fail "could not download $base_url/$archive_name."
curl -fsSL "$base_url/$archive_name.sha256" -o "$work/$archive_name.sha256" \
  || fail "could not download the checksum for $archive_name."

verify_checksum "$work/$archive_name" "$(cut -d' ' -f1 <"$work/$archive_name.sha256")"

tar -xzf "$work/$archive_name" -C "$work"
mkdir -p "$INSTALL_DIR"

# Replacing the file in one step keeps a running harness from reading a half-written binary.
mv "$work/harness" "$INSTALL_DIR/harness.tmp"
chmod +x "$INSTALL_DIR/harness.tmp"
mv "$INSTALL_DIR/harness.tmp" "$INSTALL_DIR/harness"

echo "Installed $("$INSTALL_DIR/harness" version | head -1) to $INSTALL_DIR/harness"

if [ "$SCOPE" = "user" ]; then
  case ":$PATH:" in
    *":$INSTALL_DIR:"*) ;;
    *) echo "Add it to your PATH:  export PATH=\"$INSTALL_DIR:\$PATH\"" ;;
  esac
fi

# A clone needs its commit template and hook activated once, and that step is the one agents
# and disposable sandboxes lose most often. Installing inside a framed repository does it.
if [ "$SCOPE" = "clone" ]; then
  "$INSTALL_DIR/harness" setup
elif [ -z "$NO_SETUP" ] && [ -f ".harness.json" ] && command -v git >/dev/null 2>&1 \
  && git rev-parse --git-dir >/dev/null 2>&1; then
  "$INSTALL_DIR/harness" setup || true
fi
