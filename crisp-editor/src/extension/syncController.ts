/**
 * 双方向同期コントローラ
 *
 * テキストエディタ、LSP サーバー、WebView の3者間を仲介する中枢モジュール。
 * 以下の同期フローを管理する:
 *
 * (B) LSP → WebView: treeLayout 通知を中継
 * (C) WebView → テキスト: ビジュアル操作を LSP リクエスト → テキスト編集に変換
 * (D) テキスト → WebView: カーソル位置からノード ID を特定して通知
 *
 * 同期ループの防止:
 * - isApplyingEdit フラグでビジュアル起因の変更を識別
 * - LSP Server の treeLayout が Single Source of Truth
 * - テキスト起因でもビジュアル起因でも最終的に LSP の出力で一致する
 */
import * as vscode from "vscode";
import type { LanguageClient } from "vscode-languageclient/node";
import type {
  TreeLayout,
  TreeNode,
} from "../shared/treeModel";
import type {
  TreeLayoutNotification,
  TreeDiagnosticsNotification,
  WebViewToExtensionMessage,
  EditResult,
} from "../shared/protocol";

export class SyncController {
  /** ファイル URI → WebView インスタンスのマップ */
  private webviews = new Map<string, vscode.Webview>();

  /** ファイル URI → 最新ツリーレイアウトのキャッシュ */
  private treeLayouts = new Map<string, TreeLayout>();

  /**
   * サイドパネル用の WebView インスタンス
   *
   * サイドパネルは特定のファイルに紐付かず、
   * 現在アクティブなエディタのファイルに追従する。
   */
  private sidePanelWebview: vscode.Webview | null = null;

  /**
   * サイドパネルが現在追跡しているファイル URI
   *
   * アクティブエディタが変更されるたびに更新される。
   */
  private sidePanelUri: string | null = null;

  /**
   * ビジュアル操作によるテキスト変更中かどうかのフラグ
   *
   * ビジュアル編集 → LSP リクエスト → workspace.applyEdit の間 true になる。
   * onDocumentChanged でこのフラグを確認し、同期ループを防止する。
   */
  private isApplyingEdit = false;

  constructor(private readonly lspClient: LanguageClient) {}

  /* ============================================================
   * WebView 登録・解除
   * ============================================================ */

  /**
   * WebView インスタンスを登録する
   *
   * ツリービューパネルが開かれた時に呼び出される。
   * 登録後、キャッシュ済みのレイアウト情報があれば即座に送信する。
   *
   * @param webview 登録する WebView インスタンス
   * @param uri 対応する .crisp ファイルの URI
   */
  registerWebView(webview: vscode.Webview, uri: vscode.Uri): void {
    const uriStr = uri.toString();
    this.webviews.set(uriStr, webview);

    /* キャッシュ済みレイアウトがあれば初回表示用に送信する */
    const cachedLayout = this.treeLayouts.get(uriStr);
    if (cachedLayout) {
      webview.postMessage({
        type: "treeLayout",
        payload: cachedLayout,
      });
    }
  }

  /**
   * サイドパネル用の WebView インスタンスを登録する
   *
   * サイドパネルは特定のファイルに紐付かず、
   * アクティブエディタのファイルに追従する。
   * 登録時にアクティブエディタの情報を確認し、
   * キャッシュ済みレイアウトがあれば即座に送信する。
   *
   * @param webview 登録するサイドパネルの WebView インスタンス
   */
  registerSidePanelWebView(webview: vscode.Webview): void {
    this.sidePanelWebview = webview;

    /* 現在のアクティブエディタの URI を取得してキャッシュ送信を試みる */
    const activeEditor = vscode.window.activeTextEditor;
    if (activeEditor && activeEditor.document.languageId === "crisp") {
      this.sidePanelUri = activeEditor.document.uri.toString();
      const cachedLayout = this.treeLayouts.get(this.sidePanelUri);
      if (cachedLayout) {
        webview.postMessage({
          type: "treeLayout",
          payload: cachedLayout,
        });
      }
    }
  }

