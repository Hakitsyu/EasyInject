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
                    predicate: static (s, _) => IsInjectDeclaration(s),
                    transform: static (g, _) => GetInjectDeclarations(g))
                .Where(v => v.Item1 != null);

            context.RegisterImplementationSourceOutput(injectDeclarations, (c, s) =>
            {
                var (classSyntax, fields, properties) = s;

                var @namespace = classSyntax.GetNamespaceDisplay();
                var modifier = classSyntax.Modifiers.FirstOrDefault().ToString();
                var keyword = classSyntax.Keyword.ToString();
                var name = classSyntax.Identifier.Value;

                var members = fields.Cast<IComplexMember>()
                    .Concat(properties.Cast<IComplexMember>());

                var constructorArguments = members
                    .Select(m => m.AsConstructorArgument())
                    .Aggregate((a, b) => a + "," + b);

                var constructorSetter = members
                    .Select(m => m.AsConstructorSetter())
                    .Aggregate((a, b) => a + "\n" + b);

                var source = $$"""
                    namespace {{@namespace}} {
                        {{modifier}} partial {{keyword}} {{name}} {
                            {{modifier}} {{name}} ({{constructorArguments}}) {
                                {{constructorSetter}}
                            }
                        }
                    }
                """;

                c.AddSource($"{name}.g.cs", FormatCode(source));
            });
        }

        static bool IsInjectDeclaration(SyntaxNode node)
        {
            if (node is not ClassDeclarationSyntax classSyntax)
                return false;
            
            var hasFieldsWithAttributes = node.ChildNodes()
                .OfType<FieldDeclarationSyntax>()
                .Any(f => f.AttributeLists.Count > 0);

            var hasPropertiesWithAttributes = node.ChildNodes()
                .OfType<PropertyDeclarationSyntax>()
                .Any(p => p.AttributeLists.Count > 0);

            return hasFieldsWithAttributes || hasPropertiesWithAttributes;
        }


        static (ClassDeclarationSyntax, List<ComplexField>, List<ComplexProperty>) GetInjectDeclarations(GeneratorSyntaxContext context)
        {
            var classSyntax = (ClassDeclarationSyntax) context.Node;
            var fields = classSyntax.ChildNodes()
                .OfType<FieldDeclarationSyntax>()
                .Where(f => HasInjectFied(f, context.SemanticModel))
                .Select(f => f.GetComplex(context.SemanticModel))
                .ToList();

            var properties = classSyntax.ChildNodes()
                .OfType<PropertyDeclarationSyntax>()
                .Where(p => HasInjectProperty(p, context.SemanticModel))
                .Select(p => p.GetComplex(context.SemanticModel))
                .ToList();

            return fields.Any() || properties.Any() ? (classSyntax, fields, properties) : (null, null, null); 
        }

        static bool HasInjectFied(FieldDeclarationSyntax syntax, SemanticModel model)
            => syntax.AttributeLists.Any(s => s.Attributes.Any(a => IsInjectAttribute(a, model)));

        static bool HasInjectProperty(PropertyDeclarationSyntax syntax, SemanticModel model)
            => syntax.AttributeLists.Any(s => s.Attributes.Any(a => IsInjectAttribute(a, model)));

        static bool IsInjectAttribute(AttributeSyntax attribute, SemanticModel model)
        {
            var symbol = (IMethodSymbol)model.GetSymbolInfo(attribute).Symbol;
            if (symbol == null)
                return false;

            var namedSymbol = symbol.ContainingType;
            return namedSymbol.ToDisplayString() == InjectAttributeFullName;
        }

        static string FormatCode(string str)
            => CSharpSyntaxTree.ParseText(str)
                .GetRoot()
                .NormalizeWhitespace()
                .SyntaxTree
                .ToString();

        const string InjectAttributeFullName = "EasyInject.NET.Attributes.InjectAttribute";
    }

    interface IComplexMember
    {
        string AsConstructorArgument();
        string AsConstructorSetter();
    }

    internal record ComplexField : IComplexMember
    {
        public FieldDeclarationSyntax Syntax { get; init; }
        public IFieldSymbol Symbol { get; init; }

        private string GetDisplayType()
            => Symbol.Type.ToDisplayString();

        private string GetDisplayName()
            => Syntax.Declaration.Variables.FirstOrDefault().Identifier.ValueText;

        public string AsConstructorArgument()
            => $"{GetDisplayType()} {GetDisplayName()}";

        public string AsConstructorSetter()
            => $"this.{GetDisplayName()} = {GetDisplayName()};";
    }

    internal record ComplexProperty : IComplexMember
    {
        public PropertyDeclarationSyntax Syntax { get; init; }
        public IPropertySymbol Symbol { get; init; }

        private string GetDisplayType()
            => Symbol.Type.ToDisplayString();

        private string GetDisplayName()
            => Syntax.Identifier.ValueText;

        public string AsConstructorArgument()
            => $"{GetDisplayType()} {GetDisplayName()}";

        public string AsConstructorSetter()
            => $"this.{GetDisplayName()} = {GetDisplayName()};";
    }

    internal static class ComplexExtensions
    {
        internal static ComplexField? GetComplex(this FieldDeclarationSyntax syntax, SemanticModel model)
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

        internal static ComplexProperty? GetComplex(this PropertyDeclarationSyntax syntax, SemanticModel model)
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

    internal static class ClassExtensions
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
    }
}
