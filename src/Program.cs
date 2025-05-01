using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

class TypeAndAccessor(string? type, string accessor){
    public string? type = type;
    public string accessor = accessor;
}

class ConstructMemberAccessOutput(string output, string? type){
    public string output = output;
    public string? type = type;
}

class Converter : CSharpSyntaxWalker
{
    string _output = "";
    int _indentLevel = 0;
    const int indentSize = 4; 
    CppLibrary[] libs;
    CppClassTypes cppClassTypes;
    Dictionary<string, string> variables = [];

    public Converter(CppLibrary[] libs, CppClassTypes cppClassTypes){
        this.libs = libs;
        this.cppClassTypes = cppClassTypes;
    }

    private string Indent(){
        return new string(' ', _indentLevel * indentSize);
    }

    private string Line(string line)
    {
        return Indent() + line + '\n';
    }

    static string MapType(string type)
    {
        return type switch
        {
            "var" => "auto",
            "uint" => "unsigned int",
            "int" => "int",
            "float" => "float",
            "char" => "char",
            "double" => "double",
            "string" => "const char*",
            "bool" => "bool",
            "c_string" => "const char*",
            _ => throw new Exception("Unexpected type: "+type),
        };
    }

    public override void VisitSizeOfExpression(SizeOfExpressionSyntax node)
    {
        _output += "sizeof("+node.Type.ToString()+")";
    }

