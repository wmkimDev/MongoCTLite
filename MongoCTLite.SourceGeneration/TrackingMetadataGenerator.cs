using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MongoCTLite.SourceGeneration;

[Generator(LanguageNames.CSharp)]
public sealed class TrackingMetadataGenerator : IIncrementalGenerator
{
    private const string TrackedEntityAttribute = "MongoCTLite.Tracking.MongoTrackedEntityAttribute";
    private const string IdFieldAttribute = "MongoCTLite.Tracking.MongoIdFieldAttribute";
    private const string VersionFieldAttribute = "MongoCTLite.Tracking.MongoVersionFieldAttribute";
    private const string BsonElementAttribute = "MongoDB.Bson.Serialization.Attributes.BsonElementAttribute";
    private const string BsonIdAttribute = "MongoDB.Bson.Serialization.Attributes.BsonIdAttribute";

    private static readonly DiagnosticDescriptor MissingIdDescriptor = new(
        id: "MCTL001",
        title: "Tracked entity requires an id field",
        messageFormat: "Type '{0}' is marked with [MongoTrackedEntity] but has no member annotated with [MongoIdField]",
        category: "MongoCTLite",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingVersionDescriptor = new(
        id: "MCTL002",
        title: "Tracked entity requires a version field",
        messageFormat: "Type '{0}' is marked with [MongoTrackedEntity] but has no member annotated with [MongoVersionField]",
        category: "MongoCTLite",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateIdDescriptor = new(
        id: "MCTL003",
        title: "Only one [MongoIdField] may be specified",
        messageFormat: "Type '{0}' has multiple members annotated with [MongoIdField]",
        category: "MongoCTLite",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateVersionDescriptor = new(
        id: "MCTL004",
        title: "Only one [MongoVersionField] may be specified",
        messageFormat: "Type '{0}' has multiple members annotated with [MongoVersionField]",
        category: "MongoCTLite",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidVersionTypeDescriptor = new(
        id: "MCTL005",
        title: "Version field must be an integer type",
        messageFormat: "Member '{0}' on type '{1}' must be of type long or int to be used with [MongoVersionField]",
        category: "MongoCTLite",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(IsCandidate, GetTrackingInfo)
            .Where(result => result.HasValue)
            .Select((result, _) => result.GetValueOrDefault());

        var collected = candidates.Collect();

        context.RegisterSourceOutput(collected, static (spc, results) =>
        {
            var validInfos = new List<TrackingInfo>();

            foreach (var result in results)
            {
                foreach (var diagnostic in result.Diagnostics)
                {
                    spc.ReportDiagnostic(diagnostic);
                }

                if (result.Info is TrackingInfo info)
                {
                    validInfos.Add(info);
                }
            }

            EmitMetadata(validInfos, spc);
        });
    }

    private static bool IsCandidate(SyntaxNode node, CancellationToken _) =>
        node is ClassDeclarationSyntax { AttributeLists.Count: > 0 };

    private static TrackingResult? GetTrackingInfo(GeneratorSyntaxContext context, CancellationToken ct)
    {
        if (context.Node is not ClassDeclarationSyntax classDeclaration)
            return null;

        if (context.SemanticModel.GetDeclaredSymbol(classDeclaration, ct) is not INamedTypeSymbol typeSymbol)
            return null;

        if (!HasAttribute(typeSymbol, TrackedEntityAttribute))
            return null;

        var builder = ImmutableArray.CreateBuilder<Diagnostic>();

        var idMemberResult = FindMember(typeSymbol, IdFieldAttribute, DuplicateIdDescriptor, MissingIdDescriptor, builder);
        var versionMemberResult = FindMember(typeSymbol, VersionFieldAttribute, DuplicateVersionDescriptor, MissingVersionDescriptor, builder);

        if (versionMemberResult.Member is not null && !IsSupportedVersionType(versionMemberResult.Member))
        {
            builder.Add(Diagnostic.Create(
                InvalidVersionTypeDescriptor,
                versionMemberResult.Member.Locations.FirstOrDefault(),
                versionMemberResult.Member.Name,
                typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }

        if (idMemberResult.Member is null || versionMemberResult.Member is null)
        {
            return new TrackingResult(null, builder.ToImmutable());
        }

        var info = new TrackingInfo(
            typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ResolveBsonField(idMemberResult.Member),
            ResolveBsonField(versionMemberResult.Member));

        return new TrackingResult(info, builder.ToImmutable());
    }

    private static bool HasAttribute(ISymbol symbol, string attributeName)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == attributeName)
                return true;
        }

        return false;
    }

    private static (ISymbol? Member, bool HasDuplicate) FindMember(
        INamedTypeSymbol typeSymbol,
        string attributeName,
        DiagnosticDescriptor duplicateDescriptor,
        DiagnosticDescriptor missingDescriptor,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        ISymbol? member = null;
        var members = typeSymbol.GetMembers();

        foreach (var candidate in members)
        {
            if (candidate is not IPropertySymbol and not IFieldSymbol)
                continue;

            if (!HasAttribute(candidate, attributeName))
                continue;

            if (member is not null)
            {
                diagnostics.Add(Diagnostic.Create(
                    duplicateDescriptor,
                    candidate.Locations.FirstOrDefault() ?? typeSymbol.Locations.FirstOrDefault(),
                    typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                return (null, true);
            }

            member = candidate;
        }

        if (member is null)
        {
            diagnostics.Add(Diagnostic.Create(
                missingDescriptor,
                typeSymbol.Locations.FirstOrDefault(),
                typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }

        return (member, false);
    }

    private static bool IsSupportedVersionType(ISymbol member)
    {
        var type = member switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => null
        };

        if (type is null)
            return false;

        return type.SpecialType is SpecialType.System_Int64 or SpecialType.System_Int32;
    }

    private static string ResolveBsonField(ISymbol member)
    {
        foreach (var attribute in member.GetAttributes())
        {
            var name = attribute.AttributeClass?.ToDisplayString();
            if (name == BsonElementAttribute && attribute.ConstructorArguments.Length == 1)
            {
                var arg = attribute.ConstructorArguments[0].Value;
                if (arg is string elementName && !string.IsNullOrWhiteSpace(elementName))
                    return elementName;
            }

            if (name == BsonIdAttribute)
            {
                return "_id";
            }
        }

        return member.Name;
    }

    private static void EmitMetadata(IReadOnlyList<TrackingInfo> infos, SourceProductionContext context)
    {
        if (infos.Count == 0)
            return;

        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("using System.Runtime.CompilerServices;");
        builder.AppendLine("namespace MongoCTLite.Tracking;");
        builder.AppendLine();
        builder.AppendLine("internal static class TrackingMetadataModuleInitializer");
        builder.AppendLine("{");
        builder.AppendLine("    [ModuleInitializer]");
        builder.AppendLine("    internal static void Initialize()");
        builder.AppendLine("    {");

        foreach (var info in infos)
        {
            builder.AppendLine(
                $"        TrackingMetadataRegistry.Register(typeof({info.FullyQualifiedName}), \"{Escape(info.IdField)}\", \"{Escape(info.VersionField)}\");");
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");

        context.AddSource("TrackingMetadataModuleInitializer.g.cs", builder.ToString());
    }

    private readonly struct TrackingInfo
    {
        public TrackingInfo(string fullyQualifiedName, string idField, string versionField)
        {
            FullyQualifiedName = fullyQualifiedName;
            IdField = idField;
            VersionField = versionField;
        }

        public string FullyQualifiedName { get; }
        public string IdField { get; }
        public string VersionField { get; }
    }

    private readonly struct TrackingResult
    {
        public TrackingResult(TrackingInfo? info, ImmutableArray<Diagnostic> diagnostics)
        {
            Info = info;
            Diagnostics = diagnostics;
        }

        public TrackingInfo? Info { get; }
        public ImmutableArray<Diagnostic> Diagnostics { get; }
        public bool HasValue => Info is not null || !Diagnostics.IsDefaultOrEmpty;
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
