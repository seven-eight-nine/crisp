using System.Threading;
using Crisp.Runtime;
using Crisp.Runtime.Nodes;

namespace Crisp.Runtime.Tests;

/// <summary>
/// F7: DebugProxyNode と F8: AsyncActionNode の拡張ノードテスト。
/// t-wada 式: 最小のケースから段階的に複雑さを増す。
/// </summary>
public class ExtensionNodeTests
{
    private static readonly TickContext Ctx = new(DeltaTime: 0.016f);

    // ══════════════════════════════════════════════════
    //  テストヘルパー: スタブ・モック
    // ══════════════════════════════════════════════════

    /// <summary>指定した BtStatus を返すスタブノード。</summary>
    private class StubNode : BtNode
    {
        private readonly BtStatus _status;
        public int TickCount { get; private set; }
        public int ResetCount { get; private set; }
        public int AbortCount { get; private set; }

        public StubNode(BtStatus status) => _status = status;

        public override BtStatus Tick(TickContext ctx)
        {
            TickCount++;
            return _status;
        }

        public override void Reset()
        {
            ResetCount++;
        }

        public override void Abort()
        {
            AbortCount++;
        }
    }

    /// <summary>IDebugSink のモック。呼び出し履歴を記録する。</summary>
    private class MockDebugSink : IDebugSink
    {
        public List<(int NodeId, TickContext Ctx)> BeforeTickCalls { get; } = new();
        public List<(int NodeId, BtStatus Status, TickContext Ctx)> AfterTickCalls { get; } = new();
        public List<(int NodeId, string Expr, object? Value)> ExprCalls { get; } = new();
        public List<int> AbortCalls { get; } = new();
        public List<int> ShouldBreakCalls { get; } = new();
        public int WaitForContinueCount { get; private set; }

        /// <summary>ブレークポイントが有効なノードIDの集合。</summary>
        public HashSet<int> Breakpoints { get; } = new();

        public void OnBeforeTick(int nodeId, TickContext ctx)
            => BeforeTickCalls.Add((nodeId, ctx));

        public void OnAfterTick(int nodeId, BtStatus status, TickContext ctx)
            => AfterTickCalls.Add((nodeId, status, ctx));

        public void OnExprEvaluated(int nodeId, string expr, object? value)
            => ExprCalls.Add((nodeId, expr, value));

        public void OnAbort(int nodeId)
            => AbortCalls.Add(nodeId);

        public bool ShouldBreak(int nodeId)
        {
            ShouldBreakCalls.Add(nodeId);
            return Breakpoints.Contains(nodeId);
        }

        public void WaitForContinue()
            => WaitForContinueCount++;
    }

    /// <summary>IAsyncOperation のモック。完了状態と結果を制御可能。</summary>
    private class MockAsyncOperation : IAsyncOperation
    {
        public bool IsCompleted { get; set; }
        public BtStatus Result { get; set; } = BtStatus.Success;
        public bool CancelCalled { get; private set; }

        public BtStatus GetResult() => Result;

        public void Cancel() => CancelCalled = true;
    }

    // ═══════════════════════════════════════════════════
    //  F7: DebugProxyNode
    // ═══════════════════════════════════════════════════

    [Fact]
    public void DebugProxy_内部ノードのTickをそのまま転送する()
    {
        // Arrange
        var inner = new StubNode(BtStatus.Success);
        var sink = new MockDebugSink();
        var proxy = new DebugProxyNode(inner, nodeId: 42, sink);

        // Act
        var result = proxy.Tick(Ctx);

        // Assert
        Assert.Equal(BtStatus.Success, result);
        Assert.Equal(1, inner.TickCount);
    }

    [Fact]
    public void DebugProxy_Running状態もそのまま転送する()
    {
        // Arrange
        var inner = new StubNode(BtStatus.Running);
        var sink = new MockDebugSink();
        var proxy = new DebugProxyNode(inner, nodeId: 1, sink);

        // Act
        var result = proxy.Tick(Ctx);

        // Assert
        Assert.Equal(BtStatus.Running, result);
    }

    [Fact]
    public void DebugProxy_Failure状態もそのまま転送する()
    {
        // Arrange
        var inner = new StubNode(BtStatus.Failure);
        var sink = new MockDebugSink();
        var proxy = new DebugProxyNode(inner, nodeId: 1, sink);

        // Act
        var result = proxy.Tick(Ctx);

        // Assert
        Assert.Equal(BtStatus.Failure, result);
    }