  /**
   * サイドパネルの追跡ファイルを更新する
   *
   * アクティブエディタが変更された時に呼び出され、
   * サイドパネルが追跡する URI を更新し、
   * キャッシュ済みレイアウトがあれば送信する。
   *
   * @param uri 新しいアクティブファイルの URI（null = .crisp 以外がアクティブ）
   */
  onActiveEditorChanged(uri: string | null): void {
    if (!this.sidePanelWebview) return;
    this.sidePanelUri = uri;

    if (uri) {
      const cachedLayout = this.treeLayouts.get(uri);
      if (cachedLayout) {
        this.sidePanelWebview.postMessage({
          type: "treeLayout",
          payload: cachedLayout,
        });
      }
    }
  }

  /**
   * WebView インスタンスの登録を解除する
   *
   * ツリービューパネルが閉じられた時に呼び出される。
   *
   * @param uri 対応する .crisp ファイルの URI
   */
  unregisterWebView(uri: vscode.Uri): void {
    this.webviews.delete(uri.toString());
  }

  /**
   * サイドパネルの WebView 登録を解除する
   *
   * サイドパネルが非表示になった時に呼び出される。
   */
  unregisterSidePanelWebView(): void {
    this.sidePanelWebview = null;
    this.sidePanelUri = null;
  }

  /* ============================================================
   * LSP → WebView (通知の中継)
   * ============================================================ */

  /**
   * ツリーレイアウト更新通知を処理する
   *
   * LSP Server の crisp/treeLayout 通知を受信し、
   * レイアウトをキャッシュした後、対応する WebView に転送する。
   *
   * @param params LSP 通知のパラメータ
   */
  onTreeLayoutUpdate(params: TreeLayoutNotification): void {
    const layout = params.layout;
    this.treeLayouts.set(params.uri, layout);

    const message = {
      type: "treeLayout" as const,
      payload: layout,
    };

    /* パネル WebView に送信 */
    const webview = this.webviews.get(params.uri);
    if (webview) {
      webview.postMessage(message);
    }

    /* サイドパネル WebView にも送信（追跡中の URI と一致する場合） */
    if (this.sidePanelWebview && this.sidePanelUri === params.uri) {
      this.sidePanelWebview.postMessage(message);
    }
  }

  /**
   * 診断情報更新通知を処理する
   *
   * LSP Server の crisp/diagnostics 通知を受信し、
   * 対応する WebView に転送する。エラーバッジや警告表示の更新に使用される。
   *
   * @param params LSP 通知のパラメータ
   */
  onDiagnosticsUpdate(params: TreeDiagnosticsNotification): void {
    const message = {
      type: "diagnostics" as const,
      payload: params.diagnostics,
    };

    const webview = this.webviews.get(params.uri);
    if (webview) {
      webview.postMessage(message);
    }

    /* サイドパネル WebView にも送信 */
    if (this.sidePanelWebview && this.sidePanelUri === params.uri) {
      this.sidePanelWebview.postMessage(message);
    }
  }

  /* ============================================================
   * WebView → テキストエディタ (ビジュアル操作の反映)
   * ============================================================ */

  /**
   * WebView からのメッセージをディスパッチする
   *
   * ビジュアルエディタのユーザー操作（ノード追加、移動、削除等）を
   * 対応する LSP リクエストに変換し、テキスト編集を適用する。
   *
   * @param msg WebView から受信したメッセージ
   */
  async handleWebViewMessage(msg: WebViewToExtensionMessage): Promise<void> {
    switch (msg.type) {
      case "addNode":
        await this.applyLspEdit("crisp/addNode", msg.payload);
        break;
      case "removeNode":
        await this.applyLspEdit("crisp/removeNode", msg.payload);
        break;
      case "moveNode":
        await this.applyLspEdit("crisp/moveNode", msg.payload);
        break;
      case "wrapNode":
        await this.applyLspEdit("crisp/wrapNode", msg.payload);
        break;
      case "unwrapNode":
        await this.applyLspEdit("crisp/unwrapNode", msg.payload);
        break;
      case "nodeClicked":
        this.jumpToNode(msg.payload.nodeId, msg.payload.uri);
        break;
      case "toggleBreakpoint":
        this.toggleBreakpoint(msg.payload.nodeId, msg.payload.uri);
        break;
    }
  }

