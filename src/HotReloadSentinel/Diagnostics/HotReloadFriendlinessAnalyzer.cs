namespace HotReloadSentinel.Diagnostics;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

/// <summary>
/// Syntax-only Roslyn classifier that explains why an edit at a specific line
/// might not visibly hot-reload, and what the developer can do about it.
/// </summary>
/// <remarks>
/// Intentionally NEVER emits per-class advice for <c>MetadataUpdateHandler</c>
/// — handlers are assembly-scoped, not per-class. Use
/// <see cref="ProjectHasMetadataUpdateHandler"/> for the assembly-scope check.
/// </remarks>
public static class HotReloadFriendlinessAnalyzer
{
    /// <summary>Categorizes the surrounding syntax at a change site.</summary>
    public enum Classification
    {
        /// <summary>Change site could not be located (file missing, parse error, line out of range).</summary>
        Unknown,
        /// <summary>Inside a field or property initializer (incl. object-initializer block at field level).</summary>
        FieldOrPropertyInitializer,
        /// <summary>Inside an instance or static constructor body.</summary>
        ConstructorBody,
        /// <summary>Inside a one-shot lifecycle hook (OnApplyTemplate / OnAppearing / OnHandlerChanged / etc.).</summary>
        LifecycleHook,
        /// <summary>Inside a method named after a render/build entry point (Render, Build, BuildRenderTree, RenderContent).</summary>
        NamedRenderMethod,
        /// <summary>Anywhere else inside a method body — generic advice applies.</summary>
        OtherMethodBody,
    }

    /// <summary>Result of classifying a single change site.</summary>
    public sealed record AdviceResult(
        Classification Classification,
        string? EnclosingTypeName,
        string? EnclosingMemberName,
        IReadOnlyList<string> Advice);

    static readonly HashSet<string> LifecycleMethodNames = new(StringComparer.Ordinal)
    {
        "OnApplyTemplate",
        "OnHandlerChanged",
        "OnHandlerChanging",
        "OnAppearing",
        "OnDisappearing",
        "OnNavigatedTo",
        "OnNavigatedFrom",
        "OnSizeAllocated",
        "OnParentSet",
        "OnBindingContextChanged",
        "OnInitialized",
        "OnInitializedAsync",
        "OnAfterRender",
        "OnAfterRenderAsync",
        "OnPropertyChanged",
    };

    static readonly HashSet<string> RenderMethodNames = new(StringComparer.Ordinal)
    {
        "Render",
        "Build",
        "BuildRenderTree",
        "RenderContent",
        "OnMounted",
    };

    /// <summary>
    /// Classify a change site by file path and 1-based line number.
    /// </summary>
    public static AdviceResult Classify(string filePath, int line1)
    {
        if (!File.Exists(filePath))
            return new AdviceResult(Classification.Unknown, null, null,
                new[] { $"File not found: {filePath}" });

        string source;
        try
        {
            source = File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            return new AdviceResult(Classification.Unknown, null, null,
                new[] { $"Could not read source: {ex.Message}" });
        }

        var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
        var root = tree.GetRoot();
        var text = tree.GetText();

        if (line1 < 1 || line1 > text.Lines.Count)
            return new AdviceResult(Classification.Unknown, null, null,
                new[] { $"Line {line1} is out of range (file has {text.Lines.Count} lines)." });

        var lineSpan = text.Lines[line1 - 1].Span;
        var node = root.FindNode(lineSpan, getInnermostNodeForTie: true);
        if (node is null)
            return new AdviceResult(Classification.Unknown, null, null,
                new[] { $"No syntax node at line {line1}." });

        var (cls, member) = ClassifyNode(node);
        var typeDecl = node.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
        var typeName = typeDecl?.Identifier.ValueText;

        return new AdviceResult(cls, typeName, member, AdviceFor(cls, typeName, member));
    }

