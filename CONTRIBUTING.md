# Contributing to Cyberpilot 🎉

Thank you for your interest in contributing to Cyberpilot! We're excited to have you join our community. Whether you're fixing a bug, adding a feature, improving documentation, or just exploring the code, all contributions are welcome! 🚀

## 🌟 Ways to Contribute

There are many ways you can contribute to this project:

- 🐛 **Report bugs** - Found something broken? Let us know!
- ✨ **Suggest features** - Have an idea? We'd love to hear it!
- 📝 **Improve documentation** - Help make our docs clearer and more complete
- 🔧 **Fix issues** - Pick an issue and submit a PR
- 🧪 **Add tests** - Help us improve code coverage
- 💡 **Share ideas** - Join discussions and share your thoughts
- 🎨 **Improve UI/UX** - Make the app more beautiful and user-friendly

## 🚀 Getting Started

### Prerequisites

Before you begin, make sure you have:

- [.NET 9 SDK](https://dotnet.microsoft.com/download) installed
- [Git](https://git-scm.com/) for version control
- Your favorite code editor ([VS Code](https://code.visualstudio.com/) recommended)
- A GitHub account

### Setting Up Your Development Environment

1. **Fork the repository** on GitHub
2. **Clone your fork** locally:

   ```bash
   git clone https://github.com/YOUR-USERNAME/Cyberpilot.git
   cd Cyberpilot
   ```

3. **Add the upstream remote**:

   ```bash
   git remote add upstream https://github.com/rbmathis/Cyberpilot.git
   ```

4. **Restore dependencies**:

   ```bash
   dotnet restore
   libman restore
   ```

5. **Run the application**:

   ```bash
   dotnet run
   ```

6. **Run the tests**:

   ```bash
   cd tests/Cyberpilot.Web.UnitTests
   dotnet test
   ```

### Using VS Code

This project includes VS Code configurations for debugging:

- Press **F5** to build and run the application with the debugger attached
- Tasks are available for build, test, and watch operations

## 🔄 Development Workflow

1. **Create a branch** for your work:

   ```bash
   git checkout -b feature/your-feature-name
   ```

   Use prefixes like:

   - `feature/` for new features
   - `fix/` for bug fixes
   - `docs/` for documentation
   - `test/` for test improvements

2. **Make your changes** with clear, focused commits

3. **Write or update tests** for your changes

4. **Run tests locally** to ensure everything passes:

   ```bash
   dotnet test
   ```

5. **Commit your changes** with a descriptive message:

   ```bash
   git add .
   git commit -m "Add awesome feature that does X"
   ```

   Or use our snarky commit script for fun:

   ```bash
   ./scripts/commit.sh
   ```

6. **Keep your branch updated** with upstream:

   ```bash
   git fetch upstream
   git rebase upstream/main
   ```

7. **Push to your fork**:

   ```bash
   git push origin feature/your-feature-name
   ```

8. **Open a Pull Request** on GitHub!

## 📋 Pull Request Guidelines

To help us review and merge your PR quickly:

### Before Submitting

- ✅ Code builds without errors
- ✅ All tests pass
- ✅ New features include tests
- ✅ Code follows existing style and conventions
- ✅ XML documentation added for public APIs
- ✅ Commit messages are clear and descriptive

### PR Description Should Include

- **What** - What changes does this PR make?
- **Why** - Why is this change needed?
- **How** - How does it work? (if not obvious)
- **Testing** - How did you test this?
- **Screenshots** - Include screenshots for UI changes

### Example PR Template

```markdown
## Description

Brief description of what this PR does.

## Changes

- Added feature X
- Fixed bug Y
- Updated documentation for Z

## Testing

- [ ] Unit tests added/updated
- [ ] Manual testing completed
- [ ] All tests pass locally

## Screenshots (if applicable)

[Add screenshots here]
```

## 🎯 Coding Standards

### General Guidelines

- **Follow existing patterns** - Look at similar code for examples
- **Keep it simple** - Prefer clarity over cleverness
- **Write tests** - Test your code before submitting
- **Document public APIs** - Use XML comments for all public classes and methods
- **Be consistent** - Match the style of the existing codebase

### C# Conventions

- Use **PascalCase** for class names, method names, and properties
- Use **camelCase** for local variables and parameters
- Use **meaningful names** that describe intent
- Keep methods **focused** on a single responsibility
- Use **async/await** for asynchronous operations

### XML Documentation

All public APIs should have XML documentation:

```csharp
/// <summary>
/// Brief description of what this does.
/// </summary>
/// <param name="paramName">Description of parameter.</param>
/// <returns>Description of return value.</returns>
public async Task<IActionResult> MethodName(string paramName)
{
    // Implementation
}
```

**Note**: Test files don't require XML documentation - focus on clear test names!

## 🧪 Testing Guidelines

- Write **unit tests** for business logic
- Include **integration tests** for important workflows
- Use **descriptive test names** that explain what's being tested
- Follow the **Arrange-Act-Assert** pattern
- Keep tests **focused** on a single scenario

Example test name: `Should_ReturnViewResult_When_IndexCalled()`

## 🐛 Reporting Bugs

Found a bug? Help us fix it by providing:

1. **Clear description** of the issue
2. **Steps to reproduce** the problem
3. **Expected behavior** vs actual behavior
4. **Environment details** (.NET version, OS, etc.)
5. **Screenshots or error logs** if applicable

[Open an issue](https://github.com/rbmathis/Cyberpilot/issues/new) with these details.

## 💡 Suggesting Features

Have an idea for a new feature? We'd love to hear it!

1. **Check existing issues** to see if it's already suggested
2. **Describe the feature** and its benefits
3. **Explain the use case** - why is this useful?
4. **Consider the scope** - is it a small enhancement or major feature?

[Open an issue](https://github.com/rbmathis/Cyberpilot/issues/new) to start the discussion!

## 📖 Documentation

Good documentation helps everyone! You can contribute by:

- Improving README clarity
- Adding code examples
- Writing tutorials or guides
- Fixing typos or broken links
- Adding XML comments to code

See our [Documentation Agent](.github/agents/docs.agent.md) for documentation standards.

## 🤝 Code of Conduct

### Our Pledge

We are committed to providing a welcoming and inclusive environment for everyone, regardless of:

- Experience level
- Background
- Identity
- Location

### Our Standards

**We encourage:**

- Being respectful and inclusive
- Welcoming newcomers
- Accepting constructive feedback
- Focusing on what's best for the community
- Showing empathy toward others

**We don't tolerate:**

- Harassment or discrimination
- Trolling or inflammatory comments
- Personal attacks
- Unprofessional conduct

### Enforcement

If you experience or witness unacceptable behavior, please report it to the project maintainers. All reports will be reviewed and investigated promptly and fairly.

## 🎓 First-Time Contributors

New to open source? No problem! We're here to help.

### Good First Issues

Look for issues labeled [`good first issue`](https://github.com/rbmathis/Cyberpilot/labels/good%20first%20issue) - these are great starting points for new contributors.

### Need Help?

- Read through existing code to understand patterns
- Check our [documentation](docs/)
- Look at recent PRs for examples
- Ask questions in issue comments
- Reach out to maintainers

**Remember**: Everyone was a beginner once. Don't hesitate to ask questions! 💪

## 🏆 Recognition

We appreciate all contributions! Contributors will be:

- Listed in release notes
- Acknowledged in the project
- Part of our amazing community

## 📞 Getting Help

Need assistance? Here's how to reach us:

- **Questions about the code** - Open an issue or discussion
- **Bug reports** - Use the issue tracker
- **Feature requests** - Start a discussion or open an issue
- **Security concerns** - See [SECURITY.md](SECURITY.md) (if you have one)

## 📜 License

By contributing to Cyberpilot, you agree that your contributions will be licensed under the same license as the project.

---

## 🚀 Ready to Contribute?

1. Pick an issue or create one
2. Fork the repo
3. Make your changes
4. Submit a PR
5. Celebrate your contribution! 🎉

**Thank you for making Cyberpilot better!** Every contribution, no matter how small, makes a difference. We're excited to see what you'll bring to the project! 💖

Happy coding! 👩‍💻👨‍💻
