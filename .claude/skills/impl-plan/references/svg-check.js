// 計画の HTML をブラウザで開き、コンソール（またはブラウザの JavaScript 実行ツール）で実行する。
// 各 SVG の <text> が、それを含む最小の <rect> から右にはみ出していないか、viewBox の外に出ていないかを調べる。
// 戻り値が [] なら OK。[図の番号, 文字, はみ出した px] が出たら、文言を短くするか箱を広げる。
(() => {
  const out = [];
  document.querySelectorAll('svg').forEach((svg, i) => {
    const vb = svg.viewBox.baseVal;
    const rects = [...svg.querySelectorAll('rect')].map(r => r.getBBox());
    svg.querySelectorAll('text').forEach(t => {
      const b = t.getBBox();
      const box = rects
        .filter(r => b.x >= r.x && b.x <= r.x + r.width && b.y >= r.y - 2 && b.y <= r.y + r.height)
        .sort((p, q) => p.width * p.height - q.width * q.height)[0];
      if (box && b.x + b.width > box.x + box.width + 1)
        out.push([i, t.textContent, Math.round(b.x + b.width - box.x - box.width)]);
      if (b.x + b.width > vb.width || b.y + b.height > vb.height)
        out.push([i, t.textContent, 'viewBox の外']);
    });
  });
  return out;
})();
