using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace MultiLineStringAnalyzer
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MultiLineStringAnalyzerCodeFixProvider)), Shared]
    public class MultiLineStringAnalyzerCodeFixProvider : CodeFixProvider
    {
        private const string title = "Use Environment.NewLine explicitly";

        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(MultiLineStringAnalyzerAnalyzer.DiagnosticId); }
        }

        public sealed override FixAllProvider GetFixAllProvider()
        {
            // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/FixAllProvider.md for more information on Fix All Providers
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            var node = root.FindToken(diagnosticSpan.Start).Parent.Parent;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: title,
                    createChangedDocument: c => AddEnvironmentDotNewLineAsync(context.Document, node, c),
                    equivalenceKey: title),
                diagnostic);
        }

        private async Task<Document> AddEnvironmentDotNewLineAsync(Document document, SyntaxNode equalsSyntax, CancellationToken cancellationToken)
        {
            var nodeWithEmbeddedCrLf = ((EqualsValueClauseSyntax)equalsSyntax).Value;

            var oldText = ((LiteralExpressionSyntax)nodeWithEmbeddedCrLf).Token.ValueText;

            var textLines = oldText.Split(new []{"\r\n"}, StringSplitOptions.None);

            LiteralExpressionSyntax GetLine(int index)
            {
                var line = SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(textLines[index]));

                if (index < textLines.Length - 1)
                {
                    line = line.WithTrailingTrivia(SyntaxFactory.EndOfLine(Environment.NewLine));
                }

                return line;
            }

            ExpressionSyntax AddLineRecursive(int index)
            {
                if (index > 0)
                {
                    var environment = SyntaxFactory.IdentifierName("Environment");
                    var newLine = SyntaxFactory.IdentifierName("NewLine");
                    var envNewLine = SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, environment, newLine);

                    var newLineExpression = SyntaxFactory.BinaryExpression(SyntaxKind.AddExpression, envNewLine, SyntaxFactory.Token(SyntaxKind.PlusToken), GetLine(index));
                    
                    return SyntaxFactory.BinaryExpression(SyntaxKind.AddExpression, AddLineRecursive(index -1),SyntaxFactory.Token(SyntaxKind.PlusToken), newLineExpression);
                }
                else
                {
                    return GetLine(index);
                }
            }

            ExpressionSyntax addExpression = AddLineRecursive(textLines.Length - 1);

            var oldRoot = await document.GetSyntaxRootAsync(cancellationToken);
            var newRoot = oldRoot.ReplaceNode(nodeWithEmbeddedCrLf, addExpression);

            return document.WithSyntaxRoot(newRoot);
        }
}
}
