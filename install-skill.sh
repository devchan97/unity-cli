#!/bin/sh
# Install unity-cli Claude Code skill to ~/.claude/skills/unity-cli/
set -e

SKILL_DIR="$HOME/.claude/skills/unity-cli"
SKILL_URL="https://raw.githubusercontent.com/devchan97/unity-cli/master/skill/SKILL.md"

mkdir -p "$SKILL_DIR"

echo "Installing unity-cli Claude Code skill..."
curl -fsSL "$SKILL_URL" -o "$SKILL_DIR/SKILL.md"

echo "Installed skill to $SKILL_DIR/SKILL.md"
echo "Claude Code will now recognize unity-cli commands in any project."
