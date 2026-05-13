#!/bin/bash
# 📝 Markdown Linting Script - Using Super-Linter (same as CI)
# Validates markdown files using the exact same tool as GitHub Actions

set -e

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m'

echo -e "${BOLD}${CYAN}📝 Markdown Linting with Super-Linter${NC}"
echo ""

# Check if Docker is available
if ! command -v docker &> /dev/null; then
    echo -e "${YELLOW}⚠ Docker not found${NC}"
    echo -e "${CYAN}Super-Linter requires Docker to run${NC}"
    echo -e "${MAGENTA}Install Docker or let CI handle markdown linting${NC}"
    exit 0
fi

echo -e "${CYAN}Running Super-Linter (markdown only)...${NC}"
echo ""

# Run Super-Linter for markdown only (same config as CI)
if docker run --rm \
    -e RUN_LOCAL=true \
    -e VALIDATE_MARKDOWN=true \
    -e FILTER_REGEX_EXCLUDE='^(.*/node_modules/.*|.*/bin/.*|.*/obj/.*)$' \
    -v "$PWD":/tmp/lint \
    -v "$PWD/.git":/tmp/lint/.git:ro \
    github/super-linter:v5 2>&1 | grep -v -E "(ERROR_ON_MISSING_EXEC_BIT|fatal: not a git repository)"; then

    echo ""
    echo -e "${GREEN}✓ All markdown files passed linting! 🎉${NC}"
    echo -e "${CYAN}Your documentation is gorgeous! 💖${NC}"
    exit 0
else
    echo ""
    echo -e "${RED}✗ Markdown linting failed${NC}"
    echo -e "${YELLOW}Fix the issues above and try again${NC}"
    echo -e "${CYAN}Or let CI catch them in the PR 😉${NC}"
    exit 1
fi
