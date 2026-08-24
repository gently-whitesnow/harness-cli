#!/bin/sh
# Installs the harness CLI. The binary reproduces whatever release a repository pins, so
# there is nothing to pin here: this always installs the newest one unless asked otherwise.
#
#   curl -fsSL https://raw.githubusercontent.com/gently-whitesnow/harness-cli/master/install.sh | sh
#
# Environment:
#   HARNESS_VERSION      release to install, such as 1.2.0 (default: the latest release)
#   HARNESS_INSTALL_DIR  where to put the binary (default: ~/.local/bin)
#   HARNESS_NO_SETUP     set to any value to skip `harness setup` in the current clone
set -eu

REPOSITORY="gently-whitesnow/harness-cli"
VERSION="${HARNESS_VERSION:-latest}"
INSTALL_DIR="${HARNESS_INSTALL_DIR:-$HOME/.local/bin}"
NO_SETUP="${HARNESS_NO_SETUP:-}"

while [ $# -gt 0 ]; do
  case "$1" in
    --version) VERSION="$2"; shift 2 ;;
    --dir) INSTALL_DIR="$2"; shift 2 ;;
    --no-setup) NO_SETUP=1; shift ;;
    *) echo "harness install: unknown option '$1'" >&2; exit 2 ;;
  esac
done

fail() { echo "harness install: $*" >&2; exit 1; }

need() { command -v "$1" >/dev/null 2>&1 || fail "'$1' is required but not installed."; }

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
    echo "harness install: no sha256 tool found; skipping checksum verification." >&2
    return 0
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

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

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

case ":$PATH:" in
  *":$INSTALL_DIR:"*) ;;
  *) echo "Add it to your PATH:  export PATH=\"$INSTALL_DIR:\$PATH\"" ;;
esac

# A clone needs its commit template and hook activated once, and that step is the one agents
# and disposable sandboxes lose most often. Installing inside a framed repository does it.
if [ -z "$NO_SETUP" ] && [ -f ".harness.json" ] && command -v git >/dev/null 2>&1 \
  && git rev-parse --git-dir >/dev/null 2>&1; then
  "$INSTALL_DIR/harness" setup || true
fi
