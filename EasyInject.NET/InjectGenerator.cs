using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.ComponentModel;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;

namespace System.Runtime.CompilerServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal class IsExternalInit { }
}

namespace EasyInject.NET
{
    [Generator]
    public class InjectGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var injectDeclarations = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (s, _) => IsPossibleInjection(s),
                    transform: static (g, _) => GetInjectDeclarations(g))
                .Where(v => v.Item1 != null);

            context.RegisterImplementationSourceOutput(injectDeclarations, CodeGenerator.Generate);
        }

        static bool IsPossibleInjection(SyntaxNode node)
            => node is ClassDeclarationSyntax classSyntax && classSyntax.GetPropertiesAndFieldsAsMember().Any(m => m.AnyAttribute());

        static (ClassDeclarationSyntax, 
            ConstructorDeclarationSyntax?,
            List<IComplexMember>) GetInjectDeclarations(GeneratorSyntaxContext context)
        {
            var classSyntax = (ClassDeclarationSyntax) context.Node;
            var members = classSyntax.GetPropertiesAndFieldsAsMember()
                .Where(m => m.AnyAttribute(attribute => IsInjectAttribute(attribute, context.SemanticModel)))
                .Select(m => ComplexMembers.Of(m, context.SemanticModel))
                .ToList();

            if (!members.Any())
                return (null, null, null);

            var membersNames = members.Select(m => m.GetDisplayName());
            var constructor = classSyntax.ChildNodes()
                .OfType<ConstructorDeclarationSyntax>()
                .Where(c => c.AnyAttribute(attribute => IsPartialConstructorAttribute(attribute, context.SemanticModel)))
                .Where(c => c.ParameterList.Parameters.All(p => membersNames.Contains(p.Identifier.ValueText)))
                .FirstOrDefault();

            return (classSyntax, constructor, members);
        }

        static bool IsInjectAttribute(AttributeSyntax attribute, SemanticModel model)
            => model.EqualsAttribute(attribute, InjectAttributeFullName);

        static bool IsPartialConstructorAttribute(AttributeSyntax attribute, SemanticModel model)
            => model.EqualsAttribute(attribute, PartialConstructorAttributeFullName);

        const string InjectAttributeFullName = "EasyInject.NET.Attributes.InjectAttribute";
        const string PartialConstructorAttributeFullName = "EasyInject.NET.Attributes.PartialConstructorAttribute";
    }

    internal static class CodeGenerator
    {
        internal static void Generate(SourceProductionContext context,
            (ClassDeclarationSyntax classSyntax, 
            ConstructorDeclarationSyntax? partialConstructorSyntax, 
            List<IComplexMember> members) entry)
            => Generate(context, entry.classSyntax, entry.partialConstructorSyntax, entry.members);

        internal static void Generate(SourceProductionContext context, 
            ClassDeclarationSyntax classSyntax,
            ConstructorDeclarationSyntax? partialConstructorSyntax,
            List<IComplexMember> members)
        {
            var className = classSyntax.Identifier.ValueText;
            var source = GenerateSource(classSyntax, partialConstructorSyntax, members);
            context.AddSource($"{className}.g.cs", CodeFormatter.Format(source));
        }

        private static string GenerateSource(ClassDeclarationSyntax classSyntax,
            ConstructorDeclarationSyntax? partialConstructorSyntax,
            List<IComplexMember> members)
        {
            var classNamespace = classSyntax.GetNamespaceDisplay();
            var classModifier = classSyntax.Modifiers.FirstOrDefault().ToString();
            var classKeyword = classSyntax.Keyword.ToString();
            var className = classSyntax.Identifier.Value;

            var constructorArguments = members
                .Select(m => m.AsConstructorArgument())
                .Aggregate((a, b) => a + "," + b);

            var constructorSetter = members
                .Select(m => m.AsConstructorSetter())
                .Aggregate((a, b) => a + "\n" + b);

            var partialConstructorBody = partialConstructorSyntax.GetBodyContent();

            return $$"""
                namespace {{classNamespace}} {
                    {{classModifier}} partial {{classKeyword}} {{className}} {
                        {{classModifier}} {{className}} ({{constructorArguments}}) {
                            {{constructorSetter}}
                            {{partialConstructorBody}}
                        }
                    }
                }
            """;
        }
    }

    interface IComplexMember
    {
        string AsConstructorArgument();
        string AsConstructorSetter();
        string GetDisplayName();
    }

    internal record ComplexField : IComplexMember
    {
        public FieldDeclarationSyntax Syntax { get; init; }
        public IFieldSymbol Symbol { get; init; }

        private string GetDisplayType()
            => Symbol.Type.ToDisplayString();

        public string GetDisplayName()
            => Syntax.Declaration.Variables.FirstOrDefault().Identifier.ValueText;

        public string AsConstructorArgument()
            => $"{GetDisplayType()} {GetDisplayName()}";

        public string AsConstructorSetter()
            => $"this.{GetDisplayName()} = {GetDisplayName()};";

        public static ComplexField Of(FieldDeclarationSyntax syntax, SemanticModel model)
        {
            var symbol = syntax.Declaration.Variables
               .Select(v => model.GetDeclaredSymbol(v))
               .OfType<IFieldSymbol>()
               .FirstOrDefault();

            if (symbol == null)
                return null;

            return new ComplexField
            {
                Syntax = syntax,
                Symbol = symbol
            };
        }
    }

    internal record ComplexProperty : IComplexMember
    {
        public PropertyDeclarationSyntax Syntax { get; init; }
        public IPropertySymbol Symbol { get; init; }

        private string GetDisplayType()
            => Symbol.Type.ToDisplayString();

        public string GetDisplayName()
            => Syntax.Identifier.ValueText;

        public string AsConstructorArgument()
            => $"{GetDisplayType()} {GetDisplayName()}";

        public string AsConstructorSetter()
            => $"this.{GetDisplayName()} = {GetDisplayName()};";

        public static ComplexProperty Of(PropertyDeclarationSyntax syntax, SemanticModel model)
        {
            var symbol = model.GetDeclaredSymbol(syntax);
            if (symbol == null || symbol is not IPropertySymbol propertySymbol)
                return null;

            return new ComplexProperty
            {
                Syntax = syntax,
                Symbol = propertySymbol
            };
        }
    }

    internal static class ComplexMembers
    {
        internal static IComplexMember? Of(MemberDeclarationSyntax syntax, SemanticModel model)
            => syntax switch
            {
                PropertyDeclarationSyntax propertySyntax => ComplexProperty.Of(propertySyntax, model),
                FieldDeclarationSyntax fieldSyntax => ComplexField.Of(fieldSyntax, model),
                _ => null
            };
    }

    internal static class BaseMethodDeclarationSyntanxExtensions
    {
        internal static string GetBodyContent(this ConstructorDeclarationSyntax methodSyntax)
        {
            if (methodSyntax.ExpressionBody != null)
                return methodSyntax.ExpressionBody.Expression.ToFullString() + ";";

            return methodSyntax.Body
                .Statements.ToFullString();
        }
    }

    internal static class ClassDeclarationSyntaxEntesions
    {
        internal static string GetNamespaceDisplay(this ClassDeclarationSyntax syntax)
        {
            var parent = syntax.Parent;
            while (parent != null)
            {
                if (parent is NamespaceDeclarationSyntax)
                    break;

                parent = parent?.Parent;
            }

            return ((NamespaceDeclarationSyntax?)parent)?.Name?.ToFullString();
        }

        internal static IEnumerable<MemberDeclarationSyntax> GetPropertiesAndFieldsAsMember(this ClassDeclarationSyntax classSyntax)
            => classSyntax.ChildNodes()
                .OfType<PropertyDeclarationSyntax>()
                .Cast<MemberDeclarationSyntax>()
                .Concat(classSyntax.ChildNodes()
                    .OfType<FieldDeclarationSyntax>());
    }

    internal static class MemberDeclarationSyntaxEntesions
    {
        internal static bool AnyAttribute(this MemberDeclarationSyntax classSyntax)
            => classSyntax.AttributeLists.Any(l => l.Attributes.Any());

        internal static bool AnyAttribute(this MemberDeclarationSyntax classSyntax, Func<AttributeSyntax, bool> predicate)
            => classSyntax.AttributeLists.Any(l => l.Attributes.Any(predicate));
    }

    internal static class CodeFormatter
    {
        internal static string Format(string code)
            => CSharpSyntaxTree.ParseText(code)
                .GetRoot()
                .NormalizeWhitespace()
                .SyntaxTree
                .ToString();
    }

    internal static class SemanticModelExtensions
    {
        internal static bool EqualsAttribute(this SemanticModel model, AttributeSyntax attributeSyntax, string attributeFullName)
        {
            var symbol = (IMethodSymbol?)model.GetSymbolInfo(attributeSyntax).Symbol;
            if (symbol == null)
                return false;

            var namedSymbol = symbol.ContainingType;
            return namedSymbol.ToDisplayString() == attributeFullName;
        }
    }
}
