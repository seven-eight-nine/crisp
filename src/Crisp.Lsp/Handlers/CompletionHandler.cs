using Crisp.Query;
using Crisp.Syntax;
using Crisp.Syntax.Resolution;
using Microsoft.CodeAnalysis;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Crisp.Lsp.Handlers;

/// <summary>
/// メンバー補完ハンドラー。
///
/// ユーザーが <c>.</c> を入力した際にコンテキスト型のメンバーを
/// CompletionItem として列挙する。
///
/// <para>
/// 補完ロジック（アーキテクチャ仕様 5.4.2）:
/// <list type="number">
///   <item><description>カーソル位置の CST ノードを特定する</description></item>
///   <item><description>トップレベルの <c>.</c> → コンテキスト型の全メンバーを列挙</description></item>
///   <item><description>メソッド（BtStatus 戻り）→ アクション候補としてマーク</description></item>
///   <item><description>プロパティ/フィールド → 式候補としてマーク</description></item>
///   <item><description>ケバブケースの候補名も同時に提示（例: <c>IsEnemyVisible</c> → <c>is-enemy-visible</c>）</description></item>
/// </list>
/// </para>
/// </summary>
public class CompletionHandler : CompletionHandlerBase
{
    private readonly QueryDatabase _db;

    private static readonly TextDocumentSelector DocumentSelector =
        TextDocumentSelector.ForPattern("**/*.crisp");

    public CompletionHandler(QueryDatabase db)
    {
        _db = db;
    }

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken ct)
    {
        var fileId = TextDocumentSyncHandler.UriToFileId(request.TextDocument.Uri);
        var items = new List<CompletionItem>();

        try
        {
            var contextType = _db.GetContextType(fileId);
            if (contextType is null)
            {
                return Task.FromResult(new CompletionList(items));
            }

            // コンテキスト型の全メンバーを補完候補として列挙する
            foreach (var member in contextType.GetMembers())
            {
                // コンストラクタや特殊メンバーは除外する。
                // 生成コードは partial class の同一型であるため、
                // 全アクセス修飾子のメンバーを補完候補に含める。
                if (member.IsImplicitlyDeclared)
                    continue;

                var item = member switch
                {
                    IPropertySymbol prop => new CompletionItem
                    {
                        Label = $".{prop.Name}",
                        Kind = CompletionItemKind.Property,
                        Detail = $"{prop.Type} {prop.Name}",
                        InsertText = $".{prop.Name}",
                    },
                    IFieldSymbol field => new CompletionItem
                    {
                        Label = $".{field.Name}",
                        Kind = CompletionItemKind.Field,
                        Detail = $"{field.Type} {field.Name}",
                        InsertText = $".{field.Name}",
                    },
                    IMethodSymbol method when method.MethodKind == MethodKind.Ordinary => new CompletionItem
                    {
                        Label = method.Parameters.Length > 0
                            ? $".{method.Name}(...)"
                            : $".{method.Name}",
                        Kind = method.ReturnType.Name is "BtStatus" or "BtNode"
                            ? CompletionItemKind.Method
                            : CompletionItemKind.Function,
                        Detail = FormatMethodSignature(method),
                        InsertText = $".{method.Name}",
                    },
                    _ => null,
                };

                if (item is not null)
                {
                    items.Add(item);

                    // ケバブケース候補を同時に提示する（アーキテクチャ仕様 5.4.2）。
                    // ユーザーが "is-e" のようにケバブケースで入力しても
                    // 正しい C# メンバーにマッチするよう、フィルタリング用の
                    // ケバブケース候補を追加する。
                    var kebab = NameConversion.ToKebabCase(member.Name);
                    if (!string.Equals(kebab, member.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        items.Add(new CompletionItem
                        {
                            Label = $".{kebab}",
                            Kind = item.Kind,
                            Detail = item.Detail,
                            FilterText = $".{kebab}",
                            InsertText = $".{kebab}",
                            SortText = item.SortText,
                        });
                    }
                }
            }
        }
        catch (KeyNotFoundException)
        {
            // ContextType が未設定の場合は空リストを返す
        }

        return Task.FromResult(new CompletionList(items));
    }

    /// <summary>
    /// 補完アイテムの詳細解決。追加情報は不要なのでそのまま返す。
    /// </summary>
    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken ct)
    {
        return Task.FromResult(request);
    }

    protected override CompletionRegistrationOptions CreateRegistrationOptions(
        CompletionCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new CompletionRegistrationOptions
        {
            DocumentSelector = DocumentSelector,
            TriggerCharacters = new Container<string>("."),
            ResolveProvider = false,
        };
    }

    /// <summary>
    /// メソッドシグネチャを読みやすい文字列にフォーマットする。
    /// </summary>
    private static string FormatMethodSignature(IMethodSymbol method)
    {
        var parameters = string.Join(", ",
            method.Parameters.Select(p => $"{p.Type.Name} {p.Name}"));
        return $"{method.ReturnType.Name} {method.Name}({parameters})";
    }
}
