using System.Globalization;
using System.Reflection;
using Crisp.Runtime;
using Crisp.Syntax.Ast;
using Crisp.Syntax.IR;
using ParallelPolicy = Crisp.Syntax.Ast.ParallelPolicy;

namespace Crisp.Syntax.Interpreter;

/// <summary>
/// IR ノードを直接評価するインタープリタ。
/// ビヘイビアツリーの各ノードを再帰的に評価し、<see cref="BtStatus"/> を返す。
///
/// 主な用途:
/// <list type="bullet">
///   <item><description>プロトタイピング・REPL での即時実行</description></item>
///   <item><description>テストでの動作検証（C# コード生成を経由しない軽量な実行パス）</description></item>
///   <item><description>デバッグ時の IR レベル動作確認</description></item>
/// </list>
///
/// 内部実装:
/// <list type="bullet">
///   <item><description>ステートレスなノード（Selector, Sequence 等）は純粋関数として評価</description></item>
///   <item><description>ステートフルなノード（Timeout, Cooldown）は内部辞書で状態管理</description></item>
///   <item><description>コンテキストオブジェクトへのメンバーアクセス・メソッド呼び出しにはリフレクションを使用</description></item>
///   <item><description>式の評価は <see cref="EvalExpr"/> で再帰的に行い、二項・単項・論理演算は短絡評価を含む</description></item>
/// </list>
/// </summary>
public class Evaluator
{
    /// <summary>
    /// <c>this</c> に相当するコンテキストオブジェクト。
    /// メンバーアクセスおよびメソッド呼び出しの対象となる。
    /// </summary>
    private readonly object _context;

    /// <summary>
    /// ブラックボードオブジェクト（F3: Typed Blackboard）。
    /// <c>$</c> プレフィクスのメンバーアクセスの対象。null の場合はブラックボード未使用。
    /// </summary>
    private readonly object? _blackboard;

    /// <summary>現在の tick で使用する <see cref="TickContext"/>。</summary>
    private TickContext _currentCtx;

    /// <summary>
    /// タイムアウトノードの累積経過時間を保持する辞書。
    /// ノードの参照同一性でキー管理し、tick をまたいで時間を追跡する。
    /// </summary>
    private readonly Dictionary<IrNode, float> _timeoutAccumulators = new(NodeReferenceComparer.Instance);

    /// <summary>
    /// クールダウンノードの残り時間を保持する辞書。
    /// ノードの参照同一性でキー管理し、クールダウン中は Failure を返す。
    /// </summary>
    private readonly Dictionary<IrNode, float> _cooldownTimers = new(NodeReferenceComparer.Instance);

    /// <summary>
    /// 新しい <see cref="Evaluator"/> インスタンスを生成する。
    /// </summary>
    /// <param name="context">
    /// <c>this</c> に相当するコンテキストオブジェクト。
    /// メンバーアクセス（プロパティ・フィールド）およびメソッド呼び出しの対象となる。
    /// null は許容しない。
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> が null の場合。</exception>
    public Evaluator(object context, object? blackboard = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _blackboard = blackboard;
    }

    // ═══════════════════════════════════════════════════════════
    //  公開 API
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// IR ツリーを評価する。ツリー本体の全ノードを順に評価し、最後のノードの結果を返す。
    /// </summary>
    /// <param name="tree">評価対象の IR ツリー。</param>
    /// <param name="ctx">現在の tick のコンテキスト情報。DeltaTime はタイムアウト・クールダウンの時間計測に使用する。</param>
    /// <returns>ツリー本体の最後のノードの評価結果。本体が空の場合は <see cref="BtStatus.Success"/>。</returns>
    public BtStatus Tick(IrTree tree, TickContext ctx)
    {
        _currentCtx = ctx;
        var last = BtStatus.Success;
        foreach (var node in tree.Body)
            last = TickNode(node);
        return last;
    }

    /// <summary>
    /// 任意の IR ノードを評価する。ツリー以外のノードを直接評価する場合に使用する。
    /// </summary>
    /// <param name="node">評価対象の IR ノード。</param>
    /// <param name="ctx">現在の tick のコンテキスト情報。</param>
    /// <returns>ノードの評価結果。</returns>
    public BtStatus Tick(IrNode node, TickContext ctx)
    {
        _currentCtx = ctx;
        return TickNode(node);
    }

