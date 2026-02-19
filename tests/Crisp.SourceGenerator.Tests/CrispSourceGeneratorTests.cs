using System.Collections.Immutable;
using Crisp.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Crisp.SourceGenerator.Tests;

/// <summary>
/// CrispSourceGenerator の統合テスト。
/// Roslyn の GeneratorDriver を使用して Source Generator を駆動し、
/// 生成されたコードおよび診断情報を検証する。
///
/// テスト方針:
/// <list type="bullet">
///   <item><description>最小のインライン S式から段階的に複雑さを増す</description></item>
///   <item><description>外部 .crisp ファイルのテストも含む</description></item>
///   <item><description>エラーケース（構文エラー、ファイル未検出）の診断を検証する</description></item>
///   <item><description>生成コードが BtNode 構築式を含み、コンパイル可能であることを検証する</description></item>
/// </list>
/// </summary>
public class CrispSourceGeneratorTests
{
    // ═══════════════════════════════════════════════════════════
    //  ヘルパー
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// テスト用の Roslyn Compilation を生成する。
    /// Crisp.Runtime の BehaviorTreeAttribute と BtNode 型を含む最小限のメタデータ参照を設定する。
    /// </summary>
    private static CSharpCompilation CreateCompilation(string source, params AdditionalText[] additionalTexts)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        // .NET ランタイムの基本参照 + Crisp.Runtime
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Crisp.Runtime.BtNode).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Crisp.Runtime.BehaviorTreeAttribute).Assembly.Location),
        };

        // System.Runtime の参照を追加（netcoreapp 必須）
        var systemRuntimePath = Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!,
            "System.Runtime.dll");
        if (File.Exists(systemRuntimePath))
            references.Add(MetadataReference.CreateFromFile(systemRuntimePath));

        return CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>
    /// Generator を実行し、結果（生成ソース群 + 診断情報）を返す。
    /// </summary>
    private static GeneratorDriverRunResult RunGenerator(
        string source,
        params AdditionalText[] additionalTexts)
    {
        var compilation = CreateCompilation(source);
        var generator = new CrispSourceGenerator();

        var driver = CSharpGeneratorDriver.Create(generator)
            .AddAdditionalTexts(ImmutableArray.Create(additionalTexts));

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var outputCompilation, out var diagnostics);

        return driver.GetRunResult();
    }

    /// <summary>
    /// 生成結果から BT コード生成ファイル（メソッド名.g.cs）のソースを取得する。
    /// アクセサテーブル（Accessors.g.cs）は除外する。
    /// </summary>
    private static string? GetBtGeneratedSource(GeneratorDriverRunResult result, string? methodName = null)
    {
        foreach (var tree in result.GeneratedTrees)
        {
            var path = tree.FilePath;
            if (path.Contains("Accessors.g.cs")) continue;
            if (methodName == null || path.Contains($"{methodName}.g.cs"))
                return tree.GetText().ToString();
        }
        return null;
    }

    /// <summary>
    /// 生成結果から BT コード生成ファイルの数を返す（アクセサテーブルを除く）。
    /// </summary>
    private static int GetBtGeneratedCount(GeneratorDriverRunResult result)
    {
        return result.GeneratedTrees.Count(t => !t.FilePath.Contains("Accessors.g.cs"));
    }

    /// <summary>
    /// 生成結果から F6 アクセサテーブルのソースを取得する。
    /// </summary>
    private static string? GetAccessorGeneratedSource(GeneratorDriverRunResult result, string? className = null)
    {
        foreach (var tree in result.GeneratedTrees)
        {
            var path = tree.FilePath;
            if (!path.Contains("Accessors.g.cs")) continue;
            if (className == null || path.Contains($"{className}.Accessors.g.cs"))
                return tree.GetText().ToString();
        }
        return null;
    }

    /// <summary>
    /// テスト用の AdditionalText 実装。
    /// .crisp ファイルをメモリ上で模擬する。
    /// </summary>
    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly SourceText _text;

        public InMemoryAdditionalText(string path, string content)
        {
            Path = path;
            _text = SourceText.From(content);
        }

        public override string Path { get; }

        public override SourceText? GetText(System.Threading.CancellationToken cancellationToken = default)
            => _text;
    }

    // ═══════════════════════════════════════════════════════════
    //  1. 基本: インライン S式でコード生成
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void インラインS式から正常にコード生成される()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class SimpleAI
{
    public float Health { get; set; } = 100f;

    public BtStatus Patrol() => BtStatus.Success;
    public BtStatus Flee() => BtStatus.Running;

    [BehaviorTree(""(tree EnemyCombat (select (seq (check (< .Health 30.0)) (.Flee)) (.Patrol)))"")]
    public partial BtNode Build();
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, GetBtGeneratedCount(result));

        var generatedSource = GetBtGeneratedSource(result, "Build")!;
        Assert.Contains("partial class SimpleAI", generatedSource);
        Assert.Contains("public partial Crisp.Runtime.BtNode Build()", generatedSource);
        Assert.Contains("SelectorNode", generatedSource);
        Assert.Contains("SequenceNode", generatedSource);
        Assert.Contains("ConditionNode", generatedSource);
    }

    [Fact]
    public void 最小のツリーからコード生成される()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class MinimalAI
{
    public BtStatus DoNothing() => BtStatus.Success;

    [BehaviorTree(""(tree T (.DoNothing))"")]
    public partial BtNode Build();
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, GetBtGeneratedCount(result));

        var generatedSource = GetBtGeneratedSource(result, "Build")!;
        Assert.Contains("ActionNode", generatedSource);
        Assert.Contains("this.DoNothing()", generatedSource);
    }

    [Fact]
    public void 生成コードにauto_generatedヘッダーが含まれる()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class AI
{
    public BtStatus Act() => BtStatus.Success;

    [BehaviorTree(""(tree T (.Act))"")]
    public partial BtNode Build();
}";
        var result = RunGenerator(source);

        Assert.Equal(1, GetBtGeneratedCount(result));
        var generatedSource = GetBtGeneratedSource(result, "Build")!;
        Assert.Contains("auto-generated by Crisp Source Generator", generatedSource);
        Assert.Contains("#nullable enable", generatedSource);
    }

    [Fact]
    public void 生成ファイル名がクラス名とメソッド名を含む()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class EnemyAI
{
    public BtStatus Attack() => BtStatus.Success;

    [BehaviorTree(""(tree T (.Attack))"")]
    public partial BtNode CreateTree();
}";
        var result = RunGenerator(source);

        Assert.Equal(1, GetBtGeneratedCount(result));
        var btTree = result.GeneratedTrees.First(t => !t.FilePath.Contains("Accessors.g.cs"));
        Assert.Contains("EnemyAI.CreateTree.g.cs", btTree.FilePath);
    }

    // ═══════════════════════════════════════════════════════════
    //  2. 外部 .crisp ファイル
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 外部crispファイルからコード生成される()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class ExternalAI
{
    public BtStatus Patrol() => BtStatus.Success;

    [BehaviorTree(""Combat.crisp"")]
    public partial BtNode Build();
}";
        var crispFile = new InMemoryAdditionalText(
            "/project/Combat.crisp",
            "(tree Combat (.Patrol))");

        var result = RunGenerator(source, crispFile);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, GetBtGeneratedCount(result));

        var generatedSource = GetBtGeneratedSource(result, "Build")!;
        Assert.Contains("ActionNode", generatedSource);
    }

    [Fact]
    public void 存在しない外部ファイルでCRISP0011診断が報告される()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class MissingFileAI
{
    [BehaviorTree(""NotExist.crisp"")]
    public partial BtNode Build();
}";
        var result = RunGenerator(source);

        Assert.Equal(0, GetBtGeneratedCount(result));
        Assert.Contains(result.Diagnostics, d => d.Id == "CRISP0011");
    }

    // ═══════════════════════════════════════════════════════════
    //  3. エラーケース
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 構文エラーのあるDSLでCRISP0001診断が報告される()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class BadSyntaxAI
{
    [BehaviorTree(""(tree T (select"")]
    public partial BtNode Build();
}";
        var result = RunGenerator(source);

        Assert.Equal(0, GetBtGeneratedCount(result));
        Assert.Contains(result.Diagnostics, d => d.Id == "CRISP0001");
    }

    [Fact]
    public void 属性なしのメソッドではコード生成されない()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class NoAttrAI
{
    public BtStatus Patrol() => BtStatus.Success;
    public partial BtNode Build();
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.GeneratedTrees);
    }

    // ═══════════════════════════════════════════════════════════
    //  4. 複雑なパターン
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void デコレータを含むツリーが正常に生成される()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class DecoratorAI
{
    public bool IsAlive { get; set; } = true;
    public bool IsEnemyVisible { get; set; }
    public float Health { get; set; } = 100f;

    public BtStatus Attack() => BtStatus.Success;
    public BtStatus Flee() => BtStatus.Running;
    public BtStatus Patrol() => BtStatus.Success;

    [BehaviorTree(@""(tree T
        (guard .IsAlive
            (select
                (seq
                    (check (and .IsEnemyVisible (< .Health 30.0)))
                    (.Flee))
                (.Patrol))))"")]
    public partial BtNode Build();
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, GetBtGeneratedCount(result));

        var generatedSource = GetBtGeneratedSource(result, "Build")!;
        Assert.Contains("GuardNode", generatedSource);
        Assert.Contains("SelectorNode", generatedSource);
        Assert.Contains("this.IsAlive", generatedSource);
        Assert.Contains("this.IsEnemyVisible", generatedSource);
    }

    [Fact]
    public void 複数のBehaviorTreeメソッドからそれぞれコード生成される()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class MultiAI
{
    public BtStatus Patrol() => BtStatus.Success;
    public BtStatus Attack() => BtStatus.Success;

    [BehaviorTree(""(tree T1 (.Patrol))"")]
    public partial BtNode BuildPatrol();

    [BehaviorTree(""(tree T2 (.Attack))"")]
    public partial BtNode BuildAttack();
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, GetBtGeneratedCount(result));
    }

    [Fact]
    public void Parallelノードを含むツリーが正常に生成される()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class ParallelAI
{
    public BtStatus MoveForward() => BtStatus.Running;
    public BtStatus PlayAnimation() => BtStatus.Running;

    [BehaviorTree(""(tree T (parallel :any (.MoveForward) (.PlayAnimation)))"")]
    public partial BtNode Build();
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, GetBtGeneratedCount(result));

        var generatedSource = GetBtGeneratedSource(result, "Build")!;
        Assert.Contains("ParallelNode", generatedSource);
    }

    // ═══════════════════════════════════════════════════════════
    //  5. 名前空間とクラス構造
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 正しい名前空間とクラス名で生成される()
    {
        var source = @"
using Crisp.Runtime;

namespace MyGame.AI.Enemies;

public partial class BossAI
{
    public BtStatus Rage() => BtStatus.Success;

    [BehaviorTree(""(tree T (.Rage))"")]
    public partial BtNode Build();
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, GetBtGeneratedCount(result));

        var generatedSource = GetBtGeneratedSource(result, "Build")!;
        Assert.Contains("namespace MyGame.AI.Enemies;", generatedSource);
        Assert.Contains("partial class BossAI", generatedSource);
    }

    [Fact]
    public void Repeatデコレータが正しく生成される()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class RepeatAI
{
    public BtStatus Step() => BtStatus.Success;

    [BehaviorTree(""(tree T (repeat 3 (.Step)))"")]
    public partial BtNode Build();
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, GetBtGeneratedCount(result));

        var generatedSource = GetBtGeneratedSource(result, "Build")!;
        Assert.Contains("RepeatNode", generatedSource);
        Assert.Contains("3", generatedSource);
    }

    // ═══════════════════════════════════════════════════════════
    //  6. セマンティックエラーの診断
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 存在しないメンバー参照でCRISP0001が報告される()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class SemanticErrorAI
{
    public BtStatus Patrol() => BtStatus.Success;

    [BehaviorTree(""(tree T (check .NonExistent))"")]
    public partial BtNode Build();
}";
        var result = RunGenerator(source);

        Assert.Equal(0, GetBtGeneratedCount(result));
        Assert.Contains(result.Diagnostics, d => d.Id == "CRISP0001");
    }

    [Fact]
    public void 非bool条件のcheckでCRISP0001が報告される()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class BoolCheckAI
{
    public float Health { get; set; } = 100f;
    public BtStatus Patrol() => BtStatus.Success;

    [BehaviorTree(""(tree T (check .Health))"")]
    public partial BtNode Build();
}";
        var result = RunGenerator(source);

        Assert.Equal(0, GetBtGeneratedCount(result));
        Assert.Contains(result.Diagnostics, d => d.Id == "CRISP0001");
    }

    [Fact]
    public void 非BtStatus戻り値メソッドでCRISP0001が報告される()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class ReturnTypeAI
{
    public float GetDistance() => 0f;

    [BehaviorTree(""(tree T (.GetDistance))"")]
    public partial BtNode Build();
}";
        var result = RunGenerator(source);

        Assert.Equal(0, GetBtGeneratedCount(result));
        Assert.Contains(result.Diagnostics, d => d.Id == "CRISP0001");
    }

    [Fact]
    public void セマンティックエラー時にコードが生成されない()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class NoCodeGenAI
{
    public float Health { get; set; } = 100f;
    public BtStatus Patrol() => BtStatus.Success;

    [BehaviorTree(""(tree T (check .Health))"")]
    public partial BtNode Build();
}";
        var result = RunGenerator(source);

        // セマンティックエラーがあるため、BT コード生成はスキップされる（アクセサテーブルは生成される）
        Assert.Equal(0, GetBtGeneratedCount(result));
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void 引数不一致のメソッド呼び出しでCRISP0001が報告される()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public class Entity { public float X { get; set; } }

