(function () {
  const canvas = document.getElementById("graphCanvas");
  const ctx = canvas.getContext("2d");
  let nodes = [];
  let edges = [];
  let selectedId = null;
  let animationFrame = 0;
  let theme = "light";
  let dragNode = null;
  let pointerDown = null;
  let focusPhase = 0;
  let currentView = "architecture";
  let callGraphTitle = "";
  let impactGraphTitle = "";
  let callGraphAnchor = null;
  let namespaceProjectAnchor = null;
  let namespaceAnchorPinned = false;
  let callGraphContentWidth = 0;
  let callGraphContentHeight = 0;
  const accessibilityFilterKeys = ["public", "internal", "private", "protected"];
  const namespaceAccessFilters = new Set();
  const callGraphAccessFilters = new Set();
  let namespaceClusters = [];
  let namespaceEdges = [];
  let namespaceRoot = "";
  let layoutKey = "";
  let localeStrings = {
    emptyGraph: "Nenhum projeto no grafo — use Atualizar na barra superior",
    emptyGraphHint: "O grafo ocupa toda a área até você selecionar um projeto.",
    methods: "método(s)",
    calls: "chamada(s)",
    clusters: "cluster(s)",
    links: "ligação(ões)",
    projects: "projeto(s)",
    references: "referência(s)",
    cycles: "ciclo(s)",
    focus: "Foco",
    callGraph: "Call Graph",
    namespaceMap: "Namespace map",
    typesCount: "tipos",
    typeGraph: "Type graph",
    impactGraph: "Impact analysis",
    usages: "uso(s)"
  };
  let selectedNamespaceClusterId = null;
  let circularDependencies = [];
  let activeCycleIndex = -1;
  const cycleEdgeKeys = new Set();
  const cycleNodeIds = new Set();
  const pinned = new Set();
  const pinnedCallNodes = new Set();
  const pinnedNamespaceClusters = new Set();
  let callAnchorPinned = false;
  const statusSummary = document.getElementById("statusSummary");
  const statusSelection = document.getElementById("statusSelection");
  const legendEl = document.getElementById("legend");
  const defaultLegendHtml = legendEl ? legendEl.innerHTML : "";

  const accessibilityStyle = {
    public: { color: "#0078d4", label: "public" },
    internal: { color: "#ca5010", label: "internal" },
    private: { color: "#8764b8", label: "private" },
    protected: { color: "#498205", label: "protected" },
    protectedInternal: { color: "#008575", label: "protected" },
    unknown: { color: "#6b7280", label: "?" }
  };

  const languageStyle = {
    1: { label: "C#", color: "#512bd4", ring: "#ddd6fe" },
    2: { label: "F#", color: "#378bba", ring: "#dbeafe" },
    3: { label: "VB", color: "#005a9e", ring: "#dbeafe" },
    0: { label: "?", color: "#6b7280", ring: "#e5e7eb" }
  };

  function css(name, fallback) {
    return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback;
  }

  function t(key, fallback) {
    return localeStrings[key] || fallback || key;
  }

  function applyLayoutPositions(positions) {
    if (!positions || typeof positions !== "object") {
      return;
    }

    if (currentView === "architecture" || currentView === "impactGraph") {
      for (const node of nodes) {
        const saved = positions[node.id];
        if (saved) {
          node.x = saved.x;
          node.y = saved.y;
          node.vx = 0;
          node.vy = 0;
          pinned.add(node.id);
        }
      }
      return;
    }

    if (currentView === "callGraph" || currentView === "typeGraph") {
      const anchor = positions.__anchor__;
      if (anchor && callGraphAnchor) {
        callGraphAnchor.x = anchor.x;
        callGraphAnchor.y = anchor.y;
        callAnchorPinned = true;
      }

      for (const node of nodes) {
        const saved = positions[node.id];
        if (saved) {
          node.x = saved.x;
          node.y = saved.y;
          pinnedCallNodes.add(node.id);
        }
      }
      return;
    }

    if (currentView === "namespaceMap") {
      const anchor = positions.__anchor__;
      if (anchor && namespaceProjectAnchor) {
        namespaceProjectAnchor.x = anchor.x;
        namespaceProjectAnchor.y = anchor.y;
        namespaceAnchorPinned = true;
      }

      for (const cluster of namespaceClusters) {
        const saved = positions[cluster.id];
        if (saved) {
          cluster.x = saved.x;
          cluster.y = saved.y;
          pinnedNamespaceClusters.add(cluster.id);
        }
      }
    }
  }

  function collectLayoutPositions() {
    const positions = {};
    if (currentView === "architecture") {
      for (const node of nodes) {
        positions[node.id] = { x: node.x, y: node.y };
      }
    } else if (currentView === "callGraph") {
      if (callGraphAnchor) {
        positions.__anchor__ = { x: callGraphAnchor.x, y: callGraphAnchor.y };
      }
      for (const node of nodes) {
        positions[node.id] = { x: node.x, y: node.y };
      }
    } else if (currentView === "namespaceMap") {
      if (namespaceProjectAnchor) {
        positions.__anchor__ = { x: namespaceProjectAnchor.x, y: namespaceProjectAnchor.y };
      }
      for (const cluster of namespaceClusters) {
        positions[cluster.id] = { x: cluster.x, y: cluster.y };
      }
    }

    return positions;
  }

  function postSaveLayout() {
    if (!layoutKey || !(window.chrome && window.chrome.webview)) {
      return;
    }

    window.chrome.webview.postMessage(
      JSON.stringify({
        type: "saveLayout",
        layoutKey: layoutKey,
        layoutPositions: collectLayoutPositions()
      })
    );
  }

  function applyTheme(nextTheme) {
    theme = nextTheme === "dark" ? "dark" : nextTheme === "hc" ? "hc" : "light";
    document.documentElement.classList.toggle("theme-dark", theme === "dark");
    document.documentElement.classList.toggle("theme-hc", theme === "hc");
    draw();
  }

  function normalizeAccessibilityKey(accessibility) {
    if (!accessibility || accessibility === "unknown") {
      return "unknown";
    }
    if (accessibility === "protectedInternal") {
      return "protected";
    }
    return accessibility;
  }

  function accessibilityAccentColor(accessibility) {
    const key = normalizeAccessibilityKey(accessibility);
    return (accessibilityStyle[key] || accessibilityStyle.unknown).color;
  }

  function renderAccessibilityLegend(activeFilters, onToggle) {
    if (!legendEl) return;
    legendEl.style.pointerEvents = "auto";
    legendEl.innerHTML = accessibilityFilterKeys
      .map((key) => {
        const style = accessibilityStyle[key];
        const selected = activeFilters.has(key);
        const dimmed = activeFilters.size > 0 && !selected;
        const activeStyle = selected
          ? `background:${style.color};color:#ffffff;border-color:${style.color};`
          : `border-color:${style.color};color:${style.color};`;
        const dimClass = dimmed ? " dimmed" : "";
        return `<span class="badge access-${key}${selected ? " active" : ""}${dimClass}" data-access="${key}" style="${activeStyle}cursor:pointer;">${style.label}</span>`;
      })
      .join("");

    legendEl.querySelectorAll(".badge[data-access]").forEach((badge) => {
      badge.addEventListener("click", () => {
        onToggle(badge.getAttribute("data-access"));
      });
    });
  }

  function setCallGraphLegend() {
    renderAccessibilityLegend(callGraphAccessFilters, toggleCallGraphAccessFilter);
  }

  function clusterAccessibilityKeys(cluster) {
    const types = cluster.types || [];
    if (types.length === 0) {
      return new Set(["unknown"]);
    }
    const keys = new Set();
    for (const typeEntry of types) {
      keys.add(normalizeAccessibilityKey(typeEntry.accessibility || "unknown"));
    }
    return keys;
  }

  function clusterMatchesAccessFilter(cluster) {
    if (namespaceAccessFilters.size === 0) {
      return true;
    }
    const keys = clusterAccessibilityKeys(cluster);
    for (const filterKey of namespaceAccessFilters) {
      if (keys.has(filterKey) || keys.has(normalizeAccessibilityKey(filterKey))) {
        return true;
      }
    }
    return false;
  }

  function callGraphNodeMatchesAccessFilter(node) {
    if (callGraphAccessFilters.size === 0) {
      return true;
    }
    const key = normalizeAccessibilityKey(node.accessibility);
    return callGraphAccessFilters.has(key);
  }

  function visibleCallGraphNodes() {
    return nodes.filter(callGraphNodeMatchesAccessFilter);
  }

  function toggleCallGraphAccessFilter(key) {
    if (callGraphAccessFilters.has(key)) {
      callGraphAccessFilters.delete(key);
    } else {
      callGraphAccessFilters.add(key);
    }
    setCallGraphLegend();
    updateStatusBar();
    draw();
  }

  function visibleNamespaceClusters() {
    return namespaceClusters.filter(clusterMatchesAccessFilter);
  }

  function toggleNamespaceAccessFilter(key) {
    if (namespaceAccessFilters.has(key)) {
      namespaceAccessFilters.delete(key);
    } else {
      namespaceAccessFilters.add(key);
    }
    setNamespaceLegend();
    updateStatusBar();
    draw();
  }

  function setNamespaceLegend() {
    renderAccessibilityLegend(namespaceAccessFilters, toggleNamespaceAccessFilter);
  }

  function restoreArchitectureLegend() {
    if (legendEl) {
      legendEl.style.pointerEvents = "none";
      legendEl.innerHTML = defaultLegendHtml;
    }
  }

  function highlightLegend(language) {
    const map = { 1: "cs", 2: "fs", 3: "vb" };
    const active = map[language] || null;
    document.querySelectorAll("#legend .badge").forEach((badge) => {
      badge.classList.toggle("active", active !== null && badge.classList.contains(active));
    });
  }

  function updateStatusBar() {
    if (!statusSummary || !statusSelection) return;
    if (currentView === "callGraph") {
      const visibleMethods = visibleCallGraphNodes();
      const filterHint =
        callGraphAccessFilters.size > 0 ? ` · ${visibleMethods.length}/${nodes.length}` : "";
      statusSummary.textContent = `${nodes.length} ${t("methods", "método(s)")}${filterHint} · ${edges.length} ${t("calls", "chamada(s)")}`;
      statusSelection.textContent = callGraphTitle ? `${t("callGraph", "Call graph")}: ${callGraphTitle}` : t("callGraph", "Call graph");
      return;
    }
    if (currentView === "namespaceMap") {
      const visible = visibleNamespaceClusters();
      const filterHint =
        namespaceAccessFilters.size > 0 ? ` · ${visible.length}/${namespaceClusters.length}` : "";
      statusSummary.textContent = `${namespaceClusters.length} ${t("clusters", "cluster(s)")}${filterHint} · ${namespaceEdges.length} ${t("links", "ligação(ões)")}`;
      statusSelection.textContent = namespaceRoot ? `Namespace: ${namespaceRoot}` : t("namespaceMap", "Namespace map");
      return;
    }
    if (currentView === "typeGraph") {
      statusSummary.textContent = `${nodes.length} ${t("typesCount", "tipos")} · ${edges.length} ${t("links", "ligação(ões)")}`;
      statusSelection.textContent = callGraphTitle ? `${t("typeGraph", "Type graph")}: ${callGraphTitle}` : t("typeGraph", "Type graph");
      return;
    }
    if (currentView === "impactGraph") {
      const usageTotal = nodes.reduce((sum, n) => sum + (n.usageCount || 0), 0);
      statusSummary.textContent = `${nodes.length} ${t("projects", "projeto(s)")} · ${usageTotal} ${t("usages", "uso(s)")}`;
      statusSelection.textContent = impactGraphTitle ? `${t("impactGraph", "Impact analysis")}: ${impactGraphTitle}` : t("impactGraph", "Impact analysis");
      return;
    }
    const count = nodes.length;
    const cycleHint =
      circularDependencies.length > 0
        ? ` · ⚠ ${circularDependencies.length} ${t("cycles", "ciclo(s)")}`
        : "";
    statusSummary.textContent =
      count === 0
        ? t("emptyGraph", "Nenhum projeto no grafo — use Atualizar na barra superior")
        : `${count} ${t("projects", "projeto(s)")} · ${edges.length} ${t("references", "referência(s)")}${cycleHint}`;
    if (!selectedId) {
      statusSelection.textContent = "";
      return;
    }
    const node = nodes.find((n) => n.id === selectedId);
    statusSelection.textContent = node ? `${t("focus", "Foco")}: ${node.name}` : "";
  }

  function computeNamespaceFrameBounds(clusters) {
    const list = clusters && clusters.length ? clusters : namespaceClusters;
    if (list.length === 0) {
      return null;
    }

    const minX = Math.min(...list.map((c) => c.x)) - 24;
    const minY = Math.min(...list.map((c) => c.y)) - 36;
    const maxX = Math.max(...list.map((c) => c.x + c.w)) + 24;
    const maxY = Math.max(...list.map((c) => c.y + c.h)) + 24;
    return { minX, minY, maxX, maxY };
  }

  function getGraphViewport() {
    return document.getElementById("graphViewport");
  }

  function getLayoutViewportWidth() {
    const viewport = getGraphViewport();
    return viewport ? viewport.clientWidth : canvas.clientWidth || window.innerWidth;
  }

  function getLayoutViewportHeight() {
    const viewport = getGraphViewport();
    return viewport ? viewport.clientHeight : canvas.clientHeight || window.innerHeight;
  }

  function isCallGraphLikeView() {
    return currentView === "callGraph" || currentView === "typeGraph";
  }

  function computeCallGraphBounds() {
    const halfW = 66;
    const halfH = 28;
    const anchorRadius = 52;
    const anchorLabel = 28;
    let minX = Infinity;
    let maxX = -Infinity;
    let minY = Infinity;
    let maxY = -Infinity;

    if (callGraphAnchor) {
      minX = Math.min(minX, callGraphAnchor.x - anchorRadius);
      maxX = Math.max(maxX, callGraphAnchor.x + anchorRadius);
      minY = Math.min(minY, callGraphAnchor.y - anchorRadius);
      maxY = Math.max(maxY, callGraphAnchor.y + anchorRadius + anchorLabel);
    }

    for (const node of nodes) {
      minX = Math.min(minX, node.x - halfW);
      maxX = Math.max(maxX, node.x + halfW);
      minY = Math.min(minY, node.y - halfH);
      maxY = Math.max(maxY, node.y + halfH);
    }

    if (!Number.isFinite(minX)) {
      return null;
    }

    return { minX, minY, maxX, maxY };
  }

  function shiftCallGraphLayout(deltaX, deltaY) {
    if (deltaX === 0 && deltaY === 0) {
      return;
    }

    if (callGraphAnchor) {
      callGraphAnchor.x += deltaX;
      callGraphAnchor.y += deltaY;
    }

    for (const node of nodes) {
      node.x += deltaX;
      node.y += deltaY;
    }
  }

  function normalizeCallGraphLayout() {
    const bounds = computeCallGraphBounds();
    if (!bounds) {
      return;
    }

    const pad = 48;
    const deltaX = bounds.minX < pad ? pad - bounds.minX : 0;
    const deltaY = bounds.minY < pad ? pad - bounds.minY : 0;
    shiftCallGraphLayout(deltaX, deltaY);
  }

  function recomputeCallGraphContentBounds() {
    const rowPadding = 96;
    const vpW = getLayoutViewportWidth();
    const vpH = getLayoutViewportHeight();
    const bounds = computeCallGraphBounds();
    if (!bounds) {
      callGraphContentWidth = vpW;
      callGraphContentHeight = vpH;
      return;
    }

    callGraphContentWidth = Math.max(vpW, bounds.maxX + rowPadding);
    callGraphContentHeight = Math.max(vpH, bounds.maxY + 64);
  }

  function scrollCallGraphToVisibleCenter() {
    const viewport = getGraphViewport();
    if (!viewport || !isCallGraphLikeView()) {
      return;
    }

    const bounds = computeCallGraphBounds();
    if (!bounds) {
      return;
    }

    const pad = 32;
    const maxScrollLeft = Math.max(0, canvas.clientWidth - viewport.clientWidth);
    const maxScrollTop = Math.max(0, canvas.clientHeight - viewport.clientHeight);
    const contentW = bounds.maxX - bounds.minX;
    const contentH = bounds.maxY - bounds.minY;

    if (contentW + pad * 2 <= viewport.clientWidth) {
      viewport.scrollLeft = Math.min(
        maxScrollLeft,
        Math.max(0, bounds.minX - (viewport.clientWidth - contentW) / 2)
      );
    } else {
      viewport.scrollLeft = Math.min(maxScrollLeft, Math.max(0, bounds.minX - pad));
    }

    if (contentH + pad * 2 <= viewport.clientHeight) {
      viewport.scrollTop = Math.min(
        maxScrollTop,
        Math.max(0, bounds.minY - (viewport.clientHeight - contentH) / 2)
      );
    } else {
      viewport.scrollTop = Math.min(maxScrollTop, Math.max(0, bounds.minY - pad));
    }
  }

  function finalizeCallGraphLayout() {
    normalizeCallGraphLayout();
    recomputeCallGraphContentBounds();
    updateCanvasDimensions();
    scrollCallGraphToVisibleCenter();
  }

  function updateCanvasDimensions() {
    const vpW = getLayoutViewportWidth();
    const vpH = getLayoutViewportHeight();
    const drawW = isCallGraphLikeView()
      ? Math.max(vpW, callGraphContentWidth || vpW)
      : vpW;
    const drawH = isCallGraphLikeView()
      ? Math.max(vpH, callGraphContentHeight || vpH)
      : vpH;
    canvas.style.width = `${drawW}px`;
    canvas.style.height = `${drawH}px`;
    const ratio = window.devicePixelRatio || 1;
    canvas.width = drawW * ratio;
    canvas.height = drawH * ratio;
    ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
  }

  function hitNamespaceFrame(x, y) {
    const bounds = computeNamespaceFrameBounds();
    if (!bounds) {
      return false;
    }

    if (x < bounds.minX || x > bounds.maxX || y < bounds.minY || y > bounds.maxY) {
      return false;
    }

    if (y <= bounds.minY + 30) {
      return true;
    }

    return !hitNamespaceCluster(x, y);
  }

  function drawFocusDots(node, color) {
    const baseRadius = node.isCallNode ? 34 : nodeRadius(node);
    const radius = baseRadius + 20;
    const dots = 10;
    for (let i = 0; i < dots; i++) {
      const angle = (Math.PI * 2 * i) / dots + focusPhase;
      const wobble = 1 + Math.sin(focusPhase * 2 + i * 0.65) * 0.08;
      const orbit = radius * wobble;
      const x = node.x + Math.cos(angle) * orbit;
      const y = node.y + Math.sin(angle) * orbit;
      const dotSize = 2.2 + Math.sin(focusPhase + i) * 0.6;
      ctx.beginPath();
      ctx.fillStyle = color;
      ctx.globalAlpha = 0.55 + Math.sin(focusPhase + i * 0.4) * 0.35;
      ctx.arc(x, y, dotSize, 0, Math.PI * 2);
      ctx.fill();
    }
    ctx.globalAlpha = 1;
  }

  window.addEventListener("resize", () => {
    updateCanvasDimensions();
    if (currentView === "callGraph" || currentView === "typeGraph") {
      layoutCallGraph();
      finalizeCallGraphLayout();
    } else if (currentView === "namespaceMap") {
      layoutNamespaceMap();
    }
    draw();
  });
  updateCanvasDimensions();

  function nodeRadius(node) {
    return selectedId === node.id ? 58 : 52;
  }

  function hitNamespaceCluster(x, y) {
    for (let i = namespaceClusters.length - 1; i >= 0; i--) {
      const c = namespaceClusters[i];
      if (!clusterMatchesAccessFilter(c)) {
        continue;
      }
      if (x >= c.x && x <= c.x + c.w && y >= c.y && y <= c.y + c.h) {
        return c;
      }
    }
    return null;
  }

  function hitCallGraph(x, y) {
    if (callGraphAnchor) {
      const r = 58;
      if (Math.hypot(callGraphAnchor.x - x, callGraphAnchor.y - y) <= r + 8) {
        return { kind: "callAnchor", ref: callGraphAnchor };
      }
    }
    for (const node of nodes) {
      if (!callGraphNodeMatchesAccessFilter(node)) {
        continue;
      }
      const w = 132;
      const h = 48;
      if (x >= node.x - w / 2 && x <= node.x + w / 2 && y >= node.y - h / 2 && y <= node.y + h / 2) {
        return { kind: "callNode", ref: node };
      }
    }
    return null;
  }

  function hitTestArchitecture(x, y) {
    let best = null;
    let bestDist = Infinity;
    for (const node of nodes) {
      const r = nodeRadius(node) + 8;
      const labelY = node.y + r + 22;
      const inCircle = Math.hypot(node.x - x, node.y - y) <= r + 6;
      const inLabel =
        Math.abs(x - node.x) <= 90 &&
        y >= labelY - 14 &&
        y <= labelY + 14;
      if (inCircle || inLabel) {
        const d = Math.hypot(node.x - x, node.y - y);
        if (d < bestDist) {
          bestDist = d;
          best = node;
        }
      }
    }
    return best ? { kind: "archNode", ref: best } : null;
  }

  function hitTestPointer(x, y) {
    if (currentView === "namespaceMap") {
      if (namespaceProjectAnchor) {
        const r = 58;
        if (
          Math.hypot(namespaceProjectAnchor.x - x, namespaceProjectAnchor.y - y) <=
          r + 8
        ) {
          return { kind: "nsAnchor", ref: namespaceProjectAnchor };
        }
      }

      const cluster = hitNamespaceCluster(x, y);
      if (cluster && clusterMatchesAccessFilter(cluster)) {
        return { kind: "nsCluster", ref: cluster };
      }

      if (hitNamespaceFrame(x, y)) {
        return { kind: "nsFrame", ref: namespaceClusters };
      }

      return null;
    }
    if (currentView === "callGraph" || currentView === "typeGraph") {
      return hitCallGraph(x, y);
    }
    if (currentView === "architecture" || currentView === "impactGraph") {
      const arch = hitTestArchitecture(x, y);
      return arch;
    }
    return null;
  }

  function rebuildCycleHighlightSets() {
    cycleEdgeKeys.clear();
    cycleNodeIds.clear();
    const cycles =
      activeCycleIndex >= 0 && circularDependencies[activeCycleIndex]
        ? [circularDependencies[activeCycleIndex]]
        : circularDependencies;
    for (const cycle of cycles) {
      for (const edge of cycle.edges || []) {
        cycleEdgeKeys.add(`${edge.sourceProjectId}\u0001${edge.targetProjectId}`);
        cycleNodeIds.add(edge.sourceProjectId);
        cycleNodeIds.add(edge.targetProjectId);
      }
      for (const id of cycle.projectIds || []) {
        cycleNodeIds.add(id);
      }
    }
  }

  function isCycleEdge(sourceId, targetId) {
    return cycleEdgeKeys.has(`${sourceId}\u0001${targetId}`);
  }

  function buildGraph(data) {
    if (!data || !data.projects) {
      return;
    }

    currentView = "architecture";
    circularDependencies = data.circularDependencies || [];
    activeCycleIndex = circularDependencies.length > 0 ? -1 : -1;
    rebuildCycleHighlightSets();
    callGraphTitle = "";
    callGraphAnchor = null;
    namespaceProjectAnchor = null;
    namespaceAnchorPinned = false;
    namespaceAccessFilters.clear();
    callGraphAccessFilters.clear();
    restoreArchitectureLegend();
    pinned.clear();
    selectedId = null;
    highlightLegend(null);

    nodes = (data.projects || []).map((p, index) => ({
      id: p.id,
      name: p.name,
      language: p.language,
      targetFramework: p.targetFramework || "",
      solutionFolder: p.solutionFolderPath || "",
      x: 140 + (index % 5) * 180,
      y: 140 + Math.floor(index / 5) * 160,
      vx: 0,
      vy: 0
    }));

    edges = (data.references || []).map(r => ({
      source: r.sourceProjectId,
      target: r.targetProjectId
    }));

    layoutKey = data.layoutKey || "";
    callGraphContentWidth = 0;
    updateCanvasDimensions();
    applyLayoutPositions(data.layoutPositions);
    const hasSavedLayout = data.layoutPositions && Object.keys(data.layoutPositions).length > 0;
    if (!hasSavedLayout) {
      for (let i = 0; i < 160; i++) {
        tickPhysics(0.82);
      }
    }

    updateStatusBar();
    draw();
  }

  function buildCallGraph(data) {
    currentView = "callGraph";
    callGraphTitle = data.title || data.projectName || "Call graph";
    cancelAnimationFrame(animationFrame);
    pinned.clear();
    pinnedCallNodes.clear();
    callAnchorPinned = false;
    callGraphAccessFilters.clear();
    selectedId = null;
    setCallGraphLegend();

    const lang = data.projectLanguage || 1;
    callGraphContentWidth = 0;
    callGraphContentHeight = 0;
    callGraphAnchor = {
      x: getLayoutViewportWidth() / 2,
      y: 118,
      name: data.projectName || "Project",
      language: lang,
      targetFramework: data.targetFramework || ""
    };

    layoutKey = data.layoutKey || "";
    nodes = (data.nodes || []).map((n) => ({
      id: n.id,
      name: n.label,
      subtitle: n.subtitle || "",
      depth: n.depth || 0,
      accessibility: n.accessibility || "unknown",
      sourceFilePath: n.sourceFilePath || "",
      sourceLine: n.sourceLine || 0,
      x: 0,
      y: 0,
      language: lang,
      isCallNode: true
    }));

    edges = (data.edges || []).map((e) => ({
      source: e.sourceId,
      target: e.targetId
    }));

    layoutCallGraph();
    applyLayoutPositions(data.layoutPositions);
    finalizeCallGraphLayout();
    updateStatusBar();
    animate();
  }

  function buildTypeGraph(data) {
    currentView = "typeGraph";
    callGraphTitle = data.projectName || "Type graph";
    cancelAnimationFrame(animationFrame);
    pinned.clear();
    pinnedCallNodes.clear();
    callAnchorPinned = false;
    callGraphAccessFilters.clear();
    selectedId = null;
    restoreArchitectureLegend();

    const lang = data.projectLanguage || 1;
    callGraphContentWidth = 0;
    callGraphContentHeight = 0;
    callGraphAnchor = {
      x: getLayoutViewportWidth() / 2,
      y: 118,
      name: data.projectName || "Project",
      language: lang,
      targetFramework: data.targetFramework || ""
    };

    layoutKey = data.layoutKey || "";
    nodes = (data.nodes || []).map((n) => ({
      id: n.id,
      name: n.label,
      subtitle: n.subtitle || "",
      depth: 0,
      accessibility: n.accessibility || "unknown",
      sourceFilePath: n.sourceFilePath || "",
      sourceLine: n.sourceLine || 0,
      x: 0,
      y: 0,
      language: lang,
      isCallNode: true
    }));

    edges = (data.edges || []).map((e) => ({
      source: e.sourceId,
      target: e.targetId
    }));

    layoutCallGraph();
    applyLayoutPositions(data.layoutPositions);
    finalizeCallGraphLayout();
    updateStatusBar();
    animate();
  }

  function buildImpactGraph(data) {
    currentView = "impactGraph";
    impactGraphTitle = data.symbolLabel || "";
    circularDependencies = [];
    activeCycleIndex = -1;
    rebuildCycleHighlightSets();
    callGraphTitle = "";
    callGraphAnchor = null;
    restoreArchitectureLegend();
    pinned.clear();
    selectedId = null;

    nodes = (data.projects || []).map((p, index) => ({
      id: p.id,
      name: p.name,
      language: p.language,
      targetFramework: p.targetFramework || "",
      usageCount: p.usageCount || 0,
      isDefinition: !!p.isDefinition,
      x: 140 + (index % 5) * 180,
      y: 140 + Math.floor(index / 5) * 160,
      vx: 0,
      vy: 0
    }));

    edges = (data.references || []).map((r) => ({
      source: r.sourceProjectId,
      target: r.targetProjectId
    }));

    layoutKey = data.layoutKey || "";
    callGraphContentWidth = 0;
    updateCanvasDimensions();
    applyLayoutPositions(data.layoutPositions);
    const hasSavedLayout = data.layoutPositions && Object.keys(data.layoutPositions).length > 0;
    if (!hasSavedLayout) {
      for (let i = 0; i < 120; i++) {
        tickPhysics(0.82);
      }
    }
    updateStatusBar();
    draw();
  }

  function buildNamespaceMap(data) {
    currentView = "namespaceMap";
    cancelAnimationFrame(animationFrame);
    pinnedNamespaceClusters.clear();
    selectedNamespaceClusterId = null;
    namespaceAccessFilters.clear();
    namespaceAnchorPinned = false;
    namespaceClusters = (data.clusters || []).map((c) => ({ ...c, x: 0, y: 0, w: 140, h: 78 }));
    namespaceEdges = data.clusterEdges || [];
    namespaceRoot = data.rootNamespace || data.projectName || "";
    layoutKey = data.layoutKey || "";
    const lang = data.projectLanguage || 1;
    namespaceProjectAnchor = {
      x: getLayoutViewportWidth() / 2,
      y: 118,
      name: data.projectName || namespaceRoot || "Project",
      language: lang,
      targetFramework: data.targetFramework || ""
    };
    setNamespaceLegend();
    layoutNamespaceMap();
    applyLayoutPositions(data.layoutPositions);
    updateCanvasDimensions();
    updateStatusBar();
    animate();
  }

  function layoutNamespaceMap() {
    const cols = Math.max(1, Math.ceil(Math.sqrt(namespaceClusters.length)));
    const cellW = 148;
    const cellH = 82;
    const gapX = 28;
    const gapY = 36;
    const startX = 72;
    const startY = 210;
    namespaceClusters.forEach((cluster, index) => {
      if (pinnedNamespaceClusters.has(cluster.id)) {
        return;
      }
      const col = index % cols;
      const row = Math.floor(index / cols);
      cluster.x = startX + col * (cellW + gapX);
      cluster.y = startY + row * (cellH + gapY);
      cluster.w = cellW;
      cluster.h = cellH;
    });
  }

  function layoutCallGraph() {
    const byDepth = {};
    for (const node of nodes) {
      if (!byDepth[node.depth]) {
        byDepth[node.depth] = [];
      }
      byDepth[node.depth].push(node);
    }

    const depths = Object.keys(byDepth)
      .map((d) => Number(d))
      .sort((a, b) => a - b);

    const horizontalGap = 148;
    const rowPadding = 96;
    const primaryRowY = 196;
    const secondaryRowStartY = 292;
    const verticalGap = 104;

    let maxRowWidth = 0;
    for (const depth of depths) {
      const count = byDepth[depth].length;
      maxRowWidth = Math.max(maxRowWidth, Math.max(0, (count - 1) * horizontalGap));
    }

    const layoutWidth = Math.max(getLayoutViewportWidth(), maxRowWidth + rowPadding * 2);
    const centerX = layoutWidth / 2;

    depths.forEach((depth) => {
      const row = byDepth[depth].slice().sort((a, b) => a.name.localeCompare(b.name));
      const rowWidth = Math.max(0, (row.length - 1) * horizontalGap);
      const startX = centerX - rowWidth / 2;
      row.forEach((node, index) => {
        if (pinnedCallNodes.has(node.id)) {
          return;
        }
        node.x = startX + index * horizontalGap;
        if (depth === 0) {
          node.y = primaryRowY;
        } else {
          const depthIndex = depths.indexOf(depth);
          const secondaryIndex = Math.max(0, depthIndex - (depths[0] === 0 ? 1 : 0));
          node.y = secondaryRowStartY + secondaryIndex * verticalGap;
        }
      });
    });

    callGraphContentWidth = layoutWidth;

    if (callGraphAnchor && !callAnchorPinned) {
      callGraphAnchor.x = centerX;
      callGraphAnchor.y = 118;
    }

    finalizeCallGraphLayout();
  }

  function clearSelection() {
    selectedId = null;
    highlightLegend(null);
    updateStatusBar();
    draw();
  }

  function isFixed(node) {
    return pinned.has(node.id) || node === dragNode;
  }

  function tickPhysics(damping) {
    const centerX = canvas.clientWidth / 2;
    const centerY = canvas.clientHeight / 2;

    for (const node of nodes) {
      if (isFixed(node)) continue;
      node.vx += (centerX - node.x) * 0.0008;
      node.vy += (centerY - node.y) * 0.0008;
    }

    for (let i = 0; i < nodes.length; i++) {
      for (let j = i + 1; j < nodes.length; j++) {
        const a = nodes[i];
        const b = nodes[j];
        const dx = b.x - a.x;
        const dy = b.y - a.y;
        const dist = Math.max(Math.hypot(dx, dy), 1);
        const force = 14000 / (dist * dist);
        const fx = (dx / dist) * force;
        const fy = (dy / dist) * force;
        if (!isFixed(a)) {
          a.vx -= fx;
          a.vy -= fy;
        }
        if (!isFixed(b)) {
          b.vx += fx;
          b.vy += fy;
        }
      }
    }

    for (const edge of edges) {
      const source = nodes.find(n => n.id === edge.source);
      const target = nodes.find(n => n.id === edge.target);
      if (!source || !target) continue;
      const dx = target.x - source.x;
      const dy = target.y - source.y;
      const dist = Math.max(Math.hypot(dx, dy), 1);
      const force = (dist - 220) * 0.018;
      const fx = (dx / dist) * force;
      const fy = (dy / dist) * force;
      if (!isFixed(source)) {
        source.vx += fx;
        source.vy += fy;
      }
      if (!isFixed(target)) {
        target.vx -= fx;
        target.vy -= fy;
      }
    }

    for (const node of nodes) {
      if (isFixed(node)) {
        node.vx = 0;
        node.vy = 0;
        continue;
      }
      node.vx *= damping;
      node.vy *= damping;
      node.x += node.vx;
      node.y += node.vy;
      node.x = Math.max(80, Math.min(canvas.clientWidth - 80, node.x));
      node.y = Math.max(80, Math.min(canvas.clientHeight - 80, node.y));
    }
  }

  function callNodeBoxHalfSize() {
    return { halfW: 66, halfH: 24 };
  }

  function anchorEdgePoint(anchor, targetX, targetY) {
    const radius = 52;
    const dx = targetX - anchor.x;
    const dy = targetY - anchor.y;
    const len = Math.hypot(dx, dy) || 1;
    return {
      x: anchor.x + (dx / len) * (radius + 2),
      y: anchor.y + (dy / len) * (radius + 2)
    };
  }

  function callBoxEdgePoint(node, towardX, towardY) {
    const { halfW, halfH } = callNodeBoxHalfSize();
    const dx = towardX - node.x;
    const dy = towardY - node.y;
    const len = Math.hypot(dx, dy) || 1;
    const ux = dx / len;
    const uy = dy / len;
    const scale = Math.min(
      halfW / Math.max(Math.abs(ux), 0.001),
      halfH / Math.max(Math.abs(uy), 0.001)
    );
    return { x: node.x + ux * scale, y: node.y + uy * scale };
  }

  function drawCallEdge(source, target, options) {
    const fromAnchor = options && options.fromAnchor;
    const dotted = options && options.dotted;
    const edgeColor = dotted
      ? css("--dg-accent", "#0078d4") + (theme === "dark" ? "88" : "55")
      : css("--dg-edge", "#cbd5e1");

    let start;
    let end;
    if (fromAnchor) {
      start = anchorEdgePoint(source, target.x, target.y);
      end = callBoxEdgePoint(target, source.x, source.y);
    } else {
      start = callBoxEdgePoint(source, target.x, target.y);
      end = callBoxEdgePoint(target, source.x, source.y);
    }

    ctx.strokeStyle = edgeColor;
    ctx.lineWidth = dotted ? 1.5 : 2;
    if (dotted) {
      ctx.setLineDash([6, 5]);
    }
    ctx.beginPath();
    ctx.moveTo(start.x, start.y);
    ctx.lineTo(end.x, end.y);
    ctx.stroke();
    ctx.setLineDash([]);

    if (dotted) {
      return;
    }

    const dx = end.x - start.x;
    const dy = end.y - start.y;
    const len = Math.hypot(dx, dy) || 1;
    const ux = dx / len;
    const uy = dy / len;
    const head = 7;
    ctx.fillStyle = css("--dg-edge-head", "#94a3b8");
    ctx.beginPath();
    ctx.moveTo(end.x, end.y);
    ctx.lineTo(end.x - ux * head - uy * 3.5, end.y - uy * head + ux * 3.5);
    ctx.lineTo(end.x - ux * head + uy * 3.5, end.y - uy * head - ux * 3.5);
    ctx.closePath();
    ctx.fill();
  }

  function drawAnchorCircle(anchor) {
    const style = languageStyle[anchor.language] || languageStyle[0];
    const radius = 52;
    drawFocusDots({ x: anchor.x, y: anchor.y }, style.color);

    ctx.beginPath();
    ctx.fillStyle = theme === "dark" ? style.color + "33" : style.ring;
    ctx.arc(anchor.x, anchor.y, radius + 8, 0, Math.PI * 2);
    ctx.fill();

    ctx.beginPath();
    ctx.fillStyle = css("--dg-node-fill", "#ffffff");
    ctx.arc(anchor.x, anchor.y, radius, 0, Math.PI * 2);
    ctx.fill();

    ctx.lineWidth = 2;
    ctx.strokeStyle = style.color;
    ctx.stroke();

    ctx.fillStyle = style.color;
    ctx.font = "bold 18px Segoe UI Variable, Segoe UI, sans-serif";
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    ctx.fillText(style.label, anchor.x, anchor.y - 8);

    ctx.fillStyle = css("--dg-node-sub", "#616161");
    ctx.font = "11px Segoe UI Variable, Segoe UI, sans-serif";
    const tf = anchor.targetFramework || "";
    ctx.fillText(tf.length > 18 ? tf.slice(0, 16) + "…" : tf, anchor.x, anchor.y + 14);

    ctx.fillStyle = css("--dg-node-label", "#1e1e1e");
    ctx.font = "600 13px Segoe UI Variable, Segoe UI, sans-serif";
    ctx.fillText(anchor.name, anchor.x, anchor.y + radius + 22);
  }

  function drawCallNodeBox(node) {
    const width = 132;
    const height = 48;
    const x = node.x - width / 2;
    const y = node.y - height / 2;
    const fill = css("--dg-node-fill", "#ffffff");
    const border = css("--dg-card-border", css("--dg-border", "#b8b8b8"));
    const accent = accessibilityAccentColor(node.accessibility);

    ctx.fillStyle = fill;
    ctx.strokeStyle = border;
    ctx.lineWidth = 1;
    if (ctx.roundRect) {
      ctx.beginPath();
      ctx.roundRect(x, y, width, height, 6);
      ctx.fill();
      ctx.stroke();
    } else {
      ctx.fillRect(x, y, width, height);
      ctx.strokeRect(x, y, width, height);
    }

    ctx.fillStyle = accent;
    ctx.fillRect(x, y, width, 3);

    ctx.fillStyle = css("--dg-node-label", "#1e1e1e");
    ctx.font = "600 12px Segoe UI Variable, Segoe UI, sans-serif";
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    ctx.fillText(node.name, node.x, node.y - 4);

    ctx.fillStyle = css("--dg-node-sub", "#616161");
    ctx.font = "10px Segoe UI Variable, Segoe UI, sans-serif";
    const subtitle =
      node.subtitle.length > 20 ? node.subtitle.slice(0, 18) + "…" : node.subtitle;
    ctx.fillText(subtitle, node.x, node.y + 12);
  }

  function drawNamespaceMapView() {
    const visible = visibleNamespaceClusters();
    if (namespaceClusters.length === 0) {
      return;
    }

    if (namespaceProjectAnchor) {
      for (const cluster of visible) {
        drawCallEdge(
          namespaceProjectAnchor,
          { x: cluster.x + cluster.w / 2, y: cluster.y + cluster.h / 2 },
          { dotted: true, fromAnchor: true }
        );
      }
    }

    const bounds = computeNamespaceFrameBounds(visible.length ? visible : namespaceClusters);
    if (bounds) {
      const { minX, minY, maxX, maxY } = bounds;
      ctx.fillStyle = css("--dg-surface", "#ffffff");
      ctx.strokeStyle = css("--dg-card-border", css("--dg-border", "#b8b8b8"));
      ctx.lineWidth = theme === "dark" ? 2 : 2.5;
      if (ctx.roundRect) {
        ctx.beginPath();
        ctx.roundRect(minX, minY, maxX - minX, maxY - minY, 10);
        ctx.fill();
        ctx.stroke();
      } else {
        ctx.fillRect(minX, minY, maxX - minX, maxY - minY);
        ctx.strokeRect(minX, minY, maxX - minX, maxY - minY);
      }

      ctx.fillStyle = css("--dg-node-label", "#1e1e1e");
      ctx.font = "600 14px Segoe UI Variable, Segoe UI, sans-serif";
      ctx.textAlign = "left";
      ctx.fillText(namespaceRoot, minX + 14, minY + 22);
    }

    for (const edge of namespaceEdges) {
      const source = namespaceClusters.find((c) => c.id === edge.sourceId);
      const target = namespaceClusters.find((c) => c.id === edge.targetId);
      if (!source || !target) continue;
      if (!clusterMatchesAccessFilter(source) || !clusterMatchesAccessFilter(target)) {
        continue;
      }
      drawCallEdge(
        { x: source.x + source.w / 2, y: source.y + source.h / 2 },
        { x: target.x + target.w / 2, y: target.y + target.h / 2 }
      );
    }

    for (const cluster of visible) {
      const selected = selectedNamespaceClusterId === cluster.id;
      ctx.fillStyle = css("--dg-node-fill", "#ffffff");
      ctx.strokeStyle = selected ? css("--dg-accent", "#0078d4") : css("--dg-card-border", css("--dg-border", "#b8b8b8"));
      ctx.lineWidth = selected ? 2.5 : theme === "dark" ? 1.5 : 2;
      if (ctx.roundRect) {
        ctx.beginPath();
        ctx.roundRect(cluster.x, cluster.y, cluster.w, cluster.h, 6);
        ctx.fill();
        ctx.stroke();
      } else {
        ctx.fillRect(cluster.x, cluster.y, cluster.w, cluster.h);
        ctx.strokeRect(cluster.x, cluster.y, cluster.w, cluster.h);
      }

      ctx.fillStyle = css("--dg-node-label", "#1e1e1e");
      ctx.font = "600 12px Segoe UI Variable, Segoe UI, sans-serif";
      ctx.textAlign = "center";
      ctx.fillText(cluster.label, cluster.x + cluster.w / 2, cluster.y + 22);
      ctx.fillStyle = css("--dg-node-sub", "#616161");
      ctx.font = "10px Segoe UI Variable, Segoe UI, sans-serif";
      ctx.fillText(`${cluster.typeCount} ${t("typesCount", "tipos")}`, cluster.x + cluster.w / 2, cluster.y + 38);
      const sample = (cluster.samples || []).slice(0, 2).join(", ");
      if (sample) {
        ctx.fillText(sample.length > 22 ? sample.slice(0, 20) + "…" : sample, cluster.x + cluster.w / 2, cluster.y + 54);
      }
    }

    if (namespaceProjectAnchor) {
      drawAnchorCircle(namespaceProjectAnchor);
    }
  }

  function drawCallGraphView() {
    const visible = visibleCallGraphNodes();
    const visibleIds = new Set(visible.map((n) => n.id));

    if (callGraphAnchor) {
      for (const node of visible) {
        if (currentView === "callGraph" && node.depth !== 0) {
          continue;
        }
        drawCallEdge(callGraphAnchor, node, { dotted: true, fromAnchor: true });
      }
    }

    for (const edge of edges) {
      if (!visibleIds.has(edge.source) || !visibleIds.has(edge.target)) {
        continue;
      }
      const source = nodes.find((n) => n.id === edge.source);
      const target = nodes.find((n) => n.id === edge.target);
      if (!source || !target) {
        continue;
      }
      drawCallEdge(source, target);
    }

    for (const node of visible) {
      drawCallNodeBox(node);
    }

    if (callGraphAnchor) {
      drawAnchorCircle(callGraphAnchor);
    }
  }

  function draw() {
    const drawW = canvas.width / (window.devicePixelRatio || 1);
    const drawH = canvas.height / (window.devicePixelRatio || 1);
    ctx.clearRect(0, 0, drawW, drawH);

    if (currentView === "namespaceMap") {
      drawNamespaceMapView();
      return;
    }

    if (currentView === "callGraph" || currentView === "typeGraph") {
      drawCallGraphView();
      return;
    }

    const edgeColor = css("--dg-edge", "#cbd5e1");
    const edgeHead = css("--dg-edge-head", "#94a3b8");
    const nodeFill = css("--dg-node-fill", "#ffffff");
    const labelColor = css("--dg-node-label", "#111827");
    const subColor = css("--dg-node-sub", "#6b7280");

    for (const edge of edges) {
      const source = nodes.find(n => n.id === edge.source);
      const target = nodes.find(n => n.id === edge.target);
      if (!source || !target) continue;

      const dx = target.x - source.x;
      const dy = target.y - source.y;
      const len = Math.hypot(dx, dy) || 1;
      const ux = dx / len;
      const uy = dy / len;
      const startX = source.x + ux * 52;
      const startY = source.y + uy * 52;
      const endX = target.x - ux * 52;
      const endY = target.y - uy * 52;

      const onCycle = isCycleEdge(source.id, target.id);
      ctx.strokeStyle = onCycle ? css("--dg-cycle-edge", "#d13438") : edgeColor;
      ctx.lineWidth = onCycle ? 3.5 : 2;
      ctx.beginPath();
      ctx.moveTo(startX, startY);
      ctx.lineTo(endX, endY);
      ctx.stroke();

      const head = 8;
      ctx.fillStyle = onCycle ? css("--dg-cycle-edge", "#d13438") : edgeHead;
      ctx.beginPath();
      ctx.moveTo(endX, endY);
      ctx.lineTo(endX - ux * head - uy * 4, endY - uy * head + ux * 4);
      ctx.lineTo(endX - ux * head + uy * 4, endY - uy * head - ux * 4);
      ctx.closePath();
      ctx.fill();
    }

    if (nodes.length === 0) {
      ctx.fillStyle = css("--dg-text", "#616161");
      ctx.font = "13px Segoe UI Variable, Segoe UI, sans-serif";
      ctx.textAlign = "center";
      ctx.textBaseline = "middle";
      ctx.fillText(
        t("emptyGraphHint", "O grafo ocupa toda a área até você selecionar um projeto."),
        drawW / 2,
        drawH / 2
      );
      return;
    }

    for (const node of nodes) {
      const style = languageStyle[node.language] || languageStyle[0];
      const radius = nodeRadius(node);
      const ring = theme === "dark" ? style.color + "33" : style.ring;
      const isSelected = selectedId === node.id;
      const onCycle = cycleNodeIds.has(node.id);

      if (isSelected) {
        drawFocusDots(node, style.color);
      } else if (onCycle) {
        drawFocusDots(node, css("--dg-cycle-edge", "#d13438"));
      }

      ctx.beginPath();
      ctx.fillStyle = ring;
      ctx.arc(node.x, node.y, radius + 8, 0, Math.PI * 2);
      ctx.fill();

      ctx.beginPath();
      ctx.fillStyle = nodeFill;
      ctx.arc(node.x, node.y, radius, 0, Math.PI * 2);
      ctx.fill();

      ctx.lineWidth = isSelected ? 4 : onCycle ? 3 : node.isDefinition ? 4 : 2;
      ctx.strokeStyle = onCycle
        ? css("--dg-cycle-edge", "#d13438")
        : node.isDefinition
          ? css("--dg-accent", "#0e639c")
          : style.color;
      ctx.stroke();

      if (isSelected) {
        ctx.beginPath();
        ctx.fillStyle = theme === "dark" ? style.color + "44" : style.color + "22";
        ctx.arc(node.x, node.y, radius - 4, 0, Math.PI * 2);
        ctx.fill();
      }

      ctx.fillStyle = style.color;
      ctx.font = isSelected ? "bold 20px Segoe UI Variable, Segoe UI, sans-serif" : "bold 18px Segoe UI Variable, Segoe UI, sans-serif";
      ctx.textAlign = "center";
      ctx.textBaseline = "middle";
      ctx.fillText(style.label, node.x, node.y - 8);

      ctx.fillStyle = subColor;
      ctx.font = "11px Segoe UI Variable, Segoe UI, sans-serif";
      let subtitle = node.targetFramework || "Class Library";
      if (currentView === "impactGraph" && node.usageCount > 0) {
        subtitle = `${node.usageCount} ${t("usages", "uso(s)")}`;
      } else if (currentView === "architecture" && node.solutionFolder) {
        subtitle = node.solutionFolder.length > 18 ? node.solutionFolder.slice(0, 16) + "…" : node.solutionFolder;
      }
      ctx.fillText(subtitle.length > 18 ? subtitle.slice(0, 16) + "…" : subtitle, node.x, node.y + 14);

      ctx.fillStyle = labelColor;
      ctx.font = "600 13px Segoe UI Variable, Segoe UI, sans-serif";
      ctx.fillText(node.name, node.x, node.y + radius + 22);
    }
  }

  function animate() {
    focusPhase += 0.04;
    if ((currentView === "architecture" || currentView === "impactGraph") && !dragNode) {
      tickPhysics(0.9);
    }
    draw();
    if (
      currentView === "architecture" ||
      currentView === "impactGraph" ||
      currentView === "callGraph" ||
      currentView === "typeGraph" ||
      currentView === "namespaceMap"
    ) {
      animationFrame = requestAnimationFrame(animate);
    }
  }

  function postNodeClick(projectId) {
    const node = nodes.find((n) => n.id === projectId);
    if (node) {
      highlightLegend(node.language);
      updateStatusBar();
    }
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(JSON.stringify({ type: "nodeClick", projectId: projectId }));
    }
  }

  function postBackgroundClick() {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(JSON.stringify({ type: "backgroundClick" }));
    }
  }

  function postNamespaceClusterClick(clusterId) {
    selectedNamespaceClusterId = clusterId;
    updateStatusBar();
    draw();
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(
        JSON.stringify({ type: "namespaceClusterClick", clusterId: clusterId })
      );
    }
  }

  function applyDrag(hit, x, y) {
    if (!hit) return;
    if (hit.kind === "archNode") {
      hit.ref.x = x;
      hit.ref.y = y;
      hit.ref.vx = 0;
      hit.ref.vy = 0;
      return;
    }
    if (hit.kind === "callNode") {
      hit.ref.x = x;
      hit.ref.y = y;
      return;
    }
    if (hit.kind === "callAnchor" || hit.kind === "nsAnchor") {
      hit.ref.x = x;
      hit.ref.y = y;
      return;
    }
    if (hit.kind === "nsCluster" && pointerDown) {
      hit.ref.x = x - (pointerDown.offsetX ?? hit.ref.w / 2);
      hit.ref.y = y - (pointerDown.offsetY ?? hit.ref.h / 2);
    }
    if (hit.kind === "nsFrame" && pointerDown) {
      const dx = x - (pointerDown.lastX ?? pointerDown.x);
      const dy = y - (pointerDown.lastY ?? pointerDown.y);
      for (const cluster of namespaceClusters) {
        cluster.x += dx;
        cluster.y += dy;
      }
      pointerDown.lastX = x;
      pointerDown.lastY = y;
    }
  }

  function pinDragTarget(hit) {
    if (!hit) return;
    if (hit.kind === "archNode") pinned.add(hit.ref.id);
    if (hit.kind === "callNode") pinnedCallNodes.add(hit.ref.id);
    if (hit.kind === "callAnchor") callAnchorPinned = true;
    if (hit.kind === "nsAnchor") namespaceAnchorPinned = true;
    if (hit.kind === "nsCluster") pinnedNamespaceClusters.add(hit.ref.id);
    if (hit.kind === "nsFrame") {
      for (const cluster of namespaceClusters) {
        pinnedNamespaceClusters.add(cluster.id);
      }
    }
  }

  function postCallNodeClick(nodeId) {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(
        JSON.stringify({ type: "callNodeClick", nodeId: nodeId })
      );
    }
  }

  function postTypeNodeClick(nodeId) {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(
        JSON.stringify({ type: "typeNodeClick", nodeId: nodeId })
      );
    }
  }

  function canvasPoint(event) {
    const rect = canvas.getBoundingClientRect();
    return {
      x: event.clientX - rect.left,
      y: event.clientY - rect.top
    };
  }

  canvas.addEventListener("mousedown", (event) => {
    const { x, y } = canvasPoint(event);
    const hit = hitTestPointer(x, y);
    pointerDown = { x, y, hit, moved: false };
    if (hit && hit.kind === "nsCluster") {
      pointerDown.offsetX = x - hit.ref.x;
      pointerDown.offsetY = y - hit.ref.y;
    }
    if (hit && hit.kind === "nsFrame") {
      pointerDown.lastX = x;
      pointerDown.lastY = y;
    }
    if (hit) {
      dragNode = hit.kind === "archNode" ? hit.ref : hit;
      if (hit.kind === "archNode") {
        hit.ref.vx = 0;
        hit.ref.vy = 0;
      }
      canvas.style.cursor = "grabbing";
    }
  });

  canvas.addEventListener("mousemove", (event) => {
    const { x, y } = canvasPoint(event);
    if (dragNode) {
      const hit = pointerDown && pointerDown.hit ? pointerDown.hit : null;
      if (hit) {
        applyDrag(hit, x, y);
      } else if (dragNode.x !== undefined && dragNode.kind === undefined) {
        dragNode.x = x;
        dragNode.y = y;
        dragNode.vx = 0;
        dragNode.vy = 0;
      }
      if (pointerDown) {
        pointerDown.moved =
          pointerDown.moved ||
          Math.hypot(x - pointerDown.x, y - pointerDown.y) > 4;
      }
      draw();
      return;
    }
    canvas.style.cursor = hitTestPointer(x, y) ? "grab" : "default";
  });

  canvas.addEventListener("mouseup", (event) => {
    const { x, y } = canvasPoint(event);
    if (pointerDown && pointerDown.hit) {
      const moved =
        pointerDown.moved || Math.hypot(x - pointerDown.x, y - pointerDown.y) > 4;
      const hit = pointerDown.hit;
      if (!moved) {
        if (hit.kind === "archNode") {
          selectedId = hit.ref.id;
          draw();
          postNodeClick(hit.ref.id);
        } else if (hit.kind === "nsCluster") {
          postNamespaceClusterClick(hit.ref.id);
        } else if (hit.kind === "callNode") {
          selectedId = hit.ref.id;
          updateStatusBar();
          draw();
          if (currentView === "typeGraph") {
            postTypeNodeClick(hit.ref.id);
          } else {
            postCallNodeClick(hit.ref.id);
          }
        }
      } else {
        pinDragTarget(hit);
        postSaveLayout();
      }
    } else if (pointerDown && !pointerDown.hit) {
      const moved =
        pointerDown.moved || Math.hypot(x - pointerDown.x, y - pointerDown.y) > 4;
      if (!moved && selectedId && currentView === "architecture") {
        clearSelection();
        postBackgroundClick();
      }
    }
    dragNode = null;
    pointerDown = null;
    canvas.style.cursor = "default";
  });

  canvas.addEventListener("mouseleave", () => {
    if (dragNode && pointerDown && pointerDown.hit) {
      const moved = pointerDown.moved;
      pinDragTarget(pointerDown.hit);
      if (moved) {
        postSaveLayout();
      }
    }
    dragNode = null;
    pointerDown = null;
    canvas.style.cursor = "default";
  });

  canvas.addEventListener("dblclick", (event) => {
    const { x, y } = canvasPoint(event);
    const hit = hitTestPointer(x, y);
    if (!hit) return;
    if (hit.kind === "archNode") pinned.delete(hit.ref.id);
    if (hit.kind === "callNode") pinnedCallNodes.delete(hit.ref.id);
    if (hit.kind === "callAnchor") callAnchorPinned = false;
    if (hit.kind === "nsAnchor") namespaceAnchorPinned = false;
    if (hit.kind === "nsCluster") pinnedNamespaceClusters.delete(hit.ref.id);
    if (currentView === "callGraph" || currentView === "typeGraph") layoutCallGraph();
    if (currentView === "namespaceMap") layoutNamespaceMap();
    draw();
  });

  function mermaidEscapeLabel(text) {
    return String(text || " ")
      .replace(/"/g, "#quot;")
      .replace(/\n/g, " ")
      .replace(/\]/g, "#93;");
  }

  function safeExportBaseName(raw) {
    const cleaned = String(raw || "graph")
      .replace(/[^\w\-]+/g, "-")
      .replace(/^-+|-+$/g, "")
      .slice(0, 72);
    return cleaned || "dotnet-graph";
  }

  function getExportBaseName() {
    if (currentView === "callGraph") {
      return safeExportBaseName("call-graph-" + (callGraphTitle || "project"));
    }
    if (currentView === "typeGraph") {
      return safeExportBaseName("type-graph-" + (callGraphTitle || "project"));
    }
    if (currentView === "namespaceMap") {
      return safeExportBaseName("namespaces-" + (namespaceRoot || "project"));
    }
    if (currentView === "impactGraph") {
      return safeExportBaseName("impact-" + (impactGraphTitle || "symbol"));
    }
    return safeExportBaseName("architecture");
  }

  function graphHasExportableContent() {
    if (currentView === "namespaceMap") {
      return namespaceClusters.length > 0;
    }
    return nodes.length > 0 || !!callGraphAnchor;
  }

  function postExportResult(payload) {
    if (!(window.chrome && window.chrome.webview)) {
      return;
    }
    window.chrome.webview.postMessage(JSON.stringify({ type: "exportResult", ...payload }));
  }

  function buildMermaidDiagram() {
    const lines = ["flowchart LR"];
    const idMap = new Map();

    function nodeId(key) {
      if (!idMap.has(key)) {
        const slug = String(key)
          .replace(/[^a-zA-Z0-9_]/g, "_")
          .slice(0, 48);
        idMap.set(key, "n" + idMap.size + "_" + (slug || "x"));
      }
      return idMap.get(key);
    }

    function declareNode(key, label, shape) {
      const id = nodeId(key);
      const text = mermaidEscapeLabel(label);
      if (shape === "circle") {
        lines.push(`  ${id}(("${text}"))`);
      } else if (shape === "stadium") {
        lines.push(`  ${id}(["${text}"])`);
      } else {
        lines.push(`  ${id}["${text}"]`);
      }
      return id;
    }

    function link(fromKey, toKey, style) {
      const from = nodeId(fromKey);
      const to = nodeId(toKey);
      lines.push(`  ${from} ${style || "-->"} ${to}`);
    }

    lines.push(`  %% Dotnet Graph · view: ${currentView}`);

    if (currentView === "namespaceMap") {
      for (const cluster of namespaceClusters) {
        const label = `${cluster.label || cluster.id} (${cluster.typeCount || 0} types)`;
        declareNode(cluster.id, label, "box");
      }
      for (const edge of namespaceEdges) {
        link(edge.sourceId, edge.targetId);
      }
      if (namespaceProjectAnchor && namespaceClusters.length > 0) {
        declareNode("__ns_anchor__", namespaceProjectAnchor.name || namespaceRoot, "circle");
        for (const cluster of namespaceClusters) {
          link("__ns_anchor__", cluster.id, "-.->");
        }
      }
      return lines.join("\n");
    }

    if (currentView === "callGraph" || currentView === "typeGraph") {
      if (callGraphAnchor) {
        const anchorLabel = `${callGraphAnchor.name}${callGraphAnchor.targetFramework ? " · " + callGraphAnchor.targetFramework : ""}`;
        declareNode("__anchor__", anchorLabel, "circle");
      }
      for (const node of nodes) {
        const label = node.subtitle ? `${node.name} — ${node.subtitle}` : node.name;
        declareNode(node.id, label, "box");
      }
      if (callGraphAnchor) {
        for (const node of nodes) {
          if (currentView === "callGraph" && node.depth !== 0) {
            continue;
          }
          link("__anchor__", node.id, "-.->");
        }
      }
      for (const edge of edges) {
        link(edge.source, edge.target);
      }
      return lines.join("\n");
    }

    for (const node of nodes) {
      const tf = node.targetFramework ? " · " + node.targetFramework : "";
      const usage =
        currentView === "impactGraph" && node.usageCount
          ? ` · ${node.usageCount} ${t("usages", "uso(s)")}`
          : "";
      const label = `${node.name}${tf}${usage}`;
      declareNode(node.id, label, node.isDefinition ? "circle" : "box");
    }
    for (const edge of edges) {
      link(edge.source, edge.target);
    }
    return lines.join("\n");
  }

  function capturePngBase64() {
    draw();
    return canvas.toDataURL("image/png");
  }

  function handleExportRequest(format) {
    if (!graphHasExportableContent()) {
      postExportResult({ success: false, format, error: "empty" });
      return;
    }

    if (format === "png") {
      try {
        postExportResult({
          success: true,
          format: "png",
          pngBase64: capturePngBase64(),
          fileName: getExportBaseName() + ".png",
          view: currentView
        });
      } catch (err) {
        postExportResult({ success: false, format: "png", error: String(err) });
      }
      return;
    }

    if (format === "mermaid") {
      try {
        postExportResult({
          success: true,
          format: "mermaid",
          mermaid: buildMermaidDiagram(),
          fileName: getExportBaseName() + ".mmd",
          view: currentView
        });
      } catch (err) {
        postExportResult({ success: false, format: "mermaid", error: String(err) });
      }
      return;
    }

    postExportResult({ success: false, format, error: "unsupported" });
  }

  function onHostMessage(event) {
    try {
      const data = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
      if (!data) return;
      if (data.type === "exportRequest") {
        handleExportRequest(data.format);
        return;
      }
      if (data.type === "setTheme") {
        applyTheme(data.theme);
        return;
      }
      if (data.type === "setLocale" && data.strings) {
        localeStrings = { ...localeStrings, ...data.strings };
        const hintEl = document.getElementById("hint");
        if (hintEl && data.strings.hint) {
          hintEl.textContent = data.strings.hint;
        }
        if (statusSummary && data.strings.statusNoGraph && nodes.length === 0) {
          statusSummary.textContent = data.strings.statusNoGraph;
        }
        updateStatusBar();
        draw();
        return;
      }
      if (data.type === "highlightLanguage") {
        highlightLegend(data.language);
        draw();
        return;
      }
      if (data.type === "clearSelection") {
        clearSelection();
        return;
      }
      if (data.type === "highlightCycle") {
        activeCycleIndex =
          typeof data.cycleIndex === "number" ? data.cycleIndex : -1;
        rebuildCycleHighlightSets();
        draw();
        return;
      }
      if (data.view === "callGraph") {
        buildCallGraph(data);
        return;
      }
      if (data.view === "namespaceMap") {
        buildNamespaceMap(data);
        return;
      }
      if (data.view === "typeGraph") {
        buildTypeGraph(data);
        return;
      }
      if (data.view === "impactGraph") {
        buildImpactGraph(data);
        cancelAnimationFrame(animationFrame);
        animate();
        return;
      }
      if (data.view === "architecture" || Array.isArray(data.projects)) {
        buildGraph(data);
        cancelAnimationFrame(animationFrame);
        animate();
      }
    } catch (err) {
      console.error(err);
    }
  }

  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener("message", onHostMessage);
  }

  applyTheme("light");
  buildGraph({ projects: [], references: [] });
})();