    /// <summary>
    /// Returns true if any .cs file under <paramref name="projectDir"/> contains an
    /// assembly-level <c>[MetadataUpdateHandler(...)]</c> attribute. The check is
    /// intentionally project-wide because the attribute is assembly-scoped.
    /// </summary>
    public static bool ProjectHasMetadataUpdateHandler(string projectDir)
    {
        if (!Directory.Exists(projectDir)) return false;
        foreach (var f in Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories))
        {
            // Cheap text probe before parsing — handler attribute usage is rare.
            string text;
            try { text = File.ReadAllText(f); }
            catch { continue; }
            if (text.IndexOf("MetadataUpdateHandler", StringComparison.Ordinal) < 0) continue;

            var tree = CSharpSyntaxTree.ParseText(text, path: f);
            var root = tree.GetRoot();
            foreach (var attr in root.DescendantNodes().OfType<AttributeSyntax>())
            {
                if (IsMetadataUpdateHandlerName(attr.Name) &&
                    attr.Parent is AttributeListSyntax list &&
                    list.Target?.Identifier.IsKind(SyntaxKind.AssemblyKeyword) == true)
                {
                    return true;
                }
            }
        }
        return false;
    }

    static bool IsMetadataUpdateHandlerName(NameSyntax name)
    {
        var text = name.ToString();
        return text == "MetadataUpdateHandler"
            || text.EndsWith(".MetadataUpdateHandler", StringComparison.Ordinal)
            || text == "MetadataUpdateHandlerAttribute"
            || text.EndsWith(".MetadataUpdateHandlerAttribute", StringComparison.Ordinal);
    }

    static (Classification cls, string? memberName) ClassifyNode(SyntaxNode start)
    {
        // Field / property initializer takes precedence — this is the "rude edit"
        // pattern (object-initializer assigned to a cached field).
        var initializer = start.AncestorsAndSelf().OfType<EqualsValueClauseSyntax>().FirstOrDefault();
        if (initializer is not null)
        {
            var owner = initializer.Parent;
            if (owner is VariableDeclaratorSyntax decl &&
                decl.Parent?.Parent is FieldDeclarationSyntax field)
            {
                return (Classification.FieldOrPropertyInitializer, decl.Identifier.ValueText);
            }
            if (owner is PropertyDeclarationSyntax prop)
            {
                return (Classification.FieldOrPropertyInitializer, prop.Identifier.ValueText);
            }
        }

        // Constructor body.
        var ctor = start.AncestorsAndSelf().OfType<ConstructorDeclarationSyntax>().FirstOrDefault();
        if (ctor is not null)
        {
            return (Classification.ConstructorBody, ctor.Identifier.ValueText);
        }

        // Method body — distinguish lifecycle / render / other.
        var method = start.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method is not null)
        {
            var name = method.Identifier.ValueText;
            if (LifecycleMethodNames.Contains(name))
                return (Classification.LifecycleHook, name);
            if (RenderMethodNames.Contains(name))
                return (Classification.NamedRenderMethod, name);
            return (Classification.OtherMethodBody, name);
        }

        // Could be top-level statement, accessor body, lambda, etc. Treat as Other.
        return (Classification.OtherMethodBody, null);
    }

    static IReadOnlyList<string> AdviceFor(Classification cls, string? typeName, string? member)
    {
        var t = string.IsNullOrEmpty(typeName) ? "<type>" : typeName;
        var m = string.IsNullOrEmpty(member) ? "<member>" : member;

        return cls switch
        {
            Classification.FieldOrPropertyInitializer => new[]
            {
                $"This is the classic 'rude-edit' pattern: '{m}' is a field/property initializer on '{t}'. " +
                "Hot Reload patches the IL, but the already-constructed instance keeps its original value.",
                "Option A — add an assembly-level [MetadataUpdateHandler] that re-runs the relevant initializers " +
                "on live instances of '" + t + "' (track instances via WeakReference in the type's ctor).",
                "Option B — refactor '" + t + "' to a reactive UI pattern (e.g., MauiReactor Component<T>) where " +
                "the field is rebuilt every Render() pass.",
                "Workaround for one-off iteration: re-trigger the code path that constructs '" + t + "' (e.g., reopen the popup/page).",
            },
            Classification.ConstructorBody => new[]
            {
                $"Change is inside the constructor body of '{t}'. Hot Reload updates the IL, but existing instances " +
                "have already been constructed — they will not re-run the ctor.",
                "Option A — add an assembly-level [MetadataUpdateHandler] that calls a method like " +
                "'ReapplyConstructorState()' on live instances of '" + t + "'.",
                "Option B — re-create the object (close+reopen the page or popup) to see the change.",
            },
            Classification.LifecycleHook => new[]
            {
                $"Change is inside lifecycle hook '{m}' on '{t}'. These fire once per attach/appearance and Hot Reload " +
                "will NOT re-invoke them automatically.",
                "Trigger the lifecycle event manually (navigate away and back, dismiss and re-show, rotate) to see the update.",
                "For repeated iteration, add an assembly-level [MetadataUpdateHandler] that re-invokes '" + m + "' on live instances.",
            },
            Classification.NamedRenderMethod => new[]
            {
                $"Change is inside '{m}' on '{t}', which is a render-style method. In MauiReactor / Blazor, this should " +
                "re-render automatically after Hot Reload.",
                "If you don't see the change: (1) confirm the file has a UTF-8 BOM (./scripts/check-bom.sh), " +
                "(2) confirm HotReloadSentinel.Diagnostics is wired in MauiProgram.cs, " +
                "(3) check the IDE's Hot Reload output for source-gen errors that may have suppressed the apply.",
            },
            Classification.OtherMethodBody => new[]
            {
                $"Change is inside method '{m}' on '{t}'. Hot Reload updated the method body successfully — to see the " +
                "result, trigger a code path that calls '" + m + "' again.",
            },
            _ => new[] { "Could not classify the change site; use generic Hot Reload checks (BOM, handler registration, IDE output)." },
        };
    }
}
