using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

class SimpleConverter : CSharpSyntaxWalker
{
    private string _output = "";
    private int _indentLevel = 0;
    private const int indentSize = 4; 

    static string RewriteExpression(string expr)
    {
        return expr.Replace("null", "NULL");
    }

    private string Line(string line)
    {
        return new string(' ', _indentLevel * indentSize) + line + '\n';
    }

    public override void VisitReturnStatement(ReturnStatementSyntax node)
    {
        string expr = node.Expression?.ToString() ?? "";
        expr = RewriteExpression(expr);
        _output += Line($"return {expr};");
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (node.Identifier.Text == "Main")
        {
            string returnType = node.ReturnType.ToString(); // "int"
            _output += "#include <glad/glad.h>\n";
            _output += "#include <GLFW/glfw3.h>\n\n";
            _output += $"{returnType} main() {{\n";
            _indentLevel++;
            // Walk actual statements first
            foreach (var statement in node.Body.Statements)
            {
                Visit(statement);
            }

            // Then extract and append block comments as code
            var comments = node.DescendantTrivia()
                .Where(trivia => trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
                .Select(trivia => trivia.ToFullString());

            foreach (var comment in comments)
            {
                string stripped = comment.Trim(' ', '/', '*', '\r', '\n');
                _output += Line("// Appended from comment:");
                foreach (var line in stripped.Split('\n'))
                {
                    _output += Line(RewriteExpression(line.TrimEnd()));
                }
            }

            _indentLevel--;
            _output += Line("}");
        }
    }

    public override void VisitIfStatement(IfStatementSyntax node)
    {
        string condition = node.Condition.ToString();
        _output += Line($"if ({condition}) {{");
        _indentLevel++;
        // Visit inner statements (block or single)
        if (node.Statement is BlockSyntax block)
        {
            foreach (var stmt in block.Statements)
            {
                Visit(stmt); // recursively handle return statements, etc.
            }
        }
        else
        {
            Visit(node.Statement);
        }

        _indentLevel--;
        _output += Line("}");

        if (node.Else != null)
        {
            _output += Line("else {");
            _indentLevel++;
            Visit(node.Else.Statement);
            _indentLevel--;
            _output += Line("}");
        }
    }

    public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        var decl = node.Declaration;
        string type = decl.Type.ToString();

        foreach (var variable in decl.Variables)
        {
            string name = variable.Identifier.Text;
            string init = variable.Initializer?.Value.ToString() ?? "";
            init = RewriteExpression(init);
            // Heuristic: convert GLFWwindow → GLFWwindow* (you can later make this smarter)
            if (type == "GLFWwindow")
                type += "*";
            _output += Line($"{type} {name} = {init};");
        }
    }

    public override void VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        string expr = node.Expression.ToString();
        expr = RewriteExpression(expr);
        _output += Line($"{expr};");
    }

    public override void VisitWhileStatement(WhileStatementSyntax node)
    {
        string condition = RewriteExpression(node.Condition.ToString());
        _output += Line($"while ({condition}) {{");
        _indentLevel++;
        if (node.Statement is BlockSyntax block)
        {
            foreach (var stmt in block.Statements)
                Visit(stmt);
        }
        else
        {
            Visit(node.Statement);
        }
        _indentLevel--;
        _output += Line("}");
    }

    public string GetConvertedCode() => _output;
}

class Program
{
    static void Main()
    {
        var code = File.ReadAllText("input.txt");
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        var converter = new SimpleConverter();
        converter.Visit(root);

        var cppCode = converter.GetConvertedCode();

        PackageManager.Build([CppLibrary.Glfw(), CppLibrary.Glad()]);
        File.WriteAllText(Path.Combine("build", "out.cpp"), cppCode);
        Console.WriteLine("✅ build\\out.cpp generated.");
        Console.WriteLine(cppCode);
    }
}