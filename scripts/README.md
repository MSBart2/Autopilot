# 🔥 Snarky Auto-Commit Script

Because your code deserves attitude and automation.

## What It Does

This script is your personal DevOps assistant with a serious personality. It will:

1. **Build** your app (Release mode, because we're professionals here)
2. **Run tests** (all of them, no excuses)
3. **Lint markdown** (using Super-Linter, same as CI 📝)
4. **Check coverage** (35% controller line coverage via Coverlet MSBuild, warning-only)
5. **Smoke test** (make sure the app actually runs)
6. **Create a branch** (if you forgot like the rebel you are)
7. **Commit** with a randomly selected snarky message
8. **Create a PR** with maximum attitude

All while throwing shade at your coding practices. 😎

## Prerequisites

- .NET SDK (obviously)
- Git (duh)
- Docker (for Super-Linter markdown checks)
- GitHub CLI (`gh`) - optional but recommended for auto PR creation

### Install Prerequisites

```bash
# macOS
brew install gh docker

# Linux
sudo apt install gh docker.io
```

Then authenticate:

```bash
gh auth login
```

## Usage

### Full Version

```bash
./scripts/commit.sh
```

### Quick Version

```bash
./scripts/c.sh
```

### What You'll Get

**Random Snarky Commit Messages:**

- "Fixed the thing. You know, THAT thing. 🙄"
- "Code so clean it sparkles ✨ (unlike my commit history)"
- "This commit is chef's kiss 👌 Your code review? Probably not."
- "Made the build green. Made the reviewers green with envy 💚"
- ...and many more!

**Random Spicy PR Titles:**

- "🔥 This PR is hotter than your last performance review"
- "✨ Fixed everything. You're welcome."
- "💪 Flex on 'em: The Commit"
- "🎯 Bullseye: Actually working code incoming"
- ...and more sass!

## What Happens If

### ❌ Build Fails

```text
✗ Build failed!
Even the compiler can't handle this mess 💀
Fix your build errors first, genius 🤓
```

Script exits. Fix your code, champ.

### ❌ Tests Fail

```text
✗ Tests failed!
Shocking absolutely no one 🙄
Fix your tests before you embarrass yourself
```

Script exits. Green tests only in this house.

### ⚠ Coverage Drops

```text
⚠ Coverage gate warning
Coverlet MSBuild reported controller line coverage below 35%
```

The script warns and continues. Write those tests, but your commit flow keeps moving.

### ✅ Everything Passes

```text
╔═══════════════════════════════════════════════════╗
║           ✨ COMMIT COMPLETE ✨                   ║
║  Your code is now someone else's problem 😎      ║
╚═══════════════════════════════════════════════════╝
```

Victory! 🎉

## Safety Features

- **Never commits to main/master directly** - Creates a feature branch automatically
- **Won't commit without tests passing** - Because we're not animals
- **Checks coverage** - Warns if controller line coverage falls below 35%
- **Smoke tests the app** - Makes sure it actually runs
- **Pushes safely** - Sets upstream tracking automatically

## Configuration

### Adjust Coverage Threshold

Edit the Coverlet MSBuild threshold in `commit.sh`:

```bash
/p:Threshold=35
#            ^^ Change this
```

### Add Your Own Snarky Messages

Edit the arrays at the top of `commit.sh`:

- `SNARKY_COMMIT_MESSAGES` - For commit messages
- `SNARKY_PR_TITLES` - For PR titles

## Examples

```bash
# Just run it!
./scripts/commit.sh

# Or use the shortcut
./scripts/c.sh
```

Sample output:

```text
╔═══════════════════════════════════════════════════╗
║  🔥 SNARKY AUTO-COMMIT EXTRAVAGANZA 3000™ 🔥     ║
║  Because your code deserves attitude             ║
╚═══════════════════════════════════════════════════╝

[1/8] Building the app... (pretending this isn't scary)
      ✓ Build successful! The compiler actually likes you today 🎉

[2/8] Running tests... (fingers crossed)
      ✓ All tests passed! Your code is less broken than usual 🎊

[3/8] Linting markdown... (docs matter too)
      ✓ Markdown looks gorgeous! Your docs are 💯

[4/8] Checking code coverage... (hoping you wrote tests)
      ✓ Coverage check passed!
      Coverlet MSBuild enforced 35% controller line coverage.
      Look at you, writing tests like a responsible adult 🏆

[5/8] Smoke testing... (please don't catch fire)
      ✓ App starts successfully! It's alive! IT'S ALIVE! ⚡

[6/8] Checking git branch... (not like you were organized anyway)
      ⚠ You're on main! Rookie mistake 🙈
      Creating branch: feature/absolutely-legendary-20251208-143022
      ✓ Switched to new branch Crisis averted! 😅

[7/8] Committing changes... (with maximum attitude)
      ✓ Committed with message:
      "Deployed code. Dropped mic. 🎤"

[8/8] Creating PR... (prepare for glory)
      ✓ PR created successfully!
      💎 Premium code at economy prices
      https://github.com/rbmathis/Cyberpilot/pull/42

🎉 MISSION ACCOMPLISHED 🎉
Now go tell everyone how amazing you are 💪
```

## Troubleshooting

### "gh: command not found"

Install GitHub CLI (see Prerequisites above)

### "python3: command not found"

Install Python 3:

```bash
# macOS
brew install python3

# Linux
sudo apt install python3
```

### "Permission denied"

Make scripts executable:

```bash
chmod +x scripts/commit.sh scripts/c.sh
```

### Tests keep failing

That's not the script's fault, genius. Fix your code! 😏

### "Docker not found"

Install Docker:

```bash
# macOS
brew install docker

# Linux
sudo apt install docker.io
```

Or the script will skip markdown linting (CI will catch it anyway).

## Other Useful Scripts

### `cleanup.sh` - Workspace Cleanup 🧹

Deep clean your workspace to free up disk space:

```bash
./scripts/cleanup.sh
```

**What it removes:**
- Build artifacts (bin/, obj/)
- Test results and coverage files
- Playwright browsers (~400MB!)
- NuGet caches
- Temp files

Run this when your codespace is running low on storage!

### `lint-docs.sh` - Markdown Linting 📝

Run Super-Linter for markdown (same as CI):

```bash
./scripts/lint-docs.sh
```

**What it checks:**
- Heading hierarchy
- Consistent list formatting
- Proper link formatting
- Code block syntax
- Uses `.markdownlint.json` configuration
- Same tool as GitHub Actions workflow!

## Philosophy

> "Why waste time write good commit message when bad message do trick... but with style?"

This script embodies the philosophy that automation should be:

- ✅ **Reliable** - Won't let bad code through
- ✅ **Fast** - Complete workflow in seconds
- ✅ **Entertaining** - Because dev work should be fun
- ✅ **Safe** - Multiple quality gates

## Contributing

Want to add more snark? Create a PR with your own sassy messages!

## License

MIT - Use it, abuse it, add more attitude to it.

---

_Made with 💅 and excessive confidence_
