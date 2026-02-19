using System.Collections.Immutable;
using Crisp.Query;
using Crisp.Runtime;
using Crisp.Semantics;
using Crisp.Syntax;
using Crisp.Syntax.Ast;
using Crisp.Syntax.Emitter;
using Crisp.Syntax.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using CrispDiagnosticSeverity = Crisp.Syntax.DiagnosticSeverity;

namespace Crisp.Integration.Tests;

/// <summary>
/// QueryDatabase を使った end-to-end 統合テスト（アーキテクチャ仕様 Section 10）。
///
/// PipelineIntegrationTests（CompilationPipeline 直接呼び出し）とは異なり、
/// <see cref="QueryDatabase"/> + 実際の <see cref="Compilation"/> を経由して
/// 名前解決・型検査を含む完全なセマンティック分析を検証する。
///
/// <para>
/// テスト範囲:
/// <list type="number">
///   <item><description>Pattern 1: 最小構造 — QueryDatabase 経由で DSL → IR → C# の完全パイプライン</description></item>
///   <item><description>Pattern 2: 全デコレータ — セマンティック分析付きで全ノード型を検証</description></item>
///   <item><description>Pattern 3: 式と型推論 — 実 Roslyn 型による名前解決・型推論を検証</description></item>
///   <item><description>Pattern 4: パラレル — ポリシーとメソッド引数の型検査を検証</description></item>
///   <item><description>Pattern 5b: セマンティックエラー — 名前未解決・型不一致等のエラー診断を検証</description></item>
///   <item><description>増分計算 — ソース変更時のキャッシュ無効化を検証</description></item>
/// </list>
/// </para>
///
/// <para>
/// t-wada 式 TDD: 最小の正常系テストから段階的にエラーケースへ複雑さを増す。
/// </para>
/// </summary>
public class QueryDatabaseIntegrationTests
{
    // ═══════════════════════════════════════════════════════════
    //  ヘルパー
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// テスト用の Roslyn Compilation を生成する。
    /// コンテキスト型の C# ソースコードを受け取り、必要な参照を含む Compilation を構築する。
    /// </summary>
    /// <param name="contextSource">コンテキスト型を定義する C# ソースコード。</param>
    /// <returns>Compilation とコンテキスト型シンボルのタプル。</returns>
    private static (Compilation Compilation, INamedTypeSymbol ContextType) CreateCompilation(
        string contextSource, string typeName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(contextSource);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(BtNode).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(BtStatus).Assembly.Location),
        };

        // System.Runtime の参照を追加（.NET Core 必須）
        var systemRuntimePath = Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!,
            "System.Runtime.dll");
        if (File.Exists(systemRuntimePath))
            references.Add(MetadataReference.CreateFromFile(systemRuntimePath));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var contextType = compilation.GetTypeByMetadataName(typeName)!;
        return (compilation, contextType);
    }

    /// <summary>
    /// QueryDatabase にソースとコンテキストを設定して返す。
    /// </summary>
    private static (QueryDatabase Db, FileId FileId) SetupDatabase(
        string crispSource, string contextCSharp, string typeName)
    {
        var db = new QueryDatabase();
        var fileId = FileId.ForInline(typeName, "Build");

        var (compilation, contextType) = CreateCompilation(contextCSharp, typeName);

        db.SetSourceText(fileId, crispSource);
        db.SetContextType(fileId, contextType);
        db.SetRoslynCompilation(fileId, compilation);

        return (db, fileId);
    }

    // ═══════════════════════════════════════════════════════════
    //  1. Pattern 1: Minimal — 最小構造の完全パイプライン
    // ═══════════════════════════════════════════════════════════

    private const string Pattern1CrispSource = """
        (tree SimpleCombat
          (select
            (seq
              (check (< .Health 30))
              (.Flee))
            (.Patrol)))
        """;

    private const string Pattern1ContextCSharp = """
        using Crisp.Runtime;
        namespace Game.AI
        {
            public partial class SimpleAI
            {
                public int Health { get; set; }
                public BtStatus Flee() => BtStatus.Success;
                public BtStatus Patrol() => BtStatus.Success;
                public partial BtNode Build();
            }
        }
        """;

    [Fact]
    public void Pattern1_セマンティックエラーがない()
    {
        var (db, fileId) = SetupDatabase(Pattern1CrispSource, Pattern1ContextCSharp, "Game.AI.SimpleAI");

        var diagnostics = db.AllDiagnostics(fileId);

        Assert.Empty(diagnostics.Where(d => d.Severity == CrispDiagnosticSeverity.Error));
    }

    [Fact]
    public void Pattern1_IRツリーが生成される()
    {
        var (db, fileId) = SetupDatabase(Pattern1CrispSource, Pattern1ContextCSharp, "Game.AI.SimpleAI");

        var irTrees = db.EmitIR(fileId);

        Assert.Single(irTrees);
        Assert.Equal("SimpleCombat", irTrees[0].Name);
    }

    [Fact]
    public void Pattern1_名前解決でHealthが解決される()
    {
        var (db, fileId) = SetupDatabase(Pattern1CrispSource, Pattern1ContextCSharp, "Game.AI.SimpleAI");

        var result = db.Resolve(fileId);

        // 名前解決エラーがないことを確認
        var resolutionErrors = db.AllDiagnostics(fileId)
            .Where(d => d.Descriptor.Id == "BS0001")
            .ToArray();
        Assert.Empty(resolutionErrors);
    }

    [Fact]
    public void Pattern1_CSharpコードが生成される()
    {
        var (db, fileId) = SetupDatabase(Pattern1CrispSource, Pattern1ContextCSharp, "Game.AI.SimpleAI");

        var irTrees = db.EmitIR(fileId);
        var csharp = CSharpEmitter.Emit(irTrees[0], "Game.AI", "SimpleAI", "Build");

        Assert.Contains("namespace Game.AI", csharp);
        Assert.Contains("partial class SimpleAI", csharp);
        Assert.Contains("SelectorNode", csharp);
        Assert.Contains("SequenceNode", csharp);
        Assert.Contains("ConditionNode", csharp);
        Assert.Contains("ActionNode", csharp);
    }

    [Fact]
    public void Pattern1_生成されたCSharpがコンパイル可能である()
    {
        var (db, fileId) = SetupDatabase(Pattern1CrispSource, Pattern1ContextCSharp, "Game.AI.SimpleAI");

        var irTrees = db.EmitIR(fileId);
        var csharp = CSharpEmitter.Emit(irTrees[0], "Game.AI", "SimpleAI", "Build");

        // 生成コードをコンパイルして検証
        var (compilation, _) = CreateCompilation(Pattern1ContextCSharp, "Game.AI.SimpleAI");
        var generatedTree = CSharpSyntaxTree.ParseText(csharp);
        var newCompilation = compilation.AddSyntaxTrees(generatedTree);

        var compileDiags = newCompilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToArray();
        Assert.Empty(compileDiags);
    }

    [Fact]
    public void Pattern1_名前解決でFleeがBtStatus戻りのメソッドとして解決される()
    {
        var (db, fileId) = SetupDatabase(Pattern1CrispSource, Pattern1ContextCSharp, "Game.AI.SimpleAI");

        var result = db.Resolve(fileId);
        var ast = db.Lower(fileId)[0];

        // Flee アクション呼び出しを AST から検索
        var fleeAction = FindNode<AstActionCall>(ast, n => n.Member.ToString() == "Flee");
        Assert.NotNull(fleeAction);

        // 名前解決で IMethodSymbol として解決されていることを確認
        var symbol = result.Model.GetSymbol(fleeAction!);
        Assert.NotNull(symbol);
        Assert.IsAssignableFrom<IMethodSymbol>(symbol);
        Assert.Equal("Flee", symbol!.Name);
    }

    [Fact]
    public void Pattern1_型推論でHealthがint型として推論される()
    {
        var (db, fileId) = SetupDatabase(Pattern1CrispSource, Pattern1ContextCSharp, "Game.AI.SimpleAI");

        var result = db.Resolve(fileId);
        var ast = db.Lower(fileId)[0];

        // Health メンバーアクセスを AST から検索
        var healthExpr = FindNode<AstMemberAccessExpr>(ast, n => n.Path.ToString() == "Health");
        Assert.NotNull(healthExpr);

        // 型推論で int 型として推論されていることを確認
        var type = result.Model.GetType(healthExpr!);
        Assert.Equal(CrispType.Int, type);
    }

    // ═══════════════════════════════════════════════════════════
    //  2. Pattern 2: Full Decorators — セマンティック分析付き
    // ═══════════════════════════════════════════════════════════

    private const string Pattern2CrispSource = """
        (tree DecoratorShowcase
          (select
            (guard .IsAlive
              (seq
                (if (and .IsEnemyVisible (not .IsFleeing))
                  (timeout 5.0
                    (.Attack .Target))
                  (.Patrol))
                (repeat 3
                  (.TauntEnemy))
                (cooldown 2.5
                  (.UseAbility))
                (while (> .Stamina 0)
                  (.Sprint))
                (invert
                  (check .IsStunned))))))
        """;

    private const string Pattern2ContextCSharp = """
        using Crisp.Runtime;
        namespace Game.AI
        {
            public class DecoratorAI
            {
                public bool IsAlive { get; set; }
                public bool IsEnemyVisible { get; set; }
                public bool IsFleeing { get; set; }
                public bool IsStunned { get; set; }
                public int Stamina { get; set; }
                public string Target { get; set; }
                public BtStatus Attack(string target) => BtStatus.Success;
                public BtStatus Patrol() => BtStatus.Success;
                public BtStatus TauntEnemy() => BtStatus.Success;
                public BtStatus UseAbility() => BtStatus.Success;
                public BtStatus Sprint() => BtStatus.Success;
            }
        }
        """;

    [Fact]
    public void Pattern2_セマンティックエラーがない()
    {
        var (db, fileId) = SetupDatabase(Pattern2CrispSource, Pattern2ContextCSharp, "Game.AI.DecoratorAI");

        var diagnostics = db.AllDiagnostics(fileId);

        Assert.Empty(diagnostics.Where(d => d.Severity == CrispDiagnosticSeverity.Error));
    }

    [Fact]
    public void Pattern2_IRに全デコレータノードが含まれる()
    {
        var (db, fileId) = SetupDatabase(Pattern2CrispSource, Pattern2ContextCSharp, "Game.AI.DecoratorAI");

        var irTrees = db.EmitIR(fileId);
        var irText = IrSerializer.Serialize(irTrees);

        Assert.Contains("ir-guard", irText);
        Assert.Contains("ir-if", irText);
        Assert.Contains("ir-timeout", irText);
        Assert.Contains("ir-repeat", irText);
        Assert.Contains("ir-cooldown", irText);
        Assert.Contains("ir-while", irText);
        Assert.Contains("ir-invert", irText);
    }

    [Fact]
    public void Pattern2_CSharpに全ランタイムノードが含まれる()
    {
        var (db, fileId) = SetupDatabase(Pattern2CrispSource, Pattern2ContextCSharp, "Game.AI.DecoratorAI");

        var irTrees = db.EmitIR(fileId);
        var csharp = CSharpEmitter.Emit(irTrees[0], "Game.AI", "DecoratorAI", "Build");

        Assert.Contains("GuardNode", csharp);
        Assert.Contains("IfNode", csharp);
        Assert.Contains("TimeoutNode", csharp);
        Assert.Contains("RepeatNode", csharp);
        Assert.Contains("CooldownNode", csharp);
        Assert.Contains("WhileNode", csharp);
        Assert.Contains("InvertNode", csharp);
    }

    [Fact]
    public void Pattern2_guard条件のIsAliveがbool型として解決される()
    {
        var (db, fileId) = SetupDatabase(Pattern2CrispSource, Pattern2ContextCSharp, "Game.AI.DecoratorAI");

        var result = db.Resolve(fileId);
        var ast = db.Lower(fileId)[0];

        // IsAlive メンバーアクセスを AST から検索
        var isAliveExpr = FindNode<AstMemberAccessExpr>(ast, n => n.Path.ToString() == "IsAlive");
        Assert.NotNull(isAliveExpr);

        // bool 型として推論されていることを確認
        var type = result.Model.GetType(isAliveExpr!);
        Assert.Equal(CrispType.Bool, type);
    }

    [Fact]
    public void Pattern2_Attackメソッドが引数つきで解決される()
    {
        var (db, fileId) = SetupDatabase(Pattern2CrispSource, Pattern2ContextCSharp, "Game.AI.DecoratorAI");

        var result = db.Resolve(fileId);
        var ast = db.Lower(fileId)[0];

        // Attack アクション呼び出しを AST から検索
        var attackAction = FindNode<AstActionCall>(ast, n => n.Member.ToString() == "Attack");
        Assert.NotNull(attackAction);

        var symbol = result.Model.GetSymbol(attackAction!);
        Assert.NotNull(symbol);
        var method = Assert.IsAssignableFrom<IMethodSymbol>(symbol);
        Assert.Single(method.Parameters);
        Assert.Equal("target", method.Parameters[0].Name);
    }

    // ═══════════════════════════════════════════════════════════
    //  3. Pattern 3: Expressions — 実 Roslyn 型による型推論
    // ═══════════════════════════════════════════════════════════

    private const string Pattern3CrispSource = """
        (tree ExpressionTest
          (select
            (seq
              (check (< (.DistanceTo .Target) (* .AttackRange 1.5)))
              (.Attack .Target))
            (seq
              (check (= .Name "Guardian"))
              (check .IsReady)
              (.Idle))))
        """;

    private const string Pattern3ContextCSharp = """
        using Crisp.Runtime;
        namespace Game.AI
        {
            public partial class ExprAI
            {
                public string Target { get; set; }
                public float AttackRange { get; set; }
                public string Name { get; set; }
                public bool IsReady { get; set; }
                public float DistanceTo(string target) => 0f;
                public BtStatus Attack(string target) => BtStatus.Success;
                public BtStatus Idle() => BtStatus.Success;
                public partial BtNode Build();
            }
        }
        """;

    [Fact]
    public void Pattern3_セマンティックエラーがない()
    {
        var (db, fileId) = SetupDatabase(Pattern3CrispSource, Pattern3ContextCSharp, "Game.AI.ExprAI");

        var diagnostics = db.AllDiagnostics(fileId);

        Assert.Empty(diagnostics.Where(d => d.Severity == CrispDiagnosticSeverity.Error));
    }

    [Fact]
    public void Pattern3_メソッド呼び出し式がIRに含まれる()
    {
        var (db, fileId) = SetupDatabase(Pattern3CrispSource, Pattern3ContextCSharp, "Game.AI.ExprAI");

        var irTrees = db.EmitIR(fileId);
        var irText = IrSerializer.Serialize(irTrees);

        Assert.Contains("ir-call", irText);
        Assert.Contains("\"DistanceTo\"", irText);
    }

    [Fact]
    public void Pattern3_生成されたCSharpがコンパイル可能である()
    {
        var (db, fileId) = SetupDatabase(Pattern3CrispSource, Pattern3ContextCSharp, "Game.AI.ExprAI");

        var irTrees = db.EmitIR(fileId);
        var csharp = CSharpEmitter.Emit(irTrees[0], "Game.AI", "ExprAI", "Build");

        var (compilation, _) = CreateCompilation(Pattern3ContextCSharp, "Game.AI.ExprAI");
        var generatedTree = CSharpSyntaxTree.ParseText(csharp);
        var newCompilation = compilation.AddSyntaxTrees(generatedTree);

        var compileDiags = newCompilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToArray();
        Assert.Empty(compileDiags);
    }

    [Fact]
    public void Pattern3_DistanceToがfloat戻りのメソッドとして型推論される()
    {
        var (db, fileId) = SetupDatabase(Pattern3CrispSource, Pattern3ContextCSharp, "Game.AI.ExprAI");

        var result = db.Resolve(fileId);
        var ast = db.Lower(fileId)[0];

        // DistanceTo のメソッド呼び出し式を検索
        var callExpr = FindNode<AstCallExpr>(ast, n => n.Member.ToString() == "DistanceTo");
        Assert.NotNull(callExpr);

        // float 型として推論されていることを確認
        var type = result.Model.GetType(callExpr!);
        Assert.Equal(CrispType.Float, type);
    }

    [Fact]
    public void Pattern3_比較式がbool型として推論される()
    {
        var (db, fileId) = SetupDatabase(Pattern3CrispSource, Pattern3ContextCSharp, "Game.AI.ExprAI");

        var result = db.Resolve(fileId);
        var ast = db.Lower(fileId)[0];

        // (< ...) の二項比較式を検索
        var binaryExpr = FindNode<AstBinaryExpr>(ast, n => n.Op == BinaryOp.Lt);
        Assert.NotNull(binaryExpr);

        // bool 型として推論されていることを確認
        var type = result.Model.GetType(binaryExpr!);
        Assert.Equal(CrispType.Bool, type);
    }

    // ═══════════════════════════════════════════════════════════
    //  4. Pattern 4: Parallel — ポリシーと引数の型検査
    // ═══════════════════════════════════════════════════════════

    private const string Pattern4CrispSource = """
        (tree ParallelTest
          (seq
            (parallel :any
              (.SearchForEnemy)
              (.CheckRadar))
            (parallel :all
              (.AimAtTarget .Target)
              (.ChargeWeapon))
            (.ExecuteAttack)))
        """;

    private const string Pattern4ContextCSharp = """
        using Crisp.Runtime;
        namespace Game.AI
        {
            public class ParallelAI
            {
                public string Target { get; set; }
                public BtStatus SearchForEnemy() => BtStatus.Success;
                public BtStatus CheckRadar() => BtStatus.Success;
                public BtStatus AimAtTarget(string target) => BtStatus.Success;
                public BtStatus ChargeWeapon() => BtStatus.Success;
                public BtStatus ExecuteAttack() => BtStatus.Success;
            }
        }
        """;

    [Fact]
    public void Pattern4_セマンティックエラーがない()
    {
        var (db, fileId) = SetupDatabase(Pattern4CrispSource, Pattern4ContextCSharp, "Game.AI.ParallelAI");

        var diagnostics = db.AllDiagnostics(fileId);

        Assert.Empty(diagnostics.Where(d => d.Severity == CrispDiagnosticSeverity.Error));
    }

    [Fact]
    public void Pattern4_IRにパラレルノードが含まれる()
    {
        var (db, fileId) = SetupDatabase(Pattern4CrispSource, Pattern4ContextCSharp, "Game.AI.ParallelAI");

        var irTrees = db.EmitIR(fileId);
        var irText = IrSerializer.Serialize(irTrees);

        Assert.Contains("ir-parallel :any", irText);
        Assert.Contains("ir-parallel :all", irText);
    }

    [Fact]
    public void Pattern4_AimAtTargetが引数つきメソッドとして解決される()
    {
        var (db, fileId) = SetupDatabase(Pattern4CrispSource, Pattern4ContextCSharp, "Game.AI.ParallelAI");

        var result = db.Resolve(fileId);
        var ast = db.Lower(fileId)[0];

        // AimAtTarget アクション呼び出しを AST から検索
        var aimAction = FindNode<AstActionCall>(ast, n => n.Member.ToString() == "AimAtTarget");
        Assert.NotNull(aimAction);

        // string 型の引数を1つ持つメソッドとして解決されていることを確認
        var symbol = result.Model.GetSymbol(aimAction!);
        Assert.NotNull(symbol);
        var method = Assert.IsAssignableFrom<IMethodSymbol>(symbol);
        Assert.Single(method.Parameters);
        Assert.Equal("String", method.Parameters[0].Type.Name);
    }

    // ═══════════════════════════════════════════════════════════
    //  5. Pattern 5b: セマンティックエラー検出
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void メンバー未解決でBS0001エラーが報告される()
    {
        const string crispSource = "(tree T (check .NonExistent))";
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test { public class AI { } }
            """;

        var (db, fileId) = SetupDatabase(crispSource, contextCSharp, "Test.AI");

        var diagnostics = db.AllDiagnostics(fileId);

        var error = diagnostics.FirstOrDefault(d => d.Descriptor.Id == "BS0001");
        Assert.NotNull(error);
        Assert.Contains("NonExistent", error.Message);
    }

    [Fact]
    public void 引数個数不一致でBS0005エラーが報告される()
    {
        const string crispSource = "(tree T (.Attack .Target .Extra))";
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public string Target { get; set; }
                    public string Extra { get; set; }
                    public BtStatus Attack(string target) => BtStatus.Success;
                }
            }
            """;

        var (db, fileId) = SetupDatabase(crispSource, contextCSharp, "Test.AI");

        var diagnostics = db.AllDiagnostics(fileId);

        var error = diagnostics.FirstOrDefault(d => d.Descriptor.Id == "BS0005");
        Assert.NotNull(error);
        Assert.Contains("Attack", error.Message);
    }

    [Fact]
    public void BtStatus以外の戻り値でBS0008エラーが報告される()
    {
        const string crispSource = "(tree T (.GetName))";
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public string GetName() => "test";
                }
            }
            """;

        var (db, fileId) = SetupDatabase(crispSource, contextCSharp, "Test.AI");

        var diagnostics = db.AllDiagnostics(fileId);

        var error = diagnostics.FirstOrDefault(d => d.Descriptor.Id == "BS0008");
        Assert.NotNull(error);
        Assert.Contains("GetName", error.Message);
    }

    [Fact]
    public void check式がbool以外でBS0007エラーが報告される()
    {
        const string crispSource = "(tree T (check .Name))";
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public string Name { get; set; }
                }
            }
            """;

        var (db, fileId) = SetupDatabase(crispSource, contextCSharp, "Test.AI");

        var diagnostics = db.AllDiagnostics(fileId);

        var error = diagnostics.FirstOrDefault(d => d.Descriptor.Id == "BS0007");
        Assert.NotNull(error);
    }

    [Fact]
    public void 引数型ミスマッチでBS0006エラーが報告される()
    {
        // Attack メソッドは int 型の引数を期待するが、string プロパティ Target を渡す
        const string crispSource = "(tree T (.Attack .Target))";
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public string Target { get; set; }
                    public BtStatus Attack(int damage) => BtStatus.Success;
                }
            }
            """;

        var (db, fileId) = SetupDatabase(crispSource, contextCSharp, "Test.AI");

        var diagnostics = db.AllDiagnostics(fileId);

        var error = diagnostics.FirstOrDefault(d => d.Descriptor.Id == "BS0006");
        Assert.NotNull(error);
        Assert.Contains("Attack", error.Message);
    }

    [Fact]
    public void セマンティックエラー時にIR生成は可能だがエラー情報を伴う()
    {
        const string crispSource = "(tree T (check .NonExistent))";
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test { public class AI { } }
            """;

        var (db, fileId) = SetupDatabase(crispSource, contextCSharp, "Test.AI");

        // エラーがあることを確認
        var diagnostics = db.AllDiagnostics(fileId);
        Assert.Contains(diagnostics, d => d.Severity == CrispDiagnosticSeverity.Error);

        // EmitIR は呼び出し可能（エラー判定は呼び出し側の責務）
        var irTrees = db.EmitIR(fileId);
        Assert.NotEmpty(irTrees);
    }

    // ═══════════════════════════════════════════════════════════
    //  5c. 警告 Diagnostic の検証
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void elseなしifでBS0302警告が報告される()
    {
        const string crispSource = """
            (tree T
              (if .IsReady
                (.Attack)))
            """;
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public bool IsReady { get; set; }
                    public BtStatus Attack() => BtStatus.Success;
                }
            }
            """;

        var (db, fileId) = SetupDatabase(crispSource, contextCSharp, "Test.AI");

        var diagnostics = db.AllDiagnostics(fileId);

        var warning = diagnostics.FirstOrDefault(d => d.Descriptor.Id == "BS0302");
        Assert.NotNull(warning);
        Assert.Equal(CrispDiagnosticSeverity.Warning, warning.Severity);
    }

    [Fact]
    public void elseありifではBS0302警告が出ない()
    {
        const string crispSource = """
            (tree T
              (if .IsReady
                (.Attack)
                (.Patrol)))
            """;
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public bool IsReady { get; set; }
                    public BtStatus Attack() => BtStatus.Success;
                    public BtStatus Patrol() => BtStatus.Success;
                }
            }
            """;

        var (db, fileId) = SetupDatabase(crispSource, contextCSharp, "Test.AI");

        var diagnostics = db.AllDiagnostics(fileId);

        Assert.DoesNotContain(diagnostics, d => d.Descriptor.Id == "BS0302");
    }

    [Fact]
    public void selector内の無条件SuccessでBS0301警告が報告される()
    {
        // (check true) は常に Success を返すため、後続の (.Patrol) は到達不能
        const string crispSource = """
            (tree T
              (select
                (check true)
                (.Patrol)))
            """;
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public BtStatus Patrol() => BtStatus.Success;
                }
            }
            """;

        var (db, fileId) = SetupDatabase(crispSource, contextCSharp, "Test.AI");

        var diagnostics = db.AllDiagnostics(fileId);

        var warning = diagnostics.FirstOrDefault(d => d.Descriptor.Id == "BS0301");
        Assert.NotNull(warning);
        Assert.Equal(CrispDiagnosticSeverity.Warning, warning.Severity);
    }

    [Fact]
    public void sequence内の無条件FailureでBS0301警告が報告される()
    {
        // (check false) は常に Failure を返すため、後続の (.Attack) は到達不能
        const string crispSource = """
            (tree T
              (seq
                (check false)
                (.Attack)))
            """;
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public BtStatus Attack() => BtStatus.Success;
                }
            }
            """;

        var (db, fileId) = SetupDatabase(crispSource, contextCSharp, "Test.AI");

        var diagnostics = db.AllDiagnostics(fileId);

        var warning = diagnostics.FirstOrDefault(d => d.Descriptor.Id == "BS0301");
        Assert.NotNull(warning);
    }

    [Fact]
    public void 到達可能なノードにはBS0301警告が出ない()
    {
        const string crispSource = """
            (tree T
              (select
                (check .IsReady)
                (.Patrol)))
            """;
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public bool IsReady { get; set; }
                    public BtStatus Patrol() => BtStatus.Success;
                }
            }
            """;

        var (db, fileId) = SetupDatabase(crispSource, contextCSharp, "Test.AI");

        var diagnostics = db.AllDiagnostics(fileId);

        Assert.DoesNotContain(diagnostics, d => d.Descriptor.Id == "BS0301");
    }

    [Fact]
    public void ObsoleteメンバーでBS0010警告が報告される()
    {
        const string crispSource = "(tree T (check .OldFlag))";
        const string contextCSharp = """
            using System;
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    [Obsolete("Use NewFlag instead")]
                    public bool OldFlag { get; set; }
                }
            }
            """;

        var (db, fileId) = SetupDatabase(crispSource, contextCSharp, "Test.AI");

        var diagnostics = db.AllDiagnostics(fileId);

        var warning = diagnostics.FirstOrDefault(d => d.Descriptor.Id == "BS0010");
        Assert.NotNull(warning);
        Assert.Contains("OldFlag", warning.Message);
        Assert.Contains("Use NewFlag instead", warning.Message);
    }

    [Fact]
    public void ObsoleteメソッドでBS0010警告が報告される()
    {
        const string crispSource = "(tree T (.OldAttack))";
        const string contextCSharp = """
            using System;
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    [Obsolete("Use Attack instead")]
                    public BtStatus OldAttack() => BtStatus.Success;
                }
            }
            """;

        var (db, fileId) = SetupDatabase(crispSource, contextCSharp, "Test.AI");

        var diagnostics = db.AllDiagnostics(fileId);

        var warning = diagnostics.FirstOrDefault(d => d.Descriptor.Id == "BS0010");
        Assert.NotNull(warning);
        Assert.Contains("OldAttack", warning.Message);
    }

    [Fact]
    public void Obsoleteでないメンバーには警告が出ない()
    {
        const string crispSource = "(tree T (check .IsReady))";
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public bool IsReady { get; set; }
                }
            }
            """;

        var (db, fileId) = SetupDatabase(crispSource, contextCSharp, "Test.AI");

        var diagnostics = db.AllDiagnostics(fileId);

        Assert.DoesNotContain(diagnostics, d => d.Descriptor.Id == "BS0010");
    }

    [Fact]
    public void 複数ツリー定義時に未使用ツリーでBS0020警告が報告される()
    {
        const string crispSource = """
            (tree Main (.Patrol))
            (tree Unused (.Attack))
            """;
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public BtStatus Patrol() => BtStatus.Success;
                    public BtStatus Attack() => BtStatus.Success;
                }
            }
            """;

        var (db, fileId) = SetupDatabase(crispSource, contextCSharp, "Test.AI");

        var diagnostics = db.AllDiagnostics(fileId);

        var warning = diagnostics.FirstOrDefault(d => d.Descriptor.Id == "BS0020");
        Assert.NotNull(warning);
        Assert.Contains("Unused", warning.Message);
    }

    [Fact]
    public void 単一ツリーではBS0020警告が出ない()
    {
        const string crispSource = "(tree Main (.Patrol))";
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public BtStatus Patrol() => BtStatus.Success;
                }
            }
            """;

        var (db, fileId) = SetupDatabase(crispSource, contextCSharp, "Test.AI");

        var diagnostics = db.AllDiagnostics(fileId);

        Assert.DoesNotContain(diagnostics, d => d.Descriptor.Id == "BS0020");
    }

    // ───────── BS0104: あいまいなメンバー名 ─────────

    [Fact]
    public void ケバブケースで異なるメンバーにマッチする場合にBS0104警告が報告される()
    {
        // "is-alive" → 候補 "IsAlive"(PascalCase) と "isAlive"(camelCase) の両方がマッチ
        const string crispSource = "(tree T (check .is-alive))";
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public bool IsAlive { get; set; }
                    public bool isAlive;
                }
            }
            """;

        var (db, fileId) = SetupDatabase(crispSource, contextCSharp, "Test.AI");

        var diagnostics = db.AllDiagnostics(fileId);

        var warning = diagnostics.FirstOrDefault(d => d.Descriptor.Id == "BS0104");
        Assert.NotNull(warning);
        Assert.Equal(CrispDiagnosticSeverity.Warning, warning!.Severity);
        Assert.Contains("is-alive", warning.Message);
        Assert.Contains("IsAlive", warning.Message);
        Assert.Contains("isAlive", warning.Message);
    }

    [Fact]
    public void メソッド名のあいまいさでBS0104警告が報告される()
    {
        // "do-attack" → 候補 "DoAttack"(PascalCase) と "doAttack"(camelCase) の両方がマッチ
        const string crispSource = "(tree T (.do-attack))";
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public BtStatus DoAttack() => BtStatus.Success;
                    public BtStatus doAttack() => BtStatus.Success;
                }
            }
            """;

        var (db, fileId) = SetupDatabase(crispSource, contextCSharp, "Test.AI");

        var diagnostics = db.AllDiagnostics(fileId);

        var warning = diagnostics.FirstOrDefault(d => d.Descriptor.Id == "BS0104");
        Assert.NotNull(warning);
        Assert.Contains("do-attack", warning!.Message);
        Assert.Contains("DoAttack", warning.Message);
        Assert.Contains("doAttack", warning.Message);
    }

    [Fact]
    public void 一意に解決される名前ではBS0104警告が出ない()
    {
        const string crispSource = "(tree T (check .is-alive))";
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public bool IsAlive { get; set; }
                }
            }
            """;

        var (db, fileId) = SetupDatabase(crispSource, contextCSharp, "Test.AI");

        var diagnostics = db.AllDiagnostics(fileId);

        Assert.DoesNotContain(diagnostics, d => d.Descriptor.Id == "BS0104");
    }

    // ═══════════════════════════════════════════════════════════
    //  6. 増分計算の検証
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void ソース変更時にリビジョンが更新される()
    {
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public BtStatus Patrol() => BtStatus.Success;
                    public BtStatus Flee() => BtStatus.Success;
                }
            }
            """;

        var db = new QueryDatabase();
        var fileId = FileId.ForInline("Test.AI", "Build");
        var (compilation, contextType) = CreateCompilation(contextCSharp, "Test.AI");

        db.SetSourceText(fileId, "(tree T (.Patrol))");
        db.SetContextType(fileId, contextType);
        db.SetRoslynCompilation(fileId, compilation);

        var rev1 = db.CurrentRevision;
        var ir1 = db.EmitIR(fileId);
        Assert.Single(ir1);

        // ソースを変更する
        db.SetSourceText(fileId, "(tree T (.Flee))");

        var rev2 = db.CurrentRevision;
        Assert.True(rev2 > rev1, "リビジョンが更新されていること");

        var ir2 = db.EmitIR(fileId);
        Assert.Single(ir2);

        // IR が変わっていることを確認
        var irText1 = IrSerializer.Serialize(ir1);
        var irText2 = IrSerializer.Serialize(ir2);
        Assert.NotEqual(irText1, irText2);
        Assert.Contains("\"Flee\"", irText2);
    }

    [Fact]
    public void 同一ソース再設定でリビジョンが変わらない()
    {
        const string source = "(tree T (.Patrol))";
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public BtStatus Patrol() => BtStatus.Success;
                }
            }
            """;

        var db = new QueryDatabase();
        var fileId = FileId.ForInline("Test.AI", "Build");
        var (compilation, contextType) = CreateCompilation(contextCSharp, "Test.AI");

        db.SetSourceText(fileId, source);
        db.SetContextType(fileId, contextType);
        db.SetRoslynCompilation(fileId, compilation);

        // 初回クエリ実行
        db.EmitIR(fileId);
        var rev1 = db.CurrentRevision;

        // 同じソースを再設定
        db.SetSourceText(fileId, source);
        var rev2 = db.CurrentRevision;

        Assert.Equal(rev1, rev2);
    }

    // ═══════════════════════════════════════════════════════════
    //  7. 複数ファイルの処理
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 複数ファイルが独立して処理される()
    {
        const string contextCSharp = """
            using Crisp.Runtime;
            namespace Test
            {
                public class AI
                {
                    public BtStatus Patrol() => BtStatus.Success;
                    public BtStatus Attack() => BtStatus.Success;
                }
            }
            """;

        var db = new QueryDatabase();
        var (compilation, contextType) = CreateCompilation(contextCSharp, "Test.AI");

        var fileId1 = FileId.ForInline("Test.AI", "Build1");
        db.SetSourceText(fileId1, "(tree T1 (.Patrol))");
        db.SetContextType(fileId1, contextType);
        db.SetRoslynCompilation(fileId1, compilation);

        var fileId2 = FileId.ForInline("Test.AI", "Build2");
        db.SetSourceText(fileId2, "(tree T2 (.Attack))");
        db.SetContextType(fileId2, contextType);
        db.SetRoslynCompilation(fileId2, compilation);

        var ir1 = db.EmitIR(fileId1);
        var ir2 = db.EmitIR(fileId2);

        Assert.Single(ir1);
        Assert.Single(ir2);
        Assert.Equal("T1", ir1[0].Name);
        Assert.Equal("T2", ir2[0].Name);
    }

    // ═══════════════════════════════════════════════════════════
    //  ヘルパー: AST ノード検索
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// AST ツリーを深さ優先で走査し、指定した型と条件に一致する最初のノードを返す。
    /// セマンティック解析結果の検証に使用する。
    /// </summary>
    /// <typeparam name="T">検索対象のノード型。</typeparam>
    /// <param name="root">走査開始のルートノード。</param>
    /// <param name="predicate">ノードの絞り込み条件。</param>
    /// <returns>条件に一致した最初のノード。見つからない場合は <c>null</c>。</returns>
    private static T? FindNode<T>(AstNode root, Func<T, bool> predicate) where T : AstNode
    {
        if (root is T typed && predicate(typed))
            return typed;

        foreach (var child in root.Children)
        {
            var found = FindNode(child, predicate);
            if (found is not null)
                return found;
        }

        return null;
    }
}