    [Fact]
    public void DebugProxy_OnBeforeTickが正しいnodeIdとctxで呼ばれる()
    {
        // Arrange
        var inner = new StubNode(BtStatus.Success);
        var sink = new MockDebugSink();
        var proxy = new DebugProxyNode(inner, nodeId: 99, sink);
        var ctx = new TickContext(DeltaTime: 0.033f, FrameIndex: 7);

        // Act
        proxy.Tick(ctx);

        // Assert
        Assert.Single(sink.BeforeTickCalls);
        Assert.Equal(99, sink.BeforeTickCalls[0].NodeId);
        Assert.Equal(0.033f, sink.BeforeTickCalls[0].Ctx.DeltaTime);
        Assert.Equal(7, sink.BeforeTickCalls[0].Ctx.FrameIndex);
    }

    [Fact]
    public void DebugProxy_OnAfterTickが正しいnodeIdとstatusとctxで呼ばれる()
    {
        // Arrange
        var inner = new StubNode(BtStatus.Failure);
        var sink = new MockDebugSink();
        var proxy = new DebugProxyNode(inner, nodeId: 5, sink);

        // Act
        proxy.Tick(Ctx);

        // Assert
        Assert.Single(sink.AfterTickCalls);
        Assert.Equal(5, sink.AfterTickCalls[0].NodeId);
        Assert.Equal(BtStatus.Failure, sink.AfterTickCalls[0].Status);
    }

    [Fact]
    public void DebugProxy_OnBeforeTickがTick前にOnAfterTickがTick後に呼ばれる()
    {
        // Arrange
        var inner = new StubNode(BtStatus.Success);
        var sink = new MockDebugSink();
        var proxy = new DebugProxyNode(inner, nodeId: 1, sink);

        // Act
        proxy.Tick(Ctx);

        // Assert — Before が先、After が後
        Assert.Single(sink.BeforeTickCalls);
        Assert.Single(sink.AfterTickCalls);
    }

    [Fact]
    public void DebugProxy_Abortが内部ノードに転送されOnAbortが通知される()
    {
        // Arrange
        var inner = new StubNode(BtStatus.Success);
        var sink = new MockDebugSink();
        var proxy = new DebugProxyNode(inner, nodeId: 10, sink);

        // Act
        proxy.Abort();

        // Assert
        Assert.Equal(1, inner.AbortCount);
        Assert.Single(sink.AbortCalls);
        Assert.Equal(10, sink.AbortCalls[0]);
    }

    [Fact]
    public void DebugProxy_Resetが内部ノードに転送される()
    {
        // Arrange
        var inner = new StubNode(BtStatus.Success);
        var sink = new MockDebugSink();
        var proxy = new DebugProxyNode(inner, nodeId: 1, sink);

        // Act
        proxy.Reset();

        // Assert
        Assert.Equal(1, inner.ResetCount);
    }

    [Fact]
    public void DebugProxy_ブレークポイント未設定ならWaitForContinueは呼ばれない()
    {
        // Arrange
        var inner = new StubNode(BtStatus.Success);
        var sink = new MockDebugSink();
        // Breakpoints は空 = ShouldBreak は常に false
        var proxy = new DebugProxyNode(inner, nodeId: 1, sink);

        // Act
        proxy.Tick(Ctx);

        // Assert
        Assert.Single(sink.ShouldBreakCalls);
        Assert.Equal(0, sink.WaitForContinueCount);
    }

    [Fact]
    public void DebugProxy_ブレークポイント設定時にWaitForContinueが呼ばれる()
    {
        // Arrange
        var inner = new StubNode(BtStatus.Success);
        var sink = new MockDebugSink();
        sink.Breakpoints.Add(20);
        var proxy = new DebugProxyNode(inner, nodeId: 20, sink);

        // Act
        proxy.Tick(Ctx);

        // Assert
        Assert.Single(sink.ShouldBreakCalls);
        Assert.Equal(1, sink.WaitForContinueCount);
        // ブレークポイント後も内部ノードは評価される
        Assert.Equal(1, inner.TickCount);
    }

    [Fact]
    public void DebugProxy_異なるnodeIdのブレークポイントには反応しない()
    {
        // Arrange
        var inner = new StubNode(BtStatus.Success);
        var sink = new MockDebugSink();
        sink.Breakpoints.Add(99); // nodeId=99 にブレークポイント
        var proxy = new DebugProxyNode(inner, nodeId: 1, sink); // nodeId=1

        // Act
        proxy.Tick(Ctx);

        // Assert
        Assert.Equal(0, sink.WaitForContinueCount);
    }

    // ═══════════════════════════════════════════════════
    //  F8: AsyncActionNode
    // ═══════════════════════════════════════════════════

    [Fact]
    public void AsyncAction_未完了のOperationでRunningを返す()
    {
        // Arrange
        var op = new MockAsyncOperation { IsCompleted = false };
        var node = new AsyncActionNode(_ => op);

        // Act
        var result = node.Tick(Ctx);

        // Assert
        Assert.Equal(BtStatus.Running, result);
    }

