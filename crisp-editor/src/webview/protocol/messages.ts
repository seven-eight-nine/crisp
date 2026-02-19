/**
 * WebView 内部で使用するメッセージ型定義
 *
 * Extension ↔ WebView 間の postMessage 通信で使用するメッセージ型を
 * WebView 側で再エクスポートする。
 * shared/protocol.ts の型を直接使用するが、
 * WebView 固有の内部メッセージ型もここで定義する。
 */
export type {
  ExtensionToWebViewMessage,
  WebViewToExtensionMessage,
  TreeLayoutMessage,
  DiagnosticsMessage,
  CursorSyncMessage,
  DebugStateMessage,
  FitToViewMessage,
  AddNodeMessage,
  RemoveNodeMessage,
  MoveNodeMessage,
  WrapNodeMessage,
  UnwrapNodeMessage,
  NodeClickedMessage,
  ToggleBreakpointMessage,
} from "../../shared/protocol";

export type {
  TreeLayout,
  TreeNode,
  TreeEdge,
  TextSpan,
  TreeBounds,
  NodeDiagnostic,
  DebugTreeState,
  NodeDebugInfo,
} from "../../shared/treeModel";

export type { NodeType } from "../../shared/nodeTypes";
