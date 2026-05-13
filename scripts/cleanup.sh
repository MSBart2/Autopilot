#!/bin/bash
# 🧹 Workspace Cleanup Script - Keep Your Codespace Lean & Mean

echo "🧹 Starting workspace cleanup - let's reclaim that precious disk space! 💪"

# Show disk usage before cleanup
echo ""
echo "📊 Disk usage BEFORE cleanup:"
df -h /workspaces

# Function to show size of directory before deletion
cleanup_with_stats() {
    local path=$1
    local description=$2

    if [ -d "$path" ] || [ -f "$path" ]; then
        local size=$(du -sh "$path" 2>/dev/null | cut -f1)
        echo "   Removing $description ($size)..."
        rm -rf "$path"
    fi
}

echo ""
echo "🗑️  Cleaning build artifacts..."
cleanup_with_stats "bin/" "bin directory"
cleanup_with_stats "obj/" "obj directory"
cleanup_with_stats "tests/Cyberpilot.Web.UnitTests/bin" "unit test bins"
cleanup_with_stats "tests/Cyberpilot.Web.UnitTests/obj" "unit test objs"
cleanup_with_stats "tests/Cyberpilot.Web.PlaywrightTests/bin" "playwright test bins"
cleanup_with_stats "tests/Cyberpilot.Web.PlaywrightTests/obj" "playwright test objs"

echo ""
echo "🧪 Cleaning test results..."
find . -type d -name "TestResults" -exec rm -rf {} + 2>/dev/null
find . -name "*.trx" -delete 2>/dev/null
find . -name "coverage*.json" -delete 2>/dev/null
find . -name "coverage*.xml" -delete 2>/dev/null

echo ""
echo "📦 Cleaning NuGet cache (local workspace only)..."
rm -rf ~/.nuget/packages 2>/dev/null || true

echo ""
echo "🎭 Cleaning Playwright browsers (BIG space saver!)..."
# Playwright browsers are stored in ~/.cache/ms-playwright
cleanup_with_stats "$HOME/.cache/ms-playwright" "Playwright browsers"

echo ""
echo "📚 Cleaning Docker artifacts (if any)..."
docker system prune -f 2>/dev/null || echo "   Docker not available or no artifacts to clean"

echo ""
echo "🧹 Cleaning temporary files..."
find . -type f -name "*.tmp" -delete 2>/dev/null
find . -type f -name "*.log" -delete 2>/dev/null
find . -type d -name "tmp" -exec rm -rf {} + 2>/dev/null
find . -type d -name "temp" -exec rm -rf {} + 2>/dev/null

echo ""
echo "📊 Disk usage AFTER cleanup:"
df -h /workspaces

echo ""
echo "✨ Cleanup complete! Your workspace is now squeaky clean! 💖"