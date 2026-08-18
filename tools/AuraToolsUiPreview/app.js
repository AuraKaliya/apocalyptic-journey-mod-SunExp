(() => {
  "use strict";

  const spec = window.AURA_TOOLBOX_PREVIEW_SPEC;
  const iconRoot = "../../AuraToolsExp/ModResource/Images/UI/ToolboxIcons/";
  const params = new URLSearchParams(window.location.search);
  const scenarioName = params.get("scenario") || "default";
  const captureMode = params.get("capture") === "1";
  const state = {
    scenario: scenarioName,
    category: "all",
    search: "",
    modules: [],
    scrollByCategory: new Map(),
    overlayTrigger: null
  };

  const categoryRail = document.querySelector("#category-rail");
  const moduleList = document.querySelector("#module-list");
  const emptyState = document.querySelector("#empty-state");
  const resultTitle = document.querySelector("#result-title");
  const toolboxHeader = document.querySelector(".toolbox-header");
  const searchInput = document.querySelector("#search-input");
  const clearSearch = document.querySelector("#clear-search");
  const openDirectory = document.querySelector("#open-directory");
  const scenarioSelect = document.querySelector("#scenario-select");
  const viewportOutput = document.querySelector("#viewport-output");
  const overlay = document.querySelector("#settings-overlay");
  const overlayTitle = document.querySelector("#overlay-title");
  const overlaySummary = document.querySelector("#overlay-summary");
  const closeOverlayButton = document.querySelector("#close-overlay");
  const toast = document.querySelector("#toast");
  let toastTimer = 0;

  applyVisualSpec();
  document.body.classList.toggle("capture-mode", captureMode);
  scenarioSelect.value = knownScenario(scenarioName) ? scenarioName : "default";
  applyScenario(scenarioSelect.value);
  bindEvents();
  updateViewportOutput();
  render();

  function applyVisualSpec() {
    const root = document.documentElement.style;
    const colors = spec.colors;
    const metrics = spec.metrics;
    root.setProperty("--background", colors.background);
    root.setProperty("--panel", colors.panel);
    root.setProperty("--control", colors.control);
    root.setProperty("--control-highlighted", colors.controlHighlighted);
    root.setProperty("--category-selected", colors.categorySelected);
    root.setProperty("--accent", colors.accent);
    root.setProperty("--aura-accent", colors.auraAccent);
    root.setProperty("--text", colors.text);
    root.setProperty("--muted-text", colors.mutedText);
    root.setProperty("--success", colors.success);
    root.setProperty("--warning", colors.warning);
    root.setProperty("--error", colors.error);
    root.setProperty("--category-width", `${metrics.categoryWidth}px`);
    root.setProperty("--header-height", `${metrics.headerHeight}px`);
    root.setProperty("--module-row-height", `${metrics.moduleRowHeight}px`);
    root.setProperty("--spacing", `${metrics.spacing}px`);
  }

  function knownScenario(value) {
    return ["default", "long-text", "warning", "empty", "extensions"].includes(value);
  }

  function cloneModules() {
    return spec.modules.map(module => ({ ...module }));
  }

  function applyScenario(name) {
    state.scenario = knownScenario(name) ? name : "default";
    state.category = "all";
    state.search = "";
    state.modules = cloneModules();

    if (state.scenario === "long-text") {
      Object.assign(findModule("gameplay.starter-deck"), {
        name: "自定义开局",
        summary: "全局 · 卡牌 0/15 · 遗物 0/6",
        description: "为世界推演配置全局或按角色的开局卡牌与遗物，并支持配置导入导出。"
      });
      Object.assign(findModule("intelligence.auto-battle"), {
        summary: "完整应用 · foundation-package-20260810-155337232-c81434ca",
        attention: "候选模型尚未通过高级难度 Wilson 置信下界认证"
      });
    }

    if (state.scenario === "warning") {
      Object.assign(findModule("presentation.feast-cg"), {
        summary: "随一键美餐暂停 · 已配置 16 个角色",
        attention: "开启一键美餐后恢复自动播放",
        availability: "warning"
      });
      Object.assign(findModule("presentation.skin"), {
        summary: "已启用 2/3 个候选皮肤",
        attention: "1 个资源目录缺失",
        availability: "warning"
      });
      Object.assign(findModule("intelligence.auto-battle"), {
        summary: "模型扫描中",
        attention: "请等待当前索引任务完成",
        availability: "busy"
      });
      Object.assign(findModule("multiplayer.mod-sync"), {
        summary: "联机协议不可用",
        attention: "当前版本不兼容",
        availability: "error"
      });
    }

    if (state.scenario === "empty") {
      state.search = "不存在的工具";
    }

    if (state.scenario === "extensions") {
      state.modules.push(
        {
          id: "extensions.resource-inspector",
          category: "extensions",
          name: "资源检查器",
          description: "检查已注册共享资源的可用状态。",
          summary: "来自 AuroraExtension · 24 个注册项",
          icon: "extensions",
          enabled: true,
          settings: true
        },
        {
          id: "extensions.seed-notebook",
          category: "extensions",
          name: "种子笔记",
          description: "记录并筛选最近使用的世界种子。",
          summary: "已收藏 8 个种子",
          icon: "records",
          enabled: false,
          settings: true
        }
      );
    }

    searchInput.value = state.search;
  }

  function findModule(id) {
    return state.modules.find(module => module.id === id);
  }

  function bindEvents() {
    scenarioSelect.addEventListener("change", () => {
      applyScenario(scenarioSelect.value);
      const next = new URL(window.location.href);
      next.searchParams.set("scenario", state.scenario);
      window.history.replaceState(null, "", next);
      render();
    });

    searchInput.addEventListener("input", () => {
      state.search = searchInput.value;
      render();
    });
    searchInput.addEventListener("keydown", event => {
      if (event.key === "Escape" && state.search) {
        clearCurrentSearch();
      }
    });
    clearSearch.addEventListener("click", clearCurrentSearch);
    openDirectory.addEventListener("click", () => showToast("数据目录动作已触发（预览模式）"));
    closeOverlayButton.addEventListener("click", closeOverlay);
    overlay.addEventListener("mousedown", event => {
      if (event.target === overlay) closeOverlay();
    });
    window.addEventListener("keydown", event => {
      if (event.key === "Escape" && !overlay.hidden) closeOverlay();
    });
    window.addEventListener("resize", updateViewportOutput);
  }

  function clearCurrentSearch() {
    state.search = "";
    searchInput.value = "";
    render();
    searchInput.focus();
  }

  function render() {
    document.body.dataset.previewReady = "false";
    renderCategories();
    renderModules();
    clearSearch.hidden = state.search.trim().length === 0;
    toolboxHeader.classList.toggle("search-empty", clearSearch.hidden);
    requestAnimationFrame(() => {
      document.body.dataset.previewReady = "true";
    });
  }

  function renderCategories() {
    const counts = new Map(spec.categories.map(category => [category.id, 0]));
    for (const module of state.modules) {
      counts.set("all", (counts.get("all") || 0) + 1);
      counts.set(module.category, (counts.get(module.category) || 0) + 1);
    }

    categoryRail.replaceChildren();
    for (const category of spec.categories) {
      if (category.id === "extensions" && (counts.get("extensions") || 0) === 0) continue;
      const button = document.createElement("button");
      button.type = "button";
      button.className = "category-button";
      button.dataset.categoryId = category.id;
      const selectedCategory = state.search.trim() ? "all" : state.category;
      button.setAttribute("aria-pressed", String(selectedCategory === category.id));
      button.innerHTML = `
        <span class="category-marker"></span>
        <img src="${iconRoot}${category.icon}.png" alt="">
        <span class="category-label">${category.label}</span>
        <span class="category-count">${counts.get(category.id) || 0}</span>`;
      button.addEventListener("click", () => selectCategory(category.id));
      button.addEventListener("keydown", handleCategoryArrow);
      categoryRail.append(button);
    }
  }

  function handleCategoryArrow(event) {
    if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
    const buttons = [...categoryRail.querySelectorAll(".category-button")];
    const current = buttons.indexOf(event.currentTarget);
    const direction = event.key === "ArrowDown" ? 1 : -1;
    const next = (current + direction + buttons.length) % buttons.length;
    buttons[next].focus();
    event.preventDefault();
  }

  function selectCategory(categoryId) {
    if (state.category === categoryId) return;
    state.scrollByCategory.set(state.category, moduleList.scrollTop);
    state.category = categoryId;
    render();
    requestAnimationFrame(() => {
      moduleList.scrollTop = state.scrollByCategory.get(categoryId) || 0;
    });
  }

  function visibleModules() {
    const search = state.search.trim().toLocaleLowerCase("zh-CN");
    return state.modules.filter(module => {
      const categoryMatch = search || state.category === "all" || module.category === state.category;
      const haystack = `${module.name} ${module.description} ${module.summary} ${module.attention || ""}`.toLocaleLowerCase("zh-CN");
      return categoryMatch && (!search || haystack.includes(search));
    });
  }

  function renderModules() {
    const modules = visibleModules();
    moduleList.replaceChildren();
    for (const module of modules) {
      moduleList.append(createModuleRow(module));
    }
    emptyState.hidden = modules.length !== 0;
    emptyState.textContent = state.search.trim()
      ? "没有符合搜索条件的工具。"
      : "当前分类暂无工具。";
    resultTitle.textContent = state.search.trim()
      ? `搜索结果  ·  ${modules.length}`
      : `${categoryLabel(state.category)}  ·  ${modules.length}`;
  }

  function createModuleRow(module) {
    const row = document.createElement("article");
    const visualState = resolveVisualState(module);
    row.className = "module-row";
    row.dataset.moduleId = module.id;
    row.dataset.state = visualState;
    row.setAttribute("role", "listitem");

    const settingsControl = module.settings
      ? `<button type="button" class="icon-button module-settings" title="设置 ${module.name}" aria-label="设置 ${module.name}"><img src="${iconRoot}settings.png" alt=""></button>`
      : "";
    const status = module.attention
      ? `${module.summary}  ·  ${module.attention}`
      : module.summary;
    row.innerHTML = `
      <span class="status-marker" aria-hidden="true"></span>
      <span class="module-icon"><img src="${iconRoot}${module.icon}.png" alt=""></span>
      <span class="module-copy">
        <span class="module-title">${module.name}${module.experimental ? '<span class="experimental-label">· 实验</span>' : ""}</span>
        <span class="module-status" title="${status}">${status}</span>
        <span class="module-description" title="${module.description}">${module.description}</span>
      </span>
      <span class="settings-slot${module.settings ? "" : " empty"}">${settingsControl}</span>
      <span class="enable-control"><span>启用</span><button type="button" class="toolbox-checkbox" role="checkbox" aria-label="启用 ${module.name}" aria-checked="${module.enabled}" ${module.availability === "error" || module.availability === "busy" ? "disabled" : ""}></button></span>`;

    row.querySelector(".module-settings")?.addEventListener("click", event => openOverlay(module, event.currentTarget));
    row.querySelector(".toolbox-checkbox").addEventListener("click", event => {
      module.enabled = !module.enabled;
      event.currentTarget.setAttribute("aria-checked", String(module.enabled));
      row.dataset.state = resolveVisualState(module);
    });
    return row;
  }

  function resolveVisualState(module) {
    if (module.availability === "error") return "error";
    if (module.attention || module.availability === "warning" || module.availability === "busy") return "warning";
    return module.enabled ? "enabled" : "disabled";
  }

  function categoryLabel(categoryId) {
    return spec.categories.find(category => category.id === categoryId)?.label || "全部";
  }

  function openOverlay(module, trigger) {
    state.overlayTrigger = trigger;
    overlayTitle.textContent = `${module.name}设置`;
    overlaySummary.textContent = `${module.summary}。${module.description}`;
    overlay.hidden = false;
    closeOverlayButton.focus();
  }

  function closeOverlay() {
    if (overlay.hidden) return;
    overlay.hidden = true;
    state.overlayTrigger?.focus();
    state.overlayTrigger = null;
  }

  function showToast(message) {
    window.clearTimeout(toastTimer);
    toast.textContent = message;
    toast.hidden = false;
    toastTimer = window.setTimeout(() => {
      toast.hidden = true;
    }, 1400);
  }

  function updateViewportOutput() {
    viewportOutput.textContent = `${window.innerWidth} × ${window.innerHeight}`;
  }

  function validate() {
    const errors = [];
    const settingsWindow = document.querySelector(".settings-window");
    const workspace = document.querySelector(".toolbox-workspace");
    const header = document.querySelector(".toolbox-header");
    const windowRect = settingsWindow.getBoundingClientRect();
    const workspaceRect = workspace.getBoundingClientRect();
    const listRect = moduleList.getBoundingClientRect();
    const rows = [...moduleList.querySelectorAll(".module-row")];

    if (document.documentElement.scrollWidth > window.innerWidth + 1) errors.push("document overflows horizontally");
    if (document.documentElement.scrollHeight > window.innerHeight + 1) errors.push("document overflows vertically");
    if (windowRect.left < -1 || windowRect.right > window.innerWidth + 1) errors.push("settings window leaves viewport");
    if (workspaceRect.width <= 0 || workspaceRect.height <= 0) errors.push("workspace is blank");
    if (alphaOf(getComputedStyle(workspace).backgroundColor) < 0.999) errors.push("workspace background is not opaque");

    const centerElement = document.elementFromPoint(workspaceRect.left + workspaceRect.width / 2, workspaceRect.top + workspaceRect.height / 2);
    if (!centerElement || !workspace.contains(centerElement)) errors.push("native underlay can receive the workspace center hit");

    for (const row of rows) {
      const rowRect = row.getBoundingClientRect();
      if (Math.abs(rowRect.height - spec.metrics.moduleRowHeight) > 1) errors.push(`${row.dataset.moduleId} row height drifted`);
      if (row.scrollWidth > row.clientWidth + 1) errors.push(`${row.dataset.moduleId} overflows horizontally`);
      const copy = row.querySelector(".module-copy").getBoundingClientRect();
      const settings = row.querySelector(".settings-slot").getBoundingClientRect();
      if (copy.right > settings.left + 1) errors.push(`${row.dataset.moduleId} copy overlaps settings action`);
    }

    const headerChildren = [...header.children].filter(element => !element.hidden);
    for (let index = 1; index < headerChildren.length; index++) {
      const previous = headerChildren[index - 1].getBoundingClientRect();
      const current = headerChildren[index].getBoundingClientRect();
      if (previous.right > current.left + 1) errors.push("header controls overlap");
    }

    for (const label of document.querySelectorAll(".category-label, .native-tabs button")) {
      if (label.scrollWidth > label.clientWidth + 1) errors.push(`${label.textContent.trim()} label is truncated`);
    }

    const missingImages = [...document.images]
      .filter(image => image.offsetParent !== null && (!image.complete || image.naturalWidth === 0))
      .map(image => image.getAttribute("src"));
    if (missingImages.length) errors.push(`missing images: ${missingImages.join(", ")}`);

    const fullyVisibleRows = rows.filter(row => {
      const rect = row.getBoundingClientRect();
      return rect.top >= listRect.top - 1 && rect.bottom <= listRect.bottom + 1;
    }).length;
    if (rows.length && fullyVisibleRows < 4) errors.push("fewer than four complete tool rows are visible");
    if (state.scenario === "empty" && rows.length !== 0) errors.push("empty scenario still renders rows");

    return {
      ok: errors.length === 0,
      errors,
      scenario: state.scenario,
      viewport: { width: window.innerWidth, height: window.innerHeight },
      modules: rows.length,
      fullyVisibleRows,
      workspace: { width: Math.round(workspaceRect.width), height: Math.round(workspaceRect.height) },
      opaqueBackground: getComputedStyle(workspace).backgroundColor
    };
  }

  function alphaOf(color) {
    const match = color.match(/^rgba?\(([^)]+)\)$/);
    if (!match) return 0;
    const parts = match[1].split(",").map(part => Number(part.trim()));
    return parts.length < 4 ? 1 : parts[3];
  }

  window.__AURA_PREVIEW__ = Object.freeze({
    validate,
    visibleModuleIds: () => visibleModules().map(module => module.id),
    state: () => ({ scenario: state.scenario, category: state.category, search: state.search })
  });
})();