  /**
   * LSP にツリー編集リクエストを送信し、テキスト編集を適用する
   *
   * ビジュアル操作 → LSP リクエスト → テキスト編集の一連のフローを実行する。
   * isApplyingEdit フラグで同期ループを防止する。
   *
   * @param method LSP メソッド名 (例: "crisp/addNode")
   * @param params リクエストパラメータ
   */
  private async applyLspEdit(method: string, params: unknown): Promise<void> {
    this.isApplyingEdit = true;
    try {
      const result: EditResult = await this.lspClient.sendRequest(
        method,
        params
      );
      const payload = params as { uri: string };
      const uri = vscode.Uri.parse(payload.uri);
      const edit = new vscode.WorkspaceEdit();

      for (const e of result.edits) {
        const range = new vscode.Range(
          new vscode.Position(e.range.start.line, e.range.start.character),
          new vscode.Position(e.range.end.line, e.range.end.character)
        );
        edit.replace(uri, range, e.newText);
      }

      await vscode.workspace.applyEdit(edit);
    } finally {
      this.isApplyingEdit = false;
    }
  }

  /* ============================================================
   * テキストエディタ → WebView (カーソル同期)
   * ============================================================ */

  /**
   * テキストエディタのカーソル位置変更を処理する
   *
   * カーソルのオフセットから対応するノード ID を特定し、
   * WebView に cursorSync メッセージを送信する。
   * ツリーの origin フィールドを使って、最も深いノードを特定する。
   *
   * @param uri ファイルの URI
   * @param offset テキスト先頭からのカーソルオフセット
   */
  onCursorPositionChanged(uri: string, offset: number): void {
    const layout = this.treeLayouts.get(uri);
    if (!layout) return;

    const nodeId = this.findNodeAtOffset(layout, offset);
    if (nodeId === null) return;

    const message = {
      type: "cursorSync" as const,
      payload: { nodeId },
    };

    const webview = this.webviews.get(uri);
    if (webview) {
      webview.postMessage(message);
    }

    /* サイドパネル WebView にも送信 */
    if (this.sidePanelWebview && this.sidePanelUri === uri) {
      this.sidePanelWebview.postMessage(message);
    }
  }

  /**
   * テキスト変更イベントを処理する
   *
   * ビジュアル編集によるテキスト変更は無視する（ループ防止）。
   * テキスト起因の変更は LSP の didChange が自動処理するため、
   * ここでは特に何もする必要がない。
   *
   * @param _uri ファイルの URI（将来の拡張用）
   */
  onDocumentChanged(_uri: string): void {
    if (this.isApplyingEdit) return;
    /* LSP が自動で treeLayout 通知を送信するため、ここでは何もしない */
  }

  /* ============================================================
   * ノード検索ヘルパー
   * ============================================================ */

  /**
   * テキストオフセットに対応するノード ID を特定する
   *
   * ツリーを深さ優先で走査し、origin.start <= offset < origin.start + origin.length
   * を満たす最も深いノードの ID を返す。
   *
   * @param layout ツリーレイアウト
   * @param offset テキスト先頭からのオフセット
   * @returns 対応するノード ID、見つからなければ null
   */
  private findNodeAtOffset(layout: TreeLayout, offset: number): number | null {
    function walk(node: TreeNode): number | null {
      if (
        offset < node.origin.start ||
        offset >= node.origin.start + node.origin.length
      ) {
        return null;
      }

      /* 子を先に探索 — より深い（具体的な）マッチを優先する */
      if (node.children) {
        for (const child of node.children) {
          const found = walk(child);
          if (found !== null) return found;
        }
      }

      return node.id;
    }

    return walk(layout.root);
  }