    /// <summary>
    /// 任意の IR ノードを評価する。<see cref="TickContext"/> なしの簡易版。
    /// タイムアウト・クールダウンを使用しない場合に便利。
    /// </summary>
    /// <param name="node">評価対象の IR ノード。</param>
    /// <returns>ノードの評価結果。</returns>
    public BtStatus Tick(IrNode node)
    {
        return Tick(node, default);
    }

    /// <summary>
    /// ステートフルノード（タイムアウト・クールダウン）の内部状態をリセットする。
    /// ツリーの再実行時やテストのセットアップで使用する。
    /// </summary>
    public void Reset()
    {
        _timeoutAccumulators.Clear();
        _cooldownTimers.Clear();
    }

    // ═══════════════════════════════════════════════════════════
    //  ノード評価（メインディスパッチ）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// IR ノードを種別に応じてディスパッチし評価する。
    /// </summary>
    /// <exception cref="NotSupportedException">未対応のノード型が渡された場合。</exception>
    private BtStatus TickNode(IrNode node) => node switch
    {
        IrTree tree      => TickTree(tree),
        IrSelector sel   => TickSelector(sel),
        IrSequence seq   => TickSequence(seq),
        IrParallel par   => TickParallel(par),
        IrCondition cond => EvalCondition(cond),
        IrAction act     => InvokeAction(act),
        IrGuard grd      => EvalGuard(grd),
        IrIf ifn         => EvalIf(ifn),
        IrInvert inv     => TickInvert(inv),
        IrRepeat rep     => TickRepeat(rep),
        IrTimeout tmo    => TickTimeout(tmo),
        IrCooldown cld   => TickCooldown(cld),
        IrWhile whl      => TickWhile(whl),
        IrReactive rct   => TickReactive(rct),
        IrReactiveSelect rsel => TickReactiveSelect(rsel),
        IrTreeRef tref   => TickTreeRef(tref),
        _ => throw new NotSupportedException(
            $"未対応の IR ノード型: {node.GetType().Name}")
    };

    // ═══════════════════════════════════════════════════════════
    //  複合ノード: ツリー・セレクター・シーケンス・パラレル
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// ツリーノードを評価する。本体の全ノードを順に評価し、最後の結果を返す。
    /// </summary>
    private BtStatus TickTree(IrTree tree)
    {
        var last = BtStatus.Success;
        foreach (var child in tree.Body)
            last = TickNode(child);
        return last;
    }

    /// <summary>
    /// セレクターを評価する。子を順に評価し、最初に Failure 以外を返す子の結果を返す。
    /// 全子が Failure の場合のみ Failure を返す。
    /// </summary>
    private BtStatus TickSelector(IrSelector sel)
    {
        foreach (var child in sel.Children)
        {
            var result = TickNode(child);
            if (result != BtStatus.Failure)
                return result;
        }
        return BtStatus.Failure;
    }

    /// <summary>
    /// シーケンスを評価する。子を順に評価し、全て Success なら Success を返す。
    /// いずれかの子が Success 以外を返した時点でその結果を返す。
    /// </summary>
    private BtStatus TickSequence(IrSequence seq)
    {
        foreach (var child in seq.Children)
        {
            var result = TickNode(child);
            if (result != BtStatus.Success)
                return result;
        }
        return BtStatus.Success;
    }

