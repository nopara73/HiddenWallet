using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace WalletWasabi.Fluent.Generators
{
	[Generator]
	public class StaticViewLocatorGenerator : ISourceGenerator
	{
		private const string AttributeText = @"// <auto-generated />
using System;

namespace WalletWasabi.Fluent
{
	[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
	public sealed class StaticViewLocatorAttribute : Attribute
	{
	}
}";

		public void Initialize(GeneratorInitializationContext context)
		{
			// System.Diagnostics.Debugger.Launch();
			context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
		}

		public void Execute(GeneratorExecutionContext context)
		{
			context.AddSource("StaticViewLocatorAttribute", SourceText.From(AttributeText, Encoding.UTF8));

			if (context.SyntaxReceiver is not SyntaxReceiver receiver)
			{
				return;
			}

			var options = (context.Compilation as CSharpCompilation)?.SyntaxTrees[0].Options as CSharpParseOptions;
			var compilation = context.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(SourceText.From(AttributeText, Encoding.UTF8), options));

			var attributeSymbol = compilation.GetTypeByMetadataName("WalletWasabi.Fluent.StaticViewLocatorAttribute");
			if (attributeSymbol is null)
			{
				return;
			}

			List<INamedTypeSymbol> namedTypeSymbolLocators = new();
			List<INamedTypeSymbol> namedTypeSymbolViewModels = new();

			foreach (var candidateClass in receiver.CandidateClasses)
			{
				var semanticModel = compilation.GetSemanticModel(candidateClass.SyntaxTree);
				var namedTypeSymbol = semanticModel.GetDeclaredSymbol(candidateClass);
				if (namedTypeSymbol is null)
				{
					continue;
				}

				var attributes = namedTypeSymbol.GetAttributes();
				if (attributes.Any(ad => ad?.AttributeClass?.Equals(attributeSymbol, SymbolEqualityComparer.Default) ?? false))
				{
					namedTypeSymbolLocators.Add(namedTypeSymbol);
				}
				else if (namedTypeSymbol.Name.EndsWith("ViewModel"))
				{
					if (!namedTypeSymbol.IsAbstract)
					{
						namedTypeSymbolViewModels.Add(namedTypeSymbol);
					}
				}
			}

			foreach (var namedTypeSymbol in namedTypeSymbolLocators)
			{
				var classSource = ProcessClass(compilation, namedTypeSymbol, namedTypeSymbolViewModels);
				if (classSource is not null)
				{
					context.AddSource($"{namedTypeSymbol.Name}_StaticViewLocator.cs", SourceText.From(classSource, Encoding.UTF8));
				}
			}
		}

		private static string? ProcessClass(Compilation compilation, INamedTypeSymbol namedTypeSymbolLocator, List<INamedTypeSymbol> namedTypeSymbolViewModels)
		{
			if (!namedTypeSymbolLocator.ContainingSymbol.Equals(namedTypeSymbolLocator.ContainingNamespace, SymbolEqualityComparer.Default))
			{
				return null;
			}

			string namespaceNameLocator = namedTypeSymbolLocator.ContainingNamespace.ToDisplayString();

			var format = new SymbolDisplayFormat(
				typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
				genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeTypeConstraints | SymbolDisplayGenericsOptions.IncludeVariance);

			string classNameLocator = namedTypeSymbolLocator.ToDisplayString(format);

			var source = new StringBuilder($@"// <auto-generated />
#nullable enable
using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace {namespaceNameLocator}
{{
    public partial class {classNameLocator}
    {{");
			_ = source.Append($@"
		private static Dictionary<Type, Func<Control>> s_views = new()
		{{
");

			var userControlViewSymbol = compilation.GetTypeByMetadataName("Avalonia.Controls.UserControl");

			foreach (var namedTypeSymbolViewModel in namedTypeSymbolViewModels)
			{
				string namespaceNameViewModel = namedTypeSymbolViewModel.ContainingNamespace.ToDisplayString();
				string classNameViewModel = $"{namespaceNameViewModel}.{namedTypeSymbolViewModel.ToDisplayString(format)}";
				string classNameView = classNameViewModel.Replace("ViewModel", "View");

				var classNameViewSymbol = compilation.GetTypeByMetadataName(classNameView);
				if (classNameViewSymbol is null || classNameViewSymbol.BaseType?.Equals(userControlViewSymbol, SymbolEqualityComparer.Default) != true)
				{
					_ = source.AppendLine($@"			[typeof({classNameViewModel})] = () => new TextBlock() {{ Text = {("\"Not Found: " + classNameView + "\"")} }},");
				}
				else
				{
					_ = source.AppendLine($@"			[typeof({classNameViewModel})] = () => new {classNameView}(),");
				}
			}

			_ = source.Append($@"		}};
	}}
}}");

			return source.ToString();
		}

		private class SyntaxReceiver : ISyntaxReceiver
		{
			public List<ClassDeclarationSyntax> CandidateClasses { get; } = new();

			public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
			{
				if (syntaxNode is ClassDeclarationSyntax classDeclarationSyntax)
				{
					CandidateClasses.Add(classDeclarationSyntax);
				}
			}
		}
	}
}