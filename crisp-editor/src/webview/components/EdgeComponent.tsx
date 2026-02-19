/**
 * エッジ（接続線）コンポーネント
 *
 * 親ノードから子ノードへの接続線をベジェカーブで描画する。
 * 親の下端中央から子の上端中央へ、なめらかな曲線で接続する。
 *
 * スタイル:
 * - 通常: VSCode のウィジェットボーダー色で控えめに表示
 * - デバッグ中のアクティブパス: アクセントカラーで強調（将来拡張）
 */
import React from "react";
import type { EdgeRenderInfo } from "../hooks/useTreeLayout";

/**
 * エッジコンポーネントのプロパティ
 */
interface EdgeProps {
  /** エッジの描画情報（パスデータを含む） */
  edgeInfo: EdgeRenderInfo;
}

/**
 * エッジの描画コンポーネント
 *
 * SVG の <path> 要素でベジェカーブを描画する。
 * 塗りつぶしなし (fill: none)、ストロークのみで表現する。
 */
export function EdgeComponent({ edgeInfo }: EdgeProps): React.JSX.Element | null {
  if (!edgeInfo.pathData) return null;

  return (
    <path
      d={edgeInfo.pathData}
      className="crisp-edge"
      fill="none"
    />
  );
}
