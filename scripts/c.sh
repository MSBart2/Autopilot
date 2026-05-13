#!/bin/bash
# Quick alias for the snarky commit script
# Usage: ./scripts/c.sh

cd "$(dirname "$0")/.."
./scripts/commit.sh "$@"
