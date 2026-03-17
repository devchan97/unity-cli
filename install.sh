#!/bin/sh
set -e

REPO="devchan97/unity-cli"

OS="$(uname -s | tr '[:upper:]' '[:lower:]')"
case "$OS" in
  linux)  ;;
  darwin) ;;
  *)      echo "Unsupported OS: $OS (use Windows instructions in README)"; exit 1 ;;
esac

ARCH="$(uname -m)"
case "$ARCH" in
  x86_64|amd64)  ARCH="amd64" ;;
  aarch64|arm64)  ARCH="arm64" ;;
  *)              echo "Unsupported architecture: $ARCH"; exit 1 ;;
esac

INSTALL_DIR="$HOME/.local/bin"
mkdir -p "$INSTALL_DIR"

URL="https://github.com/${REPO}/releases/latest/download/unity-cli-${OS}-${ARCH}"

echo "Downloading unity-cli for ${OS}/${ARCH}..."
curl -fsSL "$URL" -o "$INSTALL_DIR/unity-cli"
chmod +x "$INSTALL_DIR/unity-cli"

case ":$PATH:" in
  *":$INSTALL_DIR:"*) ;;
  *)
    export PATH="$INSTALL_DIR:$PATH"
    LINE="export PATH=\"$INSTALL_DIR:\$PATH\""
    if [ -f "$HOME/.zshrc" ]; then
      echo "$LINE" >> "$HOME/.zshrc"
    elif [ -f "$HOME/.bashrc" ]; then
      echo "$LINE" >> "$HOME/.bashrc"
    else
      echo "$LINE" >> "$HOME/.profile"
    fi
    echo "Added $INSTALL_DIR to PATH (restart shell to apply)" ;;
esac

echo "Installed unity-cli to $INSTALL_DIR/unity-cli"
"$INSTALL_DIR/unity-cli" version

# Auto-install Claude Code skill if ~/.claude/ exists
CLAUDE_DIR="$HOME/.claude"
if [ -d "$CLAUDE_DIR" ]; then
    SKILL_DIR="$CLAUDE_DIR/skills/unity-cli"
    SKILL_URL="https://raw.githubusercontent.com/devchan97/unity-cli/master/skill/SKILL.md"
    mkdir -p "$SKILL_DIR"
    if curl -fsSL "$SKILL_URL" -o "$SKILL_DIR/SKILL.md" 2>/dev/null; then
        echo "Installed Claude Code skill to $SKILL_DIR"
    else
        echo "Claude Code skill install skipped (download failed)"
    fi
fi