    [Fact]
    public void AsyncAction_完了したOperationでSuccessを返す()
    {
        // Arrange
        var op = new MockAsyncOperation { IsCompleted = true, Result = BtStatus.Success };
        var node = new AsyncActionNode(_ => op);

        // Act
        var result = node.Tick(Ctx);

        // Assert
        Assert.Equal(BtStatus.Success, result);
    }

    [Fact]
    public void AsyncAction_完了したOperationでFailureを返す()
    {
        // Arrange
        var op = new MockAsyncOperation { IsCompleted = true, Result = BtStatus.Failure };
        var node = new AsyncActionNode(_ => op);

        // Act
        var result = node.Tick(Ctx);

        // Assert
        Assert.Equal(BtStatus.Failure, result);
    }

    [Fact]
    public void AsyncAction_2回目のTickで完了するとその結果を返す()
    {
        // Arrange
        var op = new MockAsyncOperation { IsCompleted = false };
        var node = new AsyncActionNode(_ => op);

        // Act — 1回目: Running
        var result1 = node.Tick(Ctx);
        Assert.Equal(BtStatus.Running, result1);

        // 完了状態に変更
        op.IsCompleted = true;
        op.Result = BtStatus.Success;

        // Act — 2回目: Success
        var result2 = node.Tick(Ctx);

        // Assert
        Assert.Equal(BtStatus.Success, result2);
    }

    [Fact]
    public void AsyncAction_ファクトリにCancellationTokenが渡される()
    {
        // Arrange
        CancellationToken capturedToken = default;
        var op = new MockAsyncOperation { IsCompleted = false };
        var node = new AsyncActionNode(ct =>
        {
            capturedToken = ct;
            return op;
        });

        // Act
        node.Tick(Ctx);

        // Assert
        Assert.False(capturedToken.IsCancellationRequested);
    }

    [Fact]
    public void AsyncAction_AbortでCancellationTokenがキャンセルされる()
    {
        // Arrange
        CancellationToken capturedToken = default;
        var op = new MockAsyncOperation { IsCompleted = false };
        var node = new AsyncActionNode(ct =>
        {
            capturedToken = ct;
            return op;
        });
        node.Tick(Ctx); // 操作開始

        // Act
        node.Abort();

        // Assert
        Assert.True(capturedToken.IsCancellationRequested);
        Assert.True(op.CancelCalled);
    }

    [Fact]
    public void AsyncAction_AbortでOperationのCancelが呼ばれる()
    {
        // Arrange
        var op = new MockAsyncOperation { IsCompleted = false };
        var node = new AsyncActionNode(_ => op);
        node.Tick(Ctx);

        // Act
        node.Abort();

        // Assert
        Assert.True(op.CancelCalled);
    }

    [Fact]
    public void AsyncAction_Resetで進行中の操作がキャンセルされる()
    {
        // Arrange
        CancellationToken capturedToken = default;
        var op = new MockAsyncOperation { IsCompleted = false };
        var node = new AsyncActionNode(ct =>
        {
            capturedToken = ct;
            return op;
        });
        node.Tick(Ctx);

        // Act
        node.Reset();

        // Assert
        Assert.True(capturedToken.IsCancellationRequested);
        Assert.True(op.CancelCalled);
    }

    [Fact]
    public void AsyncAction_完了後の新しいTickで新しいOperationが生成される()
    {
        // Arrange
        var factoryCallCount = 0;
        var node = new AsyncActionNode(_ =>
        {
            factoryCallCount++;
            return new MockAsyncOperation { IsCompleted = true, Result = BtStatus.Success };
        });

        // Act — 1回目: 完了
        node.Tick(Ctx);
        // 2回目: 新しい Operation が生成される
        node.Tick(Ctx);

        // Assert
        Assert.Equal(2, factoryCallCount);
    }

    [Fact]
    public void AsyncAction_操作未開始でAbortしてもエラーにならない()
    {
        // Arrange
        var node = new AsyncActionNode(_ => new MockAsyncOperation());

        // Act & Assert — 例外が出ないこと
        node.Abort();
    }

    [Fact]
    public void AsyncAction_操作未開始でResetしてもエラーにならない()
    {
        // Arrange
        var node = new AsyncActionNode(_ => new MockAsyncOperation());

        // Act & Assert — 例外が出ないこと
        node.Reset();
    }

    [Fact]
    public void AsyncAction_Reset後のTickで新しいOperationが生成される()
    {
        // Arrange
        var factoryCallCount = 0;
        var node = new AsyncActionNode(_ =>
        {
            factoryCallCount++;
            return new MockAsyncOperation { IsCompleted = false };
        });
        node.Tick(Ctx); // 1回目の操作開始
        Assert.Equal(1, factoryCallCount);

        // Act
        node.Reset(); // 操作キャンセル
        node.Tick(Ctx); // 新しい操作開始

        // Assert
        Assert.Equal(2, factoryCallCount);
    }

    [Fact]
    public void AsyncAction_nullファクトリでArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AsyncActionNode(null!));
    }
}