    /// <summary>
    /// パラレルを評価する。全子を評価し、ポリシーに応じて結果を判定する。
    ///
    /// ポリシー別の判定ロジック:
    /// <list type="bullet">
    ///   <item><description><see cref="ParallelPolicy.Any"/>: いずれかが Success → Success、いずれかが Running → Running、全て Failure → Failure</description></item>
    ///   <item><description><see cref="ParallelPolicy.All"/>: いずれかが Failure → Failure、いずれかが Running → Running、全て Success → Success</description></item>
    ///   <item><description><see cref="ParallelPolicy.N"/>: N 個以上 Success → Success、N 個到達不可能 → Failure、それ以外 → Running</description></item>
    /// </list>
    /// </summary>
    private BtStatus TickParallel(IrParallel par)
    {
        var successCount = 0;
        var failureCount = 0;
        var hasRunning = false;

        foreach (var child in par.Children)
        {
            switch (TickNode(child))
            {
                case BtStatus.Success: successCount++; break;
                case BtStatus.Failure: failureCount++; break;
                case BtStatus.Running: hasRunning = true; break;
            }
        }

        var total = par.Children.Length;

        return par.Policy switch
        {
            ParallelPolicy.Any =>
                successCount > 0 ? BtStatus.Success :
                hasRunning       ? BtStatus.Running :
                                   BtStatus.Failure,

            ParallelPolicy.All =>
                failureCount > 0 ? BtStatus.Failure :
                hasRunning       ? BtStatus.Running :
                                   BtStatus.Success,

            ParallelPolicy.N n =>
                successCount >= n.Count        ? BtStatus.Success :
                failureCount > total - n.Count ? BtStatus.Failure :
                hasRunning                     ? BtStatus.Running :
                                                 BtStatus.Failure,

            _ => throw new NotSupportedException(
                $"未対応のパラレルポリシー: {par.Policy.GetType().Name}")
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  条件・ガード・制御フロー
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 条件ノードを評価する。式が true なら Success、それ以外なら Failure。
    /// </summary>
    private BtStatus EvalCondition(IrCondition cond)
    {
        var value = EvalExpr(cond.Expr);
        return value is true ? BtStatus.Success : BtStatus.Failure;
    }

    /// <summary>
    /// ガードノードを評価する。条件が true ならボディを評価、false なら Failure。
    /// </summary>
    private BtStatus EvalGuard(IrGuard grd)
    {
        var value = EvalExpr(grd.Condition);
        return value is true ? TickNode(grd.Body) : BtStatus.Failure;
    }

    /// <summary>
    /// if ノードを評価する。条件が true なら Then 分岐、false なら Else 分岐（または Failure）。
    /// </summary>
    private BtStatus EvalIf(IrIf ifn)
    {
        var value = EvalExpr(ifn.Condition);
        if (value is true)
            return TickNode(ifn.Then);
        return ifn.Else != null ? TickNode(ifn.Else) : BtStatus.Failure;
    }

    /// <summary>
    /// 反転ノードを評価する。子の Success と Failure を反転し、Running はそのまま通す。
    /// </summary>
    private BtStatus TickInvert(IrInvert inv)
    {
        return TickNode(inv.Child) switch
        {
            BtStatus.Success => BtStatus.Failure,
            BtStatus.Failure => BtStatus.Success,
            var s => s // Running はそのまま
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  デコレータ: リピート・タイムアウト・クールダウン・While
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// リピートノードを評価する。子を指定回数繰り返す。
    /// 子が Failure または Running を返した場合はその時点で中断し、その結果を返す。
    /// 全回 Success なら Success を返す。
    /// </summary>
    private BtStatus TickRepeat(IrRepeat rep)
    {
        for (var i = 0; i < rep.Count; i++)
        {
            var result = TickNode(rep.Child);
            if (result != BtStatus.Success)
                return result;
        }
        return BtStatus.Success;
    }

    /// <summary>
    /// タイムアウトノードを評価する。子が Running を返し続ける間、経過時間を累積する。
    /// 累積時間が指定秒数を超えた場合は Failure を返す。
    /// 子が Success/Failure を返した場合はその結果を返し、タイマーをリセットする。
    /// </summary>
    private BtStatus TickTimeout(IrTimeout tmo)
    {
        // 累積時間を取得（なければ 0 で初期化）
        if (!_timeoutAccumulators.TryGetValue(tmo, out var accumulated))
            accumulated = 0f;

        // タイムアウト超過チェック（前回 tick の累積で超過済みの場合）
        if (accumulated >= tmo.Seconds)
        {
            _timeoutAccumulators.Remove(tmo);
            return BtStatus.Failure;
        }

        var result = TickNode(tmo.Child);

        if (result == BtStatus.Running)
        {
            // Running の場合は経過時間を累積
            accumulated += _currentCtx.DeltaTime;
            _timeoutAccumulators[tmo] = accumulated;

            // 累積後にタイムアウト超過していたら Failure
            if (accumulated >= tmo.Seconds)
            {
                _timeoutAccumulators.Remove(tmo);
                return BtStatus.Failure;
            }
            return BtStatus.Running;
        }

        // Success/Failure の場合はタイマーリセットして結果をそのまま返す
        _timeoutAccumulators.Remove(tmo);
        return result;
    }

    /// <summary>
    /// クールダウンノードを評価する。子が Success を返した後、指定秒数が経過するまで Failure を返す。
    /// クールダウン期間中は子を評価せずに即座に Failure を返す。
    /// クールダウン終了後は通常通り子を評価する。
    /// </summary>
    private BtStatus TickCooldown(IrCooldown cld)
    {
        // クールダウン残時間を確認
        if (_cooldownTimers.TryGetValue(cld, out var remaining))
        {
            remaining -= _currentCtx.DeltaTime;
            if (remaining > 0f)
            {
                _cooldownTimers[cld] = remaining;
                return BtStatus.Failure;
            }
            // クールダウン終了
            _cooldownTimers.Remove(cld);
        }

        var result = TickNode(cld.Child);

        // 子が Success を返したらクールダウン開始
        if (result == BtStatus.Success)
            _cooldownTimers[cld] = cld.Seconds;

        return result;
    }

    /// <summary>
    /// while ノードを評価する。条件が true の場合にボディを評価してその結果を返す。
    /// 条件が false の場合は Success を返す（ループ完了）。
    ///
    /// 注意: 単一 tick での評価のため、ボディは1回のみ評価される。
    /// Running を含むループは tick をまたいで実行される。
    /// </summary>
    private BtStatus TickWhile(IrWhile whl)
    {
        var condValue = EvalExpr(whl.Condition);
        if (condValue is not true)
            return BtStatus.Success;
        return TickNode(whl.Body);
    }

    // ═══════════════════════════════════════════════════════════
    //  リアクティブノード（F1）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// リアクティブノードの Running 追跡状態を保持する辞書。
    /// </summary>
    private readonly Dictionary<IrNode, bool> _reactiveRunning = new(NodeReferenceComparer.Instance);

    /// <summary>
    /// リアクティブセレクターノードの Running 子インデックス集合を保持する辞書。
    /// </summary>
    private readonly Dictionary<IrNode, HashSet<int>> _reactiveSelectRunning = new(NodeReferenceComparer.Instance);

    /// <summary>
    /// リアクティブノードを評価する。毎 tick 条件を再評価し、
    /// false になったら Failure を返す。
    /// </summary>
    private BtStatus TickReactive(IrReactive rct)
    {
        var condValue = EvalExpr(rct.Condition);
        if (condValue is not true)
        {
            // 条件 false → Running 状態をクリア
            _reactiveRunning.Remove(rct);
            return BtStatus.Failure;
        }

        var status = TickNode(rct.Body);
        _reactiveRunning[rct] = status == BtStatus.Running;
        return status;
    }

    /// <summary>
    /// リアクティブセレクターノードを評価する。毎 tick 先頭から再評価する。
    /// </summary>
    private BtStatus TickReactiveSelect(IrReactiveSelect rsel)
    {
        for (int i = 0; i < rsel.Children.Length; i++)
        {
            var status = TickNode(rsel.Children[i]);
            if (status != BtStatus.Failure)
                return status;
        }
        return BtStatus.Failure;
    }

    /// <summary>
    /// ツリー参照ノードを評価する。現在のツリーコレクションから参照先を検索して評価する。
    /// インタープリタの制約により、ref は同一ツリー内のサブツリーのみ対応。
    /// </summary>
    private BtStatus TickTreeRef(IrTreeRef tref)
    {
        // インタープリタでの ref 評価は、現在のツリーコレクションに依存
        // 単一ツリー評価モードでは未対応
        throw new NotSupportedException(
            $"インタープリタでの ref '{tref.TreeName}' の評価は未対応です。" +
            $"Source Generator 経由の C# コード生成を使用してください。");
    }

    // ═══════════════════════════════════════════════════════════
    //  アクション呼び出し
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// アクションノードを評価する。コンテキストオブジェクトのメソッドをリフレクションで呼び出す。
    /// メソッドは <see cref="BtStatus"/> を返す必要がある。
    /// </summary>
    /// <exception cref="MissingMethodException">メソッドが見つからない場合。</exception>
    /// <exception cref="InvalidOperationException">メソッドが BtStatus 以外を返した場合。</exception>
    private BtStatus InvokeAction(IrAction action)
    {
        var type = _context.GetType();
        var method = FindMethod(type, action.Method.Name, action.Args.Length)
            ?? throw new MissingMethodException(
                $"メソッド '{action.Method.Name}' が型 '{type.Name}' に見つかりません。");

        var args = EvalArgs(action.Args);
        var result = method.Invoke(_context, args);

        if (result is BtStatus status)
            return status;

        if (result is BtNode subtree)
            return subtree.Tick(_currentCtx);

        throw new InvalidOperationException(
            $"アクション '{action.Method.Name}' が BtStatus/BtNode 以外の値 " +
            $"({result?.GetType().Name ?? "null"}) を返しました。");
    }

    // ═══════════════════════════════════════════════════════════
    //  式の評価（メインディスパッチ）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// IR 式を評価して値を返す。式の種別に応じて適切な評価メソッドにディスパッチする。
    /// </summary>
    /// <exception cref="NotSupportedException">未対応の式ノード型が渡された場合。</exception>
    private object? EvalExpr(IrExpr expr) => expr switch
    {
        IrLiteral lit    => lit.Value,
        IrMemberLoad mem => ResolveMemberChain(mem.Ref),
        IrBinaryOp bin   => EvalBinaryOp(bin),
        IrUnaryOp un     => EvalUnaryOp(un),
        IrLogicOp log    => EvalLogicOp(log),
        IrBlackboardLoad bb => ResolveBlackboardChain(bb.Ref),
        IrCall call      => InvokeMethod(call),
        IrConvert conv   => EvalConvert(conv),
        _ => throw new NotSupportedException(
            $"未対応の IR 式ノード型: {expr.GetType().Name}")
    };

    // ═══════════════════════════════════════════════════════════
    //  メンバーアクセス（リフレクション）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// メンバーチェーンを辿って値を解決する。
    /// コンテキストオブジェクトから開始し、プロパティまたはフィールドを順に読み取る。
    ///
    /// 例: <c>MemberRef(["Target", "Position", "x"])</c> は
    /// <c>_context.Target.Position.x</c> を読み取る。
    /// </summary>
    /// <exception cref="MissingMemberException">メンバーが見つからない場合。</exception>
    private object? ResolveMemberChain(MemberRef memberRef)
    {
        object? current = _context;
        foreach (var segment in memberRef.Chain)
        {
            if (current == null) return null;

            var type = current.GetType();

            var prop = type.GetProperty(segment);
            if (prop != null)
            {
                current = prop.GetValue(current);
                continue;
            }

            var field = type.GetField(segment);
            if (field != null)
            {
                current = field.GetValue(current);
                continue;
            }

            throw new MissingMemberException(
                $"メンバー '{segment}' が型 '{type.Name}' に見つかりません。");
        }
        return current;
    }

    // ═══════════════════════════════════════════════════════════
    //  ブラックボードアクセス（F3）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// ブラックボードメンバーチェーンを辿って値を解決する。
    /// ブラックボードオブジェクトから開始し、プロパティまたはフィールドを順に読み取る。
    /// </summary>
    /// <exception cref="InvalidOperationException">ブラックボードが設定されていない場合。</exception>
    private object? ResolveBlackboardChain(MemberRef memberRef)
    {
        if (_blackboard == null)
            throw new InvalidOperationException(
                "ブラックボードアクセス '$' が使用されていますが、ブラックボードが設定されていません。");

        object? current = _blackboard;
        foreach (var segment in memberRef.Chain)
        {
            if (current == null) return null;

            var type = current.GetType();

            var prop = type.GetProperty(segment);
            if (prop != null)
            {
                current = prop.GetValue(current);
                continue;
            }

            var field = type.GetField(segment);
            if (field != null)
            {
                current = field.GetValue(current);
                continue;
            }

            throw new MissingMemberException(
                $"ブラックボードメンバー '{segment}' が型 '{type.Name}' に見つかりません。");
        }
        return current;
    }

    // ═══════════════════════════════════════════════════════════
    //  二項演算の評価
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 二項演算式を評価する。算術演算（+, -, *, /, %）と比較演算（&lt;, &gt;, &lt;=, &gt;=, =, !=）を処理する。
    /// int と float が混在する場合は float に昇格して演算する（IrConvert 経由が通常だが、フォールバックも対応）。
    /// </summary>
    private object EvalBinaryOp(IrBinaryOp bin)
    {
        var lhs = EvalExpr(bin.Lhs);
        var rhs = EvalExpr(bin.Rhs);

        return bin.Op switch
        {
            // 算術演算
            BinaryOp.Add => NumericOp(lhs, rhs, (a, b) => a + b, (a, b) => a + b),
            BinaryOp.Sub => NumericOp(lhs, rhs, (a, b) => a - b, (a, b) => a - b),
            BinaryOp.Mul => NumericOp(lhs, rhs, (a, b) => a * b, (a, b) => a * b),
            BinaryOp.Div => NumericOp(lhs, rhs, (a, b) => a / b, (a, b) => a / b),
            BinaryOp.Mod => NumericOp(lhs, rhs, (a, b) => a % b, (a, b) => a % b),

            // 比較演算
            BinaryOp.Lt => CompareOp(lhs, rhs, (a, b) => a < b, (a, b) => a < b),
            BinaryOp.Gt => CompareOp(lhs, rhs, (a, b) => a > b, (a, b) => a > b),
            BinaryOp.Le => CompareOp(lhs, rhs, (a, b) => a <= b, (a, b) => a <= b),
            BinaryOp.Ge => CompareOp(lhs, rhs, (a, b) => a >= b, (a, b) => a >= b),
            BinaryOp.Eq => (object)Equals(lhs, rhs),
            BinaryOp.Ne => (object)!Equals(lhs, rhs),

            _ => throw new NotSupportedException($"未対応の二項演算子: {bin.Op}")
        };
    }

    /// <summary>
    /// 数値演算ヘルパー。オペランドに float が含まれる場合は float 演算、そうでなければ int 演算を行う。
    /// </summary>
    private static object NumericOp(
        object? lhs, object? rhs,
        Func<int, int, int> intOp,
        Func<float, float, float> floatOp)
    {
        if (lhs is float || rhs is float)
        {
            var lv = Convert.ToSingle(lhs, CultureInfo.InvariantCulture);
            var rv = Convert.ToSingle(rhs, CultureInfo.InvariantCulture);
            return floatOp(lv, rv);
        }
        return intOp(Convert.ToInt32(lhs), Convert.ToInt32(rhs));
    }

    /// <summary>
    /// 比較演算ヘルパー。オペランドに float が含まれる場合は float 比較、そうでなければ int 比較を行う。
    /// </summary>
    private static object CompareOp(
        object? lhs, object? rhs,
        Func<int, int, bool> intOp,
        Func<float, float, bool> floatOp)
    {
        if (lhs is float || rhs is float)
        {
            var lv = Convert.ToSingle(lhs, CultureInfo.InvariantCulture);
            var rv = Convert.ToSingle(rhs, CultureInfo.InvariantCulture);
            return floatOp(lv, rv);
        }
        return intOp(Convert.ToInt32(lhs), Convert.ToInt32(rhs));
    }

    // ═══════════════════════════════════════════════════════════
    //  単項演算の評価
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 単項演算式を評価する。論理否定（not）および算術否定（-）を処理する。
    /// </summary>
    private object EvalUnaryOp(IrUnaryOp un)
    {
        var operand = EvalExpr(un.Operand);
        return un.Op switch
        {
            UnaryOp.Not => operand is true ? (object)false : true,
            UnaryOp.Negate when operand is int i => (object)(-i),
            UnaryOp.Negate when operand is float f => (object)(-f),
            _ => throw new NotSupportedException(
                $"未対応の単項演算: {un.Op} (オペランド型: {operand?.GetType().Name ?? "null"})")
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  論理演算の評価（短絡評価）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 論理演算式を評価する。短絡評価を行い、必要最小限のオペランドのみ評価する。
    /// </summary>
    private object EvalLogicOp(IrLogicOp log) => log.Op switch
    {
        LogicOp.And => EvalLogicAnd(log.Operands),
        LogicOp.Or  => EvalLogicOr(log.Operands),
        _ => throw new NotSupportedException($"未対応の論理演算子: {log.Op}")
    };

    /// <summary>
    /// AND 論理演算を短絡評価する。いずれかのオペランドが false なら即座に false を返す。
    /// </summary>
    private object EvalLogicAnd(System.Collections.Immutable.ImmutableArray<IrExpr> operands)
    {
        foreach (var op in operands)
        {
            if (EvalExpr(op) is not true)
                return false;
        }
        return true;
    }

    /// <summary>
    /// OR 論理演算を短絡評価する。いずれかのオペランドが true なら即座に true を返す。
    /// </summary>
    private object EvalLogicOr(System.Collections.Immutable.ImmutableArray<IrExpr> operands)
    {
        foreach (var op in operands)
        {
            if (EvalExpr(op) is true)
                return true;
        }
        return false;
    }

    // ═══════════════════════════════════════════════════════════
    //  メソッド呼び出し（式位置）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 式位置のメソッド呼び出しを評価する。コンテキストオブジェクトのメソッドをリフレクションで呼び出し、
    /// 戻り値をそのまま返す。アクションと異なり、BtStatus 以外の戻り値も許容する。
    /// </summary>
    /// <exception cref="MissingMethodException">メソッドが見つからない場合。</exception>
    private object? InvokeMethod(IrCall call)
    {
        var type = _context.GetType();
        var method = FindMethod(type, call.Method.Name, call.Args.Length)
            ?? throw new MissingMethodException(
                $"メソッド '{call.Method.Name}' が型 '{type.Name}' に見つかりません。");

        var args = EvalArgs(call.Args);
        return method.Invoke(_context, args);
    }

    // ═══════════════════════════════════════════════════════════
    //  暗黙型変換の評価
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 暗黙型変換式を評価する。ターゲット型に応じて <see cref="Convert"/> クラスで型変換を行う。
    /// 主に int → float の暗黙昇格で使用される。
    /// </summary>
    /// <exception cref="InvalidOperationException">型名を解決できない場合。</exception>
    private object? EvalConvert(IrConvert conv)
    {
        var value = EvalExpr(conv.Operand);
        return conv.TargetType.FullName switch
        {
            "float"  => Convert.ToSingle(value, CultureInfo.InvariantCulture),
            "int"    => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            "double" => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            "string" => value?.ToString(),
            "bool"   => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException(
                $"型 '{conv.TargetType.FullName}' への変換はサポートされていません。")
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  ヘルパーメソッド
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 指定された名前と引数数に一致するパブリックインスタンスメソッドを検索する。
    /// </summary>
    private static MethodInfo? FindMethod(Type type, string name, int argCount)
    {
        return type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == argCount);
    }

    /// <summary>
    /// 引数の式配列を評価し、結果の配列を返す。
    /// </summary>
    private object?[] EvalArgs(System.Collections.Immutable.ImmutableArray<IrExpr> args)
    {
        var result = new object?[args.Length];
        for (var i = 0; i < args.Length; i++)
            result[i] = EvalExpr(args[i]);
        return result;
    }
}

/// <summary>
/// IR ノードの参照同一性でキー比較を行う <see cref="IEqualityComparer{T}"/>。
/// <see cref="Evaluator"/> 内の Timeout/Cooldown 状態管理で、
/// ノードオブジェクトのインスタンス同一性をキーとして使用する。
/// </summary>
internal sealed class NodeReferenceComparer : IEqualityComparer<IrNode>
{
    public static readonly NodeReferenceComparer Instance = new();

    public bool Equals(IrNode? x, IrNode? y) => ReferenceEquals(x, y);

    public int GetHashCode(IrNode obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
}
