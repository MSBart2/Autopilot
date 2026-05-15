using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var roots = args.Length > 0
    ? args
    : ["web", "copilot-sdk"];

var results = new List<(string File, string Type, string Method, int CC)>();

foreach (var root in roots)
{
    if (!Directory.Exists(root))
    {
        Console.Error.WriteLine($"[skip] {root} not found");
        continue;
    }

    foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
    {
        // skip generated and test files
        if (file.Contains("obj\\") || file.Contains("obj/") || file.Contains(".Designer.")) continue;

        var source = await File.ReadAllTextAsync(file);
        var tree = CSharpSyntaxTree.ParseText(source, path: file);
        var root2 = await tree.GetRootAsync();

        foreach (var method in root2.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
        {
            var cc = ComputeCyclomaticComplexity(method);
            var typeName = GetTypeName(method);
            var methodName = GetMethodName(method);
            var relPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), file);
            results.Add((relPath, typeName, methodName, cc));
        }
    }
}

// Sort by CC descending
var sorted = results.OrderByDescending(r => r.CC).ToList();

Console.WriteLine($"\n{"CC",-5} {"Method",-60} {"File",-80}");
Console.WriteLine(new string('-', 150));

foreach (var (file, type, method, cc) in sorted)
{
    var label = $"{type}.{method}";
    if (label.Length > 58) label = label[..55] + "...";
    var fileShort = file.Length > 78 ? "..." + file[^75..] : file;

    var color = cc >= 15 ? ConsoleColor.Red
              : cc >= 10 ? ConsoleColor.Yellow
              : ConsoleColor.Green;

    var prev = Console.ForegroundColor;
    Console.ForegroundColor = color;
    Console.Write($"{cc,-5}");
    Console.ForegroundColor = prev;
    Console.WriteLine($" {label,-60} {fileShort}");
}

Console.WriteLine();
Console.WriteLine($"Total methods: {sorted.Count}");
Console.WriteLine($"  High complexity (CC ≥ 15): {sorted.Count(r => r.CC >= 15)}  ← needs attention");
Console.WriteLine($"  Medium  (10–14):            {sorted.Count(r => r.CC is >= 10 and < 15)}");
Console.WriteLine($"  Good     (< 10):            {sorted.Count(r => r.CC < 10)}");

static int ComputeCyclomaticComplexity(SyntaxNode method)
{
    // CC = 1 + number of decision points
    int cc = 1;
    foreach (var node in method.DescendantNodes())
    {
        cc += node switch
        {
            IfStatementSyntax                                   => 1,
            ElseClauseSyntax { Statement: not IfStatementSyntax } => 0, // else alone isn't a branch
            WhileStatementSyntax                               => 1,
            ForStatementSyntax                                 => 1,
            ForEachStatementSyntax                             => 1,
            DoStatementSyntax                                  => 1,
            CaseSwitchLabelSyntax                              => 1,
            CasePatternSwitchLabelSyntax                       => 1,
            SwitchExpressionArmSyntax                          => 1,
            CatchClauseSyntax                                  => 1,
            WhenClauseSyntax                                    => 1,
            ConditionalExpressionSyntax                        => 1,
            BinaryExpressionSyntax b when
                b.IsKind(SyntaxKind.LogicalAndExpression) ||
                b.IsKind(SyntaxKind.LogicalOrExpression)   => 1,
            _ => 0
        };
    }
    return cc;
}

static string GetTypeName(BaseMethodDeclarationSyntax method)
{
    var parent = method.Parent;
    return parent switch
    {
        BaseTypeDeclarationSyntax t => t.Identifier.Text,
        _ => "<unknown>"
    };
}

static string GetMethodName(BaseMethodDeclarationSyntax method)
{
    return method switch
    {
        MethodDeclarationSyntax m => m.Identifier.Text + (m.TypeParameterList != null ? "<T>" : ""),
        ConstructorDeclarationSyntax c => $".ctor",
        OperatorDeclarationSyntax o => $"op_{o.OperatorToken.Text}",
        ConversionOperatorDeclarationSyntax cv => $"op_{cv.Type}",
        _ => "?"
    };
}