    public override void VisitCastExpression(CastExpressionSyntax node)
    {
        string targetType = MapType(node.Type.ToString());
        _output += $"({targetType})";
        Visit(node.Expression);
    }

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        _output += "(";
        Visit(node.Left);
        _output += $" {node.OperatorToken.Text} ";
        Visit(node.Right);
        _output += ")";
    }

    public override void VisitExpressionElement(ExpressionElementSyntax node)
    {
        Visit(node.Expression);
    }

    public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
    {
        _output += node.OperatorToken.Text;
        Visit(node.Operand);
    }

    public override void VisitEqualsValueClause(EqualsValueClauseSyntax node)
    {
        _output += " = ";
        Visit(node.Value);
    }

    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        Visit(node.Left);
        _output += " = ";
        Visit(node.Right);
    }

    public override void VisitArgument(ArgumentSyntax node)
    {
        if (node.RefKindKeyword.Kind() == SyntaxKind.RefKeyword)
        {
            _output += "&";
        }
        Visit(node.Expression);
    }

    public override void VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        var token = node.Token;

        // Handle null literal
        if (node.IsKind(SyntaxKind.NullLiteralExpression))
        {
            _output += "NULL";
            return;
        }

        // Handle C# verbatim strings → C++ raw strings
        if (token.Kind() == SyntaxKind.StringLiteralToken && token.Text.StartsWith("@\""))
        {
            _output += $"R\"({token.ValueText})\"";
            return;
        }

        // Default: emit literal token as-is (e.g., numbers, "text")
        _output += token.Text;
    }

    public override void VisitReturnStatement(ReturnStatementSyntax node)
    {
        _output += Indent() + $"return ";
        Visit(node.Expression);
        _output += ";\n";
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (node.Identifier.Text == "Main")
        {
            string returnType = node.ReturnType.ToString();
            foreach(var lib in libs){
                foreach(var h in lib.IncludeHeaders){
                    _output += $"#include {h}\n";
                }
            }
            _output += "#include <cstdio>\n";
            _output += "#include <cstdlib>\n\n";

            _output += $"{returnType} main() {{\n";
            _indentLevel++;

            foreach (var statement in node.Body.Statements)
            {
                Visit(statement);
            }

            _indentLevel--;
            _output += Line("}");
        }
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        Visit(node.Expression);
        _output += "(";

        var args = node.ArgumentList.Arguments;
        for (int i = 0; i < args.Count; i++)
        {
            Visit(args[i]);
            if (i < args.Count - 1)
                _output += ", ";
        }

        _output += ")";
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
        if(decl.Type is ArrayTypeSyntax arrayTypeSyntax){
            var elementType = MapType(arrayTypeSyntax.ElementType.ToString());
            foreach (var variable in decl.Variables)
            {
                string name = variable.Identifier.Text;
                _output += $"{Indent()}{elementType} {name}";

                if (variable.Initializer != null)
                {
                    if(variable.Initializer.Value is ArrayCreationExpressionSyntax arrayCreation){
                        string sizeExpr = arrayCreation.Type.RankSpecifiers.First().Sizes.First().ToString();
                        _output += $"[{sizeExpr}]";
                    }
                    else if(variable.Initializer.Value is CollectionExpressionSyntax expressionSyntax){
                        _output += "[] = {";
                        var elements = expressionSyntax.Elements.ToArray();
                        for(var i=0;i<elements.Length;i++){
                            Visit(elements[i]);
                            if(i < elements.Length-1){
                                _output += ", ";
                            }
                        }
                        _output += "}";
                    }
                    else{
                        Console.WriteLine("--------------------"+variable.Initializer.Value.GetType().Name);
                    }
                }

                _output += ";\n";
            }
        }
        else{
            var typename = decl.Type.ToString(); 
            string type;
            if(cppClassTypes.cppPtrs.ContainsKey(typename)){
                type = typename+"*";
            }
            else if(cppClassTypes.cppAnds.ContainsKey(typename)){
                type = typename+"&";
            }
            else if(cppClassTypes.cpps.ContainsKey(typename)){
                type = typename;
            }
            else{
                type = MapType(typename);
            }
            foreach (var variable in decl.Variables)
            {
                string name = variable.Identifier.Text;
                variables.Add(name, typename);
                _output += $"{Indent()}{type} {name}";

                if (variable.Initializer != null)
                {
                    _output += " = ";
                    Visit(variable.Initializer.Value);
                }

                _output += ";\n";
            }
        }
        
    }

    public override void VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        _output += Indent();
        Visit(node.Expression);
        _output += ";\n";
    }

    public override void VisitWhileStatement(WhileStatementSyntax node)
    {
        _output += Indent() + "while(";
        Visit(node.Condition);
        _output += ") {\n";
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

    string? GetTypeFromClassDeclarationAndFieldName(ClassDeclarationSyntax classDeclarationSyntax, string fieldName){
        foreach(var member in classDeclarationSyntax.Members){
            if(member is FieldDeclarationSyntax field){
                foreach(var v in field.Declaration.Variables){
                    if(v.Identifier.Text == fieldName){
                        return field.Declaration.Type.ToString();
                    }
                }
            }
        }
        return null;
    }

    TypeAndAccessor GetTypeAndAccessorFromInitialMemberAccess(string name0, string name1){
        if(variables.TryGetValue(name0, out string? typename)){
            if(cppClassTypes.cppAnds.ContainsKey(typename)){
                var type = GetTypeFromClassDeclarationAndFieldName(cppClassTypes.cppAnds[typename], name1);
                return new TypeAndAccessor(type, ".");
            }
            else if(cppClassTypes.cppPtrs.ContainsKey(typename)){
                var type = GetTypeFromClassDeclarationAndFieldName(cppClassTypes.cppPtrs[typename], name1);
                return new TypeAndAccessor(type, "->");
            }
            else if(cppClassTypes.cpps.ContainsKey(typename)){
                var type = GetTypeFromClassDeclarationAndFieldName(cppClassTypes.cpps[typename], name1);
                return new TypeAndAccessor(type, ".");
            }   
            else{
                throw new Exception("type not found:"+typename);
            }
        }
        else{
            if(cppClassTypes.cppNamespaces.ContainsKey(name0)){
                var type = GetTypeFromClassDeclarationAndFieldName(cppClassTypes.cppNamespaces[name0], name1);
                return new TypeAndAccessor(type, "::");
            }
            else{
                throw new Exception("Cant find namespace:"+name0);
            }
        }
    }

    TypeAndAccessor GetTypeAndAccessorFromPreviousTypeAndFieldName(string? type, string fieldName){
        if(type != null){
            if(cppClassTypes.cppPtrs.ContainsKey(type)){
               var nextType = GetTypeFromClassDeclarationAndFieldName(cppClassTypes.cppPtrs[type], fieldName);
                return new TypeAndAccessor(nextType, "->");
            }
            else if(cppClassTypes.cppNamespaces.ContainsKey(type)){
                var nextType = GetTypeFromClassDeclarationAndFieldName(cppClassTypes.cppNamespaces[type], fieldName);
                return new TypeAndAccessor(nextType, "::");
            }
            else if(cppClassTypes.cpps.ContainsKey(type)){
                var nextType = GetTypeFromClassDeclarationAndFieldName(cppClassTypes.cpps[type], fieldName);
                return new TypeAndAccessor(nextType, ".");
            }
            else if(cppClassTypes.cppAnds.ContainsKey(type)){
                var nextType = GetTypeFromClassDeclarationAndFieldName(cppClassTypes.cppAnds[type], fieldName);
                return new TypeAndAccessor(nextType, ".");
            }
            else{
                throw new Exception("type not found:"+type);
            }
        }
        else{
            throw new Exception("type is null");
        }
    }

    ConstructMemberAccessOutput GetMemberAccess(MemberAccessExpressionSyntax node){
        var name1 = node.Name.Identifier.Text;
        if(node.Expression is IdentifierNameSyntax identifierNameSyntax){
            var name0 = identifierNameSyntax.Identifier.Text;
            var typeAndAccessor = GetTypeAndAccessorFromInitialMemberAccess(name0, name1);
            return new ConstructMemberAccessOutput(name0+typeAndAccessor.accessor+name1, typeAndAccessor.type);
        }
        else if(node.Expression is MemberAccessExpressionSyntax memberAccessExpressionSyntax){
            var memberAccess = GetMemberAccess(memberAccessExpressionSyntax);
            var typeAndAccessor = GetTypeAndAccessorFromPreviousTypeAndFieldName(memberAccess.type, name1);
            if(memberAccess.type == null){
                memberAccess.output += "." + name1;
                return memberAccess;
            }
            memberAccess.output += typeAndAccessor.accessor + name1;
            memberAccess.type = typeAndAccessor.type;
            return memberAccess;
        }
        else{
            throw new Exception("Incomplete member access: "+node.Expression.GetType().Name);
        }
    }

    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        _output += GetMemberAccess(node).output;
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        _output += node.Identifier.Text;
    }

    public override void VisitCompilationUnit(CompilationUnitSyntax node)
    {
        foreach (var member in node.Members){
            Visit(member);
        }
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        foreach (var member in node.Members){
            Visit(member);
        }
    }

    public override void DefaultVisit(SyntaxNode node)
    {
        Console.WriteLine("----->" + node.GetType().Name);
    }

    public string GetConvertedCode() => _output;
}

