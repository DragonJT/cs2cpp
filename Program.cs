using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Net;
using System.IO.Compression;

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
    static void EnsureGLFWDownloaded(){
        string glfwDir = Path.Combine("third_party", "glfw");
        if (Directory.Exists(glfwDir))
        {
            Console.WriteLine("✅ GLFW already downloaded.");
            return;
        }

        Console.WriteLine("⬇️ Downloading GLFW...");

        Directory.CreateDirectory("third_party");

        // Download GLFW zip
        string url = "https://github.com/glfw/glfw/releases/download/3.3.8/glfw-3.3.8.bin.WIN64.zip";
        string zipPath = Path.Combine("third_party", "glfw.zip");

        using (var client = new WebClient())
        {
            client.DownloadFile(url, zipPath);
        }

        Console.WriteLine("📦 Extracting GLFW...");

        // Extract zip
        ZipFile.ExtractToDirectory(zipPath, glfwDir);

        // Clean up zip
        File.Delete(zipPath);

        // Move extracted folder contents up one level if needed
        string innerFolder = Directory.GetDirectories(glfwDir).FirstOrDefault();
        if (innerFolder != null)
        {
            foreach (var file in Directory.GetFiles(innerFolder, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(innerFolder, file);
                string destPath = Path.Combine(glfwDir, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                File.Move(file, destPath, true);
            }

            Directory.Delete(innerFolder, true);
        }

        Console.WriteLine("✅ GLFW downloaded and extracted.");
    }

    static void Main()
    {
        var code = File.ReadAllText("input.txt");
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        var converter = new SimpleConverter();
        converter.Visit(root);

        var cppCode = converter.GetConvertedCode();

        EnsureGLFWDownloaded();

        string script = @"
        @echo off
        cl /nologo /EHsc /MD ^
        /I third_party\glfw\include ^
        build\out.cpp ^
        third_party\glfw\lib-vc2022\glfw3.lib ^
        /Fobuild\out.obj /Febuild\program.exe ^
        user32.lib gdi32.lib shell32.lib opengl32.lib

        if exist build\program.exe (
        echo.
        echo ▶️ Running program.exe...
        build\program.exe
        )
        ";

        string scriptPath = Path.Combine("build", "run_msvc.bat");
        Directory.CreateDirectory("build");
        File.WriteAllText(scriptPath, script);
        Console.WriteLine("✅ Generated build\\run_msvc.bat");

        File.WriteAllText(Path.Combine("build", "out.cpp"), cppCode);
        Console.WriteLine("✅ build\\out.cpp generated.");
        Console.WriteLine(cppCode);
    }
}