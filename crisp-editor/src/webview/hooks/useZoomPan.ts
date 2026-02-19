/**
 * ズーム・パンフック
 *
 * キャンバスのパン（平行移動）とズーム（拡大縮小）を管理する。
 *
 * 操作:
 * - マウスドラッグ（背景）: キャンバスのパン
 * - マウスホイール: ズーム（カーソル位置を中心に拡大縮小）
 * - Ctrl+Shift+F: ツリー全体をフィットするようにズーム
 *
 * ズームはカーソル位置を中心に行い、ユーザーが注目している
 * 部分が画面から外れないようにする。
 */
import { useState, useCallback, useRef } from "react";
import { ZOOM_PAN } from "../layout/constants";

/**
 * キャンバスのトランスフォーム状態
 */
export interface Transform {
  /** X 方向の平行移動量（ピクセル） */
  x: number;
  /** Y 方向の平行移動量（ピクセル） */
  y: number;
  /** ズーム倍率 */
  scale: number;
}

/**
 * ズーム・パンフックの戻り値
 */
interface UseZoomPanResult {
  /** 現在のトランスフォーム状態 */
  transform: Transform;
  /** キャンバス要素に設定するイベントハンドラ群 */
  handlers: {
    onMouseDown: (e: React.MouseEvent) => void;
    onMouseMove: (e: React.MouseEvent) => void;
    onMouseUp: (e: React.MouseEvent) => void;
    onMouseLeave: (e: React.MouseEvent) => void;
    onWheel: (e: React.WheelEvent) => void;
  };
  /** トランスフォームを直接設定する関数（fitToView 等で使用） */
  setTransform: (t: Transform) => void;
}

/**
 * ズーム・パンを管理するカスタムフック
 *
 * @returns トランスフォーム状態とイベントハンドラ
 */
export function useZoomPan(): UseZoomPanResult {
  const [transform, setTransform] = useState<Transform>({
    x: 0,
    y: 0,
    scale: ZOOM_PAN.DEFAULT_SCALE,
  });

  /* ドラッグ操作の追跡用 ref */
  const isDragging = useRef(false);
  const lastMouse = useRef({ x: 0, y: 0 });

  /**
   * マウスボタンが押された時 — ドラッグ開始
   *
   * 背景（ノード以外の場所）をクリックした場合にのみパンを開始する。
   * ノードへのクリックは NodeComponent が stopPropagation で処理する。
   */
  const onMouseDown = useCallback((e: React.MouseEvent) => {
    if (e.button !== 0) return;
    isDragging.current = true;
    lastMouse.current = { x: e.clientX, y: e.clientY };
  }, []);

  /**
   * マウスが移動した時 — パン（平行移動）
   *
   * ドラッグ中のマウス移動量をトランスフォームに加算する。
   */
  const onMouseMove = useCallback((e: React.MouseEvent) => {
    if (!isDragging.current) return;

    const dx = e.clientX - lastMouse.current.x;
    const dy = e.clientY - lastMouse.current.y;
    lastMouse.current = { x: e.clientX, y: e.clientY };

    setTransform((prev) => ({
      ...prev,
      x: prev.x + dx,
      y: prev.y + dy,
    }));
  }, []);

  /**
   * マウスボタンが離された時 — ドラッグ終了
   */
  const onMouseUp = useCallback(() => {
    isDragging.current = false;
  }, []);

  /**
   * マウスがキャンバス外に出た時 — ドラッグ中止
   */
  const onMouseLeave = useCallback(() => {
    isDragging.current = false;
  }, []);

  /**
   * マウスホイール操作 — ズーム
   *
   * カーソル位置を中心にズームする。
   * ズーム前後でカーソル位置のキャンバス座標が変わらないように
   * 平行移動量を調整する。
   */
  const onWheel = useCallback((e: React.WheelEvent) => {
    e.preventDefault();

    const delta = -e.deltaY * ZOOM_PAN.WHEEL_ZOOM_FACTOR;
    const newScale = Math.min(
      ZOOM_PAN.MAX_SCALE,
      Math.max(ZOOM_PAN.MIN_SCALE, transform.scale * (1 + delta))
    );

    /* カーソル位置を中心にズームするための座標計算 */
    const rect = e.currentTarget.getBoundingClientRect();
    const mouseX = e.clientX - rect.left;
    const mouseY = e.clientY - rect.top;

    const scaleRatio = newScale / transform.scale;

    setTransform({
      x: mouseX - (mouseX - transform.x) * scaleRatio,
      y: mouseY - (mouseY - transform.y) * scaleRatio,
      scale: newScale,
    });
  }, [transform]);

  return {
    transform,
    handlers: {
      onMouseDown,
      onMouseMove,
      onMouseUp,
      onMouseLeave,
      onWheel,
    },
    setTransform,
  };
}
