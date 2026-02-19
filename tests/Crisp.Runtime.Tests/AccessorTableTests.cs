using System;
using System.Collections.Generic;
using Crisp.Runtime;

namespace Crisp.Runtime.Tests;

/// <summary>
/// F6: AOT Support (IAccessorTable / AccessorTable) のユニットテスト。
/// t-wada 式 TDD に基づき、アクセサテーブルの基本操作から段階的にテストする。
///
/// テスト対象:
/// <list type="bullet">
///   <item><description>AccessorTable: メンバーアクセサとメソッドインボーカの管理</description></item>
///   <item><description>HasMember / HasMethod: アクセサ存在チェック</description></item>
///   <item><description>GetMember / InvokeMethod: 値取得・メソッド呼び出し</description></item>
///   <item><description>エラーケース: 存在しないメンバー・メソッドへのアクセス</description></item>
/// </list>
/// </summary>
public class AccessorTableTests
{
    // ──────────────────────────────────────────────
    //  テスト用コンテキストクラス
    // ──────────────────────────────────────────────

    private class TestAgent
    {
        public int Health { get; set; } = 100;
        public string Name { get; set; } = "Agent";
        public bool IsAlive { get; set; } = true;

        public BtStatus Patrol() => BtStatus.Success;
        public BtStatus Attack(int damage) => BtStatus.Success;
        public void Heal(int amount) => Health += amount;
    }

    /// <summary>テスト用のアクセサテーブルを構築するヘルパー。</summary>
    private static AccessorTable<TestAgent> CreateTestTable()
    {
        var memberAccessors = new Dictionary<string, Func<TestAgent, object?>>
        {
            ["Health"] = ctx => ctx.Health,
            ["Name"] = ctx => ctx.Name,
            ["IsAlive"] = ctx => ctx.IsAlive,
        };

        var methodInvokers = new Dictionary<string, Func<TestAgent, object?[], object?>>
        {
            ["Patrol"] = (ctx, args) => ctx.Patrol(),
            ["Attack"] = (ctx, args) => ctx.Attack((int)args[0]!),
            ["Heal"] = (ctx, args) => { ctx.Heal((int)args[0]!); return null; },
        };

        return new AccessorTable<TestAgent>(memberAccessors, methodInvokers);
    }

    // ═══════════════════════════════════════════════
    //  1. 基本操作: HasMember / HasMethod
    // ═══════════════════════════════════════════════

    [Fact]
    public void 存在するメンバーでHasMemberがtrueを返す()
    {
        var table = CreateTestTable();

        Assert.True(table.HasMember("Health"));
        Assert.True(table.HasMember("Name"));
        Assert.True(table.HasMember("IsAlive"));
    }

    [Fact]
    public void 存在しないメンバーでHasMemberがfalseを返す()
    {
        var table = CreateTestTable();

        Assert.False(table.HasMember("NonExistent"));
    }

    [Fact]
    public void 存在するメソッドでHasMethodがtrueを返す()
    {
        var table = CreateTestTable();

        Assert.True(table.HasMethod("Patrol"));
        Assert.True(table.HasMethod("Attack"));
        Assert.True(table.HasMethod("Heal"));
    }

    [Fact]
    public void 存在しないメソッドでHasMethodがfalseを返す()
    {
        var table = CreateTestTable();

        Assert.False(table.HasMethod("NonExistent"));
    }

    // ═══════════════════════════════════════════════
    //  2. GetMember: 値取得
    // ═══════════════════════════════════════════════

    [Fact]
    public void GetMemberでプロパティ値を取得できる()
    {
        var table = CreateTestTable();
        var agent = new TestAgent { Health = 75 };

        var health = table.GetMember(agent, "Health");

        Assert.Equal(75, health);
    }

    [Fact]
    public void GetMemberで文字列プロパティを取得できる()
    {
        var table = CreateTestTable();
        var agent = new TestAgent { Name = "Scout" };

        var name = table.GetMember(agent, "Name");

        Assert.Equal("Scout", name);
    }

    [Fact]
    public void GetMemberでboolプロパティを取得できる()
    {
        var table = CreateTestTable();
        var agent = new TestAgent { IsAlive = false };

        var isAlive = table.GetMember(agent, "IsAlive");

        Assert.Equal(false, isAlive);
    }

    [Fact]
    public void GetMemberで存在しないメンバーはKeyNotFoundExceptionをスロー()
    {
        var table = CreateTestTable();
        var agent = new TestAgent();

        Assert.Throws<KeyNotFoundException>(() => table.GetMember(agent, "NonExistent"));
    }

    // ═══════════════════════════════════════════════
    //  3. InvokeMethod: メソッド呼び出し
    // ═══════════════════════════════════════════════

    [Fact]
    public void InvokeMethodで引数なしメソッドを呼び出せる()
    {
        var table = CreateTestTable();
        var agent = new TestAgent();

        var result = table.InvokeMethod(agent, "Patrol");

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void InvokeMethodで引数ありメソッドを呼び出せる()
    {
        var table = CreateTestTable();
        var agent = new TestAgent();

        var result = table.InvokeMethod(agent, "Attack", 10);

        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void InvokeMethodでvoid戻り値メソッドを呼び出せる()
    {
        var table = CreateTestTable();
        var agent = new TestAgent { Health = 50 };

        var result = table.InvokeMethod(agent, "Heal", 25);

        Assert.Null(result);
        Assert.Equal(75, agent.Health);
    }

    [Fact]
    public void InvokeMethodで存在しないメソッドはKeyNotFoundExceptionをスロー()
    {
        var table = CreateTestTable();
        var agent = new TestAgent();

        Assert.Throws<KeyNotFoundException>(() => table.InvokeMethod(agent, "NonExistent"));
    }

    // ═══════════════════════════════════════════════
    //  4. IAccessorTable インターフェース契約
    // ═══════════════════════════════════════════════

    [Fact]
    public void MemberAccessorsプロパティで辞書を取得できる()
    {
        var table = CreateTestTable();

        Assert.Equal(3, table.MemberAccessors.Count);
        Assert.True(table.MemberAccessors.ContainsKey("Health"));
    }

    [Fact]
    public void MethodInvokersプロパティで辞書を取得できる()
    {
        var table = CreateTestTable();

        Assert.Equal(3, table.MethodInvokers.Count);
        Assert.True(table.MethodInvokers.ContainsKey("Patrol"));
    }

    // ═══════════════════════════════════════════════
    //  5. コンストラクタのバリデーション
    // ═══════════════════════════════════════════════

    [Fact]
    public void nullのmemberAccessorsでArgumentNullExceptionをスロー()
    {
        var methodInvokers = new Dictionary<string, Func<TestAgent, object?[], object?>>();

        Assert.Throws<ArgumentNullException>(() =>
            new AccessorTable<TestAgent>(null!, methodInvokers));
    }

    [Fact]
    public void nullのmethodInvokersでArgumentNullExceptionをスロー()
    {
        var memberAccessors = new Dictionary<string, Func<TestAgent, object?>>();

        Assert.Throws<ArgumentNullException>(() =>
            new AccessorTable<TestAgent>(memberAccessors, null!));
    }

    [Fact]
    public void 空のテーブルが正しく動作する()
    {
        var table = new AccessorTable<TestAgent>(
            new Dictionary<string, Func<TestAgent, object?>>(),
            new Dictionary<string, Func<TestAgent, object?[], object?>>());

        Assert.False(table.HasMember("Health"));
        Assert.False(table.HasMethod("Patrol"));
        Assert.Equal(0, table.MemberAccessors.Count);
        Assert.Equal(0, table.MethodInvokers.Count);
    }
}