  /**
   * ノード ID からツリーノードを検索する
   *
   * @param layout ツリーレイアウト
   * @param nodeId 検索対象のノード ID
   * @returns 見つかったノード、存在しなければ null
   */
  private findNodeById(layout: TreeLayout, nodeId: number): TreeNode | null {
    function walk(node: TreeNode): TreeNode | null {
      if (node.id === nodeId) return node;
      if (node.children) {
        for (const child of node.children) {
          const found = walk(child);
          if (found !== null) return found;
        }
      }
      return null;
    }

    return walk(layout.root);
  }

  /* ============================================================
   * ノードクリック → テキストジャンプ
   * ============================================================ */

  /**
   * ビジュアル上のノードクリックでテキストエディタの対応位置にジャンプする
   *
   * ノードの origin からテキスト位置を計算し、
   * アクティブなテキストエディタのカーソルをその位置に移動する。
   *
   * @param nodeId クリックされたノードの ID
   * @param uri ファイルの URI
   */
  private jumpToNode(nodeId: number, uri: string): void {
    const layout = this.treeLayouts.get(uri);
    if (!layout) return;

    const node = this.findNodeById(layout, nodeId);
    if (!node) return;

    const doc = vscode.workspace.textDocuments.find(
      (d) => d.uri.toString() === uri
    );
    if (!doc) return;

    const pos = doc.positionAt(node.origin.start);
    const editor = vscode.window.visibleTextEditors.find(
      (e) => e.document.uri.toString() === uri
    );
    if (editor) {
      editor.selection = new vscode.Selection(pos, pos);
      editor.revealRange(new vscode.Range(pos, pos));
    }
  }

  /* ============================================================
   * fitToView コマンド
   * ============================================================ */

  /**
   * 指定 URI に対応する WebView に fitToView メッセージを送信する
   *
   * ツリー全体がビューに収まるようにズームレベルを自動調整する。
   * crisp.fitTree コマンドから呼び出される。
   *
   * @param uri 対象ファイルの URI
   */
  sendFitToView(uri: string): void {
    const message = {
      type: "fitToView" as const,
    };

    const webview = this.webviews.get(uri);
    if (webview) {
      webview.postMessage(message);
    }

    if (this.sidePanelWebview && this.sidePanelUri === uri) {
      this.sidePanelWebview.postMessage(message);
    }
  }

  /* ============================================================
   * ブレークポイント管理 (F7)
   * ============================================================ */

  /**
   * ノードのブレークポイントを切り替える
   *
   * ビジュアルエディタの右クリック "Toggle Breakpoint" から呼び出される。
   * ノードの origin からテキスト位置を計算し、VSCode のブレークポイントを操作する。
   *
   * @param nodeId ブレークポイント対象のノード ID
   * @param uri ファイルの URI
   */
  private toggleBreakpoint(nodeId: number, uri: string): void {
    const layout = this.treeLayouts.get(uri);
    if (!layout) return;

    const node = this.findNodeById(layout, nodeId);
    if (!node) return;

    const doc = vscode.workspace.textDocuments.find(
      (d) => d.uri.toString() === uri
    );
    if (!doc) return;

    const pos = doc.positionAt(node.origin.start);
    const location = new vscode.Location(vscode.Uri.parse(uri), pos);

    /*
     * 既存のブレークポイントを確認し、存在すれば削除、なければ追加する。
     * VSCode API は addBreakpoints / removeBreakpoints で操作する。
     */
    const existingBps = vscode.debug.breakpoints.filter(
      (bp): bp is vscode.SourceBreakpoint =>
        bp instanceof vscode.SourceBreakpoint &&
        bp.location.uri.toString() === uri &&
        bp.location.range.start.line === pos.line
    );

    if (existingBps.length > 0) {
      vscode.debug.removeBreakpoints(existingBps);
    } else {
      vscode.debug.addBreakpoints([
        new vscode.SourceBreakpoint(location),
      ]);
    }
  }
}