public partial class ArgMismatchAI
{
    public Entity Target { get; set; } = new();
    public BtStatus Attack(Entity target) => BtStatus.Success;

    [BehaviorTree(""(tree T (.Attack))"")]
    public partial BtNode Build();
}";
        var result = RunGenerator(source);

        Assert.Equal(0, GetBtGeneratedCount(result));
        Assert.Contains(result.Diagnostics, d => d.Id == "CRISP0001");
    }

    [Fact]
    public void 複数のセマンティックエラーが全て報告される()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class MultiErrorAI
{
    public float Health { get; set; } = 100f;
    public BtStatus Patrol() => BtStatus.Success;

    [BehaviorTree(""(tree T (select (check .Health) (check .NonExistent) (.Patrol)))"")]
    public partial BtNode Build();
}";
        var result = RunGenerator(source);

        Assert.Equal(0, GetBtGeneratedCount(result));
        // BS0007 (.Health は float → bool 要求) + BS0001 (.NonExistent 存在しない)
        // → 少なくとも 2 つの CRISP0001 が報告される
        var crispErrors = result.Diagnostics.Where(d => d.Id == "CRISP0001").ToList();
        Assert.True(crispErrors.Count >= 2, $"Expected at least 2 CRISP0001 diagnostics, got {crispErrors.Count}");
    }

    // ═══════════════════════════════════════════════════════════
    //  7. 外部ファイル詳細テスト（U7 AdditionalFiles マッチ）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 外部ファイル名のマッチは大文字小文字を区別しない()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class CaseAI
{
    public BtStatus Patrol() => BtStatus.Success;

    [BehaviorTree(""combat.crisp"")]
    public partial BtNode Build();
}";
        // ファイル名が大文字で登録されている
        var crispFile = new InMemoryAdditionalText(
            "/project/Combat.crisp",
            "(tree Combat (.Patrol))");

        var result = RunGenerator(source, crispFile);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, GetBtGeneratedCount(result));
    }

    [Fact]
    public void 複数のcrispファイルから正しいファイルが選択される()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class SelectFileAI
{
    public BtStatus Patrol() => BtStatus.Success;
    public BtStatus Attack() => BtStatus.Success;

    [BehaviorTree(""patrol.crisp"")]
    public partial BtNode Build();
}";
        var file1 = new InMemoryAdditionalText(
            "/project/patrol.crisp",
            "(tree Patrol (.Patrol))");
        var file2 = new InMemoryAdditionalText(
            "/project/combat.crisp",
            "(tree Combat (.Attack))");

        var result = RunGenerator(source, file1, file2);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, GetBtGeneratedCount(result));

        var generatedSource = GetBtGeneratedSource(result, "Build")!;
        Assert.Contains("this.Patrol()", generatedSource);
    }

    [Fact]
    public void 外部ファイルのセマンティックエラーでCRISP0001が報告される()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class ExtErrAI
{
    public BtStatus Patrol() => BtStatus.Success;

    [BehaviorTree(""bad.crisp"")]
    public partial BtNode Build();
}";
        var crispFile = new InMemoryAdditionalText(
            "/project/bad.crisp",
            "(tree T (check .NonExistent))");

        var result = RunGenerator(source, crispFile);

        Assert.Equal(0, GetBtGeneratedCount(result));
        Assert.Contains(result.Diagnostics, d => d.Id == "CRISP0001");
    }

    // ═══════════════════════════════════════════════════════════
    //  8. 警告がコード生成を阻止しない
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void 警告のみの場合はコード生成が続行される()
    {
        // if without else は BS0302 警告だが、エラーではないので生成は成功する
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class WarnAI
{
    public bool IsReady { get; set; } = true;
    public BtStatus Attack() => BtStatus.Success;

    [BehaviorTree(""(tree T (if .IsReady (.Attack)))"")]
    public partial BtNode Build();
}";
        var result = RunGenerator(source);

        // 警告があっても生成は成功する
        Assert.Equal(1, GetBtGeneratedCount(result));
    }

    [Fact]
    public void セマンティックエラーがない場合は正常に生成される()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class ValidSemanticAI
{
    public bool IsAlive { get; set; } = true;
    public float Health { get; set; } = 100f;

    public BtStatus Patrol() => BtStatus.Success;
    public BtStatus Flee() => BtStatus.Running;

    [BehaviorTree(@""(tree T
        (guard .IsAlive
            (select
                (seq (check (< .Health 30.0)) (.Flee))
                (.Patrol))))"")]
    public partial BtNode Build();
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, GetBtGeneratedCount(result));

        var generatedSource = GetBtGeneratedSource(result, "Build")!;
        Assert.Contains("GuardNode", generatedSource);
    }

    // ═══════════════════════════════════════════════════════════
    //  9. F6: AOT アクセサテーブル生成
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void アクセサテーブルが生成されメンバーアクセサを含む()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class AotAI
{
    public int Health { get; set; } = 100;
    public string Name { get; set; } = ""Agent"";

    public BtStatus Patrol() => BtStatus.Success;

    [BehaviorTree(""(tree T (.Patrol))"")]
    public partial BtNode Build();
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);

        var accessorSource = GetAccessorGeneratedSource(result, "AotAI")!;
        Assert.NotNull(accessorSource);
        Assert.Contains("_crispMemberAccessors", accessorSource);
        Assert.Contains("[\"Health\"]", accessorSource);
        Assert.Contains("[\"Name\"]", accessorSource);
        Assert.Contains("ctx.Health", accessorSource);
        Assert.Contains("ctx.Name", accessorSource);
    }

    [Fact]
    public void アクセサテーブルがメソッドインボーカを含む()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class MethodAotAI
{
    public BtStatus Patrol() => BtStatus.Success;
    public BtStatus Attack() => BtStatus.Success;

    [BehaviorTree(""(tree T (.Patrol))"")]
    public partial BtNode Build();
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);

        var accessorSource = GetAccessorGeneratedSource(result, "MethodAotAI")!;
        Assert.NotNull(accessorSource);
        Assert.Contains("_crispMethodInvokers", accessorSource);
        Assert.Contains("[\"Patrol\"]", accessorSource);
        Assert.Contains("[\"Attack\"]", accessorSource);
    }

    [Fact]
    public void アクセサテーブルにGetAccessorTableメソッドが生成される()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class TableMethodAI
{
    public int Health { get; set; } = 100;
    public BtStatus Act() => BtStatus.Success;

    [BehaviorTree(""(tree T (.Act))"")]
    public partial BtNode Build();
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);

        var accessorSource = GetAccessorGeneratedSource(result, "TableMethodAI")!;
        Assert.Contains("GetAccessorTable()", accessorSource);
        Assert.Contains("IAccessorTable<TableMethodAI>", accessorSource);
        Assert.Contains("new AccessorTable<TableMethodAI>", accessorSource);
    }

    [Fact]
    public void 複数メソッドの同一クラスでアクセサテーブルが1回だけ生成される()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class MultiMethodAI
{
    public BtStatus Patrol() => BtStatus.Success;
    public BtStatus Attack() => BtStatus.Success;

    [BehaviorTree(""(tree T1 (.Patrol))"")]
    public partial BtNode BuildPatrol();

    [BehaviorTree(""(tree T2 (.Attack))"")]
    public partial BtNode BuildAttack();
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, GetBtGeneratedCount(result));

        // アクセサテーブルは1つだけ生成される
        var accessorCount = result.GeneratedTrees.Count(t => t.FilePath.Contains("Accessors.g.cs"));
        Assert.Equal(1, accessorCount);
    }

    [Fact]
    public void アクセサテーブルの名前空間が正しい()
    {
        var source = @"
using Crisp.Runtime;

namespace MyGame.AI;

public partial class NsAotAI
{
    public int Health { get; set; } = 100;
    public BtStatus Act() => BtStatus.Success;

    [BehaviorTree(""(tree T (.Act))"")]
    public partial BtNode Build();
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);

        var accessorSource = GetAccessorGeneratedSource(result, "NsAotAI")!;
        Assert.Contains("namespace MyGame.AI", accessorSource);
        Assert.Contains("partial class NsAotAI", accessorSource);
    }

    // ═══════════════════════════════════════════════════════════
    //  10. サブツリー埋め込み（BtNode 戻りメソッド）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void BtNodeを返すメソッドがActionNodeなしで直接呼び出しされる()
    {
        var source = @"
using Crisp.Runtime;

namespace TestApp;

public partial class SubtreeAI
{
    public BtNode BuildCombat() => new ActionNode(() => BtStatus.Success);
    public BtStatus Patrol() => BtStatus.Success;

    [BehaviorTree(""(tree T (select (.BuildCombat) (.Patrol)))"")]
    public partial BtNode Build();
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);

        var generatedSource = GetBtGeneratedSource(result, "Build")!;
        // BtNode 戻りは直接呼び出し（ActionNode ラッパーなし）
        Assert.Contains("this.BuildCombat()", generatedSource);
        Assert.DoesNotContain("new ActionNode(() => this.BuildCombat())", generatedSource);
        // BtStatus 戻りは通常の ActionNode ラッパー
        Assert.Contains("new ActionNode(() => this.Patrol())", generatedSource);
    }
}