class CppClassTypes : CSharpSyntaxWalker {
    public Dictionary<string, ClassDeclarationSyntax> cppPtrs = [];
    public Dictionary<string, ClassDeclarationSyntax> cppAnds = [];
    public Dictionary<string, ClassDeclarationSyntax> cpps = [];
    public Dictionary<string, ClassDeclarationSyntax> cppNamespaces = [];

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        foreach (var attributeList in node.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var attrName = attribute.Name.ToString();

                if (attrName == "CppPtr")
                {
                    cppPtrs.Add(node.Identifier.Text, node);
                }
                else if (attrName == "CppAnd")
                {
                    cppAnds.Add(node.Identifier.Text, node);
                }
                else if(attrName == "Cpp")
                {
                    cpps.Add(node.Identifier.Text, node);
                }
                else if(attrName == "CppNamespace")
                {
                    cppNamespaces.Add(node.Identifier.Text, node);
                }
            }
        }

        base.VisitClassDeclaration(node);
    }
}

class Program
{
    static void Main()
    {
        var code = File.ReadAllText("input/imgui.txt");
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        CppLibrary[] libs = [CppLibrary.Glad(), CppLibrary.Glfw(), CppLibrary.ImGUIDocking()];
        PackageManager.Build(libs);

        var cppClassTypes = new CppClassTypes();
        cppClassTypes.Visit(root);

        var converter = new Converter(libs, cppClassTypes);
        converter.Visit(root);

        var cppCode = converter.GetConvertedCode();

        File.WriteAllText(Path.Combine("build", "out.cpp"), cppCode);
        Console.WriteLine("✅ build\\out.cpp generated.");
        Console.WriteLine(cppCode);
    }
}